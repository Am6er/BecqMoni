# -*- coding: utf-8 -*-
u"""bqp15_edge_fit.py — П15 / A307(а)-1: край порога регистрации по СЫРЫМ спектрам, в каналах, на прибор.

Модель края: μ(ch) = (a + b·(ch − c)) · Φ((ch − c)/s), Φ — нормальная функция распределения;
c — середина края (50 % плато), s — его ширина (σ), плато линейное. Фит по каналам от первого
ненулевого − 2 до первого ненулевого + N (N на прибор), веса 1/max(raw,1). Только ЧИСТЫЕ спектры
(без рентгена/линий у края); спектры с рентгеном печатаются для сравнения, но в разброс не идут.
Печать: c, s, ch10 = c − 1.28 s, ch90 = c + 1.28 s, плато; разброс c по нуклидам прибора (СКО, размах)
против ошибки фита; то же в кэВ по калибровке спектра (чтобы видеть, как расходятся калибровки).
"""
import csv, io, os, sys, glob
import numpy as np
from scipy.optimize import least_squares
from scipy.special import erf
sys.stdout.reconfigure(encoding='utf-8')
DUMP = r'C:\Users\moroz\bqp12_out\a_dump'
OUT = r'C:\Users\moroz\bqp15_out'
os.makedirs(OUT, exist_ok=True)
XRAY = {'Am241', 'Ba133', 'Cd109', 'Ce139', 'Cs137', 'Eu152', 'Co57', 'Lu176', 'Th232WT20'}
EDGE_CH = {'G1S16': (5, 8.6, 12, 16), 'G1S24': (5, 9.9, 14, 18), 'AS80': (52, 59.6, 66, 92), 'ASN16': (5, 28.4, 37, 48)}
NFIT = {'G1S16': 13, 'G1S24': 15, 'AS80': 36, 'ASN16': 44}


def Phi(x):
    return 0.5 * (1.0 + erf(x / np.sqrt(2.0)))


def load(key):
    rows = list(csv.DictReader(io.open(os.path.join(DUMP, key + '_chi.csv'), encoding='utf-8-sig')))
    return (np.array([int(r['raw']) for r in rows]), np.array([float(r['keV']) for r in rows]))


def first_nonzero(raw):
    nz = np.where((raw[:-1] > 0) & (raw[1:] > 0))[0]
    return int(nz[0]) if len(nz) else -1


def fit_edge(raw, c1, n):
    ch = np.arange(max(0, c1 - 2), c1 + n + 1)
    y = raw[ch].astype(float)
    sig = np.sqrt(np.maximum(y, 1.0))
    plateau0 = np.median(y[-4:])
    def resid(p):
        a, b, c, s = p
        return ((a + b * (ch - c)) * Phi((ch - c) / max(s, 1e-3)) - y) / sig
    p0 = [plateau0, 0.0, c1 + 0.4 * n, 0.15 * n]
    r = least_squares(resid, p0, max_nfev=4000)
    J = r.jac
    try:
        cov = np.linalg.inv(J.T @ J)
        err = np.sqrt(np.diag(cov))
    except np.linalg.LinAlgError:
        err = np.full(4, np.nan)
    chi2 = float((r.fun ** 2).sum()); ndf = len(ch) - 4
    return r.x, err, chi2, ndf


def main():
    keys = sorted(os.path.basename(f)[:-8] for f in glob.glob(os.path.join(DUMP, '*_chi.csv')))
    inst = {}
    for k in keys:
        inst.setdefault(k.split('_')[0], []).append(k)
    out = io.open(os.path.join(OUT, 'edge_fit.csv'), 'w', encoding='utf-8', newline='')
    out.write('spectrum,clean,ch_first,c_mid,c_err,s,s_err,ch10,ch90,plateau,chi2,ndf,keV_first,keV_c,keV_ch90,keV_per_ch\n')
    for det, ks in inst.items():
        n = NFIT[det]
        print('\n=== %s: край порога, каналы (фит μ = (a + b(ch−c))·Φ((ch−c)/s)) ===' % det)
        print('%-14s %5s %7s %6s %6s %6s %6s %9s %8s   %7s %7s %7s %6s' % ('спектр', 'ch1', 'c±err', '', 's', 'ch10', 'ch90', 'плато', 'χ²/ndf', 'кэВ ch1', 'кэВ c', 'кэВ90', 'кэВ/ch'))
        cs = []; ss = []; c90 = []; kc = []; errs = []
        for k in ks:
            raw, kev = load(k)
            c1 = first_nonzero(raw)
            # чистота КРАЯ: у AS80 и ASN16 рентген сидит на ch ≥ 90 — от края (ch 50…65 / 20…37) далеко, край чист
            clean = (k.split('_')[1] not in XRAY) or det in ('AS80', 'ASN16')
            p, e, chi2, ndf = fit_edge(raw, c1, n)
            a, b, c, s = p
            s = abs(s)
            ch10, ch90 = c - 1.2816 * s, c + 1.2816 * s
            kpc = kev[101] - kev[100]
            kev_c = float(np.interp(c, np.arange(len(kev)), kev)); kev_90 = float(np.interp(ch90, np.arange(len(kev)), kev))
            print('%-14s %5d %7.2f±%4.2f %6.2f %6.2f %6.2f %9.0f %8.2f   %7.1f %7.1f %7.1f %6.3f %s' % (k[len(det) + 1:], c1, c, e[2], s, ch10, ch90, a, chi2 / ndf, kev[c1], kev_c, kev_90, kpc, '' if clean else '  (рентген/линии у края — не в разброс)'))
            out.write('%s,%d,%d,%r,%r,%r,%r,%r,%r,%r,%r,%d,%r,%r,%r,%r\n' % (k, int(clean), c1, float(c), float(e[2]), float(s), float(e[3]), float(ch10), float(ch90), float(a), chi2, ndf, float(kev[c1]), kev_c, kev_90, float(kpc)))
            if clean:
                cs.append(c); ss.append(s); c90.append(ch90); kc.append(kev_c); errs.append(e[2])
        if cs:
            cs = np.array(cs); ss = np.array(ss); c90 = np.array(c90); kc = np.array(kc); errs = np.array(errs)
            print('  чистых %d: c = %.2f ± %.2f (СКО по нуклидам), размах %.2f…%.2f ch; медиана ошибки фита %.2f ch; s = %.2f ± %.2f; ch90 = %.1f…%.1f (медиана %.1f)'
                  % (len(cs), cs.mean(), cs.std(ddof=1) if len(cs) > 1 else 0, cs.min(), cs.max(), np.median(errs), ss.mean(), ss.std(ddof=1) if len(ss) > 1 else 0, c90.min(), c90.max(), np.median(c90)))
            print('  то же в кэВ КАЛИБРОВКИ спектра: середина края %.1f ± %.1f кэВ (СКО), размах %.1f…%.1f кэВ — разброс калибровок, не порога' % (kc.mean(), kc.std(ddof=1) if len(kc) > 1 else 0, kc.min(), kc.max()))
    out.close()
    # якорь «истинной» шкалы на прибор: центроид K-рентгена бария (≈32 кэВ) в спектре Cs-137 и канал 662 кэВ по калибровке
    print('\n=== оценка истинной энергии края: линейная шкала через центроид K-рентгена Ba (32 кэВ) в Cs-137 и канал 662 по калибровке ===')
    ANCH = {'G1S16': ('G1S16_Cs137_P5', 9, 17), 'G1S24': ('G1S24_Cs137_P5', 8, 17), 'AS80': ('AS80_Cs137_0cm', 84, 112), 'ASN16': ('ASN16_Cs137', 84, 120)}
    for det, (key, a, b) in ANCH.items():
        raw, kev = load(key)
        ch = np.arange(a, b + 1)
        # центроид над локальным линейным фоном по концам окна
        base = np.interp(ch, [a, b], [raw[a], raw[b]])
        net = np.maximum(raw[a:b + 1] - base, 0)
        cx = float((ch * net).sum() / net.sum())
        c662 = float(np.interp(662.0, kev, np.arange(len(kev))))
        g = (662.0 - 32.0) / (c662 - cx)
        e0 = 32.0 - g * cx
        print('  %-6s центроид рентгена ch %.2f (окно %d…%d), 662 кэВ на ch %.1f → %.4f кэВ/ch, ch0 = %.1f кэВ; истинная энергия ch %s → %s кэВ'
              % (det, cx, a, b, c662, g, e0, EDGE_CH[det], ' / '.join('%.1f' % (e0 + g * c) for c in EDGE_CH[det])))


if __name__ == '__main__':
    main()
