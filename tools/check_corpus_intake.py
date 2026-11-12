# -*- coding: utf-8 -*-
u"""⛔ Сторож ПРИЁМА спектра в корпус (`AMBER314`).

Правило Amber 06.11.2026, дословно: «Принимай новые правила. Если спектр подходит
под все критерии алгоритма FSA, в нем известна геометрия и нет проблем с всякими
статистическими критериями, то этот спектр принимается в корпус. Только
дополнительно нужно сделать перекалибровку своими силами по энергии и по ПШПВ.»
Уточнения вопросником того же дня, дословно: критерии — «Сторож по окну FSA
(Рекомендую)»; паспорт вне допуска — «Не принимать вне допуска»; связь с правилом
02.11 — «Дополняет (Рекомендую)»; срок — «После этой волны (Рекомендую)».

ЧТО СУДИТ. Спектр, уже ПРОШЕДШИЙ корпусный конвейер: копия в `corpus/spectra`
(перекалибровка по энергии и ПШПВ — `build_corpus.py`, своими силами), строки в
`manifest.csv` и `parts.csv`, матрица его сцены в складе, и разбор FSA ТЕМ ЖЕ путём,
что у корпуса — `run_appwd.ps1` / `run_mini.ps1 -List` в свой `-Out`. Своего разбора
сторож не делает: он читает каталог прогона `CorpusFsaProbe` и файлы корпуса.

Критерии (каждый — вердикт с числом и причиной; пороги — константы ниже, таблица
порогов на нынешнем корпусе — журнал `handover/handover-2026-11-11-a314.md`):

  К1 геометрия — часть `known` (`parts.csv` называет сцену) и матрица НАЙДЕНА и
     ПРИМЕНЕНА (`matrix_found`, `matrix_applied` в `*_runs.csv`);
  К2 разбор   — строка прогона есть, без отказа (`error`, `chi2ndf` = ERROR), фон
     взят (`bg_rejected` пуст), шкала не на краю сетки (`*_edge`), библиотека
     `sample`;
  К3 опоры    — не меньше `MIN_ECAL_LINES` линий в перекалибровке конвейером
     (`manifest.ecal_lines`); опоры привязки FSA (`anchors_used`) печатаются рядом;
  К4 фон      — нет строки «фон больше спектра» (`neg_flag` в `*_bandwarn.csv` —
     то же правило, что красная строка окна FSA, `FsaAnalyzer.JudgeBandWarnings`);
  К5 переполнение — доля отсчётов в канале переполнения не больше
     `MAX_OVERFLOW_SHARE`; канал опознаётся ПРАВИЛОМ ПРИЛОЖЕНИЯ
     `OverflowChannel` (`A203`), константы читаются из `DoseRateManager.cs`;
  К6 главная линия — главный объявленный компонент разбора (наибольшая доля среди
     семейств истины манифеста) значим: z ≥ `MIN_MAIN_Z` (порог «найден» `score.Z_THR`,
     одно объявление на проект, `A282`);
  К7 паспорт  — если в `manifest.why` есть опорная активность, «разбор/паспорт» в
     допуске ±(dA + 3 %) (`tools/pie/passport.py`, мерка §3.11 README корпуса); нет
     паспорта — не судится;
  К8 калибровка — независимая приёмка записанной копии (`check_corpus.check`: пики
     линий садятся на табличные энергии): медиана промаха ≤ `MAX_CAL_MED_FWHM` ПШПВ,
     медиана ширины пика к модели в [`MIN_WIDTH_RATIO`, `MAX_WIDTH_RATIO`]. ⚠ Ловит сдвиг
     шкалы копии только в окне ~×1.04…×1.08 (критик `crit314`): при большем сдвиге пик
     садится на чужую линию списка, и промах выходит малым — большой сдвиг ловит К9;
  К9 статистика разбора (решение Amber 12.11.2026 «Да, добавить») — у разбора есть хоть одна
     опора привязки шкалы FSA (`anchors_used` ≥ `MIN_FSA_ANCHORS`), и избыток χ²/ndf на
     средний отсчёт в канале q = (χ²/ndf − 1)/(N/каналов) не больше `MAX_CHI2_EXCESS`, если
     χ²/ndf выше `CHI2_FLOOR` (ниже — шум статистики, не судится). Порог общий на все группы:
     таблица — журнал §2а.

Справочно (не вердикт): «образы выше спектра», пики вне состава, мёртвое время.

Запуск:

    python tools/check_corpus_intake.py --only=<ключи|файл> --run=<каталог прогона>
            [--corpus=<каталог corpus>]       # вердикт по кандидатам; код 1 — отказ
    python tools/check_corpus_intake.py --table [--part=known] [--csv=<файл>]
                                              # таблица порогов по объявленной базе
    python tools/check_corpus_intake.py       # = --selftest (в check_all сторожа нет —
                                              #   журнал §6: идёт 30…50 с)

⚠ Самопроверка (`--selftest`, она же запуск без ключей) — положительный контроль на
копиях спектра корпуса во временном каталоге: годный проходит все девять, а каждая
порча (нет геометрии, переполнение, подменённая калибровка, паспорт вне допуска, «фон
больше спектра», слабая главная линия, одна опора, отказ разбора, ноль опор привязки FSA,
избыток χ²) обязана отказать РОВНО по своему критерию. Данные — объявленная полная база (`ДЕЙСТВУЮЩАЯ БАЗА`
README корпуса) и копии корпуса; ни корпус, ни база не пишутся.

Коды возврата: 0 — все судимые прошли (или самопроверка сошлась); 1 — отказ (каждый
назван) или самопроверка не сошлась; 2 — нет данных (каталог прогона, манифест,
спектр, объявление базы).
"""

import argparse
import copy
import csv
import glob
import io
import math
import os
import re
import shutil
import sys
import tempfile
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')
README = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
PIE = os.path.join(ROOT, 'tools', 'pie')
DOSE_CS = os.path.join(ROOT, 'BecquerelMonitor', 'DoseRateManager.cs')
MODE = 'spline'

# ---------------------------------------------------------------------------
# ПОРОГИ. Выбраны по нынешнему корпусу (часть known, объявленная база) с запасом;
# таблица и разбор каждого — журнал `handover-2026-11-11-a314.md` §2 и §2а.
# ---------------------------------------------------------------------------
#: К3. Линий перекалибровки конвейером — не меньше. Число из постановки («не меньше
#: 2 опор»); опоры — линии конвейера (`ecal_lines`), а не привязки FSA (решение Amber
#: 12.11.2026); у корпуса known не проходит 1 из 92, по опорам FSA было бы 29.
MIN_ECAL_LINES = 2
#: К5. Доля канала переполнения, наибольшая. Корпус known: наибольшая 0.34 %
#: (`ASN16_Cs137_10cm`), весь корпус — 1.99 % (`ASN16_BrazilNuts`, непонятная).
MAX_OVERFLOW_SHARE = 0.01
#: К6. z главного объявленного компонента — тот же порог, что «найден» у `score.py`.
MIN_MAIN_Z = None            # берётся из score.Z_THR при загрузке
#: К8. Медиана промаха линий в долях ПШПВ: корпус known 0.076 медианой, наибольшая
#: 0.600 (`AS80_Am241`); `check_corpus.TOL_WARN` 0.40 корпус не держит (9 из 92).
MAX_CAL_MED_FWHM = 0.65
#: К8. Ширина пика к модели ПШПВ файла, медиана по линиям: корпус known 0.880…1.678.
MIN_WIDTH_RATIO = 0.80
MAX_WIDTH_RATIO = 1.80
#: К9. Опор привязки шкалы FSA — не меньше. Ноль — привязка не нашла ни одной линии
#: (шкала ×1.08…×1.20 у критика); на known ноль у 5 из 92.
MIN_FSA_ANCHORS = 1
#: К9. Избыток χ²/ndf на средний отсчёт в канале. На known без трёх смесей G1S16 —
#: не выше 0.0051 (`G1S16_Ce139_P5`), группы 0.0032…0.0051; смеси 0.015…0.029;
#: ПШПВ ×0.5 — 0.0122, шкала ×1.08…×1.20 — 0.058…0.110. Порог — вдвое выше 0.0051.
MAX_CHI2_EXCESS = 0.010
#: К9. χ²/ndf не выше этого не судится: у бедного спектра (N/каналов ~ 10) шум χ²/ndf
#: сам даёт q ~ 0.02.
CHI2_FLOOR = 2.0

CRITERIA = (
    ('К1', u'геометрия и матрица'),
    ('К2', u'разбор без отказа'),
    ('К3', u'опоры перекалибровки'),
    ('К4', u'нет «фон больше спектра»'),
    ('К5', u'нет переполнения'),
    ('К6', u'главная линия значима'),
    ('К7', u'паспорт в допуске'),
    ('К8', u'калибровка энергии и ПШПВ'),
    ('К9', u'статистика разбора'),
)

PASS, FAIL, NA = 'да', 'НЕТ', '—'


def _console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def _read_csv(path):
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def _f(text, default=float('nan')):
    try:
        return float(text)
    except (TypeError, ValueError):
        return default


# ---------------------------------------------------------------------------
# Модули корпуса — из ЭТОГО дерева. Импорт ленивый: `check_corpus` тянет numpy.
# ---------------------------------------------------------------------------
_MODS = {}


def mods():
    if not _MODS:
        for p in (SCRIPTS, PIE):
            if p not in sys.path:
                sys.path.insert(0, p)
        import score                                    # noqa: E402
        import passport                                 # noqa: E402
        import check_corpus                             # noqa: E402
        score.enable_members()
        _memo_sample_lines(check_corpus)
        _MODS.update(score=score, passport=passport, check_corpus=check_corpus)
        global MIN_MAIN_Z
        MIN_MAIN_Z = score.Z_THR
    return _MODS


def _memo_sample_lines(check_corpus):
    u"""Линии образца — одни на состав: самопроверка судит один состав десять раз, а
    `build_corpus.sample_lines` каждый раз собирает их из `nucdb` заново. Ключ — состав
    (`chains`, `nuclides`); результат отдаётся копией. Числа приёмки от этого не меняются.

    ⚠ Время сторожа съедает не это, а ОДИН запрос `chains.level_fallback_nucids` на процесс
    (коррелированный подзапрос по `decay_radiations` без индекса: 26…113 с на занятой машине
    11.11.2026, замер `D:\\BqMoni_Claude\\a314\\qtime.py`). Файл `chains.py` входит в клеймо
    генератора корпуса, поэтому здесь не правится."""
    bc = check_corpus.build_corpus
    raw = bc.sample_lines
    if getattr(raw, '_intake_memo', False):
        return
    memo = {}

    def sample_lines(entry):
        k = (tuple(entry.get('chains') or ()), tuple(entry.get('nuclides') or ()))
        if k not in memo:
            memo[k] = raw(entry)
        return list(memo[k])

    sample_lines._intake_memo = True
    bc.sample_lines = sample_lines
    check_corpus.calibrate.sample_lines = sample_lines


# ---------------------------------------------------------------------------
# К5: правило «канал переполнения» — константы ПРИЛОЖЕНИЯ, своей копии чисел нет.
# ---------------------------------------------------------------------------
_OVF = {}


def overflow_rule():
    if not _OVF:
        text = io.open(DOSE_CS, encoding='utf-8-sig').read()
        body = text[text.index('class OverflowChannel'):]
        for name in ('NeighbourWindow', 'MinRatio', 'MinSigma', 'MinNeighbours'):
            m = re.search(r'const\s+(?:int|double)\s+%s\s*=\s*([0-9.]+)\s*;' % name, body)
            if not m:
                raise RuntimeError(u'в DoseRateManager.cs нет константы OverflowChannel.%s' % name)
            _OVF[name] = float(m.group(1))
    return _OVF


def is_overflow(sp, ch):
    u"""Порт `OverflowChannel.IsOverflow` (`A203`): крайний канал против медианы соседей."""
    r = overflow_rule()
    n = len(sp)
    if n < r['MinNeighbours'] + 2 or (ch != 0 and ch != n - 1):
        return False
    window = int(min(r['NeighbourWindow'], n - 2))
    if window < r['MinNeighbours']:
        return False
    nb = sorted(sp[1 + k] if ch == 0 else sp[n - 2 - k] for k in range(window))
    m = nb[window // 2] if window % 2 == 1 else 0.5 * (nb[window // 2 - 1] + nb[window // 2])
    v = sp[ch]
    return v >= r['MinRatio'] * max(m, 1.0) and v - m >= r['MinSigma'] * math.sqrt(m + 1.0)


RE_ES = re.compile(r'<EnergySpectrum>(.*?)</EnergySpectrum>', re.S)
RE_DP = re.compile(r'<DataPoint>(-?\d+)</DataPoint>')


def _tag(text, name):
    m = re.search(r'<%s>([^<]*)</%s>' % (name, name), text)
    return m.group(1) if m else None


def spectrum_facts(path):
    u"""Канал переполнения, доля его отсчётов, мёртвое время — из ПЕРВОГО `EnergySpectrum` (сама проба)."""
    text = io.open(path, encoding='utf-8', errors='replace').read()
    es = RE_ES.search(text)
    if not es:
        return None
    s = es.group(1)
    sp = [int(x) for x in RE_DP.findall(s)]
    total = float(sum(sp))
    ovf = 0
    for ch in (0, len(sp) - 1):
        if sp and is_overflow(sp, ch):
            ovf += sp[ch]
    live = _f(_tag(s, 'LiveTime'), 0.0)
    real = _f(_tag(s, 'MeasurementTime'), 0.0)
    dead = 1.0 - live / real if live > 0 and real > 0 else float('nan')
    return dict(channels=len(sp), total=total, overflow=ovf,
                overflow_share=(ovf / total) if total > 0 else float('nan'), dead=dead)


# ---------------------------------------------------------------------------
# Данные: корпус и каталог прогона
# ---------------------------------------------------------------------------
class Corpus(object):
    def __init__(self, corpus_dir):
        self.dir = os.path.abspath(corpus_dir)
        self.spectra = os.path.join(self.dir, 'spectra')
        self.manifest_path = os.path.join(self.dir, 'manifest.csv')
        for p in (self.manifest_path, os.path.join(self.dir, 'parts.csv')):
            if not os.path.isfile(p):
                raise IOError(u'нет %s' % p)
        self.manifest = {r['key']: r for r in _read_csv(self.manifest_path)}
        self.parts = {r['spectrum']: r for r in _read_csv(os.path.join(self.dir, 'parts.csv'))}
        self.truth = mods()['score'].load_truth(self.manifest_path)

    def entry(self, key):
        u"""Запись для `check_corpus.check` — из манифеста (поля истины = `corpus_def`, сторож `check_corpus_truth`)."""
        row = self.manifest[key]
        split = lambda s: [x.strip() for x in (s or '').split(';') if x.strip()]   # noqa: E731
        return dict(key=key, det=row['det'], channels=int(row['channels']),
                    chains=split(row['chains']), nuclides=split(row['nuclides']), why=row['why'])


class Run(object):
    def __init__(self, run_dir):
        self.dir = os.path.abspath(run_dir)
        if not os.path.isdir(self.dir):
            raise IOError(u'нет каталога прогона %s' % self.dir)
        self.runs, self.bandwarn, self.components, self.limits = {}, {}, defaultdict(list), defaultdict(list)
        for kind, sink, many in (('runs', self.runs, False), ('bandwarn', self.bandwarn, False),
                                 ('components', self.components, True), ('limits', self.limits, True)):
            for path in sorted(glob.glob(os.path.join(self.dir, '*_%s_%s.csv' % (MODE, kind)))):
                for r in _read_csv(path):
                    if many:
                        sink[r['spectrum']].append(r)
                    else:
                        sink[r['spectrum']] = r
        if not self.runs:
            raise IOError(u'в %s нет ни одного *_%s_runs.csv' % (self.dir, MODE))


def declared_full_base():
    u"""Каталог полной базы из раздела «ДЕЙСТВУЮЩАЯ БАЗА» README корпуса (своей копии имени нет)."""
    text = io.open(README, encoding='utf-8').read()
    m = re.search(r'^## ✅ ДЕЙСТВУЮЩАЯ БАЗА.*?(?=^## )', text, re.S | re.M)
    if not m:
        raise IOError(u'в %s нет раздела «ДЕЙСТВУЮЩАЯ БАЗА»' % README)
    names = re.findall(r'^\|\s*\*\*полная `(out_[A-Za-z0-9_]+)`\*\*', m.group(0), re.M)
    if not names:
        raise IOError(u'в объявлении базы нет строки «полная `out_…`»')
    return os.path.join(PIE, names[0])


# ---------------------------------------------------------------------------
# Суд одного спектра
# ---------------------------------------------------------------------------
def judge(key, corpus, run, cal_cache=None):
    u"""Вердикт по восьми критериям: {К: (вердикт, число, причина)} и справочное."""
    M = mods()
    v = {}
    info = {}
    man = corpus.manifest.get(key)
    part = corpus.parts.get(key, {})
    row = run.runs.get(key)

    # К1 — геометрия и матрица
    geo = (part.get('geometry') or '').strip()
    if part.get('part') != 'known' or not geo:
        v['К1'] = (FAIL, part.get('part') or u'нет строки', u'часть %s, сцены нет' % (part.get('part') or '?'))
    elif row is None:
        v['К1'] = (FAIL, geo, u'сцена %s, но строки прогона нет' % geo)
    elif row.get('matrix_found') != '1' or row.get('matrix_applied') != '1':
        v['К1'] = (FAIL, geo, u'матрица найдена %s, применена %s — %s' % (
            row.get('matrix_found'), row.get('matrix_applied'), row.get('matrix_note')))
    else:
        v['К1'] = (PASS, geo, row.get('matrix_note') or '')

    # К2 — разбор без отказа
    if row is None:
        v['К2'] = (FAIL, '', u'спектра нет в прогоне %s' % run.dir)
    else:
        why = []
        if (row.get('error') or '').strip():
            why.append(u'отказ: ' + row['error'].strip())
        if row.get('chi2ndf') == 'ERROR' or math.isnan(_f(row.get('chi2ndf'))):
            why.append(u'χ²/ndf %s' % row.get('chi2ndf'))
        if (row.get('bg_rejected') or '').strip():
            why.append(u'фон не взят: ' + row['bg_rejected'].strip())
        edges = [n for n in ('drift_edge', 'gain_edge', 'offset_edge') if row.get(n) not in ('0', '', None)]
        if edges:
            why.append(u'шкала на краю сетки: ' + ', '.join(edges))
        if row.get('library') != 'sample':
            why.append(u'библиотека %s, а не sample' % row.get('library'))
        v['К2'] = (FAIL if why else PASS, row.get('chi2ndf', ''), '; '.join(why))

    # К3 — опоры перекалибровки конвейером
    if man is None:
        v['К3'] = (FAIL, '', u'строки манифеста нет — конвейер спектр не калибровал')
    else:
        n = int(_f(man.get('ecal_lines'), 0))
        v['К3'] = (PASS if n >= MIN_ECAL_LINES else FAIL, str(n),
                   u'%s, линий %d (порог %d), невязка %s ПШПВ' % (
                       man.get('ecal_mode'), n, MIN_ECAL_LINES, man.get('ecal_rms_fwhm')))
    info['anchors_fsa'] = row.get('anchors_used', '') if row else ''

    # К4 — «фон больше спектра»
    bw = run.bandwarn.get(key)
    if bw is None:
        v['К4'] = (FAIL, '', u'нет строки *_bandwarn.csv — признак не читается')
    elif bw.get('neg_flag') == '1':
        v['К4'] = (FAIL, bw.get('neg_z') or bw.get('neg_worst_z'),
                   u'фон больше спектра %s…%s кэВ, z %s' % (bw.get('neg_lo_kev'), bw.get('neg_hi_kev'), bw.get('neg_z')))
    else:
        v['К4'] = (PASS, bw.get('neg_worst_z', ''), '')
    info['img'] = bw.get('img_flag', '') if bw else ''

    # К5 — переполнение
    path = os.path.join(corpus.spectra, key + '.xml')
    facts = spectrum_facts(path) if os.path.isfile(path) else None
    if facts is None:
        v['К5'] = (FAIL, '', u'нет файла спектра %s' % path)
    else:
        share = facts['overflow_share']
        v['К5'] = (PASS if share <= MAX_OVERFLOW_SHARE else FAIL, '%.4f%%' % (100 * share),
                   u'в канале переполнения %d из %d отсчётов (порог %.2f %%)' % (
                       facts['overflow'], facts['total'], 100 * MAX_OVERFLOW_SHARE))
        info['dead_pct'] = '%.3f' % (100 * facts['dead']) if not math.isnan(facts['dead']) else ''

    # К6 — главная линия значима
    truth = corpus.truth.get(key, {}).get('components', set())
    fams = {M['score'].family(c) for c in truth}
    declared = [c for c in run.components.get(key, []) if M['score'].family(c['component']) in fams]
    if not declared:
        v['К6'] = (FAIL, '', u'в разборе нет ни одного объявленного компонента (%s)' % ', '.join(sorted(truth)))
    else:
        main = max(declared, key=lambda c: _f(c.get('share_pct'), 0.0))
        z = _f(main.get('z'), 0.0)
        v['К6'] = (PASS if z >= MIN_MAIN_Z else FAIL, '%.2f' % z,
                   u'%s доля %s %%, z %.2f (порог %.1f)' % (main['component'], main.get('share_pct'), z, MIN_MAIN_Z))

    # К7 — паспорт
    P = M['passport']
    keep = P.SPECTRA
    P.SPECTRA = corpus.spectra
    try:
        rows, refused, unparsed = P.collect({key: run.components.get(key, [])}, {key},
                                            manifest=corpus.manifest_path, ruled=P.ruled_in(run.dir))
    finally:
        P.SPECTRA = keep
    if refused or unparsed:
        v['К7'] = (FAIL, '', u'паспорт есть, отношения нет: ' + '; '.join(
            ['%s %s' % (n, w) for _k, n, w in refused] + [u'запись не разобрана'] * len(unparsed)))
    elif not rows:
        v['К7'] = (NA, '', u'паспорта нет — не судится')
    else:
        out = [r for r in rows if r['out']]
        v['К7'] = (FAIL if out else PASS, ' '.join('%.3f' % r['ratio'] for r in rows),
                   '; '.join(u'%s %.3f, допуск ±%.0f %%' % (r['nuc'], r['ratio'], 100 * r['tol']) for r in rows))

    # К8 — калибровка энергии и ПШПВ (независимая приёмка записанной копии)
    C = M['check_corpus']
    if man is None or facts is None:
        v['К8'] = (FAIL, '', u'нет строки манифеста или файла спектра')
    else:
        keep = C.SPECTRA
        C.SPECTRA = corpus.spectra
        try:
            r = C.check(corpus.entry(key))
        finally:
            C.SPECTRA = keep
        if r.get('err'):
            v['К8'] = (FAIL, '', u'приёмка калибровки: ' + r['err'])
        else:
            bad = []
            if r['med'] > MAX_CAL_MED_FWHM:
                bad.append(u'промах линий %.3f ПШПВ > %.2f' % (r['med'], MAX_CAL_MED_FWHM))
            if not MIN_WIDTH_RATIO <= r['width_med'] <= MAX_WIDTH_RATIO:
                bad.append(u'ширина к модели %.3f вне [%.2f, %.2f]' % (r['width_med'], MIN_WIDTH_RATIO, MAX_WIDTH_RATIO))
            v['К8'] = (FAIL if bad else PASS, '%.3f/%.3f' % (r['med'], r['width_med']),
                       '; '.join(bad) or u'линий %d, промах %.3f ПШПВ, ширина %.3f' % (r['n'], r['med'], r['width_med']))

    # К9 — статистика разбора: опоры привязки FSA и избыток χ²/ndf на отсчёт в канале
    if row is None or man is None:
        v['К9'] = (FAIL, '', u'нет строки прогона или манифеста')
    else:
        chi2 = _f(row.get('chi2ndf'))
        anchors = int(_f(row.get('anchors_used'), 0))
        per_ch = _f(man.get('counts'), 0.0) / max(_f(man.get('channels'), 1.0), 1.0)
        q = (chi2 - 1.0) / per_ch if per_ch > 0 and not math.isnan(chi2) else float('nan')
        bad = []
        if anchors < MIN_FSA_ANCHORS:
            bad.append(u'опор привязки FSA %d (порог %d)' % (anchors, MIN_FSA_ANCHORS))
        if math.isnan(q):
            v['К9'] = (NA, '', u'χ²/ndf %s — разбора нет, судит К2' % row.get('chi2ndf'))
        elif chi2 > CHI2_FLOOR and q > MAX_CHI2_EXCESS:
            bad.append(u'избыток χ² %.4f > %.3f (χ²/ndf %.2f при %.1f отсчёта на канал)' % (q, MAX_CHI2_EXCESS, chi2, per_ch))
        if 'К9' not in v:
            v['К9'] = (FAIL if bad else PASS, '%.4f/%d' % (q, anchors),
                       '; '.join(bad) or u'χ²/ndf %.2f, избыток %.4f, опор FSA %d' % (chi2, q, anchors))

    if row is not None:
        info['chi2ndf'] = row.get('chi2ndf', '')
        info['uncovered'] = row.get('uncovered_peaks', '')
    return v, info


def accepted(verdicts):
    return all(v[0] != FAIL for v in verdicts.values())


# ---------------------------------------------------------------------------
# Печать
# ---------------------------------------------------------------------------
def print_one(key, verdicts, info):
    print(u'%s: %s' % (key, u'ПРИНЯТЬ' if accepted(verdicts) else u'⛔ НЕ ПРИНИМАТЬ'))
    for c, name in CRITERIA:
        ver, num, why = verdicts[c]
        print(u'  %s %-28s %-4s %-14s %s' % (c, name, ver, num, why))
    print(u'  справочно: образы выше спектра %s, пиков вне состава %s, мёртвое время %s %%'
          % (info.get('img', ''), info.get('uncovered', ''), info.get('dead_pct', '')))


def table(keys, corpus, run, csv_path=None):
    rows = []
    for key in keys:
        v, info = judge(key, corpus, run)
        rows.append((key, v, info))
    hdr = ['spectrum', 'det', 'part'] + [c for c, _ in CRITERIA] + ['verdict'] + \
          ['%s_num' % c for c, _ in CRITERIA] + ['anchors_fsa', 'chi2ndf', 'img_flag', 'uncovered', 'dead_pct', 'why']
    if csv_path:
        with io.open(csv_path, 'w', encoding='utf-8-sig', newline='') as fh:
            w = csv.writer(fh)
            w.writerow(hdr)
            for key, v, info in rows:
                w.writerow([key, corpus.manifest.get(key, {}).get('det', ''), corpus.parts.get(key, {}).get('part', '')]
                           + [v[c][0] for c, _ in CRITERIA]
                           + [u'принять' if accepted(v) else u'отказ']
                           + [v[c][1] for c, _ in CRITERIA]
                           + [info.get('anchors_fsa', ''), info.get('chi2ndf', ''), info.get('img', ''),
                              info.get('uncovered', ''), info.get('dead_pct', ''),
                              ' | '.join('%s: %s' % (c, v[c][2]) for c, _ in CRITERIA if v[c][0] == FAIL)])
    print(u'%-26s %-7s ' % ('спектр', 'группа') + ' '.join(u'%-3s' % c for c, _ in CRITERIA) + u'  итог')
    for key, v, info in rows:
        print(u'%-26s %-7s ' % (key, corpus.manifest.get(key, {}).get('det', ''))
              + ' '.join(u'%-3s' % v[c][0] for c, _ in CRITERIA)
              + u'  ' + (u'принять' if accepted(v) else u'ОТКАЗ'))
    print()
    print(u'итого по критериям (не прошли / судилось):')
    for c, name in CRITERIA:
        judged = [r for r in rows if r[1][c][0] != NA]
        fails = [r[0] for r in judged if r[1][c][0] == FAIL]
        print(u'  %s %-28s %3d / %3d  %s' % (c, name, len(fails), len(judged), ', '.join(fails)))
    ok = [r[0] for r in rows if accepted(r[1])]
    print(u'  ПРИНЯТЬ: %d из %d' % (len(ok), len(rows)))
    return rows


# ---------------------------------------------------------------------------
# Самопроверка: положительный контроль на копиях
# ---------------------------------------------------------------------------
#: Годный образец и спектр без геометрии с разбором — из объявленной полной базы.
GOOD = 'G1S24_Cs137_P25'
NO_GEOMETRY = 'AS80_Th232WT20'


def _stage(tmp, corpus, run, key):
    u"""Копия корпуса (манифест, раздел, один спектр) и прогона (строки ключа) в `tmp`."""
    cdir = os.path.join(tmp, 'corpus')
    rdir = os.path.join(tmp, 'run')
    os.makedirs(os.path.join(cdir, 'spectra'))
    os.makedirs(rdir)
    shutil.copy2(corpus.manifest_path, os.path.join(cdir, 'manifest.csv'))
    shutil.copy2(os.path.join(corpus.dir, 'parts.csv'), os.path.join(cdir, 'parts.csv'))
    shutil.copy2(os.path.join(corpus.spectra, key + '.xml'), os.path.join(cdir, 'spectra', key + '.xml'))
    det = corpus.manifest[key]['det']
    for kind in ('runs', 'bandwarn', 'components', 'limits'):
        src = os.path.join(run.dir, '%s_%s_%s.csv' % (det, MODE, kind))
        rows = [r for r in _read_csv(src) if r['spectrum'] == key]
        with io.open(src, encoding='utf-8-sig') as fh:
            head = next(csv.reader(fh))
        _write_csv(os.path.join(rdir, os.path.basename(src)), head, rows)
    rule = os.path.join(run.dir, 'xray_rule.csv')
    if os.path.isfile(rule):
        shutil.copy2(rule, os.path.join(rdir, 'xray_rule.csv'))
    return cdir, rdir


def _write_csv(path, head, rows):
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=head)
        w.writeheader()
        for r in rows:
            w.writerow(r)


def _edit_csv(path, key, **fields):
    rows = _read_csv(path)
    with io.open(path, encoding='utf-8-sig') as fh:
        head = next(csv.reader(fh))
    for r in rows:
        if r.get('spectrum', r.get('key')) == key:
            r.update(fields)
    _write_csv(path, head, rows)


def _edit_text(path, fn):
    text = io.open(path, encoding='utf-8').read()
    new = fn(text)
    if new == text:
        raise RuntimeError(u'порча не легла: %s не изменился' % path)
    with io.open(path, 'w', encoding='utf-8', newline='') as fh:
        fh.write(new)


def _spoil_overflow(text):
    u"""Переполнение: в последний канал пробы — 5 % её отсчётов (шкала режет верх)."""
    es = RE_ES.search(text)
    s = es.group(1)
    dps = list(RE_DP.finditer(s))
    total = sum(int(m.group(1)) for m in dps)
    last = dps[-1]
    new_s = s[:last.start(1)] + str(int(last.group(1)) + int(0.05 * total)) + s[last.end(1):]
    return text[:es.start(1)] + new_s + text[es.end(1):]


def _spoil_calibration(text):
    u"""Подменённая калибровка: коэффициенты энергии пробы ×1.06 (шкала растянута на 6 %)."""
    es = RE_ES.search(text)
    s = es.group(1)
    m = re.search(r'<EnergyCalibration>.*?</EnergyCalibration>', s, re.S)
    block = re.sub(r'<Coefficient>([^<]+)</Coefficient>',
                   lambda c: '<Coefficient>%r</Coefficient>' % (float(c.group(1)) * 1.06), m.group(0))
    new_s = s[:m.start()] + block + s[m.end():]
    return text[:es.start(1)] + new_s + text[es.end(1):]


def selftest(corpus, run):
    u"""Годный проходит всё; каждая порча отказывает РОВНО по своему критерию."""
    for key in (GOOD, NO_GEOMETRY):
        if key not in run.runs or key not in corpus.manifest:
            print(u'⛔ самопроверка: %s нет в базе %s или в манифесте' % (key, run.dir))
            return 2
    cases = []
    tmp_root = tempfile.mkdtemp(prefix='intake_')
    try:
        def case(name, expect, spoil, key=GOOD):
            tmp = os.path.join(tmp_root, name)
            os.makedirs(tmp)
            cdir, rdir = _stage(tmp, corpus, run, key)
            det = corpus.manifest[key]['det']
            ctx = dict(cdir=cdir, rdir=rdir, key=key,
                       xml=os.path.join(cdir, 'spectra', key + '.xml'),
                       manifest=os.path.join(cdir, 'manifest.csv'),
                       parts=os.path.join(cdir, 'parts.csv'),
                       runs=os.path.join(rdir, '%s_%s_runs.csv' % (det, MODE)),
                       bandwarn=os.path.join(rdir, '%s_%s_bandwarn.csv' % (det, MODE)),
                       components=os.path.join(rdir, '%s_%s_components.csv' % (det, MODE)))
            if spoil:
                spoil(ctx)
            v, _info = judge(key, Corpus(cdir), Run(rdir))
            failed = {c for c, x in v.items() if x[0] == FAIL}
            ok = failed == set(expect)
            cases.append((name, key, expect, failed, ok, v))

        case(u'годный', [], None)
        # Живые данные: разбор без матрицы у этого спектра хуже и по статистике (избыток χ² 0.0117).
        case(u'нет геометрии', ['К1', 'К9'], None, key=NO_GEOMETRY)
        case(u'сцена снята из раздела', ['К1'],
             lambda c: _edit_csv(c['parts'], c['key'], part='unknown', geometry=''))
        case(u'отказ разбора', ['К2'],
             lambda c: _edit_csv(c['runs'], c['key'], error=u'порча самопроверки', chi2ndf='ERROR'))
        case(u'одна опора', ['К3'],
             lambda c: _edit_csv(c['manifest'], c['key'], ecal_lines='1'))
        case(u'фон больше спектра', ['К4'],
             lambda c: _edit_csv(c['bandwarn'], c['key'], neg_flag='1', neg_z='9.99',
                                 neg_lo_kev='100.0', neg_hi_kev='200.0'))
        case(u'переполнение 5 %', ['К5'],
             lambda c: _edit_text(c['xml'], _spoil_overflow))
        case(u'главная линия слаба', ['К6'],
             lambda c: _edit_csv(c['components'], c['key'], z='2.50'))
        case(u'паспорт вне допуска', ['К7'],
             lambda c: _edit_csv(c['manifest'], c['key'], why=corpus.manifest[c['key']]['why'].replace(
                 'A=106000 ', 'A=127200 ')))
        case(u'калибровка ×1.06', ['К8'],
             lambda c: _edit_text(c['xml'], _spoil_calibration))
        # К9: числа — из сквозного опыта критика `crit314` (шкала ×1.09 у пробы и фона, ПШПВ ×0.5)
        case(u'ноль опор FSA', ['К9'],
             lambda c: _edit_csv(c['runs'], c['key'], anchors_used='0'))
        case(u'избыток χ² (ПШПВ ×0.5)', ['К9'],
             lambda c: _edit_csv(c['runs'], c['key'], chi2ndf='14.2112'))
    finally:
        shutil.rmtree(tmp_root, ignore_errors=True)

    print(u'САМОПРОВЕРКА сторожа приёма (AMBER314): база %s, годный %s' % (os.path.basename(run.dir), GOOD))
    bad = 0
    for name, key, expect, failed, ok, v in cases:
        print(u'  %-24s %-18s ждали отказа %-8s получили %-12s %s' % (
            name, key, ','.join(expect) or u'нет', ','.join(sorted(failed)) or u'нет',
            u'сошлось' if ok else u'⛔ НЕ СОШЛОСЬ'))
        if not ok:
            bad += 1
            for c, _n in CRITERIA:
                print(u'      %s %s %s %s' % (c, v[c][0], v[c][1], v[c][2]))
    print(u'итог: %s' % (u'сошлось %d из %d' % (len(cases), len(cases)) if not bad
                         else u'⛔ не сошлось %d из %d' % (bad, len(cases))))
    return 1 if bad else 0


def main():
    _console()
    ap = argparse.ArgumentParser(description=u'Сторож приёма спектра в корпус (AMBER314)')
    ap.add_argument('--only', help=u'ключи кандидатов через запятую или файл (первый столбец)')
    ap.add_argument('--run', help=u'каталог прогона CorpusFsaProbe (умолчание — объявленная полная база)')
    ap.add_argument('--corpus', default=CORPUS, help=u'каталог corpus (умолчание — корпус дерева)')
    ap.add_argument('--table', action='store_true', help=u'таблица по всем спектрам части')
    ap.add_argument('--part', default='known', choices=['known', 'unknown', 'all'])
    ap.add_argument('--csv', help=u'записать таблицу в csv')
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()
    try:
        mods()
        run = Run(a.run or declared_full_base())
        corpus = Corpus(a.corpus)
    except (IOError, OSError, RuntimeError) as why:
        print(u'⛔ нет данных: %s' % why)
        return 2
    if a.selftest or not (a.only or a.table):
        return selftest(corpus, run)
    if a.table:
        keys = sorted(k for k in run.runs
                      if a.part == 'all' or corpus.parts.get(k, {}).get('part') == a.part)
        print(u'ТАБЛИЦА ПРИЁМА (AMBER314): прогон %s, часть %s, спектров %d' % (run.dir, a.part, len(keys)))
        table(keys, corpus, run, a.csv)
        return 0
    keys = sorted(mods()['score'].read_only(a.only))
    missing = [k for k in keys if k not in corpus.manifest]
    if missing:
        print(u'⛔ нет в манифесте %s: %s — кандидат не прошёл конвейер корпуса' % (corpus.manifest_path, ', '.join(missing)))
        return 2
    print(u'ПРИЁМ В КОРПУС (AMBER314): прогон %s, корпус %s' % (run.dir, corpus.dir))
    bad = 0
    for key in keys:
        v, info = judge(key, corpus, run)
        print_one(key, v, info)
        bad += 0 if accepted(v) else 1
    print(u'итог: принять %d, не принимать %d' % (len(keys) - bad, bad))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
