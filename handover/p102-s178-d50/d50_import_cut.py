# -*- coding: utf-8 -*-
"""
П102 (`D50`, 18.09.2026). ЗАМЕР цены среза импорта пар совпадений: сколько пар и какой вес
совпадений теряется тем, что `tools/nucdb/import_sandia_coincidence.py` режет по ПОСТАВОЧНОЙ
доле (`<coincidentGamma intensity>` < MIN_FRACTION = 0.001), а поставочная доля системно
занижена (`S176`/`D50`: лишний множитель на каждый пройденный уровень).

Ход, на каждый родитель Sandia (символ вида `Eu152`):
  * читается `sandia.decay.xml` ТЕМ ЖЕ правилом, что импортёр (`collect`): выходы линий
    складываются по переходам (I·branchRatio, % на распад), у пары берётся наибольшая доля;
    дочернее ядро пары запоминается (нужно для схемы);
  * P_схемы(B|A) — ходом по схеме `schemedb.g4_gamma` дочернего ядра, как в
    `handover/p90-s176/sandia_vs_scheme.py` (переход A — по энергии, допуск 0.3 кэВ, кандидат с
    наименьшим уровнем; спуск P(t|уровень) = I_t(1+α_t)/Σ I(1+α); партнёр B — по выходам
    достигнутых уровней);
  * пара классифицируется по правилу импорта (обе линии ≥ 0.1 % И доля ≥ 0.001):
      kept        — в базе;
      cut_frac    — срезана ТОЛЬКО долей (линии ≥ 0.1 %), при этом P_схемы ≥ 0.001 — ЭТО И ЕСТЬ
                    цена `D50` (при верной доле пара была бы в базе);
      cut_frac_ok — срезана долей, и P_схемы < 0.001 тоже — срез верен;
      cut_int     — срезана выходом линии (< 0.1 %) — полнота поставки, не `D50`;
      no_scheme   — переход A в схеме не найден (P_схемы нет) — считается по поставочной доле.
  * вес пары — вероятность совпадения на распад w = I_A/100 · P(B|A) (P — схемная, где есть),
    и «вес в площади сумм-пиков» w_ε = w · ε_p(A) · ε_p(B) с ОПОРНОЙ кривой ε_p сцены
    `G1S_point5` (NaI 63×63, точка 5 см), снятой с линий Eu-152 арбитра Geant4 60 млн (П101):
    ε_p(122) = 0.0553, ε_p(344) = 0.0328, ε_p(779) = 0.0149, ε_p(1408) = 0.0083 —
    кусочная степенная: E ≤ 122 → 0.0553·(E/122)^0.1 (мягкий спад к рентгену: ε_T(40) ≈ 0.053);
    122…344 → 0.0553·(E/122)^−0.50; выше 344 → 0.0328·(E/344)^−0.97. Кривая — для ДОЛЕЙ
    (отношение срезанного к оставленному), а не для абсолютных площадей.

Печатает по родителю: число пар по классам, Σw и Σw_ε по классам, долю срезанного `D50` от
оставленного (Σw_ε cut_frac / Σw_ε kept) — и список самих срезанных пар. Ничего не пишет в базы.

    python d50_import_cut.py <sandia.decay.xml> <schemedb.sqlite> <out.md> Eu152 Lu176 ...
"""
import sys, os, sqlite3, math
import xml.etree.ElementTree as ET
from collections import defaultdict

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

XML, SCHEME, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
WANT = sys.argv[4:]
NS = '{sandia.decay.xsd}'
MIN_INTENSITY = 0.1     # % на распад — как у импортёра
MIN_FRACTION = 0.001    # доля — как у импортёра
MATCH_KEV = 0.3

ELEMENTS = ['n','H','He','Li','Be','B','C','N','O','F','Ne','Na','Mg','Al','Si','P','S','Cl','Ar','K','Ca',
            'Sc','Ti','V','Cr','Mn','Fe','Co','Ni','Cu','Zn','Ga','Ge','As','Se','Br','Kr','Rb','Sr','Y','Zr',
            'Nb','Mo','Tc','Ru','Rh','Pd','Ag','Cd','In','Sn','Sb','Te','I','Xe','Cs','Ba','La','Ce','Pr','Nd',
            'Pm','Sm','Eu','Gd','Tb','Dy','Ho','Er','Tm','Yb','Lu','Hf','Ta','W','Re','Os','Ir','Pt','Au','Hg',
            'Tl','Pb','Bi','Po','At','Rn','Fr','Ra','Ac','Th','Pa','U','Np','Pu','Am','Cm','Bk','Cf','Es','Fm',
            'Md','No','Lr','Rf','Db','Sg','Bh','Hs','Mt','Ds','Rg','Cn','Nh','Fl','Mc','Lv','Ts','Og']
ZOF = {s: i for i, s in enumerate(ELEMENTS)}


def split_symbol(sym):
    i = 0
    while i < len(sym) and sym[i].isalpha():
        i += 1
    el = sym[:i]
    rest = sym[i:]
    j = 0
    while j < len(rest) and rest[j].isdigit():
        j += 1
    return ZOF.get(el), int(rest[:j]) if rest[:j] else None, rest[j:]


def eps_p(e):
    """Опорная кривая ε_p сцены G1S_point5 (см. шапку); только для долей."""
    if e <= 122.0:
        return 0.0553 * (e / 122.0) ** 0.1
    if e <= 344.0:
        return 0.0553 * (e / 122.0) ** -0.50
    return 0.0328 * (e / 344.0) ** -0.97


con = sqlite3.connect('file:%s?mode=ro' % SCHEME.replace('\\', '/'), uri=True)
_scheme_cache = {}


def scheme(z, a):
    key = (z, a)
    if key in _scheme_cache:
        return _scheme_cache[key]
    rows = con.execute('select from_seq, to_seq, energy_ev/1000.0, intensity_ppm/1e6, icc_total '
                       'from g4_gamma where z=? and a=? and intensity_ppm>0', (z, a)).fetchall()
    exits = defaultdict(list)
    for fs, ts, e, ig, al in rows:
        if al is None or al < 0:
            al = 0.0
        if al > 1e4:
            al = 1e4
        exits[fs].append((ts, e, ig, al))
    _scheme_cache[key] = exits
    return exits


_reach_cache = {}


def descend_probs(z, a, exits, start):
    key = (z, a, start)
    if key in _reach_cache:
        return _reach_cache[key]
    reach = defaultdict(float)
    reach[start] = 1.0
    order = sorted(set(list(exits.keys()) + [start]), reverse=True)
    for lv in order:
        p = reach.get(lv, 0.0)
        if p <= 0 or lv not in exits:
            continue
        tot = sum(ig * (1 + al) for _, _, ig, al in exits[lv])
        if tot <= 0:
            continue
        for ts, e, ig, al in exits[lv]:
            reach[ts] += p * ig * (1 + al) / tot
    _reach_cache[key] = reach
    return reach


def p_b_given_a(z, a, exits, ea, eb):
    """Кандидаты A по энергии → [(from_seq, P(B|A))]; None, если A в схеме нет."""
    out = []
    for fs, lst in exits.items():
        for ts, e, ig, al in lst:
            if abs(e - ea) < MATCH_KEV:
                reach = descend_probs(z, a, exits, ts)
                pb = 0.0
                for lv, p in reach.items():
                    if p <= 0 or lv not in exits:
                        continue
                    tot = sum(ig2 * (1 + al2) for _, _, ig2, al2 in exits[lv])
                    if tot <= 0:
                        continue
                    for ts2, e2, ig2, al2 in exits[lv]:
                        if abs(e2 - eb) < MATCH_KEV:
                            pb += p * ig2 / tot
                out.append((fs, pb))
    if not out:
        return None
    out.sort(key=lambda c: c[0])      # наименьший уровень — как MatchTransition
    best = out[0][1]
    if best <= 0.0:
        best = max(c[1] for c in out)  # у низшего кандидата партнёра нет — берём лучший
    return best


def collect(xml_path):
    """Как `import_sandia_coincidence.collect`, но у пары запоминается дочернее ядро."""
    root = ET.parse(xml_path).getroot()
    lines = defaultdict(lambda: defaultdict(float))
    pairs = defaultdict(dict)      # символ -> (E1,E2) -> [доля, child]
    for tr in root.iter(NS + 'transition'):
        parent = tr.get('parent')
        try:
            br = float(tr.get('branchRatio') or 0.0)
        except ValueError:
            continue
        if not parent or br <= 0.0:
            continue
        if WANT and parent not in WANT:
            continue
        gammas = [g for g in tr if g.tag == NS + 'gamma']
        if not gammas:
            continue
        by_id = {g.get('id'): g for g in gammas if g.get('id')}
        for g in gammas:
            lines[parent][float(g.get('energy'))] += 100.0 * float(g.get('intensity')) * br
        child = tr.get('child')
        for g in gammas:
            energy = float(g.get('energy'))
            for c in g:
                if c.tag != NS + 'coincidentGamma':
                    continue
                other = by_id.get(c.get('id'))
                if other is None:
                    continue
                key = (energy, float(other.get('energy')))
                fraction = float(c.get('intensity'))
                have = pairs[parent].get(key)
                if have is None or fraction > have[0]:
                    pairs[parent][key] = [fraction, child]
    return lines, pairs


def main():
    lines, pairs = collect(XML)
    out = []
    out.append('# `D50` — цена среза импорта пар (П102, 18.09.2026)')
    out.append('')
    out.append('Поставка `%s`; схема `%s`; отсечка импорта: линии ≥ %g %%, доля ≥ %g; '
               'кривая ε_p — опорная `G1S_point5` (шапка скрипта).' % (XML, SCHEME, MIN_INTENSITY, MIN_FRACTION))
    out.append('')
    out.append('| родитель | пар всего | в базе | срез долей, P_схемы ≥ 0.001 (**`D50`**) | срез долей верный | срез выходом | без схемы | Σw в базе | Σw `D50` | Σw_ε в базе | Σw_ε `D50` | доля `D50` от базы, % (по w_ε) | доля по w, % | Σw срез выходом | Σw_ε срез выходом | доля среза выходом от базы, % (по w_ε) |')
    out.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    details = []
    totals = defaultdict(float)
    order = WANT if WANT else sorted(pairs)
    for parent in order:
        if parent not in pairs:
            out.append('| %s | — | | | | | | | | | | | | | | |' % parent)
            continue
        cls_n = defaultdict(int)
        cls_w = defaultdict(float)
        cls_we = defaultdict(float)
        rows = []
        for (e1, e2), (frac, child) in sorted(pairs[parent].items()):
            i1 = lines[parent].get(e1, 0.0)
            i2 = lines[parent].get(e2, 0.0)
            z, a, _ = split_symbol(child or '')
            p_scheme = None
            if z is not None and a is not None:
                ex = scheme(z, a)
                if ex:
                    p_scheme = p_b_given_a(z, a, ex, e1, e2)
            p_use = p_scheme if p_scheme is not None else frac
            if p_use > 1.0:
                p_use = 1.0
            w = i1 / 100.0 * p_use
            we = w * eps_p(e1) * eps_p(e2)
            if i1 < MIN_INTENSITY or i2 < MIN_INTENSITY:
                cls = 'cut_int'
            elif frac < MIN_FRACTION:
                if p_scheme is None:
                    cls = 'no_scheme'
                elif p_scheme >= MIN_FRACTION:
                    cls = 'cut_frac'
                else:
                    cls = 'cut_frac_ok'
            else:
                cls = 'kept'
            cls_n[cls] += 1
            cls_w[cls] += w
            cls_we[cls] += we
            rows.append((cls, e1, e2, frac, p_scheme, i1, i2, w, we, child))
        share_we = 100.0 * cls_we['cut_frac'] / cls_we['kept'] if cls_we['kept'] > 0 else float('nan')
        share_w = 100.0 * cls_w['cut_frac'] / cls_w['kept'] if cls_w['kept'] > 0 else float('nan')
        share_int = 100.0 * cls_we['cut_int'] / cls_we['kept'] if cls_we['kept'] > 0 else float('nan')
        out.append('| %s | %d | %d | %d | %d | %d | %d | %.4e | %.4e | %.4e | %.4e | %.3f | %.3f | %.4e | %.4e | %.2f |' % (
            parent, len(rows), cls_n['kept'], cls_n['cut_frac'], cls_n['cut_frac_ok'], cls_n['cut_int'],
            cls_n['no_scheme'], cls_w['kept'], cls_w['cut_frac'], cls_we['kept'], cls_we['cut_frac'],
            share_we, share_w, cls_w['cut_int'], cls_we['cut_int'], share_int))
        for k in cls_n:
            totals['n_' + k] += cls_n[k]
        details.append('')
        details.append('## %s — срезанные долей при P_схемы ≥ 0.001 (`D50`) и сильнейшие срезанные выходом' % parent)
        details.append('')
        details.append('| класс | E_A | E_B | доля поставки | P_схемы | I_A, % | I_B, % | w = I_A·P | w_ε | дочернее |')
        details.append('|---|---|---|---|---|---|---|---|---|---|')
        shown = 0
        for r in sorted(rows, key=lambda r: -r[8]):
            cls = r[0]
            if cls == 'cut_frac' or (cls == 'cut_int' and shown < 12 and r[7] > 1e-5):
                if cls == 'cut_int':
                    shown += 1
                details.append('| %s | %.3f | %.3f | %.6f | %s | %.3f | %.3f | %.3e | %.3e | %s |' % (
                    cls, r[1], r[2], r[3], ('%.6f' % r[4]) if r[4] is not None else '—', r[5], r[6], r[7], r[8], r[9]))
    out.append('')
    out.append('Итого по перечисленным родителям: пар в базе %d, срезано долей при верной P ≥ 0.001 (`D50`) %d, '
               'срезано долей верно %d, срезано выходом линии %d, без схемы %d.' % (
                   totals['n_kept'], totals['n_cut_frac'], totals['n_cut_frac_ok'], totals['n_cut_int'], totals['n_no_scheme']))
    out.extend(details)
    with open(OUT, 'w', encoding='utf-8') as f:
        f.write('\n'.join(out) + '\n')
    print('\n'.join(out[:len(order) + 8]))


if __name__ == '__main__':
    main()
