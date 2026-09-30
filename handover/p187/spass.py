# П187: эффективность по окнам КАНАЛОВ против паспорта: S = (Σnet/Σmodel) · (разбор/паспорт)
# = данные / (модель, отнормированная на паспортную активность). Окна — в сырых каналах (peaks.py).
import csv, sys, os, re
CUR, ACT = sys.argv[1], sys.argv[2]
act = {}
for l in open(ACT, encoding='utf-8'):
    m = re.match(r'(\S+)\s.*разбор/паспорт ([0-9.]+)', l)
    if m: act[m.group(1)] = float(m.group(2))
W = {
 'Cd109': [('AgK 22', 6, 13), ('88', 29, 39)],
 'Am241': [('NpL 17', 6, 9), ('26.3', 10, 13), ('59.5', 19, 28)],
 'Ba133': [('CsK 31-35', 9, 16), ('81', 27, 36), ('356', 120, 134)],
 'Ce139': [('LaK 33-38', 10, 17), ('166', 55, 67)],
 'Eu152': [('SmK 40-46', 13, 19), ('122', 41, 51), ('344', 116, 128)],
 'Cs137': [('BaK 32-37', 10, 16), ('662', 215, 245)],
 'Co57':  [('122', 41, 51)],
}
for k in sorted(act):
    nuc = k.split('_')[1]
    if nuc not in W: continue
    p = os.path.join(CUR, k + '_curves.csv')
    if not os.path.exists(p): continue
    rows = {int(r['ch']): r for r in csv.DictReader(open(p))}
    out = []
    for lab, lo, hi in W[nuc]:
        n = sum(float(rows[c]['net']) for c in range(lo, hi + 1))
        m = sum(float(rows[c]['model']) for c in range(lo, hi + 1))
        out.append('%s: %.3f' % (lab, n / m * act[k] if m > 0 else float('nan')))
    print('%-17s разбор/паспорт %.3f | ' % (k, act[k]) + ' | '.join(out))
