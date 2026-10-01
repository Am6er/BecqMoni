# -*- coding: utf-8 -*-
u"""`AMBER151`: добрать в `nucdb.decay_radiations` слабые гамма-линии, срезанные поставкой.

⛔ ЧТО ЗДЕСЬ ПРОИСХОДИТ. Поставка выходов (`decay_radiations`, веб-выгрузка NuDat 3,
февраль 2024, `database/scheme.md` §2) срезана по ОТНОСИТЕЛЬНОЙ интенсивности: у
каждого нуклида остались только линии с выходом ≥ 1 % от его сильнейшей (у
многолинейных наименьшая — 1.01…1.10 %). Bi-214 потерял так 212 линий из 235 на
10.5 % распадов, Ac-228 — 8.5 %, у Y-88 нет одиночной 2734.0 кэВ (0.715 %), у
Eu-152 — 1528.1 (0.28 %). Замер — `tools/nucdb/missing_lines.py` (П191).

ИСТОЧНИК — ЛОКАЛЬНЫЙ, ИЗ ДЕРЕВА: `schemedb.ensdf_gammas` (поставка ЛСРМ
`TCCFCALC\\LIB\\ENSDF2`, издание 2015, втянута `import_ensdf.py`). Сеть не нужна.
Там выходы ОТНОСИТЕЛЬНЫЕ (RI, запись `N` с NR·BR не втянута), поэтому норма на
распад берётся ЯКОРЕМ на саму поставку: для каждого набора распада (родитель,
мода) линии ENSDF сопоставляются с уже лежащими строками `decay_radiations` того
же родителя и того же канала (`dec_type`) по энергии, и множитель RI → % на распад
— медиана отношений I_nucdb / RI по сильным сопоставленным линиям. Так новая
слабая линия встаёт в ту же шкалу, что и действующие сильные (оценки 2011…2021),
а не в шкалу оценки ENSDF 1988…1999.

⚠ ЦЕНА ИСТОЧНИКА, назвать вслух: оценки ENSDF в `schemedb` — 1988…1999 (Y-88
1988, Bi-214 1995, Eu-152 1996), то есть ОТНОСИТЕЛЬНЫЕ выходы слабых линий — по
старой оценке. Подлиний L-серии в ENSDF нет вовсе (рентген в записи `G` не
пишется), поэтому вторая половина `AMBER151` (сводная `L` вместо Lℓ/Lα/Lβ/Lγ)
этим скриптом не закрывается — её закрывает `import_l_sublines.py` (расчёт из атомных
данных, П202), и звать его надо ПОСЛЕ этого скрипта.

ЧТО ДОБАВЛЯЕТСЯ — только то, что срезал порог:

  * линия ENSDF набора, у которого нашёлся якорь, НЕ сопоставленная ни одной
    строке поставки в пределах max(1.0 кэВ, 4·10⁻⁴·E);
  * её выход на распад меньше `ADD_REL_MAX` (1.2 %) от сильнейшей гаммы родителя
    в поставке: линия сильнее — поставка её НЕ срезала бы, значит её отсутствие
    имеет другую причину (сдвиг энергии в новой оценке, снятая линия), и она
    печатается списком «аномалий», а не добавляется;
  * рядом (±3 кэВ) нет НЕСОПОСТАВЛЕННОЙ строки поставки — иначе это, скорее
    всего, та же линия со сдвинутой энергией, и она тоже идёт в список;
  * она не ближе `DOUBLET_KEV` (0.1 кэВ) к СОПОСТАВЛЕННОЙ строке: ENSDF пишет
    дублет двумя записями одной энергии (Th-231 25.64/25.65, Th-227 250.27 дважды),
    а поставка — одной, и вторая запись уже сидит в ней;
  * выход на распад не ниже `--min-yield` — по умолчанию **0.01 %** (решение Amber 01.10.2026
    вопросником, дословно: «≥ 0.01 % после ускорения сумматора»); `--min-yield 0` — без порога.

Записи одного набора ближе `SAME_KEV` (0.05 кэВ) — одна строка: при равных выходах это
одна линия, размещённая в схеме дважды (берётся одна), при разных — дублет (выходы
складываются); правило то же, что у читателя `FsaSampleLibrary.DecayLines` (`S161`).

Набор распада подбирается к родителю по ПЕРИОДУ (|ln(T_ENSDF / T_nucdb)| ≤ ln
1.25), а не по номеру уровня: нумерация уровней ЛСРМ и NNDC — разные величины
(`database/scheme.md` §2), а у `109CD`/`207BI` наборы IT собственного изомера
(10.9 мкс, 182 мкс) иначе подменяли бы распад основного состояния. Наборы
задержанных частиц (`B-A`, `B-N`, `ECP`…) не берутся. Набор, у которого в
поставке НЕ НАШЛАСЬ линия ≥ `REJECT_REL` (10 %) от сильнейшей гаммы родителя,
описывает не тот распад (так у `91TCm`, `129INm2` — период совпал с чужим
изомером, «недостающие» линии выходили в 10…24 раза сильнее сильнейшей) и не
берётся целиком; набор с разбросом отношений якоря больше `ANCHOR_SPREAD_MAX`
(×1.5) — тоже; второй набор той же моды у того же родителя — тоже (двойной
счёт). Набор без якоря (у
родителя в этом канале нет ни одной строки поставки — так у α-ветви Bi-214
0.021 %) пропускается и печатается.

ПРОВЕНАНС. Новые строки получают `dr_pk` от `OWN_PK_BASE` (100001) и выше; у
поставки `dr_pk` ≤ 66290. `intensity_unc`, `energy_unc` пусты: в `schemedb` RI и
энергия лежат ЧИСЛОМ, а запись погрешности ENSDF — в единицах последнего знака
ТЕКСТА, которого уже нет; пересчитать её честно нечем. Текстовые `energy` и
`intensity` — запись числа инвариантно, без группировки разрядов.

ПОВТОРЯЕМОСТЬ. `--apply` сперва снимает строки `OWN_PK_BASE ≤ dr_pk < OWN_PK_END` (свои; от
200001 лежат L-подлинии `import_l_sublines.py` — они не снимаются, но считаны по гаммам
этого добора, поэтому после повторного добора их пересчитывают тем скриптом), затем
пишет заново — повторный прогон даёт ту же базу. Строки поставки (`dr_pk` <
`OWN_PK_BASE`) не меняются ни на бит: отпечаток sha256 всех их колонок печатается
до и после, и при расхождении транзакция откатывается.

⛔ ЗАПИСЬ — ПО РЕШЕНИЮ AMBER 01.10.2026 ВОПРОСНИКОМ, дословно: «Разрешаю писать
агенту» (`AMBER151`), после показа diff. `--apply` ВЫКЛЮЧЕН по умолчанию: без
него база открыта `mode=ro`.

    python tools/nucdb/import_weak_gammas.py                       # числа, без записи
    python tools/nucdb/import_weak_gammas.py --nucdb <копия> --apply
    python tools/nucdb/import_weak_gammas.py --list 88Y,152EU      # построчно по нуклидам
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
DEFAULT_SCHEMEDB = os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite")

OWN_PK_BASE = 100001        # dr_pk своих строк — от этого номера
OWN_PK_END = 200001         # и ниже этого: от 200001 — L-подлинии `import_l_sublines.py`, их не трогать
ADD_REL_MAX = 1.2           # %, от сильнейшей гаммы родителя: выше — «аномалия», не добавлять
HL_TOL = math.log(1.25)     # допуск периода набора против `nuclides`
DUP_KEV = 3.0               # несопоставленная строка поставки ближе — не добавлять
ANCHOR_FRAC = 0.1           # якорь — по линиям не слабее 10 % сильнейшей сопоставленной
ANCHOR_SPREAD_MAX = 1.5     # разброс отношений якоря больше — набор не брать
REJECT_REL = 10.0           # %: несопоставленная линия набора сильнее — набор описывает не тот распад
DOUBLET_KEV = 0.1           # несопоставленная линия ближе к СОПОСТАВЛЕННОЙ строке — дублет, уже в ней
SAME_KEV = 0.05             # две записи набора ближе — одна строка (как `S161` у читателя)

# мода набора ENSDF → допустимые `dec_type` поставки (0 α, 1 EC+β⁺, 7 EC, 2 β⁻, 3 IT)
MODES = {"B-": ("2",), "EC": ("1", "7"), "B+": ("1", "7"), "A": ("0",), "IT": ("3",)}

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


def connect(path, write):
    if not os.path.isfile(path):
        sys.exit(u"нет базы: %s" % path)
    uri = "file:%s%s" % (path.replace(chr(92), "/"), "" if write else "?mode=ro")
    return sqlite3.connect(uri, uri=True)


def tol_kev(e):
    return max(1.0, 4e-4 * e)


def num_text(x):
    u"""Число записью без экспоненты и группировки, 4 значащих (как у поставки)."""
    if x == 0:
        return "0"
    digits = max(0, 3 - int(math.floor(math.log10(abs(x)))))
    s = ("%." + str(digits) + "f") % x
    return s


def energy_text(e):
    s = repr(float(e))
    return s if "e" not in s.lower() else "%.6f" % e


def dataset_mode(dsid):
    u"""«214BI B- DECAY» → 'B-'; наборы задержанных частиц и прочее → None."""
    m = re.match(r"^\s*\S+\s+(\S+)\s+DECAY", dsid)
    if not m:
        return None
    mode = m.group(1)
    return mode if mode in MODES else None


def fingerprint(con):
    h = hashlib.sha256()
    n = 0
    for row in con.execute("select * from decay_radiations where dr_pk < ? order by dr_pk", (OWN_PK_BASE,)):
        h.update(repr(row).encode("utf-8"))
        n += 1
    return n, h.hexdigest()[:16]


def match(db_lines, ens_lines):
    u"""Жадное сопоставление по |ΔE|, один к одному. db_lines/ens_lines — списки (E, I, ...)."""
    pairs = []
    for i, d in enumerate(db_lines):
        for j, g in enumerate(ens_lines):
            de = abs(d[0] - g[0])
            if de <= tol_kev(g[0]):
                pairs.append((de, i, j))
    pairs.sort()
    used_d, used_g, out = set(), set(), []
    for de, i, j in pairs:
        if i in used_d or j in used_g:
            continue
        used_d.add(i)
        used_g.add(j)
        out.append((i, j))
    return out


def plan(nuc, sch, min_yield):
    u"""Посчитать добор. Возвращает (строки к записи, сводка по родителям, аномалии, пропуски)."""
    parents = nuc.execute(
        "select distinct parent_nucid, parent_l_seqno from decay_radiations"
        " where type_a = 'G' and dr_pk < ? order by parent_nucid, parent_l_seqno", (OWN_PK_BASE,)).fetchall()
    hl = {}
    for nucid, lseq, t in nuc.execute("select nucid, l_seqno, half_life_sec from nuclides"):
        hl.setdefault(nucid, []).append((lseq, t))
    datasets = {}
    for did, dsid, pn, phl in sch.execute(
            "select id, dsid, parent_nucid, parent_hl_sec from ensdf_datasets where parent_nucid is not null"):
        datasets.setdefault(pn.upper(), []).append((did, dsid, phl))

    rows, summary, anomalies, skipped = [], {}, [], []
    merges = {"placed_twice": 0, "summed": 0}
    for pnucid, plseq in parents:
        db = nuc.execute(
            "select energy_num, intensity_num, dec_type from decay_radiations where parent_nucid = ?"
            " and parent_l_seqno is ? and type_a = 'G' and dr_pk < ? and energy_num > 0 and intensity_num > 0",
            (pnucid, plseq, OWN_PK_BASE)).fetchall()
        if not db:
            continue
        imax = max(i for e, i, c in db)
        cand = hl.get(pnucid, [])
        t = None
        for lseq, tt in cand:
            if lseq == plseq:
                t = tt
        if t is None and len(cand) == 1:
            t = cand[0][1]
        base = re.match(r"^(\d+[A-Z]+)", pnucid)
        s = summary.setdefault((pnucid, plseq), {"db": len(db), "added": 0, "added_pct": 0.0,
                                                  "ensdf": 0, "left_pct": 0.0, "rejected": 0.0, "sets": []})
        if t is None or not (t > 0) or base is None:
            skipped.append((pnucid, plseq, u"нет периода родителя в `nuclides`"))
            continue
        used_db = set()
        seen_modes = set()
        for did, dsid, phl in datasets.get(base.group(1), []):
            mode = dataset_mode(dsid)
            if mode is None or not phl or not (phl > 0):
                continue
            if math.isinf(t) or abs(math.log(phl / t)) > HL_TOL:
                continue
            ens = sch.execute("select energy_kev, intensity from ensdf_gammas where dataset_id = ?"
                              " and intensity > 0 and energy_kev > 0", (did,)).fetchall()
            if not ens:
                continue
            if mode in seen_modes or (mode in ("EC", "B+") and seen_modes & {"EC", "B+"}):
                skipped.append((pnucid, plseq, u"второй набор той же моды «%s» — не берётся" % dsid))
                continue
            seen_modes.add(mode)
            s["ensdf"] += len(ens)
            codes = MODES[mode]
            dbm = [(e, i, c, k) for k, (e, i, c) in enumerate(db) if c in codes]
            pairs = match(dbm, ens)
            if not pairs:
                skipped.append((pnucid, plseq, u"набор «%s» без якоря (%d линий ENSDF, сумма RI %.4g)"
                                % (dsid, len(ens), sum(g[1] for g in ens))))
                continue
            for i, j in pairs:
                used_db.add(dbm[i][3])
            strong = max(dbm[i][1] for i, j in pairs)
            ratios = sorted(dbm[i][1] / ens[j][1] for i, j in pairs if dbm[i][1] >= ANCHOR_FRAC * strong)
            norm = ratios[len(ratios) // 2] if len(ratios) % 2 else 0.5 * (ratios[len(ratios) // 2 - 1] + ratios[len(ratios) // 2])
            spread = ratios[-1] / ratios[0]
            s["sets"].append((dsid, len(pairs), len(ratios), norm, spread))
            if spread > ANCHOR_SPREAD_MAX:
                skipped.append((pnucid, plseq, u"набор «%s»: разброс якоря ×%.2f > ×%.2f" % (dsid, spread, ANCHOR_SPREAD_MAX)))
                continue
            code = max(codes, key=lambda c: sum(1 for i, j in pairs if dbm[i][2] == c))
            matched_g = set(j for i, j in pairs)
            # Набор, у которого НЕ НАШЛАСЬ в поставке линия, заведомо проходившая порог
            # (≥ REJECT_REL от сильнейшей), описывает не тот распад (чужой изомер, иная
            # схема новой оценки) — его слабые линии тоже не берутся, весь набор в пропуск.
            lost = [ens[j] for j in range(len(ens)) if j not in matched_g and 100.0 * ens[j][1] * norm / imax >= REJECT_REL]
            matched_db = [dbm[i] for i, j in pairs]
            if lost:
                e_l, ri_l = max(lost, key=lambda g: g[1])
                skipped.append((pnucid, plseq, u"набор «%s»: %d линий ≥ %.0f %% от сильнейшей не найдены в поставке"
                                u" (сильнейшая %.1f кэВ, %.3g %%) — набор не берётся"
                                % (dsid, len(lost), REJECT_REL, e_l, 100.0 * ri_l * norm / imax)))
                continue
            unmatched_db = [d for d in dbm if all(d is not dbm[i] for i, j in pairs)]
            cand = []
            for j, (e, ri) in enumerate(ens):
                if j in matched_g:
                    continue
                y = ri * norm
                rel = 100.0 * y / imax
                near = [d for d in unmatched_db if abs(d[0] - e) <= DUP_KEV]
                twin = [d for d in matched_db if abs(d[0] - e) <= DOUBLET_KEV]
                if twin:
                    anomalies.append((pnucid, dsid, e, y, rel, u"дублет при сопоставленной строке %.3f кэВ — уже в ней" % twin[0][0]))
                    s["left_pct"] += y
                    continue
                if rel >= ADD_REL_MAX:
                    anomalies.append((pnucid, dsid, e, y, rel, u"сильнее порога поставки — не срезана, а отсутствует"))
                    s["left_pct"] += y
                    continue
                if near:
                    anomalies.append((pnucid, dsid, e, y, rel, u"рядом несопоставленная строка поставки %.3f кэВ" % near[0][0]))
                    s["left_pct"] += y
                    continue
                cand.append((e, y))
            # Две записи ENSDF одной энергии (ближе SAME_KEV) в одном наборе — одна строка: при
            # РАВНЫХ выходах это одна линия, размещённая в схеме дважды (выход не делён), — берётся
            # одна; при разных — неразрешённый дублет двух переходов, выходы складываются. Иначе
            # читатель (`FsaSampleLibrary.DecayLines`, правило `S161`) молча выбросил бы вторую
            # копию того же канала, а `NucBase` показал бы обе.
            cand.sort()
            groups = []
            for e, y in cand:
                if groups and e - groups[-1][-1][0] < SAME_KEV:
                    groups[-1].append((e, y))
                else:
                    groups.append([(e, y)])
            for g in groups:
                ys = [y for e, y in g]
                if len(g) > 1 and max(ys) - min(ys) <= 1e-9 * max(ys):
                    y = ys[0]
                    merges["placed_twice"] += len(g) - 1
                elif len(g) > 1:
                    y = sum(ys)
                    merges["summed"] += len(g) - 1
                else:
                    y = ys[0]
                e = g[0][0]
                if y < min_yield:
                    s["left_pct"] += y
                    continue
                rows.append((plseq, pnucid, code, e, y, dsid))
                s["added"] += 1
                s["added_pct"] += y
    return rows, summary, anomalies, skipped, merges


def main():
    p = argparse.ArgumentParser(description=u"AMBER151: добор слабых гамма-линий из schemedb.ensdf_gammas")
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--apply", action="store_true", help=u"ЗАПИСЬ в --nucdb, в транзакции")
    p.add_argument("--min-yield", type=float, default=0.01, help=u"порог выхода на распад, %% (по умолчанию 0.01 — решение Amber 01.10.2026)")
    p.add_argument("--list", default="", help=u"родители через запятую — построчно, что добавляется")
    p.add_argument("--anomalies", action="store_true", help=u"печатать все аномалии, а не первые 40")
    p.add_argument("--corpus", action="store_true", help=u"сводка по нуклидам корпуса (manifest.csv)")
    a = p.parse_args()

    nuc = connect(a.nucdb, a.apply)
    sch = connect(a.schemedb, False)
    w(u"nucdb: %s (%s)", a.nucdb, u"ЗАПИСЬ" if a.apply else u"mode=ro, без записи")
    w(u"schemedb: %s (mode=ro)", a.schemedb)
    w(u"порог выхода на распад: %s %%", num_text(a.min_yield) if a.min_yield > 0 else u"нет")
    own0 = nuc.execute("select count(*) from decay_radiations where dr_pk >= ? and dr_pk < ?",
                       (OWN_PK_BASE, OWN_PK_END)).fetchone()[0]
    n0, fp0 = fingerprint(nuc)
    w(u"строк поставки (dr_pk < %d): %d, отпечаток %s; своих строк до прогона: %d", OWN_PK_BASE, n0, fp0, own0)

    rows, summary, anomalies, skipped, merges = plan(nuc, sch, a.min_yield)
    parents_added = sum(1 for s in summary.values() if s["added"])
    w(u"к добавлению: %d линий у %d родителей; аномалий (не добавлено): %d; пропусков наборов: %d",
      len(rows), parents_added, len(anomalies), len(skipped))
    w(u"записей ENSDF одной энергии слито: размещённых дважды (выход один) %d, дублетов (выходы сложены) %d",
      merges["placed_twice"], merges["summed"])

    want = [x.strip() for x in a.list.split(",") if x.strip()]
    for n in want:
        got = [r for r in rows if r[1] == n]
        w(u"--- %s: %d линий, сумма %s %% на распад", n, len(got), num_text(sum(r[4] for r in got)))
        for key, s in summary.items():
            if key[0] == n:
                for dsid, npair, nanc, norm, spread in s["sets"]:
                    w(u"    набор «%s»: сопоставлено %d, якорь по %d, норма %.5g, разброс ×%.3f", dsid, npair, nanc, norm, spread)
        for r in sorted(got, key=lambda r: -r[4])[:15]:
            w(u"    %10s кэВ  %s %%  dec_type %s  [%s]", energy_text(r[3]), num_text(r[4]), r[2], r[5])
    if a.corpus:
        from missing_lines import corpus_nuclides
        w(u"нуклиды корпуса (manifest.csv): строк γ поставки / добавлено / %% на распад добавлено / не добавлено (аномалии, порог)")
        tot_n, tot_p = 0, 0.0
        for n in corpus_nuclides(nuc):
            for key, s in sorted(summary.items()):
                if key[0] != n:
                    continue
                tot_n += s["added"]
                tot_p += s["added_pct"]
                w(u"    %-9s ур. %s  %4d  %+5d  %8s  %8s", n, key[1], s["db"], s["added"],
                  num_text(s["added_pct"]), num_text(s["left_pct"]))
        w(u"    итого по корпусу: %d линий", tot_n)
    lim = None if a.anomalies else 40
    w(u"аномалии (первые %s, по выходу):" % (u"все" if lim is None else lim))
    for x in sorted(anomalies, key=lambda x: -x[3])[:lim]:
        w(u"    %-9s %10s кэВ  %s %% (%.2f %% от сильнейшей)  %s  [%s]", x[0], energy_text(x[2]), num_text(x[3]), x[4], x[5], x[1])
    w(u"пропуски наборов (первые 40):")
    for x in (skipped if a.anomalies else skipped[:40]):
        w(u"    %-9s ур. %s: %s", x[0], x[1], x[2])

    if not a.apply:
        w(u"⛔ ЗАПИСИ НЕ БЫЛО: база открыта `mode=ro`, ключ `--apply` не назван.")
        return 0

    cur = nuc.cursor()
    cur.execute("BEGIN")
    try:
        cur.execute("delete from decay_radiations where dr_pk >= ? and dr_pk < ?", (OWN_PK_BASE, OWN_PK_END))
        for k, (plseq, pnucid, code, e, y, dsid) in enumerate(sorted(rows, key=lambda r: (r[1], r[3]))):
            cur.execute(
                "insert into decay_radiations (parent_l_seqno, parent_nucid, dec_type, type_a, type_c, intensity,"
                " intensity_unc, intensity_num, energy_num, energy, energy_unc, endpoint, endpoint_unc, dr_pk)"
                " values (?, ?, ?, 'G', '', ?, null, ?, ?, ?, null, null, null, ?)",
                (plseq, pnucid, code, num_text(y), float(num_text(y)), float(e), energy_text(e), OWN_PK_BASE + k))
        n1, fp1 = fingerprint(nuc)
        if (n1, fp1) != (n0, fp0):
            raise RuntimeError(u"строки поставки изменились: %d/%s → %d/%s" % (n0, fp0, n1, fp1))
        own1 = cur.execute("select count(*) from decay_radiations where dr_pk >= ? and dr_pk < ?",
                           (OWN_PK_BASE, OWN_PK_END)).fetchone()[0]
        nuc.commit()
    except Exception:
        nuc.rollback()
        raise
    integrity = nuc.execute("PRAGMA integrity_check").fetchone()[0]
    w(u"ЗАПИСАНО: своих строк %d (было %d); строк поставки %d, отпечаток %s — тот же; integrity_check %s",
      own1, own0, n1, fp1, integrity)
    return 0 if integrity == "ok" else 1


if __name__ == "__main__":
    sys.exit(main())
