# -*- coding: utf-8 -*-
u"""Проба устойчивости энергокалибровок корпуса к малому возмущению модели
разрешения группы (`S209`, П205 01.10.2026).

⛔ Зачем. П202 (01.10.2026) пересобрал корпус на базе с добором слабых гамм:
у K-40, Zn-65 и Cs-137 новых линий нет вовсе, а набор опор калибровки у них
переключился — `RC103_K40` невязка опор 8.1 → 47.3 кэВ, `G1S24_Zn65_P5`
0.7 → 13.8 кэВ. Сдвинула их модель разрешения ГРУППЫ (подсказка стадии 2а),
сменившаяся на сотые доли процента от чужих спектров группы. Генератор,
у которого набор опор зависит от третьего знака подсказки, меряет не спектр.

Как меряет. Стадия 1 (калибровка по собственному разрешению спектра) от модели
группы не зависит и считается ОДИН раз; стадии 2а и 2б — тот же код, что у
пересборки (`build_corpus.calibrate_stage2`), — повторяются с подсказкой
группы, умноженной на `--scales` (умолчание ±0.05 %). Подсказка у каждого
спектра — своей группы, поэтому общий множитель равносилен независимому
возмущению каждой группы. Вход — собственные копии стадии 1 из `_corpus_raw`
(их пишет `extract` при пересборке; библиотеку проба не читает).

Что сравнивается у каждого спектра (передний план и свой фон):
  * НАБОР ОПОР — пары (метка, энергия опоры с точностью 0.01 кэВ);
  * режим калибровки (`mode`);
  * сдвиг шкалы max|ΔE| по каналам, кэВ.

Подсказка группы приводится к записанной (`data/res_hint.csv`) с гистерезисом
`build_corpus.RES_HINT_HYSTERESIS` — ровно как у пересборки; `--no-hysteresis`
выключает его (поведение до 01.10.2026: при ±0.05 % сменилось 21 из 129).
Множитель за полосой (`--scales=0.97,1.03`) — положительный контроль самой
пробы: подсказка обязана обновиться, и проба обязана это увидеть.

⚖ Положительный контроль — `--control`: множитель 1.0 обязан повторить
`corpus_state.json` пересборки (коэффициенты шкалы до 1e-9 относительно и
режимы) — иначе проба меряет не тот код или не тот вход.

    python tools/CORPUS/scripts/stability_probe.py [--scales=0.9995,1.0005]
           [--control=<corpus_state.json>] [--only=KEY,KEY] [--csv=<файл>]

Код возврата: 0 — набор опор не сменился ни у одного спектра ни при одном
множителе (и контроль, если задан, сошёлся); 1 — сменился; 2 — контроль
не сошёлся; 3 — охват неполон (спектр потерян на чтении `_corpus_raw`).
"""
import copy
import io
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

import build_corpus as bc                            # noqa: E402
import corpus_def                                    # noqa: E402
from gaussfit_check import Coverage, frozen_keys     # noqa: E402  охват — ОДИН на мерки (T76/V15)


def raw_extractor(entry):
    u"""Копии стадии 1 в `_corpus_raw` — то, что `extract` записал при пересборке."""
    raw = os.path.join(bc.RAW, entry['key'] + '.xml')
    if not os.path.isfile(raw):
        raise IOError('нет копии стадии 1: %s (сначала пересборка)' % raw)
    bg = os.path.join(bc.RAW, entry['key'] + '_bg.xml')
    return raw, (bg if os.path.isfile(bg) else None), None, None


def anchors(acc):
    return tuple(sorted((str(a.get('label', '')), round(float(a['e_ref']), 2)) for a in (acc or [])))


def signature(state):
    out = {}
    for key, st in state.items():
        n = st['sp'].n
        grid = np.arange(n, dtype=float)
        out[key] = dict(
            fg=anchors(st['accepted']), mode=st['mode'],
            e=st['ecal'].energy(grid),
            bg=anchors(st.get('bg_accepted')), bg_mode=st.get('bg_mode', '-'),
            coef=[float(c) for c in st['ecal'].coef])
    return out


def quiet(*_a, **_k):
    pass


def report_against(base, state, path):
    u"""Сводка «чем калибровки при множителе 1.0 отличаются от `corpus_state.json`
    другой пересборки» — для таблицы до/после; на приговор не влияет."""
    with io.open(path, encoding='utf-8') as fh:
        ref = json.load(fh)
    rows = []
    for key in sorted(base):
        r = ref.get(key)
        if r is None:
            rows.append((float('inf'), key, u'нет в сравнении', '', 0.0))
            continue
        st = state[key]
        n = st['sp'].n
        grid = np.arange(n, dtype=float)
        e_ref = sum(c * grid ** i for i, c in enumerate(r['ecal']))
        de = np.abs(base[key]['e'] - e_ref)
        # в долях ПШПВ группы на каждом канале (модель — из сравнения, корнем)
        fw = 0.0
        if r.get('res_kev'):
            rk = np.array(r['res_kev'], dtype=float)
            ee = np.maximum(e_ref, 15.0)
            f = np.sqrt(np.maximum(rk[0] + rk[1] * ee + (rk[2] if len(rk) > 2 else 0.0) * ee * ee, 1e-9))
            lo = int(np.searchsorted(e_ref, 15.0))
            fw = float(np.max(de[lo:] / f[lo:])) if lo < n else 0.0
        rows.append((float(np.max(de)), key, r['mode'], base[key]['mode'], fw))
    moved = [x for x in rows if x[0] > 1e-6]
    print(u'\nпротив %s: шкала сдвинулась у %d из %d (max|ΔE| > 1e-6 кэВ), из них > 0.1 ПШПВ у %d'
          % (os.path.basename(path), len(moved), len(rows), sum(1 for x in rows if x[4] > 0.1)))
    for de, key, m0, m1, fw in sorted(rows, reverse=True):
        if de > 1e-6:
            print(u'   %-26s %-24s -> %-24s max|ΔE| %8.3f кэВ = %6.3f ПШПВ' % (key, m0, m1, de, fw))


def main():
    scales = [0.9995, 1.0005]
    control = None
    against = None
    only = None
    csv_path = None
    hyst = True
    for a in sys.argv[1:]:
        if a.startswith('--scales='):
            scales = [float(x) for x in a.split('=', 1)[1].split(',') if x]
        elif a.startswith('--control='):
            control = a.split('=', 1)[1]
        elif a.startswith('--against='):
            against = a.split('=', 1)[1]
        elif a.startswith('--only='):
            only = set(a.split('=', 1)[1].split(','))
        elif a.startswith('--csv='):
            csv_path = a.split('=', 1)[1]
        elif a == '--no-hysteresis':
            hyst = False
    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if only is None or e['key'] in only]
    print(u'проба устойчивости опор (S209): спектров %d, множители подсказки группы: %s'
          % (len(entries), ', '.join('%.4f' % s for s in scales)))
    state1, _ = bc.calibrate_stage1(entries, log=quiet, extractor=raw_extractor)
    print(u'стадия 1: откалибровано %d' % len(state1))
    # Охват (`T76`/`V15`): мерка на `_corpus_raw` обязана сказать, что посчитала.
    # Знаменатель — как у `ecal_extrapolation.coverage_for`: весь объявленный корпус
    # (семёрка LEGACY заморожена), а при `--only` — сам запрос.
    full = set(e['key'] for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON)
    keys = [e['key'] for e in entries]
    cov = Coverage(requested=None if set(keys) >= full else set(keys), frozen=frozen_keys())
    cov.add(u'вход пробы (NEW+VIBE+ETALON)', keys, hard=True)
    cov.add(u'стадия 1 прочитана из _corpus_raw', list(state1), hard=True)
    if cov.report(u'ОХВАТ stability_probe (стадия 1 на _corpus_raw)'):
        print(u'приговор: ОХВАТ НЕПОЛОН — спектры потеряны, проба корпус не описывает')
        return 3

    stored = bc.load_res_hints() if hyst else None
    print(u'гистерезис подсказки группы: %s' % (
        u'ВЫКЛЮЧЕН (--no-hysteresis), как до 01.10.2026' if stored is None else
        u'полоса ±%.1f %%, записанных групп %d (%s)' % (100 * bc.RES_HINT_HYSTERESIS, len(stored),
                                                     os.path.relpath(bc.RES_HINT_FILE, HERE))))
    base_state = copy.deepcopy(state1)
    bc.calibrate_stage2(base_state, log=quiet, res_scale=1.0, stored_hints=stored)
    base = signature(base_state)

    code = 0
    if control:
        with io.open(control, encoding='utf-8') as fh:
            ref = json.load(fh)
        bad = []
        for key, sig in base.items():
            r = ref.get(key)
            if r is None:
                bad.append('%s: нет в контроле' % key)
                continue
            c0 = np.array(r['ecal'], dtype=float)
            c1 = np.array(sig['coef'], dtype=float)
            if (len(c0) != len(c1) or r['mode'] != sig['mode'] or r['bg_mode'] != sig['bg_mode']
                    or not np.allclose(c0, c1, rtol=1e-9, atol=1e-12)):
                bad.append('%s: режим %s / %s, фон %s / %s' % (key, sig['mode'], r['mode'],
                                                                sig['bg_mode'], r['bg_mode']))
        print(u'контроль (множитель 1.0 против %s): %s'
              % (os.path.basename(control), u'СОШЛОСЬ, %d спектров' % len(base) if not bad
                 else u'НЕ СОШЛОСЬ у %d' % len(bad)))
        for line in bad[:20]:
            print(u'   ' + line)
        if bad:
            code = 2

    if against:
        report_against(base, base_state, against)

    rows = []
    changed_any = set()
    for s in scales:
        st = copy.deepcopy(state1)
        said = []
        bc.calibrate_stage2(st, log=lambda m: said.append(m) if u'обновлена' in m else None,
                            res_scale=s, stored_hints=stored)
        sig = signature(st)
        changed = []
        for key in sorted(base):
            b, t = base[key], sig.get(key)
            if t is None:
                continue
            shift = float(np.max(np.abs(t['e'] - b['e']))) if len(t['e']) == len(b['e']) else float('inf')
            fg_diff = t['fg'] != b['fg']
            bg_diff = t['bg'] != b['bg']
            mode_diff = t['mode'] != b['mode'] or t['bg_mode'] != b['bg_mode']
            rows.append((s, key, int(fg_diff), int(bg_diff), int(mode_diff), shift,
                         len(b['fg']), len(t['fg'])))
            if fg_diff or bg_diff or mode_diff:
                changed.append((key, b, t, shift, fg_diff, bg_diff))
        print(u'\nмножитель %.4f: набор опор или режим сменился у %d из %d'
              % (s, len(changed), len(base)))
        for m in sorted(set(said)):
            print(u' ' + m)
        for key, b, t, shift, fg_diff, bg_diff in changed:
            changed_any.add(key)
            gone = sorted(set(b['fg']) - set(t['fg']))
            new = sorted(set(t['fg']) - set(b['fg']))
            print(u'   %-26s %-22s -> %-22s max|ΔE| %8.3f кэВ%s%s%s'
                  % (key, b['mode'], t['mode'], shift,
                     (u'  ушли: ' + ', '.join('%s@%.2f' % x for x in gone)) if gone else '',
                     (u'  пришли: ' + ', '.join('%s@%.2f' % x for x in new)) if new else '',
                     u'  [фон]' if bg_diff else ''))
        top = sorted(((r[5], r[1]) for r in rows if r[0] == s), reverse=True)[:5]
        print(u'   наибольший сдвиг шкалы: ' + ', '.join('%s %.3f кэВ' % (k, v) for v, k in top))

    if csv_path:
        with io.open(csv_path, 'w', encoding='utf-8', newline='') as fh:
            fh.write(u'scale,spectrum,fg_changed,bg_changed,mode_changed,max_shift_kev,n_base,n_new\n')
            for r in rows:
                fh.write(u'%.6f,%s,%d,%d,%d,%.6f,%d,%d\n' % r)
    print(u'\nИТОГ: набор опор сменился у %d спектров: %s'
          % (len(changed_any), ', '.join(sorted(changed_any)) or u'—'))
    if code == 0 and changed_any:
        code = 1
    print(u'приговор: %s' % {0: u'УСТОЙЧИВО', 1: u'НЕУСТОЙЧИВО', 2: u'КОНТРОЛЬ НЕ СОШЁЛСЯ'}[code])
    return code


if __name__ == '__main__':
    sys.exit(main())
