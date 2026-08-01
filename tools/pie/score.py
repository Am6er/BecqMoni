#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Скоринг полноспектральной декомпозиции (pie) против истины корпуса.

Читает tools/LibraryFitLab/corpus/manifest.csv и out/<группа>_<режим>_components.csv,
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

Запуск:  python tools/pie/score.py [--sthr 3] [--zthr 4] [--mode snip]
"""
import argparse
import csv
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, '..', 'LibraryFitLab', 'corpus', 'manifest.csv')
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


def family(comp):
    return FAMILY.get(comp, comp)


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
    return results, groups, errors


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sthr', type=float, default=3.0, help='порог доли, %%')
    ap.add_argument('--zthr', type=float, default=4.0, help='порог z')
    ap.add_argument('--mode', default='snip', choices=['snip', 'spline'])
    ap.add_argument('--out-dir', default=OUT,
                    help='каталог с <группа>_<режим>_{components,runs}.csv')
    ap.add_argument('--verbose', action='store_true')
    args = ap.parse_args()

    truth = load_truth()
    results, groups, errors = load_results(args.mode, args.out_dir)
    if not results:
        sys.exit('нет результатов режима %s в %s' % (args.mode, args.out_dir))

    # покрытие: группа, чей файл есть в результатах, обязана содержать все
    # свои спектры из манифеста — упавший спектр не оставляет строк в
    # components.csv и иначе молча выпадал бы из recall
    missing = sorted(k for k, t in truth.items()
                     if t['det'] in groups and k not in results)
    for s in sorted(results):
        if s not in truth:
            print('ВНИМАНИЕ: %s есть в результатах, но не в манифесте — не скорится'
                  % s, file=sys.stderr)

    per_det = defaultdict(lambda: [0, 0, 0, 0])  # hits, truths, phantoms, spectra
    total_soft = 0
    print('режим=%s, критерий: share>=%.1f%% и z>=%.1f' % (args.mode, args.sthr, args.zthr))
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
    print('%-10s %8d %9.0f%% %10d  (+%d комнатных)' % (
        'итого', ts, 100.0 * th / tt if tt else 0, tp, total_soft))


if __name__ == '__main__':
    main()
