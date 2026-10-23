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

Шесть сверок, каждая — отказ:
  1. `src=` в паспорте DLL == sha256 (16 знаков) файлов `*.cu *.cuh *.h *.inc` каталога
     `tools/effmaker/gpu`, отсортированных по имени порядково без учёта регистра и
     склеенных подряд (то же правило — `tools/effmaker/gpu/src_hash.ps1`);
  2. `phys=` в паспорте DLL == `RM_PHYSICS_VERSION` в `physics.h` ==
     `ResponseMatrix.PhysicsVersion` в `BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs`;
  3. копия `tools/effmaker/gpu/bin/rmgpu_f.dll` (если есть — она не в git) байт в байт
     равна поставочной: скрипт сборки кладёт обе, расхождение — чья-то сборка мимо скрипта;
  4. (П253, 14.10.2026) настройки ядра `cfg.h` == открытые простые поля `EfficiencySimulator`
     по исходнику (имя и вид) — то, что упаковщик `GpuPack.Settings` шлёт в `rm_cfg_set`.
     Прежде это сверял только `gen_cfg.ps1 -Check` по собранному exe, и в `check_all` его не
     было: поле, заведённое в C# мимо `gen_cfg.ps1`, проходило все сторожа, а в приложении
     счёт на GPU отказывал у человека;
  5. (`AMBER233`, П337, 18.10.2026) `cudart=` в паспорте DLL == закреплённый в `build_gpu.cmd`
     тулкит (`set CUDA_VER=13.4` → 13040). Рантайм CUDA слинкован статически и задаёт
     наименьший драйвер NVIDIA у каждого человека; прежде `nvcc` брался из PATH, и снос
     CUDA 12 (18.10.2026) поднял бы это требование следующей же пересборкой молча.
     Поставочная DLL сборки 07–14.10.2026 (CUDA 12.8) принимается по своему отпечатку
     исходников (`LEGACY_CUDART`) — до первой пересборки;
  6. (`AMBER262`, 23.10.2026) `RmGpu.LinkedCudaRuntime` в `GpuMatrixRun.cs` == `cudart=` в
     паспорте DLL. Предпроверка драйвера идёт ДО загрузки библиотеки (рантайм 13.4 без
     драйвера падает) и потому знает нужную версию из константы, а не из паспорта; константа,
     отставшая от пересборки, пускала бы рантайм к драйверу, которого ему мало, или гасила бы
     годный.

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
BUILD_CMD = os.path.join(GPU_DIR, 'build_gpu.cmd')
EXTS = ('.cu', '.cuh', '.h', '.inc')
# (`AMBER233`) Поставочная DLL, собранная до закрепления тулкита: отпечаток исходников → её
# `cudart`. Пересборка `build_gpu.cmd` даёт закреплённый рантайм, и строку можно снять.
LEGACY_CUDART = {u'ef10f05d0fa68e36': 12080}


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def source_hash(gpu_dir=GPU_DIR):
    u"""sha256 (16 знаков) исходников ядра — то же правило, что `src_hash.ps1`.

    Байты CR (0x0D) из хеша выброшены нарочно (П245-R, 07.10.2026): свежий checkout при
    `core.autocrlf=true` даёт CRLF, основное дерево держит LF — один текст давал два отпечатка,
    и сторож краснел в каждом worktree на той же DLL. Самопроверка это сторожит.
    """
    names = [n for n in os.listdir(gpu_dir)
             if os.path.isfile(os.path.join(gpu_dir, n)) and os.path.splitext(n)[1].lower() in EXTS]
    names.sort(key=lambda n: n.lower())
    h = hashlib.sha256()
    for n in names:
        with io.open(os.path.join(gpu_dir, n), 'rb') as f:
            h.update(f.read().replace(b'\r', b''))
    return h.hexdigest()[:16], names


def physics_h(path=PHYSICS_H):
    text = io.open(path, encoding='utf-8').read()
    m = re.search(r'#define\s+RM_PHYSICS_VERSION\s+(\d+)', text)
    return int(m.group(1)) if m else None


def physics_cs(path=RESPONSE_CS):
    text = io.open(path, encoding='utf-8-sig').read()
    m = re.search(r'public\s+const\s+int\s+PhysicsVersion\s*=\s*(\d+)\s*;', text)
    return int(m.group(1)) if m else None


GPU_RUN_CS = os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', 'GpuMatrixRun.cs')


def linked_cs(path=GPU_RUN_CS):
    u"""(`AMBER262`) `RmGpu.LinkedCudaRuntime` — рантайм, под который приложение проверяет драйвер."""
    text = io.open(path, encoding='utf-8-sig').read()
    m = re.search(r'public\s+const\s+int\s+LinkedCudaRuntime\s*=\s*(\d+)\s*;', text)
    return int(m.group(1)) if m else None


def linked_problems(fields, linked):
    u"""(`AMBER262`) Шестая сверка: `LinkedCudaRuntime` приложения == `cudart=` паспорта DLL."""
    if linked is None:
        return [u'в GpuMatrixRun.cs нет «public const int LinkedCudaRuntime = …» — предпроверке драйвера '
                u'не с чем сравнивать версию']
    try:
        got = int(fields.get(u'cudart', u''))
    except ValueError:
        return []  # паспорт без cudart — отказ уже дала пятая сверка
    if got == linked:
        return []
    return [u'RmGpu.LinkedCudaRuntime = %d, а DLL слинкована с рантаймом %d — предпроверка драйвера '
            u'(AMBER262) судит не под ту библиотеку; поправить константу' % (linked, got)]


def pinned_cudart(path=BUILD_CMD):
    u"""`CUDART_VERSION` закреплённого в `build_gpu.cmd` тулкита: `set CUDA_VER=13.4` → 13040."""
    text = io.open(path, encoding='ascii', errors='replace').read()
    m = re.search(r'^set CUDA_VER=(\d+)\.(\d+)\s*$', text, re.M)
    return int(m.group(1)) * 1000 + int(m.group(2)) * 10 if m else None


SIM_CS = [os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', n)
          for n in ('EfficiencySimulator.cs', 'ElectronTransport.cs')]
CFG_H = os.path.join(GPU_DIR, 'cfg.h')
CS_KIND = {u'bool': u'B', u'int': u'I', u'long': u'L', u'double': u'D'}
CS_PRIMITIVE = (u'bool', u'int', u'long', u'double', u'float', u'uint', u'ulong', u'short', u'ushort',
                u'byte', u'sbyte', u'char')


def sim_fields(texts):
    u"""Открытые экземплярные поля примитивных типов `EfficiencySimulator` по ИСХОДНИКУ — то же
    правило, что у упаковщика `GpuPack.Settings` и `gen_cfg.ps1` (отражение по сборке): кроме
    счётчиков-выходов `Count*`/`Sum*`/`Last*`/`Weight*`. Класс — `partial`, тела берутся из
    блоков `public sealed partial class EfficiencySimulator` (отступ 4) до их `    }`, поля —
    строки членов класса (отступ 8). Словарь имя → вид (B/I/L/D; иной примитив — `?`)."""
    out = {}
    for text in texts:
        inside = False
        for line in text.splitlines():
            if not inside:
                if re.match(r'^    public sealed partial class EfficiencySimulator\b', line):
                    inside = True
                continue
            if line.rstrip() == u'    }':
                inside = False
                continue
            m = re.match(r'^        public (?:readonly )?(\w+) (\w+)\s*(?:=[^;]*)?;', line)
            if not m or m.group(1) not in CS_PRIMITIVE:
                continue
            name = m.group(2)
            if name.startswith((u'Count', u'Sum', u'Last', u'Weight')):
                continue
            out[name] = CS_KIND.get(m.group(1), u'?')
    return out


def cfg_fields(text):
    return dict((n, k) for k, n in re.findall(r'X\(([BILD]), (\w+)\)', text))


def cfg_problems(sim, cfg):
    u"""(П253, 14.10.2026) Четвёртая сверка: настройки ядра (`cfg.h`) == открытые простые поля
    симулятора. Поле, заведённое в C# без `gen_cfg.ps1`, сторож прежде не видел: исходники ядра
    не менялись, отпечаток сходился, а в приложении при галке «Use Nvidia GPU» счёт отказывал
    уже у человека (`rm_cfg_set`: «настройка … GPU-пути не известна»)."""
    problems = []
    if not sim:
        return [u'не найдено ни одного поля EfficiencySimulator — разбор исходника сломан']
    for n in sorted(set(sim) - set(cfg)):
        problems.append(u'поле EfficiencySimulator.%s есть в C#, нет в cfg.h — gen_cfg.ps1 и перенос в ядро' % n)
    for n in sorted(set(cfg) - set(sim)):
        problems.append(u'поле %s есть в cfg.h, нет в EfficiencySimulator — gen_cfg.ps1' % n)
    for n in sorted(set(sim) & set(cfg)):
        if sim[n] != cfg[n]:
            problems.append(u'поле %s: в C# вид %s, в cfg.h — %s' % (n, sim[n], cfg[n]))
    return problems


def read_sim_and_cfg():
    texts = [io.open(p, encoding='utf-8-sig').read() for p in SIM_CS if os.path.isfile(p)]
    return sim_fields(texts), cfg_fields(io.open(CFG_H, encoding='utf-8').read())


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


def cudart_problems(fields, want):
    u"""(`AMBER233`) Пятая сверка: рантайм CUDA в паспорте == закреплённый в `build_gpu.cmd`."""
    if want is None:
        return [u'в build_gpu.cmd нет строки «set CUDA_VER=<major>.<minor>» — тулкит не закреплён']
    try:
        got = int(fields.get(u'cudart', u''))
    except ValueError:
        return [u'паспорт DLL без версии рантайма CUDA (cudart=%s)' % fields.get(u'cudart')]
    if got == want or LEGACY_CUDART.get(fields.get(u'src')) == got:
        return []
    return [u'DLL слинкована с рантаймом CUDA %d, build_gpu.cmd закрепляет %d — собрана мимо закрепления; '
            u'пересобрать build_gpu.cmd (рантайм задаёт наименьший драйвер у людей, README «Сборка»)' % (got, want)]


def judge(info, src, phys_h, phys_cs, cudart_want, linked):
    u"""Список отказов (пустой — сходится) по паспорту `info` и ожиданиям."""
    problems = []
    fields = parse_info(info)
    problems.extend(cudart_problems(fields, cudart_want))
    problems.extend(linked_problems(fields, linked))
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
    ph, pc, cw, lk = physics_h(), physics_cs(), pinned_cudart(), linked_cs()
    if ph is None or pc is None or cw is None or lk is None:
        print(u'самопроверка: не прочитаны physics.h (%s), PhysicsVersion (%s), CUDA_VER build_gpu.cmd (%s) '
              u'или LinkedCudaRuntime (%s)' % (ph, pc, cw, lk))
        return 1
    good = u'src=%s;phys=%d;arch=sm_86;real=float;cudart=%d;' % (src, ph, cw)
    legacy = sorted(LEGACY_CUDART.items())[0]
    fake = u'a' * 16
    # Случай — (паспорт, отпечаток дерева, ожидаемый ответ, что проверяется[, LinkedCudaRuntime]);
    # без пятого поля константа приложения равна закреплённому рантайму.
    cases = [
        (good, src, 0, u'верный паспорт'),
        (u'src=0000000000000000;phys=%d;cudart=%d;' % (ph, cw), src, 1, u'чужой отпечаток'),
        (u'src=%s;phys=%d;cudart=%d;' % (src, ph + 1, cw), src, 1, u'чужая физика'),
        (u'src=unknown;phys=%d;cudart=%d;' % (ph, cw), src, 1, u'сборка мимо скрипта'),
        (u'', src, 1, u'пустой паспорт'),
        (u'src=%s;phys=%d;cudart=%d;' % (src, ph, cw - 10), src, 1, u'рантайм CUDA мимо закрепления'),
        (u'src=%s;phys=%d;' % (src, ph), src, 1, u'паспорт без cudart'),
        (u'src=%s;phys=%d;cudart=%d;' % (legacy[0], ph, legacy[1]), legacy[0], 0,
         u'прежняя поставочная DLL (CUDA %d) по своему отпечатку' % legacy[1], legacy[1]),
        (u'src=%s;phys=%d;cudart=%d;' % (fake, ph, legacy[1]), fake, 1,
         u'рантайм прежней DLL у других исходников'),
        (good, src, 1, u'LinkedCudaRuntime отстал от паспорта (AMBER262)', cw - 10),
        (good, src, 1, u'LinkedCudaRuntime не найден в GpuMatrixRun.cs', None),
    ]
    bad = 0
    for case in cases:
        info, tree, want, what = case[:4]
        linked = case[4] if len(case) > 4 else cw
        got = 1 if judge(info, tree, ph, pc, cw, linked) else 0
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
        # CRLF вместо LF — тот же отпечаток (checkout с autocrlf не должен красить сторож).
        p0 = os.path.join(tmp, names[0])
        raw = io.open(p0, 'rb').read()
        io.open(p0, 'wb').write(raw.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))
        crlf, _ = source_hash(tmp)
        with io.open(p0, 'ab') as f:
            f.write(b'\n')
        changed, _ = source_hash(tmp)
        ok = same == src and crlf == src and changed != src
        bad += 0 if ok else 1
        print(u'  %s копия даёт тот же отпечаток, CRLF — тот же, байт сверху — другой' % (u'✓' if ok else u'✗'))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    # (П253) Сверка настроек: дерево сходится; лишнее поле в C#, пропавшее поле и смена вида —
    # отказ. Порча — на копии текста исходника, а не на дереве.
    sim, cfg = read_sim_and_cfg()
    texts = [io.open(p, encoding='utf-8-sig').read() for p in SIM_CS if os.path.isfile(p)]
    anchor = u'        public bool AnalogContinuum'
    extra = [t.replace(anchor, u'        public double P253Probe = 0.0;\n' + anchor, 1) for t in texts]
    gone = [t.replace(anchor, u'        bool AnalogContinuumHidden', 1) for t in texts]
    kind = [t.replace(anchor, u'        public int AnalogContinuum', 1) for t in texts]
    cfg_cases = [
        (sim, 0, u'настройки: дерево (%d полей C#, %d в cfg.h)' % (len(sim), len(cfg))),
        (sim_fields(extra), 1, u'настройки: новое поле в C# без cfg.h'),
        (sim_fields(gone), 1, u'настройки: поле cfg.h пропало из C#'),
        (sim_fields(kind), 1, u'настройки: поле сменило вид'),
    ]
    for fields, want, what in cfg_cases:
        got = 1 if cfg_problems(fields, cfg) else 0
        ok = got == want
        bad += 0 if ok else 1
        print(u'  %s %s → %s' % (u'✓' if ok else u'✗', what, u'отказ' if got else u'сходится'))
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
    ph, pc, cw, lk = physics_h(), physics_cs(), pinned_cudart(), linked_cs()
    print(u'исходники ядра: %d файлов, отпечаток %s; physics.h %s, приложение %s; build_gpu.cmd закрепляет cudart %s; '
          u'предпроверка драйвера приложения — под рантайм %s' % (len(names), src, ph, pc, cw, lk))
    if not os.path.isfile(SHIPPED):
        print(u'⛔ нет поставочной %s — её кладёт build_gpu.cmd' % os.path.relpath(SHIPPED, REPO))
        return 1
    info, why = build_info(SHIPPED)
    if info is None:
        print(u'⛔ %s: %s' % (os.path.relpath(SHIPPED, REPO), why))
        return 1
    print(u'паспорт поставочной DLL: %s' % info)
    problems = judge(info, src, ph, pc, cw, lk)
    got_cudart = parse_info(info).get(u'cudart')
    if not problems and got_cudart != str(cw):
        print(u'⚠ поставочная DLL собрана до закрепления (cudart=%s), принята по отпечатку исходников; '
              u'следующая пересборка build_gpu.cmd даст %s' % (got_cudart, cw))
    if os.path.isfile(BUILT):
        a = io.open(SHIPPED, 'rb').read()
        b = io.open(BUILT, 'rb').read()
        if a != b:
            problems.append(u'%s и %s — разные файлы (%d и %d байт): одна из них собрана мимо скрипта'
                            % (os.path.relpath(SHIPPED, REPO), os.path.relpath(BUILT, REPO), len(a), len(b)))
        else:
            print(u'копия в gpu/bin — та же, байт в байт')
    sim, cfg = read_sim_and_cfg()
    cfgp = cfg_problems(sim, cfg)
    if not cfgp:
        print(u'настройки ядра (cfg.h) = открытые простые поля EfficiencySimulator: %d' % len(cfg))
    problems.extend(cfgp)
    for p in problems:
        print(u'⛔ ' + p)
    print(u'положительный контроль этой проверки: python tools/check_gpu_dll.py --selftest')
    print(u'GPU-библиотека: %s' % (u'СХОДИТСЯ' if not problems else u'НЕ СХОДИТСЯ'))
    return 0 if not problems else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
