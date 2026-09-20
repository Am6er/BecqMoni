# -*- coding: utf-8 -*-
# П100: собрать cmp100.py из cmp94.py (П94) — свои пути, столбцы П100 (ВЫКЛ/ВКЛ elmix) рядом с П94 (eltr1).
import io, os
SRC = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p94-amber44\cmp94.py'
DST = r'D:\BqMoni_Claude\p100\cmp100.py'
s = io.open(SRC, encoding='utf-8').read()

def rep(old, new):
    global s
    assert s.count(old) == 1, old[:60]
    s = s.replace(old, new)

rep("ROOT = r'D:\\BqMoni_Claude\\p94'",
    "ROOT = r'D:\\BqMoni_Claude\\p100'\nP94 = os.path.join(r'C:\\Users\\moroz\\source\\repos\\BQ Eng res .NET 4.8', 'handover', 'p94-amber44')")
rep("# П94 (AMBER44 + занос M12, 17.09.2026): сводка приёмки",
    "# П100 (M13, 18.09.2026; наследник cmp94.py П94): сводка приёмки")
rep("""    # наши CSV П92 (ref, detour0 …) — метка «92»""",
    """    # наши CSV П94 (eltr1, off, eltr1_detour0 …) — метка «94»: «ПОСЛЕ» П94 = «ВЫКЛ» П100 (физика 19)
    d = os.path.join(P94, 'ours')
    if os.path.isdir(d):
        for f in sorted(os.listdir(d)):
            m = re.match(r'ours_%s_%s_(\\w+)\\.csv$' % (re.escape(scene), re.escape(e)), f)
            if m:
                h, n = read_ours(os.path.join(d, f))
                data['наша ' + m.group(1) + '94'] = (h, n)
    # наши CSV П92 (ref, detour0 …) — метка «92»""")
rep("""    sources = [(os.path.join(ROOT, 'g4out'), scene), (os.path.join(P92, 'g4'), scene)]""",
    """    sources = [(os.path.join(ROOT, 'g4out'), scene), (os.path.join(P94, 'g4'), scene), (os.path.join(P92, 'g4'), scene)]""")
rep("""    cols = [('ДО: off/def', 'наша off', 'G4 def'), ('ДО (П92 ref)/def', 'наша ref92', 'G4 def'),
            ('ДО (П55 ref)/def', 'наша ref55', 'G4 def'),
            ('ПОСЛЕ: eltr1/def', 'наша eltr1', 'G4 def'),
            ('eltr1/killesc', 'наша eltr1', 'G4 killesc'), ('eltr1/fullcarry', 'наша eltr1', 'G4 fullcarry'),
            ('ВКЛ без заноса: eltr1_detour0/killcarry', 'наша eltr1_detour0', 'G4 killcarry'),
            ('detour0/killcarry', 'наша off_detour0', 'G4 killcarry'),
            ('detour0(П92)/killcarry', 'наша detour092', 'G4 killcarry')]
    cols = [c for c in cols if c[1] in ms and c[2] in ms]
    table('наша сторона / арбитр − 1, % (ДО — ключ ВЫКЛ, ПОСЛЕ — ключ eltr=1; арбитр def — с возвратом e⁻)', keys, cols, ms, ns, md)""",
    """    cols = [('физика 18 (П92 ref)/def', 'наша ref92', 'G4 def'), ('физика 18 (П55 ref)/def', 'наша ref55', 'G4 def'),
            ('П94 = физика 19 (eltr1)/def', 'наша eltr194', 'G4 def'),
            ('П100 ВЫКЛ (off)/def', 'наша off', 'G4 def'),
            ('П100 ВКЛ: elmix1/def', 'наша elmix1', 'G4 def'),
            ('ВКЛ без заноса: elmix1_detour0/killcarry', 'наша elmix1_detour0', 'G4 killcarry'),
            ('П94 без заноса: eltr1_detour0/killcarry', 'наша eltr1_detour094', 'G4 killcarry')]
    cols = [c for c in cols if c[1] in ms and c[2] in ms]
    table('наша сторона / арбитр − 1, % (П94 — ключ eltr=1 = физика 19 = ВЫКЛ П100; ВКЛ — elmix=1; арбитр def — с возвратом e⁻)', keys, cols, ms, ns, md)""")
rep("""    cols = [('наша eltr1/off', 'наша eltr1', 'наша off'), ('наша eltr1/ref92', 'наша eltr1', 'наша ref92'),
            ('наша eltr1/ref55', 'наша eltr1', 'наша ref55'), ('наша off_detour0/off', 'наша off_detour0', 'наша off'),
            ('наш занос ВКЛ: eltr1_detour0/eltr1', 'наша eltr1_detour0', 'наша eltr1'),
            ('наш возврат: eltr1_detour0/off_detour0', 'наша eltr1_detour0', 'наша off_detour0'),
            ('G4 killesc/def', 'G4 killesc', 'G4 def'), ('G4 killcarry/def', 'G4 killcarry', 'G4 def'),
            ('G4 fullcarry/def', 'G4 fullcarry', 'G4 def')]""",
    """    cols = [('П100: elmix1/eltr1(П94)', 'наша elmix1', 'наша eltr194'), ('elmix1/off', 'наша elmix1', 'наша off'),
            ('П100 возврат: elmix1_detour0/off_detour0(П94)', 'наша elmix1_detour0', 'наша off_detour094'),
            ('П94 возврат: eltr1_detour0/off_detour0', 'наша eltr1_detour094', 'наша off_detour094'),
            ('G4 возврат: def/killesc', 'G4 def', 'G4 killesc'),
            ('G4 killesc/def', 'G4 killesc', 'G4 def'), ('G4 killcarry/def', 'G4 killcarry', 'G4 def')]""")
rep("""    print('доли на историю (G4 def / наша eltr1 / наша off|ref92):')
    ours_off = 'наша off' if 'наша off' in ms else ('наша ref92' if 'наша ref92' in ms else 'наша ref55')
    for k in keys:
        a = ms['G4 def'][k] if 'G4 def' in ms else float('nan')
        b = ms['наша eltr1'][k] if 'наша eltr1' in ms else float('nan')""",
    """    print('доли на историю (G4 def / наша elmix1 / наша П94 eltr1|off):')
    ours_off = 'наша eltr194' if 'наша eltr194' in ms else ('наша off' if 'наша off' in ms else 'наша ref92')
    for k in keys:
        a = ms['G4 def'][k] if 'G4 def' in ms else float('nan')
        b = ms['наша elmix1'][k] if 'наша elmix1' in ms else float('nan')""")
io.open(DST, 'w', encoding='utf-8').write(s)
print('ok', DST)
