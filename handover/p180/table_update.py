# П180 S198: таблица света FSA (FsaLightScale) из съёмки shoot_*.txt; NaI >= 1173 кэВ — прежний ряд × отношение в 1000 кэВ
import re, glob, sys
sys.stdout.reconfigure(encoding='utf-8')
def load(prefix):
    d = {}
    for f in sorted(glob.glob(r'D:\BqMoni_Claude\p180\ls\shoot_%s_*.txt' % prefix)):
        for line in open(f, encoding='utf-8'):
            m = re.match(r'\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)', line)
            if m: d[float(m.group(1))] = (float(m.group(2)), float(m.group(3)))
    return d
p = r'D:\BqMoni_Claude\p180\wt\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs'
b = open(p, 'rb').read(); s = b.decode('utf-8-sig')
crlf = '\r\n' in s; s = s.replace('\r\n', '\n')
def old_table(name):
    i = s.index('static readonly double[][] %s =' % name); j = s.index('};', i)
    return i, j, [(float(a), float(v)) for a, v in re.findall(r'new\[\] \{ ([\d.]+), ([\d.]+) \}', s[i:j])]
nai, csi = load('nai'), load('csi')
out = {}
for name, shot, tail in (('NaITable', nai, True), ('CsITable', csi, False)):
    i, j, old = old_table(name)
    ratio = shot[1000.0][0] / dict(old)[1000.0]
    rows = []
    print('## %s: узел | было | стало | Δ %% | ±стат %%' % name)
    for e, v in old:
        near = [k for k in shot if abs(k - e) < 0.06]
        if near: nv, err = shot[near[0]]; src = ''
        elif tail and e > 1000.0: nv, err = round(v * ratio, 4), float('nan'); src = ' (прежний × %.4f)' % ratio
        else: raise SystemExit('нет узла %s в съёмке %s' % (e, name))
        rows.append((e, nv))
        print('%9s %.4f %.4f %+6.2f %5.2f%s' % (('%g' % e), v, nv, 100 * (nv / v - 1), err, src))
    out[name] = (i, j, rows)
# запись: те же узлы, по четыре в строке
for name in ('CsITable', 'NaITable'):
    i, j, rows = out[name]
    body = s[i:j]
    head = body[:body.index('{') + 1]
    cells = ['new[] { %s, %.4f }' % (('%g' % e) if e != int(e) else ('%.1f' % e), v) for e, v in rows]
    lines = ['            ' + ', '.join(cells[k:k + 4]) + ',' for k in range(0, len(cells), 4)]
    s = s[:i] + head + '\n' + '\n'.join(lines) + '\n        ' + s[j:]
if '--write' in sys.argv:
    if crlf: s = s.replace('\n', '\r\n')
    open(p, 'wb').write((b'\xef\xbb\xbf' if b[:3] == b'\xef\xbb\xbf' else b'') + s.encode('utf-8'))
    print('записано')
