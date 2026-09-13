# -*- coding: utf-8 -*-
# П43 (E29), контроль (а): узел <Efficiency> спектра корпуса против узла в scratch-копии,
# пересчитанной путём кривой БЕЗ ключа. Сверяются ПО СТРОКАМ XML (полная точность double):
# число узлов, Energy/Efficiency/ErrorPercent каждого, ComputeStamp. LastUpdated не судится.
# Код 0 — всё тождественно; 1 — расхождение (печатается первое).
import re, sys, io

def curve(path):
    t = io.open(path, encoding="utf-8").read()
    # ⚠ Не `<Efficiency>(.*?)</Efficiency>`: нежадный поиск обрывается на ВЛОЖЕННОМ
    # `<Efficiency>` первого узла и даёт 0 узлов — пустую приёмку «0 из 0 тождественны»
    # (поймано на первом же прогоне 13.09.2026). Узлы берутся из `<Curve>`, клеймо — из
    # `<ComputeStamp>`; в файле спектра корпуса они по одному (проверено grep -o).
    m = re.search(r"<Curve>(.*?)</Curve>", t, re.S)
    if not m:
        return None, None
    body = m.group(1)
    stamp = re.search(r"<ComputeStamp>(.*?)</ComputeStamp>", t)
    nodes = re.findall(r"<ROIEfficiencyData><Energy>(.*?)</Energy><Efficiency>(.*?)</Efficiency>"
                       r"<ErrorPercent>(.*?)</ErrorPercent></ROIEfficiencyData>", body)
    return nodes, (stamp.group(1) if stamp else None)

a, sa = curve(sys.argv[1])
b, sb = curve(sys.argv[2])
if a is None or b is None:
    print("нет узла Efficiency:", sys.argv[1] if a is None else sys.argv[2])
    sys.exit(1)
print("клеймо живое  :", sa)
print("клеймо scratch:", sb)
print("узлов живых %d, scratch %d" % (len(a), len(b)))
bad = 0
if sa != sb:
    print("КЛЕЙМО РАЗОШЛОСЬ")
    bad += 1
if len(a) != len(b):
    print("ЧИСЛО УЗЛОВ РАЗОШЛОСЬ")
    bad += 1
same = 0
for i, (x, y) in enumerate(zip(a, b)):
    if x == y:
        same += 1
    else:
        bad += 1
        if bad < 6:
            print("узел %d: живой %s, scratch %s" % (i, x, y))
print("узлов тождественных по строкам XML: %d из %d" % (same, len(a)))
if len(a) == 0:
    print("ПУСТАЯ ПРИЁМКА: узлов 0 — читатель ничего не сверил")
    bad += 1
print("ТОЖДЕСТВЕННО" if bad == 0 else "РАЗОШЛОСЬ")
sys.exit(0 if bad == 0 else 1)
