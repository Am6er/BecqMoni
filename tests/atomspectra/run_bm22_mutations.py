"""#BM-22: runs only the bm22-* real-build mutations of rest_suite.py against ALL real-build scenarios (IMPL-11: the exactly-one check over the whole set, as run_pr2_mutations.py).
Usage: python run_bm22_mutations.py <srcDir> <workDir>   (srcDir = the BecquerelMonitor/ directory of the tree)
Each mutation must turn RED exactly its own scenario; the others stay green."""
import os, shutil, sys
sys.stdout.reconfigure(encoding="utf-8")
import rest_suite as rs

src_dir, work_dir = sys.argv[1], sys.argv[2]
tree_root = os.path.dirname(os.path.abspath(src_dir))
scn = [s for s in rs.SCENARIOS if s[0] in rs.REAL_ONLY_MIN_CHECKS or s[0] in ("f5-ctl-calib", "f5-ctl-calib-channels", "f5-form-calib")]   # all real-build scenarios (a mutation of one may redden another)
only = sys.argv[3].split(",") if len(sys.argv) > 3 else None   # optional: a comma list of mutation ids
for mid, desc, replacements, target in [m for m in rs.REAL_MUTATIONS if m[0].startswith("bm22-") and (only is None or m[0] in only)]:
    mdir = os.path.join(work_dir, "realmut_" + mid)
    try:
        exe_dir = rs.build_real_mutant(tree_root, mdir, replacements)
        bdir = os.path.join(mdir, "harness")
        rs.build_ref(bdir, exe_dir)
    except RuntimeError as e:
        print(f"MUTATION {mid}: BUILD FAIL {e}", flush=True)
        continue
    res = rs.run_many(bdir, scn)
    failed = sorted(n for n, (ok, _) in res.items() if not ok)
    print(f"MUTATION {mid} ({desc}): {'RED' if target in failed else 'GREEN'}; red tests: {', '.join(failed) or 'none'}; exactly one: {'yes' if failed == [target] else 'NO'}", flush=True)
    shutil.rmtree(mdir, ignore_errors=True)
