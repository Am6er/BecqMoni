import sys, json, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'; P = 'D:/BqMoni_Claude/p338/g4/page/'
def csv(fn):
    hdr = {}; ch = []; c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#'):
            k, _, v = line[1:].partition(':'); hdr.setdefault(k.strip(), []).append(v.strip()); continue
        if line.startswith('channel'): continue
        a, b = line.split(','); ch.append(int(a)); c.append(float(b))
    return hdr, np.array(c)
def coeffs(s):
    m = re.search(r'coeffs:\s*([-0-9.eE+ ]+)', s); return [float(x) for x in m.group(1).split()[:5]]
def E(cf, n): ch = np.arange(n, dtype=float); return sum(c * ch ** i for i, c in enumerate(cf))
hb, bg = csv('bg_marinelli_water_1l_33.2h.csv')
for page, fn, sfile, bgidx in (('th', 'data.js', 'th232_reference_marinelli_1l.csv', 0), ('berry', 'data-berry.js', 'berry_dried_marinelli_1l_482g_49.8h.csv', 1)):
    t = open(P + fn, encoding='utf-8').read().strip(); d = json.loads(t[t.index('{'):].rstrip(';'))
    s = d['spectrum']; e = np.array(s['e_of_ch']); BG = np.array(s['stack']['BG'])
    hs, y = csv(sfile)
    cs = coeffs(hs['page_scale_1'][0]); cb = coeffs(hb['page_scale_%d' % (bgidx + 1)][0])
    es = E(cs, len(y)); eb = E(cb, len(bg))
    k = float(hs['live_time_s'][0]) / float(hb['live_time_s'][0])
    # bg counts density per keV on its own scale, rebinned to the sample channels
    cum = np.concatenate([[0], np.cumsum(bg)]); ebe = np.concatenate([eb - 0.5 * np.gradient(eb), [eb[-1] + 0.5 * np.gradient(eb)[-1]]])
    ese = np.concatenate([es - 0.5 * np.gradient(es), [es[-1] + 0.5 * np.gradient(es)[-1]]])
    bg_on_s = np.diff(np.interp(ese, ebe, cum)) * k
    n = min(len(e), len(bg_on_s)); 
    print(f'== {page}: k={k:.5f}; e_of_ch vs own scale max dev {np.max(np.abs(e[:n]-es[:n])):.3f} keV')
    print(' E,keV | BG layer | bg×k (на шкале пробы) | r = BG/(bg×k) | данные')
    for a, b in ((15, 20), (20, 25), (25, 30), (30, 35), (35, 40), (40, 50), (50, 60), (60, 70), (70, 80), (80, 90), (90, 100), (100, 120), (120, 150), (150, 200), (200, 300), (300, 500), (500, 1000)):
        m = (e[:n] >= a) & (e[:n] < b)
        print(f' {a:4d}-{b:<4d} {BG[:n][m].sum():11.0f} {bg_on_s[:n][m].sum():11.0f} {BG[:n][m].sum()/max(bg_on_s[:n][m].sum(),1):7.3f} {np.array(s["counts"])[:n][m].sum():11.0f}')
