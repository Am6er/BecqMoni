# -*- coding: utf-8 -*-
# П90: направление пар Sandia — всегда ли A (носитель) выше B в схеме; сколько пар без сопоставления.
import sys, sqlite3
import xml.etree.ElementTree as ET
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
from sandia_vs_scheme import scheme, p_b_given_a, split_symbol, NS, XML
for _s in (sys.stdout,): _s.reconfigure(encoding='utf-8', errors='replace')
root = ET.parse(XML).getroot()
n_total = n_down = n_up = n_none = n_noscheme = 0
examples_up = []; examples_none = []
for tr in root.iter(NS + 'transition'):
    parent = tr.get('parent'); child = tr.get('child')
    if not child: continue
    z, a, st = split_symbol(child)
    if z is None or a is None: continue
    br = float(tr.get('branchRatio') or 0)
    gs = [g for g in tr if g.tag == NS + 'gamma']
    byid = {g.get('id'): g for g in gs if g.get('id')}
    exits = scheme(z, a)
    for g in gs:
        ea = float(g.get('energy')); ia = float(g.get('intensity'))
        for c in g:
            if c.tag != NS + 'coincidentGamma': continue
            o = byid.get(c.get('id'))
            if o is None: continue
            eb = float(o.get('energy')); f = float(c.get('intensity'))
            ib = float(o.get('intensity'))
            if f < 0.001 or ia * br < 0.001 or ib * br < 0.001: continue
            n_total += 1
            if not exits:
                n_noscheme += 1; continue
            down = [x for x in p_b_given_a(exits, ea, eb) if x[2] > 0]
            if down:
                n_down += 1; continue
            up = [x for x in p_b_given_a(exits, eb, ea) if x[2] > 0]
            if up:
                n_up += 1
                if len(examples_up) < 15: examples_up.append((parent, child, ea, eb, f))
            else:
                n_none += 1
                if len(examples_none) < 15: examples_none.append((parent, child, ea, eb, f))
print('пар (обе линии >=0.1%%, доля >=0.001): %d' % n_total)
print('  вниз (B ниже A): %d' % n_down)
print('  только вверх (B выше A): %d' % n_up)
print('  без сопоставления в схеме: %d; схемы нет вовсе: %d' % (n_none, n_noscheme))
print('примеры «вверх»:'); [print('  ', e) for e in examples_up]
print('примеры «нет»:'); [print('  ', e) for e in examples_none]
