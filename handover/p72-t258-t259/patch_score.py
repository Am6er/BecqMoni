# -*- coding: utf-8 -*-
"""П72 (T259): правка `tools/pie/score.py` — метка-член ряда («Rn-222») в истине манифеста по правилу
`chain_labels` (тому же, что `FsaSampleChain.FromLabel`), ключ `--manifest=` (копия манифеста плеча).

    python handover/p72-t258-t259/patch_score.py <путь к score.py>
"""
import sys

p = sys.argv[1]
raw = open(p, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
crlf = b'\r\n' in raw
t = raw.decode('utf-8-sig').replace('\r\n', '\n')

old_imports = """import argparse
import csv
import io
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, '..', 'CORPUS', 'corpus', 'manifest.csv')
"""
new_imports = """import argparse
import csv
import io
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, '..', 'CORPUS', 'corpus', 'manifest.csv')
# Правило метки-члена ряда — общее с приложением (`T259`, `chain_labels.py`).
sys.path.insert(0, os.path.join(HERE, '..', 'CORPUS', 'scripts'))
import chain_labels                                   # noqa: E402
import chains                                         # noqa: E402
"""
assert t.count(old_imports) == 1
t = t.replace(old_imports, new_imports)

old_map = """# манифест -> компоненты. Компонент U-238 в pie — только голова ряда
# (радиевая ветвь вырезана и живёт в Ra-226), поэтому равновесный «U-238»
# манифеста означает оба компонента, а «U-238u» (стекло) — только голову.
CHAIN_MAP = {
    'Th-232': ['Th-232'], 'Th-228': ['Th-228'], 'Ra-226': ['Ra-226'],
    'U-238': ['U-238', 'Ra-226'], 'U-238u': ['U-238'], 'U-235': ['U-235'],
}
"""
new_map = """# манифест -> компоненты. Компонент U-238 в pie — только голова ряда
# (радиевая ветвь вырезана и живёт в Ra-226), поэтому равновесный «U-238»
# манифеста означает оба компонента, а «U-238u» (стекло) — только голову.
CHAIN_MAP = {
    'Th-232': ['Th-232'], 'Th-228': ['Th-228'], 'Ra-226': ['Ra-226'],
    'U-238': ['U-238', 'Ra-226'], 'U-238u': ['U-238'], 'U-235': ['U-235'],
}
# (`T259`) Метка-ЧЛЕН ряда («Rn-222», «Rn-220», «Pb-214»…) — подряд от этого
# члена по правилу приложения (`FsaSampleChain.FromLabel`; здесь —
# `chain_labels.chain_root`: разбор «Xx-NNN», распадается ли по nucdb, обрыв
# по периоду). В истине pie такой член засчитывается СЕМЕЙСТВОМ того ряда из
# `CHAIN_MAP`, в чей обход он входит (Rn-222 → Ra-226; Rn-220 → торий): разбор
# приложения называет дочерние (Pb-214, Bi-214), и семейство у них то же, что у
# всего ряда. Неизвестная база метка — отказ словами, как у `CHAIN_MAP`.
# Кэш — чтобы не ходить в базу на каждой строке манифеста.
_MEMBER_CHAIN = {}


def chain_components(label):
    \"\"\"Метка ряда манифеста → компоненты pie (истина).\"\"\"
    if label in CHAIN_MAP:
        return CHAIN_MAP[label]
    if label in _MEMBER_CHAIN:
        return _MEMBER_CHAIN[label]
    root, _ = chain_labels.chain_root(label)          # ValueError — неизвестная метка
    c = chains.conn()
    try:
        for series in CHAIN_MAP:
            if series == chain_labels.GLASS_LABEL:
                continue
            if root in chains.chain_branches(chains.CHAINS[series], c):
                _MEMBER_CHAIN[label] = CHAIN_MAP[series]
                return CHAIN_MAP[series]
    finally:
        c.close()
    raise ValueError('метка ряда %r: нуклид %s не входит ни в один ряд CHAIN_MAP (%s) — '
                     'семейство истины назначить нечем (T259)'
                     % (label, root, ', '.join(k for k in CHAIN_MAP if k != chain_labels.GLASS_LABEL)))
"""
assert t.count(old_map) == 1
t = t.replace(old_map, new_map)

old_truth = """def load_truth():
    truth = {}
    with open(MANIFEST, encoding='utf-8-sig') as fh:
        for row in csv.DictReader(fh):
            comps = set()
            for ch in (row['chains'] or '').split(';'):
                ch = ch.strip()
                if not ch:
                    continue
                if ch not in CHAIN_MAP:
                    sys.exit('манифест: неизвестная цепочка %r у %s' % (ch, row['key']))
                comps.update(CHAIN_MAP[ch])
"""
new_truth = """def load_truth(manifest=None):
    truth = {}
    with open(manifest or MANIFEST, encoding='utf-8-sig') as fh:
        for row in csv.DictReader(fh):
            comps = set()
            for ch in (row['chains'] or '').split(';'):
                ch = ch.strip()
                if not ch:
                    continue
                try:
                    comps.update(chain_components(ch))
                except ValueError as why:
                    sys.exit('манифест: неизвестная цепочка %r у %s — %s' % (ch, row['key'], why))
"""
assert t.count(old_truth) == 1
t = t.replace(old_truth, new_truth)

old_arg = """    ap.add_argument('--only', default=None,
                    help='ограничить объявленным списком: имена через запятую '
                         'либо путь к csv/txt, где ключ — первый столбец '
                         '(строки с # и заголовок пропускаются)')
    args = ap.parse_args()
"""
new_arg = """    ap.add_argument('--only', default=None,
                    help='ограничить объявленным списком: имена через запятую '
                         'либо путь к csv/txt, где ключ — первый столбец '
                         '(строки с # и заголовок пропускаются)')
    # (`T259`) Плечо с ДРУГОЙ истиной судится по КОПИИ манифеста, а не по
    # правке живого: смена истины меняет отпечаток объявленной базы.
    ap.add_argument('--manifest', default=None,
                    help='манифест истины (умолчание — corpus/manifest.csv); '
                         'копия с изменённой истиной для плеча')
    args = ap.parse_args()
"""
assert t.count(old_arg) == 1
t = t.replace(old_arg, new_arg)

old_call = "    truth = load_truth()\n"
new_call = "    truth = load_truth(args.manifest)\n"
assert t.count(old_call) == 1
t = t.replace(old_call, new_call)

out = t.replace('\n', '\r\n') if crlf else t
open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
print('ok bom=%s crlf=%s' % (bom, crlf))
