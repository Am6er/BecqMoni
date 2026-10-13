# -*- coding: utf-8 -*-
r"""П114 — два оставшихся переноса строки «(П114, ночь\n19→20.09.2026…» и «П114 ночью\n19→20.09.2026»."""
import sys
sys.stdout.reconfigure(encoding='utf-8')
for root in (r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8', r'D:\BqMoni_Claude\p114\wt'):
    for rel, pairs in (
        (r'BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs',
         [(u'21 → 22 (П114, ночь\r\n        /// 19→20.09.2026; решение Amber', u'21 → 22 (П114,\r\n        /// 19–21.09.2026; решение Amber')]),
        (r'tools\check_corpus_scenes.py',
         [(u'и П114 ночью\n19→20.09.2026 единым счётом физики 22', u'и П114\n19–21.09.2026 единым счётом физики 22'),
          (u'и П114 ночью\r\n19→20.09.2026 единым счётом физики 22', u'и П114\r\n19–21.09.2026 единым счётом физики 22')]),
    ):
        p = root + '\\' + rel
        raw = open(p, 'rb').read()
        t = raw.decode('utf-8')
        n = 0
        for a, b in pairs:
            n += t.count(a)
            t = t.replace(a, b)
        open(p, 'wb').write(t.encode('utf-8'))
        print('%-55s %s замен %d, осталось %d' % (rel, 'main' if root.startswith('C:') else 'wt  ', n, t.count(u'19→20.09.2026')))
