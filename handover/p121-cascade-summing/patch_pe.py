# -*- coding: utf-8 -*-
"""П121 (AMBER58): копия PhotonEvaporation6.1.2, в которой код смеси мультипольностей
переставлен так, чтобы МЛАДШАЯ шла первой (403 -> 304, 502 -> 205, 605 -> 506, 704 -> 607)
у переходов с ненулевым коэффициентом смешивания. Geant4 (G4GammaTransition::SampleDirection)
читает код позиционно и кладёт δ² на ВТОРОЙ компонент; δ ENSDF/PhotonEvaporation определён
как отношение СТАРШЕЙ мультипольности к младшей — на коде 403 арбитр считает не ту величину.
Переставленный код даёт арбитру ту же смесь в соглашении, которое он реально исполняет.
Переходы с δ = 0 НЕ трогаются: там первый компонент — доминирующий, и Geant4 берёт его чистым.
"""
import os, re, sys, shutil
src = r"C:\Users\moroz\source\repos\GEANT4\PhotonEvaporation6.1.2"
dst = r"D:\BqMoni_Claude\p121\pe_patched"
def order(c):  # E0=1 -> 0 (не гамма), E1,M1 -> 1, E2,M2 -> 2, ...
    return c // 2
swapped = {}
files = 0
for name in sorted(os.listdir(src)):
    s = os.path.join(src, name); d = os.path.join(dst, name)
    if not (name.startswith("z") and ".a" in name):
        shutil.copyfile(s, d); continue
    files += 1
    out = []
    with open(s, "r", encoding="ascii", errors="strict", newline="") as f:
        for line in f:
            parts = line.split()
            # строка гаммы: >= 6 колонок, первая — целый номер уровня, 4-я — код
            if len(parts) >= 6 and parts[0].isdigit() and parts[3].isdigit() and int(parts[3]) >= 100:
                code = int(parts[3]); hi, lo = code // 100, code % 100
                mixing = float(parts[4])
                if mixing != 0.0 and 2 <= hi <= 16 and 2 <= lo <= 16 and order(hi) > order(lo):
                    new = lo * 100 + hi
                    swapped[(code, new)] = swapped.get((code, new), 0) + 1
                    # замена ровно поля кода, ширина колонки сохраняется (оба трёхзначные)
                    m = list(re.finditer(r"\S+", line))[3]     # ровно 4-й токен — код
                    assert m.group(0) == str(code)
                    line = line[:m.start()] + str(new).rjust(len(m.group(0))) + line[m.end():]
            out.append(line)
    with open(d, "w", encoding="ascii", newline="") as f:
        f.writelines(out)
print("файлов схем:", files)
for (a, b), n in sorted(swapped.items()):
    print("%d -> %d: %d переходов" % (a, b, n))
print("всего переставлено:", sum(swapped.values()))
