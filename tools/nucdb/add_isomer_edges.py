# -*- coding: utf-8 -*-
u"""П282 (14.10.2026): рёбра «родитель → изомер» в `nucdb.decay_chain` из ENSDF дерева.

РЕШЕНИЕ AMBER 14.10.2026, вопросником, дословно: «Достроить рёбра из ENSDF»
(вариант «Mo-99 → Tc-99m ≈ 87.6 % и т.д. — меняется база nucdb (только с твоего
слова на запись), прогон корпуса»). Находка — П266 (№7), журнал
`handover/handover-2026-10-14-p266-decay-data-recheck.md`.

ЧТО НЕ ТАК. `decay_chain` (выгрузка NuDat 3) ведёт ряды по ОСНОВНЫМ состояниям
дочек; изомер-дочь в поставке был ровно один — `234TH → 234PAm1` (после записи П294
14.10.2026 их 51). Линии изомера, который
распад родителя населяет, у родителя либо СЛОЖЕНЫ в `decay_radiations` (Cs-137
661.657, Zr-89 909, Cd-109 88.03 — там всё верно), либо НЕ лежат нигде, кроме
строк самого изомера — и тогда ряд родителя их теряет: Mo-99 без 140.5 кэВ.

ИСТОЧНИК ДОЛИ — ТОЛЬКО ДЕРЕВО: `schemedb.ensdf_levels / ensdf_gammas /
ensdf_feedings` (ENSDF2 ЛСРМ, импорт `tools/nucdb/import_ensdf.py`). Доля прохода
через изомер считается ХОДОМ ПО СХЕМЕ набора распада родителя: заселение каждого
уровня = питание + приток гамм сверху; уровень делит свой поток между выходящими
переходами пропорционально I_полн = TI, иначе RI·(1+α); уровень изомера —
конечный (дальше идёт его собственный распад). f_отн = заселение изомера / Σ
питаний набора — величина ОТНОСИТЕЛЬНАЯ внутри набора, нормировка NR/BR (запись
`N`, не втянута) не нужна. На распад родителя: f = BR · f_отн, где BR — доля ветви
`decay_chain` родителя в эту дочь по правилу приложения (`ChainPercColumn`).
Сверка независимым путём — балансом основного состояния с якорем на поставку
(как у `import_weak_gammas.py`): f_бал = BR − (прямое питание основного + приток
к нему мгновенных гамм по якорю); печатается рядом.

ЧТО СЧИТАЕТСЯ СЛУЧАЕМ. Родитель P из `decay_chain`, его ребро (правило приложения:
`DecayParentRule.ChainTable` + `ChainLevelClause`, текст читается из .cs, как в
`tools/CORPUS/scripts/chains.py`) в основное состояние D, изомер X того же
нуклида D (строка `nuclides` с меткой состояния строчными), уровень которого
нашёлся в наборе распада P → D по периоду, с f > 0. Сложено ли: сильнейшая
γ-линия X ищется у P (`decay_radiations`, правило уровня `LevelClause`) в допуске
max(1 кэВ, 4·10⁻⁴·E); выход у P ≥ 0.5·f·I_X — СЛОЖЕНО (ребро удвоило бы линию),
< 0.2·f·I_X — ПОТЕРЯНО, между — ЧАСТИЧНО (печатается, ребра нет).

ЧТО ПИШЕТСЯ (`--apply`): ПОТЕРЯНО, f ≥ `--min-f` (0.1 % = MinChainBranch), хоть одна линия изомера
≥ 0.01 % на распад родителя, сверка балансом не расходится (|f − f_бал| ≤ max(0.3 п.п., 15 %) или
сверять нечем; расходящиеся — ключом `--include-diverging`). Запись — РАСЩЕПЛЕНИЕМ ветви:
  * P → X: та же строка уровня и канала, что P → D, `perc` = f;
  * P → D: `perc` уменьшается на f (сумма ветвей P в нуклид D прежняя, как у
    прецедента поставки `234TH → 234PAm1` 100 % без строки в 234PA);
  * X → его дочери (если X в `decay_chain` родителем не стоит): ветви своего
    уровня X из `l_decays` (`perc_num` > 0), а нет их — строки `decay_chain` под
    именем D на уровне X (так поставка пишет распад изомера); IT — в D.
  ⛔ Голое «добавить P → X, P → D оставить» НЕ годится: ряд считает D дважды
  (Sn-126: Sb-126 100 % + Sb-126m 100 % при истине Sb-126 ≈ 19 %), а
  `CascadeAtomicData` получает ветвей на 100 % + f.
  Строки, которых нет, вставляются; уже лежащие (тот же ключ и то же число) —
  пропускаются: повторный прогон не меняет базу. Каждая вставка/правка печатается.

НУЛЕВЫЕ СТРОКИ РАСЩЕПЛЕНИЯ (П294). У родителя, весь распад которого в эту дочь идёт
через изомер (f = BR: `126SN`, `52FE`, `106SN`, `108SN`, `110SN`, `130SN`, `166HF`,
`73BR`, `85Y`, `94RU`, `96PD`, `123INm` — 12 на 14.10.2026), строка P → D после
расщепления остаётся с `perc` = 0 (карточка NucBase до П296 печатала её — «126SB 0»; с П296
14.10.2026 строк с долей ровно 0 карточка не показывает, в базе они остаются). Такая
строка УДАЛЯЕТСЯ — но только порождённая самим расщеплением в этом прогоне (нулевые
строки поставки — 25 строк «0»/«0.» на 14.10.2026 — не трогаются) и только если правило
приложения (`ChainTable` + `ChainLevelClause`) после удаления НЕ даёт ребра P → D иным путём
(иначе ряд удвоился бы); проверяется тут же, после каждого удаления, отказ — откат. Замер
14.10.2026: удалено 9, оставлено 3 — у `130SN`, `52FE`, `85Y` без нулевой строки своего уровня
правило берёт строку изомера родителя под тем же именем (уровни 2, 42, 1; см. `drop_zero_rows`).

⛔ ЗАПИСЬ В ЖИВУЮ БАЗУ — ТОЛЬКО ПО СЛОВУ AMBER («изменение баз — только через
Amber; разрешение на МЕТОД ≠ разрешение на ЗАПИСЬ»). Без `--apply` база открыта
`mode=ro`; `--apply` в файл дерева, каталога сборки или %AppData% отказывает без
`--allow-live`. Слово дано 14.10.2026 вопросником, дословно: «Записать + убрать
нулевые строки (Рекомендую)» — 51 ребро без расходящихся (их решено «Не писать,
разобрать по одному») и удаление нулевых строк расщепления; записано полосой П294
(журнал `handover/handover-2026-10-14-p294-isomer-edges-write.md`), метод и перечень —
П282 (`handover/handover-2026-10-14-p282-isomer-edges.md`).

    python tools/nucdb/add_isomer_edges.py                          # перечень, без записи (база дерева, ro)
    python tools/nucdb/add_isomer_edges.py --nucdb <копия> --apply  # запись в копию
    python tools/nucdb/add_isomer_edges.py --list 99MO,91SR         # подробно по родителям
    python tools/nucdb/add_isomer_edges.py --csv <файл>             # перечень всех пар в CSV
"""
import argparse
import csv
import io
import math
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(REPO, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(REPO, "BecquerelMonitor", "schemedb.sqlite")
RULE_CS = os.path.join(REPO, "BecquerelMonitor", "FullSpectrumAnalysis", "DecayParentRule.cs")
MANIFEST = os.path.join(REPO, "tools", "CORPUS", "corpus", "manifest.csv")

HL_TOL = math.log(1.25)       # период набора ENSDF против `nuclides` (как import_weak_gammas)
ISO_HL_TOL = math.log(1.5)    # период уровня изомера в наборе против `nuclides` изомера
FOLD_MIN = 0.5                # у родителя ≥ 0.5·f·I_X — сложено
LOST_MAX = 0.2                # у родителя < 0.2·f·I_X — потеряно
LINE_MIN = 0.01               # %, линии изомера на распад родителя — в перечень
ANCHOR_FRAC = 0.1             # якорь сверки — линии не слабее 10 % сильнейшей сопоставленной

# мода набора ENSDF → `dec_type` поставки (0 α, 1 EC+β⁺, 7 EC, 15 β⁺, 50 β⁺ ec, 2 β⁻, 3 IT)
MODES = {"B-": ("2",), "EC": ("1", "7", "15", "50"), "B+": ("1", "7", "15", "50"), "A": ("0",), "IT": ("3",)}

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")
    OUT.flush()


# ---------------------------------------------------------------------------
# Правило приложения — ЧИТАЕТСЯ из DecayParentRule.cs, своей копии нет
# ---------------------------------------------------------------------------

def _literals(body):
    body = re.sub(r"//[^\n]*", "", body)
    body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
    if "\\" in body:
        raise RuntimeError(u"в правиле появились escape-последовательности C#")
    return "".join(re.findall(r'"([^"]*)"', body))


def rule(name):
    with io.open(RULE_CS, encoding="utf-8-sig") as f:
        text = f.read()
    m = re.search(r"const\s+string\s+" + name + r"\s*=(.*?);", text, re.S)
    if not m:
        raise RuntimeError(u"в %s нет const string %s" % (RULE_CS, name))
    return _literals(m.group(1))


CHAIN_TABLE = rule("ChainTable")
CHAIN_LEVEL_CLAUSE = rule("ChainLevelClause")
CHAIN_PERC_COLUMN = rule("ChainPercColumn")
LEVEL_CLAUSE = rule("LevelClause")


def connect(path, write):
    if not os.path.isfile(path):
        sys.exit(u"нет базы: %s" % path)
    uri = "file:%s%s" % (path.replace("\\", "/"), "" if write else "?mode=ro")
    return sqlite3.connect(uri, uri=True)


def is_live(path):
    p = os.path.normcase(os.path.abspath(path))
    live = [os.path.normcase(os.path.abspath(REPO))]
    appdata = os.environ.get("APPDATA")
    if appdata:
        live.append(os.path.normcase(os.path.join(appdata, "BecqMoni")))
    return any(p.startswith(x + os.sep) for x in live)


def tol_kev(e):
    return max(1.0, 4e-4 * e)


def num_text(x):
    u"""Число записью без экспоненты и группировки, 4 значащих (как у поставки и import_weak_gammas)."""
    if x == 0:
        return "0"
    digits = max(0, 3 - int(math.floor(math.log10(abs(x)))))
    s = ("%." + str(digits) + "f") % x
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    return s


def to_float(t):
    try:
        return float(t)
    except (TypeError, ValueError):
        return None


ISO_RE = re.compile(r"^(\d+)([A-Z]+)([a-z]\w*)$")
GROUND_RE = re.compile(r"^(\d+)([A-Z]+)$")


def dataset_mode(dsid):
    m = re.match(r"^\s*\S+\s+(\S+)\s+DECAY", dsid)
    if not m:
        return None
    return m.group(1) if m.group(1) in MODES else None


def hl_of(nuc, nucid):
    u"""Период СВОЕГО уровня (nuclides.l_seqno), первая строка по l_seqno — как TryHalfLifeSeconds."""
    r = nuc.execute("select l_seqno, half_life_sec from nuclides where nucid = ? order by l_seqno limit 1",
                    (nucid,)).fetchone()
    return (r[0], r[1]) if r else (None, None)


def edges_of(nuc, p):
    u"""Рёбра P правилом приложения: [(daughter, perc_eff, perc_raw, dec_type, l_seqno, в decay_chain?)]."""
    rows = nuc.execute(
        "select daughter_nucid," + CHAIN_PERC_COLUMN + ", perc, dec_type, l_seqno from" + CHAIN_TABLE + " d"
        " where nucid = $n and perc not null" + CHAIN_LEVEL_CLAUSE, {"n": p}).fetchall()
    out = []
    for d, pe, pr, dt, lv in rows:
        real = nuc.execute("select 1 from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                           " and dec_type = ?", (p, lv, d, dt)).fetchone() is not None
        out.append((d, to_float(pe), pr, dt, lv, real))
    return out


def lines_of(nuc, nucid, types=("G",)):
    q = ("select type_a, energy_num, intensity_num from decay_radiations where parent_nucid = $n"
         " and type_a in (%s) and energy_num > 0 and intensity_num > 0" % ",".join("'%s'" % t for t in types)
         + LEVEL_CLAUSE)
    return nuc.execute(q, {"n": nucid}).fetchall()


# ---------------------------------------------------------------------------
# Ход по схеме набора ENSDF
# ---------------------------------------------------------------------------

def scheme(sch, did):
    levels = {}
    for seq, e, hl, meta in sch.execute(
            "select seq, energy_kev, half_life_sec, metastable from ensdf_levels where dataset_id = ?", (did,)):
        levels[seq] = (e, hl, meta)
    gammas = sch.execute(
        "select id, from_level_seq, to_level_seq, energy_kev, intensity, conv_coef, total_intensity"
        " from ensdf_gammas where dataset_id = ?", (did,)).fetchall()
    feed = {}
    feed_ids = {}
    for fid, lv, kind, ib, ie, ti in sch.execute(
            "select id, level_seq, kind, intensity, intensity_ec, intensity_total from ensdf_feedings"
            " where dataset_id = ?", (did,)):
        if lv is None:
            continue
        if kind == "E":
            v = ti if ti is not None else (ib or 0.0) + (ie or 0.0)
        else:
            v = ib
        if v:
            feed[lv] = feed.get(lv, 0.0) + v
            feed_ids.setdefault(lv, []).append(fid)
    return levels, gammas, feed, feed_ids


def walk(levels, gammas, feed, terminal):
    u"""Заселение уровней ходом сверху вниз. terminal — уровни изомеров (конечные)."""
    out = {}
    alpha = {}
    flow = {}
    placed = 0
    for gid, fr, to, e, ri, a, ti in gammas:
        if fr is None:
            continue
        if to is None and fr in levels and e:
            # Привязка импорта — допуск max(1.5 кэВ, 0.2 %); уровни ENSDF2 бывают округлены
            # («345.0» у 110SN), и гамма повисает. Здесь — второй, грубый допуск max(3 кэВ, 1 %).
            target = (levels[fr][0] or 0.0) - e
            best = None
            for k, v in levels.items():
                if v[0] is None or k == fr:
                    continue
                d = abs(v[0] - target)
                if d <= max(3.0, 0.01 * (levels[fr][0] or 0.0)) and (best is None or d < best[0]):
                    best = (d, k)
            if best is not None:
                to = best[1]
                placed += 1
        if ti is not None and ti > 0:
            tot = ti
        elif ri is not None and ri > 0:
            tot = ri * (1.0 + (a or 0.0))
        else:
            tot = None
        out.setdefault(fr, []).append((to, tot, gid))
        alpha[gid] = a or 0.0
    pop = dict((k, 0.0) for k in levels)
    for k, v in feed.items():
        pop[k] = pop.get(k, 0.0) + v
    lost = 0.0
    order = sorted(levels, key=lambda k: -(levels[k][0] or 0.0))
    for lv in order:
        if lv in terminal:
            continue
        e = levels[lv][0] or 0.0
        if e <= 0.0:
            continue
        p = pop.get(lv, 0.0)
        if p <= 0.0:
            continue
        outs = [(to, t, gid) for to, t, gid in out.get(lv, []) if t]
        s = sum(t for to, t, gid in outs)
        if s <= 0.0:
            # выход у уровня ОДИН и его интенсивность не записана (141EU: 1.58 кэВ в основное) — весь поток туда
            only = [to for to, t, gid in out.get(lv, []) if to is not None and to in levels]
            if len(out.get(lv, [])) == 1 and len(only) == 1:
                pop[only[0]] = pop.get(only[0], 0.0) + p
                flow[out[lv][0][2]] = flow.get(out[lv][0][2], 0.0) + p
                continue
            lost += p
            continue
        for to, t, gid in outs:
            # поток перехода (γ + конверсия); γ-квантов из него — поток / (1 + α)
            flow[gid] = flow.get(gid, 0.0) + p * t / s
            if to is None or to not in levels:
                lost += p * t / s
            else:
                pop[to] = pop.get(to, 0.0) + p * t / s
    gflow = dict((gid, v / (1.0 + alpha.get(gid, 0.0))) for gid, v in flow.items())
    return pop, lost, placed, gflow


def anchor(nuc, sch, did, p, codes):
    u"""Якорь RI → % на распад родителя (как import_weak_gammas): медиана I_nucdb / RI."""
    db = [r for r in nuc.execute(
        "select energy_num, intensity_num, dec_type from decay_radiations where parent_nucid = $n"
        " and type_a = 'G' and energy_num > 0 and intensity_num > 0 and dr_pk < 100001" + LEVEL_CLAUSE,
        {"n": p}).fetchall() if r[2] in codes]
    ens = sch.execute("select energy_kev, intensity from ensdf_gammas where dataset_id = ? and intensity > 0"
                      " and energy_kev > 0", (did,)).fetchall()
    pairs = []
    for i, d in enumerate(db):
        for j, g in enumerate(ens):
            de = abs(d[0] - g[0])
            if de <= tol_kev(g[0]):
                pairs.append((de, i, j))
    pairs.sort()
    ui, uj, m = set(), set(), []
    for de, i, j in pairs:
        if i in ui or j in uj:
            continue
        ui.add(i)
        uj.add(j)
        m.append((i, j))
    if not m:
        return None
    strong = max(db[i][1] for i, j in m)
    ratios = sorted(db[i][1] / ens[j][1] for i, j in m if db[i][1] >= ANCHOR_FRAC * strong)
    k = len(ratios)
    return ratios[k // 2] if k % 2 else 0.5 * (ratios[k // 2 - 1] + ratios[k // 2])


# ---------------------------------------------------------------------------
# План
# ---------------------------------------------------------------------------

def canonical(nuc, xs, level_hl):
    u"""Из изомеров одного нуклида — тот, чей период ближе к уровню набора; при «m»/«m1» одного
    состояния — тот, к которому сводит имя приложение (`NucidOf`: «Tc-99m» → точное «99TCm», иначе «…m1»)."""
    best = None
    for nucid, lv, hl in xs:
        if not hl or not level_hl or hl <= 0 or level_hl <= 0:
            continue
        d = abs(math.log(hl / level_hl))
        if d > ISO_HL_TOL:
            continue
        rank = (round(d, 6), nucid)
        if best is None or rank < best[0]:
            best = (rank, nucid, lv, hl)
    if best is not None and best[1].endswith("m1"):
        # «m1» и «m» — одно состояние в двух соглашениях базы (AMBER78), если периоды совпали до 1 %:
        # берётся то имя, к которому сводит подпись приложение (`NucidOf`: точное «…m» первым)
        twin = best[1][:-1]
        for nucid, lv, hl in xs:
            if nucid == twin and hl and abs(math.log(hl / best[3])) <= math.log(1.01):
                best = (best[0], nucid, lv, hl)
                break
    return best


def twin_conflict(xs, nucid):
    u"""(П266 №8) «m1» и «m» под одним нуклидом — РАЗНЫЕ состояния: подпись «X-Am» уводит не туда."""
    if not nucid.endswith("m1"):
        return None
    hl = dict((n, h) for n, lv, h in xs)
    twin = nucid[:-1]
    if twin in hl and hl[twin] and hl.get(nucid) and abs(math.log(hl[twin] / hl[nucid])) > math.log(1.01):
        return twin
    return None


def corpus_nuclides(nuc):
    u"""Нуклиды корпуса и их ряды (manifest.csv, столбцы nuclides/chains) — грубо, по написанию."""
    names = set()
    with io.open(MANIFEST, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            for col in ("nuclides", "chains"):
                for tok in re.split(r"[;+ ,]+", row.get(col) or ""):
                    if tok:
                        names.add(tok)
    known = dict((r[0].upper(), r[0]) for r in nuc.execute("select nucid from nuclides where nucid is not null"))
    out = set()
    for tok in names:
        m = re.match(r"^([A-Za-z]+)-?(\d+)(m\d*)?$", tok) or None
        if m:
            key = m.group(2) + m.group(1).upper() + (m.group(3) or "").upper()
        else:
            key = tok.upper()
        if key in known:
            out.add(known[key])
    return out, sorted(names)


def plan(nuc, sch, min_f, diverging=False):
    isomers = {}
    for nucid, lv, hl in nuc.execute("select nucid, l_seqno, half_life_sec from nuclides where nucid is not null"):
        m = ISO_RE.match(nucid)
        if m:
            isomers.setdefault(m.group(1) + m.group(2), []).append((nucid, lv, hl))
    datasets = {}
    for did, dnuc, dsid, pn, phl, pl, src in sch.execute(
            "select id, nucid, dsid, parent_nucid, parent_hl_sec, parent_l_seqno, source_file from ensdf_datasets"
            " where parent_nucid is not null"):
        datasets.setdefault((pn.upper(), dnuc.upper()), []).append((did, dsid, phl, pl, src))
    parents = [r[0] for r in nuc.execute("select distinct nucid from decay_chain order by nucid")]
    cases, nodata = [], []
    for p in parents:
        plv, pt = hl_of(nuc, p)
        edges = edges_of(nuc, p)
        if not edges:
            continue
        mb = GROUND_RE.match(p)
        pbase = p if mb else (ISO_RE.match(p).group(1) + ISO_RE.match(p).group(2) if ISO_RE.match(p) else None)
        # семьи дочек: основное D → его рёбра (в D и уже проложенные в изомеры D)
        fam = {}
        for d, pe, pr, dt, lv, real in edges:
            if d == p:
                continue
            md = GROUND_RE.match(d)
            mi = ISO_RE.match(d)
            ground = d if md else (mi.group(1) + mi.group(2) if mi else None)
            if ground is None:
                continue
            fam.setdefault(ground, []).append((d, pe, pr, dt, lv, real))
        for D, rows in fam.items():
            xs = isomers.get(D, [])
            if not xs:
                continue
            ground_rows = [r for r in rows if r[0] == D]
            iso_rows = dict((r[0], r) for r in rows if r[0] != D)
            if not ground_rows:
                # ряд идёт только в изомер (234TH → 234PAm1) — поставка, не трогаем
                continue
            # Набор распада P → D: по моде (EC и B+ — одна), ближайший по периоду родителя. Допуск
            # ln 1.25 (как import_weak_gammas); шире — до ×10 — только набору ОСНОВНОГО состояния
            # (parent_l_seqno пуст) у родителя в основном состоянии: у долгоживущих период переоценён
            # (Sn-126 1·10⁵ → 2.3·10⁵ лет, Se-79), а спутать набор с изомером родителя тут не с чем.
            bymode = {}
            for did, dsid, phl, pl, src in datasets.get((pbase, D), []):
                mode = dataset_mode(dsid)
                if mode is None or not phl or not pt or not (pt > 0) or math.isinf(pt):
                    continue
                if pl is not None and pl != (plv or 0):
                    continue   # набор ИЗОМЕРА родителя (191HG «(50.8 M)» — уровень 35), не его распад
                d = abs(math.log(phl / pt))
                key = "EC" if mode in ("EC", "B+") else mode
                ok = d <= HL_TOL or (pl is None and (plv or 0) == 0 and d <= math.log(10.0))
                if not ok:
                    continue
                if key not in bymode or d < bymode[key][0]:
                    bymode[key] = (d, (did, dsid, mode, src, phl / pt))
            sets = [v[1] for k, v in sorted(bymode.items())]
            if not sets:
                # доли в дереве нет: печатается лишь, если у изомера есть γ, а у родителя её нет
                pl_lines = lines_of(nuc, p)
                for nucid, lv, hl in xs:
                    xl = lines_of(nuc, nucid)
                    if not xl:
                        continue
                    e, i = max(((r[1], r[2]) for r in xl), key=lambda t: t[1])
                    have = [r[2] for r in pl_lines if abs(r[1] - e) <= tol_kev(e)]
                    nodata.append((p, pt, D, nucid, hl, e, i, max(have) if have else 0.0))
                continue
            for did, dsid, mode, src, hl_ratio in sets:
                codes = MODES[mode]
                br_rows = [r for r in ground_rows if r[3] in codes]
                if not br_rows:
                    br_rows = ground_rows
                levels, gammas, feed, feed_ids = scheme(sch, did)
                sfeed = sum(feed.values())
                if sfeed <= 0:
                    continue
                # уровни изомеров D в наборе
                matched = {}
                for lvseq, (e, lhl, meta) in levels.items():
                    if not lhl or lhl <= 0 or not e or e <= 0:
                        continue
                    best = canonical(nuc, xs, lhl)
                    if best is None:
                        continue
                    if lhl < 1e-6 and not meta:
                        continue
                    prev = matched.get(best[1])
                    if prev is None or abs(math.log(lhl / best[3])) < prev[1]:
                        matched[best[1]] = (lvseq, abs(math.log(lhl / best[3])), best[2], best[3])
                if not matched:
                    continue
                # Конечный уровень хода — изомер, у которого в nucdb есть СВОЙ распад (линии или
                # ветви l_decays). Изомер без данных (126SBm2: 11 с, ни строк, ни ветвей) — сквозной:
                # его поток идёт дальше по переходам набора (у Sn-126 — в 126SBm1).
                def has_data(x):
                    return (nuc.execute("select 1 from decay_radiations where parent_nucid = ? limit 1", (x,)).fetchone()
                            or nuc.execute("select 1 from l_decays where nucid = ? and perc_num > 0 limit 1",
                                           (x,)).fetchone()) is not None
                for x in [x for x in matched if not has_data(x)]:
                    del matched[x]
                if not matched:
                    continue
                terminal = set(v[0] for v in matched.values())
                pop, lost, placed, gflow = walk(levels, gammas, feed, terminal)
                ground_seq = [k for k, v in levels.items() if (v[0] or 0.0) == 0.0]
                k_anchor = anchor(nuc, sch, did, p, codes)
                for X, (mseq, dl, xlv, xhl) in sorted(matched.items()):
                    f_rel = pop.get(mseq, 0.0) / sfeed
                    already = iso_rows.get(X)
                    # BR — вся ветвь P в нуклид D этой моды: строка в D плюс уже проложенные рёбра в
                    # изомеры D (повторный прогон по записанной копии даёт ту же f)
                    br = sum((r[1] or 0.0) for r in br_rows) + sum(
                        (r[1] or 0.0) for r in iso_rows.values() if r[3] in codes)
                    f = br * f_rel
                    # сверка балансом основного (якорь на поставку)
                    f_bal = None
                    if k_anchor is not None and ground_seq:
                        g0 = ground_seq[0]
                        prompt = 0.0
                        for gid, fr, to, e, ri, a, ti in gammas:
                            if to == g0 and fr not in terminal and fr is not None:
                                tot = ti if (ti is not None and ti > 0) else (ri * (1.0 + (a or 0.0)) if ri else 0.0)
                                prompt += tot
                        f_bal = br - br * feed.get(g0, 0.0) / sfeed - k_anchor * prompt
                        if len(terminal) > 1:
                            f_bal = None   # несколько изомеров — баланс общий, не по одному
                    # сложено ли
                    xl = lines_of(nuc, X)
                    xg = sorted(((r[1], r[2]) for r in xl), key=lambda t: -t[1])
                    pl_lines = lines_of(nuc, p)
                    status, fold_e, fold_i, prompt_line, ref = None, None, None, 0.0, None
                    lost_frac = lost / sfeed
                    if already is not None:
                        status = "ЕСТЬ"
                    elif not xg:
                        status = "НЕТ_γ"
                    elif f <= 0 and lost_frac > 0.5:
                        status = "НЕ_ОПРЕДЕЛЕНО"
                    elif f <= 0:
                        status = "НЕ_НАСЕЛЯЕТ"
                    else:
                        # Опорная линия сверки — СОБСТВЕННАЯ линия изомера: та, что не совпадает ни с
                        # одной гаммой мгновенной части набора P (переходы не из уровня изомера). Её выход
                        # у P — только проход через изомер (Bi-207: 1063.66, а не 569.70, которую дают и
                        # мгновенные распады). Годится, если поставка её не срезала бы: f·I ≥ 1.2 %
                        # сильнейшей гаммы P (порог среза NuDat, AMBER151).
                        nonterm = [(ge, ri) for gid, fr, to, ge, ri, a, ti in gammas
                                   if ge and fr is not None and fr not in terminal]
                        p_max = max([r[2] for r in pl_lines] or [0.0])
                        own = [(e0, i0) for e0, i0 in xg
                               if not any(abs(ge - e0) <= tol_kev(e0) for ge, ri in nonterm)
                               and f / 100.0 * i0 >= 0.012 * p_max]
                        if own:
                            e, i = max(own, key=lambda t: t[1])
                            ref_kind = u"своя линия изомера"
                        else:
                            e, i = xg[0]
                            ref_kind = u"сильнейшая, с вычетом мгновенной части"
                            # Мгновенная часть той же линии у самого родителя (переход каскада ниже
                            # изомера: Mo-99 140.511 4.5 %, Sb-125 35.49) — из того же хода по схеме:
                            # γ-поток перехода МИМО изомера, на распад родителя. Сложенным считается
                            # ИЗБЫТОК выхода у P над ней. (Не RI набора по якорю: у Os-191 набор несёт и
                            # распад изомера, и RI 129.43 — это весь поток, мгновенной части там ноль.)
                            for gid, fr, to, ge, ri, a, ti in gammas:
                                if ge and gid in gflow and abs(ge - e) <= tol_kev(e):
                                    prompt_line += br * gflow[gid] / sfeed
                        have = sorted(((abs(r[1] - e), r[1], r[2]) for r in pl_lines if abs(r[1] - e) <= tol_kev(e)))
                        y = f / 100.0 * i
                        fold_i = have[0][2] if have else 0.0
                        fold_e = have[0][1] if have else None
                        ref = (e, i, ref_kind)
                        excess = fold_i - prompt_line
                        if excess >= FOLD_MIN * y:
                            status = "СЛОЖЕНО"
                        elif excess < LOST_MAX * y:
                            status = "ПОТЕРЯНО"
                        else:
                            status = "ЧАСТИЧНО"
                    lost_lines = [(e, f / 100.0 * i) for e, i in xg if f / 100.0 * i >= LINE_MIN]
                    notes = []
                    if f_bal is None:
                        check = "нет"
                    elif abs(f - f_bal) <= max(0.3, 0.15 * f):
                        check = "сошлось"
                    else:
                        check = "расходится"
                    if ref is not None:
                        notes.append(u"сверка сложения по %.3f кэВ (%s): у изомера %s %%, у родителя %s %%%s"
                                     % (ref[0], ref[2], num_text(ref[1]), num_text(fold_i or 0.0),
                                        u", мгновенная часть %s %%" % num_text(prompt_line) if prompt_line > 0 else u""))
                    if placed:
                        notes.append(u"привязано грубым допуском гамм: %d" % placed)
                    if lost / sfeed > 0.01:
                        notes.append(u"поток %.0f %% питаний ушёл в уровни без интенсивности выхода/без привязки"
                                     % (100.0 * lost / sfeed))
                    unpl = sum((g[4] or 0.0) for g in gammas if g[2] is None)
                    allri = sum((g[4] or 0.0) for g in gammas)
                    if allri > 0 and unpl / allri > 0.02:
                        notes.append(u"непривязанных гамм %.0f %% ΣRI" % (100.0 * unpl / allri))
                    if abs(sfeed - 100.0) > 5.0:
                        notes.append(u"Σпитаний %.4g" % sfeed)
                    lsum = nuc.execute("select max(perc_num) from l_decays where nucid = ? and daughter_nucid = ?"
                                       " and perc_num > 0", (p, D)).fetchone()[0]
                    if lsum is not None and abs(lsum - br) > 0.5:
                        notes.append(u"BR ряда %s при %s по l_decays — f посчитана от BR ряда" % (num_text(br), num_text(lsum)))
                    if abs(math.log(hl_ratio)) > HL_TOL:
                        notes.append(u"период родителя в наборе ×%.2f против nuclides (набор основного состояния)" % hl_ratio)
                    tc = twin_conflict(xs, X)
                    if tc:
                        notes.append(u"имя: «%s» — другое состояние, подпись приложения уводит в него (П266 №8)" % tc)
                    if X in iso_rows and False:
                        pass
                    cases.append(dict(
                        p=p, plv=plv, pt=pt, D=D, X=X, xlv=xlv, xt=xhl, mode=mode, did=did, dsid=dsid, src=src,
                        mseq=mseq, mE=levels[mseq][0], f_rel=f_rel, br=br, f=f, f_bal=f_bal, sfeed=sfeed,
                        lostflow=lost / sfeed, status=status, fold_e=fold_e, fold_i=fold_i, xg=xg,
                        lost_lines=lost_lines, br_row=max(br_rows, key=lambda r: r[1] or 0.0), check=check,
                        notes=notes, ref=ref,
                        feed_ids=feed_ids.get(mseq, []),
                        apply=(status == "ПОТЕРЯНО" and f >= min_f and bool(lost_lines)
                               and (check != "расходится" or diverging))))
    return cases, nodata


# ---------------------------------------------------------------------------
# Ветви изомера X
# ---------------------------------------------------------------------------

def isomer_branches(nuc, X, xlv, D):
    rows = nuc.execute(
        "select daughter_nucid, perc, perc_num, dec_type from l_decays where nucid = ? and l_seqno = ?"
        " and daughter_nucid is not null and perc_num > 0 and daughter_nucid <> nucid", (X, xlv)).fetchall()
    if rows:
        best = {}
        for d, pt, pn, dt in rows:
            # одна строка на дочь — наибольшая (как ChainTable: иначе ε+β⁺ и β⁺ сложились бы)
            if d not in best or pn > best[d][1] or (pn == best[d][1] and dt < best[d][2]):
                best[d] = (pt, pn, dt)
        out = []
        for d, (pt, pn, dt) in sorted(best.items()):
            text = pt if to_float(pt) == pn else num_text(pn)
            out.append((d, str(dt), text, "l_decays %s ур. %d" % (X, xlv)))
        return out
    rows = nuc.execute("select daughter_nucid, dec_type, perc from decay_chain where nucid = ? and l_seqno = ?",
                       (D, xlv)).fetchall()
    out = []
    for d, dt, pc in rows:
        if to_float(pc) is None or not to_float(pc) > 0:
            continue
        out.append((D if d == D else d, dt, pc, "decay_chain %s ур. %d" % (D, xlv)))
    return out


def apply(nuc, cases, zeros=None):
    u"""Записать рёбра плана. `zeros` — список, куда кладутся строки P → D, которые
    расщепление этого прогона довело до нуля: (P, уровень, D, канал, X)."""
    cur = nuc.cursor()
    done = {"ins": 0, "upd": 0, "skip": 0}
    for c in cases:
        if not c["apply"]:
            continue
        p, X, D = c["p"], c["X"], c["D"]
        d, pe, pr, dt, lv, real = c["br_row"]
        if not real:
            w(u"  ⚠ %s → %s: ребро достроено ChainTable из l_decays, строки decay_chain нет — пропуск", p, D)
            continue
        if dt == "15":
            w(u"  ⚠ %s → %s: канал β⁺ (15), perc — доля позитронов; расщеплять нечего — пропуск", p, D)
            continue
        f_text = num_text(c["f"])
        have = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                           " and dec_type = ?", (p, lv, X, dt)).fetchone()
        if have is not None:
            w(u"  = %s ур.%d → %s [%s] %s — уже есть", p, lv, X, dt, have[0])
            done["skip"] += 1
        else:
            cur_perc = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                                   " and dec_type = ?", (p, lv, D, dt)).fetchone()[0]
            pr = cur_perc
            old = to_float(cur_perc)
            # из ЗАПИСАННЫХ чисел: сумма строк P → D и P → X равна прежней строке до записи
            new = old - float(f_text)
            if new < 1e-9 * max(1.0, old):
                new = 0.0
            # знаков после точки — сколько у вычитаемых, чтобы строка P → D + строка P → X
            # давали прежнее число ТОЧНО (100 − 1.173 = 98.827, а не «98.83»: сумма 100.003)
            places = max(len(t.split(".")[1]) if "." in t else 0 for t in (cur_perc.strip(), f_text))
            new_text = ("%." + str(places) + "f") % new
            if "." in new_text:
                new_text = new_text.rstrip("0").rstrip(".")
            if new_text in ("", "-0"):
                new_text = "0"
            cur.execute("insert into decay_chain (nucid, l_seqno, daughter_nucid, dec_type, perc) values (?,?,?,?,?)",
                        (p, lv, X, dt, f_text))
            w(u"  + %s ур.%d → %s [%s] perc %s   (ENSDF набор %d «%s», уровень %d %.3f кэВ)",
              p, lv, X, dt, f_text, c["did"], c["dsid"], c["mseq"], c["mE"])
            cur.execute("update decay_chain set perc = ? where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                        " and dec_type = ?", (new_text, p, lv, D, dt))
            w(u"  ~ %s ур.%d → %s [%s] perc %s → %s", p, lv, D, dt, pr, new_text)
            done["ins"] += 1
            done["upd"] += 1
            if new_text == "0" and zeros is not None:
                zeros.append((p, lv, D, dt, X))
        if nuc.execute("select 1 from decay_chain where nucid = ?", (X,)).fetchone() is None or \
                nuc.execute("select 1 from decay_chain where nucid = ? and l_seqno = ?", (X, c["xlv"])).fetchone():
            for y, ydt, ytext, src in isomer_branches(nuc, X, c["xlv"], D):
                h = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                                " and dec_type = ?", (X, c["xlv"], y, ydt)).fetchone()
                if h is not None:
                    w(u"  = %s ур.%d → %s [%s] %s — уже есть", X, c["xlv"], y, ydt, h[0])
                    done["skip"] += 1
                    continue
                cur.execute("insert into decay_chain (nucid, l_seqno, daughter_nucid, dec_type, perc)"
                            " values (?,?,?,?,?)", (X, c["xlv"], y, ydt, ytext))
                w(u"  + %s ур.%d → %s [%s] perc %s   (ветвь изомера: %s)", X, c["xlv"], y, ydt, ytext, src)
                done["ins"] += 1
    return done


def zero_rows(nuc):
    u"""Все строки `decay_chain` с ЧИСЛОВЫМ нулём в `perc` («0», «0.», «0.0»…); «?» и «LT 4 4»
    (`cast` даёт 0, но это «доля неизвестна») сюда не входят."""
    out = []
    for row in nuc.execute("select nucid, l_seqno, daughter_nucid, dec_type, perc from decay_chain"
                           " where perc is not null order by nucid, l_seqno, daughter_nucid, dec_type"):
        v = to_float(row[4])
        if v is not None and v == 0.0:
            out.append(row)
    return out


def drop_zero_rows(nuc, zeros):
    u"""(П294) Удалить строки P → D, которые расщепление ЭТОГО прогона довело до нуля.

    Строка удаляется, только если (а) она всё ещё числовой нуль, (б) рядом стоит её пара
    P → X того же уровня и канала с долей > 0, (в) правило приложения (`ChainTable` +
    `ChainLevelClause`) после удаления не даёт P ни одной строки в D и прочие его рёбра —
    те же, что были (иначе удаление открыло бы достройку ребра P → D из `l_decays`, и ряд
    удвоился бы). Не выполнено (в) — откат этой строки и печать ⛔.

    ⚠ Замер 14.10.2026 (П294): у трёх из двенадцати — `130SN`, `52FE`, `85Y` — нулевая строка
    уровня 0 НЕ лишняя. Под тем же именем родителя в ту же дочь лежит строка его изомера (130SNm —
    уровень 2, 52FEm — 42, 85Ym — 1, у каждой 100 %), и `ChainLevelClause` выбирает её запасным
    ходом «наименьший уровень тройки», как только строки своего уровня в D не станет: ряд получил
    бы P → D 100 % рядом с P → X 100 %. Нулевая строка своего уровня держит выбор на уровне 0 —
    она остаётся.

    Возвращает счётчики и список оставленных нулевых строк (как их печатает `zero_rows`)."""
    cur = nuc.cursor()
    done = {"del": 0, "kept": 0, "kept_rows": []}
    for p, lv, D, dt, X in zeros:
        row = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                          " and dec_type = ?", (p, lv, D, dt)).fetchone()
        if row is None:
            continue
        if to_float(row[0]) != 0.0:
            w(u"  ⚠ %s ур.%d → %s [%s] perc %s — уже не нуль, не трогаю", p, lv, D, dt, row[0])
            done["kept"] += 1
            continue
        pair = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                           " and dec_type = ?", (p, lv, X, dt)).fetchone()
        if pair is None or not (to_float(pair[0]) or 0.0) > 0.0:
            w(u"  ⚠ %s ур.%d → %s [%s]: пары → %s с долей > 0 нет — не трогаю", p, lv, D, dt, X)
            done["kept"] += 1
            done["kept_rows"].append((p, lv, D, dt, row[0]))
            continue
        before = sorted((d, pe, dt2, lv2) for d, pe, pr, dt2, lv2, real in edges_of(nuc, p))
        cur.execute("savepoint dz")
        cur.execute("delete from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                    " and dec_type = ? and perc = ?", (p, lv, D, dt, row[0]))
        after = sorted((d, pe, dt2, lv2) for d, pe, pr, dt2, lv2, real in edges_of(nuc, p))
        expect = [r for r in before if not (r[0] == D and r[2] == dt and r[3] == lv)]
        if any(r[0] == D for r in after) or after != expect:
            cur.execute("rollback to dz")
            cur.execute("release dz")
            w(u"  ⛔ %s ур.%d → %s [%s] perc %s: после удаления правило приложения дало ветви %s вместо %s —"
              u" откат, строка оставлена", p, lv, D, dt, row[0], after, expect)
            done["kept"] += 1
            done["kept_rows"].append((p, lv, D, dt, row[0]))
            continue
        cur.execute("release dz")
        w(u"  − %s ур.%d → %s [%s] perc %s удалена (пара: → %s %s; ветви %s по правилу приложения: %s)",
          p, lv, D, dt, row[0], X, pair[0], p, ", ".join("%s %s" % (r[0], r[1]) for r in after))
        done["del"] += 1
    return done


def fingerprint(nuc):
    import hashlib
    h = hashlib.sha256()
    n = 0
    for row in nuc.execute("select * from decay_chain order by nucid, l_seqno, daughter_nucid, dec_type"):
        h.update(repr(row).encode("utf-8"))
        n += 1
    return n, h.hexdigest()[:16]


def fmt_t(t):
    if t is None:
        return "—"
    if math.isinf(t):
        return "стаб."
    for unit, s in (("y", 365.0 * 86400), ("d", 86400.0), ("h", 3600.0), ("m", 60.0), ("s", 1.0),
                    ("ms", 1e-3), ("us", 1e-6), ("ns", 1e-9)):
        if t >= s:
            return "%.4g %s" % (t / s, unit)
    return "%.3g s" % t


def main():
    ap = argparse.ArgumentParser(description=u"П282: рёбра родитель → изомер из ENSDF дерева")
    ap.add_argument("--nucdb", default=DEFAULT_NUCDB)
    ap.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    ap.add_argument("--apply", action="store_true", help=u"ЗАПИСЬ в --nucdb (только копия)")
    ap.add_argument("--allow-live", action="store_true", help=u"разрешить запись в базу дерева/сборки — ТОЛЬКО по слову Amber")
    ap.add_argument("--min-f", type=float, default=0.1, help=u"наименьшая доля прохода, %% на распад родителя (0.1 = MinChainBranch)")
    ap.add_argument("--include-diverging", action="store_true",
                    help=u"писать и случаи, где сверка балансом РАСХОДИТСЯ с ходом по схеме (по умолчанию — нет)")
    ap.add_argument("--csv", default="", help=u"перечень всех пар в CSV (по умолчанию не пишется)")
    ap.add_argument("--list", default="")
    a = ap.parse_args()
    if a.apply and is_live(a.nucdb) and not a.allow_live:
        sys.exit(u"⛔ %s — база дерева/сборки/AppData; запись только в копию (или --allow-live по слову Amber)" % a.nucdb)
    nuc = connect(a.nucdb, a.apply)
    sch = connect(a.schemedb, False)
    w(u"nucdb: %s (%s)", a.nucdb, u"ЗАПИСЬ" if a.apply else u"mode=ro")
    w(u"schemedb: %s (mode=ro)", a.schemedb)
    n0, fp0 = fingerprint(nuc)
    w(u"decay_chain до: %d строк, отпечаток %s", n0, fp0)
    z0 = zero_rows(nuc)
    w(u"строк decay_chain с числовым нулём в perc до: %d (их прогон не трогает; удаляются только нули,"
      u" порождённые расщеплением этого прогона)", len(z0))
    cases, nodata = plan(nuc, sch, a.min_f, a.include_diverging)
    corpus, corpus_names = corpus_nuclides(nuc)
    by = {}
    for c in cases:
        by.setdefault(c["status"], []).append(c)
    w(u"случаев «родитель → изомер дочки, набор ENSDF есть, уровень изомера найден»: %d", len(cases))
    for k in ("ПОТЕРЯНО", "ЧАСТИЧНО", "СЛОЖЕНО", "ЕСТЬ", "НЕТ_γ", "НЕ_НАСЕЛЯЕТ", "НЕ_ОПРЕДЕЛЕНО"):
        w(u"  %-12s %d", k, len(by.get(k, [])))
    w(u"к записи (ПОТЕРЯНО, f ≥ %s %%, линия изомера ≥ %s %% на распад родителя, сверка %s): %d",
      num_text(a.min_f), num_text(LINE_MIN), u"любая" if a.include_diverging else u"не расходится",
      sum(1 for c in cases if c["apply"]))
    w(u"родитель → изомер с γ, набора ENSDF в дереве нет: %d (у родителя линии изомера нет: %d)",
      len(nodata), sum(1 for r in nodata if r[7] <= 0))
    if a.csv:
        write_csv(a.csv, cases, nodata, corpus)
        w(u"перечень: %s", a.csv)
    report(a, cases, by, corpus)
    if a.apply:
        w(u"")
        w(u"ЗАПИСЬ:")
        # До неподвижной точки: изомер, ставший родителем (X → D2), сам может кормить изомер дочки
        # (123INm → 123SNm, 117CDm → 117INm) — второй проход находит и это, третий — ничего.
        total = {"ins": 0, "upd": 0, "skip": 0}
        zeros = []
        for rnd in range(1, 6):
            done = apply(nuc, cases, zeros)
            nuc.commit()
            for k in total:
                total[k] += done[k]
            w(u"проход %d: вставлено %d, изменено %d, уже было %d", rnd, done["ins"], done["upd"], done["skip"])
            if done["ins"] == 0 and done["upd"] == 0:
                break
            cases, nodata = plan(nuc, sch, a.min_f, a.include_diverging)
        w(u"нулевые строки расщепления (P → D, доля 0 — весь распад в D идёт через изомер): %d", len(zeros))
        dz = drop_zero_rows(nuc, zeros)
        nuc.commit()
        w(u"удалено %d, оставлено %d", dz["del"], dz["kept"])
        # Неподвижная точка ПОСЛЕ удаления: план по записанной базе не должен ничего писать.
        cases, nodata = plan(nuc, sch, a.min_f, a.include_diverging)
        cur = nuc.cursor()
        cur.execute("savepoint fixed")
        chk = apply(nuc, cases, None)
        cur.execute("rollback to fixed")
        cur.execute("release fixed")
        n1, fp1 = fingerprint(nuc)
        z1 = zero_rows(nuc)
        w(u"итого вставлено %d, изменено %d, удалено нулевых %d; decay_chain после: %d строк, отпечаток %s",
          total["ins"], total["upd"], dz["del"], n1, fp1)
        w(u"строк с числовым нулём после: %d (до: %d)", len(z1), len(z0))
        if set(z1) != set(z0) | set(dz["kept_rows"]):
            w(u"⛔ набор нулевых строк не тот: прибыло %s, убыло %s (оставлены расщеплением: %s)",
              sorted(set(z1) - set(z0)), sorted(set(z0) - set(z1)), dz["kept_rows"])
            sys.exit(1)
        if dz["kept_rows"]:
            w(u"нулевые строки расщепления ОСТАВЛЕНЫ (без них правило приложения двоит ряд): %s",
              ", ".join("%s ур.%d → %s" % (r[0], r[1], r[2]) for r in dz["kept_rows"]))
        if chk["ins"] or chk["upd"]:
            w(u"⛔ после удаления нулевых строк план снова пишет (вставок %d, правок %d) — неподвижной точки нет",
              chk["ins"], chk["upd"])
            sys.exit(1)
        w(u"неподвижная точка: повторный план — 0 вставок, 0 правок")


def write_csv(path, cases, nodata, corpus):
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        wr = csv.writer(f)
        wr.writerow(["status", "apply", "parent", "parent_level", "parent_T", "daughter", "isomer", "isomer_level",
                     "isomer_T", "mode", "BR_pct", "f_rel", "f_pct", "f_balance_pct", "sum_feed", "lost_flow",
                     "ensdf_dataset_id", "dsid", "ensdf_file", "isomer_level_seq", "isomer_level_keV",
                     "feeding_ids", "ref_isomer_gamma_keV", "isomer_I_pct", "parent_has_keV", "parent_has_pct",
                     "lost_lines_keV:pct_per_parent", "in_corpus", "in_FSA_chain", "check", "notes"])
        for c in sorted(cases, key=lambda c: (c["status"], c["p"], c["X"])):
            fsa = (c["f"] >= 50.0 and c["xt"] and c["pt"] and c["xt"] < c["pt"])
            wr.writerow([c["status"], int(c["apply"]), c["p"], c["plv"], fmt_t(c["pt"]), c["D"], c["X"], c["xlv"],
                         fmt_t(c["xt"]), c["mode"], num_text(c["br"]), "%.5f" % c["f_rel"], num_text(c["f"]),
                         "" if c["f_bal"] is None else num_text(c["f_bal"]), num_text(c["sfeed"]),
                         "%.4f" % c["lostflow"], c["did"], c["dsid"], c["src"], c["mseq"], c["mE"],
                         " ".join(str(i) for i in c["feed_ids"]),
                         c["ref"][0] if c["ref"] else (c["xg"][0][0] if c["xg"] else ""),
                         c["ref"][1] if c["ref"] else (c["xg"][0][1] if c["xg"] else ""),
                         c["fold_e"] if c["fold_e"] is not None else "", "" if c["fold_i"] is None else c["fold_i"],
                         "; ".join("%g:%s" % (e, num_text(y)) for e, y in c["lost_lines"][:8]),
                         int(c["p"] in corpus or c["X"] in corpus), int(bool(fsa)), c["check"], " | ".join(c["notes"])])
        for r in nodata:
            wr.writerow(["НЕТ_НАБОРА", 0, r[0], "", fmt_t(r[1]), r[2], r[3], "", fmt_t(r[4]), "", "", "", "", "", "",
                         "", "", "", "", "", "", "", r[5], r[6], "", r[7], "", int(r[0] in corpus or r[3] in corpus), ""])


def report(a, cases, by, corpus):
    w(u"")
    w(u"ПОТЕРЯНО (* — к записи):")
    for c in sorted(by.get("ПОТЕРЯНО", []), key=lambda c: -c["f"]):
        e, i = c["xg"][0]
        w(u"  %s%-8s (%s) → %-9s (%s)  f = %7s %%  [бал. %s]  BR %s  набор %d «%s» ур.%d %.2f кэВ  "
          u"%.3f кэВ: %s %% на распад родителя%s",
          "*" if c["apply"] else " ", c["p"], fmt_t(c["pt"]), c["X"], fmt_t(c["xt"]), num_text(c["f"]),
          "—" if c["f_bal"] is None else num_text(c["f_bal"]), num_text(c["br"]), c["did"], c["dsid"], c["mseq"],
          c["mE"], e, num_text(c["f"] / 100.0 * i), u"  КОРПУС" if (c["p"] in corpus or c["X"] in corpus) else "")
        if c["check"] != "сошлось" or c["notes"]:
            w(u"        сверка: %s; %s", c["check"], "; ".join(c["notes"]) or u"—")
    w(u"ЧАСТИЧНО:")
    for c in by.get("ЧАСТИЧНО", []):
        e, i = c["xg"][0]
        w(u"  %-8s → %-9s f = %s %%  %.3f кэВ: ждём %s, у родителя %s (%.3f кэВ)", c["p"], c["X"], num_text(c["f"]), e,
          num_text(c["f"] / 100.0 * i), num_text(c["fold_i"]), c["fold_e"])
    want = [x.strip() for x in a.list.split(",") if x.strip()]
    for c in cases:
        if c["p"] in want:
            w(u"--- %s → %s [%s]: набор %d «%s» (%s), Σпитаний %.4g, ушло мимо %.4f, уровень %d %.3f кэВ, f_отн %.5f,"
              u" BR %s, f %s, f_бал %s", c["p"], c["X"], c["status"], c["did"], c["dsid"], c["src"], c["sfeed"],
              c["lostflow"], c["mseq"], c["mE"], c["f_rel"], num_text(c["br"]), num_text(c["f"]),
              "—" if c["f_bal"] is None else num_text(c["f_bal"]))
            for e, y in c["lost_lines"][:10]:
                w(u"      %.3f кэВ  %s %% на распад родителя", e, num_text(y))


if __name__ == "__main__":
    main()
