# -*- coding: utf-8 -*-
# П111: обрезать логи арбитра g4cf для handover — оставить SETUP, блок CUTS (вещество/пороги), HISTBEGIN/HIST/ESCPOP;
# 35 тыс. строк таблиц физики Geant4 не переносятся (как П106 pack_logs.py).
import io, os, sys
src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
for name in sorted(os.listdir(src)):
    if not name.endswith('.log'):
        continue
    keep = []
    in_cuts = False
    for line in io.open(os.path.join(src, name), encoding='utf-8', errors='replace'):
        s = line.rstrip('\r\n')
        if s.startswith('SETUP') or s.startswith('HISTBEGIN') or s.startswith('HIST ') or s.startswith('ESCPOP'):
            keep.append(s)
        elif s.startswith('CUTS'):
            in_cuts = True
            keep.append(s)
        elif in_cuts:
            if s.startswith(' Material :') or s.startswith(' Range cuts') or s.startswith(' Energy thresholds') or s.startswith('Index :') or s.startswith('===='):
                keep.append(s)
            if s.startswith('HISTBEGIN') or (s.strip() == '' and len(keep) > 40):
                in_cuts = False
    with io.open(os.path.join(dst, name), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(keep) + '\n')
    print(name, len(keep), 'строк')
