# -*- coding: utf-8 -*-
"""П64 (AMBER29), п. 2 — три ранние съёмки против Бейтмана; п. 3 — ожидания по торону.

Ряд Rn-222 → Po-218 (3.10 мин) → Pb-214 (27.06) → Bi-214 (19.71) [→ Po-214 мгновенно]; периоды —
из `nucdb` (те же, что у разбора). Уравнения линейны, поэтому средняя по окну съёмки
активность каждого члена = сумма откликов на единичные начальные условия:

    ⟨A_X⟩(окно) = S_C·1 + B·u_X^{Rn}(окно) + P0·u_X^{Pb}(окно) + Q0·u_X^{Bi}(окно),

где B, S_C — уровень радона в угле и постоянная подставка на t₀ из фита п. 1 (модель
`fixed`: A(t) = C + B·exp(−λ_Rn t), по КАЖДОМУ нуклиду своя пара), u — отклики на
единичную начальную активность (радон с Po-218 в равновесии с ним; Pb-214; Bi-214),
считанные матричной экспонентой (`scipy.linalg.expm`) и проинтегрированные по окну.
Свободных параметров ДВА — P0 и Q0 (начальные Pb-214 и Bi-214 на t₀ из воздуха и из
роста за прокачку; начальный избыток Po-218 вырожден с P0: атом Po-218 даёт 0.115 Бк
Pb-214 на 1 Бк Po-218, см. §2 журнала); t₀ — сетка вокруг гипотезы «старт первой съёмки
− 20 мин», профиль χ²(t₀).

Вход — `activities.csv` (`collect_rates.py`): по строке на съёмку, столбцы
`key,t_start_h,live_s,<нуклид>_bq,<нуклид>_err,<нуклид>_z,<нуклид>_lim,<нуклид>_det`,
`t_start_h` — от гипотезы t₀ = 01.11.2025 12:52:52 (`T0`); и `decay_<нуклид>.csv` фита
п. 1 (строка `fixed`: B, C).

    python handover/p64-amber29/bateman.py <activities.csv> <каталог decay_*.csv> [--out=<csv>]
        [--t0-scan=-90:30:5]   # минуты относительно гипотезы, от:до:шаг

Печатает по каждой ранней съёмке измеренное и предсказанное Pb-214/Bi-214 (Бк), остатки в σ,
P0/Q0 ± σ, χ²/ndf, профиль по t₀; п. 3 — ожидаемый уход Pb-212/Bi-212/Tl-208 (T½ 10.64 ч):
отношение средних по окнам e01…e10 и ранних к окну t20m.
"""
import csv
import io
import math
import os
import sqlite3
import sys

import numpy as np
from scipy.linalg import expm

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
NUCDB = os.path.join(REPO, 'BecquerelMonitor', 'nucdb.sqlite')


def halflives():
    c = sqlite3.connect('file:' + NUCDB.replace('\\', '/') + '?mode=ro', uri=True)
    cur = c.cursor()
    hl = {}
    for n in ('222RN', '218PO', '214PB', '214BI', '212PB', '212BI', '208TL'):
        hl[n] = float(cur.execute('select half_life_sec from nuclides where nucid=? and l_seqno=0', (n,)).fetchone()[0])
    c.close()
    return hl


def chain_matrix(lams):
    n = len(lams)
    M = np.zeros((n, n))
    for i, l in enumerate(lams):
        M[i, i] = -l
        if i + 1 < n:
            M[i + 1, i] = l
    return M


def window_mean_activity(M, lams, n0, t_from, t_to):
    """Средняя активность каждого члена по окну при свободной эволюции от n0 в t = 0:
    ∫N dt = M⁻¹(e^{Mt2} − e^{Mt1}) n0 (M невырождена — все λ > 0)."""
    Minv = np.linalg.inv(M)
    integ = Minv @ (expm(M * t_to) - expm(M * t_from)) @ n0
    return lams * integ / (t_to - t_from)


def read_activities(path):
    rows = []
    with io.open(path, encoding='utf-8-sig') as fh:
        for r in csv.DictReader(fh):
            rows.append(r)
    return rows


def read_fixed(path):
    with io.open(path, encoding='utf-8-sig') as fh:
        for r in csv.DictReader(fh):
            if r['model'] == 'fixed':
                return float(r['B']), float(r['B_err']), float(r['C']), float(r['C_err'])
    raise SystemExit('нет строки fixed в ' + path)


def main():
    act_path, decay_dir = sys.argv[1], sys.argv[2]
    out = None
    scan = (-90.0, 30.0, 5.0)
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
        elif a.startswith('--t0-scan='):
            scan = tuple(float(x) for x in a[10:].split(':'))
    hl = halflives()
    lam = {k: math.log(2.0) / v for k, v in hl.items()}
    lams = np.array([lam['222RN'], lam['218PO'], lam['214PB'], lam['214BI']])
    M = chain_matrix(lams)
    rows = read_activities(act_path)
    early = [r for r in rows if r['key'] in ('coal_t20m', 'coal_t2h', 'coal_t3h')]
    early.sort(key=lambda r: float(r['t_start_h']))
    # уровень поддержки на t₀ из п. 1 — по каждому нуклиду (модель fixed: t от гипотезы t₀)
    supp = {}
    for nuc, fn in (('Pb-214', 'decay_Pb-214.csv'), ('Bi-214', 'decay_Bi-214.csv')):
        supp[nuc] = read_fixed(os.path.join(decay_dir, fn))
        print('поддержка %s на t₀: радон B = %.4g ± %.2g Бк, подставка C = %.4g ± %.2g Бк' % ((nuc,) + supp[nuc]))
    lines = ['section,key,t0_shift_min,nuclide,t_from_s,t_to_s,measured,err,predicted,residual_sigma']

    def fit_at(shift_min, verbose=False):
        """χ² при сдвиге t₀ на shift_min минут (t₀' = t₀ + shift): линейный МНК по P0, Q0."""
        y, e, U = [], [], []
        design = []
        for r in early:
            t_from = float(r['t_start_h']) * 3600.0 - shift_min * 60.0
            t_to = t_from + float(r['live_s'])
            for j, nuc in ((2, 'Pb-214'), (3, 'Bi-214')):
                v, s = float(r[nuc + '_bq']), float(r[nuc + '_err'])
                B, _, C, _ = supp[nuc]
                # радон + Po-218 в равновесии с ним на t₀ (B — в Бк на t₀ гипотезы; сдвиг t₀ — поправка на распад)
                B0 = B * math.exp(lam['222RN'] * shift_min * 60.0)
                n_rn = np.array([B0 / lams[0], B0 / lams[1], 0.0, 0.0])
                base = C + window_mean_activity(M, lams, n_rn, t_from, t_to)[j]
                uP = window_mean_activity(M, lams, np.array([0, 0, 1.0 / lams[2], 0]), t_from, t_to)[j]
                uQ = window_mean_activity(M, lams, np.array([0, 0, 0, 1.0 / lams[3]]), t_from, t_to)[j]
                y.append(v - base)
                e.append(s)
                U.append([uP, uQ])
                design.append((r['key'], nuc, t_from, t_to, v, s, base))
        y, e, U = np.array(y), np.array(e), np.array(U)
        w = 1.0 / e
        cov = np.linalg.inv(U.T @ (U * (w * w)[:, None]))
        p = cov @ (U.T @ (y * w * w))
        res = (y - U @ p) * w
        chi2 = float(res @ res)
        if verbose:
            for (key, nuc, t_from, t_to, v, s, base), rr, u in zip(design, res, U):
                pred = base + u @ p
                print('  %-9s %-6s окно %6.0f…%6.0f с: измерено %8.3f ± %6.3f, предсказано %8.3f (поддержка %7.3f + начальные %6.3f), остаток %+5.2f σ'
                      % (key, nuc, t_from, t_to, v, s, pred, base, u @ p, rr))
                lines.append('early,%s,%.1f,%s,%.0f,%.0f,%.4f,%.4f,%.4f,%.3f' % (key, shift_min, nuc, t_from, t_to, v, s, pred, rr))
        return chi2, p, np.sqrt(np.diag(cov)), len(y) - 2

    print('\n== п. 2: гипотеза t₀ = старт первой съёмки − 20 мин (сдвиг 0) ==')
    chi2, p, sp, ndf = fit_at(0.0, True)
    print('P0 (Pb-214 на t₀) = %.3f ± %.3f Бк, Q0 (Bi-214 на t₀) = %.3f ± %.3f Бк; χ²/ndf = %.2f/%d; параметров 2 (+ уровень радона и подставка из п. 1)'
          % (p[0], sp[0], p[1], sp[1], chi2, ndf))
    lines.append('fit,hyp,0,P0,,,%.4f,%.4f,,' % (p[0], sp[0]))
    lines.append('fit,hyp,0,Q0,,,%.4f,%.4f,,' % (p[1], sp[1]))
    lines.append('fit,hyp,0,chi2,,,%.3f,%d,,' % (chi2, ndf))
    print('\nпрофиль χ² по сдвигу t₀ (минуты; отрицательный — прокачка кончилась РАНЬШЕ):')
    best = None
    for sh in np.arange(scan[0], scan[1] + 1e-9, scan[2]):
        c2, pp, spp, _ = fit_at(sh)
        print('  сдвиг %+6.1f мин: χ² = %8.2f, P0 = %7.3f ± %5.3f, Q0 = %7.3f ± %5.3f' % (sh, c2, pp[0], spp[0], pp[1], spp[1]))
        lines.append('scan,,%.1f,,,,%.4f,%.4f,%.4f,%.3f' % (sh, pp[0], pp[1], c2, 0.0))
        if best is None or c2 < best[0]:
            best = (c2, sh, pp, spp)
    print('минимум: сдвиг %+.1f мин, χ² %.2f (гипотеза: %.2f)' % (best[1], best[0], chi2))
    if abs(best[1] - scan[0]) < 1e-9 or abs(best[1] - scan[1]) < 1e-9:
        print('⚠ минимум на краю сетки — t₀ этими данными не определяется')

    # ---- п. 3: торон — ожидаемый уход
    print('\n== п. 3: торон из воздуха, ожидание ухода (Pb-212 T½ = %.2f ч, Bi-212 %.1f мин, Tl-208 %.2f мин) ==' % (hl['212PB'] / 3600, hl['212BI'] / 60, hl['208TL'] / 60))
    lt = np.array([lam['212PB'], lam['212BI'], lam['208TL']])
    Mt = chain_matrix(lt)
    ref = [r for r in rows if r['key'] == 'coal_t20m'][0]
    t_ref = (float(ref['t_start_h']) * 3600.0, float(ref['t_start_h']) * 3600.0 + float(ref['live_s']))
    for r0 in (0.0, 0.5, 1.0):
        n0 = np.array([1.0 / lt[0], r0 / lt[1], r0 * 0.3594 / lt[2]])
        a_ref = window_mean_activity(Mt, lt, n0, *t_ref)
        print('  Bi-212/Pb-212 на t₀ = %.1f: Pb-212 ⟨окно⟩/⟨t20m⟩ и Bi-212/Pb-212 (физ.) по съёмкам:' % r0)
        for key in ('coal_t20m', 'coal_t2h', 'coal_t3h', 'coal_e01', 'coal_e02', 'coal_e03', 'coal_e05', 'coal_e10'):
            rr = [x for x in rows if x['key'] == key][0]
            tf = float(rr['t_start_h']) * 3600.0
            a = window_mean_activity(Mt, lt, n0, tf, tf + float(rr['live_s']))
            print('    %-9s Pb-212 %.4f   Bi-212/Pb-212 %.3f   Tl-208/Bi-212 %.3f (в ед. FSA %.3f)'
                  % (key, a[0] / a_ref[0], a[1] / a[0], a[2] / a[1], a[2] / a[1] / 0.3594))
            lines.append('thoron,%s,%.1f,r0=%.1f,%.0f,%.0f,%.5f,%.4f,%.4f,' % (key, 0.0, r0, tf, tf + float(rr['live_s']), a[0] / a_ref[0], a[1] / a[0], a[2] / a[1]))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
