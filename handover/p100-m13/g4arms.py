# -*- coding: utf-8 -*-
# П100: плечи арбитра между собой по полосам (nodelta/def, nofluct/def, killesc/def, killcarry/def) — где сидит физика,
# которой у нас нет. Читает логи П55/П92/П94 тем же разбором, что cmp100.py.
import sys, os
sys.path.insert(0, r'D:\BqMoni_Claude\p100')
import cmp100 as c
sys.stdout.reconfigure(encoding='utf-8')

def main():
    scene, e = sys.argv[1], sys.argv[2]
    data = c.load(scene, e)
    g4 = {k: v for k, v in data.items() if k.startswith('G4 ')}
    ours = {k: v for k, v in data.items() if not k.startswith('G4 ')}
    p = max(max(h) for h, n in data.values())
    ms = {k: c.measures(h, p, float(e)) for k, (h, n) in data.items()}
    ns = {k: n for k, (h, n) in data.items()}
    print('=== %s %s: плечи арбитра / def − 1, %% ===' % (scene, e))
    arms = [k for k in g4 if k != 'G4 def']
    keys = list(next(iter(ms.values())).keys())
    print('%-22s' % 'полоса' + ''.join('%22s' % a for a in arms))
    for k in keys:
        row = []
        for a in arms:
            row.append(c.cell(ms[a][k], ms['G4 def'][k], ns[a], ns['G4 def']))
        print('%-22s' % k + ''.join('%22s' % r for r in row))
    # наша сторона против плеч арбитра: ВКЛ П100 против nodelta и nofluct
    print()
    print('наша elmix1 / плечо арбитра − 1, %')
    cols = [('elmix1/def', 'наша elmix1', 'G4 def'), ('elmix1/nodelta', 'наша elmix1', 'G4 nodelta'),
            ('elmix1/nofluct', 'наша elmix1', 'G4 nofluct'), ('eltr1(П94)/nodelta', 'наша eltr194', 'G4 nodelta'),
            ('off(П94, физ.18)/killesc', 'наша off94', 'G4 killesc')]
    cols = [x for x in cols if x[1] in ms and x[2] in ms]
    print('%-22s' % 'полоса' + ''.join('%22s' % x[0] for x in cols))
    for k in keys:
        print('%-22s' % k + ''.join('%22s' % c.cell(ms[a][k], ms[b][k], ns[a], ns[b]) for _, a, b in cols))

if __name__ == '__main__':
    main()
