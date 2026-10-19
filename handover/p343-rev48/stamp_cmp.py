# -*- coding: utf-8 -*-
"""П343: клеймо и первые узлы кривой <Efficiency> спектра — HEAD против рабочего файла.
python stamp_cmp.py <worktree> <спектр> [<спектр> ...]
"""
import os
import re
import subprocess
import sys

for _s in (sys.stdout,):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass
wt = sys.argv[1]


def info(text):
    import xml.etree.ElementTree as ET
    root = ET.fromstring(text.encode('utf-8'))
    eff = root.find('ResultDataList/ResultData/Efficiency')
    en = [float(e.findtext('Energy')) for e in eff.findall('Curve/ROIEfficiencyData')]
    return eff.findtext('ComputeStamp'), en, eff.findtext('LastUpdated')


for name in sys.argv[2:]:
    rel = 'tools/CORPUS/corpus/spectra/%s.xml' % name
    old = subprocess.check_output(['git', '-C', wt, 'show', 'HEAD:' + rel]).decode('utf-8-sig')
    new = open(os.path.join(wt, rel), encoding='utf-8-sig').read()
    for tag, text in (('HEAD', old), ('НОВ ', new)):
        st, en, upd = info(text)
        print('%-22s %s узлов %d от %.3f кэВ, обновлена %s' % (name, tag, len(en), en[0] if en else 0, upd))
        print('      клеймо: %s' % st)

