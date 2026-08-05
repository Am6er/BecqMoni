# -*- coding: utf-8 -*-
"""
Прогнать TCCFCALC.dll ЛСРМ по нашей геометрии и снять кривую эффективности.

Как это устроено у них самих. `TCCFCALC_Prepare` умеет выбрать только нуклид
(A, Z, изомер), поэтому произвольную сетку энергий EffCalcMC получает
подлогом: кладёт в библиотеку ENSDF ПОДДЕЛЬНЫЙ набор данных (`Data\\Scale.enx`,
A = 290, Z = 27, «Fake ENSDF2 B- DECAY») — столько уровней, сколько нужно
точек, каждый заселяется своей бета-ветвью и разряжается одной гаммой прямо в
основное состояние. Каскадов при такой схеме нет, и `Eff` в отчёте — чистая
эффективность в пике полного поглощения.

Здесь то же самое, только сетку задаём мы.

Работа идёт ТОЛЬКО в копии каталога: установку ЛСРМ трогать нельзя.

    python run_tccf.py --workdir=... --geometry=X.in --decays=20000000
                       [--energies=50,100,...] [--tag=имя]
"""
import argparse
import io
import os
import re
import shutil
import subprocess
import sys
import time

FAKE_A, FAKE_Z = 290, 27

DEFAULT_ENERGIES = [40, 50, 60, 80, 100, 120, 150, 200, 250, 300, 400, 500,
                    600, 662, 800, 1000, 1250, 1461, 1800, 2200, 2614, 3000]


def write_scale(path, energies):
    """Собрать поддельный набор ENSDF по образцу `Data\\Scale.enx`.

    Колонки жёсткие: 1-5 номер уровня, 8 тип записи, 10-19 энергия, 22-29
    интенсивность, 56-62 коэффициент конверсии. Он выставлен в ноль нарочно:
    иначе часть переходов уйдёт в конверсионные электроны и выход перестанет
    быть тем, что мы задали.
    """
    n = len(energies)
    share = 100.0 / n
    lines = []
    lines.append("%03dXX   *Fake ENSDF2 B- DECAY" % FAKE_A)
    lines.append("%03d %2d I    %2d        %2d         0        %2d         1"
                 % (FAKE_A, FAKE_Z, n + 1, n, n))
    lines.append("       N 1.0         1.0       1.0       1.0")
    lines.append("%03dCO  P 0.0" % FAKE_A)
    lines.append("    1  L 0.0")
    for i, energy in enumerate(energies, start=2):
        # 1-5 номер, 8 тип, 10-19 энергия, 20-21 её погрешность (пусто),
        # 22-29 интенсивность, 56-62 коэффициент конверсии
        lines.append("%5d  L %-10.4f" % (i, energy))
        lines.append("       B %-10.4f  %-8.4f" % (0.0, share))
        lines.append("    1  G %-10.4f  %-8.4f%26s%-7s"
                     % (energy, share, "", "0.0"))
    with io.open(path, "w", encoding="latin-1", newline="\r\n") as f:
        f.write("\n".join(lines) + "\n")


RESULT = re.compile(r"^\s*(\d+)\s+([\d.]+)\s+([\d.eE+-]+)\s+([\d.]+)\s+"
                    r"([\d.]+)\s+([\d.]+)\s+([\d.eE+-]+)\s+([\d.]+)\s*$")


def parse_out(path):
    """Снять таблицу результатов: энергия (кэВ), CF, dCF, Eff, dEff."""
    rows = []
    with io.open(path, encoding="latin-1") as f:
        for line in f:
            m = RESULT.match(line)
            if m:
                rows.append({
                    "energy_kev": float(m.group(2)) * 1000.0,
                    "intensity": float(m.group(3)),
                    "cf": float(m.group(5)),
                    "cf_err_pct": float(m.group(6)),
                    "eff": float(m.group(7)),
                    "eff_err_pct": float(m.group(8)),
                })
    return rows


def run(workdir, geometry, decays, energies, tag):
    ensdf = os.path.join(workdir, "LIB", "ENSDF2", "%03d.ENX" % FAKE_A)
    write_scale(ensdf, energies)
    shutil.copyfile(geometry, os.path.join(workdir, "TCCFCALC.in"))

    started = time.time()
    proc = subprocess.run(
        [os.path.join(workdir, "TccfProbe.exe"), workdir,
         str(FAKE_A), str(FAKE_Z), "0", str(decays)],
        cwd=workdir, capture_output=True, text=True, encoding="latin-1")
    elapsed = time.time() - started
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        raise SystemExit("TccfProbe вернул %d" % proc.returncode)

    out = os.path.join(workdir, "tccfcalc.out")
    rows = parse_out(out)
    if len(rows) != len(energies):
        raise SystemExit("строк в отчёте %d, а энергий %d — отчёт не тот"
                         % (len(rows), len(energies)))
    if tag:
        shutil.copyfile(out, os.path.join(workdir, "out_%s.txt" % tag))
    return rows, elapsed


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--workdir", required=True)
    p.add_argument("--geometry", required=True)
    p.add_argument("--decays", type=int, default=2000000)
    p.add_argument("--energies", default="")
    p.add_argument("--tag", default="")
    p.add_argument("--csv", default="")
    args = p.parse_args()

    energies = ([float(x) for x in args.energies.split(",")]
                if args.energies else DEFAULT_ENERGIES)
    rows, elapsed = run(args.workdir, args.geometry, args.decays, energies, args.tag)

    print("# %s, распадов %d, %.1f с" % (os.path.basename(args.geometry),
                                         args.decays, elapsed))
    print("E_keV,eff,eff_err_pct,cf,cf_err_pct")
    for r in rows:
        print("%.1f,%.5E,%.2f,%.5f,%.2f"
              % (r["energy_kev"], r["eff"], r["eff_err_pct"],
                 r["cf"], r["cf_err_pct"]))
    if args.csv:
        with io.open(args.csv, "w", encoding="utf-8") as f:
            f.write("E_keV,eff,eff_err_pct,cf,cf_err_pct\n")
            for r in rows:
                f.write("%.1f,%.5E,%.2f,%.5f,%.2f\n"
                        % (r["energy_kev"], r["eff"], r["eff_err_pct"],
                           r["cf"], r["cf_err_pct"]))


if __name__ == "__main__":
    main()
