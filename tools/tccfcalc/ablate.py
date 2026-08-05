# -*- coding: utf-8 -*-
"""
Лестница абляций: снимать с геометрии по одному слою и смотреть, на каком шаге
расходятся наш расчёт и TCCFCALC.

Смысл в том, что абсолютная эффективность зависит сразу от всего — телесного
угла, окна, самопоглощения в пробе и физики в кристалле, — и по одному числу не
понять, где именно разошлось. Поэтому геометрия раздевается по слоям:

    base    как есть
    nosrc   проба почти без вещества (плотность 1e-6) — снимает самопоглощение
            и рассеяние в самой пробе
    nowin   плюс убран передний отражатель и передняя оболочка — снимает окно
    bare    плюс убраны боковые отражатель и оболочка и оправа — голый кристалл

На последней ступени остаются только телесный угол и физика в кристалле. Если
на ней расчёты сошлись, а на предыдущей разошлись, виноват снятый слой.

    python ablate.py --workdir=... --geometry=X.in --out=каталог [--decays=N]
"""
import argparse
import io
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run_tccf

VARIANTS = {
    "base": {},
    "nosrc": {"SC_RoSource": 1e-6, "SM_RoSource": 1e-6},
    "nowin": {"SC_RoSource": 1e-6, "SM_RoSource": 1e-6,
              "DS_CrystalFrontReflectorThickness": 0.0,
              "DS_CrystalFrontCladdingThickness": 0.0},
    "bare": {"SC_RoSource": 1e-6, "SM_RoSource": 1e-6,
             "DS_CrystalFrontReflectorThickness": 0.0,
             "DS_CrystalFrontCladdingThickness": 0.0,
             "DS_CrystalSideReflectorThickness": 0.0,
             "DS_CrystalSideCladdingThickness": 0.0,
             "DS_DetectorMountingThickness": 0.0},
}


def patch(src_path, dst_path, overrides, drop_box=True):
    """Переписать .in с подменёнными значениями ключей.

    Ключи коробки (`DS_CrystalBox*`) выбрасываются: это НАШЕ расширение
    формата, ЛСРМ его не видит и считает цилиндр. Пока они в файле, две
    программы считают разные тела, и сравнивать нечего.
    """
    out = []
    with io.open(src_path, encoding="latin-1") as f:
        for line in f:
            stripped = line.strip()
            if drop_box and stripped.startswith("DS_CrystalBox"):
                continue
            m = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(\S+)(.*)$", line)
            if m and m.group(1) in overrides:
                tail = m.group(3).rstrip("\r\n")
                out.append("%s = %g%s\n" % (m.group(1), overrides[m.group(1)], tail))
            else:
                out.append(line)
    with io.open(dst_path, "w", encoding="latin-1", newline="") as f:
        f.writelines(out)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--workdir", required=True)
    p.add_argument("--geometry", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--decays", type=int, default=20000000)
    p.add_argument("--energies", default="28,40,60,100,200,662,1400,2600")
    p.add_argument("--only", default="")
    args = p.parse_args()

    energies = [float(x) for x in args.energies.split(",")]
    if not os.path.isdir(args.out):
        os.makedirs(args.out)

    names = args.only.split(",") if args.only else list(VARIANTS)
    for name in names:
        geom = os.path.join(args.out, "%s.in" % name)
        patch(args.geometry, geom, VARIANTS[name])
        rows, elapsed = run_tccf.run(args.workdir, geom, args.decays, energies, name)
        with io.open(os.path.join(args.out, "tccf_%s.csv" % name), "w",
                     encoding="utf-8") as f:
            f.write("E_keV,eff,eff_err_pct\n")
            for r in rows:
                f.write("%.1f,%.5E,%.2f\n"
                        % (r["energy_kev"], r["eff"], r["eff_err_pct"]))
        print("%-6s %5.0f с  %s" % (name, elapsed,
                                    " ".join("%.3E" % r["eff"] for r in rows)))
        sys.stdout.flush()


if __name__ == "__main__":
    main()
