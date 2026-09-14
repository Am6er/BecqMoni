# -*- coding: utf-8 -*-
u"""⛔ Сторож ИСТИНЫ корпуса: `corpus_def` ↔ `corpus/manifest.csv` (`T239`).

Что сверяет. Два поля, которыми задаётся, ЧТО в пробе лежит, — `chains` и
`nuclides`, — у всех 129 строк манифеста против объявления в
`tools/CORPUS/scripts/corpus_def.py`. Плюс состав ключей: строка манифеста без
записи в `corpus_def` и запись без строки — тоже отказ.

Зачем. Истину корпусных прогонов берут из `manifest.csv` (`tools/pie/score.py`),
а из тех же полей строится список линий модели разрешения (`res_low.py`).
06.09.2026 (`T239`) выяснилось, что у семёрки `corpus_def.LEGACY` строки
манифеста собирала отдельная ветка `build_corpus.py:legacy_manifest`, которая
заполняла `chains`, а `nuclides` выбрасывала целиком (`nuclides=''`
безусловно). Приказ Amber 02.09.2026 добавить K-40 чароитам был исполнен
коммитом `b19319ec`, а пересборка `251bbd30` в тот же день вернула
`ASN16_Charoite` и `AS80_Charoite` пустое поле — МОЛЧА, и recall с фантомами у
обоих считались против истины без калия.

⚠ Строка ~~`T126`~~ была закрыта записью «сверка манифеста с `corpus_def`
машинная — расхождений 0». Сверка была верна в час, когда её сделали, и через
несколько часов перестала: повторить её оказалось некому. Этот сторож и есть
тот повторяющий — ни `check_corpus.py`, ни остальные `tools/check_*.py` этих
полей не сверяют.

    python tools/check_corpus_truth.py                # проверить дерево
    python tools/check_corpus_truth.py --manifest=X   # проверить другой файл
    python tools/check_corpus_truth.py --selftest     # доказать, что отказывает

⚠ Сторож без доказанного отказа ничего не меряет (`T69`), поэтому `--selftest`
подкладывает заведомо плохой манифест (у одного спектра `nuclides` очищено) и
требует от проверки кода 1 с ИМЕНЕМ этого спектра в выводе; на чистом дереве —
код 0.

Коды возврата:
  0 — расхождений нет;
  1 — есть расхождение (каждое названо поимённо) либо не сошёлся `--selftest`;
  2 — не найден `manifest.csv` или `corpus_def.py`.
"""

import csv
import os
import shutil
import subprocess
import sys
import tempfile

# `T137`: cp1251-консоль не должна ронять печать знаков вне неё (⛔, ↔).
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')
MANIFEST = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'manifest.csv')

# Поля истины и то, как генератор их печатает: список через точку с запятой.
TRUTH_FIELDS = ('chains', 'nuclides')


def declared():
    u"""{ключ: {поле: строка}} — как поля истины объявлены в `corpus_def`."""
    if SCRIPTS not in sys.path:
        sys.path.insert(0, SCRIPTS)
    import corpus_def
    out = {}
    for e in corpus_def.ALL:
        out[e['key']] = dict((f, ';'.join(e.get(f) or [])) for f in TRUTH_FIELDS)
    return out


def written(path):
    u"""{ключ: {поле: строка}} — как поля истины лежат в `manifest.csv`."""
    with open(path, encoding='utf-8-sig', newline='') as fh:
        rows = list(csv.DictReader(fh))
    return dict((r['key'], dict((f, r.get(f, '')) for f in TRUTH_FIELDS))
                for r in rows)


def offences(decl, man):
    u"""Список расхождений: (ключ, поле, что в манифесте, что объявлено)."""
    bad = []
    for key in sorted(set(decl) | set(man)):
        if key not in man:
            bad.append((key, u'строка', u'НЕТ', u'объявлен в corpus_def'))
            continue
        if key not in decl:
            bad.append((key, u'строка', u'есть', u'НЕТ в corpus_def'))
            continue
        for f in TRUTH_FIELDS:
            if man[key][f] != decl[key][f]:
                bad.append((key, f, man[key][f] or u'<пусто>',
                            decl[key][f] or u'<пусто>'))
    return bad


def check(path):
    decl = declared()
    man = written(path)
    bad = offences(decl, man)
    print(u'манифест: %s (%d строк), corpus_def: %d записей'
          % (os.path.relpath(path, ROOT), len(man), len(decl)))
    for key, field, was, must in bad:
        print(u'  ⛔ %-22s %-8s манифест=%s  corpus_def=%s' % (key, field, was, must))
    print(u'РАСХОЖДЕНИЙ: %d' % len(bad))
    return len(bad)


def selftest():
    u"""Положительный контроль: заведомо плохой вход обязан дать код 1."""
    rc = 0
    tmp = tempfile.mkdtemp(prefix='corpus_truth_')
    try:
        victim = None
        with open(MANIFEST, encoding='utf-8-sig', newline='') as fh:
            rows = list(csv.reader(fh))
        head = rows[0]
        inuc, ikey = head.index('nuclides'), head.index('key')
        for r in rows[1:]:
            if r[inuc]:
                victim = r[ikey]
                r[inuc] = ''
                break
        if victim is None:
            print(u'⛔ подлог невозможен: в манифесте нет ни одной непустой '
                  u'графы nuclides — сторож не доказан')
            return 1
        fake = os.path.join(tmp, 'manifest.csv')
        with open(fake, 'w', encoding='utf-8-sig', newline='') as fh:
            csv.writer(fh, lineterminator='\r\n').writerows(rows)

        print(u'контроль ПЛОХОГО входа: у %s графа nuclides очищена' % victim)
        got = _run([fake])
        named = victim in got[1]
        print(u'  код возврата %d (ожидался 1), спектр назван поимённо: %s'
              % (got[0], u'да' if named else u'НЕТ'))
        print(u'контроль ХОРОШЕГО входа: манифест дерева как есть')
        good = _run([MANIFEST])
        print(u'  код возврата %d (ожидался 0)' % good[0])
        ok = got[0] == 1 and named and good[0] == 0
        print(u'СОШЛОСЬ' if ok else u'НЕ СОШЛОСЬ')
        rc = 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return rc


def _run(paths):
    u"""Позвать самого себя отдельным процессом — чтобы мерился КОД ВОЗВРАТА."""
    env = dict(os.environ, PYTHONIOENCODING='utf-8', PYTHONUTF8='1')
    p = subprocess.run([sys.executable, os.path.abspath(__file__)]
                       + [u'--manifest=' + x for x in paths],
                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
    out = p.stdout.decode('utf-8', 'replace')
    for line in out.splitlines():
        print(u'    | ' + line)
    return p.returncode, out


def main(argv):
    if '--selftest' in argv:
        return selftest()
    path = MANIFEST
    for a in argv:
        if a.startswith('--manifest='):
            path = a.split('=', 1)[1]
    if not os.path.isfile(path):
        print(u'⛔ не найден манифест: %s' % path)
        return 2
    if not os.path.isfile(os.path.join(SCRIPTS, 'corpus_def.py')):
        print(u'⛔ не найден corpus_def.py в %s' % SCRIPTS)
        return 2
    print(u'⛔ истина корпуса: corpus_def ↔ manifest.csv, поля %s'
          % ', '.join(TRUTH_FIELDS))
    return 1 if check(path) else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
