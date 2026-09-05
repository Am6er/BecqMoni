# -*- coding: utf-8 -*-
u"""Сторож оговорки о СЕТКЕ ДРЕЙФА в `tools/pie/README.md`.

Заведён 05.09.2026 по строке `T71`. Родня: `T65` (вторая копия сетки в пробе),
`T70` и `T82` (вторые копии чисел в описаниях; их стережёт
`tools/check_fsa_docs.py`).

## Зачем

`tools/pie` — доматричный харнесс со СВОЕЙ сеткой дрейфа, и решением Amber от
05.09.2026 это признано законной независимостью: умолчания `pie` не двигаются.
Цена независимости — оговорка в его `README`, чтобы числа `pie` не складывали с
корпусными числами приложения. Оговорка называет умолчания ОБЕИХ сторон, то
есть заводит ВТОРУЮ КОПИЮ значений, живущих в коде, а вторая копия протухает
молча: компилятор `README` не читает.

Ровно так и протухла строка, из-за которой заведена `T71`: `README` описывал
скорость словами «сетка дрейфа 9×9», а к 01.09.2026 приложение по решению
Amber уже считало в ОДИН узел, и разница «81 против 1» нигде не была записана.

## Что сверяется

Единственный источник значения — ИСХОДНИК; `README` с ним СЛИЧАЕТСЯ.

* умолчания `pie` — `tools/pie/Program.cs`: поля `Options`
  (`GainRange`, `GainSteps`, `OffsetRangeKev`, `OffsetSteps`) и пол шага узлов
  континуума в `BuildHatBasis` (`minStep`, вбит числом);
* умолчания приложения — `BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs`:
  конструктор `FsaAnalyzer()` — те же четыре имени плюс
  `ContinuumKnotDivisor`. ⛔ Файл только ЧИТАЕТСЯ.

Каждая проверка привязана к якорю — куску текста строки `README`, — и разбирает
из неё числа. Отказ (код 1) даёт любой из четырёх случаев:

1. **ПРОТУХЛО** — в `README` записано не то, что стоит в исходнике;
2. **ЯКОРЬ НЕ НАЙДЕН** — оговорку или строку с числами убрали/переписали;
3. **ЯКОРЬ НЕ ОДИН** — текст размножился, и непонятно, что сверять;
4. **НЕ РАЗОБРАНО / РАЗБОР ИСХОДНИКА НЕ СОСТОЯЛСЯ** — строка найдена, но чисел
   в ней нет, или умолчание в исходнике перестало быть литералом.

Три последних — отказ НАРОЧНО. Сторож, который при непонятном входе молчит, —
это сторож, который всегда молчит.

## Чего этот сторож НЕ ловит

Он не судит ТЕКСТ оговорки, только числа в ней. Вывод «складывать нельзя»
можно выкинуть, оставив таблицу, и сторож промолчит. Он также не трогает
исторические числа `README` (§10 «Сетка дрейфа упирается в границу»,
±2 % × ±8 кэВ, 21×33 узла) — это ЗАМЕР, а не умолчание, и совпадать с
конструктором он не обязан.

Запуск:

    python tools/pie/check_grid_note.py
    python tools/pie/check_grid_note.py --self-test
    python tools/pie/check_grid_note.py --readme=<путь> --pie-src=<путь> --app-src=<путь>

Коды возврата: 0 — сошлось, 1 — отказ (любой из четырёх случаев выше).
"""

from __future__ import print_function

import io
import os
import re
import shutil
import sys
import tempfile


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

DEFAULT_README = os.path.join(REPO_ROOT, u'tools', u'pie', u'README.md')
DEFAULT_PIE_SRC = os.path.join(REPO_ROOT, u'tools', u'pie', u'Program.cs')
DEFAULT_APP_SRC = os.path.join(
    REPO_ROOT, u'BecquerelMonitor', u'FullSpectrumAnalysis', u'FsaAnalyzer.cs')

# Имена настроек. Это НЕ копия значений: значения берутся у исходников.
GRID_NAMES = [u'GainRange', u'GainSteps', u'OffsetRangeKev', u'OffsetSteps']


# ---------------------------------------------------------------- чтение --

def read_lines(path):
    u"""Читать с `newline=''`: питон рвёт текст на одиночном CR, а в дереве
    лежат файлы и с LF (README), и с CRLF (Program.cs)."""
    with io.open(path, u'r', encoding=u'utf-8-sig', newline=u'') as handle:
        return handle.read().splitlines()


def parse_defaults(lines, pattern_for, what):
    u"""Вынуть умолчания из исходника. Требуется РОВНО ОДНО присваивание
    литералом на имя: ноль — настройку переименовали, два — непонятно, какое
    из них живое. И то и другое — отказ."""
    values = {}
    problems = []
    for name in GRID_NAMES:
        rx = re.compile(pattern_for(name))
        hits = [(i + 1, m) for i, line in enumerate(lines)
                for m in [rx.search(line)] if m]
        if len(hits) != 1:
            problems.append(
                u'%s: умолчание `%s` разобрать не удалось — совпадений %d, '
                u'нужно ровно одно' % (what, name, len(hits)))
            continue
        line_no, match = hits[0]
        values[name] = (float(match.group(1)), line_no)
    return values, problems


def parse_knot_floor(lines, rx, what):
    u"""Пол шага узлов континуума: у `pie` он вбит в код числом, у приложения
    стоит настройкой. Требуется РОВНО ОДНО совпадение — иначе отказ."""
    compiled = re.compile(rx)
    hits = [m for line in lines for m in [compiled.search(line)] if m]
    if len(hits) != 1:
        return None, [u'%s: пол шага узлов континуума разобрать не удалось — '
                      u'совпадений %d, нужно ровно одно' % (what, len(hits))]
    return float(hits[0].group(1)), []


PIE_KNOT_RX = u'double\\s+minStep\\s*=\\s*\\(chHi\\s*-\\s*chLo\\)\\s*/\\s*([0-9.]+)'
APP_KNOT_RX = u'^\\s*this\\.ContinuumKnotDivisor\\s*=\\s*([0-9.]+)\\s*;'


def pie_pattern(name):
    return u'^\\s*public\\s+(?:double|int)\\s+' + name + u'\\s*=\\s*([0-9.]+)\\s*;'


def app_pattern(name):
    return u'^\\s*this\\.' + name + u'\\s*=\\s*([0-9.]+)\\s*;'


# ------------------------------------------------------------- ожидания --

def expectations(pie, app, pie_knot=None, app_knot=None):
    u"""Всё, что оговорка вправе называть числом, — производные умолчаний.
    Ни одного числа здесь не записано руками."""
    pie_nodes = pie[u'GainSteps'][0] * pie[u'OffsetSteps'][0]
    app_nodes = app[u'GainSteps'][0] * app[u'OffsetSteps'][0]
    return {
        u'pie.gain_range': pie[u'GainRange'][0],
        u'pie.gain_range_pct': pie[u'GainRange'][0] * 100.0,
        u'pie.gain_steps': pie[u'GainSteps'][0],
        u'pie.offset_kev': pie[u'OffsetRangeKev'][0],
        u'pie.offset_steps': pie[u'OffsetSteps'][0],
        u'pie.nodes': pie_nodes,
        u'app.gain_range': app[u'GainRange'][0],
        u'app.gain_range_pct': app[u'GainRange'][0] * 100.0,
        u'app.gain_steps': app[u'GainSteps'][0],
        u'app.offset_kev': app[u'OffsetRangeKev'][0],
        u'app.offset_steps': app[u'OffsetSteps'][0],
        u'app.nodes': app_nodes,
        u'ratio.nodes': pie_nodes / app_nodes if app_nodes else float(u'nan'),
        u'pie.knot_div': pie_knot,
        u'app.knot_div': app_knot,
    }


# -------------------------------------------------------------- проверки --

# (имя, якорь, выражение, [ключи ожиданий по группам])
CHECKS = [
    (u'модель/диапазоны сетки',
     u'перебираются сеткой (по умолчанию',
     u'по умолчанию \u00b1([0-9.]+) % \u00d7 \u00b1([0-9.]+) \u043a\u044dВ',
     [u'pie.gain_range_pct', u'pie.offset_kev']),

    (u'таблица ключей/усиление',
     u'| `--gain-range/--gain-steps` |',
     u'\\|\\s*([0-9.]+)\\s*/\\s*([0-9]+)\\s*\\|',
     [u'pie.gain_range', u'pie.gain_steps']),

    (u'таблица ключей/ноль шкалы',
     u'| `--offset-range/--offset-steps` |',
     u'\\|\\s*([0-9.]+)\\s*\u043a\u044dВ\\s*/\\s*([0-9]+)\\s*\\|',
     [u'pie.offset_kev', u'pie.offset_steps']),

    (u'скорость/размер сетки',
     u'Скорость: ',
     u'сетка дрейфа ([0-9]+)\u00d7([0-9]+)',
     [u'pie.gain_steps', u'pie.offset_steps']),

    (u'оговорка/строка pie',
     u'| **`pie`** (',
     u'\u00b1([0-9.]+) % \\(`GainRange = ([0-9.]+)`\\)\\s*\\|'
     u'\\s*\u00b1([0-9.]+) \u043a\u044dВ \\(`OffsetRangeKev = ([0-9.]+)`\\)\\s*\\|'
     u'\\s*\\*\\*([0-9]+)\\*\\* \u2014 сетка ([0-9]+)\u00d7([0-9]+) '
     u'\\(`GainSteps = ([0-9]+)`, `OffsetSteps = ([0-9]+)`\\)',
     [u'pie.gain_range_pct', u'pie.gain_range',
      u'pie.offset_kev', u'pie.offset_kev',
      u'pie.nodes', u'pie.gain_steps', u'pie.offset_steps',
      u'pie.gain_steps', u'pie.offset_steps']),

    (u'оговорка/строка приложения',
     u'| **приложение** (',
     u'\u00b1([0-9.]+) % \\(`GainRange = ([0-9.]+)`\\)\\s*\\|'
     u'\\s*\u00b1([0-9.]+) \u043a\u044dВ \\(`OffsetRangeKev = ([0-9.]+)`\\)\\s*\\|'
     u'\\s*\\*\\*([0-9]+)\\*\\* \u2014 сетка ([0-9]+)\u00d7([0-9]+) '
     u'\\(`GainSteps = ([0-9]+)`, `OffsetSteps = ([0-9]+)`\\)',
     [u'app.gain_range_pct', u'app.gain_range',
      u'app.offset_kev', u'app.offset_kev',
      u'app.nodes', u'app.gain_steps', u'app.offset_steps',
      u'app.gain_steps', u'app.offset_steps']),

    (u'оговорка/пол шага узлов континуума',
     u'\u0443 `pie` \u2014 \u0434\u0438\u0430\u043f\u0430\u0437\u043e\u043d/',
     u'\u0434\u0438\u0430\u043f\u0430\u0437\u043e\u043d/([0-9]+)',
     [u'pie.knot_div']),

    (u'оговорка/пол шага узлов у приложения',
     u'`FsaAnalyzer.ContinuumKnotDivisor`',
     u'\u0434\u0438\u0430\u043f\u0430\u0437\u043e\u043d/([0-9]+)',
     [u'app.knot_div']),

    (u'оговорка/сколько положений перебирает pie',
     u'перебирает',
     u'перебирает ([0-9]+) положени',
     [u'pie.nodes']),

    (u'оговорка/во сколько раз отличается свобода',
     u'отличается в',
     u'отличается в ([0-9]+) раз',
     [u'ratio.nodes']),
]

# Ключи ожиданий, у которых источник — приложение. Нужны только для отчёта:
# отказ должен называть, В КАКОЙ исходник смотреть.
APP_KEYS = set(k for k in [u'app.gain_range', u'app.gain_range_pct',
                           u'app.gain_steps', u'app.offset_kev',
                           u'app.offset_steps', u'app.nodes',
                           u'app.knot_div'])


def same(written, live):
    u"""Сличать ЧИСЛАМИ, не текстом: «3» и «3.0» — одно значение."""
    return abs(written - live) <= 1e-9 * max(1.0, abs(live))


def check(readme_lines, exp, readme_path):
    findings = []
    for name, anchor, expr, keys in CHECKS:
        hits = [(i + 1, line) for i, line in enumerate(readme_lines)
                if anchor in line]
        if not hits:
            findings.append(u'ЯКОРЬ НЕ НАЙДЕН  %-42s %s: нет строки с «%s»'
                            % (name, os.path.basename(readme_path), anchor))
            continue
        if len(hits) > 1:
            findings.append(
                u'ЯКОРЬ НЕ ОДИН    %-42s %s: строк с «%s» — %d (%s)'
                % (name, os.path.basename(readme_path), anchor, len(hits),
                   u', '.join(str(h[0]) for h in hits)))
            continue
        line_no, line = hits[0]
        match = re.search(expr, line)
        if match is None or len(match.groups()) != len(keys):
            findings.append(
                u'НЕ РАЗОБРАНО     %-42s %s:%d — чисел в строке не нашлось'
                % (name, os.path.basename(readme_path), line_no))
            continue
        for group, key in zip(match.groups(), keys):
            written = float(group)
            live = exp[key]
            if not same(written, live):
                if key == u'ratio.nodes':
                    src = u'Program.cs + FsaAnalyzer.cs'
                elif key in APP_KEYS:
                    src = u'FsaAnalyzer.cs'
                else:
                    src = u'Program.cs'
                findings.append(
                    u'ПРОТУХЛО         %-42s %s:%d — записано %s, '
                    u'в коде %s (%s, %s)'
                    % (name, os.path.basename(readme_path), line_no,
                       group, format_num(live), key, src))
    return findings


def format_num(value):
    if abs(value - round(value)) < 1e-9:
        return u'%d' % int(round(value))
    return (u'%g' % value)


# ------------------------------------------------------------------ ход --

def run(readme_path, pie_path, app_path, quiet=False):
    u"""Вернуть (код возврата, строки отчёта)."""
    out = []
    for path in (readme_path, pie_path, app_path):
        if not os.path.isfile(path):
            return 1, [u'НЕТ ФАЙЛА: %s' % path]

    pie, pie_problems = parse_defaults(read_lines(pie_path), pie_pattern,
                                       u'tools/pie/Program.cs')
    app, app_problems = parse_defaults(read_lines(app_path), app_pattern,
                                       u'FullSpectrumAnalysis/FsaAnalyzer.cs')
    pie_lines = read_lines(pie_path)
    app_lines = read_lines(app_path)
    pie_knot, pie_knot_problems = parse_knot_floor(
        pie_lines, PIE_KNOT_RX, u'tools/pie/Program.cs')
    app_knot, app_knot_problems = parse_knot_floor(
        app_lines, APP_KNOT_RX, u'FullSpectrumAnalysis/FsaAnalyzer.cs')
    problems = (pie_problems + app_problems
                + pie_knot_problems + app_knot_problems)
    if problems:
        out.append(u'РАЗБОР ИСХОДНИКА НЕ СОСТОЯЛСЯ — молчать нельзя:')
        for line in problems:
            out.append(u'  * ' + line)
        return 1, out

    exp = expectations(pie, app, pie_knot, app_knot)
    shape = (u'%-22s: \u00b1%s %% (GainRange = %s) \u00d7 \u00b1%s \u043a\u044dВ, '
             u'сетка %s\u00d7%s, узлов %s')
    out.append(shape % (u'умолчания pie',
                        format_num(exp[u'pie.gain_range_pct']),
                        format_num(exp[u'pie.gain_range']),
                        format_num(exp[u'pie.offset_kev']),
                        format_num(exp[u'pie.gain_steps']),
                        format_num(exp[u'pie.offset_steps']),
                        format_num(exp[u'pie.nodes'])))
    out.append(shape % (u'умолчания приложения',
                        format_num(exp[u'app.gain_range_pct']),
                        format_num(exp[u'app.gain_range']),
                        format_num(exp[u'app.offset_kev']),
                        format_num(exp[u'app.gain_steps']),
                        format_num(exp[u'app.offset_steps']),
                        format_num(exp[u'app.nodes'])))
    out.append(u'свобода подгонки шкалы отличается в %s раз'
               % format_num(exp[u'ratio.nodes']))
    out.append(u'пол шага узлов континуума: pie диапазон/%s, '
               u'приложение диапазон/%s'
               % (format_num(exp[u'pie.knot_div']),
                  format_num(exp[u'app.knot_div'])))

    findings = check(read_lines(readme_path), exp, readme_path)
    if not findings:
        out.append(u'СОШЛОСЬ: все %d проверок README отвечают исходникам.'
                   % len(CHECKS))
        return 0, out

    out.append(u'')
    out.append(u'ОТКАЗ, находок %d:' % len(findings))
    for line in findings:
        out.append(u'  ' + line)
    out.append(u'')
    out.append(u'Лечение: править README по исходнику, а не наоборот. '
               u'Умолчания `pie` не двигать (решение Amber 05.09.2026, `T71`).')
    return 1, out


# ----------------------------------------------------- положительный контроль --

def spoil(src, dst, old, new):
    u"""Скопировать файл и подменить в копии кусок текста. Копия — нарочно:
    живые файлы порче не подлежат."""
    with io.open(src, u'r', encoding=u'utf-8-sig', newline=u'') as handle:
        text = handle.read()
    if text.count(old) != 1:
        raise RuntimeError(u'порча не однозначна: «%s» встречается %d раз'
                           % (old, text.count(old)))
    bom = u'\ufeff' if _has_bom(src) else u''
    with io.open(dst, u'w', encoding=u'utf-8', newline=u'') as handle:
        handle.write(bom + text.replace(old, new))
    return dst


def _has_bom(path):
    with io.open(path, u'rb') as handle:
        return handle.read(3) == b'\xef\xbb\xbf'


def self_test(readme_path, pie_path, app_path):
    u"""Три плеча. Первое — чистое дерево, два других — подброшенный плохой
    вход. ⚠ Сторож без плохого входа выглядит работающим всегда: код 0,
    находок 0."""
    lines = []
    ok = True

    lines.append(u'=== ПЛЕЧО 1: чистое дерево — ждём код 0 ===')
    code, report = run(readme_path, pie_path, app_path)
    lines.extend(report)
    lines.append(u'код возврата: %d — %s' % (code, u'ОЖИДАЕМО' if code == 0
                                             else u'ПЛЕЧО ПРОВАЛЕНО'))
    ok = ok and code == 0

    tmp = tempfile.mkdtemp(prefix=u'pie_grid_note_')
    try:
        lines.append(u'')
        lines.append(u'=== ПЛЕЧО 2: порча в КОПИИ README '
                     u'(«сетка дрейфа 9×9» → «9×7») — ждём код 1 ===')
        bad_readme = spoil(readme_path, os.path.join(tmp, u'README.md'),
                           u'сетка дрейфа 9\u00d79', u'сетка дрейфа 9\u00d77')
        code, report = run(bad_readme, pie_path, app_path)
        lines.extend(report)
        named = any(u'скорость/размер сетки' in r and u'ПРОТУХЛО' in r
                    for r in report)
        lines.append(u'код возврата: %d, порча названа поимённо: %s — %s'
                     % (code, u'да' if named else u'НЕТ',
                        u'ОЖИДАЕМО' if (code == 1 and named)
                        else u'ПЛЕЧО ПРОВАЛЕНО'))
        ok = ok and code == 1 and named

        lines.append(u'')
        lines.append(u'=== ПЛЕЧО 3: порча в КОПИИ Program.cs '
                     u'(OffsetRangeKev 3.0 → 4.0) — ждём код 1 ===')
        bad_pie = spoil(pie_path, os.path.join(tmp, u'Program.cs'),
                        u'public double OffsetRangeKev = 3.0;',
                        u'public double OffsetRangeKev = 4.0;')
        code, report = run(readme_path, bad_pie, app_path)
        lines.extend(report)
        named = any(u'ПРОТУХЛО' in r and u'оговорка/строка pie' in r
                    for r in report)
        lines.append(u'код возврата: %d, порча названа поимённо: %s — %s'
                     % (code, u'да' if named else u'НЕТ',
                        u'ОЖИДАЕМО' if (code == 1 and named)
                        else u'ПЛЕЧО ПРОВАЛЕНО'))
        ok = ok and code == 1 and named
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    lines.append(u'')
    lines.append(u'САМОПРОВЕРКА %s' % (u'ПРОШЛА: сторож ловит подброшенный '
                                       u'плохой вход с обеих сторон.' if ok
                                       else u'НЕ ПРОШЛА.'))
    return (0 if ok else 1), lines


# ---------------------------------------------------------------- запуск --

def main(argv):
    flags = [a for a in argv[1:] if a.startswith(u'--')]
    readme_path, pie_path, app_path = (DEFAULT_README, DEFAULT_PIE_SRC,
                                       DEFAULT_APP_SRC)
    do_self_test = False
    for flag in flags:
        if flag == u'--self-test':
            do_self_test = True
        elif flag.startswith(u'--readme='):
            readme_path = flag[len(u'--readme='):]
        elif flag.startswith(u'--pie-src='):
            pie_path = flag[len(u'--pie-src='):]
        elif flag.startswith(u'--app-src='):
            app_path = flag[len(u'--app-src='):]
        else:
            print(u'НЕИЗВЕСТНЫЙ КЛЮЧ: %s' % flag)
            return 1

    if do_self_test:
        code, report = self_test(readme_path, pie_path, app_path)
    else:
        code, report = run(readme_path, pie_path, app_path)
    for line in report:
        print(line)
    return code


if __name__ == u'__main__':
    if hasattr(sys.stdout, u'reconfigure'):
        try:
            sys.stdout.reconfigure(encoding=u'utf-8')
        except Exception:
            pass
    sys.exit(main(sys.argv))
