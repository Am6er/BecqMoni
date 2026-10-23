import sys
import os
import shutil
import subprocess
import socket
import time
import json
import tempfile

sys.stdout.reconfigure(encoding="utf-8")

CSC = os.environ.get("ATOMSPECTRA_CSC") or r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
D = os.path.dirname(os.path.abspath(__file__))
W = os.environ.get("REST_WORK") or os.path.join(tempfile.gettempdir(), "atomspectra_tests_tcp")

def get_free_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port

def wait_for_log(path, token, timeout=10.0):
    start = time.time()
    while time.time() - start < timeout:
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as f:
                    if token in f.read():
                        return True
            except Exception:
                pass
        time.sleep(0.05)
    return False

def run_subprocess(cmd, timeout=120):
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
        return p
    except subprocess.TimeoutExpired:
        return None

def build_exe_ref(out_dir, exe_dir):
    os.makedirs(out_dir, exist_ok=True)
    src_exe = os.path.join(exe_dir, "BecquerelMonitor.exe")
    dst_exe = os.path.join(out_dir, "BecquerelMonitor.exe")
    shutil.copy2(src_exe, dst_exe)
    
    session_harness_cs = os.path.join(D, "harness", "SessionHarness.cs")
    cal_harness_cs = os.path.join(D, "harness", "CalHarness.cs")
    
    cmd_session = [CSC, "-nologo", "-langversion:7.3", "-r:" + dst_exe, "-out:" + os.path.join(out_dir, "SessionHarness.exe"), session_harness_cs]
    p1 = run_subprocess(cmd_session)
    if p1 is None or p1.returncode != 0:
        raise Exception("Failed to build SessionHarness.exe (ref): " + (p1.stderr if p1 else "timeout"))
        
    cmd_cal = [CSC, "-nologo", "-langversion:7.3", "-r:" + dst_exe, "-out:" + os.path.join(out_dir, "CalHarness.exe"), cal_harness_cs]
    p2 = run_subprocess(cmd_cal)
    if p2 is None or p2.returncode != 0:
        raise Exception("Failed to build CalHarness.exe (ref): " + (p2.stderr if p2 else "timeout"))

def build_src(out_dir, src_dir, mutation):
    os.makedirs(out_dir, exist_ok=True)
    
    files_to_copy = [
        "AtomSpectraTcpTransport.cs",
        "AtomSpectraTcpAcquisition.cs",
        "AtomSpectraTcpSession.cs",
        "AtomSpectraTcpCalibrationReader.cs",
        "AtomSpectraTcpDeviceConfig.cs",
        "InputDeviceConfig.cs",
        "Hash/Crc32.cs",
        # the TCP sources now reference the shared log, the session interface and the REST types
        "AtomSpectraTcpLog.cs",
        "IAtomSpectraSession.cs",
        "AtomSpectraTcpDeviceConfig.Rest.cs",
        "AtomSpectraRestPolicy.cs",
        "AtomSpectraRestClient.cs",
        "AtomSpectraRestAcquisition.cs",
        "AtomSpectraRestSession.cs",
        "AtomSpectraRestCalibration.cs"
    ]
    
    for f in files_to_copy:
        src_path = os.path.join(src_dir, f)
        dst_name = os.path.basename(f)
        dst_path = os.path.join(out_dir, dst_name)
        with open(src_path, "r", encoding="utf-8-sig") as fin:
            content = fin.read()
        
        if mutation:
            m_file, old, new = mutation
            if os.path.basename(m_file) == dst_name:
                count = content.count(old)
                if count != 1:
                    raise Exception(f"Mutation failed: expected 1 occurrence of {repr(old)} in {dst_name}, found {count}")
                content = content.replace(old, new)
        
        with open(dst_path, "w", encoding="utf-8-sig") as fout:
            fout.write(content)
    
    stubs_content = """namespace BecquerelMonitor.Properties { public static class Resources {
      public static string ERRAtomSpectraTcpCalBusy = "busy {0}:{1}"; public static string ERRAtomSpectraTcpConnect = "connect {0}:{1} {2}";
      public static string ERRIncorrectCRC = "crc"; public static string ERREmptyCoefficients = "empty"; public static string ERRReadDataFromPort_Empty = "noanswer";
      public static string ERRAtomSpectraTcpCalGap = "gap"; public static string MSGAtomSpectraRestLinkLost = "REST {0}: polling every {1} s"; public static string ERRAtomSpectraRestUnreachable = "REST {0} ({2}): not started, polling every {1} s"; } }
namespace BecquerelMonitor { public class PolynomialEnergyCalibration { public int PolynomialOrder { get; set; } public double[] Coefficients { get; set; } } }
"""
    with open(os.path.join(out_dir, "Stubs.cs"), "w", encoding="utf-8-sig") as f:
        f.write(stubs_content)
    
    all_cs = [os.path.join(out_dir, os.path.basename(f)) for f in files_to_copy]
    all_cs.append(os.path.join(out_dir, "Stubs.cs"))
    
    session_harness_cs = os.path.join(D, "harness", "SessionHarness.cs")
    cal_harness_cs = os.path.join(D, "harness", "CalHarness.cs")
    
    cmd_session = [CSC, "-nologo", "-langversion:7.3", "-nowarn:0162,0219", "-r:System.Windows.Forms.dll", "-r:System.Security.dll", "-out:" + os.path.join(out_dir, "SessionHarness.exe")]
    cmd_session.append(session_harness_cs)
    cmd_session.extend(all_cs)
    p1 = run_subprocess(cmd_session)
    if p1 is None or p1.returncode != 0:
        raise Exception("Failed to build SessionHarness.exe (src): " + (p1.stderr if p1 else "timeout"))
        
    cmd_cal = [CSC, "-nologo", "-langversion:7.3", "-nowarn:0162,0219", "-r:System.Windows.Forms.dll", "-r:System.Security.dll", "-out:" + os.path.join(out_dir, "CalHarness.exe")]
    cmd_cal.append(cal_harness_cs)
    cmd_cal.extend(all_cs)
    p2 = run_subprocess(cmd_cal)
    if p2 is None or p2.returncode != 0:
        raise Exception("Failed to build CalHarness.exe (src): " + (p2.stderr if p2 else "timeout"))

def run_scenario(name, harness_exe, mock_path, mock_args, harness_args, run_dir):
    port = get_free_port()
    mock_proc = None
    mock_log_path = None
    
    if mock_path:
        mock_log_path = os.path.join(run_dir, f"{name}_cmd.jsonl")
        if os.path.exists(mock_log_path):
            os.remove(mock_log_path)
        
        final_mock_args = mock_args + ["--port", str(port), "--log", mock_log_path]
        mock_log_file = open(mock_log_path, "w", encoding="utf-8")
        mock_proc = subprocess.Popen([sys.executable, "-u", mock_path] + final_mock_args, stdout=mock_log_file, stderr=subprocess.STDOUT)
        
        if not wait_for_log(mock_log_path, "LISTENING", 10.0):
            if mock_proc:
                mock_proc.kill()
            mock_log_file.close()
            return False, "Mock failed to start"
    else:
        # For failfast, we just use the port, no mock
        pass

    final_harness_args = harness_args(port, mock_log_path)
    harness_exe_path = os.path.join(run_dir, harness_exe)
    
    try:
        p = subprocess.run([harness_exe_path] + final_harness_args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
        if p.stdout:
            for line in p.stdout.splitlines():
                print("    " + line)
        if p.stderr:
            for line in p.stderr.splitlines():
                print("    [ERR] " + line)
        return p.returncode == 0, p.stderr
    except subprocess.TimeoutExpired:
        return False, "Harness timeout"
    finally:
        if mock_proc:
            try:
                mock_proc.wait(timeout=30)
            except subprocess.TimeoutExpired:
                mock_proc.kill()
            if mock_log_path:
                try:
                    with open(mock_log_path, "r", encoding="utf-8", errors="replace") as f:
                        log_content = f.read()
                        if log_content:
                            for line in log_content.splitlines():
                                print("    [MOCK] " + line)
                except Exception:
                    pass

def main():
    if len(sys.argv) != 3:
        print("Usage: python run_review_tests.py <exeDir> <srcDir>")
        sys.exit(1)
    
    exe_dir = sys.argv[1]
    src_dir = sys.argv[2]
    
    os.makedirs(W, exist_ok=True)
    
    def get_scenarios(run_dir):
        return [
            ("p1", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "20", "--connections", "2", "--drop-cycle", "2"], lambda port, log: [f"p1", str(port), log]),
            ("p1clear", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "10", "--connections", "3", "--drop-cycle", "2", "--down-ms", "2000"], lambda port, log: [f"p1clear", str(port), log]),
            ("p2fast", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "20", "--connections", "2", "--drop-cycle", "2"], lambda port, log: [f"p2fast", str(port), log]),
            ("p2deferred", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "10", "--connections", "2", "--drop-cycle", "2", "--down-ms", "3500"], lambda port, log: [f"p2deferred", str(port), log]),
            ("busy", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "8", "--connections", "1"], lambda port, log: [f"busy", str(port)]),
            ("failfast", "SessionHarness.exe", None, None, lambda port, log: [f"failfast", "127.0.0.1", str(port)]),
            ("pairing-hs", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "1.0", "--cycles", "6", "--connections", "2", "--drop-cycle", "2", "--stale-join", "--expected", os.path.join(run_dir, "expected_hs.txt")], lambda port, log: [f"pairing", str(port), os.path.join(run_dir, "expected_hs.txt"), "5"]),
            ("pairing-sh", "SessionHarness.exe", "mock_cmd.py", ["--order", "sh", "--period", "1.0", "--cycles", "6", "--connections", "2", "--drop-cycle", "2", "--stale-join", "--expected", os.path.join(run_dir, "expected_sh.txt")], lambda port, log: [f"pairing", str(port), os.path.join(run_dir, "expected_sh.txt"), "5"]),
            ("cal-ok", "CalHarness.exe", "mock_cal.py", ["--mode", "ok", "--coeffs", "0.5,3.0,0,0,0"], lambda port, log: ["127.0.0.1", str(port), "None", "0.5,3.0"]),
            ("cal-nan", "CalHarness.exe", "mock_cal.py", ["--mode", "ok", "--coeffs", "0.5,nan,0,0,0"], lambda port, log: ["127.0.0.1", str(port), "InvalidNumber"]),
            ("cal-inf", "CalHarness.exe", "mock_cal.py", ["--mode", "ok", "--coeffs", "0.5,inf,0,0,0"], lambda port, log: ["127.0.0.1", str(port), "InvalidNumber"]),
            ("cal-gap", "CalHarness.exe", "mock_cal.py", ["--mode", "ok", "--coeffs", "0.5,none,1e-5,0,0"], lambda port, log: ["127.0.0.1", str(port), "GapInCoefficients"]),
            # PR37 fix, TCP (the fix-* modes of SessionHarness.cs): T1 a reset from outside (and our own -rst as the control), T2 a silent
            # bridge, T3 one status in the probe, T7 the document at Start, T4 an unanswered -sto (the mock waits before streaming, so
            # the resent -sto is read before anything is written to the closed socket) and an open socket without data
            ("fix-reset", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "2", "--drop-cycle", "3", "--reset-at", "4"], lambda port, log: ["fix-reset", str(port), log]),
            ("fix-ownreset", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "1", "--honor-rst"], lambda port, log: ["fix-ownreset", str(port), log]),
            ("fix-silent", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "4", "--connections", "1", "--silent-after", "0"], lambda port, log: ["fix-silent", str(port), log]),
            ("fix-silent-ask", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "4", "--connections", "1", "--silent-after", "0"], lambda port, log: ["fix-silent-ask", str(port), log]),
            ("fix-onesample", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "1", "--connections", "1", "--stopped-at", "500", "--first-delay", "0.8"], lambda port, log: ["fix-onesample", str(port), log]),
            ("fix-onesample-headless", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "1", "--connections", "1", "--stopped-at", "500", "--first-delay", "0.8"], lambda port, log: ["fix-onesample-headless", str(port), log]),
            ("fix-docbehind", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1"], lambda port, log: ["fix-docbehind", str(port), log]),
            ("fix-docbehind-ask", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "12", "--connections", "1"], lambda port, log: ["fix-docbehind-ask", str(port), log]),
            ("fix-stopmute", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "20", "--connections", "2", "--mute", "sto", "--first-delay", "0.5"], lambda port, log: ["fix-stopmute", str(port), log]),
            ("fix-stall", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "30", "--connections", "2", "--silent-after", "3"], lambda port, log: ["fix-stall", str(port), log]),
            # the controller by reflection: real build only (n/a on the source build); T8 Clear of a joined / own acquisition, T1+T9 the controller on a reset;
            # the last argument is the directory of the build, where the harness finds the program's own libraries
            ("fix-ctl-clear", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "4", "--connections", "2", "--honor-rst"], lambda port, log: ["fix-ctl-clear", str(port), log, exe_dir]),
            ("fix-ctl-reset", "SessionHarness.exe", "mock_cmd.py", ["--order", "hs", "--period", "0.5", "--cycles", "16", "--connections", "2", "--drop-cycle", "3", "--reset-at", "4"], lambda port, log: ["fix-ctl-reset", str(port), log, exe_dir]),
        ]

    def run_scenarios(build_dir, scenario_list):
        results = {}
        for name, harness_exe, mock_path, mock_args, harness_builder in scenario_list:
            run_dir = build_dir
            port = get_free_port()
            mock_proc = None
            mock_log_path = None
            
            if mock_path:
                mock_log_path = os.path.join(run_dir, f"{name}_cmd.jsonl")
                if os.path.exists(mock_log_path):
                    os.remove(mock_log_path)
                
                final_mock_args = mock_args + ["--port", str(port), "--log", mock_log_path]
                mock_out_path = os.path.join(run_dir, f"{name}_mock.txt")
                mock_log_file = open(mock_out_path, "w", encoding="utf-8")
                mock_proc = subprocess.Popen([sys.executable, "-u", os.path.join(D, "mocks", mock_path)] + final_mock_args, stdout=mock_log_file, stderr=subprocess.STDOUT)
                
                if not wait_for_log(mock_out_path, "LISTENING", 10.0):
                    if mock_proc:
                        mock_proc.kill()
                    mock_log_file.close()
                    results[name] = False
                    continue
            else:
                # For failfast, we just use the port, no mock
                pass

            final_harness_args = harness_builder(port, mock_log_path)
            harness_exe_path = os.path.join(run_dir, harness_exe)
            
            try:
                p = subprocess.run([harness_exe_path] + final_harness_args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
                if p.stdout:
                    for line in p.stdout.splitlines():
                        print("    " + line)
                if p.stderr:
                    for line in p.stderr.splitlines():
                        print("    [ERR] " + line)
                results[name] = (p.returncode == 0)
            except subprocess.TimeoutExpired:
                results[name] = False
            finally:
                if mock_proc:
                    try:
                        mock_proc.wait(timeout=30)
                    except subprocess.TimeoutExpired:
                        mock_proc.kill()
                    if mock_log_path:
                        try:
                            with open(mock_log_path, "r", encoding="utf-8", errors="replace") as f:
                                log_content = f.read()
                                if log_content:
                                    for line in log_content.splitlines():
                                        print("    [MOCK] " + line)
                        except Exception:
                            pass
        return results

    # 1. BASE on real build
    base_real_dir = os.path.join(W, "base")
    if os.path.exists(base_real_dir):
        shutil.rmtree(base_real_dir)
    build_exe_ref(base_real_dir, exe_dir)
    scenarios = get_scenarios(base_real_dir)
    res_base_real = run_scenarios(base_real_dir, scenarios)
    
    pass_count_real = sum(1 for v in res_base_real.values() if v)
    total_real = len(res_base_real)
    print(f"BASE real: {pass_count_real}/{total_real}")
    for name, res in res_base_real.items():
        print(f"BASE {name}: {'PASS' if res else 'FAIL'}")

    # 2. BASE on source build
    base_src_dir = os.path.join(W, "src_base")
    if os.path.exists(base_src_dir):
        shutil.rmtree(base_src_dir)
    build_src(base_src_dir, src_dir, None)
    scenarios = get_scenarios(base_src_dir)
    res_base_src = run_scenarios(base_src_dir, scenarios)
    
    pass_count_src = sum(1 for v in res_base_src.values() if v)
    total_src = len(res_base_src)
    print(f"BASE src: {pass_count_src}/{total_src}")
    for name, res in res_base_src.items():
        print(f"BASE {name}: {'PASS' if res else 'FAIL'}")

    # 3. Mutations
    mutations = [
        ("clear-flag", "Clear in an outage also sets resetOnNextStart, as before the fix", ("AtomSpectraTcpSession.cs", "acquisition.QueueOnReconnect(\"-rst\");", "acquisition.QueueOnReconnect(\"-rst\"); resetOnNextStart = true;"), ["p1clear"]),
        ("reconnect-rst", "the automatic reconnect sends -rst", ("AtomSpectraTcpSession.cs", "acquisition.CommandOnReconnect = \"-sta\";", "acquisition.CommandOnReconnect = \"-rst\";"), ["p1"]),
        ("no-stop-retry", "Stop without a link sends nothing, as before the fix", ("AtomSpectraTcpSession.cs", "acquisition.CommandOnReconnect = \"-sto\";", "Close(); return AtomSpectraTcpStopOutcome.Stopped;"), ["p2fast", "p2deferred"]),
        ("event-on-status", "an event on every status packet, as before the fix", ("AtomSpectraTcpAcquisition.cs", "                    statusFresh = true;\n", "                    statusFresh = true; histogramFresh = true;\n"), ["pairing-hs", "pairing-sh"]),
        ("no-window", "no pairing time window (a leftover status of the previous cycle pairs with the next histogram)", ("AtomSpectraTcpAcquisition.cs", "public const int PairWindowMs = 400;", "public const int PairWindowMs = int.MaxValue;"), ["pairing-hs", "pairing-sh"]),
        ("nan-accepted", "NaN/Infinity accepted", ("AtomSpectraTcpCalibrationReader.cs", "if (double.IsNaN(value) || double.IsInfinity(value))", "if (false)"), ["cal-nan", "cal-inf"]),
        ("gap-compacts", "a gap compacts the list", ("AtomSpectraTcpCalibrationReader.cs", "if (emptySeen)", "if (false)"), ["cal-gap"]),
        # PR37 fix, TCP
        ("reset-copied", "a drop of the board's counters goes to the document (no reset guard)", ("AtomSpectraTcpSession.cs", "if (e.ElapsedTime < guardTime || total < guardTotal || channel >= 0)", "if (false)"), ["fix-reset"]),
        ("own-rst-unknown", "our own -rst is not noted: Clear is taken for a reset from outside", ("AtomSpectraTcpAcquisition.cs", "Interlocked.Exchange(ref resetWrittenAtMs, ClockMs);", ""), ["fix-ownreset"]),
        ("silent-blind", "a silent bridge gets a blind -sta", ("AtomSpectraTcpSession.cs", "if (BoardState == AtomSpectraTcpBoardState.Silent) plan = AtomSpectraRestStartPlan.Ask;", ""), ["fix-silent", "fix-silent-ask"]),
        ("one-sample-counts", "one status is taken for a counting board (join, no -sta)", ("AtomSpectraTcpSession.cs", "bool counting = grew;", "bool counting = grew || s.Length == 1;"), ["fix-onesample", "fix-onesample-headless"]),
        ("doc-ignored", "the plan does not know what the document holds", ("AtomSpectraTcpSession.cs", "PlanStart(false, Ask != null, counting, board, DocumentSeconds, DocumentCounts)", "PlanStart(false, Ask != null, counting, board, 0, 0)"), ["fix-docbehind"]),
        ("doc-continue-allowed", "Continue is offered when the board holds less than the document", ("AtomSpectraTcpSession.cs", "ContinueAllowed = continueAllowed", "ContinueAllowed = true"), ["fix-docbehind-ask"]),
        ("sto-unanswered-stopped", "Stop reports Stopped when -sto got no answer", ("AtomSpectraTcpSession.cs", "if (Command(\"-sto\", commandTimeoutMs))", "Command(\"-sto\", commandTimeoutMs); if (true)"), ["fix-stopmute"]),
        ("no-stall-watchdog", "an open socket without data is never a lost link", ("AtomSpectraTcpSession.cs", "acquisition.NoDataLimitMs = NoDataLinkLostMs;", "acquisition.NoDataLimitMs = 0;"), ["fix-stall"]),
    ]
    
    red_count = 0
    total_mutations = len(mutations)
    
    for mid, desc, mutation, target_scenarios in mutations:
        mut_dir = os.path.join(W, f"mut_{mid}")
        if os.path.exists(mut_dir):
            shutil.rmtree(mut_dir)
        
        try:
            build_src(mut_dir, src_dir, mutation)
        except Exception as e:
            print(f"MUTATION {mid} ({desc}): BUILD FAIL")
            continue
            
        scenarios = get_scenarios(mut_dir)
        # Filter scenarios to only those in target_scenarios
        filtered_scenarios = [s for s in scenarios if s[0] in target_scenarios]
        
        res_mut = run_scenarios(mut_dir, filtered_scenarios)
        
        # Mutation is RED if at least one listed scenario FAILS
        is_red = any(not res_mut.get(name, False) for name in target_scenarios)
        
        if is_red:
            red_count += 1
            status = "RED"
        else:
            status = "GREEN"
            
        print(f"MUTATION {mid} ({desc}): {status}")
        for name in target_scenarios:
            res = res_mut.get(name, False)
            print(f"    {name}: {'PASS' if res else 'FAIL'}")

    # 4. Summary
    print(f"mock tests (base, real build): {pass_count_real}/{total_real}")
    print(f"mock tests (base, source build): {pass_count_src}/{total_src}")
    print(f"mutations red: {red_count}/{total_mutations}")
    
    all_passed = (pass_count_real == total_real) and (pass_count_src == total_src) and (red_count == total_mutations)

    # 5. REST mode (): own harness, mock board and mutations (rest_suite.py)
    import rest_suite
    # REST runs work on a local disk (build dirs and mock logs on a synced folder add IO stalls); under the work directory
    # of this run, so parallel runs with their own REST_WORK do not share it
    rest_ok = rest_suite.run_rest_suite(exe_dir, src_dir, os.path.join(W, "rest_part"))
    print(f"TCP suite: {'PASS' if all_passed else 'FAIL'}; REST suite: {'PASS' if rest_ok else 'FAIL'}")
    sys.exit(0 if (all_passed and rest_ok) else 1)

if __name__ == "__main__":
    main()
