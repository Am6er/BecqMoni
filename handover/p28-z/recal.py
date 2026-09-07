# -*- coding: utf-8 -*-
u"""Поправить энергошкалу спектра ТОЛЬКО ДЛЯ ЗАМЕРА (`A278`, `A279`).

Кладёт в копию корпуса два новых ключа: `<base>_ref` — тот же спектр, шкала НЕ
тронута (отрицательный контроль: одна перезапись файла ничего менять не
должна), и `<base>_cal` — та же самая гистограмма, поправлена ОДНА величина,
коэффициенты калибровки. Отсчёты не меняются ни на один: статистика та же.
"""
import io, os, sys, xml.etree.ElementTree as ET

def rows(path):
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        head = f.readline()
        return head, [l.rstrip('\r\n') for l in f if l.strip()]

def split_row(row):
    out, cur, q = [], '', False
    for ch in row:
        if ch == '"': q = not q; cur += ch
        elif ch == ',' and not q: out.append(cur); cur = ''
        else: cur += ch
    out.append(cur); return out

corpus, base, gain, off = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
sp = os.path.join(corpus, 'spectra')
add = {}
for suffix, g, o in (('ref', 1.0, 0.0), ('cal', gain, off)):
    tree = ET.parse(os.path.join(sp, base + '.xml'))
    co = tree.getroot().find('.//EnergySpectrum/EnergyCalibration/Coefficients')
    old = [float(x.text) for x in co]
    new = [(old[0] - o) / g] + [c / g for c in old[1:]]
    for el, v in zip(co, new):
        el.text = repr(v)
    key = base + '_' + suffix
    tree.write(os.path.join(sp, key + '.xml'), encoding='utf-8', xml_declaration=True)
    add[key] = new
    print('%s: %s -> %s' % (key, old, new))

for name in ('manifest.csv', 'parts.csv', 'materials.csv'):
    p = os.path.join(corpus, name)
    head, rr = rows(p)
    src = [r for r in rr if split_row(r)[0] == base][0]
    with io.open(p, 'a', encoding='utf-8', newline='') as f:
        for key in add:
            cells = split_row(src); cells[0] = key
            f.write(','.join(cells) + '\n')
print('дописано ключей: %s' % ', '.join(add))
