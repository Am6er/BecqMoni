# -*- coding: utf-8 -*-
"""П72 (T258): свести вывод geom_diff.py — что в каждой разошедшейся сцене расходится СВЕРХ блока зазора
(`AMBER1`: `GeometryWriter` с 08.09.2026 пишет `DS_Crystal*GapThickness` и блок «Gap between reflector and
cladding», а 42 сцены корпуса записаны раньше). Блок — ровно 15 строк: два размера, пустая, заголовок, 11 строк вещества.

    python handover/p72-t258-t259/gap_only.py handover/p72-t258-t259/geom_diff_new.txt
"""
import io, re, sys
GAP_SIZES = ['+DS_CrystalFrontGapThickness = 0 cm', '+DS_CrystalSideGapThickness = 0 cm']
GAP_BLOCK = ['+', '+// Gap between reflector and cladding ', '+DS_nCrystalGapElements = 2', '+DS_RoCrystalGap = 0.001205',
             '+DS_ZCrystalGap[0] = 7', '+DS_FractionsCrystalGap[0] = 0.636483', '+DS_ZCrystalGap[1] = 8',
             '+DS_FractionsCrystalGap[1] = 0.363517', '+DS_FractionTypeGap = MASS', '+M_DS_Gap.MName = Air, dry',
             '+M_DS_Gap.Nmaterials = 1', '+M_DS_Gap.Name[0] = Air, dry                                 ',
             '+M_DS_Gap.MatRelWeight[0] = 1']

def main():
    text = io.open(sys.argv[1], encoding='utf-8').read()
    blocks = re.split(r'\n(?=РАЗОШЁЛСЯ|НЕТ|ЛИШНИЙ|index|живых)', text)
    n_gap_only, n_other = 0, 0
    for b in blocks:
        if not b.startswith('РАЗОШЁЛСЯ'):
            continue
        name = b.split()[1].rstrip(':')
        lines = [l[6:] for l in b.splitlines()[1:] if l.startswith('      ')]
        lines = [l for l in lines if not (l.startswith('---') or l.startswith('+++') or l.startswith('@@'))]
        # блок зазора — как МНОЖЕСТВО строк (difflib ставит пустую строку то до, то после блока)
        import collections
        want = collections.Counter(GAP_SIZES + GAP_BLOCK)
        have = collections.Counter(lines)
        rest = have - want
        if have >= want and not rest:
            n_gap_only += 1
            print('%-34s только блок зазора (15 строк)' % name)
        else:
            n_other += 1
            print('%-34s блок зазора: %s; СВЕРХ НЕГО %d строк: %s'
                  % (name, 'да' if have >= want else 'нет', sum(rest.values()), sorted(rest.elements())))
    print()
    print('разошедшихся: %d — только блок зазора %d, иное %d' % (n_gap_only + n_other, n_gap_only, n_other))
    return 0 if n_other == 0 else 1

if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try: s.reconfigure(encoding='utf-8', errors='replace')
        except Exception: pass
    sys.exit(main())
