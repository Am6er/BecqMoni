# -*- coding: utf-8 -*-
"""Раздел корпуса на ПОНЯТНЫЕ и НЕПОНЯТНЫЕ спектры (B1).

Понятный спектр — тот, у которого есть геометрия: кристалл, обвязка, форма и
положение пробы. Только у такого можно построить матрицу отклика, а значит
только у такого образ компонента в полноспектральном разборе — полный (пик
вместе с континуумом, краем и пиками вылета). У остальных образ строится из
одних пиков, и числа двух половин НЕСРАВНИМЫ: сводка, смешавшая их, даёт
среднее по двум разным моделям, и это незаметно.

Отсюда правило, ради которого файл и написан: **любая цифра по корпусу
называет ЧАСТЬ, к которой относится.** Раздел — не украшение отчёта, а условие
его осмысленности.

Мэппинг «геометрия -> спектр» НЕ набирается здесь: он приходит описью
`corpus/geometries/index.csv`, которую пишет `CorpusGeomProbe` тем же проходом,
которым строит сами файлы `.in`. Второй список тех же пар разошёлся бы с
файлами при первой правке — ровно тот способ, которым уже терялась работа.

    python tools/CORPUS/scripts/split_corpus.py [--check]

Пишет `corpus/parts.csv` — строка на КАЖДЫЙ спектр манифеста, ни одного
пропуска. С `--check` ничего не пишет, а проверяет, что лежащий файл сходится с
манифестом и описью, и возвращает 1, если нет.
"""
import csv
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(os.path.dirname(HERE), 'corpus')
MANIFEST = os.path.join(CORPUS, 'manifest.csv')
INDEX = os.path.join(CORPUS, 'geometries', 'index.csv')
PARTS = os.path.join(CORPUS, 'parts.csv')

# Германий исключён из работы приказом Amber 08.08.2026 (модель не разбирает
# коаксиальную ветвь, собирает сплошной цилиндр). Он не «непонятный» по
# отсутствию сведений — он вне работы, и это разные вещи: у HPGeGEM геометрия
# как раз названа (маринелли), но заводить по германию задачи нельзя.
GERMANIUM_GROUPS = ('HPGE', 'HPGE_GEM', 'HPGE_GMX')

FIELDS = ['spectrum', 'det', 'part', 'geometry', 'why']


def read_csv(path):
    with open(path, encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def build():
    manifest = read_csv(MANIFEST)
    index = read_csv(INDEX) if os.path.exists(INDEX) else []
    geom_of = {r['spectrum']: r['geometry'] for r in index}
    vessel_of = {r['spectrum']: r['vessel'] for r in index}

    rows = []
    for m in manifest:
        key, det = m['key'], m['det']
        if key in geom_of:
            rows.append(dict(spectrum=key, det=det, part='known',
                             geometry=geom_of[key], why=vessel_of[key]))
        elif det in GERMANIUM_GROUPS:
            rows.append(dict(spectrum=key, det=det, part='excluded', geometry='',
                             why='германий — вне работы по приказу Amber 08.08.2026'))
        else:
            rows.append(dict(spectrum=key, det=det, part='unknown', geometry='',
                             why='форма и положение пробы не записаны нигде'))

    orphans = sorted(set(geom_of) - {m['key'] for m in manifest})
    return rows, orphans


def write(rows):
    with open(PARTS, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        w.writeheader()
        w.writerows(rows)


def main(argv):
    check = '--check' in argv
    rows, orphans = build()

    bad = False
    if orphans:
        print('В описи геометрий есть спектры, которых нет в манифесте: %s'
              % ', '.join(orphans))
        bad = True

    if check:
        if not os.path.exists(PARTS):
            print('нет %s — сначала прогоните без --check' % PARTS)
            return 1
        have = read_csv(PARTS)
        if [{k: r[k] for k in FIELDS} for r in have] != rows:
            print('parts.csv разошёлся с манифестом или описью геометрий')
            bad = True
    else:
        write(rows)

    counts = {}
    for r in rows:
        counts[r['part']] = counts.get(r['part'], 0) + 1
    total = len(rows)
    print('спектров всего: %d' % total)
    for part in ('known', 'unknown', 'excluded'):
        print('  %-9s %3d' % (part, counts.get(part, 0)))
    geoms = sorted({r['geometry'] for r in rows if r['geometry']})
    print('геометрий: %d — %s' % (len(geoms), ', '.join(geoms)))
    if not check:
        print('записано: %s' % PARTS)

    print('РАЗОШЛОСЬ' if bad else 'СОШЛОСЬ')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
