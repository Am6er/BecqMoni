#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сравнение двух прогонов pie по корпусу: χ²/ndf, время, состав, recall.

    python tools/pie/compare.py out_base out_p1_shape [--mode spline]
    python tools/pie/compare.py out_base out_p1_shape --groups ASN16,AS80x80
    python tools/pie/compare.py out_rev13_mini out_new --mode spline \\
           --part known --members --only tools/CORPUS/corpus/mini.csv

Печатает по группам сумму и медиану χ²/ndf, время, и — отдельно — спектры,
у которых состав «пирога» разошёлся сильнее порога.

⛔ RECALL И ФАНТОМЫ БЕРУТСЯ ТЕМИ ЖЕ ПРАВИЛАМИ, ЧТО У `score.py`, И ПРАВИЛА
БЕРУТСЯ У НЕГО ЖЕ, А НЕ ПЕРЕПИСЫВАЮТСЯ ЗДЕСЬ (`A282`). Этот файл — второй
читатель одного прогона, и три года он тихо расходился с первым:

  * порог доли стоял 3 %, тогда как развёртка `S90` (23.08.2026) сменила меру
    доли и увела умолчание на **0.30 %** — теперь оба конца берут `score.S_THR`;
  * «комнатные» отсеивались условием `not t['has_bg'] and c in score.ROOM`, то
    есть НЕ ОТСЕИВАЛИСЬ НИКОГДА: спектров без фона в корпусе-129 ноль, а под
    `--members` компоненты зовутся дочерними (`Ac-228`), и ни один в `ROOM` не
    входит. Сравнивать надо СЕМЕЙСТВО — `score.ROOM_FAMILIES`, как в `score.py`;
  * часть корпуса не спрашивалась вовсе: германий (`excluded`) шёл в счёт, а
    понятная и непонятная складывались в одно число — две разные модели под
    одной цифрой (⛔ правило `CLAUDE.md`: всякое корпусное число называет часть);
  * подмножества не было (`S136`): знаменатель брался из манифеста целиком, и
    прогон малой базы печатал recall 40 % там, где он 100 %, — не найдено, а
    НЕ ПРОГНАНО;
  * `--members` не было, и состав, названный дочерними, не засчитывался ни
    разу — recall выходил примерно вдвое хуже правды, молча.

⚠ И до 10.09.2026 раздел recall не печатался вовсе: `score.load_results` с
15.08.2026 отдаёт шесть значений, а распаковка ждала три — `ValueError` падал
УЖЕ ПОСЛЕ таблицы χ², то есть отказ выглядел как «сравнение сделано».
"""
import argparse
import csv
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import score  # noqa: E402

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass


def load_runs(d, mode):
    out = {}
    for f in glob.glob(os.path.join(d, '*_%s_runs.csv' % mode)):
        group = os.path.basename(f)[:-len('_%s_runs.csv' % mode)]
        for r in csv.DictReader(open(f, encoding='utf-8-sig')):
            out[r['spectrum']] = (group, r)
    return out


def load_comps(d, mode):
    out = {}
    for f in glob.glob(os.path.join(d, '*_%s_components.csv' % mode)):
        for r in csv.DictReader(open(f, encoding='utf-8-sig')):
            if r['kind'] == 'nuisance':
                continue
            out.setdefault(r['spectrum'], {})[r['component']] = float(r['share_pct'])
    return out


def select_truth(part, declared):
    """Истина корпуса в тех же границах, в каких её берёт `score.py`.

    ⛔ Границы — ЧАСТЬ корпуса и ОБЪЯВЛЕННЫЙ СПИСОК, и обе обязательны. Германий
    (`excluded`) не считается никогда (приказ Amber 08.08.2026); понятная и
    непонятная части — две разные модели, и складывать их числа нельзя. Список
    (`--only`, `S136`) переносит знаменатель с манифеста на то, что реально
    гоняли: без него спектр, которого в прогоне НЕ БЫЛО, считается промахом
    наравне с упавшим.
    """
    truth = score.load_truth()
    in_manifest = set(truth)
    parts = score.load_parts()
    if parts:
        truth = {k: t for k, t in truth.items()
                 if parts.get(k, 'unknown') != 'excluded'
                 and (part == 'all' or parts.get(k, 'unknown') == part)}
    elif part != 'all':
        sys.exit('нет parts.csv — часть корпуса выбрать нечем')

    if declared:
        unknown_names = sorted(declared - in_manifest)
        if unknown_names:
            print('ВНИМАНИЕ: в списке --only есть имена, которых нет в манифесте '
                  '(%d): %s' % (len(unknown_names), ', '.join(unknown_names[:5])),
                  file=sys.stderr)
        truth = {k: t for k, t in truth.items() if k in declared}
        if not truth:
            sys.exit('--only: в части «%s» не осталось ни одного спектра списка' % part)
    return truth


def recall(d, mode, truth, sthr, zthr, groups_only=None, members=False):
    """Попадания, знаменатель, ЖЁСТКИЕ фантомы, комнатные и разбивка по группам.

    ⚠ Берём поля связки ПОЗИЦИОННО, а не распаковкой целиком: `load_results`
    отдавала три значения, потом стала отдавать шесть, и сравнение падало
    `ValueError` уже ПОСЛЕ печати таблицы χ² — отказ выглядел как «сравнение
    сделано». Позиционное чтение переживает следующий рост связки молча и верно.
    """
    loaded = score.load_results(mode, d)
    results, groups = loaded[0], loaded[1]
    if groups_only:
        groups &= set(groups_only)
    # Тот же сторож, что у `score.py`: состав, названный ДОЧЕРНИМИ, без ключа
    # не засчитывается ни разу, и recall выходит вдвое хуже правды — молча.
    score.warn_members(results, members)
    hits = tot = phantom = soft_total = 0
    per = {}
    for spectrum, t in truth.items():
        if t['det'] not in groups:
            continue
        fams = {score.family(c) for c in t['components']}
        detected = set()
        for row in results.get(spectrum, []):
            if row['kind'] == 'nuisance':
                continue
            if float(row['share_pct']) >= sthr and float(row['z']) >= zthr:
                detected.add(row['component'])
        detfams = {score.family(c) for c in detected}
        # ⛔ Вездесущие ряды разбираются ПО СЕМЕЙСТВУ (`score.ROOM_FAMILIES`), а
        # не по имени компонента, и БЕЗ условия «у спектра нет фона»: прежнее
        # правило не срабатывало ни разу (спектров без фона в корпусе ноль, а
        # дочерние ряда в `ROOM` не входят), и все комнатные шли жёсткими
        # фантомами. Считаются отдельной колонкой, а не прощаются молча.
        hard, soft = [], []
        for c in detected:
            if score.family(c) in fams:
                continue
            (soft if score.family(c) in score.ROOM_FAMILIES else hard).append(c)
        acc = per.setdefault(t['det'], [0, 0, 0, 0])
        acc[0] += len(fams & detfams)
        acc[1] += len(fams)
        acc[2] += len(hard)
        acc[3] += len(soft)
        hits += len(fams & detfams)
        tot += len(fams)
        phantom += len(hard)
        soft_total += len(soft)
    return hits, tot, phantom, soft_total, per


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('a')
    ap.add_argument('b')
    ap.add_argument('--mode', default='spline', choices=['snip', 'spline'])
    ap.add_argument('--groups')
    ap.add_argument('--share-eps', type=float, default=5.0,
                    help='печатать спектры, где доля компонента сдвинулась больше, %%')
    # ⛔ Умолчания порогов НЕ ПОВТОРЯЮТСЯ ЗДЕСЬ ЧИСЛАМИ (`A282`): своя копия «3 %»
    # пережила развёртку `S90` на полгода, и два читателя одного прогона считали
    # recall по разным критериям.
    ap.add_argument('--sthr', type=float, default=score.S_THR, help='порог доли, %%')
    ap.add_argument('--zthr', type=float, default=score.Z_THR, help='порог z')
    ap.add_argument('--part', default='all', choices=['all', 'known', 'unknown'],
                    help='часть корпуса по corpus/parts.csv; германий (excluded) '
                         'не считается никогда')
    ap.add_argument('--members', action='store_true',
                    help='состав назван ДОЧЕРНИМИ нуклидами (разбор приложения), '
                         'а не цепочками: развернуть цепочки манифеста в их членов')
    ap.add_argument('--only', default=None,
                    help='ограничить объявленным списком: имена через запятую '
                         'либо путь к csv/txt, где ключ — первый столбец (S136)')
    args = ap.parse_args()

    if args.members:
        score.enable_members()
    declared = score.read_only(args.only) if args.only else None
    if args.only and not declared:
        sys.exit('--only=%s: не нашлось ни одного имени' % args.only)
    truth = select_truth(args.part, declared)

    only = set(args.groups.split(',')) if args.groups else None
    ra, rb = load_runs(args.a, args.mode), load_runs(args.b, args.mode)
    ca, cb = load_comps(args.a, args.mode), load_comps(args.b, args.mode)

    keys = sorted(set(ra) & set(rb))
    if only:
        keys = [k for k in keys if ra[k][0] in only]
    # ⛔ Таблица χ² живёт в ТЕХ ЖЕ границах, что и recall: иначе «ИТОГО» складывает
    # понятную часть с непонятной (две разные модели) и с германием, который вне
    # предмета, — а часть при этом нигде не названа.
    keys = [k for k in keys if k in truth]
    if not keys:
        sys.exit('в части «%s» не осталось ни одного общего спектра' % args.part)

    per = {}
    for k in keys:
        g = ra[k][0]
        va, vb = ra[k][1], rb[k][1]
        if va['chi2ndf'] in ('', 'ERROR') or vb['chi2ndf'] in ('', 'ERROR'):
            continue
        per.setdefault(g, []).append((float(va['chi2ndf']), float(vb['chi2ndf']),
                                      float(va['ms']), float(vb['ms'])))

    # ⛔ Часть корпуса называется В ШАПКЕ И В ИТОГЕ, как у `score.py`: числа
    # понятной и непонятной частей относятся к разным моделям, и строка без
    # имени части читается как корпусная.
    print('режим %s, часть корпуса=%s%s%s: %s  ->  %s'
          % (args.mode, args.part,
             ', состав по дочерним' if args.members else '',
             ', подмножество %d' % len(declared) if declared else '',
             os.path.basename(args.a), os.path.basename(args.b)))
    print('критерий «найден»: доля >= %.2f %%, z >= %.1f (взято у score.py)'
          % (args.sthr, args.zthr))
    print()
    print('%-11s %4s %10s %10s %8s %9s %9s' %
          ('группа', 'n', 'chi2 A', 'chi2 B', 'дельта%', 'мс A', 'мс B'))
    ta = tb = tma = tmb = 0.0
    n = 0
    for g in sorted(per):
        v = per[g]
        sa, sb = sum(x[0] for x in v), sum(x[1] for x in v)
        ma, mb = sum(x[2] for x in v), sum(x[3] for x in v)
        ta += sa; tb += sb; tma += ma; tmb += mb; n += len(v)
        print('%-11s %4d %10.2f %10.2f %+7.1f%% %9.0f %9.0f' %
              (g, len(v), sa, sb, 100 * (sb - sa) / sa if sa else 0, ma, mb))
    print('%-11s %4d %10.2f %10.2f %+7.1f%% %9.0f %9.0f' %
          ('ИТОГО', n, ta, tb, 100 * (tb - ta) / ta if ta else 0, tma, tmb))

    # recall считается по одному и тому же множеству групп: в каталоге ветки
    # может лежать только часть корпуса, и сравнивать её с полным нельзя.
    common = set(per) if not only else (set(per) & only)
    ha, tot_a, pa, sa_soft, _ = recall(args.a, args.mode, truth, args.sthr,
                                       args.zthr, common, args.members)
    hb, tot_b, pb, sb_soft, _ = recall(args.b, args.mode, truth, args.sthr,
                                       args.zthr, common, args.members)
    print()
    # ⛔ Знаменатель ОБЯЗАН быть виден: recall «40 %» малой базы означал не
    # «не найдено», а «не прогнано» — 121 спектр манифеста против 59 в каталоге
    # (`S136`). Разошедшиеся знаменатели двух плеч — отказ, а не сноска.
    if tot_a != tot_b:
        print('⛔ ЗНАМЕНАТЕЛИ ПЛЕЧ РАЗОШЛИСЬ: A %d, B %d — прогоны накрывают разные'
              ' множества спектров, сравнивать recall нельзя' % (tot_a, tot_b),
              file=sys.stderr)
    print('recall  A %3.0f%% (%d/%d), фантомов %d, комнатных %d  часть: %s'
          % (100.0 * ha / tot_a if tot_a else 0, ha, tot_a, pa, sa_soft, args.part))
    print('recall  B %3.0f%% (%d/%d), фантомов %d, комнатных %d  часть: %s'
          % (100.0 * hb / tot_b if tot_b else 0, hb, tot_b, pb, sb_soft, args.part))

    moved = []
    for k in keys:
        a, b = ca.get(k, {}), cb.get(k, {})
        for comp in set(a) | set(b):
            d = b.get(comp, 0.0) - a.get(comp, 0.0)
            if abs(d) >= args.share_eps:
                moved.append((abs(d), k, comp, a.get(comp, 0.0), b.get(comp, 0.0)))
    if moved:
        print()
        print('сдвиг доли >= %.0f %%:' % args.share_eps)
        for _, k, comp, x, y in sorted(moved, reverse=True)[:40]:
            print('  %-24s %-10s %5.1f%% -> %5.1f%%' % (k, comp, x, y))
        print('  всего таких пар: %d' % len(moved))


if __name__ == '__main__':
    main()
