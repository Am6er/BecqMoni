# -*- coding: utf-8 -*-
# П100: абсолютные доли на историю по всем плечам (наши и арбитра) в трёх полосах — разложение «возврат» / «занос» / «ни того, ни другого».
import sys
sys.path.insert(0, r'D:\BqMoni_Claude\p100')
import cmp100 as c
sys.stdout.reconfigure(encoding='utf-8')
scene, e = sys.argv[1], sys.argv[2]
data = c.load(scene, e)
p = max(max(h) for h, n in data.values())
ms = {k: c.measures(h, p, float(e)) for k, (h, n) in data.items()}
bands = ['[ 0.. 25%E)', '0–50 кэВ', '0–100 кэВ', '100–200 кэВ', 'континуум']
print('=== %s %s: доли на историю ===' % (scene, e))
print('%-28s' % 'плечо' + ''.join('%14s' % b for b in bands))
for k in sorted(ms):
    print('%-28s' % k + ''.join('%14.4E' % ms[k][b] for b in bands))
g = lambda k, b: ms[k][b]
if 'G4 def' in ms and 'G4 killesc' in ms and 'G4 killcarry' in ms:
    print()
    print('G4 «ни возврата, ни заноса» ≈ killesc + killcarry − def (без члена взаимодействия):')
    print('%-28s' % 'G4 neither≈' + ''.join('%14.4E' % (g('G4 killesc', b) + g('G4 killcarry', b) - g('G4 def', b)) for b in bands))
    print('%-28s' % 'G4 возврат (def−killesc)' + ''.join('%14.4E' % (g('G4 def', b) - g('G4 killesc', b)) for b in bands))
    print('%-28s' % 'G4 занос (def−killcarry)' + ''.join('%14.4E' % (g('G4 def', b) - g('G4 killcarry', b)) for b in bands))
for a, b0 in (('наша elmix1_detour0', 'наша off_detour094'), ('наша eltr1_detour094', 'наша off_detour094')):
    if a in ms and b0 in ms:
        print('%-28s' % ('наш возврат ' + a[5:]) + ''.join('%14.4E' % (g(a, b) - g(b0, b)) for b in bands))
for a, b0 in (('наша elmix1', 'наша elmix1_detour0'), ('наша eltr194', 'наша eltr1_detour094')):
    if a in ms and b0 in ms:
        print('%-28s' % ('наш занос ' + a[5:]) + ''.join('%14.4E' % (g(a, b) - g(b0, b)) for b in bands))
