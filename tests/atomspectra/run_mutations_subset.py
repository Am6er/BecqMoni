# the suite with only the named source mutations (a rerun of part of the set, in parallel with the long full run).
# usage: run_mutations_subset.py <exeDir> <srcDir> <workDir> <id,id,...>   (no real-build mutations: they run apart, rest_suite.py --real-mutations)
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")
import rest_suite
ids = sys.argv[4].split(",")
rest_suite.MUTATIONS = [m for m in rest_suite.MUTATIONS if m[0] in ids]
missing = [i for i in ids if i not in [m[0] for m in rest_suite.MUTATIONS]]
if missing:
    print("unknown mutation ids:", missing)
    sys.exit(2)
rest_suite.REAL_MUTATIONS = []
ok = rest_suite.run_rest_suite(sys.argv[1], sys.argv[2], sys.argv[3])
print("SUBSET " + ("OK" if ok else "NOT OK"))
sys.exit(0 if ok else 1)
