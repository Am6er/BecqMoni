# -*- coding: utf-8 -*-
"""
П90 (S176). ЭТАЛОН для `CascadePairProbe`: доли пар P(B|A) ходом по схеме уровней —
независимая (питон) реализация того же правила, что в `FsaCascadeSummer.RectifyBySchemes`:

  * пары и выходы линий — из `nucdb` (`v_gamma_coincidence`, `v_gamma_coincidence_line`),
    дочерние ядра — из `decay_chain` тем же довеском уровня (`DecayParentRule.ChainLevelClause`);
  * носитель A ищется во всех схемах дочерних (`schemedb.g4_gamma`, допуск 0.6 кэВ);
  * кандидат выбирается: (1) наибольшая Σ min(f,1) по партнёрам, которых схема после него знает;
    (2) при равенстве — согласие интенсивности с сильнейшим соседом с того же уровня;
    (3) более низкий уровень; (4) ближайшая энергия;
  * P(B|A) = Σ_k reach(k)·I_B/Σ I(1+α) по достигнутым уровням, на каждом уровне — ближайший
    по энергии выход в допуске.

  python ref_pairs.py <nucdb.sqlite> <schemedb.sqlite> <out.csv> 152EU 176LU ...

Пишет `nucid;E_A;E_B;P;from_seq;z;a` (P = -1 — доля осталась поставочной). Ничего не пишет в базы.
"""
import sys, sqlite3, math
from collections import defaultdict

for _s in (sys.stdout, sys.stderr):
    try: _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception: pass

NUC, SCH, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
WANT = sys.argv[4:]
TOL = 0.6
ALPHA_CEIL = 1.0e4
TIE = 0.02   # допуск равенства покрытия, как SchemeScoreTie в C#

ELEMENTS = ['n','H','He','Li','Be','B','C','N','O','F','Ne','Na','Mg','Al','Si','P','S','Cl','Ar','K','Ca',
            'Sc','Ti','V','Cr','Mn','Fe','Co','Ni','Cu','Zn','Ga','Ge','As','Se','Br','Kr','Rb','Sr','Y','Zr',
            'Nb','Mo','Tc','Ru','Rh','Pd','Ag','Cd','In','Sn','Sb','Te','I','Xe','Cs','Ba','La','Ce','Pr','Nd',
            'Pm','Sm','Eu','Gd','Tb','Dy','Ho','Er','Tm','Yb','Lu','Hf','Ta','W','Re','Os','Ir','Pt','Au','Hg',
            'Tl','Pb','Bi','Po','At','Rn','Fr','Ra','Ac','Th','Pa','U','Np','Pu','Am','Cm','Bk','Cf','Es','Fm',
            'Md','No','Lr','Rf','Db','Sg','Bh','Hs','Mt','Ds','Rg','Cn','Nh','Fl','Mc','Lv','Ts','Og']
ZOF = {s.upper(): i for i, s in enumerate(ELEMENTS)}

def split_nucid(nucid):
    # 152EU, 137BAm, 234PAm1
    i = 0
    while i < len(nucid) and nucid[i].isdigit(): i += 1
    a = int(nucid[:i]); rest = nucid[i:]
    j = 0
    while j < len(rest) and rest[j].isalpha() and rest[j].isupper(): j += 1
    return ZOF.get(rest[:j]), a

nuc = sqlite3.connect('file:%s?mode=ro' % NUC.replace('\\', '/'), uri=True)
sch = sqlite3.connect('file:%s?mode=ro' % SCH.replace('\\', '/'), uri=True)

CHAIN_CLAUSE = (" and l_seqno = (select min(l_seqno) from decay_chain x"
                " where x.nucid = d.nucid and x.daughter_nucid = d.daughter_nucid"
                " and x.dec_type = d.dec_type)")

class Scheme:
    def __init__(self, z, a):
        self.z, self.a = z, a
        self.exits = defaultdict(list)
        for fs, ts, e, ig, al in sch.execute(
                'select from_seq, to_seq, energy_ev/1000.0, intensity_ppm/1e4, icc_total '
                'from g4_gamma where z=? and a=? and intensity_ppm>0', (z, a)):
            if al is None or not al > 0: al = 0.0
            elif al > ALPHA_CEIL: al = ALPHA_CEIL
            self.exits[fs].append((ts, e, ig, al))
        self.norm = {k: sum(ig * (1 + al) for _, _, ig, al in v) for k, v in self.exits.items()}
        self.desc = sorted(self.exits, reverse=True)
        self._reach = {}
    def count(self): return sum(len(v) for v in self.exits.values())
    def candidates(self, e):
        return [(fs, x) for fs, lst in self.exits.items() for x in lst if abs(x[1] - e) < TOL]
    def reach(self, start):
        if start in self._reach: return self._reach[start]
        r = defaultdict(float); r[start] = 1.0
        for lv in self.desc:
            if lv > start: continue
            here = r.get(lv, 0.0)
            if not here > 0: continue
            tot = self.norm[lv]
            if not tot > 0: continue
            for ts, e, ig, al in self.exits[lv]:
                if ts >= lv: continue
                r[ts] += here * ig * (1 + al) / tot
        self._reach[start] = r
        return r
    def gamma_share(self, reach, e):
        s = 0.0
        for lv, p in reach.items():
            if not p > 0 or lv not in self.exits: continue
            tot = self.norm[lv]
            if not tot > 0: continue
            best = None; bd = TOL
            for x in self.exits[lv]:
                d = abs(x[1] - e)
                if d < bd: best = x; bd = d
            if best is not None: s += p * best[2] / tot
        return s

def match_intensity(intensity, e):
    best = None; bd = TOL
    for k, v in intensity.items():
        d = abs(k - e)
        if d < bd: best = v; bd = d
    return best

rows_out = []
for nucid in WANT:
    intensity = {e: i for e, i in nuc.execute(
        "select energy_kev, intensity_pct from v_gamma_coincidence_line where nucid=? and isomer=0", (nucid,))}
    pairs = [[a, b, f] for a, b, f in nuc.execute(
        "select energy_kev, coinc_energy_kev, fraction from v_gamma_coincidence where nucid=? and isomer=0", (nucid,))]
    daughters = []
    for (name,) in nuc.execute("select distinct daughter_nucid from decay_chain d where nucid=?" + CHAIN_CLAUSE, (nucid,)):
        if not name or name.upper() == nucid.upper(): continue
        z, a = split_nucid(name)
        if z and a: daughters.append((z, a))
    schemes = [Scheme(z, a) for z, a in daughters]
    schemes = [s for s in schemes if s.count() > 0]
    by_carrier = defaultdict(list)
    for i, p in enumerate(pairs): by_carrier[p[0]].append(i)
    result = {i: (-1.0, -1, 0, 0) for i in range(len(pairs))}
    for ea, idxs in by_carrier.items():
        ia = intensity.get(ea, 0.0)
        best = None  # (scheme, reach, score, delta, from, sibling)
        for s in schemes:
            for fs, x in s.candidates(ea):
                reach = s.reach(x[0])
                score = sum(min(pairs[i][2], 1.0) for i in idxs if s.gamma_share(reach, pairs[i][1]) > 0)
                if not score > 0: continue
                sib = float('nan')
                if ia > 0 and x[2] > 0:
                    strongest = 0.0
                    for o in s.exits[fs]:
                        if o is x or not o[2] > 0: continue
                        known = match_intensity(intensity, o[1])
                        if known is None or not known > strongest: continue
                        strongest = known
                        sib = abs(math.log(known * x[2] / o[2] / ia))
                delta = abs(x[1] - ea)
                if best is None or score > best[2] * (1 + TIE): better = True
                elif score < best[2] * (1 - TIE): better = False
                elif not math.isnan(sib) and not math.isnan(best[5]) and abs(sib - best[5]) > 1e-9: better = sib < best[5]
                elif fs != best[4]: better = fs < best[4]
                else: better = delta < best[3]
                if better: best = (s, reach, score, delta, fs, sib)
        if best is None: continue
        s, reach, _, _, fs, _ = best
        for i in idxs:
            share = s.gamma_share(reach, pairs[i][1])
            if share > 0: result[i] = (share, fs, s.z, s.a)
    for i, p in enumerate(pairs):
        share, fs, z, a = result[i]
        rows_out.append('%s;%.3f;%.3f;%.12g;%d;%d;%d' % (nucid, p[0], p[1], share, fs, z, a))
    n_ok = sum(1 for i in result if result[i][0] >= 0)
    print('%s: пар %d, по схеме %d, дочерних схем %d' % (nucid, len(pairs), n_ok, len(schemes)))

with open(OUT, 'w', encoding='utf-8') as f:
    f.write('nucid;E_A;E_B;P;from_seq;z;a\n')
    for r in rows_out: f.write(r + '\n')
print('записано', len(rows_out), '->', OUT)
