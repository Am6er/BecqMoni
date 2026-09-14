# -*- coding: utf-8 -*-
u"""`V18` (полоса F56): стало ли ЛУЧШЕ — по расстоянию известных линий от их
каналов, а не по невязке подгонки.

⛔ Невязкой подгонки эту правку мерить НЕЛЬЗЯ по построению: плата за степень
свободы меняет как раз то, какому кандидату разрешено уронить невязку, и rms
подгонки — ровно та величина, которой машинка себя обманывает. Мерка здесь
внешняя, та же, что калибровала запрет `B24`: канал линии определяется ОДИН РАЗ
по данным (согласный у всех вариантов, `ecal_compare.consensus`), и каждый
вариант оценивается ТОЛЬКО своей кривой на неподвижном наборе.

    python calib_quality_f56.py --dump=<файл.json> [--rule=df:1] [--scope=all]
                                [--order=2] [--only=k1,k2]
    python calib_quality_f56.py --compare=a.json,b.json[,c.json] [--band=200]
                                [--csv=<файл>]

Слепок пишется в том же формате, что `ecal_extrapolation.py --dump=`, и той же
стадией 1+2а (`build_state`); отличается только правило отбора.
"""
import csv
import io
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

import corpus_calib as cc                              # noqa: E402
import ecal_compare as ecmp                            # noqa: E402


def make_dump(path, rule, coef, scope, order, only, min_free_df=1):
    import build_corpus
    import corpus_def
    import ecal_extrapolation as ee
    import calib_sweep_f56 as sweep

    sweep.set_rule(rule, coef, scope, order, min_free_df)
    print(u'правило отбора: %s:%g, плата с %s, степень %d, ν ≥ %d'
          % (rule, coef, cc.MODEL_TEST_SCOPE, cc.MAX_ORDER, cc.MIN_FREE_DF))
    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if only is None or e['key'] in only]
    state = ee.build_state(entries)
    out = {}
    for key, st in state.items():
        if not st['accepted']:
            continue
        res_a = st['r662'] * np.sqrt(662.0)
        ent = dict(st['entry'])
        ent['wanted'] = build_corpus.wanted_lines(st['entry'])
        build_corpus.calibrate.sample_lines = build_corpus.sample_lines
        lines = build_corpus.calibrate.curate(
            ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)), min_purity=0.45)
        found = cc.match_lines(st['sp'].counts, st['ecal'], lines, res_a,
                               tol_fwhm=6.0, width_lo=0.3, width_hi=3.0)
        out[key] = dict(det=st['det'], mode=st['mode'], res_a=res_a, n=st['sp'].n,
                        coef=[float(c) for c in st['ecal'].coef],
                        stored=[float(c) for c in st['sp'].ecal],
                        anchors=sorted(round(float(a['e_ref']), 2) for a in st['accepted']),
                        found={('%.2f' % a['e_ref']): float(a['ch']) for a in found})
    with io.open(path, 'w', encoding='utf-8') as h:
        json.dump(out, h, ensure_ascii=False, indent=1)
    print(u'слепок варианта: %s (%d спектров)' % (path, len(out)))


def per_spectrum(dump, cons, band):
    u"""Спектр -> (число линий, Σ|промах|, худший промах) на неподвижном наборе."""
    out = {}
    for key, c in cons.items():
        st = dump.get(key)
        if not st:
            continue
        res_a = c['res_a']
        n, total, worst = 0, 0.0, 0.0
        for e_ref, ch in sorted(c['lines'].items()):
            if e_ref > band:
                continue
            d = ecmp.energy(st['coef'], ch) - e_ref
            f = d / max(res_a * np.sqrt(max(e_ref, 5.0)), 1e-9)
            n += 1
            total += abs(f)
            if abs(f) > abs(worst):
                worst = f
        if n:
            out[key] = (n, total, worst)
    return out


def compare(paths, band, csv_path, line_set='first'):
    u"""⛔ Набор линий фиксируется ОДИН РАЗ и не должен зависеть от того,
    сколько вариантов сравнивают. `ecal_compare.consensus` берёт согласный
    канал по ВСЕМ поданным слепкам и выбрасывает линии, о канале которых
    варианты спорят, — то есть ровно те, где варианты и расходятся. Измерено
    (журнал F56): одна и та же пара `margin` против `df:1.5` на наборе из двух
    слепков даёт «хуже» (Σ 25.35 -> 25.70 ниже 200 кэВ), а на наборе из семи —
    «лучше» (22.02 -> 21.10), потому что наборы линий разные. Поэтому
    умолчание здесь `first`: канал линии берётся из ПЕРВОГО слепка (нынешний
    ответ корпуса), и все варианты судятся на одном и том же неподвижном
    наборе. `joint` — прежнее поведение, для сверки."""
    dumps = {}
    for p in paths:
        with io.open(p, encoding='utf-8') as h:
            dumps[p] = json.load(h)
    cons, dropped = ecmp.consensus(dumps if line_set == 'joint'
                                   else {paths[0]: dumps[paths[0]]})
    nl = sum(len([e for e in c['lines'] if e <= band]) for c in cons.values())
    print(u'НЕПОДВИЖНЫЙ НАБОР: %d спектров, %d линий ниже %.0f кэВ (спорных отброшено %d)'
          % (len(cons), nl, band, dropped))
    print()
    print(u'%-28s %6s %10s %9s %9s %6s %6s'
          % (u'вариант', u'линий', u'Σ|промах|', u'медиана', u'максимум', u'>0.25', u'>0.50'))
    tab = {}
    for p in paths:
        ps = per_spectrum(dumps[p], cons, band)
        tab[p] = ps
        n = sum(v[0] for v in ps.values())
        total = sum(v[1] for v in ps.values())
        worst = max(ps.values(), key=lambda v: abs(v[2]))
        med = float(np.median([abs(v[2]) for v in ps.values()]))
        print(u'%-28s %6d %10.2f %9.3f %9.3f %6d %6d'
              % (os.path.basename(p).replace('.json', ''), n, total, med, abs(worst[2]),
                 sum(1 for v in ps.values() if abs(v[2]) > 0.25),
                 sum(1 for v in ps.values() if abs(v[2]) > 0.5)))
    base = paths[0]
    rows = []
    print()
    print(u'ПОСПЕКТРОВО против «%s» (порог 0.05 ПШПВ по худшей линии спектра):'
          % os.path.basename(base).replace('.json', ''))
    for p in paths[1:]:
        better, worse, same = [], [], []
        for key in sorted(tab[p]):
            b = tab[base].get(key)
            if b is None:
                continue
            cur = tab[p][key]
            mode_b = dumps[base][key]['mode']
            mode_c = dumps[p][key]['mode']
            d_kev, d_fwhm = _scale_shift(dumps[base][key], dumps[p][key])
            if mode_b == mode_c and d_kev <= 1e-6:
                continue
            verdict = (u'лучше' if abs(cur[2]) < abs(b[2]) - 0.05 else
                       u'ХУЖЕ' if abs(cur[2]) > abs(b[2]) + 0.05 else u'ровно')
            rows.append(dict(variant=os.path.basename(p), spectrum=key,
                             det=dumps[p][key]['det'], mode_base=mode_b, mode_new=mode_c,
                             d_max_kev=round(d_kev, 3), d_max_fwhm=round(d_fwhm, 3),
                             lines=cur[0], worst_base=round(b[2], 3),
                             worst_new=round(cur[2], 3),
                             sum_base=round(b[1], 3), sum_new=round(cur[1], 3),
                             verdict=verdict))
            (better if verdict == u'лучше' else worse if verdict == u'ХУЖЕ'
             else same).append(key)
        print(u'  %s: сменили режим или сдвинулись %d; из них лучше %d, хуже %d, ровно %d'
              % (os.path.basename(p).replace('.json', ''),
                 len(better) + len(worse) + len(same), len(better), len(worse), len(same)))
        for r in rows:
            if r['variant'] != os.path.basename(p):
                continue
            print(u'      %-6s %-24s %-22s -> %-22s  ΔE %8.3f кэВ = %5.2f ПШПВ; '
                  u'худшая линия %+6.2f -> %+6.2f ПШПВ, Σ %6.2f -> %6.2f (%d линий)'
                  % (r['verdict'], r['spectrum'], r['mode_base'], r['mode_new'],
                     r['d_max_kev'], r['d_max_fwhm'], r['worst_base'], r['worst_new'],
                     r['sum_base'], r['sum_new'], r['lines']))
    if csv_path and rows:
        with io.open(csv_path, 'w', encoding='utf-8', newline='') as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print(u'-> %s' % csv_path)


def _scale_shift(a, b):
    grid = np.arange(0, a['n'], dtype=float)
    ea = ecmp.energy(a['coef'], grid)
    eb = ecmp.energy(b['coef'], grid)
    d = np.abs(ea - eb)
    fw = d / (a['res_a'] * np.sqrt(np.maximum(np.abs(ea), 5.0)))
    return float(d.max()), float(fw.max())


def main(argv):
    a = dict(dump=None, rule='df:1', scope='all', order=2, only=None,
             compare=None, band=1e9, csv=None, line_set='first')
    for x in argv:
        if x.startswith('--'):
            k, _, v = x[2:].partition('=')
            if k == 'only':
                a[k] = set(v.split(','))
            elif k == 'compare':
                a[k] = v.split(',')
            elif k == 'band':
                a[k] = float(v)
            elif k == 'order':
                a[k] = int(v)
            elif k == 'line-set':
                a['line_set'] = v
            else:
                a[k] = v
    if a['compare']:
        return compare(a['compare'], a['band'], a['csv'], a['line_set'])
    if not a['dump']:
        raise SystemExit(u'нужен --dump=<файл.json> или --compare=a.json,b.json')
    part = a['rule'].split(':')
    name = part[0]
    val = float(part[1]) if len(part) > 1 and part[1] else 0.0
    nu = int(part[2]) if len(part) > 2 and part[2] else 1
    make_dump(a['dump'], name, val, a['scope'], a['order'], a['only'], nu)


if __name__ == '__main__':
    main(sys.argv[1:])
