# -*- coding: utf-8 -*-
u"""
Перевод геометрии `.in` в json для `TCCFCALC_Prepare_Json` новой DLL ЛСРМ.

Зачем: через `tccfcalc.in` НЕДОСТУПНА половина ключей расчёта — `useEPDL97`,
`useGLECS`, `calc_electron_real`, все пороги и параметры спектра читаются
только из json, и умолчания у EPDL97/GLECS — `false` (разобрано 08.08.2026,
см. [README.md](README.md) §13.3, §13.5). Единственный способ включить
связанный комптон и форм-фактор Рэлея — этот путь.

Схема json восстановлена из самой DLL и проверена прогоном: json, собранный
отсюда из `Nano16Pro_tube.in`, даёт `Prepare_Json -> 0`, отчёт печатает ту же
геометрию до знака, эффективность сходится с `.in`-прогоном в пределах шума.

Что важно знать:

* узел `Source` ОБЯЗАТЕЛЕН (без него код 18);
* узел `ContainerSource` (ячейки произвольной формы) в 2.10.1844 **мёртв** —
  при наличии `Source` он не разбирается вовсе, мусор в нём проходит с кодом 0.
  Поэтому здесь его нет;
* зерно ГСЧ через json НЕ задаётся: `Prepare_Json` принимает только два слова,
  шестого (зерна) у него нет. Хотите воспроизводимость — считайте через `.in`
  и `--seed`;
* имена ключей геометрии те же, что в `.in`, но БЕЗ префиксов
  `DS_`/`DC_`/`SC_`/`SM_`.

    python in2json.py <геометрия.in> <выход.json> [ключ=значение ...]

Ключи в конце — блок `CalculationParameters`, например `useGLECS=true`
`calc_full_eff=true` `threads_number=1`.
"""
import io
import json
import os
import re
import sys

# Наше расширение формата (`DS_CrystalBox*`) ни та ни другая DLL не видит.
IGNORED_PREFIX = "DS_CrystalBox"

DET_GEOM = {
    "SCINTILLATOR": ("DS_", [
        "CrystalDiameter", "CrystalHeight",
        "CrystalFrontReflectorThickness", "CrystalSideReflectorThickness",
        "CrystalFrontCladdingThickness", "CrystalSideCladdingThickness",
        "DetectorMountingThickness",
        "DetectorFrontPackagingThickness", "DetectorSidePackagingThickness",
        "DetectorFrontCapThickness", "DetectorSideCapThickness",
    ]),
    "COAXIAL": ("DC_", [
        "CrystalDiameter", "CrystalHeight",
        "CrystalHoleDiameter", "CrystalHoleHeight",
        "CrystalFrontDeadLayer", "CrystalSideDeadLayer", "CrystalBackDeadLayer",
        "CrystalHoleBottomDeadLayer", "CrystalHoleSideDeadLayer",
        "CrystalSideCladdingThickness", "CapToCrystalDistance",
        "DetectorCapDiameter", "DetectorCapFrontThickness",
        "DetectorCapSideThickness", "DetectorCapBackThickness",
        "DetectorMountingThickness", "BevelLength",
    ]),
}

# узел материала -> имя, под которым он лежит в `.in` (после префикса)
DET_MAT = {
    "SCINTILLATOR": [("Crystal", "Crystal"),
                     ("CrystalCladding", "CrystalCladding"),
                     ("CrystalReflector", "CrystalReflector"),
                     ("DetectorPackaging", "DetectorPackaging"),
                     ("DetectorCap", "DetectorCap")],
    "COAXIAL": [("Crystal", "Crystal"),
                ("CrystalSideCladding", "CrystalSideCladding"),
                ("CrystalMounting", "CrystalMounting"),
                ("DetectorCap", "DetectorCap"),
                ("DetectorCapFront", "DetectorCap"),
                ("Vacuum", "Vacuum")],
}

SRC_GEOM = {
    "POINT": ("", ["pdistance", "prho"]),
    "CYLINDER": ("SC_", ["BeakerToDetectorFrontDistance", "BeakerDiameter",
                         "BeakerHeight", "BeakerSideWallThickness",
                         "BeakerEndWallThickness", "SourceHeight"]),
    "MARINELLI": ("SM_", ["BeakerToDetectorFrontDistance", "BeakerDiameter",
                          "BeakerHeight", "BeakerHoleDiameter",
                          "BeakerHoleHeight", "BeakerSideThickness",
                          "BeakerEndWallThickness", "BeakerHoleSideThickness",
                          "BeakerHoleEndWallThickness", "SourceHeight"]),
}

SRC_MAT_PREFIX = {"POINT": "SC_", "CYLINDER": "SC_", "MARINELLI": "SM_"}
SRC_MAT = [("Wall", "Wall"), ("Source", "Source"), ("EmptySpace", "EmptySpace")]


def read_in(path):
    u"""Ключ -> строка значения. Единицы («cm») отбрасываются."""
    out = {}
    with io.open(path, encoding="latin-1") as f:
        for line in f:
            line = line.split("//")[0]
            m = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_\[\].]*)\s*=\s*(.+?)\s*$", line)
            if not m:
                continue
            key, value = m.group(1), m.group(2)
            if key.startswith(IGNORED_PREFIX):
                continue
            out[key] = value.split()[0] if value.split() else value
    return out


def num(cfg, key, default=None):
    if key not in cfg:
        if default is None:
            raise KeyError(u"нет ключа %s" % key)
        return default
    return float(cfg[key])


def material(cfg, prefix, name):
    u"""Собрать узел вещества. Имена счётчика и типа долей в поставке ЛСРМ
    не единообразны — `DS_FractionTypeReflector` против
    `DS_FractionTypeCrystalReflector`, `DC_nVacuum` против `..._nVacuumElements`,
    поэтому берём первое попавшееся из списка кандидатов."""
    def pick(*names):
        for n in names:
            if n in cfg:
                return cfg[n]
        return None

    count = pick(prefix + "n" + name + "Elements", prefix + "n" + name)
    if count is None:
        raise KeyError(u"нет числа элементов для %s%s" % (prefix, name))
    count = int(float(count))
    rho = float(pick(prefix + "Ro" + name))
    ftype = pick(prefix + "FractionType" + name,
                 prefix + "FractionType" + name.replace("Crystal", ""),
                 "MASS")
    elements = []
    for i in range(count):
        z = int(float(cfg["%sZ%s[%d]" % (prefix, name, i)]))
        frac = float(cfg["%sFractions%s[%d]" % (prefix, name, i)])
        elements.append({"z": z, "frac": frac})
    return {"rho": rho, "fraction_type": ftype, "elements": elements}


def convert(in_path, params=None, nuclide=(290, 27, 0)):
    cfg = read_in(in_path)
    det_type = cfg.get("DetectorType", "SCINTILLATOR").strip()
    src_type = cfg.get("SourceType", "CYLINDER").strip()
    if det_type not in DET_GEOM:
        raise ValueError(u"тип детектора %s не поддержан" % det_type)
    if src_type not in SRC_GEOM:
        raise ValueError(u"тип пробы %s не поддержан" % src_type)

    dprefix, dkeys = DET_GEOM[det_type]
    # Слои, которых нет в старых файлах, — нулевые: это та же геометрия,
    # которую читала старая DLL (см. NEW_KEYS в run_tccf2.py).
    dgeom = dict((k, num(cfg, dprefix + k, 0.0)) for k in dkeys)
    dmat = {}
    for node, name in DET_MAT[det_type]:
        try:
            dmat[node] = material(cfg, dprefix, name)
        except KeyError:
            # Слоя нет в файле — кладём алюминий нулевой толщины: узел
            # обязателен, а толщина уже нулевая.
            dmat[node] = {"rho": 2.7, "fraction_type": "MASS",
                          "elements": [{"z": 13, "frac": 1.0}]}

    sprefix, skeys = SRC_GEOM[src_type]
    sgeom = dict((k, num(cfg, sprefix + k, 0.0)) for k in skeys)
    smat = {}
    for node, name in SRC_MAT:
        try:
            smat[node] = material(cfg, SRC_MAT_PREFIX[src_type], name)
        except KeyError:
            smat[node] = {"rho": 0.001205, "fraction_type": "MASS",
                          "elements": [{"z": 7, "frac": 0.78},
                                       {"z": 8, "frac": 0.22}]}

    doc = {
        "Nuclide": {"a": nuclide[0], "z": nuclide[1], "m": nuclide[2]},
        "Detector": {"Type": det_type, "Geometry": dgeom, "Material": dmat},
        "Source": {"Type": src_type, "Geometry": sgeom, "Material": smat},
    }
    if params:
        doc["CalculationParameters"] = dict(params)
    return doc


def main():
    if len(sys.argv) < 3:
        sys.stderr.write(u"in2json.py <геометрия.in> <выход.json> [ключ=значение ...]\n")
        return 2
    params = {}
    for arg in sys.argv[3:]:
        key, _, value = arg.partition("=")
        if value in ("true", "false"):
            params[key] = (value == "true")
        else:
            params[key] = float(value) if "." in value else int(value)
    doc = convert(sys.argv[1], params)
    with io.open(sys.argv[2], "w", encoding="utf-8") as f:
        f.write(json.dumps(doc, indent=1, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
