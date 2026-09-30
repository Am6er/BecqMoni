# П187: центры пиков в СЫРЫХ каналах (центр тяжести ±w каналов вокруг максимума в окне каналов), без калибровок
import sys
sys.path.insert(0, r'D:\BqMoni_Claude\p187')
from low import load
def cen(d, lo, hi, w=2):
    i = max(range(lo, hi + 1), key=lambda c: d[c])
    s = sum(d[c] for c in range(i - w, i + w + 1))
    return sum(c * d[c] for c in range(i - w, i + w + 1)) / s, i
for arg in sys.argv[1:]:
    k, *wins = arg.split(':')
    cal, d, lt = load(k)
    E = lambda c: sum(a * c ** j for j, a in enumerate(cal))
    out = []
    for w in wins:
        lo, hi = map(int, w.split('-'))
        c, i = cen(d, lo, hi)
        out.append('[%d-%d] ch %.2f (файл %.1f кэВ)' % (lo, hi, c, E(c)))
    print('%-18s ' % k + '  '.join(out))
