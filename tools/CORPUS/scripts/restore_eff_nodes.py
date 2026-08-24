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

NODE = re.compile(r'<Efficiency>\s*<Guid>.*</Efficiency>', re.S)

#: Тот же узел, но только его начало — для вопроса «он уже на месте?».
#: ⚠ Спрашивать это подстрокой `'<Efficiency>' in text` НЕЛЬЗЯ, и до 16.08.2026
#: здесь спрашивалось именно так: в файле С КРИВОЙ тег встречается 35 раз —
#: каждая точка кривой записана им же (`<Efficiency>0.00636…</Efficiency>`
#: внутри `ROIEfficiencyData`).
HEAD = re.compile(r'<Efficiency>\s*<Guid>')

#: Имя кривой в узле — по нему видно, ЧЬЯ она.
NAME = re.compile(r'<Efficiency>\s*<Guid>[^<]*</Guid>\s*<Name>([^<]*)</Name>')


def node_name(text):
    m = NAME.search(text)
    return m.group(1).strip() if m else None


def wanted_geometry(scope='geometry'):
    u"""Спектр -> геометрия, которую он обязан нести (`corpus/parts.csv`).

    ⛔ КОМУ ПОЛОЖЕНА КРИВАЯ — решение Amber 24.08.2026: **всем, у кого есть
    геометрия**, а не только понятной части (`W29`). До этого правило нигде не
    было записано, и раздача шла побочным действием: инструмент берёт всякую
    непустую клетку `geometry`, а она есть и у трёх спектров НЕПОНЯТНОЙ части
    (`ASN16_Lu176_P0`, `ASN16_Cs137`, `AS80_Th232WT20`). Заметили это только
    когда узлов стало три вместо одного — то есть по изменению, а не по
    правилу.

    ⚠ Цена правила названа и принята: непонятная часть перестаёт быть
    однородной по модели — у трёх спектров из 40 образ строится С кривой, у
    остальных из одних пиков. Числа непонятной части читать с этой оговоркой.

    `scope='known'` — прежнее поведение (только понятная часть). Оставлено
    ключом, чтобы разницу можно было ЗАМЕРИТЬ, а не обсуждать; ⚠ снять узлы у
    двух из трёх в лоб нельзя — проверено 24.08.2026, удаление всех
    `<Efficiency>` ломает разбор двух спектров (ошибок 0 → 2).
    """
    import csv
    path = os.path.join(SPECTRA, os.pardir, 'parts.csv')
    if not os.path.isfile(path):
        return {}
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        rows = [r for r in csv.DictReader(fh) if r.get('geometry')]
    if scope == 'known':
        rows = [r for r in rows if r.get('part') == 'known']
    return {r['spectrum']: r['geometry'] for r in rows}


def parts_of():
    u"""Спектр -> часть корпуса; нужна, чтобы раздача НАЗЫВАЛА себя (`W29`)."""
    import csv
    path = os.path.join(SPECTRA, os.pardir, 'parts.csv')
    if not os.path.isfile(path):
        return {}
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return {r['spectrum']: r.get('part', '') for r in csv.DictReader(fh)}

# B6 (решение Amber 15.08.2026): двенадцать ключей `G1S_*` сняты как побайтные
# дубликаты эталонов, а геометрии и матрицы перевешены на эталоны-оригиналы.
# Узлы `<Efficiency>` лежат в git ПОД ПРЕЖНИМИ ИМЕНАМИ, поэтому источник ищется
# по этой таблице. Убрать её будет можно, когда узлы новых ключей окажутся
# закоммичены хотя бы раз, — но убирать не нужно: она не мешает и объясняет,
# откуда взялся узел.
RENAMED = {
    'G1S16_Th228_P5':         'G1S_Th228_5cm',
    'G1S16_Eu152_P5':         'G1S_Eu152_5cm',
    'G1S16_Eu152_P25':        'G1S_Eu152_25cm',
    'G1S16_Co60_P25':         'G1S_Co60_25cm',
    'G1S16_Ba133_P25':        'G1S_Ba133_25cm',
    'G1S24_Th228_P25':        'G1S_Th228_25cm',
    'G1S24_Th232_Denta120_2': 'G1S_Th232_Denta',
    'G1S24_Ra226_Denta120':   'G1S_Ra226_Denta',
    'G1S24_K40_Denta120':     'G1S_K40_Denta',
    'G1S24_Th232_Petri_2':    'G1S_Th232_Petri',
    'G1S24_Ra226_Petri':      'G1S_Ra226_Petri',
    'G1S24_Th232_Mar_2':      'G1S_Th232_Marinelli',
}


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
    ap.add_argument('--scope', default='geometry', choices=('geometry', 'known'),
                    help=u'кому положена кривая: всем с геометрией (решение Amber '
                         u'24.08.2026, W29) или только понятной части')
    args = ap.parse_args()

    want = wanted_geometry(args.scope)
    parts = parts_of()

    # Готовые узлы по ГЕОМЕТРИИ — донорский запас для спектров, которым
    # геометрию назначили только что и в git их узла нет (см. ниже).
    donors = {}
    for path in sorted(glob.glob(os.path.join(args.spectra, '*.xml'))):
        key = os.path.splitext(os.path.basename(path))[0]
        need = want.get(key)
        if not need or need in donors:
            continue
        text = io.open(path, encoding='utf-8-sig').read()
        m = NODE.search(text)
        if m is not None and node_name(text) == need:
            donors[need] = m.group(0)

    restored, already, missing, foreign = 0, 0, 0, 0
    declined, undue = [], []
    for path in sorted(glob.glob(os.path.join(args.spectra, '*.xml'))):
        key = os.path.splitext(os.path.basename(path))[0]
        text = io.open(path, encoding='utf-8-sig').read()

        # ⛔ Возвращать узел ТОЛЬКО тому, кому он положен (`W29`, решение Amber
        # 24.08.2026). До 24.08.2026 это условие проверялось лишь у УЖЕ
        # СТОЯЩЕГО узла — чей он, — а решение ВОЗВРАЩАТЬ принималось по одному
        # признаку «в git узел был». Поэтому `ASN16_Lu176_P0` получал его
        # обратно на каждой пересборке, хотя его геометрия `ASN16_lu_front`
        # снята решением Amber 17.08.2026 (`B19`): ни `.in`, ни `.rmx`, ни
        # строки в `index.csv` нет, матрица по guid не находится — и спектр
        # разбирается с КРИВОЙ съёмки, которую сама Amber назвала ошибочной.
        if key not in want:
            if HEAD.search(text) is not None:
                # Узел уже стоит, а положен не был. Снять его ЗДЕСЬ нельзя: это
                # смена базы корпуса, решение Amber, — да и в лоб не выходит
                # (проверено 24.08.2026: удаление всех `<Efficiency>` ломает
                # разбор двух спектров, ошибок 0 → 2). Дело сторожа — назвать.
                name = node_name(text)
                guid = re.search(r'<Efficiency>\s*<Guid>([^<]+)', text)
                geom = os.path.join(args.spectra, os.pardir, 'geometries')
                dead = not os.path.isfile(os.path.join(geom, (name or '') + '.in'))
                nomx = guid is None or not os.path.isfile(
                    os.path.join(geom, 'response', guid.group(1) + '.rmx'))
                undue.append('%s (кривая «%s»%s)'
                             % (key, name,
                                u'; ГЕОМЕТРИИ НЕТ на диске' if dead else '')
                             + (u' [матрицы по guid нет]' if nomx else ''))
            else:
                rel = os.path.relpath(os.path.abspath(path), REPO).replace(os.sep, '/')
                old = from_git(args.rev, rel)
                if old is not None and NODE.search(old) is not None:
                    declined.append(key)
            continue

        if HEAD.search(text) is not None:
            # ⚠ «Узел есть» и «узел ТОТ» — разные вещи, и 16.08.2026 разница
            # стоила прогона. Пересборка строит копию из ИСХОДНОГО файла
            # библиотеки, а у него бывает свой узел `<Efficiency>` — у
            # `!ASN16\Lu176.xml` это кривая «Цилиндр». Такой узел проходил как
            # «уже на месте», корпусный не возвращался, матрицы под чужой guid
            # не находилось, и спектр понятной части ТИХО считался без матрицы:
            # в сводке «с матр. 16 из 17», и всё.
            #
            # Поэтому спрашивается имя: узел годен, только если он несёт ТУ
            # геометрию, которая назначена спектру в `parts.csv`.
            name = node_name(text)
            need = want.get(key)
            if need is None or name == need:
                already += 1
                continue

            print('%-24s ЧУЖОЙ узел «%s», нужен «%s» — заменяю'
                  % (key, name, need))
            foreign += 1
            text = NODE.sub('', text, count=1)

        rel = os.path.relpath(os.path.abspath(path), REPO).replace(os.sep, '/')
        old = from_git(args.rev, rel)
        src_key = key
        donor_note = ''
        if old is None or NODE.search(old) is None:
            # переименованный ключ (B6): узел лежит в git под прежним именем
            prev = RENAMED.get(key)
            if prev:
                alt = rel.rsplit('/', 1)[0] + '/' + prev + '.xml'
                cand = from_git(args.rev, alt)
                if cand is not None and NODE.search(cand) is not None:
                    old, src_key = cand, prev
        node = None
        if old is not None:
            m = NODE.search(old)
            if m is not None:
                node = m.group(0)

        if node is None:
            # Спектра с этим узлом в git нет вовсе — так бывает у спектра,
            # которому геометрию НАЗНАЧИЛИ ТОЛЬКО ЧТО. Берём узел у СОСЕДА по
            # той же геометрии: узел несёт кривую геометрии, а не спектра, и у
            # всех спектров одной геометрии он обязан быть одним и тем же.
            # Именно так 16.08.2026 в понятную часть въехали тридцать две
            # точечные съёмки поверки: сцена у них одна (паспорт ОСГИ —
            # `Material=not essential`, `Mass,g=0`), а узла не было ни у одной.
            need = want.get(key)
            node, donor = (donors.get(need), need) if need else (None, None)
            if node is None:
                continue

            src_key = None
            donor_note = '   [узел взят у соседа по геометрии «%s»]' % donor

        m = re.search(r'<Guid>', node)
        anchor = '</ROIConfigReference>' if '</ROIConfigReference>' in text \
            else '</DeviceConfigReference>'
        if anchor not in text:
            print('%-22s НЕКУДА вставить (нет %s)' % (key, anchor))
            missing += 1
            continue

        guid = re.search(r'<Guid>([^<]+)', node)
        name = re.search(r'<Name>([^<]+)', node)
        print('%-24s <- %s (%s, %d символов)%s'
              % (key, name.group(1) if name else '?',
                 guid.group(1)[:8] if guid else '?', len(node),
                 donor_note if src_key is None
                 else ('' if src_key == key else '   [из ' + src_key + ', B6]')))
        restored += 1
        if not args.apply:
            continue

        i = text.index(anchor) + len(anchor)
        io.open(path, 'w', encoding='utf-8', newline='').write(
            u'﻿' + text[:i] + node + text[i:])

    print()
    print('вернуть узлов: %d (из них взамен ЧУЖИХ: %d); уже на месте: %d; без места: %d%s'
          % (restored, foreign, already, missing,
             '' if args.apply else '  (--apply не задан, файлы не тронуты)'))

    # `W29`: раздача обязана НАЗЫВАТЬ себя. Прежде она молчала, и то, что кривая
    # досталась трём спектрам непонятной части вместо одного, увидели по
    # изменению числа, а не по правилу.
    by_part = {}
    for key in sorted(want):
        by_part.setdefault(parts.get(key, '?'), []).append(key)
    print(u'кому кривая положена (--scope=%s): %s'
          % (args.scope,
             ', '.join('%s %d' % (p, len(v)) for p, v in sorted(by_part.items()))))
    others = [k for p, v in by_part.items() if p != 'known' for k in v]
    if others:
        print(u'   ⚠ из них ВНЕ понятной части: %s' % ', '.join(sorted(others)))
        print(u'      это правило, а не оплошность (решение Amber 24.08.2026, W29);')
        print(u'      цена — непонятная часть не однородна по модели, числа с оговоркой')
    if declined:
        print(u'   НЕ ВОЗВРАЩЁН, хотя в git узел есть: %s' % ', '.join(sorted(declined)))
        print(u'      геометрии у них нет — узел не положен (W29)')
    if undue:
        print(u'   УЗЕЛ СТОИТ, А НЕ ПОЛОЖЕН:')
        for line in sorted(undue):
            print(u'      %s' % line)
        print(u'      Этот инструмент их туда не клал: узел приезжает С КОПИЕЙ —')
        print(u'      либо из исходного файла библиотеки (пересборка копирует')
        print(u'      ResultData целиком, а спектр там сохранён с прикреплённой')
        print(u'      кривой), либо из прошлого коммита. Снять — решение Amber:')
        print(u'      это меняет числа непонятной части, то есть базу корпуса.')


if __name__ == '__main__':
    main()
