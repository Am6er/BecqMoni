# -*- coding: utf-8 -*-
"""П343: положительный контроль табличного I у tools/estar/estar.py (до и после M14).

python estar_py_ctl.py <корень дерева>
"""
import inspect
import os
import sqlite3
import sys

root = sys.argv[1]
sys.path.insert(0, os.path.join(root, 'tools', 'estar'))
import estar  # noqa: E402

db = sqlite3.connect('file:' + os.path.join(root, 'BecquerelMonitor', 'matdb.sqlite') + '?mode=ro', uri=True)
two = len(inspect.signature(estar.tabulated_potential).parameters) == 2
CASES = [
    ('Polyethylene', 'C1 H2', 0.94),
    ('Polystyrene', 'C8 H8', 1.06),
    ('Carbon, graphite', 'C1', 1.7),
    ('Activated coal (bulk)', 'C1', 0.461),
    ('Amorphous carbon', 'C1', 2.0),
    ('Water', 'H2 O1', 1.0),
    ('Water vapor', 'H2 O1', 0.000756),
    ('Polypropylene', 'C3 H6', 0.905),
    ('Benzene', 'C6 H6', 0.8787),
]
for name, formula, rho in CASES:
    m = estar.from_formula(db, name, formula, rho)
    i = estar.tabulated_potential(db, m.fractions) if two else estar.tabulated_potential(db, m.fractions, rho)
    print('%-22s rho %-9g I %s' % (name, rho, ('%.1f eV' % i) if i is not None else 'нет'))
