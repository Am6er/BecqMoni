# -*- coding: utf-8 -*-
r"""П85 (AMBER42): прогон настоящего нуклида в TCCFCALC2 с ключом angular=true/false.
Пишет tccfcalc.in из <геометрия>.in через run_tccf2.upgrade_in (блок параметров ЦЕЛИКОМ),
зовёт TccfProbe2.exe, копирует отчёт в out_<tag>.txt.
    python tccf_run.py --workdir D:\BqMoni_Claude\p85\tccf --geometry <.in> --a 60 --z 27 --decays 40000000 --angular 1 --tag co60_ang1 [--seed 20260915]
"""
import argparse, io, os, re, shutil, subprocess, sys, time
sys.path.insert(0, r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\tccfcalc2")
import run_tccf2

p = argparse.ArgumentParser()
p.add_argument("--workdir", required=True)
p.add_argument("--geometry", required=True)
p.add_argument("--a", type=int, required=True)
p.add_argument("--z", type=int, required=True)
p.add_argument("--m", type=int, default=0)
p.add_argument("--decays", type=int, default=1000000)
p.add_argument("--angular", type=int, default=1)
p.add_argument("--seed", type=int, default=20260915)
p.add_argument("--tag", required=True)
a = p.parse_args()

wd = a.workdir
run_tccf2.upgrade_in(a.geometry, os.path.join(wd, "tccfcalc.in"), variant="full", threads=1,
                     extra_params={"angular": bool(a.angular), "calc_coincidence": True})
args = [os.path.join(wd, "TccfProbe2.exe"), wd, str(a.a), str(a.z), str(a.m), str(a.decays), "--seed=%d" % a.seed]
t0 = time.time()
proc = subprocess.run(args, cwd=wd, capture_output=True, text=True, encoding="latin-1")
dt = time.time() - t0
log = os.path.join(wd, "log_%s.txt" % a.tag)
io.open(log, "w", encoding="utf-8").write(proc.stdout + proc.stderr)
if proc.returncode != 0 or "Prepare -> 0" not in proc.stdout or not re.search(r"^Calculate -> 0\s*$", proc.stdout, re.M):
    sys.stderr.write(proc.stdout[-3000:] + proc.stderr)
    raise SystemExit("TccfProbe2: код %d / Prepare или Calculate отказал" % proc.returncode)
out = os.path.join(wd, "tccfcalc.out")
dst = os.path.join(wd, "out_%s.txt" % a.tag)
shutil.copyfile(out, dst)
print("%s: A=%d Z=%d распадов %d angular=%d зерно %d, %.1f с -> %s" % (a.tag, a.a, a.z, a.decays, a.angular, a.seed, dt, dst))
