# -*- coding: utf-8 -*-
u"""П295/П299 (14.10.2026): 19 из 21 расходящегося ребра П282 «родитель → изомер» с долями разбора П295.

РЕШЕНИЕ AMBER 14.10.2026, вопросником, дословно: «Записать 19 (Рекомендую)». Вопрос был: «…Предлагается
записать 19: 15 — долей по схеме, I-119 — 0.956 % (после починки его ветви), три — числом оценщика ENSDF
(Zr-99 36.7 %, Sb-129 15.7 %, Sb-131 6.9 %); не писать Os-193, Sr-81 и 8 малых…». Записано полосой П299
(журнал `handover/handover-2026-10-14-p299-lost-branches-diverging-write.md`); разбор каждого случая —
П295 (`handover/handover-2026-10-14-p295-isomer-diverging-lost-branches.md`, таблица «21 расходящийся случай»).

МЕТОД ЗАПИСИ — тот же, что у `add_isomer_edges.py` (`plan`, `apply`: расщепление ветви P → D, ветви
изомера из `l_decays`), импортируется оттуда, своей копии нет. Доли: у 15 — ход по схеме (f П282 как
есть), у 119I — ход от ветви ε+β⁺ после починки потерянной β⁺ (0.956 %), у трёх — число оценщика из
самого набора ENSDF2: 99ZR → 99NBm 36.7 (нормировка NR: ΣTI в основное и в 365.3 = 99.1 %), 129SB →
129TEm 15.7 («полное ветвление в основное 129Te 84.3 % (11)»), 131SB → 131TEm 6.9 («ΣRI(1+α) в основное
без ИТ = 93.1 (11)»).

⛔ ЗАПИСАНО 18, ОДНО УДЕРЖАНО (П299): 119I → 119TEm краснит сторож `check_parent_rule` §3г — ветвь
изомера 119TEm в `l_decays` только «β+ 0.41», судьи ε+β⁺ нет (см. `HELD` ниже); пишется ключом
`--include-held` после решения Amber.

⚠ ПОРЯДОК: ПОСЛЕ `fix_lost_branches.py` — у 119I ребро считается от строки захвата 100 (после правки
49 → 100); на поставке (BR 49) метод дал бы 0.4685 %. Скрипт проверяет это сам и отказывает.

⚠ ОДИН ПРОХОД. Записанные изомеры сами становятся родителями, и `add_isomer_edges.py` находит у них
изомеры дочек (129SNm1 → 129SBm1, 130SNm → 130SBm, 91MOm → 91NBm — П295, «второй проход»); здесь они
НЕ пишутся — это отдельный вопрос.

⛔ ЗАПИСЬ В ЖИВУЮ БАЗУ — ТОЛЬКО ПО СЛОВУ AMBER: база дерева, каталога сборки и %AppData% — отказ без
`--allow-live` (как у `add_isomer_edges.py`). Повтор — без изменений.

    python tools/nucdb/add_diverging_isomer_edges.py --nucdb <копия>            # план (ro)
    python tools/nucdb/add_diverging_isomer_edges.py --nucdb <копия> --apply    # запись
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import add_isomer_edges as m  # noqa: E402

# (родитель, изомер) → доля, % на распад родителя; None — ход по схеме как есть
PROPOSED = {
    ("139EU", "139SMm"): None, ("151DY", "151TBm"): None, ("179RE", "179Wm"): None,
    ("99ZR", "99NBm"): 36.7, ("130IN", "130SNm"): None, ("129SB", "129TEm"): 15.7,
    ("118PD", "118AGm"): None, ("123AG", "123CDm1"): None, ("133SB", "133TEm"): None,
    ("97SR", "97Ym1"): None, ("129IN", "129SNm1"): None, ("131SB", "131TEm"): 6.9,
    ("193HG", "193AUm"): None, ("97PD", "97RHm"): None, ("141EU", "141SMm"): None,
    ("191PT", "191IRm"): None, ("91TC", "91MOm"): None, ("119I", "119TEm"): None,
    ("195TL", "195HGm"): None,
    # 193OS → 193IRm (0.378 %) и 81SR → 81RBm (0.158 %) НЕ пишутся: линий изомера ≥ 0.01 % на
    # распад родителя нет (порог AMBER151) — как у восьми «малых» П282
}

# ⛔ УДЕРЖАНО до решения Amber (П299, 14.10.2026): пишется только ключом `--include-held`.
# 119I → 119TEm 0.956 % решено «Записать», но вместе с ребром метод кладёт ветвь самого изомера
# из `l_decays` его уровня — а там у 119TEm ТОЛЬКО «β+ 0.41» (строки ε+β⁺ 100 нет; ENSDF:
# %ε+%β⁺ = 100). Выходит строка `119TEm ур.2 → 119SB [15] 0.41`; `ChainPercColumn` верно
# дотягивает ветвь канала β⁺ до 100 остатком, но судьи — ε+β⁺ своего уровня в `l_decays` — нет, и
# `tools/check_parent_rule.py` §3г краснеет (замер на копии: «119TEm → 119SB: ветвь β⁺ расширена
# до 100, судьи нет» и «положительный контроль 5 из 6»). Сторож — в приёмке `check_all`; ослаблять
# его под свою запись нельзя. Остальные 18 рёбер сторожа не задевают. Журнал —
# `handover/handover-2026-10-14-p299-lost-branches-diverging-write.md`.
HELD = {("119I", "119TEm")}


def main():
    ap = argparse.ArgumentParser(description=u"П295/П299: 19 расходящихся рёбер «родитель → изомер»")
    ap.add_argument("--nucdb", default=m.DEFAULT_NUCDB)
    ap.add_argument("--schemedb", default=m.DEFAULT_SCHEMEDB, help=u"открывается mode=ro")
    ap.add_argument("--apply", action="store_true", help=u"ЗАПИСЬ в --nucdb (только копия)")
    ap.add_argument("--allow-live", action="store_true", help=u"запись в базу дерева/сборки — ТОЛЬКО по слову Amber")
    ap.add_argument("--include-held", action="store_true",
                    help=u"писать и удержанные (HELD: 119I → 119TEm) — только после решения Amber по сторожу §3г")
    a = ap.parse_args()
    if a.apply and m.is_live(a.nucdb) and not a.allow_live:
        sys.exit(u"⛔ %s — база дерева/сборки/AppData; запись только в копию (или --allow-live по слову Amber)" % a.nucdb)
    nuc = m.connect(a.nucdb, a.apply)
    sch = m.connect(a.schemedb, False)
    m.w(u"nucdb: %s (%s)", a.nucdb, u"ЗАПИСЬ" if a.apply else u"mode=ro")
    n0, fp0 = m.fingerprint(nuc)
    m.w(u"decay_chain до: %d строк, отпечаток %s", n0, fp0)
    # порядок: ветвь 119I уже починена (строка захвата 100, а не 49)
    r = nuc.execute("select perc from decay_chain where nucid = '119I' and l_seqno = 0 and daughter_nucid = '119TE'"
                    " and dec_type = '7'").fetchone()
    if r is None or m.to_float(r[0]) is None or m.to_float(r[0]) < 99.0:
        sys.exit(u"⛔ строка 119I → 119TE [7] = %s, а не 100: сначала tools/nucdb/fix_lost_branches.py" % (r and r[0]))
    cases, nodata = m.plan(nuc, sch, 0.0, True)
    sel = []
    seen = set()
    for c in cases:
        key = (c["p"], c["X"])
        if key not in PROPOSED:
            c["apply"] = False
            continue
        seen.add(key)
        if c["status"] == u"ЕСТЬ":
            m.w(u"  = %s → %s уже записано", c["p"], c["X"])
            c["apply"] = False
            continue
        if key in HELD and not a.include_held:
            m.w(u"  ⏸ %s → %s: ход %s %% — УДЕРЖАНО (ветвь изомера только β⁺ без судьи, check_parent_rule §3г;"
                u" --include-held — после решения Amber)", c["p"], c["X"], m.num_text(c["f"]))
            c["apply"] = False
            continue
        f = PROPOSED[key]
        if f is not None:
            m.w(u"  %s → %s: ход %s %% → число оценщика %s %%", c["p"], c["X"], m.num_text(c["f"]), m.num_text(f))
            c["f"] = f
        else:
            m.w(u"  %s → %s: ход %s %% (BR %s)", c["p"], c["X"], m.num_text(c["f"]), m.num_text(c["br"]))
        # строка P → D, которую расщеплять: захват (7/1), а не β⁺ (15)
        d, pe, pr, dt, lv, real = c["br_row"]
        if dt == "15":
            alt = [r for r in m.edges_of(nuc, c["p"]) if r[0] == c["D"] and r[3] in ("7", "1") and r[5]]
            if alt:
                c["br_row"] = max(alt, key=lambda r: r[1] or 0.0)
                m.w(u"    расщепляется строка канала %s (%s %%), а не β⁺", c["br_row"][3], c["br_row"][2])
        c["apply"] = True
        sel.append(c)
    missing = sorted(set(PROPOSED) - seen)
    if missing:
        sys.exit(u"⛔ план не нашёл пар: %s" % ", ".join("%s → %s" % k for k in missing))
    m.w(u"к записи: %d из %d (удержано: %s)", len(sel), len(PROPOSED),
        u"нет" if a.include_held else ", ".join("%s → %s" % k for k in sorted(HELD)))
    if a.apply:
        zeros = []
        done = m.apply(nuc, cases, zeros)
        if zeros:
            nuc.rollback()
            sys.exit(u"⛔ расщепление довело до нуля строки %s — не ожидалось, запись отменена" % zeros)
        nuc.commit()
        n1, fp1 = m.fingerprint(nuc)
        m.w(u"вставлено %d, изменено %d, уже было %d; decay_chain после: %d строк, отпечаток %s",
            done["ins"], done["upd"], done["skip"], n1, fp1)


if __name__ == "__main__":
    main()
