# -*- coding: utf-8 -*-
"""Разбор находок проверки 2 из run_before.txt: каждая цель получает разряд и причину.

Список 1 («нет в дереве», к глазам) и список 2 («НЕТ В РЕПОЗИТОРИИ», в счёт).
"""
import io, os, re, subprocess, sys, collections

ROOT = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8"
SRC = os.path.join(ROOT, "handover", "g3-registry-check2", "run_before.txt")
OUT = os.path.join(ROOT, "handover", "g3-registry-check2", "check2_classified_before.md")

text = io.open(SRC, encoding="utf-8").read()
sec1 = text.split(u"# Ссылки на файлы, которых нет в дереве")[1].split(u"# Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ")[0]
sec2 = text.split(u"# Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ")[1].split(u"# Вне репозитория НАРОЧНО")[0]

ROWRE = re.compile(r"^\s+(TODO\.md|DONE\.md)\s+(\S+)\s+строка\s+(\d+)\s+(.*)$")

def rows(sec):
    for line in sec.split(u"\n"):
        m = ROWRE.match(line)
        if m:
            yield m.group(1), m.group(2), int(m.group(3)), m.group(4)

# все файлы на диске внутри дерева, включая игнорируемые (без .git)
disk = collections.defaultdict(list)
for base, dirs, files in os.walk(ROOT):
    dirs[:] = [d for d in dirs if d != ".git"]
    for f in files:
        disk[f.lower()].append(os.path.relpath(os.path.join(base, f), ROOT))

def deleted_in_git(basename):
    try:
        out = subprocess.check_output(
            ["git", "log", "--all", "--oneline", "--diff-filter=D", "--name-only",
             "--", "*" + basename], cwd=ROOT).decode("utf-8", "replace")
    except subprocess.CalledProcessError:
        return None
    lines = [l.strip() for l in out.split("\n") if l.strip()]
    paths = [l for l in lines if l.lower().endswith(basename.lower())]
    return paths[0] if paths else None

_cache = {}
def classify(t):
    if t in _cache:
        return _cache[t]
    base = os.path.basename(t.replace("\\", "/"))
    if "*" in t:
        r = (u"ложная", u"шаблон с `*`")
    elif "<" in t or u"…" in t:
        r = (u"ложная", u"обобщение `<…>`/`…` в описании")
    elif t.startswith("--"):
        r = (u"ложная", u"ключ командной строки, не путь")
    elif re.match(r"^Foo\.", t):
        r = (u"ложная", u"образец имени `Foo.*`")
    elif t.startswith(".") and "/" not in t and "\\" not in t and base.lower() not in disk:
        r = (u"ложная", u"голое расширение (`.resx`, `.cs`…)")
    elif t in ("Designer.cs", "ru.resx"):
        r = (u"ложная", u"хвост имени файла, не имя")
    elif t.lower() in ("claude.md", "agents.md"):
        r = (u"ложная", u"OUTSIDE_ON_PURPOSE")
    else:
        d = deleted_in_git(base)
        if d:
            r = (u"настоящая, историчная", u"был в git, удалён: `%s`" % d)
        elif base.lower() in disk:
            where = disk[base.lower()][0]
            r = (u"на диске вне git", u"лежит `%s` (игнорируемый каталог)" % where.replace("\\", "/"))
        else:
            r = (u"настоящая, файла нет", u"ни в git, ни на диске в дереве")
    _cache[t] = r
    return r

def report(sec, title):
    out = [u"## %s\n" % title, u"| файл | строка | № | цель | разряд | причина |", u"|---|---|---|---|---|---|"]
    tally = collections.Counter()
    n = 0
    for f, num, line, targets in rows(sec):
        for t in [x.strip() for x in targets.split(u", ")]:
            kind, why = classify(t)
            tally[kind] += 1
            n += 1
            out.append(u"| %s | %d | %s | `%s` | %s | %s |" % (f, line, num, t, kind, why))
    out.append(u"")
    out.append(u"Итого целей: %d; %s" % (n, u"; ".join(u"%s — %d" % kv for kv in sorted(tally.items()))))
    out.append(u"")
    return out

lines = [u"# Проверка 2 до правки — разбор находок по целям (06.09.2026, снимок 01:42)\n",
         u"Источник: `run_before.txt`. Разряды: «ложная» — не ссылка на файл вовсе; "
         u"«настоящая, историчная» — файл был в git и удалён (строка рассказывает историю); "
         u"«настоящая, файла нет» — имя, которого в дереве не было никогда (памятка вне репозитория, "
         u"чужое дерево, обещанный и не написанный файл); «на диске вне git» — лежит в игнорируемом каталоге.\n"]
lines += report(sec1, u"Список 1 — «нет в дереве» (печатается к глазам, в счёт НЕ входит)")
lines += report(sec2, u"Список 2 — «НЕТ В РЕПОЗИТОРИИ, лежат только на диске» (В СЧЁТ, красит сторож)")
io.open(OUT, "w", encoding="utf-8", newline="\n").write(u"\n".join(lines) + u"\n")
sys.stdout.reconfigure(encoding="utf-8")
print(u"\n".join(l for l in lines if l.startswith(u"Итого") or l.startswith(u"## ")))
