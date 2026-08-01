#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Картинка декомпозиции: спектр + образы компонентов + невязка.

Читает дамп модели (pie.exe --dump-model) и CSV компонентов, рисует PNG.
Два стиля:
  stacked (по умолчанию) — послойный: фон и континуум внизу, компоненты
      цветными слоями, верх стека = модель, измеренный спектр линией сверху;
  lines — каждый образ отдельной кривой на общем поле.

  python plot_decomp.py <model.csv> <components.csv> <spectrum> <out.png>
                        [--title ...] [--style stacked|lines]
"""
import argparse
import csv

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

# Цвет закреплён за компонентом (не за рангом в конкретном спектре), чтобы
# картинки разных спектров читались одной легендой. База — Окабе-Ито
# (CVD-safe); идентичность дублируется прямыми подписями.
COLOR_BY_COMPONENT = {
    'Th-232': '#0072B2',   # синий
    'Ra-226': '#D55E00',   # киноварь
    'U-238':  '#E69F00',   # оранжевый
    'U-235':  '#009E73',   # зелёный
    'K-40':   '#F0E442',   # жёлтый — не голубой: Pb-212 из разреза цепочки
                           # взял #56B4E9, а в фоновых спектрах оба — главные слои
    'Cs-137': '#CC79A7',   # розовый
    'Am-241': '#6A3D9A',   # фиолетовый
    'Co-60':  '#332288',
    'I-131':  '#117733',
    'Eu-152': '#44AA99',
    'Ba-133': '#999933',
    'Lu-176': '#88CCEE',
    # дочерние нуклиды ториевой цепочки (--split-chain=Th-232): семья в
    # сине-голубых тонах, чтобы читалась как разрез единой цепочки
    'Ac-228': '#0072B2',
    'Pb-212': '#56B4E9',
    'Tl-208': '#332288',
    'Bi-212': '#88CCEE',
    'Th-228': '#44AA99',
    'Ra-224': '#117733',
    'Xray-W': '#997700',
    'Xray-Pb': '#6B4E3D',
    'SE-2614': '#DDCC77',
    'DE-2614': '#805B3A',
}
FALLBACK = ['#0072B2', '#D55E00', '#009E73', '#CC79A7', '#E69F00', '#56B4E9']
INK = '#222222'
MUTED = '#777777'
FLOOR = 0.5

STRINGS = {
    'ru': dict(nuisance=' (мешающий)', xray='рентген ', other='прочее',
               spectrum='спектр', model='модель (сумма)', continuum='континуум',
               bg='фон, β = 1', counts='отсчёты в канале', energy='энергия, кэВ',
               resid='(y − модель)/√N'),
    'en': dict(nuisance=' (nuisance)', xray='X-ray ', other='other',
               spectrum='spectrum', model='model (sum)', continuum='continuum',
               bg='background, β = 1', counts='counts per channel',
               energy='energy, keV', resid='(y − model)/√N'),
}
L = STRINGS['ru']

BASE_COLS = {'channel', 'energy', 'raw', 'continuum', 'y', 'model', 'residual'}


def load(model_csv, comp_csv, spectrum):
    with open(model_csv, encoding='utf-8-sig') as fh:
        rows = list(csv.DictReader(fh))
    comp_names = [c for c in rows[0].keys() if c not in BASE_COLS]

    d = {}
    d['e'] = np.array([float(r['energy']) for r in rows])
    d['raw'] = np.array([float(r['raw']) for r in rows])
    d['y'] = np.array([float(r['y']) for r in rows])
    d['snip'] = np.array([float(r['continuum']) for r in rows])
    d['model'] = np.array([float(r['model']) for r in rows])
    d['residual'] = np.array([float(r['residual']) for r in rows])
    curves = {c: np.array([float(r[c]) for r in rows]) for c in comp_names}

    bg = d['raw'] - d['y'] - d['snip']            # вычтенный фон (beta=1)
    hats = curves.pop('hats', None)
    bgfit = curves.pop('bgfit', None)
    if bgfit is not None:
        bg = bg + bgfit
    cont = d['snip'].copy()
    if hats is not None:
        cont = cont + hats
    d['bg'], d['cont'], d['curves'] = bg, cont, curves

    info = {}
    with open(comp_csv, encoding='utf-8-sig') as fh:
        for r in csv.DictReader(fh):
            if r['spectrum'] == spectrum:
                info[r['component']] = (float(r['share_pct']), float(r['z']), r['kind'])
    d['info'] = info
    return d


def component_order(d, max_main=6):
    """(имя, вклад, цвет, подпись легенды) в порядке рисования."""
    curves, info = d['curves'], d['info']
    order = sorted(curves, key=lambda c: -curves[c].max())
    main = [c for c in order if info.get(c, (0, 0, ''))[2] != 'nuisance'][:max_main]
    nuis = [c for c in order if info.get(c, (0, 0, ''))[2] == 'nuisance']
    rest = [c for c in order if c not in main and c not in nuis]

    out, ci = [], 0
    for c in main:
        share = info.get(c, (0, 0, ''))[0]
        color = COLOR_BY_COMPONENT.get(c)
        if color is None:
            color, ci = FALLBACK[ci % len(FALLBACK)], ci + 1
        out.append((c, curves[c], color, '%s — %.1f %%' % (c, share)))
    if rest:
        v = np.sum([curves[r] for r in rest], axis=0)
        out.append((L['other'], v, '#9e9e9e',
                    '%s (%s)' % (L['other'], ', '.join(rest))))
    for c in nuis:
        color = COLOR_BY_COMPONENT.get(c, '#997700')
        out.append((c, curves[c], color,
                    c.replace('Xray-', L['xray']) + L['nuisance']))
    return out


def style_axes(ax, axr):
    for a in (ax, axr):
        a.tick_params(colors=MUTED)
        for s in ('top', 'right'):
            a.spines[s].set_visible(False)
        for s in ('left', 'bottom'):
            a.spines[s].set_color('#cccccc')
    ax.grid(True, which='major', color='#eeeeee', lw=0.6, zorder=0)
    axr.grid(True, axis='y', color='#eeeeee', lw=0.6)


def pow2_ticks(ymax, n=6):
    """Круглые деления, равномерные по sqrt: густо внизу, редко наверху."""
    import math
    ticks = [0.0]
    for i in range(1, n + 1):
        v = (i / float(n)) ** 2 * ymax
        mag = 10 ** math.floor(math.log10(max(v, 1.0)))
        for m in (1, 1.5, 2, 3, 5, 7.5, 10):
            if m * mag >= v:
                v = m * mag
                break
        if v <= ticks[-1] or v > ymax:
            continue
        ticks.append(v)
    return ticks


def logsafe(v):
    v = np.asarray(v, dtype=float).copy()
    v[v < FLOOR] = np.nan
    return v


def draw_residual(axr, d):
    sigma = np.sqrt(np.maximum(d['raw'], 1.0))
    resid = d['residual'] / sigma
    axr.axhline(0, color='#cccccc', lw=0.8)
    axr.fill_between(d['e'], np.clip(resid, -8, 8), 0, step='mid',
                     color='#9aa7b0', lw=0, alpha=0.8)
    axr.set_ylim(-8, 8)
    axr.set_ylabel(L['resid'], fontsize=8.5, color=MUTED)
    axr.set_xlabel(L['energy'], color=INK)


def distribute_continuum(cont, comps):
    """Разнести континуум по компонентам, как это неявно делают полные
    образы: подложка на энергии E приписывается компонентам пропорционально
    их пиковому счёту ВЫШЕ E (комптоновское рассеяние сбрасывает энергию
    только вниз). Выше самого высокого пика — по глобальным долям."""
    vs = [v for _, v, _, _ in comps]
    above = [np.cumsum(v[::-1])[::-1] for v in vs]
    totals = np.sum(above, axis=0)
    glob = np.array([v.sum() for v in vs])
    glob = glob / glob.sum() if glob.sum() > 0 else np.full(len(vs), 1.0 / len(vs))
    out = []
    for j, (name, v, color, label) in enumerate(comps):
        w = np.where(totals > 0, above[j] / np.maximum(totals, 1e-300), glob[j])
        out.append((name, v + w * cont, color, label))
    return out


def draw_stacked(ax, d, spectrum, scale='log'):
    """Послойно: компоненты с разнесённым по ним континуумом; верх стека =
    модель. Фон слоем не рисуется — и стек, и линия спектра показаны за его
    вычетом. В легенде — доли полного счёта модели в диапазоне (аналог
    панели «Интенсивность»), а не «пирог» по пикам из components.csv.
    """
    e = d['e']
    is_log = scale == 'log'
    comps = component_order(d)
    # индексы прямых подписей — по пиковой части, до добавления континуума
    ann_at = {name: int(np.nanargmax(v)) for name, v, _, _ in comps
              if np.nanmax(v) > 1}
    if np.nanmax(d['cont']) > 0:
        comps = distribute_continuum(d['cont'], comps)

    total = sum(v.sum() for _, v, _, _ in comps)
    cum = np.zeros_like(e)
    for name, v, color, _ in comps:
        label = name.replace('Xray-', L['xray'])
        top = cum + v
        pct = 100.0 * v.sum() / total if total > 0 else 0.0
        lo = np.maximum(cum, FLOOR) if is_log else cum
        hi = np.maximum(top, FLOOR) if is_log else top
        ax.fill_between(e, lo, hi, color=color, lw=0, alpha=0.92,
                        label='%s — %.2f %%' % (label, pct), zorder=2)
        # прямая подпись цветного слоя у максимума его пиковой части
        if name != L['other'] and name in ann_at:
            imax = ann_at[name]
            ax.annotate(name.replace('Xray-', ''), (e[imax], max(top[imax], FLOOR)),
                        textcoords='offset points', xytext=(4, 6),
                        fontsize=8, color=color, fontweight='bold', zorder=8)
        cum = top

    ax.plot(e, logsafe(cum) if is_log else cum, color='white', lw=0.6, zorder=5)
    net = d['raw'] - d['bg']                      # спектр за вычетом фона
    ax.plot(e, logsafe(net) if is_log else net,
            color='#007700' if scale == 'linear' else INK, lw=0.8,
            drawstyle='steps-mid', label='%s (%s)' % (L['spectrum'], spectrum),
            zorder=6)


def draw_lines(ax, d, spectrum):
    e = d['e']
    # (raw − y) — всё, что вычли до фита (SNIP-континуум и/или фон): модель
    # поверх сырого спектра = модель фита + вычтенное. Колонку continuum
    # прибавлять отдельно нельзя — в snip-режиме она уже сидит внутри raw − y.
    total = d['model'] + (d['raw'] - d['y'])
    ax.fill_between(e, FLOOR, logsafe(d['raw']), step='mid', color='#d9d9d9',
                    lw=0, label='%s (%s)' % (L['spectrum'], spectrum), zorder=1)
    ax.plot(e, logsafe(total), color=INK, lw=1.1, label=L['model'], zorder=6)
    if np.nanmax(d['cont']) > 0:
        ax.plot(e, logsafe(d['cont']), color=MUTED, lw=1.0, ls=(0, (4, 2)),
                label=L['continuum'], zorder=3)
    if np.nanmax(d['bg']) > 0:
        ax.plot(e, logsafe(d['bg']), color='#aaaaaa', lw=1.0, ls=(0, (1, 1.5)),
                label=L['bg'], zorder=2)
    for i, (name, v, color, label) in enumerate(component_order(d)):
        ls = (0, (5, 1.5)) if L['nuisance'].strip('() ') in label else '-'
        ax.plot(e, logsafe(v), color=color, ls=ls, lw=1.6 if i == 0 else 1.3,
                label=label, zorder=5, alpha=0.95)
        if name != L['other'] and np.nanmax(v) > 1:
            imax = int(np.nanargmax(v))
            ax.annotate(name.replace('Xray-', ''), (e[imax], v[imax]),
                        textcoords='offset points', xytext=(4, 5),
                        fontsize=8, color=color, fontweight='bold')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('model_csv')
    ap.add_argument('comp_csv')
    ap.add_argument('spectrum')
    ap.add_argument('out_png')
    ap.add_argument('title', nargs='?', default=None)
    ap.add_argument('--style', choices=['stacked', 'lines'], default='stacked')
    ap.add_argument('--scale', choices=['pow2', 'log', 'linear'], default='pow2',
                    help='pow2 — ось в sqrt(N): ноль на месте, верх сжат, '
                         'ничего не режется (по умолчанию); linear — с '
                         'обрезанным верхом, как в классических пакетах')
    ap.add_argument('--linear', action='store_true', help='синоним --scale=linear')
    ap.add_argument('--ymax', type=float, default=None,
                    help='верх линейной шкалы (авто: пик спектра вне '
                         'низкоэнергетической зоны)')
    ap.add_argument('--lang', choices=['ru', 'en'], default='ru')
    args = ap.parse_args()
    scale = 'linear' if args.linear else args.scale
    global L
    L = STRINGS[args.lang]

    d = load(args.model_csv, args.comp_csv, args.spectrum)

    fig, (ax, axr) = plt.subplots(
        2, 1, figsize=(13.5, 7.8), dpi=150, sharex=True,
        gridspec_kw={'height_ratios': [4, 1], 'hspace': 0.06})
    fig.patch.set_facecolor('white')

    if args.style == 'stacked':
        draw_stacked(ax, d, args.spectrum, scale=scale)
    else:
        draw_lines(ax, d, args.spectrum)
        scale = 'log'

    net_max = float(np.max(d['raw'] - d['bg']))
    if scale == 'linear':
        # низкоэнергетический пик сознательно обрезается, как в классических
        # пакетах: верх шкалы — по максимуму спектра правее первых 8 % оси
        e0, e1 = d['e'][0], d['e'][-1]
        cut = d['e'] > e0 + 0.08 * (e1 - e0)
        peak = float(d['raw'][cut].max()) if cut.any() else float(d['raw'].max())
        ymax = args.ymax if args.ymax is not None else 1.15 * peak
        ax.set_ylim(0, ymax)
    elif scale == 'pow2':
        ax.set_yscale('function',
                      functions=(lambda x: np.sqrt(np.maximum(x, 0.0)),
                                 lambda x: np.square(x)))
        ymax = args.ymax if args.ymax is not None else 1.05 * net_max
        ax.set_ylim(0, ymax)
        ax.set_yticks(pow2_ticks(ymax))
    else:
        ax.set_yscale('log')
        ax.set_ylim(bottom=FLOOR)
    ax.set_xlim(d['e'][0], d['e'][-1])
    ax.set_ylabel(L['counts'], color=INK)
    ax.set_title(args.title or args.spectrum, color=INK, fontsize=12,
                 loc='left', pad=10)
    ax.legend(loc='upper right', fontsize=8.5, frameon=False, ncol=2)

    draw_residual(axr, d)
    style_axes(ax, axr)

    fig.savefig(args.out_png, bbox_inches='tight', facecolor='white')
    print('saved', args.out_png)


if __name__ == '__main__':
    main()
