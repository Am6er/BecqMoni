import argparse
import json
import random
import socket
import struct
import sys
import threading
import time


def crc16(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            if crc & 1:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
    return crc


def _escape(data: bytes) -> bytes:
    out = bytearray()
    for b in data:
        if b in (0xFE, 0xFD, 0xA5):
            out.append(0xFD)
            out.append(~b & 0xFF)
        else:
            out.append(b)
    return bytes(out)


def encode(cmd: int, payload: bytes, corrupt_crc: bool = False) -> bytes:
    data = bytes([cmd]) + payload
    c = crc16(data)
    if corrupt_crc:
        c ^= 0x5A
    lo = c & 0xFF
    hi = (c >> 8) & 0xFF
    body = bytes([cmd]) + payload + bytes([lo, hi])
    return b'\xFF\xFE' + _escape(body) + b'\xA5'


def make_histogram(channels: int, seed: int) -> list[int]:
    rng = random.Random(seed)
    vals = [rng.getrandbits(27) for _ in range(channels)]
    forced = [0x00A5FDFE, 0x0000FEFE, 0x00FDA5A5, 0xA5, 0xFD, 0xFE]
    indices = [0, 1, 2, 100, 4095, channels - 1]
    for i, idx in enumerate(indices):
        if 0 <= idx < channels:
            vals[idx] = forced[i]
    return vals


def reader(sock: socket.socket, counter: list, lock: threading.Lock):
    try:
        while True:
            data = sock.recv(4096)
            if not data:
                break
            with lock:
                counter[0] += len(data)
    except OSError:
        pass


def main() -> int:
    parser = argparse.ArgumentParser(description="Mock Wi-Fi bridge board for Atom Spectra gamma analyzer")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=18234)
    parser.add_argument("--channels", type=int, default=8192)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--chunk", type=int, default=256)
    parser.add_argument("--mode", choices=["ok", "badcrc", "drop", "drop-then-ok"], default="ok")
    parser.add_argument("--expected", default=None)
    parser.add_argument("--cycles", type=int, default=30)
    parser.add_argument("--period", type=float, default=0.2)
    parser.add_argument("--connections", type=int, default=1)
    parser.add_argument("--report", default=None)
    args = parser.parse_args()

    hist = make_histogram(args.channels, args.seed)

    if args.expected:
        with open(args.expected, "w") as f:
            for v in hist:
                f.write(f"{v}\n")

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        server.bind((args.host, args.port))
    except OSError as e:
        print(f"ERROR bind: {e}", file=sys.stderr)
        return 1
    server.listen(5)
    server.settimeout(60)
    print(f"LISTENING {args.host}:{args.port}", flush=True)

    total_bytes = 0
    lock = threading.Lock()
    threads = []
    counters = []
    served = 0

    for k in range(args.connections):
        try:
            conn, _ = server.accept()
        except socket.timeout:
            break
        except OSError:
            break

        conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        counter = [0]
        counters.append(counter)
        t = threading.Thread(target=reader, args=(conn, counter, lock), daemon=True)
        t.start()
        threads.append(t)

        try:
            conn.sendall(bytes([0x12, 0x00, 0xA5, 0x34, 0xFD, 0x56, 0xA5]))
        except OSError:
            conn.close()
            served += 1
            continue

        effective_mode = args.mode
        if args.mode == "drop-then-ok" and k == 0:
            effective_mode = "drop"

        for c in range(args.cycles):
            try:
                num_packets = (args.channels + args.chunk - 1) // args.chunk
                if effective_mode == "drop" and c == 0:
                    num_packets = num_packets // 2

                for p in range(num_packets):
                    offset = p * args.chunk
                    end = min(offset + args.chunk, args.channels)
                    payload = struct.pack("<H", offset) + b"".join(
                        struct.pack("<I", hist[i]) for i in range(offset, end)
                    )
                    corrupt = (effective_mode == "badcrc")
                    conn.sendall(encode(0x01, payload, corrupt_crc=corrupt))

                if effective_mode == "drop" and c == 0:
                    conn.close()
                    break

                status_payload = struct.pack("<IHIII", c + 1, 7, 1234, 55, 0)  # 18 bytes: time u32, cpu u16, cps u32, invalid u32, width u32
                conn.sendall(encode(0x04, status_payload))
            except OSError:
                break

            if c < args.cycles - 1:
                time.sleep(args.period)

        try:
            conn.close()
        except OSError:
            pass

        served += 1

    for t in threads:
        t.join(timeout=2)
    with lock:
        total_bytes = sum(c[0] for c in counters)
    report = {"connections": served, "bytes_from_client": total_bytes, "mode": args.mode}
    if args.report:
        with open(args.report, "w") as f:
            json.dump(report, f)
    print(f"DONE connections={served} bytes_from_client={total_bytes}", flush=True)
    if served == 0:
        return 2  # no client came within the accept timeout
    return 0


if __name__ == "__main__":
    sys.exit(main())
