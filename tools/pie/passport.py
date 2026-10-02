#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Метрика ПАСПОРТА: активность разбора против паспорта источника (`AMBER153` (а)).

ЗАЧЕМ. Мерка корпуса — χ²/ndf, recall, фантомы, подавленные — спрашивает «назван ли
нуклид и как легла модель», но не «СКОЛЬКО его». А активность у паспортных спектров
систематически ниже паспорта (П191, 01.10.2026: на 5 см −4…−7 % у всех нуклидов,
Cd-109 0.53…0.80), и ни одно заглавное число этого не видело: паспорта лежали в
`manifest.why` (32+ спектра), а читателя у них не было.

ЧТО СЧИТАЕТ. По каждому спектру, у которого в `manifest.why` есть опорная
активность, и по каждому её нуклиду — отношение «разбор / паспорт»:

* разбор — `decay_s` строки компонента в `*_components.csv` прогона (у ряда — строка
  его головы; у членов ряда `decay_s` общий, и при отсутствии головы берётся он);
* паспорт — приведённый РАСПАДОМ на момент начала набора (`StartTime` файла спектра)
  по периоду из `nucdb.nuclides.half_life_sec` (база открывается только на чтение);
* объёмные эталоны (паспорт в Бк/кг) — × масса пробы = плотность × объём сосуда
  ПО ГЕОМЕТРИИ ФАЙЛА СПЕКТРА (маринелли — кольцо минус колодец, цилиндр — внутренний
  диаметр × высота пробы). ⚠ Масса — сценная, не взвешенная: ошибка плотности сцены
  входит в отношение один к одному (`AMBER159`);
* порог: |разбор/паспорт − 1| ≤ dA + 3 % (dA — из паспорта; у опорных активностей
  без dA — 0, порог ±3 %).

Три вида опоры распознаются в `why` (иных нет — новая запись чужого вида молча не
выпадет: строка `why`, где есть слово «паспорт» и цифра «Бк», но ни один шаблон не
подошёл, печатается отдельно как «не разобрана»):

  1. «паспорт: Cs-137 A=94200 Бк dA=2% 01-10-2008»       — точечный ЛСРМ;
  2. «паспорт: Cs-137 A=1760 Бк/кг dA=5% 24-05-2002»      — объёмный эталон ЛСРМ;
  3. «на дату съёмки 03.12.2022 — 5712 Бк» / «на 24.11.2022 — 5715.5 Бк»
                                                          — чек-источник Amber
     (якорь 9.25 кБк на 02.01.2002, `tools/CORPUS/README.md` §1.4); нуклид — из
     колонки `nuclides` манифеста (обязан быть ровно один).

⛔ Метрика — МЕТКА, а не мерка базы: recall, фантомы, χ² от неё не меняются ни на
знак. Печатается ВСЕГДА, нулём в том числе (разряд, видимый только когда сработал,
неотличим от незаведённого — `T101`).

Запуск отдельно:
    python tools/pie/passport.py --out-dir=tools/pie/out_rev39_full [--only=…] [--part=known]
и в конце `score.py` — тем же списком спектров, что судится там.
"""
import argparse
import csv
import datetime
import io
import math
import os
import re
import sqlite3
import statistics
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, '..', 'CORPUS', 'corpus')
MANIFEST = os.path.join(CORPUS, 'manifest.csv')
SPECTRA = os.path.join(CORPUS, 'spectra')
PARTS = os.path.join(CORPUS, 'parts.csv')
NUCDB = os.path.join(HERE, '..', '..', 'BecquerelMonitor', 'nucdb.sqlite')

#: Добавка к паспортной dA в пороге, доля (`AMBER153` (а): «порог ±(dA + 3 %)»).
EXTRA_TOL = 0.03

# 1 и 2: «паспорт: Xx-NNN A=<число> Бк[/кг] dA=<число>% ДД-ММ-ГГГГ». Число — и в
# E-записи («A=1.033E6»): прежняя проба П191 (`passport_ratio.py`) брала `[\d.]+` и
# молча теряла оба паспорта Cd-109 G1S16 — ровно те, где расхождение самое большое.
RE_LSRM = re.compile(
    r'паспорт:\s*([A-Z][a-z]?-\d+m?)\s*A=([0-9.]+(?:[Ee][+-]?\d+)?)\s*Бк(/кг)?\s*'
    r'dA=([0-9.]+)%\s*(\d{2})-(\d{2})-(\d{4})')
# 3: активность чек-источника на названную дату.
RE_DATED = re.compile(
    r'[Нн]а (?:дату съёмки )?(\d{2})\.(\d{2})\.(\d{4})\s*—\s*([0-9]+(?:\.[0-9]+)?)\s*Бк\b')
# «паспорт» и «Бк» в одной записи без шаблона — сигнал, что формат разошёлся.
RE_HINT = re.compile(r'паспорт[^;]*?\d\s*(?:к?Бк)')


def _nucid(name):
    u"""Cs-137 -> 137CS (ключ `nucdb.nuclides`)."""
    m = re.match(r'^([A-Z][a-z]?)-(\d+)(m?)$', name)
    if not m:
        return None
    return m.group(2) + m.group(1).upper() + ('M' if m.group(3) else '')


class HalfLives(object):
    u"""Периоды из `nucdb` — только чтение; нет периода — отказ словами."""

    def __init__(self, path=NUCDB):
        self.path = os.path.abspath(path)
        self._c = None
        self._cache = {}

    def days(self, name):
        if name in self._cache:
            return self._cache[name]
        if self._c is None:
            uri = 'file:%s?mode=ro' % self.path.replace('\\', '/')
            self._c = sqlite3.connect(uri, uri=True)
        nid = _nucid(name)
        row = self._c.execute(
            'select half_life_sec from nuclides where nucid=? and half_life_sec is not null',
            (nid,)).fetchone() if nid else None
        val = float(row[0]) / 86400.0 if row and row[0] else None
        self._cache[name] = val
        return val


def _xml_head(key):
    path = os.path.join(SPECTRA, key + '.xml')
    if not os.path.isfile(path):
        return None
    return io.open(path, encoding='utf-8', errors='replace').read()


def start_of(text):
    u"""Начало набора — `StartTime` (первое вхождение — сама проба, не фон)."""
    m = re.search(r'<StartTime>(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})', text)
    if not m:
        return None
    return datetime.datetime(*[int(x) for x in m.groups()])


def geometry_of(text):
    u"""(вид, расстояние точки мм, масса пробы г или None) — из `<Geometry>` спектра."""
    g = re.search(r'<Geometry>.*?</Geometry>', text, re.S)
    if not g:
        return '', 0.0, None
    g = g.group(0)

    def v(tag):
        m = re.search(r'<%s>([^<]*)</%s>' % (tag, tag), g)
        try:
            return float(m.group(1)) if m else 0.0
        except ValueError:
            return 0.0

    kind = (re.search(r'<SourceType>([^<]*)</SourceType>', g) or [None, ''])[1]
    rho_m = re.search(r'<Source>.*?<Density>([^<]*)</Density>', g, re.S)
    rho = float(rho_m.group(1)) if rho_m else 0.0
    mass = None
    if kind == 'Marinelli':
        r_in = (v('MarinelliBeakerDiameter') / 2 - v('MarinelliSideThickness')) / 10.0
        r_hole = (v('MarinelliHoleDiameter') / 2 + v('MarinelliHoleSideThickness')) / 10.0
        vol = math.pi * (r_in ** 2 * v('MarinelliSourceHeight') / 10.0
                         - r_hole ** 2 * v('MarinelliHoleHeight') / 10.0)
        mass = rho * vol
    elif kind == 'Cylinder':
        r_in = (v('BeakerDiameter') / 2 - v('BeakerSideWallThickness')) / 10.0
        mass = rho * math.pi * r_in ** 2 * v('SourceHeight') / 10.0
    return kind, v('PointDistance'), mass


def references(manifest=MANIFEST):
    u"""Опоры из `why`: [(спектр, нуклид, A, ед., dA, дата, вид)], и неразобранные."""
    refs, unparsed = [], []
    with io.open(manifest, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            why = row.get('why') or ''
            found = False
            for m in RE_LSRM.finditer(why):
                nuc, a, perkg, da, dd, mm, yy = m.groups()
                refs.append((row['key'], nuc, float(a), 'Бк/кг' if perkg else 'Бк',
                             float(da) / 100.0, datetime.datetime(int(yy), int(mm), int(dd)),
                             'паспорт ЛСРМ'))
                found = True
            if not found:
                dated = RE_DATED.findall(why)
                nucs = [n for n in (row.get('nuclides') or '').split(';') if n.strip()]
                if dated and len(nucs) == 1:
                    if HERE not in sys.path:
                        sys.path.insert(0, HERE)
                    import score                                   # noqa: E402
                    nuc = score.NUCLIDE_MAP.get(nucs[0].strip())
                    dd, mm, yy, a = dated[0]
                    if nuc:
                        refs.append((row['key'], nuc, float(a), 'Бк', None,
                                     datetime.datetime(int(yy), int(mm), int(dd)),
                                     'чек-источник'))
                        found = True
            if not found and RE_HINT.search(why) and 'паспорта нет' not in why \
                    and 'паспорт не нужен' not in why and 'паспортов нет' not in why:
                unparsed.append(row['key'])
    return refs, unparsed


def ruled_in(out_dir):
    u"""Спектр -> пол фита по правилу корпуса (`xray_rule.csv` в каталоге прогона, `AMBER153` (б))."""
    path = os.path.join(out_dir, 'xray_rule.csv')
    if not os.path.isfile(path):
        return {}
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return {r['spectrum']: float(r['floor_kev']) for r in csv.DictReader(fh)}


def collect(results, keys, manifest=MANIFEST, half=None, ruled=None):
    u"""Строки метрики по спектрам из `keys`, у которых есть результат и опора.

    `results` — спектр -> строки `components.csv` (как у `score.load_results`).
    Возвращает (строки, отказы): отказ — опора есть, а отношения нет (нет
    компонента, нет периода, нет файла), словами; молча не выпадает ничего.
    """
    half = half or HalfLives()
    ruled = ruled or {}
    refs, unparsed = references(manifest)
    rows, refused = [], []
    for key, nuc, a0, unit, da, d0, how in refs:
        if key not in keys:
            continue
        comp = results.get(key)
        if not comp:
            refused.append((key, nuc, 'нет результата разбора'))
            continue
        text = _xml_head(key)
        if text is None:
            refused.append((key, nuc, 'нет файла спектра'))
            continue
        d1 = start_of(text)
        if d1 is None:
            refused.append((key, nuc, 'нет StartTime'))
            continue
        hl = half.days(nuc)
        if not hl:
            refused.append((key, nuc, 'нет периода в nucdb'))
            continue
        kind, dist, mass = geometry_of(text)
        a_ref = a0 * 0.5 ** ((d1 - d0).total_seconds() / 86400.0 / hl)
        if unit == 'Бк/кг':
            if not mass:
                refused.append((key, nuc, 'паспорт в Бк/кг, а массу по сосуду «%s» не взять' % kind))
                continue
            a_ref *= mass / 1000.0
        hit = [c for c in comp if c.get('component') == nuc]
        if not hit:
            refused.append((key, nuc, 'компонента %s в разборе нет' % nuc))
            continue
        try:
            a_fsa = float(hit[0]['decay_s'])
        except (KeyError, TypeError, ValueError):
            refused.append((key, nuc, 'нет decay_s'))
            continue
        try:
            sig = float(hit[0].get('dt_decay_s') or 'nan')
        except ValueError:
            sig = float('nan')
        ratio = a_fsa / a_ref
        tol = (da or 0.0) + EXTRA_TOL
        if kind == 'Point':
            group = 'точка %.0f мм' % dist
        elif kind == 'Marinelli':
            group = 'маринелли'
        elif kind == 'Cylinder':
            group = 'цилиндр'
        else:
            group = kind or '?'
        det = comp[0].get('det', '')
        rows.append(dict(key=key, det=det, group=group, nuc=nuc, a_fsa=a_fsa,
                         a_ref=a_ref, ratio=ratio, sig_rel=sig / a_fsa if a_fsa else float('nan'),
                         da=da, tol=tol, out=abs(ratio - 1.0) > tol, how=how, mass=mass,
                         d0=d0, d1=d1, floor=ruled.get(key)))
    return rows, refused, [k for k in unparsed if k in keys]


#: Верх шкалы (`AMBER153` ➕ 01.10.2026): пара линий ОДНОГО компонента — верхняя
#: (E ≥ HI_MIN_KEV) и опорная (LO_RANGE_KEV), обе с выходом ≥ PAIR_MIN_YIELD_PCT и
#: чистотой ≥ PAIR_MIN_PURITY; из кандидатов — с наибольшей ожидаемой площадью.
#: На корпусе 02.10.2026 это 2614/239 у ряда тория, 1836/898 у Y-88, 1770/570 у Bi-207.
HI_MIN_KEV = 1500.0
LO_RANGE_KEV = (200.0, 1000.0)
PAIR_MIN_YIELD_PCT = 5.0
PAIR_MIN_PURITY = 0.9


def upper_scale(out_dir, keys, mode='spline'):
    u"""Отношение «изм./ожид.» верхней линии к опорной по сверке линий прогона.

    Источник — `lines_<режим>.csv`, который пишет `CorpusFsaProbe --audit`
    (`run_appwd.ps1` ставит ключ всегда с П214): `ratio` = измерено/ожидание
    модели по линии, то есть суммирование, матрица и кривая уже в ожидании, и
    двойное отношение не зависит ни от активности, ни от паспорта — это мерка
    ПРИБОРА/СЦЕНЫ по верху шкалы (у G1S24 дефицит 2614 и 1836, П191).
    Возвращает (строки, есть ли файл).
    """
    path = os.path.join(out_dir, 'lines_%s.csv' % mode)
    if not os.path.isfile(path):
        return [], False
    by = defaultdict(list)
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            if r['spectrum'] in keys:
                by[(r['spectrum'], r['component'])].append(r)
    out = []
    for (key, comp), rs in sorted(by.items()):
        def good(r):
            try:
                return (float(r['purity']) >= PAIR_MIN_PURITY
                        and float(r['intensity_pct']) >= PAIR_MIN_YIELD_PCT
                        and float(r['measured']) > 0 and float(r['expected']) > 0)
            except (KeyError, ValueError):
                return False
        hi = [r for r in rs if good(r) and float(r['energy_kev']) >= HI_MIN_KEV]
        lo = [r for r in rs if good(r) and LO_RANGE_KEV[0] <= float(r['energy_kev']) < LO_RANGE_KEV[1]]
        if not hi or not lo:
            continue
        h = max(hi, key=lambda r: float(r['expected']))
        l = max(lo, key=lambda r: float(r['expected']))
        rh, rl = float(h['ratio']), float(l['ratio'])
        sh = float(h['sigma']) / float(h['measured'])
        sl = float(l['sigma']) / float(l['measured'])
        out.append(dict(key=key, det=rs[0].get('det', ''), comp=comp, e_hi=float(h['energy_kev']),
                        e_lo=float(l['energy_kev']), ratio=rh / rl,
                        sig=(rh / rl) * math.sqrt(sh * sh + sl * sl)))
    return out, True


def report_upper(rows, have_file, out=None):
    out = out or sys.stdout
    p = lambda s='': out.write(s + '\n')                      # noqa: E731
    if not have_file:
        p('  верх шкалы (AMBER153 ➕): нет lines_*.csv — прогон снят без --audit, пар не считать')
        return
    p('  верх шкалы (AMBER153 ➕): (изм./ожид.) верхней линии к опорной, %d пар' % len(rows))
    for r in sorted(rows, key=lambda r: (r['det'], r['key'])):
        p('    %-24s %-8s %6.1f/%-6.1f %.3f ± %.3f' % (r['key'], r['comp'], r['e_hi'], r['e_lo'],
                                                      r['ratio'], r['sig']))


def report(rows, refused=(), unparsed=(), out=None, table=True):
    u"""Таблица по спектрам и итог: вне порога, медиана по группам «прибор · сосуд»."""
    out = out or sys.stdout
    p = lambda s='': out.write(s + '\n')                       # noqa: E731
    n_out = sum(1 for r in rows if r['out'])
    p()
    p('ПАСПОРТ (AMBER153 (а)): разбор/паспорт у %d нуклидо-спектров, вне порога '
      '±(dA + %.0f %%) — %d' % (len(rows), EXTRA_TOL * 100, n_out))
    if table and rows:
        p('  %-26s %-8s %-14s %11s %11s %7s %6s %6s  %s' % (
            'спектр', 'нуклид', 'группа', 'разбор, Бк', 'паспорт, Бк', 'отн.', 'σ,%', 'порог',
            'опора'))
        for r in sorted(rows, key=lambda r: (r['det'], r['group'], r['nuc'], r['key'])):
            p('  %-26s %-8s %-14s %11.4g %11.4g %7.3f %6.2f %5.0f%%  %s%s%s%s' % (
                r['key'], r['nuc'], r['group'], r['a_fsa'], r['a_ref'], r['ratio'],
                100.0 * r['sig_rel'], 100.0 * r['tol'], r['how'],
                (', масса %.0f г' % r['mass']) if r['mass'] else '',
                (', фит от %.0f кэВ (правило корпуса)' % r['floor']) if r.get('floor') else '',
                '  ⛔ ВНЕ ПОРОГА' if r['out'] else ''))
    groups = defaultdict(list)
    for r in rows:
        groups[(r['det'], r['group'])].append(r)
    if groups:
        p('  по группам «прибор · сосуд»: медиана отношения [мин…макс], вне порога')
        for (det, grp), rs in sorted(groups.items()):
            vals = [r['ratio'] for r in rs]
            p('    %-8s %-14s n=%2d  медиана %.3f  [%.3f…%.3f]  вне порога %d' % (
                det, grp, len(rs), statistics.median(vals), min(vals), max(vals),
                sum(1 for r in rs if r['out'])))
    if rows:
        vals = [r['ratio'] for r in rows]
        p('  всего: медиана %.3f, вне порога %d из %d' % (statistics.median(vals), n_out, len(rows)))
    for key, nuc, why in refused:
        p('  ⚠ опора есть, отношения нет: %-24s %-8s %s' % (key, nuc, why))
    for key in unparsed:
        p('  ⚠ в why есть «паспорт … Бк», но запись не разобрана: %s' % key)
    return n_out


def main():
    sys.path.insert(0, HERE)
    import score                                               # noqa: E402
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--out-dir', required=True)
    ap.add_argument('--mode', default='spline', choices=['snip', 'spline'])
    ap.add_argument('--only', default=None)
    ap.add_argument('--part', default='all', choices=['all', 'known', 'unknown'])
    ap.add_argument('--csv', default=None, help='записать строки метрики в csv')
    args = ap.parse_args()
    results = score.load_results(args.mode, args.out_dir)[0]
    keys = set(results)
    parts = score.load_parts()
    if args.part != 'all':
        keys = {k for k in keys if parts.get(k, 'unknown') == args.part}
    keys = {k for k in keys if parts.get(k, 'unknown') != 'excluded'}
    if args.only:
        keys &= score.read_only(args.only)
    rows, refused, unparsed = collect(results, keys, ruled=ruled_in(args.out_dir))
    report(rows, refused, unparsed)
    report_upper(*upper_scale(args.out_dir, {r['key'] for r in rows}, args.mode))
    if args.csv:
        with io.open(args.csv, 'w', encoding='utf-8-sig', newline='') as fh:
            w = csv.writer(fh)
            w.writerow(['spectrum', 'det', 'group', 'nuclide', 'fsa_bq', 'ref_bq', 'ratio',
                        'sigma_rel', 'tol', 'out', 'reference'])
            for r in rows:
                w.writerow([r['key'], r['det'], r['group'], r['nuc'], '%.6g' % r['a_fsa'],
                            '%.6g' % r['a_ref'], '%.5f' % r['ratio'], '%.5f' % r['sig_rel'],
                            '%.3f' % r['tol'], int(r['out']), r['how']])


if __name__ == '__main__':
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    main()
