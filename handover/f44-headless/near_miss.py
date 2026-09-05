# -*- coding: utf-8 -*-
"""Счёт мест, которые станут безоконным путём, как только ОЧЕРЕДНАЯ проба
назовёт очередное имя метода. Пользуется моделью самого сторожа, чтобы
числа были того же происхождения, что и у него.

Правило `REFLECT_OVERRIDE`: место считается безоконным, если
    тип в засеве от проб  И  тип оконный  И  ИМЯ МЕТОДА есть в литералах проб.
Первые два условия уже выполнены у целого разряда мест; их держит на плаву
ровно третье — имя метода, которого пока нет ни в одной пробе. Их и считаем.
"""
import io, os, sys, collections

ROOT = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8"
sys.path.insert(0, os.path.join(ROOT, "tools"))
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="")

import check_headless as ch

m = ch.measure()
ui, seed, lits = m["ui"], m["seed"], m["probe_literals"]

near = collections.defaultdict(list)   # (тип, метод) -> строки
live = collections.defaultdict(list)
for c in m["calls"]:
    t, meth = c["type"], c["method"]
    if t in seed and t in ui:
        if meth in lits:
            live[(t, meth)].append(c)
        else:
            near[(t, meth)].append(c)

print("Тип В ЗАСЕВЕ и ОКОННЫЙ — два условия из трёх уже выполнены.")
print("Держит только третье: имя метода ещё не встречалось в литералах проб.")
print()
print("уже сработавших (метод назван пробой): %d пар тип.метод, %d вызовов"
      % (len(live), sum(len(v) for v in live.values())))
print("НА ВОЛОСОК (метод пока не назван):      %d пар тип.метод, %d вызовов"
      % (len(near), sum(len(v) for v in near.values())))
print()
print("── разбор по типам (сколько вызовов ждёт своего имени) ──")
by_type = collections.Counter()
for (t, meth), v in near.items():
    by_type[t] += len(v)
for t, n in by_type.most_common():
    ms = sorted({meth for (tt, meth) in near if tt == t})
    print("  %-28s %3d вызовов, методов %2d: %s"
          % (t, n, len(ms), ", ".join(ms[:6]) + (" …" if len(ms) > 6 else "")))
print()
print("── самые ходовые имена методов среди них ──")
cnt = collections.Counter(meth for (t, meth) in near)
for meth, n in cnt.most_common(12):
    print("  %-32s встречается у %d оконных типов засева" % (meth, n))
print()
print("ВСЕГО оконных типов в засеве: %d" % len(seed & ui))
print("ВСЕГО живых MessageBox.Show в дереве: %d" % len(m["calls"]))
