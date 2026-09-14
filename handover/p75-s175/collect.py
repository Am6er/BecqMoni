# -*- coding: utf-8 -*-
"""П75 (S175): собрать артефакты полосы в дерево — handover/p75-s175/ (скрипты, логи, rates, дампы 0–120 кэВ, снимки,
таблицы малой базы, мера п. 7, патч)."""
import csv
import io
import os
import shutil

LANE = r'D:\BqMoni_Claude\p75'
DST = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p75-s175'


def cp(src, sub=''):
    d = os.path.join(DST, sub) if sub else DST
    os.makedirs(d, exist_ok=True)
    shutil.copy2(src, os.path.join(d, os.path.basename(src)))


def cut_dump(src, dst, kev_max=120.0):
    with io.open(src, encoding='utf-8', newline='') as fh, io.open(dst, 'w', encoding='utf-8', newline='') as out:
        r = csv.reader(fh)
        w = csv.writer(out, lineterminator='\n')
        w.writerow(next(r))
        for row in r:
            if float(row[1]) <= kev_max:
                w.writerow(row)


os.makedirs(DST, exist_ok=True)
for name in ['main_build.ps1', 'wt_build.ps1', 'radon_stand.ps1', 'mk_wd.ps1', 'accept.ps1', 'mini.ps1', 'run_all.ps1',
             'run_all2.ps1', 'item7.ps1', 'compare_rates.py', 'band.py', 'mini_ab.py', 'mini_diff.py', 'mini_rows.py',
             'collect.py', 's175.patch']:
    cp(os.path.join(LANE, name))
for name in ['main_build.log', 'main_build2.log', 'main_build3.log', 'main_build4.log', 'wt_build_a.log', 'wt_build_b.log',
             'wt_build_b2.log', 'radon_stand.log', 'run_all.log', 'run_all2.log', 'accept.log', 'accept2.log', 'accept3.log',
             'mini_a.log', 'mini_b.log', 'mini_b2.log', 'run_mini_a.log', 'run_mini_b.log', 'check_all.log', 'check_all2.log',
             'item7.log']:
    p = os.path.join(LANE, 'logs', name)
    if os.path.exists(p):
        cp(p, 'logs')
for name in ['count_ASN16_rn_side.log', 'eff_ASN16_rn_side.log']:
    cp(os.path.join(LANE, 'radon', 'logs', name), 'logs')
acc = os.path.join(LANE, 'out', 'accept')
for name in sorted(os.listdir(acc)):
    p = os.path.join(acc, name)
    if name.startswith('rates_') or name in ('bitwise.txt', 'bands.txt') or name.endswith('.log'):
        cp(p, 'accept')
    elif name.startswith('curves_'):
        os.makedirs(os.path.join(DST, 'accept', 'dumps_0-120keV'), exist_ok=True)
        cut_dump(p, os.path.join(DST, 'accept', 'dumps_0-120keV', name))
    elif name.endswith('.png') and ('zoom' in name or name in ('cs_a.png', 'cs_b.png', 'cs_g.png', 'cs_c.png', 'cs_f.png', 'cs_r.png')):
        cp(p, 'png')
it7 = os.path.join(LANE, 'out', 'item7')
for name in sorted(os.listdir(it7)):
    p = os.path.join(it7, name)
    if name.startswith('rates_') or name.endswith('.log') or name.endswith('.png'):
        cp(p, 'item7')
for name in ['mini_ab.csv', 'mini_rows.csv', 'mini_diff.txt']:
    cp(os.path.join(LANE, 'out', name), 'mini')
for arm in 'ab':
    src = os.path.join(LANE, 'out_mini_p75' + arm)
    for name in sorted(os.listdir(src)):
        if name.endswith('.csv') and ('_components' in name or '_grey' in name or '_tails' in name or '_runs' in name):
            d = os.path.join(DST, 'mini', 'out_mini_p75' + arm)
            os.makedirs(d, exist_ok=True)
            shutil.copy2(os.path.join(src, name), os.path.join(d, name))
    shutil.copy2(os.path.join(src, '.run.json'), os.path.join(DST, 'mini', 'run_mini_%s.run.json' % arm))
total = 0
count = 0
for root, dirs, files in os.walk(DST):
    for f in files:
        total += os.path.getsize(os.path.join(root, f))
        count += 1
print('файлов %d, %.1f МБ' % (count, total / 1048576.0))
