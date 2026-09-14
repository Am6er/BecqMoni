# -*- coding: utf-8 -*-
"""П72 (T259): чтение `rates_<ключ>.csv` (FsaStackShot --rates=) — состав по компонентам и сверка плеч.

    python handover/p72-t258-t259/rates_compare.py <out-dir> <ключ> [<ключ2>]

Один ключ — печать состава (компонент, найден, скорость счёта 1/с, z, доля слоя %, ряд связки); два ключа —
побайтная сверка двух csv и, если разошлись, построчный дифф.
"""
import csv, io, os, sys

def read(path):
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))

def show(out, key):
    path = os.path.join(out, 'rates_%s.csv' % key)
    rows = read(path)
    meta = dict((r['name'], r['count_rate']) for r in rows if r['section'] == 'meta')
    print('== %s: live %s с, chi2/ndf %s, невязка модели %s, матрица %s' % (
        key, meta.get('live_time'), meta.get('chi2ndf'), meta.get('model_residual'), meta.get('response_matrix')))
    print('   %-10s %-8s %-4s %12s %8s %8s  %-8s %-8s' % ('компонент', 'вид', 'найд', 'счёт 1/с', 'z', 'доля %', 'связка', 'ряд'))
    for r in rows:
        if r['section'] != 'component':
            continue
        try:
            rate = float(r['count_rate']); z = float(r['z']); share = float(r['share_pct'])
        except ValueError:
            rate = z = share = float('nan')
        print('   %-10s %-8s %-4s %12.4f %8.2f %8.3f  %-8s %-8s' % (
            r['name'], r['kind'], r['detected'], rate, z, share, r['chain_root'], r['decay_chain_root']))

def main():
    out = sys.argv[1]
    keys = sys.argv[2:]
    for k in keys:
        show(out, k)
    if len(keys) == 2:
        a = open(os.path.join(out, 'rates_%s.csv' % keys[0]), 'rb').read()
        b = open(os.path.join(out, 'rates_%s.csv' % keys[1]), 'rb').read()
        if a == b:
            print('rates %s == rates %s: ПОБАЙТНО РАВНЫ (%d байт)' % (keys[0], keys[1], len(a)))
            return 0
        print('rates %s != rates %s: РАЗОШЛИСЬ' % (keys[0], keys[1]))
        al = a.decode('utf-8-sig').splitlines(); bl = b.decode('utf-8-sig').splitlines()
        for i, (x, y) in enumerate(zip(al, bl)):
            if x != y:
                print('   строка %d:\n     %s\n     %s' % (i + 1, x[:160], y[:160]))
        if len(al) != len(bl):
            print('   строк %d против %d' % (len(al), len(bl)))
        return 1
    return 0

if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try: s.reconfigure(encoding='utf-8', errors='replace')
        except Exception: pass
    sys.exit(main())
