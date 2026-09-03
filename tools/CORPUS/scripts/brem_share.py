# -*- coding: utf-8 -*-
u"""A62: есть ли в невязке модели след ТОРМОЗНОГО бета-частиц пробы.

ЗАЧЕМ. Тормозного излучения в полноспектральном разборе нет вовсе: у
бета-излучателя сплошной спектр до граничной энергии беты сейчас целиком
достаётся свободной подложке-сплайну, то есть модель его не объясняет, а
закрашивает. Величина не измерена, и строка требует оценить её ПО КОРПУСУ:
доля зависит от граничной энергии беты и от вещества пробы, то есть от состава
каждого спектра, и на одной сцене ответа не получить.

КАК МЕРИТСЯ. Прямого образа нет, поэтому мерим косвенно и честно: спектры
делятся по САМОЙ ЖЁСТКОЙ бете объявленного состава, и сравнивается невязка
модели (`model_residual_pct` из `*_runs.csv`). Если тормозное — заметная часть
отсчётов, у жёстких бета-излучателей невязка обязана быть систематически выше:
выход тормозного растёт с граничной энергией примерно как её квадрат.

⚠ Это НЕ доказательство: невязка растёт и от других причин. Замер отвечает на
один вопрос — стоит ли заводить образ вообще, или величина теряется в разбросе.

    python tools/CORPUS/scripts/brem_share.py <каталог прогона>
"""
import csv
import glob
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.abspath(os.path.join(HERE, os.pardir, 'corpus'))

# Граничная энергия беты, кэВ. Ноль — распад без беты (ЭЗ, альфа) либо
# бета настолько мягкая, что тормозного из пробы не выходит.
BETA_MAX = {
    'K-40': 1311.0, 'Cs-137': 1175.6, 'Y-90': 2280.1, 'Sr-90': 545.9,
    'Tl-208': 1803.0, 'Bi-212': 2254.0, 'Ac-228': 2069.0, 'Pb-212': 573.8,
    'Bi-214': 3272.0, 'Pb-214': 1023.0, 'Th-234': 199.0, 'Pa-234m': 2269.0,
    'Lu-176': 593.0, 'Co-60': 317.9, 'Eu-152': 1474.0, 'Eu-154': 1846.0,
    'Cs-134': 658.0, 'Ce-144': 318.7, 'Ru-106': 39.4, 'I-131': 606.3,
    'Na-22': 545.7, 'Co-57': 0.0, 'Cd-109': 0.0, 'Ba-133': 0.0,
    'Am-241': 0.0, 'Zn-65': 329.0, 'Mn-54': 0.0, 'Cr-51': 0.0,
    'Y-88': 0.0, 'Bi-207': 0.0, 'Ce-139': 0.0, 'Se-75': 0.0,
    'Th-232': 2069.0, 'U-238': 3272.0, 'Ra-226': 3272.0, 'U-235': 1400.0,
}

# Ряд разворачивается в самого жёсткого своего бета-излучателя.
CHAIN_MAX = {'Th-232': 2254.0, 'U-238': 3272.0, 'Ra-226': 3272.0, 'U-235': 1400.0}


def truth():
    u"""{ключ спектра: макс. граничная энергия беты объявленного состава}."""
    out = {}
    path = os.path.join(CORPUS, 'manifest.csv')
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            key = row.get('key') or row.get('spectrum')
            names = []
            for col in ('nuclides', 'chains'):
                v = (row.get(col) or '').strip()
                if v:
                    names += [s.strip() for s in v.replace(';', ' ').split() if s.strip()]
            top = 0.0
            for n in names:
                e = CHAIN_MAX.get(n, BETA_MAX.get(n))
                if e is None:
                    continue
                top = max(top, e)
            out[key] = (top, names)
    return out


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.abspath(os.path.join(HERE, os.pardir, os.pardir)), 'pie', 'out_v11')
    tru = truth()

    rows = []
    for path in glob.glob(os.path.join(out_dir, '*_runs.csv')):
        with io.open(path, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                key = r['spectrum']
                resid = r.get('model_residual_pct')
                if not resid:
                    continue
                try:
                    resid = float(resid)
                except ValueError:
                    continue
                top, names = tru.get(key, (None, []))
                if top is None:
                    continue
                rows.append((key, r['part'], top, resid, names))

    if not rows:
        sys.stderr.write(u'нет данных: не найдено *_runs.csv в %s\n' % out_dir)
        return 2

    print(u'прогон: %s' % out_dir)
    print(u'спектров с известной истиной: %d' % len(rows))
    print(u'')

    bands = ((0.0, 1.0, u'без беты (ЭЗ, альфа)'),
             (1.0, 700.0, u'мягкая бета < 700 кэВ'),
             (700.0, 1500.0, u'средняя 700…1500'),
             (1500.0, 1e9, u'жёсткая > 1500 кэВ'))
    print(u'%-26s %7s %10s %10s %10s' % (u'полоса граничной беты', u'спектров',
                                         u'медиана', u'среднее', u'кварт.75'))
    for lo, hi, name in bands:
        sel = sorted(r[3] for r in rows if lo <= r[2] < hi)
        if not sel:
            print(u'%-26s %7d %10s' % (name, 0, u'—'))
            continue
        med = sel[len(sel) // 2]
        avg = sum(sel) / len(sel)
        q75 = sel[int(len(sel) * 0.75)] if len(sel) > 3 else sel[-1]
        print(u'%-26s %7d %10.2f %10.2f %10.2f' % (name, len(sel), med, avg, q75))

    print(u'')
    print(u'⚠ Если у жёсткой беты невязка не выше прочих — тормозное в невязке')
    print(u'  не различимо, и образ заводить не по чему.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
