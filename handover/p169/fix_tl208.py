# -*- coding: utf-8 -*-
"""Правка одной строки schemedb.ensdf_gammas: 208TL B- DECAY, 570.0 -> 583.187 (П169, 28.09.2026).

Решение Amber 28.09.2026 вопросником, дословно: «Правь базу (Рекомендую)».
Поставка ЛСРМ (ENSDF2/208.ENX, строка 272) несёт у этой линии энергию «570» с
погрешностью «15»; ENSDF (IAEA LiveChart, оценка M. J. Martin 2007) и DDEP (Surrey
2010): 583.187(2) кэВ, переход 3197.7 -> 2614.5 кэВ (уровень 3 -> 2 набора).
Интенсивность (85.2(7) поставки) не трогается.

    python fix_tl208.py <schemedb.sqlite> [--write]
"""
import sqlite3
import sys

path = sys.argv[1]
write = "--write" in sys.argv
c = sqlite3.connect(path)
row = c.execute(
    "select g.id, g.from_level_seq, g.to_level_seq, g.energy_kev, g.energy_unc"
    " from ensdf_gammas g join ensdf_datasets d on d.id = g.dataset_id"
    " where d.dsid = '208TL B- DECAY' and d.parent_nucid = '208TL'"
    " and g.from_level_seq = 3 and abs(g.energy_kev - 570.0) < 1e-9").fetchall()
done = c.execute(
    "select g.id from ensdf_gammas g join ensdf_datasets d on d.id = g.dataset_id"
    " where d.dsid = '208TL B- DECAY' and g.from_level_seq = 3 and g.to_level_seq = 2"
    " and abs(g.energy_kev - 583.187) < 1e-9").fetchall()
if done and not row:
    print("уже исправлено: строка id %d — 583.187, 3 -> 2; ничего не делаю" % done[0][0])
    sys.exit(0)
if len(row) != 1 or row[0][2] is not None or row[0][4] != "15":
    print("ОТКАЗ: ожидалась одна строка 570.0(15) из уровня 3 без конечного уровня, найдено %r" % row)
    sys.exit(2)
lv = dict(c.execute("select seq, energy_kev from ensdf_levels l join ensdf_datasets d"
                    " on d.id = l.dataset_id where d.dsid = '208TL B- DECAY'").fetchall())
print("строка id %d: %r; уровни 3 -> 2: %.3f - %.3f = %.3f кэВ"
      % (row[0][0], row[0], lv[3], lv[2], lv[3] - lv[2]))
if not write:
    print("сухой прогон")
    sys.exit(0)
c.execute("update ensdf_gammas set energy_kev = 583.187, energy_unc = '2', to_level_seq = 2"
          " where id = ?", (row[0][0],))
c.commit()
print("записано:", c.execute("select * from ensdf_gammas where id = ?", (row[0][0],)).fetchone())
