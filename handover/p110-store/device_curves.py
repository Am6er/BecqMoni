# -*- coding: utf-8 -*-
r"""П110 (19.09.2026): кривые эффективности из конфигов приборов Amber и их геометрии.
Читает <каталог с config\device\*.xml> и печатает CSV: прибор, кривая (Guid, имя), тип источника,
зазор (толщины и вещество), клеймо кривой, UseResponseMatrix, LastUpdated. Ничего не пишет.
Использование: python device_curves.py <каталог device> <каталог response>
"""
import sys, os, glob
import xml.etree.ElementTree as ET

def txt(el, tag, default=''):
    x = el.find(tag)
    return (x.text or '').strip() if x is not None and x.text is not None else default

def main():
    dev_dir, resp_dir = sys.argv[1], sys.argv[2]
    rmx = {os.path.splitext(os.path.basename(p))[0].lower(): p for p in glob.glob(os.path.join(resp_dir, '*.rmx'))}
    cols = ['device_file','device_name','device_guid','curve_guid','curve_name','last_updated','source_type','scene',
            'crystal','front_gap_mm','side_gap_mm','gap_material','gap_density','source_material','use_matrix',
            'curve_stamp','rmx_present']
    print(';'.join(cols))
    used = set()
    for p in sorted(glob.glob(os.path.join(dev_dir, '*.xml'))):
        try:
            root = ET.parse(p).getroot()
        except ET.ParseError as e:
            print(os.path.basename(p) + ';PARSE ERROR ' + str(e)); continue
        if root.find('EfficiencyConfigs') is None and root.find('Guid') is None:
            continue
        dname = txt(root, 'Name'); dguid = txt(root, 'Guid')
        effs = root.find('EfficiencyConfigs')
        items = list(effs) if effs is not None else []
        if not items:
            print(';'.join([os.path.basename(p), dname, dguid] + [''] * (len(cols) - 3)))
            continue
        for e in items:
            g = e.find('Geometry')
            gap = g.find('Gap') if g is not None else None
            src = g.find('Source') if g is not None else None
            cg = txt(e, 'Guid').lower()
            used.add(cg)
            row = [os.path.basename(p), dname, dguid, cg, txt(e, 'Name'), txt(e, 'LastUpdated')[:16],
                   txt(g, 'SourceType') if g is not None else '(нет геометрии)',
                   txt(g, 'Scene') if g is not None else '',
                   txt(g.find('Crystal'), 'Name') if g is not None and g.find('Crystal') is not None else '',
                   txt(g, 'FrontGapThickness') if g is not None else '',
                   txt(g, 'SideGapThickness') if g is not None else '',
                   txt(gap, 'Name') if gap is not None else '(нет <Gap>)',
                   txt(gap, 'Density') if gap is not None else '',
                   txt(src, 'Name') if src is not None else '',
                   txt(e, 'UseResponseMatrix'), txt(e, 'ComputeStamp').replace(';', ','),
                   'да' if cg in rmx else 'НЕТ']
            print(';'.join(row))
    orphans = sorted(set(rmx) - used)
    print()
    print('# матрицы без кривой (сироты): %d' % len(orphans))
    for o in orphans:
        print('# ' + o + '.rmx')

if __name__ == '__main__':
    main()
