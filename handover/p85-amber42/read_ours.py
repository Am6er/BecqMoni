# -*- coding: utf-8 -*-
r"""П85: чтение логов FsaCascadeProbe (ВЫКЛ/ВКЛ) — CF линий и площади сумм-пиков, отношения ВКЛ/ВЫКЛ.
    python read_ours.py <каталог с cf_p5_ang0.log/cf_p5_ang1.log> [--all]
"""
import io, re, sys
def fix(t):
    out = []
    for line in t.splitlines():
        try: out.append(line.encode('cp866').decode('utf-8'))
        except Exception: out.append(line)
    return out
def read(p): return fix(io.open(p, encoding='utf-8', errors='replace').read())

def table(d, name='cf_p5'):
    res = {}; census = {}
    for k in (0, 1):
        comp = None
        for l in read('%s/%s_ang%d.log' % (d, name, k)):
            m = re.match(r'^(\S+)\s+линий (\d+), сумм-пиков (\d+)', l)
            if m: comp = m.group(1); continue
            if l.startswith('=== перечень'): comp = None
            m = re.match(r'^\s+([\d.]+) кэВ\s+I\s+([\d.]+) %\s+CF ([\d.]+)', l)
            if m and comp:
                res.setdefault((comp, 'CF', float(m.group(1))), {})[k] = float(m.group(3)); continue
            m = re.match(r'^\s+сумм-пик\s+([\d.]+) кэВ\s+площадь ([\dE+.-]+)', l)
            if m and comp:
                res.setdefault((comp, 'Σ', float(m.group(1))), {})[k] = float(m.group(2)); continue
            if 'угловые корреляции (N14): пар' in l: census[k] = l.strip()
    return res, census

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    d = sys.argv[1]; show_all = '--all' in sys.argv
    res, census = table(d)
    print('ВЫКЛ:', census.get(0)); print('ВКЛ: ', census.get(1))
    print('%-8s %-3s %9s %12s %12s %8s' % ('нуклид', '', 'кэВ', 'ВЫКЛ', 'ВКЛ', 'ВКЛ/ВЫКЛ'))
    for key in sorted(res, key=lambda x: (x[0], x[1], -res[x].get(0, 0) if x[1]=='Σ' else x[2])):
        a, b = res[key].get(0), res[key].get(1)
        if a is None or b is None: continue
        r = b / a if a else float('nan')
        if show_all or abs(r - 1) > 0.0005:
            print('%-8s %-3s %9.2f %12.5g %12.5g %8.4f' % (key[0], key[1], key[2], a, b, r))
