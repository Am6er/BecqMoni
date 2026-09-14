# -*- coding: utf-8 -*-
r"""П66 — перенести узлы `<Efficiency>` 17 сцен маринелли (кривые сосуда ОМАСН, `CorpusEffProbe` по
корпусу основного дерева) в копии спектров worktree 16de7feb (корпус rev22, 131 спектр) — стенд плеча A
«только сосуды». Кривая — Монте-Карло с фиксированным зерном (`EfficiencySimulator.Seed = 20260803`,
поток на узел по индексу), поэтому узел один и тот же для любого стенда; переносится текстом, как это
делает `restore_eff_nodes.py` (узел целиком, ловушка T30 — последний `</Efficiency>` строки).

    python handover/p66-rev23/eff_nodes_copy.py <spectra-источник> <spectra-приёмник> <ключ,ключ,…>

Приёмник обязан уже нести узел (старой сцены) — он заменяется; иначе отказ. Разделитель — точка.
"""
import io
import os
import re
import sys

NODE = re.compile(r'<Efficiency>\s*<Guid>.*</Efficiency>', re.S)
STAMP = re.compile(r'<ComputeStamp>([^<]*)</ComputeStamp>')
MAR = re.compile(r'<MarinelliBeakerDiameter>([^<]*)</MarinelliBeakerDiameter>')


def main():
    src, dst, keys = sys.argv[1], sys.argv[2], sys.argv[3].split(',')
    done = 0
    for key in keys:
        a = io.open(os.path.join(src, key + '.xml'), 'rb').read()
        b_path = os.path.join(dst, key + '.xml')
        b = io.open(b_path, 'rb').read()
        ta = a.decode('utf-8-sig')
        tb = b.decode('utf-8-sig')
        ma, mb = NODE.search(ta), NODE.search(tb)
        if not ma or not mb:
            raise SystemExit('%s: узла нет (источник %s, приёмник %s)' % (key, bool(ma), bool(mb)))
        node = ma.group(0)
        if b.count(b'\r\n') and not a.count(b'\r\n'):
            node = node.replace('\n', '\r\n')
        elif a.count(b'\r\n') and not b.count(b'\r\n'):
            node = node.replace('\r\n', '\n')
        new = tb[:mb.start()] + node + tb[mb.end():]
        d_before = MAR.search(mb.group(0)).group(1) if MAR.search(mb.group(0)) else '?'
        d_after = MAR.search(node).group(1) if MAR.search(node) else '?'
        bom = b.startswith(b'\xef\xbb\xbf')
        with open(b_path, 'wb') as fh:
            fh.write((b'\xef\xbb\xbf' if bom else b'') + new.encode('utf-8'))
        print('%-26s узел заменён: Ø маринелли %s -> %s; клеймо %s' % (key, d_before, d_after, STAMP.search(node).group(1)[:40]))
        done += 1
    print('перенесено узлов: %d' % done)
    return 0


if __name__ == '__main__':
    sys.exit(main())
