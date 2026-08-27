# -*- coding: utf-8 -*-
u"""Слепой перемер гейта отбора опорных линий: вершина, ложь, цена порога.

Мерка ничего не меняет и ничего не пересобирает: читает ПОСТАВЛЕННЫЕ спектры
корпуса (`corpus/spectra`, все 129), их собственную энергокалибровку и модель
разрешения ГРУППЫ из `corpus/detectors.csv`, и прогоняет по ним тот же
`corpus_calib.match_lines`, каким корпус набирает опорные линии.

Отвечает на три вопроса, и все три — ПО ЧАСТЯМ корпуса (`corpus/parts.csv`),
без сложения частей:

1. **Есть ли у линии штатного набора вершина.** Проверка (`vertex`) идёт по
   СЫРЫМ отсчётам и про гаусс не знает вовсе: сглаженные тройкой отсчёты
   обязаны иметь ВНУТРЕННИЙ максимум, поднятый на 3·sqrt(N) над прямой по краям
   окна и спадающий на столько же по ОБЕ стороны. Центр фита в проверку не
   входит — иначе она подтверждала бы сама себя.

   ⚠ И вот что про эту проверку надо знать раньше, чем цитировать её долю:
   **она СЛЕПА к широким слабым пикам**, и насколько — меряет стадия B.

2. **Отрицательный контроль.** Те же линии, отодвинутые на ±5 ПШПВ; такие по
   построению не существуют. ⛔ Считается ДОЛЯ ПРИНЯТЫХ ОТ ПРЕДЛОЖЕННЫХ, а не
   голое число найденных: списков два, они разной длины, и «нашлось 433 ложных
   против 493 настоящих» без знаменателей не значит ничего.

3. **Цена порога по `sig_fit` = A/sigma(A)** (ковариация фита; считает её
   `gaussfit._amp_error`, гейтом она не служит). Для порогов 3 и 8: сколько
   линий остаётся, сколько остаётся ЛОЖНЫХ, и сколько спектров теряет опору.

**Стадия B — положительный контроль САМОЙ мерки вершины.** В спектр на пустое
место подсаживается гауссиана ЗАВЕДОМО СУЩЕСТВУЮЩАЯ, с модельной шириной
группы; меряется, с какого z проверка вершины начинает её видеть. Без этой
стадии долю из пункта 1 читать нельзя: неизвестно, «вершины нет» — это линии
нет или мерка не видит.

Запуск:  python tools/CORPUS/scripts/gate_blind_check.py [--only=KEY,KEY]
                                                          [--csv=файл]
                                                          [--gate=prod|pass1]
                                                          [--no-inject]
"""
import os
import sys
import io
import csv
import argparse

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
SPECTRA = os.path.join(LAB, 'corpus', 'spectra')
PARTS = os.path.join(LAB, 'corpus', 'parts.csv')
DETECTORS = os.path.join(LAB, 'corpus', 'detectors.csv')
sys.path.insert(0, HERE)

# Мерка печатает ⛔ и ⚠; консоль сопровождающего бывает не в UTF-8.
try:
    sys.stdout.reconfigure(errors='backslashreplace')
except Exception:
    pass

import corpus_def                                       # noqa: E402
import corpus_calib                                     # noqa: E402
import calibrate                                        # noqa: E402
import build_corpus                                     # noqa: E402
import gaussfit                                         # noqa: E402
import check_corpus                                     # noqa: E402

calibrate.sample_lines = build_corpus.sample_lines

FS = gaussfit.FWHM_SIGMA
#: последний (штатный) проход `corpus_calib.calibrate`
PROD = dict(tol_fwhm=0.7, width_lo=0.5, width_hi=1.8)
#: первый проход того же конвейера — им меряет `gaussfit_check.py`
PASS1 = dict(tol_fwhm=2.5, width_lo=0.35, width_hi=2.6)
#: на сколько ПШПВ отодвигается линия в отрицательном контроле
FAKE_OFFSET = 5.0
#: ложная линия ДАЛЬШЕ этого от любой курированной считается «вдали»
FAR_FWHM = 2.0
PARTS_ORDER = ('known', 'unknown', 'excluded', '?')
#: корзины значимости, в которых сравниваются наблюдение и контроль
ZBINS = ((0.0, 10.0, u'z 5..10'), (10.0, 30.0, u'z 10..30'),
         (30.0, 100.0, u'z 30..100'), (100.0, 1e18, u'z >100'))
#: стадия B
INJ_POS = 6
INJ_Z = (5.0, 8.0, 12.0, 20.0, 35.0, 60.0, 120.0)
INJ_SEED = 20260827


def det_res_a():
    u"""a модели разрешения группы: FWHM(E) = a·sqrt(E)."""
    out = {}
    with io.open(DETECTORS, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            out[row['det']] = (float(row['fwhm_662_pct']) / 100.0 * 662.0
                               / np.sqrt(662.0))
    return out


def parts_of():
    out = {}
    with io.open(PARTS, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            out[row['spectrum']] = row['part']
    return out


# ---------------------------------------------------------------------------
# проверка вершины — БЕЗ гаусса
# ---------------------------------------------------------------------------
def vertex(counts, ch0, sigma0, window=2.2):
    u"""Есть ли в окне вокруг ch0 настоящая вершина. Фит не участвует.

    Возвращает dict или None (окно короче восьми каналов).
    """
    n = len(counts)
    half = max(4, int(round(window * sigma0)))
    lo = int(max(0, round(ch0 - half)))
    hi = int(min(n - 1, round(ch0 + half)))
    if hi - lo + 1 < 8:
        return None
    x = np.arange(lo, hi + 1, dtype=float)
    y = np.asarray(counts[lo:hi + 1], dtype=float)
    ys = np.convolve(y, np.ones(3) / 3.0, mode='same')
    ys[0], ys[-1] = y[0], y[-1]
    edge = max(2, x.size // 6)
    xe = np.concatenate([x[:edge], x[-edge:]])
    ye = np.concatenate([y[:edge], y[-edge:]])
    try:
        b1, b0 = np.polyfit(xe, ye, 1)
    except Exception:
        b1, b0 = 0.0, float(np.median(ye))
    k = int(np.argmax(ys))
    top = float(y[k] - (b0 + b1 * x[k]))
    need = 3.0 * np.sqrt(max(y[k], 1.0))
    inner = bool(1 <= k <= x.size - 2)
    rise = bool(top >= need)
    drop = (min(float(ys[k] - ys[:k].min()), float(ys[k] - ys[k + 1:].min()))
            if inner else 0.0)
    twoside = bool(drop >= need)
    d = np.diff(ys)
    return dict(ok=bool(inner and rise and twoside), inner=inner, rise=rise,
                twoside=twoside, monotone=bool(np.all(d >= 0) or np.all(d <= 0)),
                peak_ch=float(x[k]), top=top, need=need)


def fwhm_model_ch(cal, res_a, e_ref):
    u"""(канал, модельная ПШПВ в каналах) — как их считает `match_lines`."""
    ch0 = cal.channel(e_ref)
    dedch = abs(cal.dEdch(ch0))
    if dedch <= 0:
        return ch0, None
    return ch0, max(res_a * np.sqrt(max(e_ref, 5.0)) / dedch, 1.2)


def in_window(cal, n, e):
    u"""Дошла бы линия до фита вообще: те же границы, что в `match_lines`."""
    ch = cal.channel(e)
    return bool(4 <= ch <= n - 5 and abs(cal.dEdch(ch)) > 0)


def fake_lines(lines, res_a, sign):
    u"""Те же линии, отодвинутые на ±5 ПШПВ — отрицательный контроль."""
    out = []
    for e_ref, label, purity, e_table in lines:
        e2 = e_ref + sign * FAKE_OFFSET * res_a * np.sqrt(max(e_ref, 5.0))
        if e2 < 20.0:
            continue
        out.append((e2, label + u'/ЛОЖЬ', purity, e2))
    return out


def load_scene(entry, res_by_det):
    u"""(counts, cal, res_a, lines) либо (None, причина)."""
    key = entry['key']
    path = os.path.join(SPECTRA, key + '.xml')
    if not os.path.isfile(path):
        return None, u'нет файла в corpus/spectra'
    res_a = res_by_det.get(entry['det'])
    if res_a is None:
        return None, u'нет группы %s в detectors.csv' % entry['det']
    try:
        counts, ecal, fwhm_coef, rd = check_corpus.load(path)
    except Exception as ex:
        return None, u'ОШИБКА чтения: %s' % ex
    cal = corpus_calib.Ecal(ecal, len(counts))
    ent = dict(entry)
    ent['wanted'] = build_corpus.wanted_lines(entry)
    lines = calibrate.curate(
        ent, lambda en: res_a * np.sqrt(max(float(en), 5.0)), min_purity=0.45)
    if not lines:
        return None, u'нет курированных линий'
    return (counts, cal, res_a, lines), None


# ---------------------------------------------------------------------------
# стадия A
# ---------------------------------------------------------------------------
def stage_a(entries, res_by_det, part_of, gate):
    rows = []
    agg = {}
    skipped = []
    read = {}
    for e in entries:
        key = e['key']
        scene, why = load_scene(e, res_by_det)
        if scene is None:
            skipped.append((key, why))
            continue
        counts, cal, res_a, lines = scene
        n = len(counts)
        part = part_of.get(key, '?')
        read[key] = part
        a = agg.setdefault(part, dict(sp=0, off=0, off_f=0, off_far=0))
        a['sp'] += 1
        a['off'] += sum(1 for L in lines if in_window(cal, n, L[0]))

        def collect(found, kind, far=None):
            for r in found:
                ch0, fw = fwhm_model_ch(cal, res_a, r['e_ref'])
                if fw is None:
                    continue
                v = vertex(counts, ch0, fw / FS)
                if v is None:
                    continue
                rows.append(dict(
                    key=key, det=e['det'], part=part, kind=kind,
                    e_ref=float(r['e_ref']), label=r['label'], ch=float(r['ch']),
                    sig=float(r['sig']), sig_fit=float(r.get('sig_fit', 0.0)),
                    fwhm=float(r['fwhm']), fwhm_model=float(fw),
                    vertex=1 if v['ok'] else 0,
                    monotone=1 if v['monotone'] else 0,
                    inner=1 if v['inner'] else 0,
                    far=1 if (far is not None and round(r['e_ref'], 3) in far) else 0))

        collect(corpus_calib.match_lines(counts, cal, lines, res_a, **gate), u'штат')

        # ⛔ ЛОЖНЫЙ НАБОР ПРОХОДИТ ВОРОТА ОДНИМ ВЫЗОВОМ, КАК И НАСТОЯЩИЙ.
        # Прежде здесь стоял цикл `for sign in (+1.0, -1.0)` с ОТДЕЛЬНЫМ
        # `match_lines` на каждый знак. Дедуп у `match_lines` работает ВНУТРИ
        # вызова, поэтому ложная позиция, на которую садятся оба знака,
        # засчитывалась ДВАЖДЫ, а настоящий набор дедупился целиком — сравнение
        # выходило несимметричным и завышало ложь примерно на 15 %
        # (понятная 434 против 372, непонятная 270 против 234). Найдено
        # встречной проверкой 27.08.2026; германий не был затронут.
        fl = []
        for sign in (+1.0, -1.0):
            fl.extend(fake_lines(lines, res_a, sign))
        if fl:
            far = set()
            for L in fl:
                if not in_window(cal, n, L[0]):
                    continue
                a['off_f'] += 1
                d = min(abs(L[0] - t[0])
                        / max(res_a * np.sqrt(max(t[0], 5.0)), 1e-9) for t in lines)
                if d > FAR_FWHM:
                    a['off_far'] += 1
                    far.add(round(L[0], 3))
            collect(corpus_calib.match_lines(counts, cal, fl, res_a, **gate),
                    u'ложь', far=far)
    return rows, agg, skipped, read


# ---------------------------------------------------------------------------
# стадия B — положительный контроль мерки вершины
# ---------------------------------------------------------------------------
def stage_b(entries, res_by_det, part_of):
    rng = np.random.RandomState(INJ_SEED)
    out = []
    occupied = [0, 0]
    for e in entries:
        scene, why = load_scene(e, res_by_det)
        if scene is None:
            continue
        counts, cal, res_a, lines = scene
        n = len(counts)
        part = part_of.get(e['key'], '?')
        busy = [cal.channel(a[0]) for a in lines]
        picked = 0
        tries = 0
        while picked < INJ_POS and tries < 200:
            tries += 1
            ch0 = float(rng.randint(int(0.05 * n), int(0.90 * n)))
            ee = cal.energy(ch0)
            dedch = abs(cal.dEdch(ch0))
            if ee <= 5 or dedch <= 0:
                continue
            fw = max(res_a * np.sqrt(max(ee, 5.0)) / dedch, 1.2)
            if ch0 - 3 * fw < 4 or ch0 + 3 * fw > n - 5:
                continue
            if any(abs(ch0 - b) < 3 * fw for b in busy):
                continue
            v0 = vertex(counts, ch0, fw / FS)
            if v0 is None:
                continue
            occupied[1] += 1
            if v0['ok']:
                occupied[0] += 1
                continue                      # тут уже что-то есть — не место
            picked += 1
            sg = fw / FS
            lo = int(max(0, round(ch0 - 4 * sg)))
            hi = int(min(n - 1, round(ch0 + 4 * sg)))
            xx = np.arange(lo, hi + 1, dtype=float)
            prof = np.exp(-0.5 * ((xx - ch0) / sg) ** 2)
            prof = prof / prof.sum()
            nbg = max(float(np.median(counts[lo:hi + 1])), 0.0) * sg * np.sqrt(2.0 * np.pi)
            for z in INJ_Z:
                area = 0.5 * (z * z + np.sqrt(z ** 4 + 8.0 * z * z * nbg))
                y = counts.copy()
                y[lo:hi + 1] = y[lo:hi + 1] + rng.poisson(area * prof)
                r, status = gaussfit.fit_peak_ex(y, ch0, sg, window=2.2)
                v = vertex(y, ch0, sg)
                out.append(dict(part=part, fw=float(fw), z_want=z,
                                sig=(float(r['sig']) if r else 0.0),
                                vertex=1 if (v and v['ok']) else 0))
    return out, occupied


def eff_by_bin(inj, part):
    u"""Эффективность мерки вершины на ЗАВЕДОМО настоящих пиках, по корзинам z."""
    eff = []
    for lo, hi, nm in ZBINS:
        s = [r for r in inj if r['part'] == part and lo <= r['sig'] < hi]
        eff.append((sum(r['vertex'] for r in s) / float(len(s))) if s else None)
    return eff


# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', default=None)
    ap.add_argument('--csv', default=None)
    ap.add_argument('--gate', default='prod', choices=('prod', 'pass1'))
    ap.add_argument('--no-inject', action='store_true')
    args = ap.parse_args()
    only = set(args.only.split(',')) if args.only else None
    gate = PROD if args.gate == 'prod' else PASS1

    res_by_det = det_res_a()
    part_of = parts_of()
    entries = [e for e in corpus_def.ALL if not only or e['key'] in only]

    rows, agg, skipped, read = stage_a(entries, res_by_det, part_of, gate)

    # ---- охват: знаменатель из parts.csv, а не из прочитанного --------------
    dec = {}
    for k, p in part_of.items():
        if not only or k in only:
            dec[p] = dec.get(p, 0) + 1
    print(u'')
    print(u'=== ОХВАТ: прочитано / объявлено в corpus/parts.csv ===')
    if only:
        print(u'⚠ прогон по --only: знаменатель — запрошенные спектры')
    broken = 0
    for p in PARTS_ORDER:
        if p not in dec:
            continue
        g = sum(1 for k, q in read.items() if q == p)
        if g != dec[p]:
            broken += dec[p] - g
        print(u'  %-9s %d/%d%s' % (p, g, dec[p],
                                   u'' if g == dec[p] else u'   ⛔ НЕПОЛНО'))
    if skipped:
        print(u'  вне мерки (%d):' % len(skipped))
        for k, why in skipped:
            print(u'     %-26s %s' % (k, why))

    print(u'')
    print(u'гейт «%s»: tol=%.2f ПШПВ, ширина %.2f…%.2f, min_sig=5'
          % (args.gate, gate['tol_fwhm'], gate['width_lo'], gate['width_hi']))

    # ---- 1 + 2: приём настоящих и ложных, со знаменателями ------------------
    print(u'')
    print(u'=== 1+2. ПРИЁМ ГЕЙТА: настоящие линии против ЗАВЕДОМО ЛОЖНЫХ ===')
    print(u'%-9s %4s | %6s %6s %6s | %6s %6s %6s | %6s %6s %6s | %5s'
          % (u'часть', u'спк', u'предл', u'принят', u'доля',
             u'предл', u'принят', u'доля', u'предл', u'принят', u'доля', u'н/л'))
    print(u'%-9s %4s | %-20s | %-20s | %-20s |'
          % ('', '', u'      НАСТОЯЩИЕ', u'        ЛОЖНЫЕ', u'   ЛОЖНЫЕ ВДАЛИ'))
    for p in PARTS_ORDER:
        a = agg.get(p)
        if not a:
            continue
        st = [r for r in rows if r['kind'] == u'штат' and r['part'] == p]
        lz = [r for r in rows if r['kind'] == u'ложь' and r['part'] == p]
        fr = [r for r in lz if r['far']]
        ra = 100.0 * len(st) / max(a['off'], 1)
        rb = 100.0 * len(lz) / max(a['off_f'], 1)
        rc = 100.0 * len(fr) / max(a['off_far'], 1)
        print(u'%-9s %4d | %6d %6d %5.1f%% | %6d %6d %5.1f%% | %6d %6d %5.1f%% | %5.2f'
              % (p, a['sp'], a['off'], len(st), ra, a['off_f'], len(lz), rb,
                 a['off_far'], len(fr), rc, ra / max(rb, 1e-9)))
        print(u'%-9s %4s | %6s %6d %6s | %6s %6d %6s | %6s %6d %6s |'
              % ('', '', u'', sum(1 for r in st if not r['vertex']), u'',
                 u'', sum(1 for r in lz if not r['vertex']), u'',
                 u'', sum(1 for r in fr if not r['vertex']), u''))
        print(u'%-9s %4s |%s<- БЕЗ ВЕРШИНЫ' % ('', '', ' ' * 5))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ — числа разных моделей')

    # ---- стадия B и разбор доли вершин -------------------------------------
    inj = []
    occ = [0, 0]
    if not args.no_inject:
        inj, occ = stage_b(entries, res_by_det, part_of)
        print(u'')
        print(u'=== B. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ МЕРКИ ВЕРШИНЫ ===')
        print(u'подсажено заведомо настоящих пиков: %d в %d местах'
              % (len(inj), len(inj) // max(len(INJ_Z), 1)))
        print(u'мерка сказала «вершина есть» на ПУСТОМ месте до подсадки: '
              u'%d из %d = %.1f%%' % (occ[0], occ[1],
                                      100.0 * occ[0] / max(occ[1], 1)))

    print(u'')
    print(u'=== 1. ДОЛЯ С ВЕРШИНОЙ — и чего она стоит ===')
    print(u'%-9s %-9s %6s %6s %7s %9s %9s'
          % (u'часть', u'набор', u'линий', u'верш', u'доля',
             u'ОЖИД.если', u'ожид.доля'))
    print(u'%-9s %-9s %6s %6s %7s %9s'
          % ('', '', '', '', '', u'ВСЕ настоящие'))
    for p in PARTS_ORDER:
        st = [r for r in rows if r['kind'] == u'штат' and r['part'] == p]
        if not st:
            continue
        eff = eff_by_bin(inj, p) if inj else [None] * len(ZBINS)
        for kind, nm in ((u'штат', u'штат'), (u'ложь', u'ложь')):
            sub = [r for r in rows if r['kind'] == kind and r['part'] == p]
            if not sub:
                continue
            obs = sum(r['vertex'] for r in sub)
            exp = None
            if any(x is not None for x in eff):
                exp = 0.0
                for (lo, hi, _n), ee in zip(ZBINS, eff):
                    s = [r for r in sub if lo <= r['sig'] < hi]
                    if s and ee is not None:
                        exp += ee * len(s)
            print(u'%-9s %-9s %6d %6d %6.1f%% %9s %9s'
                  % (p, nm, len(sub), obs, 100.0 * obs / len(sub),
                     (u'%.1f' % exp) if exp is not None else u'—',
                     (u'%.1f%%' % (100.0 * exp / len(sub))) if exp is not None else u'—'))
        if any(x is not None for x in eff):
            print(u'   эффективность мерки на ПОДСАЖЕННЫХ: %s'
                  % u', '.join(u'%s %.0f%%' % (n, 100 * ee)
                               for (_l, _h, n), ee in zip(ZBINS, eff)
                               if ee is not None))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ')
    print(u'⚠ «ожидаемое» — сколько вершин мерка нашла бы, будь КАЖДАЯ линия')
    print(u'  набора настоящей. Наблюдение не ниже ожидаемого значит только то,')
    print(u'  что мерка НЕ РАЗЛИЧАЕТ эти наборы, а не что набор чист.')

    # ---- 3: цена порога -----------------------------------------------------
    print(u'')
    print(u'=== 3. ЦЕНА ПОРОГА ПО A/sigma(A) (`sig_fit`) ===')
    print(u'%-9s %5s | %6s %6s %6s %6s | %6s %6s | %5s | %4s %4s'
          % (u'часть', u'порог', u'линий', u'доля', u'верш', u'потер',
             u'ложных', u'доля', u'н/л', u'спк', u'<3'))
    for p in PARTS_ORDER:
        a = agg.get(p)
        st = [r for r in rows if r['kind'] == u'штат' and r['part'] == p]
        lz = [r for r in rows if r['kind'] == u'ложь' and r['part'] == p]
        if not st or not a:
            continue
        base_v = sum(r['vertex'] for r in st)
        keys = set(r['key'] for r in st)
        for thr in (0.0, 3.0, 8.0):
            k1 = [r for r in st if r['sig_fit'] >= thr]
            k2 = [r for r in lz if r['sig_fit'] >= thr]
            v = sum(r['vertex'] for r in k1)
            ra = 100.0 * len(k1) / max(a['off'], 1)
            rb = 100.0 * len(k2) / max(a['off_f'], 1)
            cnt = {}
            for r in k1:
                cnt[r['key']] = cnt.get(r['key'], 0) + 1
            print(u'%-9s %5.0f | %6d %5.1f%% %6d %6d | %6d %5.1f%% | %5.2f | %4d %4d'
                  % (p, thr, len(k1), ra, v, base_v - v, len(k2), rb,
                     ra / max(rb, 1e-9), len(cnt),
                     sum(1 for k in keys if cnt.get(k, 0) < 3)))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ')
    print(u'  «верш» — сколько из оставшихся имеют вершину; «потер» — сколько')
    print(u'  линий С ВЕРШИНОЙ порог выбросил; «спк» — спектров хоть с одной')
    print(u'  линией; «<3» — спектров, у которых линий меньше трёх (на трёх')
    print(u'  точках квадратичную энергокалибровку уже не построить).')

    if args.csv:
        with io.open(args.csv, 'w', encoding='utf-8', newline='') as f:
            w = csv.writer(f)
            cols = ['key', 'det', 'part', 'kind', 'e_ref', 'label', 'ch', 'sig',
                    'sig_fit', 'fwhm', 'fwhm_model', 'vertex', 'monotone',
                    'inner', 'far']
            w.writerow(cols)
            for r in rows:
                w.writerow([r[c] for c in cols])
        print(u'\nподробности: %s' % args.csv)

    # `T76`: у сторожа охвата есть ЧИТАТЕЛЬ — код возврата.
    if broken:
        print(u'')
        print(u'⛔ ОХВАТ СЛОМАН: %d спектров не посчитано. Код возврата 3.' % broken)
        return 3
    return 0


if __name__ == '__main__':
    sys.exit(main())
