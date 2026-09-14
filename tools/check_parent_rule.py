#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""СТОРОЖ ПРАВИЛА «РОДИТЕЛЬ» (`D48`): копии зажима по уровню и согласие трёх носителей.

Что такое «родитель», в проекте решает ОДНО правило —
`BecquerelMonitor/FullSpectrumAnalysis/DecayParentRule.cs` (`LevelClause` для
`decay_radiations`, `ChainLevelClause` для `decay_chain`; ~~`S89`~~, ~~`S94`~~,
~~`D39`~~). Копии правила уже расходились (`T78` — пять рукописных в
`decay_chain`), и ничто не мешало следующему написать шестую — молча. Заведено
12.09.2026 полосой П19 по строке `D48`.

Две проверки.

**1. Рукописные копии правила в коде.** Всякая строка кода (не комментарий), где
внутри строкового литерала `parent_l_seqno` ЗАЖИМАЕТСЯ — сравнением
(`= < > <= >= <> in between`) или `min(...)` — либо где для `decay_chain`
берётся `min(l_seqno)`, есть кандидат в копию. Не копия — если в трёх строках
рядом стоит ссылка на само правило (`DecayParentRule.LevelClause` /
`ChainLevelClause`, `LEVEL_CLAUSE` / `CHAIN_LEVEL_CLAUSE`): это применение, а
не переписывание. Остальное разрешено только поимённо, с причиной и ОЖИДАЕМЫМ
числом вхождений (`ALLOWED`): лишнее вхождение в разрешённом файле — тоже
находка, иначе список стал бы глушилкой. Список разрешённых печатается каждый
прогон.

**2. Согласие трёх носителей номера уровня родителя** (только чтение,
`mode=ro`): `nucdb.decay_radiations.parent_l_seqno`,
`nucdb.gamma_coincidence_parent.isomer`/`l_seqno` и
`schemedb.ensdf_datasets.parent_l_seqno` (заведена ~~`D38`~~). Опора одна —
`nucdb.nuclides` (имя изомера ↔ `l_seqno`):

  2а. `gamma_coincidence_parent`: `isomer = 0` ⇒ `l_seqno = 0`; `isomer > 0` с
      заполненным `l_seqno` ⇒ в `nuclides` есть строка `<nucid><тег Sandia>` с
      этим `l_seqno` (так и привязывали, ~~`D11`~~);
  2б. `ensdf_datasets.parent_l_seqno` не пуст ⇒ в `nuclides` есть строка того же
      ядра (`parent_nucid` с любым суффиксом изомера) с этим `l_seqno`, и её
      период сходится с `parent_hl_sec` в 1 % (правило разметки
      `mark_isomer_datasets.py`);
  2в. крест: полное имя, к которому привёл носитель (2а или 2б), если имеет
      строки в `decay_radiations`, обязано получать от `LevelClause` (текст
      правила читается из .cs, как в `tools/CORPUS/scripts/chains.py`) РОВНО
      этот уровень — иначе два носителя зовут родителем разные состояния.

⚠ Что сторож НЕ судит: верность физики (`check_isomer_levels.py` держит
известные особенности поставки — `144TBm`, запасная ветвь у `123CSm2`, четыре
двухуровневых родителя), правило разметки `ensdf_datasets` целиком
(`check_db_marks.py`) — это их проверки, вторых копий здесь нет.

    python tools/check_parent_rule.py [--root <дерево>] [--nucdb …] [--schemedb …]
                                      [--rule <DecayParentRule.cs>] [--quiet]

Коды возврата: 0 — копий нет и носители согласны; 1 — хоть одна находка;
2 — нет файла базы/правила.
"""
import argparse
import io
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

RULE_CS = os.path.join('BecquerelMonitor', 'FullSpectrumAnalysis', 'DecayParentRule.cs')

#: Где искать копии: приложение и вся оснастка (пробы, скрипты).
SCAN_DIRS = ['BecquerelMonitor', 'tools']
SCAN_EXT = ('.cs', '.py', '.ps1')
SKIP_DIR = re.compile(r'^(bin|obj|packages|__pycache__|build[^/\\]*|wd_[^/\\]*|_corpus_raw|out_[^/\\]*|\.git)$')

#: Зажим `parent_l_seqno` внутри литерала: сравнение, `min(...)`, `in`/`between`.
LEVEL_CLAMP = re.compile(
    r'parent_l_seqno\s*(=|<>|!=|<=|>=|<|>)'
    r'|min\s*\(\s*(?:\w+\.)?parent_l_seqno'
    r'|parent_l_seqno\s+(?:not\s+)?(?:in|between)\b', re.I)
#: Зажим ряда: минимум `l_seqno` по `decay_chain`.
CHAIN_CLAMP = re.compile(
    r'min\s*\(\s*(?:\w+\.)?l_seqno\s*\)\s*from\s+decay_chain'
    r'|l_seqno\s*=\s*\(\s*select\s+min\s*\(\s*(?:\w+\.)?l_seqno', re.I)
#: Применение правила рядом — не копия.
RULE_REF = re.compile(r'DecayParentRule\s*\.\s*(LevelClause|ChainLevelClause)'
                      r'|\b(CHAIN_)?LEVEL_CLAUSE\b')
NEAR = 3

#: Разрешённые вхождения: файл -> (ожидаемое число, причина). Больше ожидаемого
#: — находка. ⛔ Запись сюда — только с причиной, почему это НЕ второе
#: соглашение о родителе.
ALLOWED = {
    RULE_CS: (4, u'само правило — единственный носитель текста (три строки LevelClause, одна ChainLevelClause)'),
    os.path.join('tools', 'check_parent_rule.py'):
        (0, u'этот сторож: образцы в его тексте — не SQL'),
    os.path.join('tools', 'CORPUS', 'scripts', 'chains.py'):
        (1, u'`fallbacks()`: диагностика «кому достался чужой уровень» тем же `exists`, '
            u'что у правила; текст правила читается из .cs (`_level_clause`)'),
    os.path.join('tools', 'effmaker', 'probes', 'DecayReadersProbe.cs'):
        (1, u'`Fallbacks()`: та же диагностика в пробе, сверяющей обоих читателей (`S89`)'),
    os.path.join('tools', 'nucdb', 'check_isomer_levels.py'):
        (1, u'сторож ищет текст запасной ветви правила — проверка, что она жива'),
    os.path.join('tools', 'effmaker', 'probes', 'ChainRuleProbeF58.cs'):
        (1, u'мера снятого варианта B (`perc not null` внутри минимума) — плечо «было», копией нарочно, как и RetiredClauseC'),
}


def rel(path, root):
    return os.path.relpath(path, root).replace('\\', '/')


def walk(root):
    for base in SCAN_DIRS:
        top = os.path.join(root, base)
        if not os.path.isdir(top):
            continue
        for d, dirs, files in os.walk(top):
            dirs[:] = sorted(x for x in dirs if not SKIP_DIR.match(x))
            for f in sorted(files):
                if f.lower().endswith(SCAN_EXT):
                    yield os.path.join(d, f)


def code_lines(path):
    u"""Строки файла без строк-комментариев (`//`, `///`, `#`) и без пустых."""
    try:
        with open(path, 'rb') as h:
            text = h.read().decode('utf-8-sig', 'replace')
    except OSError:
        return []
    out = []
    for i, line in enumerate(text.splitlines(), 1):
        s = line.strip()
        if not s or s.startswith('//') or s.startswith('#'):
            out.append((i, u''))
            continue
        out.append((i, line))
    return out


def find_copies(root):
    u"""[(файл, строка, текст)] — кандидаты, не покрытые ссылкой на правило."""
    hits = []
    for path in walk(root):
        lines = code_lines(path)
        for k, (no, line) in enumerate(lines):
            if not line:
                continue
            # SQL живёт в литерале: без кавычки на строке это документация
            if '"' not in line and "'" not in line:
                continue
            if not (LEVEL_CLAMP.search(line) or CHAIN_CLAMP.search(line)):
                continue
            window = u' '.join(l for _n, l in lines[max(0, k - NEAR):k + NEAR + 1])
            if RULE_REF.search(window):
                continue
            hits.append((rel(path, root), no, line.strip()))
    return hits


def judge_copies(root):
    hits = find_copies(root)
    by_file = {}
    for f, no, line in hits:
        by_file.setdefault(f, []).append((no, line))
    bad = []
    print(u'=== 1. КОПИИ ПРАВИЛА «РОДИТЕЛЬ» В КОДЕ ===')
    for f, (n_exp, why) in sorted(ALLOWED.items()):
        f_rel = f.replace('\\', '/')
        got = len(by_file.get(f_rel, []))
        print(u'  разрешён %-52s вхождений %d (ожидалось %d) — %s' % (f_rel, got, n_exp, why))
        if got > n_exp:
            for no, line in by_file[f_rel]:
                print(u'    :%d %s' % (no, line[:110]))
            bad.append((f_rel, u'вхождений %d при разрешённых %d' % (got, n_exp)))
        if got < n_exp:
            print(u'    ⚠ вхождений МЕНЬШЕ ожидаемого — правило или диагностика переписаны; '
                  u'ожидание в `ALLOWED` пересмотреть')
    for f_rel in sorted(by_file):
        if f_rel in {k.replace('\\', '/') for k in ALLOWED}:
            continue
        for no, line in by_file[f_rel]:
            print(u'⛔ %s:%d  %s' % (f_rel, no, line[:120]))
            bad.append((f_rel, u':%d рукописный зажим по уровню родителя' % no))
    print(u'  файлов просмотрено: %d; копий вне правила: %d'
          % (sum(1 for _ in walk(root)), len(bad)))
    return bad


# ---------------------------------------------------------------------------
# 2. три носителя
# ---------------------------------------------------------------------------
SANDIA = re.compile(r'^([A-Za-z]{1,2})(\d{1,3})(m\d?)$')
ISOMER_TAIL = re.compile(r'^(\d+[A-Z]+?)(m\d*)?$')
HL_TOL = 0.01


def ro(path):
    return sqlite3.connect('file:%s?mode=ro' % path.replace(os.sep, '/'), uri=True)


def level_clause(rule_path):
    u"""Текст `LevelClause` из .cs — тем же способом, что `chains._level_clause`."""
    with io.open(rule_path, encoding='utf-8-sig') as h:
        text = h.read()
    text = re.sub(r'//[^\n]*', '', text)
    body = re.search(r'const\s+string\s+LevelClause\s*=(.*?);', text, re.S)
    if not body:
        raise RuntimeError(u'в %s нет объявления LevelClause' % rule_path)
    clause = u''.join(re.findall(r'"((?:[^"\\]|\\.)*)"', body.group(1)))
    for must in ('parent_l_seqno', 'coalesce', 'nuclides', '$n'):
        if must not in clause:
            raise RuntimeError(u'LevelClause разобран неправдоподобно: нет %r' % must)
    return clause


def chosen_level(nuc, clause, name):
    row = nuc.execute('select parent_l_seqno from decay_radiations where parent_nucid = $n'
                      + clause + ' limit 1', {'n': name}).fetchone()
    return None if row is None else int(row[0])


def judge_carriers(nucdb, schemedb, rule_path, quiet):
    print(u'')
    print(u'=== 2. ТРИ НОСИТЕЛЯ НОМЕРА УРОВНЯ РОДИТЕЛЯ ===')
    bad = []
    nuc = ro(nucdb)
    sch = ro(schemedb)
    clause = level_clause(rule_path)

    names = {}          # полное имя -> l_seqno (у `144TBm` три строки — все)
    by_base = {}        # база -> [(полное имя, l_seqno, hl_sec)]
    for nucid, seq, hl in nuc.execute('select nucid, l_seqno, half_life_sec from nuclides'):
        names.setdefault(nucid, set()).add(seq)
        m = ISOMER_TAIL.match(nucid or '')
        if m:
            by_base.setdefault(m.group(1), []).append((nucid, seq, hl))
    has_rad = set(r[0] for r in nuc.execute('select distinct parent_nucid from decay_radiations'))

    # 2а
    n_zero = n_iso = 0
    resolved = []       # (носитель, полное имя, уровень)
    for pid, sym, nucid, iso, seq in nuc.execute(
            'select id, sandia_symbol, nucid, isomer, l_seqno from gamma_coincidence_parent'
            ' where nucid is not null and l_seqno is not null'):
        if iso == 0:
            n_zero += 1
            if seq != 0:
                bad.append((u'2а', u'%s (id %d): isomer 0, а l_seqno %s' % (sym, pid, seq)))
            else:
                resolved.append((u'gamma_coincidence_parent', nucid, 0))
            continue
        n_iso += 1
        m = SANDIA.match(sym or '')
        full = (u'%s%s%s' % (m.group(2), m.group(1).upper(), m.group(3))) if m else None
        if not full or seq not in names.get(full, set()):
            bad.append((u'2а', u'%s (id %d): isomer %d, l_seqno %s — в nuclides нет %s с этим уровнем'
                        % (sym, pid, iso, seq, full or u'(имя не разобрано)')))
        else:
            resolved.append((u'gamma_coincidence_parent', full, seq))
    print(u'  2а gamma_coincidence_parent: основных %d (l_seqno = 0 у всех: %s), изомеров с '
          u'привязкой %d, расхождений %d'
          % (n_zero, u'да' if not any(b[0] == u'2а' and 'isomer 0' in b[1] for b in bad) else u'НЕТ',
             n_iso, sum(1 for b in bad if b[0] == u'2а')))

    # 2б
    n_ds = 0
    for did, base, seq, hl in sch.execute(
            'select id, parent_nucid, parent_l_seqno, parent_hl_sec from ensdf_datasets'
            ' where parent_l_seqno is not null'):
        n_ds += 1
        cands = [(full, s, h) for full, s, h in by_base.get(base or '', []) if s == seq]
        if not cands:
            bad.append((u'2б', u'ensdf_datasets id %d: %s уровень %s — в nuclides нет такой строки'
                        % (did, base, seq)))
            continue
        ok = [full for full, s, h in cands
              if h and hl and abs(h - hl) <= HL_TOL * hl]
        if not ok:
            bad.append((u'2б', u'ensdf_datasets id %d: %s уровень %s — период %s не сходится с %s'
                        % (did, base, seq, [h for _f, _s, h in cands], hl)))
            continue
        resolved.append((u'ensdf_datasets', ok[0], seq))
    print(u'  2б ensdf_datasets.parent_l_seqno: заполнено %d, расхождений с nuclides/периодом %d'
          % (n_ds, sum(1 for b in bad if b[0] == u'2б')))

    # 2в
    seen = set()
    n_cross = n_absent = 0
    for carrier, full, seq in resolved:
        key = (full, seq)
        if key in seen:
            continue
        seen.add(key)
        if full not in has_rad:
            n_absent += 1
            continue
        n_cross += 1
        got = chosen_level(nuc, clause, full)
        if got != seq:
            bad.append((u'2в', u'%s: %s зовёт уровень %s, правило LevelClause даёт %s'
                        % (carrier, full, seq, got)))
    print(u'  2в крест с decay_radiations: имён проверено %d (без строк излучений %d), '
          u'расхождений с LevelClause %d'
          % (n_cross, n_absent, sum(1 for b in bad if b[0] == u'2в')))

    if not quiet:
        for where, what in bad:
            print(u'⛔ %s %s' % (where, what))
    nuc.close()
    sch.close()
    return bad


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--root', default=ROOT)
    ap.add_argument('--nucdb', default=None)
    ap.add_argument('--schemedb', default=None)
    ap.add_argument('--rule', default=None)
    ap.add_argument('--quiet', action='store_true')
    a = ap.parse_args(argv)
    root = os.path.abspath(a.root)
    nucdb = a.nucdb or os.path.join(root, 'BecquerelMonitor', 'nucdb.sqlite')
    schemedb = a.schemedb or os.path.join(root, 'BecquerelMonitor', 'schemedb.sqlite')
    rule = a.rule or os.path.join(root, RULE_CS)
    for p in (nucdb, schemedb, rule):
        if not os.path.isfile(p):
            print(u'ОТКАЗ: нет файла %s' % p)
            return 2

    bad1 = judge_copies(root)
    try:
        bad2 = judge_carriers(nucdb, schemedb, rule, a.quiet)
    except (sqlite3.Error, RuntimeError) as ex:
        print(u'ОТКАЗ: базы не прочитаны — %s' % ex)
        return 2

    print(u'')
    print(u'=== сводка ===')
    print(u'  копий правила вне DecayParentRule: %d' % len(bad1))
    print(u'  расхождений между носителями: %d' % len(bad2))
    if bad1 or bad2:
        print(u'ОСТАНОВ: о том, что такое «родитель», в дереве больше одного соглашения '
              u'либо носители зовут разные уровни.')
        return 1
    print(u'ПРАВИЛО «РОДИТЕЛЬ» ОДНО: копий нет, три носителя согласны с nuclides и LevelClause.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
