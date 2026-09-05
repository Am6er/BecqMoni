# -*- coding: utf-8 -*-
"""F28: формат КАЖДОГО правленого файла — ТОЛЬКО ПО БАЙТАМ.

⛔ Сравнивать переводы строк с `git show HEAD:файл` НЕЛЬЗЯ: git отдаёт БЛОБ, а
   он в этом дереве хранится с LF, тогда как в рабочем каталоге у большинства
   файлов CRLF. Такое сравнение объявляет «формат изменён» у 22 файлов из 31,
   и все 22 — ложные. Поэтому:
     * BOM сверяется с блобом — он ЧАСТЬ СОДЕРЖИМОГО и нормализации не знает;
     * переводы строк судятся ВНУТРИ файла (смеси быть не должно) и по ДОЛЕ
       правленых строк: сплошная смена переводов показала бы весь файл
       изменённым, а не десяток строк.
"""
import subprocess, sys, io

names = [n for n in subprocess.run(['git', 'diff', '--name-only'],
                                   capture_output=True).stdout.decode('utf-8').split('\n') if n.strip()]
numstat = {}
for line in subprocess.run(['git', 'diff', '--numstat'],
                           capture_output=True).stdout.decode('utf-8').split('\n'):
    p = line.split('\t')
    if len(p) == 3 and p[0].isdigit():
        numstat[p[2]] = (int(p[0]), int(p[1]))

bad = 0
for n in names:
    head = subprocess.run(['git', 'show', 'HEAD:' + n], capture_output=True).stdout
    now = io.open(n, 'rb').read()
    crlf = now.count(b'\r\n')
    cr = now.count(b'\r') - crlf
    lf = now.count(b'\n') - crlf
    total = crlf + cr + lf
    bom_head = head[:3] == b'\xef\xbb\xbf'
    bom_now = now[:3] == b'\xef\xbb\xbf'
    add, dele = numstat.get(n, (0, 0))
    problems = []
    if bom_head != bom_now:
        problems.append('BOM %s->%s' % ('да' if bom_head else 'нет', 'да' if bom_now else 'нет'))
    if crlf > 0 and lf > 0:
        problems.append('СМЕСЬ CRLF+LF')
    if cr > 0:
        problems.append('одиночные CR: %d' % cr)
    if total and add > total * 0.5:
        problems.append('правлено %d строк из %d — похоже на смену переводов' % (add, total))
    if problems:
        bad += 1
        print('⛔ %-48s %s' % (n, '; '.join(problems)))
    else:
        print('ok %-48s строк %d (%s), правлено +%d/-%d, BOM %s'
              % (n, total, 'CRLF' if crlf else 'LF', add, dele, 'да' if bom_now else 'нет'))
print('---')
print('файлов правлено: %d, формат нарушен: %d' % (len(names), bad))
sys.exit(1 if bad else 0)
