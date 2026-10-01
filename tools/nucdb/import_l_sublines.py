# -*- coding: utf-8 -*-
u"""`AMBER151` (вторая половина): разложить сводную строку L-рентгена на подлинии.

⛔ ЧТО ЗДЕСЬ ПРОИСХОДИТ. Поставка выходов (`decay_radiations`, веб-выгрузка NuDat 3,
`database/scheme.md` §2) держит L-серию рентгена ОДНОЙ строкой `type_c = 'L'` —
центр тяжести и суммарный выход (у Am-241 «L 17.136 кэВ 36.64 %» вместо Np Lℓ 11.9,
Lα 13.9, Lβ 17.8, Lγ 20.8). Образ группы от этого у́же и выше данных, а на CdTe/CZT
вместо трёх пиков — один. Подлиний нет ни в NuDat 3 (одна строка «XR l»), ни в ENSDF
(рентген в записи `G` не пишется) — их надо СЧИТАТЬ из атомных данных. Решение Amber
01.10.2026 вопросником, дословно: «Считать из атомных данных (Рекомендую)».

ИСТОЧНИКИ — ТОЛЬКО ИЗ ДЕРЕВА, сеть не нужна:

  * `matdb.eadl_radiative` (EADL) — вероятности и энергии радиационных переходов на
    вакансию L1/L2/L3 (обозначения EADL 3/5/6): из них — ДОЛИ линий внутри подоболочки
    и энергии подлиний;
  * `matdb.fluorescence_yield` (xraylib, Krause с заменами Campbell) — ω_L1, ω_L2, ω_L3 и
    ω_K; `matdb.coster_kronig` (xraylib) — f12, f13, f23. Это ТОТ ЖЕ выбор, что у счёта
    матриц по умолчанию (`ResponseMatrixOptions.LYieldSupply = 2`); элемента в поставке
    xraylib нет — EADL (сумма `eadl_radiative` по вакансии, переходы `eadl_auger`), как
    у `MaterialDatabase.OmegaLAt`;
  * `matdb.eadl_radiative` + `eadl_auger` по вакансии K — сколько L-вакансий каждой
    подоболочки рождает заполнение одной K-дырки (Kα2 → L2, Kα1 → L3, оже KLL → две,
    KLX → одна);
  * `schemedb.g4_gamma` (PhotonEvaporation) — полный α и доля L у перехода, его
    мультипольность и δ; `matdb.icc_coefficients` (ЛСРМ, BrIcc) — α_L1 : α_L2 : α_L3 той
    же мультипольности (лог-лог по энергии, смесь (α₁ + δ²α₂)/(1 + δ²) — правило
    `CascadeAtomicData.IccGrid`).

СЧЁТ — на каждую строку `L` (родитель, уровень, канал `dec_type`):

  1. дочерние атомы — из `decay_chain` (ветви родителя); у родителя с ОДНОЙ строкой `L`
     в неё идут все ветви (у Eu-152 строка `L` стоит на канале EC, а центр 6.354 кэВ —
     между Sm и Gd: β⁻-ветвь своей строки не имеет), при нескольких строках — каждая
     берёт свой канал;
  2. первичные вакансии подоболочек L_i на распад:
       * от K-вакансий: N_K = I_K / ω_K (I_K — Kα1 + Kα2 + Kβ поставки, Kβ — итог `KB`,
         иначе разложение; K-строка отдаётся атому ветви по доле от K-края, правило
         `CascadeAtomicData.SplitKLines`), умножить на выход L_i на K-дырку (EADL);
       * от конверсии: Σ_T I_γ(T) · α(T) · доля_L(T) · α_Li/α_L (сопоставление линии
         с переходом — правило `CascadeAtomicData.MatchTransition`: ±0.6 кэВ, переход с
         ненулевой интенсивностью, низший уровень, затем ближайший);
       * от захвата на L: ТОЛЬКО у канала EC и только ОСТАТКОМ — выход поставки минус
         посчитанный (1) и (2), отнесённый к L1 (разрешённый захват идёт с s-оболочек;
         L2-захват у тяжёлых ~5…10 % от L1 — не учтён, назван);
  3. Костер—Крониг: n2' = n2 + f12·n1, n3' = n3 + f13·n1 + f23·n2';
  4. выход линии L_i—X: n_i' · ω_i · P(L_i→X) / Σ_X P(L_i→X) (EADL);
  5. линии слабее `MIN_SHARE` (0.2 %) от суммы L выбрасываются, остальные НОРМИРУЮТСЯ
     на выход строки `L` поставки: Σ подлиний = итог `L` (выход нуклида сохраняется).

ЧТО НЕ ДЕЛАЕТСЯ — и печатается поимённо: строка `L` ниже `MIN_KEV` (1 кэВ — ни один
прибор корпуса не видит; это L-серия Z < 30); родитель, у которого уже есть подробные
L-строки поставки (225RA, 225RN, 229TH); родитель, у которого посчитать нечего (нет ни
K-вакансий, ни сопоставленной конверсии, и канал не EC); нет атомных данных у Z;
**атом не тот** — Lℓ (L3-M1) подлиний дальше `MAX_LL_DEV` (1.5 %) от нижнего края
диапазона текста строки `L` поставки («11.870 - 22.402» — от Lℓ до верхней линии) или
больше `MAX_OUTSIDE` (5 %) выхода подлиний вне диапазона ±2 %. Положительный контроль
(П202): при верном Z отклонение Lℓ ≤ 0.76 % у 99 % строк, при дочернем Z ± 1 — больше
1 % у 90…94 %.
Не разложилась хоть одна строка `L` родителя — не раскладывается ни одна: читатель
выбирает подробные L-строки РОДИТЕЛЯ целиком (`FsaSampleLibrary.DecayLines`, `AMBER68`),
и половинное разложение потеряло бы вторую сводную строку.

⛔ СТРОКА ПОСТАВКИ НЕ ТРОГАЕТСЯ — ОНА ВЫТЕСНЯЕТСЯ ЧИТАТЕЛЕМ. Подлинии пишутся рядом, с
именами подоболочечных переходов, как у трёх подробных родителей поставки (`L3M5`,
`L2M4`, `L1N3`…); сводная `L` остаётся, и отпечаток строк поставки (`dr_pk` < 100001)
читается до и после. Предпочтение «подробные, иначе итоги подоболочек, иначе сводная»
уже стоит у всех читателей: `FsaSampleLibrary.DecayLines` (`AMBER68`), редактор базы
(`NucBaseFramework`: сводная получает пометку ∑ и галочку снятой), оснастка корпуса
(`chains.drop_superseded_l`). Так выбор обратим (`--apply` снимает свои строки и пишет
заново) и не путает провенанс: свои строки — `dr_pk` от `OWN_PK_BASE` (200001).

ПОРЯДОК. Конверсия считается по ВСЕМ гамма-строкам базы, включая добранные
`import_weak_gammas.py` (у Am-241 33.2 кэВ — 0.126 % γ при α = 185, это 17 % L-вакансий):
сперва добор слабых линий, потом этот скрипт. `import_weak_gammas.py --apply` своих строк
этого скрипта не трогает (снимает только `dr_pk` от 100001 до 199999).

⛔ ЗАПИСЬ — ПО РЕШЕНИЮ AMBER 01.10.2026 ВОПРОСНИКОМ, дословно: «Разрешаю писать агенту»
(`AMBER151`). `--apply` ВЫКЛЮЧЕН по умолчанию: без него база открыта `mode=ro`.

    python tools/nucdb/import_l_sublines.py                     # числа, без записи
    python tools/nucdb/import_l_sublines.py --list 241AM,210PB  # построчно
    python tools/nucdb/import_l_sublines.py --apply             # запись в --nucdb
"""
import argparse
import hashlib
import io
import math
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_MATDB = os.path.join(ROOT, "BecquerelMonitor", "matdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite")

OWN_PK_BASE = 200001     # dr_pk своих строк; у добора слабых гамм — 100001…199999
WEAK_PK_BASE = 100001    # строки ниже — поставка
MIN_KEV = 1.0            # строка `L` ниже — не раскладывается (Z < 30, приборы не видят)
MIN_SHARE = 0.002        # линия слабее этой доли суммы L — не пишется (остальные нормируются)
MAX_OUTSIDE = 0.05       # доля выхода подлиний вне диапазона текста строки `L` (±2 %) больше — не раскладывать
MAX_LL_DEV = 0.015       # Lℓ подлиний дальше от нижнего края диапазона строки `L` — не раскладывать
MATCH_KEV = 0.6          # сопоставление гамма-линии с переходом, как `CascadeAtomicData.MatchKev`
K_SHARES = (0.844, 0.855, 0.969)   # Kα2, Kα1, Kβ1 от K-края — `CascadeAtomicData.KLineShares`
EC_CODES = ("1", "7")

# Обозначения подоболочек EADL (`eadl_binding.shell_id`).
SHELL = {1: "K", 3: "L1", 5: "L2", 6: "L3", 8: "M1", 10: "M2", 11: "M3", 13: "M4", 14: "M5",
         16: "N1", 18: "N2", 19: "N3", 21: "N4", 22: "N5", 24: "N6", 25: "N7",
         27: "O1", 29: "O2", 30: "O3", 32: "O4", 33: "O5", 35: "O6", 36: "O7",
         41: "P1", 43: "P2", 44: "P3", 46: "P4", 47: "P5", 58: "Q1"}
L_IDS = (3, 5, 6)

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


def connect(path, write=False):
    if not os.path.isfile(path):
        sys.exit(u"нет базы: %s" % path)
    uri = "file:%s%s" % (path.replace(chr(92), "/"), "" if write else "?mode=ro")
    return sqlite3.connect(uri, uri=True)


def num_text(x):
    u"""Число записью без экспоненты и группировки, 4 значащих (как у поставки и добора)."""
    if x == 0:
        return "0"
    digits = max(0, 3 - int(math.floor(math.log10(abs(x)))))
    return ("%." + str(digits) + "f") % x


def energy_text(e):
    u"""Энергия, кэВ, — до эВ (EADL даёт энергии кванта с точностью до сотых эВ)."""
    return "%.4f" % e


def siegbahn(name):
    u"""Группа Зигбана подоболочечного перехода: ℓ, η, α, β, γ (для сводок)."""
    vac, src = name[:2], name[2:]
    if vac == "L3":
        return u"ℓ" if src == "M1" else (u"α" if src in ("M4", "M5") else u"β")
    if vac == "L2":
        return u"η" if src == "M1" else (u"β" if src.startswith("M") else u"γ")
    return u"β" if src.startswith("M") else u"γ"


class Atoms(object):
    u"""Атомные данные из `matdb` — по Z, лениво."""

    def __init__(self, mat):
        self.mat = mat
        self.cache = {}
        self.ck_supply = {}
        for z, t, p in mat.execute("select z, transition, probability from coster_kronig where source = 'xraylib'"):
            self.ck_supply.setdefault(z, {})[t] = p
        self.omega = {}
        for z, sh, om, src in mat.execute("select z, shell, omega, source from fluorescence_yield"):
            d = self.omega.setdefault(z, {})
            if src == "xraylib" or (sh, "xraylib") not in d:
                d[(sh, src)] = om

    def omega_of(self, z, shell):
        d = self.omega.get(z, {})
        for src in ("xraylib", "xraydb"):
            v = d.get((shell, src))
            if v is not None and v > 0:
                return v, src
        return None, None

    def of(self, z):
        if z in self.cache:
            return self.cache[z]
        m = self.mat
        binding = dict((s, e / 1000.0) for s, e in m.execute("select shell_id, binding_ev from eadl_binding where z = ?", (z,)))
        rad = {}
        for vac, frm, p, e in m.execute(
                "select vacancy_shell, from_shell, probability, energy_ev from eadl_radiative where z = ?", (z,)):
            rad.setdefault(vac, []).append((frm, p, e / 1000.0))
        aug = {}
        for vac, frm, ej, p in m.execute(
                "select vacancy_shell, from_shell, ejected_shell, probability from eadl_auger where z = ?", (z,)):
            aug.setdefault(vac, []).append((frm, ej, p))
        if not all(rad.get(i) for i in L_IDS):
            self.cache[z] = None
            return None
        a = {"z": z, "binding": binding, "lines": {}, "omega": {}, "omega_src": {}}
        for i in L_IDS:
            omega_eadl = sum(p for f, p, e in rad[i])
            # Радиационные переходы ВНУТРИ L (L1—L3, 2…5 кэВ) — в сводную строку `L`
            # поставки не входят (её диапазон начинается с Lℓ) и не пишутся; доли
            # нормируются на оставшиеся.
            keep = [(f, p, e) for f, p, e in rad[i] if f in SHELL and f not in (1, 3, 5, 6) and e > 0]
            tot = sum(p for f, p, e in keep)
            a["lines"][i] = [(SHELL[i] + SHELL[f], e, p / tot) for f, p, e in keep]
            om, src = self.omega_of(z, SHELL[i])
            a["omega"][i] = om if om is not None else omega_eadl
            a["omega_src"][i] = src or "eadl"
        # Костер—Крониг: xraylib, иначе EADL (сумма `eadl_auger` по (L1←L2), (L1←L3), (L2←L3)).
        ck = self.ck_supply.get(z)
        if ck:
            a["f12"], a["f13"], a["f23"] = ck.get("f12", 0.0), ck.get("f13", 0.0), ck.get("f23", 0.0)
            a["ck_src"] = "xraylib"
        else:
            a["f12"] = sum(p for f, ej, p in aug.get(3, []) if f == 5)
            a["f13"] = sum(p for f, ej, p in aug.get(3, []) if f == 6)
            a["f23"] = sum(p for f, ej, p in aug.get(5, []) if f == 6)
            a["ck_src"] = "eadl"
        # L-вакансии на одну K-дырку (EADL): радиационный K-L_i — одна, оже — по
        # каждой L-подоболочке среди «откуда» и «вылетел».
        perk = dict((i, 0.0) for i in L_IDS)
        for f, p, e in rad.get(1, []):
            if f in perk:
                perk[f] += p
        for f, ej, p in aug.get(1, []):
            if f in perk:
                perk[f] += p
            if ej in perk:
                perk[ej] += p
        a["per_k"] = perk
        om_k, src_k = self.omega_of(z, "K")
        a["omega_k"] = om_k if om_k is not None else sum(p for f, p, e in rad.get(1, []))
        self.cache[z] = a
        return a


class Icc(object):
    u"""α_L1 : α_L2 : α_L3 по `icc_coefficients` (variant 1) — правило `CascadeAtomicData.IccGrid`."""

    COLS = ("e1", "e2", "e3", "e4", "m1", "m2", "m3", "m4")

    def __init__(self, mat):
        self.mat = mat
        self.grid = {}

    def table(self, z, shell):
        key = (z, shell)
        if key not in self.grid:
            self.grid[key] = self.mat.execute(
                "select energy_kev, e1, e2, e3, e4, m1, m2, m3, m4 from icc_coefficients"
                " where variant = 1 and z = ? and shell = ? order by energy_kev", (z, shell)).fetchall()
        return self.grid[key]

    @staticmethod
    def column(part):
        if part < 2:
            return None
        k = part // 2
        if k > 4:
            return None
        return 4 + k - 1 if part % 2 else k - 1

    def components(self, code, delta):
        u"""(колонка₁, колонка₂ или None, δ, был ли откат). Смесь без δ — младшая одна (откат)."""
        if code <= 0:
            return None
        hi = code // 100 if code >= 100 else code
        lo = code % 100 if code >= 100 else -1
        c1 = self.column(hi)
        if c1 is None:
            return None
        if lo < 0:
            return (c1, None, 0.0, False)
        c2 = self.column(lo)
        if c2 is None:
            return (c1, None, 0.0, True)
        if lo // 2 < hi // 2:
            c1, c2 = c2, c1
        if delta == 0.0:
            return (c1, None, 0.0, True)
        return (c1, c2, delta, False)

    def value(self, z, shell, e, col, edge):
        if e <= edge:
            return 0.0
        rows = self.table(z, shell)
        if not rows:
            return None
        xs = [r[0] for r in rows]
        if e <= xs[0]:
            return rows[0][1 + col]           # между краем и первым узлом — первый узел
        if e >= xs[-1]:
            return rows[-1][1 + col]
        k = 1
        while xs[k] < e:
            k += 1
        y0, y1 = rows[k - 1][1 + col], rows[k][1 + col]
        if y0 <= 0 or y1 <= 0:
            return max(y0, y1, 0.0)
        t = (math.log(e) - math.log(xs[k - 1])) / (math.log(xs[k]) - math.log(xs[k - 1]))
        return math.exp(math.log(y0) + t * (math.log(y1) - math.log(y0)))

    def split(self, z, e, code, delta, edges):
        u"""Доли L1, L2, L3 в L-конверсии; None — сказать нечего. Второе — был ли откат."""
        comp = self.components(code, delta)
        fallback = False
        if comp is None:
            comp = (4, None, 0.0, True)       # мультипольность не известна — M1 (назван откат)
        c1, c2, d, fallback = comp
        out = []
        for sh, edge in zip(("L1", "L2", "L3"), edges):
            a1 = self.value(z, sh, e, c1, edge)
            if a1 is None:
                return None, fallback
            if c2 is not None:
                a2 = self.value(z, sh, e, c2, edge)
                if a2 is None:
                    return None, fallback
                a1 = (a1 + d * d * a2) / (1.0 + d * d)
            out.append(a1)
        s = sum(out)
        if not (s > 0):
            return None, fallback
        return [x / s for x in out], fallback


def nucid_za(nucid, nz):
    m = re.match(r"^(\d+)", nucid)
    return (nz.get(nucid), int(m.group(1)) if m else None)


def plan(nuc, mat, sch):
    atoms = Atoms(mat)
    icc = Icc(mat)
    nz = {}
    for nucid, z in nuc.execute("select nucid, z from nuclides where z is not null"):
        nz.setdefault(nucid, int(z))
    chain = {}
    for nucid, lseq, d, dt, perc in nuc.execute("select nucid, l_seqno, daughter_nucid, dec_type, perc from decay_chain"):
        chain.setdefault((nucid, lseq), []).append((d, dt))

    lrows = nuc.execute(
        "select parent_nucid, parent_l_seqno, dec_type, energy_num, intensity_num, dr_pk, energy from decay_radiations"
        " where type_a = 'X' and type_c = 'L' and dr_pk < ? and intensity_num > 0 order by dr_pk",
        (WEAK_PK_BASE,)).fetchall()
    by_parent = {}
    for r in lrows:
        by_parent.setdefault((r[0], r[1]), []).append(r)

    detailed = set()
    for pn, pl in nuc.execute(
            "select distinct parent_nucid, parent_l_seqno from decay_radiations where type_a = 'X'"
            " and type_c like 'L%' and type_c != 'L' and dr_pk < ?", (WEAK_PK_BASE,)):
        detailed.add((pn, pl))

    g4cache = {}

    def g4(z, a):
        if (z, a) not in g4cache:
            g4cache[(z, a)] = sch.execute(
                "select from_seq, energy_ev, multipolarity, mixing_ratio, icc_total, icc_l_ppm from g4_gamma"
                " where z = ? and a = ? and intensity_ppm > 0", (z, a)).fetchall()
        return g4cache[(z, a)]

    out_rows, report, skipped = [], [], []
    for (pn, pl), rows in sorted(by_parent.items()):
        if (pn, pl) in detailed:
            skipped.append((pn, pl, u"у поставки уже есть подробные L-строки — не раскладывается"))
            continue
        if any(r[3] is None or r[3] < MIN_KEV for r in rows):
            skipped.append((pn, pl, u"строка L ниже %.0f кэВ — не раскладывается" % MIN_KEV))
            continue
        # Ветви изомера `decay_chain` держит под ИМЕНЕМ ОСНОВНОГО состояния и уровнем
        # изомера (`100AGm` ур. 1 → `100AG` ур. 1): метка состояния — строчные буквы.
        base_n = re.sub(r"[a-z]+\d*$", "", pn)
        # Ветви одного уровня бывают разнесены по ОБОИМ именам: у `234PAm1` ур. 2 β⁻ лежит
        # под `234PAm1`, а ИП (0.16 %) — под `234PA` ур. 2. Берётся объединение.
        branches = []
        for key in ((pn, pl), (base_n, pl)):
            for br in chain.get(key, []):
                if br not in branches:
                    branches.append(br)
        if not branches:
            branches = list(chain.get((pn, 0)) or chain.get((base_n, 0)) or [])
        result = []
        fail = None
        for (_, _, dt, e_l, i_l, pk, e_text) in rows:
            own = [b for b in branches if b[1] == dt] if len(rows) > 1 else list(branches)
            if not own:
                fail = u"канал %s строки L: нет ветви в decay_chain" % dt
                break
            atom_list = []
            for d, bdt in own:
                z, a = nucid_za(d, nz)
                at = atoms.of(z) if z else None
                if at is None:
                    continue
                atom_list.append((d, bdt, z, a, at))
            if not atom_list:
                fail = u"канал %s: нет атомных данных L у дочерних (%s)" % (dt, ",".join(d for d, b in own))
                break
            # K-строки родителя этих каналов → атомам по доле от K-края.
            codes = set(b for d, b, z, a, at in atom_list) | {dt}
            krows = nuc.execute(
                "select type_c, energy_num, intensity_num, dec_type from decay_radiations where parent_nucid = ?"
                " and parent_l_seqno is ? and type_a = 'X' and type_c like 'K%' and intensity_num > 0",
                (pn, pl)).fetchall()
            kin = {}
            for tc, e, i, kdt in krows:
                if kdt not in codes or e is None:
                    continue
                best, bd = None, None
                for d, bdt, z, a, at in atom_list:
                    edge = at["binding"].get(1)
                    if not edge or e > edge:
                        continue
                    dd = min(abs(e - s * edge) for s in K_SHARES)
                    if bd is None or dd < bd:
                        best, bd = d, dd
                if best is None:
                    best = atom_list[0][0]
                kin.setdefault(best, {}).setdefault(tc.strip(), 0.0)
                kin[best][tc.strip()] += i
            lines = {}
            info = {"n_k": 0.0, "conv": 0.0, "ec": 0.0, "fallback": 0.0, "unmatched": 0, "pred": 0.0}
            pred_total = 0.0
            per_atom = []
            for d, bdt, z, a, at in atom_list:
                n = dict((i, 0.0) for i in L_IDS)
                kk = kin.get(d, {})
                kb = kk.get("KB")
                if kb is None:
                    kb = kk.get("KpB1", 0.0) + kk.get("KpB2", 0.0)
                i_k = kk.get("KA1", 0.0) + kk.get("KA2", 0.0) + kb
                n_k = i_k / at["omega_k"] if at["omega_k"] > 0 else 0.0
                info["n_k"] += n_k
                for i in L_IDS:
                    n[i] += n_k * at["per_k"][i] / 100.0
                edges = [at["binding"].get(3, 1e9), at["binding"].get(5, 1e9), at["binding"].get(6, 1e9)]
                gam = nuc.execute(
                    "select energy_num, intensity_num from decay_radiations where parent_nucid = ?"
                    " and parent_l_seqno is ? and type_a = 'G' and dec_type = ? and energy_num > 0 and intensity_num > 0",
                    (pn, pl, bdt)).fetchall()
                trans = g4(z, a)
                for e, ig in gam:
                    best = None
                    for t in trans:
                        de = abs(t[1] / 1000.0 - e)
                        if de >= MATCH_KEV:
                            continue
                        if best is None or t[0] < best[0][0] or (t[0] == best[0][0] and de < best[1]):
                            best = (t, de)
                    if best is None:
                        info["unmatched"] += 1
                        continue
                    t = best[0]
                    alpha_l = t[4] * (t[5] or 0) / 1e6
                    if not (alpha_l > 0):
                        continue
                    nl = ig / 100.0 * alpha_l
                    sp, fb = icc.split(z, e, t[2], t[3], edges)
                    if sp is None:
                        continue
                    info["conv"] += nl
                    if fb:
                        info["fallback"] += nl
                    for k, i in enumerate(L_IDS):
                        n[i] += nl * sp[k]
                per_atom.append([d, z, at, n])
            # Костер—Крониг и выход.
            def emit(at, n):
                n1 = n[3]
                n2 = n[5] + at["f12"] * n1
                n3 = n[6] + at["f13"] * n1 + at["f23"] * n2
                res = {}
                for i, ni in ((3, n1), (5, n2), (6, n3)):
                    for name, e, fr in at["lines"][i]:
                        res[name] = (e, res.get(name, (e, 0.0))[1] + ni * at["omega"][i] * fr)
                return res

            for item in per_atom:
                item.append(emit(item[2], item[3]))
                pred_total += sum(v[1] for v in item[4].values())
            supply = i_l / 100.0
            # Захват на L — остатком, только у EC-канала, к L1 атома канала строки.
            ec_atom = next((it for it in per_atom if it[0] in [d for d, b in own if b in EC_CODES]), None)
            if dt in EC_CODES and ec_atom is not None and supply > pred_total:
                at = ec_atom[2]
                y1 = at["omega"][3] + at["f12"] * at["omega"][5] + (at["f13"] + at["f12"] * at["f23"]) * at["omega"][6]
                if y1 > 0:
                    r = (supply - pred_total) / y1
                    ec_atom[3][3] += r
                    info["ec"] = r
                    ec_atom[4] = emit(at, ec_atom[3])
                    pred_total = sum(sum(v[1] for v in it[4].values()) for it in per_atom)
            info["pred"] = pred_total
            if not (pred_total > 0):
                fail = u"канал %s: посчитать нечего — нет K-вакансий, нет сопоставленной конверсии, не EC" % dt
                break
            merged = {}
            for d, z, at, n, res in per_atom:
                sym = re.sub(r"^\d+", "", d)
                for name, (e, y) in res.items():
                    key = (name if len(per_atom) == 1 else name, round(e, 4))
                    merged[key] = merged.get(key, 0.0) + y
            tot = sum(merged.values())
            kept = [(k[0], k[1], y) for k, y in merged.items() if y >= MIN_SHARE * tot]
            ktot = sum(y for _, _, y in kept)
            emitted = [(name, e, i_l * y / ktot) for name, e, y in kept]
            centroid = sum(e * y for _, e, y in emitted) / sum(y for _, _, y in emitted)
            # Сверка атома: доля выхода подлиний ВНЕ диапазона текста строки `L` поставки
            # («11.870 - 22.402»; её `energy_num` — СЕРЕДИНА диапазона, а не центр
            # тяжести, поэтому сверять с ней центр нельзя). Допуск 2 %.
            rng = re.findall(r"\d+\.?\d*", e_text or "")
            outside = None
            if len(rng) >= 2:
                lo, hi = float(rng[0]), float(rng[1])
                outside = sum(y for _, e, y in emitted if e < 0.98 * lo or e > 1.02 * hi) / i_l
            info["outside"] = outside
            # Сверка атома по ЯКОРЮ: нижний край диапазона поставки — Lℓ (L3-M1) того же
            # атома. Замер П202: при верном Z отклонение ≤ 0.76 % у 99 % строк, при Z ± 1
            # — больше 1 % у 90…94 % (сам диапазон ±2 % этого не ловит: линии L-серии
            # разбросаны на десяток кэВ, и сдвиг атома на 4 % оставляет их внутри).
            ll = [e for name, e, y in emitted if name == "L3M1"]
            if len(rng) >= 2 and ll:
                ll_dev = min(abs(e - lo) / lo for e in ll)
                info["ll_dev"] = ll_dev
                if ll_dev > MAX_LL_DEV:
                    fail = (u"канал %s: Lℓ подлиний на %.1f %% от нижнего края «%s» поставки — атом не тот"
                            % (dt, 100 * ll_dev, e_text))
                    break
            if outside is not None and outside > MAX_OUTSIDE:
                fail = (u"канал %s: %.1f %% выхода подлиний вне диапазона строки L поставки «%s» — атом не тот"
                        % (dt, 100 * outside, e_text))
                break
            result.append((pn, pl, dt, pk, e_l, i_l, emitted, centroid, info,
                           [(d, z) for d, z, at, n, res in per_atom]))
        if fail:
            skipped.append((pn, pl, fail))
            continue
        for r in result:
            report.append(r)
            for name, e, y in r[6]:
                out_rows.append((r[1], r[0], r[2], name, e, y))
    return out_rows, report, skipped


def fingerprint(con):
    h = hashlib.sha256()
    n = 0
    for row in con.execute("select * from decay_radiations where dr_pk < ? order by dr_pk", (OWN_PK_BASE,)):
        h.update(repr(row).encode("utf-8"))
        n += 1
    return n, h.hexdigest()[:16]


def main():
    p = argparse.ArgumentParser(description=u"AMBER151: L-подлинии из атомных данных вместо сводной строки L")
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--matdb", default=DEFAULT_MATDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--apply", action="store_true", help=u"ЗАПИСЬ в --nucdb, в транзакции")
    p.add_argument("--list", default="", help=u"родители через запятую — построчно")
    p.add_argument("--corpus", action="store_true", help=u"сводка по нуклидам корпуса")
    p.add_argument("--all-skipped", action="store_true", help=u"печатать все пропуски")
    a = p.parse_args()

    nuc = connect(a.nucdb, a.apply)
    mat = connect(a.matdb)
    sch = connect(a.schemedb)
    w(u"nucdb: %s (%s)", a.nucdb, u"ЗАПИСЬ" if a.apply else u"mode=ro, без записи")
    n0, fp0 = fingerprint(nuc)
    own0 = nuc.execute("select count(*) from decay_radiations where dr_pk >= ?", (OWN_PK_BASE,)).fetchone()[0]
    sup = nuc.execute("select count(*) from decay_radiations where dr_pk < ?", (WEAK_PK_BASE,)).fetchone()[0]
    w(u"строк ниже dr_pk %d (поставка %d + добор слабых γ %d): отпечаток %s; своих строк до прогона: %d",
      OWN_PK_BASE, sup, n0 - sup, fp0, own0)

    rows, report, skipped = plan(nuc, mat, sch)
    nl = nuc.execute("select count(*) from decay_radiations where type_a = 'X' and type_c = 'L' and dr_pk < ?",
                     (WEAK_PK_BASE,)).fetchone()[0]
    w(u"строк `L` поставки: %d; разложено: %d (у %d родителей), подлиний к записи: %d; пропущено родителей: %d",
      nl, len(report), len(set((r[0], r[1]) for r in report)), len(rows), len(skipped))
    dev = sorted(r[8]["outside"] for r in report if r[8].get("outside") is not None)
    if dev:
        w(u"доля выхода подлиний вне диапазона строки `L` поставки (±2 %%), строк %d (у %d диапазона нет):"
          u" медиана %.2f %%, 95 %% — %.2f %%, максимум %.2f %%; строк с долей > 5 %%: %d",
          len(dev), len(report) - len(dev), 100 * dev[len(dev) // 2], 100 * dev[int(0.95 * (len(dev) - 1))],
          100 * dev[-1], sum(1 for x in dev if x > 0.05))
        for r in report:
            if (r[8].get("outside") or 0) > 0.05:
                w(u"    вне диапазона %.1f %%: %s ур. %s канал %s, атомы %s", 100 * r[8]["outside"], r[0], r[1], r[2],
                  ",".join("%s" % x[0] for x in r[9]))
    ratio = sorted((r[5] / 100.0) / r[8]["pred"] for r in report if r[8]["ec"] == 0 and r[8]["pred"] > 0)
    if ratio:
        w(u"выход поставки / посчитанный (строки без остатка захвата, %d): медиана %.3f, 10…90 %% %.3f…%.3f",
          len(ratio), ratio[len(ratio) // 2], ratio[int(0.1 * (len(ratio) - 1))], ratio[int(0.9 * (len(ratio) - 1))])

    def show(r):
        pn, pl, dt, pk, e_l, i_l, em, cen, info, atoms = r
        groups = {}
        for name, e, y in em:
            g = siegbahn(name)
            ge, gy = groups.get(g, (0.0, 0.0))
            groups[g] = (ge + e * y, gy + y)
        gtxt = u", ".join(u"L%s %.2f кэВ %s %%" % (g, ge / gy, num_text(gy))
                          for g, (ge, gy) in sorted(groups.items(), key=lambda kv: kv[1][0] / kv[1][1]))
        w(u"--- %s ур. %s канал %s: L поставки %.3f кэВ %s %% → %d подлиний, центр %.3f кэВ; атомы %s",
          pn, pl, dt, e_l, num_text(i_l), len(em), cen, ",".join("%s(Z=%d)" % x for x in atoms))
        w(u"    вакансии на распад: от K %.4f (N_K), конверсия на L %.4f (откат мультипольности %.4f), захват на L1 %.4f;"
          u" несопоставленных γ %d; посчитанный выход L %.3f %% против %s %% поставки",
          info["n_k"] / 100.0, info["conv"], info["fallback"], info["ec"], info["unmatched"], 100 * info["pred"], num_text(i_l))
        w(u"    группы: %s", gtxt)
        for name, e, y in sorted(em, key=lambda x: x[1]):
            w(u"      %-6s %8.4f кэВ  %s %%  (L%s)", name, e, num_text(y), siegbahn(name))

    want = [x.strip() for x in a.list.split(",") if x.strip()]
    for r in report:
        if r[0] in want:
            show(r)
    for x in skipped:
        if x[0] in want:
            w(u"--- %s ур. %s: ПРОПУЩЕН — %s", x[0], x[1], x[2])
    if a.corpus:
        sys.path.insert(0, HERE)
        from missing_lines import corpus_nuclides
        cn = set(corpus_nuclides(nuc))
        w(u"нуклиды корпуса со строкой L:")
        for r in report:
            if r[0] in cn:
                show(r)
        for x in skipped:
            if x[0] in cn:
                w(u"--- %s ур. %s: ПРОПУЩЕН — %s", x[0], x[1], x[2])
    reasons = {}
    for x in skipped:
        key = re.sub(r"\(.*\)|\d+", "#", x[2])
        reasons[key] = reasons.get(key, 0) + 1
    w(u"пропуски по причинам:")
    for k, v in sorted(reasons.items(), key=lambda kv: -kv[1]):
        w(u"    %5d  %s", v, k)
    if a.all_skipped:
        for x in skipped:
            w(u"    %-9s ур. %s: %s", x[0], x[1], x[2])

    if not a.apply:
        w(u"⛔ ЗАПИСИ НЕ БЫЛО: база открыта `mode=ro`, ключ `--apply` не назван.")
        return 0

    cur = nuc.cursor()
    cur.execute("BEGIN")
    try:
        cur.execute("delete from decay_radiations where dr_pk >= ?", (OWN_PK_BASE,))
        for k, (plseq, pnucid, code, name, e, y) in enumerate(sorted(rows, key=lambda r: (r[1], r[0] if r[0] is not None else -1, r[2], r[4]))):
            cur.execute(
                "insert into decay_radiations (parent_l_seqno, parent_nucid, dec_type, type_a, type_c, intensity,"
                " intensity_unc, intensity_num, energy_num, energy, energy_unc, endpoint, endpoint_unc, dr_pk)"
                " values (?, ?, ?, 'X', ?, ?, null, ?, ?, ?, null, null, null, ?)",
                (plseq, pnucid, code, name, num_text(y), float(num_text(y)), float(energy_text(e)), energy_text(e),
                 OWN_PK_BASE + k))
        n1, fp1 = fingerprint(nuc)
        if (n1, fp1) != (n0, fp0):
            raise RuntimeError(u"строки ниже %d изменились: %d/%s → %d/%s" % (OWN_PK_BASE, n0, fp0, n1, fp1))
        own1 = cur.execute("select count(*) from decay_radiations where dr_pk >= ?", (OWN_PK_BASE,)).fetchone()[0]
        nuc.commit()
    except Exception:
        nuc.rollback()
        raise
    integrity = nuc.execute("PRAGMA integrity_check").fetchone()[0]
    w(u"ЗАПИСАНО: своих строк %d (было %d); строк ниже %d — %d, отпечаток %s — тот же; integrity_check %s",
      own1, own0, OWN_PK_BASE, n1, fp1, integrity)
    return 0 if integrity == "ok" else 1


if __name__ == "__main__":
    sys.exit(main())
