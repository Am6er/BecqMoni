# -*- coding: utf-8 -*-
"""П64 (AMBER29): весь разбор чисел FSA одним движением (после run_shots.ps1).

    python handover/p64-amber29/analyze.py [<D:\\BqMoni_Claude\\p64>]

Порядок: collect_rates по плечам A_noeq / B_noeq / A_eq / A_bg2 → decay_fit по Pb-214, Bi-214
(обе сцены), Pb-212, Ra-226 → bateman (ранние съёмки, торон) → vessels (два сосуда).
Всё пишется в handover/p64-amber29/ (rates/ — копии rates_*.csv, tables/ — своды),
вывод — analysis.txt.
"""
import io
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
P = sys.argv[1] if len(sys.argv) > 1 else r'D:\BqMoni_Claude\p64'
OUT = os.path.join(P, 'out')
RATES = os.path.join(HERE, 'rates')
TABLES = os.path.join(HERE, 'tables')
LOG = io.open(os.path.join(HERE, 'analysis.txt'), 'w', encoding='utf-8', newline='\n')


def run(args, title):
    LOG.write('\n### ' + title + '\n$ ' + ' '.join(args) + '\n')
    print('### ' + title)
    env = dict(os.environ, PYTHONIOENCODING='utf-8')
    r = subprocess.run([sys.executable] + args, capture_output=True, text=True, encoding='utf-8', env=env)
    LOG.write(r.stdout)
    if r.stderr:
        LOG.write('[stderr]\n' + r.stderr)
    print(r.stdout)
    if r.returncode != 0:
        print('⛔ код %d: %s' % (r.returncode, r.stderr[-2000:]))
        LOG.write('⛔ код %d\n' % r.returncode)
    return r.returncode


def main():
    os.makedirs(RATES, exist_ok=True)
    os.makedirs(TABLES, exist_ok=True)
    n = 0
    for f in os.listdir(OUT):
        if f.startswith('rates_') and f.endswith('.csv'):
            shutil.copy(os.path.join(OUT, f), RATES)
            n += 1
    LOG.write('скопировано rates_*.csv: %d\n' % n)
    tl = os.path.join(HERE, 'timeline.csv')
    for arm in ('A_noeq', 'B_noeq', 'A_eq', 'A_bg2'):
        run([os.path.join(HERE, 'collect_rates.py'), RATES, arm, tl, TABLES], 'свод плеча ' + arm)
    for arm in ('A_noeq', 'B_noeq'):
        for nuc in ('Pb-214', 'Bi-214', 'Pb-212', 'Ra-226', 'Tl-208', 'Bi-212'):
            s = os.path.join(TABLES, 'series_%s_%s.csv' % (nuc, arm))
            if os.path.exists(s):
                run([os.path.join(HERE, 'decay_fit.py'), s, '--label=%s %s' % (nuc, arm),
                     '--out=' + os.path.join(TABLES, 'decay_%s_%s.csv' % (nuc, arm))], 'фит распада %s (%s)' % (nuc, arm))
    # bateman ждёт decay_Pb-214.csv / decay_Bi-214.csv — плечо A_noeq
    for nuc in ('Pb-214', 'Bi-214'):
        src = os.path.join(TABLES, 'decay_%s_A_noeq.csv' % nuc)
        if os.path.exists(src):
            shutil.copy(src, os.path.join(TABLES, 'decay_%s.csv' % nuc))
    run([os.path.join(HERE, 'bateman.py'), os.path.join(TABLES, 'activities_A_noeq.csv'), TABLES,
         '--out=' + os.path.join(TABLES, 'bateman_A_noeq.csv')], 'Бейтман, ранние съёмки (A_noeq)')
    run([os.path.join(HERE, 'vessels.py'), os.path.join(P, 'spectra_ab'), TABLES,
         '--out=' + os.path.join(TABLES, 'vessels.csv')], 'два сосуда')
    LOG.close()


if __name__ == '__main__':
    main()
