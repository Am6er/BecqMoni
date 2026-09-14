# -*- coding: utf-8 -*-
"""
Побайтная сверка «проба против корпуса» по каталогу геометрий (`T164`, F13,
05.09.2026).

  python handover/f13-t164/geom_check.py <каталог сборки пробы> [<каталог корпуса>]
        [--loose]

Сверяет два каталога: все `.in` побитово (множество имён и содержимое) и опись
`index.csv` — побитово. Без `--loose` любое расхождение описи — отказ (код 1).
С `--loose` расхождение описи разбирается: BOM, переводы строк, МНОЖЕСТВО строк
и их ПОРЯДОК печатаются отдельно; отказом (код 1) считается только разное
множество строк или разные `.in`. Так один скрипт мерит и состояние ДО
(ожидается: множества равны, BOM и порядок разные), и приёмку ПОСЛЕ (побайтно).

Переводы строк считаются байтами: строки режутся по b"\\r\\n"/b"\\n" и
сравниваются как байты, без декодирования.
"""
from __future__ import print_function
import os
import sys

BOM = b'\xef\xbb\xbf'


def read(path):
    with open(path, 'rb') as fh:
        return fh.read()


def split_lines(data):
    if data.startswith(BOM):
        data = data[len(BOM):]
    return [l.rstrip(b'\r') for l in data.split(b'\n') if l.rstrip(b'\r') != b'']


def main(argv):
    loose = '--loose' in argv
    args = [a for a in argv if not a.startswith('--')]
    if not args:
        print(__doc__)
        return 2
    probe = os.path.abspath(args[0])
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    corpus = os.path.abspath(args[1]) if len(args) > 1 else os.path.join(
        root, 'tools', 'CORPUS', 'corpus', 'geometries')
    print('проба : %s' % probe)
    print('корпус: %s' % corpus)

    bad = False

    # --- .in побитово ------------------------------------------------------
    p_in = sorted(f for f in os.listdir(probe) if f.endswith('.in'))
    c_in = sorted(f for f in os.listdir(corpus) if f.endswith('.in'))
    only_p = sorted(set(p_in) - set(c_in))
    only_c = sorted(set(c_in) - set(p_in))
    same = 0
    differ = []
    for f in sorted(set(p_in) & set(c_in)):
        if read(os.path.join(probe, f)) == read(os.path.join(corpus, f)):
            same += 1
        else:
            differ.append(f)
    print('.in: у пробы %d, в корпусе %d, побитово равных %d, разных %d, '
          'только у пробы %d, только в корпусе %d'
          % (len(p_in), len(c_in), same, len(differ), len(only_p), len(only_c)))
    for f in differ:
        print('   РАЗНЫЙ .in: %s' % f)
    for f in only_p:
        print('   только у пробы: %s' % f)
    for f in only_c:
        print('   только в корпусе: %s' % f)
    if differ or only_p or only_c:
        bad = True

    # --- index.csv ---------------------------------------------------------
    pi = os.path.join(probe, 'index.csv')
    ci = os.path.join(corpus, 'index.csv')
    if not os.path.isfile(pi) or not os.path.isfile(ci):
        print('index.csv: нет у %s' % ('пробы' if not os.path.isfile(pi) else 'корпуса'))
        return 1
    pd, cd = read(pi), read(ci)
    if pd == cd:
        print('index.csv: ПОБАЙТНО РАВНЫ, %d байт, строк %d'
              % (len(pd), len(split_lines(pd))))
    else:
        print('index.csv: байты РАЗНЫЕ (%d против %d байт)' % (len(pd), len(cd)))
        print('   BOM: у пробы %s, в корпусе %s'
              % ('есть' if pd.startswith(BOM) else 'нет',
                 'есть' if cd.startswith(BOM) else 'нет'))
        print('   CRLF: у пробы %d, в корпусе %d' % (pd.count(b'\r\n'), cd.count(b'\r\n')))
        pl, cl = split_lines(pd), split_lines(cd)
        print('   строк (с шапкой): у пробы %d, в корпусе %d' % (len(pl), len(cl)))
        sp, sc = set(pl), set(cl)
        if sp == sc:
            print('   МНОЖЕСТВА строк РАВНЫ (%d), порядок %s'
                  % (len(sp), 'тот же' if pl == cl else 'РАЗНЫЙ'))
            if not loose:
                bad = True
        else:
            bad = True
            print('   МНОЖЕСТВА строк РАЗНЫЕ: только у пробы %d, только в корпусе %d'
                  % (len(sp - sc), len(sc - sp)))
            for l in sorted(sp - sc):
                print('      только у пробы   : %s' % l.decode('utf-8', 'replace'))
            for l in sorted(sc - sp):
                print('      только в корпусе : %s' % l.decode('utf-8', 'replace'))

    print('ИТОГ: %s' % ('ОТКАЗ' if bad else 'СОШЛОСЬ'))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
