# -*- coding: utf-8 -*-
"""П166: витрина — сдвиг каждой правки порознь, функциями самого сторожа (`compare`).
Снимки: эталон git (= сборка ДО, сверено: 0 строк), showcase_after (все правки),
showcase_noXX (все, кроме XX), showcase_off (все три рычага выкл: остаётся только AMBER122).
Сдвиг правки XX = after против noXX."""
import io
import json
import os
import sys

sys.path.insert(0, r'D:\BqMoni_Claude\p166\wt\tools')
import check_fsa_showcase as cs  # noqa: E402

BASE = r'D:\BqMoni_Claude\p166'
REF_GIT = r'D:\BqMoni_Claude\p166\wt\tools\fsa_showcase\reference'


def load(d, stem):
    with io.open(os.path.join(d, stem + u'.json'), encoding='utf-8') as fh:
        return json.load(fh)


manifest = cs.load_manifest()
pairs = [(m[u'key'], mode) for m in manifest[u'members'] for mode in m[u'modes']]
arms = [(u'все', REF_GIT), (u'AMBER123', os.path.join(BASE, u'showcase_no123')),
        (u'AMBER124/125', os.path.join(BASE, u'showcase_no124')), (u'AMBER134', os.path.join(BASE, u'showcase_no134')),
        (u'выкл↔ДО', None)]
out = io.open(os.path.join(BASE, u'art', u'52-showcase-attribution.txt'), 'w', encoding='utf-8')


def say(s=u''):
    out.write(s + u'\n')
    print(s)


say(u'строк diff: «все» — после против эталона git (= сборка ДО); AMBERxx — после против «все, кроме xx»;')
say(u'«выкл↔ДО» — плечо с тремя рычагами выкл против эталона git (остаётся одна AMBER122, рычага нет)')
say(u'%-24s %-18s %6s %9s %13s %9s %9s' % (u'спектр', u'режим', u'все', u'AMBER123', u'AMBER124/125', u'AMBER134', u'выкл↔ДО'))
details = {}
for key, mode in pairs:
    stem = u'%s__%s' % (key, mode)
    after = load(os.path.join(BASE, u'showcase_after'), stem)
    counts = []
    for label, d in arms:
        if d is None:
            diffs = cs.compare(load(REF_GIT, stem), load(os.path.join(BASE, u'showcase_off'), stem), 1e-9, 1e-6)
        else:
            diffs = cs.compare(load(d, stem), after, 1e-9, 1e-6)
        details[(key, mode, label)] = diffs
        counts.append(len(diffs))
    say(u'%-24s %-18s %6d %9d %13d %9d %9d' % ((key, mode) + tuple(counts)))

for label, _ in arms[1:4]:
    say()
    say(u'==== %s: крупнейшие сдвиги (после против «кроме») ====' % label)
    for key, mode in pairs:
        diffs = details[(key, mode, label)]
        if not diffs:
            continue
        say(u'  %s / %s: %d строк' % (key, mode, len(diffs)))
        for d in diffs[:8]:
            say(u'    ' + (u' | '.join(u'%s' % x for x in d) if isinstance(d, (list, tuple)) else u'%s' % d))
