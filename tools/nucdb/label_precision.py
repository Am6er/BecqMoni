# -*- coding: utf-8 -*-
"""
Насколько ВИДИМЫЕ подписи поставочного `config/NuclideDefinition.xml` попадают
в настоящие линии — и что стоило бы поправить.

Строки задачи — S38 (точные линии в конфиге СКРЫТЫ, и подпись достаётся
округлённой записи), S36 «а» (`Am-241/x-rays` записан на 59.000 при истинных
59.541), S33 «а» (сдвоенные подписи).

ЗАЧЕМ. Состав библиотеки FSA задаёт ПОИСК ПИКОВ: `FsaLibrary.BuildFromPeaks`
берёт нуклиды у подписанных пиков, а подписывает `PeakDetector.MatchNuclide`,
и он перебирает только записи с `Visible = true`. Точные линии, дописанные
ради полноты образов, лежат скрытыми — поэтому подпись уходит более старой
видимой записи, округлённой до целых кэВ, и в библиотеку попадает чужой
нуклид. Измерено на корпусе: так рождается крупнейший фантом (Np-239 забирает
87 % пиковой доли у Ac-228 на Obsidian, S35).

    python tools/nucdb/label_precision.py database/nucdb.sqlite \
           BecquerelMonitor/config/NuclideDefinition.xml [--apply]

⛔ `--apply` ВЫКЛЮЧЕН по умолчанию. Без него инструмент только считает и
печатает; файл не трогается. Правка поставочного конфига — решение Amber.

ЧТО ДЕЛАЕТ `--apply`. Ровно одну правку и только там, где она безопасна:
если у видимой округлённой записи есть СКРЫТЫЙ близнец того же нуклида,
стоящий на настоящей линии, флаги `Visible` меняются местами. Ни одна запись
не добавляется и не удаляется, ни одна энергия не переписывается — значит,
образ компонента (он строится по записям с выходом, а не по видимости) не
меняется ВООБЩЕ, меняется только то, какая запись имеет право подписать пик.

Почему не «переписать энергию у видимой»: у нуклида тогда окажутся две записи
на одной линии — старая исправленная и скрытый близнец, — и в образе линия
задвоится. Этой ловушкой уже поймана S33 «б».

Почему не «разрешить скрытым подписывать»: проверено прогоном по всему
корпусу — фантомов 25 -> 37 в непонятной части и 2 -> 5 в понятной. Полсотни
точных линий раздают подписи всем подряд, правило «скрытые не подписывают»
защитное.
"""

import io
import os
import re
import sqlite3
import sys

# Насколько далеко от подписи ищется настоящая линия. То же окно, что у
# `fill_intensity.py`: подписи округлены до целых, а местами и «придуманы».
SEARCH_KEV = 4.0

# Ближе этого две линии не разделит ни один прибор корпуса — для подписи это
# ОДИН пик, и из кандидатов берётся сильнейший (правило S33 «в»).
UNRESOLVED_KEV = 0.5

# Подпись считается ОКРУГЛЁННОЙ, если её энергия — целое число кэВ. Это не
# догадка: в файле такие записи стоят с 2024 года, а точные дописаны вторым
# проходом `fill_intensity.py` и имеют дробную часть.
def is_rounded(energy):
    return abs(energy - round(energy)) < 1e-9


def nucid_candidates(token):
    """«Cs-137» -> [137CS]; «Pa-234m» -> [234PAm, 234PAm1, 234PAm2]."""
    m = re.match(r"^([A-Za-z]{1,2})-?(\d{1,3})([mM]\d?)?$", token)
    if not m:
        return []
    base = "%d%s" % (int(m.group(2)), m.group(1).upper())
    if not m.group(3):
        return [base]
    return [base + "m", base + "m1", base + "m2"]


class Record(object):
    def __init__(self, index, name, energy, visible, intensity):
        self.index = index
        self.name = name
        self.energy = energy
        self.visible = visible
        self.intensity = intensity
        self.token = name.split(" ")[0]         # правило NuclideNameOf

    def __repr__(self):
        return "%s %.3f %s" % (self.name, self.energy,
                               "видима" if self.visible else "скрыта")


def read_records(text):
    """Разбор без XML-парсера: файл правится посимвольно, порядок и отступы
    сохраняются, а нам нужны только поля и позиции блоков."""
    records = []
    for i, block in enumerate(re.findall(r"<Nuclide>.*?</Nuclide>", text, re.S)):
        name = re.search(r"<Name>(.*?)</Name>", block, re.S)
        energy = re.search(r"<Energy>(.*?)</Energy>", block, re.S)
        visible = re.search(r"<Visible>(.*?)</Visible>", block, re.S)
        intensity = re.search(r"<Intencity>(.*?)</Intencity>", block, re.S)
        if name is None or energy is None:
            continue
        try:
            value = float(energy.group(1))
        except ValueError:
            continue
        records.append(Record(
            i, name.group(1), value,
            visible is not None and visible.group(1).strip() == "true",
            float(intensity.group(1)) if intensity else None))
    return records


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    apply_it = "--apply" in sys.argv[1:]
    if len(args) != 2:
        sys.exit(__doc__)

    # sqlite3.connect СОЗДАЁТ файл, если его нет, и промах по пути выглядит
    # как «в базе нет таблицы nuclides» — а в дереве остаётся пустой
    # nucdb.sqlite. Поймано на себе 13.08.2026: разрез базы (D25) положил
    # рабочие файлы рядом со сборкой, а не в `database/`.
    if not os.path.exists(args[0]):
        sys.exit("нет файла базы: %s\n"
                 "После разреза (D25) куски лежат рядом со сборкой —\n"
                 "например BecquerelMonitor/nucdb.sqlite." % args[0])

    db = sqlite3.connect(args[0])
    if db.execute("select count(*) from sqlite_master"
                  " where type = 'table' and name = 'nuclides'").fetchone()[0] == 0:
        sys.exit("в %s нет таблицы `nuclides` — это не тот кусок базы.\n"
                 "Нуклиды и линии лежат в nucdb.sqlite." % args[0])

    path = args[1]
    with io.open(path, encoding="utf-8-sig") as f:
        text = f.read()

    records = read_records(text)
    visible = [r for r in records if r.visible]
    hidden = [r for r in records if not r.visible]
    print("записей %d: видимых %d, скрытых %d"
          % (len(records), len(visible), len(hidden)))
    print()

    cache = {}

    def gamma_lines(token):
        if token in cache:
            return cache[token]
        rows = []
        for nucid in nucid_candidates(token):
            if db.execute("select 1 from nuclides where nucid = ?",
                          (nucid,)).fetchone() is None:
                continue
            rows = db.execute(
                "select energy_num, intensity_num from decay_radiations"
                " where parent_nucid = ? and type_a = 'G'"
                " and energy_num is not null and intensity_num is not null",
                (nucid,)).fetchall()
            break
        cache[token] = rows
        return rows

    def nearest(token, energy):
        """Ближайшая настоящая линия; из неразделимых берётся сильнейшая."""
        rows = gamma_lines(token)
        best = None
        for e, i in rows:
            if abs(e - energy) > SEARCH_KEV:
                continue
            if best is None:
                best = (e, i)
                continue
            de, di = abs(e - energy), abs(best[0] - energy)
            if abs(de - di) < UNRESOLVED_KEV:
                if i > best[1]:
                    best = (e, i)
            elif de < di:
                best = (e, i)
        return best

    compound, nolines, exact, swap, orphan = [], [], [], [], []
    for r in visible:
        if "/" in r.token:
            compound.append(r)
            continue
        if not gamma_lines(r.token):
            nolines.append(r)
            continue
        true_line = nearest(r.token, r.energy)
        if true_line is None:
            orphan.append((r, None))
            continue
        delta = abs(r.energy - true_line[0])
        if delta < 0.05:
            exact.append(r)
            continue
        twin = None
        for h in hidden:
            if h.token == r.token and abs(h.energy - true_line[0]) < 0.05:
                twin = h
                break
        if twin is not None:
            swap.append((r, true_line, twin))
        else:
            orphan.append((r, true_line))

    # ГЛАВНОЕ: скрытая точная линия, которую ЗАСЛОНЯЕТ видимая запись ДРУГОГО
    # нуклида. Это и есть механизм S38 — пик на такой энергии получит чужое
    # имя при любом приборе, потому что ближайшая ВИДИМАЯ запись чужая.
    shadowed = []
    for h in hidden:
        near = None
        for v in visible:
            d = abs(v.energy - h.energy)
            if d > SEARCH_KEV:
                continue
            if near is None or d < abs(near.energy - h.energy):
                near = v
        if near is None:
            continue
        if near.token.split("/")[0] == h.token or h.token in near.token.split("/"):
            continue                            # свой же нуклид — не подмена
        shadowed.append((h, near))

    print("== СКРЫТЫЕ ТОЧНЫЕ ЛИНИИ, ЗАСЛОНЁННЫЕ ЧУЖОЙ ВИДИМОЙ ЗАПИСЬЮ ==")
    print("   (пик на этой энергии получит ЧУЖОЕ имя при любом приборе:")
    print("    ближайшая видимая запись принадлежит другому нуклиду)")
    print()
    # Два разных случая, и путать их нельзя. Если у ВИДИМОЙ записи есть своя
    # настоящая линия рядом — это честное совпадение энергий двух нуклидов, и
    # данными оно не лечится. Если своей линии у неё нет, подпись придумана
    # или округлена, и она ворует чужой пик — вот это и надо чинить.
    real, stolen = [], []
    for h, v in shadowed:
        own = None
        for token in v.token.split("/"):
            got = nearest(token, v.energy) if gamma_lines(token) else None
            if got is not None and (own is None
                                    or abs(got[0] - v.energy) < abs(own[0] - v.energy)):
                own = got
        (real if own is not None and abs(own[0] - v.energy) <= 1.0
         else stolen).append((h, v, own))

    print("   А. ПОДПИСЬ ВОРУЕТ ЧУЖОЙ ПИК — у неё самой линии рядом нет")
    print("   %-14s %10s   %-24s %10s %8s   %s"
          % ("настоящий", "линия", "подпишется как", "запись", "сдвиг", "своя линия"))
    for h, v, own in sorted(stolen, key=lambda t: abs(t[1].energy - t[0].energy)):
        print("   %-14s %10.3f   %-24s %10.2f %8.2f   %s"
              % (h.token, h.energy, v.name, v.energy, v.energy - h.energy,
                 "нет" if own is None else "%.3f (%+.2f)" % (own[0], own[0] - v.energy)))
    print("   всего: %d" % len(stolen))
    print()
    print("   Б. ЧЕСТНОЕ СОВПАДЕНИЕ — у обоих нуклидов линия тут есть")
    print("   (данными не лечится: разделить их может только прибор)")
    for h, v, own in sorted(real, key=lambda t: abs(t[1].energy - t[0].energy)):
        print("   %-14s %10.3f   %-24s %10.2f   своя %.3f"
              % (h.token, h.energy, v.name, v.energy, own[0]))
    print("   всего: %d" % len(real))
    print("   ---- заслонено скрытых: %d из %d" % (len(shadowed), len(hidden)))
    print()

    print("== ВИДИМЫЕ ПОДПИСИ, У КОТОРЫХ ЕСТЬ СКРЫТЫЙ ТОЧНЫЙ БЛИЗНЕЦ ==")
    print("   (правка безопасна: меняются местами только флаги Visible)")
    print()
    print("   %-14s %10s %12s %8s   %s"
          % ("нуклид", "подпись", "линия", "сдвиг", "выход подписи"))
    for r, line, twin in sorted(swap, key=lambda t: -abs(t[0].energy - t[1][0])):
        print("   %-14s %10.2f %12.3f %8.2f   %s"
              % (r.token, r.energy, line[0], r.energy - line[0],
                 "нет" if r.intensity is None else "%.3f %%" % r.intensity))
    print("   всего: %d" % len(swap))
    print()

    print("== ВИДИМЫЕ ПОДПИСИ БЕЗ ТОЧНОГО БЛИЗНЕЦА ==")
    print("   (правка НЕ автоматическая: переписать энергию значит задвоить")
    print("    линию в образе, а снять подпись — решение по каждой отдельно)")
    print()
    for r, line in sorted(orphan, key=lambda t: t[0].token):
        if line is None:
            print("   %-14s %10.2f   настоящей линии в +-%.0f кэВ НЕТ ВОВСЕ"
                  % (r.token, r.energy, SEARCH_KEV))
        else:
            print("   %-14s %10.2f   ближайшая %.3f (%.2f %%), сдвиг %+.2f"
                  % (r.token, r.energy, line[0], line[1], r.energy - line[0]))
    print("   всего: %d" % len(orphan))
    print()

    print("== СДВОЕННЫЕ ПОДПИСИ (S33 «а») ==")
    for r in compound:
        print("   %-24s %10.2f" % (r.name, r.energy))
    print("   всего: %d" % len(compound))
    print()

    print("== ПОДПИСИ, У КОТОРЫХ НУКЛИД НЕ РАЗБИРАЕТСЯ ==")
    print("   (рентген, вылеты, аннигиляция — им линия из decay_radiations")
    print("    не положена; здесь только для полноты счёта)")
    names = {}
    for r in nolines:
        names[r.token] = names.get(r.token, 0) + 1
    for token in sorted(names):
        print("   %-24s записей %d" % (token, names[token]))
    print("   всего: %d" % len(nolines))
    print()

    print("точных подписей (сдвиг < 0.05 кэВ): %d" % len(exact))

    if not apply_it:
        print()
        print("--apply не задан: файл не тронут.")
        return

    # Правка ровно одна и самая узкая из возможных: ВЫДУМАННАЯ подпись
    # нуклида — та, у которой своей линии нет ни одной в пределах окна, — и
    # при этом заслоняющая настоящую чужую линию. Такая запись не описывает
    # ничего, а пик у настоящего нуклида отбирает.
    #
    # Меняются только флаги `Visible`: выдуманная гаснет, заслонённая точная
    # зажигается. Ни одна запись не добавляется, не удаляется и не меняет
    # энергию — образ компонента (он строится по записям с ВЫХОДОМ, а не по
    # видимости) остаётся прежним до последнего бита.
    #
    # Обобщённые метки («x-rays», «Pb x-ray», «W x-ray») сюда НЕ попадают
    # нарочно, хотя воруют подписи чаще всех: гасить их — это решение о том,
    # как приложение подписывает окно 40–100 кэВ, а не исправление опечатки.
    blocks = list(re.finditer(r"<Nuclide>.*?</Nuclide>", text, re.S))
    edits = {}
    for h, v, own in stolen:
        if own is not None or not nucid_candidates(v.token) or not gamma_lines(v.token):
            continue                            # обобщённая метка или своя линия есть
        edits[v.index] = "false"
        edits[h.index] = "true"
        print("   гашу выдуманную %s %.2f, зажигаю %s %.3f"
              % (v.name, v.energy, h.name, h.energy))

    if not edits:
        print("править нечего: выдуманных подписей, ворующих чужую линию, нет")
        return

    for r, line, twin in swap:
        edits[r.index] = "false"
        edits[twin.index] = "true"

    out, last = [], 0
    for i, block in enumerate(blocks):
        if i not in edits:
            continue
        piece = block.group(0)
        fixed = re.sub(r"<Visible>\s*(?:true|false)\s*</Visible>",
                       "<Visible>%s</Visible>" % edits[i], piece)
        out.append(text[last:block.start()])
        out.append(fixed)
        last = block.end()
    out.append(text[last:])

    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write("".join(out))
    print()
    print("ЗАПИСАНО: пар переставлено %d, записей тронуто %d"
          % (len(swap), len(edits)))


if __name__ == "__main__":
    main()
