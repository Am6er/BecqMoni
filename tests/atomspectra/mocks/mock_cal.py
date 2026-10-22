import argparse
import os
import socket
import struct
import sys
import time
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_board


class Decoder:
    def __init__(self):
        self.in_packet = False
        self.buffer = []
        self.escape = False

    def feed(self, b: int):
        if not self.in_packet:
            if b == 0xFE:
                self.in_packet = True
                self.buffer = []
                self.escape = False
            return None
        if b == 0xA5:
            self.in_packet = False
            body = bytes(self.buffer)
            if len(body) >= 3:
                crc_calc = mock_board.crc16(body[:-2])
                crc_recv = body[-2] | (body[-1] << 8)
                if crc_calc == crc_recv:
                    return (body[0], bytes(body[1:-2]))
            return None
        if b == 0xFD:
            self.escape = True
            return None
        if self.escape:
            self.buffer.append(~b & 0xFF)
            self.escape = False
        else:
            self.buffer.append(b)
        return None


def cal_answer(coeffs: list, corrupt_crc: bool) -> str:
    lines = []
    for c in coeffs:
        if c is None:
            hex16 = "FFFFFFFFFFFFFFFF"
        else:
            hex16 = struct.pack('>d', float(c)).hex().upper()
        lines.append(hex16[:8])
        lines.append(hex16[8:])
    crc = zlib.crc32("".join(lines).encode("ascii")) & 0xFFFFFFFF
    if corrupt_crc:
        crc ^= 0x1
    return "\r\n".join(lines + ["%08X" % crc]) + "\r\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=18234)
    parser.add_argument('--mode', choices=['ok', 'badcrc', 'empty', 'split'], default='ok')
    parser.add_argument('--coeffs', default='0.5,3.0,0,0,0')
    parser.add_argument('--log', default=None)
    args = parser.parse_args()

    coeffs = []
    for part in args.coeffs.split(','):
        part = part.strip()
        if part.lower() == 'none':
            coeffs.append(None)
        else:
            coeffs.append(float(part))

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((args.host, args.port))
    srv.listen(1)
    srv.settimeout(30)
    print(f"LISTENING {args.host}:{args.port}", flush=True)

    try:
        conn, _ = srv.accept()
    except socket.timeout:
        sys.exit(2)
    finally:
        srv.close()

    conn.sendall(bytes([0x01, 0xA5, 0x00, 0xFD, 0x10]))

    decoder = Decoder()
    recorded = []
    cal_received = False
    start_time = time.time()
    budget = 5.0

    while True:
        elapsed = time.time() - start_time
        if elapsed >= budget:
            break
        remaining = budget - elapsed
        if remaining <= 0:
            break
        conn.settimeout(remaining)
        try:
            data = conn.recv(1024)
            if not data:
                break
            for byte in data:
                res = decoder.feed(byte)
                if res is not None:
                    cmd, payload = res
                    text = payload.decode('ascii', errors='replace')
                    recorded.append(f"cmd=0x{cmd:02X} text={text}")
                    if cmd == 0x03 and text == '-cal':
                        cal_received = True
                        if args.mode == 'ok':
                            ans = cal_answer(coeffs, False)
                            conn.sendall(mock_board.encode(0x03, ans.encode('ascii'), False))
                        elif args.mode == 'badcrc':
                            ans = cal_answer(coeffs, True)  # wrong CRC32 line, valid shproto crc
                            conn.sendall(mock_board.encode(0x03, ans.encode('ascii'), False))
                        elif args.mode == 'empty':
                            empty_coeffs = [None] * 5
                            ans = cal_answer(empty_coeffs, False)
                            conn.sendall(mock_board.encode(0x03, ans.encode('ascii'), False))
                        elif args.mode == 'split':
                            ans = cal_answer(coeffs, False)
                            mid = len(ans) // 2
                            part1 = ans[:mid]
                            part2 = ans[mid:]
                            conn.sendall(mock_board.encode(0x03, part1.encode('ascii'), False))
                            time.sleep(0.05)
                            conn.sendall(mock_board.encode(0x03, part2.encode('ascii'), False))
        except socket.timeout:
            break
        except ConnectionResetError:
            break

    time.sleep(0.3)
    conn.close()

    print(f"RECEIVED {recorded}")
    if args.log:
        with open(args.log, 'w') as f:
            for line in recorded:
                f.write(line + '\n')

    sys.exit(0 if cal_received else 1)


if __name__ == '__main__':
    main()
