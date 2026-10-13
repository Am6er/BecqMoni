# -*- coding: utf-8 -*-
import io, re
gen = io.open(r"D:\BqMoni_Claude\p123\art\gen_cs_after.txt", encoding="utf-8").read().replace("\r\n", "\n")
path = r"D:\BqMoni_Claude\p122\wt\BecquerelMonitor\EfficiencyMaker\ElectronData.cs"
src = io.open(path, encoding="utf-8", newline="").read()
assert "\r\n" in src
s = src.replace("\r\n", "\n")
parts = [p for p in re.split(r"(?=        static readonly Material \w+ = new Material\n)", gen) if p.strip()]
new_gso = [p for p in parts if p.startswith("        static readonly Material Gso ")][0].rstrip("\n") + "\n"
m = re.search(r"        static readonly Material Gso = new Material\n.*?\n        \};\n", s, re.S)
assert m, "нет блока Gso"
old_gso = m.group(0)
assert old_gso != new_gso
s = s.replace(old_gso, new_gso)
note_old = u"""    /// Остальные шесть (CeBr₃, SrI₂, CdTe, CZT, GSO, Ge) посчитаны своей
    /// реализацией того же алгоритма — `tools/estar/estar.py`, входные данные
    /// из `matdb.sqlite`. Она сверена с четырьмя веществами выше: расхождение
    /// не хуже 0.05 % и по пробегу, и по выходу. До этого `Match` возвращала
    /// у них null, и поправка на тормозное не считалась вовсе.
"""
note_new = u"""    /// Остальные шесть (CeBr₃, SrI₂, CdTe, CZT, GSO, Ge) посчитаны своей
    /// реализацией того же алгоритма — `tools/estar/estar.py`, входные данные
    /// из `matdb.sqlite`. Она сверена с четырьмя веществами выше: расхождение
    /// не хуже 0.05 % и по пробегу, и по выходу. До этого `Match` возвращала
    /// у них null, и поправка на тормозное не считалась вовсе.
    /// ⚠ Блок GSO ПЕРЕГЕНЕРИРОВАН 22.09.2026 (`AMBER56`, П123, тем же
    /// `tools/estar/gen_cs.py`): правило Брэгга стало брать кислороду
    /// потенциал «в соединении» 106 эВ вместо элементных 95, I у GSO 395.8 →
    /// 405.5 эВ, пробег CSDA +0.3…0.5 %; у остальных восьми блоков генератор
    /// дал прежние байты (у них либо табличное I, либо лёгких элементов нет).
"""
assert s.count(note_old) == 1
s = s.replace(note_old, note_new)
with io.open(path, "w", encoding="utf-8", newline="") as f:
    f.write(s.replace("\n", "\r\n"))
print("ok: заменён блок Gso, %d -> %d байт" % (len(old_gso), len(new_gso)))
