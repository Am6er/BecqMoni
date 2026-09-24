# П146 AMBER88: по снимку экрана — рёбра столбиков у вершины пика и сетка линейки
import sys
from PIL import Image
im = Image.open(sys.argv[1]).convert('RGB')
W, H = im.size
x0, x1 = 634, 1144           # полоса пика (оранжевые столбики)
top = []
for x in range(x0, x1):
    yy = None
    for y in range(80, 945):
        r, g, b = im.getpixel((x, y))
        if r > 200 and 40 < g < 110 and b < 40:   # оранжевый столбик
            yy = y
            break
    top.append(yy)
edges = [x0 + i for i in range(1, len(top)) if top[i] != top[i - 1]]
print('рёбра столбиков (x):', edges)
# сетка: вертикальные линии сетки видны в строке y=990-? берём подписи линейки по засечкам: строка y=943
row = 944
ticks = []
prev = im.getpixel((330, row))
for x in range(330, 1410):
    p = im.getpixel((x, row))
    if sum(p) > sum(prev) + 120:
        ticks.append(x)
    prev = p
print('засечки линейки (x):', ticks)
