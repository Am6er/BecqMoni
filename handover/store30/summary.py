# -*- coding: utf-8 -*-
# store30: сводка «сцена — клеймо — время — шум» по логам счёта (фото: photon_*.log пары A77, густых и основного;
# раздел: sections_times.csv и логи разделов; клейма и годность — stamps.txt от StampViewS30).
import csv, hashlib, io, os, re, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
S = r'D:\BqMoni_Claude\store30'
L = os.path.join(S, 'logs')
rows = {}
for name in ('a77_photon.log', 'photon_dense.log', 'photon_main.log'):
    p = os.path.join(L, name)
    if not os.path.exists(p):
        continue
    key = None
    for line in io.open(p, encoding='utf-8', errors='replace'):
        m = re.match(r'== (\S+) ==', line)
        if m:
            key = m.group(1)
            continue
        if key is None:
            continue
        r = rows.setdefault(key, {})
        m = re.search(r'клеймо   : (phys=\d+;[0-9a-f]{16})', line)
        if m: r['stamp'] = m.group(1); r['src'] = name
        m = re.search(r'время    : ([\d.]+) с на часах', line)
        if m: r['t_photon'] = float(m.group(1))
        m = re.search(r'шум конт\.: взвешенная ([\d.]+) %\s+(\S+.*)$', line)
        if m: r['noise'] = float(m.group(1)); r['noise_note'] = m.group(2).strip()
        m = re.search(r'κ пар    : (.*)$', line)
        if m: r['kappa'] = 'точка' if 'точечная' in m.group(1) else 'таблица'
tp = os.path.join(L, 'sections_times.csv')
if os.path.exists(tp):
    for rec in csv.DictReader(io.open(tp, encoding='utf-8-sig')):
        rows.setdefault(rec['scene'], {})['t_sect'] = float(rec['seconds']); rows[rec['scene']]['sect_exit'] = rec['exit']
for k in ('RC103_point0', 'AS80_lu_front'):
    p = os.path.join(L, 'a77_section_%s.log' % k)
    if os.path.exists(p):
        m = re.search(r'раздел на GPU: .*?, ([\d.]+) с\n', io.open(p, encoding='utf-8', errors='replace').read())
        if m: rows.setdefault(k, {})['t_sect'] = float(m.group(1)); rows[k]['sect_exit'] = '0'
sec = {}
for k in rows:
    p = os.path.join(L, 'sections', k + '.log') if k not in ('RC103_point0', 'AS80_lu_front') else os.path.join(L, 'a77_section_%s.log' % k)
    if os.path.exists(p):
        t = io.open(p, encoding='utf-8', errors='replace').read()
        m = re.search(r'клеймо раздела: (esec=\d+;phys=\d+;for=[0-9a-f]+).*?;br=(\d+),', t)
        m2 = re.search(r'отпечаток тела ([0-9a-f]+) → ([0-9a-f]+); клеймо то же: (True|False)', t)
        sec[k] = (m.group(1) + ';br=' + m.group(2)) if m else '—'
        if m2: rows[k]['body'] = m2.group(2) + ('' if m2.group(1) == m2.group(2) and m2.group(3) == 'True' else ' ⛔')
def sha(k):
    p = os.path.join(S, 'store', k + '.rmx')
    return hashlib.sha256(open(p, 'rb').read()).hexdigest()[:16] if os.path.exists(p) else '—'
print(u'| сцена | клеймо фото | густая | шум конт., % | фото, с | раздел, с | клеймо раздела | тело | sha256 .rmx |')
print(u'|---|---|---|---|---|---|---|---|---|')
tot_p = tot_s = 0.0
for k in sorted(rows):
    r = rows[k]
    tot_p += r.get('t_photon', 0); tot_s += r.get('t_sect', 0)
    print(u'| %s | `%s…` | %s | %s%s | %s | %s | `%s` | `%s` | `%s` |' % (
        k, r.get('stamp', '—'), '×2' if r.get('src') == 'photon_dense.log' else '',
        ('%.2f' % r['noise']) if 'noise' in r else '—', '' if r.get('noise_note', 'тихо') == 'тихо' else ' ⚠ ' + r['noise_note'],
        ('%.0f' % r['t_photon']) if 't_photon' in r else '—', ('%.0f' % r['t_sect']) if 't_sect' in r else '—',
        sec.get(k, '—'), r.get('body', '—'), sha(k)))
print(u'\nсцен %d; фото Σ %.1f мин, разделы Σ %.1f мин' % (len(rows), tot_p / 60, tot_s / 60))
