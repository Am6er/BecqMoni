# П70 (AMBER30): таблица бисекции по дампам FsaStackShot --dump= (кривые по каналам).
# Полосы: 56–100 кэВ (Pb K-рентген 72.8/75.0/85 кэВ) и 5–20 кэВ (левый край шкалы, Pb L).
import csv, os, sys

lane = r'D:\BqMoni_Claude\p70\out'
points = ['27ba5c4b', '32eaeb09', '4851f51f', 'a6f2b227', '025a65a9']
if len(sys.argv) > 1:
    points = sys.argv[1:]


def band(rows, lo, hi, col):
    return sum(float(r[col]) for r in rows if lo <= float(r['keV']) <= hi and r.get(col) not in (None, ''))


def peak(rows, lo, hi, col):
    best = None
    for r in rows:
        k = float(r['keV'])
        if lo <= k <= hi and r.get(col):
            v = float(r[col])
            if best is None or v > best[1]:
                best = (k, v)
    return best


print('| коммит | χ²/ndf | Xray-Pb доля % | Xray-Pb слой: макс отсч./кан. @ кэВ | Xray-Pb-L доля % | Σ 56–100 кэВ: данные / модель / хвост (отсч.) | данные−модель 56–100 | Σ 5–20 кэВ: данные / модель / хвост | данные−модель 5–20 |')
print('|---|---|---|---|---|---|---|---|---|')
for p in points:
    d = os.path.join(lane, p)
    log = open(os.path.join(d, 'shot.log'), encoding='utf-8', errors='replace').read().splitlines()
    chi = next((l.split()[1].rstrip(',') for l in log if l.startswith('chi2/ndf')), '?')
    rows_share = {}
    for l in log:
        if l.startswith('ROW\t'):
            c = l.split('\t')
            rows_share[c[1]] = c[3]
    rows = list(csv.DictReader(open(os.path.join(d, 'curves.csv'), encoding='utf-8')))
    has_tail = 'untied_tail' in rows[0]
    net56, mod56 = band(rows, 56, 100, 'net'), band(rows, 56, 100, 'model')
    tail56 = band(rows, 56, 100, 'untied_tail') if has_tail else 0.0
    net5, mod5 = band(rows, 5, 20, 'net'), band(rows, 5, 20, 'model')
    tail5 = band(rows, 5, 20, 'untied_tail') if has_tail else 0.0
    pk = peak(rows, 40, 110, 'Xray-Pb')
    print('| `%s` | %s | %s | %.0f @ %.1f | %s | %.0f / %.0f / %s | %.0f (%.1f %%) | %.0f / %.0f / %s | %.0f (%.1f %%) |' % (
        p, chi, rows_share.get('Xray-Pb', '?'), pk[1], pk[0], rows_share.get('Xray-Pb-L', '?'),
        net56, mod56, ('%.0f' % tail56) if has_tail else 'столбца нет',
        net56 - mod56, 100.0 * (net56 - mod56) / net56,
        net5, mod5, ('%.0f' % tail5) if has_tail else 'столбца нет',
        net5 - mod5, 100.0 * (net5 - mod5) / net5 if net5 else 0.0))

# слой Xray-Pb по каналам — одинаков ли между коммитами (амплитуда образа)
print()
print('Слой Xray-Pb 60–100 кэВ, отсч./кан. (шаг 6 каналов):')
hdr = None
tab = {}
for p in points:
    rows = list(csv.DictReader(open(os.path.join(lane, p, 'curves.csv'), encoding='utf-8')))
    for r in rows:
        k = float(r['keV'])
        if 60 <= k <= 100 and int(r['ch']) % 6 == 0:
            tab.setdefault('%.1f' % k, []).append(float(r['Xray-Pb']))
print('кэВ\t' + '\t'.join(points))
for k, v in tab.items():
    print(k + '\t' + '\t'.join('%.1f' % x for x in v))
