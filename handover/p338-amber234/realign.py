import sys, json, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'; P = 'D:/BqMoni_Claude/p338/g4/page/'
def csv(fn):
    hdr = {}; c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#'):
            k, _, v = line[1:].partition(':'); hdr.setdefault(k.strip(), []).append(v.strip()); continue
        if line.startswith('channel'): continue
        c.append(float(line.split(',')[1]))
    return hdr, np.array(c)
def coeffs(s): return [float(x) for x in re.search(r'coeffs:\s*([-0-9.eE+ ]+)', s).group(1).split()[:5]]
def E(cf, ch): return sum(c * ch ** i for i, c in enumerate(cf))
def chan(cf, e0):
    ch = np.arange(0, 8192, 0.01); return float(np.interp(e0, E(cf, ch), ch))
hb, bg = csv('bg_marinelli_water_1l_33.2h.csv'); bg[-1] = 0
for page, fn, sfile, bgi in (('Th-232', 'data.js', 'th232_reference_marinelli_1l.csv', 1), ('черника', 'data-berry.js', 'berry_dried_marinelli_1l_482g_49.8h.csv', 2)):
    t = open(P + fn, encoding='utf-8').read().strip(); d = json.loads(t[t.index('{'):].rstrip(';'))
    s = d['spectrum']; e = np.array(s['e_of_ch']); n = len(e)
    data = np.array(s['counts'], float); st = {k: np.array(v, float) for k, v in s['stack'].items()}
    model = sum(st.values()); BG = st['BG']
    hs, y = csv(sfile); cs = coeffs(hs['page_scale_1'][0]); cb = coeffs(hb['page_scale_%d' % bgi][0])
    k = float(hs['live_time_s'][0]) / float(hb['live_time_s'][0])
    # page axis is the sample page scale? check
    es = E(cs, np.arange(n, dtype=float))
    # gain from high-energy anchors: ch_s = g * ch_b
    anchors = [351.9, 609.3, 911.2, 1460.8, 2614.5]
    g = np.mean([chan(cs, a) / chan(cb, a) for a in anchors])
    # applied r(E): BG layer / (bg on sample axis via BG scale)
    eb = E(cb, np.arange(len(bg), dtype=float))
    def rebin(edges_src, counts, edges_dst):
        cum = np.concatenate([[0], np.cumsum(counts)]); return np.diff(np.interp(edges_dst, edges_src, cum))
    edg = lambda x: np.concatenate([x - 0.5 * np.gradient(x), [x[-1] + 0.5 * np.gradient(x)[-1]]])
    bg_by_scale = rebin(edg(eb), bg, edg(e)) * k
    r = np.where(bg_by_scale > 0, BG / np.maximum(bg_by_scale, 1e-9), 1.0)
    rs = np.convolve(r, np.ones(41) / 41, 'same')
    # by channel: sample channel c  <->  bg channel c/g
    chb = np.arange(len(bg), dtype=float) * g
    bg_by_ch = rebin(edg(chb), bg, edg(np.arange(n, dtype=float))) * k
    BG2 = bg_by_ch * rs
    model2 = model - BG + BG2
    print(f'== {page}: усиление проба/фон по реперам ≥352 кэВ g = {g:.5f}; ось страницы против шкалы пробы: макс. откл. {np.max(np.abs(e - es)[:3000]):.3f} кэВ (до канала 3000)')
    print('  полоса кэВ |  данные  | модель стр. | мод/данные | фон по шкале | фон по каналу | модель с фоном по каналу | мод/данные')
    for a, b in ((20, 30), (30, 40), (40, 50), (50, 60), (60, 70), (70, 80), (80, 90), (90, 100), (100, 120), (120, 150), (150, 200), (200, 300)):
        m = (e >= a) & (e < b)
        D, M, M2 = data[m].sum(), model[m].sum(), model2[m].sum()
        print(f'  {a:4d}-{b:<4d} {D:10.0f} {M:11.0f} {M/D:9.3f} {BG[m].sum():12.0f} {BG2[m].sum():12.0f} {M2:14.0f} {M2/D:9.3f}')

print()
print('ПЕРЕПОДГОНКА амплитуд на окне 150–3000 кэВ (как у них), фон фиксирован; χ² по Пуассону данных')
from scipy.optimize import nnls
for page, fn, sfile, bgi, groups in (('Th-232', 'data.js', 'th232_reference_marinelli_1l.csv', 1, [['Th232','Ac228','Th228','Ra224','Rn220','Pb212','Bi212','Tl208']]),
                                     ('черника', 'data-berry.js', 'berry_dried_marinelli_1l_482g_49.8h.csv', 2, [['CS137'], ['K40'], ['SRY90']])):
    t = open(P + fn, encoding='utf-8').read().strip(); d = json.loads(t[t.index('{'):].rstrip(';'))
    s = d['spectrum']; e = np.array(s['e_of_ch']); n = len(e)
    data = np.array(s['counts'], float); st = {k: np.array(v, float) for k, v in s['stack'].items()}
    BG = st['BG']
    hs, y = csv(sfile); cs = coeffs(hs['page_scale_1'][0]); cb = coeffs(hb['page_scale_%d' % bgi][0])
    k = float(hs['live_time_s'][0]) / float(hb['live_time_s'][0])
    g = np.mean([chan(cs, a) / chan(cb, a) for a in [351.9, 609.3, 911.2, 1460.8, 2614.5]])
    eb = E(cb, np.arange(len(bg), dtype=float))
    def rebin(es_, counts, ed):
        cum = np.concatenate([[0], np.cumsum(counts)]); return np.diff(np.interp(ed, es_, cum))
    edg = lambda x: np.concatenate([x - 0.5 * np.gradient(x), [x[-1] + 0.5 * np.gradient(x)[-1]]])
    bgs = rebin(edg(eb), bg, edg(e)) * k
    rs = np.convolve(np.where(bgs > 0, BG / np.maximum(bgs, 1e-9), 1.0), np.ones(41) / 41, 'same')
    BG2 = rebin(edg(np.arange(len(bg), dtype=float) * g), bg, edg(np.arange(n, dtype=float))) * k * rs
    T = [sum(st[x] for x in grp) for grp in groups]
    w = (e >= 150) & (e <= 3000)
    for lab, B in (('фон по шкале (как на странице)', BG), ('фон по каналу', BG2)):
        sig = np.sqrt(np.maximum(data[w], 1))
        A = np.vstack([tt[w] for tt in T]).T / sig[:, None]; a, _ = nnls(A, (data[w] - B[w]) / sig)
        M = B + sum(ai * tt for ai, tt in zip(a, T))
        chi = float(np.sum(((data[w] - M[w]) / sig) ** 2) / (w.sum() - len(a)))
        print(f'== {page}, {lab}: амплитуды {", ".join(f"{x:.4f}" for x in a)} (1 = страница), χ²/ν(150–3000) {chi:.2f}')
        print('   ' + ' '.join(f'{lo}-{hi}:{M[(e>=lo)&(e<hi)].sum()/data[(e>=lo)&(e<hi)].sum():.3f}' for lo, hi in ((20,30),(30,40),(40,50),(50,60),(60,70),(70,80),(80,90),(90,100),(100,120),(120,150))))
