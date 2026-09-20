# -*- coding: utf-8 -*-
"""
П90 (S176). Сверка долей совпадений SandiaDecay (`sandia.decay.xml`, то, что лежит в
`nucdb.gamma_coincidence`) со схемой уровней PhotonEvaporation (`schemedb.g4_gamma`).

Для каждой пары (A -> B, fraction) внутри перехода parent -> child:
  * переход A ищется в схеме дочернего ядра по энергии (допуск MATCH_KEV);
  * от его конечного уровня спускаемся вниз: P(t | уровень) = I_t(1+alpha_t)/sum I(1+alpha),
    P(gamma_t | уровень) = I_t / sum I(1+alpha);
  * P(B|A) = sum по уровням k достижимым: P(достичь k) * P(gamma_B | k) для выходов k
    с энергией B.
Печатает по родителю: число пар, медиану отношения Sandia/схема, разброс, и строки
с отношением вне [0.9, 1.1] (только для названных родителей).

Ничего не пишет в базы.
"""
import sys, os, sqlite3, statistics
import os as _os
REPO = _os.path.normpath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), '..', '..'))
import xml.etree.ElementTree as ET
from collections import defaultdict

for _s in (sys.stdout, sys.stderr):
    try: _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception: pass

XML = r'C:\Users\moroz\App\InterSpec\data\sandia.decay.xml'
SCHEME = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] else _os.path.join(REPO, 'BecquerelMonitor', 'schemedb.sqlite')
WANT = [a for a in sys.argv[2:]]  # символы Sandia: Eu152 Lu176 ...
MATCH_KEV = 0.3
NS = '{sandia.decay.xsd}'

ELEMENTS = ['n','H','He','Li','Be','B','C','N','O','F','Ne','Na','Mg','Al','Si','P','S','Cl','Ar','K','Ca',
            'Sc','Ti','V','Cr','Mn','Fe','Co','Ni','Cu','Zn','Ga','Ge','As','Se','Br','Kr','Rb','Sr','Y','Zr',
            'Nb','Mo','Tc','Ru','Rh','Pd','Ag','Cd','In','Sn','Sb','Te','I','Xe','Cs','Ba','La','Ce','Pr','Nd',
            'Pm','Sm','Eu','Gd','Tb','Dy','Ho','Er','Tm','Yb','Lu','Hf','Ta','W','Re','Os','Ir','Pt','Au','Hg',
            'Tl','Pb','Bi','Po','At','Rn','Fr','Ra','Ac','Th','Pa','U','Np','Pu','Am','Cm','Bk','Cf','Es','Fm',
            'Md','No','Lr','Rf','Db','Sg','Bh','Hs','Mt','Ds','Rg','Cn','Nh','Fl','Mc','Lv','Ts','Og']
ZOF = {s: i for i, s in enumerate(ELEMENTS)}

def split_symbol(sym):
    # Sm152, Ba137m, Ag108m
    i = 0
    while i < len(sym) and sym[i].isalpha(): i += 1
    el = sym[:i]; rest = sym[i:]
    j = 0
    while j < len(rest) and rest[j].isdigit(): j += 1
    return ZOF.get(el), int(rest[:j]) if rest[:j] else None, rest[j:]

con = sqlite3.connect('file:%s?mode=ro' % SCHEME.replace('\\', '/'), uri=True)
_scheme_cache = {}
def scheme(z, a):
    key = (z, a)
    if key in _scheme_cache: return _scheme_cache[key]
    rows = con.execute('select from_seq, idx, to_seq, energy_ev/1000.0, intensity_ppm/1e6, icc_total '
                       'from g4_gamma where z=? and a=? and intensity_ppm>0', (z, a)).fetchall()
    exits = defaultdict(list)   # from_seq -> [(to_seq, E, Igamma, alpha)]
    for fs, idx, ts, e, ig, al in rows:
        if al is None or al < 0: al = 0.0
        if al > 1e4: al = 1e4
        exits[fs].append((ts, e, ig, al))
    _scheme_cache[key] = exits
    return exits

def descend_probs(exits, start):
    """P(достичь уровня k | стоим на start) для всех k ниже."""
    reach = defaultdict(float); reach[start] = 1.0
    order = sorted([s for s in exits.keys()] + [start], reverse=True)
    seen = set()
    for lv in order:
        if lv in seen: continue
        seen.add(lv)
        p = reach.get(lv, 0.0)
        if p <= 0 or lv not in exits: continue
        tot = sum(ig * (1 + al) for _, _, ig, al in exits[lv])
        if tot <= 0: continue
        for ts, e, ig, al in exits[lv]:
            reach[ts] += p * ig * (1 + al) / tot
    return reach

def p_b_given_a(exits, ea, eb):
    """Все кандидаты A по энергии; возвращает список (from_seq, P(B|A))."""
    out = []
    for fs, lst in exits.items():
        for ts, e, ig, al in lst:
            if abs(e - ea) < MATCH_KEV:
                reach = descend_probs(exits, ts)
                pb = 0.0
                for lv, p in reach.items():
                    if p <= 0 or lv not in exits: continue
                    tot = sum(ig2 * (1 + al2) for _, _, ig2, al2 in exits[lv])
                    if tot <= 0: continue
                    for ts2, e2, ig2, al2 in exits[lv]:
                        if abs(e2 - eb) < MATCH_KEV:
                            pb += p * ig2 / tot
                out.append((fs, e, pb))
    return out

if __name__ == '__main__':
    root = ET.parse(XML).getroot()
    summary = []
    for tr in root.iter(NS + 'transition'):
        parent = tr.get('parent'); child = tr.get('child')
        if WANT and parent not in WANT: continue
        if not child: continue
        z, a, st = split_symbol(child)
        if z is None or a is None: continue
        gs = [g for g in tr if g.tag == NS + 'gamma']
        byid = {g.get('id'): g for g in gs if g.get('id')}
        exits = scheme(z, a)
        if not exits: continue
        ratios = []; lines = []
        for g in gs:
            ea = float(g.get('energy')); ia = float(g.get('intensity'))
            for c in g:
                if c.tag != NS + 'coincidentGamma': continue
                o = byid.get(c.get('id'))
                if o is None: continue
                eb = float(o.get('energy')); f = float(c.get('intensity'))
                if f < 0.001 or ia * float(tr.get('branchRatio') or 0) < 0.001: continue
                cands = p_b_given_a(exits, ea, eb)
                cands = [c_ for c_ in cands if c_[2] > 0]
                if not cands: continue
                # берём кандидата с НАИМЕНЬШИМ уровнем (как MatchTransition), затем ближайший
                cands.sort(key=lambda c_: (c_[0], abs(c_[1] - ea)))
                fs, e, pb = cands[0]
                # но если у другого кандидата P ближе к Sandia — отметим лучшее
                best = min(cands, key=lambda c_: abs(c_[2] - f))
                r = f / pb
                ratios.append(r)
                lines.append((ea, eb, f, pb, r, fs, best[2]))
        if not ratios: continue
        med = statistics.median(ratios)
        summary.append((parent, child, tr.get('branchRatio'), len(ratios), med, min(ratios), max(ratios)))
        if WANT:
            print('=== %s -> %s BR=%s  пар=%d  медиана Sandia/схема=%.4f  min=%.4f max=%.4f'
                  % (parent, child, tr.get('branchRatio'), len(ratios), med, min(ratios), max(ratios)))
            for ea, eb, f, pb, r, fs, alt in sorted(lines, key=lambda t: -f):
                flag = '' if 0.9 <= r <= 1.1 else '  <-- '
                print('   %9.3f -> %9.3f  Sandia=%.6f  схема=%.6f  отн=%.4f  (уровень A с seq %d; лучший кандидат %.6f)%s'
                      % (ea, eb, f, pb, r, fs, alt, flag))

    if not WANT:
        print('родитель;дочерний;BR;пар;медиана;min;max')
        for s in summary:
            print('%s;%s;%s;%d;%.4f;%.4f;%.4f' % s)
        meds = [s[4] for s in summary]
        print('переходов %d; медиана отношений вне [0.9,1.1]: %d' % (len(summary), sum(1 for m in meds if not 0.9 <= m <= 1.1)))
