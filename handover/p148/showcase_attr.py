# -*- coding: utf-8 -*-
"""П148: витрина — сдвиг каждой правки порознь.

Свёртка — функциями самого сторожа (`summarize`, `compare`), по сохранённым
выходам FsaStackShot: `showcase_before` (сборка без правок П148), `showcase_after`
(все правки), `showcase_noXX` (все, кроме XX). Сдвиг правки XX = after против noXX.
"""
import io
import json
import os
import sys

sys.path.insert(0, r'D:\BqMoni_Claude\p148\wt\tools')
import check_fsa_showcase as cs  # noqa: E402

BASE = r'D:\BqMoni_Claude\p148'
manifest = cs.load_manifest()
bands = [tuple(b) for b in manifest[u'bands_kev']]
pairs = []
for m in manifest[u'members']:
    for mode in m[u'modes']:
        pairs.append((m[u'key'], mode))


def summary(tag, key, mode):
    d = os.path.join(BASE, u'showcase_' + tag)
    stem = u'%s__%s' % (key, mode)
    with io.open(os.path.join(d, stem + u'.log'), encoding='utf-8') as fh:
        text = fh.read()
    return cs.summarize(text, os.path.join(d, stem + u'.curves.csv'), os.path.join(d, stem + u'.rates.csv'), bands)


out = io.open(os.path.join(BASE, u'24-showcase-attribution.txt'), 'w', encoding='utf-8')


def say(s=u''):
    out.write(s + u'\n')
    print(s)


arms = [(u'all', u'before'), (u'AMBER76', u'no76'), (u'AMBER92', u'no92'), (u'AMBER99', u'no99')]
totals = {}
for key, mode in pairs:
    after = summary(u'after', key, mode)
    for label, ref in arms:
        diffs = cs.compare(summary(ref, key, mode), after, 1e-9, 1e-6)
        totals[(key, mode, label)] = diffs

say(u'строк diff (после против плеча): all — против сборки без правок П148; AMBERxx — против «все правки, кроме xx»')
say(u'%-24s %-18s %6s %8s %8s %8s' % (u'спектр', u'режим', u'all', u'AMBER76', u'AMBER92', u'AMBER99'))
for key, mode in pairs:
    say(u'%-24s %-18s %6d %8d %8d %8d' % (key, mode, len(totals[(key, mode, u'all')]), len(totals[(key, mode, u'AMBER76')]),
                                         len(totals[(key, mode, u'AMBER92')]), len(totals[(key, mode, u'AMBER99')])))

for label, _ in arms[1:]:
    say()
    say(u'==== %s: ключевые строки (после против «кроме %s») ====' % (label, label))
    for key, mode in pairs:
        diffs = totals[(key, mode, label)]
        if not diffs:
            continue
        keep = []
        for band, comp, a, b in diffs:
            if band.startswith(u'rates') and (u'.count_rate' in comp or comp.endswith(u'.share_pct') or u'chi2' in comp.lower()):
                keep.append((band, comp, a, b))
            elif band in (u'SCREEN', u'ROWS', u'шапка'):
                keep.append((band, comp, a, b))
            elif comp in (u'model', u'excess_fit', u'missing_fit', u'pile-up'):
                keep.append((band, comp, a, b))
        for band, comp, a, b in keep[:40]:
            if isinstance(a, float) and isinstance(b, float):
                d = b - a
                pct = (u' (%+.3f %%)' % (100.0 * d / a)) if a else u''
                say(u'  %-22s %-12s %-16s %-34s %16s %16s %s' % (key[:22], mode[:12], band[:16], comp[:34], cs.fmt(a), cs.fmt(b), cs.fmt(d) + pct))
            else:
                say(u'  %-22s %-12s %-16s %-34s %s  →  %s' % (key[:22], mode[:12], band[:16], comp[:34], a, b))
out.close()
