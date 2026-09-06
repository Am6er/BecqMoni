# -*- coding: utf-8 -*-
u"""`V18` (полоса F56, 06.09.2026): развёртка платы за степень свободы.

Решение Amber 06.09.2026: «Дефект: кандидат платит за степень свободы». Здесь
эта плата ВЫВОДИТСЯ РАЗВЁРТКОЙ по коэффициенту, а не назначается: для каждого
значения коэффициента меряются три вещи на ОДНОМ И ТОМ ЖЕ наборе опор —

  * **нулевое плечо** (опора ровно туда, куда её кладёт нынешний ответ): после
    правки оно обязано перестать двигать калибровку;
  * **положительный контроль** (та же опора, сдвинутая на +2 ПШПВ): обязан
    двигать, иначе плата просто задушила отбор целиком;
  * **что стало с настоящими опорами**: сколько спектров сменили ответ `choose`
    против прежнего правила и в какую сторону.

Опоры берутся ОДИН РАЗ конвейером стадии 1 при ПРЕЖНЕМ правиле и кладутся в
кэш (`--cache=`): развёртка обязана менять ровно одну вещь — правило отбора, а
не ещё и набор найденных линий. Цена полного прогона конвейера — 122 спектра
по ~5 с; развёртка по кэшу — секунды на значение.

Плечи ставятся кодом мерки `calib_null_check.py` (её функции импортируются, не
копируются): одно правило нулевого плеча на двоих.

    python calib_sweep_f56.py --out=<каталог> [--cache=<файл.json>]
        [--rules=margin:0,df:0.5,df:1,df:2,aic:2,ftest:0.05]
        [--only=k1,k2] [--scope=all|poly] [--order=2]

Артефакты: `sweep_f56.csv` (по правилу — сводка), `sweep_arms_f56.csv` (каждый
опыт), `sweep_moved_f56.csv` (спектры, у которых ответ `choose` сменился),
`log_sweep_f56.txt`.
"""
import csv
import io
import json
import os
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

import corpus_calib as cc                              # noqa: E402
import calib_null_check as nc                          # noqa: E402


def build_cache(path, raw, only=None, log=print):
    u"""Стадия 1 конвейера при ПРЕЖНЕМ правиле -> опоры каждого спектра."""
    import build_corpus as bc
    import corpus_def
    from spectrum import Spectrum
    keep = (cc.MODEL_TEST, cc.MODEL_TEST_SCOPE, cc.MAX_ORDER)
    cc.MODEL_TEST, cc.MODEL_TEST_SCOPE, cc.MAX_ORDER = 'margin', 'all', 2
    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if only is None or e['key'] in only]
    out = {}
    t0 = time.time()
    for e in entries:
        key = e['key']
        rawp = os.path.join(raw, key + '.xml')
        if not os.path.isfile(rawp):
            log(u'%-24s НЕТ КОПИИ' % key)
            continue
        sp = Spectrum(rawp)
        cal0, pairs0, r662, tag0 = bc.calibrate_one(sp, e)
        out[key] = dict(det=e['det'], nmax=int(sp.n), tag_pipeline=tag0,
                        force=bool(e.get('recal')),
                        res_a=float(r662) * float(np.sqrt(662.0)),
                        stored=[float(c) for c in sp.ecal],
                        coef=[float(c) for c in cal0.coef],
                        pairs=[{k: (float(v) if isinstance(v, (int, float, np.floating))
                                    else v) for k, v in a.items()} for a in pairs0])
        log(u'%-24s n=%2d %s' % (key, len(pairs0), tag0))
    cc.MODEL_TEST, cc.MODEL_TEST_SCOPE, cc.MAX_ORDER = keep
    with io.open(path, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    log(u'кэш опор: %s (%d спектров, %.0f с)' % (path, len(out), time.time() - t0))
    return out


_ORIG_PAID = cc.freedom_paid


def _aic_paid(k):
    u"""Вторая форма платы, которую надо было измерить, а не выбрать по вкусу:
    AIC в его обычном виде для гауссовой невязки с неизвестной дисперсией —
    n·ln(SS/n) + k·p, то есть кандидат проходит при
    n·ln(SS_best/SS_cand) > k·Δp. Живёт ЗДЕСЬ, в мерке, а не в конвейере:
    развёрткой она проиграла (журнал F56), и держать её в `corpus_calib`
    незачем."""
    def paid(best_tag, best_ss, tag, ss, n):
        dp = cc.npar(tag) - cc.npar(best_tag)
        if dp <= 0:
            return True
        if cc.MODEL_TEST_SCOPE == 'poly' and not tag.startswith('poly'):
            return True
        if tag == 'gain' and n == 1:
            return True
        nu = n - cc.npar(tag)
        if nu < 1:
            return False
        if ss <= 0.0 or not np.isfinite(best_ss):
            return True
        return n * np.log(best_ss / ss) > k * dp
    return paid


def _flat_paid(k):
    u"""Третья форма: плата за КАЖДЫЙ лишний параметр, не зависящая ни от n,
    ни от ν — кандидат обязан уронить SS в e^k раз за параметр:
        ln(SS_best/SS_cand) > k·Δp.
    Единственная из трёх, которая НЕЙТРАЛЬНА к избыточной опоре по построению
    в самом сравнении: лишняя точка с нулевой невязкой не трогает SS
    победителя, кандидату SS только поднимает, а порог не двигает вовсе.
    У 'df' и 'aic' порог от числа опор ЗАВИСИТ и с лишней опорой ДЕШЕВЕЕТ —
    отсюда у них механизм `rank`, которого у 'flat' быть не может."""
    def paid(best_tag, best_ss, tag, ss, n):
        dp = cc.npar(tag) - cc.npar(best_tag)
        if dp <= 0:
            return True
        if cc.MODEL_TEST_SCOPE == 'poly' and not tag.startswith('poly'):
            return True
        if tag == 'gain' and n == 1:
            return True
        nu = n - cc.npar(tag)
        if nu < 1:
            return False
        if ss <= 0.0 or not np.isfinite(best_ss):
            return True
        return np.log(best_ss / ss) > k * dp
    return paid


def set_rule(rule, coef, scope, order, min_free_df=1):
    cc.MODEL_TEST_SCOPE = scope
    cc.MAX_ORDER = order
    cc.MIN_FREE_DF = int(min_free_df)
    cc.freedom_paid = _ORIG_PAID
    if rule in ('aic', 'flat'):
        cc.MODEL_TEST = 'df'                 # не 'margin' — ветка платы нужна
        cc.freedom_paid = (_aic_paid if rule == 'aic' else _flat_paid)(coef)
        return
    cc.MODEL_TEST = rule
    if rule == 'ftest':
        cc.MODEL_ALPHA = coef
    elif rule == 'df':
        cc.MODEL_FREEDOM_K = coef


def run_rule(cache, rule, coef, scope, order, log, min_free_df=1):
    u"""Плечи и ответ `choose` по всем спектрам кэша при одном правиле."""
    set_rule(rule, coef, scope, order, min_free_df)
    arms, verdict = [], {}
    for key in sorted(cache):
        st = cache[key]
        pairs = st['pairs']
        if not pairs:
            continue
        stored = cc.Ecal(st['stored'], st['nmax'])
        res_a = st['res_a']
        tag_b, cal_b, rows = nc.choose_arms(cc, stored, pairs, res_a, st['nmax'],
                                            st['force'], 'choose')
        verdict[key] = dict(tag=tag_b, coef=[float(c) for c in cal_b.coef],
                            n=len(pairs), det=st['det'], nmax=st['nmax'], res_a=res_a)
        for r in rows:
            r.update(spectrum=key, det=st['det'], n=len(pairs),
                     rule='%s:%g/nu%d' % (rule, coef, min_free_df))
        arms.extend(rows)
    return arms, verdict


def summarize(rule_id, arms, verdict, base_verdict, log):
    nulls = [r for r in arms if r['arm'] == 'null']
    shifts = [r for r in arms if r['arm'] == 'shift']
    per_spec = {}
    for r in nulls:
        w = per_spec.get(r['spectrum'])
        if w is None or r['d_max_fwhm'] > w['d_max_fwhm']:
            per_spec[r['spectrum']] = r
    bad = [r for r in per_spec.values() if r['cause'] != 'neutral']
    frozen = set()
    reacted = set()
    for r in shifts:
        (reacted if r['cause'] == 'reacted' else frozen).add(r['spectrum'])
    frozen -= reacted
    moved = []
    if base_verdict:
        for key, v in verdict.items():
            b = base_verdict.get(key)
            if b is None:
                continue
            d = max_delta_coef(b['coef'], v['coef'], b['nmax'], b['res_a'])
            if b['tag'] != v['tag'] or d[0] > 1e-6:
                moved.append(dict(rule=rule_id, spectrum=key, det=v['det'], n=v['n'],
                                  tag_base=b['tag'], tag_rule=v['tag'],
                                  d_max_kev=round(d[0], 4), d_max_fwhm=round(d[1], 4)))
    worst = max(per_spec.values(), key=lambda r: r['d_max_kev'], default=None)
    row = dict(rule=rule_id, spectra=len(per_spec),
               neutral=len(per_spec) - len(bad), moved_by_null=len(bad),
               null_arms_bad=sum(1 for r in nulls if r['cause'] != 'neutral'),
               worst_null_kev=round(worst['d_max_kev'], 4) if worst else 0.0,
               worst_null_fwhm=round(max((r['d_max_fwhm'] for r in per_spec.values()),
                                         default=0.0), 4),
               worst_null_at=worst['spectrum'] if worst else '',
               causes=u'; '.join(u'%s=%d' % kv for kv in sorted(
                   _count(r['cause'] for r in per_spec.values()).items())),
               control_reacted=len(reacted), control_frozen=len(frozen),
               changed_vs_base=len([m for m in moved if m['tag_base'] != m['tag_rule']]),
               moved_vs_base=len(moved))
    log(u'%-14s нулевое плечо: нейтральных %3d из %3d, худший %8.3f кэВ (%s); '
        u'контроль: отреагировали %3d, заморожены %2d; против прежнего: сменили режим %2d, '
        u'сдвинулись %2d'
        % (rule_id, row['neutral'], row['spectra'], row['worst_null_kev'],
           row['worst_null_at'], row['control_reacted'], row['control_frozen'],
           row['changed_vs_base'], row['moved_vs_base']))
    log(u'                причины сдвига: %s' % (row['causes'] or u'нет'))
    return row, moved


def _count(it):
    d = {}
    for x in it:
        d[x] = d.get(x, 0) + 1
    return d


def max_delta_coef(coef_a, coef_b, nmax, res_a):
    grid = np.arange(0, nmax, dtype=float)
    ea = sum(c * grid ** i for i, c in enumerate(coef_a))
    eb = sum(c * grid ** i for i, c in enumerate(coef_b))
    d = np.abs(ea - eb)
    fw = d / (res_a * np.sqrt(np.maximum(np.abs(ea), 5.0)))
    return float(d.max()), float(fw.max())


def main(argv):
    a = dict(out=None, cache=None, raw=os.path.join(HERE, '_corpus_raw'),
             rules='margin:0', only=None, scope='all', order=2)
    for x in argv:
        if x.startswith('--'):
            k, _, v = x[2:].partition('=')
            if k == 'only':
                a[k] = set(v.split(','))
            elif k == 'order':
                a[k] = int(v)
            else:
                a[k] = v
    if not a['out']:
        raise SystemExit(u'нужен --out=<каталог>')
    os.makedirs(a['out'], exist_ok=True)
    log_f = io.open(os.path.join(a['out'], 'log_sweep_f56.txt'), 'w', encoding='utf-8')

    def log(s):
        print(s)
        log_f.write(s + u'\n')
        log_f.flush()

    cache_path = a['cache'] or os.path.join(a['out'], 'anchors_f56.json')
    if os.path.isfile(cache_path):
        cache = json.load(io.open(cache_path, encoding='utf-8'))
        log(u'кэш опор прочитан: %s (%d спектров)' % (cache_path, len(cache)))
    else:
        cache = build_cache(cache_path, a['raw'], a['only'], log)
    if a['only']:
        cache = {k: v for k, v in cache.items() if k in a['only']}
    log(u'corpus_calib sha %s; спектров с опорами %d из %d'
        % (nc._sha(cc.__file__), sum(1 for v in cache.values() if v['pairs']), len(cache)))

    rules = []
    for spec in a['rules'].split(','):
        part = spec.split(':')
        name = part[0]
        val = float(part[1]) if len(part) > 1 and part[1] else 0.0
        nu = int(part[2]) if len(part) > 2 and part[2] else 1
        rules.append((name, val, nu))

    all_arms, rows, all_moved = [], [], []
    base_verdict = None
    for name, coef, nu in rules:
        rule_id = '%s:%g/nu%d' % (name, coef, nu)
        arms, verdict = run_rule(cache, name, coef, a['scope'], a['order'], log, nu)
        if base_verdict is None:
            base_verdict = verdict
        row, moved = summarize(rule_id, arms, verdict, base_verdict, log)
        rows.append(row)
        all_arms.extend(arms)
        all_moved.extend(moved)

    with io.open(os.path.join(a['out'], 'sweep_f56.csv'), 'w', encoding='utf-8',
                 newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    with io.open(os.path.join(a['out'], 'sweep_arms_f56.csv'), 'w', encoding='utf-8',
                 newline='') as f:
        cols = ['rule', 'spectrum', 'det', 'n', 'level', 'arm', 'place', 'e_star',
                'ch_star', 'tag_before', 'tag_after', 'd_max_kev', 'd_max_fwhm',
                'ch_max', 'd_at_star_kev', 'cause']
        w = csv.DictWriter(f, fieldnames=cols, extrasaction='ignore')
        w.writeheader()
        w.writerows(all_arms)
    if all_moved:
        with io.open(os.path.join(a['out'], 'sweep_moved_f56.csv'), 'w', encoding='utf-8',
                     newline='') as f:
            w = csv.DictWriter(f, fieldnames=list(all_moved[0].keys()))
            w.writeheader()
            w.writerows(all_moved)
    log(u'готово: %s' % a['out'])
    log_f.close()


if __name__ == '__main__':
    main(sys.argv[1:])
