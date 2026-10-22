import argparse, gzip, json, os, sys, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit, parse_qs
sys.stdout.reconfigure(encoding="utf-8")

# Global state and locks
LOCK = threading.Lock()
LOGLOCK = threading.Lock()
T0 = time.monotonic()
DEVLOG = [{"seq": 1, "t": 1000, "text": "hello\n"}, {"seq": 2, "t": 2000, "text": "-ok\n"}]   # board log (/api/devlog)
SYNCED = False   # the timeline starts at the harness GET /__sync, not at process start (determinism)
TOKEN = "tok-123456"
S = {
    "counting": False,
    "time": 0,
    "total": 0,
    "hist": 0,
    "valid": False,
    "cache_body": None,
    "cache_at": -1e9,
    "new_run": False
}
FiredEvents = set()
args = None
SHAPE = None
SHAPE_SUM = 0
RENDER_COUNT = 0

CASES = []      # --cases, (status, body, content type) per GET /api/spectrum.json
CASE_I = [0]
OPEN = [0]     # connections the mock holds open right now (GET /__open), for the close-before-repeat tests

def elapsed():
    return (time.monotonic() - T0) if SYNCED else 0.0

def load_cases():
    # recorded responses (fixtures/responses/<name>), bodies as they are;
    # a .txt body takes its status line from the .hdr next to it (c10: "HTTP/1.1 404 Not Found")
    base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures", "responses")
    for item in args.cases.split(","):
        # "file|key=int|key=int*N" = the recorded file with some top-level numbers replaced, N times in a row
        times = int(item.rsplit("*", 1)[1]) if "*" in item else 1
        name, *overrides = item.rsplit("*", 1)[0].split("|")
        path = os.path.join(base, name)
        with open(path, "rb") as f:
            body = f.read()
        hang = 0
        status_over = 0   # the "@status" override, e.g. 401/403/503 with the body of the recorded case
        cut = 0
        gzmode = 0   # set by the @gz override below (it was reset AFTER the loop: the cause of the plain answers)
        if overrides:
            doc = json.loads(body.decode("utf-8"))
            for kv in overrides:
                k, v = kv.split("=")
                if k == "@gz":   # 1 = the body gzipped, 2 = gzipped with the 8-byte trailer cut off, 3 = gzipped and cut at 60%,
                    gzmode = int(v)   # 4 = gzipped with a wrong CRC32 (length right), 5 = a gzip bomb (3 MiB of zeros in a few KB)
                elif k == "@cut":   # the headers (whole Content-Length) and 60% of the body, then 1 = the connection is closed, 2 = silence for 20 s
                    cut = int(v)
                elif k == "@hang":   # the answer comes after N seconds (the client gives up first)
                    hang = int(v)
                elif k == "@status":   # the HTTP status of the answer (the body stays the recorded case's)
                    status_over = int(v)
                else:
                    doc[k] = int(v)
            body = json.dumps(doc, separators=(",", ":")).encode("utf-8")
        status, ctype = 200, "application/json"
        if status_over: status = status_over   # (a .txt case below may still take its own status line)
        hdr = os.path.splitext(path)[0] + ".hdr"
        if path.endswith(".txt") and os.path.exists(hdr):
            with open(hdr, encoding="utf-8") as f:
                status, ctype = int(f.read().split()[1]), "text/plain"
        CASES.extend([(status, body, ctype, gzmode, hang, cut)] * times)

def load_shape():
    global SHAPE, SHAPE_SUM
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures", "spectrum_sample.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    SHAPE = data["bins"]
    SHAPE_SUM = sum(SHAPE)

def ticker():
    global FiredEvents
    k = 1
    while True:
        time.sleep(0.01)
        if not SYNCED or elapsed() < k * args.tick:
            continue   # ticks at fixed points of the synced timeline: 1, 2, 3 ... s
        k += 1
        with LOCK:
            e = elapsed()
            # Apply one-shot timed events
            if args.decrease_at >= 0 and "decrease" not in FiredEvents and e >= args.decrease_at:
                FiredEvents.add("decrease")
                S["time"] = S["time"] // 2
                S["total"] = S["total"] // 2
                S["hist"] = 0
                S["cache_body"] = None
            if args.external_reset_at >= 0 and "ext_reset" not in FiredEvents and e >= args.external_reset_at:
                FiredEvents.add("ext_reset")
                S["valid"] = False
                S["time"] = 0
                S["total"] = 0
                S["cache_body"] = None
            if args.stop_at >= 0 and "stop" not in FiredEvents and e >= args.stop_at:
                FiredEvents.add("stop")
                S["counting"] = False
            # reset from outside and a NEW run that already overtook time/total (hist_ok is not reset)
            if args.overtake_at >= 0 and "overtake" not in FiredEvents and e >= args.overtake_at:
                FiredEvents.add("overtake")
                S["time"] += 30; S["total"] += 1000; S["new_run"] = True; S["cache_body"] = None
            # only the board time goes back (spectrum and total intact): caught by the counter check alone
            if args.time_back_at >= 0 and "timeback" not in FiredEvents and e >= args.time_back_at:
                FiredEvents.add("timeback")
                S["time"] = S["time"] // 2; S["cache_body"] = None
            # someone else resumes the stopped board with -sta (no reset)
            if args.resume_at >= 0 and "resume" not in FiredEvents and e >= args.resume_at:
                FiredEvents.add("resume")
                S["counting"] = True

            # Counting step
            if S["counting"]:
                S["time"] += 1
                S["total"] += 37
                S["hist"] += 1
                S["valid"] = True

def spectrum_body():
    global RENDER_COUNT
    with LOCK:
        e = elapsed()
        if S["cache_body"] is not None and e - S["cache_at"] < args.cache:
            return S["cache_body"]
        
        # Compute bins based on shape
        if S.get("new_run"):
            shape = [0]*300 + SHAPE[:-300]
            shape_sum = sum(shape)
        else:
            shape = SHAPE
            shape_sum = SHAPE_SUM
        
        total = S["total"]
        if shape_sum == 0:
            bins = [0] * 8192
        else:
            bins = [b * total // shape_sum for b in shape]
        
        # Ensure sum(bins) == total by adjusting the last non-zero bin or first bin if all zero
        # The prompt says: "The reported "total" is sum(bins) (the real board: sum(bins) == total)."
        # And "each channel never decreases when T grows".
        # The formula b * T // SHAPE_SUM might result in sum(bins) != T due to integer division.
        # We need to adjust bins so that sum(bins) == T.
        # Since we want to match the real board behavior where sum(bins) == total, we adjust the largest bin or distribute the remainder.
        # However, the prompt says "bins = [b * T // SHAPE_SUM for b in SHAPE]". It doesn't explicitly say to adjust for remainder, but it says "The reported "total" is sum(bins)".
        # If we just use the formula, sum(bins) might be slightly less than T.
        # Let's check the constraint: "The reported "total" is sum(bins)". This implies we should set total = sum(bins) in the JSON, OR adjust bins.
        # "The reported "total" is sum(bins) (the real board: sum(bins) == total)."
        # This suggests the JSON field "total" should be the sum of the bins array.
        
        reported_total = sum(bins)
        
        # Handle live field
        live_val = round(max(0.0, S["time"] - 0.8), 1)
        if args.live_nan:
            live_val = "__LIVE__"
        
        calib_floats = [float(x) for x in args.calib.split(",")]
        
        obj = {
            "bins": bins,
            "total": reported_total,
            "cpu": 10,
            "cps": 37,
            "lost": 5,
            "time": S["time"],
            "live": live_val,
            "bridge_drop": 0,
            "usb_rx_err": 0,
            "rx_ring_drops": 0,
            "hist_ok": S["hist"],
            "hist_drop": 0,
            "autosave_age_sec": 2,
            "autosave_fail_streak": 0,
            "t1": 27.0,
            "t2": None,
            "t3": None,
            "serial": "00000001",
            "dead": args.dead,
            "calib": [0.0] * len(calib_floats) if args.calib_unset else calib_floats,
            "calib_set": not args.calib_unset,   # --calib-unset = the board has no calibration
            "reset_unconfirmed": 0
        }
        
        body_str = json.dumps(obj, separators=(",", ":"))
        if args.live_nan:
            body_str = body_str.replace('"__LIVE__"', 'nan')
        
        body = body_str.encode()
        S["cache_body"] = body
        S["cache_at"] = e
        RENDER_COUNT += 1
        return body

class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):
        pass

    def setup(self):
        super().setup()
        with LOCK:
            OPEN[0] += 1

    def finish(self):
        try:
            super().finish()
        finally:
            with LOCK:
                OPEN[0] -= 1

    def send(self, status, body_bytes, ctype="application/json", extra=None, gzmode=0, hang=0, cut=0):
        if args.delay_ms > 0:
            time.sleep(args.delay_ms / 1000.0)
        if hang > 0:
            time.sleep(hang)
        
        gz = False
        if args.gzip and "gzip" in (self.headers.get("Accept-Encoding") or "") and status == 200:
            body_bytes = gzip.compress(body_bytes)
            gz = True
        if gzmode and status == 200:
            # the board's way (X-Spectrum-Gz): the case is gzipped whatever the request asked for
            body_bytes = gzip.compress(bytes(1) * (3 * 1024 * 1024) if gzmode == 5 else body_bytes)
            if gzmode == 4:
                body_bytes = body_bytes[:-8] + bytes(b ^ 0xFF for b in body_bytes[-8:-4]) + body_bytes[-4:]
            if gzmode == 2:
                body_bytes = body_bytes[:-8]
            elif gzmode == 3:
                body_bytes = body_bytes[:len(body_bytes) * 6 // 10]
            gz = True
        
        # Record log BEFORE sending response bytes , exactly once
        self._recorded = True
        self.record(status, gz)
        
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body_bytes)))
        if gz:
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Vary", "Accept-Encoding")
            self.send_header("X-Spectrum-Gz", "1")
        if extra:
            for k, v in extra.items():
                self.send_header(k, v)
        self.end_headers()
        
        if cut:
            try:
                self.wfile.write(body_bytes[:len(body_bytes) * 6 // 10])
                self.wfile.flush()
                if cut == 2:
                    time.sleep(20)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                pass
            self.close_connection = True
            return
        try:
            self.wfile.write(body_bytes)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass

    def record(self, status, gz):
        # Use stored method/path/query/body from route()
        method = getattr(self, '_req_method', '')
        path = getattr(self, '_req_path', '')
        query = getattr(self, '_req_query', {})
        body_text = getattr(self, '_req_body', '')
        
        log_entry = {
            "t": round(elapsed(), 3),
            "method": method,
            "path": path,
            "query": query,
            "body": body_text,
            "status": status,
            "gz": gz,
            "auth": self.headers.get("Authorization") is not None,
            "accept_encoding": self.headers.get("Accept-Encoding") or "",
            "csrf": self.headers.get("X-CSRF-Token") or ""
        }
        with LOGLOCK:
            with open(args.log, "a", encoding="utf-8") as f:
                f.write(json.dumps(log_entry) + "\n")
                f.flush()

    def route(self, method):
        global T0, SYNCED
        parts = urlsplit(self.path)
        path = parts.path
        if path == "/__sync":
            # test clock origin: not logged, not subject to auth/prefix/windows
            if not SYNCED:
                T0 = time.monotonic()
                SYNCED = True
            self.send_response(204)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if path == "/__open":
            # connections held open now, this one included; not logged
            raw = json.dumps({"open": OPEN[0]}).encode()
            self.send_response(200)
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)
            return
        query = parse_qs(parts.query)
        
        body_text = ""
        if method == "POST":
            n = int(self.headers.get("Content-Length") or 0)
            if n > 0:
                body_text = self.rfile.read(n).decode("utf-8", "replace")
        
        # Store request info for logging
        self._req_method = method
        self._req_path = path
        self._req_query = query
        self._req_body = body_text
        self._recorded = False
        
        try:
            e = elapsed()
            
            # Check hist-reset-at event
            if args.hist_reset_at >= 0 and "hist_reset" not in FiredEvents and e >= args.hist_reset_at:
                with LOCK:
                    if "hist_reset" not in FiredEvents:
                        FiredEvents.add("hist_reset")
                        S["hist"] = 0
                        S["cache_body"] = None

            # 0. Hang window the board accepts the connection and does not answer in time
            # only the spectrum poll hangs: commands answer at once, so a test can tell "Stop waited for the poll" apart
            if args.hang_from >= 0 and args.hang_from <= e < args.hang_from + args.hang_for and path.endswith("/api/spectrum.json"):
                time.sleep(args.hang_from + args.hang_for - e)
                self.send(503, b'{"error":"late"}')
            # 1. Down window
            elif args.down_from >= 0 and args.down_from <= e < args.down_from + args.down_for:
                if args.down_mode == "refuse":
                    # In refuse mode, we close the connection without sending a response.
                    # The main loop handles the socket shutdown, but for this specific request,
                    # we just close the connection.
                    self.close_connection = True
                    # Record with status 0
                    self.record(0, False)
                    self._recorded = True
                    return
                else:
                    self.send(503, b'{"error":"down"}')
            # 1b. from --fail-from seconds on, the spectrum poll answers --fail-status (401/403/503: the board answers, but not with data)
            elif args.fail_from >= 0 and e >= args.fail_from and path.endswith("/api/spectrum.json"):
                self.send(args.fail_status, b'{"error":"refused"}')
            # 2. Auth
            elif args.require_auth and self.headers.get("Authorization") != args.require_auth:
                self.send(401, b"unauthorized", "text/plain")
            # 3. Prefix
            elif args.prefix and not path.startswith(args.prefix + "/"):
                self.send(404, b"no prefix", "text/plain")
            # 4. Dispatch
            else:
                if args.prefix:
                    path = path[len(args.prefix):]
                
                if method == "GET" and path == "/api/spectrum.json" and CASES:
                    # one case per poll (the probes of Start included), the last one held
                    with LOCK:
                        i = min(CASE_I[0], len(CASES) - 1)
                        CASE_I[0] += 1
                    self.send(CASES[i][0], CASES[i][1], CASES[i][2], gzmode=CASES[i][3], hang=CASES[i][4], cut=CASES[i][5])

                elif method == "GET" and path == "/api/spectrum.json":
                    with LOCK:
                        valid = S["valid"]
                        # the 2 s JSON cache still holds the snapshot from before the reset
                        stale = args.reset_keeps_cache and S["cache_body"] is not None and elapsed() - S["cache_at"] < args.cache
                    if not valid and not stale:
                        self.send(404, b"No spectrum data yet", "text/plain", {"X-Spectrometer-Dead": str(args.dead)})
                    else:
                        body = spectrum_body()
                        # Get render count for header
                        with LOCK:
                            rc = RENDER_COUNT
                        self.send(200, body, extra={"X-Spectrum-Render-Count": str(rc), "Cache-Control": "no-store"})
                
                elif method == "GET" and path == "/api/status":
                    with LOCK:
                        t = S["time"]
                        tot = S["total"]
                    self.send(200, json.dumps({"time": t, "total_counts": tot, "fw_version": "mock-1.2.37", "analyzer_connected": True}).encode())
                
                elif method == "GET" and path == "/api/device":
                    calib_floats = [float(x) for x in args.calib.split(",")]
                    self.send(200, json.dumps({"valid": True, "rise": 11, "fall": 27, "freq": 14000000, "serial": "00000001", "calibration": [0.0] * len(calib_floats) if args.calib_unset else calib_floats, "calib_order": 4, "calib_set": not args.calib_unset}).encode())
                
                elif method == "GET" and path == "/api/boot-config":
                    # a development firmware shows gzip_level; release 1.2.37 has no such field
                    cfg = {"wifi_ssid": "mock", "hostname": "atomspectra"}
                    with LOCK:
                        if S["gzip_level"] >= 0:
                            cfg["gzip_level"] = S["gzip_level"]
                    self.send(200, json.dumps(cfg).encode())

                elif method == "POST" and path == "/api/boot-config":
                    # CSRF only; 0/1/6 or 400; {"ok":false} on a storage error;
                    # release 1.2.37 answers {"ok":true} and changes nothing (release 1.2.37)
                    if self.headers.get("X-CSRF-Token") != TOKEN:
                        self.send(403, b"Bad or missing CSRF token", "text/plain")
                    else:
                        try:
                            req = json.loads(body_text) if body_text.strip() else None
                        except ValueError:
                            req = "bad"
                        if req is None:
                            self.send(400, b"Empty body", "text/plain")
                        elif req == "bad" or not isinstance(req, dict):
                            self.send(400, b"Bad JSON", "text/plain")
                        elif S["gzip_level"] < 0:
                            self.send(200, b'{"ok":true}')
                        elif "gzip_level" in req and req["gzip_level"] not in (0, 1, 6):
                            self.send(400, b"gzip_level must be 0, 1 or 6", "text/plain")
                        elif args.boot_ok_false:
                            self.send(200, b'{"ok":false}')
                        else:
                            with LOCK:
                                if "gzip_level" in req and not args.boot_ignore:   # --boot-ignore: ok, but nothing changes
                                    S["gzip_level"] = req["gzip_level"]
                            self.send(200, b'{"ok":true}')

                elif method == "GET" and path == "/api/csrf-token":
                    self.send(200, json.dumps({"token": TOKEN}).encode())
                
                elif method == "GET" and path == "/api/devlog":
                    since = int(query.get("since", ["0"])[0])
                    with LOCK:
                        lines = list(DEVLOG)
                    filtered = [l for l in lines if l["seq"] > since]
                    last = lines[-1]["seq"] if lines else 0
                    self.send(200, json.dumps({"up_ms": int(elapsed()*1000), "lines": filtered[-8:], "next": last}).encode())   # ring of 8, as fw usb_host_cdc.c
                
                elif method == "POST" and path == "/api/command":
                    if self.headers.get("X-CSRF-Token") != TOKEN:
                        self.send(403, b"Bad or missing CSRF token", "text/plain")
                    elif args.cmd_ok_false:
                        self.send(200, b'{"ok":false}')
                    else:
                        cmd = body_text.strip()
                        late = False
                        with LOCK:
                            if cmd.startswith("-sta"):
                                S["counting"] = True
                            elif cmd.startswith("-sto"):
                                S["counting"] = False
                                late = args.sto_late   # executed, but the answer is lost
                            elif cmd.startswith("-rst"):
                                S["valid"] = False
                                S["time"] = 0
                                S["total"] = 0
                                S["cache_body"] = None
                        if not late and not args.cmd_no_text:
                            # the analyzer's text answer goes to the board log (fw: CMD_TEXT -> devlog_push), not the POST body;
                            # the text is SYNTHETIC (a mock), only its path is the board's
                            with LOCK:
                                seq = (DEVLOG[-1]["seq"] if DEVLOG else 0) + 1
                                DEVLOG.append({"seq": seq, "t": int(elapsed() * 1000), "text": "mock answer to " + cmd + "\r\n"})
                        if late:
                            self.send(503, b'{"error":"answer lost"}')
                        else:
                            self.send(200, b'{"ok":true}')
                
                elif method == "POST" and path == "/api/reset":
                    if args.reset_delay_ms > 0:
                        time.sleep(args.reset_delay_ms / 1000.0)   # the reset request is in flight while polling goes on
                    if self.headers.get("X-CSRF-Token") != TOKEN:
                        self.send(403, b"Bad or missing CSRF token", "text/plain")
                    elif args.reset_fail:
                        self.send(500, b"reset failed", "text/plain")
                    else:
                        if args.reset_unsent:
                            # Gateway did not deliver -rst, state unchanged
                            self.send(200, b'{"ok":true,"sent":false}')
                        else:
                            with LOCK:
                                S["valid"] = False
                                S["time"] = 0
                                S["total"] = 0
                                if args.reset_keeps_cache:
                                    S["cache_at"] = elapsed()   # the old body stays servable for another 2 s
                                else:
                                    S["cache_body"] = None
                            self.send(200, b'{"ok":true,"sent":true}')
                
                else:
                    self.send(404, b"not found", "text/plain")
        finally:
            # If no send() was called (e.g. refuse mode), record with status 0
            if not self._recorded:
                self.record(0, False)

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        self.route("POST")

def main():
    global args, S
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--log", type=str, required=True)
    parser.add_argument("--duration", type=float, default=40.0)
    parser.add_argument("--counting", type=int, default=0)
    parser.add_argument("--time", type=int, default=0)
    parser.add_argument("--total", type=int, default=0)
    parser.add_argument("--hist", type=int, default=100)
    parser.add_argument("--valid", type=int, default=1)
    parser.add_argument("--tick", type=float, default=1.0)
    parser.add_argument("--cache", type=float, default=2.0)
    parser.add_argument("--decrease-at", type=float, default=-1)
    parser.add_argument("--external-reset-at", type=float, default=-1)
    parser.add_argument("--stop-at", type=float, default=-1)
    parser.add_argument("--down-from", type=float, default=-1)
    parser.add_argument("--down-for", type=float, default=0)
    parser.add_argument("--down-mode", type=str, choices=["refuse", "503"], default="refuse")
    parser.add_argument("--delay-ms", type=int, default=0)
    parser.add_argument("--reset-delay-ms", type=int, default=0)
    parser.add_argument("--gzip", action="store_true")
    parser.add_argument("--cmd-ok-false", action="store_true")
    parser.add_argument("--reset-fail", action="store_true")
    parser.add_argument("--require-auth", type=str, default="")
    parser.add_argument("--prefix", type=str, default="")
    parser.add_argument("--dead", type=int, default=0)
    parser.add_argument("--overtake-at", type=float, default=-1)
    parser.add_argument("--resume-at", type=float, default=-1)
    parser.add_argument("--time-back-at", type=float, default=-1)
    parser.add_argument("--sto-late", action="store_true")
    parser.add_argument("--reset-keeps-cache", action="store_true")
    parser.add_argument("--hang-from", type=float, default=-1)
    parser.add_argument("--hang-for", type=float, default=0)
    parser.add_argument("--fail-from", type=float, default=-1)   # see "1b" in route()
    parser.add_argument("--fail-status", type=int, default=401)
    parser.add_argument("--calib", type=str, default="3.65,0.41,4.5e-05,-1.3e-08,1.2e-12")
    parser.add_argument("--calib-unset", action="store_true")
    parser.add_argument("--boot-gzip", type=int, default=-1)   # gzip_level in /api/boot-config; -1 = no such field (release 1.2.37)
    parser.add_argument("--boot-ok-false", action="store_true")
    parser.add_argument("--boot-ignore", action="store_true")   # answers {"ok":true} and keeps the old level (only a read-back can tell)
    parser.add_argument("--live-nan", action="store_true")
    parser.add_argument("--reset-unsent", action="store_true")
    parser.add_argument("--hist-reset-at", type=float, default=-1)
    parser.add_argument("--cmd-no-text", action="store_true")   # the analyzer gives no text answer (nothing reaches the board log)
    parser.add_argument("--cases", type=str, default="")   # comma list of fixtures/responses files
    args = parser.parse_args()

    # Load shape
    load_shape()
    if args.cases:
        load_cases()
    
    # Initialize state
    with LOCK:
        S["counting"] = bool(args.counting)
        S["time"] = args.time
        S["total"] = args.total
        S["hist"] = args.hist
        S["valid"] = bool(args.valid)
        S["gzip_level"] = args.boot_gzip
    
    # Truncate log
    open(args.log, "w").close()
    
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.daemon_threads = True
    server.allow_reuse_address = True
    
    t = threading.Thread(target=ticker, daemon=True)
    t.start()
    
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    
    print("LISTENING", args.port, flush=True)
    
    start = time.monotonic()
    while time.monotonic() - start < args.duration:
        time.sleep(0.02)
        
        # Handle down window in refuse mode
        e = elapsed()
        if args.down_from >= 0 and args.down_from <= e < args.down_from + args.down_for and args.down_mode == "refuse":
            # Close the listening socket first: from this moment a connect is refused at once (determinism);
            # shutdown() alone waits for serve_forever's 0.5 s poll and the OS backlog keeps accepting meanwhile
            try:
                server.socket.close()
            except OSError:
                pass
            server.shutdown()
            server.server_close()
            
            # Wait until window ends
            while time.monotonic() - start < args.duration:
                e = elapsed()
                if e >= args.down_from + args.down_for:
                    break
                time.sleep(0.02)
            
            # Restart server
            server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
            server.daemon_threads = True
            server.allow_reuse_address = True
            server_thread = threading.Thread(target=server.serve_forever, daemon=True)
            server_thread.start()
    
    server.shutdown()
    print("DONE", flush=True)

if __name__ == "__main__":
    main()
