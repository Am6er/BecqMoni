# -*- coding: utf-8 -*-
u"""П295 (14.10.2026): родители `nucdb.decay_chain`, у которых сумма долей ветвей по правилу
приложения НЕ 100 %, — перечень с причиной и починка ПОТЕРЯННЫХ ветвей на КОПИИ базы.

РЕШЕНИЕ AMBER 14.10.2026, вопросником, дословно: «Измерить масштаб и принести список
(Рекомендую)» — «Полоса ищет родителей с суммой ветвей заметно ниже 100 % и чинит на КОПИИ по
данным дерева, с корпусом; запись — отдельным словом.» Повод — находка П282 №1: у `119I` в
`decay_chain` только захват 49 %, ветвь β⁺ 51 % потеряна, ряд идёт на 49 % вместо 100 %.

ЧТО МЕРИТСЯ. Родитель P — каждый `nucid` из `decay_chain`. Его рёбра — ПРАВИЛОМ ПРИЛОЖЕНИЯ
(`DecayParentRule.ChainTable` + `ChainLevelClause` + `ChainPercColumn`, текст читается из .cs,
своей копии нет), петли «дочь = сам» сняты, доля читается числом и берётся > 0 — так читают
обходы рядов (`FsaSampleLibrary.ChainBranches`, `NucBaseFramework.GetChainBranches`). S — сумма
по рёбрам, C[d] — доля в дочь d.

С ЧЕМ СВЕРЯЕТСЯ — `l_decays` СВОЕГО уровня P (`nuclides.l_seqno`), моды ENSDF/NNDC с числами.
Ожидаемая доля в дочь T[d] — по соглашению ENSDF: задержанные моды (β⁻n, β⁻2n…, εp, βp, εα…)
— ЧАСТЬ своей основной моды (%B- = 100 включает %B-N), поэтому основная дочь семьи получает
V_семьи − Σ задержанных, а дочь задержанной моды — её долю. Значение семьи захвата — строка
«ec β+» (код 1), а нет её — «ec» (7) + «β+» (15). Моды без дочери (деление SF, εSF, кластеры)
дочери не дают — их доля законно уходит из суммы рядов.

ПОРОГ — 0.1 п.п. (`MinChainBranch` = 1e-3 приложения: ветвь ≥ 0.1 % обход рядов несёт, меньше —
отбрасывает сам; расхождение меньше порога приложению не видно).

КЛАССЫ (каждый родитель с |S − 100| > порога; одна главная причина, прочие — в заметках):
  ПОТЕРЯ_МОДЫ   — дочь есть в `l_decays` своего уровня с числом, ребра в неё нет;
  ЕС_БЕЗ_B+     — ребро захвата (код 7) несёт только %ε, а `l_decays` знает %ε+β⁺ (119I);
  ДЕЛЕНИЕ       — недостача = доля мод без дочери (SF и т. п.): законно, править нечего;
  НЕТ_В_ДЕРЕВЕ  — доля моды «?»/пуста — числа в дереве нет;
  ПОСТАВКА<100  — сама `l_decays` с числами даёт < 100 (поставка неполна);
  ЗАДЕРЖ_ДВАЖДЫ — S > 100: основная дочь получает всю моду, а задержанные идут сверху;
  ПОСТАВКА>100  — сама `l_decays` с числами даёт > 100 (пределы, AP, разные оценки);
  ЧУЖОЙ_УРОВЕНЬ — ребро в дочь, которой у своего уровня нет (строка изомера под именем
                  основного; у стабильных `87SR`/`58NI`/`180HF` — распад изомера).

ЧТО ЧИНИТ (`--apply`, ТОЛЬКО копия): класс ПОТЕРЯ_МОДЫ — вставкой недостающих строк
`decay_chain` (P, свой уровень, дочь, код моды `l_decays`, доля): потерянная основная мода —
долей V − Σ задержанных (знаков — сколько у слагаемых), задержанная — своей долей; вставка,
после которой сумма превысила бы 100 (моды поставки сами противоречивы или вставляемое — лишь
верхний предел), не делается (класс ПОТЕРЯ_НЕ_ЧИНИТСЯ). Класс ЕС_БЕЗ_B+ (`119I`) — ПРАВКОЙ
доли строки захвата (код 7) до ε+β⁺ семьи по `l_decays` (49 → 100), код остаётся: линии β⁺
таких родителей лежат в `decay_radiations` кодом 7, и вставка строки β⁺ (код 15) отдала бы
позитроны в сумматоре совпадений одной ε (511 кэВ у 119I 109.78 → 98 %, замерено). Прочие
строки поставки не правятся и не удаляются. Повтор — без изменений. Ключ `--fix-excess` (НЕ по
умолчанию) ещё и уменьшает долю основной дочери на Σ задержанных (классы ЗАДЕРЖ_ДВАЖДЫ,
ЗАДЕРЖ_БЕЗ_СТРОКИ) — правит строки поставки, это отдельное решение Amber.

⛔ ЗАПИСЬ В ЖИВУЮ БАЗУ — ТОЛЬКО ПО СЛОВУ AMBER. Без `--apply` база открыта `mode=ro`; `--apply`
в файл дерева, каталога сборки или %AppData% отказывает без `--allow-live`. Доли — только из
`l_decays` той же базы; где числа нет — «нет в дереве», ничего не подставляется.
Слово на запись дано 14.10.2026 вопросником, дословно: «Записать (Рекомендую)» (вопрос: «…у 141
родителя в decay_chain нет строки для части видов распада…, среди них I-119 (49 % вместо 100 %)
и Lu-166m (ряд Hf-166 0.925 → 1.0). На копии: 146 вставок + 1 правка, корпус побайтно rev46…
Записывать в живую базу?»); записано полосой П299 (журнал
`handover/handover-2026-10-14-p299-lost-branches-diverging-write.md`), разбор и метод — П295
(`handover/handover-2026-10-14-p295-isomer-diverging-lost-branches.md`). Ключ `--fix-excess`
(сумма выше 100 %) этим словом НЕ покрыт — решено «Починить на копии, принести».

Скрипт стоит ДО `add_diverging_isomer_edges.py`: ребро 119I → 119TEm (0.956 %) считается от
ветви ε+β⁺ 100, которую даёт правка 119I здесь.

    python tools/nucdb/fix_lost_branches.py                              # перечень, база дерева (ro)
    python tools/nucdb/fix_lost_branches.py --nucdb <копия> --apply      # запись потерянных ветвей в копию
    python tools/nucdb/fix_lost_branches.py --csv <файл>                 # перечень всех классов в CSV
"""
import argparse
import csv
import hashlib
import io
import math
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(REPO, "BecquerelMonitor", "nucdb.sqlite")
RULE_CS = os.path.join(REPO, "BecquerelMonitor", "FullSpectrumAnalysis", "DecayParentRule.cs")
THR = 0.1   # п.п., = MinChainBranch приложения (1e-3)

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")
    OUT.flush()


# --- правило приложения: читается из DecayParentRule.cs --------------------------------------

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

# --- моды l_decays (коды — database/scheme.md §2) --------------------------------------------
# основные моды и их семьи
PRIMARY = {2: "B-", 1: "EC", 7: "EC", 15: "EC", 50: "EC", 0: "A", 3: "IT", 4: "P", 5: "N",
           20: "2B-", 21: "2P", 26: "2B+", 30: "2EC",
           23: "CL", 27: "CL", 31: "CL", 33: "CL", 36: "CL", 38: "CL", 47: "CL", 52: "CL", 57: "CL",
           8: "SF"}
# задержанные моды — часть своей семьи (ENSDF: %B- включает %B-N)
DELAYED = {6: "B-", 17: "B-", 29: "B-", 42: "B-", 22: "B-", 55: "B-", 49: "B-",
           10: "EC", 12: "EC", 16: "EC", 19: "EC", 24: "EC", 25: "EC", 46: "EC", 34: "EC",
           28: "EC", 32: "EC"}
LABEL = {2: u"β⁻", 1: u"ε+β⁺", 7: u"ε", 15: u"β⁺", 50: u"β⁺ε", 0: u"α", 3: u"IT", 4: u"p", 5: u"n",
         20: u"2β⁻", 21: u"2p", 26: u"2β⁺", 30: u"2ε", 8: u"SF", 6: u"β⁻n", 17: u"β⁻2n", 29: u"β⁻3n",
         42: u"β⁻4n", 22: u"β⁻α", 55: u"β⁻p", 49: u"β⁻F", 10: u"εp", 12: u"βp", 16: u"εα", 19: u"β⁺α",
         24: u"ε2p", 25: u"β⁺2p", 46: u"ε3p", 34: u"εp ε2p", 28: u"εSF", 32: u"εF"}


def lab(code):
    return LABEL.get(code, u"код %s" % code)


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


def to_float(t):
    try:
        return float(t)
    except (TypeError, ValueError):
        return None


def decimals(t):
    t = (t or "").strip()
    if "e" in t.lower():
        return None
    return len(t.split(".")[1]) if "." in t else 0


def num_text(x, places):
    u"""Число записью без экспоненты; знаков — сколько у слагаемых (100 − 2.4 = 97.6, а не 97.60000001)."""
    if places is None:
        s = "%.6g" % x
    else:
        s = ("%." + str(places) + "f") % x
    if "e" in s.lower():
        s = ("%.12f" % x).rstrip("0").rstrip(".")
    if "." in s:
        s = s.rstrip("0").rstrip(".")
    return "0" if s in ("", "-0") else s


def fmt_t(t):
    if t is None:
        return u"—"
    if math.isinf(t):
        return u"стаб."
    for unit, s in (("y", 365.25 * 86400), ("d", 86400.0), ("h", 3600.0), ("m", 60.0), ("s", 1.0),
                    ("ms", 1e-3), ("us", 1e-6), ("ns", 1e-9)):
        if t >= s:
            return "%.4g %s" % (t / s, unit)
    return "%.3g s" % t


def fingerprint(nuc):
    h = hashlib.sha256()
    n = 0
    for row in nuc.execute("select * from decay_chain order by nucid, l_seqno, daughter_nucid, dec_type"):
        h.update(repr(row).encode("utf-8"))
        n += 1
    return n, h.hexdigest()[:16]


# --- разбор одного родителя ------------------------------------------------------------------

def app_edges(nuc, p):
    q = ("select daughter_nucid," + CHAIN_PERC_COLUMN + ", perc, dec_type, l_seqno from" + CHAIN_TABLE
         + " d where nucid = $n and perc not null" + CHAIN_LEVEL_CLAUSE)
    out = []
    for d, pe, pr, dt, lv in nuc.execute(q, {"n": p}):
        if not d or d.upper() == p.upper():
            continue
        v = to_float(pe)
        if v is None or not v > 0:
            continue
        out.append((d, v, pr, str(dt), lv))
    return out


def own_modes(nuc, p, plv):
    if plv is None:
        return []
    return nuc.execute("select daughter_nucid, dec_type, perc, perc_num, perc_oper, trim(decay_label) from l_decays"
                       " where nucid = ? and l_seqno = ?", (p, plv)).fetchall()


def expected(modes, parent=None):
    u"""T[d] по соглашению ENSDF. Возвращает (T, src, nd, unknown, notes, fam_main, delayed_of)."""
    fam_rows = {}
    delayed = {}
    nd = 0.0
    unknown = []
    notes = []
    for d, code, ptxt, pnum, oper, label in modes:
        code = int(code)
        if d and parent and ground(d) == ground(parent):
            # IT в самого себя (`183HF` «IT 100%(0)» на своём уровне) — петля, в ряд не идёт
            notes.append(u"l_decays своего уровня: %s в себя %s %%" % (lab(code), (ptxt or "").strip()))
            continue
        # «?» / пусто — числа нет; «0» с оператором GT/GE («≥ 0 %») — тоже числа нет
        if pnum is None or (pnum == 0 and oper in ("GT", "GE")):
            unknown.append(u"%s %s" % (lab(code), (ptxt or "?").strip() + ((" " + oper) if oper else "")))
            continue
        if pnum == 0:
            continue
        if code in DELAYED:
            if not d:
                nd += pnum
            else:
                delayed.setdefault(DELAYED[code], []).append((d, code, ptxt, pnum, oper))
            continue
        fam = PRIMARY.get(code)
        if fam is None:
            if d:
                notes.append(u"мода %s без расшифровки — в дочь %s %s %%" % (code, d, ptxt))
            else:
                nd += pnum
            continue
        if not d:
            nd += pnum
            continue
        fam_rows.setdefault(fam, []).append((d, code, ptxt, pnum, oper))
    T, src = {}, {}
    fam_main = {}
    for fam, rows in fam_rows.items():
        if fam == "EC":
            byc = dict((r[1], r) for r in rows)
            if 1 in byc and byc[1][3] > 0:
                main = [byc[1]]
            elif 50 in byc and byc[50][3] > 0:
                main = [byc[50]]
            else:
                main = [r for r in rows if r[1] in (7, 15) and r[3] > 0]
        else:
            main = [r for r in rows if r[3] > 0]
        if not main:
            continue
        vfam = sum(r[3] for r in main)
        dl = delayed.get(fam, [])
        sdl = sum(r[3] for r in dl)
        places = max([decimals(r[2]) for r in main + dl if decimals(r[2]) is not None] or [0])
        # основная дочь семьи: у захвата — одна (ε и β⁺ в одну дочь)
        dmain = main[0][0]
        if any(r[0] != dmain for r in main):
            notes.append(u"семья %s идёт в разные дочери" % fam)
        rest = vfam - sdl
        if rest < -1e-9:
            notes.append(u"задержанные %s %s %% больше основной моды %s %%" % (fam, num_text(sdl, places), num_text(vfam, places)))
            rest = 0.0
        fam_main[fam] = (dmain, main, dl, vfam, sdl, places)
        T[dmain] = T.get(dmain, 0.0) + rest
        src.setdefault(dmain, []).append((fam, "main", main, rest, places))
        for r in dl:
            T[r[0]] = T.get(r[0], 0.0) + r[3]
            src.setdefault(r[0], []).append((fam, "delayed", [r], r[3], decimals(r[2])))
    # задержанные без основной моды своей семьи (48NI: βp 30 при 2p 70, ε не названа)
    for fam, dl in delayed.items():
        if fam in fam_main:
            continue
        for r in dl:
            T[r[0]] = T.get(r[0], 0.0) + r[3]
            src.setdefault(r[0], []).append((fam, "delayed", [r], r[3], decimals(r[2])))
            notes.append(u"задержанная %s без своей основной моды" % lab(r[1]))
    return T, src, nd, unknown, notes, fam_main, delayed


GROUND_RE = re.compile(r"^(\d+[A-Z]+)([a-z]\w*)?$")


def ground(d):
    m = GROUND_RE.match(d or "")
    return m.group(1) if m else d


def analyse(nuc, p):
    r = nuc.execute("select l_seqno, half_life_sec from nuclides where nucid = ? order by l_seqno limit 1", (p,)).fetchone()
    plv, pt = (r[0], r[1]) if r else (None, None)
    edges = app_edges(nuc, p)
    C = {}        # по ОСНОВНОМУ состоянию дочки: ребро в изомер дочки (П282 `126SN → 126SBm1`) — та же дочь
    Ccodes = {}
    for d, v, pr, dt, lv in edges:
        g = ground(d)
        C[g] = C.get(g, 0.0) + v
        Ccodes.setdefault(g, []).append((dt, pr, lv, v, d))
    S = sum(C.values())
    modes = own_modes(nuc, p, plv)
    T, src, nd, unknown, notes, fam_main, delayed = expected(modes, p)
    T2, src2 = {}, {}
    for d, v in T.items():
        T2[ground(d)] = T2.get(ground(d), 0.0) + v
    for d, v in src.items():
        src2.setdefault(ground(d), []).extend(v)
    T, src = T2, src2
    E = sum(T.values())
    lost, short, excess, extra = [], [], [], []
    for d in sorted(set(T) | set(C)):
        t, c = T.get(d), C.get(d, 0.0)
        if t is None:
            if c > 0:
                extra.append((d, c))
            continue
        if c <= 0 and t >= THR:
            lost.append((d, t))
        elif c < t - THR:
            short.append((d, c, t))
        elif c > t + THR:
            excess.append((d, c, t))
    known_primary = sum(m[3] for m in modes if m[3] is not None and int(m[1]) not in DELAYED)
    return dict(p=p, plv=plv, pt=pt, S=S, C=C, Ccodes=Ccodes, T=T, src=src, E=E, nd=nd, unknown=unknown,
                notes=notes, lost=lost, short=short, excess=excess, extra=extra, modes=modes,
                known_primary=known_primary, fam_main=fam_main, delayed=delayed)


def loss_kind(a, d):
    u"""Потерянная дочь d: 'real' — ветви в ряду нет; 'delayed_full' — потеряна задержанная мода, а
    основная дочь её семьи несёт всю моду (сумма 100 случайно, раскол неверен — класс двойного
    счёта, правится только с --fix-excess)."""
    for fam, kind, rows, val, places in a["src"].get(d, []):
        if kind == "delayed" and fam in a["fam_main"]:
            dm = a["fam_main"][fam][0]
            cm = a["C"].get(ground(dm), 0.0)
            if cm > a["T"].get(ground(dm), 0.0) + THR:
                return "delayed_full"
    return "real"


def classify(a):
    S = a["S"]
    real = [x for x in a["lost"] if loss_kind(a, x[0]) == "real"]
    dfull = [x for x in a["lost"] if loss_kind(a, x[0]) == "delayed_full"]
    a["real"], a["dfull"] = real, dfull
    if not a["modes"] and a["pt"] is None:
        # ни периода, ни мод своего уровня: стабильный (верная сумма 0) или радиоактивный без данных
        # (сумма 100 рядом — сверять не с чем); между — ветвь чужого уровня у стабильного (`87SR`)
        if S <= 0 or abs(S - 100.0) <= THR:
            return None
        return u"СТАБИЛЬНЫЙ_С_ВЕТВЬЮ"
    if abs(S - 100.0) <= THR and not a["lost"] and not a["short"] and not a["excess"]:
        return None
    if real:
        add = sum(t for d, t in real)
        lim = all(any(r[4] in ("LT", "LE") for s in a["src"].get(d, []) for r in s[2]) for d, t in real)
        if S + add > 100.0 + (1e-6 if lim else THR):
            a["notes"].append(u"вставка дала бы S = %s > 100%s — не чинится"
                              % (num_text(S + add, 4), u" (вставляемое — лишь верхний предел)" if lim else
                                 u" (моды поставки противоречивы)"))
            return u"ПОТЕРЯ_НЕ_ЧИНИТСЯ"
        return u"ПОТЕРЯ_МОДЫ"
    ec7 = [d for d, c, t in a["short"]
           if any(x[0] == "7" for x in a["Ccodes"].get(d, [])) and "EC" in a["fam_main"]]
    if ec7 and S < 100.0 - THR:
        return u"ЕС_БЕЗ_B+"
    if S < 100.0 - THR:
        if a["short"]:
            return u"ДОЛЯ_МЕНЬШЕ"
        if a["nd"] > 0 and abs(S - a["E"]) <= THR:
            if abs(S + a["nd"] - 100.0) > 1.0:
                a["notes"].append(u"S + без дочери = %s ≠ 100 (моды поставки)" % num_text(S + a["nd"], 4))
            return u"ДЕЛЕНИЕ"
        if a["unknown"] or not a["modes"]:
            return u"НЕТ_В_ДЕРЕВЕ"
        if a["known_primary"] < 100.0 - THR:
            return u"ПОСТАВКА<100"
        if a["extra"]:
            return u"ЧУЖОЙ_УРОВЕНЬ"
        return u"ИНОЕ<100"
    exc_main = [x for x in a["excess"] if any(s[1] == "main" for s in a["src"].get(x[0], []))]
    if dfull:
        return u"ЗАДЕРЖ_БЕЗ_СТРОКИ"
    if exc_main and any(a["delayed"].values()):
        return u"ЗАДЕРЖ_ДВАЖДЫ"
    if a["extra"]:
        return u"ЧУЖОЙ_УРОВЕНЬ"
    if a["known_primary"] > 100.0 + THR:
        return u"ПОСТАВКА>100"
    if a["excess"]:
        return u"ДОЛЯ_БОЛЬШЕ"
    if a["short"]:
        return u"ДОЛЯ_МЕНЬШЕ"
    return u"ИНОЕ>100"


# --- правки ----------------------------------------------------------------------------------

def _insert_for(a, d):
    u"""Строки вставки для потерянной дочери d (основная мода — долей V − Σ задержанных)."""
    out = []
    p, plv = a["p"], a["plv"]
    for fam, kind, rows, val, places in a["src"].get(d, []):
        if kind == "main":
            dl = a["fam_main"][fam][2]
            if len(rows) == 1 and abs(val - rows[0][3]) < 1e-12:
                out.append(("ins", p, plv, rows[0][0], str(rows[0][1]), rows[0][2].strip(), None,
                            u"%s %s %% (l_decays)" % (lab(rows[0][1]), rows[0][2].strip())))
            elif len(rows) == 1:
                out.append(("ins", p, plv, rows[0][0], str(rows[0][1]), num_text(val, places), None,
                            u"%s %s %% − задержанные %s (l_decays)" % (
                                lab(rows[0][1]), rows[0][2].strip(),
                                " + ".join(u"%s %s" % (lab(r[1]), r[2].strip()) for r in dl))))
            elif dl:
                out.append(("skip", p, plv, d, "", "", None,
                            u"ε и β⁺ порознь при задержанных — делить остаток не по чему"))
            else:
                for r in rows:
                    out.append(("ins", p, plv, r[0], str(r[1]), r[2].strip(), None,
                                u"%s %s %% (l_decays)" % (lab(r[1]), r[2].strip())))
        else:
            r = rows[0]
            out.append(("ins", p, plv, r[0], str(r[1]), r[2].strip(), None,
                        u"%s %s %% (l_decays)" % (lab(r[1]), r[2].strip())))
    return out


def plan_fixes(nuc, a, cls, fix_excess):
    u"""[(вид, nucid, l_seqno, daughter, dec_type, perc_text, было, пояснение)]"""
    out = []
    if a["plv"] is None or cls is None:
        return out
    p, plv = a["p"], a["plv"]
    add = 0.0
    if cls == u"ПОТЕРЯ_МОДЫ":
        for d, t in a["real"]:
            out.extend(_insert_for(a, d))
            add += t
    if cls == u"ЕС_БЕЗ_B+":
        # Строка захвата (код 7) несёт только %ε (`119I` 49 при ε+β⁺ 100): её доля поднимается
        # до ε+β⁺ семьи по `l_decays`, код 7 остаётся. Почему не вставкой строки β⁺ (код 15):
        # линии β⁺ в `decay_radiations` у таких родителей записаны кодом 7 (набор «EC DECAY»), и
        # сумматор совпадений (`CascadeAtomicData`, канал = код строк излучений) зажал бы позитроны
        # долей одной ε — у 119I 511 кэВ упала бы 109.78 → 98 % (замерено пробой на копии).
        for d, c, t in a["short"]:
            rows = [x for x in a["Ccodes"].get(d, []) if x[0] == "7" and x[2] == plv and x[4] == d]
            if len(rows) != 1 or len(a["Ccodes"].get(d, [])) != 1:
                continue
            dt, pr, lv, v, dn = rows[0]
            fam = a["fam_main"].get("EC")
            if not fam:
                continue
            dmain, main, dl, vfam, sdl, places = fam
            places = max(places, decimals(pr) or 0)
            out.append(("upd", p, plv, d, "7", num_text(t, places), pr,
                        u"ε %s %% → ε+β⁺ %s %% (l_decays: %s)" % (
                            (pr or "").strip(), num_text(t, places),
                            "; ".join(u"%s %s" % (lab(r[1]), r[2].strip()) for r in main + dl))))
            add += t - c
    if out and a["S"] + add > 100.0 + THR:
        # вставка дала бы сумму > 100: моды поставки сами противоречивы — не чинится
        a["notes"].append(u"вставка дала бы S = %s > 100 — не чинится (моды поставки противоречивы)"
                          % num_text(a["S"] + add, 4))
        return [("skip", p, plv, x[3], "", "", None, u"S после вставки %s > 100" % num_text(a["S"] + add, 4))
                for x in out if x[0] == "ins"]
    if fix_excess and cls in (u"ЗАДЕРЖ_ДВАЖДЫ", u"ЗАДЕРЖ_БЕЗ_СТРОКИ"):
        for d, t in a["dfull"]:
            out.extend(_insert_for(a, d))
        for d, c, t in a["excess"]:
            mains = [s for s in a["src"].get(d, []) if s[1] == "main"]
            if not mains:
                continue
            rows = [x for x in a["Ccodes"].get(d, []) if x[2] == plv and x[4] == d]
            if len(rows) != 1 or len(a["Ccodes"].get(d, [])) != 1:
                out.append(("skip", p, plv, d, "", "", None,
                            u"строк в дочь %d (правило: %d) — правка доли не однозначна"
                            % (len(rows), len(a["Ccodes"].get(d, [])))))
                continue
            dt, pr, lv, v, dn = rows[0]
            out.append(("upd", p, plv, d, dt, num_text(t, mains[0][4]), pr,
                        u"основная мода минус задержанные (%s)" % num_text(c - t, mains[0][4])))
    return out


def apply_fixes(nuc, fixes):
    cur = nuc.cursor()
    done = {"ins": 0, "upd": 0, "same": 0, "skip": 0}
    for kind, p, lv, d, dt, ptxt, old, why in fixes:
        if kind == "skip":
            w(u"  ! %s → %s: %s — пропуск", p, d, why)
            done["skip"] += 1
            continue
        have = cur.execute("select perc from decay_chain where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                           " and dec_type = ?", (p, lv, d, dt)).fetchone()
        if kind == "ins":
            if have is not None:
                w(u"  = %s ур.%d → %s [%s] %s — уже есть", p, lv, d, dt, have[0])
                done["same"] += 1
                continue
            cur.execute("insert into decay_chain (nucid, l_seqno, daughter_nucid, dec_type, perc) values (?,?,?,?,?)",
                        (p, lv, d, dt, ptxt))
            w(u"  + %s ур.%d → %s [%s] perc %s   (%s)", p, lv, d, dt, ptxt, why)
            done["ins"] += 1
        elif kind == "upd":
            if have is None:
                w(u"  ! %s ур.%d → %s [%s]: строки нет — пропуск", p, lv, d, dt)
                done["skip"] += 1
                continue
            if have[0] == ptxt:
                done["same"] += 1
                continue
            cur.execute("update decay_chain set perc = ? where nucid = ? and l_seqno = ? and daughter_nucid = ?"
                        " and dec_type = ?", (ptxt, p, lv, d, dt))
            w(u"  ~ %s ур.%d → %s [%s] perc %s → %s   (%s)", p, lv, d, dt, have[0], ptxt, why)
            done["upd"] += 1
    return done


def corpus_members():
    u"""Нуклиды manifest.csv (столбцы nuclides/chains) — грубо, по написанию."""
    path = os.path.join(REPO, "tools", "CORPUS", "corpus", "manifest.csv")
    names = set()
    try:
        with io.open(path, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                for col in ("nuclides", "chains"):
                    for tok in re.split(r"[;+ ,]+", row.get(col) or ""):
                        m = re.match(r"^([A-Za-z]+)-?(\d+)(m\d*)?$", tok)
                        if m:
                            names.add(m.group(2) + m.group(1).upper() + (m.group(3) or ""))
                        elif tok:
                            names.add(tok.upper())
    except IOError:
        pass
    return names


ENSDF2_DIR = r"C:\LSRM\NuclideMaster\TCCFCALC\LIB\ENSDF2"


def ensdf2_br(path, dsid):
    u"""BR из записи N набора `dsid` сырого ENSDF2 ЛСРМ (кол. 32–39); `import_ensdf.py` записи N не читает."""
    inside = False
    for raw in io.open(path, encoding="latin-1").read().split("\n"):
        line = raw.rstrip("\r")
        if len(line) > 9 and line[8] == "*" and line[5] == " " and line[7] == " ":
            inside = line[9:39].strip() == dsid
            continue
        if inside and len(line) > 8 and line[7] == "N" and line[5] == " " and line[6] == " ":
            return line[31:39].strip() or None
    return None


def it_from_ensdf2(nuc, sch, a, cls, ensdf2_dir):
    u"""(Находка П294, 14.10.2026) Изомер, у которого в `l_decays` своего уровня нет ИТ, а набор
    «… IT DECAY» в ENSDF2 дерева есть (`schemedb.ensdf_datasets`, тот же уровень родителя): доля ИТ — BR
    записи N сырого набора, дочь — состояние, в котором кончается каскад набора (уровень с T½ ≥ 1 мкс или
    основное), именем `nuclides` по периоду. `166LUm2`: ε+β⁺ «> 80», ИТ 20 % (5) → 166LUm1 через 8.5 кэВ."""
    out = []
    p, plv = a["p"], a["plv"]
    m = GROUND_RE.match(p)
    if cls != u"ПОСТАВКА<100" or not m or not m.group(2) or sch is None or plv is None:
        return out
    if any(int(x[1]) == 3 for x in a["modes"]):
        return out
    g = m.group(1)
    for did, dsid, src, phl, pl in sch.execute(
            "select id, dsid, source_file, parent_hl_sec, parent_l_seqno from ensdf_datasets"
            " where upper(parent_nucid) = ? and dsid like '% IT DECAY%'", (g.upper(),)):
        if pl != plv or not phl or not a["pt"] or abs(math.log(phl / a["pt"])) > math.log(1.25):
            continue
        path = os.path.join(ensdf2_dir, src)
        if not os.path.isfile(path):
            a["notes"].append(u"ИТ: нет файла %s" % path)
            continue
        br = ensdf2_br(path, dsid)
        brv = to_float(br)
        if not brv or brv <= 0:
            a["notes"].append(u"ИТ: в наборе «%s» нет BR" % dsid)
            continue
        levels = dict((s, (e, hl)) for s, e, hl in sch.execute(
            "select seq, energy_kev, half_life_sec from ensdf_levels where dataset_id = ?", (did,)))
        top = max(levels, key=lambda s: levels[s][0] or 0.0)
        cur = top
        for _ in range(50):
            gs = sch.execute("select to_level_seq, coalesce(total_intensity, intensity, 0) from ensdf_gammas"
                             " where dataset_id = ? and from_level_seq = ? and to_level_seq is not null"
                             " order by 2 desc", (did, cur)).fetchall()
            if not gs:
                break
            cur = gs[0][0]
            e, hl = levels.get(cur, (None, None))
            if not e or (hl and hl >= 1e-6):
                break
        e, hl = levels.get(cur, (None, None))
        dest = None
        for nucid, lv, nhl in nuc.execute("select nucid, l_seqno, half_life_sec from nuclides where nucid = ?"
                                          " or nucid like ?", (g, g + "m%")):
            if not e:
                if nucid == g:
                    dest = nucid
            elif hl and nhl and GROUND_RE.match(nucid).group(2) and abs(math.log(hl / nhl)) <= math.log(1.5):
                dest = nucid
        if dest is None or dest == p:
            a["notes"].append(u"ИТ: конец каскада набора «%s» (%s кэВ) не назван в nuclides" % (dsid, e))
            continue
        val = 100.0 * brv
        places = max(0, (decimals(br) or 0) - 2)
        if a["S"] + val > 100.0 + THR:
            a["notes"].append(u"ИТ %s %% из «%s» дал бы S > 100 — не вставляется" % (num_text(val, places), dsid))
            continue
        out.append(("ins", p, plv, dest, "3", num_text(val, places), None,
                    u"ИТ %s %% — BR записи N набора ENSDF2 «%s» (%s), каскад кончается в %s %s кэВ"
                    % (num_text(val, places), dsid, src, dest, e)))
    return out


def main():
    ap = argparse.ArgumentParser(description=u"П295: потерянные ветви decay_chain")
    ap.add_argument("--nucdb", default=DEFAULT_NUCDB)
    ap.add_argument("--apply", action="store_true", help=u"ЗАПИСЬ в --nucdb (только копия)")
    ap.add_argument("--allow-live", action="store_true", help=u"запись в базу дерева/сборки — ТОЛЬКО по слову Amber")
    ap.add_argument("--fix-excess", action="store_true",
                    help=u"ещё и снять двойной счёт задержанных (правит строки поставки; отдельное решение)")
    ap.add_argument("--csv", default="", help=u"перечень всех классов в CSV (по умолчанию не пишется)")
    ap.add_argument("--list", default="")
    ap.add_argument("--schemedb", default=os.path.join(REPO, "BecquerelMonitor", "schemedb.sqlite"),
                    help=u"для ИТ изомеров (находка П294); открывается mode=ro")
    ap.add_argument("--ensdf2", default=ENSDF2_DIR, help=u"сырой ENSDF2 ЛСРМ — BR записи N набора ИТ")
    a = ap.parse_args()
    if a.apply and is_live(a.nucdb) and not a.allow_live:
        sys.exit(u"⛔ %s — база дерева/сборки/AppData; запись только в копию (или --allow-live по слову Amber)" % a.nucdb)
    nuc = connect(a.nucdb, a.apply)
    sch = connect(a.schemedb, False) if os.path.isfile(a.schemedb) else None
    if sch is None or not os.path.isdir(a.ensdf2):
        w(u"⚠ нет schemedb или ENSDF2 — ИТ изомеров не достраиваются")
        sch = None
    w(u"nucdb: %s (%s)", a.nucdb, u"ЗАПИСЬ" if a.apply else u"mode=ro")
    n0, fp0 = fingerprint(nuc)
    w(u"decay_chain до: %d строк, отпечаток %s", n0, fp0)
    corpus = corpus_members()
    parents = [r[0] for r in nuc.execute("select distinct nucid from decay_chain order by nucid")]
    rows, fixes_all = [], []
    by = {}
    for p in parents:
        an = analyse(nuc, p)
        cls = classify(an)
        if cls is None:
            continue
        fx = plan_fixes(nuc, an, cls, a.fix_excess)
        it = it_from_ensdf2(nuc, sch, an, cls, a.ensdf2)
        if it:
            cls = u"ИТ_ИЗОМЕРА"
            fx = fx + it
        rows.append((an, cls, fx))
        by.setdefault(cls, []).append(an)
        fixes_all.extend(fx)
    w(u"родителей в decay_chain: %d; |S − 100| > %s п.п. или потеряна дочь: %d", len(parents), THR, len(rows))
    order = [u"ПОТЕРЯ_МОДЫ", u"ЕС_БЕЗ_B+", u"ИТ_ИЗОМЕРА", u"ПОТЕРЯ_НЕ_ЧИНИТСЯ", u"ДОЛЯ_МЕНЬШЕ", u"ДЕЛЕНИЕ", u"НЕТ_В_ДЕРЕВЕ", u"ПОСТАВКА<100",
             u"ИНОЕ<100", u"ЗАДЕРЖ_ДВАЖДЫ", u"ЗАДЕРЖ_БЕЗ_СТРОКИ", u"ПОСТАВКА>100", u"ЧУЖОЙ_УРОВЕНЬ",
             u"ДОЛЯ_БОЛЬШЕ", u"ИНОЕ>100", u"СТАБИЛЬНЫЙ_С_ВЕТВЬЮ"]
    for k in order:
        lst = by.get(k, [])
        if not lst:
            continue
        lo = sum(1 for x in lst if x["S"] < 100 - THR)
        hi = sum(1 for x in lst if x["S"] > 100 + THR)
        big = sum(1 for x in lst if abs(x["S"] - 100) > 1.0)
        minute = sum(1 for x in lst if x["pt"] and x["pt"] >= 60)
        w(u"  %-14s %4d   (S<100: %d, S>100: %d, |S−100|>1 п.п.: %d, T½ ≥ 1 мин: %d)", k, len(lst), lo, hi, big, minute)
    if a.csv:
        write_csv(a.csv, rows, order, corpus)
        w(u"перечень: %s", a.csv)
    want = [x.strip() for x in a.list.split(",") if x.strip()]
    for an, cls, fx in rows:
        if an["p"] in want:
            w(u"--- %s [%s] S %s E %s nd %s; lost %s; short %s; excess %s; extra %s; unknown %s", an["p"], cls,
              num_text(an["S"], 6), num_text(an["E"], 6), num_text(an["nd"], 6), an["lost"], an["short"],
              an["excess"], an["extra"], an["unknown"])
            for f in fx:
                w(u"      %s", f)
    w(u"правок по плану: вставок %d, правок %d, пропусков %d",
      sum(1 for f in fixes_all if f[0] == "ins"), sum(1 for f in fixes_all if f[0] == "upd"),
      sum(1 for f in fixes_all if f[0] == "skip"))
    if a.apply:
        w(u"")
        w(u"ЗАПИСЬ:")
        done = apply_fixes(nuc, fixes_all)
        nuc.commit()
        n1, fp1 = fingerprint(nuc)
        w(u"вставлено %d, изменено %d, уже было %d, пропущено %d; decay_chain после: %d строк, отпечаток %s",
          done["ins"], done["upd"], done["same"], done["skip"], n1, fp1)


def write_csv(path, rows, order, corpus):
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        wr = csv.writer(f)
        wr.writerow(["class", "parent", "level", "T", "T_sec", "S_app_pct", "E_tree_pct", "no_daughter_pct",
                     "lost", "short", "excess", "extra", "unknown_modes", "l_decays_own_level", "chain_rows_app",
                     "fix", "in_corpus_manifest", "notes"])
        for an, cls, fx in sorted(rows, key=lambda r: (order.index(r[1]), r[0]["p"])):
            wr.writerow([cls, an["p"], an["plv"], fmt_t(an["pt"]), an["pt"] if an["pt"] is not None else "",
                         num_text(an["S"], 6), num_text(an["E"], 6), num_text(an["nd"], 6),
                         "; ".join("%s %s" % (d, num_text(t, 6)) for d, t in an["lost"]),
                         "; ".join("%s %s<%s" % (d, num_text(c, 6), num_text(t, 6)) for d, c, t in an["short"]),
                         "; ".join("%s %s>%s" % (d, num_text(c, 6), num_text(t, 6)) for d, c, t in an["excess"]),
                         "; ".join("%s %s" % (d, num_text(c, 6)) for d, c in an["extra"]),
                         "; ".join(an["unknown"]),
                         "; ".join(u"%s %s %s%s" % (m[0] or u"—", lab(int(m[1])), (m[2] or "").strip(),
                                                    (" " + m[4]) if m[4] else "") for m in an["modes"]),
                         "; ".join(u"%s[%s]%s ур.%s" % (x[4], x[0], num_text(x[3], 6), x[2])
                                   for d in sorted(an["Ccodes"]) for x in an["Ccodes"][d]),
                         "; ".join(u"%s %s→%s [%s] %s" % (k2, p2, d2, dt2, t2) for k2, p2, l2, d2, dt2, t2, o2, y2 in fx),
                         int(an["p"] in corpus),
                         " | ".join(an["notes"])])


if __name__ == "__main__":
    main()
