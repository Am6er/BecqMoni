# -*- coding: utf-8 -*-
"""F72. Выписать тело цикла разбора доводов у названных файлов — чтобы править глазами."""
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = "C:/Users/moroz/source/repos/BQ Eng res .NET 4.8"

LOOP = re.compile(r'(foreach\s*\(\s*(?:string|var)\s+(\w+)\s+in\s+args\s*\)|for\s*\(\s*int\s+(\w+)\s*=\s*0\s*;[^)]*args\.Length[^)]*\))')


def block(t, pos):
    d = 0
    for i in range(pos, len(t)):
        if t[i] == '{':
            d += 1
        elif t[i] == '}':
            d -= 1
            if d == 0:
                return i
    return len(t)


for rel in sys.argv[1:]:
    p = os.path.join(ROOT, rel)
    t = io.open(p, encoding='utf-8-sig', errors='replace').read()
    print("#" * 78)
    print("### " + rel)
    found = False
    for m in LOOP.finditer(t):
        found = True
        j = t.find('{', m.end())
        # цикл может быть без скобок
        nl = t.find('\n', m.end())
        if j < 0 or (nl >= 0 and j > nl and t[m.end():nl].strip()):
            seg = t[m.start(): nl if nl > 0 else m.end() + 200]
        else:
            seg = t[m.start(): block(t, j) + 1]
        line = t[:m.start()].count('\n') + 1
        print("  --- строка %d ---" % line)
        for k, ln in enumerate(seg.split('\n')):
            print("  %4d| %s" % (line + k, ln.rstrip()))
    if not found:
        print("  ЦИКЛА ПО args НЕ НАЙДЕНО")
    print()
