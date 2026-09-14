# -*- coding: utf-8 -*-
"""П67: итоговая проверка моделей сцен .in против точной геометрии (кабошон 3+2, кольцо 1 мм, трубка) — те параметры,
что ушли в mk_scenes.py: контакт цилиндр 4.08 + сталь 1.0; лицом 81 — цилиндр 4.08, торец целлюлоза 8.0; ребром —
коробка 40×4.08×40, торец смесь (сталь 1.0 + целлюлоза 7.5), боковая 0.8. Печатает модель/точно по энергиям и
сдвиг 238:2614 при ρ 2.8/3.3/3.8/4.2 и П13, чувствительность к кольцу, губку."""
import sys

import numpy as np

sys.path.insert(0, r'D:\BqMoni_Claude\p67\py')
import glass   # noqa: E402
import tracer  # noqa: E402

n = 800000
print('ПРОВЕРКА МОДЕЛЕЙ СЦЕН .in ПРОТИВ ТОЧНОЙ ГЕОМЕТРИИ (кабошон 3+2, H_EQ %.2f): модель/точно по энергиям %s; 238:2614 — сдвиг отношения' % (tracer.H_EQ, tracer.E_LIST))


def trio(rho, fr, tag):
    ex, _ = tracer.run(tracer.scene_contact_exact, n, rho, fr)
    m, _ = tracer.run(tracer.scene_contact_model, n, rho, fr, h=4.08, side=1.0)
    tracer.show('%s contact: cyl 4.08 + steel side 1.0' % tag, m, ex)
    ex, _ = tracer.run(tracer.scene_face81_exact, n, rho, fr)
    m, _ = tracer.run(tracer.scene_contact_model, n, rho, fr, z_bottom=82.0, side=0.0, end=8.0, end_key='paper', h=4.08)
    tracer.show('%s face81: cyl 4.08, paper end 8.0' % tag, m, ex)
    ex, _ = tracer.run(tracer.scene_edge_exact, n, rho, fr)
    m, _ = tracer.run(tracer.scene_edge_model2, n, rho, fr, ay=2.04, t_fe=1.0, t_p=7.5, w=0.8)
    tracer.show('%s edge93: box 40x4.08x40, fe1.0+p7.5, w0.8' % tag, m, ex)


for rho in (2.8, 3.3, 3.8, 4.2):
    trio(rho, glass.oxide_fractions(glass.composition(rho)), 'rho %.1f' % rho)
trio(glass.RHO_P13, glass.P13, 'P13 4.345')
print('-- чувствительность к кольцу ребром при rho 3.3: точно ring 0.5/2.0 против точно 1.0; модель fe 0.5/2.0 против точно')
rho = 3.3
fr = glass.oxide_fractions(glass.composition(rho))
ex1, _ = tracer.run(tracer.scene_edge_exact, n, rho, fr, ring_t=1.0)
for rt in (0.5, 2.0):
    ex, _ = tracer.run(tracer.scene_edge_exact, n, rho, fr, ring_t=rt)
    tracer.show('exact ring %.1f vs exact ring 1.0' % rt, ex, ex1)
    m, _ = tracer.run(tracer.scene_edge_model2, n, rho, fr, ay=2.04, t_fe=rt, t_p=7.5, w=0.8)
    tracer.show('model fe %.1f vs exact ring %.1f' % (rt, rt), m, ex)
print('-- губка 1 мм стали к детектору (контакт): точно+губка / точно')
ex, _ = tracer.run(tracer.scene_contact_exact, n, rho, fr)
exl, _ = tracer.run(tracer.scene_contact_exact, n, rho, fr, lip=1.0)
tracer.show('contact + lip 1.0', exl, ex)
