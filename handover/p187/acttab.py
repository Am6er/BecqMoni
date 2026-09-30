# П187: таблица разбор/паспорт по плечам (act_<плечо>.txt)
import sys, re
arms = sys.argv[1:]
T = {}
for a in arms:
    for l in open(r'D:\BqMoni_Claude\p187\art\act_%s.txt' % a, encoding='utf-8'):
        m = re.match(r'(\S+)\s.*разбор/паспорт ([0-9.]+)', l)
        if m: T.setdefault(m.group(1), {})[a] = float(m.group(2))
print('%-18s' % 'спектр' + ''.join('%8s' % a for a in arms))
for k in sorted(T):
    print('%-18s' % k + ''.join('%8.3f' % T[k].get(a, float('nan')) for a in arms))
import statistics as st
for a in arms:
    v = [T[k][a] for k in T if 'Cd109' not in k and a in T[k]]
    print('%-6s медиана без Cd-109 %.3f, σ %.3f' % (a, st.median(v), st.pstdev(v)))
