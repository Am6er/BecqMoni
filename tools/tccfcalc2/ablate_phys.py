# -*- coding: utf-8 -*-
u"""
Лестница абляций физики 7 и 8 против Geant4 (задача F26).

ЗАЧЕМ. Тройная сверка §14 сняла «физику 7» сборкой, которая захватила ДВА
коммита сразу — связанное рассеяние (7) и спектр тормозного по
Зельцеру — Бергеру (8). Поэтому вывод «связанный комптон приподнял пик»
атрибутирован неверно: поднять могло и то, и другое, и оба вместе. Здесь
они разводятся ключами `--no-bound` и `--no-brem-sb`.

Geant4 и ЛСРМ не пересчитываются: от нашей физики они не зависят, и их
числа читаются из готового отчёта (`out/3way/report.md`). Гоняется ТОЛЬКО
наша сторона, четырьмя наборами ключей:

    физика 6     --no-bound --no-brem-sb   (обязана воспроизвести report.md)
    + связанное  --no-brem-sb
    + тормозное  --no-bound
    физика 8     без ключей

Первая строка — сама себе поверка: если она не легла на report.md, значит
разошлось что-то ещё, и сравнивать остальные три бессмысленно.

    python ablate_phys.py --effsim-dir=<каталог с effsim.exe>
                          [--report=out/3way/report.md] [--n=1000000]
                          [--geoms=...] [--energies=...] [--out=out/ablate]
"""
import argparse
import io
import os
import re
import subprocess
import sys
import time

# Отчёт — в UTF-8: без этого stdout уходит в кодировку консоли (cp1251), и
# готовый файл потом читается как каша. Наступали 08.08.2026.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))

VARIANTS = [
    (u"физика 6", ["--no-bound", "--no-brem-sb"]),
    (u"+ связанное", ["--no-brem-sb"]),
    (u"+ тормозное", ["--no-bound"]),
    (u"физика 8", []),
]


def read_report(path):
    u"""{геометрия: {E: (наш6_пик, g4_пик, наш6_полн, g4_полн)}} из отчёта §14."""
    out, geom = {}, None
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

        out[geom][round(float(cells[0]), 1)] = (
            val(cells[1]), val(cells[3]), val(cells[6]), val(cells[8]))
    return out


def num(s):
    return float(s.replace(",", "."))


def run_ours(effsim_dir, geometry, energies, n, extra, out_csv):
    u"""Пик и полная эффективность нашего расчёта; возвращает {E: (пик, полн)}."""
    args = [os.path.join(effsim_dir, "effsim.exe"),
            "--geometry=" + geometry, "--n=%d" % n, "--total",
            "--energies=" + ",".join("%g" % e for e in energies),
            "--out=" + out_csv] + extra
    p = subprocess.run(args, cwd=effsim_dir, capture_output=True,
                       encoding="cp866", errors="replace")
    if p.returncode != 0:
        sys.stderr.write(p.stdout[-3000:] + p.stderr[-3000:])
        raise SystemExit(u"effsim вернул %d" % p.returncode)

    peak = {}
    for line in io.open(out_csv, encoding="utf-8"):
        parts = line.split(",")
        if len(parts) < 3:
            continue
        try:
            peak[round(float(parts[0]), 1)] = float(parts[1])
        except ValueError:
            continue

    total = {}
    for line in p.stdout.splitlines():
        m = re.match(r"^\s*(\d+)\s+\S+\s+([\d.,]+E[+-]\d+)\s+\S?([\d.,]+)\s*%", line)
        if m:
            total[round(float(m.group(1)), 1)] = num(m.group(2))
    return peak, total


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--effsim-dir", required=True)
    p.add_argument("--report", default=os.path.join(HERE, "out", "3way", "report.md"))
    p.add_argument("--n", type=int, default=1000000)
    p.add_argument("--geoms", default="Nano16Pro_tube,Nano16Pro,"
                                      "Nano16Pro_Marinelli,RadiaCode_AuthorMarinelli0.5")
    p.add_argument("--energies", default="60,200,662,1461,2614")
    p.add_argument("--out", default=os.path.join(HERE, "out", "ablate"))
    args = p.parse_args()

    energies = [float(x) for x in args.energies.split(",")]
    geoms = args.geoms.split(",")
    prev = read_report(args.report)
    if not os.path.isdir(args.out):
        os.makedirs(args.out)

    print(u"# Лестница абляций физики 7 и 8 против Geant4 (F26), %s"
          % time.strftime("%d.%m.%Y"))
    print(u"")
    print(u"Наших историй %d на точку. Geant4 и «физика 6» — из `%s`; "
          u"строка «физика 6» пересчитана заново и обязана лечь на неё."
          % (args.n, os.path.basename(args.report)))

    for g in geoms:
        # Сцена та же, что у тройной сверки: копия без ключей коробки.
        cyl = os.path.join(HERE, "out", "3way", g + "_cyl.in")
        if not os.path.exists(cyl):
            sys.stderr.write(u"нет %s — сначала three_way.py\n" % cyl)
            continue

        rows = {}
        for name, extra in VARIANTS:
            started = time.time()
            csv = os.path.join(args.out, "%s_%s.csv"
                               % (g, name.replace(" ", "_").replace("+", "p")))
            rows[name] = run_ours(args.effsim_dir, cyl, energies, args.n, extra, csv)
            sys.stderr.write(u"%s / %s: %.0f с\n" % (g, name, time.time() - started))
            sys.stderr.flush()

        print(u"")
        print(u"## %s" % g)
        print(u"")
        print(u"### Пик полного поглощения, отношение к Geant4")
        print(u"")
        print(u"| E, кэВ | G4 | физика 6 | было (§14) | + связанное | + тормозное | физика 8 |")
        print(u"|---|---|---|---|---|---|---|")
        for e in energies:
            k = round(e, 1)
            old6, g4p, old6t, g4t = prev.get(g, {}).get(k, (None, None, None, None))

            def rat(v):
                return u"%.3f" % (v / g4p) if v and g4p else u"—"

            print(u"| %.0f | %s | %s | %s | %s | %s | %s |"
                  % (e,
                     (u"%.4e" % g4p) if g4p else u"—",
                     rat(rows[u"физика 6"][0].get(k)),
                     rat(old6),
                     rat(rows[u"+ связанное"][0].get(k)),
                     rat(rows[u"+ тормозное"][0].get(k)),
                     rat(rows[u"физика 8"][0].get(k))))

        print(u"")
        print(u"### Полная эффективность, отношение к Geant4")
        print(u"")
        print(u"| E, кэВ | G4 | физика 6 | было (§14) | + связанное | + тормозное | физика 8 |")
        print(u"|---|---|---|---|---|---|---|")
        for e in energies:
            k = round(e, 1)
            old6, g4p, old6t, g4t = prev.get(g, {}).get(k, (None, None, None, None))

            def rat(v):
                return u"%.3f" % (v / g4t) if v and g4t else u"—"

            print(u"| %.0f | %s | %s | %s | %s | %s | %s |"
                  % (e,
                     (u"%.4e" % g4t) if g4t else u"—",
                     rat(rows[u"физика 6"][1].get(k)),
                     rat(old6t),
                     rat(rows[u"+ связанное"][1].get(k)),
                     rat(rows[u"+ тормозное"][1].get(k)),
                     rat(rows[u"физика 8"][1].get(k))))
        sys.stdout.flush()


if __name__ == "__main__":
    main()
