import argparse
import json
import os
import socket
import struct
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_board
import mock_cal


def main():
    parser = argparse.ArgumentParser(description="Mock Wi-Fi bridge board for Atom Spectra analyzer")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=18234)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--chunk", type=int, default=256)
    parser.add_argument("--order", choices=["hs", "sh"], default="hs")
    parser.add_argument("--period", type=float, default=1.0)
    parser.add_argument("--cycles", type=int, default=8)
    parser.add_argument("--connections", type=int, default=2)
    parser.add_argument("--drop-cycle", type=int, default=-1)
    parser.add_argument("--down-ms", type=int, default=0)
    parser.add_argument("--stale-join", action="store_true")
    parser.add_argument("--expected", type=str, default=None)
    parser.add_argument("--log", type=str, default=None)
    parser.add_argument("--inf-text", type=str, default=None)
    # a board that does not count: the status time stays N (0 = idle, empty histogram); -1 = counting (time = cycle)
    parser.add_argument("--stopped-at", type=int, default=-1)
    args = parser.parse_args()

    # Generate base histogram
    base = mock_board.make_histogram(8192, args.seed)

    # Write expected file if requested
    if args.expected:
        with open(args.expected, "w") as f:
            for i, v in enumerate(base):
                if i == 5:
                    f.write("0\n")
                else:
                    f.write(f"{v}\n")

    # Helper to log
    def log_entry(entry):
        if args.log:
            with open(args.log, "a") as f:
                f.write(json.dumps(entry) + "\n")
                f.flush()

    def status_time(cycle):
        return cycle if args.stopped_at < 0 else args.stopped_at

    # Global cycle counter
    g = 1

    # Create listening socket
    def create_listener():
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((args.host, args.port))
        s.listen(5)
        s.settimeout(20)
        return s

    listener = create_listener()
    print(f"LISTENING {args.host}:{args.port}", flush=True)

    connections_served = 0

    for k in range(args.connections):
        try:
            conn, addr = listener.accept()
        except socket.timeout:
            break
        except OSError:
            break

        conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        log_entry({"conn": k, "event": "accept", "t": time.time()})

        send_lock = threading.Lock()
        decoder = mock_cal.Decoder()
        reader_alive = threading.Event()
        reader_alive.set()

        def reader_thread_final():
            # Feed every received byte to the decoder; answer every text command with "-ok".
            try:
                while True:
                    data = conn.recv(4096)
                    if not data:
                        break
                    for byte in data:
                        result = decoder.feed(byte)
                        if result is None:
                            continue
                        cmd, payload = result
                        if cmd == 0x03:
                            text = payload.decode('ascii', 'replace')
                            log_entry({"conn": k, "cmd": text, "t": time.time()})
                            # --inf-text answers -inf with a (synthetic) device text instead of "-ok"
                            reply = (args.inf_text + "\r\n").encode("ascii", "replace") if args.inf_text and text.strip() == "-inf" else b"-ok\r\n"
                            with send_lock:
                                conn.sendall(mock_board.encode(0x03, reply))
            except Exception:
                pass
            finally:
                reader_alive.clear()

        t = threading.Thread(target=reader_thread_final, daemon=True)
        t.start()

        dropped = False
        try:
            # Stale join logic
            if args.stale_join and k > 0:
                # Previous cycle number is g - 1
                prev_g = g - 1
                if args.order == "hs":
                    # Send one status packet of the PREVIOUS cycle number
                    status_payload = struct.pack('<IHIII', status_time(prev_g), 7, 1234, 55, 0)
                    with send_lock:
                        conn.sendall(mock_board.encode(0x04, status_payload))
                else:
                    # Send the second half of the previous cycle's histogram packets
                    # Histogram for cycle prev_g
                    hist = base.copy() if args.stopped_at != 0 else [0] * len(base)
                    hist[5] = (1000 + prev_g) if args.stopped_at < 0 else hist[5]
                    # Total channels 8192, chunk size args.chunk
                    total_packets = 8192 // args.chunk
                    # Second half: packets from total_packets//2 to end
                    start_packet = total_packets // 2
                    for i in range(start_packet, total_packets):
                        offset = i * args.chunk
                        values = hist[offset:offset + args.chunk]
                        payload = struct.pack('<H', offset) + struct.pack(f'<{len(values)}I', *values)
                        with send_lock:
                            conn.sendall(mock_board.encode(0x01, payload))
                # A client that joins after the leftover waits for the next cycle like on the real board
                # (cycles are `period` apart; the leftover and the next cycle are not back to back).
                time.sleep(args.period)

            # Streaming cycles
            dropped = False
            for c in range(args.cycles):
                # Check drop condition
                if k == 0 and args.drop_cycle == c:
                    # Send the first part based on order, then close
                    if args.order == "hs":
                        # Send histogram part
                        hist = base.copy() if args.stopped_at != 0 else [0] * len(base)
                        hist[5] = (1000 + g) if args.stopped_at < 0 else hist[5]
                        for i in range(8192 // args.chunk):
                            offset = i * args.chunk
                            values = hist[offset:offset + args.chunk]
                            payload = struct.pack('<H', offset) + struct.pack(f'<{len(values)}I', *values)
                            with send_lock:
                                conn.sendall(mock_board.encode(0x01, payload))
                    else:
                        # Send status part
                        status_payload = struct.pack('<IHIII', status_time(g), 7, 1234, 55, 0)
                        with send_lock:
                            conn.sendall(mock_board.encode(0x04, status_payload))
                
                    # Close abruptly
                    conn.close()
                    log_entry({"conn": k, "event": "drop"})
                    dropped = True
                    break

                # Normal cycle
                if args.order == "hs":
                    # Histogram first
                    hist = base.copy() if args.stopped_at != 0 else [0] * len(base)
                    hist[5] = (1000 + g) if args.stopped_at < 0 else hist[5]
                    for i in range(8192 // args.chunk):
                        offset = i * args.chunk
                        values = hist[offset:offset + args.chunk]
                        payload = struct.pack('<H', offset) + struct.pack(f'<{len(values)}I', *values)
                        with send_lock:
                            conn.sendall(mock_board.encode(0x01, payload))
                    # Then status
                    status_payload = struct.pack('<IHIII', status_time(g), 7, 1234, 55, 0)
                    with send_lock:
                        conn.sendall(mock_board.encode(0x04, status_payload))
                else:
                    # Status first
                    status_payload = struct.pack('<IHIII', status_time(g), 7, 1234, 55, 0)
                    with send_lock:
                        conn.sendall(mock_board.encode(0x04, status_payload))
                    # Then histogram
                    hist = base.copy() if args.stopped_at != 0 else [0] * len(base)
                    hist[5] = (1000 + g) if args.stopped_at < 0 else hist[5]
                    for i in range(8192 // args.chunk):
                        offset = i * args.chunk
                        values = hist[offset:offset + args.chunk]
                        payload = struct.pack('<H', offset) + struct.pack(f'<{len(values)}I', *values)
                        with send_lock:
                            conn.sendall(mock_board.encode(0x01, payload))

                g += 1
                time.sleep(args.period)

        except OSError:
            pass  # the client closed the connection: end this connection quietly

        if not dropped:
            # Keep connection open up to 30s while reader is alive
            start_wait = time.time()
            while reader_alive.is_set() and (time.time() - start_wait) < 30:
                time.sleep(0.1)
            conn.close()
            log_entry({"conn": k, "event": "close"})

        connections_served += 1

        # Handle down-ms after drop on connection 0
        if dropped and k == 0 and args.down_ms > 0:
            listener.close()
            time.sleep(args.down_ms / 1000.0)
            listener = create_listener()
            print(f"LISTENING {args.host}:{args.port}", flush=True)

    log_entry({"event": "done", "connections": connections_served})
    sys.exit(0)


if __name__ == "__main__":
    main()
