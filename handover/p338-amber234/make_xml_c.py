"""Вариант В: вариант Б, но фон умножен на их r(E) (восстановлено со страницы: слой фона / фон × k по шкале пробы,
сглажено 41 каналом) — проверка, что недобор фона у Th-232 в нашем разборе — ослабление внешнего фона пробой."""
import sys, re, json, os
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'; P = G + 'page/'
SRC = 'D:/BqMoni_Claude/p338/show/spectra_b/'; DST = 'D:/BqMoni_Claude/p338/show/spectra_c/'
os.makedirs(DST, exist_ok=True)


def csvh(fn):
    hdr = {}; c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#'):
            k, _, v = line[1:].partition(':'); hdr.setdefault(k.strip(), []).append(v.strip()); continue
        if line.startswith('channel'):
            continue
        c.append(float(line.split(',')[1]))
    return hdr, np.array(c)


def coeffs(s):
    return [float(x) for x in re.search(r'coeffs:\s*([-0-9.eE+ ]+)', s).group(1).split()[:5]]


def E(cf, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(cf))


def edg(x):
    return np.concatenate([x - 0.5 * np.gradient(x), [x[-1] + 0.5 * np.gradient(x)[-1]]])


def rebin(es, counts, ed):
    cum = np.concatenate([[0], np.cumsum(counts)]); return np.diff(np.interp(ed, es, cum))


hb, bg = csvh('bg_marinelli_water_1l_33.2h.csv'); bg[-1] = 0
FILE = [float(x) for x in hb['energy_scale_file_coeffs'][0].split()]
for key, page, sfile, bgi in (('GS2020_Th232_oisn10', 'data.js', 'th232_reference_marinelli_1l.csv', 1),
                              ('GS2020_berry_482g', 'data-berry.js', 'berry_dried_marinelli_1l_482g_49.8h.csv', 2)):
    t = open(P + page, encoding='utf-8').read().strip(); d = json.loads(t[t.index('{'):].rstrip(';'))
    e = np.array(d['spectrum']['e_of_ch']); BG = np.array(d['spectrum']['stack']['BG'], float)
    hs, _ = csvh(sfile); k = float(hs['live_time_s'][0]) / float(hb['live_time_s'][0])
    eb = E(coeffs(hb['page_scale_%d' % bgi][0]), np.arange(len(bg), dtype=float))
    bgs = rebin(edg(eb), bg, edg(e)) * k
    r = np.convolve(np.where(bgs > 50, BG / np.maximum(bgs, 1e-9), 1.0), np.ones(41) / 41, 'same')
    good = (e > 15) & (e < 3000)
    rE = lambda x: np.interp(x, e[good], r[good], left=r[good][0], right=1.0)
    ebd = E(FILE, np.arange(len(bg), dtype=float))   # энергия канала фона по шкале прибора (вариант Б)
    newbg = np.rint(bg * rE(ebd)).astype(int)
    x = open(SRC + key + '.xml', encoding='utf-8-sig').read()
    i = x.index('<BackgroundEnergySpectrum>'); j = x.index('</BackgroundEnergySpectrum>', i)
    blk = x[i:j]
    it = iter(newbg)
    blk = re.sub(r'<DataPoint>[-0-9.eE+]+</DataPoint>', lambda m: f'<DataPoint>{next(it)}</DataPoint>', blk)
    tot = int(newbg.sum())
    blk = re.sub(r'<ValidPulseCount>\d+</ValidPulseCount>', f'<ValidPulseCount>{tot}</ValidPulseCount>', blk)
    blk = re.sub(r'<TotalPulseCount>\d+</TotalPulseCount>', f'<TotalPulseCount>{tot}</TotalPulseCount>', blk)
    x = x[:i] + blk + x[j:]
    x = x.replace('(energy_scale_file_coeffs)', '(energy_scale_file_coeffs); фон × их r(E) со страницы (вариант В)')
    open(DST + key + '.xml', 'wb').write(('\ufeff' + x).encode('utf-8'))
    print(key, 'r(E) на 30/45/60/80/120/300 кэВ:', ' '.join(f'{rE(v):.3f}' for v in (30, 45, 60, 80, 120, 300)),
          f'| фон {bg.sum():.0f} → {tot}')
