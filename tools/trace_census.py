# -*- coding: utf-8 -*-
u"""A15: перепись `Trace.WriteLine` — кто из них ОТКАЗ, а кто ход работы.

Разряд ставится не на глаз: смотрится, стоит ли вызов ВНУТРИ `catch`, и что
делает код после него — возвращает ли управление, глотает ли исключение.
"""
from __future__ import unicode_literals
import io, os, re, glob, sys, json

ROOT = os.path.join("C:" + os.sep, "Users", "moroz", "source", "repos",
                    "BQ Eng res .NET 4.8", "BecquerelMonitor")

CALL = re.compile(r"Trace\s*\.\s*WriteLine\s*\(")


def strip_bom(t):
    return t[1:] if t and t[0] == "﻿" else t


def enclosing_catch(lines, idx):
    u"""Идём вверх по скобкам: если ближайший объемлющий блок — catch, вернуть
    его заголовок. Считаем фигурные скобки грубо, без разбора строк-литералов —
    для разметки этого хватает, а сомнительные случаи помечаются отдельно."""
    depth = 0
    for i in range(idx, max(-1, idx - 400), -1):
        line = lines[i]
        if i != idx:
            depth += line.count("}") - line.count("{")
        if depth < 0:
            head = lines[i].strip()
            j = i
            while not head and j > 0:
                j -= 1
                head = lines[j].strip()
            # заголовок блока — строка со скобкой либо строка над ней
            for k in (j, j - 1, j - 2):
                if k < 0:
                    break
                s = lines[k].strip()
                if s.startswith("catch"):
                    return s
                if s.startswith(("try", "if", "else", "for", "foreach", "while",
                                 "switch", "using", "lock", "do")):
                    return None
                if "(" in s and ")" in s and s.endswith(("{", ")")):
                    return None
            return None
    return None


rows = []
for path in glob.glob(os.path.join(ROOT, "**", "*.cs"), recursive=True):
    if os.sep + "obj" + os.sep in path:
        continue
    text = strip_bom(io.open(path, encoding="utf-8-sig", newline="", errors="replace").read())
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if not CALL.search(line):
            continue
        head = enclosing_catch(lines, i)
        rel = os.path.relpath(path, os.path.dirname(ROOT)).replace(os.sep, "/")
        msg = line.strip()
        rows.append({
            "file": rel, "line": i + 1,
            "in_catch": head is not None,
            "catch": head or "",
            "text": msg[:150],
        })

by_file = {}
for r in rows:
    by_file.setdefault(r["file"], []).append(r)

print("ВСЕГО вызовов Trace.WriteLine: %d в %d файлах" % (len(rows), len(by_file)))
print("из них ВНУТРИ catch: %d" % sum(1 for r in rows if r["in_catch"]))
print()
print("%-46s %5s %6s" % ("файл", "всего", "в catch"))
for f in sorted(by_file, key=lambda k: -len(by_file[k])):
    rs = by_file[f]
    print("%-46s %5d %6d" % (f, len(rs), sum(1 for r in rs if r["in_catch"])))

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "trace_census.json")
io.open(out, "w", encoding="utf-8", newline="").write(
    json.dumps(rows, ensure_ascii=False, indent=1))
print()
print("перепись поимённо: %s" % out)
