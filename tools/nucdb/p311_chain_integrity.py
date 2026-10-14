# -*- coding: utf-8 -*-
u"""П311 (15.10.2026): целостность рядов `nucdb` после трёх записей 14.10.2026
(П294 — 51 ребро «родитель → изомер», П299 — потерянные ветви и 18 рёбер, П308 —
двойной счёт задержанных частиц семьи β⁻ и три ребра второго прохода).

⛔ ТОЛЬКО ЧТЕНИЕ. База открывается `file:…?mode=ro` (uri=True) и закрывается в
`finally`; ничего не пишется ни при каком ключе. Ключа `--apply` нет: это сторож-
замер, а не правка (правка базы — только через Amber).

Что судится (правило ряда — ОДНО на проект, из `DecayParentRule.cs`, текстом через
`tools/CORPUS/scripts/chains.py`; своей копии SQL здесь нет):

  1. висячие рёбра — `decay_chain.daughter_nucid`, которого нет в `nuclides`
     (точным именем; отдельно — найденные лишь без учёта регистра);
  2. родители `decay_chain`, которых нет в `nuclides`;
  3. задвоения — одна и та же тройка (родитель, уровень, ДОЧЬ БЕЗ УЧЁТА РЕГИСТРА,
     канал) дважды; и пары (родитель, уровень, дочь) с несколькими каналами;
  4. изомеры без уровня — имя с меткой изомера (`…m`, `…m1`) в `decay_chain`
     (родитель или дочь), у которого нет строки `nuclides` или `l_seqno` пуст;
  5. рёбра ПРАВИЛОМ (`ChainTable` + `ChainLevelClause` + `ChainPercColumn`, как
     у трёх обходов приложения): сумма ветвей родителя выше 100 % (петли
     `дочь = сам` не в счёт) — с разбивкой по классам П306/П308;
  6. циклы в графе правила (сильные компоненты больше одного узла);
  7. обходы ряда приложения на этом графе — для КАЖДОГО родителя:
     * `NucBaseFramework.GetChainBranches` (очередь дельт, отсечка 1e-6, обход
       обрывается, когда очередь длиннее 1000 записей — молча);
     * `FsaSampleLibrary.ChainBranches` (обход в ширину до 128 узлов — молча,
       затем релаксация 256 проходов);
     сколько корней упирается в предел и насколько их ответ расходится с
     неподвижной точкой без пределов.

    python tools/nucdb/p311_chain_integrity.py [--nucdb ПУТЬ] [--csv ПУТЬ]

Код возврата: 0 — замер прошёл (числа напечатаны; сам по себе код 0 не значит
«нарушений нет» — смотреть разделы); 2 — базы нет или правило не прочиталось.
"""
from __future__ import print_function

import argparse
import io
import os
import re
import sqlite3
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
sys.path.insert(0, os.path.join(ROOT, "tools", "CORPUS", "scripts"))

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt=u"", *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


#: Пределы, решённые Amber «Оставить» 14.10.2026 (П306/П308, «Чего делать НЕ надо»).
LIMIT_PARENTS = {"35SI", "36SI", "61V", "72CO", "85GA"}
#: Каналы задержанных частиц (коды `dec_type`, подписи — `NucBase.Decay.ModeLabel`).
CAPTURE_DELAYED = {"10", "12", "16", "19", "24", "25", "34", "46"}   # εp, βp, εα, β+α, ε2p, β+2p, …, ε3p
BETA_DELAYED = {"6", "17", "22", "29", "42", "55"}                   # β⁻n, β⁻2n, β⁻α, β⁻3n, β⁻4n, β⁻p
CAPTURE_MAIN = {"1", "7", "15", "26", "30", "50"}
ISOMER_RE = re.compile(r"^\d+[A-Za-z]+?M\d*$", re.IGNORECASE)
ISOMER_TAIL = re.compile(r"m\d*$")


def is_isomer(nucid):
    # Метка изомера — строчная «m» с номером или без: «99TCm», «234PAm1». Заглавная
    # «M» — часть символа элемента («24MG», «25MN»).
    return bool(ISOMER_TAIL.search(nucid or ""))


def try_percent(value):
    u"""Тот же разбор, что `DecayParentRule.TryPercent`: REAL/INTEGER — как есть,
    текст — числом в инвариантной записи."""
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str):
        s = value.strip()
        if not re.match(r"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", s):
            return None
        return float(s)
    return None


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--csv", default=None, help="таблица по корням (обходы, пределы)")
    a = p.parse_args()
    if not os.path.isfile(a.nucdb):
        w(u"нет базы: %s", a.nucdb)
        return 2
    try:
        import chains  # noqa: E402  — правило ряда текстом из DecayParentRule.cs
    except Exception as ex:   # правило не прочиталось — замер бессмыслен
        w(u"правило ряда не прочиталось: %r", ex)
        return 2

    rule_sql = ("select daughter_nucid," + chains.CHAIN_PERC_COLUMN + ", dec_type from"
                + chains.CHAIN_TABLE + " d where nucid = $n and perc not null"
                + chains.CHAIN_LEVEL_CLAUSE)

    uri = "file:%s?mode=ro" % a.nucdb.replace("\\", "/")
    c = sqlite3.connect(uri, uri=True)
    try:
        return run(c, rule_sql, a)
    finally:
        c.close()


def run(c, rule_sql, a):
    nuclide_rows = c.execute("select nucid, l_seqno from nuclides").fetchall()
    names = set(r[0] for r in nuclide_rows)
    names_ci = set(r[0].upper() for r in nuclide_rows if r[0])
    level_of = defaultdict(list)
    for nucid, lev in nuclide_rows:
        level_of[nucid].append(lev)
    chain = c.execute("select nucid, l_seqno, daughter_nucid, dec_type, perc from decay_chain").fetchall()
    w(u"# П311: целостность рядов nucdb (только чтение)")
    w(u"база: %s", a.nucdb)
    w(u"строк decay_chain: %d, родителей: %d, строк nuclides: %d",
      len(chain), len(set(r[0] for r in chain)), len(nuclide_rows))
    w()

    # 1–2. висячие рёбра и родители без строки nuclides
    dangling = [r for r in chain if r[2] not in names]
    dangling_ci = [r for r in dangling if r[2].upper() in names_ci]
    w(u"## 1. Висячие рёбра (дочь без строки nuclides): %d (из них найдены только без учёта регистра: %d)",
      len(dangling), len(dangling_ci))
    for r in sorted(dangling)[:40]:
        w(u"   %s ур.%s → %s [%s] %s%s", r[0], r[1], r[2], r[3], r[4],
          u"  (регистр)" if r in dangling_ci else u"")
    parents_missing = sorted(set(r[0] for r in chain if r[0] not in names))
    w(u"## 2. Родители decay_chain без строки nuclides: %d", len(parents_missing))
    w(u"   %s", u", ".join(parents_missing[:60]))
    w()

    # 3. задвоения
    seen = defaultdict(list)
    for r in chain:
        seen[(r[0], r[1], r[2].upper(), r[3])].append(r)
    dup = [v for v in seen.values() if len(v) > 1]
    multi = defaultdict(set)
    for r in chain:
        if r[0] != r[2]:
            multi[(r[0], r[1], r[2])].add(r[3])
    multi_rows = sorted((k, sorted(v)) for k, v in multi.items() if len(v) > 1)
    w(u"## 3. Задвоения тройки (без учёта регистра дочери): %d; пар (родитель, уровень, дочь) с несколькими каналами: %d",
      len(dup), len(multi_rows))
    for v in dup[:20]:
        w(u"   %r", v)
    for k, v in multi_rows[:40]:
        w(u"   %s ур.%s → %s каналы %s", k[0], k[1], k[2], u",".join(v))
    w()

    # 4. изомеры без уровня
    iso_names = sorted(set(n for r in chain for n in (r[0], r[2]) if is_isomer(n)))
    iso_no_row = [n for n in iso_names if n not in names]
    iso_no_level = [n for n in iso_names if n in names and any(l is None for l in level_of[n])]
    w(u"## 4. Изомеры в decay_chain: %d; без строки nuclides: %d; со строкой без l_seqno: %d",
      len(iso_names), len(iso_no_row), len(iso_no_level))
    for n in iso_no_row[:40]:
        roles = sorted(set((u"родитель" if r[0] == n else u"дочь") for r in chain if n in (r[0], r[2])))
        w(u"   %s (%s)", n, u", ".join(roles))
    for n in iso_no_level[:40]:
        w(u"   %s уровни %r", n, level_of[n])
    w()

    # 5. рёбра правилом и суммы
    parents = sorted(set(r[0] for r in chain))
    edges = {}
    for par in parents:
        rows = c.execute(rule_sql, {"n": par}).fetchall()
        step = []
        for daughter, perc, dec in rows:
            if not daughter or daughter.upper() == par.upper():
                continue
            pv = try_percent(perc)
            step.append((daughter, pv, str(dec)))
        edges[par] = step
    over = []
    for par in parents:
        total = sum(pv for d, pv, dec in edges[par] if pv is not None)
        if total > 100.0 + 1e-6:
            decs = set(dec for d, pv, dec in edges[par])
            if par in LIMIT_PARENTS:
                cls = u"предел (Amber «Оставить»)"
            elif decs & CAPTURE_DELAYED and decs & CAPTURE_MAIN:
                cls = u"семья захвата (Amber «Оставить»)"
            elif decs & BETA_DELAYED:
                cls = u"⚠ семья β⁻ с задержанной"
            else:
                cls = u"иное"
            over.append((par, total, cls, edges[par]))
    by_cls = defaultdict(list)
    for o in over:
        by_cls[o[2]].append(o)
    # Порог П295/П306 (`fix_lost_branches.THR` = 0.1 п.п. = MinChainBranch 1e-3): им считались
    # «191 → 70» журналов П306/П308; здесь печатаются оба счёта — строго и с этим порогом.
    over_thr = [o for o in over if o[1] > 100.0 + 0.1]
    w(u"## 5. Сумма ветвей родителя ПРАВИЛОМ выше 100 %%: %d (выше 100.1 — порог П306: %d)",
      len(over), len(over_thr))
    for cls in sorted(by_cls):
        w(u"   %s: %d (выше 100.1: %d) — %s", cls, len(by_cls[cls]),
          sum(1 for o in by_cls[cls] if o[1] > 100.1),
          u", ".join(u"%s %.4g" % (o[0], o[1]) for o in sorted(by_cls[cls])))
    for o in sorted(over):
        if o[2] in (u"иное", u"⚠ семья β⁻ с задержанной"):
            w(u"     %s Σ=%.6g: %s", o[0], o[1],
              u"; ".join(u"%s [%s] %s" % (d, dec, pv) for d, pv, dec in o[3]))
    w()

    # 6. циклы (Тарьян, итеративно)
    graph = defaultdict(set)
    for par, step in edges.items():
        for d, pv, dec in step:
            if pv is not None and pv > 0:
                graph[par.upper()].add(d.upper())
    sccs = tarjan(graph)
    cyc = [s for s in sccs if len(s) > 1]
    w(u"## 6. Циклы в графе правила (сильные компоненты > 1 узла): %d", len(cyc))
    for s in cyc[:20]:
        w(u"   %s", u" ↔ ".join(sorted(s)))
    w()

    # 7. обходы ряда приложения
    rows_out = []
    nucbase_cap = 0
    fsa_cap = 0
    nucbase_bad = []
    fsa_bad = []
    for par in parents:
        exact = relax(par, edges, max_nodes=10 ** 6)
        nb, qlen, cut = nucbase_walk(par, edges)
        fs, nodes, fcut = relax_with_cap(par, edges)
        if cut:
            nucbase_cap += 1
        if fcut:
            fsa_cap += 1
        dnb = worst_gap(nb, exact, 1e-6)
        dfs = worst_gap(fs, exact, 0.0)
        if dnb[0] > 1e-9:
            nucbase_bad.append((par, qlen, dnb))
        if dfs[0] > 1e-9:
            fsa_bad.append((par, nodes, dfs))
        rows_out.append((par, qlen, int(cut), nodes, int(fcut), dnb[0], dfs[0]))
    w(u"## 7. Обходы ряда приложения на графе правила (%d корней)", len(parents))
    w(u"   NucBase GetChainBranches: очередь > 1000 (обход оборван молча) — %d корней; ответ разошёлся "
      u"с неподвижной точкой больше 1e-9 (члены с долей ≥ 1e-6) — %d", nucbase_cap, len(nucbase_bad))
    for par, qlen, gap in sorted(nucbase_bad, key=lambda t: -t[2][0])[:30]:
        w(u"     %s: очередь %d, худший член %s — %.6g против %.6g (Δ %.3g)",
          par, qlen, gap[1], gap[2], gap[3], gap[0])
    w(u"   FSA ChainBranches: узлов > 128 (обход оборван молча) — %d корней; ответ разошёлся больше 1e-9 — %d",
      fsa_cap, len(fsa_bad))
    for par, nodes, gap in sorted(fsa_bad, key=lambda t: -t[2][0])[:30]:
        w(u"     %s: узлов %d, худший член %s — %.6g против %.6g (Δ %.3g)",
          par, nodes, gap[1], gap[2], gap[3], gap[0])
    big = sorted(rows_out, key=lambda r: -r[1])[:10]
    w(u"   самые длинные очереди NucBase: %s", u", ".join(u"%s %d" % (r[0], r[1]) for r in big))
    bign = sorted(rows_out, key=lambda r: -r[3])[:10]
    w(u"   самые большие ряды (узлов): %s", u", ".join(u"%s %d" % (r[0], r[3]) for r in bign))
    if a.csv:
        with io.open(a.csv, "w", encoding="utf-8", newline="") as f:
            f.write(u"root,nucbase_queue,nucbase_cut,fsa_nodes,fsa_cut,nucbase_gap,fsa_gap\n")
            for r in rows_out:
                f.write(u"%s,%d,%d,%d,%d,%.6g,%.6g\n" % r)
    return 0


def worst_gap(got, exact, floor):
    u"""Худшее расхождение по членам с точной долей ≥ floor (и по лишним в got)."""
    keys = set(k for k, v in exact.items() if v >= floor) | set(got)
    best = (0.0, u"", 0.0, 0.0)
    for k in keys:
        g = got.get(k, 0.0)
        e = exact.get(k, 0.0)
        gap = abs(g - e)
        if gap > best[0]:
            best = (gap, k, g, e)
    return best


def nucbase_walk(root, edges, min_fraction=1e-6, cap=1000):
    u"""Копия `NucBaseFramework.GetChainBranches`: очередь дельт, ключи без
    учёта регистра, запрос — точным именем (как `where nucid = $n`)."""
    fraction = {root.upper(): 1.0}
    queue = [(root, 1.0)]
    i = 0
    while i < len(queue) and len(queue) <= cap:
        cur, share = queue[i]
        i += 1
        for d, pv, dec in edges.get(cur, []):
            if pv is None:
                continue
            add = share * pv / 100.0
            if add < min_fraction:
                continue
            fraction[d.upper()] = fraction.get(d.upper(), 0.0) + add
            queue.append((d, add))
    cut = i < len(queue)
    return fraction, len(queue), cut


def relax(root, edges, max_nodes=128, passes=256):
    return relax_with_cap(root, edges, max_nodes, passes)[0]


def relax_with_cap(root, edges, max_nodes=128, passes=256):
    u"""Копия `FsaSampleLibrary.ChainBranches`: открыть узлы в ширину (до 128),
    затем релаксация до неподвижной точки."""
    order = [root]
    known = {root.upper()}
    opened = {}
    i = 0
    while i < len(order) and len(order) <= max_nodes:
        cur = order[i]
        i += 1
        step = []
        for d, pv, dec in edges.get(cur, []):
            if pv is None or not pv > 0:
                continue
            step.append((d, pv))
            if d.upper() not in known:
                known.add(d.upper())
                order.append(d)
        opened[cur.upper()] = step
    cut = i < len(order)
    branch = {}
    for _ in range(passes):
        nxt = {root.upper(): 1.0}
        for par in order:
            have = branch.get(par.upper())
            if not have or not have > 0 or par.upper() not in opened:
                continue
            for d, pv in opened[par.upper()]:
                nxt[d.upper()] = nxt.get(d.upper(), 0.0) + have * pv / 100.0
        drift = max([abs(v - branch.get(k, 0.0)) for k, v in nxt.items()] + [0.0])
        branch = nxt
        if drift <= 1e-12:
            break
    return branch, len(order), cut


def tarjan(graph):
    index = {}
    low = {}
    on = set()
    stack = []
    out = []
    counter = [0]
    nodes = set(graph)
    for v in list(graph):
        nodes |= graph[v]
    for v0 in sorted(nodes):
        if v0 in index:
            continue
        work = [(v0, iter(sorted(graph.get(v0, ()))))]
        index[v0] = low[v0] = counter[0]
        counter[0] += 1
        stack.append(v0)
        on.add(v0)
        while work:
            v, it = work[-1]
            advanced = False
            for wv in it:
                if wv not in index:
                    index[wv] = low[wv] = counter[0]
                    counter[0] += 1
                    stack.append(wv)
                    on.add(wv)
                    work.append((wv, iter(sorted(graph.get(wv, ())))))
                    advanced = True
                    break
                elif wv in on:
                    low[v] = min(low[v], index[wv])
            if advanced:
                continue
            work.pop()
            if work:
                low[work[-1][0]] = min(low[work[-1][0]], low[v])
            if low[v] == index[v]:
                comp = set()
                while True:
                    x = stack.pop()
                    on.discard(x)
                    comp.add(x)
                    if x == v:
                        break
                out.append(comp)
    return out


if __name__ == "__main__":
    sys.exit(main())
