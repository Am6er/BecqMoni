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

Пункт 4 — не догадка о WinForms, а ИЗМЕРЕННЫЙ закон, и мерен он дважды:
* в самом дереве (`A87`) — в `NucBase.resx` у `groupBox1` (14 детей) и
  `groupBox2` (12 детей) `ZOrder` совпадает с порядком `Controls.Add` ровно,
  ребёнок в ребёнка; первый добавленный получает 0;
* НА ЖИВЫХ ОБЪЕКТАХ (`A118`, 05.09.2026) — построенный
  `new EnergySpectrumView()` даёт детей `[0] panel2`, `[1] vScrollBar1`,
  `[2] hScrollBar1`, а `new DocEnergySpectrum()` — `[0] toolStripContainer1`.
  То есть `ZOrder` — это индекс в `Controls`, и он равен порядку `Controls.Add`.

## ⚠ Чего это НЕ ловит

**Контролы, добавленные ВО ВРЕМЯ РАБОТЫ.** `Controls.Add` из `Foo.cs`
(не `Foo.Designer.cs`) сюда не попадает — и правильно: конструктор о таком
ребёнке не знает и метаданных ему не заводит.

⛔ **Но «сдвиг нумерации» этим НЕ объясняется, и это измерено, а не выведено**
(`A118`, 05.09.2026). Прежде здесь стояло, что такой ребёнок может занять
`ZOrder` 0 и сдвинуть designer-детей на единицу, и примером служил `$this` у
`MainForm` (`dockPanel1`/`statusStrip1`/`menuStrip1` = 1/2/3 при верном
порядке). Проверено:

* `Controls.Add` **дописывает в конец**, поэтому ребёнок, добавленный во время
  работы, получает ПОСЛЕДНИЙ индекс и ничего не сдвигает;
* ни в `MainForm.cs`, ни в `DocEnergySpectrum.cs`, ни в `EnergySpectrumView.cs`,
  ни в `DCControlPanel.cs` вызова `this.Controls.Add` нет ВОВСЕ;
* `MdiClient`, который заводит `IsMdiContainer = true` у `MainForm`, встаёт
  ПОСЛЕДНИМ (замер: три ребёнка плюс `MdiClient` дают 0/1/2/3, и порядок не
  зависит от того, ставится ли `IsMdiContainer` до детей или после);
* `WeifenLuo.DockContent`, от которого наследует `DocEnergySpectrum`, своих
  детей не имеет вовсе (замер: 0).

Сдвиг был просто устаревшей нумерацией в `.resx`, и он снят правкой `A118`.
Поэтому пункт 3 — такая же находка, как остальные, а не «повод посмотреть».

## Состояние на 05.09.2026 (после закрытия `A118`)

Пар `Foo.Designer.cs` / `Foo.resx` — 43, контейнеров с метаданными — 87,
расхождений **0**, код возврата 0. ⚠ Числа записаны на вечер 05.09.2026 и
растут с деревом (было 42 / 78 утром, до `FSAReportView`); живое печатает сама
сверка первыми двумя строками — сверять надо с ними, а не с этим абзацем. `A87` закрыла `NucBase :: panel1`, `A118` —
остальные десять (`DCPeakDetectionView`, `DeviceConfigForm`, `NuclideSetForm`,
`ROIConfigForm` дважды, `DCFwhmCalibrationView`, `DCControlPanel`, `MainForm`,
`DocEnergySpectrum`, `EnergySpectrumView`).

⛔ Сверка говорит, что метаданные СОГЛАСОВАНЫ, но не что конструктор Visual
Studio их принял и форму нарисовал: проверка ОТКРЫТИЕМ форм в конструкторе
машинной сверкой не заменяется и остаётся за Amber.

Запуск:
    python tools/check_resx_zorder.py [--form NucBase] [--out FILE]
Код возврата 0 — расхождений нет, 1 — есть.

## ⚠ Формат файла — плечо сторожа (`T156`, 05.09.2026)

Каждый `*.resx` обязан быть UTF-8 С BOM и с переводами строк CRLF, без
примесей (правило, счёт по дереву и причины — в `tools/resx_format.py`).
Файл чужого формата сверка НЕ читает молча: печатает `ФОРМАТ  <файл>: чем
плох` и возвращает 1, даже если по своему предмету всё сошлось. Правка,
написанная под BOM+CRLF, на таком файле отказывает (`A118`), и заход уходит
на выяснение причины — плечо заведено, чтобы причину называл сторож.
Снять плечо — ключ `--no-format` (на дереве с `core.autocrlf=false` всё
лежит LF, и там оно красно на всех файлах по устройству, а не по дефекту).

⛔ **Плечо формата идёт по ВСЕМ `*.resx` дерева, а не по тем, что прочла эта
сверка** (`T234`, 05.09.2026). Раньше ему подавался список `pairs` — 43 файла,
у которых есть парный `Foo.Designer.cs`, — и `*.ru.resx` не проверялся НИКОГДА,
хотя их 32 из 77. Измерено на копии дерева с одним переведённым в LF
`NuclideSetForm.ru.resx`: три остальные сверки назвали 1 файл чужого формата и
код 1, эта — 0 файлов и код 0, то есть отказ пропадал целиком. Список берётся
тем же выражением, что у `check_resx.py`, `check_resx_letters.py` и
`check_resx_designer.py`, — чтобы четыре сверки называли одно и то же число.
⚠ Поэтому `--form` сужает ПРЕДМЕТ сверки, но не плечо формата: формат — про
дерево целиком, и молчать о чужом файле из-за сужения предмета нельзя.

## ⛔ ПУСТОЙ КОРЕНЬ — ОТКАЗ (`T238`, 05.09.2026)

Умолчание корня здесь абсолютное (`<этот файл>/../BecquerelMonitor`) и было
таким всегда — первой половины `T238`, из-за которой три остальные сверки
молча проверяли пустоту при запуске не из корня репозитория, у этой сверки
нет. А ВТОРАЯ половина была: корень можно передать явно (`--root`) и
промахнуться, и на заведомо пустом каталоге сверка печатала «пар 0,
контейнеров 0, расхождений нет» и возвращала 0. Замер 05.09.2026, до правки:

    пустой корень → check_resx 1, check_resx_letters 1,
                    check_resx_designer 1, check_resx_zorder **0**

То есть после починки трёх сторожа разошлись ровно тем родом, что чинила
`T234`: сверка, которой нечего проверять, отчитывалась успехом, а «сторож
resx 0» — приёмочная формулировка десятков строк реестра.

Теперь корень, под которым нет ни одного `*.resx` или ни одного
`*.Designer.cs` (или которого нет вовсе), даёт строку `ПУСТОЙ КОРЕНЬ …` и код
1. Проверка сделана тем же выражением и с той же константой `NEEDED`, что у
трёх остальных, — разнобой в СПОСОБЕ был бы тем же дефектом под другим именем.
⚠ Единственное отличие от них по виду: второй строкой они печатают свой
приговор `РАЗОШЛОСЬ`, а у этой сверки такого слова нет вовсе (её приговор —
тело отчёта плюс код возврата), поэтому и здесь его нет.

⛔ **Та же дыра была у `--form`, и найдена она приёмкой этой правки:**
`--form NoSuchFormAtAll` на ПОЛНОМ дереве печатал «пар 0, контейнеров 0,
расхождений нет» и возвращал 0. Ключа `--form` у трёх остальных сверок нет,
поэтому в `T238` этот случай не попал, но разряд у него тот же — сторож,
которому нечего проверять, отчитывался успехом. Поэтому при `--form X`
`NEEDED` сужается до `X.resx` и `X.Designer.cs`, а слово отказа остаётся ОДНО
на оба случая. ⚠ Плечо формата этим не сужается (см. выше): оно про дерево.

    python tools/check_resx_zorder.py --root <пустой каталог>   → код 1
    python tools/check_resx_zorder.py --form NoSuchFormAtAll    → код 1
"""
import argparse
import glob
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

import resx_format

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.normpath(os.path.join(HERE, "..", "BecquerelMonitor"))

# Что обязано найтись под корнем, чтобы сверке было что проверять (`T238`).
# Это ВТОРОЕ требование той же строки, и оно отдельное: корень можно передать
# явно и промахнуться, а сторож, которому нечего проверять, обязан сказать об
# этом и ОТКАЗАТЬ, а не отчитаться успехом. Предмет здесь — ПАРА
# `Foo.Designer.cs` / `Foo.resx`, поэтому названы оба вида файла: без
# `*.Designer.cs` не с чем сверять метаданные, без `*.resx` нет самих
# метаданных. `*.ru.resx` в списке НЕТ: русский файл designer-метаданных не
# держит, и его предметом этой сверки не является (в плече формата он, как и
# все прочие resx дерева, участвует — см. `T234`).
NEEDED = (u"*.resx", u"*.Designer.cs")

ap = argparse.ArgumentParser()
ap.add_argument("--root", default=APP, help="корень приложения")
ap.add_argument("--form", default=None, help="только эта форма (имя без .resx)")
ap.add_argument("--out", default=None, help="писать отчёт в файл (UTF-8)")
ap.add_argument("--no-format", action="store_true",
                help="не проверять формат файлов (BOM + CRLF, T156)")
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


def empty_root(root, needed=NEEDED):
    u"""Причина, по которой проверять нечего, либо `None` (`T238`)."""
    if not os.path.isdir(root):
        return u"каталога нет"
    missing = [p for p in needed
               if not glob.glob(os.path.join(root, "**", p), recursive=True)]
    if missing:
        return u"ни одного %s" % u", ".join(missing)
    return None


def emit(text):
    u"""Вывод — туда же, куда и отчёт: в `--out` либо в stdout."""
    if args.out:
        io.open(args.out, "w", encoding="utf-8", newline="").write(text)
        print("написано: %s" % args.out)
    else:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stdout.write(text)


# ⛔ Пустой корень — ОТКАЗ, а не «расхождений нет» (`T238`).
# ⚠ При `--form` предмет сужен до ОДНОЙ формы, и «проверять нечего» сужается
# вместе с ним: опечатка в имени формы давала «пар 0, контейнеров 0,
# расхождений нет» и код 0 — та же подделка зелёного, только другим ключом.
# Слово отказа при этом ОДНО на оба случая: заводить второе значило бы тот же
# разнобой, что чинила `T234`.
needed = NEEDED if not args.form else tuple(args.form + p.lstrip("*") for p in NEEDED)
why = empty_root(args.root, needed)
if why is not None:
    emit(u"ПУСТОЙ КОРЕНЬ  %s: %s — проверять нечего\n" % (args.root, why))
    sys.exit(1)

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

# Плечо формата (`T156`) — по ВСЕМ resx дерева, не только парным и не только
# прочитанным (`T234`), чтобы четыре сверки называли одно и то же число.
fmt = [] if args.no_format else resx_format.problems(
    sorted(glob.glob(os.path.join(args.root, "**", "*.resx"), recursive=True)))
report.extend(fmt)
buf.write(u"контейнеров проверено: %d, с расхождением: %d\n" % (n_cont, n_bad))
buf.write(u"файлов чужого формата (не BOM+CRLF): %d\n" % len(fmt))
buf.write(u"=" * 74 + u"\n")
buf.write(u"\n".join(report) if report else u"расхождений нет")
buf.write(u"\n")
emit(buf.getvalue())
sys.exit(1 if n_bad or fmt else 0)
