"""Пик K-рентгена Ba у черники: центр и ширина в данных (минус фон страницы) и в их слое Cs-137, на оси страницы."""
import sys, json
import numpy as np
from scipy.optimize import curve_fit
sys.stdout.reconfigure(encoding='utf-8')
P = 'D:/BqMoni_Claude/p338/g4/page/'
t = open(P + 'data-berry.js', encoding='utf-8').read().strip(); d = json.loads(t[t.index('{'):].rstrip(';'))
s = d['spectrum']; e = np.array(s['e_of_ch']); y = np.array(s['counts'], float)
st = {k: np.array(v, float) for k, v in s['stack'].items()}


def g(x, a, mu, sg, b0, b1):
    return a * np.exp(-0.5 * ((x - mu) / sg) ** 2) + b0 + b1 * (x - mu)


def fit(v, lo, hi, w=None):
    m = (e >= lo) & (e <= hi); x = e[m]; yy = v[m]
    p0 = [yy.max() - yy.min(), x[np.argmax(yy)], 3.0, yy.min(), 0.0]
    q, cv = curve_fit(g, x, yy, p0=p0, sigma=None if w is None else w[m], maxfev=20000)
    return q, np.sqrt(np.diag(cv))


for lab, v, w in (('данные − фон страницы', y - st['BG'], np.sqrt(np.maximum(y, 1))),
                  ('данные − фон − тормозное Sr/Y', y - st['BG'] - st['SRY90'], np.sqrt(np.maximum(y, 1))),
                  ('их слой Cs-137', st['CS137'], None)):
    q, dq = fit(v, 24, 44, w)
    print(f'{lab:32s}: центр {q[1]:6.2f} ± {dq[1]:.2f} кэВ, ПШПВ {2.3548 * abs(q[2]):5.2f} кэВ, высота {q[0]:8.0f}')
print('их закон ПШПВ k·E^p на 32.2 кэВ:', round(0.7880103601331053 * 32.2 ** 0.6335125944403551, 2), 'кэВ')
