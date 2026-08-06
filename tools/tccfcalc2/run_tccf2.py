# -*- coding: utf-8 -*-
u"""
Прогнать НОВУЮ tccfcalc.dll ЛСРМ (NuclideMasterPlus 2.10.1844) по нашей
геометрии и снять кривую эффективности.

Родня: `tools/tccfcalc/run_tccf.py` — то же самое для старой DLL. Подлог
поддельного набора ENSDF (A = 290, Z = 27, «Fake ENSDF2 B- DECAY») взят оттуда
без изменений — новая DLL этот формат по-прежнему читает, см. README, §3.

Чем отличается прогон:

* входной файл называется `tccfcalc.in` и лежит в baseDir, а не в текущем
  каталоге (у старой DLL — `TCCFCALC.in` в текущем);
* библиотека лежит в `<baseDir>Lib`, поддельный набор кладётся в
  `<baseDir>Lib\\ENSDF2\\290.ENX` (каталога `ENSDF2` в поставке НЕТ, его
  создаём мы; настоящая библиотека теперь в `Lib\\ENSDF` в стандартном
  формате ENSDF);
* геометрия требует четырёх новых слоёв (`DS_DetectorFront/SidePackaging`,
  `DS_DetectorFront/SideCap`) и двух наборов веществ к ним — без них Prepare
  отвечает кодом 6 «Incorrect input geometry or material data». Старые файлы
  дополняются нулевыми толщинами: это та же геометрия, что читала старая DLL;
* появился блок параметров расчёта. ЛОВУШКА: стоит появиться в файле ХОТЬ
  ОДНОМУ ключу расчёта — все булевы параметры считаются заданными, и не
  упомянутые становятся `false`. Поэтому блок пишется целиком, всегда.

    python run_tccf2.py --workdir=... --geometry=X.in --decays=20000000
                        [--energies=50,100,...] [--tag=имя] [--csv=файл]
                        [--variant=full|nottb|noepdl|oldlike] [--threads=1]

Работа идёт ТОЛЬКО в копии каталога: установку ЛСРМ трогать нельзя.
"""
import argparse
import io
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
# Поддельный набор ENSDF пишется тем же кодом, что и для старой DLL, —
# формат общий, и расходиться этим двум описаниям нельзя.
sys.path.insert(0, os.path.join(HERE, os.pardir, "tccfcalc"))
import run_tccf                                        # noqa: E402

FAKE_A, FAKE_Z = run_tccf.FAKE_A, run_tccf.FAKE_Z

DEFAULT_ENERGIES = run_tccf.DEFAULT_ENERGIES

# Ключи, которых в старом формате нет, а новая DLL их требует. Нулевые толщины
# и алюминий в веществе — слои, которых в старой модели не было вовсе.
NEW_KEYS = u"""
// --- layers introduced in 2.10; zero thickness = the old geometry ---
DS_DetectorFrontPackagingThickness = 0 cm
DS_DetectorSidePackagingThickness = 0 cm
DS_DetectorFrontCapThickness = 0 cm
DS_DetectorSideCapThickness = 0 cm

DS_nDetectorCapElements = 1
DS_RoDetectorCap = 2.7
DS_ZDetectorCap[0] = 13
DS_FractionsDetectorCap[0] = 1
DS_FractionTypeDetectorCap = MASS

DS_nDetectorPackagingElements = 1
DS_RoDetectorPackaging = 2.7
DS_ZDetectorPackaging[0] = 13
DS_FractionsDetectorPackaging[0] = 1
DS_FractionTypeDetectorPackaging = MASS
"""

# Наборы параметров расчёта. `full` — то, что DLL берёт по умолчанию, когда в
# файле нет НИ ОДНОГО ключа расчёта (проверено по шапке отчёта).
VARIANTS = {
    "full":    {},
    "nottb":   {"calc_electron_ttb": False},
    "noepdl":  {"useEPDL97": False, "useGLECS": False},
    "oldlike": {"calc_electron_ttb": False, "useEPDL97": False,
                "useGLECS": False},
}

BASE_PARAMS = [
    ("xrays", True), ("annihilation", True), ("angular", True),
    ("angle_optimize", False), ("calc_full_eff", False),
    ("calc_spectrum", False), ("calc_coincidence", True),
    ("calc_scattered", True), ("calc_effc", False),
    ("calc_electron_ttb", True), ("useGLECS", True), ("useEPDL97", True),
]


def params_block(variant, threads):
    over = VARIANTS[variant]
    lines = [u"", u"// --- calculation parameters: the block is always written IN FULL ---"]
    for name, default in BASE_PARAMS:
        value = over.get(name, default)
        lines.append(u"%s = %s" % (name, "true" if value else "false"))
    lines.append(u"threads_number = %d" % threads)
    return u"\n".join(lines) + u"\n"


def upgrade_in(src_path, dst_path, variant="full", threads=1, overrides=None,
               drop_box=True):
    u"""Переписать .in старого образца под новую DLL.

    Ключи коробки (`DS_CrystalBox*`) выбрасываются: это НАШЕ расширение
    формата, ни та ни другая DLL его не видит и считает цилиндр.
    """
    overrides = overrides or {}
    out = []
    present = set()
    with io.open(src_path, encoding="latin-1") as f:
        for line in f:
            stripped = line.strip()
            if drop_box and stripped.startswith("DS_CrystalBox"):
                continue
            m = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_\[\].]*)\s*=\s*(\S+)(.*)$", line)
            if m:
                present.add(m.group(1))
                if m.group(1) in overrides:
                    tail = m.group(3).rstrip("\r\n")
                    out.append(u"%s = %g%s\n" % (m.group(1),
                                                 overrides[m.group(1)], tail))
                    continue
            out.append(line)

    text = u"".join(out)
    if "DS_DetectorFrontPackagingThickness" not in present:
        text += NEW_KEYS
    text += params_block(variant, threads)
    with io.open(dst_path, "w", encoding="latin-1", newline="") as f:
        f.write(text)


# Строка результата новой DLL: поля разделены табуляцией, столбцов десять
# (у старой — восемь, без Areas/AreasCoi).
def parse_out(path):
    rows = []
    with io.open(path, encoding="latin-1") as f:
        for line in f:
            parts = [p.strip() for p in line.rstrip("\r\n").split("\t")]
            if len(parts) < 9 or not re.match(r"^\d+$", parts[0]):
                continue
            try:
                rows.append({
                    "energy_kev": float(parts[1]) * 1000.0,
                    "intensity": float(parts[2]),
                    "cf": float(parts[4]),
                    "cf_err_pct": float(parts[5]),
                    "eff": float(parts[6]),
                    "eff_err_pct": float(parts[7]),
                    "area": float(parts[8]),
                })
            except ValueError:
                continue
    return rows


def run(workdir, geometry, decays, energies, tag="", variant="full", threads=1,
        overrides=None):
    ensdf_dir = os.path.join(workdir, "Lib", "ENSDF2")
    if not os.path.isdir(ensdf_dir):
        os.makedirs(ensdf_dir)
    run_tccf.write_scale(os.path.join(ensdf_dir, "%03d.ENX" % FAKE_A), energies)
    upgrade_in(geometry, os.path.join(workdir, "tccfcalc.in"),
               variant=variant, threads=threads, overrides=overrides)

    started = time.time()
    proc = subprocess.run(
        [os.path.join(workdir, "TccfProbe2.exe"), workdir,
         str(FAKE_A), str(FAKE_Z), "0", str(decays)],
        cwd=workdir, capture_output=True, text=True, encoding="latin-1")
    elapsed = time.time() - started
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        raise SystemExit(u"TccfProbe2 вернул %d" % proc.returncode)
    if "Prepare -> 0" not in proc.stdout:
        sys.stderr.write(proc.stdout)
        raise SystemExit(u"Prepare отказал")

    out = os.path.join(workdir, "tccfcalc.out")
    rows = parse_out(out)
    if len(rows) != len(energies):
        raise SystemExit(u"строк в отчёте %d, а энергий %d — отчёт не тот"
                         % (len(rows), len(energies)))
    if tag:
        shutil.copyfile(out, os.path.join(workdir, "out_%s.txt" % tag))
    return rows, elapsed


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--workdir", required=True)
    p.add_argument("--geometry", required=True)
    p.add_argument("--decays", type=int, default=2000000)
    p.add_argument("--energies", default="")
    p.add_argument("--variant", default="full", choices=sorted(VARIANTS))
    p.add_argument("--threads", type=int, default=1)
    p.add_argument("--tag", default="")
    p.add_argument("--csv", default="")
    args = p.parse_args()

    energies = ([float(x) for x in args.energies.split(",")]
                if args.energies else DEFAULT_ENERGIES)
    rows, elapsed = run(args.workdir, args.geometry, args.decays, energies,
                        args.tag, args.variant, args.threads)

    print(u"# %s, %s, распадов %d, потоков %d, %.1f с"
          % (os.path.basename(args.geometry), args.variant, args.decays,
             args.threads, elapsed))
    print("E_keV,eff,eff_err_pct,cf,cf_err_pct,area")
    for r in rows:
        print("%.1f,%.5E,%.2f,%.5f,%.2f,%.0f"
              % (r["energy_kev"], r["eff"], r["eff_err_pct"],
                 r["cf"], r["cf_err_pct"], r["area"]))
    if args.csv:
        with io.open(args.csv, "w", encoding="utf-8") as f:
            f.write(u"E_keV,eff,eff_err_pct,cf,cf_err_pct,area\n")
            for r in rows:
                f.write(u"%.1f,%.5E,%.2f,%.5f,%.2f,%.0f\n"
                        % (r["energy_kev"], r["eff"], r["eff_err_pct"],
                           r["cf"], r["cf_err_pct"], r["area"]))


if __name__ == "__main__":
    main()
