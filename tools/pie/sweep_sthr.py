# -*- coding: utf-8 -*-
u"""Развёртка порога доли `--sthr` по корпусу — вывод порога, а не подбор (`S90`).

ЗАЧЕМ. Порог отбора «обнаружено» в `score.py` был выведен под ПРЕЖНЮЮ меру доли:
«пирог» по пиковым отсчётам среди нуклидных образов. С 23.08.2026 мера одна на
весь проект и это доля СЛОЯ — вклад компонента в полный счёт модели с
разнесённой подложкой (`S76`). Знаменатель у неё больше, доли ниже, и старое
умолчание 3 % занижает recall. Пересчитать порог множителем НЕЛЬЗЯ: сдвиг не
равномерный — на группе ASN16 у 44 строк из 133 пиковая доля БОЛЬШЕ слоевой.

⛔ Порог здесь именно ВЫВОДИТСЯ, как выводился порог `S57`: считается вся
развёртка, печатается целиком, и правило выбора названо словами — а не
подбирается число, при котором «получилось красиво».

ПРАВИЛО ВЫБОРА, и оно одно: **самый НИЗКИЙ порог, при котором число жёстких
фантомов ещё равно наименьшему достижимому.** Ниже него фантомы растут (порог
пускает шум), выше — растёт только потеря recall, потому что фантомов меньше
минимума не станет. Так порог покупает максимум узнанного, не платя выдумкой.

⚠ Части корпуса НЕ СМЕШИВАЮТСЯ: развёртка считается по каждой отдельно, и порог
обязан сойтись на обеих. Разошлись — это находка, а не повод усреднить.

    python tools/pie/sweep_sthr.py --out-dir=tools/pie/out_XXX [--mode=spline]
                                   [--zthr=4] [--members]
"""
import argparse
import io
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCORE = os.path.join(HERE, 'score.py')

GRID = [0.10, 0.20, 0.30, 0.50, 0.75, 1.00, 1.50, 2.00, 3.00, 4.00, 5.00, 7.00, 10.00]

TOTAL = re.compile(r'^итого\s+(\d+)\s+(\d+)%\s+(\d+)\s+\(\+(\d+) комнатных\)')
RESID = re.compile(r'model residual медиана\s+([0-9.]+)')


def run(out_dir, mode, part, sthr, zthr, members):
    cmd = [sys.executable, SCORE, '--out-dir', out_dir, '--mode', mode,
           '--part', part, '--sthr', str(sthr), '--zthr', str(zthr)]
    if members:
        cmd.append('--members')
    p = subprocess.run(cmd, capture_output=True)
    text = p.stdout.decode('utf-8', 'replace')
    row = dict(spectra=0, recall=0, phantom=0, room=0, residual=float('nan'))
    for line in text.splitlines():
        m = TOTAL.match(line.strip())
        if m:
            row.update(spectra=int(m.group(1)), recall=int(m.group(2)),
                       phantom=int(m.group(3)), room=int(m.group(4)))
        m = RESID.search(line)
        if m:
            row['residual'] = float(m.group(1))
    return row


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out-dir', required=True)
    ap.add_argument('--mode', default='spline')
    ap.add_argument('--zthr', type=float, default=4.0)
    ap.add_argument('--members', action='store_true', default=True)
    ap.add_argument('--parts', default='known,unknown')
    args = ap.parse_args()

    chosen = {}
    for part in args.parts.split(','):
        print()
        print(u'=== часть: %s ===' % part)
        print(u'%8s %9s %10s %10s %12s' % (u'порог, %', u'recall', u'фантомов',
                                           u'комнатных', u'невязка, %'))
        rows = []
        for sthr in GRID:
            r = run(args.out_dir, args.mode, part, sthr, args.zthr, args.members)
            rows.append((sthr, r))
            print(u'%8.2f %8d%% %10d %10d %11.1f' % (
                sthr, r['recall'], r['phantom'], r['room'], r['residual']))

        floor = min(r['phantom'] for _, r in rows)
        best = None
        for sthr, r in rows:
            if r['phantom'] == floor:
                best = (sthr, r)
                break
        chosen[part] = best
        print(u'  наименьшее число жёстких фантомов: %d; самый низкий порог с ним: %.2f %%'
              u' (recall %d %%)' % (floor, best[0], best[1]['recall']))

    print()
    if len(chosen) > 1:
        picks = sorted(set(v[0] for v in chosen.values()))
        if len(picks) == 1:
            print(u'✅ обе части сошлись на пороге %.2f %%' % picks[0])
        else:
            print(u'⛔ ЧАСТИ РАЗОШЛИСЬ: %s — усреднять НЕЛЬЗЯ, это находка'
                  % u', '.join(u'%s %.2f %%' % (k, v[0]) for k, v in sorted(chosen.items())))
    return 0


if __name__ == '__main__':
    sys.exit(main())
