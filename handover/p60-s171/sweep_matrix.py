# -*- coding: utf-8 -*-
"""П60 (S171): матрица развёртки по порогу из sweep.csv — по спектру и члену: мера (доля по Шуру /
парная / партнёр, из плеча с наибольшим порогом, где ничего не слито) и приговор на каждом пороге
(T:<партнёр> — привязан, f — свободен, . — не судился), у эталона AS80 ещё отношение к равновесной
активности 865.3 (x_eq) либо предел «<…».

    python handover/p60-s171/sweep_matrix.py handover/p60-s171/sweep.csv col [--out=...]
"""
import csv
import io
import sys


def main():
    path = sys.argv[1]
    measure = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else 'col'
    out = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
    rows = list(csv.DictReader(io.open(path, encoding='utf-8', newline='')))
    arms = []
    for r in rows:
        if r['arm'].startswith(measure) and r['arm'] not in arms:
            arms.append(r['arm'])
    arms.sort(key=lambda a: float(a[len(measure):]))
    lines = []

    def say(s):
        lines.append(s)

    say('мера: %s (по %s); плечи: %s' % (measure, 'колонкам' if measure == 'col' else 'линиям', ', '.join(arms)))
    for spec in ['as80', 'radon1', 'radon2']:
        members = []
        for r in rows:
            if r['spectrum'] == spec and r['member'] not in members:
                members.append(r['member'])
        if not members:
            continue
        say('')
        say('=== %s' % spec)
        say('%-8s %-13s %-8s | %s' % ('член', 'шур/пара', 'партнёр', ' '.join('%-16s' % a for a in arms)))
        for m in members:
            d = {r['arm']: r for r in rows if r['spectrum'] == spec and r['member'] == m}
            base = d.get(arms[-1])
            cells = []
            for a in arms:
                r = d.get(a)
                if not r:
                    cells.append('-')
                    continue
                v = r['gate_verdict']
                x = r['x_eq'] if r['x_eq'] else (r['rate'] if r['rate'].startswith('<') else '')
                c = 'T:' + r['tied_to'] if v == 'tied' else ('f' if v == 'free' else '.')
                if spec == 'as80' and x:
                    c += ' ' + (('x%.2f' % float(x)) if not x.startswith('<') else x)
                cells.append(c)
            say('%-8s %-13s %-8s | %s' % (
                m, (base['gate_share'] + '/' + base['gate_pair']) if base and base['gate_share'] else '-',
                base['gate_partner'] if base and base['gate_partner'] else '-',
                ' '.join('%-16s' % c for c in cells)))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    if out:
        io.open(out, 'w', encoding='utf-8', newline='').write(text)


if __name__ == '__main__':
    main()
