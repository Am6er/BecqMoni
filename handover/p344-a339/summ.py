# -*- coding: utf-8 -*-
"""A339: сводка прогонов плеч — χ², Пуассон, образов, Xray-Lu, ход привязки."""
import csv
import glob
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
A = r'D:\BqMoni_Claude\a339'
tags = sys.argv[1:] or ['cur']
for tag in tags:
    print('==', tag)
    for d in sorted(glob.glob(os.path.join(A, 'out_%s_s*' % tag))):
        seed = d.rsplit('_s', 1)[1]
        runs = os.path.join(d, 'RC103_spline_runs.csv')
        if not os.path.isfile(runs):
            print(seed, 'нет прогона')
            continue
        for r in csv.DictReader(io.open(runs, encoding='utf-8-sig')):
            if r['spectrum'] != 'RC103_Lu176':
                continue
            note = r['anchor_note']
            m = re.search(r'совместно с усилением: шагов нуля (\d+), нуль света ([-\d.]+) кэВ([^)]*)', note)
            bg = 'фон измерен' if 'дрейф фона против пробы' in note.split('фон:')[-1][:60] else 'фон не измерен'
            comps = []
            for c in csv.DictReader(io.open(os.path.join(d, 'RC103_spline_components.csv'), encoding='utf-8-sig')):
                if c['spectrum'] == 'RC103_Lu176':
                    comps.append('%s %s%% z%s' % (c['component'], c['share_pct'], c['z']))
            log = os.path.join(A, 'run_%s_s%s.log' % (tag, seed))
            cut = ''
            ref = ''
            if os.path.isfile(log):
                txt = io.open(log, encoding='utf-8', errors='replace').read()
                cut = ' '.join(re.findall(r'CUT\tRC103_Lu176\t(\S+)\t\S+\t([\d.]+)', txt) and
                               ['%s %s' % x for x in re.findall(r'CUT\tRC103_Lu176\t(\S+)\t\S+\t([\d.]+)', txt)])
            print('s%s chi2 %s pois %s img %s | %s | %s | CUT: %s | %s' % (
                seed, r['chi2ndf'], r['chi2ndf_pois'], r['matrix_images'],
                m.group(0)[len('совместно с усилением: '):] if m else '?', bg, cut, '; '.join(comps)))
