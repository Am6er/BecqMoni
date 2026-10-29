# the real-build mutations with only the named ids (a rerun of part of the set; the full set is rest_suite.py --real-mutations)
# usage: run_real_mutations_subset.py <srcDir> <workDir> <id,id,...>
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")
import rest_suite
ids = sys.argv[3].split(",")
rest_suite.REAL_MUTATIONS = [m for m in rest_suite.REAL_MUTATIONS if m[0] in ids]
if len(rest_suite.REAL_MUTATIONS) != len(ids):
    print("unknown mutation ids:", ids)
    sys.exit(2)
sys.exit(0 if rest_suite.run_real_mutations(sys.argv[1], sys.argv[2]) else 1)
