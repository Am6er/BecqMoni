# -*- coding: utf-8 -*-
r"""П88 (AMBER22): сцены ториевого диска Amber с АЛЮМИНИЕВОЙ оправой (Amber 15.09.2026: «Поднёс - не магнитится»,
«Серебристая, лёгкая на ощупь» → Al ρ 2.70, не сталь) поверх стенда П67 — ребром 93 мм при ρ 2.8 / 3.3 / 3.8 и
контакт при 3.3; всё остальное (кабошон 4.08 мм, трубка, эквиваленты формата `.in`) — как в `mk_scenes.py` П67.
Плюс новая опись склада `index.csv`: прежние стальные сцены П67 и новые Al — на спектры полосы П88
(contact52k — корпусный файл; contact_cal0809 / edge93 / edge1709 — файлы Amber с ПЕРЕКАЛИБРОВАННЫМ фоном;
*_bg0 — те же файлы ДО перекалибровки, копии из D:\BqMoni_Claude\p88\backup). face81 — «Не использовать» (Amber 14.09).

    python mk_scenes_p88.py <каталог склада полосы (копия склада П67)>

Пишет <ключ>.in (cp1251, CRLF) только для новых сцен и ПЕРЕПИСЫВАЕТ index.csv (geometry,spectrum,preset,vessel).
"""
import io
import os
import sys

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p67-amber22\py')
import glass          # noqa: E402
import mk_scenes as m67   # noqa: E402

m67.TEMPLATE = r'D:\BqMoni_Claude\p88\wt\tools\CORPUS\corpus\geometries\AS80_th_disk.in'
RHO_AL = 2.70
AL = {13: 1.0}


def composite_al(t_al, t_p):
    """стенка-смесь: алюминий t_al см + целлюлоза t_p см по массовой толщине → (ρ, доли, толщина) — как m67.composite, но Al"""
    m_al = RHO_AL * t_al
    m_p = m67.RHO_PAPER * t_p
    t = t_al + t_p
    rho = (m_al + m_p) / t
    fr = {13: m_al / (m_al + m_p)}
    for z, w in m67.CELL.items():
        fr[z] = fr.get(z, 0.0) + w * m_p / (m_al + m_p)
    return rho, fr, t


def scene_contact_al(t, src_name, src_rho, src_fr, ring_t=m67.RING_T):
    t = m67.set_key(t, 'SourceType', 'CYLINDER')
    t = m67.set_key(t, 'SC_BeakerToDetectorFrontDistance', '0 cm')
    t = m67.set_key(t, 'SC_BeakerDiameter', '%s cm' % m67.fmt(m67.D_GLASS + 2 * ring_t))
    t = m67.set_key(t, 'SC_BeakerHeight', '%s cm' % m67.fmt(m67.H_GLASS))
    t = m67.set_key(t, 'SC_BeakerSideWallThickness', '%s cm' % m67.fmt(ring_t))
    t = m67.set_key(t, 'SC_BeakerEndWallThickness', '0 cm')
    t = m67.set_key(t, 'SC_SourceHeight', '%s cm' % m67.fmt(m67.H_GLASS))
    return m67.with_materials(t, src_name, src_rho, src_fr, 'Aluminium ring', RHO_AL, AL)


def scene_edge93_al(t, src_name, src_rho, src_fr, ring_t=m67.RING_T):
    rho_w, fr_w, t_w = composite_al(ring_t, m67.EDGE_PAPER)
    t = m67.set_key(t, 'SourceType', 'BOX')
    t = m67.set_key(t, 'SB_BoxToDetectorFrontDistance', '%s cm' % m67.fmt(m67.Z_EDGE_C - m67.EDGE_H / 2 - t_w))
    t = m67.set_key(t, 'SB_SourceX', '%s cm' % m67.fmt(m67.D_GLASS))
    t = m67.set_key(t, 'SB_SourceY', '%s cm' % m67.fmt(m67.H_GLASS))
    t = m67.set_key(t, 'SB_BoxSideWallThickness', '%s cm' % m67.fmt(m67.EDGE_SIDE))
    t = m67.set_key(t, 'SB_BoxEndWallThickness', '%s cm' % m67.fmt(t_w))
    t = m67.set_key(t, 'SB_SourceHeight', '%s cm' % m67.fmt(m67.EDGE_H))
    return m67.with_materials(t, src_name, src_rho, src_fr, 'Aluminium ring + cardboard (equiv. mix)', rho_w, fr_w)


def main():
    out = sys.argv[1]
    t0 = io.open(m67.TEMPLATE, 'rb').read().decode('cp1251')
    assert 'SourceType = CYLINDER' in t0 and 'SC_RoSource = 4.345' in t0
    new = []
    for rho in (2.8, 3.3, 3.8):
        tag = 'r%02d' % int(round(rho * 10))
        fr = glass.oxide_fractions(glass.composition(rho))
        name = 'Th glass P67 rho %.1f' % rho
        new.append(('AS80_p88_edge93_%s_al' % tag, scene_edge93_al(t0, name, rho, fr), 'edge93', rho))
    fr = glass.oxide_fractions(glass.composition(3.3))
    new.append(('AS80_p88_contact_r33_al', scene_contact_al(t0, 'Th glass P67 rho 3.3', 3.3, fr), 'contact', 3.3))
    for key, text, geom, rho in new:
        if '--step2' in sys.argv and os.path.exists(os.path.join(out, key + '.rmx')):
            print('сцена', key, '— .in не перезаписывается (матрица есть)'); continue
        with io.open(os.path.join(out, key + '.in'), 'wb') as fh:
            fh.write(text.encode('cp1251'))
        print('сцена', key)
    rho_w, fr_w, t_w = composite_al(m67.RING_T, m67.EDGE_PAPER)
    print('смесь ребром Al: ρ %.4f, толщина %.3f см, доли %s' % (rho_w, t_w, ' '.join('Z%d %.4f' % (z, w) for z, w in sorted(fr_w.items()))))
    rho_s, fr_s, t_s = m67.composite(m67.RING_T, m67.EDGE_PAPER)
    print('смесь ребром сталь (П67): ρ %.4f, толщина %.3f см, доли %s' % (rho_s, t_s, ' '.join('Z%d %.4f' % (z, w) for z, w in sorted(fr_s.items()))))

    # опись: стальные сцены П67 (без face81) + Al на спектры полосы
    step2 = '--step2' in sys.argv
    spectra = ({'contact': ['contact52k', 'contact_cal0809', 'contact_cal0809_s1'], 'edge93': ['edge93', 'edge93_s1', 'edge1709', 'edge1709_s1']} if step2 else
               {'contact': ['contact52k', 'contact_cal0809', 'contact_cal0809_bg0'], 'edge93': ['edge93', 'edge93_bg0', 'edge1709', 'edge1709_bg0']})
    rows = ['geometry,spectrum,preset,vessel']
    steel = []
    for rho in m67.RHOS:
        tag = 'r%02d' % int(round(rho * 10))
        steel.append(('AS80_p67_contact_%s' % tag, 'contact', rho, 'сталь 1.0'))
        steel.append(('AS80_p67_edge93_%s' % tag, 'edge93', rho, 'сталь 1.0'))
    steel.append(('AS80_p67_edge93_r33_ring05', 'edge93', 3.3, 'сталь 0.5'))
    steel.append(('AS80_p67_edge93_r33_ring20', 'edge93', 3.3, 'сталь 2.0'))
    steel.append(('AS80_p67_contact_p13', 'contact', glass.RHO_P13, 'сталь 1.0'))
    steel.append(('AS80_p67_edge93_p13', 'edge93', glass.RHO_P13, 'сталь 1.0'))
    for key, geom, rho, ring in steel:
        assert os.path.exists(os.path.join(out, key + '.rmx')), key
        for sp in spectra[geom]:
            rows.append('%s,%s__%s,Atom Spectra Pro 80x80,"диск П67 %s ρ %.3f кольцо %s мм"' % (key, sp, key, geom, rho, ring))
    for key, text, geom, rho in new:
        for sp in spectra[geom]:
            rows.append('%s,%s__%s,Atom Spectra Pro 80x80,"диск П88 %s ρ %.3f кольцо Al 1.0 мм"' % (key, sp, key, geom, rho))
    with io.open(os.path.join(out, 'index.csv'), 'w', encoding='utf-8', newline='') as fh:
        fh.write('\n'.join(rows) + '\n')
    print('опись: %d строк, новых сцен %d' % (len(rows) - 1, len(new)))


if __name__ == '__main__':
    main()
