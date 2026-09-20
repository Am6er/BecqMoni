# -*- coding: utf-8 -*-
r"""П103 — `B31`: самопроверка гейта `--selftest-b31` (искусственный спектр-выброс в тестовой группе не сдвигает
модель; гейт выключен — сдвигает; честный спектр в допуске не режется). Без библиотеки. CRLF сохраняется."""
import os
WT = r'D:\BqMoni_Claude\p103\wt'
p = os.path.join(WT, r'tools\CORPUS\scripts\build_corpus.py')
b = open(p, 'rb').read()
s = b.decode('utf-8').replace('\r\n', '\n')

reps = []
reps.append((
'''    return keep, [p for p in pts if p[3] in out]
''',
'''    return keep, [p for p in pts if p[3] in out]


def selftest_b31():
    """`B31`: положительный контроль гейта на искусственной группе (без библиотеки).

    Группа из четырёх честных спектров (приведённая ширина 2.0 ± 5 %, веса
    10…100) и ОДНОГО выброса — сильного спектра с шестью точками веса 100 при
    приведённой ширине ×1.5 (как `RC103_K40` П99: ×1.41…1.57). Ожидание:
    (1) с гейтом модель группы = модели честных четырёх (точки те же);
    (2) без гейта (`tol = 1.0`) модель на 662 уходит больше чем на 5 %;
    (3) честный пятый спектр в допуске (×1.10) гейтом не режется.
    Код 0 — сошлось, 1 — нет. Зовётся `build_corpus.py --selftest-b31`.
    """
    global RES_SPECTRUM_TOL
    saved = RES_SPECTRUM_TOL
    rng = np.random.RandomState(20260918)
    energies = [186.2, 351.9, 609.3, 661.7, 911.2, 1120.3, 1460.8, 1764.5, 2614.5]
    honest = []
    for i in range(4):
        for e in energies[i::2]:
            r = 2.0 * (1.0 + 0.05 * rng.uniform(-1.0, 1.0))
            honest.append((e, r * np.sqrt(e), float(rng.uniform(10.0, 100.0)), 'honest_%d' % i))
    outlier = [(e, 3.0 * np.sqrt(e), 100.0, 'drifted') for e in energies[::1][:6]]
    within = [(e, 2.2 * np.sqrt(e), 100.0, 'honest_4') for e in energies[1::3]]
    quiet = lambda *_a: None

    def model(points):
        return corpus_calib.resolution_fn(corpus_calib.fit_resolution_kev([q[:3] for q in points]))(662.0)

    bad = 0
    RES_SPECTRUM_TOL = 1.25
    kept, gone = gate_spectra('TEST', honest + outlier, quiet)
    ok1 = sorted(gone) == sorted(outlier) and abs(model(kept) / model(honest) - 1.0) < 1e-12
    print('  (1) выброс ×1.5 отброшен, модель = честной: %s (662: %.3f против %.3f кэВ)'
          % ('да' if ok1 else 'НЕТ', model(kept), model(honest)))
    bad += 0 if ok1 else 1
    RES_SPECTRUM_TOL = 1.0
    kept0, gone0 = gate_spectra('TEST', honest + outlier, quiet)
    shift = model(kept0) / model(honest) - 1.0
    ok2 = not gone0 and shift > 0.05
    print('  (2) гейт выключен: выброс в модели, сдвиг на 662 %+.1f %% (> 5 %%): %s' % (100 * shift, 'да' if ok2 else 'НЕТ'))
    bad += 0 if ok2 else 1
    RES_SPECTRUM_TOL = 1.25
    kept2, gone2 = gate_spectra('TEST', honest + within, quiet)
    ok3 = not gone2 and len(kept2) == len(honest) + len(within)
    print('  (3) честный спектр ×1.10 в допуске не режется: %s' % ('да' if ok3 else 'НЕТ'))
    bad += 0 if ok3 else 1
    RES_SPECTRUM_TOL = saved
    print('B31 самопроверка: %s' % ('СОШЛОСЬ' if bad == 0 else 'НЕ СОШЛОСЬ (%d)' % bad))
    return bad
'''))
reps.append((
'''def main():
    only = None
    if not library_permission(sys.argv[1:]):
        return None
''',
'''def main():
    only = None
    if '--selftest-b31' in sys.argv[1:]:
        # (`B31`) самопроверка гейта — без библиотеки и без записи; код — приговор
        sys.exit(selftest_b31())
    if not library_permission(sys.argv[1:]):
        return None
'''))
for o, n in reps:
    assert s.count(o) == 1, (o[:60], s.count(o))
    s = s.replace(o, n)
nb = s.replace('\n', '\r\n').encode('utf-8')
open(p, 'wb').write(nb)
print('ok CRLF', nb.count(b'\r\n'))
