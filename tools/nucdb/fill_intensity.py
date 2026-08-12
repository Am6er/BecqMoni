# -*- coding: utf-8 -*-
"""
Проставить выходы линий (`Intencity`), метку ряда (`Chain`) и разметку по
наборам (`Sets`) в поставочный `config/NuclideDefinition.xml` из `nucdb.sqlite`.

Строка задачи — S28. Без этого на свежей установке полноспектральный разбор
живёт одной встроенной таблицей на 11 нуклидов: образ по конфигу строится
только из линий, у которых выход заполнен (`FsaLibrary.BuildFromPeaks`), а
разметки по наборам нет вовсе.

СОГЛАШЕНИЕ О НОРМИРОВКЕ ИЗМЕНЕНО 08.08.2026 (решение Amber). Раньше выход
писался на распад СОБСТВЕННОГО нуклида и поле `Chain` оставалось пустым.
Теперь у членов ряда выход даётся НА РАСПАД РОДИТЕЛЯ РЯДА, а `Chain`
заполняется его именем — ровно то, чего ждёт потребитель:
`EfficiencyLibrary.BuildChains` («там выходы записаны на распад родителя ряда,
а это ровно то, что нужно для векового равновесия»).

Множитель перехода — накопленная доля ветвления от родителя к нуклиду,
посчитанная обходом `decay_chain` по ВСЕМ путям (у Bi-214 их два: через
Po-218 -> Pb-214 и через At-218). Проверка: Tl-208 выходит 35.94 % от распада
Th-232 — справочное значение того же ряда.

Родители пробуются по порядку, первый достигший и берётся:

    232TH -> «Th-232»    226RA -> «Ra-226»    238U -> «U-238»    235U -> «U-235»

Нуклид, который сам является родителем, и нуклид, не достижимый ни от одного
из них (Cs-137, Co-60, K-40, Eu-152, Am-241, Ba-133, Lu-176, I-131, Np-239,
Cs-134, Ag-108m, плутоний), получает выход на собственный распад и пустой
`Chain` — по семантике `NuclideDefinition.Chain` это и значит «на себя».

КАК ИЩЕТСЯ ЛИНИЯ

  * токен нуклида — всё до первого пробела (правило `NuclideNameOf`);
  * `nucid` = масса + символ («Cs-137» -> 137CS, «K-40» -> 40K); ИЗОМЕРЫ
    больше не пропускаются: в базе они лежат отдельными записями с суффиксом
    («234PAm1», «108AGm», «176LUm»), пробуются `m`, `m1`, `m2` и берётся та,
    что есть в `nuclides`. Прежний отказ («у Ag-108m только основное
    состояние») сегодня неверен — у 108AGm 4 линии с выходом до 90.8 %;
  * гамма-линии из `decay_radiations` (`type_a = 'G'`);
  * энергия подписи округлена — берётся ближайшая линия в пределах ±4 кэВ;
    если вторая по близости ближе полутора расстояний первой И сильнее её,
    подпись неоднозначна и запись пропускается. НО неоднозначностью считается
    только то, что прибор РАЗДЕЛЯЕТ: кандидаты, разнесённые меньше чем на
    `UNRESOLVED_KEV`, — это один пик, и из них берётся сильнейшая. Так
    разрешилась `Np-239` 228, где 227.830 (0.51 %) и 228.183 (10.73 %) стоят
    в 0.35 кэВ друг от друга и прежнее правило оставляло запись без выхода
    (S33 «в», решение Amber 12.08.2026).

ЧТО ПРОПУСКАЕТСЯ СОЗНАТЕЛЬНО

  * подписи С КОСОЙ ЧЕРТОЙ — «U-238/U-234», «Bi-212/Ac-228», «Am-241/x-rays»
    и остальные: одна линия названа двумя источниками, и выход такой записи не
    определён в принципе (решение Amber 08.08.2026 — оставить без выхода).
    Подпись при этом честная: линия и вправду общая, и на графике так и надо;
  * «x-rays», «Low Bremsstrahlung x-rays», «Annihilation» — не нуклиды;
  * «Tl-208 SE» — пик вылета: линии 2103 у Tl-208 нет, отсеивается сам;
  * «W x-ray» / «Pb x-ray» — характеристический рентген, выхода НА РАСПАД у
    него не существует; их доли внутри K-серии уже проставлены (S12) и не
    трогаются, как и любое непустое `Intencity`.

ВТОРОЙ ПРОХОД — ДОБОР ЛИНИЙ. Образ нуклида строится из ВСЕХ его строк файла,
и образ из трёх видимых линий хуже встроенного из шести. Поэтому каждому
нуклиду, получившему хоть одну линию, дописываются недостающие гамма-линии с
выходом ≥ 1 % (порог из критериев отбора сетов) СКРЫТЫМИ записями
(`Visible=false`): на графике их нет, а образ полон. Занятые энергии (±3 кэВ)
пропускаются, поэтому повторный прогон ничего не дублирует.

ТРЕТИЙ ПРОХОД — НАБОРЫ. Заводятся пять наборов теми же именами, что у
пользователя: `Th-232`, `U+Ra`, `Cs-137+K-40`, `Lu-176`, `NORM`. Состав — по
тому, что есть в самом файле: ряд тория, ряд урана-радия, калий с цезием,
лютеций; `NORM` — объединение всего природного. Идентификаторы наборов
ПОСТОЯННЫЕ (заданы здесь константами), иначе повторный прогон рвал бы ссылки
у пользователя, который уже открыл файл.

    python fill_intensity.py <nucdb.sqlite> <NuclideDefinition.xml>
"""
import io
import re
import sqlite3
import sys


# Родители рядов: в каком порядке пробовать и как подписывать `Chain`.
CHAIN_ROOTS = [("232TH", "Th-232"), ("226RA", "Ra-226"),
               ("238U", "U-238"), ("235U", "U-235")]

# Ближе этого две линии не разделит ни один прибор из корпуса: лучшее
# разрешение там — HPGe, 0.22 % на 662 кэВ, то есть ~1.5 кэВ полуширины, и
# внизу шкалы оно не лучше. Поэтому пара, разнесённая меньше чем на столько,
# для подписи есть ОДИН пик.
UNRESOLVED_KEV = 0.5

# Наборы: постоянные идентификаторы и правило состава. Правило — по МЕТКЕ РЯДА
# (`Chain`) и по токену нуклида, а не по списку энергий: список энергий
# разошёлся бы с файлом при первом же доборе линий.
SETS = [
    ("Th-232",      "b1f0a1c2-3d4e-4f50-9a61-7c8d9e0f1a21", {"Th-232"}, {"Th-232"}),
    ("U+Ra",        "c2e1b2d3-4e5f-4061-8b72-8d9e0f1a2b32", {"Ra-226", "U-238", "U-235"},
                    {"U-238", "U-235", "Ra-226", "Pb-210"}),
    ("Cs-137+K-40", "d3f2c3e4-5f60-4172-9c83-9e0f1a2b3c43", set(), {"Cs-137", "K-40"}),
    ("Lu-176",      "e4031405-6071-4283-8d94-0f1a2b3c4d54", set(), {"Lu-176"}),
    ("NORM",        "f5142516-7182-4394-9ea5-1a2b3c4d5e65",
                    {"Th-232", "Ra-226", "U-238", "U-235"},
                    {"Th-232", "U-238", "U-235", "Ra-226", "Pb-210", "K-40", "Lu-176"}),
]


def nucid_candidates(token):
    """«Cs-137» -> [137CS]; «Pa-234m» -> [234PAm, 234PAm1, 234PAm2]."""
    m = re.match(r"^([A-Za-z]{1,2})-?(\d{1,3})([mM]\d?)?$", token)
    if not m:
        return []
    base = "%d%s" % (int(m.group(2)), m.group(1).upper())
    if not m.group(3):
        return [base]
    return [base + "m", base + "m1", base + "m2"]


def chain_factors(db, root):
    """Доля распадов нуклида на один распад `root` — по всем путям ряда."""
    reach = {root: 1.0}
    order = [root]
    guard = 0
    while order and guard < 10000:
        guard += 1
        node = order.pop()
        share = reach[node]
        for daughter, perc in db.execute(
                "select daughter_nucid, perc from decay_chain"
                " where nucid = ? and l_seqno = 0", (node,)):
            if daughter is None or perc is None or daughter == node:
                continue
            try:
                part = share * float(perc) / 100.0
            except (TypeError, ValueError):
                continue
            if part <= 1e-9:
                continue
            # Ряд конечен, но в таблице встречаются петли через изомеры —
            # дальше идём, только если доля выросла заметно.
            if daughter in reach and part <= reach[daughter] * 1.000001:
                continue
            reach[daughter] = reach.get(daughter, 0.0) + part
            order.append(daughter)

    return reach


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    db = sqlite3.connect(sys.argv[1])
    path = sys.argv[2]

    with io.open(path, encoding="utf-8-sig") as f:
        text = f.read()

    roots = [(root, label, chain_factors(db, root)) for root, label in CHAIN_ROOTS]
    lines_cache = {}
    nucid_cache = {}

    def resolve(token):
        """Токен подписи -> (nucid, метка ряда, множитель к выходу)."""
        if token in nucid_cache:
            return nucid_cache[token]

        result = (None, "", 1.0)
        for nucid in nucid_candidates(token):
            row = db.execute("select 1 from nuclides where nucid = ?", (nucid,)).fetchone()
            if row is None:
                continue
            chain, factor = "", 1.0
            for root, label, reach in roots:
                if nucid == root:
                    break                       # сам родитель — выход на себя
                if nucid in reach:
                    chain, factor = label, reach[nucid]
                    break
            result = (nucid, chain, factor)
            break

        nucid_cache[token] = result
        return result

    def gamma_lines(nucid):
        if nucid not in lines_cache:
            # числовые колонки *_num: строковые energy/intensity хранят запись
            # оригинала с неопределённостью (scheme.md, §2)
            lines_cache[nucid] = db.execute(
                "select energy_num, intensity_num from decay_radiations"
                " where parent_nucid = ? and type_a = 'G'"
                " and energy_num is not null and intensity_num is not null",
                (nucid,)).fetchall()
        return lines_cache[nucid]

    filled, skipped = [], []

    def patch(match):
        block = match.group(0)
        name = re.search(r"<Name>(.*?)</Name>", block)
        energy = re.search(r"<Energy>([\d.]+)</Energy>", block)
        if not name or not energy:
            return block
        label = name.group(1)
        e = float(energy.group(1))

        if "<Intencity>" in block:
            return block

        token = label.split(" ")[0].strip()
        if "/" in token:
            skipped.append((label, e, "сдвоенная подпись: выход не определён"))
            return block

        nucid, chain, factor = resolve(token)
        if nucid is None:
            skipped.append((label, e, "не нуклид либо нет в базе"))
            return block

        candidates = sorted(gamma_lines(nucid), key=lambda r: abs(r[0] - e))
        if not candidates or abs(candidates[0][0] - e) > 4.0:
            skipped.append((label, e, "нет линии в ±4 кэВ"))
            return block

        # Линии, которые НИ ОДИН прибор не разделит, — это один пик, а не
        # неоднозначность: у Np-239 под меткой 228 лежат 227.830 (0.51 %) и
        # 228.183 (10.73 %), между ними 0.35 кэВ. Прежнее правило объявляло
        # такую подпись неоднозначной и оставляло запись без выхода, хотя
        # ответ очевиден — берётся сильнейшая (решение Amber 12.08.2026, S33).
        # Неоднозначностью считается только то, что прибор РАЗДЕЛЯЕТ.
        same_peak = [c for c in candidates if abs(c[0] - candidates[0][0]) < UNRESOLVED_KEV]
        best = max(same_peak, key=lambda r: r[1])
        rest = [c for c in candidates if c not in same_peak]
        if rest:
            second = rest[0]
            if (abs(second[0] - e) < 1.5 * max(0.3, abs(candidates[0][0] - e))
                    and second[1] > best[1]):
                skipped.append((label, e, "неоднозначно: %.1f и %.1f кэВ"
                                % (best[0], second[0])))
                return block

        yield_percent = best[1] * factor
        filled.append((label, e, best[0], best[1], chain, yield_percent))
        return block.replace(
            "</Visible>",
            "</Visible>\n      <Intencity>%g</Intencity>" % round(yield_percent, 4)
            + (("\n      <Chain>%s</Chain>" % chain) if chain else ""),
            1)

    text = re.sub(r"<Nuclide>.*?</Nuclide>", patch, text, flags=re.S)

    # --- добор недостающих линий скрытыми записями -----------------------
    per_nuclide = {}
    for m in re.finditer(r"<Nuclide>.*?</Nuclide>", text, flags=re.S):
        block = m.group(0)
        name = re.search(r"<Name>(.*?)</Name>", block)
        energy = re.search(r"<Energy>([\d.]+)</Energy>", block)
        if not name or not energy:
            continue
        token = name.group(1).split(" ")[0].strip()
        if "/" in token:
            continue
        nucid, chain, factor = resolve(token)
        if nucid is None:
            continue
        entry = per_nuclide.setdefault(nucid, {
            "token": token, "chain": chain, "factor": factor, "energies": [],
            "half_life": None, "color": None, "filled": False,
        })
        label_e = float(energy.group(1))
        entry["energies"].append(label_e)
        # Занятой считается и БЛИЖАЙШАЯ к метке линия базы (в ±4 кэВ): метка
        # округлена («Eu-152 125» несёт выход линии 121.78), и без этого добор
        # клал бы ту же линию второй раз — двойной вес в образе. Ближайшая, а
        # не все в окне: у Cs-134 в ±4 кэВ от метки 798 лежат ДВЕ настоящие
        # линии (795.86 и 801.95), и вторую добор обязан доложить.
        near = [le for le, _ in gamma_lines(nucid) if abs(le - label_e) <= 4.0]
        if near:
            entry["energies"].append(min(near, key=lambda le: abs(le - label_e)))
        if "<Intencity>" in block:
            entry["filled"] = True
        if entry["half_life"] is None:
            hl = re.search(r"<HalfLife>(.*?)</HalfLife>", block)
            color = re.search(r"<NuclideColor>(.*?)</NuclideColor>", block)
            entry["half_life"] = hl.group(1) if hl else "0"
            entry["color"] = color.group(1) if color else "Gray"

    added = []
    blocks = []
    for nucid, entry in sorted(per_nuclide.items()):
        if not entry["filled"]:
            continue
        for le, i in sorted(gamma_lines(nucid)):
            # Порог — на выход НА РАСПАД РОДИТЕЛЯ, то есть на то самое число,
            # которое ляжет в файл: иначе у Tl-208 отбор шёл бы по 99.75 %, а в
            # файл попадало 35.8 %, и «≥ 1 %» значило бы разное у разных строк.
            value = i * entry["factor"]
            if value < 1.0:
                continue
            if any(abs(le - taken) <= 3.0 for taken in entry["energies"]):
                continue
            entry["energies"].append(le)
            added.append((entry["token"], le, value))
            blocks.append(
                "    <Nuclide>\n"
                "      <Name>%s</Name>\n"
                "      <Energy>%g</Energy>\n"
                "      <HalfLife>%s</HalfLife>\n"
                "      <NuclideColor>%s</NuclideColor>\n"
                "      <Note />\n"
                "      <Visible>false</Visible>\n"
                "      <Intencity>%g</Intencity>\n"
                % (entry["token"], round(le, 3), entry["half_life"],
                   entry["color"], round(value, 4))
                + (("      <Chain>%s</Chain>\n" % entry["chain"])
                   if entry["chain"] else "")
                + "    </Nuclide>\n")

    if blocks:
        text = text.replace("</NuclideDefinitions>",
                            "".join(blocks) + "  </NuclideDefinitions>", 1)

    # --- наборы ----------------------------------------------------------
    # Порядок элементов внутри записи задан объявлением полей
    # NuclideDefinition (Name, Energy, HalfLife, NuclideColor, Note, Visible,
    # Intencity, Sets, Chain) — XmlSerializer читает его в этом же порядке.
    in_set = {}

    def sets_of(token):
        if token in in_set:
            return in_set[token]
        nucid, chain, _ = resolve(token)
        guids = []
        for name, guid, chains, tokens in SETS:
            if (chain and chain in chains) or token in tokens:
                guids.append(guid)
        in_set[token] = guids
        return guids

    marked = [0]

    def mark(match):
        block = match.group(0)
        if "<Sets>" in block:
            return block
        name = re.search(r"<Name>(.*?)</Name>", block)
        if not name:
            return block
        token = name.group(1).split(" ")[0].strip()
        if "/" in token:
            return block
        guids = sets_of(token)
        if not guids:
            return block

        body = "".join("        <guid>%s</guid>\n" % g for g in guids)
        marked[0] += 1
        # Sets стоит ПОСЛЕ Intencity и ПЕРЕД Chain.
        if "<Chain>" in block:
            return block.replace("      <Chain>",
                                 "      <Sets>\n%s      </Sets>\n      <Chain>" % body, 1)
        if "<Intencity>" in block:
            return re.sub(r"(<Intencity>[^<]*</Intencity>\n)",
                          r"\1      <Sets>\n%s      </Sets>\n" % body, block, count=1)
        return block.replace("</Visible>",
                             "</Visible>\n      <Sets>\n%s      </Sets>" % body, 1)

    text = re.sub(r"<Nuclide>.*?</Nuclide>", mark, text, flags=re.S)

    if "<NuclideSets>" not in text:
        declared = "  <NuclideSets>\n" + "".join(
            "    <NuclideSet>\n"
            "      <Id>%s</Id>\n"
            "      <Name>%s</Name>\n"
            "      <HideUnknownPeaks>false</HideUnknownPeaks>\n"
            "    </NuclideSet>\n" % (guid, name)
            for name, guid, _, _ in SETS) + "  </NuclideSets>\n"
        text = text.replace("</NuclideDefinitionFile>",
                            declared + "</NuclideDefinitionFile>", 1)

    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)

    out = io.open(1, "w", encoding="utf-8", closefd=False)
    out.write("проставлено %d:\n" % len(filled))
    for label, e, le, own, chain, value in filled:
        out.write("  %-24s %7.1f -> линия %8.2f кэВ, I = %g %% на распад %s\n"
                  % (label, e, le, value, chain or "свой"))
        if chain:
            out.write("  %-24s          (на собственный распад %g %%)\n" % ("", own))
    out.write("пропущено %d:\n" % len(skipped))
    for label, e, why in skipped:
        out.write("  %-24s %7.1f -> %s\n" % (label, e, why))
    out.write("добрано скрытых линий (>=1 %%): %d\n" % len(added))
    for token, le, i in added:
        out.write("  %-10s %9.2f кэВ, I = %g %%\n" % (token, le, i))
    out.write("размечено по наборам записей: %d\n" % marked[0])
    out.flush()


if __name__ == "__main__":
    main()
