# -*- coding: utf-8 -*-
u"""
Прогнать НОВУЮ tccfcalc.dll ЛСРМ (NuclideMasterPlus 2.10.1844) по нашей
геометрии и снять кривую эффективности.

Подлог поддельного набора ENSDF (A = 290, Z = 27, «Fake ENSDF2 B- DECAY») —
в [`scale_set.py`](scale_set.py) рядом; новая DLL этот формат по-прежнему
читает, см. README, §3.

Сборка СТАРОЙ DLL (`tools/tccfcalc`) убрана 08.08.2026 по решению Amber —
она больше не нужна. Её журнал измерений сохранён рядом как
[`old-dll-journal.md`](old-dll-journal.md): на него ссылается десяток открытых
задач.

Чем отличается прогон от старой версии:

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
  упомянутые становятся `false` (механизм: `CnfReader::getBoolValueOpt` на
  отсутствующий ключ отдаёт `false`). Поэтому блок пишется целиком, всегда.

ЧТО ИЗМЕНИЛОСЬ ПОСЛЕ ПОЛНОГО РАЗБОРА DLL (08.08.2026, README §13):

* **зерно ГСЧ задаётся снаружи** — это шестое слово `Prepare`. Харнесс шлёт
  `--seed` (умолчание `DEFAULT_SEED`), и прогон становится побитово
  воспроизводимым: абляции меряются на ОДНИХ И ТЕХ ЖЕ историях, разница
  ключей больше не тонет в шуме. Ноль означает «взять `time(0)`»;
* **`useEPDL97` и `useGLECS` из `.in` НЕ читаются** — их нет в `.in`-ветке
  разбора, только в json, и умолчания у обоих `false`. Прежние постановки
  `noepdl`/`oldlike` были тождественны `full`/`nottb` и убраны. Хотите
  GLECS — гоняйте через json (`--engine=json`, `--glecs`), для чего рядом
  лежит [`in2json.py`](in2json.py);
* **подлог «Scale» (A = 290) молча выключает** `xrays`, `annihilation`,
  `angular` и `calc_coincidence` и включает `angle_optimize` — что бы ни
  стояло в файле. Для моноэнергетической кривой это безразлично (рентгена и
  аннигиляции в поддельной схеме нет), но шапка отчёта показывает итог, а не
  файл, и об этом надо помнить;
* **отчёт врёт про `calc_scattered`**: `true` в файле даёт `false` в шапке;
* **строки ниже `low_energy_threshold` = 10 кэВ отбрасываются** при разборе
  отчёта: у настоящих нуклидов DLL печатает там `Eff` в тысячи и `CF`,
  прижатый к 0/2/5.

    python run_tccf2.py --workdir=... --geometry=X.in --decays=20000000
                        [--energies=50,100,...] [--tag=имя] [--csv=файл]
                        [--variant=full|nottb] [--threads=1] [--seed=N]
                        [--engine=in|json] [--glecs] [--full-eff]

Работа идёт ТОЛЬКО в копии каталога: установку ЛСРМ трогать нельзя.
"""
import argparse
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import in2json                                         # noqa: E402
import scale_set                                       # noqa: E402

FAKE_A, FAKE_Z = scale_set.FAKE_A, scale_set.FAKE_Z

DEFAULT_ENERGIES = scale_set.DEFAULT_ENERGIES

# Зерно ГСЧ по умолчанию. Ненулевое — чтобы прогоны были воспроизводимы, а
# абляции считались на одних историях (README §13.8).
DEFAULT_SEED = 20260808

# Порог `low_energy_threshold` самой DLL. Строки отчёта ниже него содержат
# мусор (`Eff` в тысячи) — см. README §13.7.
LOW_ENERGY_KEV = 10.0

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
#
# Прежние `noepdl` и `oldlike` УБРАНЫ: `useEPDL97` и `useGLECS` из `.in` не
# читаются вовсе, и обе постановки были тождественны `full`/`nottb`
# (README §13.3). Их место занял json-путь — см. `--engine=json --glecs`.
VARIANTS = {
    "full":    {},
    "nottb":   {"calc_electron_ttb": False},
}

# Ключи, которые `.in`-ветка разбора ДЕЙСТВИТЕЛЬНО читает (11 булевых плюс
# число потоков). Ни `useEPDL97`, ни `useGLECS` сюда не входят — писать их в
# файл бессмысленно, DLL их не видит.
BASE_PARAMS = [
    ("xrays", True), ("annihilation", True), ("angular", True),
    ("angle_optimize", False), ("calc_full_eff", False),
    ("calc_spectrum", False), ("calc_coincidence", True),
    ("calc_scattered", True), ("calc_effc", False),
    ("calc_electron_ttb", True),
]


def params_block(variant, threads, extra=None):
    over = dict(VARIANTS[variant])
    if extra:
        over.update(extra)
    # Комментарий уходит в файл, который DLL читает как latin-1, — поэтому он
    # по-английски: кириллица тут не пишется.
    lines = [u"", u"// --- calculation parameters: the block is always written IN FULL ---",
             u"// NB: in Scale mode (A = 290) the DLL forces xrays, annihilation,",
             u"// angular and calc_coincidence to false and angle_optimize to true."]
    for name, default in BASE_PARAMS:
        value = over.get(name, default)
        lines.append(u"%s = %s" % (name, "true" if value else "false"))
    lines.append(u"threads_number = %d" % threads)
    return u"\n".join(lines) + u"\n"


def upgrade_in(src_path, dst_path, variant="full", threads=1, overrides=None,
               drop_box=True, extra_params=None):
    u"""Переписать .in старого образца под новую DLL.

    Ключи коробки (`DS_CrystalBox*`) выбрасываются: это НАШЕ расширение
    формата, ни та ни другая DLL его не видит и считает цилиндр.
    """
    overrides = overrides or {}
    out = []
    present = set()
    # Повторный вход: блок параметров расчёта прежнего прогона вычищается,
    # иначе ключи задваивались с неопределённым победителем — а пишется он
    # ниже всегда целиком (см. ЛОВУШКУ в шапке).
    param_names = set(name for name, _ in BASE_PARAMS)
    param_names.add(u"threads_number")
    with io.open(src_path, encoding="latin-1") as f:
        for line in f:
            stripped = line.strip()
            if drop_box and stripped.startswith("DS_CrystalBox"):
                continue
            if stripped.startswith(u"// --- calculation parameters"):
                continue
            m = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_\[\].]*)\s*=\s*(\S+)(.*)$", line)
            if m:
                if m.group(1) in param_names:
                    continue
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
    # Хвостовые пустые строки срезаются, иначе каждый повторный вход
    # добавлял бы по одной перед блоком параметров.
    text = text.rstrip(u"\r\n \t") + u"\n"
    text += params_block(variant, threads, extra_params)
    with io.open(dst_path, "w", encoding="latin-1", newline="") as f:
        f.write(text)


# Строка результата новой DLL: поля разделены табуляцией, столбцов десять
# (у старой — восемь, без Areas/AreasCoi); одиннадцатый — полная
# эффективность, появляется при `calc_full_eff = true`.
def parse_out(path, drop_low=True):
    rows = []
    dropped = []
    with io.open(path, encoding="latin-1") as f:
        for line in f:
            parts = [p.strip() for p in line.rstrip("\r\n").split("\t")]
            if len(parts) < 9 or not re.match(r"^\d+$", parts[0]):
                continue
            try:
                row = {
                    "energy_kev": float(parts[1]) * 1000.0,
                    "intensity": float(parts[2]),
                    "cf": float(parts[4]),
                    "cf_err_pct": float(parts[5]),
                    "eff": float(parts[6]),
                    "eff_err_pct": float(parts[7]),
                    "area": float(parts[8]),
                    "full_eff": (float(parts[10])
                                 if len(parts) > 10 and parts[10] else None),
                }
            except (ValueError, IndexError):
                continue
            # Ниже `low_energy_threshold` DLL печатает мусор: у Co-60 линия
            # 7.3 кэВ выходит с Eff = 9.155e+03 (README §13.7). Молча такие
            # строки пропускать нельзя — они попадут в кривую как настоящие.
            if drop_low and (row["energy_kev"] < LOW_ENERGY_KEV
                             or row["eff"] > 1.0):
                dropped.append(row)
                continue
            rows.append(row)
    if dropped:
        sys.stderr.write(
            u"отброшено строк ниже %g кэВ или с Eff > 1: %d (%s)\n"
            % (LOW_ENERGY_KEV, len(dropped),
               ", ".join("%.1f кэВ" % r["energy_kev"] for r in dropped[:6])))
    return rows


def run(workdir, geometry, decays, energies, tag="", variant="full", threads=1,
        overrides=None, seed=DEFAULT_SEED, engine="in", glecs=False,
        full_eff=False):
    u"""Один прогон. `engine="json"` идёт через `Prepare_Json` — только там
    доступны `useGLECS`/`useEPDL97`; зерно туда не передаётся (у экспорта нет
    такого слова), поэтому воспроизводимость есть только у `engine="in"`."""
    ensdf_dir = os.path.join(workdir, "Lib", "ENSDF2")
    if not os.path.isdir(ensdf_dir):
        os.makedirs(ensdf_dir)
    scale_set.write_scale(os.path.join(ensdf_dir, "%03d.ENX" % FAKE_A), energies)

    args = [os.path.join(workdir, "TccfProbe2.exe"), workdir,
            str(FAKE_A), str(FAKE_Z), "0", str(decays)]
    extra = {"calc_full_eff": True} if full_eff else None
    if engine == "json":
        params = {"threads_number": threads,
                  "calc_electron_ttb": VARIANTS[variant].get(
                      "calc_electron_ttb", True),
                  "useGLECS": bool(glecs),
                  "calc_full_eff": bool(full_eff)}
        doc = in2json.convert(geometry, params, (FAKE_A, FAKE_Z, 0))
        json_path = os.path.join(workdir, "tccfcalc_geometry.json")
        with io.open(json_path, "w", encoding="utf-8") as f:
            f.write(json.dumps(doc, indent=1, ensure_ascii=False))
        args.append("--json=" + json_path)
        expect = "Prepare_Json -> 0"
    else:
        if glecs:
            raise SystemExit(u"--glecs требует --engine=json: из .in этот ключ "
                             u"не читается (README §13.3)")
        upgrade_in(geometry, os.path.join(workdir, "tccfcalc.in"),
                   variant=variant, threads=threads, overrides=overrides,
                   extra_params=extra)
        args.append("--seed=%d" % seed)
        expect = "Prepare -> 0"

    started = time.time()
    proc = subprocess.run(args, cwd=workdir, capture_output=True, text=True,
                          encoding="latin-1")
    elapsed = time.time() - started
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        raise SystemExit(u"TccfProbe2 вернул %d" % proc.returncode)
    if expect not in proc.stdout:
        sys.stderr.write(proc.stdout)
        raise SystemExit(u"Prepare отказал")
    # Проверка по ПЕЧАТИ, а не по коду процесса: TccfProbe2 исторически
    # возвращал 0 и при отказе Calculate (отказ ловился лишь числом строк
    # отчёта, то есть поздно и невнятно). По печати работает и со старыми
    # копиями exe в рабочих каталогах.
    if not re.search(r"^Calculate(_n_sec)? -> 0\s*$", proc.stdout, re.M):
        sys.stderr.write(proc.stdout)
        raise SystemExit(u"Calculate отказал")

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
    p.add_argument("--seed", type=int, default=DEFAULT_SEED,
                   help=u"зерно ГСЧ (шестое слово Prepare); 0 — от time(0)")
    p.add_argument("--engine", default="in", choices=("in", "json"))
    p.add_argument("--glecs", action="store_true",
                   help=u"useGLECS = true; требует --engine=json")
    p.add_argument("--full-eff", action="store_true", dest="full_eff",
                   help=u"calc_full_eff = true: одиннадцатый столбец отчёта")
    args = p.parse_args()

    energies = ([float(x) for x in args.energies.split(",")]
                if args.energies else DEFAULT_ENERGIES)
    rows, elapsed = run(args.workdir, args.geometry, args.decays, energies,
                        args.tag, args.variant, args.threads,
                        seed=args.seed, engine=args.engine, glecs=args.glecs,
                        full_eff=args.full_eff)

    print(u"# %s, %s, %s, распадов %d, потоков %d, зерно %s, %.1f с"
          % (os.path.basename(args.geometry), args.variant,
             args.engine + (" +GLECS" if args.glecs else ""), args.decays,
             args.threads, args.seed if args.engine == "in" else u"—", elapsed))
    print("E_keV,eff,eff_err_pct,cf,cf_err_pct,area,full_eff")
    for r in rows:
        print("%.1f,%.5E,%.2f,%.5f,%.2f,%.0f,%s"
              % (r["energy_kev"], r["eff"], r["eff_err_pct"],
                 r["cf"], r["cf_err_pct"], r["area"],
                 "" if r["full_eff"] is None else "%.5E" % r["full_eff"]))
    if args.csv:
        with io.open(args.csv, "w", encoding="utf-8") as f:
            f.write(u"E_keV,eff,eff_err_pct,cf,cf_err_pct,area,full_eff\n")
            for r in rows:
                f.write(u"%.1f,%.5E,%.2f,%.5f,%.2f,%.0f,%s\n"
                        % (r["energy_kev"], r["eff"], r["eff_err_pct"],
                           r["cf"], r["cf_err_pct"], r["area"],
                           "" if r["full_eff"] is None
                           else "%.5E" % r["full_eff"]))


if __name__ == "__main__":
    main()
