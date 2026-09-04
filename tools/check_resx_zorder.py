# -*- coding: utf-8 -*-
u"""Designer-метаданные `>>X.Parent` / `>>X.ZOrder` против designer-КОДА.

`check_resx.py` рядом сверяет пару `Foo.resx` / `Foo.ru.resx` — что есть
по-английски, есть и по-русски. `check_resx_designer.py` ищет обращения к
ключам, которых нет ни в одном из двух. Класс, разобранный ЗДЕСЬ, не виден ни
той, ни другой: ключи-то на месте, расходятся ОПИСАНИЕ формы в `.resx` и
КОД, который её строит.

## Откуда взялась (`A87`, 04.09.2026)

У `NucBase.resx` метка `SearchStatusLabel` добавлялась на `panel1` первой
строкой `panel1.Controls.Add(...)`, а designer-метаданных у неё не было ВОВСЕ:
двенадцать детей в коде против одиннадцати записей `>>X.Parent = panel1`.
Конструктор Visual Studio такую метку на форме не показывает. **На работу
приложения это не влияет** — `ApplyResources` ключи с приставкой `>>` не
читает, — влияет только на вид формы в конструкторе, поэтому находка тихая:
собирается, работает, а у человека за конструктором формы неполная.

## Что именно проверяется

Для каждого КОНТЕЙНЕРА (`this.X.Controls.Add(this.Y)`; сама форма зовётся
`$this`, её дети — `this.Controls.Add(this.Y)`) в `Foo.Designer.cs`:

1. число детей в коде = число записей `>>X.Parent = <контейнер>` в `Foo.resx`;
2. у каждого ребёнка есть все ЧЕТЫРЕ метаданные — `Name`, `Type`, `Parent`,
   `ZOrder`;
3. `ZOrder` идёт 0..N−1 без пропусков и повторов;
4. порядок по `ZOrder` совпадает с порядком `Controls.Add`.

Пункт 4 — не догадка о WinForms, а ИЗМЕРЕННЫЙ в этом же дереве закон:
в `NucBase.resx` у `groupBox1` (14 детей) и `groupBox2` (12 детей) `ZOrder`
совпадает с порядком `Controls.Add` ровно, ребёнок в ребёнка. Первый
добавленный получает 0.

## ⚠ Чего это НЕ ловит и где ошибается

**Контролы, добавленные ВО ВРЕМЯ РАБОТЫ.** `Controls.Add` из `Foo.cs`
(не `Foo.Designer.cs`) сюда не попадает — и правильно: конструктор о таком
ребёнке не знает и метаданных ему не заводит. Но обратная сторона: если
такой ребёнок занимает `ZOrder` 0, у designer-детей нумерация начнётся с 1, и
пункт 3 закричит на здоровой форме. Ровно это видно на `$this` у `MainForm`
(`dockPanel1`/`statusStrip1`/`menuStrip1` = 1/2/3, порядок при этом верен).

Поэтому вывод читается ГЛАЗАМИ, а не по коду возврата: «порядок ZOrder !=
порядок Controls.Add» и «нет >>X.Parent» — находки, а один лишь сдвиг
нумерации при верном порядке — повод посмотреть, кто занял ноль.

## Состояние на 04.09.2026 (после закрытия `A87`)

Пар `Foo.Designer.cs` / `Foo.resx` — 42, контейнеров с метаданными — 78.
`NucBase :: panel1` закрыт `A87` и сверку проходит (`--form NucBase` даёт 0).
Расхождение остаётся ещё у ДЕСЯТИ контейнеров в девяти формах — это тот же
класс, найден этой же сверкой, разбора НЕ проходил и вынесен отдельной
строкой `TODO.md` (номер назначает Amber; на 04.09.2026 серия `A` занята
до `A104`).
Поэтому сверка без `--form` сегодня возвращает 1: это не поломка, это
непрочитанный список.

Запуск:
    python tools/check_resx_zorder.py [--form NucBase] [--out FILE]
Код возврата 0 — расхождений нет, 1 — есть.
"""
import argparse
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.normpath(os.path.join(HERE, "..", "BecquerelMonitor"))

ap = argparse.ArgumentParser()
ap.add_argument("--root", default=APP, help="корень приложения")
ap.add_argument("--form", default=None, help="только эта форма (имя без .resx)")
ap.add_argument("--out", default=None, help="писать отчёт в файл (UTF-8)")
args = ap.parse_args()

PROPS = ("Name", "Type", "Parent", "ZOrder")


def read_meta(resx):
    meta = {}
    for data in ET.parse(resx).getroot().findall("data"):
        name = data.get("name") or ""
        if not name.startswith(">>"):
            continue
        body = name[2:]
        if "." not in body:
            continue
        ctrl, prop = body.rsplit(".", 1)
        if prop not in PROPS:
            continue
        v = data.find("value")
        meta.setdefault(ctrl, {})[prop] = (v.text or "").strip() if v is not None else ""
    return meta


def read_kids(designer):
    src = io.open(designer, encoding="utf-8-sig", newline="").read()
    kids = {}
    for m in re.finditer(r"(?<![\w.])this\.(?:(\w+)\.)?Controls\.Add\(this\.(\w+)\)", src):
        kids.setdefault(m.group(1) or "$this", []).append(m.group(2))
    return kids


pairs = []
for dirpath, dirnames, filenames in os.walk(args.root):
    parts = dirpath.split(os.sep)
    if "bin" in parts or "obj" in parts:
        continue
    for fn in filenames:
        if not fn.endswith(".Designer.cs"):
            continue
        stem = fn[:-len(".Designer.cs")]
        if args.form and stem != args.form:
            continue
        resx = os.path.join(dirpath, stem + ".resx")
        if os.path.exists(resx):
            pairs.append((os.path.join(dirpath, fn), resx))
pairs.sort()

buf = io.StringIO()
buf.write(u"пар Foo.Designer.cs / Foo.resx: %d\n" % len(pairs))
n_cont = 0
n_bad = 0
report = []
for des, resx in pairs:
    meta = read_meta(resx)
    if not meta:
        continue                       # форма без designer-метаданных вовсе
    kids = read_kids(des)
    rel = os.path.relpath(resx, args.root)
    for cont, code_kids in sorted(kids.items()):
        n_cont += 1
        resx_kids = [c for c, mm in meta.items() if mm.get("Parent") == cont]
        problems = []
        miss = [c for c in code_kids if c not in resx_kids]
        extra = [c for c in resx_kids if c not in code_kids]
        if miss:
            problems.append(u"нет >>X.Parent: %s" % u", ".join(miss))
        if extra:
            problems.append(u"лишние >>X.Parent (в коде нет Controls.Add): %s" % u", ".join(extra))
        for c in code_kids:
            lack = [p for p in PROPS if p not in meta.get(c, {})]
            if lack:
                problems.append(u"у %s нет метаданных: %s" % (c, u", ".join(lack)))
        zs = {}
        for c in resx_kids:
            z = meta[c].get("ZOrder")
            if z is not None and z.lstrip("-").isdigit():
                zs[c] = int(z)
            elif z is not None:
                problems.append(u"у %s ZOrder не число: %r" % (c, z))
        if sorted(zs.values()) != list(range(len(resx_kids))):
            problems.append(u"ZOrder не 0..%d без пропусков и повторов: %s"
                            % (len(resx_kids) - 1, sorted(zs.values())))
        by_z = [c for c, z in sorted(zs.items(), key=lambda kv: kv[1])]
        if by_z != code_kids:
            problems.append(u"порядок по ZOrder не совпал с порядком Controls.Add")
        if problems:
            n_bad += 1
            report.append(u"%s :: %s (детей в коде %d, записей в resx %d)"
                          % (rel, cont, len(code_kids), len(resx_kids)))
            for p in problems:
                report.append(u"      %s" % p)
            if by_z != code_kids:
                for i in range(max(len(by_z), len(code_kids))):
                    a = by_z[i] if i < len(by_z) else u"—"
                    b = code_kids[i] if i < len(code_kids) else u"—"
                    report.append(u"      %2d  ZOrder=%-28s Controls.Add=%-28s %s"
                                  % (i, a, b, u"  " if a == b else u"<<"))

buf.write(u"контейнеров проверено: %d, с расхождением: %d\n" % (n_cont, n_bad))
buf.write(u"=" * 74 + u"\n")
buf.write(u"\n".join(report) if report else u"расхождений нет")
buf.write(u"\n")
text = buf.getvalue()
if args.out:
    io.open(args.out, "w", encoding="utf-8", newline="").write(text)
    print("написано: %s" % args.out)
else:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stdout.write(text)
sys.exit(1 if n_bad else 0)
