# -*- coding: utf-8 -*-
u"""
Тройная сверка на одной геометрии и одной сетке: НАШ расчёт — ЛСРМ 2.10 —
Geant4. Считает пик полного поглощения и полную эффективность всеми тремя и
печатает таблицу отношений.

Зачем именно так:

* **геометрия одна на троих.** Из файла выбрасываются наши ключи коробки
  (`DS_CrystalBox*`): ЛСРМ кристалл-брус не умеет и молча считает цилиндр, —
  значит и наш расчёт, и Geant4 должны считать цилиндр, иначе сравниваются
  разные сцены. Копия «без коробки» кладётся в каталог результатов;
* **ЛСРМ — с фиксированным зерном** (`--seed`, шестое слово `Prepare`,
  README §13.8) и в ОДИН поток: в несколько потоков она размножает один и тот
  же поток случайных чисел;
* **ЛСРМ — с `calc_full_eff`**, иначе одиннадцатого столбца (полной
  эффективности) в отчёте нет;
* **Geant4 — по сцене из `effsim --dump-scene`**, то есть по нашей же сцене,
  область в область.

    python three_way.py --workdir=<копия каталога TCCFCALC>
                        [--geoms=Nano16Pro_tube,...] [--energies=60,200,662,...]
                        [--lsrm=10000000] [--our=2000000] [--g4=5000000]
                        [--out=out/3way] [--skip=lsrm,g4]

Работа с ЛСРМ идёт ТОЛЬКО в копии каталога.
"""
import argparse
import io
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import run_tccf2                                       # noqa: E402

MODELS = os.path.join(REPO, "tools", "effmaker", "models")
EFFSIM_DIR = os.path.join(REPO, "BecquerelMonitor", "bin", "Debug_Codex")
EFFSIM = os.path.join(EFFSIM_DIR, "effsim.exe")


def set_effsim(directory):
    u"""Каталог сборки нашей стороны. Отдельный каталог нужен, когда в дереве
    работает параллельная сессия: собирать в общий bin/Debug_Codex — значит
    подменить ей бинарь под руками."""
    global EFFSIM_DIR, EFFSIM
    EFFSIM_DIR = directory
    EFFSIM = os.path.join(directory, "effsim.exe")
G4DIR = os.path.join(REPO, "tools", "g4cf")
G4RUN = os.path.join(G4DIR, "run_g4cf.bat")
G4EXE = os.path.join(G4DIR, "build", "g4cf.exe")


def g4_env():
    u"""Окружение Geant4 берём из `run_g4cf.bat`, а `g4cf.exe` зовём напрямую:
    `cmd /c` спотыкается о пробелы в пути репозитория."""
    env = dict(os.environ)
    for line in io.open(G4RUN, encoding="latin-1"):
        m = re.match(r'^\s*set\s+"([A-Za-z0-9_()]+)=(.*)"\s*$', line)
        if not m:
            continue
        name, value = m.group(1), m.group(2)
        value = re.sub(r"%([A-Za-z0-9_()]+)%",
                       lambda mm: env.get(mm.group(1), ""), value)
        env[name] = value
    return env

DEFAULT_GEOMS = ["Nano16Pro_tube", "Nano16Pro", "Nano16Pro_Marinelli",
                 "RadiaCode_AuthorMarinelli0.5"]
DEFAULT_ENERGIES = [60.0, 200.0, 662.0, 1461.0, 2614.0]


def strip_box(src, dst):
    u"""Копия геометрии без наших ключей коробки — общая сцена для троих."""
    text = io.open(src, encoding="latin-1").read()
    kept = [l for l in text.splitlines(True)
            if not l.strip().startswith("DS_CrystalBox")]
    io.open(dst, "w", encoding="latin-1", newline="").write("".join(kept))


def num(s):
    u"""Число из вывода effsim: десятичная запятая зависит от локали."""
    return float(s.replace(",", "."))


OUR_FLAGS = []


def run_ours(geometry, energies, n, out_csv):
    args = [EFFSIM, "--geometry=" + geometry, "--n=%d" % n, "--total",
            "--energies=" + ",".join("%g" % e for e in energies),
            "--out=" + out_csv] + OUR_FLAGS
    started = time.time()
    p = subprocess.run(args, cwd=EFFSIM_DIR, capture_output=True,
                       encoding="cp866", errors="replace")
    if p.returncode != 0:
        sys.stderr.write(p.stdout + p.stderr)
        raise SystemExit(u"effsim вернул %d" % p.returncode)
    peak = {}
    for line in io.open(out_csv, encoding="utf-8"):
        parts = line.split(",")
        if len(parts) < 3:
            continue
        try:
            peak[round(float(parts[0]), 1)] = (float(parts[1]), float(parts[2]))
        except ValueError:
            continue
    total = {}
    for line in p.stdout.splitlines():
        m = re.match(r"^\s*(\d+)\s+\S+\s+([\d.,]+E[+-]\d+)\s+\S?([\d.,]+)\s*%",
                     line)
        if m:
            total[round(float(m.group(1)), 1)] = (num(m.group(2)),
                                                  num(m.group(3)))
    return peak, total, time.time() - started


def run_g4(scene, energies, n):
    out = {}
    env = g4_env()
    started = time.time()
    for e in energies:
        p = subprocess.run([G4EXE, "scene", scene, "mono", "%g" % e, str(n)],
                           capture_output=True, encoding="cp866",
                           errors="replace", env=env)
        peak = totalv = None
        for line in p.stdout.splitlines():
            m = re.match(r"RESULT any=(\d+) eps_total=([\deE.+-]+)", line)
            if m:
                totalv = (float(m.group(2)), 100.0 / max(1.0, float(m.group(1))) ** 0.5)
            m = re.match(r"RESULT window=[\d.]+ counts=(\d+) eps=([\deE.+-]+)", line)
            if m:
                peak = (float(m.group(2)), 100.0 / max(1.0, float(m.group(1))) ** 0.5)
        if peak is None:
            sys.stderr.write(p.stdout[-2000:] + p.stderr[-2000:])
            raise SystemExit(u"g4cf не дал результата на %g кэВ" % e)
        out[round(e, 1)] = (peak, totalv)
        sys.stderr.write(u"  g4 %.0f кэВ: пик %.4e, полная %.4e\n"
                         % (e, peak[0], totalv[0]))
        sys.stderr.flush()
    return out, time.time() - started


def run_lsrm(workdir, geometry, energies, decays, seed):
    rows, elapsed = run_tccf2.run(workdir, geometry, decays, energies,
                                  seed=seed, threads=1, full_eff=True)
    out = {}
    for r in rows:
        out[round(r["energy_kev"], 1)] = ((r["eff"], r["eff_err_pct"]),
                                          (r["full_eff"], None))
    return out, elapsed


def read_report(path):
    u"""Прочитать таблицы прежнего отчёта: {геометрия: {E: (лсрм_пик,
    лсрм_полн, g4_пик, g4_полн)}}. Нужно, чтобы пересчитать ТОЛЬКО нашу
    сторону, не гоняя Geant4 заново — он в этой тройке самый дорогой."""
    out = {}
    geom = None
    for line in io.open(path, encoding="utf-8"):
        if line.startswith("## "):
            geom = line[3:].strip()
            out[geom] = {}
            continue
        if not line.startswith("| ") or geom is None:
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 11 or not re.match(r"^\d+$", cells[0]):
            continue

        def val(s):
            try:
                return float(s)
            except ValueError:
                return None

        out[geom][round(float(cells[0]), 1)] = (val(cells[2]), val(cells[7]),
                                                val(cells[3]), val(cells[8]))
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--workdir", required=True)
    p.add_argument("--reuse", default="",
                   help=u"прежний report.md: взять оттуда ЛСРМ и Geant4, "
                        u"пересчитать только наше")
    p.add_argument("--geoms", default=",".join(DEFAULT_GEOMS))
    p.add_argument("--energies",
                   default=",".join("%g" % e for e in DEFAULT_ENERGIES))
    p.add_argument("--lsrm", type=int, default=10000000)
    p.add_argument("--our", type=int, default=2000000)
    p.add_argument("--g4", type=int, default=5000000)
    p.add_argument("--seed", type=int, default=run_tccf2.DEFAULT_SEED)
    p.add_argument("--out", default=os.path.join(HERE, "out", "3way"))
    p.add_argument("--skip", default="")
    p.add_argument("--our-flags", default="",
                   help=u"лишние ключи effsim через запятую, например --no-bound")
    p.add_argument("--effsim-dir", default="",
                   help=u"каталог с effsim.exe (по умолчанию bin/Debug_Codex)")
    args = p.parse_args()

    energies = [float(x) for x in args.energies.split(",")]
    geoms = args.geoms.split(",")
    skip = set(x for x in args.skip.split(",") if x)
    prev = read_report(args.reuse) if args.reuse else {}
    if prev:
        skip |= {"lsrm", "g4"}
    if args.our_flags:
        OUR_FLAGS[:] = [x for x in args.our_flags.split(",") if x]
    if args.effsim_dir:
        set_effsim(args.effsim_dir)
    if not os.path.isdir(args.out):
        os.makedirs(args.out)

    print(u"# Наше — ЛСРМ 2.10 — Geant4, %s"
          % time.strftime("%d.%m.%Y"))
    print(u"")
    print(u"Истории: наши %d, ЛСРМ %d распадов на геометрию (одно зерно %d, "
          u"один поток), Geant4 %d на точку. Геометрия — общая, без ключей "
          u"коробки (ЛСРМ бруса не умеет)."
          % (args.our, args.lsrm, args.seed, args.g4))

    for g in geoms:
        src = os.path.join(MODELS, g + ".in")
        cyl = os.path.join(args.out, g + "_cyl.in")
        strip_box(src, cyl)
        scene = os.path.join(args.out, g + ".scene")
        sp = subprocess.run([EFFSIM, "--geometry=" + cyl, "--dump-scene"],
                            cwd=EFFSIM_DIR, capture_output=True,
                            encoding="cp866", errors="replace")
        io.open(scene, "w", encoding="utf-8", newline="\n").write(sp.stdout)

        ours = g4 = lsrm = None
        if "our" not in skip:
            ours = run_ours(cyl, energies, args.our,
                            os.path.join(args.out, g + "_our.csv"))
            sys.stderr.write(u"%s: наши %.0f с\n" % (g, ours[2]))
        if "lsrm" not in skip:
            lsrm = run_lsrm(args.workdir, cyl, energies, args.lsrm, args.seed)
            sys.stderr.write(u"%s: ЛСРМ %.0f с\n" % (g, lsrm[1]))
        if "g4" not in skip:
            g4 = run_g4(scene, energies, args.g4)
            sys.stderr.write(u"%s: Geant4 %.0f с\n" % (g, g4[1]))

        print(u"")
        print(u"## %s" % g)
        print(u"")
        print(u"| E, кэВ | наш пик | ЛСРМ пик | G4 пик | наш/G4 | ЛСРМ/G4 | "
              u"наш полн. | ЛСРМ полн. | G4 полн. | наш/G4 | ЛСРМ/G4 |")
        print(u"|---|---|---|---|---|---|---|---|---|---|---|")
        for e in energies:
            k = round(e, 1)
            op = ours[0].get(k, (None, None))[0] if ours else None
            ot = ours[1].get(k, (None, None))[0] if ours else None
            old = prev.get(g, {}).get(k, (None, None, None, None))
            lp = lsrm[0][k][0][0] if lsrm else old[0]
            lt = lsrm[0][k][1][0] if lsrm else old[1]
            gp = g4[0][k][0][0] if g4 else old[2]
            gt = g4[0][k][1][0] if g4 else old[3]

            def cell(v):
                return u"%.4e" % v if v else u"—"

            def rat(a, b):
                return u"%.3f" % (a / b) if a and b else u"—"

            print(u"| %.0f | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |"
                  % (e, cell(op), cell(lp), cell(gp), rat(op, gp), rat(lp, gp),
                     cell(ot), cell(lt), cell(gt), rat(ot, gt), rat(lt, gt)))
        sys.stdout.flush()


if __name__ == "__main__":
    main()
