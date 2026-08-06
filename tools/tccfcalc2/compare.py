# -*- coding: utf-8 -*-
u"""
Сверка старой и новой TCCFCALC на ОДНОЙ геометрии и одной сетке энергий.

Считает обе программы по очереди и печатает таблицу отношений
новая / старая. Новую можно гонять в нескольких постановках (см.
`run_tccf2.VARIANTS`), чтобы видеть, какая из новых частей физики двигает
кривую:

    full     как DLL считает по умолчанию (электроны с тормозным, EPDL97, GLECS)
    nottb    без переноса электронов и тормозного
    noepdl   без EPDL97 и GLECS (ослабление как у старой — ATTENUAT.BIN)
    oldlike  без того и другого — ближайшее к физике старой DLL

    python compare.py --old-workdir=... --new-workdir=... --geometry=X.in
                      [--decays=20000000] [--energies=...] [--variants=full,oldlike]
                      [--out=каталог]

Работа идёт ТОЛЬКО в копиях каталогов ЛСРМ.
"""
import argparse
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, os.pardir, "tccfcalc"))
import run_tccf                                        # noqa: E402
import run_tccf2                                       # noqa: E402
import ablate                                          # noqa: E402


def write_csv(path, rows):
    with io.open(path, "w", encoding="utf-8") as f:
        f.write(u"E_keV,eff,eff_err_pct\n")
        for r in rows:
            f.write(u"%.1f,%.5E,%.2f\n"
                    % (r["energy_kev"], r["eff"], r["eff_err_pct"]))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--old-workdir", required=True)
    p.add_argument("--new-workdir", required=True)
    p.add_argument("--geometry", required=True)
    p.add_argument("--decays", type=int, default=20000000)
    p.add_argument("--energies", default="28,40,60,100,200,662,1400,2600")
    p.add_argument("--variants", default="full,oldlike")
    p.add_argument("--threads", type=int, default=1)
    p.add_argument("--out", default="")
    args = p.parse_args()

    energies = [float(x) for x in args.energies.split(",")]
    variants = args.variants.split(",")
    name = os.path.splitext(os.path.basename(args.geometry))[0]
    outdir = args.out or os.path.join(HERE, "out")
    if not os.path.isdir(outdir):
        os.makedirs(outdir)

    # Старой DLL нужен файл без ключей коробки; `ablate.patch` их и снимает.
    geom_old = os.path.join(outdir, "%s_old.in" % name)
    ablate.patch(args.geometry, geom_old, {})

    results = {}
    old_rows, old_time = run_tccf.run(args.old_workdir, geom_old, args.decays,
                                      energies, "cmp_%s" % name)
    results["старая"] = old_rows
    write_csv(os.path.join(outdir, "%s_old.csv" % name), old_rows)
    sys.stderr.write(u"старая: %.0f с\n" % old_time)
    sys.stderr.flush()

    for v in variants:
        rows, elapsed = run_tccf2.run(args.new_workdir, args.geometry,
                                      args.decays, energies, "cmp_%s_%s" % (name, v),
                                      variant=v, threads=args.threads)
        results[u"новая/%s" % v] = rows
        write_csv(os.path.join(outdir, "%s_new_%s.csv" % (name, v)), rows)
        sys.stderr.write(u"новая/%s: %.0f с\n" % (v, elapsed))
        sys.stderr.flush()

    keys = [u"старая"] + [u"новая/%s" % v for v in variants]
    print(u"# %s, распадов %d, потоков %d" % (name, args.decays, args.threads))
    print(u"")
    print(u"Эффективность в пике полного поглощения (погрешность, %):")
    head = u"| E, кэВ |" + u"".join(u" %s |" % k for k in keys)
    print(head)
    print(u"|---|" + u"---|" * len(keys))
    for i, e in enumerate(energies):
        cells = []
        for k in keys:
            r = results[k][i]
            cells.append(u" %.3E (%.1f) |" % (r["eff"], r["eff_err_pct"]))
        print(u"| %.0f |" % e + u"".join(cells))

    print(u"")
    print(u"Отношение новая / старая:")
    vkeys = keys[1:]
    print(u"| E, кэВ |" + u"".join(u" %s |" % k for k in vkeys))
    print(u"|---|" + u"---|" * len(vkeys))
    for i, e in enumerate(energies):
        base = results[u"старая"][i]["eff"]
        cells = []
        for k in vkeys:
            r = results[k][i]
            if base > 0:
                # погрешность отношения — квадратичная сумма относительных
                d = (r["eff_err_pct"] ** 2 + results[u"старая"][i]["eff_err_pct"] ** 2) ** 0.5
                cells.append(u" %.3f ± %.3f |" % (r["eff"] / base,
                                                  r["eff"] / base * d / 100.0))
            else:
                cells.append(u" — |")
        print(u"| %.0f |" % e + u"".join(cells))


if __name__ == "__main__":
    main()
