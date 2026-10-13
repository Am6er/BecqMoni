# -*- coding: utf-8 -*-
import io, json, os, sys
sys.path.insert(0, r'D:\BqMoni_Claude\p166\wt\tools')
import check_fsa_showcase as cs
BASE = r'D:\BqMoni_Claude\p166'
REF = r'D:\BqMoni_Claude\p166\wt\tools\fsa_showcase\reference'
def load(d, stem):
    with io.open(os.path.join(d, stem + '.json'), encoding='utf-8') as fh:
        return json.load(fh)
m = cs.load_manifest()
for key, mode in [(x['key'], md) for x in m['members'] for md in x['modes']]:
    stem = '%s__%s' % (key, mode)
    for tag, a, b in (('все', REF, 'showcase_after'), ('AMBER124/125', 'showcase_no124', 'showcase_after'), ('AMBER134', 'showcase_no134', 'showcase_after')):
        da = load(a if os.path.isabs(a) else os.path.join(BASE, a), stem)
        db = load(os.path.join(BASE, b), stem)
        diffs = cs.compare(da, db, 1e-9, 1e-6)
        rows = [d for d in diffs if 'кэВ' not in str(d[0])]
        if rows:
            print('%s / %s [%s]:' % (key, mode, tag))
            for d in rows[:14]:
                print('    ' + ' | '.join(str(x) for x in d))
