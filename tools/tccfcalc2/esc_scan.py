# -*- coding: utf-8 -*-
u"""
Скан параметров вылета электрона против Geant4 (задачи M8 и F18).

`ElectronEscapeSlope` = 0.4 калиброван по развёртке Geant4 ПРИ СТАРОМ спектре
тормозного (dN/dk = C/k, F12/§10). Физика 8 спектр сменила, и наклон надо
переснять — либо подтвердить. Geant4 берётся из готового отчёта тройной
сверки: от нашей физики он не зависит.

Второй параметр — порог `ElectronEscapeT0Kev`. В коде зашито 300, в доке,
сообщении коммита и §10 журнала стоит 350; артефактов, различающих их, не
сохранилось, и решение принимается повтором одной точки развёртки с обоими
значениями (F18). Ключ `--esc-t0` доезжает до симулятора с закрытия T8,
пересборка не нужна.

    # наклон при пороге по умолчанию (M8)
    python esc_scan.py --effsim-dir=... [--slopes=0.4,0.5,0.6,0.7]
    # порог при наклоне по умолчанию (F18)
    python esc_scan.py --effsim-dir=... --t0s=300,350

Сканируется РОВНО ОДИН параметр за прогон: два подвижных сразу дали бы
таблицу, из которой нельзя назвать виновника сдвига.
"""
import argparse, io, os, re, subprocess, sys, time
HERE = os.path.dirname(os.path.abspath(__file__))

def read_g4(path, geom):
    out, cur = {}, None
    for line in io.open(path, encoding="utf-8"):
        if line.startswith("## "):
            cur = line[3:].strip(); continue
        if cur != geom or not line.startswith("| "): continue
        c = [x.strip() for x in line.strip().strip("|").split("|")]
        if len(c) < 11 or not c[0].isdigit(): continue
        out[round(float(c[0]), 1)] = float(c[3])
    return out

def run(effsim_dir, geometry, energies, n, key, value, csv):
    args = [os.path.join(effsim_dir, "effsim.exe"), "--geometry=" + geometry,
            "--n=%d" % n, "--energies=" + ",".join("%g" % e for e in energies),
            "--out=" + csv, "--%s=%g" % (key, value)]
    p = subprocess.run(args, cwd=effsim_dir, capture_output=True,
                       encoding="cp866", errors="replace")
    if p.returncode != 0:
        sys.stderr.write(p.stdout[-2000:] + p.stderr[-2000:])
        raise SystemExit("effsim %d" % p.returncode)
    out = {}
    for line in io.open(csv, encoding="utf-8"):
        q = line.split(",")
        try: out[round(float(q[0]), 1)] = (float(q[1]), float(q[2]))
        except ValueError: pass
    return out

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--effsim-dir", required=True)
    p.add_argument("--geom", default="Nano16Pro_tube")
    p.add_argument("--slopes", default="0.4,0.5,0.6,0.7")
    p.add_argument("--t0s", default=None,
                   help=u"пороги в кэВ; задан — сканируется порог, а не наклон (F18)")
    p.add_argument("--energies", default="662,1461,2614")
    p.add_argument("--n", type=int, default=1000000)
    p.add_argument("--report", default=os.path.join(HERE, "out", "3way", "report.md"))
    p.add_argument("--out", default=os.path.join(HERE, "out", "esc"))
    a = p.parse_args()
    energies = [float(x) for x in a.energies.split(",")]
    if a.t0s:
        key, label, task = "esc-t0", u"порог, кэВ", u"F18"
        values = [float(x) for x in a.t0s.split(",")]
        note = (u"Умолчание порога — 300 в коде против 350 в доке и журнале §10; "
                u"наклон оставлен умолчанием 0.4.")
    else:
        key, label, task = "esc-slope", u"наклон", u"M8"
        values = [float(x) for x in a.slopes.split(",")]
        note = u"Умолчание наклона — 0.4, калибровано при СТАРОМ спектре тормозного."
    g4 = read_g4(a.report, a.geom)
    if not os.path.isdir(a.out): os.makedirs(a.out)
    cyl = os.path.join(HERE, "out", "3way", a.geom + "_cyl.in")

    print(u"# Параметры вылета электрона против Geant4 (%s), %s"
          % (task, time.strftime("%d.%m.%Y")))
    print(u"")
    print(u"Геометрия %s, наших историй %d на точку, физика 8." % (a.geom, a.n))
    print(note)
    print(u"")
    print(u"| %s | " % label + u" | ".join(u"%.0f кэВ" % e for e in energies) + u" |")
    print(u"|---" * (len(energies) + 1) + u"|")
    for s in values:
        started = time.time()
        r = run(a.effsim_dir, cyl, energies, a.n, key, s,
                os.path.join(a.out, "%s_%s_%g.csv" % (a.geom, key, s)))
        cells = []
        for e in energies:
            k = round(e, 1)
            v = r.get(k)
            cells.append(u"%.3f ± %.3f" % (v[0] / g4[k], v[0] / g4[k] * v[1] / 100.0)
                         if v and g4.get(k) else u"—")
        print(u"| %g | %s |" % (s, u" | ".join(cells)))
        sys.stdout.flush()
        sys.stderr.write(u"%s = %g: %.0f с\n" % (key, s, time.time() - started))
        sys.stderr.flush()

if __name__ == "__main__":
    main()
