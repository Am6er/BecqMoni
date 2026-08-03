# -*- coding: utf-8 -*-
"""Сверка кривых, восстановленных с нуля, с кривой из файла геометрии."""
import csv, io, os, math, re
import numpy as np
OUT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\out'
ROI = os.path.join(os.environ['APPDATA'], 'BecqMoni', 'config', 'ROI', 'Nano - cilinder (close distance).xml')

def load_roi(p):
    t = open(p, encoding='utf-8', errors='replace').read()
    return sorted((float(a), float(b)) for a, b in re.findall(
        r'<Energy>([-0-9.eE+]+)</Energy>\s*<Efficiency>([-0-9.eE+]+)</Efficiency>', t) if float(b) > 0)

def load_csv(p):
    rows = list(csv.reader(io.open(p, encoding='utf-8')))
    out = []
    for r in rows[1:]:
        if not r or r[0] == 'spectrum':
            break
        out.append((float(r[0]), float(r[1])))
    return sorted(out)

def interp(c, e):
    if e <= c[0][0]: return c[0][1]
    if e >= c[-1][0]: return c[-1][1]
    for i in range(1, len(c)):
        if c[i][0] >= e:
            x0, y0 = c[i-1]; x1, y1 = c[i]
            f = (math.log(e)-math.log(x0))/(math.log(x1)-math.log(x0))
            return math.exp(math.log(y0)+f*(math.log(y1)-math.log(y0)))
    return c[-1][1]

curves = [
    ('файл геометрии', load_roi(ROI)),
    ('с нуля: корпус, 10 сп.', load_csv(os.path.join(OUT, 'ASN16_noref_curve.csv'))),
    ('с нуля: всё, 81 сп.', load_csv(os.path.join(OUT, 'ASN16_all_noref_curve.csv'))),
    ('с нуля: папка, 71 сп.', load_csv(os.path.join(OUT, 'ASN16_user_noref_curve.csv'))),
]
PROBE = [60, 100, 150, 186, 239, 352, 583, 662, 911, 1120, 1461, 1765, 2204, 2615]
print('Все кривые приведены к 1 на 662 кэВ (абсолютный уровень при счёте с нуля не определён)')
print()
print('%8s' % 'E, кэВ' + ''.join('%24s' % n for n, _ in curves))
norm = [interp(c, 662.0) for _, c in curves]
for e in PROBE:
    line = '%8d' % e
    base = interp(curves[0][1], e) / norm[0]
    for i, (n, c) in enumerate(curves):
        v = interp(c, e) / norm[i]
        line += '%16.3f%8s' % (v, '' if i == 0 else '(x%.2f)' % (v / base))
    print(line)

try:
    import matplotlib; matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(figsize=(10, 6.5))
    styles = [('--', '#909090', 2.0), ('-', '#1f6fb2', 2.2), ('-', '#d9534f', 2.0), ('-', '#f0ad4e', 1.6)]
    for (n, c), (ls, col, lw), nm in zip(curves, styles, norm):
        xs = [e for e, _ in c]; ys = [v/nm for _, v in c]
        ax.plot(xs, ys, ls, color=col, lw=lw, label=n)
    ax.set_xscale('log'); ax.set_yscale('log')
    ax.set_xlim(40, 3000)
    ax.set_xlabel('E, кэВ'); ax.set_ylabel('эффективность, приведена к 1 на 662 кэВ')
    ax.set_title('ASN16: кривая с нуля против кривой из файла геометрии')
    ax.grid(True, which='both', alpha=0.25); ax.legend()
    p = os.path.join(OUT, 'ASN16_scratch_vs_geometry.png')
    fig.savefig(p, dpi=110, bbox_inches='tight'); plt.close(fig)
    print(); print('график:', p)
except ImportError:
    pass
