# -*- coding: utf-8 -*-
u"""Сводная таблица корпуса: одна строка на спектр, всё в одном месте.

ЗАЧЕМ. Про корпус до сих пор надо было держать в голове четыре файла:
`manifest.csv` (что в спектре), `parts.csv` (в какой он части и под какой
геометрией), `detectors.csv` (модель разрешения группы) и `geometries/*.in`
(что за сосуд и ЧТО В НЁМ). Последнее не было видно нигде, кроме самих `.in`, —
и ровно на этом 15.08.2026 вышла ошибка в разы: у спектров Lu₂O₃ вещество пробы
осталось воздухом шаблона, а заметить это было негде (§13ж журнала матрицы,
строки `E19`, `B9`).

Поэтому здесь есть колонка **`проба`** и колонка **`ρ_пробы`**: пустая проба
видна с первого взгляда, а не после разбора расхождения.

Ничего не считает и не пересобирает — только сводит уже посчитанное:

    python tools/CORPUS/scripts/corpus_summary.py

Пишет два файла рядом с корпусом:
    corpus/summary.csv — машинный, все колонки;
    corpus/SUMMARY.md  — человеческий, сгруппирован по частям и приборам.

⚠ Файлы СГЕНЕРИРОВАНЫ. Правится не они, а источники: `corpus_def.py`
(состав), `CorpusGeomProbe` (геометрии), конвейер сборки.
"""
import csv
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import corpus_def                                        # noqa: E402

CORPUS = os.path.join(os.path.dirname(HERE), 'corpus')
GEOM = os.path.join(CORPUS, 'geometries')
SPECTRA = os.path.join(CORPUS, 'spectra')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

COLUMNS = [
    'состояние', 'чего_нет', 'спектр', 'группа', 'часть', 'истина', 'каналов',
    'живое_с', 'отсчётов', 'ПШПВ_662_%', 'фон', 'калибровка', 'геометрия',
    'тип_источника', 'сосуд', 'проба', 'ρ_пробы', 'кристалл', 'матрица',
    'узел_кривой', 'исходник',
]

OK, BAD = '✅', '❗'


def diagnose(r):
    u"""Чего у спектра НЕ ХВАТАЕТ, словами. Пусто — всё на месте.

    Красным метится только то, чего быть НЕ ДОЛЖНО. Отсутствие геометрии у
    непонятной части — не дефект, а её определение (B1), и оно называется
    отдельной фразой без флага: иначе красными станут сто строк из ста
    двадцати пяти и колонка перестанет что-либо значить.
    """
    bad, note = [], []

    if r['часть'] == 'known':
        if r['матрица'] in ('нет', '—'):
            bad.append(u'матрицы')
        if r['узел_кривой'] in ('нет', 'файла нет'):
            bad.append(u'узла кривой в спектре')
        if r['тип_источника'] == 'CYLINDER' and r['проба'] == 'Air, dry':
            bad.append(u'вещества пробы (осталось воздухом, E19)')
        if not r['ρ_пробы'] and r['тип_источника'] in ('CYLINDER', 'MARINELLI', 'BOX'):
            bad.append(u'плотности пробы')
    else:
        note.append(u'геометрии нет — потому и непонятная')

    if r['фон'] in ('нет', ''):
        bad.append(u'фона')
    if not r['истина']:
        bad.append(u'истины в манифесте')
    try:
        if float(r['живое_с']) <= 0:
            bad.append(u'живого времени (LiveTime = 0)')
    except (TypeError, ValueError):
        bad.append(u'живого времени')
    try:
        if int(r['отсчётов']) <= 0:
            bad.append(u'отсчётов')
    except (TypeError, ValueError):
        pass
    if str(r['калибровка']).startswith('stored/'):
        # У ЗАКРЕПЛЁННЫХ спектров (`from_corpus`) хранившаяся калибровка — это
        # СВОЯ, проверенная прошлой сборкой: копия корпуса тем и получена. Новый
        # проход не может её побить по построению (она подогнана к этим самым
        # линиям), и «нет проверенной» здесь было бы ложной тревогой на ровном
        # месте — а ложная тревога в сводке дороже отсутствующей.
        if r['спектр'] in PINNED:
            note.append(u'калибровка своя, проверенная прошлой сборкой '
                        u'(источник закреплён)')
        else:
            bad.append(u'проверенной энергокалибровки (осталась хранившаяся)')

    text = u'; '.join([u'нет ' + b for b in bad] + note)
    return (BAD if bad else OK), text


PINNED = frozenset(
    e['key'] for e in (corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON)
    if e.get('from_corpus'))


def read_csv(path, key=None):
    if not os.path.isfile(path):
        return {} if key else []
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        rows = list(csv.DictReader(fh))
    return {r[key]: r for r in rows} if key else rows


def geometry_facts(name):
    u"""Тип источника, вещество пробы, её плотность и кристалл — из файла `.in`.

    Читается САМ ФАЙЛ, а не таблица рядом: второй список тех же фактов разошёлся
    бы с ним при первой правке — тем же способом, каким уже терялась работа
    (см. шапку `split_corpus.py`).
    """
    path = os.path.join(GEOM, name + '.in')
    if not os.path.isfile(path):
        return {}
    text = io.open(path, encoding='cp1251', errors='replace').read()

    def one(pattern):
        m = re.search(pattern, text, re.M)
        return m.group(1).strip() if m else ''

    kind = one(r'^SourceType\s*=\s*(\S+)')
    material = one(r'^M_S[A-Z]_Source\.MName\s*=\s*(.*)$')
    density = one(r'^S[A-Z]_RoSource\s*=\s*(\S+)')
    crystal = one(r'^M_DS_Crystal\.MName\s*=\s*(.*)$')
    if kind == 'POINT':
        # У точечного источника пробы нет по построению: «воздух» здесь — среда
        # вокруг, а не незаполненное поле. Разница важна, иначе сводка будет
        # кричать на девять геометрий, где всё в порядке.
        material, density = '— (точечный)', ''
    return dict(kind=kind, material=material, density=density, crystal=crystal)


def matrix_facts(name):
    path = os.path.join(GEOM, name + '.rmx')
    if not os.path.isfile(path):
        return 'нет'
    head = io.open(path, 'rb').read(64)
    m = re.search(br'phys=(\d+)', head)
    size = os.path.getsize(path) / 1024.0
    return ('физика %s, %.0f КБ' % (m.group(1).decode('ascii'), size)) if m \
        else ('есть, %.0f КБ' % size)


def efficiency_node(key):
    path = os.path.join(SPECTRA, key + '.xml')
    if not os.path.isfile(path):
        return 'файла нет'
    eff = ET.parse(path).getroot().find('ResultDataList/ResultData/Efficiency')
    if eff is None:
        return 'нет'
    name = eff.findtext('Name') or '?'
    return name


def build():
    manifest = read_csv(os.path.join(CORPUS, 'manifest.csv'))
    parts = read_csv(os.path.join(CORPUS, 'parts.csv'), key='spectrum')
    index = read_csv(os.path.join(CORPUS, 'geometries', 'index.csv'))
    vessel = {r['spectrum']: (r['geometry'], r['vessel']) for r in index}

    geom_cache, matrix_cache = {}, {}
    rows = []
    for m in manifest:
        key = m['key']
        part = parts.get(key, {})
        gname, gvessel = vessel.get(key, ('', ''))
        if gname and gname not in geom_cache:
            geom_cache[gname] = geometry_facts(gname)
            matrix_cache[gname] = matrix_facts(gname)
        g = geom_cache.get(gname, {})
        rows.append({
            'спектр': key,
            'группа': m['det'],
            'часть': part.get('part', ''),
            'истина': m['chains'] or m['nuclides'] or '',
            'каналов': m['channels'],
            'живое_с': m['live_s'],
            'отсчётов': m['counts'],
            'ПШПВ_662_%': m['fwhm_662_pct'],
            'фон': m['background'],
            'калибровка': m['ecal_mode'],
            'геометрия': gname or '—',
            'тип_источника': g.get('kind', ''),
            'сосуд': gvessel,
            'проба': g.get('material', ''),
            'ρ_пробы': g.get('density', ''),
            'кристалл': g.get('crystal', ''),
            'матрица': matrix_cache.get(gname, '—') if gname else '—',
            'узел_кривой': efficiency_node(key),
            'исходник': m['source'],
        })
    for r in rows:
        r['состояние'], r['чего_нет'] = diagnose(r)
    return rows


def write_csv(rows):
    path = os.path.join(CORPUS, 'summary.csv')
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.DictWriter(fh, COLUMNS)
        w.writeheader()
        for r in rows:
            w.writerow(r)
    print('таблица: %s (%d строк)' % (path, len(rows)))
    return path


def write_md(rows):
    path = os.path.join(CORPUS, 'SUMMARY.md')
    order = {'known': 0, 'unknown': 1, 'excluded': 2}
    title = {'known': 'ПОНЯТНАЯ часть — геометрия и матрица есть',
             'unknown': 'НЕПОНЯТНАЯ часть — образ строится из одних пиков',
             'excluded': 'ИСКЛЮЧЕНЫ из прогонов (германий)'}
    out = []
    out.append(u'# Корпус: сводная таблица\n')
    out.append(u'⚠ **Файл СГЕНЕРИРОВАН** `scripts/corpus_summary.py`. Правится не он,\n'
               u'а источники: `corpus_def.py` (состав), `CorpusGeomProbe` (геометрии),\n'
               u'конвейер сборки. Пересобрать — запустить скрипт.\n')
    out.append(u'**Каждая цифра по корпусу обязана называть свою ЧАСТЬ** (B1): «понятная» '
               u'считана с матрицей отклика, «непонятная» — из одних пиков, и смешивать их\n'
               u'нельзя.\n')

    flagged = [r for r in rows if r['состояние'] == BAD]
    out.append(u'## Состояние: %s %d, %s %d из %d\n'
               % (OK, len(rows) - len(flagged), BAD, len(flagged), len(rows)))
    out.append(u'%s — у спектра есть всё, что положено ЕГО ЧАСТИ. %s — чего-то нет,\n'
               u'и колонка «чего нет» называет что. Отсутствие геометрии у непонятной\n'
               u'части флагом НЕ считается: это её определение (B1), а не изъян, —\n'
               u'иначе красной стала бы почти вся таблица и колонка ничего бы не значила.\n'
               % (OK, BAD))
    if flagged:
        out.append(u'| спектр | часть | чего нет |')
        out.append(u'|---|---|---|')
        for r in sorted(flagged, key=lambda r: (r['часть'], r['группа'], r['спектр'])):
            out.append(u'| `%s` | %s | %s |' % (r['спектр'], r['часть'], r['чего_нет']))
        out.append(u'')

    air = [r for r in rows if r['проба'] == 'Air, dry']
    out.append(u'## Проба-воздух: %d %s\n' % (
        len(air),
        u'— чисто' if not air else u'⛔ ГЕОМЕТРИЙ С НЕЗАПОЛНЕННОЙ ПРОБОЙ (строка `E19`)'))
    if air:
        for r in air:
            out.append(u'* `%s` — геометрия `%s`, сосуд «%s»' % (r['спектр'], r['геометрия'], r['сосуд']))
        out.append(u'')

    for part in sorted({r['часть'] for r in rows}, key=lambda p: order.get(p, 9)):
        sel = [r for r in rows if r['часть'] == part]
        out.append(u'## %s — %d спектров\n' % (title.get(part, part), len(sel)))
        out.append(u'| | чего нет | спектр | группа | истина | отсчётов | ПШПВ 662 | фон | геометрия | сосуд | проба | ρ | матрица |')
        out.append(u'|---|---|---|---|---|---|---|---|---|---|---|---|---|')
        for r in sorted(sel, key=lambda r: (r['состояние'] != BAD, r['группа'], r['спектр'])):
            out.append(u'| %s | %s | `%s` | %s | %s | %s | %s %% | %s | %s | %s | %s | %s | %s |' % (
                r['состояние'], r['чего_нет'] or '—',
                r['спектр'], r['группа'], r['истина'] or '—', r['отсчётов'],
                r['ПШПВ_662_%'], r['фон'], r['геометрия'], r['сосуд'] or '—',
                r['проба'] or '—', r['ρ_пробы'] or '—', r['матрица']))
        out.append(u'')

    io.open(path, 'w', encoding='utf-8', newline='\n').write(u'\n'.join(out))
    print('документ: %s' % path)
    return path


def main():
    rows = build()
    write_csv(rows)
    write_md(rows)
    by_part = {}
    for r in rows:
        by_part[r['часть']] = by_part.get(r['часть'], 0) + 1
    print('спектров: %d  (%s)' % (len(rows), ', '.join(
        '%s %d' % (k, v) for k, v in sorted(by_part.items()))))
    air = [r['спектр'] for r in rows if r['проба'] == 'Air, dry']
    print('проба осталась воздухом: %d%s' % (len(air), (' — ' + ', '.join(air)) if air else ''))
    flagged = [r for r in rows if r['состояние'] == BAD]
    print('состояние: %s %d, %s %d' % (OK, len(rows) - len(flagged), BAD, len(flagged)))
    for r in flagged:
        print('   %s %-22s %-9s %s' % (BAD, r['спектр'], r['часть'], r['чего_нет']))


if __name__ == '__main__':
    main()
