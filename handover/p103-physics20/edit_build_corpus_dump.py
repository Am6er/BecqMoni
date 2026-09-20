# -*- coding: utf-8 -*-
r"""П103 — шаг 1 `B31`: диагностика точек модели разрешения группы (`--res-points=<csv>`):
кто (спектр) даёт какие точки (E, FWHM, вес), что режет фильтр выбросов. Правил ещё не меняет.
CRLF сохраняется."""
import os
WT = r'D:\BqMoni_Claude\p103\wt'
p = os.path.join(WT, r'tools\CORPUS\scripts\build_corpus.py')
b = open(p, 'rb').read()
assert b[:3] != b'\xef\xbb\xbf'
s = b.decode('utf-8').replace('\r\n', '\n')

reps = []
# 1. collect_points несёт имя спектра (4-й элемент) по запросу; наружу по умолчанию — прежние тройки
reps.append((
"""def collect_points(state, det, min_purity):
    pts = []
    for st in state.values():
        if st['det'] != det:
            continue
        for a in st['accepted']:
            # `B25`: опора низа задаёт ШКАЛУ, но не РАЗРЕШЕНИЕ — довод там же,
            # где ставится признак (`match_low_anchor`).
            if a.get('xlow'):
                continue
            if a.get('purity', 1.0) < min_purity:
                continue
            pts.append((a['e_ref'], a['fwhm'] * abs(st['ecal'].dEdch(a['ch'])),
                        min(a['sig'], 100.0) * a.get('purity', 1.0)))
    return pts
""",
"""def collect_points(state, det, min_purity, with_key=False):
    \"\"\"Точки (E, FWHM, вес) группы `det`; `with_key` — четвёртым элементом имя
    спектра (нужно правилу `B31` и диагностике `--res-points=`; наружу, в
    отпечаток `points_sha` и в фит, уходят прежние тройки).\"\"\"
    pts = []
    for key, st in state.items():
        if st['det'] != det:
            continue
        for a in st['accepted']:
            # `B25`: опора низа задаёт ШКАЛУ, но не РАЗРЕШЕНИЕ — довод там же,
            # где ставится признак (`match_low_anchor`).
            if a.get('xlow'):
                continue
            if a.get('purity', 1.0) < min_purity:
                continue
            p = (a['e_ref'], a['fwhm'] * abs(st['ecal'].dEdch(a['ch'])),
                 min(a['sig'], 100.0) * a.get('purity', 1.0))
            pts.append(p + (key,) if with_key else p)
    return pts
"""))
# 2. resolution_points — с диагностикой (пока прежнее правило)
reps.append((
"""    pts = collect_points(state, det, 0.85)
    if len(pts) < 3:
        pts = collect_points(state, det, 0.0)
    if len(pts) < 3:
        return pts
    red = np.array([f / np.sqrt(max(e, 1.0)) for e, f, _ in pts])
    med = float(np.median(red))
    keep = [p for p, r in zip(pts, red) if 0.6 * med <= r <= 1.7 * med]
    return keep if len(keep) >= 3 else pts
""",
"""    pts = collect_points(state, det, 0.85, with_key=True)
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


#: Диагностика `B31`: `--res-points=<csv>` — все точки модели разрешения по
#: группам с именем спектра и приговором фильтра (стадия 3 зовёт
#: `resolution_points` последней — в файле остаётся её вызов).
RES_POINTS_CSV = None
_RES_POINTS = {}


def _res_points_log(det, pts, kept):
    if not RES_POINTS_CSV:
        return
    _RES_POINTS[det] = [(p[3], p[0], p[1], p[2], k) for p, k in zip(pts, kept)]
    with io.open(RES_POINTS_CSV, 'w', encoding='utf-8', newline='') as fh:
        fh.write(u'det,spectrum,e_kev,fwhm_kev,weight,reduced,kept\\n')
        for d in sorted(_RES_POINTS):
            for key, e, f, w, k in sorted(_RES_POINTS[d]):
                fh.write(u'%s,%s,%r,%r,%r,%r,%d\\n' % (d, key, float(e), float(f), float(w),
                                                        float(f) / np.sqrt(max(float(e), 1.0)), 1 if k else 0))
"""))
# 3. ключ командной строки
reps.append((
"""        elif a.startswith('--res-form='):""",
"""        elif a.startswith('--res-points='):
            # (`B31`) диагностика: точки модели разрешения по группам с именем спектра
            global RES_POINTS_CSV
            RES_POINTS_CSV = a.split('=', 1)[1]
            print('точки модели разрешения пишутся в %s' % RES_POINTS_CSV)
        elif a.startswith('--res-form='):"""))
for o, n in reps:
    assert s.count(o) == 1, (o[:60], s.count(o))
    s = s.replace(o, n)
nb = s.replace('\n', '\r\n').encode('utf-8')
open(p, 'wb').write(nb)
print('ok CRLF', nb.count(b'\r\n'))
