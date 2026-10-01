# -*- coding: utf-8 -*-
u"""`AMBER151`: какие гамма-линии ENSDF (`schemedb.ensdf_gammas`) отсутствуют в `nucdb.decay_radiations`.

Проба П191 (`D:\\BqMoni_Claude\\p191\\missing_lines.py`), перенесена в дерево П196 01.10.2026 с
двумя правками, обе — против артефактов самой пробы, а не базы:

  * набор распада подбирается по ПЕРИОДУ родителя (как в `import_weak_gammas.py`), а не
    «набор с наибольшим числом линий»: у `207BI` и `109CD` прежняя проба брала набор IT
    СОБСТВЕННОГО изомера (182 мкс, 10.9 мкс) и печатала «нет 459 %» и «нет 6.97 %»;
  * берутся ВСЕ наборы родителя этого периода (у Eu-152 — EC и β⁻), норма RI → % на распад
    — по сильнейшей сопоставленной линии КАЖДОГО набора; прежде β⁻-ветвь Eu-152 не
    смотрелась вовсе.

Нуклиды корпуса — колонки `nuclides` и `chains` `tools/CORPUS/corpus/manifest.csv`, ряды
развёрнуты по `nucdb.decay_chain` до членов с гамма-линиями.

Печатает по нуклиду: линий ENSDF / строк nucdb (из них своих, `dr_pk` ≥ 100001), выход
отсутствующих на распад (%), сильнейшие отсутствующие. Итог — максимум «нет» по корпусу.

    python tools/nucdb/missing_lines.py [--nucdb <путь>] [--schemedb <путь>] [нуклид ...]
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
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
from import_weak_gammas import HL_TOL, MODES, OWN_PK_BASE, dataset_mode, tol_kev  # noqa: E402

OUT = io.open(1, "w", encoding="utf-8", closefd=False)
CHAIN_PARENT = {"Th-232": "232TH", "Ra-226": "226RA", "U-238": "238U", "U-235": "235U"}


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


def ro(path):
    return sqlite3.connect("file:%s?mode=ro" % path.replace(chr(92), "/"), uri=True)


def nucid_of(name):
    m = re.match(r"^([A-Za-z]+)-(\d+)(m\d*)?$", name.strip())
    if not m:
        return None
    return m.group(2) + m.group(1).upper() + (m.group(3) or "")


def corpus_nuclides(nuc):
    out = set()
    path = os.path.join(ROOT, "tools", "CORPUS", "corpus", "manifest.csv")
    with io.open(path, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            for col in ("nuclides", "chains"):
                for tok in re.split(r"[;+ ,]+", row.get(col) or ""):
                    if not tok:
                        continue
                    if col == "chains":
                        head = CHAIN_PARENT.get(tok) or nucid_of(re.sub(r"[a-z]+$", "", tok))
                        if not head:
                            continue
                        out.add(head)
                        stack = [head]
                        while stack:
                            n = stack.pop()
                            for (d,) in nuc.execute("select daughter_nucid from decay_chain where nucid = ?", (n,)):
                                if d and d not in out:
                                    out.add(d)
                                    stack.append(d)
                    else:
                        n = tok if re.match(r"^\d+[A-Z]+(m\d*)?$", tok) else nucid_of(tok)
                        if n:
                            out.add(n)
    have = set(r[0] for r in nuc.execute("select distinct parent_nucid from decay_radiations where type_a = 'G'"))
    return sorted(out & have, key=lambda n: (int(re.match(r"\d+", n).group(0)), n))


def measure(nuc, sch, n):
    db = nuc.execute("select energy_num, intensity_num, dr_pk from decay_radiations where parent_nucid = ?"
                     " and type_a = 'G' and energy_num > 0 and intensity_num > 0", (n,)).fetchall()
    if not db:
        return None
    t = None
    rows = nuc.execute("select l_seqno, half_life_sec from nuclides where nucid = ?", (n,)).fetchall()
    if rows:
        t = rows[0][1]
    base = re.match(r"^(\d+[A-Z]+)", n).group(1)
    sets = sch.execute("select id, dsid, parent_hl_sec from ensdf_datasets where parent_nucid = ?", (base,)).fetchall()
    nens, missing = 0, []
    for did, dsid, phl in sets:
        if dataset_mode(dsid) is None or not phl or not t or math.isinf(t) or abs(math.log(phl / t)) > HL_TOL:
            continue
        ens = sch.execute("select energy_kev, intensity from ensdf_gammas where dataset_id = ? and intensity > 0",
                          (did,)).fetchall()
        if not ens:
            continue
        nens += len(ens)
        hit = [(g, d) for g in ens for d in db if abs(g[0] - d[0]) <= tol_kev(g[0])]
        if not hit:
            continue
        # якорь — сильнейшая сопоставленная строка nucdb и БЛИЖАЙШАЯ к ней линия ENSDF (у Th-234
        # 62.86 и 63.29 кэВ обе в допуске от 63.29, и «первая попавшаяся» давала норму ×230)
        d0 = max((d for g, d in hit), key=lambda d: d[1])
        g0 = min((g for g, d in hit if d is d0), key=lambda g: abs(g[0] - d0[0]))
        norm = d0[1] / g0[1]
        for e, ri in ens:
            if not any(abs(e - d[0]) <= tol_kev(e) for d in db):
                missing.append((e, ri * norm))
    missing.sort(key=lambda m: -m[1])
    own = sum(1 for d in db if d[2] >= OWN_PK_BASE)
    return nens, len(db), own, sum(m[1] for m in missing), missing


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite"))
    p.add_argument("--schemedb", default=os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite"))
    p.add_argument("nuclides", nargs="*")
    a = p.parse_args()
    nuc, sch = ro(a.nucdb), ro(a.schemedb)
    nucs = a.nuclides or corpus_nuclides(nuc)
    w(u"nucdb: %s", a.nucdb)
    w(u"%-9s %6s %6s %5s %9s  %s", u"нуклид", u"ENSDF", u"nucdb", u"своих", u"нет,%расп", u"сильнейшие отсутствующие (кэВ: % на распад)")
    worst = (0.0, None)
    for n in nucs:
        r = measure(nuc, sch, n)
        if r is None:
            w(u"%-9s нет строк в nucdb", n)
            continue
        nens, ndb, own, tot, missing = r
        if nens == 0:
            w(u"%-9s %6s %6d %5d %9s  нет набора ENSDF этого периода", n, u"—", ndb, own, u"—")
            continue
        top = u", ".join(u"%.1f: %.3f" % m for m in missing[:4])
        w(u"%-9s %6d %6d %5d %9.3f  %s", n, nens, ndb, own, tot, top)
        if tot > worst[0]:
            worst = (tot, n)
    w(u"максимум «нет, %% расп» по списку: %.3f (%s)", worst[0], worst[1] or u"—")
    return 0


if __name__ == "__main__":
    sys.exit(main())
