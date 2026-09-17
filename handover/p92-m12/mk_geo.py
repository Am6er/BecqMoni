# -*- coding: utf-8 -*-
# П92 (M12): варианты геометрий для абляции обвязки — из копий живых .in
# (D:\BqMoni_Claude\p92\geo\*.in, только чтение склада). Меняются ТОЛЬКО толщины
# обвязки и расстояние источника (чтобы кристалл-источник стояли как были).
#   bare    — обвязки нет вовсе (отражатель, зазор, корпус, оправа = 0)
#   noback  — нет оправы (Al сзади)
#   nofront — нет передних слоёв (отражатель/зазор/корпус спереди)
#   noside  — нет боковых слоёв (отражатель/зазор/корпус сбоку)
# Читать и писать с newline='' (одиночный CR в .in ЛСРМ не рвать).
import io, os, re, sys

geo_dir = r'D:\BqMoni_Claude\p92\geo'


def set_key(text, key, value):
    pat = re.compile(r'^(%s\s*=\s*)([-\d.eE+]+)(\s*\S*)' % re.escape(key), re.M)
    m = pat.search(text)
    assert m, key
    return pat.sub(lambda mm: mm.group(1) + value + mm.group(3), text, count=1), float(m.group(2))


def variant(src, dst, zero_keys, dist_key=None):
    text = io.open(src, encoding='cp1251', newline='').read()
    front_total = 0.0
    for k in ('DS_CrystalFrontReflectorThickness', 'DS_CrystalFrontGapThickness', 'DS_CrystalFrontCladdingThickness'):
        m = re.search(r'^%s\s*=\s*([-\d.eE+]+)' % k, text, re.M)
        front_total += float(m.group(1)) if m else 0.0
    removed_front = 0.0
    for k in zero_keys:
        if not re.search(r'^%s\s*=' % k, text, re.M):
            continue                        # ключа нет в файле (старый .in без зазора) — толщина 0
        text, old = set_key(text, k, '0')
        if k in ('DS_CrystalFrontReflectorThickness', 'DS_CrystalFrontGapThickness', 'DS_CrystalFrontCladdingThickness'):
            removed_front += old
    if dist_key is not None and removed_front > 0.0:
        text, old = set_key(text, dist_key, '%g' % (old_dist(text, dist_key) + removed_front))
    io.open(dst, 'w', encoding='cp1251', newline='').write(text)
    print(os.path.basename(dst), 'front removed %.3f cm' % removed_front)


def old_dist(text, key):
    m = re.search(r'^%s\s*=\s*([-\d.eE+]+)' % key, text, re.M)
    return float(m.group(1))


FRONT = ('DS_CrystalFrontReflectorThickness', 'DS_CrystalFrontGapThickness', 'DS_CrystalFrontCladdingThickness')
SIDE = ('DS_CrystalSideReflectorThickness', 'DS_CrystalSideGapThickness', 'DS_CrystalSideCladdingThickness')
BACK = ('DS_DetectorMountingThickness',)

for base, dist_key in (('RC103_point0_p55', 'pdistance'), ('AS80_th_disk', 'SC_BeakerToDetectorFrontDistance'),
                       ('RC103_point0', 'pdistance')):
    src = os.path.join(geo_dir, base + '.in')
    variant(src, os.path.join(geo_dir, base + '_bare.in'), FRONT + SIDE + BACK, dist_key)
    variant(src, os.path.join(geo_dir, base + '_noback.in'), BACK)
    variant(src, os.path.join(geo_dir, base + '_nofront.in'), FRONT, dist_key)
    variant(src, os.path.join(geo_dir, base + '_noside.in'), SIDE)
