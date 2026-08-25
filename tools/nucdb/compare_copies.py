# -*- coding: utf-8 -*-
u"""D23: измерение расхождений между копиями одной величины в базе.

Таблица старшинства (`database/scheme.md`, §0а) написана 08.08.2026 и
ОПИСЫВАЕТ, кто главный, но расхождения между копиями измерены не были ни у
одной пары, кроме краёв поглощения (`check_edges.py`, D18), интенсивностей
гамма (`compare_intensities.py`, D5) и фотоэффекта (`compare_photo.py`, D24).
Здесь меряются остальные ЧЕТЫРЕ пары.

⚠ Пара «`xcom_cross_sections` против `epics_photo_*`» здесь НЕ считается: она
закрыта отдельно как D24 инструментом `compare_photo.py` (1145 узлов, 97.8 %
до 1 %, медиана 0.070 %). Повторять её нечем и незачем.

У каждой пары правило сопоставления объявляется ЗДЕСЬ, до всяких медиан, —
потому что вся цена такой сверки в том, что именно с чем сведено. Медиана,
посчитанная по неверно сведённым парам, меряет сопоставление, а не поставки.

────────────────────────────────────────────────────────────────────────────
ПАРА 2. Энергия связи для доплера: `compton_profile_shell.potential_ev`
        (EADL, `shell_seq` 0-базный) против `estar_shells.binding_ev`
        (EPICS/ESTAR, `shell_index` 1-базный). Обе — matdb, 100 элементов.

ПРАВИЛО. Номер оболочки НЕ является ключом сопоставления: у 66 элементов из
100 число оболочек у поставок разное — EADL местами держит L2+L3 одной
строкой с занятостью 6, а ESTAR теми же электронами заполняет две строки 2 и
4. Сопоставление ПОРЯДКОВОЕ ПО УБЫВАНИЮ ЭНЕРГИИ СВЯЗИ: оба списка элемента
сортируются по убыванию энергии (сами таблицы НЕ отсортированы — у свинца в
EADL идут 153.04, 147.81, 152.83), и списки идут парами сверху вниз, пока
СТРУКТУРА совпадает.

Признак совпадения структуры — ЗАНЯТОСТЬ, а не энергия. Это разделение
намеренное: занятость определяет, та же ли это оболочка, энергия — та самая
величина, расхождение которой мы и меряем. Сравнивай мы по энергии, критерий
сопоставления и измеряемая величина были бы одним и тем же числом.

На первом же расхождении занятости разбиение оболочек у поставок разошлось, и
дальше идти нельзя: остаток обоих списков ВЫБРАСЫВАЕТСЯ (в ESTAR занятость
валентной оболочки бывает отрицательной — берётся модуль). По каждому Z
печатается, сколько оболочек сопоставлено и сколько выброшено с каждой
стороны.

────────────────────────────────────────────────────────────────────────────
ПАРА 3. Коэффициенты внутренней конверсии: `icc_coefficients` (matdb, ЛСРМ
        2007) против `g4_gamma.icc_*` и `ensdf_gammas.conv_coef` (schemedb).

⛔ СВЕРЯЕТСЯ СУММА K+L+M, А НЕ ПОЛНЫЙ α. В сетке ЛСРМ внешних оболочек нет
вовсе (K, L1–L3, M1–M5 и всё), а у Geant4 доля внешних лежит отдельным полем
`icc_outer_ppm`. Сложив у ЛСРМ то, что есть, и сравнив с полным α Geant4, мы
намеряли бы ровно долю внешних оболочек и назвали бы её расхождением
поставок. Поэтому у Geant4 берётся α·(K+L+M)/1e6.

РАСШИФРОВКА КОДА Geant4. `scheme.md` §5г пишет «1…7 = E0,E1,M1,E2,M2,E3,M3»;
в таблице встречаются коды 8…16, то есть описание оборвано. Продолжение
очевидно (E_k = 2k, M_k = 2k+1 при k ≥ 1, E0 = 1) и здесь НЕ принимается на
веру, а ПРОВЕРЯЕТСЯ: для каждого кода печатается, какая из восьми колонок
ЛСРМ (e1…e4, m1…m4) ближе всего к α_K поставки Geant4. Совпал ли ответ с
разгаданной кодировкой, видно в отчёте.

Смесь = 100·Nx+Ny (304 = M1+E2, 17 207 переходов — самая частая смесь, что
кодировку и подтверждает). Вес по `mixing_ratio` δ:
α = (α_младш + δ²·α_старш) / (1 + δ²).

⚠ δ² ложится на СТАРШУЮ мультипольность, а не на второй компонент кода.
Порядок в коде не постоянен: 304 = M1+E2 и 403 = E2+M1 — одна и та же смесь,
записанная в обе стороны. Взяв «второй компонент», на коде 403 намеряешь
медиану 49.65 % (238 переходов), а на 502 — 791 % (3 перехода); с сортировкой
по порядку мультипольности они падают до 2.448 % и 0.783 %. Это измерено, а не
выведено (перемерено 25.08.2026 тем же кодом; прежние «35 % → 4.1 %» неверны).

⛔ СМЕСЬ С δ = 0 НЕСОПОСТАВИМА и выбрасывается. Ноль в `mixing_ratio` у
Geant4 означает и «чистый переход», и «данных нет», а код смеси прямо говорит,
что переход НЕ чистый: свернув такую смесь в первый компонент, считаешь не ту
величину. Цена измерена: у смесей с настоящим δ медиана расхождения 1.99 % и
2.7 % хвоста выше 20 %, у смесей с δ = 0 — 20.9 % и 51 %.

НЕСОПОСТАВИМО и печатается отдельными строками: код 0 (неизвестна), код 1 (E0 —
колонки под него у ЛСРМ нет), коды ≥ 10 (E5 и выше — колонок нет), смесь без
δ. Коды 8 и 9 (E4/M4) на колонки e4/m4 ложатся, и проверка кодировки это
показывает.

`variant`: берётся `variant = 1`. Второй расчёт есть только у Z = 30 (15
строк, K и L1–L3) и расходится с первым в третьей значащей цифре; насколько
именно — печатается отдельно, в сверку он не идёт.

РАЗБОР ТЕКСТА ENSDF. Скобки круглые и квадратные СНИМАЮТСЯ и запись берётся:
это оценка составителя, а не неоднозначность (`scheme.md` §5г). Запятая —
это именно неоднозначность («M1,E2» = «или то, или это»), такие ОТБРАСЫВАЮТСЯ.
Плюс — смесь, берётся с δ. Всё, что не разбирается в E1–E4 / M1–M4 («D», «Q»,
«1,2+», пусто), отбрасывается; сколько именно — печатается.

⚠ У ENSDF `conv_coef` — ПОЛНЫЙ α, пооболочечного разбиения там нет вовсе,
поэтому в сверку «K+L+M» ENSDF войти не может. Она считается отдельно и
односторонне: ЛСРМ(K+L+M) обязана быть НИЖЕ полного α ENSDF ровно на долю
внешних оболочек, и эта доля тут же меряется по Geant4 для сравнения.

⚠ Мусорные `icc_total` Geant4 (α выше 1e4 = «гаммы нет», до 3.7e22) зажаты
порогом 1e4, как у читателей в коде (`CascadeAtomicData.AlphaCeiling`).

⚠ У каждой оболочки СВОЯ сетка по энергии и свой верх: у Z = 56 K доходит до
1550 кэВ, L1 до 1500, M1 только до 500. Переход, у которого хоть одна из
девяти оболочек вне своей сетки, из сверки K+L+M выбрасывается — сколько
именно, печатается. Экстраполяции нет: сечение конверсии падает как степень
энергии, и продлённая за край сетка мерила бы саму себя. Для контроля тем же
ходом считается сверка по ОДНОЙ K-оболочке, где сетка шире всего.

────────────────────────────────────────────────────────────────────────────
ПАРА 4. Период полураспада: `nuclides.half_life_sec` (nucdb) против
        `g4_level` (seq основного состояния = 0), `ensdf_levels`
        (seq основного состояния = 1, НЕ 0) и `ensdf_datasets.parent_hl_sec`.

ПРАВИЛО. Соединение по `nucid` С НОРМАЛИЗАЦИЕЙ РЕГИСТРА: nucdb пишет
основные состояния прописными («100AG»), ENSDF — смешанным регистром
(«100Ag»), и без `upper()` не сходится вообще ничего. Из nucdb берутся только
основные состояния (`l_seqno = 0`) — изомеры у ENSDF отдельного nucid не
имеют, и «100AGm» соединился бы с основным состоянием серебра.

Для g4 соединение по (z, a): a = z + n из nucdb. У `g4_level` основное
состояние — seq = 0, у `ensdf_levels` — seq = 1 (нумерация 1-базная).

СТАБИЛЬНОСТЬ СЧИТАЕТСЯ ОТДЕЛЬНО ОТ ЧИСЛА, И КОДИРУЕТСЯ ОНА ПО-РАЗНОМУ.
У Geant4 стабильный нуклид — NULL (в файле −1), у ENSDF — БЕСКОНЕЧНОСТЬ (304
уровня `ensdf_levels` несут `+inf`), у nucdb — NULL либо огромное число
(двойной бета-распад Mo-100: 2.2e26 с). Оба вида «стабилен» сводятся к None,
иначе Bi-209 даёт «расхождение +inf %». Сперва печатается согласие
«стабилен / не стабилен», и только по нуклидам, где ОБЕ поставки дали
конечное число, считается расхождение. Порог — 1 %, как у остальных сверок.

⛔ НУКЛИД, У КОТОРОГО СВЕРЯЕМАЯ ТАБЛИЦА ДАЁТ НЕСКОЛЬКО РАЗНЫХ ЗНАЧЕНИЙ,
ВЫБРАСЫВАЕТСЯ. Это изомерная неоднозначность, а не расхождение поставок:
`ensdf_datasets.parent_nucid` НЕ помечает изомер, и у Ag-110 (24.6 с) и
Ag-110m (249.76 сут) там одинаковое «110AG» — различие живёт только в тексте
`dsid`. Без отсева такая пара приходит как расхождение в 8.8·10⁷ %, и хвост
выше 20 % раздувается с 7.3 % до 23 %. Тот же приём и по той же причине, что
и отсев изомерных родителей в `compare_intensities.py` (W9).

⚠ Отдельная ловушка НЕ поставки, а величины: у 37 нуклидов nucdb несёт ровно
3e-07 с — это предел «> 300 нс» из исходной таблицы, записанный числом. Такие
строки дают верх хвоста (Ir-200, Ir-201, Tl-215) и расхождением поставок не
являются.

────────────────────────────────────────────────────────────────────────────
ПАРА 5. Привязка гамма к уровням: `g4_gamma` против `ensdf_gammas` (обе
        schemedb). Нумерации уровней у поставок НЕЗАВИСИМЫЕ и несравнимы
        напрямую: у ENSDF номер живёт внутри набора данных (`dataset_id`,
        1-базный, схема РАСПАДА — только заселяемые уровни), у Geant4 —
        внутри нуклида (`seq`, 0-базный, схема УРОВНЕЙ — все).

ПРАВИЛО. Сперва строится и ИЗМЕРЯЕТСЯ соответствие нумераций ПО ЭНЕРГИИ
УРОВНЯ, и только потом сравнивается что-либо ещё. Оба списка уровней нуклида
сортируются по энергии, идут навстречу друг другу монотонно (жадное
сопоставление ближайших с сохранением порядка — «сопоставить можно только
вперёд»), допуск на энергию — max(`--level-tol` кэВ, 0.05 % энергии): у
Geant4 энергии целыми эВ, у ENSDF — с точностью составителя, и на 3 МэВ
абсолютный допуск в доли кэВ заведомо мал. Доля сопоставленных уровней
печатается ПЕРВОЙ строкой отчёта — она и есть цена всего остального.

Дальше по сопоставленным уровням сверяется САМА ПРИВЯЗКА: для каждой гаммы
ENSDF, у которой оба конца попали в соответствие, ищется переход Geant4 с той
же парой (from, to). Печатается, у скольких пара совпала, у скольких Geant4
кладёт переход той же энергии между ДРУГИМИ уровнями (это и есть расхождение
привязки), и у скольких перехода такой энергии не нашлось.

⛔ ФИЛЬТР ПО ИНТЕНСИВНОСТИ ОБЪЯВЛЕН И СЧИТАЕТСЯ В ОБЕ СТОРОНЫ. Отбор
`g4_gamma.intensity_ppm > 0` выбрасывает 32 221 переход из 297 055 (10.8 %) —
у Geant4 это переходы схемы уровней без заселённости, а не отсутствующие
переходы. Заглавное число от такого отбора меняется впятеро, поэтому сверка
идёт ДВАЖДЫ и печатаются ОБА столбца:

    все 297 055 переходов        совпало 70 175 (98.8 %), между другими 582,
                                 не нашлось 271;
    только intensity_ppm > 0     совпало 68 914 (97.0 %), между другими 788,
                                 не нашлось 1326.

Разница — 1055 гамма, у которых переход в `g4_gamma` ЕСТЬ, но с нулевой
интенсивностью. Поэтому строка отчёта про них читается «нет СРЕДИ ПЕРЕХОДОВ С
НЕНУЛЕВОЙ ИНТЕНСИВНОСТЬЮ», а «нет вовсе» верно только для столбца по всем
переходам. Измерено 25.08.2026.

────────────────────────────────────────────────────────────────────────────
В базы ничего не пишется: все три открываются `file:…?mode=ro`.

    python compare_copies.py [--pair 2,3,4,5] [--matdb ...] [--nucdb ...]
                             [--schemedb ...] [--worst N] [--level-tol 0.4]
"""
import argparse
import bisect
import collections
import io
import math
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
BIN = os.path.join(ROOT, "BecquerelMonitor")
DEFAULT_MATDB = os.path.join(BIN, "matdb.sqlite")
DEFAULT_NUCDB = os.path.join(BIN, "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(BIN, "schemedb.sqlite")

ALPHA_CEILING = 1e4          # CascadeAtomicData.AlphaCeiling
ICC_SHELLS = ("K", "L1", "L2", "L3", "M1", "M2", "M3", "M4", "M5")
ICC_COLS = ("e1", "e2", "e3", "e4", "m1", "m2", "m3", "m4")

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


def ro(path):
    if not os.path.isfile(path):
        sys.exit(u"нет базы: %s" % path)
    return sqlite3.connect("file:%s?mode=ro" % path.replace(chr(92), "/"),
                           uri=True)


def median(values):
    values = sorted(values)
    if not values:
        return float("nan")
    return values[len(values) // 2]


def pct(values):
    u"""Строка «медиана / 90-й процентиль / максимум», %."""
    if not values:
        return u"нет пар"
    s = sorted(values)
    return u"медиана %.3f %%, 90-й проц. %.2f %%, макс %.1f %%" % (
        s[len(s) // 2], s[int(0.9 * (len(s) - 1))], s[-1])


def buckets(values):
    b = collections.Counter()
    for d in values:
        b[u"≤1 %" if d <= 1 else u"≤5 %" if d <= 5 else
          u"≤20 %" if d <= 20 else u"> 20 %"] += 1
    return b


def print_buckets(values, width=7):
    n = max(1, len(values))
    b = buckets(values)
    for key in (u"≤1 %", u"≤5 %", u"≤20 %", u"> 20 %"):
        w(u"    расхождение %-*s %7d  %5.1f %%", width, key, b[key],
          100.0 * b[key] / n)


def symbols(nuc):
    out = {}
    for sym, z in nuc.execute(
            "select distinct symbol, z from nuclides where z > 0"):
        out.setdefault(z, sym)
    return out


# ═══════════════════════════════════════════════════════════════════════════
# ПАРА 2 — энергия связи оболочки: EADL против EPICS/ESTAR
# ═══════════════════════════════════════════════════════════════════════════
def pair2(mat, sym, worst_n):
    w(u"")
    w(u"══ ПАРА 2. Энергия связи оболочки: `compton_profile_shell` (EADL)")
    w(u"           против `estar_shells` (EPICS/ESTAR), matdb")
    w(u"")
    w(u"Правило: порядковое сопоставление по УБЫВАНИЮ энергии связи, признак")
    w(u"той же оболочки — ЗАНЯТОСТЬ (по модулю); на первом расхождении")
    w(u"занятости остаток обоих списков выбрасывается.")
    w(u"")

    eadl = collections.defaultdict(list)
    for z, seq, occ, e in mat.execute(
            "select z, shell_seq, occupancy, potential_ev"
            " from compton_profile_shell"):
        eadl[z].append((e, abs(occ)))
    estar = collections.defaultdict(list)
    for z, idx, occ, e in mat.execute(
            "select z, shell_index, occupation, binding_ev from estar_shells"):
        estar[z].append((e, abs(occ)))

    rows, devs, worst = [], [], []
    inner, outer = [], []
    same_count = 0
    for z in sorted(set(eadl) & set(estar)):
        a = sorted(eadl[z], key=lambda t: -t[0])
        b = sorted(estar[z], key=lambda t: -t[0])
        if len(a) == len(b):
            same_count += 1
        k = 0
        while k < len(a) and k < len(b) and abs(a[k][1] - b[k][1]) < 1e-9:
            ea, eb = a[k][0], b[k][0]
            if ea > 0:
                d = abs(100.0 * (eb - ea) / ea)
                devs.append(d)
                (inner if ea >= 1000.0 else outer).append(d)
                worst.append((d, z, k, ea, eb))
            k += 1
        rows.append((z, k, len(a) - k, len(b) - k))

    w(u"элементов в обеих таблицах: %d; число оболочек совпадает у %d,"
      u" расходится у %d", len(rows), same_count, len(rows) - same_count)
    w(u"")
    w(u"По каждому Z: сопоставлено / выброшено EADL / выброшено ESTAR")
    line = []
    for z, k, da, db in rows:
        line.append(u"%3d %-2s %2d/%2d/%2d" % (z, sym.get(z, u"?"), k, da, db))
        if len(line) == 5:
            w(u"  " + u"   ".join(line))
            line = []
    if line:
        w(u"  " + u"   ".join(line))

    tot_a = sum(r[2] for r in rows)
    tot_b = sum(r[3] for r in rows)
    w(u"")
    w(u"итого сопоставлено оболочек: %d; выброшено EADL %d, ESTAR %d",
      len(devs), tot_a, tot_b)
    print_buckets(devs)
    w(u"    %s", pct(devs))
    w(u"")
    w(u"Разбивка по глубине оболочки — читателю (`ScatteringData`) важны")
    w(u"внутренние, доплеровское размытие внешними не задаётся:")
    w(u"    связь ≥ 1 кэВ: %5d оболочек, %s", len(inner), pct(inner))
    w(u"    связь < 1 кэВ: %5d оболочек, %s", len(outer), pct(outer))
    w(u"    доля ≤ 1 %% : внутренние %.1f %%, внешние %.1f %%",
      100.0 * sum(1 for d in inner if d <= 1) / max(1, len(inner)),
      100.0 * sum(1 for d in outer if d <= 1) / max(1, len(outer)))

    worst.sort(reverse=True)
    w(u"")
    w(u"худшие %d сопоставленных оболочек:", worst_n)
    for d, z, k, ea, eb in worst[:worst_n]:
        w(u"    Z=%-3d %-2s  оболочка %2d:  EADL %11.2f эВ  ESTAR %11.2f эВ"
          u"  %+.1f %%", z, sym.get(z, u"?"), k, ea, eb,
          100.0 * (eb - ea) / ea)


# ═══════════════════════════════════════════════════════════════════════════
# ПАРА 3 — коэффициенты внутренней конверсии
# ═══════════════════════════════════════════════════════════════════════════
def g4_multipole(code):
    u"""Код Geant4 -> список компонентов ('e1'..'e4','m1'..'m4') или None.

    E0 = 1; при k >= 1: E_k = 2k, M_k = 2k+1. Смесь = 100*Nx + Ny.
    Возвращает None, если хоть один компонент не ложится на колонки ЛСРМ.
    """
    if code <= 0:
        return None
    parts = [code // 100, code % 100] if code >= 100 else [code]
    out = []
    for p in parts:
        if p < 2:                      # 0 — нет, 1 — E0: колонки нет
            return None
        k, rem = p // 2, p % 2
        if k > 4:                      # E5 и выше: колонок нет
            return None
        out.append(("m" if rem else "e") + str(k))
    return out


def order_components(comps, delta):
    u"""Упорядочить смесь: младшая мультипольность первой, δ² — на старшую.

    Возвращает None, если смесь заявлена, а δ отсутствует (в поставке ноль
    означает и «чистый переход», и «данных нет» — свернуть смесь в первый
    компонент значило бы считать не ту величину).
    """
    if comps is None or len(comps) == 1:
        return comps
    if not delta:
        return None
    return sorted(comps, key=lambda c: int(c[1]))


ENSDF_TOKEN = re.compile(r"^([EM])([1-4])$")


def ensdf_multipole(text):
    u"""Текст ENSDF -> список колонок, либо None (неразбираемо/неоднозначно)."""
    if not text:
        return None
    t = text.replace("(", "").replace(")", "").replace("[", "")
    t = t.replace("]", "").replace(" ", "").upper()
    if not t or "," in t or "OR" in t:
        return None                    # неоднозначность, а не смесь
    parts = t.split("+")
    if len(parts) > 2:
        return None
    out = []
    for p in parts:
        m = ENSDF_TOKEN.match(p)
        if not m:
            return None
        out.append(("e" if m.group(1) == "E" else "m") + m.group(2))
    return out


class IccGrid(object):
    u"""Сетка ЛСРМ: (z, shell) -> отсортированные энергии и восемь колонок."""

    def __init__(self, mat):
        self.e = {}
        self.v = {}
        self.zshells = collections.defaultdict(set)
        cols = ", ".join(ICC_COLS)
        for row in mat.execute(
                "select z, shell, energy_kev, " + cols +
                " from icc_coefficients where variant = 1"
                " order by z, shell, energy_kev"):
            z, shell, e = row[0], row[1], row[2]
            self.e.setdefault((z, shell), []).append(e)
            self.v.setdefault((z, shell), []).append(row[3:])
            self.zshells[z].add(shell)

    def full(self, z):
        return len(self.zshells.get(z, ())) == len(ICC_SHELLS)

    def alpha(self, z, shell, e_kev, col):
        u"""log-log интерполяция по энергии; None — вне сетки."""
        key = (z, shell)
        xs = self.e.get(key)
        if not xs or e_kev < xs[0] or e_kev > xs[-1]:
            return None
        j = ICC_COLS.index(col)
        i = bisect.bisect_left(xs, e_kev)
        if i < len(xs) and xs[i] == e_kev:
            return self.v[key][i][j]
        lo, hi = i - 1, i
        y0, y1 = self.v[key][lo][j], self.v[key][hi][j]
        if y0 <= 0 or y1 <= 0:
            return None
        t = ((math.log(e_kev) - math.log(xs[lo])) /
             (math.log(xs[hi]) - math.log(xs[lo])))
        return math.exp(math.log(y0) + t * (math.log(y1) - math.log(y0)))

    def mixed(self, z, shell, e_kev, comps, delta):
        u"""Смесь: (a_младш + d^2 * a_старш)/(1 + d^2).

        `comps` для смеси обязан быть уже упорядочен по мультипольности
        (см. `order_components`); смесь без δ сюда не должна попадать.
        """
        a1 = self.alpha(z, shell, e_kev, comps[0])
        if a1 is None:
            return None
        if len(comps) == 1:
            return a1
        a2 = self.alpha(z, shell, e_kev, comps[1])
        if a2 is None:
            return None
        d2 = delta * delta
        return (a1 + d2 * a2) / (1.0 + d2)

    def sum_klm(self, z, e_kev, comps, delta):
        total = 0.0
        for shell in ICC_SHELLS:
            a = self.mixed(z, shell, e_kev, comps, delta)
            if a is None:
                return None
            total += a
        return total


def verify_encoding(grid, scheme):
    u"""Проверка расшифровки кода Geant4 по α_K, без опоры на догадку."""
    w(u"")
    w(u"── 3г. Проверка расшифровки кода Geant4: какая колонка ЛСРМ ближе")
    w(u"       всего к α_K поставки Geant4 (чистые коды, без смесей)")
    hits = collections.defaultdict(lambda: collections.defaultdict(list))
    for z, e_ev, code, tot, kppm in scheme.execute(
            "select z, energy_ev, multipolarity, icc_total, icc_k_ppm"
            " from g4_gamma where multipolarity between 1 and 99"
            " and icc_k_ppm is not null and icc_total > 0"
            " and icc_total < ?", (ALPHA_CEILING,)):
        if not grid.full(z):
            continue
        ak = tot * kppm / 1e6
        if ak <= 0:
            continue
        e_kev = e_ev / 1000.0
        for col in ICC_COLS:
            a = grid.alpha(z, "K", e_kev, col)
            if a and a > 0:
                hits[code][col].append(abs(math.log(a / ak)))
    w(u"       код  ожидание  лучшая  медиана |ln(α_ЛСРМ/α_K)|  переходов")
    for code in sorted(hits):
        exp = g4_multipole(code)
        best = sorted((median(v), c) for c, v in hits[code].items()
                      if len(v) >= 5)
        if not best:
            continue
        n = len(hits[code][best[0][1]])
        mark = (u"совпало" if exp and best[0][1] == exp[0] else
                u"НЕ совпало" if exp else u"кода нет у ЛСРМ")
        w(u"      %4d  %-8s  %-6s  %18.4f  %6d   ← %s", code,
          exp[0] if exp else u"—", best[0][1], best[0][0], n, mark)


def pair3(mat, scheme, nuc, sym, worst_n):
    w(u"")
    w(u"══ ПАРА 3. Коэффициенты конверсии: `icc_coefficients` (ЛСРМ, matdb)")
    w(u"           против `g4_gamma.icc_*` и `ensdf_gammas.conv_coef`")
    w(u"")
    w(u"Правило: сверяется СУММА K+L+M, а не полный α — внешних оболочек в")
    w(u"сетке ЛСРМ нет вовсе, а у Geant4 они лежат в `icc_outer_ppm`.")
    w(u"Смесь 100·Nx+Ny взвешивается по δ: (α₁+δ²α₂)/(1+δ²). variant = 1.")
    w(u"")

    grid = IccGrid(mat)
    w(u"сетка ЛСРМ: %d элементов, из них с полным набором K+L1-3+M1-5: %d",
      len(grid.zshells), sum(1 for z in grid.zshells if grid.full(z)))

    # --- variant 1 против variant 2 ---
    vpairs = []
    for z, shell, e in mat.execute(
            "select distinct z, shell, energy_kev from icc_coefficients"
            " where variant = 2"):
        cols = ", ".join(ICC_COLS)
        a = mat.execute("select " + cols + " from icc_coefficients where z=?"
                        " and shell=? and energy_kev=? and variant=1",
                        (z, shell, e)).fetchone()
        b = mat.execute("select " + cols + " from icc_coefficients where z=?"
                        " and shell=? and energy_kev=? and variant=2",
                        (z, shell, e)).fetchone()
        if a and b:
            for x, y in zip(a, b):
                if x:
                    vpairs.append(abs(100.0 * (y - x) / x))
    w(u"variant 2 (в сверку не идёт): %d значений, %s",
      len(vpairs), pct(vpairs))

    # --- 3а: ЛСРМ(K+L+M) против Geant4(K+L+M) ---
    w(u"")
    w(u"── 3а. ЛСРМ(K+L+M) против Geant4 α·(K+L+M)/1e6")
    devs, worst = [], []
    skip = collections.Counter()
    outer_share = []
    per_code = collections.defaultdict(list)
    for z, a, e_ev, code, delta, tot, kp, lp, mp, op in scheme.execute(
            "select z, a, energy_ev, multipolarity, mixing_ratio, icc_total,"
            " icc_k_ppm, icc_l_ppm, icc_m_ppm, icc_outer_ppm from g4_gamma"
            " where icc_k_ppm is not null"):
        if not (0 < tot < ALPHA_CEILING):
            skip[u"мусорный или нулевой α Geant4 (порог 1e4)"] += 1
            continue
        comps = g4_multipole(code)
        if comps is None:
            skip[u"НЕСОПОСТАВИМО: код 0 / E0 / E5 и выше"] += 1
            continue
        comps = order_components(comps, delta)
        if comps is None:
            skip[u"НЕСОПОСТАВИМО: смесь заявлена, а δ = 0"] += 1
            continue
        if not grid.full(z):
            skip[u"у Z нет полного набора оболочек в ЛСРМ"] += 1
            continue
        e_kev = e_ev / 1000.0
        lsrm = grid.sum_klm(z, e_kev, comps, delta)
        if lsrm is None:
            skip[u"энергия вне сетки хотя бы одной оболочки"] += 1
            continue
        g4 = tot * (kp + lp + mp) / 1e6
        if g4 <= 0:
            skip[u"нулевая доля K+L+M у Geant4"] += 1
            continue
        outer_share.append(100.0 * op / 1e6)
        d = abs(100.0 * (lsrm / g4 - 1.0))
        devs.append(d)
        per_code[code].append(d)
        worst.append((d, z, a, e_kev, code, lsrm, g4))

    w(u"    сверено переходов: %d", len(devs))
    for k in sorted(skip, key=lambda x: -skip[x]):
        w(u"    выброшено — %-46s %7d", k, skip[k])
    print_buckets(devs)
    w(u"    %s", pct(devs))
    if outer_share:
        w(u"    доля внешних оболочек у сверенных (`icc_outer_ppm`):"
          u" медиана %.2f %%", median(outer_share))
    w(u"    по кодам мультипольности (все с 20+ переходами):")
    for code in sorted(per_code, key=lambda c: -len(per_code[c])):
        v = per_code[code]
        if len(v) < 20:
            continue
        comps = g4_multipole(code)
        w(u"      код %-4d %-8s n=%6d  медиана %7.2f %%  хвост > 20 %%:"
          u" %5.1f %%", code, u"+".join(comps).upper(), len(v), median(v),
          100.0 * sum(1 for x in v if x > 20) / len(v))
    worst.sort(reverse=True)
    w(u"    худшие %d:", worst_n)
    for d, z, a, e, code, l, g in worst[:worst_n]:
        w(u"      %-2s-%-3d %9.3f кэВ  код %-4d  ЛСРМ %.4g  Geant4 %.4g"
          u"  %+.0f %%", sym.get(z, u"?"), a, e, code, l, g,
          100.0 * (l / g - 1.0))

    # --- 3б: ЛСРМ(K+L+M) против ENSDF conv_coef (ПОЛНЫЙ α) ---
    w(u"")
    w(u"── 3б. ЛСРМ(K+L+M) против `ensdf_gammas.conv_coef` (ПОЛНЫЙ α)")
    w(u"       односторонняя: у ENSDF пооболочечного разбиения нет, ЛСРМ")
    w(u"       обязана быть НИЖЕ ровно на долю внешних оболочек")
    zof = {}
    for nucid, z, n in nuc.execute(
            "select nucid, z, n from nuclides where l_seqno = 0"):
        zof[nucid.upper()] = (z, z + n)
    ratios = []
    eskip = collections.Counter()
    for nucid, e_kev, mtext, delta, cc in scheme.execute(
            "select d.nucid, g.energy_kev, g.multipolarity, g.mixing_ratio,"
            " g.conv_coef from ensdf_gammas g join ensdf_datasets d"
            " on d.id = g.dataset_id where g.conv_coef is not null"
            " and g.energy_kev is not null"):
        if not (cc > 0):
            eskip[u"нулевой или отрицательный α"] += 1
            continue
        za = zof.get(nucid.upper())
        if za is None:
            eskip[u"нуклид не найден в nucdb"] += 1
            continue
        comps = ensdf_multipole(mtext)
        if comps is None:
            eskip[u"мультипольность не разобрана (пусто, запятая, D/Q, …)"] += 1
            continue
        comps = order_components(comps, delta)
        if comps is None:
            eskip[u"НЕСОПОСТАВИМО: смесь заявлена, а δ нет"] += 1
            continue
        if not grid.full(za[0]):
            eskip[u"у Z нет полного набора оболочек в ЛСРМ"] += 1
            continue
        lsrm = grid.sum_klm(za[0], e_kev, comps, delta)
        if lsrm is None:
            eskip[u"энергия вне сетки хотя бы одной оболочки"] += 1
            continue
        ratios.append(100.0 * (lsrm / cc - 1.0))
    w(u"    сверено линий: %d", len(ratios))
    for k in sorted(eskip, key=lambda x: -eskip[x]):
        w(u"    выброшено — %-46s %7d", k, eskip[k])
    if ratios:
        s = sorted(ratios)
        w(u"    ЛСРМ(K+L+M) / ENSDF(полный α) − 1: медиана %+.2f %%,"
          u" 10-й проц. %+.1f %%, 90-й проц. %+.1f %%",
          s[len(s) // 2], s[int(0.1 * (len(s) - 1))],
          s[int(0.9 * (len(s) - 1))])
        w(u"    доля |отклонения| ≤ 5 %%: %.1f %%; ≤ 20 %%: %.1f %%",
          100.0 * sum(1 for r in ratios if abs(r) <= 5) / len(ratios),
          100.0 * sum(1 for r in ratios if abs(r) <= 20) / len(ratios))

    # --- 3в: контроль по одной K-оболочке ---
    w(u"")
    w(u"── 3в. Контроль: та же сверка по ОДНОЙ K-оболочке (сетка шире всего)")
    kdevs = []
    for z, e_ev, code, delta, tot, kp in scheme.execute(
            "select z, energy_ev, multipolarity, mixing_ratio, icc_total,"
            " icc_k_ppm from g4_gamma where icc_k_ppm is not null"):
        if not (0 < tot < ALPHA_CEILING):
            continue
        comps = order_components(g4_multipole(code), delta)
        if comps is None or z not in grid.zshells:
            continue
        lsrm = grid.mixed(z, "K", e_ev / 1000.0, comps, delta)
        g4 = tot * kp / 1e6
        if lsrm is None or g4 <= 0:
            continue
        kdevs.append(abs(100.0 * (lsrm / g4 - 1.0)))
    w(u"    сверено переходов: %d (против %d у K+L+M)", len(kdevs), len(devs))
    print_buckets(kdevs)
    w(u"    %s", pct(kdevs))

    verify_encoding(grid, scheme)


# ═══════════════════════════════════════════════════════════════════════════
# ПАРА 4 — период полураспада
# ═══════════════════════════════════════════════════════════════════════════
def pair4(nuc, scheme, sym, worst_n):
    w(u"")
    w(u"══ ПАРА 4. Период полураспада: `nuclides.half_life_sec` (nucdb)")
    w(u"           против `g4_level`, `ensdf_levels`, `ensdf_datasets`")
    w(u"")
    w(u"Правило: соединение по nucid с нормализацией регистра (upper), только")
    w(u"основные состояния nucdb (`l_seqno = 0`); у `g4_level` основное")
    w(u"состояние seq = 0, у `ensdf_levels` seq = 1. Стабильность считается")
    w(u"отдельно от числа.")
    w(u"")

    base = {}
    for nucid, z, n, hl in nuc.execute(
            "select nucid, z, n, half_life_sec from nuclides"
            " where l_seqno = 0"):
        base[nucid.upper()] = (z, z + n, hl)
    w(u"основных состояний в nucdb: %d, из них с числом: %d",
      len(base), sum(1 for v in base.values() if v[2] is not None))

    def compare(title, other, note=u""):
        u"""other: nucid(upper) -> множество значений (None = стабилен).

        Нуклид, у которого сверяемая таблица даёт НЕСКОЛЬКО разных значений,
        из сверки выбрасывается: это изомерная неоднозначность, а не
        расхождение поставок (см. шапку, пара 4).
        """
        w(u"")
        w(u"── %s", title)
        if note:
            w(u"      %s", note)
        common = set(base) & set(other)
        ambiguous = sum(1 for k in common if len(other[k]) > 1)
        both_fin, only_a, only_b, both_stable = 0, 0, 0, 0
        devs, worst = [], []
        for k in common:
            if len(other[k]) > 1:
                continue
            a = base[k][2]
            b = list(other[k])[0]
            if a is None and b is None:
                both_stable += 1
            elif a is None:
                only_b += 1
            elif b is None:
                only_a += 1
            else:
                both_fin += 1
                if a > 0 and b > 0:
                    d = abs(100.0 * (b / a - 1.0))
                    devs.append(d)
                    worst.append((d, k, a, b))
        w(u"    общих нуклидов: %d, из них выброшено как изомерно"
          u" неоднозначные (у сверяемой несколько разных значений): %d",
          len(common), ambiguous)
        w(u"    оба стабильны: %d; число только у nucdb: %d;"
          u" только у сверяемой: %d", both_stable, only_a, only_b)
        w(u"    оба дали число: %d, из них сравнимо (оба > 0): %d",
          both_fin, len(devs))
        print_buckets(devs)
        w(u"    %s", pct(devs))
        worst.sort(reverse=True)
        for d, k, a, b in worst[:worst_n]:
            w(u"      %-8s nucdb %.6g с   сверяемая %.6g с   %+.0f %%",
              k, a, b, 100.0 * (b / a - 1.0))
        return devs

    def stable(hl):
        u"""None и +inf — оба «стабилен»: Geant4 пишет NULL (в файле −1),
        ENSDF пишет бесконечность (304 уровня)."""
        return None if hl is None or hl > 1e300 else hl

    byza = dict(((v[0], v[1]), k) for k, v in base.items())
    g4 = collections.defaultdict(set)
    for z, a, hl in scheme.execute(
            "select z, a, half_life_sec from g4_level where seq = 0"):
        k = byza.get((z, a))
        if k is not None:
            g4[k].add(stable(hl))
    compare(u"4а. `g4_level`, seq = 0 (соединение по z, a = z + n)", g4,
            u"NULL у Geant4 = стабилен (в файле −1)")

    ens = collections.defaultdict(set)
    for nucid, hl in scheme.execute(
            "select d.nucid, l.half_life_sec from ensdf_levels l"
            " join ensdf_datasets d on d.id = l.dataset_id where l.seq = 1"):
        ens[nucid.upper()].add(stable(hl))
    compare(u"4б. `ensdf_levels`, seq = 1 — основное состояние ДОЧКИ", ens,
            u"наборов на нуклид несколько, у каждого свой список уровней")

    par = collections.defaultdict(set)
    for nucid, hl in scheme.execute(
            "select parent_nucid, parent_hl_sec from ensdf_datasets"
            " where parent_nucid is not null"):
        par[nucid.upper()].add(stable(hl))
    compare(u"4в. `ensdf_datasets.parent_hl_sec` — период РОДИТЕЛЯ набора",
            par, u"⛔ изомер родителя в `parent_nucid` НЕ помечен: у Ag-110 и"
                 u" Ag-110m там одинаковое «110AG», различие только в тексте"
                 u" `dsid` «(249.76 D)»")


# ═══════════════════════════════════════════════════════════════════════════
# ПАРА 5 — привязка гамма к уровням
# ═══════════════════════════════════════════════════════════════════════════
def pair5(nuc, scheme, sym, tol_kev, worst_n):
    w(u"")
    w(u"══ ПАРА 5. Привязка гамма к уровням: `g4_gamma` против `ensdf_gammas`")
    w(u"")
    w(u"Правило: нумерации НЕЗАВИСИМЫЕ, поэтому сперва строится соответствие")
    w(u"уровней ПО ЭНЕРГИИ (монотонное жадное, допуск max(%.2f кэВ, 0.05 %%)),",
      tol_kev)
    w(u"измеряется доля сопоставленных уровней — и только потом сверяется")
    w(u"сама привязка переходов.")
    w(u"")

    zof = {}
    for nucid, z, n in nuc.execute(
            "select nucid, z, n from nuclides where l_seqno = 0"):
        zof[nucid.upper()] = (z, z + n)

    g4_levels = collections.defaultdict(list)
    for z, a, seq, e in scheme.execute(
            "select z, a, seq, energy_ev from g4_level"):
        g4_levels[(z, a)].append((e / 1000.0, seq))
    for v in g4_levels.values():
        v.sort()

    # ⛔ Отбор по интенсивности ОБЪЯВЛЕН и считается в обе стороны: он
    # выбрасывает 10.8 % переходов Geant4 и меняет заглавное число впятеро
    # (см. шапку, ПАРА 5). Ниже сверка идёт двумя наборами.
    g4_all = collections.defaultdict(list)
    g4_pos = collections.defaultdict(list)
    n_all = n_pos = 0
    for z, a, f, t, e, ipm in scheme.execute(
            "select z, a, from_seq, to_seq, energy_ev, intensity_ppm"
            " from g4_gamma"):
        g4_all[(z, a)].append((f, t, e / 1000.0))
        n_all += 1
        if ipm is not None and ipm > 0:
            g4_pos[(z, a)].append((f, t, e / 1000.0))
            n_pos += 1

    ds_levels = collections.defaultdict(list)
    for did, seq, e in scheme.execute(
            "select dataset_id, seq, energy_kev from ensdf_levels"
            " where energy_kev is not null"):
        ds_levels[did].append((e, seq))
    for v in ds_levels.values():
        v.sort()

    ds_nucid = dict(scheme.execute("select id, nucid from ensdf_datasets"))

    def tol(e):
        return max(tol_kev, 0.0005 * abs(e))

    lv_total = lv_matched = 0
    ds_used = ds_nonuc = ds_nog4 = 0
    maps = {}
    for did, levels in ds_levels.items():
        za = zof.get(ds_nucid.get(did, u"").upper())
        if za is None:
            ds_nonuc += 1
            continue
        gl = g4_levels.get(za)
        if not gl:
            ds_nog4 += 1
            continue
        ds_used += 1
        mp = {}
        j = 0
        for e, seq in levels:
            lv_total += 1
            best = None
            k = j
            while k < len(gl) and gl[k][0] <= e + tol(e):
                d = abs(gl[k][0] - e)
                if d <= tol(e) and (best is None or d < best[0]):
                    best = (d, k)
                k += 1
            if best is not None:
                mp[seq] = gl[best[1]][1]
                lv_matched += 1
                j = best[1] + 1
        maps[did] = (za, mp)

    w(u"наборов ENSDF разобрано: %d (нуклид не найден в nucdb: %d;"
      u" нуклида нет у Geant4: %d)", ds_used, ds_nonuc, ds_nog4)
    w(u"уровней ENSDF с энергией: %d, СОПОСТАВЛЕНО %d — %.1f %%",
      lv_total, lv_matched, 100.0 * lv_matched / max(1, lv_total))

    # Сверка идёт ДВАЖДЫ — по всем переходам Geant4 и только по переходам с
    # ненулевой интенсивностью. Отбор выбрасывает 10.8 % и меняет заглавное
    # число впятеро, поэтому печатаются ОБА столбца, а не выбранный.
    def compare(trans_by_za):
        same = other_pair = no_energy = 0
        unmapped = noseq = 0
        worst = []
        for did, (za, mp) in maps.items():
            trans = trans_by_za.get(za, ())
            for f, t, e in scheme.execute(
                    "select from_level_seq, to_level_seq, energy_kev"
                    " from ensdf_gammas where dataset_id = ? and energy_kev"
                    " is not null", (did,)):
                if f is None or t is None:
                    noseq += 1
                    continue
                if f not in mp or t not in mp:
                    unmapped += 1
                    continue
                gf, gt = mp[f], mp[t]
                hit = [x for x in trans if x[0] == gf and x[1] == gt]
                if hit:
                    same += 1
                    continue
                near = [x for x in trans if abs(x[2] - e) <= tol(e)]
                if near:
                    other_pair += 1
                    if len(worst) < 4 * worst_n:
                        worst.append((ds_nucid[did], e, (gf, gt),
                                      [(x[0], x[1]) for x in near[:3]]))
                else:
                    no_energy += 1
        return same, other_pair, no_energy, noseq, unmapped, worst

    a_same, a_other, a_none, noseq, unmapped, worst = compare(g4_all)
    p_same, p_other, p_none, _, _, _ = compare(g4_pos)
    a_tot = a_same + a_other + a_none
    p_tot = p_same + p_other + p_none

    w(u"")
    w(u"переходов Geant4 всего %d, из них с ненулевой интенсивностью %d"
      u" (отбор выбрасывает %d, %.1f %%)",
      n_all, n_pos, n_all - n_pos, 100.0 * (n_all - n_pos) / max(1, n_all))
    w(u"гамма ENSDF с обоими концами в соответствии: %d", a_tot)
    w(u"    у ENSDF нет номера уровня (from/to NULL):        %7d", noseq)
    w(u"    конец не попал в соответствие уровней:           %7d", unmapped)
    w(u"")
    w(u"                                                  ВСЕ переходы    "
      u"только intensity_ppm > 0")
    if a_tot and p_tot:
        w(u"    привязка СОВПАЛА:                       %7d %5.1f %%   %7d %5.1f %%",
          a_same, 100.0 * a_same / a_tot, p_same, 100.0 * p_same / p_tot)
        w(u"    Geant4 кладёт ту же энергию МЕЖДУ ДРУГИМИ:%7d %5.1f %%   %7d %5.1f %%",
          a_other, 100.0 * a_other / a_tot, p_other, 100.0 * p_other / p_tot)
        w(u"    перехода такой энергии НЕ НАШЛОСЬ:      %7d %5.1f %%   %7d %5.1f %%",
          a_none, 100.0 * a_none / a_tot, p_none, 100.0 * p_none / p_tot)
    w(u"")
    w(u"⛔ Разница столбцов — %d гамма, у которых переход в `g4_gamma` ЕСТЬ,"
      u" но с нулевой", p_none - a_none)
    w(u"   интенсивностью. Поэтому «нет вовсе» верно ТОЛЬКО для левого"
      u" столбца; для правого")
    w(u"   читается «нет среди переходов с ненулевой интенсивностью».")
    w(u"")
    w(u"примеры расхождения привязки (нуклид, энергия, пара Geant4 по"
      u" соответствию, что нашлось по энергии; столбец ВСЕ переходы):")
    for nucid, e, pr, near in worst[:worst_n]:
        w(u"    %-8s %9.3f кэВ  ожидалось %s, по энергии %s",
          nucid, e, pr, near)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--matdb", default=DEFAULT_MATDB)
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--pair", default="2,3,4,5")
    p.add_argument("--worst", type=int, default=10)
    p.add_argument("--level-tol", type=float, default=0.4)
    a = p.parse_args()
    want = set(x.strip() for x in a.pair.split(","))

    mat, nuc, scheme = ro(a.matdb), ro(a.nucdb), ro(a.schemedb)
    sym = symbols(nuc)

    w(u"# D23: расхождения между копиями одной величины — четыре пары")
    w(u"")
    w(u"Пара «`xcom_cross_sections` против `epics_photo_*`» здесь НЕ считается:")
    w(u"закрыта как D24 инструментом `tools/nucdb/compare_photo.py` — 1145")
    w(u"узлов, 97.8 % сходятся до 1 %, медиана 0.070 %.")

    if "2" in want:
        pair2(mat, sym, a.worst)
    if "3" in want:
        pair3(mat, scheme, nuc, sym, a.worst)
    if "4" in want:
        pair4(nuc, scheme, sym, a.worst)
    if "5" in want:
        pair5(nuc, scheme, sym, a.level_tol, a.worst)
    OUT.flush()


if __name__ == "__main__":
    main()
