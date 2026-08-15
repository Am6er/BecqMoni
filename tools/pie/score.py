#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Скоринг полноспектральной декомпозиции (pie) против истины корпуса.

Читает tools/CORPUS/corpus/manifest.csv и out/<группа>_<режим>_components.csv,
считает по каждому спектру попадания (все ли компоненты истины найдены)
и фантомы (найденные компоненты, которых в образце нет).

Критерий «найден»: share >= S_THR (доля в «пироге») И z >= Z_THR.

Соответствие истине. Цепочка манифеста разворачивается в компоненты pie
по CHAIN_MAP: равновесный «U-238» манифеста = «U-238 (голова)» + «Ra-226»,
и найти требуется ОБА; «U-238u» (урановое стекло) — только голова. Th-228
и Th-232 — одно семейство (подцепочка засчитывается за цепочку и
наоборот). Для спектров без вычтенного/встроенного фона комнатные
Th-232/Ra-226/K-40 считаются «мягкими» фантомами: физически они в
спектре есть.

Покрытие. Для каждой группы, чей файл найден в каталоге результатов,
множество спектров сверяется с манифестом: спектр без строк в
components.csv (упал — ERROR в *_runs.csv — или не был прогнан) попадает
в таблицу как «НЕТ РЕЗУЛЬТАТА» и целиком считается промахом. Recall по
молчаливому подмножеству не бывает.

Часть корпуса (`--part`). С 09.08.2026 корпус разделён `corpus/parts.csv` на
«понятную» часть (геометрия восстановлена, матрица отклика есть) и
«непонятную» (ни того, ни другого); германий помечен `excluded` и не
считается никогда (приказ Amber 08.08.2026). Это две разные модели, и
общее число по ним — среднее двух разных вещей, поэтому часть печатается
в шапке и в итоговой строке ВСЕГДА, даже когда взят весь корпус.

Состав приложения (`--members`). `tools/pie` раскладывает спектр на ЦЕПОЧКИ
(компонент «Th-232»), а полноспектральный разбор в самой программе
(`CorpusFsaProbe`) — на ДОЧЕРНИЕ нуклиды: состав задаёт поиск пиков, а он
подписывает пики Ac-228, Pb-212, Tl-208. Ключ разворачивает цепочку
манифеста в её членов; цепочка засчитана, если найден хоть один из них.
Список членов взят из `nucdb` (`tools/CORPUS/scripts/chains.py`,
`chain_branches` с отсечкой 1e-4), а не выдуман.

Запуск:  python tools/pie/score.py [--sthr 3] [--zthr 4] [--mode snip]
                                   [--part known|unknown|all] [--members]
"""
import argparse
import csv
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, '..', 'CORPUS', 'corpus', 'manifest.csv')
DETECTORS = os.path.join(HERE, '..', 'CORPUS', 'corpus', 'detectors.csv')
PARTS = os.path.join(HERE, '..', 'CORPUS', 'corpus', 'parts.csv')
OUT = os.path.join(HERE, 'out')

# манифест -> компоненты. Компонент U-238 в pie — только голова ряда
# (радиевая ветвь вырезана и живёт в Ra-226), поэтому равновесный «U-238»
# манифеста означает оба компонента, а «U-238u» (стекло) — только голову.
CHAIN_MAP = {
    'Th-232': ['Th-232'], 'Th-228': ['Th-228'], 'Ra-226': ['Ra-226'],
    'U-238': ['U-238', 'Ra-226'], 'U-238u': ['U-238'], 'U-235': ['U-235'],
}
NUCLIDE_MAP = {
    '40K': 'K-40', '137CS': 'Cs-137', '241AM': 'Am-241', '60CO': 'Co-60',
    '131I': 'I-131', '152EU': 'Eu-152', '133BA': 'Ba-133', '176LU': 'Lu-176',
    '88Y': None, '139CE': None,   # компонентов в библиотеке нет
}
# компонент -> семейство (взаимозачёт при коллинеарных цепочках)
FAMILY = {
    'Th-232': 'thorium', 'Th-228': 'thorium',
}
ROOM = {'Th-232', 'Ra-226', 'K-40'}

# Члены цепочек для `--members`: разбор в приложении раскладывает спектр на
# ДОЧЕРНИЕ нуклиды, потому что состав библиотеки задаёт поиск пиков, а он
# подписывает пики Ac-228, Pb-212, Tl-208 (см. FsaLibrary.BuildFromPeaks —
# «разрез цепочки получается сам»). Списки сняты из nucdb:
#
#   python -c "import chains; print(chains.chain_branches('232TH', chains.conn(), 1e-4))"
#
# Радиевая ветвь у U-238 не повторяется нарочно: манифест уже разворачивает
# равновесный «U-238» в «U-238 + Ra-226» через CHAIN_MAP, и дочерние радия
# принадлежат семейству Ra-226 в обоих случаях. Стабильные концы (Pb-206,
# Pb-207, Pb-208) выброшены — у них нет ни распада, ни линий.
CHAIN_MEMBERS = {
    'Th-232': ['Th-232', 'Ra-228', 'Ac-228', 'Th-228', 'Ra-224', 'Rn-220',
               'Po-216', 'Pb-212', 'Bi-212', 'Tl-208', 'Po-212'],
    'Th-228': ['Th-228', 'Ra-224', 'Rn-220', 'Po-216', 'Pb-212', 'Bi-212',
               'Tl-208', 'Po-212'],
    'Ra-226': ['Ra-226', 'Rn-222', 'Po-218', 'At-218', 'Pb-214', 'Bi-214',
               'Po-214', 'Pb-210', 'Bi-210', 'Po-210', 'Tl-210'],
    'U-238': ['U-238', 'Th-234', 'Pa-234m', 'Pa-234', 'U-234', 'Th-230'],
    'U-235': ['U-235', 'Th-231', 'Pa-231', 'Ac-227', 'Th-227', 'Fr-223',
              'Ra-223', 'Rn-219', 'Po-215', 'Pb-211', 'Bi-211', 'Tl-207',
              'Po-211'],
}
# дочерний -> семейство цепочки; заполняется при --members
MEMBER_FAMILY = {}


def family(comp):
    if MEMBER_FAMILY:
        return MEMBER_FAMILY.get(comp, FAMILY.get(comp, comp))
    return FAMILY.get(comp, comp)


def enable_members():
    """Развернуть цепочки в дочерние нуклиды (состав приложения)."""
    for chain, members in CHAIN_MEMBERS.items():
        fam = FAMILY.get(chain, chain)
        for member in members:
            # Столкновений быть не должно: радиевая ветвь названа один раз.
            # Если появится — молчать нельзя, иначе дочерний уедет в чужое
            # семейство и станет то промахом, то фантомом.
            if MEMBER_FAMILY.get(member, fam) != fam:
                sys.exit('дочерний %s принадлежит двум семействам: %s и %s'
                         % (member, MEMBER_FAMILY[member], fam))
            MEMBER_FAMILY[member] = fam


def load_parts():
    """Спектр -> часть корпуса (known / unknown / excluded)."""
    parts = {}
    if not os.path.exists(PARTS):
        return parts
    with open(PARTS, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            if row.get('spectrum'):
                parts[row['spectrum']] = row['part']
    return parts


def load_resolutions():
    """Группа -> ПШПВ на 662 кэВ, % (последняя колонка detectors.csv)."""
    res = {}
    with open(DETECTORS, encoding='utf-8-sig') as fh:
        for row in csv.reader(fh):
            if len(row) < 2 or not row[0] or row[0].startswith('#'):
                continue
            try:
                res[row[0]] = float(row[-1])
            except ValueError:
                continue          # строка заголовка
    return res


def load_truth():
    truth = {}
    with open(MANIFEST, encoding='utf-8-sig') as fh:
        for row in csv.DictReader(fh):
            comps = set()
            for ch in (row['chains'] or '').split(';'):
                ch = ch.strip()
                if not ch:
                    continue
                if ch not in CHAIN_MAP:
                    sys.exit('манифест: неизвестная цепочка %r у %s' % (ch, row['key']))
                comps.update(CHAIN_MAP[ch])
            for nu in (row['nuclides'] or '').split(';'):
                nu = nu.strip()
                if nu and NUCLIDE_MAP.get(nu):
                    comps.add(NUCLIDE_MAP[nu])
            truth[row['key']] = {
                'components': comps,
                'has_bg': row['background'] not in ('', 'нет', '-'),
                'det': row['det'],
            }
    return truth


def load_results(mode, out_dir):
    """(spectrum -> строки components.csv, группы из имён файлов,
    спектры с ERROR в парном *_runs.csv)."""
    suffix = '_%s_components.csv' % mode
    results = defaultdict(list)
    source = {}
    groups = set()
    errors = set()
    chi2 = {}
    eps = {}
    for name in sorted(os.listdir(out_dir)):
        if not name.endswith(suffix):
            continue
        group = name[:-len(suffix)]
        groups.add(group)
        with open(os.path.join(out_dir, name), encoding='utf-8-sig') as fh:
            for row in csv.DictReader(fh):
                spec = row['spectrum']
                if spec in source and source[spec] != name:
                    print('ВНИМАНИЕ: %s есть и в %s, и в %s — строки смешаны'
                          % (spec, source[spec], name), file=sys.stderr)
                source[spec] = name
                results[spec].append(row)
        runs = os.path.join(out_dir, '%s_%s_runs.csv' % (group, mode))
        if os.path.exists(runs):
            with open(runs, encoding='utf-8-sig') as fh:
                for row in csv.DictReader(fh):
                    if row.get('chi2ndf') == 'ERROR':
                        errors.add(row['spectrum'])
                        continue
                    try:
                        chi2[row['spectrum']] = float(row['chi2ndf'])
                    except (TypeError, ValueError):
                        pass
                    # S51: невязка модели. Колонка молодая — у прежних прогонов
                    # её нет, и это не повод падать: сводка тогда печатает
                    # только chi2/ndf, как раньше.
                    try:
                        eps[row['spectrum']] = float(row['model_residual_pct'])
                    except (TypeError, ValueError, KeyError):
                        pass
    return results, groups, errors, chi2, eps


def warn_members(results, members_on):
    """Сказать, если состав назван ДОЧЕРНИМИ, а считают по цепочкам.

    Ошибка старая и дорогая: выход `CorpusFsaProbe` называет состав дочерними
    нуклидами (Ac-228, Pb-212, Tl-208 — так их подписывает поиск пиков), а
    манифест — цепочками (Th-232). Без `--members` цепочка не засчитывается ни
    разу, и recall выходит примерно вдвое хуже правды. В завещаниях это
    записано словами трижды, но словами: сам скрипт молчал и выдавал ровные
    неверные числа, по которым делались выводы.

    Признак — прямой: среди названных компонентов есть ЧЛЕНЫ цепочек, которые
    сами цепочками не являются. Пусто — считают выход `tools/pie` (он раскладывает
    на цепочки), и ключ действительно не нужен.
    """
    if members_on:
        return

    daughters = set()
    for chain, members in CHAIN_MEMBERS.items():
        for member in members:
            if member not in CHAIN_MEMBERS:
                daughters.add(member)

    seen = set()
    for rows in results.values():
        for row in rows:
            name = row.get('component')
            if name in daughters:
                seen.add(name)

    if not seen:
        return

    print('⚠ СОСТАВ НАЗВАН ДОЧЕРНИМИ, А СЧИТАЕТСЯ ПО ЦЕПОЧКАМ — добавьте --members',
          file=sys.stderr)
    print('   встречены: %s%s'
          % (', '.join(sorted(seen)[:8]), ' …' if len(seen) > 8 else ''),
          file=sys.stderr)
    print('   без ключа цепочка не засчитывается ни разу: recall выйдет примерно '
          'вдвое хуже правды', file=sys.stderr)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sthr', type=float, default=3.0, help='порог доли, %%')
    ap.add_argument('--zthr', type=float, default=4.0, help='порог z')
    ap.add_argument('--mode', default='snip', choices=['snip', 'spline'])
    ap.add_argument('--out-dir', default=OUT,
                    help='каталог с <группа>_<режим>_{components,runs}.csv')
    ap.add_argument('--verbose', action='store_true')
    # Приборы с разрешением лучше 3 % (HPGe, CZT) — вне предмета: там пик
    # разрешается сам, и полноспектральная декомпозиция решает другую задачу.
    # Порог 0 возвращает весь корпус.
    ap.add_argument('--min-fwhm', type=float, default=0.0,
                    help='нижняя граница ПШПВ на 662 кэВ, %% (3 — только сцинтилляторы)')
    ap.add_argument('--part', default='all', choices=['all', 'known', 'unknown'],
                    help='часть корпуса по corpus/parts.csv; германий (excluded) '
                         'не считается никогда')
    ap.add_argument('--members', action='store_true',
                    help='состав назван ДОЧЕРНИМИ нуклидами (разбор приложения), '
                         'а не цепочками: развернуть цепочки манифеста в их членов')
    args = ap.parse_args()

    if args.members:
        enable_members()

    truth = load_truth()

    # Что вообще есть в манифесте — запоминается ДО отбора по части: иначе
    # спектр, выброшенный отбором, попадает в предупреждение «есть в
    # результатах, но не в манифесте», которое означает совсем другое (ключ
    # разошёлся с описью). Полсотни ложных строк подряд гасят настоящую.
    in_manifest = set(truth)

    # Часть корпуса. Отбор идёт ДО скоринга, потому что покрытие считается по
    # тем же строкам: спектр, выброшенный из истины, не должен всплыть в
    # «НЕТ РЕЗУЛЬТАТА».
    parts = load_parts()
    if parts:
        truth = {k: t for k, t in truth.items()
                 if parts.get(k, 'unknown') != 'excluded'
                 and (args.part == 'all' or parts.get(k, 'unknown') == args.part)}
    elif args.part != 'all':
        sys.exit('нет %s — часть корпуса выбрать нечем' % PARTS)
    results, groups, errors, chi2, eps = load_results(args.mode, args.out_dir)
    if not results:
        sys.exit('нет результатов режима %s в %s' % (args.mode, args.out_dir))

    warn_members(results, args.members)

    resolution = load_resolutions()
    if args.min_fwhm > 0.0:
        dropped = sorted(d for d, f in resolution.items() if f < args.min_fwhm)
        truth = {k: t for k, t in truth.items()
                 if resolution.get(t['det'], 0.0) >= args.min_fwhm}
        print('исключены по разрешению < %.1f %%: %s'
              % (args.min_fwhm, ', '.join(dropped) or '-'))

    # покрытие: группа, чей файл есть в результатах, обязана содержать все
    # свои спектры из манифеста — упавший спектр не оставляет строк в
    # components.csv и иначе молча выпадал бы из recall
    missing = sorted(k for k, t in truth.items()
                     if t['det'] in groups and k not in results)
    for s in sorted(results):
        if s not in in_manifest:
            print('ВНИМАНИЕ: %s есть в результатах, но не в манифесте — не скорится'
                  % s, file=sys.stderr)

    per_det = defaultdict(lambda: [0, 0, 0, 0])  # hits, truths, phantoms, spectra
    total_soft = 0
    print('режим=%s, часть корпуса=%s%s, критерий: share>=%.1f%% и z>=%.1f'
          % (args.mode, args.part, ', состав по дочерним' if args.members else '',
             args.sthr, args.zthr))
    print()
    print('%-22s %-28s %-34s %s' % ('спектр', 'истина', 'найдено (доля%/z)', 'вердикт'))
    for spectrum in sorted(set(results) | set(missing)):
        if spectrum not in truth:
            continue
        t = truth[spectrum]
        truth_fams = {family(c) for c in t['components']}
        acc = per_det[t['det']]

        if spectrum not in results:
            why = 'ERROR в runs.csv' if spectrum in errors else 'нет строк в components.csv'
            print('%-22s %-28s %-34s %s' % (
                spectrum, '+'.join(sorted(t['components'])) or '-', '-',
                'НЕТ РЕЗУЛЬТАТА (%s) — весь спектр в промах' % why))
            acc[1] += len(truth_fams)
            acc[3] += 1
            continue

        detected = {}
        for row in results[spectrum]:
            if row['kind'] == 'nuisance':
                continue
            share = float(row['share_pct'])
            z = float(row['z'])
            if share >= args.sthr and z >= args.zthr:
                detected[row['component']] = (share, z)

        det_fams = {family(c) for c in detected}

        hits = truth_fams & det_fams
        misses = truth_fams - det_fams
        phantom_comps = [c for c in detected if family(c) not in truth_fams]
        hard, soft = [], []
        for c in phantom_comps:
            if not t['has_bg'] and c in ROOM:
                soft.append(c)
            else:
                hard.append(c)

        det_str = ', '.join('%s(%.0f/%.0f)' % (c, s, z)
                            for c, (s, z) in sorted(detected.items(), key=lambda kv: -kv[1][0]))
        verdict = []
        if misses:
            verdict.append('MISS:' + '+'.join(sorted(misses)))
        if hard:
            verdict.append('PHANTOM:' + '+'.join(sorted(hard)))
        if soft:
            verdict.append('room:' + '+'.join(sorted(soft)))
        if not verdict:
            verdict.append('ok')
        print('%-22s %-28s %-34s %s' % (
            spectrum, '+'.join(sorted(t['components'])) or '-', det_str or '-', ' '.join(verdict)))

        acc[0] += len(hits)
        acc[1] += len(truth_fams)
        acc[2] += len(hard)
        acc[3] += 1
        total_soft += len(soft)

    print()
    print('%-10s %8s %10s %10s' % ('детектор', 'спектров', 'recall', 'фантомов'))
    th = tt = tp = ts = 0
    for det in sorted(per_det):
        h, t_, p, n = per_det[det]
        th += h; tt += t_; tp += p; ts += n
        print('%-10s %8d %9.0f%% %10d' % (det, n, 100.0 * h / t_ if t_ else 0, p))
    # Итог ВСЕГДА называет свою часть: числа понятной и непонятной частей
    # относятся к разным моделям (образ с матрицей против образа из одних
    # пиков), и строка «итого» без имени части читается как корпусная.
    print('%-10s %8d %9.0f%% %10d  (+%d комнатных)  часть: %s' % (
        'итого', ts, 100.0 * th / tt if tt else 0, tp, total_soft, args.part))

    # χ²/ndf по тем же спектрам, что вошли в скоринг: сумма — чтобы сравнивать
    # прогоны между собой, медиана — чтобы один тяжёлый спектр не решал всё.
    scored = sorted(chi2[s] for s in chi2 if s in truth)
    if scored:
        mid = scored[len(scored) // 2] if len(scored) % 2 else \
            0.5 * (scored[len(scored) // 2 - 1] + scored[len(scored) // 2])
        # без греческих букв: консоль под cp1251 их не печатает
        print('%-10s %8d  sum chi2/ndf %.1f   медиана %.2f'
              % ('', len(scored), sum(scored), mid))

    # S51: НЕВЯЗКА МОДЕЛИ — рядом с chi2/ndf, а не вместо. Сумма для неё
    # бессмысленна (это доля, а не вклад), поэтому медиана и квартили:
    # chi2/ndf растёт со статистикой и между спектрами несравним, невязка
    # сравнима. Измерено по корпусу: r(lg отсчётов, lg chi2/ndf) = +0.72,
    # r(lg отсчётов, невязка) = -0.03.
    se = sorted(eps[s] for s in eps if s in truth)
    if se:
        def quant(p):
            i = max(0, min(len(se) - 1, int(round(p * (len(se) - 1)))))
            return se[i]
        print('%-10s %8d  model residual медиана %.1f %%   кварт. %.1f .. %.1f %%'
              % ('', len(se), quant(0.5), quant(0.25), quant(0.75)))


if __name__ == '__main__':
    main()
