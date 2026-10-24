# usage: python run_live.py <exeDir> <workDir> name1,name2,...  - one line per scenario as soon as it ends (live log)
import os, sys, time, concurrent.futures
sys.stdout.reconfigure(encoding="utf-8")
import rest_suite as rs
exe_dir, work, names = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
ref = os.path.join(work, "base")
t0 = time.time()
rs.build_ref(ref, exe_dir)
print(f"[{time.time() - t0:6.1f}s] harness built against {exe_dir}", flush=True)
args = dict(rs.SCENARIOS)
missing = [n for n in names if n not in args]
ok_count = 0
with concurrent.futures.ThreadPoolExecutor(max_workers=int(os.environ.get("RL_WORKERS", "4"))) as ex:
    futs = {ex.submit(rs.run_one, ref, n, args[n]): n for n in names if n in args}
    for f in concurrent.futures.as_completed(futs):
        ok, text = f.result()
        ok_count += ok
        print(f"[{time.time() - t0:6.1f}s] {futs[f]}: {'PASS' if ok else 'FAIL'}", flush=True)
        if not ok:
            for line in text.splitlines():
                print("    " + line, flush=True)
print(f"PASS {ok_count}/{len(names)}; unknown: {missing}", flush=True)
sys.exit(0 if ok_count == len(names) and not missing else 1)
