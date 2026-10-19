"""Наша матрица (.rmx, формат 8–11 тела) против их npz (ОИСН-10) в мягкой зоне.
Окна — те же, что в npz_lowE.py: пик [E-1,E+1), K-вылет иода [E-33.6,E-27.8), между ними, ниже.
Запуск: python cmp_rmx_npz.py <файл.rmx> [<файл.rmx> ...]   (все узлы файлов 15–100 кэВ)."""
import struct
import sys
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
NPZ = 'D:/BqMoni_Claude/p338/g4/response_matrix_marinelli1l_oisn10.npz'


def read_string(b, pos):
    n, shift = 0, 0
    while True:
        c = b[pos]; pos += 1
        n |= (c & 0x7F) << shift
        if c < 0x80:
            break
        shift += 7
    return b[pos:pos + n].decode('utf-8'), pos + n


def load_rmx(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'BQRM', path
    fmt = struct.unpack_from('<i', b, 4)[0]
    stamp, p = read_string(b, 8)
    bin_kev = struct.unpack_from('<d', b, p)[0]
    p += 8 + 4 + 8 + 8
    p += 8 + 8 + 4 + 8 + 4 + 8 + 4
    nodes = struct.unpack_from('<i', b, p)[0]; p += 4
    energies = np.array(struct.unpack_from('<%dd' % nodes, b, p)); p += 8 * nodes
    channels = struct.unpack_from('<i', b, p)[0]; p += 4
    rows = [[None] * nodes for _ in range(channels)]
    for c in range(channels):
        for i in range(nodes):
            n = struct.unpack_from('<i', b, p)[0]; p += 4
            rows[c][i] = np.array(struct.unpack_from('<%df' % n, b, p), float); p += 4 * n
    return fmt, stamp, bin_kev, energies, rows


def windows(e, edges_lo, r):
    """r — плотность на бин, edges_lo — левые края бинов, ширина бина единая."""
    w = edges_lo[1] - edges_lo[0] if len(edges_lo) > 1 else 1.0
    def band(a, b):
        # доля бина, попадающая в [a, b)
        lo = np.clip(np.minimum(edges_lo + w, b) - np.maximum(edges_lo, a), 0, w) / w
        return float((r * lo).sum())
    tot = float(r.sum())
    pk = band(e - 1, e + 1)
    if e > 33.17:
        ke = band(e - 35.0, e - 26.0); mid = band(e - 26.0, e - 1); lo = band(0, e - 35.0)   # окно K-вылета шире обоих пиков (Kα 28.3–28.6, Kβ 32.3–33.0) при любой раскладке бинов
    else:
        ke = 0.0; mid = 0.0; lo = band(0, e - 1)
    return tot, pk, ke, mid, lo


z = np.load(NPZ)
GE = z['E_nodes']; GC = z['counts_edep'].astype(float); GN = z['n_primary'].astype(float); gx = z['bin_centers_keV'] - 0.5
for path in sys.argv[1:]:
    fmt, stamp, bk, E, rows = load_rmx(path)
    print(f'== {path}\n   формат {fmt}, бин {bk} кэВ, клеймо: {stamp[:160]}')
    print('   E наш | E их  |   ε_tot наш/их   |   пик наш/их     | K-вылет наш/их   | между наш/их     | ниже наш/их      | отн.: пик  K-вых  между  ниже  ε_tot')
    for i, e in enumerate(E):
        if e < 15 or e > 100:
            continue
        n = max(len(rows[c][i]) for c in range(len(rows)))
        r = np.zeros(n)
        for c in range(len(rows)):
            v = rows[c][i]; r[:len(v)] += v
        ed = (np.arange(len(r)) - 0.5) * bk   # бины центрированы: BinOf = round(dep/bin) (EfficiencySimulator.cs:8722)
        a = windows(e, ed, r)
        j = int(np.argmin(abs(GE - e)))
        g = windows(GE[j], gx, GC[j] / GN[j])
        rel = [x / y if y > 0 else float('nan') for x, y in zip(a, g)]
        print(f'  {e:7.3f}|{GE[j]:7.3f}| {a[0]:.3e}/{g[0]:.3e} | {a[1]:.3e}/{g[1]:.3e} | {a[2]:.3e}/{g[2]:.3e} | {a[3]:.3e}/{g[3]:.3e} | {a[4]:.3e}/{g[4]:.3e} |'
              f' {rel[1]:6.3f} {rel[2]:6.3f} {rel[3]:6.3f} {rel[4]:6.3f} {rel[0]:6.3f}')
