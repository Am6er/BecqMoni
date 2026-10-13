# -*- coding: utf-8 -*-
"""Читатель ЗАГОЛОВКА .rmx (П110, 19.09.2026) — по раскладке
BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs (Load / ReadOptions / PeekVersions):
  "BQRM" | int32 format | BinaryReader.ReadString stamp (7-бит длина + UTF-8) |
  double BinKev | int32 Histories | int64 CreatedUtc(ticks) | double BuildSeconds |
  Options: double emin, double emax, int32 nodes, double bin, int32 hist,
           8 x bool (xray, coh, brem, scat, npl, acont, bound, bremsb), int32 seed |
  тело: int32 nodes, nodes x double energies, int32 channels, ...
Ничего не пишет. Печатает CSV на stdout.
Использование: python rmx_header.py <каталог с .rmx>
"""
import io, os, struct, sys, glob, datetime, hashlib

def read7bit(f):
    n = 0; shift = 0
    while True:
        b = f.read(1)[0]
        n |= (b & 0x7F) << shift
        if not (b & 0x80):
            return n
        shift += 7

def parse(path):
    with open(path, 'rb') as fh:
        data = fh.read()
    f = io.BytesIO(data)
    magic = f.read(4)
    if magic != b'BQRM':
        return {'file': os.path.basename(path), 'error': 'not BQRM: %r' % magic}
    fmt, = struct.unpack('<i', f.read(4))
    n = read7bit(f)
    stamp = f.read(n).decode('utf-8')
    binkev, hist, ticks, build_s = struct.unpack('<diqd', f.read(8 + 4 + 8 + 8))
    emin, emax, nodes_opt, bin_opt, hist_opt = struct.unpack('<ddidi', f.read(8 + 8 + 4 + 8 + 4))
    flags = struct.unpack('<8?', f.read(8))
    # формат 6 — без зерна (комментарий у FormatVersion: «7 — появилось ЗЕРНО, файл длиннее на четыре байта»)
    seed = struct.unpack('<i', f.read(4))[0] if fmt >= 7 else ''
    nodes, = struct.unpack('<i', f.read(4))
    energies = struct.unpack('<%dd' % nodes, f.read(8 * nodes))
    channels, = struct.unpack('<i', f.read(4))
    # пропустить тело: channels x nodes x (int32 len + len x float32)
    for c in range(channels):
        for i in range(nodes):
            ln, = struct.unpack('<i', f.read(4))
            f.seek(4 * ln, 1)
    body_end = f.tell()
    tag = f.read(4)
    has_angk = (tag == b'ANGK')
    tags = []
    if has_angk:
        qn, = struct.unpack('<i', f.read(4))
        f.seek(qn * (10 * 8 + 8), 1)   # 10 double + int64 Histories на узел (Load, блок ANGK)
        tags.append('ANGK(%d)' % qn)
    else:
        f.seek(-4, 1)
    # первый хвост NOIS (T46): достигнутый шум и потраченные истории
    noise_rel = ''; hist_spent = ''; hist_worst = ''; cpu_s = ''; par = ''
    nxt = f.read(4)
    if nxt == b'NOIS':
        noise_rel, noise_w, hist_spent, hist_worst = struct.unpack('<ddqq', f.read(32))
        noise_rel = round(noise_rel, 3)   # уже в процентах (ContinuumRelativeError, %)
        # NodeHistories (int32 n + n int64), NodeErrors (int32 n + n double), NodeSeconds (int32 n + n double)
        k, = struct.unpack('<i', f.read(4)); f.seek(8 * k, 1)
        k, = struct.unpack('<i', f.read(4)); f.seek(8 * k, 1)
        k, = struct.unpack('<i', f.read(4)); node_s = struct.unpack('<%dd' % k, f.read(8 * k))
        cpu_s = round(sum(node_s), 1)                       # ЦП-секунд по узлам
        par = round(cpu_s / build_s, 1) if build_s > 0 else ''   # достигнутый параллелизм
        tags.append('NOIS')
    elif nxt:
        tags.append(nxt.decode('ascii', 'replace'))
    phys = 0
    if stamp.startswith('phys='):
        e = stamp.find(';')
        try:
            phys = int(stamp[5:e])
        except ValueError:
            phys = 0
    created = datetime.datetime(1, 1, 1) + datetime.timedelta(microseconds=ticks // 10)
    return {
        'file': os.path.basename(path),
        'size': len(data),
        'mtime': datetime.datetime.fromtimestamp(os.path.getmtime(path)).strftime('%Y-%m-%d %H:%M'),
        'created_utc': created.strftime('%Y-%m-%d %H:%M'),
        'format': fmt,
        'phys': phys,
        'has_ANGK': int(has_angk),
        'nodes': nodes,
        'channels': channels,
        'emin': emin, 'emax': emax, 'bin_kev': binkev,
        'histories_per_node': hist,
        'build_seconds': round(build_s, 1),
        'xray': int(flags[0]), 'coh': int(flags[1]), 'brem': int(flags[2]), 'scat': int(flags[3]),
        'npl': int(flags[4]), 'acont': int(flags[5]), 'bound': int(flags[6]), 'bremsb': int(flags[7]),
        'seed': seed,
        'noise_rel_pct': noise_rel,
        'hist_spent': hist_spent,
        'hist_worst_node': hist_worst,
        'cpu_seconds': cpu_s,
        'parallelism': par,
        'tails_first': '/'.join(tags),
        'sha256': hashlib.sha256(data).hexdigest()[:16],
        'stamp': stamp.replace(';', '|'),   # разделитель CSV — «;», в клейме он заменён на «|»
    }

def main():
    d = sys.argv[1]
    cols = ['file','size','mtime','created_utc','format','phys','has_ANGK','nodes','channels','emin','emax',
            'bin_kev','histories_per_node','build_seconds','xray','coh','brem','scat','npl','acont','bound',
            'bremsb','seed','noise_rel_pct','hist_spent','hist_worst_node','cpu_seconds','parallelism','tails_first','sha256','stamp']
    print(';'.join(cols))
    for p in sorted(glob.glob(os.path.join(d, '*.rmx'))):
        r = parse(p)
        if 'error' in r:
            print(r['file'] + ';' + r['error']); continue
        print(';'.join(str(r.get(c, '')) for c in cols))

if __name__ == '__main__':
    main()
