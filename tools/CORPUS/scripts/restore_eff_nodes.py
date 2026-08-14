# -*- coding: utf-8 -*-
u"""Вернуть узлы `<Efficiency>` в спектры корпуса ИЗ GIT, без пересчёта МК (T30).

ЗАЧЕМ. Полная пересборка корпуса (`build_corpus.py`) строит рабочие копии
заново и о вставленных узлах привязки не знает — кривая и матрица понятной
части исчезают молча. Штатный способ вернуть их — `corpuseffprobe`, но он
СЧИТАЕТ кривую заново: это Монте-Карло, и свежий розыгрыш сдвигает базу
корпуса не физикой, а шумом генератора. Когда пересборка сделана не ради
новой физики, узлы надо ВЕРНУТЬ ТЕ ЖЕ.

Источник — прошлая версия того же файла в git (`HEAD` по умолчанию). Узел
берётся целиком, вставляется на своё место по порядку свойств (сразу после
`</ROIConfigReference>`, иначе после `</DeviceConfigReference>`), остальной
файл не трогается.

    python tools/CORPUS/scripts/restore_eff_nodes.py [--rev=HEAD] [--apply]

⛔ `--apply` ВЫКЛЮЧЕН по умолчанию: без него печатается план.

Матрицы при этом не нужны — они лежат отдельными файлами `.rmx` и ищутся по
Guid, который внутри узла и записан.
"""
import argparse
import glob
import io
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir, os.pardir))
SPECTRA = os.path.join(HERE, os.pardir, 'corpus', 'spectra')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

NODE = re.compile(r'<Efficiency>.*</Efficiency>', re.S)


def from_git(rev, rel):
    out = subprocess.run(['git', 'show', '%s:%s' % (rev, rel)],
                         cwd=REPO, capture_output=True)
    if out.returncode != 0:
        return None
    return out.stdout.decode('utf-8-sig', 'replace')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--rev', default='HEAD')
    ap.add_argument('--spectra', default=SPECTRA)
    ap.add_argument('--apply', action='store_true')
    args = ap.parse_args()

    restored, already, missing = 0, 0, 0
    for path in sorted(glob.glob(os.path.join(args.spectra, '*.xml'))):
        key = os.path.splitext(os.path.basename(path))[0]
        text = io.open(path, encoding='utf-8-sig').read()
        if '<Efficiency>' in text:
            already += 1
            continue

        rel = os.path.relpath(os.path.abspath(path), REPO).replace(os.sep, '/')
        old = from_git(args.rev, rel)
        if old is None:
            continue

        m = NODE.search(old)
        if m is None:
            continue

        node = m.group(0)
        anchor = '</ROIConfigReference>' if '</ROIConfigReference>' in text \
            else '</DeviceConfigReference>'
        if anchor not in text:
            print('%-22s НЕКУДА вставить (нет %s)' % (key, anchor))
            missing += 1
            continue

        guid = re.search(r'<Guid>([^<]+)', node)
        name = re.search(r'<Name>([^<]+)', node)
        print('%-22s <- %s (%s, %d символов)'
              % (key, name.group(1) if name else '?',
                 guid.group(1)[:8] if guid else '?', len(node)))
        restored += 1
        if not args.apply:
            continue

        i = text.index(anchor) + len(anchor)
        io.open(path, 'w', encoding='utf-8', newline='').write(
            u'﻿' + text[:i] + node + text[i:])

    print()
    print('вернуть узлов: %d; уже на месте: %d; без места: %d%s'
          % (restored, already, missing,
             '' if args.apply else '  (--apply не задан, файлы не тронуты)'))


if __name__ == '__main__':
    main()
