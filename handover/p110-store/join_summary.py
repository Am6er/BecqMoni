# -*- coding: utf-8 -*-
r"""П110: сводка «матрица — прибор/кривая — формат — физика — читает ли Load HEAD — использует ли FSA HEAD —
нужен ли пересчёт — какие спектры ссылаются». Вход: rmx_headers.csv, device_curves.csv (рядом), список спектров
(spectra_refs.csv, guid;файл). Выход: store_summary.csv и store_summary.md на stdout. Ничего не считает.
Правила — по коду HEAD 0aab363e: `ResponseMatrix.Load` принимает ТОЛЬКО FormatVersion 9 (ResponseMatrix.cs:2902-2913),
FSA привязывает матрицу только при `IsValidFor` — клеймо начинается с `phys=21;` (ResponseMatrix.cs:427, :880, :1347;
FsaAnalysisSession.cs:540).
"""
import csv, os, sys, collections
HERE = os.path.dirname(os.path.abspath(__file__))
FORMAT_HEAD, PHYS_HEAD = 9, 21

def rows(name):
    with open(os.path.join(HERE, name), encoding='utf-8') as fh:
        return [r for r in csv.DictReader(fh, delimiter=';') if r.get('file') or r.get('device_file')]

hdr = {r['file'][:36].lower(): r for r in rows('rmx_headers.csv')}
curves = {}
for r in rows('device_curves.csv'):
    if r.get('curve_guid'):
        curves[r['curve_guid'].lower()] = r
refs = collections.defaultdict(list)
p = os.path.join(HERE, 'spectra_refs.csv')
if os.path.exists(p):
    with open(p, encoding='utf-8') as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith('#') or line.startswith('guid;'): continue
            g, f = line.split(';', 1)
            refs[g.lower()].append(f)

cols = ['rmx', 'дата файла', 'прибор', 'кривая', 'формат', 'физика', 'Load HEAD читает', 'FSA HEAD использует',
        'пересчёт', 'ссылок из спектров', 'узлов', 'историй/узел', 'секунд сборки', 'ЦП-с']
out = []
for g in sorted(hdr, key=lambda k: (k not in curves, curves.get(k, {}).get('device_name', ''), curves.get(k, {}).get('curve_name', ''))):
    h = hdr[g]; c = curves.get(g)
    fmt = int(h['format']); phys = int(h['phys'])
    reads = 'да' if fmt == FORMAT_HEAD else 'нет — OldFormat (формат %d)' % fmt
    uses = 'да' if (fmt == FORMAT_HEAD and phys == PHYS_HEAD) else ('нет — физика %d ≠ %d (клеймо)' % (phys, PHYS_HEAD) if fmt == FORMAT_HEAD else 'нет — не прочитана')
    if c:
        need = 'ДА (в EffMaker, после обновления сборки до физики 21)'
    else:
        need = 'нельзя — кривой нет ни в одном конфиге прибора' + (' (кривая живёт только внутри файлов спектров — там перевыбрать кривую прибора)' if refs.get(g) else '; сирота — судьба файла за Amber (как в A50)')
    out.append({
        'rmx': g[:8] + '…', 'дата файла': h['mtime'][:10],
        'прибор': c['device_name'] if c else '—', 'кривая': c['curve_name'] if c else '— (сирота)',
        'формат': fmt, 'физика': phys, 'Load HEAD читает': reads, 'FSA HEAD использует': uses, 'пересчёт': need,
        'ссылок из спектров': len(refs.get(g, [])), 'узлов': h['nodes'], 'историй/узел': h['histories_per_node'],
        'секунд сборки': h['build_seconds'], 'ЦП-с': h['cpu_seconds'] or '—',
    })

with open(os.path.join(HERE, 'store_summary.csv'), 'w', encoding='utf-8', newline='') as fh:
    w = csv.DictWriter(fh, fieldnames=cols, delimiter=';'); w.writeheader(); w.writerows(out)
print('| ' + ' | '.join(cols) + ' |')
print('|' + '---|' * len(cols))
for r in out:
    print('| ' + ' | '.join(str(r[c]) for c in cols) + ' |')
