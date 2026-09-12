# -*- coding: utf-8 -*-
# П13 (S169, нуль по съёмке): ПРЕДВАРИТЕЛЬНЫЙ РАЗБОР по опорам плеча calib П12 — какой МНК нуля
# куда ставит образ Am-241 у трёх смесей и что даёт у остальных. Считается ДО правки кода, чтобы
# форму МНК выбрать замером, а не вкусом.
#   x_j = свет пика = model_kev + light_shift_kev;  y_j = канал измеренного центра (калибровкой файла);
#   МНК y = g·x + c0 по кандидатам ≤ 700 кэВ, прошедшим z ≥ 5, |сдвиг| ≤ 1 ПШПВ, доля ≥ порог; z0 = −c0/g.
#   Карта adc: E꜀(x) = E0 + (x − z0)·s, s = (x1 − E0)/(x1 − z0), x1 — верхняя линия библиотеки (≈ верхний кандидат).
import csv, io, os, sys, math
import xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
out_dir = sys.argv[1]
spectra_dir = sys.argv[2]
SHARE = float(sys.argv[3]) if len(sys.argv) > 3 else 0.25
ZMIN = 5.0
MAXKEV = 700.0


def calib_of(path):
    t = ET.parse(path).getroot()
    ec = t.find('.//EnergyCalibration')
    co = [float(c.text) for c in ec.find('Coefficients')]
    nch = len(t.find('.//Spectrum').findall('DataPoint'))
    return co, nch


def poly(co, ch): return sum(c * ch ** i for i, c in enumerate(co))


def e2ch(co, e, nch):
    a, b = -50.0, float(nch)
    if poly(co, a) > e: return float('nan')
    for _ in range(80):
        m = 0.5 * (a + b)
        if poly(co, m) < e: a = m
        else: b = m
    return 0.5 * (a + b)


rows = {}
drift = {}
for name in os.listdir(out_dir):
    if name.endswith('_anchors.csv'):
        for r in csv.DictReader(io.open(os.path.join(out_dir, name), encoding='utf-8-sig', newline='')):
            rows.setdefault(r['spectrum'], []).append(r)
    if name.endswith('_runs.csv'):
        for r in csv.DictReader(io.open(os.path.join(out_dir, name), encoding='utf-8-sig', newline='')):
            try:
                drift[r['spectrum']] = (float(r['gain']), float(r['offset_ch']))
            except ValueError:
                pass

# верхняя линия библиотеки — берём верхний кандидат спектра (в анализаторе — верхняя линия библиотеки; у смесей 1408)
def lsq(pts, weighted):
    sw = swx = swy = swxx = swxy = 0.0
    for x, y, w in pts:
        if not weighted: w = 1.0
        sw += w; swx += w * x; swy += w * y; swxx += w * x * x; swxy += w * x * y
    det = sw * swxx - swx * swx
    if abs(det) < 1e-12: return None
    g = (sw * swxy - swx * swy) / det
    c0 = (swxx * swy - swx * swxy) / det
    return g, c0


only = set(sys.argv[4].split(',')) if len(sys.argv) > 4 else None
print(u'%-22s %2s %7s %7s %7s | %7s %7s | %s' % (u'спектр', u'n', u'c0(u)', u'c0(w)', u'c0(2)', u'E(0)', u'z0cal', u'кандидаты: линия(доля,z,сдвиг) ; Am-241/низ: образ→канал при c0(u)/c0(w)/c0(2)/adc0, данные'))
for sp in sorted(rows):
    if only and sp not in only: continue
    rs = rows[sp]
    co, nch = calib_of(os.path.join(spectra_dir, sp + '.xml'))
    e0 = co[0]
    top = 0.0
    cands = []
    for r in rs:
        line = float(r['line_kev'])
        top = max(top, line + float(r['light_shift_kev'] or 0))
        if r['measured_kev'] in ('', 'NaN'): continue
        ref = r['refusal']
        if ref in ('edge', 'skip', 'narrow'): continue
        z = float(r['z']); sh = float(r['peak_share'] or 0)
        if z < ZMIN or sh < SHARE: continue
        mk = float(r['model_kev']); meas = float(r['measured_kev'])
        cm = e2ch(co, mk, nch); cd = e2ch(co, meas, nch)
        # свет модельного центра — дрейф снят (в плече calib β = 1: свет уже в образе)
        ga, ob = drift.get(sp, (1.0, 0.0))
        pu = (cm - ob) / ga
        xl = poly(co, pu)
        lo, hi = int(r['ch_lo']), int(r['ch_hi'])
        fwhm = (hi - lo) / 2.0
        if abs(cd - cm) > 1.0 * fwhm: continue
        x = xl
        if x > MAXKEV: continue
        sig = e2ch(co, meas + float(r['sigma_kev']), nch) - cd
        w = 1.0 / (sig * sig) if sig > 0 else 1.0
        cands.append((x, cd, w, line, sh, z, meas - mk))
    if len(cands) < 2:
        print(u'%-22s %2d  %s' % (sp, len(cands), u'— нехватка (запасной путь)'))
        continue
    xs = [c[0] for c in cands]
    lever = min(xs) > 0 and max(xs) - min(xs) >= 0.5 * max(xs)
    if not lever:
        print(u'%-22s %2d  %s' % (sp, len(cands), u'— нет плеча (запасной путь)'))
        continue
    pts = [(c[0], c[1], c[2]) for c in cands]
    ru = lsq(pts, False); rw = lsq(pts, True)
    two = sorted(pts)
    r2 = lsq([two[0], two[-1]], False)
    z0c = e2ch(co, 0.0, nch)
    # низший кандидат: куда ляжет его образ при каждом нуле
    low = min(cands)
    def land(g, c0):
        z0 = -c0 / g
        s = (top - e0) / (top - z0)
        ec = e0 + (low[0] - z0) * s
        return e2ch(co, ec, nch)
    lu = land(*ru); lw = land(*rw); l2 = land(*r2)
    s0 = (top - e0) / top; la = e2ch(co, e0 + low[0] * s0, nch)
    desc = ' '.join('%.0f(%.2f,%.0f,%+.1f)' % (c[3], c[4], c[5], c[6]) for c in sorted(cands, key=lambda c: c[0]))
    print(u'%-22s %2d %7.2f %7.2f %7.2f | %7.2f %7.2f | %s ; %.0f: %.1f/%.1f/%.1f/%.1f, данные %.1f' % (
        sp, len(cands), ru[1], rw[1], r2[1], e0, z0c, desc, low[3], lu, lw, l2, la, low[1]))
