# -*- coding: utf-8 -*-
"""П123 (AMBER56): I по Брэггу из tools/estar/estar.py — сверка с C# EstarPotentialProbe.
   python estar_bragg.py <matdb.sqlite>"""
import sys, os, sqlite3
sys.path.insert(0, r"D:\BqMoni_Claude\p122\wt\tools\estar")
import estar
db = sqlite3.connect("file:%s?mode=ro" % sys.argv[1].replace("\\", "/"), uri=True)
for name, formula, density in [("BGO", "Bi4 Ge3 O12", 7.13), ("Lu2O3", "Lu2 O3", 9.42),
                               ("GSO", "Gd2 Si1 O5", 6.71), ("NaI", "Na1 I1", 3.667),
                               ("Water", "H2 O1", 1.0), ("Al", "Al1", 2.699), ("LaBr3", "La1 Br3", 5.08)]:
    m = estar.from_formula(db, name, formula, density)
    b = estar.bragg_potential(db, m.fractions)
    t = estar.tabulated_potential(db, m.fractions)
    print("%-6s bragg=%r table=%r" % (name, b, t))
