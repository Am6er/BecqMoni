# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Пункт 3: подгонка параметров Пейна GAGG сквозь перенос — по коэффициентам LightFitP200 --jac (свет пика —
# линейная функция узлов кривой электронов: S(E) = Σ_k a_k(E)·y_k, поток историй тот же при любой кривой).
# Цели вместе: форма Kole & De Angelis рис. 2 (25.7…75.7 кэВ, масштаб свободный) и σ_nPR 2.4…3.0 %.
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import glob
import io
import math
import sys

import numpy as np

sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, HERE)
import payne  # noqa: E402

J = WORK + 'jac/'   # LightFitP200 --jac (jac_runs.py)
KOLE = _os.path.join(TREE, 'tools', 'nucdb', 'data', 'photon_light', 'kole2025_fig2_digitized.tsv')
ONS = 36.4
DB = (0.0, 36.4, 0.0, 133.33333333333334)   # η, S_Ons, S_Trap, S_Birks базы (kB Kole 0.075 мм/МэВ)
NPR_DBDATA = [10, 10.3, 10.4, 10.5, 10.7, 11, 11.5, 12, 13, 14, 15, 16, 18, 20, 22, 25, 28, 30, 33, 36, 40, 43, 46, 48,
              50, 50.2, 50.3, 50.4, 50.6, 50.9, 51.3, 51.8, 52.5, 53.5, 55, 60, 81, 100, 122, 200, 356, 661.657]


def load_jac(prefix):
    a = {}
    nodes = {}
    for path in sorted(glob.glob(J + prefix + '_*.csv')):
        for line in io.open(path, encoding='utf-8'):
            if line.startswith('E_kev') or line.count(',') != 4 or not line.endswith(chr(10)):
                continue            # строка, которую процесс ещё не дописал
            e, k, nk, coef, s0 = line.strip().split(',')
            e, k = float(e), int(k)
            a.setdefault(e, {})[k] = float(coef)
            nodes[k] = float(nk)
            a[e]['S0'] = float(s0)
    return a, nodes


class Photon(object):
    def __init__(self, prefix):
        self.a, self.nodes = load_jac(prefix)
        self.E = sorted(self.a)

    def check_nodes(self, cv):
        bad = [k for k, e in self.nodes.items() if e != cv[k][0]]
        return len(self.nodes), len(bad)

    def S(self, cv):
        y = [v for _, v in cv]
        out = {}
        for e in self.E:
            out[e] = sum(c * y[k] for k, c in self.a[e].items() if k != 'S0')
        return out

    def S0_check(self, cv):
        s = self.S(cv)
        return max(abs(s[e] / self.a[e]['S0'] - 1.0) for e in self.E)


def interp_log(pts, e):
    if e <= pts[0][0]:
        return pts[0][1]
    for i in range(1, len(pts)):
        if e <= pts[i][0]:
            t = (math.log(e) - math.log(pts[i - 1][0])) / (math.log(pts[i][0]) - math.log(pts[i - 1][0]))
            return pts[i - 1][1] + t * (pts[i][1] - pts[i - 1][1])
    return pts[-1][1]


_GRID = {}


def sigma_npr(s, subset=None, lo=10.5, hi=661.657, n=20000):
    """σ_nPR по Ходюку: среднее по ЛИНЕЙНОЙ энергии lo…hi |1 − r(E)/r(662)|, r между точками — линейно по ln E
    (как photon_cmp.py полосы dbdata; там — цикл, здесь — numpy, числа те же)."""
    pts = sorted((e, v) for e, v in s.items() if subset is None or any(abs(e - x) < 1e-9 for x in subset))
    key = (lo, hi, n)
    if key not in _GRID:
        _GRID[key] = np.log(lo + (hi - lo) * (np.arange(n) + 0.5) / n)
    lx = np.log([p[0] for p in pts])
    v = np.array([p[1] for p in pts])
    r0 = interp_log(pts, hi)
    r = np.interp(_GRID[key], lx, v)
    return float(np.mean(np.abs(1.0 - r / r0)) * 100.0)


def kole_data():
    return [tuple(float(v) for v in l.split('\t')[:2]) for l in io.open(KOLE, encoding='utf-8') if l.strip() and not l.startswith('#')]


def kole_cmp(s, data):
    pairs = [(e, d, s[min(s, key=lambda x: abs(x - e))]) for e, d in data]
    k = sum(d * m for e, d, m in pairs) / sum(m * m for e, d, m in pairs)
    dev = [(k * m - d) / d * 100.0 for e, d, m in pairs]
    return math.sqrt(sum(v * v for v in dev) / len(dev)), max(abs(v) for v in dev), dev, k


class Model(object):
    def __init__(self):
        self.d = payne.load_dump()
        self.npr = Photon('npr')
        self.kole = Photon('kole')
        self.data = kole_data()

    def evaluate(self, cv):
        sn = self.npr.S(cv)
        sk = self.kole.S(cv)
        r0 = sn[661.657]
        sig = sigma_npr(sn)
        sko, mx, dev, k = kole_cmp(sk, self.data)
        return {'sigma': sig, 'sigma_dbdata': sigma_npr(sn, NPR_DBDATA), 'sko': sko, 'max': mx, 'dev': dev,
                'r': dict((e, v / r0) for e, v in sn.items()), 'kole': sk, 'k': k}

    def params(self, eta, trap, birks, ons=ONS):
        return self.evaluate(payne.curve(self.d, eta, ons, trap, birks))


def fmt(res):
    r = res['r']
    return 'σ_nPR %.2f %% (точки dbdata %.2f %%); Kole СКО %.2f %%, наибольшее %.2f %%; r(10) %.3f r(30) %.3f r(100) %.3f r(356) %.3f' % (
        res['sigma'], res['sigma_dbdata'], res['sko'], res['max'], r[10], r[30], r[100], r[356])


if __name__ == '__main__':
    m = Model()
    cv = payne.curve(m.d, *DB)
    print('узлы якобиана против узлов кривой:', m.npr.check_nodes(cv), m.kole.check_nodes(cv))
    print('Σ a_k·y_k против S0 пробы (наибольшее отн.):', m.npr.S0_check(cv), m.kole.S0_check(cv))
    print('ПАРАМЕТРЫ БАЗЫ (η 0, S_Trap 0, S_Birks 133.33):', fmt(m.evaluate(cv)))
