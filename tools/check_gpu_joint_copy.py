#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож копии полей `Joint*` в GPU-пути матрицы (`AMBER219`, П245-R, 07.10.2026).

Зачем. κ пар (`ResponseMatrixBuilder.BuildJoint`) GPU-путь считает на ЦП во ВРЕМЕННУЮ матрицу
одновременно с узлами на устройстве, а после узлов переносит её поля `Joint*` в итоговую
(`GpuBuild.Build`, BecquerelMonitor/EfficiencyMaker/GpuMatrixRun.cs). До П245-R перенос шёл
перебором свойств отражением — «всё, что начинается на Joint»; с переходом на прямой доступ он
стал ПОИМЁННЫМ списком присваиваний. Новое поле `Joint*`, которое начнёт писать `BuildJoint`,
до GPU-матрицы без этого сторожа не доехало бы МОЛЧА: компилятор не знает, что список обязан
быть полным, а матрица со склада выглядела бы годной (клеймо то же).

Правило: каждое `matrix.JointX = …` в теле `BuildJoint` обязано иметь пару
`matrix.JointX = jointMatrix.JointX` в `GpuBuild.Build`; лишняя копия (поля, которого
`BuildJoint` не пишет) — тоже отказ, чтобы список не зарастал.

  python tools/check_gpu_joint_copy.py            — сверка (код 0 — сходится, 1 — нет, 2 — не нашёл кода)
  python tools/check_gpu_joint_copy.py --selftest — положительный контроль на подложных списках
"""

import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
BUILDER = os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', 'ResponseMatrixBuilder.cs')
GPU_RUN = os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', 'GpuMatrixRun.cs')

RE_WRITE = re.compile(r'\bmatrix\.(Joint\w+)\s*=[^=]')
RE_COPY = re.compile(r'\bmatrix\.(Joint\w+)\s*=\s*jointMatrix\.(Joint\w+)\s*;')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def method_body(text, signature_re):
    u"""Тело метода от первой `{` после сигнатуры до парной `}`; None — не нашёл."""
    m = re.search(signature_re, text)
    if not m:
        return None
    i = text.find('{', m.end())
    if i < 0:
        return None
    depth = 0
    for j in range(i, len(text)):
        c = text[j]
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return text[i:j + 1]
    return None


def written_by_build_joint(text):
    body = method_body(text, r'static\s+void\s+BuildJoint\s*\(')
    if body is None:
        return None
    return set(RE_WRITE.findall(body))


def copied_by_gpu_build(text):
    body = method_body(text, r'public\s+static\s+ResponseMatrix\s+Build\s*\(\s*RmGpu\s+gpu\s*,\s*GeometryModel\s+geometry\s*,'
                             r'\s*ResponseMatrixOptions\s+options\s*,\s*IProgress')
    if body is None:
        return None, []
    pairs = RE_COPY.findall(body)
    mismatched = [(a, b) for a, b in pairs if a != b]
    return set(a for a, b in pairs if a == b), mismatched


def judge(written, copied, mismatched):
    problems = []
    for name in sorted(written - copied):
        problems.append(u'BuildJoint пишет matrix.%s, а GpuBuild.Build его из временной матрицы не переносит' % name)
    for name in sorted(copied - written):
        problems.append(u'GpuBuild.Build переносит matrix.%s, которого BuildJoint не пишет — лишняя копия' % name)
    for a, b in mismatched:
        problems.append(u'перенос кривой: matrix.%s = jointMatrix.%s' % (a, b))
    return problems


def selftest():
    bad = 0
    cases = [
        ({'JointA', 'JointB'}, {'JointA', 'JointB'}, [], 0, u'списки равны'),
        ({'JointA', 'JointB', 'JointC'}, {'JointA', 'JointB'}, [], 1, u'новое поле не перенесено'),
        ({'JointA'}, {'JointA', 'JointB'}, [], 1, u'лишняя копия'),
        ({'JointA'}, {'JointA'}, [('JointA', 'JointB')], 1, u'кривой перенос'),
    ]
    for w, c, m, want, what in cases:
        got = 1 if judge(w, c, m) else 0
        ok = got == want
        bad += 0 if ok else 1
        print(u'  %s %s → %s' % (u'✓' if ok else u'✗', what, u'отказ' if got else u'сходится'))
    # Разбор настоящего кода обязан что-то находить: пустые списки — слепой сторож.
    w = written_by_build_joint(io.open(BUILDER, encoding='utf-8-sig').read())
    c, _ = copied_by_gpu_build(io.open(GPU_RUN, encoding='utf-8-sig').read())
    ok = bool(w) and bool(c)
    bad += 0 if ok else 1
    print(u'  %s разбор кода находит поля: BuildJoint %s, GpuBuild %s' % (u'✓' if ok else u'✗',
          len(w) if w else 0, len(c) if c else 0))
    print(u'самопроверка: %s' % (u'ПРОЙДЕНА' if bad == 0 else u'ПРОВАЛЕНА (%d)' % bad))
    return 0 if bad == 0 else 1


def main(argv):
    _utf8_console()
    if u'--selftest' in argv:
        return selftest()
    for p in (BUILDER, GPU_RUN):
        if not os.path.isfile(p):
            print(u'нет %s' % p)
            return 2
    written = written_by_build_joint(io.open(BUILDER, encoding='utf-8-sig').read())
    copied, mismatched = copied_by_gpu_build(io.open(GPU_RUN, encoding='utf-8-sig').read())
    if written is None or copied is None:
        print(u'не нашёл BuildJoint или GpuBuild.Build(gpu, geometry, options, progress, …) — разбор сторожа устарел')
        return 2
    print(u'BuildJoint пишет полей Joint*: %d; GpuBuild.Build переносит: %d' % (len(written), len(copied)))
    problems = judge(written, copied, mismatched)
    for p in problems:
        print(u'⛔ ' + p)
    print(u'положительный контроль этой проверки: python tools/check_gpu_joint_copy.py --selftest')
    print(u'копия Joint*: %s' % (u'СХОДИТСЯ' if not problems else u'НЕ СХОДИТСЯ'))
    return 0 if not problems else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
