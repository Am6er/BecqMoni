import io, re, sys, os
art = 'handover/p48-t257'
keys = ('шкала:', 'chi2/ndf', 'отсев по значимости', 'ROW\t', 'CUT\t', 'LIB\t', 'состав объявлен', 'отчёт: строк', 'SETUP\tпротив')
def lines(p):
    with io.open(p, encoding='utf-8', newline='') as f:
        return [l.rstrip('\r\n') for l in f if l.startswith(keys)]
out = []
allsame = True
for s in ('G1S16_Mix_Mar', 'G1S16_Mix_Denta100', 'G1S16_Mix_Petri'):
    b = lines(f'{art}/{s}_before.log'); a = lines(f'{art}/{s}_after.log'); w = lines(f'{art}/{s}_after_withfile.log')
    same_ba = b == a; same_aw = a == w
    allsame &= same_ba and same_aw
    chi = [l for l in a if l.startswith('chi2')][0]
    rows = '; '.join(l.split('\t')[1] + ' ' + l.split('\t')[3] for l in a if l.startswith('ROW'))
    out.append(f'| `{s}` | {len(b)} | {"тождественны" if same_ba else "РАСХОДЯТСЯ"} | {"тождественны" if same_aw else "РАСХОДЯТСЯ"} | {chi.split(",")[0].replace("chi2/ndf ","")} | {rows} |')
    if not same_ba:
        for x, y in zip(b, a):
            if x != y: out.append(f'    до:    {x}\n    после: {y}')
print('| спектр | строк сверено | до (HEAD-exe, файл есть) = после (мой exe, файла нет) | после = после при файле | χ²/ndf | доли слоёв, % |')
print('|---|---|---|---|---|---|')
print('\n'.join(out))
print('ВСЕ ТОЖДЕСТВЕННЫ' if allsame else 'ЕСТЬ РАСХОЖДЕНИЯ')
sys.exit(0 if allsame else 1)
