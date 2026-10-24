# usage: python run_mutants.py <srcDir> <treeDir> <names> [mutation ids]  - each mutation of mutations/<MUT_FILE>.json (default startpkg): patch a copy of the tree,
# MSBuild it, run run_live.py (the scenarios <names>, comma separated) on the built exe. <treeDir> is a copy of the repository (the sources are patched in it).
import json, os, re, shutil, subprocess, sys, tempfile
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rest_suite   # MSBUILD: the environment variable or vswhere
src, tree, names = sys.argv[1], sys.argv[2], sys.argv[3]
D = os.path.dirname(os.path.abspath(__file__))
WORK = os.environ.get("REST_WORK") or os.path.join(tempfile.gettempdir(), "atomspectra_mutants")
for m in json.load(open(os.path.join(D, "mutations", os.environ.get("MUT_FILE", "startpkg") + ".json"), encoding="utf-8")):
    if len(sys.argv) > 4 and m["id"] not in sys.argv[4].split(","): continue
    shutil.rmtree(os.path.join(WORK, "work_mut"), ignore_errors=True)   # mock_cmd appends to its log: no log of an earlier mutant
    dst = os.path.join(tree, "BecquerelMonitor", m["file"])
    raw = open(os.path.join(src, m["file"]), "rb").read()
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    old, new = m["old"].replace("\n", nl), m["new"].replace("\n", nl)
    assert text.count(old) == 1, m["id"] + ": the patched text is not found exactly once"
    open(dst, "wb").write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + text.replace(old, new).encode("utf-8"))
    b = subprocess.run([rest_suite.MSBUILD, os.path.join(tree, "BecquerelMonitor", "BecquerelMonitor.csproj"), "-restore", "-p:Configuration=Release", "-p:OutputPath=bin\\mut\\", "-p:IntermediateOutputPath=obj\\mut\\", "-nologo", "-v:minimal"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(f"MUTATION {m['id']} (target {m['target']}): build rc={b.returncode}", flush=True)
    if b.returncode != 0:
        print("    BUILD FAIL, not run: " + b.stdout[-800:], flush=True)
        shutil.copy2(os.path.join(src, m["file"]), dst)
        continue
    r = subprocess.run([sys.executable, "-u", os.path.join(D, "run_live.py"), os.path.join(tree, "BecquerelMonitor", "bin", "mut"), os.path.join(WORK, "work_mut"), names], capture_output=True, text=True, encoding="utf-8", errors="replace")
    red = sorted(set(re.findall(r"\] (\S+): FAIL", r.stdout)))
    summary = re.search(r"PASS \d+/\d+", r.stdout)
    print("    run: " + (summary.group(0) if summary else "RUN ERROR " + (r.stdout + r.stderr)[-600:]), flush=True)
    print(f"MUTATION {m['id']}: {'RED' if m['target'] in red else 'GREEN'}; red tests: {', '.join(red) or 'none'}; exactly one: {'yes' if red == [m['target']] else 'NO'}", flush=True)
    shutil.copy2(os.path.join(src, m["file"]), dst)   # back to the unmutated source
