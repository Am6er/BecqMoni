import argparse
import json
import socket
import struct
import threading
import sys
import time
import http.server
import socketserver


def read_name(buf, off):
    labels = []
    jumps = 0
    original_off = off
    while True:
        if off >= len(buf):
            raise ValueError("buffer overrun")
        length = buf[off]
        if length == 0:
            off += 1
            break
        if (length & 0xC0) == 0xC0:
            if off + 1 >= len(buf):
                raise ValueError("buffer overrun")
            pointer = ((length & 0x3F) << 8) | buf[off + 1]
            if jumps >= 64:
                raise ValueError("compression loop")
            jumps += 1
            if pointer == original_off:
                raise ValueError("compression loop")
            off = pointer
            continue
        off += 1
        if off + length > len(buf):
            raise ValueError("buffer overrun")
        label = buf[off:off + length].decode('utf-8', errors='replace')
        labels.append(label)
        off += length
    return '.'.join(labels), off


def encode_name(name):
    parts = name.split('.')
    out = b''
    for part in parts:
        b = part.encode('utf-8')
        if len(b) > 255:
            raise ValueError("label too long")
        out += bytes([len(b)]) + b
    out += b'\x00'
    return out


def encode_rr(rec, ttl=120):   # ttl 0: a goodbye (RFC 6762 10.1), the record is withdrawn
    name_bytes = encode_name(rec["name_raw"])
    rtype = rec["type"]
    rclass = 0x0001

    if rtype == 1:
        addr = rec["addr"]
        octets = [int(x) for x in addr.split('.')]
        rdata = bytes(octets)
    elif rtype == 12:
        rdata = encode_name(rec["target_raw"])
    elif rtype == 33:
        priority = 0
        weight = 0
        port = rec["port"]
        target = rec["target"]
        rdata = struct.pack('!HHH', priority, weight, port) + encode_name(target)
    elif rtype == 16:
        rdata = b''
        for s in rec["txt"]:
            b = s.encode('utf-8')
            if len(b) > 255:
                raise ValueError("txt string too long")
            rdata += bytes([len(b)]) + b
    else:
        raise ValueError("unsupported type")

    rdlength = len(rdata)
    return name_bytes + struct.pack('!HHIH', rtype, rclass, ttl, rdlength) + rdata


def new_service(owner, instance_label, host, sn, P, rest=True):   # rest=False: an announcement without TXT rest (the port is not given)
    ptr_name = "_atomspectra._tcp.local"
    instance_name = instance_label + "._atomspectra._tcp.local"
    instance_name_lower = instance_name.lower()
    ptr_target_raw = instance_name
    ptr_target_lower = instance_name_lower

    ptr_rec = {
        "owner": owner,
        "name": ptr_name,
        "name_raw": ptr_name,
        "type": 12,
        "target": ptr_target_lower,
        "target_raw": ptr_target_raw
    }

    srv_rec = {
        "owner": owner,
        "name": instance_name_lower,
        "name_raw": instance_name,
        "type": 33,
        "port": 8234,
        "target": host
    }

    txt_rec = {
        "owner": owner,
        "name": instance_name_lower,
        "name_raw": instance_name,
        "type": 16,
        "txt": ["sn=" + sn, "proto=1"] + (["rest=" + str(P)] if rest else []) + ["fw=1.2.39"]
    }

    return [ptr_rec, srv_rec, txt_rec]


def http_service(owner, instance_label, host, P):
    ptr_name = "_http._tcp.local"
    instance_name = instance_label + "._http._tcp.local"
    instance_name_lower = instance_name.lower()
    ptr_target_raw = instance_name
    ptr_target_lower = instance_name_lower

    ptr_rec = {
        "owner": owner,
        "name": ptr_name,
        "name_raw": ptr_name,
        "type": 12,
        "target": ptr_target_lower,
        "target_raw": ptr_target_raw
    }

    srv_rec = {
        "owner": owner,
        "name": instance_name_lower,
        "name_raw": instance_name,
        "type": 33,
        "port": P,
        "target": host
    }

    return [ptr_rec, srv_rec]


def a_record(owner, host, addr):
    return {
        "owner": owner,
        "name": host,
        "name_raw": host,
        "type": 1,
        "addr": addr
    }


def build_records(set_name, P):
    records = []
    if set_name == "two-new":
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += new_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", "00000119", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name == "split":
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += new_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", "00000119", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name == "legacy":
        records += http_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", P)
        records += http_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", P)
        records += http_service("printer", "HP LaserJet", "printer.local", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
        records.append(a_record("printer", "printer.local", "127.0.0.4"))
    elif set_name == "real-146":
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += http_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", P)
        records += http_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name == "three":   # VERIFY-4 finding 1: the boards .146 and .119 and a third one (owner b3, answers late by --delays)
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += new_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", "00000081", P)
        records += new_service("b3", "AtomSpectra Gateway-3", "atomspectra-3.local", "00000091", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
        records.append(a_record("b3", "atomspectra-3.local", "127.0.0.4"))
    elif set_name == "difflabel":   # VERIFY-4 finding 2: the board .146 shows two labels (_atomspectra Gateway-2, _http Gateway-4), one address
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += http_service("b146", "AtomSpectra Gateway-4", "atomspectra.local", P)
        records += new_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", "00000081", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name in ("samehost", "samehost-split"):   # two boards with ONE host name (the conflict not resolved), b146 shows a second instance
        records += new_service("b146", "AtomSpectra Gateway", "atomspectra.local", "00000051", P)
        records += new_service("b119", "AtomSpectra Gateway-2", "atomspectra.local", "00000119", P)
        records += new_service("b146", "AtomSpectra Gateway-5", "atomspectra.local", "00000051", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra.local", "127.0.0.3"))
    elif set_name == "orphan":   # the two boards and an instance that answers its PTR only (no SRV, no address ever comes)
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += new_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", "00000119", P)
        records += new_service("b3", "AtomSpectra Gateway-3", "atomspectra-3.local", "00000091", P)[0:1]
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name == "longlabel":   # an instance read from two labels of 40 bytes: joined, it is longer than one label may be
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P)
        records += new_service("b119", "x" * 40 + "." + "y" * 40, "atomspectra-2.local", "00000119", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name == "norest":   # a board without TXT rest and one with it
        records += new_service("b146", "AtomSpectra Gateway-2", "atomspectra.local", "00000051", P, rest=False)
        records += new_service("b119", "AtomSpectra Gateway", "atomspectra-2.local", "00000119", P)
        records.append(a_record("b146", "atomspectra.local", "127.0.0.2"))
        records.append(a_record("b119", "atomspectra-2.local", "127.0.0.3"))
    elif set_name == "flood":   # VERIFY-2 finding 7: 40 gateway instances (_http._tcp), each with SRV and A, one packet per owner
        for i in range(1, 41):
            records += http_service("g%d" % i, "AtomSpectra Gateway-%d" % i, "gw%d.local" % i, P)
            records.append(a_record("g%d" % i, "gw%d.local" % i, "127.0.1.%d" % i))
    return records


class LogHandler:
    def __init__(self, path):
        self.path = path
        self.lock = threading.Lock()
        self.file = open(path, 'a', encoding='utf-8')

    def log(self, obj):
        line = json.dumps(obj)
        with self.lock:
            self.file.write(line + '\n')
            self.file.flush()


class HTTPHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def do_GET(self):
        host = self.server.server_address[0]
        path = self.path
        self.server.log_handler.log({"kind": "http", "host": host, "path": path})

        if path == "/api/device":
            serial = self.server.serial
            body = json.dumps({"serial": serial, "fw": "1.2.38"}).encode('utf-8')
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        else:
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()


class QuickHTTPServer(http.server.ThreadingHTTPServer):
    def server_bind(self):   # HTTPServer.server_bind calls getfqdn (reverse DNS of 127.0.0.x, seconds on Windows)
        socketserver.TCPServer.server_bind(self)
        self.server_name, self.server_port = "mock", self.server_address[1]


def start_http_server(ip, port, serial, log_handler):
    try:
        server = QuickHTTPServer((ip, port), HTTPHandler)
        server.serial = serial
        server.log_handler = log_handler
        t = threading.Thread(target=server.serve_forever, daemon=True)
        t.start()
    except OSError as e:
        print(f"HTTP BIND FAIL {ip} {e}")


ONLY_FROM = ''
DELAYS = {}
GOODBYE = {}   # owner -> ms: after its answer the board sends the same records again with TTL 0 (a goodbye)
REPLY_FROM = {}   # owner -> socket bound to the board's own address: the answer comes from the board, as on a network
SPLIT_SETS = ("split", "samehost-split", "longlabel")   # answers without additional records: SRV/TXT and A only to their own questions
OWNER_IP = {"b146": "127.0.0.2", "b119": "127.0.0.3", "b3": "127.0.0.4", "printer": "127.0.0.4"}


def udp_loop(sock, records, set_name, log_handler):
    while True:
        try:
            data, addr = sock.recvfrom(9000)
        except Exception:
            continue
        if ONLY_FROM and addr[0] != ONLY_FROM:   # #BM-17: answers only the queries sent from this source address
            continue

        try:
            if len(data) < 12:
                continue
            header = struct.unpack('!HHHHHH', data[:12])
            qid = header[0]
            qdcount = header[2]

            questions = []
            off = 12
            for _ in range(qdcount):
                qname, off = read_name(data, off)
                if off + 4 > len(data):
                    raise ValueError("buffer overrun")
                qtype, qclass = struct.unpack('!HH', data[off:off+4])
                off += 4
                questions.append((qname, qtype))

            log_handler.log({
                "kind": "dns",
                "from": f"{addr[0]}:{addr[1]}",
                "q": [[qname.lower(), qtype] for qname, qtype in questions]
            })

            owners_order = []
            owner_records = {}

            for qname, qtype in questions:
                qname_lower = qname.lower()
                matching = [r for r in records if r["name"] == qname_lower and (qtype == 255 or r["type"] == qtype)]
                for rec in matching:
                    owner = rec["owner"]
                    if owner not in owner_records:
                        owner_records[owner] = []
                        owners_order.append(owner)
                    if rec not in owner_records[owner]:
                        owner_records[owner].append(rec)

            for owner in owners_order:
                answers = owner_records[owner]
                additionals = []

                if set_name not in SPLIT_SETS:
                    answer_names = set(r["name"] for r in answers)
                    additionals_names = set()

                    for ans in answers:
                        if ans["type"] == 12:
                            target_lower = ans["target"]
                            srvs = [r for r in records if r["owner"] == owner and r["type"] == 33 and r["name"] == target_lower]
                            txts = [r for r in records if r["owner"] == owner and r["type"] == 16 and r["name"] == target_lower]
                            for r in srvs + txts:
                                if r not in answers and r not in additionals:   # SRV and TXT share a name: dedupe by record
                                    additionals.append(r)

                            for srv in srvs:
                                srv_target = srv["target"]
                                a_recs = [r for r in records if r["owner"] == owner and r["type"] == 1 and r["name"] == srv_target]
                                for a in a_recs:
                                    if a not in answers and a not in additionals:
                                        additionals.append(a)

                def make(ttl):
                    packet = struct.pack('!HHHHHH', qid, 0x8400, 0, len(answers), 0, len(additionals))
                    for r in answers:
                        packet += encode_rr(r, ttl)
                    for r in additionals:
                        packet += encode_rr(r, ttl)
                    return packet
                packet = make(120)
                out = REPLY_FROM.get(owner, sock)

                delay_ms = DELAYS.get(owner, 0)   # #BM-17: a board answers late (random delay of the real boards)
                if delay_ms > 0:
                    threading.Timer(delay_ms / 1000.0, lambda p=packet, a=addr, s=out: s.sendto(p, a)).start()
                else:
                    out.sendto(packet, addr)
                if owner in GOODBYE:
                    threading.Timer((delay_ms + GOODBYE[owner]) / 1000.0, lambda p=make(0), a=addr, s=out: s.sendto(p, a)).start()

        except Exception:
            continue


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--log', required=True)
    parser.add_argument('--set', required=True, choices=['two-new', 'legacy', 'real-146', 'split', 'flood', 'three', 'difflabel', 'samehost', 'samehost-split', 'norest', 'orphan', 'longlabel'])
    parser.add_argument('--duration', type=int, default=60)
    parser.add_argument('--only-from', default='')
    parser.add_argument('--delays', default='')   # owner=ms,owner=ms: delay of the answer of each board
    parser.add_argument('--goodbye', default='')   # owner=ms: the board withdraws its records (TTL 0) this long after its answer
    parser.add_argument('--reply-from-owner', action='store_true')   # each board answers from its own address (OWNER_IP), not 127.0.0.1
    args = parser.parse_args()
    global ONLY_FROM
    ONLY_FROM = args.only_from
    for item in args.delays.split(','):
        if '=' in item:
            DELAYS[item.split('=')[0]] = int(item.split('=')[1])
    for item in args.goodbye.split(','):
        if '=' in item:
            GOODBYE[item.split('=')[0]] = int(item.split('=')[1])
    if args.reply_from_owner:
        for owner, ip in OWNER_IP.items():
            if ip not in [s.getsockname()[0] for s in REPLY_FROM.values()]:
                s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                s.bind((ip, 0))
                REPLY_FROM[owner] = s
            else:
                REPLY_FROM[owner] = [s for s in REPLY_FROM.values() if s.getsockname()[0] == ip][0]

    P = args.port
    log_handler = LogHandler(args.log)
    records = build_records(args.set, P)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(('127.0.0.1', P))

    start_http_server('127.0.0.2', P, "00000051", log_handler)
    start_http_server('127.0.0.3', P, "00000119", log_handler)
    start_http_server('127.0.0.4', P, "PRINTER", log_handler)

    t = threading.Thread(target=udp_loop, args=(sock, records, args.set, log_handler), daemon=True)
    t.start()

    print("LISTENING", flush=True)
    time.sleep(args.duration)
    sys.exit(0)


if __name__ == '__main__':
    main()
