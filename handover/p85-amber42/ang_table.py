# -*- coding: utf-8 -*-
r"""П85: сводка ANGCORR из логов ang_*.log против наших A_kk (AngularProbe) и формулы с обратным знаком δ1."""
import glob, io, re, sys
sys.stdout.reconfigure(encoding='utf-8')
OURS = {('co60', 1173.2, 1332.5): (0.1035, 0.0087, 0.1005, 0.0094), ('y88', 898.0, 1836.1): (-0.0714, 0.0, -0.0714, 0.0),
        ('cs134', 604.7, 795.9): (0.1020, 0.0091, 0.1020, 0.0091), ('cs134', 569.3, 795.9): (0.2496, 0.0096, 0.1034, 0.0096),
        ('cs134', 563.2, 604.7): (-0.1678, 0.3207, 0.0265, 0.3207), ('eu152', 1408.0, 121.8): (0.2808, 0.0006, 0.2180, 0.0006),
        ('eu152', 1112.1, 121.8): (-0.1136, -0.0806, -0.2911, -0.0806), ('eu152', 964.1, 121.8): (0.1685, 0.0037, 0.3241, 0.0037),
        ('eu152', 778.9, 344.3): (-0.0714, 0.0, -0.0714, 0.0), ('eu152', 411.1, 344.3): (0.1020, 0.0091, 0.1020, 0.0091)}
print('%-28s %-4s %9s %20s %20s %22s %22s' % ('пара', 'corr', 'пар', 'G4 A22 ± σ', 'G4 A44 ± σ', 'наше (A22, A44)', 'знак δ1 обратный'))
for f in sorted(glob.glob(r'D:\BqMoni_Claude\p85\g4\ang_*_*_*_o*.log')):
    m = re.search(r'ang_(\w+)_([\d.]+)_([\d.]+)_(on|off)\.log', f)
    n, e1, e2, mode = m.group(1), float(m.group(2)), float(m.group(3)), m.group(4)
    line = [l for l in io.open(f, encoding='utf-8', errors='replace') if l.startswith('ANGCORR')]
    if not line: continue
    g = re.search(r'pairs=(\d+) A22=([-\d.]+) sigma=([\d.]+) A44=([-\d.]+) sigma=([\d.]+)', line[0])
    o = OURS.get((n, e1, e2), (float('nan'),) * 4)
    print('%-28s %-4s %9s %9.4f ± %.4f   %9.4f ± %.4f   %9.4f %9.4f   %9.4f %9.4f' % ('%s %g+%g' % (n, e1, e2), mode, g.group(1), float(g.group(2)), float(g.group(3)), float(g.group(4)), float(g.group(5)), o[0], o[1], o[2], o[3]))
