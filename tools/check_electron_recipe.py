#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож РЕЦЕПТА РАЗДЕЛА ЭЛЕКТРОНА ПРОБЫ: окно матрицы = склад корпуса (`AMBER305`, 05.11.2026).

## Зачем

Окно «Матрица отклика…» и склад корпуса считают раздел одним кодом, но рецепт записан числами
(`ElectronSectionRecipe`), а склад лежит файлами. Правка числа (зерно, сетка, позитрон) никому бы не
отказала: окно считало бы другое, разбор на матрицах пользователя и корпуса разошёлся бы молча. Критик
`crit305` показал, что мутант `Seed = 1` не ловился ничем.

## Что судит

  1. Константы рецепта в исходниках (`ElectronSectionRecipe`, `ResponseElectronSection`) против клейм
     всех разделов склада `corpus/geometries/*.rmx` и `response/*.rmx`: `grid`, `seed`, `e+`, `n`, `esec`; и у
     каждого файла — строки обратного рассеяния `BSCR` (`AMBER311`).
     Строки разобраны по хвосту, а не найдены подстрокой: новый exe со старой DLL пишет 144 пустые. Клеймо `for`
     раздела = хеш клейма фотонной части файла; `lines` и `br` закреплены (`STORE_LIB`).
  2. Склад однороден: смесь поколений — отказ.
  3. Провода: `ResponseMatrixForm` зовёт `AttachElectronSection`, тот берёт `ElectronSectionRecipe`,
     `BetaSectionProbeA274` — тот же код, без `--n` — рецепт.
  3б. (`AMBER318`, 07.11.2026) Поле окна «Истории бета»: его умолчание в разметке
     (`ResponseMatrixForm.Layout.cs`) — `ElectronSectionRecipe.StoreHistories` или то же число, то есть
     умолчание поля = рецепт = клеймо склада (`n`); окно снимает номинал С ПОЛЯ и не читает рычаг проб
     `ElectronSectionRecipe.Histories`; номинал доходит параметром `histories` до клейма (`StampFor`) и
     до обоих построителей (ЦП и GPU) в `AttachElectronSection`, цель останова — `errorTarget`.
  3а. Порог метки один: `BackscatterMaxCos` в `EfficiencySimulator.cs` = в `decl_branch.inc`, и `sim_branch.cuh`
     зовёт константу, а не свою копию числа.
  4. Пробы (если каталог `build` свеж, иначе код 3, как у соседей): `MatrixRefusalProbeP8`,
     `ElectronSectionFormProbeA305 --mode=failure|old`. `MatrixWindowProbeP291` не зовётся: 70 с и GPU.

⚠ **`STORE_PENDING`.** Пуста: рецепт `n=800000` (решение Amber 05.11.2026) и канал обратного рассеяния (`AMBER311`, 06.11.2026)
посчитаны единым счётом склада 07.11.2026, база `rev53`. Склад судится рецептом весь. Запись заводится на время следующей смены рецепта:
пока она стоит, `esec` и `n` старого поколения не отказ, а `CorpusFsaProbe` кончает кодом 68; пересчитанный склад при живой записи — код 1.

## Положительный контроль — на каждом прогоне

Подсаженные мутанты исходников: `Seed = 1`, `Positron = false`, другие `n`, `DefaultNodes`, края сетки,
окно без `AttachElectronSection`; (`AMBER318`) другое умолчание поля «Истории бета», окно на рычаге проб,
клеймо и построители ЦП и GPU без номинала из поля, ЦП-ветка окна без цели останова (`betatarget`). Каждый обязан быть пойман; не пойман — код 1.

  python tools/check_electron_recipe.py [--mutant=seed|positron|n|nodes|edge|wire] [--no-probes] [--probes=<каталог>] [--skip-freshness]

Коды: 0 — сошлось; 1 — разошлось; 2 — нет склада; 3 — каталог проб не свеж или нет пробы.
"""
import argparse
import glob
import io
import hashlib
import os
import re
import struct
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

BUILDER = os.path.join(REPO, u'BecquerelMonitor', u'EfficiencyMaker', u'ResponseMatrixBuilder.cs')
MATRIX = os.path.join(REPO, u'BecquerelMonitor', u'EfficiencyMaker', u'ResponseMatrix.cs')
FORM = os.path.join(REPO, u'BecquerelMonitor', u'ResponseMatrixForm.cs')
LAYOUT = os.path.join(REPO, u'BecquerelMonitor', u'ResponseMatrixForm.Layout.cs')
BETA = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'BetaSectionProbeA274.cs')
STORE = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'geometries')
PROBES_DEFAULT = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'build')
SIM = os.path.join(REPO, u'BecquerelMonitor', u'EfficiencyMaker', u'EfficiencySimulator.cs')
GPU_DECL = os.path.join(REPO, u'tools', u'effmaker', u'gpu', u'decl_branch.inc')
GPU_SIM = os.path.join(REPO, u'tools', u'effmaker', u'gpu', u'sim_branch.cuh')
# Библиотека, с которой считается склад: `lines` и `br` раздела (`D:\\BqMoni_Claude\\fix305\\lib`, журнал `fix305`).
STORE_LIB = {u'lines': u'141', u'br': u'116'}

# Запись снята: склад пересчитан 07.11.2026, база `rev53`. Следующий рецепт, ждущий пересчёта, пишется сюда же: {esec, n}.
STORE_PENDING = {}

RE_STAMP = re.compile(br'esec=(\d+);phys=(\d+);for=[0-9a-f]+;grid=(\d+),([\d.]+),([\d.]+),[0-9a-f]+;n=(\d+);seed=(-?\d+);e\+=(\d);bin=[\d.]+')

MUTANTS = {
    u'seed': (u'builder', u'public const int Seed = 0;', u'public const int Seed = 1;'),
    u'positron': (u'builder', u'public const bool Positron = true;', u'public const bool Positron = false;'),
    u'n': (u'builder', u'public const int StoreHistories = 800000;', u'public const int StoreHistories = 400000;'),
    u'nodes': (u'matrix', u'public const int DefaultNodes = 80;', u'public const int DefaultNodes = 40;'),
    u'edge': (u'matrix', u'return Grid(DefaultNodes, 60.0, 3600.0);', u'return Grid(DefaultNodes, 50.0, 3600.0);'),
    u'wire': (u'form', u'ResponseMatrixBuilder.AttachElectronSection(', u'ResponseMatrixBuilder.Absent('),
    u'cpucos': (u'sim', u'public const double BackscatterMaxCos = 0.0;', u'public const double BackscatterMaxCos = -0.5;'),
    u'gpucos': (u'gpu_decl', u'BackscatterMaxCos = (real)0.0;', u'BackscatterMaxCos = (real)-0.5;'),
    u'gpuliteral': (u'gpu_sim', u'cos < BackscatterMaxCos', u'cos < (real)0.0'),
    # (`AMBER318`) поле «Истории бета» и провод его номинала до построителя
    u'betadefault': (u'layout', u'0, ElectronSectionRecipe.StoreHistories);', u'0, 400000);'),
    u'betalever': (u'form', u'int betaHistories = (int)this.betaHistoriesBox.Value;',
                   u'int betaHistories = ElectronSectionRecipe.Histories;'),
    u'betastamp': (u'builder', u'recipe.StampFor(geometry, matrix, histories)', u'recipe.StampFor(geometry, matrix)'),
    u'betacpu': (u'builder', u': BuildElectronSection(geometry, matrix, recipe.Grid, histories,',
                 u': BuildElectronSection(geometry, matrix, recipe.Grid, ElectronSectionRecipe.Histories,'),
    u'betatarget': (u'form', u'betaHistories, options.ContinuumErrorTarget,', u'betaHistories, 0.0,'),
    u'betagpu': (u'builder', u'GpuBuild.BuildElectronSection(gpu, geometry, matrix, recipe.Grid, histories,',
                 u'GpuBuild.BuildElectronSection(gpu, geometry, matrix, recipe.Grid, ElectronSectionRecipe.Histories,'),
}


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def read(path):
    return io.open(path, encoding='utf-8-sig').read()


def sources():
    return {u'builder': read(BUILDER), u'matrix': read(MATRIX), u'form': read(FORM), u'layout': read(LAYOUT), u'beta': read(BETA),
            u'sim': read(SIM), u'gpu_decl': read(GPU_DECL), u'gpu_sim': read(GPU_SIM)}


def header_stamp(data):
    u"""Клеймо фотонной части: строка BinaryWriter сразу за BQRM и форматом."""
    if data[:4] != b'BQRM':
        return None
    pos, size, shift = 8, 0, 0
    while True:
        b = data[pos]
        pos += 1
        size |= (b & 0x7f) << shift
        shift += 7
        if not b & 0x80:
            break
    return data[pos:pos + size]


def rows_filled(data):
    u"""Хвост `BSCR` разобран: непусты ли строки хотя бы на половине узлов. Подстрока — не доказательство."""
    for m in reversed(list(re.finditer(b'BSCR', data))):
        pos = m.start() + 4
        try:
            nodes = struct.unpack_from('<i', data, pos)[0]
            if not 0 < nodes < 4096:
                continue
            pos += 4
            filled = 0
            for _ in range(nodes):
                size = struct.unpack_from('<i', data, pos)[0]
                pos += 4
                if size < 0 or pos + 4 * size > len(data):
                    raise ValueError
                if size and any(v > 0.0 for v in struct.unpack_from('<%df' % size, data, pos)):
                    filled += 1
                pos += 4 * size
        except (struct.error, ValueError):
            continue
        if data[pos:pos + 4] in (b'ELSC', b''):
            return filled * 2 >= nodes
    return False


def recipe_of(src):
    u"""Рецепт числами из исходников; None вместо числа — разобрать не вышло, это отказ."""
    def one(key, pattern):
        m = re.search(pattern, src[key])
        return m.group(1) if m else None

    positron = one(u'builder', r'public const bool Positron = (true|false);')
    return {
        u'n': one(u'builder', r'public const int StoreHistories = (\d+);'),
        u'seed': one(u'builder', r'public const int Seed = (-?\d+);'),
        u'e+': {u'true': u'1', u'false': u'0'}.get(positron),
        u'esec': one(u'matrix', r'public const int SectionPhysics = (\d+);'),
        u'nodes': one(u'matrix', r'public const int DefaultNodes = (\d+);'),
        u'lo': one(u'matrix', r'return Grid\(DefaultNodes, ([\d.]+), [\d.]+\);'),
        u'hi': one(u'matrix', r'return Grid\(DefaultNodes, [\d.]+, ([\d.]+)\);'),
    }


def num_equal(a, b):
    return a is not None and b is not None and float(a) == float(b)


def store_stamps():
    u"""Клейма разделов склада: путь → словарь полей; None — раздела в файле нет."""
    stamps = {}
    for path in sorted(glob.glob(os.path.join(STORE, u'*.rmx')) + glob.glob(os.path.join(STORE, u'response', u'*.rmx'))):
        with open(path, 'rb') as f:
            data = f.read()
        m = None
        for m in RE_STAMP.finditer(data):
            pass
        lib = None if m is None else re.search(br'lines=(\d+),[0-9a-f]+;br=(\d+),', data[m.start():m.start() + 400])
        owner = None if m is None else re.search(br';for=([0-9a-f]+);', m.group(0))
        head = header_stamp(data)
        stamps[path] = None if m is None else {
            u'lines': lib.group(1).decode() if lib else None, u'br': lib.group(2).decode() if lib else None,
            u'for_ok': owner is not None and head is not None
                        and owner.group(1).decode() == hashlib.sha256(head).hexdigest()[:16],
            u'esec': m.group(1).decode(), u'nodes': m.group(3).decode(), u'lo': m.group(4).decode(), u'hi': m.group(5).decode(),
            u'n': m.group(6).decode(), u'seed': m.group(7).decode(), u'e+': m.group(8).decode(),
            u'rows': rows_filled(data)}
    return stamps


def judge(src, stamps):
    u"""Список отказов и список замечаний."""
    bad, notes = [], []
    recipe = recipe_of(src)
    for key, value in sorted(recipe.items()):
        if value is None:
            bad.append(u'константа рецепта «%s» не разобрана в исходниках' % key)

    empty = [p for p, s in stamps.items() if s is None]
    if empty:
        bad.append(u'файлов склада без раздела: %d, например %s' % (len(empty), os.path.basename(empty[0])))
    kinds = {}
    for s in stamps.values():
        if s is not None:
            kinds[(s[u'esec'], s[u'n'])] = kinds.get((s[u'esec'], s[u'n']), 0) + 1
    if len(kinds) > 1:
        bad.append(u'склад — смесь поколений: %s' % u', '.join(u'esec=%s n=%s: %d' % (k[0], k[1], v) for k, v in sorted(kinds.items())))

    pending = 0
    for path, s in sorted(stamps.items()):
        if s is None:
            continue
        name = os.path.basename(path)
        for key in (u'nodes', u'lo', u'hi', u'seed', u'e+'):
            if not num_equal(recipe[key], s[key]):
                bad.append(u'%s: %s рецепта %s, в складе %s' % (name, key, recipe[key], s[key]))
        for key, value in sorted(STORE_LIB.items()):
            if s[key] != value:
                bad.append(u'%s: %s=%s, ждём %s' % (name, key, s[key], value))
        if not s[u'for_ok']:
            bad.append(u'%s: раздел посчитан под другое клеймо фотонной части' % name)
        current = s[u'esec'] == recipe[u'esec'] and s[u'n'] == recipe[u'n'] and s[u'rows']
        if current:
            continue
        if STORE_PENDING and s[u'esec'] == STORE_PENDING[u'esec'] and s[u'n'] == STORE_PENDING[u'n'] and not s[u'rows'] \
                and recipe[u'esec'] != STORE_PENDING[u'esec'] and recipe[u'n'] == u'800000':
            pending += 1
        else:
            bad.append(u'%s: esec=%s n=%s строки BSCR %s в складе против esec=%s n=%s рецепта' % (name, s[u'esec'], s[u'n'], s[u'rows'], recipe[u'esec'], recipe[u'n']))
    if pending:
        notes.append(u'⚠ склад ждёт пересчёта: %d файлов esec=%s n=%s против рецепта esec=%s n=%s (`AMBER305` → Э2 `AMBER274`); '
                     u'разбор их разделы не берёт, `CorpusFsaProbe` кончает кодом 68' % (pending, STORE_PENDING[u'esec'], STORE_PENDING[u'n'], recipe[u'esec'], recipe[u'n']))
    elif STORE_PENDING and stamps and not bad:
        bad.append(u'склад пересчитан: снять `STORE_PENDING` в этом стороже')

    cpu_cos = re.search(r'public const double BackscatterMaxCos = (-?[\d.]+);', src[u'sim'])
    gpu_cos = re.search(r'static constexpr real BackscatterMaxCos = \(real\)(-?[\d.]+);', src[u'gpu_decl'])
    if not cpu_cos or not gpu_cos or float(cpu_cos.group(1)) != float(gpu_cos.group(1)):
        bad.append(u'`BackscatterMaxCos`: CPU %s, GPU %s' % (cpu_cos and cpu_cos.group(1), gpu_cos and gpu_cos.group(1)))
    if src[u'gpu_sim'].count(u'backState == 0 && cos < BackscatterMaxCos') != 2 or re.search(r'backState == 0 && cos < \(real\)', src[u'gpu_sim']):
        bad.append(u'`sim_branch.cuh`: порог метки — своя копия числа, а не `BackscatterMaxCos`')

    if u'ResponseMatrixBuilder.AttachElectronSection(' not in src[u'form']:
        bad.append(u'окно не зовёт `AttachElectronSection`')
    bad.extend(beta_field_wire(src, recipe))
    for token in (u'ElectronSectionRecipe.LibraryLines(', u'ElectronSectionRecipe.BranchesOf(', u'n = ElectronSectionRecipe.StoreHistories;'):
        if token not in src[u'beta']:
            bad.append(u'`BetaSectionProbeA274` без `%s`' % token)
    return bad, notes


RE_BETA_FIELD = re.compile(r'this\.betaHistoriesBox = this\.Field\(.*?,\s*(ElectronSectionRecipe\.StoreHistories|\d+)\);', re.S)


def code_only(text):
    u"""Текст без строк-комментариев `//`: имя рычага в шапке — не чтение рычага."""
    return u'\n'.join(line for line in text.splitlines() if not line.lstrip().startswith(u'//'))


def beta_field_wire(src, recipe):
    u"""(`AMBER318`) Поле «Истории бета»: умолчание = рецепт склада, номинал с поля доходит до клейма и построителей."""
    bad = []
    m = RE_BETA_FIELD.search(src[u'layout'])
    if not m:
        bad.append(u'умолчание поля «Истории бета» не разобрано в разметке окна')
    else:
        value = recipe[u'n'] if m.group(1) == u'ElectronSectionRecipe.StoreHistories' else m.group(1)
        if not num_equal(value, recipe[u'n']):
            bad.append(u'умолчание поля «Истории бета» %s, рецепт склада n=%s' % (value, recipe[u'n']))
    form = src[u'form']
    if u'int betaHistories = (int)this.betaHistoriesBox.Value;' not in form:
        bad.append(u'окно не снимает номинал раздела с поля «Истории бета»')
    if u'ElectronSectionRecipe.Histories' in code_only(form):
        bad.append(u'окно читает рычаг проб `ElectronSectionRecipe.Histories`')
    if form.count(u'betaHistories,') < 2 or u'progress, token, null, histories, errorTarget);' not in form:
        bad.append(u'номинал поля не доходит из окна до `AttachElectronSection` (ЦП и GPU)')
    # (`AMBER318`, пятый заход, пробел, найденный `crit318b`) ЦП-ветка окна передаёт цель останова фотонов.
    if u'betaHistories, options.ContinuumErrorTarget,' not in code_only(form):
        bad.append(u'ЦП-ветка окна не передаёт цель останова `options.ContinuumErrorTarget` в раздел')
    builder = src[u'builder']
    head = builder.find(u'int histories, double errorTarget)')
    tail = builder.find(u'return ElectronSectionOutcome.Built;', head)
    if head < 0 or tail < 0:
        bad.append(u'нет `AttachElectronSection` с номиналом `histories` и целью `errorTarget`')
        return bad
    body = builder[head:tail]
    if u'ElectronSectionRecipe.FromLibrary(' not in body:
        bad.append(u'`AttachElectronSection` не берёт `ElectronSectionRecipe`')
    for token, what in ((u'recipe.StampFor(geometry, matrix, histories)', u'клеймо'),
                        (u'GpuBuild.BuildElectronSection(gpu, geometry, matrix, recipe.Grid, histories,', u'построитель GPU'),
                        (u': BuildElectronSection(geometry, matrix, recipe.Grid, histories,', u'построитель ЦП')):
        if token not in body:
            bad.append(u'`AttachElectronSection`: %s без номинала из поля' % what)
    if u'ElectronSectionRecipe.Histories' in code_only(body):
        bad.append(u'`AttachElectronSection` с номиналом читает рычаг проб `ElectronSectionRecipe.Histories`')
    if u'errorTarget);' not in body:
        bad.append(u'`AttachElectronSection`: цель останова не доходит до построителя ЦП')
    return bad


def mutated(src, name):
    key, old, new = MUTANTS[name]
    if old not in src[key]:
        return None
    copy = dict(src)
    copy[key] = src[key].replace(old, new)
    return copy


def controls(src, stamps):
    u"""Положительный контроль: каждый мутант обязан быть пойман."""
    failed = []
    for name in sorted(MUTANTS):
        copy = mutated(src, name)
        if copy is None:
            failed.append(u'%s: образец мутанта не найден в исходнике' % name)
            continue
        bad, _ = judge(copy, stamps)
        if not bad:
            failed.append(u'%s: не пойман' % name)
    return failed


def file_controls(src, stamps):
    u"""Положительный контроль разбора файла и судьи: пустые строки, подстрока, чужое клеймо, чужая библиотека."""
    failed = []
    row = struct.pack('<i2f', 2, 1.0, 0.5)
    empty = struct.pack('<i2f', 2, 0.0, 0.0)
    if not rows_filled(b'BSCR' + struct.pack('<i', 2) + row + row + b'ELSC'):
        failed.append(u'полные строки не признаны')
    if rows_filled(b'BSCR' + struct.pack('<i', 2) + empty + empty + b'ELSC'):
        failed.append(u'пустые строки признаны')
    if rows_filled(b'xxBSCRxx'):
        failed.append(u'подстрока признана за хвост')
    recipe = recipe_of(src)
    good = {u'esec': recipe[u'esec'], u'n': recipe[u'n'], u'nodes': recipe[u'nodes'], u'lo': recipe[u'lo'], u'hi': recipe[u'hi'],
            u'seed': recipe[u'seed'], u'e+': recipe[u'e+'], u'rows': True, u'for_ok': True,
            u'lines': STORE_LIB[u'lines'], u'br': STORE_LIB[u'br']}
    for key, value, name in ((u'rows', False, u'пустые строки'), (u'for_ok', False, u'чужое клеймо'),
                             (u'lines', u'140', u'чужие линии'), (u'br', u'115', u'чужие ветви')):
        fake = dict(good)
        fake[key] = value
        bad, _ = judge(src, {u'файл-контроль.rmx': fake})
        if not [b for b in bad if u'файл-контроль.rmx' in b]:
            failed.append(u'%s не пойманы судьёй' % name)
    return failed


def run_probe(probes, exe, args, tail):
    path = os.path.join(probes, exe)
    if not os.path.isfile(path):
        return 3, u'нет пробы %s' % path
    proc = subprocess.run([path] + args, cwd=probes, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=240)
    text = proc.stdout.decode('utf-8', 'replace')
    if proc.returncode != 0 or tail not in text:
        return 1, u'%s: код %d\n%s' % (exe, proc.returncode, u'\n'.join(text.splitlines()[-12:]))
    return 0, u'%s: код 0' % exe


def probes_part(probes, say, skip_freshness=False):
    if skip_freshness:
        say(u'  ⚠ свежесть каталога проб НЕ судится (--skip-freshness)')
    else:
        try:
            import check_fsa_report_view as neighbour
        except ImportError:
            say(u'  ⛔ сторож свежести не подключился')
            return 3
        exes = [u'matrixrefusalprobep8', u'electronsectionformprobea305']
        bad_fresh, _, err = neighbour.freshness(probes, exes)
        if err or bad_fresh:
            say(u'  ⛔ КАТАЛОГ ПРОБ ПРОТУХ (T226): %s' % (err or u'; '.join(bad_fresh)[:400]))
            say(u'     собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex')
            return 3
    scene = os.path.join(STORE, u'RC103_point0.in')
    rmx = os.path.join(STORE, u'RC103_point0.rmx')
    code = 0
    for exe, args in ((u'MatrixRefusalProbeP8.exe', [u'--geometry=' + scene]),
                      (u'ElectronSectionFormProbeA305.exe', [u'--mode=failure', u'--scene=' + scene]),
                      (u'ElectronSectionFormProbeA305.exe', [u'--mode=old', u'--scene=' + scene, u'--store=' + rmx])):
        c, text = run_probe(probes, exe, args, u'ВСЕ СОШЛИСЬ')
        say(u'  %s %s' % (u'ok' if c == 0 else u'⛔', text))
        code = max(code, c)
    return code


def main():
    _utf8_console()
    ap = argparse.ArgumentParser()
    ap.add_argument('--mutant', choices=sorted(MUTANTS))
    ap.add_argument('--no-probes', action='store_true')
    ap.add_argument('--probes', default=PROBES_DEFAULT)
    ap.add_argument('--skip-freshness', action='store_true')
    args = ap.parse_args()

    stamps = store_stamps()
    if not stamps:
        print(u'⛔ нет склада %s' % STORE)
        return 2

    src = sources()
    if args.mutant:
        src = mutated(src, args.mutant)
        print(u'⚠ мутант исходника в памяти: %s' % args.mutant)

    recipe = recipe_of(src)
    print(u'рецепт: n=%s seed=%s e+=%s esec=%s сетка %s %s…%s; файлов склада: %d' % (
        recipe[u'n'], recipe[u'seed'], recipe[u'e+'], recipe[u'esec'], recipe[u'nodes'], recipe[u'lo'], recipe[u'hi'], len(stamps)))
    field = RE_BETA_FIELD.search(src[u'layout'])
    print(u'поле окна «Истории бета»: умолчание %s' % (field.group(1) if field else u'не разобрано'))

    bad, notes = judge(src, stamps)
    for note in notes:
        print(u'  ' + note)
    for item in bad[:20]:
        print(u'  ⛔ ' + item)
    if len(bad) > 20:
        print(u'  ⛔ … и ещё %d' % (len(bad) - 20))

    failed = controls(sources(), stamps)
    for item in failed:
        print(u'  ⛔ контроль: ' + item)
    print(u'  контроль мутантами: %d из %d пойманы' % (len(MUTANTS) - len(failed), len(MUTANTS)))
    failed_files = file_controls(sources(), stamps)
    for item in failed_files:
        print(u'  ⛔ контроль файлов: ' + item)
    print(u'  контроль файлов: разбор хвоста и клеймо, отказов контроля %d' % len(failed_files))
    if bad or failed or failed_files:
        print(u'РАЗОШЛОСЬ')
        return 1

    if not args.no_probes:
        code = probes_part(args.probes, print, args.skip_freshness)
        if code != 0:
            print(u'РАЗОШЛОСЬ' if code == 1 else u'СТЕНД')
            return code
    print(u'СОШЛОСЬ')
    return 0


if __name__ == '__main__':
    sys.exit(main())
