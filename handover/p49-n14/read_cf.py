# -*- coding: utf-8 -*-
import io, re, sys, glob, os
def fix(t):
    out = []
    for line in t.splitlines():
        try:
            out.append(line.encode('cp866').decode('utf-8'))
        except Exception:
            out.append(line)
    return out
def read(p):
    return fix(io.open(p, encoding='utf-8', errors='replace').read())

def cf_table(name):
    res = {}
    for k in (0, 1):
        lines = read('D:/BqMoni_Claude/p49/%s_ang%d.log' % (name, k))
        comp = None
        for l in lines:
            m = re.match(r'^(\S+)\s+линий (\d+), сумм-пиков (\d+)', l)
            if m:
                comp = m.group(1); continue
            m = re.match(r'^\s+([\d.]+) кэВ\s+I\s+([\d.]+) %\s+CF ([\d.]+)', l)
            if m and comp:
                res.setdefault((comp, float(m.group(1))), {})[k] = float(m.group(3)); continue
            m = re.match(r'^\s+сумм-пик\s+([\d.]+) кэВ\s+площадь ([\dE+.-]+)', l)
            if m and comp:
                res.setdefault((comp, 'Σ' + m.group(1)), {})[k] = float(m.group(2)); continue
            if 'угловые корреляции (N14): пар' in l:
                res.setdefault(('census', 0), {})[k] = l.strip()
    return res

def scan_sum(name, lo, hi):
    out = {}
    for k in (0, 1):
        lines = read('D:/BqMoni_Claude/p49/%s_ang%d.log' % (name, k))
        meas = a = b = 0.0; inside = False
        for l in lines:
            if l.startswith('=== участок'):
                inside = True; continue
            if inside:
                m = re.match(r'^\s+([\d.]+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)', l)
                if m:
                    e = float(m.group(1))
                    if lo <= e <= hi:
                        meas += float(m.group(2)); a += float(m.group(3)); b += float(m.group(4))
                elif l.strip() == '' and meas > 0:
                    break
        out[k] = (meas, a, b)
    return out

if __name__ == '__main__':
    what = sys.argv[1]
    if what == 'cf':
        name = sys.argv[2]
        res = cf_table(name)
        for key in sorted(res, key=lambda x: (x[0], str(x[1]))):
            v = res[key]
            if key[0] == 'census':
                print('  ВЫКЛ:', v.get(0)); print('  ВКЛ: ', v.get(1)); continue
            a, b = v.get(0), v.get(1)
            if a is None or b is None: continue
            tag = 'сумм-пик' if isinstance(key[1], str) else 'CF'
            d = (b / a - 1) * 100 if a else float('nan')
            if abs(d) > 0.05 or (len(sys.argv) > 3):
                print('%-10s %-9s %10s  ВЫКЛ %-12.5g ВКЛ %-12.5g %+6.2f %%' % (key[0], tag, key[1], a, b, d))
    else:
        name, lo, hi = sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
        r = scan_sum(name, lo, hi)
        for k in (0, 1):
            meas, a, b = r[k]
            print('%s %s: измерено %.0f, модель без каскада %.0f, с каскадом %.0f; сумм-пик модели %.0f, данных (изм − без каскада) %.0f, модель/данные %.3f'
                  % (name, 'ВЫКЛ' if k == 0 else 'ВКЛ ', meas, a, b, b - a, meas - a, (b - a) / (meas - a) if meas != a else float('nan')))
