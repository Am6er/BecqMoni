# -*- coding: utf-8 -*-
u"""П74 (`T260`): таблица эталона витрины по полосам энергии — из `reference/*.json`
(`check_fsa_showcase.py --snapshot`) в markdown, для журнала и отчёта.

  python handover/p74-t260/bands_table.py [<каталог эталона>] [--csv=<файл>]

Умолчание — `tools/fsa_showcase/reference`. По каждой паре: χ²/ndf, невязка строки
отчёта, доли (ROW), и по полосам — измерение (fit), модель, «не описано» и «лишнее»
против фита, серый слой, слой самого большого компонента.
"""
import glob
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))


def band_key(k):
    return float(k.split(u'-')[0])


def main():
    ref = os.path.join(REPO, u'tools', u'fsa_showcase', u'reference')
    csv_out = None
    for a in sys.argv[1:]:
        if a.startswith(u'--csv='):
            csv_out = a[6:]
        else:
            ref = a
    files = sorted(glob.glob(os.path.join(ref, u'*.json')))
    if not files:
        print(u'эталона нет: ' + ref)
        return 4
    rows = []
    out = []
    for path in files:
        with io.open(path, encoding='utf-8') as fh:
            d = json.load(fh)
        pair = u'%s / %s' % (d[u'member'], d[u'mode'])
        chi2 = d.get(u'head_lines', {}).get(u'chi2', u'')
        resid = [s[1] for s in d.get(u'screen', []) if s[0].startswith(u'Невязка')]
        shares = u', '.join(u'%s %s' % (r[0], r[2]) for r in d.get(u'rows', []))
        out.append(u'')
        out.append(u'**%s** — объявлен %s (HEAD %s, %.1f с; `%s`; матрица `%s`)' % (
            pair, d.get(u'declared'), d.get(u'head'), d.get(u'seconds', 0.0), u' '.join(d.get(u'args', [])), d.get(u'matrix')))
        out.append(u'')
        out.append(u'%s; невязка строки отчёта %s' % (chi2, resid[0] if resid else u'—'))
        out.append(u'')
        out.append(u'доли: %s' % shares)
        out.append(u'')
        layers = [l for l in d.get(u'layers', []) if l != u'continuum']
        top = layers[0] if layers else u'—'
        out.append(u'| полоса, кэВ | каналов | измерение (fit) | модель | не описано | лишнее | серый слой | %s |' % top)
        out.append(u'|---|---|---|---|---|---|---|---|')
        for band in sorted(d[u'bands'], key=band_key):
            b = d[u'bands'][band]
            grey = b.get(u'continuum', 0.0)
            out.append(u'| %s | %d | %.0f | %.0f | %.0f | %.0f | %.0f | %.0f |' % (
                band, b.get(u'channels', 0), b.get(u'fit', 0.0), b.get(u'model', 0.0),
                b.get(u'missing_fit', 0.0), b.get(u'excess_fit', 0.0), grey, b.get(top, 0.0)))
            rows.append((pair, band, b.get(u'fit', 0.0), b.get(u'model', 0.0), b.get(u'missing_fit', 0.0),
                         b.get(u'excess_fit', 0.0), grey, top, b.get(top, 0.0)))
    text = u'\n'.join(out) + u'\n'
    sys.stdout.write(text)
    if csv_out:
        with io.open(csv_out, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(u'pair,band_kev,fit,model,missing_fit,excess_fit,grey,top_layer,top_counts\n')
            for r in rows:
                fh.write(u'%s,%s,%.3f,%.3f,%.3f,%.3f,%.3f,%s,%.3f\n' % r)
    return 0


if __name__ == '__main__':
    sys.exit(main())
