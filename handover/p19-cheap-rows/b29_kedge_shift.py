# -*- coding: utf-8 -*-
"""`B29`: оценка сдвига центроида K-группы рентгена при взвешивании на эффективность.

Читает ТОЛЬКО базы (`mode=ro`): линии `X` из `nucdb.decay_radiations` тем же
отбором, что `build_corpus.xray_lines` (intensity > 0.5, E >= 5 кэВ, зажим
уровня родителя из `chains.LEVEL_CLAUSE`), сечения XCOM и K-флуоресценцию из
`matdb`. Ничего не пишет.

Модель эффективности пика полного поглощения на энергии E (по порядку величины):

    eps(E) = T_win(E) * (1 - P_esc(E)) * (1 - exp(-mu(E) * t))

  * T_win — пропускание входного окна (Al заданной толщины, ключ --al=мм);
  * P_esc — вероятность вылета K-рентгена ИОДА (или Cs) назад через входную
    грань у полубесконечного кристалла при нормальном падении:
        P_esc = (tau/mu) * k_frac * omega_K * 1/2 * [1 - (mu_f/mu) * ln(1 + mu/mu_f)]
    суммой по ветвям Ka/Kb; ниже K-края кристалла P_esc = 0;
  * последний множитель для t >= 10 мм равен 1 по обе стороны края.

Центроид группы: E_c = sum(E_i I_i eps_i) / sum(I_i eps_i) против табличного
E_c0 = sum(E_i I_i) / sum(I_i). Сдвиг — в кэВ и в долях ПШПВ группы (модель
`detectors.csv`: ПШПВ = sqrt(c0 + c1 E + c2 E^2)).

Запуск:  python handover/p19-cheap-rows/b29_kedge_shift.py [--al=0.5]
"""
import csv
import io
import math
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools', 'CORPUS', 'scripts'))
for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

import chains  # noqa: E402  зажим уровня родителя — ОДИН, из приложения

NUCDB = os.path.join(ROOT, 'BecquerelMonitor', 'nucdb.sqlite')
MATDB = os.path.join(ROOT, 'BecquerelMonitor', 'matdb.sqlite')

NA = 6.02214076e23

# кристаллы: (состав {Z: массовая доля}, плотность г/см3, Z излучателей K-вылета)
CRYSTALS = {
    'NaI': ({11: 22.989771 / (22.989771 + 126.904503),
             53: 126.904503 / (22.989771 + 126.904503)}, 3.67, (53,)),
    'CsI': ({55: 132.905 / (132.905 + 126.904503),
             53: 126.904503 / (132.905 + 126.904503)}, 4.51, (53, 55)),
}
# группы корпуса, получившие опору низа (журнал B25 par. 6.4), и их кристалл
DETS = {'G1S16': 'NaI', 'G1S24': 'NaI', 'ASN16': 'NaI', 'AS80x80': 'NaI', 'RC103': 'CsI'}
# образцы строки: нуклид -> подпись K-серии
SAMPLES = [('137CS', 'Ba K'), ('133BA', 'Cs K'), ('139CE', 'La K'),
           ('109CD', 'Ag K'), ('241AM', 'Np L'), ('152EU', 'Sm/Gd K')]


def ro(path):
    return sqlite3.connect('file:%s?mode=ro' % path.replace(os.sep, '/'), uri=True)


def xcom(c, z):
    rows = c.execute('select energy_ev, coherent_b, incoherent_b, photoelectric_b '
                     'from xcom_cross_sections where z=? order by energy_ev', (z,)).fetchall()
    aw = c.execute('select atomic_weight from xcom_elements where z=?', (z,)).fetchone()[0]
    return rows, aw


def interp_b(rows, e_ev, col):
    """Лог-лог интерполяция сечения (барн) по сетке XCOM; сетка дублирует край,
    поэтому ниже края берётся нижняя ветвь, на краю и выше — верхняя."""
    lo = None
    for i, r in enumerate(rows):
        if r[0] <= e_ev:
            lo = i
        else:
            break
    if lo is None:
        return rows[0][col]
    if lo == len(rows) - 1:
        return rows[-1][col]
    a, b = rows[lo], rows[lo + 1]
    if b[0] == a[0] or a[col] <= 0 or b[col] <= 0:
        return a[col]
    t = (math.log(e_ev) - math.log(a[0])) / (math.log(b[0]) - math.log(a[0]))
    return math.exp(math.log(a[col]) * (1 - t) + math.log(b[col]) * t)


class Medium(object):
    def __init__(self, c, comp, rho):
        self.parts = []
        for z, w in comp.items():
            rows, aw = xcom(c, z)
            self.parts.append((z, w, rows, aw))
        self.rho = rho

    def mu(self, e_kev, cols=(1, 2, 3), only_z=None):
        """Линейный коэффициент, 1/см (сумма указанных столбцов; only_z — вклад
        одного элемента)."""
        tot = 0.0
        for z, w, rows, aw in self.parts:
            if only_z is not None and z != only_z:
                continue
            sig = sum(interp_b(rows, e_kev * 1e3, col) for col in cols)   # барн/атом
            tot += w * sig * 1e-24 * NA / aw
        return tot * self.rho


def escape_prob(c, med, z_em, e_kev):
    """Вероятность вылета K-рентгена элемента z_em из полубесконечного кристалла."""
    f = c.execute('select k_edge_ev, k_fraction, omega_k, ka1_ev, ka1_weight, ka2_ev, '
                  'ka2_weight, kb_ev, kb_weight from fluorescence_k where z=?',
                  (z_em,)).fetchone()
    k_edge, k_frac, omega, ka1, wa1, ka2, wa2, kb, wb = f
    if e_kev * 1e3 <= k_edge:
        return 0.0
    mu = med.mu(e_kev)
    tau_z = med.mu(e_kev, cols=(3,), only_z=z_em)      # фотопоглощение НА z_em
    p = 0.0
    wsum = wa1 + wa2 + wb
    for e_f, wgt in ((ka1 / 1e3, wa1), (ka2 / 1e3, wa2), (kb / 1e3, wb)):
        mu_f = med.mu(e_f)
        geom = 0.5 * (1.0 - (mu_f / mu) * math.log(1.0 + mu / mu_f))
        p += (wgt / wsum) * geom
    return (tau_z / mu) * k_frac * omega * p


def xlines(c, nucid):
    rows = c.execute(
        "select energy_num, intensity_num from decay_radiations "
        "where parent_nucid = $n and type_a = 'X' and energy_num not null "
        "and intensity_num not null and intensity_num > 0.5" + chains.LEVEL_CLAUSE,
        {chains.LEVEL_PARAM: nucid}).fetchall()
    return sorted((float(e), float(i)) for e, i in rows if float(e) >= 5.0)


def res_models():
    out = {}
    with io.open(os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'detectors.csv'),
                 encoding='utf-8-sig', newline='') as h:
        for r in csv.DictReader(h):
            out[r['det']] = (float(r['res_c0']), float(r['res_c1']), float(r['res_c2']))
    return out


def fwhm(coef, e):
    return math.sqrt(max(coef[0] + coef[1] * e + coef[2] * e * e, 1e-6))


def main():
    al_mm = 0.5
    for a in sys.argv[1:]:
        if a.startswith('--al='):
            al_mm = float(a.split('=', 1)[1])
    nc, mc = ro(NUCDB), ro(MATDB)
    al = Medium(mc, {13: 1.0}, 2.699)
    res = res_models()
    media = {}
    for name, (comp, rho, emit) in CRYSTALS.items():
        media[name] = (Medium(mc, comp, rho), emit)

    print('окно Al %.2f мм; кристалл полубесконечный; K-вылет назад через входную грань'
          % al_mm)
    for name, (med, emit) in media.items():
        print('== %s: mu(32 кэВ) %.1f 1/см, mu(36.5) %.1f; P_esc(32) %.3f, P_esc(36.5) %.3f; '
              'T_win(32) %.3f, T_win(36.5) %.3f'
              % (name, med.mu(32.0), med.mu(36.5),
                 sum(escape_prob(mc, med, z, 32.0) for z in emit),
                 sum(escape_prob(mc, med, z, 36.5) for z in emit),
                 math.exp(-al.mu(32.0) * al_mm / 10.0),
                 math.exp(-al.mu(36.5) * al_mm / 10.0)))
    print()
    hdr = '%-6s %-8s %-6s %7s %7s %7s | %s' % ('нуклид', 'серия', 'крист', 'E_табл', 'E_eps',
                                                'сдвиг', 'по группам: сдвиг/ПШПВ')
    print(hdr)
    print('-' * len(hdr))
    for nucid, label in SAMPLES:
        lines = xlines(nc, nucid)
        if not lines:
            print('%-6s %-8s нет линий X' % (nucid, label))
            continue
        # группа: линии в пределах ПШПВ худшего прибора (G1S24) от сильнейшей
        e_top = max(lines, key=lambda x: x[1])[0]
        grp = [(e, i) for e, i in lines if abs(e - e_top) <= fwhm(res['G1S24'], e_top)]
        e0 = sum(e * i for e, i in grp) / sum(i for _, i in grp)
        eps_last = {}
        for cname, (med, emit) in media.items():
            eps = {}
            for e, _i in grp:
                t_win = math.exp(-al.mu(e) * al_mm / 10.0)
                p_esc = sum(escape_prob(mc, med, z, e) for z in emit)
                eps[e] = t_win * (1.0 - p_esc)
            e1 = sum(e * i * eps[e] for e, i in grp) / sum(i * eps[e] for e, i in grp)
            parts = []
            for det, cn in DETS.items():
                if cn != cname:
                    continue
                parts.append('%s %+.3f' % (det, (e1 - e0) / fwhm(res[det], e0)))
            print('%-6s %-8s %-6s %7.2f %7.2f %+7.2f | %s'
                  % (nucid, label, cname, e0, e1, e1 - e0, ', '.join(parts)))
            eps_last[cname] = eps
        print('        линии: ' + ', '.join(
            '%.2f (I=%.2f; eps NaI %.2f, CsI %.2f)'
            % (e, i, eps_last['NaI'][e], eps_last['CsI'][e]) for e, i in grp))
    nc.close()
    mc.close()


if __name__ == '__main__':
    main()
