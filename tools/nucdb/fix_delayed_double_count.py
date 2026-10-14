# -*- coding: utf-8 -*-
u"""П306/П308 (14.10.2026): сумма ветвей `decay_chain` выше 100 % — двойной счёт запаздывающих
частиц семьи β⁻ (β⁻n, β⁻2n, β⁻α…) и неверный раскол «основная дочь несёт всю моду».

ЧТО НЕ ТАК. Соглашение ENSDF: задержанная мода — ЧАСТЬ своей основной (%B⁻ = 100 включает
%B⁻N = 2.6). Выгрузка NNDC заводит строку `decay_chain` на НАБОР распада ENSDF (вывод П295): набор
«B- DECAY» в ядро A получил долю моды 100, набор «B-N DECAY» в ядро A−1 — %B⁻N, вычета нет; где
набора β⁻n нет — строки задержанной моды нет вовсе, а основная дочь несёт всю моду. Правило
приложения (`DecayParentRule.ChainTable`) берёт обе строки, обход рядов кладёт в основную дочь всю
моду и сверху задержанную (`137I` → 137XE 1.0 вместо 0.9286, сумма ряда `87BR` 1.026).

РЕШЕНИЯ AMBER 14.10.2026, вопросником, дословно:
  * «Починить на копии, принести (Рекомендую)» — разбор и метод П306
    (`handover/handover-2026-10-14-p306-excess-sums-second-pass.md`);
  * «Записать β⁻ + три ребра (Рекомендую)» — описание выбранного: «Рекомендуемая копия в nucdb;
    скрипты — в tools/nucdb (с запретом ребра I-119 → Te-119m); затем пересборка корпуса
    генератором, check_all, правка текстов.» Записано полосой П308
    (`handover/handover-2026-10-14-p308-beta-family-second-pass-write.md`); три ребра второго
    прохода — `add_isomer_edges.py --only` (там же);
  * семья захвата (εp, βp, β⁺α, ε2p) — «Оставить как есть (Рекомендую)»: в нынешнем коде вычет
    ломает 511 кэВ у 32 корней (потолок позитронов `CascadeAtomicData.BetaPlusByBranch` считается
    по уменьшенной доле ветви, `17NE` 199 % → 4 %). Поэтому `--families` по умолчанию `B-`, а `EC`
    принимается ТОЛЬКО для копии (замер) — в базу дерева отказ;
  * задержанная ветвь, стоящая пределом (Si-35, Si-36 «β⁻n < …» строкой ряда; Ga-85, V-61, Co-72
    «β⁻n > …») — «Оставить (Рекомендую)»: «предел — не доля, вычитать нечего». Родитель, у
    которого в вычет попал бы предел, НЕ правится (группа ПРЕДЕЛ_НЕ_ДОЛЯ), ключа включить его нет.
  Обе записи «Оставить» — в таблице «Чего делать НЕ надо» `TODO.md`.

МЕТОД (П306; классы и разбор родителя — `fix_lost_branches.py`, импортируется как модуль, своей
копии нет). Отличия от снятого ключа `--fix-excess` П295 (он неверен в семи местах — П306):
  1. Основная дочь получает V_семьи − Σ ЗАДЕРЖАННЫХ СТРОК САМОГО РЯДА (рёбер правила приложения
     с кодом задержанной моды той же семьи), а не Σ задержанных `l_decays`: сумма ветвей семьи
     по правилу становится РОВНО V.
  2. Задержанная мода, которой нет строкой, вставляется, только если `l_decays` даёт ЧИСЛО (не
     «<», не «≤»: `22F` «β⁻n < 11» — предел); вычитается ровно вставленное.
  3. Несколько строк основной семьи в одну дочь — рёбра «родитель → изомер» П282/П294/П299
     (`81GA` → 81GE 53.51 + 81GEm 46.49) — масштабируются ВМЕСТЕ на (V − Σзадерж.)/V: доля
     изомера f = BR·f_отн, а BR метода П282 был взят с двойным счётом. Последняя строка (основное
     состояние) получает остаток — сумма записанных чисел ровно целевая.
  4. Основная доля 0 (вся мода — с частицей) — строка удаляется, если правило приложения после
     удаления не достраивает ребро иным путём (проверка с откатом), иначе остаётся «0». (В семье
     β⁻ таких нет — это случай семьи захвата: `35CA`.)
  5. Ряд, целиком достроенный правилом из `l_decays` (`138SN`, `95KR`: в `decay_chain` у
     родителя одни петли ИТ изомера), — вставка НАСТОЯЩИХ строк своего уровня.
  6. НЕ трогаются: основная дочь с периодом < 1 пс (`8LI` → 8BE: ряд через 8BE уже даёт ту же
     α); задержанных больше моды (`39TI`); классы ПОСТАВКА>100 и ЧУЖОЙ_УРОВЕНЬ (не двойной
     счёт); ПРЕДЕЛ_НЕ_ДОЛЯ (решение Amber, выше).
Знаков после точки — сколько у слагаемых (100 − 2.6 = 97.4). После записи — проверка: сумма ряда
каждой правленой семьи = V (0 расхождений), повторный план — 0 правок; иначе откат.

⚠ ПОРЯДОК В КОНВЕЙЕРЕ `decay_chain` (как писалось 14.10.2026): `add_isomer_edges.py` (П294, 51
ребро) → `fix_lost_branches.py` (П299) → `add_diverging_isomer_edges.py` (П299, 18 рёбер) → ЭТОТ
скрипт (П308) → `add_isomer_edges.py --only 91MOm:91NBm,130SNm:130SBm,129SNm1:129SBm1` (П308,
второй проход). Рёбра изомеров этот скрипт масштабирует (п. 3), поэтому стоит ПОСЛЕ них.

⛔ ЗАПИСЬ В ЖИВУЮ БАЗУ — ТОЛЬКО ПО СЛОВУ AMBER. Без `--apply` база открыта `mode=ro`; `--apply` в
файл дерева, каталога сборки или %AppData% отказывает без `--allow-live`. Повтор — без изменений.

    python tools/nucdb/fix_delayed_double_count.py                           # план по базе дерева (ro)
    python tools/nucdb/fix_delayed_double_count.py --nucdb <копия> --apply   # запись в копию
    python tools/nucdb/fix_delayed_double_count.py --csv <файл>              # перечень по группам
"""
import argparse
import csv
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fix_lost_branches as F  # noqa: E402

THR = F.THR
LIMIT_UP = ("LT", "LE")
LIMITS = ("LT", "LE", "GT", "GE")
FAM_CODES = {"B-": ("2",), "EC": ("1", "7", "15", "50")}
UNBOUND_HL = 1e-12

EC_DECISION = u"решение Amber 14.10.2026 «Оставить как есть (Рекомендую)»"
LIMIT_DECISION = u"решение Amber 14.10.2026 «Оставить (Рекомендую)»: предел — не доля, вычитать нечего"

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")
    OUT.flush()


def chain_rows(nuc, p):
    u"""Рёбра правила приложения: (дочь, доля, perc как записан, канал, уровень, есть ли строка в decay_chain)."""
    out = []
    for d, v, pr, dt, lv in F.app_edges(nuc, p):
        real = nuc.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                           " and dec_type = ?", (p, lv, d, dt)).fetchone()
        out.append((d, v, pr, dt, lv, real is not None and real[0] == pr))
    return out


def hl(nuc, nucid):
    r = nuc.execute("select half_life_sec from nuclides where nucid = ? order by l_seqno limit 1", (nucid,)).fetchone()
    return r[0] if r else None


def places_of(texts):
    u"""Знаков после точки — сколько у слагаемых; число REAL (доля из `l_decays`, подставленная
    правилом) — по записи "%.6g" (0.7, а не 0.69999…)."""
    def txt(t):
        if not isinstance(t, str):
            return F.num_text(t, None)
        if "e" in t.lower() and F.to_float(t) is not None:
            return F.num_text(F.to_float(t), None)   # «5.5E-5» → «0.000055»: 6 знаков
        return t
    ds = [F.decimals(txt(t)) for t in texts if t is not None]
    ds = [d for d in ds if d is not None]
    return max(ds or [0])


def plan_parent(nuc, p, families):
    u"""(класс, разбор, правки, [группа] + заметки). Правка: (вид, p, уровень, дочь, канал, новое, было, пояснение)."""
    an = F.analyse(nuc, p)
    cls = F.classify(an)
    if cls not in (u"ЗАДЕРЖ_ДВАЖДЫ", u"ЗАДЕРЖ_БЕЗ_СТРОКИ"):
        return cls, None, [], []
    rows = chain_rows(nuc, p)
    fixes, notes = [], []
    groups = []
    for fam, (dmain, main, dl, vfam, sdl, places) in sorted(an["fam_main"].items()):
        fam_dl_l = an["delayed"].get(fam, [])
        if not fam_dl_l:
            continue
        g_main = F.ground(dmain)
        mrows = [r for r in rows if F.ground(r[0]) == g_main and r[3] in FAM_CODES[fam]]
        drows = [r for r in rows if F.to_float(r[3]) is not None and int(r[3]) in F.DELAYED
                 and F.DELAYED[int(r[3])] == fam]
        have_d = set(F.ground(r[0]) for r in rows)
        # вставки задержанных, которых ряд не несёт
        ins = []
        for d, code, ptxt, pnum, oper in fam_dl_l:
            if F.ground(d) in have_d:
                continue
            if oper in LIMIT_UP:
                notes.append(u"%s %s %s %s — предел, не доля: не вставляется" % (F.lab(code), d, oper, (ptxt or "").strip()))
                continue
            if pnum < THR:
                notes.append(u"%s %s %s — ниже порога %s, не вставляется" % (F.lab(code), d, (ptxt or "").strip(), THR))
                continue
            ins.append((d, code, ptxt.strip(), pnum, oper))
        sdel = sum(r[1] for r in drows) + sum(x[3] for x in ins)
        target = vfam - sdel
        tag = []
        if fam not in families:
            groups.append(u"семья %s не правится%s" % (fam, u" (" + EC_DECISION + u")" if fam == "EC" else u" (--families)"))
            continue
        # П308: в вычет попал бы предел (строка ряда «β⁻n < 5» или «β⁻n > 35» в `l_decays`) — не правится
        subtracted = set(F.ground(r[0]) for r in drows) | set(F.ground(x[0]) for x in ins)
        lim = [x for x in fam_dl_l if x[4] in LIMITS and F.ground(x[0]) in subtracted]
        if lim:
            groups.append(u"ПРЕДЕЛ_НЕ_ДОЛЯ")
            notes.append(u"в вычет попал бы предел %s — не правится (%s)" % (
                "; ".join(u"%s %s %s %s" % (F.lab(x[1]), x[0], x[4], (x[2] or "").strip()) for x in lim), LIMIT_DECISION))
            continue
        if not mrows:
            groups.append(u"нет строк основной семьи")
            continue
        if not all(r[5] for r in mrows):
            # Ряд родителя целиком достроен правилом из `l_decays` (`138SN`, `95KR`: в decay_chain у
            # него одни петли ИТ изомера) — строк править нет. Вставляются НАСТОЯЩИЕ строки своего
            # уровня: основная — долей V − Σзадерж., задержанные — их долями; после этого правило
            # берёт их и достройку прекращает (проверяется после записи — суммой семьи).
            if any(r[5] for r in rows if r[0] != p) or len(mrows) != 1:
                groups.append(u"строка основной семьи достроена правилом, но рядом есть настоящие — правка не однозначна")
                continue
            pl = places_of([mrows[0][2]] + [r[2] for r in drows] + [x[2] for x in ins])
            r = mrows[0]
            fixes.append(("ins", p, an["plv"], r[0], str(r[3]), F.num_text(target, pl), None,
                          u"РЯД_ИЗ_L_DECAYS: %s %s − Σзадерж. %s" % (fam, F.num_text(vfam, pl), F.num_text(sdel, pl))))
            for r in drows:
                fixes.append(("ins", p, an["plv"], r[0], str(r[3]), F.num_text(r[1], places_of([r[2]])), None,
                              u"РЯД_ИЗ_L_DECAYS: задержанная своей долей"))
            for d, code, ptxt, pnum, oper in ins:
                fixes.append(("ins", p, an["plv"], d, str(code), ptxt, None, u"%s %s %% (l_decays)" % (F.lab(code), ptxt)))
            groups.append(fam + u" РЯД_ИЗ_L_DECAYS")
            continue
        mh = hl(nuc, g_main)
        if mh is not None and mh < UNBOUND_HL:
            groups.append(u"НЕСВЯЗАННАЯ_ДОЧЬ")
            notes.append(u"основная дочь %s живёт %s с — ряд через неё уже несёт задержанную частицу" % (g_main, mh))
            continue
        if target < -1e-9:
            groups.append(u"ЗАДЕРЖАННЫХ_БОЛЬШЕ_МОДЫ")
            notes.append(u"задержанных %s > моды %s — поставка противоречива" % (F.num_text(sdel, 6), F.num_text(vfam, 6)))
            continue
        M = sum(F.to_float(r[2]) or 0.0 for r in mrows)
        texts = [r[2] for r in mrows] + [r[2] for r in drows] + [x[2] for x in ins] + [x[2].strip() for x in main]
        pl = places_of(texts)
        if abs(M - target) <= 10 ** (-pl - 1) and not ins:
            continue
        opers = set((x[4] or "") for x in fam_dl_l)
        if "AP" in opers:
            tag.append("AP")
        if len(mrows) > 1:
            tag.append(u"рёбра изомера")
        if any(r[3] == "15" for r in mrows):
            tag.append(u"канал β⁺")
        for d, code, ptxt, pnum, oper in ins:
            fixes.append(("ins", p, an["plv"], d, str(code), ptxt, None,
                          u"%s %s %% (l_decays)" % (F.lab(code), ptxt)))
        if target <= 10 ** (-pl - 1):
            tag.append(u"ОСНОВНАЯ_0")
            for r in mrows:
                fixes.append(("del", p, r[4], r[0], r[3], "0", r[2],
                              u"вся мода с частицей: %s − Σзадерж. %s = 0" % (F.num_text(vfam, pl), F.num_text(sdel, pl))))
        elif len(mrows) == 1:
            r = mrows[0]
            fixes.append(("upd", p, r[4], r[0], r[3], F.num_text(target, pl), r[2],
                          u"%s %s − Σзадерж. ряда %s" % (fam, F.num_text(vfam, pl), F.num_text(sdel, pl))))
        else:
            k = target / M
            # строка основного состояния последней — получает остаток
            order = sorted(mrows, key=lambda r: (r[0] == g_main, r[0]))
            acc = 0.0
            for i, r in enumerate(order):
                if i < len(order) - 1:
                    v = round((F.to_float(r[2]) or 0.0) * k, pl)
                    acc += v
                else:
                    v = round(target - acc, pl)
                fixes.append(("upd", p, r[4], r[0], r[3], F.num_text(v, pl), r[2],
                              u"масштаб %.6f = (%s − Σзадерж. %s)/%s" % (k, F.num_text(vfam, pl), F.num_text(sdel, pl),
                                                                       F.num_text(M, pl))))
        groups.append(fam + (" " + "+".join(tag) if tag else ""))
    return cls, an, fixes, [u"; ".join(groups)] + notes


def plan_all(nuc, fams):
    res = []
    for p in [r[0] for r in nuc.execute("select distinct nucid from decay_chain order by nucid")]:
        cls, an, fx, notes = plan_parent(nuc, p, fams)
        if an is None:
            continue
        res.append((cls, an, fx, notes))
    return res


def apply_fixes(nuc, fixes):
    cur = nuc.cursor()
    done = {"ins": 0, "upd": 0, "del": 0, "zero": 0, "same": 0}
    for kind, p, lv, d, dt, ptxt, old, why in fixes:
        have = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                           " and dec_type = ?", (p, lv, d, dt)).fetchone()
        if kind == "ins":
            if have is not None:
                done["same"] += 1
                continue
            cur.execute("insert into decay_chain (nucid, l_seqno, daughter_nucid, dec_type, perc) values (?,?,?,?,?)",
                        (p, lv, d, dt, ptxt))
            w(u"  + %s ур.%d → %s [%s] perc %s   (%s)", p, lv, d, dt, ptxt, why)
            done["ins"] += 1
        elif kind == "upd":
            if have is None:
                w(u"  ! %s ур.%d → %s [%s]: строки нет", p, lv, d, dt)
                continue
            if have[0] == ptxt:
                done["same"] += 1
                continue
            cur.execute("update decay_chain set perc = ? where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                        " and dec_type = ?", (ptxt, p, lv, d, dt))
            w(u"  ~ %s ур.%d → %s [%s] perc %s → %s   (%s)", p, lv, d, dt, have[0], ptxt, why)
            done["upd"] += 1
        elif kind == "del":
            if have is None:
                done["same"] += 1
                continue
            before = sorted((x[0], x[1], x[3], x[4]) for x in chain_rows(nuc, p))
            cur.execute("savepoint dz")
            cur.execute("delete from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ? and dec_type = ?",
                        (p, lv, d, dt))
            after = sorted((x[0], x[1], x[3], x[4]) for x in chain_rows(nuc, p))
            expect = [r for r in before if not (r[0] == d and r[2] == dt and r[3] == lv)]
            if after != expect:
                cur.execute("rollback to dz")
                cur.execute("release dz")
                cur.execute("update decay_chain set perc = ? where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                            " and dec_type = ?", ("0", p, lv, d, dt))
                w(u"  0 %s ур.%d → %s [%s] perc %s → 0 (удаление дало бы ветви %s — строка оставлена нулём)   (%s)",
                  p, lv, d, dt, have[0], after, why)
                done["zero"] += 1
                continue
            cur.execute("release dz")
            w(u"  − %s ур.%d → %s [%s] perc %s удалена   (%s)", p, lv, d, dt, have[0], why)
            done["del"] += 1
    return done


def family_sums_bad(nuc, res, fams):
    u"""У каждого правленого родителя сумма ветвей семьи по правилу приложения = V семьи."""
    bad = 0
    for cls, an, fx, notes in res:
        if not fx:
            continue
        a2 = F.analyse(nuc, an["p"])
        rows = chain_rows(nuc, an["p"])
        for fam, (dmain, main, dl, vfam, sdl, places) in a2["fam_main"].items():
            if fam not in fams:
                continue
            fam_rows = [r for r in rows if (F.ground(r[0]) == F.ground(dmain) and r[3] in FAM_CODES[fam])
                        or (F.to_float(r[3]) is not None and int(r[3]) in F.DELAYED and F.DELAYED[int(r[3])] == fam)]
            s = sum(r[1] for r in fam_rows)
            if abs(s - vfam) > 1e-6:
                bad += 1
                w(u"  ⛔ %s семья %s: сумма ряда %s ≠ %s", an["p"], fam, F.num_text(s, 6), F.num_text(vfam, 6))
    return bad


def main():
    ap = argparse.ArgumentParser(description=u"П306/П308: двойной счёт задержанных частиц семьи β⁻ в decay_chain")
    ap.add_argument("--nucdb", default=F.DEFAULT_NUCDB)
    ap.add_argument("--apply", action="store_true", help=u"ЗАПИСЬ в --nucdb (только копия)")
    ap.add_argument("--allow-live", action="store_true", help=u"запись в базу дерева/сборки — ТОЛЬКО по слову Amber")
    ap.add_argument("--families", default="B-",
                    help=u"семьи: B- (по умолчанию); EC — только копия, замер (" + EC_DECISION + u")")
    ap.add_argument("--csv", default="", help=u"перечень по группам в CSV (по умолчанию не пишется)")
    a = ap.parse_args()
    fams = set(x.strip() for x in a.families.split(",") if x.strip())
    if fams - set(FAM_CODES):
        sys.exit(u"⛔ неизвестные семьи: %s (есть B-, EC)" % ", ".join(sorted(fams - set(FAM_CODES))))
    live = F.is_live(a.nucdb)
    if a.apply and live and not a.allow_live:
        sys.exit(u"⛔ %s — база дерева/сборки/AppData; запись только в копию (или --allow-live по слову Amber)" % a.nucdb)
    if a.apply and live and "EC" in fams:
        sys.exit(u"⛔ семья захвата в базу дерева не пишется: %s" % EC_DECISION)
    nuc = F.connect(a.nucdb, a.apply)
    w(u"nucdb: %s (%s); семьи: %s", a.nucdb, u"ЗАПИСЬ" if a.apply else u"mode=ro", ",".join(sorted(fams)))
    n0, fp0 = F.fingerprint(nuc)
    w(u"decay_chain до: %d строк, отпечаток %s", n0, fp0)
    res = plan_all(nuc, fams)
    by = {}
    for cls, an, fx, notes in res:
        by.setdefault((cls, notes[0] if notes else ""), []).append((an, fx))
    w(u"родителей ЗАДЕРЖ_ДВАЖДЫ / ЗАДЕРЖ_БЕЗ_СТРОКИ: %d", len(res))
    for k in sorted(by):
        lst = by[k]
        nfx = sum(1 for an, fx in lst if fx)
        w(u"  %-18s %-55s %4d (с правкой %d): %s", k[0], k[1] or u"(ничего)", len(lst), nfx,
          " ".join(an["p"] for an, fx in lst[:14]) + (u" …" if len(lst) > 14 else ""))
    lim = sorted(an["p"] for cls, an, fx, notes in res if notes and u"ПРЕДЕЛ_НЕ_ДОЛЯ" in notes[0])
    w(u"ПРЕДЕЛ_НЕ_ДОЛЯ (не правятся, %s): %s", LIMIT_DECISION, " ".join(lim) or u"нет")
    allfx = [f for cls, an, fx, notes in res for f in fx]
    w(u"правок по плану: вставок %d, правок %d, удалений %d (родителей с правкой %d)",
      sum(1 for f in allfx if f[0] == "ins"), sum(1 for f in allfx if f[0] == "upd"),
      sum(1 for f in allfx if f[0] == "del"), sum(1 for cls, an, fx, notes in res if fx))
    if a.csv:
        with io.open(a.csv, "w", encoding="utf-8", newline="") as f:
            wr = csv.writer(f)
            wr.writerow(["class", "group", "parent", "T", "T_sec", "S_before", "S_after_plan", "l_decays_own_level",
                         "chain_rows_app", "fix", "notes"])
            for cls, an, fx, notes in sorted(res, key=lambda r: (r[0], r[3][0] if r[3] else "", r[1]["p"])):
                s_after = an["S"]
                for f2 in fx:
                    v = F.to_float(f2[5]) or 0.0
                    o = F.to_float(f2[6]) or 0.0
                    s_after += v if f2[0] == "ins" else (v - o if f2[0] == "upd" else -o)
                wr.writerow([cls, notes[0] if notes else "", an["p"], F.fmt_t(an["pt"]),
                             an["pt"] if an["pt"] is not None else "", F.num_text(an["S"], 6), F.num_text(s_after, 6),
                             "; ".join(u"%s %s %s%s" % (m[0] or u"—", F.lab(int(m[1])), (m[2] or "").strip(),
                                                        (" " + m[4]) if m[4] else "") for m in an["modes"]),
                             "; ".join(u"%s[%s]%s ур.%s" % (x[4], x[0], F.num_text(x[3], 6), x[2])
                                       for d in sorted(an["Ccodes"]) for x in an["Ccodes"][d]),
                             "; ".join(u"%s %s→%s [%s] %s→%s" % (f2[0], f2[1], f2[3], f2[4], f2[6], f2[5]) for f2 in fx),
                             " | ".join(notes[1:])])
        w(u"перечень: %s", a.csv)
    if not a.apply:
        return
    w(u"")
    w(u"ЗАПИСЬ:")
    done = apply_fixes(nuc, allfx)
    n1, fp1 = F.fingerprint(nuc)
    w(u"вставлено %d, изменено %d, удалено %d, оставлено нулём %d, уже было %d; decay_chain после: %d строк, отпечаток %s",
      done["ins"], done["upd"], done["del"], done["zero"], done["same"], n1, fp1)
    bad = family_sums_bad(nuc, res, fams)
    w(u"проверка сумм семей после записи: расхождений %d", bad)
    again = [f for cls, an, fx, notes in plan_all(nuc, fams) for f in fx]
    w(u"повторный план по записанному: правок %d", len(again))
    if bad or again:
        nuc.rollback()
        sys.exit(u"⛔ проверка не прошла — запись отменена (база не тронута)")
    nuc.commit()
    w(u"записано (commit)")


if __name__ == "__main__":
    main()
