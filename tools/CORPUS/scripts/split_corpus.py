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

    python tools/CORPUS/scripts/split_corpus.py

Пишет `corpus/parts.csv` — строка на КАЖДЫЙ спектр манифеста, ни одного
пропуска.

⛔ **Ключ `--check` СНЯТ 05.09.2026 решением Amber (`T164`), и это осознанная
потеря сторожа, а не уборка.** Он сличал лежащий `parts.csv` с построенным
ПОЛЕ В ПОЛЕ (129 строк × 5 полей = 645 сравнений, плюс длина списка и порядок)
и потому отказывал на ВЕРНОМ файле: у `ASN16_Lu176_P0` графа `why` вписана
рукой (коммит `9672feaa`, 02.09.2026 — дата съёмки, 573 с, ссылка на `B19`), а
скрипт строит `why` из графы `vessel` описи. Решение: заметку человека
сохранить, ключ снять; обучать `--check` терпеть ручные правки — дороже.

⚠ **Что теперь не ловит НИКТО.** Оставшийся сторож — `check_corpus.py`
(`check_parts`), и он проверяет ПОКРЫТИЕ и целостность, а не содержимое строк.
Мерено 05.09.2026 на девяти подменах в изолированной копии корпуса: `--check`
отказывал на девяти из девяти, `check_parts` — на четырёх. Мимо него проходят:
подменённая графа `why`, подменённая графа `det`, ПЕРЕВОД СПЕКТРА ИЗ `known` В
`unknown` со снятой геометрией, перестановка строк, `unknown` → `excluded` у
негерманиевого спектра. Третий случай — самый дорогой: спектр тихо уезжает в
другую часть корпуса, а раздел на части и заведён ровно ради того, чтобы этого
не случалось незаметно.

⚠ **Прогон БЕЗ ключей перезаписывает файл целиком и стирает ручные заметки.**
Поэтому перед записью печатается список граф `why`, которые будут затёрты, —
чтобы потеря была видна тому, кто гонит конвейер.
"""
import csv
import os
import sys

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

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


def manual_notes(rows):
    """СОХРАНИТЬ графы `why`, вписанные рукой, и сказать, что сохранено.

    ⛔ До 08.09.2026 эта функция только ПРЕДУПРЕЖДАЛА, а запись затирала
    заметку. Предупреждение — признак отказа с читателем-человеком, и читатель
    подвёл ровно так, как записано в памяти про такие признаки: 08.09.2026
    рядовой прогон конвейера снёс заметку у `ASN16_Lu176_P0` (ту самую, ради
    которой 05.09 сняли `--check`), строка проехала в выводе среди трёх сотен
    других, и потеря нашлась только в `git diff`. Второй раз подряд теряется
    одна и та же заметка — значит беда не в невнимательности.

    Теперь заметка ПЕРЕЖИВАЕТ запись, и решение Amber `T164` («заметку человека
    сохранить») исполняется механизмом, а не памятью того, кто гонит конвейер.

    ⚠ Сохраняется НЕ ВСЁ подряд. Заметка описывает ПОСТАНОВКУ спектра, поэтому
    переживает запись только тогда, когда постановка та же: часть и геометрия
    совпали. Уехал спектр в другую часть или к другой геометрии — заметка про
    прежнее место, и держать её значило бы врать; такая печатается отдельным
    списком как СНЯТАЯ.

    Отвечает двумя списками: что сохранено и что снято. `rows` правится на
    месте — сохранённое подставляется прямо в строку.
    """
    if not os.path.exists(PARTS):
        return [], []
    have = {r['spectrum']: r for r in read_csv(PARTS)}
    kept, dropped = [], []
    for r in rows:
        old = have.get(r['spectrum'])
        if old is None or old.get('why', '') == r['why']:
            continue
        same_place = (old.get('part', '') == r['part']
                      and old.get('geometry', '') == r['geometry'])
        if same_place:
            kept.append((r['spectrum'], old['why']))
            r['why'] = old['why']
        else:
            dropped.append((r['spectrum'], old['why'], old.get('part', ''),
                            old.get('geometry', '')))
    return kept, dropped


def write(rows):
    # ⛔ `utf-8-sig`, а не `utf-8`: лежащий `parts.csv` несёт BOM (его пишет
    # тот, кто правил файл руками, и читает `read_csv` выше). Запись без BOM
    # снимала его каждым прогоном, и первая же строка файла попадала в diff
    # шумом поверх настоящей правки.
    with open(PARTS, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        w.writeheader()
        w.writerows(rows)


def main(argv):
    # ⛔ `--check` снят решением Amber 05.09.2026 (`T164`). Молча принять ключ
    # нельзя: скрипт БЕЗ ключа пишет файл, то есть отставший вызов
    # `split_corpus.py --check` из старого сторожа или чужой памяти вместо
    # проверки затёр бы ручную заметку — ровно то, что решением велено
    # сохранить. Поэтому отказ, и с объяснением.
    if '--check' in argv:
        print('ОТКАЗ: ключ --check снят 05.09.2026 решением Amber (T164): он отказывал')
        print('   на ВЕРНОМ parts.csv, потому что графа `why` у ASN16_Lu176_P0')
        print('   вписана рукой (9672feaa), а скрипт строит её из описи.')
        print('   Раздел проверяет check_corpus.py (покрытие, геометрия, узел')
        print('   кривой); графы `why`, `det`, порядок строк и подмену части')
        print('   не проверяет никто — см. шапку файла.')
        return 2

    rows, orphans = build()

    bad = False
    if orphans:
        print('В описи геометрий есть спектры, которых нет в манифесте: %s'
              % ', '.join(orphans))
        bad = True

    kept, dropped = manual_notes(rows)
    if kept:
        print('заметок человека в графе `why` СОХРАНЕНО: %d (T164)' % len(kept))
        for key, why in kept:
            print('    %s: %s' % (key, why))
    if dropped:
        print('ЗАМЕТКА СНЯТА — постановка спектра сменилась: %d' % len(dropped))
        for key, why, part, geom in dropped:
            print('    %s: было %s/%s — %s' % (key, part, geom or '-', why))
        print('  заметка описывала ПРЕЖНЕЕ место; новая графа построена по описи')
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
    print('записано: %s' % PARTS)

    print('РАЗОШЛОСЬ' if bad else 'СОШЛОСЬ')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
