#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Чтение *_runs.csv корпусного прогона и разложение chi2 на систематику и статистику.

Полоса П31 (`A281`). Ничего не правит, только читает.
"""
import csv, io, os, sys, math, glob, re

def read_runs(out_dir):
    rows = {}
    for p in glob.glob(os.path.join(out_dir, '*_runs.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig')):
            rows[r['spectrum']] = r
    return rows

def counts(corpus, key):
    for r in csv.DictReader(io.open(os.path.join(corpus, 'manifest.csv'), encoding='utf-8-sig')):
        if r['key'] == key:
            return int(r['counts'])
    return 0

def manifest_counts(corpus):
    return {r['key']: int(r['counts'] or 0)
            for r in csv.DictReader(io.open(os.path.join(corpus, 'manifest.csv'), encoding='utf-8-sig'))}

def f(x):
    try:
        return float(x)
    except Exception:
        return float('nan')

def loglog_slope(xs, ys):
    xs = [math.log10(x) for x in xs if x > 0]
    ys2 = [math.log10(y) for y in ys if y > 0]
    if len(xs) != len(ys2) or len(xs) < 3:
        return float('nan'), float('nan')
    n = len(xs)
    mx = sum(xs)/n; my = sum(ys2)/n
    sxy = sum((a-mx)*(b-my) for a, b in zip(xs, ys2))
    sxx = sum((a-mx)**2 for a in xs)
    syy = sum((b-my)**2 for b in ys2)
    if sxx <= 0 or syy <= 0:
        return float('nan'), float('nan')
    return sxy/sxx, sxy/math.sqrt(sxx*syy)
