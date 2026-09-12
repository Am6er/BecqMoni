# -*- coding: utf-8 -*-
# П24: сверка узлов <Efficiency> спектров понятной части до (HEAD) и после CorpusEffProbe --force — точки кривой, клеймо.
import subprocess, re, io, glob, os, sys
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp24'
rows = []
for p in sorted(glob.glob(os.path.join(root, 'tools', 'CORPUS', 'corpus', 'spectra', '*.xml'))):
    rel = os.path.relpath(p, root).replace('\\', '/')
    new = io.open(p, encoding='utf-8-sig').read()
    if '<Efficiency><Guid>' not in new:
        continue
    old = subprocess.run(['git', 'show', 'HEAD:' + rel], capture_output=True, cwd=root).stdout.decode('utf-8-sig')
    def pts(s):
        return re.findall(r'<Energy>([^<]*)</Energy><Efficiency>([^<]*)</Efficiency><ErrorPercent>([^<]*)</ErrorPercent>', s)
    def stamp(s):
        m = re.search(r'<ComputeStamp>([^<]*)</ComputeStamp>', s)
        return m.group(1) if m else ''
    pa, pb = pts(old), pts(new)
    name = os.path.basename(p)[:-4]
    if len(pa) != len(pb):
        rows.append((name, len(pa), len(pb), 'ЧИСЛО ТОЧЕК', stamp(old), stamp(new)))
        continue
    d = [(float(e1), 100.0 * (float(v2) / float(v1) - 1.0)) for (e1, v1, _), (e2, v2, _) in zip(pa, pb) if float(v1) > 0]
    lo = [x for e, x in d if e <= 120]
    hi = [x for e, x in d if e > 120]
    rows.append((name, len(pa), len(pb), 'низ ≤120: %+.2f..%+.2f %%, верх: |max| %.2f %%' % (min(lo), max(lo), max(abs(x) for x in hi)), stamp(old)[-20:], stamp(new)[-27:]))
for r in rows:
    print('%-22s %2d/%2d %-48s %s -> %s' % r)
print('спектров с узлом:', len(rows))
