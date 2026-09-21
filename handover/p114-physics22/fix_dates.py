# -*- coding: utf-8 -*-
r"""П114 — календарь захода оказался не «ночь 19→20.09.2026», а 19–21.09.2026 (обрыв сессии 19.09, досчёт 21.09):
поправить формулировку во всех своих файлах главного дерева И worktree (байты: перевод строки каждого файла сохраняется).
«ночь 19→20.09.2026» -> «19–21.09.2026», «ночи 19→20.09.2026» -> «19–21.09.2026», «ночью 19→20.09.2026» -> «19–21.09.2026»."""
import sys
sys.stdout.reconfigure(encoding='utf-8')
FILES = [
    r'BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs',
    r'BecquerelMonitor\EfficiencyMaker\EfficiencySimulator.cs',
    r'BecquerelMonitor\EfficiencyMaker\EfficiencyCalculation.cs',
    r'tools\effmaker\probes\BoundProbeF59.cs',
    r'tools\effmaker\probes\CorpusMatrixProbe.cs',
    r'tools\effmaker\probes\G4RawProbe.cs',
    r'tools\effmaker\probes\LayerReturnProbe.cs',
    r'tools\check_matrix_keys.py',
    r'tools\check_corpus_scenes.py',
    r'tools\CORPUS\README.md',
]
PAIRS = [
    (u'ВКЛ умолчанием с ночи 19→20.09.2026', u'ВКЛ умолчанием с 19–21.09.2026'),
    (u'с ночи 19→20.09.2026', u'с 19–21.09.2026'),
    (u'ночью 19→20.09.2026', u'19–21.09.2026'),
    (u'ночь 19→20.09.2026', u'19–21.09.2026'),
    (u'ночи 19→20.09.2026', u'19–21.09.2026'),
]
for root in (r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8', r'D:\BqMoni_Claude\p114\wt'):
    for rel in FILES:
        p = root + '\\' + rel
        raw = open(p, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        t = raw.decode('utf-8-sig')
        n = 0
        for a, b in PAIRS:
            n += t.count(a)
            t = t.replace(a, b)
        if n:
            out = t.encode('utf-8')
            if bom:
                out = b'\xef\xbb\xbf' + out
            open(p, 'wb').write(out)
        left = t.count(u'19→20.09.2026')
        print('%-60s %s замен %d, осталось %d' % (rel, 'main' if root.startswith('C:') else 'wt  ', n, left))
# §25 и журнал — только главное дерево
for rel in (r'tools\effmaker\handover-response-matrix.md', r'handover\handover-2026-09-19-p114-physics22-rev32.md'):
    p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8' + '\\' + rel
    raw = open(p, 'rb').read()
    t = raw.decode('utf-8')
    n = 0
    for a, b in [(u'(П114, ночь 19→20.09.2026)', u'(П114, 19–21.09.2026)'), (u'П114 (ночь 19→20.09.2026)', u'П114 (19–21.09.2026)')] + PAIRS:
        n += t.count(a)
        t = t.replace(a, b)
    open(p, 'wb').write(t.encode('utf-8'))
    print('%-60s замен %d, осталось %d' % (rel, n, t.count(u'19→20.09.2026')))
