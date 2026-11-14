#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож отпечатка `matdb` в клейме матрицы и кривой (`S202`, П180 29.09.2026).

До физики 25 клеймо матрицы видело версию физики, ключи и геометрию, но не
справочные данные, которые читает перенос: П179 заменил в копии базы
`xray_fluorescence.kb_ev` — тело матрицы `G1S_point5` стало другим (форма L1
0.36 %) при ТОМ ЖЕ клейме, и склад молча остался «годным». Физика 25 вносит в
клеймо отпечаток СОДЕРЖИМОГО таблиц `matdb`, которые читает перенос
(`MaterialDatabase.SimulatorDataFingerprint`, список `SimulatorTables`).

Отпечаток защищает ровно столько, сколько таблиц в списке. Сторож ловит тихое
расхождение списка с кодом:

  1. таблица базы `matdb.sqlite`, имя которой стоит в КОДЕ (не в комментарии)
     читателей переноса `BecquerelMonitor/EfficiencyMaker/*.cs`, обязана стоять
     в `SimulatorTables`;
  2. каждая таблица списка обязана быть в базе (иначе отпечаток несёт метку
     отсутствия, и правка данных в ней невозможна — список устарел);
  3. клеймо матрицы (`ResponseMatrix.ComputeStamp`) и кривой
     (`EfficiencyCalculation`) обязаны нести отпечаток (`mdb=`);
  4. (`AMBER274` Э3, 14.11.2026) КОСВЕННОЕ ЧТЕНИЕ: таблица базы, имя которой
     стоит в коде файла ВНЕ `EfficiencyMaker/`, а класс этого файла зовёт код
     переноса, обязана стоять в `SimulatorTables` — или в `OUTSIDE_TRANSPORT`
     с причиной, почему её правка тело матрицы не меняет. Повод: источник QF
     альф слоя кристалла (`ScintAlphaQuenching`, таблица
     `scint_alpha_quenching`) зовёт `ResponseCrystalSection.RecipeFor`.

Запуск:
    python tools/check_matdb_fingerprint.py            # проверить дерево
    python tools/check_matdb_fingerprint.py --selftest # положительный контроль

Код возврата: 0 — согласовано; 1 — расхождение (или контроль провален).
"""
from __future__ import print_function
import io
import os
import re
import sqlite3
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
APP = os.path.join(ROOT, 'BecquerelMonitor')
EM = os.path.join(APP, 'EfficiencyMaker')


def read(path):
    with io.open(path, encoding='utf-8-sig') as f:
        return f.read()


def code_only(text):
    """Текст без комментариев `//` и `///` (строковые литералы с `//` у нас не встречаются в SQL)."""
    out = []
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith('//'):
            continue
        cut = line.find('//')
        if cut >= 0 and '"' not in line[cut:]:
            line = line[:cut]
        out.append(line)
    return '\n'.join(out)


def listed_tables(md_text):
    m = re.search(r'public static readonly string\[\] SimulatorTables\s*=\s*\{(.*?)\};', md_text, re.S)
    if not m:
        return None
    return re.findall(r'"([a-z_0-9]+)"', m.group(1))


def db_tables(db_path):
    con = sqlite3.connect('file:' + db_path + '?mode=ro', uri=True)
    try:
        return [r[0] for r in con.execute("select name from sqlite_master where type='table'")]
    finally:
        con.close()


def check(em_sources, md_text, stamp_texts, tables_in_db):
    problems = []
    listed = listed_tables(md_text)
    if listed is None:
        return [u'в MaterialDatabase.cs нет списка SimulatorTables']
    code = '\n'.join(code_only(t) for t in em_sources.values())
    for t in sorted(tables_in_db):
        if re.search(r'\b' + re.escape(t) + r'\b', code) and t not in listed:
            problems.append(u'таблицу `%s` читает код переноса, а в SimulatorTables её нет — '
                            u'её правка пройдёт мимо клейма' % t)
    for t in listed:
        if t not in tables_in_db:
            problems.append(u'таблицы `%s` из SimulatorTables нет в matdb.sqlite' % t)
    for name, text, needle in stamp_texts:
        if needle not in code_only(text):
            problems.append(u'%s: в клейме нет отпечатка (`%s`)' % (name, needle))
    return problems


#: таблицы matdb, которые читает код ВНЕ переноса, хотя его зовёт код переноса, — и почему их нет в
#: SimulatorTables (их правка тело матрицы не меняет); пусто в причине — отказ
OUTSIDE_TRANSPORT = {
    'scint_photon_light_curves': u'фотонные кривые света FSA (AMBER272): перенос не читает',
    'scint_photon_light_yield': u'то же',
    'scint_alpha_quenching': u'QF альф слоя кристалла (AMBER274 Э3): число — в клейме раздела (RecipeStamp :aq/:as)',
}


def outside_sources(app_dir):
    u"""Исходники приложения ВНЕ EfficiencyMaker/ (каталоги сборки bin/obj не берутся)."""
    out = {}
    em = os.path.normcase(os.path.abspath(EM))
    for root, dirs, files in os.walk(app_dir):
        dirs[:] = [d for d in dirs if d.lower() not in ('bin', 'obj')]
        if os.path.normcase(os.path.abspath(root)).startswith(em):
            continue
        for f in files:
            if f.endswith('.cs'):
                # часть старых исходников не в UTF-8 (знак © в cp1252): читаются с заменой — имён таблиц это не трогает
                with io.open(os.path.join(root, f), encoding='utf-8-sig', errors='replace') as src:
                    out[os.path.relpath(os.path.join(root, f), app_dir)] = src.read()
    return out


def indirect(outside, em_sources, md_text, tables_in_db, excused=None):
    u"""Таблица, имя которой стоит в коде файла ВНЕ EfficiencyMaker/, а класс этого файла зовёт код переноса."""
    excused = OUTSIDE_TRANSPORT if excused is None else excused
    listed = listed_tables(md_text) or []
    em_code = '\n'.join(code_only(t) for t in em_sources.values())
    problems = []
    for f in sorted(outside):
        code = code_only(outside[f])
        named = [t for t in tables_in_db if re.search(r'\b' + re.escape(t) + r'\b', code)]
        if not named:
            continue
        # верхние классы файла: вложенные (`Law`, `Entry`) именем в коде переноса ничего не значат
        classes = re.findall(r'^\s*(?:public|internal)?\s*(?:static\s+|sealed\s+|abstract\s+|partial\s+)*(?:class|struct)\s+([A-Za-z_]\w*)',
                             code, re.M)
        indent = lambda name: min(len(l) - len(l.lstrip()) for l in code.splitlines()
                                  if re.search(r'\b(?:class|struct)\s+' + re.escape(name) + r'\b', l))
        top = min([indent(c) for c in classes]) if classes else 0
        callers = [c for c in classes if indent(c) == top and re.search(r'\b' + re.escape(c) + r'\s*\.', em_code)]
        if not callers:
            continue
        for t in named:
            if t not in listed and not excused.get(t):
                problems.append(u'таблицу `%s` читает %s (%s), а его зовёт код переноса — в SimulatorTables её нет'
                                u' и причины в OUTSIDE_TRANSPORT нет' % (t, f, ', '.join(callers)))
    return problems


def tree_inputs():
    sources = {}
    for f in sorted(os.listdir(EM)):
        if f.endswith('.cs'):
            sources[f] = read(os.path.join(EM, f))
    md = sources['MaterialDatabase.cs']
    stamps = [(u'ResponseMatrix.ComputeStamp', sources['ResponseMatrix.cs'],
               'sb.Append("mdb=").Append(MaterialDatabase.SimulatorDataFingerprint())'),
              (u'EfficiencyCalculation (клеймо кривой)', sources['EfficiencyCalculation.cs'],
               '"; mdb=" + MaterialDatabase.SimulatorDataFingerprint()')]
    return sources, md, stamps, db_tables(os.path.join(APP, 'matdb.sqlite'))


def selftest():
    sources, md, stamps, tables = tree_inputs()
    ok = True
    # К1: дерево как есть — зелено
    base = check(sources, md, stamps, tables)
    print(u'К1 дерево как есть: %s' % (u'зелено' if not base else u'КРАСНО: ' + '; '.join(base)))
    ok &= not base
    # К2: таблица выброшена из списка — обязан покраснеть
    md2 = md.replace('"xray_fluorescence", ', '').replace('"xray_fluorescence"', '')
    r2 = check(sources, md2, stamps, tables)
    hit = any('xray_fluorescence' in p for p in r2)
    print(u'К2 xray_fluorescence выброшена из списка: %s' % (u'поймано' if hit else u'НЕ ПОЙМАНО'))
    ok &= hit
    # К3: отпечаток выброшен из клейма матрицы — обязан покраснеть
    s3 = [(n, t.replace('MaterialDatabase.SimulatorDataFingerprint()', '"0"'), k) for n, t, k in stamps]
    r3 = check(sources, md, s3, tables)
    print(u'К3 отпечаток выброшен из клейм: %s' % (u'поймано' if len(r3) == 2 else u'НЕ ПОЙМАНО'))
    ok &= len(r3) == 2
    # К4: упоминание таблицы только в комментарии — не читатель
    src4 = dict(sources)
    src4['X.cs'] = u'// select * from icc_coefficients\n/// icc_coefficients\n'
    r4 = check(src4, md, stamps, tables)
    print(u'К4 таблица только в комментарии: %s' % (u'не считается' if not r4 else u'ЛОЖНАЯ НАХОДКА'))
    ok &= not r4
    # К5: таблица в коде — считается
    src5 = dict(sources)
    src5['X.cs'] = u'cmd.CommandText = "select * from icc_coefficients";\n'
    r5 = check(src5, md, stamps, tables)
    print(u'К5 таблица в коде вне списка: %s' % (u'поймано' if r5 else u'НЕ ПОЙМАНО'))
    ok &= bool(r5)
    # К6: косвенное чтение — таблицу читает класс ВНЕ EfficiencyMaker/, а его зовёт код переноса
    outside = outside_sources(APP)
    r6 = indirect(outside, sources, md, tables)
    print(u'К6а дерево как есть, косвенное чтение: %s' % (u'зелено' if not r6 else u'КРАСНО: ' + '; '.join(r6)))
    ok &= not r6
    out6 = dict(outside)
    out6['Q.cs'] = u'namespace N\n{\n    public static class Q\n    {\n        static string T = "select * from icc_coefficients";\n    }\n}\n'
    src6 = dict(sources)
    src6['X.cs'] = u'var v = Q.Read();\n'
    r6b = indirect(out6, src6, md, tables)
    print(u'К6б класс вне переноса читает таблицу вне списка, перенос его зовёт: %s'
          % (u'поймано' if any('icc_coefficients' in p_ for p_ in r6b) else u'НЕ ПОЙМАНО'))
    ok &= any('icc_coefficients' in p_ for p_ in r6b)
    r6c = indirect(out6, sources, md, tables)
    print(u'К6в тот же класс, перенос его НЕ зовёт: %s' % (u'не считается' if not r6c else u'ЛОЖНАЯ НАХОДКА'))
    ok &= not r6c
    # К6г: источник QF альф зовёт код переноса — без причины в OUTSIDE_TRANSPORT обязан покраснеть
    bare = dict(OUTSIDE_TRANSPORT)
    bare.pop('scint_alpha_quenching', None)
    r6d = indirect(outside, sources, md, tables, bare)
    hit6 = any('scint_alpha_quenching' in p_ for p_ in r6d)
    print(u'К6г scint_alpha_quenching без причины в OUTSIDE_TRANSPORT: %s' % (u'поймано' if hit6 else u'НЕ ПОЙМАНО'))
    ok &= hit6
    return 0 if ok else 1


def main(argv):
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    if '--selftest' in argv:
        return selftest()
    sources, md, stamps, tables = tree_inputs()
    problems = check(sources, md, stamps, tables)
    problems += indirect(outside_sources(APP), sources, md, tables)
    listed = listed_tables(md) or []
    print(u'отпечаток matdb (S202): таблиц в SimulatorTables %d, в базе %d' % (len(listed), len(tables)))
    for p in problems:
        print(u'  ⛔ ' + p)
    if not problems:
        print(u'  согласовано: всё, что читает код переноса, входит в отпечаток; оба клейма его несут;'
              u' косвенное чтение — в списке или с причиной (%d)' % len(OUTSIDE_TRANSPORT))
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
