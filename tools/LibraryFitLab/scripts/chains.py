# -*- coding: utf-8 -*-
"""Decay chains from nucdb.sqlite.

Same tables NucBaseFramework uses (decay_chain / decay_radiations / nuclides),
but with two corrections that matter for a library fit:

  * only ground-level parents (l_seqno = 0) are followed - rows with l_seqno > 0
    describe decays of excited/isomeric levels and duplicate the transition with
    different branching (212BI has 35.94% and 67% rows for 208TL);
  * cumulative branching from the chain root is accumulated, so intensities are
    per decay of the CHAIN PARENT (secular equilibrium). That is what the BR
    coupling in LibraryPeakFitter needs: 208TL lines must carry the 0.3594
    factor of the 212BI branch, otherwise a bound group mixing 208TL and 212BI
    lines gets wrong weights.
"""
import sqlite3
import re
import os

# База ищется относительно дерева решения, а не по абсолютному пути: раньше
# здесь стоял путь на машине автора, и у постороннего падало всё, что строит
# сеты. Переопределяется переменной окружения LFL_NUCDB.
_HERE = os.path.dirname(os.path.abspath(__file__))
_SOLUTION = os.path.normpath(os.path.join(_HERE, '..', '..', '..'))


def _find_db():
    env = os.environ.get('LFL_NUCDB')
    if env:
        return env
    for candidate in (
            os.path.join(_SOLUTION, 'BecquerelMonitor', 'nucdb.sqlite'),
            os.path.join(_SOLUTION, 'nucdb.sqlite'),
    ):
        if os.path.isfile(candidate):
            return candidate
    return os.path.join(_SOLUTION, 'BecquerelMonitor', 'nucdb.sqlite')


DB = _find_db()


def conn():
    return sqlite3.connect(DB)


def pretty(nucid):
    """208TL -> Tl-208, 234PAM1 -> Pa-234m1"""
    m = re.match(r'^(\d+)([A-Za-z]+?)(M\d*)?$', nucid)
    if not m:
        return nucid
    mass, el, iso = m.group(1), m.group(2), m.group(3) or ''
    el = el[0].upper() + el[1:].lower()
    return '%s-%s%s' % (el, mass, iso.lower())


def chain_branches(root, c, min_fraction=1e-6):
    """{nucid: cumulative branching fraction from root}, ground levels only."""
    frac = {root: 1.0}
    order = [root]
    i = 0
    while i < len(order):
        cur = order[i]
        i += 1
        # l_seqno is the level index of the parent WITHIN its own level scheme;
        # the isomer already has its own nucid (234PAm1), so the lowest level
        # present is the physical decay. Rows with a higher l_seqno duplicate the
        # transition with branching of an excited level (212BI: 35.94% at 0,
        # 67% at 5) and must not be followed.
        rows = c.execute(
            "select daughter_nucid, perc from decay_chain d where nucid = ? and perc not null "
            "and l_seqno = (select min(l_seqno) from decay_chain x where x.nucid = d.nucid "
            "               and x.daughter_nucid = d.daughter_nucid and x.dec_type = d.dec_type)",
            (cur,)).fetchall()
        for daughter, perc in rows:
            if daughter == cur:
                continue                      # 238U l_seqno-119 self loop
            try:
                p = float(perc)
            except (TypeError, ValueError):
                continue
            add = frac[cur] * p / 100.0
            if add < min_fraction:
                continue
            if daughter in frac:
                frac[daughter] += add
            else:
                frac[daughter] = add
                order.append(daughter)
            if len(order) > 100:
                return frac
    return frac


def half_life_years(nucid, c):
    row = c.execute("select half_life_sec from nuclides where nucid=? and half_life not null",
                    (nucid,)).fetchone()
    if not row or row[0] is None:
        return 0.0
    return float(row[0]) / 31536000.0


def chain_lines(root, kinds=('G',), e_min=10.0, e_max=3200.0):
    c = conn()
    frac = chain_branches(root, c)
    placeholders = ','.join('?' * len(kinds))
    out = []
    for nucid, br in frac.items():
        # Same story for the radiations table: Pa-234m1 carries its 1001.03 keV
        # line at parent_l_seqno = 2, so pin to the lowest level present rather
        # than to 0.
        rows = c.execute(
            "select energy_num, intensity_num, type_a, type_c from decay_radiations "
            "where parent_nucid = ? and type_a in (%s) "
            "and energy_num not null and intensity_num not null "
            "and parent_l_seqno = (select min(parent_l_seqno) from decay_radiations y "
            "                      where y.parent_nucid = ?)" % placeholders,
            (nucid,) + tuple(kinds) + (nucid,)).fetchall()
        hl = half_life_years(nucid, c)
        for energy, inum, ta, tc in rows:
            if energy is None or inum is None or inum <= 0:
                continue
            if energy < e_min or energy > e_max:
                continue
            out.append(dict(
                nucid=nucid, name='%s (%s)' % (pretty(nucid), pretty(root)),
                energy=float(energy), i_nuc=float(inum), branch=br,
                i_chain=float(inum) * br, half_life_y=hl,
                kind=ta, xtype=(tc or '').strip()))
    c.close()
    out.sort(key=lambda r: (r['nucid'], r['energy']))
    merged = []
    for r in out:
        if merged and merged[-1]['nucid'] == r['nucid'] and abs(merged[-1]['energy'] - r['energy']) < 0.05:
            if r['i_chain'] > merged[-1]['i_chain']:
                merged[-1] = r
            continue
        merged.append(r)
    merged.sort(key=lambda r: r['energy'])
    return merged


# Th-228 — нижняя половина ториевого ряда, от Th-228 до Tl-208. Отдельная
# цепочка, а не Th-232: аттестованный источник Th-228 не содержит Ac-228, и его
# линий 911/969/338 кэВ в спектре нет. Считать такой источник рядом Th-232
# значит записать в знаменатель recall линии, которых там быть не может.
CHAINS = {'Th-232': '232TH', 'Th-228': '228TH', 'Ra-226': '226RA',
          'U-238': '238U', 'U-235': '235U'}

# Anchor lines: strong, clean single peaks used as the gate of the set.
ANCHORS = {
    'Th-232': [2614.51],   # Tl-208, 100% of chain, top of spectrum, no neighbours
    'Th-228': [2614.51],   # тот же Tl-208: он есть и в укороченном ряду
    'Ra-226': [609.32],    # Bi-214, strongest Ra line
    'U-238': [1001.03],    # Pa-234m, classic "uranium" monopeak (Ra-free glass too)
    'U-235': [185.72],     # U-235 itself
}

if __name__ == '__main__':
    for label, root in CHAINS.items():
        lines = chain_lines(root)
        c = conn()
        fr = chain_branches(root, c)
        c.close()
        print('=== %s (%s): %d gamma lines' % (label, root, len(lines)))
        print('    members:', ', '.join('%s=%.4f' % (pretty(k), v)
                                        for k, v in sorted(fr.items(), key=lambda kv: -kv[1])))
        for r in sorted(lines, key=lambda r: -r['i_chain'])[:14]:
            print('      %8.2f keV  Ichain=%7.3f%%  Inuc=%7.3f%%  %s' % (
                r['energy'], r['i_chain'], r['i_nuc'], r['name']))
        print('    counts by I_chain threshold:',
              ' '.join('%.2f%%:%d' % (t, sum(1 for r in lines if r['i_chain'] >= t))
                       for t in (0.01, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0)))
        for a in ANCHORS[label]:
            near = [r for r in lines if abs(r['energy'] - a) < 3.0]
            print('    anchor %.2f -> %s' % (a, near[0]['name'] if near else 'NOT FOUND'))
        print()
