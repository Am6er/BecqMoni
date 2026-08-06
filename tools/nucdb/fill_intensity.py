# -*- coding: utf-8 -*-
"""
Проставить выходы линий (`Intencity`) в поставочный `config/NuclideDefinition.xml`
из `nucdb.sqlite` (TODO N8: без выходов полноспектральный разбор работает на
встроенном десятке нуклидов вместо файла, scheme.md §9а F-1).

Соглашение о нормировке. Меток цепочки в поставке нет («Bi-214», а не
«Bi-214 (Ra-226)»), поле `Chain` остаётся пустым — а по его семантике
(NuclideDefinition.Chain) пустая цепочка означает «выход дан на распад
СОБСТВЕННОГО нуклида». Ровно так и заполняем: I на распад нуклида из подписи,
без ветвлений. Ловушка Tl-208 (выход на родителя ряда против выхода на сам
Tl-208, см. tools/effmaker/README.md «выходы Tl-208 не приведены к родителю»)
здесь не возникает — она про записи С меткой ряда.

Как ищется линия:

  * токен нуклида — всё до первого пробела (правило NuclideDefinition.NuclideNameOf);
    подписи с дробью («Am-241/x-rays», «Pb-210/x-rays») пробуются по ПЕРВОЙ
    части до «/» — это основной нуклид подписи;
  * `nucid` = масса + символ («Cs-137» -> 137CS, «K40» -> 40K);
  * гамма-линии нуклида из `decay_radiations` (type_a = 'G'), включая линии,
    сложенные на него с дочерних изомеров (l_seqno родителя любой);
  * энергия подписи округлена — берётся ближайшая линия в пределах ±4 кэВ;
    если вторая по близости линия ближе полутора расстояний первой И сильнее
    её — подпись неоднозначна, запись пропускается и печатается.

Что пропускается сознательно: «x-rays» без нуклида, «Annihilation»,
«Tl-208 SE» (пик вылета: линии 2103 у Tl-208 нет — отсеивается сам),
сдвоенные подписи, у которых первая часть не находит линию.

Записывается элемент <Intencity> сразу после <Visible> — в том порядке, в
котором поля объявлены в NuclideDefinition и в котором их пишет XmlSerializer.
Существующие ненулевые Intencity не трогаются.

ВТОРОЙ ПРОХОД — ДОБОР ЛИНИЙ. Образ нуклида в полноспектральном разборе
строится из ВСЕХ его строк файла (FsaLibrary), и образ из трёх видимых линий
хуже встроенного из шести: первый прогон без добора СУЖАЛ образы Ba-133
(1 линия против 6 у встроенного), Eu-152 (5 против 14), I-131 и Lu-176 —
поймано ревизией. Поэтому каждому нуклиду, получившему хоть одну линию,
дописываются все недостающие гамма-линии с выходом ≥ 1 % (порог из критериев
отбора сетов) — СКРЫТЫМИ записями (`Visible=false`): на графике они не
появляются, а образ становится полным. Период и цвет берутся у видимой
записи того же нуклида. Прогон повторно ничего не дублирует: занятые энергии
(±3 кэВ) пропускаются.

    python fill_intensity.py <nucdb.sqlite> <NuclideDefinition.xml>
"""
import io
import re
import sqlite3
import sys


ISOMER = "isomer"


def nucid_of(token):
    """«Cs-137» / «Cs137» / «K40» -> «137CS»; изомеры — маркер ISOMER.

    Изомеры пропускаются: в NuclideMaster `l_seqno` — номер уровня схемы, а
    не номер изомера (scheme.md, §2), и самих изомеров может не быть вовсе —
    у Ag-108m в базе только основное состояние (2.4 мин), и поиск по 108AG
    молча подставил бы ЕГО слабую линию 433.96 (0.5 %) вместо изомерных 90 %.
    """
    m = re.match(r"^([A-Za-z]{1,2})-?(\d{1,3})([mM]\d?)?$", token)
    if not m:
        return None
    if m.group(3):
        return ISOMER
    return "%d%s" % (int(m.group(2)), m.group(1).upper())


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    db = sqlite3.connect(sys.argv[1])
    path = sys.argv[2]

    with io.open(path, encoding="utf-8-sig") as f:
        text = f.read()

    lines_cache = {}

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

        token = label.split(" ")[0].split("/")[0].strip()
        nucid = nucid_of(token)
        if nucid is None:
            skipped.append((label, e, "не нуклид"))
            return block
        if nucid == ISOMER:
            skipped.append((label, e, "изомер: в NuclideMaster ненадёжен"))
            return block

        candidates = sorted(gamma_lines(nucid), key=lambda r: abs(r[0] - e))
        if not candidates or abs(candidates[0][0] - e) > 4.0:
            skipped.append((label, e, "нет линии в ±4 кэВ"))
            return block

        best = candidates[0]
        if len(candidates) > 1:
            second = candidates[1]
            if (abs(second[0] - e) < 1.5 * max(0.3, abs(best[0] - e))
                    and second[1] > best[1]):
                skipped.append((label, e, "неоднозначно: %.1f и %.1f кэВ"
                                % (best[0], second[0])))
                return block

        filled.append((label, e, best[0], best[1]))
        return block.replace(
            "</Visible>",
            "</Visible>\n      <Intencity>%g</Intencity>" % round(best[1], 4),
            1)

    text = re.sub(r"<Nuclide>.*?</Nuclide>", patch, text, flags=re.S)

    # --- добор недостающих линий скрытыми записями -----------------------
    # По каждому нуклиду: какие энергии уже заняты (любыми записями файла) и
    # каковы период и цвет его видимой записи — образцы для новых блоков.
    per_nuclide = {}
    for m in re.finditer(r"<Nuclide>.*?</Nuclide>", text, flags=re.S):
        block = m.group(0)
        name = re.search(r"<Name>(.*?)</Name>", block)
        energy = re.search(r"<Energy>([\d.]+)</Energy>", block)
        if not name or not energy:
            continue
        token = name.group(1).split(" ")[0].split("/")[0].strip()
        nucid = nucid_of(token)
        if nucid in (None, ISOMER):
            continue
        entry = per_nuclide.setdefault(nucid, {
            "token": token, "energies": [], "half_life": None, "color": None,
            "filled": False,
        })
        label_e = float(energy.group(1))
        entry["energies"].append(label_e)
        # Занятой считается и БЛИЖАЙШАЯ к метке линия базы (в ±4 кэВ): метка
        # округлена («Eu-152 125» несёт выход линии 121.78), и без этого добор
        # клал бы ту же линию второй раз — двойной вес в образе. Ближайшая, а
        # не все в окне: у Cs-134 в ±4 кэВ от метки 798 лежат ДВЕ настоящие
        # линии (795.86 и 801.95), и вторую добор обязан доложить. Правило не
        # зависит от того, в этом ли прогоне метка получила выход.
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
            if i < 1.0:
                continue
            if any(abs(le - taken) <= 3.0 for taken in entry["energies"]):
                continue
            entry["energies"].append(le)
            added.append((entry["token"], le, i))
            blocks.append(
                "    <Nuclide>\n"
                "      <Name>%s</Name>\n"
                "      <Energy>%g</Energy>\n"
                "      <HalfLife>%s</HalfLife>\n"
                "      <NuclideColor>%s</NuclideColor>\n"
                "      <Note />\n"
                "      <Visible>false</Visible>\n"
                "      <Intencity>%g</Intencity>\n"
                "    </Nuclide>\n"
                % (entry["token"], round(le, 3), entry["half_life"],
                   entry["color"], round(i, 4)))

    if blocks:
        text = text.replace("</NuclideDefinitions>",
                            "".join(blocks) + "  </NuclideDefinitions>", 1)

    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)

    print("проставлено %d:" % len(filled))
    for label, e, le, i in filled:
        print("  %-24s %7.1f -> линия %8.2f кэВ, I = %g %%" % (label, e, le, i))
    print("пропущено %d:" % len(skipped))
    for label, e, why in skipped:
        print("  %-24s %7.1f -> %s" % (label, e, why))
    print("добрано скрытых линий (>=1 %%): %d" % len(added))
    for token, le, i in added:
        print("  %-10s %9.2f кэВ, I = %g %%" % (token, le, i))


if __name__ == "__main__":
    main()
