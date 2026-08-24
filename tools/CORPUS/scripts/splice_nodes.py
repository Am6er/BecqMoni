# -*- coding: utf-8 -*-
"""Гибридный корпус: шкала из одного прогона, ПШПВ из другого.

⛔ **Зачем.** Пересборка корпуса меняет ДВЕ вещи разом, и они связаны по
построению: правка выбора энергокалибровки меняет набор ПРИНЯТЫХ ЛИНИЙ, а
модель разрешения группы строится ровно по ним. Поэтому «пересобрали — Σχ²
выросло на 17 %» не называет причину: виновата шкала или ПШПВ, из этого числа
не видно. Разводится это только подменой узлов.

    python tools/CORPUS/scripts/splice_nodes.py --base=<корпус-А> --take=<корпус-Б>
                                                --node=ecal|fwhm --out=<куда>

Берётся ВСЁ из `--base`, и только названный узел заменяется на такой же из
`--take`. Остальные файлы корпуса (манифест, части, приборы, геометрии)
копируются из `--base` — разбор читает их, и подменять их незачем.

⚠ Узел ПШПВ бывает ТРЁХ видов (`SqrtFwhmCalibration`, `SimpleSqrtFwhmCalibration`,
`PowerFwhmCalibration`, см. `V2`), и в файле их может стоять несколько — на
`ResultData` и в корне. Меняются ВСЕ, иначе разбор возьмёт чужой (`B18`).
"""
import os
import re
import shutil
import sys

FWHM_TAGS = ('SqrtFwhmCalibration', 'SimpleSqrtFwhmCalibration',
             'PowerFwhmCalibration')


def grab(text, tags):
    """[(начало, конец, кусок)] для всех вхождений любого из тегов."""
    out = []
    for tag in tags:
        for m in re.finditer(r'<%s\b.*?</%s>' % (tag, tag), text, re.S):
            out.append((m.start(), m.end(), m.group(0)))
    out.sort()
    return out


def ecal_spans(text):
    return grab(text, ('EnergyCalibration',))


def splice(base_text, take_text, node):
    spans_b = ecal_spans(base_text) if node == 'ecal' else grab(base_text, FWHM_TAGS)
    spans_t = ecal_spans(take_text) if node == 'ecal' else grab(take_text, FWHM_TAGS)
    if len(spans_b) != len(spans_t):
        return None, 'узлов %d против %d' % (len(spans_b), len(spans_t))
    out = base_text
    for (s, e, _), (_, _, piece) in zip(reversed(spans_b), reversed(spans_t)):
        out = out[:s] + piece + out[e:]
    return out, None


def main():
    base = take = out = node = None
    for a in sys.argv[1:]:
        if a.startswith('--base='):
            base = a.split('=', 1)[1]
        elif a.startswith('--take='):
            take = a.split('=', 1)[1]
        elif a.startswith('--out='):
            out = a.split('=', 1)[1]
        elif a.startswith('--node='):
            node = a.split('=', 1)[1]
    if not (base and take and out and node in ('ecal', 'fwhm')):
        print(__doc__)
        return 2

    if os.path.exists(out):
        shutil.rmtree(out)
    shutil.copytree(base, out)
    sb, st = os.path.join(base, 'spectra'), os.path.join(take, 'spectra')
    done = skipped = 0
    for name in sorted(os.listdir(sb)):
        if not name.endswith('.xml'):
            continue
        p_take = os.path.join(st, name)
        if not os.path.isfile(p_take):
            skipped += 1
            continue
        with open(os.path.join(sb, name), encoding='utf-8', newline='') as h:
            tb = h.read()
        with open(p_take, encoding='utf-8', newline='') as h:
            tt = h.read()
        res, err = splice(tb, tt, node)
        if res is None:
            print('  %-26s ПРОПУЩЕН: %s' % (name, err))
            skipped += 1
            continue
        with open(os.path.join(out, 'spectra', name), 'w',
                  encoding='utf-8', newline='') as h:
            h.write(res)
        done += 1
    print('узел %s подменён у %d спектров, пропущено %d -> %s'
          % (node, done, skipped, out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
