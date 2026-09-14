#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ГЕНЕРАТОРА корпуса (`T244`): корпус в git собран ТЕМ генератором, что
лежит в дереве, и сводка корпуса — той же пересборкой, что и корпус.

Две беды одного разряда, обе найдены живыми и обе без читателя:

  1. Корпус отстаёт от генератора. 05.09.2026 фит семьи заведён в
     `build_corpus.py`, корпус — сборки 02.09.2026, три дня числа снимались с
     корпуса без этой правки. 12.09.2026 (`f4b518f2`) из шаблона прибора снят
     `<DoseRateConfig>`, все 24 `corpus/devices/*.xml` его несут до сих пор.
     `check_corpus.py` судит корпус сам по себе и на устаревшем говорит
     СОШЛОСЬ. Судья здесь — клеймо `corpus/generator.json`, которое пишет полная
     пересборка (`tools/CORPUS/scripts/corpus_stamp.py`, там же — почему одной
     истории git для приговора мало).
  2. Сводка отстаёт от корпуса. 10.09.2026 (П6) `summary.csv` и `SUMMARY.md`
     были на два спектра короче `manifest.csv` (129 против 131): пересборку
     сводки после пересборки корпуса не сделал никто. Судья здесь — пересборка
     сводки В ПАМЯТИ тем же `corpus_summary.py` и побайтное сравнение с тем,
     что лежит: сошлось — сводка та самая; разошлось — печатается первая
     разошедшаяся строка и число строк.

Запуск:
  python tools/check_corpus_generator.py                 приговор по дереву
  python tools/check_corpus_generator.py --corpus=<дир>  судить другой каталог корпуса
  python tools/check_corpus_generator.py --selftest      положительные контроли
  python tools/check_corpus_generator.py --no-summary    только клеймо

`--selftest` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, и он обязателен для сторожа, который
на живом дереве может быть зелен годами: во временном каталоге (а) клеймо,
писанное нынешним деревом, ОБЯЗАНО дать зелёный; (б) клеймо с подменённым
отпечатком одного файла и `head` = `e6138517` (коммит фита семьи, тот самый
случай 05.09.2026) ОБЯЗАНО дать красный и перечислить коммиты генератора после
него; (в) сводка с выброшенной строкой ОБЯЗАНА дать красный с числами 130/131.
Сторож при этом ничего в дереве не трогает.

Коды возврата: 0 — сошлось (или все контроли `--selftest` повели себя как
положено); 1 — расхождение; 2 — сторожу нечем судить.
"""
import argparse
import io
import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SCRIPTS = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')

sys.path.insert(0, SCRIPTS)
import corpus_stamp                                   # noqa: E402
import corpus_summary                                 # noqa: E402

#: Коммит фита семьи (`V27`, 05.09.2026) — исторический положительный
#: контроль строки `T244`: корпус тогда лежал собранным `251bbd30` (02.09.2026).
HISTORIC_GENERATOR_COMMIT = 'e6138517'


def _console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


class Tee(object):
    u"""Печать и в консоль, и в буфер — чтобы самопроверка читала свой же вывод."""
    def __init__(self, quiet=False):
        self.buf = io.StringIO()
        self.quiet = quiet

    def write(self, s):
        self.buf.write(s)
        if not self.quiet:
            sys.stdout.write(s)

    def flush(self):
        sys.stdout.flush()


def regenerate_summary(corpus_dir, into):
    u"""Пересобрать сводку из `corpus_dir` в каталог `into` тем же corpus_summary."""
    saved = (corpus_summary.CORPUS, corpus_summary.GEOM, corpus_summary.SPECTRA)
    try:
        corpus_summary.CORPUS = corpus_dir
        corpus_summary.GEOM = os.path.join(corpus_dir, 'geometries')
        corpus_summary.SPECTRA = os.path.join(corpus_dir, 'spectra')
        rows = corpus_summary.build()
        corpus_summary.CORPUS = into
        corpus_summary.write_csv(rows)
        corpus_summary.write_md(rows)
    finally:
        corpus_summary.CORPUS, corpus_summary.GEOM, corpus_summary.SPECTRA = saved
    return rows


def check_summary(corpus_dir, live_dir=None, out=None):
    u"""Сводка на диске == сводка, пересобранная в памяти из того же корпуса."""
    out = out or sys.stdout
    say = lambda s: out.write(s + u'\n')
    live_dir = live_dir or corpus_dir
    say(u'\n== сводка корпуса — пересборкой в памяти (T244, доля П6) ==')
    manifest = os.path.join(corpus_dir, 'manifest.csv')
    if not os.path.isfile(manifest):
        say(u'  ОТКАЗ СТОРОЖА: нет %s' % manifest)
        return None
    tmp = tempfile.mkdtemp(prefix='bq_summary_')
    try:
        real_stdout = sys.stdout
        sys.stdout = io.StringIO()                    # corpus_summary печатает пути
        try:
            rows = regenerate_summary(corpus_dir, tmp)
        finally:
            sys.stdout = real_stdout
        with io.open(manifest, encoding='utf-8-sig', newline='') as fh:
            n_manifest = sum(1 for _ in fh) - 1
        say(u'  manifest.csv: %d спектров; пересобрано строк сводки: %d' % (n_manifest, len(rows)))
        ok = True
        for name in ('summary.csv', 'SUMMARY.md'):
            live = os.path.join(live_dir, name)
            fresh = os.path.join(tmp, name)
            if not os.path.isfile(live):
                say(u'  ⛔ %-11s НЕТ на диске' % name)
                ok = False
                continue
            a = open(live, 'rb').read()
            b = open(fresh, 'rb').read()
            if a == b:
                say(u'  %-11s СОШЛОСЬ побайтно (%d байт)' % (name, len(a)))
                continue
            ok = False
            al = a.decode('utf-8-sig', 'replace').splitlines()
            bl = b.decode('utf-8-sig', 'replace').splitlines()
            say(u'  ⛔ %-11s РАЗОШЛОСЬ: на диске %d строк, пересборка даёт %d' % (name, len(al), len(bl)))
            for i, (x, y) in enumerate(zip(al, bl)):
                if x != y:
                    say(u'     первая разошедшаяся строка %d:' % (i + 1))
                    say(u'       на диске:    %s' % x[:140])
                    say(u'       пересборка:  %s' % y[:140])
                    break
        if not ok:
            say(u'  Лечится пересборкой сводки: python tools/CORPUS/scripts/corpus_summary.py')
            say(u'  (сводку читают ЛЮДИ: SUMMARY.md показывал корпус двухдневной давности, П6).')
        return ok
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def selftest():
    u"""Три контроля во временном каталоге. Возвращает 0, если все повели себя как положено."""
    print(u'САМОПРОВЕРКА сторожа генератора корпуса (T244)')
    tmp = tempfile.mkdtemp(prefix='bq_gen_selftest_')
    verdicts = []
    try:
        # (а) клеймо нынешнего дерева -> зелёный
        d_ok = os.path.join(tmp, 'ok')
        os.makedirs(d_ok)
        corpus_stamp.write(d_ok)
        t = Tee(quiet=True)
        green = corpus_stamp.check(d_ok, out=t)
        verdicts.append((u'(а) клеймо нынешнего дерева -> зелёный', green is True, t.buf.getvalue()))

        # (б) подменённое клеймо с head = коммит фита семьи -> красный + коммиты
        d_bad = os.path.join(tmp, 'bad')
        os.makedirs(d_bad)
        _, stamp = corpus_stamp.write(d_bad)
        first = sorted(k for k in stamp['files'] if k.endswith('build_corpus.py'))[0]
        stamp['files'][first] = '0' * 64
        stamp['fp'] = corpus_stamp.fold(stamp['files'])
        stamp['head'] = HISTORIC_GENERATOR_COMMIT
        with io.open(os.path.join(d_bad, corpus_stamp.STAMP_NAME), 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(json.dumps(stamp, ensure_ascii=False, indent=1, sort_keys=True))
        t = Tee(quiet=True)
        red = corpus_stamp.check(d_bad, out=t)
        text = t.buf.getvalue()
        listed = u'не доехавшие до данных' in text and u'build_corpus.py' in text
        n_commits = sum(1 for line in text.splitlines() if line.startswith(u'       ') and len(line.split()) > 2)
        verdicts.append((u'(б) клеймо с чужим отпечатком и head=%s -> красный, коммиты перечислены (%d)'
                         % (HISTORIC_GENERATOR_COMMIT, n_commits), red is False and listed and n_commits >= 1, text))

        # (в) сводка с выброшенной строкой -> красный
        d_sum = os.path.join(tmp, 'sum')
        os.makedirs(d_sum)
        src = os.path.join(CORPUS, 'summary.csv')
        with io.open(src, encoding='utf-8-sig', newline='') as fh:
            lines = fh.read().splitlines(True)
        with io.open(os.path.join(d_sum, 'summary.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
            fh.writelines(lines[:-1])                 # без последней строки
        shutil.copyfile(os.path.join(CORPUS, 'SUMMARY.md'), os.path.join(d_sum, 'SUMMARY.md'))
        t = Tee(quiet=True)
        red2 = check_summary(CORPUS, live_dir=d_sum, out=t)
        text2 = t.buf.getvalue()
        verdicts.append((u'(в) summary.csv без одной строки -> красный с числами %d/%d'
                         % (len(lines) - 2, len(lines) - 1),
                         red2 is False and (u'%d строк' % (len(lines) - 1)) in text2, text2))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    ok_all = True
    for title, passed, text in verdicts:
        print(u'  %s %s' % (u'ПОЙМАНО' if passed else u'⛔ НЕ ПОЙМАНО', title))
        if not passed:
            ok_all = False
            for line in text.splitlines():
                print(u'      ' + line)
    print(u'ИТОГ САМОПРОВЕРКИ: %s' % (u'все %d контроля повели себя как положено' % len(verdicts)
                                      if ok_all else u'ЕСТЬ НЕПОЙМАННОЕ'))
    return 0 if ok_all else 1


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--corpus', default=None, help=u'каталог корпуса (по умолчанию tools/CORPUS/corpus)')
    ap.add_argument('--no-summary', action='store_true', help=u'не судить сводку')
    ap.add_argument('--selftest', action='store_true', help=u'положительные контроли')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    corpus_dir = args.corpus if args.corpus else CORPUS
    if not os.path.isabs(corpus_dir):
        corpus_dir = os.path.join(ROOT, corpus_dir)
    if not os.path.isdir(corpus_dir):
        print(u'ОТКАЗ СТОРОЖА: нет каталога корпуса %s' % corpus_dir)
        return 2

    print(u'корпус: %s' % os.path.relpath(corpus_dir, ROOT))
    ok = corpus_stamp.check(corpus_dir)
    if not args.no_summary:
        s = check_summary(corpus_dir)
        if s is None:
            return 2
        ok = ok and s
    print()
    if ok:
        print(u'СОШЛОСЬ: корпус собран нынешним генератором, сводка — этим корпусом.')
        return 0
    print(u'ОСТАНОВ: корпус или сводка отстают от своего генератора (см. выше).')
    return 1


if __name__ == '__main__':
    _console()
    sys.exit(main())
