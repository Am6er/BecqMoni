#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""`N16`: сравнение плотностей веществ — Geant4 против наших. ТОЛЬКО ПЕЧАТЬ.

Решение Amber 09.08.2026 — старшинство плотностей `Geant4` → `наши` → `ЛСРМ`
(`database/scheme.md` §0а). До 12.09.2026 правило было описанием без
исполнителя: плотностей Geant4 в базе нет, потребители берут своё из
`matdb.star_materials` (NIST ESTAR) и из засева редактора геометрии
(`BecquerelMonitor/EfficiencyMaker/GeometryMaterialLibrary.cs`, `Seed()`).
Этот скрипт — ЧИТАТЕЛЬ правила: сводит три источника ПО СОСТАВУ (а не по
имени) и печатает расхождения. ⛔ В базу НЕ пишет и читателей приложения НЕ
трогает: что делать с расхождениями — решение Amber (`N16`).

Источники:

  * Geant4 — `G4NistMaterialBuilder.cc` из поставки исходников рядом с деревом
    (`../GEANT4/geant4-v*/source/materials/src/`), таблица NIST внутри Geant4:
    `AddMaterial("G4_…", плотность, Z, I, n, …)` + `AddElementByAtomCount` /
    `AddElementByWeightFraction`; атомные счётчики переводятся в массовые доли
    атомными весами `matdb.xcom_elements`.
  * `matdb.star_materials` (+ `star_material_composition`) — `mode=ro`.
  * засев `GeometryMaterialLibrary.Seed()` — разбор текста .cs: `add(abbr, name,
    "формула", плотность, вид)` и `fill("", name, плотность, "Z:доля …")`.

Сведение: одинаковое множество элементов и массовые доли в пределах `--tol`
(умолчание 0.01), И одна фаза (газ — плотность < 0.05 г/см³ — сводится только с
газом: у воды и пара, этилена и полиэтилена состав один). У элементов (Z > 0 в
Geant4) состав — {Z: 1}. Если по составу подходит несколько веществ Geant4
(полиэтилен / парафин / полипропилен / циклогексан — все CH₂), берутся те, чьё
имя делит с нашим хотя бы одно слово; нет таких — все, с пометкой
«неоднозначно».

    python tools/nucdb/compare_densities.py [--g4=<G4NistMaterialBuilder.cc>]
        [--tol=0.01] [--csv=<файл>] [--all]

Печатает: таблицу «наше вещество — вещество Geant4 — плотности — Δ %», сводку
по разрядам (совпало / ≤ 1 % / 1…10 % / > 10 %), список наших веществ без пары
в Geant4 (по составу) и список пар seed↔star с расходящейся плотностью.
`--all` печатает и совпавшие строки. Код возврата: 0 — прочитано всё; 2 —
источник не найден.
"""
import argparse
import csv
import glob
import io
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
MATDB = os.path.join(REPO, 'BecquerelMonitor', 'matdb.sqlite')
SEED_CS = os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', 'GeometryMaterialLibrary.cs')

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def find_g4():
    u"""Поставка исходников Geant4 рядом с деревом (`../GEANT4/geant4-v*`)."""
    pats = [os.path.join(os.path.dirname(REPO), 'GEANT4', 'geant4-v*', 'source',
                         'materials', 'src', 'G4NistMaterialBuilder.cc')]
    for pat in pats:
        hits = sorted(glob.glob(pat))
        if hits:
            return hits[-1]
    return None


def ro(path):
    return sqlite3.connect('file:%s?mode=ro' % path.replace(os.sep, '/'), uri=True)


def elements(c):
    u"""Z -> (символ, атомный вес) и символ -> Z из `xcom_elements`."""
    by_z, by_sym = {}, {}
    for z, aw, sym in c.execute('select z, atomic_weight, symbol from xcom_elements'):
        # написание в базе разное (Li/LI, Ti/TI, Ni/NI — как и у приложения,
        # `MaterialDatabase.CanonicalSymbol`): приводится к каноническому
        sym = (sym or '').strip().capitalize()
        by_z[int(z)] = (sym, float(aw))
        if sym:
            by_sym[sym] = int(z)
    return by_z, by_sym


def normalise(fr):
    tot = sum(fr.values())
    return {z: w / tot for z, w in fr.items()} if tot > 0 else {}


def atoms_to_mass(counts, by_z):
    masses = {z: n * by_z[z][1] for z, n in counts.items()}
    return normalise(masses)


# ---------------------------------------------------------------------------
# Geant4
# ---------------------------------------------------------------------------
RE_ADD = re.compile(r'AddMaterial\(\s*"([^"]+)"\s*,\s*([-+0-9.eE]+)\s*,\s*(\d+)\s*,')
RE_ATOM = re.compile(r'AddElementByAtomCount\(\s*(?:"([A-Za-z]+)"|(\d+))\s*,\s*(\d+)\s*\)')
RE_WEIGHT = re.compile(r'AddElementByWeightFraction\(\s*(?:"([A-Za-z]+)"|(\d+))\s*,\s*([-+0-9.eE]+)\s*\)')


def read_g4(path, by_z, by_sym):
    u"""[(имя, плотность, {Z: массовая доля}, способ)] из .cc; строки-вызовы
    внутри функций-определений (`void …::AddMaterial(`) не трогаются — у них
    нет строкового первого довода."""
    out = []
    cur = None
    with io.open(path, encoding='utf-8', errors='replace') as h:
        for line in h:
            s = line.strip()
            if s.startswith('//'):
                continue
            m = RE_ADD.search(s)
            if m:
                if cur is not None:
                    out.append(cur)
                name, dens, z = m.group(1), float(m.group(2)), int(m.group(3))
                cur = dict(name=name, density=dens, atoms={}, weights={}, z=z)
                continue
            if cur is None:
                continue
            m = RE_ATOM.search(s)
            if m:
                z = by_sym.get(m.group(1)) if m.group(1) else int(m.group(2))
                if z:
                    cur['atoms'][z] = cur['atoms'].get(z, 0) + int(m.group(3))
                continue
            m = RE_WEIGHT.search(s)
            if m:
                z = by_sym.get(m.group(1)) if m.group(1) else int(m.group(2))
                if z:
                    cur['weights'][z] = cur['weights'].get(z, 0.0) + float(m.group(3))
    if cur is not None:
        out.append(cur)
    result = []
    for m in out:
        if m['z'] > 0 and not m['atoms'] and not m['weights']:
            fr, how = {m['z']: 1.0}, 'element'
        elif m['atoms'] and not m['weights']:
            fr, how = atoms_to_mass(m['atoms'], by_z), 'atoms'
        elif m['weights'] and not m['atoms']:
            fr, how = normalise(m['weights']), 'weights'
        else:
            continue          # универсальные/пустые определения (Galactic и т.п.)
        if not fr or not (m['density'] > 0):
            continue
        result.append((m['name'], m['density'], fr, how))
    return result


# ---------------------------------------------------------------------------
# наши: star_materials и засев редактора
# ---------------------------------------------------------------------------
def read_star(c):
    rows = {}
    for mid, name, dens in c.execute('select id, name, density_g_cm3 from star_materials'):
        rows[int(mid)] = dict(name=name, density=float(dens), fr={})
    for mid, z, w in c.execute('select material_id, z, weight_fraction from star_material_composition'):
        if int(mid) in rows:
            rows[int(mid)]['fr'][int(z)] = float(w)
    out = []
    for mid in sorted(rows):
        r = rows[mid]
        if r['fr']:
            out.append(('star', r['name'], r['density'], normalise(r['fr'])))
    return out


RE_SEED_ADD = re.compile(r'add\("([^"]*)",\s*"([^"]+)",\s*"([^"]*)",\s*([0-9.]+),\s*MaterialKind\.(\w+)\)')
RE_SEED_FILL = re.compile(r'fill\("[^"]*",\s*"([^"]+)",\s*([0-9.]+),\s*((?:"[^"]*"\s*\+?\s*)+)\)', re.S)
RE_FORMULA = re.compile(r'([A-Z][a-z]?)(\d+)')


def read_seed(path, by_z, by_sym):
    text = io.open(path, encoding='utf-8-sig').read()
    # только тело Seed(): от `public static List<Entry> Seed()` до следующего метода
    start = text.find('public static List<Entry> Seed()')
    body = text[start:] if start >= 0 else text
    out = []
    for m in RE_SEED_ADD.finditer(body):
        abbr, name, formula, dens, kind = m.groups()
        counts = {}
        for sym, n in RE_FORMULA.findall(formula):
            z = by_sym.get(sym)
            if z:
                counts[z] = counts.get(z, 0) + int(n)
        if counts:
            out.append(('seed:' + kind, name, float(dens), atoms_to_mass(counts, by_z)))
    for m in RE_SEED_FILL.finditer(body):
        name, dens, packed = m.group(1), float(m.group(2)), m.group(3)
        joined = ' '.join(re.findall(r'"([^"]*)"', packed))
        fr = {}
        for part in joined.split():
            if ':' in part:
                z, w = part.split(':', 1)
                fr[int(z)] = float(w)
        if fr:
            out.append(('seed:Source', name, dens, normalise(fr)))
    return out


# ---------------------------------------------------------------------------
# сведение
# ---------------------------------------------------------------------------
def same_composition(a, b, tol):
    if set(a) != set(b):
        return False
    return max(abs(a[z] - b[z]) for z in a) <= tol


def sym_of(fr, by_z):
    return ' '.join('%s%.3f' % (by_z[z][0], w) for z, w in sorted(fr.items(), key=lambda x: -x[1]))


GAS_DENSITY = 0.05


def is_gas(density):
    return density < GAS_DENSITY


def name_tokens(name):
    u"""Слова имени без служебного: «G4_PLASTIC_SC_VINYLTOLUENE» -> {PLASTIC, SC,
    VINYLTOLUENE}; «lPROPANE» (жидкость у Geant4) -> {PROPANE}."""
    n = name
    if n.startswith('G4_'):
        n = n[3:]
    if len(n) > 1 and n[0] == 'l' and n[1].isupper():
        n = n[1:]
    n = re.sub(r'[_(),/-]+', ' ', n).upper()
    return set(t for t in n.split() if len(t) >= 3)


def grade(delta_pct):
    a = abs(delta_pct)
    if a < 0.05:
        return u'совпало'
    if a <= 1.0:
        return u'≤ 1 %'
    if a <= 10.0:
        return u'1…10 %'
    return u'> 10 %'


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--g4', default=None, help=u'путь к G4NistMaterialBuilder.cc')
    ap.add_argument('--tol', type=float, default=0.01, help=u'допуск по массовой доле')
    ap.add_argument('--csv', default=None, help=u'куда записать таблицу пар')
    ap.add_argument('--all', action='store_true', help=u'печатать и совпавшие строки')
    a = ap.parse_args(argv)

    g4_path = a.g4 or find_g4()
    if not g4_path or not os.path.isfile(g4_path):
        print(u'ОТКАЗ: не найден G4NistMaterialBuilder.cc (ожидался в ../GEANT4/geant4-v*/'
              u'source/materials/src/, или дайте --g4=)')
        return 2
    if not os.path.isfile(MATDB) or not os.path.isfile(SEED_CS):
        print(u'ОТКАЗ: нет %s или %s' % (MATDB, SEED_CS))
        return 2

    c = ro(MATDB)
    by_z, by_sym = elements(c)
    g4 = read_g4(g4_path, by_z, by_sym)
    ours = read_star(c) + read_seed(SEED_CS, by_z, by_sym)
    c.close()

    print(u'Geant4: %s — веществ с составом %d (элементов %d, по атомам %d, по долям %d)'
          % (g4_path, len(g4), sum(1 for m in g4 if m[3] == 'element'),
             sum(1 for m in g4 if m[3] == 'atoms'), sum(1 for m in g4 if m[3] == 'weights')))
    print(u'наши: star_materials %d (mode=ro), засев редактора %d; допуск по доле %.3f'
          % (sum(1 for o in ours if o[0] == 'star'),
             sum(1 for o in ours if o[0].startswith('seed')), a.tol))
    print(u'')

    pairs, orphans = [], []
    for src, name, dens, fr in ours:
        hits = [m for m in g4 if same_composition(fr, m[2], a.tol)
                and is_gas(m[1]) == is_gas(dens)]
        if not hits:
            orphans.append((src, name, dens, fr))
            continue
        ambiguous = u''
        if len(hits) > 1:
            mine = name_tokens(name)
            exact = [m for m in hits if name_tokens(m[0]) == mine]
            named = exact or [m for m in hits if name_tokens(m[0]) & mine]
            if named:
                hits = named
            else:
                ambiguous = u' (неоднозначно: состав общий у %d веществ Geant4)' % len(hits)
        for gname, gdens, _gfr, how in hits:
            delta = 100.0 * (dens - gdens) / gdens
            pairs.append(dict(source=src, ours=name, ours_density=dens, g4=gname,
                              g4_density=gdens, delta_pct=delta,
                              grade=grade(delta) + ambiguous,
                              composition=sym_of(fr, by_z), g4_how=how))

    hdr = u'%-12s %-42s %9s  %-30s %9s %8s  %s' % (u'источник', u'наше вещество', u'ρ наша',
                                                  u'Geant4', u'ρ G4', u'Δ %', u'разряд')
    print(u'=== РАСХОЖДЕНИЯ ПЛОТНОСТЕЙ (наша − Geant4)/Geant4, сведено ПО СОСТАВУ ===')
    print(hdr)
    print(u'-' * len(hdr))
    shown = 0
    for p in sorted(pairs, key=lambda p: -abs(p['delta_pct'])):
        if not a.all and p['grade'].startswith(u'совпало'):
            continue
        shown += 1
        print(u'%-12s %-42s %9.4g  %-30s %9.4g %+8.2f  %s'
              % (p['source'], p['ours'][:42], p['ours_density'], p['g4'][:30],
                 p['g4_density'], p['delta_pct'], p['grade']))
    if not shown:
        print(u'  (расхождений нет)')

    print(u'')
    print(u'=== СВОДКА ПО РАЗРЯДАМ (пар наше↔Geant4: %d) ===' % len(pairs))
    for g in (u'совпало', u'≤ 1 %', u'1…10 %', u'> 10 %'):
        n_star = sum(1 for p in pairs if p['grade'].startswith(g) and p['source'] == 'star')
        n_seed = sum(1 for p in pairs if p['grade'].startswith(g) and p['source'] != 'star')
        print(u'  %-8s star %3d   засев %3d' % (g, n_star, n_seed))
    n_amb = sum(1 for p in pairs if u'неоднозначно' in p['grade'])
    print(u'  из них неоднозначных по составу (одно имя не выбрать): %d' % n_amb)

    print(u'')
    print(u'=== НАШИ БЕЗ ПАРЫ В GEANT4 ПО СОСТАВУ (%d) ===' % len(orphans))
    for src, name, dens, fr in orphans:
        print(u'  %-12s %-42s %9.4g  %s' % (src, name[:42], dens, sym_of(fr, by_z)[:60]))

    # засев против star: одно вещество по составу — две наши плотности
    print(u'')
    print(u'=== ЗАСЕВ ↔ star_materials: тот же состав, плотности расходятся ===')
    seeds = [o for o in ours if o[0].startswith('seed')]
    stars = [o for o in ours if o[0] == 'star']
    n = 0
    for src, name, dens, fr in seeds:
        for _s, sname, sdens, sfr in stars:
            if (same_composition(fr, sfr, a.tol) and is_gas(dens) == is_gas(sdens)
                    and (name_tokens(name) & name_tokens(sname))
                    and abs(dens - sdens) / sdens > 0.0005):
                n += 1
                print(u'  %-12s %-32s %8.4g   star %-40s %8.4g  Δ %+.1f %%'
                      % (src, name[:32], dens, sname[:40], sdens, 100.0 * (dens - sdens) / sdens))
    if not n:
        print(u'  (расхождений нет)')

    if a.csv:
        with io.open(a.csv, 'w', encoding='utf-8', newline='') as h:
            w = csv.DictWriter(h, fieldnames=['source', 'ours', 'ours_density', 'g4', 'g4_density',
                                              'delta_pct', 'grade', 'composition', 'g4_how'])
            w.writeheader()
            for p in sorted(pairs, key=lambda p: -abs(p['delta_pct'])):
                row = dict(p)
                row['delta_pct'] = '%.3f' % p['delta_pct']
                w.writerow(row)
        print(u'')
        print(u'таблица пар: %s' % a.csv)
    print(u'')
    print(u'⛔ база не тронута: скрипт только читает (mode=ro) и печатает; запись — решение Amber (N16).')
    return 0


if __name__ == '__main__':
    sys.exit(main())
