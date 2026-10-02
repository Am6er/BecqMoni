# -*- coding: utf-8 -*-
u"""Правило корпуса «рентген точечных ЛСРМ ниже ~30 кэВ в амплитуду не идёт» (`AMBER153` (б)).

РЕШЕНИЕ. Amber 01.10.2026 вопросником по (б), дословно: «Правило корпуса: рентген не
в амплитуду (Рекомендую)» — у точечных источников ЛСРМ рентген ниже ~30 кэВ в
амплитуду не идёт; капсулу НЕ моделировать, множителя рентгена в разборе НЕ вводить.

ПОЧЕМУ. Источник ОСГИ сидит в капсуле, а сцена корпуса описывает его точкой в
воздухе. Капсула гасит 22 кэВ Ag K у Cd-109 до 0.22…0.49 паспортного ожидания при
88 кэВ на месте (П191: подпись 0.05…0.1 мм стали), а NNLS ведёт амплитуду за самым
сильным пиком — за рентгеном. Значит участок ниже 30 кэВ у этих спектров мерит
СЦЕНУ (неописанную капсулу), а не разбор, и в амплитуду идти не должен.

КАК. Полоса ФИТА у такого спектра начинается с `FLOOR_KEV` (30 кэВ, число из
решения): каналы ниже в χ² не входят, образ рентгена там остаётся, но амплитуду
держат линии выше. Ставится ключом пробы `--fit-floor-by=<файл>` (`CorpusFsaProbe`,
пол на время разбора ЭТОГО спектра), файл пишет этот скрипт, зовёт `run_appwd.ps1`.

КОМУ. Спектру, у которого ОБА условия:
  1. точечный источник ЛСРМ — паспорт вида «паспорт: Xx-NNN A=… Бк dA=…% дата»
     (`tools/pie/passport.py`, вид «паспорт ЛСРМ» в Бк) И `SourceType` = Point в
     геометрии файла;
  2. у объявленного состава есть фотоны (γ или рентген `nucdb.decay_radiations`,
     выход ≥ `MIN_YIELD_PCT`) в полосе [`LIB_FLOOR_KEV`; `FLOOR_KEV`) — то есть
     правилу есть что убирать.
Условие 2 нужно, а не «для порядка»: пол на 30 кэВ без рентгена ниже 30 режет
пики ВЫШЕ 30 — левое крыло La K 33 кэВ у Ce-139, Sm K 40 кэВ у Eu-152 (замер П214
на малой базе: при поле 30 у всех 38 точек Ce-139 −4…−6 %, Eu-152 −3…−5 % к
паспорту, а правилу там убирать нечего). `LIB_FLOOR_KEV` — пол библиотеки по
кривой у сцен точек G1S (18.2…18.4 кэВ, `--band-audit=` П214): линии ниже в образы
не входят вовсе и амплитуду нести не могут. На поставке nucdb 02.10.2026 условие 2
выполняется у Cd-109 (Ag K 22.0…25.5 кэВ) и Am-241 (Np L 18.0/20.9 кэВ, γ 26.3 кэВ).

Имён нуклидов в правиле нет: состав — из манифеста, линии — из базы.

    python tools/CORPUS/scripts/xray_rule.py                 # печать списка
    python tools/CORPUS/scripts/xray_rule.py --write=<csv>   # файл для пробы
"""
import argparse
import csv
import io
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PIE = os.path.join(HERE, '..', '..', 'pie')
sys.path.insert(0, PIE)
import passport                                              # noqa: E402

#: Пол полосы фита по правилу, кэВ — число из решения Amber («ниже ~30 кэВ»).
FLOOR_KEV = 30.0
#: Ниже — линий в образах нет (пол библиотеки по кривой у точек G1S 18.2…18.4 кэВ).
LIB_FLOOR_KEV = 18.0
#: Наименьший выход фотона, %, чтобы линия считалась «есть что убирать».
MIN_YIELD_PCT = 1.0


def _lines(nucids, db=passport.NUCDB):
    uri = 'file:%s?mode=ro' % os.path.abspath(db).replace('\\', '/')
    c = sqlite3.connect(uri, uri=True)
    try:
        out = []
        for name, nid in nucids:
            for t, e, i in c.execute(
                    "select type_a, energy_num, intensity_num from decay_radiations "
                    "where parent_nucid=? and type_a in ('G','X') and energy_num>=? "
                    "and energy_num<? and intensity_num>=? order by energy_num",
                    (nid, LIB_FLOOR_KEV, FLOOR_KEV, MIN_YIELD_PCT)):
                out.append((name, t, e, i))
        return out
    finally:
        c.close()


def _composition(row):
    u"""Состав спектра из манифеста — именами `Xx-NNN` и ключами nucdb."""
    sys.path.insert(0, PIE)
    import score                                             # noqa: E402
    names = []
    for nu in (row.get('nuclides') or '').split(';'):
        nu = nu.strip()
        if nu and score.NUCLIDE_MAP.get(nu):
            names.append(score.NUCLIDE_MAP[nu])
    for ch in (row.get('chains') or '').split(';'):
        ch = ch.strip()
        for fam in (score.chain_components(ch) if ch else []):
            names.extend(score.CHAIN_MEMBERS.get(fam, [fam]))
    return [(n, passport._nucid(n)) for n in dict.fromkeys(names) if passport._nucid(n)]


def rule_rows(manifest=passport.MANIFEST):
    u"""[(спектр, пол кэВ, пояснение)] — кому правило ставит пол."""
    refs, _ = passport.references(manifest)
    points = {r[0] for r in refs if r[6] == 'паспорт ЛСРМ' and r[3] == 'Бк'}
    out = []
    with io.open(manifest, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            key = row['key']
            if key not in points:
                continue
            text = passport._xml_head(key)
            if text is None or passport.geometry_of(text)[0] != 'Point':
                continue
            lines = _lines(_composition(row))
            if not lines:
                continue
            why = '; '.join('%s %s %.2f кэВ %.1f %%' % (n, t, e, i) for n, t, e, i in lines)
            out.append((key, FLOOR_KEV, why))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--write', default=None, help='файл списка для --fit-floor-by=')
    args = ap.parse_args()
    rows = rule_rows()
    if args.write:
        with io.open(args.write, 'w', encoding='utf-8', newline='') as fh:
            w = csv.writer(fh, lineterminator='\n')
            w.writerow(['spectrum', 'floor_kev', 'why'])
            for key, kev, why in rows:
                # `,` в пояснении разломала бы разбор пробы (столбцы — по запятой)
                w.writerow([key, '%.1f' % kev, why.replace(',', ' ')])
    print(u'правило корпуса AMBER153 (б): пол фита %.1f кэВ у %d точечных спектров ЛСРМ'
          % (FLOOR_KEV, len(rows)))
    for key, kev, why in rows:
        print(u'  %-22s %.1f кэВ  %s' % (key, kev, why))
    if not rows:
        # Пустой список — не «правило соблюдено», а «правилу некому»: так бывает,
        # только если манифест или база потеряли паспорта/линии. Отказ кодом.
        sys.exit(u'⛔ правилу некому: ни одного точечного спектра ЛСРМ с рентгеном ниже %.0f кэВ'
                 % FLOOR_KEV)


if __name__ == '__main__':
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    main()
