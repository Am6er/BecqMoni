# -*- coding: utf-8 -*-
"""П67: кривые эффективности сцен из узла <Efficiency> по-сценных копий — значения на опорных линиях и отношения."""
import glob
import io
import os
import re
import sys

import numpy as np


def curve(path):
    t = io.open(path, encoding='utf-8').read()
    i = t.index('<Efficiency>')
    j = t.index('</Curve>', i)
    b = t[i:j]
    pts = re.findall(r'<Energy>([^<]+)</Energy>\s*<Efficiency>([^<]+)</Efficiency>', b)
    E = np.array([float(e) for e, f in pts])
    F = np.array([float(f) for e, f in pts])
    return E, F


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    d = r'D:\BqMoni_Claude\p67\spectra_scenes'
    rows = []
    for f in sorted(glob.glob(os.path.join(d, '*__AS80_p67_*.xml'))):
        if '.escale' in f:
            continue
        name = os.path.basename(f)[:-4]
        if not name.startswith(('contact52k__', 'face81__', 'edge93__')):
            continue
        try:
            E, F = curve(f)
        except ValueError:
            continue
        if len(E) == 0:
            continue

        def at(x):
            return float(np.exp(np.interp(np.log(x), np.log(E), np.log(np.maximum(F, 1e-30)))))
        rows.append((name.split('__')[1], len(E), at(238.6), at(338.3), at(583.2), at(911.2), at(1460.8), at(2614.5)))
    print('%-32s %3s %9s %9s %9s %9s %9s %9s | %8s %8s %8s' % ('сцена', 'n', 'ε238', 'ε338', 'ε583', 'ε911', 'ε1461', 'ε2614', '238/2614', '583/2614', '911/2614'))
    for r in rows:
        print('%-32s %3d %9.4g %9.4g %9.4g %9.4g %9.4g %9.4g | %8.3f %8.3f %8.3f' % (r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[2] / r[7], r[4] / r[7], r[5] / r[7]))


if __name__ == '__main__':
    main()
