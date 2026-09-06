# -*- coding: utf-8 -*-
"""Замер G7: три сторожа напрямую из cmd с cp1251 и БЕЗ PYTHONIOENCODING/PYTHONUTF8
(через handover/g5-cp1251/run1251.cmd — то же плечо, что у G5). Лог каждого — в
handover/g7-utf8-three/<фаза>/<имя>.log, сводка — 00-codes.txt: код возврата, был ли
UnicodeEncodeError, в какой кодировке пришёл русский текст (что видит читатель utf-8).

  python measure.py <repo> <фаза> [<скрипт> <довод>...]   # без списка — штатный план
"""
import os, re, subprocess, sys, time

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO, PHASE = sys.argv[1], sys.argv[2]
HERE = os.path.dirname(os.path.abspath(__file__))
RUN = os.path.join(REPO, 'handover', 'g5-cp1251', 'run1251.cmd')
OUT = os.path.join(HERE, PHASE)
os.makedirs(OUT, exist_ok=True)

# Только пути без побочных эффектов (замок чтения check_corpus берётся и снимается штатно).
PLAN = [
    ('tools/CORPUS/scripts/check_corpus.py', ['--key=ASN16_Th232', '--verbose'],
     'один спектр по --key (разделы целостности не идут, код 0 после сводки)'),
    ('tools/CORPUS/scripts/gate_blind_check.py', ['--help'],
     '--help argparse: подсказка с `пик-3√N` (√ вне cp1251), код 0'),
    ('tools/CORPUS/scripts/gaussfit_check.py', ['--ref=no_such_ref.py'],
     'отказ без прежнего gaussfit ДО вычислений, код 2'),
]
if len(sys.argv) > 3:
    PLAN = [(sys.argv[3], sys.argv[4:], 'по доводу')]

CYR = re.compile(u'[А-яЁё]{3,}')

def russian_as_seen_by_utf8_reader(data):
    """Как русский текст выглядит для читателя, декодирующего utf-8 (оба канала агента)."""
    text = data.decode('utf-8', errors='replace')
    if CYR.search(text):
        return u'utf-8, русский читаем'
    if CYR.search(data.decode('cp1251', errors='replace')):
        n = text.count(u'�')
        return u'cp1251: читатель utf-8 видит ������ (%d знаков замены)' % n
    return u'русского текста нет'

rows = []
for script, args, how in PLAN:
    name = os.path.splitext(os.path.basename(script))[0]
    log = os.path.join(OUT, name + '.log')
    t0 = time.time()
    with open(log, 'wb') as f:
        p = subprocess.run(['cmd', '/c', RUN, script] + args, stdout=f, stderr=subprocess.STDOUT, cwd=REPO)
    dt = time.time() - t0
    data = open(log, 'rb').read()
    uee = b'UnicodeEncodeError' in data
    tb = b'Traceback (most recent call last)' in data
    seen = russian_as_seen_by_utf8_reader(data)
    rows.append((script, ' '.join(args), p.returncode, uee, tb, seen, len(data.splitlines()), dt, how))
    print(u'== %s %s: код %d  UnicodeEncodeError=%d  трассировка=%d  %s  строк %d  %.1f с'
          % (name, ' '.join(args), p.returncode, uee, tb, seen, len(data.splitlines()), dt))

with open(os.path.join(OUT, '00-codes.txt'), 'w', encoding='utf-8') as f:
    f.write(u'| скрипт | доводы | код | UnicodeEncodeError | трассировка | русский для читателя utf-8 | строк | с | чем мерен |\n|---|---|---|---|---|---|---|---|---|\n')
    for r in rows:
        f.write(u'| `%s` | `%s` | %d | %s | %s | %s | %d | %.1f | %s |\n'
                % (r[0], r[1], r[2], u'да' if r[3] else u'нет', u'да' if r[4] else u'нет', r[5], r[6], r[7], r[8]))
