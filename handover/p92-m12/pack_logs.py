# -*- coding: utf-8 -*-
# П92: обрезанные копии логов арбитра для артефактов handover/p92-m12/g4/ — только строки
# SETUP / CUTS / Material / Range cuts / Energy thresholds / RESULT / HISTBEGIN / HIST / HISTEND
# (шапка Geant4 и рассылка по потокам не нужны; полные логи — D:\BqMoni_Claude\p92\g4out до приёмки).
import io, os, re, sys
src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
keep = re.compile(r'^(SETUP|CUTS|P55|RESULT|HISTBEGIN|HIST |HISTEND| Material :| Range cuts| Energy thresholds|область )')
for f in sorted(os.listdir(src)):
    if not f.endswith('.log') or f.startswith('smoke'):
        continue
    lines = [l for l in io.open(os.path.join(src, f), encoding='utf-8', errors='replace') if keep.match(l)]
    io.open(os.path.join(dst, f), 'w', encoding='utf-8', newline='\n').writelines(lines)
    print(f, len(lines))
