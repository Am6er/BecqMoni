#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож поставочной GPU-библиотеки `BecquerelMonitor/rmgpu_f.dll` (`AMBER219`, П245, 07.10.2026).

Зачем. Решением Amber 07.10.2026 («В git, как SpecUtilsNet.dll») ядро переноса для GPU
едет в поставку ДВОИЧНЫМ файлом рядом с приложением, а его исходники (`tools/effmaker/gpu`)
правятся вместе с C#-переносом (`EfficiencySimulator.cs` и соседи). Двоичник, отставший от
своих исходников, считает СТАРУЮ физику молча: матрица выходит с нынешним клеймом, а
числа — прежние. Ни клеймо, ни побитовая сверка этого не ловят (та же беда, что `A77`).
Поэтому сборка (`build_gpu.cmd`) вшивает в `rm_build_info()` отпечаток исходников и
поколение физики, а этот сторож пересчитывает отпечаток по дереву и сверяет.

Три сверки, каждая — отказ:
  1. `src=` в паспорте DLL == sha256 (16 знаков) файлов `*.cu *.cuh *.h *.inc` каталога
     `tools/effmaker/gpu`, отсортированных по имени порядково без учёта регистра и
     склеенных подряд (то же правило — `tools/effmaker/gpu/src_hash.ps1`);
  2. `phys=` в паспорте DLL == `RM_PHYSICS_VERSION` в `physics.h` ==
     `ResponseMatrix.PhysicsVersion` в `BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs`;
  3. копия `tools/effmaker/gpu/bin/rmgpu_f.dll` (если есть — она не в git) байт в байт
     равна поставочной: скрипт сборки кладёт обе, расхождение — чья-то сборка мимо скрипта.

Паспорт читается из самой DLL (`ctypes`): она слинкована со статическим рантаймом CUDA и
зависит только от KERNEL32, а `rm_build_info` устройства не трогает — сторож идёт и на
машине без карты NVIDIA.

  python tools/check_gpu_dll.py            — сверка (код 0 — сходится, 1 — нет, 2 — нечего сверять)
  python tools/check_gpu_dll.py --selftest — положительный контроль: подложные паспорта
                                             (чужой отпечаток, чужая физика, «unknown»)
                                             обязаны дать отказ, верный — нет
"""

import ctypes
import hashlib
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
GPU_DIR = os.path.join(REPO, 'tools', 'effmaker', 'gpu')
SHIPPED = os.path.join(REPO, 'BecquerelMonitor', 'rmgpu_f.dll')
BUILT = os.path.join(GPU_DIR, 'bin', 'rmgpu_f.dll')
PHYSICS_H = os.path.join(GPU_DIR, 'physics.h')
RESPONSE_CS = os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', 'ResponseMatrix.cs')
EXTS = ('.cu', '.cuh', '.h', '.inc')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def source_hash(gpu_dir=GPU_DIR):
    u"""sha256 (16 знаков) исходников ядра — то же правило, что `src_hash.ps1`."""
    names = [n for n in os.listdir(gpu_dir)
             if os.path.isfile(os.path.join(gpu_dir, n)) and os.path.splitext(n)[1].lower() in EXTS]
    names.sort(key=lambda n: n.lower())
    h = hashlib.sha256()
    for n in names:
        with io.open(os.path.join(gpu_dir, n), 'rb') as f:
            h.update(f.read())
    return h.hexdigest()[:16], names


def physics_h(path=PHYSICS_H):
    text = io.open(path, encoding='utf-8').read()
    m = re.search(r'#define\s+RM_PHYSICS_VERSION\s+(\d+)', text)
    return int(m.group(1)) if m else None


def physics_cs(path=RESPONSE_CS):
    text = io.open(path, encoding='utf-8-sig').read()
    m = re.search(r'public\s+const\s+int\s+PhysicsVersion\s*=\s*(\d+)\s*;', text)
    return int(m.group(1)) if m else None


def build_info(dll_path):
    u"""Строка `rm_build_info()` из DLL; None с причиной, если не читается."""
    try:
        lib = ctypes.WinDLL(dll_path)
    except OSError as e:
        return None, u'не грузится: %s' % e
    try:
        fn = lib.rm_build_info
    except AttributeError:
        return None, u'нет rm_build_info — сборка до AMBER219'
    fn.restype = ctypes.c_char_p
    fn.argtypes = []
    raw = fn()
    return (raw.decode('utf-8', 'replace') if raw else u''), None


def parse_info(info):
    out = {}
    for part in (info or u'').split(u';'):
        if u'=' in part:
            k, v = part.split(u'=', 1)
            out[k] = v
    return out


def judge(info, src, phys_h, phys_cs):
    u"""Список отказов (пустой — сходится) по паспорту `info` и ожиданиям."""
    problems = []
    fields = parse_info(info)
    got_src = fields.get(u'src')
    if not got_src or got_src == u'unknown':
        problems.append(u'паспорт DLL без отпечатка исходников (src=%s) — собрана мимо build_gpu.cmd' % got_src)
    elif got_src != src:
        problems.append(u'DLL собрана из других исходников: src=%s, дерево даёт %s — пересобрать build_gpu.cmd'
                        % (got_src, src))
    try:
        got_phys = int(fields.get(u'phys', u''))
    except ValueError:
        got_phys = None
    if got_phys is None:
        problems.append(u'паспорт DLL без поколения физики (phys=%s)' % fields.get(u'phys'))
    else:
        if phys_h is not None and got_phys != phys_h:
            problems.append(u'DLL несёт физику %d, physics.h — %d: пересобрать' % (got_phys, phys_h))
        if phys_cs is not None and got_phys != phys_cs:
            problems.append(u'DLL несёт физику %d, приложение (ResponseMatrix.PhysicsVersion) — %d: '
                            u'перенести правку физики в ядро, поднять physics.h, пересобрать' % (got_phys, phys_cs))
    if phys_h is not None and phys_cs is not None and phys_h != phys_cs:
        problems.append(u'physics.h (%d) разошёлся с ResponseMatrix.PhysicsVersion (%d)' % (phys_h, phys_cs))
    return problems


def selftest():
    src, names = source_hash()
    ph, pc = physics_h(), physics_cs()
    if ph is None or pc is None:
        print(u'самопроверка: не прочитаны physics.h (%s) или PhysicsVersion (%s)' % (ph, pc))
        return 1
    good = u'src=%s;phys=%d;arch=sm_86;real=float;cudart=12080;' % (src, ph)
    cases = [
        (good, 0, u'верный паспорт'),
        (u'src=0000000000000000;phys=%d;' % ph, 1, u'чужой отпечаток'),
        (u'src=%s;phys=%d;' % (src, ph + 1), 1, u'чужая физика'),
        (u'src=unknown;phys=%d;' % ph, 1, u'сборка мимо скрипта'),
        (u'', 1, u'пустой паспорт'),
    ]
    bad = 0
    for info, want, what in cases:
        got = 1 if judge(info, src, ph, pc) else 0
        ok = got == want
        bad += 0 if ok else 1
        print(u'  %s %s → %s' % (u'✓' if ok else u'✗', what, u'отказ' if got else u'сходится'))
    # Отпечаток обязан менять один байт исходника: считается по временной копии каталога.
    import shutil
    import tempfile
    tmp = tempfile.mkdtemp(prefix='gpu_hash_')
    try:
        for n in names:
            shutil.copy(os.path.join(GPU_DIR, n), os.path.join(tmp, n))
        same, _ = source_hash(tmp)
        with io.open(os.path.join(tmp, names[0]), 'ab') as f:
            f.write(b'\n')
        changed, _ = source_hash(tmp)
        ok = same == src and changed != src
        bad += 0 if ok else 1
        print(u'  %s копия даёт тот же отпечаток, байт сверху — другой' % (u'✓' if ok else u'✗'))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(u'самопроверка: %s' % (u'ПРОЙДЕНА' if bad == 0 else u'ПРОВАЛЕНА (%d)' % bad))
    return 0 if bad == 0 else 1


def main(argv):
    _utf8_console()
    if u'--selftest' in argv:
        return selftest()
    if not os.path.isdir(GPU_DIR):
        print(u'нет каталога %s — нечего сверять' % GPU_DIR)
        return 2
    src, names = source_hash()
    ph, pc = physics_h(), physics_cs()
    print(u'исходники ядра: %d файлов, отпечаток %s; physics.h %s, приложение %s' % (len(names), src, ph, pc))
    if not os.path.isfile(SHIPPED):
        print(u'⛔ нет поставочной %s — её кладёт build_gpu.cmd' % os.path.relpath(SHIPPED, REPO))
        return 1
    info, why = build_info(SHIPPED)
    if info is None:
        print(u'⛔ %s: %s' % (os.path.relpath(SHIPPED, REPO), why))
        return 1
    print(u'паспорт поставочной DLL: %s' % info)
    problems = judge(info, src, ph, pc)
    if os.path.isfile(BUILT):
        a = io.open(SHIPPED, 'rb').read()
        b = io.open(BUILT, 'rb').read()
        if a != b:
            problems.append(u'%s и %s — разные файлы (%d и %d байт): одна из них собрана мимо скрипта'
                            % (os.path.relpath(SHIPPED, REPO), os.path.relpath(BUILT, REPO), len(a), len(b)))
        else:
            print(u'копия в gpu/bin — та же, байт в байт')
    for p in problems:
        print(u'⛔ ' + p)
    print(u'положительный контроль этой проверки: python tools/check_gpu_dll.py --selftest')
    print(u'GPU-библиотека: %s' % (u'СХОДИТСЯ' if not problems else u'НЕ СХОДИТСЯ'))
    return 0 if not problems else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
