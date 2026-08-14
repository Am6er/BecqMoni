# -*- coding: utf-8 -*-
"""
Разрез СДВОЕННЫХ подписей поставочного `config/NuclideDefinition.xml` на
точные записи нуклидов (S33 «а», решение Amber 14.08.2026: «разрезать»).

У сдвоенной записи («Am-241/x-rays» 59, «Ra-226/U-235» 185…) нет выхода —
компонент из неё не рождается, и подписанный ею пик пропадает для библиотеки
целиком (ровно так Am-241 не доходил до состава, S36 «а»). Разрез:

  * сдвоенная ГАСНЕТ (Visible=false; запись не удаляется — след остаётся);
  * каждая половина-нуклид получает ВИДИМУЮ запись на своей настоящей линии
    из nucdb: существующая скрытая — зажигается, отсутствующая — создаётся
    с энергией и выходом из базы (HalfLife и цвет наследуются от сдвоенной);
  * половины без линии в ±4 кэВ и с выходом ниже I_MIN отпадают с печатью
    причины: запись с выходом 0.006 % на распад не увидит ни один прибор
    корпуса, а метку без линии уже снимала S38.

    python tools/nucdb/split_compound_labels.py BecquerelMonitor/nucdb.sqlite \
           BecquerelMonitor/config/NuclideDefinition.xml [--apply]

⛔ `--apply` ВЫКЛЮЧЕН по умолчанию: без него только план, файл не трогается.
Правка поставочного конфига — решение Amber; на этот разрез оно дано
14.08.2026 («S33а: разрезать на точные записи»).

Отличие от label_precision.py --apply: тот только переставляет флаги Visible
(образ не меняется вовсе), здесь СОЗДАЮТСЯ записи с выходом — образы
компонентов, куда эти линии входят, меняются. Поэтому после внесения корпус
перемеряется целиком, а не принимается на веру.
"""

import io
import os
import re
import sqlite3
import sys

SEARCH_KEV = 4.0        # то же окно, что у label_precision/fill_intensity
UNRESOLVED_KEV = 0.5    # неразделимые прибором линии — один пик, берём сильнейшую
I_MIN_PCT = 0.1         # слабее — запись бессмысленна: не увидит ни один прибор


def nucid_candidates(token):
    m = re.match(r"^([A-Za-z]{1,2})-?(\d{1,3})([mM]\d?)?$", token)
    if not m:
        return []
    base = "%d%s" % (int(m.group(2)), m.group(1).upper())
    return [base] if not m.group(3) else [base + "m", base + "m1", base + "m2"]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    apply_it = "--apply" in sys.argv[1:]
    if len(args) != 2:
        sys.exit(__doc__)

    if not os.path.exists(args[0]):
        sys.exit("нет файла базы: %s (куски лежат рядом со сборкой)" % args[0])
    db = sqlite3.connect(args[0])

    path = args[1]
    with io.open(path, encoding="utf-8-sig") as f:
        text = f.read()

    blocks = list(re.finditer(r"<Nuclide>.*?</Nuclide>", text, re.S))
    parsed = []
    for i, b in enumerate(blocks):
        piece = b.group(0)
        name = re.search(r"<Name>(.*?)</Name>", piece, re.S)
        energy = re.search(r"<Energy>(.*?)</Energy>", piece, re.S)
        visible = re.search(r"<Visible>(.*?)</Visible>", piece, re.S)
        if name is None or energy is None:
            continue
        parsed.append({
            "i": i, "name": name.group(1), "energy": float(energy.group(1)),
            "visible": visible is not None and visible.group(1).strip() == "true",
        })

    def nearest(token, energy):
        for nucid in nucid_candidates(token):
            rows = db.execute(
                "select energy_num, intensity_num from decay_radiations"
                " where parent_nucid=? and type_a='G'"
                " and energy_num is not null and intensity_num is not null"
                " and abs(energy_num - ?) <= ?",
                (nucid, energy, SEARCH_KEV)).fetchall()
            if not rows:
                continue
            best = None
            for e, inten in rows:
                if best is None:
                    best = (e, inten)
                    continue
                de, db_ = abs(e - energy), abs(best[0] - energy)
                if abs(de - db_) < UNRESOLVED_KEV:
                    if inten > best[1]:
                        best = (e, inten)
                elif de < db_:
                    best = (e, inten)
            return best
        return None

    # индекс блока -> "false"/"true" для правки Visible; список вставок
    edits = {}
    inserts = {}            # индекс блока сдвоенной -> [xml новых записей]
    for r in parsed:
        if "/" not in r["name"] or not r["visible"]:
            continue

        piece = blocks[r["i"]].group(0)
        half_life = re.search(r"<HalfLife>(.*?)</HalfLife>", piece, re.S)
        color = re.search(r"<NuclideColor>(.*?)</NuclideColor>", piece, re.S)
        print("%s  %.1f -> гашу сдвоенную" % (r["name"], r["energy"]))
        edits[r["i"]] = "false"

        for token in r["name"].split("/"):
            if not nucid_candidates(token):
                print("   %-8s не нуклид — половина отпадает" % token)
                continue
            line = nearest(token, r["energy"])
            if line is None:
                print("   %-8s линии в ±%.0f кэВ нет — половина отпадает"
                      % (token, SEARCH_KEV))
                continue
            if line[1] < I_MIN_PCT:
                print("   %-8s линия %.3f слаба (%.4f %% < %.1f %%) — отпадает"
                      % (token, line[0], line[1], I_MIN_PCT))
                continue

            existing = [p for p in parsed
                        if p["name"] == token and abs(p["energy"] - line[0]) < 0.05]
            if existing:
                if existing[0]["visible"]:
                    print("   %-8s %.3f уже видима — ничего не делаю" % (token, line[0]))
                else:
                    print("   %-8s зажигаю скрытую %.3f (%.3f %%)"
                          % (token, line[0], line[1]))
                    edits[existing[0]["i"]] = "true"
                continue

            print("   %-8s СОЗДАЮ запись %.3f (%.3f %%)" % (token, line[0], line[1]))
            record = ("\n    <Nuclide>"
                      "\n      <Name>%s</Name>"
                      "\n      <Energy>%.3f</Energy>"
                      "\n      <HalfLife>%s</HalfLife>"
                      "\n      <NuclideColor>%s</NuclideColor>"
                      "\n      <Note>разрез S33а из «%s»</Note>"
                      "\n      <Visible>true</Visible>"
                      "\n      <Intencity>%.4g</Intencity>"
                      "\n    </Nuclide>"
                      % (token, line[0],
                         half_life.group(1) if half_life else "0",
                         color.group(1) if color else "Violet",
                         r["name"], line[1]))
            inserts.setdefault(r["i"], []).append(record)

    if not apply_it:
        print()
        print("--apply не задан: файл не тронут.")
        return

    out, last = [], 0
    for i, block in enumerate(blocks):
        if i not in edits and i not in inserts:
            continue
        piece = block.group(0)
        if i in edits:
            piece = re.sub(r"<Visible>\s*(?:true|false)\s*</Visible>",
                           "<Visible>%s</Visible>" % edits[i], piece)
        if i in inserts:
            piece += "".join(inserts[i])
        out.append(text[last:block.start()])
        out.append(piece)
        last = block.end()
    out.append(text[last:])

    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write("".join(out))
    created = sum(len(v) for v in inserts.values())
    print()
    print("ЗАПИСАНО: погашено сдвоенных %d, зажжено скрытых %d, создано записей %d"
          % (len([k for k, v in edits.items() if v == "false"]),
             len([k for k, v in edits.items() if v == "true"]), created))


if __name__ == "__main__":
    main()
