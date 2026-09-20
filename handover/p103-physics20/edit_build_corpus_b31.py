# -*- coding: utf-8 -*-
r"""П103 — `B31`: правило веса одного спектра в модели разрешения группы (решение Amber 18.09.2026,
дословно: «Ограничить вес одного спектра в модели группы»). Ключ `--res-spectrum-tol=` — рычаг замера.
CRLF сохраняется."""
import os
WT = r'D:\BqMoni_Claude\p103\wt'
p = os.path.join(WT, r'tools\CORPUS\scripts\build_corpus.py')
b = open(p, 'rb').read()
s = b.decode('utf-8').replace('\r\n', '\n')

reps = []
reps.append((
'''def resolution_points(state, det):
    """Точки (E, FWHM, вес) группы, очищенные от выбросов.

    Одна плохо севшая линия портит модель на всю группу: у AS80x80 первый прогон
    дал 46 % на 60 кэВ — квадратичная по E модель с большим c0, вытянутая парой
    завышенных ширин. Отбрасываем то, что уходит от медианы приведённой ширины
    FWHM/sqrt(E) больше чем в полтора раза.
    """
    pts = collect_points(state, det, 0.85, with_key=True)
    if len(pts) < 3:
        pts = collect_points(state, det, 0.0, with_key=True)
    if len(pts) < 3:
        _res_points_log(det, pts, [True] * len(pts))
        return [p[:3] for p in pts]
    red = np.array([f / np.sqrt(max(e, 1.0)) for e, f, _, _ in pts])
    med = float(np.median(red))
    kept = [0.6 * med <= r <= 1.7 * med for r in red]
    keep = [p for p, k in zip(pts, kept) if k]
    if len(keep) < 3:
        keep, kept = pts, [True] * len(pts)
    _res_points_log(det, pts, kept)
    return [p[:3] for p in keep]
''',
'''#: `B31` (решение Amber 18.09.2026, вопросником, дословно: «Ограничить вес
#: одного спектра в модели группы»). Допуск медианы приведённой ширины
#: FWHM/sqrt(E) ОДНОГО СПЕКТРА против медианы группы ПО СПЕКТРАМ (у каждого
#: спектра один голос): спектр вне [1/tol, tol] в модель не идёт вовсе.
#:
#: Что было. П99 (18.09.2026): новый спектр группы RC103 → подсказка разрешения
#: группы → `RC103_K40` (маринелли KCl, живое время 3.6 суток, собственная
#: ширина 13.7 % на 662 против 7.1…8.0 % у остальных семи — дрейф усиления за
#: долгую съёмку) принял линии ФОНА 1120 (Bi-214) и 2614 кэВ (Tl-208) с
#: приведёнными ширинами 2.94 и 3.29 при медиане группы 2.09 (×1.41 и ×1.57);
#: пофильтр `0.6…1.7 × медиана` их пропустил, и модель группы ушла 8.11 → 8.68 %
#: на 662 (`detectors.csv`, `RC-103.xml` Width_Fwhm 28 → 30), а с ней
#: `RC103_Lu176` χ²/ndf +14.6 %, `RC103_Cs137_0cm` +5.6 %. ⚠ Вес самих точек
#: (Σ 31.5 из 708 у группы, 4.4 %) ни при чём — измерено П103 по дампу
#: `--res-points=`: ограничивать надо не вес, а ПРАВО спектра голосовать за
#: разрешение кристалла, когда его собственные линии систематически шире.
#:
#: Порог 1.25: статистическая ошибка ширины слабой линии ±10 %, а разрешение
#: одного кристалла между спектрами больше 10…15 % не гуляет; K40 при ×1.50
#: вне порога с запасом. Гейт молчит, когда в группе меньше трёх спектров с
#: точками или после него осталось бы меньше двух спектров либо трёх точек.
#: Рычаг замера — `--res-spectrum-tol=<число>` (1.0 = гейт выключен, прежнее
#: поведение). Печать: кто отброшен и с каким отношением (лог пересборки).
RES_SPECTRUM_TOL = 1.25


def resolution_points(state, det, log=print):
    """Точки (E, FWHM, вес) группы, очищенные от выбросов.

    Одна плохо севшая линия портит модель на всю группу: у AS80x80 первый прогон
    дал 46 % на 60 кэВ — квадратичная по E модель с большим c0, вытянутая парой
    завышенных ширин. Отбрасываем то, что уходит от медианы приведённой ширины
    FWHM/sqrt(E) больше чем в полтора раза.

    `B31`: ДО пофильтра судится каждый СПЕКТР целиком — медиана приведённой
    ширины его точек против медианы группы по спектрам (`RES_SPECTRUM_TOL`);
    спектр, чьи линии систематически шире (дрейф за долгую съёмку) или уже,
    точек в модель не даёт. Так один спектр не двигает разрешение кристалла,
    сколько бы сильных линий у него ни было.
    """
    pts = collect_points(state, det, 0.85, with_key=True)
    if len(pts) < 3:
        pts = collect_points(state, det, 0.0, with_key=True)
    if len(pts) < 3:
        _res_points_log(det, pts, [True] * len(pts))
        return [p[:3] for p in pts]
    pts, gated = gate_spectra(det, pts, log)
    red = np.array([f / np.sqrt(max(e, 1.0)) for e, f, _, _ in pts])
    med = float(np.median(red))
    kept = [0.6 * med <= r <= 1.7 * med for r in red]
    keep = [p for p, k in zip(pts, kept) if k]
    if len(keep) < 3:
        keep, kept = pts, [True] * len(pts)
    _res_points_log(det, gated + pts, [False] * len(gated) + kept)
    return [p[:3] for p in keep]


def gate_spectra(det, pts, log=print):
    """`B31`: (принятые точки, отброшенные точки) — по медиане приведённой ширины
    спектра против медианы группы по спектрам. Печатает отброшенных."""
    tol = float(RES_SPECTRUM_TOL)
    by_key = {}
    for p in pts:
        by_key.setdefault(p[3], []).append(p[1] / np.sqrt(max(p[0], 1.0)))
    if tol <= 1.0 or len(by_key) < 3:
        return pts, []
    med_of = {k: float(np.median(v)) for k, v in by_key.items()}
    group = float(np.median(list(med_of.values())))
    if group <= 0.0:
        return pts, []
    out = {k: m / group for k, m in med_of.items() if not (1.0 / tol <= m / group <= tol)}
    if not out:
        return pts, []
    keep = [p for p in pts if p[3] not in out]
    if len(keep) < 3 or len(set(p[3] for p in keep)) < 2:
        log(u'  B31 %-9s гейт молчит: без %s осталось бы %d точек от %d спектров'
            % (det, u', '.join(sorted(out)), len(keep), len(set(p[3] for p in keep))))
        return pts, []
    for k in sorted(out):
        log(u'  B31 %-9s %s: медиана приведённой ширины %.3f против группы %.3f (×%.2f, допуск ×%.2f) — '
            u'%d точек в модель разрешения не идут'
            % (det, k, med_of[k], group, out[k], tol, len(by_key[k])))
    return keep, [p for p in pts if p[3] in out]
'''))
reps.append((
'''        elif a.startswith('--res-form='):''',
'''        elif a.startswith('--res-spectrum-tol='):
            # (`B31`) рычаг замера: допуск спектра против медианы группы; 1.0 — гейт выключен
            global RES_SPECTRUM_TOL
            RES_SPECTRUM_TOL = float(a.split('=', 1)[1])
            print('B31: допуск спектра в модели разрешения группы ×%.3f%s'
                  % (RES_SPECTRUM_TOL, ' (гейт выключен)' if RES_SPECTRUM_TOL <= 1.0 else ''))
        elif a.startswith('--res-form='):'''))
for o, n in reps:
    assert s.count(o) == 1, (o[:60], s.count(o))
    s = s.replace(o, n)
nb = s.replace('\n', '\r\n').encode('utf-8')
open(p, 'wb').write(nb)
print('ok CRLF', nb.count(b'\r\n'))
