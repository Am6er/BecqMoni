# -*- coding: utf-8 -*-
"""Сгенерировать блоки Material для ElectronData.cs. Руками числа не набирать."""
import sqlite3, sys, estar

NAMES = {"CeBr3": "CeBr3", "SrI2": "SrI2", "CdTe": "CdTe",
         "CZT": "Czt", "GSO": "Gso", "Ge": "Ge"}

def rows(values, per=6, indent=16):
    out = []
    for i in range(0, len(values), per):
        out.append(" " * indent + ", ".join("%.3E" % v for v in values[i:i + per]))
    return ",\n".join(out)

db = sqlite3.connect(sys.argv[1])
for name, formula, density in estar.WANTED:
    rg, yield_, used = estar.compute(db, name, formula, density)
    print("""        static readonly Material %s = new Material
        {
            Name = "%s",
            Energy = Grid,
            Range = new double[]
            {
%s
            },
            Yield = new double[]
            {
%s
            }
        };
""" % (NAMES[name], name, rows(rg), rows(yield_)))
