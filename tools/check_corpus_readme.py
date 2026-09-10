#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож журнала корпуса (`T113`): СНЯТОЕ не подаётся как ДЕЙСТВУЮЩЕЕ.

Зачем. `tools/CORPUS/README.md` — лабораторный журнал, и его читают как
источник чисел. Журнал ведётся хронологически: разделы прошлых заходов
остаются в нём навсегда, а база корпуса за это время сменилась двенадцать
раз. Число, снятое вместе со своей базой, внешне неотличимо от
действующего — те же χ², те же проценты, та же уверенная подача.

Класс дефекта доказан не рассуждением, а находками: до 31.08.2026 в журнале
нашлись ДВА раздела, подававших снятое как действующее («Что сверка НАШЛА:
близкие точечные источники», числа сняты 23.08.2026 матрицей отклика, и
«Что осталось у `B17`», три кандидата, все с тех пор сняты). ⛔ Оба найдены
СЛУЧАЙНО, работой мимо, а не поиском — значит доля таких мест была
неизвестна. Осмотр глазами тут уже провалился однажды; отсюда сторож.

Чем судит. ДВА НЕЗАВИСИМЫХ ИСТОЧНИКА, своей копии чисел сторож НЕ ХРАНИТ:

  что журнал УТВЕРЖДАЕТ  <-  сам текст `tools/CORPUS/README.md`
  что в дереве ЕСТЬ      <-  `corpus/manifest.csv`, `corpus/detectors.csv`
                             и таблица раздела «ДЕЙСТВУЮЩАЯ БАЗА» того же
                             журнала (единственное место, где база названа)

Третьего места со списком снятых баз здесь нет НАРОЧНО: перечень, живущий
отдельно от источника, протухает сам (`T127`: 14 номеров из 24 разошлись за
считанные дни). Действующие базы берутся из строк ТАБЛИЦЫ объявления, все
прочие имена `out_*` в документе — снятые ПО ПОСТРОЕНИЮ.

ТРИ ПРАВИЛА.

  R1. «действующ… база `X`» обязана называть ДЕЙСТВУЮЩУЮ базу.
      Ищется слово «действующ…» и первое имя `out_*` в пределах 100 знаков
      ВПЕРЁД. Прошедшее время («числа которого БЫЛИ объявлены действующей
      базой») сторож не трогает: там имя стоит ДО оборота.
      Ловит ровно то, чем дефект и опасен: фраза настоящего времени,
      написанная год назад и с тех пор не тронутая.

  R2. Раздел, стоящий на СНЯТОЙ базе, обязан нести ПОМЕТКУ о снятии.
      Раздел «стоит на базе», если имя `out_*` есть в его заголовке или
      рядом со словом «база» в первых `HEAD_LINES` собственных строках.
      Пометка — слова снятия («Прежняя база», «СНЯТА», «не цитировать»,
      «НЕСРАВНИМ», «устарел…») в заголовке ЛЮБОГО предка или в тех же
      первых строках тела. Пометка ниже по тексту не считается: читателя
      защищает только то, что он видит вместе с числом.
      Раздел объявления действующей базы и его подразделы — исключение:
      он и есть источник, и плечи сравнения в нём законны.

  R3. Шапка документа (собственный текст `#`-заголовка до первого `##`)
      описывает корпус СЕГОДНЯШНИЙ. Её числа сверяются с деревом:
      спектров и групп — счётом строк `manifest.csv` / `detectors.csv`,
      края ПШПВ, каналов и отсчётов — минимумом и максимумом тех же
      таблиц. Абзац написан в КАНОНИЧЕСКОЙ форме нарочно, чтобы его
      разбирал не человек: форма описана в `INTRO_RULES` ниже.

Запуск:

  python tools/check_corpus_readme.py              приговор, коды ниже
  python tools/check_corpus_readme.py --audit      сплошной разбор: КАЖДОЕ
                                                   число документа с
                                                   приговором (доказательство
                                                   охвата, ~4.5 тыс. строк)
  python tools/check_corpus_readme.py --self-test  положительный контроль

Положительный контроль (`--self-test`) обязателен и делается подлогом: в
КОПИЮ текста в памяти подкладывается заведомо снятое — фраза «на действующей
базе `out_v2`», раздел на снятой базе без пометки и завышенное число
спектров в шапке, — и сторож обязан ОТКАЗАТЬ на каждом из трёх, а на чистом
тексте дать 0. Сторож, у которого не показано, что он умеет отказывать, не
сторож (`experiment-without-positive-control`).

Коды возврата:
  0 — нарушений нет;
  1 — есть (каждое названо строкой, правилом и тем, что вместо чего);
  2 — сторожу нечем судить (нет журнала, нет таблиц корпуса, нет
      объявления действующей базы).

Печать держится в пределах cp1251: консоль здесь cp1251, и знак вне неё
превращается в «?», а код возврата этого не ловит вовсе.
"""

import argparse
import csv
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOC = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
MANIFEST = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'manifest.csv')
DETECTORS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'detectors.csv')
PARTS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'parts.csv')

# Сколько СОБСТВЕННЫХ строк раздела считается «рядом с заголовком»: пометка,
# стоящая дальше, читателя таблицы уже не защищает.
HEAD_LINES = 14

NUM = re.compile(r'(?<![\w.])[0-9]+(?:[.,][0-9]+)?(?:[eE][-+]?[0-9]+)?')
BASE = re.compile(r'out_[A-Za-z0-9_]+')
DATE = re.compile(r'\b[0-3][0-9]\.[01][0-9]\.20[0-9][0-9]\b')
MARK = re.compile(u'Прежняя база|Прежняя шапка|СНЯТА|СНЯТЫ|СНЯТОЕ|СНЯТ\\b'
                  u'|СНЯТЫХ|СНЯТОЙ|Снятая|снятая база|снятой базе'
                  u'|не цитировать|НЕ цитировать|НЕСРАВНИМ'
                  u'|устарел|УСТАРЕЛ|устарело')
ACTING = re.compile(u'действующ', re.IGNORECASE)

# R4: состав частей корпуса. Числа стоят В ДВУХ местах журнала — таблице
# «Как разделён» и строке описи `corpus/spectra/`, — и оба места ОПИСАТЕЛЬНЫЕ:
# ни даты, ни имени базы рядом, то есть читаются как нынешние. До 10.09.2026
# первое говорило 13/48/8 (раздел 09.08.2026), второе «126: 17/101/8», при
# 84/39/8 в `corpus/parts.csv`. Форма обеих записей — разбираемая машиной.
PARTS_RULES = [
    (u'таблица «Как разделён», known', re.compile(r'`known`[^|\n]*\|\s*\*\*([0-9]+)\*\*'), 'known'),
    (u'таблица «Как разделён», unknown', re.compile(r'`unknown`[^|\n]*\|\s*\*\*([0-9]+)\*\*'), 'unknown'),
    (u'таблица «Как разделён», excluded', re.compile(r'`excluded`[^|\n]*\|\s*\*\*([0-9]+)\*\*'), 'excluded'),
    (u'опись `corpus/spectra/`, всего', re.compile(u'([0-9]+) спектр[а-я]* ?: ?[0-9]+ понятн'), 'spectra'),
    (u'опись `corpus/spectra/`, понятных', re.compile(u'[0-9]+ спектр[а-я]* ?: ?([0-9]+) понятн'), 'known'),
    (u'опись `corpus/spectra/`, непонятных', re.compile(u'понятн[а-я]*, ([0-9]+) непонятн'), 'unknown'),
    (u'опись `corpus/spectra/`, германиевых', re.compile(u'непонятн[а-я]*, ([0-9]+) германиев'), 'excluded'),
]

# R3: что в шапке с чем сверяется. (имя, регулярное выражение, чем мерить)
INTRO_RULES = [
    (u'спектров', re.compile(r'\*\*([0-9]+) спектр'), 'spectra'),
    (u'групп детекторов', re.compile(r'([0-9]+) групп'), 'groups'),
    (u'ПШПВ снизу, %', re.compile(r'\(([0-9.]+) % ПШПВ'), 'fwhm_lo'),
    (u'ПШПВ сверху, %', re.compile(r'до[^(]{1,40}\(([0-9.]+) %\)'), 'fwhm_hi'),
    (u'каналов снизу', re.compile(r'от ([0-9]+) до [0-9]+ каналов'), 'ch_lo'),
    (u'каналов сверху', re.compile(r'от [0-9]+ до ([0-9]+) каналов'), 'ch_hi'),
    (u'отсчётов снизу, М', re.compile(u'от ([0-9.]+) М до'), 'cnt_lo'),
    (u'отсчётов сверху, М', re.compile(u'до ([0-9.]+) М отсчётов'), 'cnt_hi'),
]


def out(text):
    line = text if isinstance(text, type(u'')) else text.decode('utf-8')
    try:
        sys.stdout.write(line + u'\n')
    except UnicodeEncodeError:
        sys.stdout.write(line.encode('cp1251', 'replace').decode('cp1251') + u'\n')


def read_doc(path):
    with io.open(path, 'r', encoding='utf-8-sig', newline='') as fh:
        return fh.read().replace('\r\n', '\n').split('\n')


def read_tree_facts():
    u"""Счётные факты корпуса — из таблиц, а не из журнала."""
    with io.open(MANIFEST, 'r', encoding='utf-8-sig', newline='') as fh:
        man = list(csv.DictReader(fh))
    with io.open(DETECTORS, 'r', encoding='utf-8-sig', newline='') as fh:
        det = list(csv.DictReader(fh))
    with io.open(PARTS, 'r', encoding='utf-8-sig', newline='') as fh:
        parts = list(csv.DictReader(fh))
    counts = [float(r['counts']) for r in man if r.get('counts')]
    chans = [int(r['channels']) for r in man if r.get('channels')]
    fw = [float(r['fwhm_662_pct']) for r in man if r.get('fwhm_662_pct')]
    by_part = {}
    for r in parts:
        by_part[r['part']] = by_part.get(r['part'], 0) + 1
    return dict(spectra=len(man), groups=len(det),
                known=by_part.get('known', 0), unknown=by_part.get('unknown', 0),
                excluded=by_part.get('excluded', 0),
                fwhm_lo=min(fw), fwhm_hi=max(fw),
                ch_lo=min(chans), ch_hi=max(chans),
                cnt_lo=min(counts) / 1e6, cnt_hi=max(counts) / 1e6)


def parse_structure(lines):
    u"""Заголовки вне заборов кода, собственные границы, цепочки предков."""
    in_code = [False] * (len(lines) + 2)
    fence = False
    for i, ln in enumerate(lines, 1):
        if ln.lstrip().startswith('```'):
            fence = not fence
            in_code[i] = True
            continue
        in_code[i] = fence

    heads = []
    for i, ln in enumerate(lines, 1):
        if in_code[i]:
            continue
        m = re.match(r'^(#{1,6})\s+(.*)$', ln)
        if m:
            heads.append((i, len(m.group(1)), m.group(2)))

    head_lines = [h[0] for h in heads]
    own_end = {}
    for k, ln in enumerate(head_lines):
        own_end[ln] = (head_lines[k + 1] - 1) if k + 1 < len(head_lines) else len(lines)

    chain_at = [()] * (len(lines) + 2)
    stack = []
    hi = 0
    for i in range(1, len(lines) + 1):
        while hi < len(heads) and heads[hi][0] == i:
            h = heads[hi]
            while stack and stack[-1][1] >= h[1]:
                stack.pop()
            stack.append(h)
            hi += 1
        chain_at[i] = tuple(stack)
    return heads, own_end, chain_at, in_code


def current_bases(lines, heads, own_end):
    u"""Действующие базы — ТОЛЬКО из строк таблицы раздела объявления."""
    cur = set()
    decl = []
    for h in heads:
        if u'ДЕЙСТВУЮЩАЯ БАЗА' in h[2]:
            decl.append(h)
            for i in range(h[0], own_end[h[0]] + 1):
                if lines[i - 1].startswith('|'):
                    cur |= set(BASE.findall(lines[i - 1]))
    return cur, decl


def rule_r1(lines, cur):
    u"""«действующ…» + первое имя базы в 100 знаках вперёд."""
    bad = []
    text = u'\n'.join(lines)
    starts = [0]
    for ln in lines[:-1]:
        starts.append(starts[-1] + len(ln) + 1)

    def lineno(pos):
        lo, hi = 0, len(starts) - 1
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if starts[mid] <= pos:
                lo = mid
            else:
                hi = mid - 1
        return lo + 1

    for m in ACTING.finditer(text):
        window = text[m.end():m.end() + 100]
        bm = BASE.search(window)
        if not bm:
            continue
        name = bm.group(0)
        if name in cur:
            continue
        i = lineno(m.start())
        bad.append((i, name, lines[i - 1].strip()[:120]))
    return bad


def rule_r2(lines, heads, own_end, chain_at, cur, decl):
    u"""Раздел на снятой базе без пометки о снятии."""
    decl_lines = set()
    for h in decl:
        # сам раздел объявления и все его подразделы (по цепочке предков)
        for i in range(h[0], len(lines) + 1):
            ch = chain_at[i]
            if not ch or h not in ch:
                if i > h[0] and (not ch or h not in ch):
                    break
            decl_lines.add(i)

    bad = []
    for h in heads:
        if h[0] in decl_lines:
            continue
        own = lines[h[0] - 1:own_end[h[0]]]
        near = own[:HEAD_LINES]
        near_txt = u'\n'.join(near)
        # база, на которой стоит раздел: имя в заголовке или рядом со словом «база»
        names = set(BASE.findall(h[2]))
        for m in BASE.finditer(near_txt):
            around = near_txt[max(0, m.start() - 60):m.end() + 60]
            if u'баз' in around.lower():
                names.add(m.group(0))
        retired = names - cur
        if not retired:
            continue
        # есть ли числа, которые эта база могла бы испортить
        if not any(NUM.search(x) for x in own):
            continue
        chain = chain_at[h[0]]
        marked = any(MARK.search(c[2]) for c in chain) or bool(MARK.search(near_txt))
        if not marked:
            bad.append((h[0], h[2][:90], u','.join(sorted(retired))))
    return bad


def rule_r3(lines, heads, own_end, facts):
    u"""Шапка документа против дерева."""
    if not heads:
        return [(0, u'шапка', u'заголовков нет вовсе')]
    h1 = heads[0]
    intro = u'\n'.join(lines[h1[0] - 1:own_end[h1[0]]])
    bad = []
    for name, rx, key in INTRO_RULES:
        m = rx.search(intro)
        if not m:
            bad.append((h1[0], name, u'в шапке не найдено (форма абзаца нарушена)'))
            continue
        got = float(m.group(1).replace(',', '.'))
        want = facts[key]
        tol = max(abs(want) * 0.02, 1e-9)
        if abs(got - want) > tol:
            bad.append((h1[0], name, u'в шапке %s, в дереве %.6g' % (m.group(1), want)))
    return bad


def rule_r4(lines, facts):
    u"""Состав частей корпуса, названный в описательных местах, — против parts.csv."""
    text = u'\n'.join(lines)
    bad = []
    for name, rx, key in PARTS_RULES:
        m = rx.search(text)
        if not m:
            bad.append((0, name, u'в журнале не найдено (форма записи нарушена)'))
            continue
        got = int(m.group(1))
        want = facts[key]
        if got != want:
            i = text[:m.start()].count(u'\n') + 1
            bad.append((i, name, u'в журнале %d, в дереве %d' % (got, want)))
    return bad


def checked_numbers(lines, heads, own_end):
    u"""(строка, значение) чисел, которые правила R3/R4 РЕАЛЬНО сверяют с
    деревом. Нужно сплошному разбору: без этого числа шапки и таблицы частей
    попали бы в «не проверяемо», хотя их-то сторож и держит."""
    got = set()
    text = u'\n'.join(lines)
    for _, rx, _ in PARTS_RULES:
        m = rx.search(text)
        if m:
            got.add((text[:m.start(1)].count(u'\n') + 1, m.group(1)))
    if heads:
        h1 = heads[0]
        base = h1[0] - 1
        intro = u'\n'.join(lines[base:own_end[h1[0]]])
        for _, rx, _ in INTRO_RULES:
            m = rx.search(intro)
            if m:
                got.add((base + intro[:m.start(1)].count(u'\n') + 1, m.group(1)))
    return got


def audit(lines, heads, own_end, chain_at, cur, decl, in_code):
    u"""Сплошной разбор: КАЖДОЕ число документа с приговором.

    ⚠ Имя `out_*` ВНУТРИ забора кода базой не считается: там это путь в
    команде (`-Out …\\tools\\pie\\out_app`), а не утверждение о базе. Иначе
    всякий пример запуска пометил бы свой раздел снятым.
    """
    decl_head_lines = set(h[0] for h in decl)
    rows = []
    own_bases = {}
    for h in heads:
        body = u'\n'.join(lines[i - 1] for i in range(h[0], own_end[h[0]] + 1)
                          if not in_code[i])
        own_bases[h[0]] = set(BASE.findall(body))
    guarded = checked_numbers(lines, heads, own_end)
    for i, ln in enumerate(lines, 1):
        for m in NUM.finditer(ln):
            if (i, m.group(0)) in guarded:
                rows.append((i, m.group(0), chain_at[i][-1][2][:60] if chain_at[i]
                             else u'преамбула', u'ДЕЙСТВУЮЩЕЕ',
                             u'сверено с деревом правилом R3/R4'))
                continue
            ch = chain_at[i]
            if not ch:
                rows.append((i, m.group(0), u'преамбула', u'не проверяемо',
                             u'до первого заголовка'))
                continue
            own = ch[-1]
            in_decl = any(c[0] in decl_head_lines for c in ch)
            bases = set(own_bases[own[0]]) | set(BASE.findall(u' '.join(c[2] for c in ch)))
            bases |= set(BASE.findall(ln))
            retired = bases - cur
            marked = any(MARK.search(c[2]) for c in ch) or \
                MARK.search(u'\n'.join(lines[own[0] - 1:own[0] - 1 + HEAD_LINES]))
            dated = any(DATE.search(c[2]) or re.search(r'\b20[12][0-9]\b', c[2])
                        for c in ch)
            if in_decl:
                v, why = u'ДЕЙСТВУЮЩЕЕ', u'раздел объявления действующей базы'
            elif bases & cur and not retired:
                v, why = u'ДЕЙСТВУЮЩЕЕ', u'раздел стоит на действующей базе'
            elif retired and marked:
                v, why = u'СНЯТОЕ', u'снятая база %s, пометка есть' % u','.join(sorted(retired))
            elif retired:
                v, why = u'СНЯТОЕ, БЕЗ ПОМЕТКИ', u'снятая база %s' % u','.join(sorted(retired))
            elif dated:
                v, why = u'не проверяемо', u'датированный замер того дня'
            else:
                v, why = u'не проверяемо', u'ни базы, ни даты'
            rows.append((i, m.group(0), own[2][:60], v, why))
    return rows


def run(lines, facts, quiet=False):
    heads, own_end, chain_at, _ = parse_structure(lines)
    cur, decl = current_bases(lines, heads, own_end)
    if not cur:
        if not quiet:
            out(u'ОТКАЗ: в журнале нет раздела «ДЕЙСТВУЮЩАЯ БАЗА» с таблицей — судить нечем')
        return 2, None
    r1 = rule_r1(lines, cur)
    r2 = rule_r2(lines, heads, own_end, chain_at, cur, decl)
    r3 = rule_r3(lines, heads, own_end, facts)
    r4 = rule_r4(lines, facts)
    if not quiet:
        out(u'действующие базы (из таблицы объявления): %s' % u', '.join(sorted(cur)))
        out(u'R1 «действующая база X» с чужим X: %d' % len(r1))
        for i, name, txt in r1:
            out(u'   :%-5d `%s` действующей НЕ является — %s' % (i, name, txt))
        out(u'R2 раздел на снятой базе без пометки: %d' % len(r2))
        for i, title, names in r2:
            out(u'   :%-5d [%s] %s' % (i, names, title))
        out(u'R3 шапка против дерева: %d' % len(r3))
        for i, name, txt in r3:
            out(u'   :%-5d %s: %s' % (i, name, txt))
        out(u'R4 состав частей против parts.csv: %d' % len(r4))
        for i, name, txt in r4:
            out(u'   :%-5d %s: %s' % (i, name, txt))
    return (1 if (r1 or r2 or r3 or r4) else 0), (r1, r2, r3, r4)


def self_test(lines, facts):
    u"""Положительный контроль: подлог в КОПИЮ текста, сторож обязан отказать."""
    ok = True
    code, _ = run(lines, facts, quiet=True)
    out(u'  чистый текст: код %d (ждём 0) — %s' % (code, u'ДА' if code == 0 else u'НЕТ'))
    ok = ok and code == 0

    heads, own_end, _, _ = parse_structure(lines)

    # (1) R1: подложить фразу настоящего времени со снятой базой
    a = list(lines)
    a.insert(own_end[heads[0][0]],
             u'На действующей базе `out_v2` понятная часть даёт 692.7 / 3.25.')
    code, res = run(a, facts, quiet=True)
    hit = bool(res and res[0])
    out(u'  подлог R1 («действующая база `out_v2`»): код %d, попаданий %d (ждём 1+) — %s'
        % (code, len(res[0]) if res else 0, u'ДА' if code == 1 and hit else u'НЕТ'))
    ok = ok and code == 1 and hit

    # (2) R2: раздел на снятой базе без пометки
    at = own_end[heads[0][0]]
    fake_bad = [u'', u'## Замер на базе `out_v5`', u'',
                u'Понятная часть 535.0, медиана 3.05.', u'']
    b = lines[:at] + fake_bad + lines[at:]
    code, res = run(b, facts, quiet=True)
    hit = any(u'out_v5' in x[2] for x in res[1]) if res else False
    out(u'  подлог R2 (раздел на `out_v5` без пометки): код %d, попаданий %d (ждём 1+) — %s'
        % (code, len(res[1]) if res else 0, u'ДА' if code == 1 and hit else u'НЕТ'))
    ok = ok and code == 1 and hit

    # (2б) отрицательный контроль к R2: тот же раздел, но С пометкой — молчит
    fake_ok = [u'', u'## Прежняя база (СНЯТА): `out_v5`', u'',
               u'Понятная часть 535.0, медиана 3.05.', u'']
    c = lines[:at] + fake_ok + lines[at:]
    code, res = run(c, facts, quiet=True)
    out(u'  отрицательный контроль R2 (тот же раздел С пометкой): код %d (ждём 0) — %s'
        % (code, u'ДА' if code == 0 else u'НЕТ'))
    ok = ok and code == 0

    # (3) R3: завысить число спектров в шапке
    d = []
    done = False
    for ln in lines:
        if not done and re.search(r'\*\*([0-9]+) спектр', ln):
            ln = re.sub(r'\*\*([0-9]+) спектр', u'**999 спектр', ln)
            done = True
        d.append(ln)
    code, res = run(d, facts, quiet=True)
    hit = bool(res and res[2])
    out(u'  подлог R3 (в шапке 999 спектров): код %d, попаданий %d (ждём 1+) — %s'
        % (code, len(res[2]) if res else 0, u'ДА' if code == 1 and hit else u'НЕТ'))
    ok = ok and code == 1 and hit

    # (4) R4: сдвинуть состав понятной части в таблице «Как разделён»
    e = []
    done = False
    for ln in lines:
        if not done and re.search(r'`known`[^|\n]*\|\s*\*\*([0-9]+)\*\*', ln):
            ln = re.sub(r'(`known`[^|\n]*\|\s*\*\*)([0-9]+)(\*\*)', r'\g<1>13\g<3>', ln)
            done = True
        e.append(ln)
    code, res = run(e, facts, quiet=True)
    hit = bool(res and res[3])
    out(u'  подлог R4 (в таблице частей known=13): код %d, попаданий %d (ждём 1+) — %s'
        % (code, len(res[3]) if res else 0, u'ДА' if code == 1 and hit else u'НЕТ'))
    ok = ok and code == 1 and hit
    return 0 if ok else 1


def main():
    p = argparse.ArgumentParser(add_help=True)
    p.add_argument('--audit', action='store_true',
                   help=u'сплошной разбор: каждое число с приговором')
    p.add_argument('--self-test', action='store_true',
                   help=u'положительный контроль подлогом')
    p.add_argument('--out', default=None, help=u'файл для --audit')
    args = p.parse_args()

    for path in (DOC, MANIFEST, DETECTORS):
        if not os.path.exists(path):
            out(u'ОТКАЗ: нет файла %s' % path)
            return 2

    lines = read_doc(DOC)
    facts = read_tree_facts()

    if args.self_test:
        out(u'=== положительный контроль сторожа журнала корпуса ===')
        return self_test(lines, facts)

    if args.audit:
        heads, own_end, chain_at, in_code = parse_structure(lines)
        cur, decl = current_bases(lines, heads, own_end)
        rows = audit(lines, heads, own_end, chain_at, cur, decl, in_code)
        tally = {}
        for r in rows:
            tally[r[3]] = tally.get(r[3], 0) + 1
        buf = [u'# сплошной разбор чисел tools/CORPUS/README.md',
               u'# строка\tчисло\tраздел\tприговор\tчем']
        for r in rows:
            buf.append(u'%d\t%s\t%s\t%s\t%s' % r)
        text = u'\n'.join(buf) + u'\n'
        if args.out:
            with io.open(args.out, 'w', encoding='utf-8', newline='\n') as fh:
                fh.write(text)
            out(u'разбор записан: %s' % args.out)
        else:
            out(text)
        out(u'ИТОГО чисел: %d' % len(rows))
        for k in sorted(tally, key=lambda x: -tally[x]):
            out(u'   %-24s %d' % (k, tally[k]))
        return 0

    out(u'=== сторож журнала корпуса (T113) ===')
    code, _ = run(lines, facts)
    if code == 0:
        out(u'СОШЛОСЬ: снятое подаётся снятым, шапка сходится с деревом')
    elif code == 1:
        out(u'ОСТАНОВ: журнал подаёт снятое как действующее (см. выше)')
    return code


if __name__ == '__main__':
    sys.exit(main())
