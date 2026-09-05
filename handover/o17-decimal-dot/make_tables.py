# -*- coding: utf-8 -*-
"""Таблицы счёта O17/A242 из hits.tsv.

⛔ QUOTE_NONE обязателен: у части полей содержимое начинается с кавычки
(формат `"n1"`, `"R"`), и csv по умолчанию склеивал такие записи — 1097 в
файле читались как 1070. Поймано положительным контролем (сравнение с
простым разбиением по переводу строки).
"""
import csv, io, collections, sys, os

src = sys.argv[1]
outdir = sys.argv[2]
with io.open(src, encoding='utf-8', newline='') as fh:
    rows = list(csv.DictReader(fh, delimiter='\t', quoting=csv.QUOTE_NONE))

with io.open(src, encoding='utf-8', newline='') as fh:
    plain = len([l for l in fh.read().split('\n') if l]) - 1
assert plain == len(rows), 'TSV прочитан не весь: %d из %d' % (len(rows), plain)

byf = collections.defaultdict(lambda: collections.Counter())
for r in rows:
    byf[r['file']][r['side']] += 1
    byf[r['file']][r['side'] + '_' + r['kind']] += 1

lines = ['file\tprint\tparse\tprint_NUM\tparse_NUM\ttotal']
tot = collections.Counter()
for f in sorted(byf, key=lambda x: -(byf[x]['print'] + byf[x]['parse'])):
    c = byf[f]
    p, q = c['print'], c['parse']
    pn = c['print_NUM']
    qn = c['parse_NUM']
    lines.append('%s\t%d\t%d\t%d\t%d\t%d' % (f, p, q, pn, qn, p + q))
    tot['print'] += p; tot['parse'] += q; tot['print_NUM'] += pn; tot['parse_NUM'] += qn
lines.append('ИТОГО\t%d\t%d\t%d\t%d\t%d' % (tot['print'], tot['parse'], tot['print_NUM'],
                                            tot['parse_NUM'], tot['print'] + tot['parse']))
with io.open(os.path.join(outdir, 'count-by-file.tsv'), 'w', encoding='utf-8', newline='') as f:
    f.write('\n'.join(lines) + '\n')

api = collections.Counter((r['side'], r['api'], r['kind']) for r in rows)
lines2 = ['side\tapi\tkind\tcount']
for k, v in sorted(api.items(), key=lambda x: -x[1]):
    lines2.append('%s\t%s\t%s\t%d' % (k[0], k[1], k[2], v))
with io.open(os.path.join(outdir, 'count-by-api.tsv'), 'w', encoding='utf-8', newline='') as f:
    f.write('\n'.join(lines2) + '\n')

print('файлов с попаданиями: %d, мест: %d (печать %d, разбор %d; числовых: печать %d, разбор %d)'
      % (len(byf), len(rows), tot['print'], tot['parse'], tot['print_NUM'], tot['parse_NUM']))
