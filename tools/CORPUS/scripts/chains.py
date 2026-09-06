# -*- coding: utf-8 -*-
"""Decay chains from nucdb.sqlite.

Same tables NucBaseFramework uses (decay_chain / decay_radiations / nuclides),
but with two corrections that matter for a library fit:

  * of each (nucid, daughter_nucid, dec_type) triple only the row at the LOWEST
    l_seqno present is followed - the higher ones describe decays of excited
    levels and duplicate the transition with different branching (212BI has
    35.94% and 67% rows for 208TL). ⚠ NOT the same as "l_seqno = 0", and the
    difference is not academic: pinning to zero would drop 576 rows of 4101 and
    109 parents outright, 238U among them (2 branches against 1);
  * cumulative branching from the chain root is accumulated, so intensities are
    per decay of the CHAIN PARENT (secular equilibrium). That is what the BR
    coupling in LibraryPeakFitter needs: 208TL lines must carry the 0.3594
    factor of the 212BI branch, otherwise a bound group mixing 208TL and 212BI
    lines gets wrong weights.
"""
import io
import sqlite3
import re
import os
import sys

# База ищется относительно дерева решения, а не по абсолютному пути: раньше
# здесь стоял путь на машине автора, и у постороннего падало всё, что строит
# сеты. Переопределяется переменной окружения LFL_NUCDB.
_HERE = os.path.dirname(os.path.abspath(__file__))
_SOLUTION = os.path.normpath(os.path.join(_HERE, '..', '..', '..'))


def _find_db():
    env = os.environ.get('LFL_NUCDB')
    if env:
        return env
    for candidate in (
            os.path.join(_SOLUTION, 'BecquerelMonitor', 'nucdb.sqlite'),
            os.path.join(_SOLUTION, 'nucdb.sqlite'),
    ):
        if os.path.isfile(candidate):
            return candidate
    return os.path.join(_SOLUTION, 'BecquerelMonitor', 'nucdb.sqlite')


DB = _find_db()


# ---------------------------------------------------------------------------
# Правило родителя: ОДНО на проект, и живёт оно НЕ ЗДЕСЬ
# ---------------------------------------------------------------------------
# Какие строки `decay_radiations` принадлежат запрошенному родителю, решает
# `DecayParentRule.LevelClause` приложения (`S89`, уточнено `S94`). Здесь это
# выражение не переписывается, а ЧИТАЕТСЯ из его исходника: переписанная от
# руки копия уже разошлась с приложением однажды (`T74`) — после `S94`
# приложение стало зажимать по СВОЕМУ уровню родителя (`nuclides.l_seqno`), а
# в этих скриптах остался `min(...)`, то есть на четырёх изомерах с двумя
# уровнями (`118INm2`, `190Wm2`, `116AGm2`, `70CUm2`) выдавался набор
# СОСЕДНЕГО состояния `m1`.
#
# Читается, а не копируется, ровно потому, что второй копии правила быть не
# должно: следующая правка `DecayParentRule` доедет сюда сама, а если файл
# переедет или разметку сменят — импорт упадёт ВСЛУХ, а не тихо разойдётся.
# Путь переопределяется переменной окружения `LFL_DECAY_RULE_CS`.
_RULE_CS = os.path.join(_SOLUTION, 'BecquerelMonitor', 'FullSpectrumAnalysis',
                        'DecayParentRule.cs')

#: Имя параметра родителя в `LEVEL_CLAUSE`. Приложение требует `$n`, и
#: `sqlite3` питона такой параметр связывает словарём — переименовывать
#: (а значит и трогать текст правила) не пришлось.
LEVEL_PARAM = 'n'


def _level_clause(path=None):
    """Вынуть `DecayParentRule.LevelClause` из исходника приложения."""
    path = path or os.environ.get('LFL_DECAY_RULE_CS') or _RULE_CS
    if not os.path.isfile(path):
        raise RuntimeError(
            'не найден источник правила родителя: %s. Правило одно на проект и '
            'живёт в DecayParentRule.LevelClause; переписывать его здесь нельзя '
            '(T74). Путь переопределяется переменной LFL_DECAY_RULE_CS.' % path)
    with io.open(path, encoding='utf-8-sig') as handle:
        text = handle.read()
    body = re.search(r'const\s+string\s+LevelClause\s*=(.*?);', text, re.S)
    if not body:
        raise RuntimeError('в %s не найдено объявление const string LevelClause '
                           '— разметка сменилась, правило читать нечем (T74).' % path)
    clause = _sql_literals(body.group(1), 'LevelClause')
    for must in ('parent_l_seqno', 'coalesce', 'nuclides', '$' + LEVEL_PARAM):
        if must not in clause:
            raise RuntimeError('LevelClause разобран неправдоподобно (нет %r): %r' %
                               (must, clause))
    return clause


def _sql_literals(body, what):
    """Склеить строковые литералы C# из тела объявления (`"…" + "…"`).

    ⛔ Комментарий ВНУТРИ объявления ломает разбор, и это не выдумка: встречная
    проверка 26.08.2026 показала опытом, что строка вида
        // тут уровень "родителя", а не дочернего
    перед первым литералом даёт выражение, начинающееся словом `родителя` —
    то есть закавыченное слово из КОММЕНТАРИЯ попадает в SQL. Поэтому комментарии
    снимаются ДО поиска литералов, а не после (T74).
    """
    body = re.sub(r'//[^\n]*', '', body)
    body = re.sub(r'/\*.*?\*/', '', body, flags=re.S)
    if '\\' in body:
        raise RuntimeError('в %s появились escape-последовательности C#; простое '
                           'склеивание литералов их не разберёт (T74).' % what)
    return ''.join(re.findall(r'"([^"]*)"', body))


#: Довесок к `where` для запроса к `decay_radiations`. Параметр родителя —
#: `$n`, связывать словарём: ``{'n': nucid}``.
LEVEL_CLAUSE = _level_clause()


# ---------------------------------------------------------------------------
# Зажим по уровню в `decay_chain`: тоже ОДНО правило, и тоже НЕ ЗДЕСЬ (`T78`)
# ---------------------------------------------------------------------------
# `LEVEL_CLAUSE` выше решает, чьи строки в `decay_radiations`. У обхода САМОЙ
# цепочки вопрос свой: какая из строк `decay_chain` описывает физический
# распад, а какая — тот же переход у ВОЗБУЖДЁННОГО уровня. Правило и на него
# одно, но 26.08.2026 оно было переписано от руки ШЕСТЬЮ местами тремя
# разными текстами:
#
#   A  min(l_seqno) по тройке (nucid, daughter, dec_type)
#      — здесь, `FsaSampleLibrary.cs` (рёбра) и он же (глубина);
#   B  то же, но минимум ищется среди строк С ЧИСЛОМ (`x.perc not null`)
#      — `NucBaseFramework.cs`;
#   C  `l_seqno = 0` — `CascadeAtomicData.cs` и `tools/nucdb/fill_intensity.py`.
#
# Измерено 05.09.2026 на всех 2535 родителях `decay_chain`: A и B дают один
# набор дочек у ВСЕХ (0 расхождений — довесок `perc not null` на поставке
# ничего не меняет), а C расходится с A у 551 родителя; глазами своего
# потребителя (одна дочка с наибольшим `perc`, петли `daughter = nucid`
# сняты) — у 39, и у 30 из них C дочки не находит ВОВСЕ, `234PAm1` среди них.
# Ряд U-238 под C обрывается на `234PAm1` (3 члена вместо 19).
#
# Поэтому текст сюда не переписывается, а ЧИТАЕТСЯ у приложения — тем же
# приёмом, что `LEVEL_CLAUSE`, с той же снятой ловушкой комментариев:
#
#   1) `DecayParentRule.ChainLevelClause`, когда он появится — единое место;
#   2) пока его нет — живой текст `FsaSampleLibrary.cs`, и ВСЕ его вхождения
#      обязаны совпасть между собой (с точностью до пробелов), иначе отказ.
#
# ⚠ Шаг 2 переходный и снимается вместе с правкой приложения по `T78`. Своей
# копии выражения здесь нет ни в одном из шагов: разойтись с приложением молча
# нечему, а если разметка сменится — импорт упадёт ВСЛУХ.
# Путь к `FsaSampleLibrary.cs` переопределяется переменной `LFL_CHAIN_RULE_CS`.
_CHAIN_CS = os.path.join(_SOLUTION, 'BecquerelMonitor', 'FullSpectrumAnalysis',
                         'FsaSampleLibrary.cs')


def _check_chain_clause(clause, where):
    clause = ' '.join(clause.split())
    if not clause.startswith('and ') or 'l_seqno' not in clause:
        raise RuntimeError('зажим цепочки из %s разобран неправдоподобно: %r'
                           % (where, clause))
    for bad in ('?', ':', '@'):
        if bad in clause:
            raise RuntimeError('в зажиме цепочки из %s появился параметр %r — '
                               'обход связывает только $%s словарём, связать этот '
                               'нечем (T78): %r' % (where, bad, LEVEL_PARAM, clause))
    return ' ' + clause


def _chain_level_clause(rule_path=None, lib_path=None):
    """Довесок к `where` обхода `decay_chain`, взятый у приложения."""
    rule_path = rule_path or os.environ.get('LFL_DECAY_RULE_CS') or _RULE_CS
    lib_path = lib_path or os.environ.get('LFL_CHAIN_RULE_CS') or _CHAIN_CS
    if os.path.isfile(rule_path):
        with io.open(rule_path, encoding='utf-8-sig') as handle:
            body = re.search(r'const\s+string\s+ChainLevelClause\s*=(.*?);',
                             handle.read(), re.S)
        if body:
            return _check_chain_clause(
                _sql_literals(body.group(1), 'ChainLevelClause'), rule_path)
    if not os.path.isfile(lib_path):
        raise RuntimeError(
            'не найден источник зажима цепочки: ни ChainLevelClause в %s, ни %s. '
            'Переписывать выражение здесь нельзя (T78); путь переопределяется '
            'переменной LFL_CHAIN_RULE_CS.' % (rule_path, lib_path))
    with io.open(lib_path, encoding='utf-8-sig') as handle:
        text = handle.read()
    found = []
    for body in re.findall(r'CommandText\s*=(.*?);', text, re.S):
        sql = _sql_literals(body, 'CommandText из %s' % os.path.basename(lib_path))
        if 'from decay_chain' not in sql:
            continue
        head = re.search(r'\s+and\s+l_seqno\s*=', sql)
        if not head:
            raise RuntimeError('в %s обход decay_chain идёт БЕЗ зажима по уровню: '
                               '%r. Читать нечего (T78).' % (lib_path, sql))
        found.append(' '.join(sql[head.start():].split()))
    if not found:
        raise RuntimeError('в %s не найдено ни одного запроса к decay_chain — '
                           'разметка сменилась, зажим читать нечем (T78).' % lib_path)
    if len(set(found)) != 1:
        raise RuntimeError('копии зажима цепочки в %s разошлись между собой '
                           '(%d разных текстов на %d вхождений): %r. Правило одно, '
                           'и выбрать за приложение здесь нельзя (T78).'
                           % (lib_path, len(set(found)), len(found), sorted(set(found))))
    return _check_chain_clause(found[0], lib_path)


#: Довесок к `where` для обхода `decay_chain`. Имя родителя, если выражение
#: его упоминает, — `$n`, связывать словарём: ``{'n': nucid}``.
CHAIN_LEVEL_CLAUSE = _chain_level_clause()

_FALLBACK_CACHE = []
_FALLBACK_SAID = set()
#: (`T93`) Родители, у которых запасная ветвь СРАБОТАЛА в этом процессе —
#: то есть чей набор линий уже собран на строках соседнего состояния.
_FALLBACK_HIT = set()


def level_fallback_nucids(c):
    """Родители, у которых сработает ЗАПАСНАЯ ветвь правила: их
    `nuclides.l_seqno` в `decay_radiations` не встречается, и им достаются
    строки самого нижнего уровня — то есть СОСЕДНЕГО состояния.

    Считается одним запросом на всю таблицу и по ИМЕНИ целиком, а не по строке
    `nuclides`: имя там не уникально (`144TBm` — три строки). Так же считает
    `DecayReadersProbe.Fallbacks`.
    """
    if not _FALLBACK_CACHE:
        _FALLBACK_CACHE.extend(row[0] for row in c.execute(
            "select distinct n.nucid from nuclides n"
            " where exists (select 1 from decay_radiations d where d.parent_nucid = n.nucid)"
            "   and not exists (select 1 from nuclides w, decay_radiations d"
            "                   where w.nucid = n.nucid and d.parent_nucid = w.nucid"
            "                     and d.parent_l_seqno = w.l_seqno)"
            " order by n.nucid").fetchall())
    return list(_FALLBACK_CACHE)


def warn_level_fallback(nucid, c):
    """Сказать ВСЛУХ, что родителю достался чужой уровень — как это делает
    `DecayReadersProbe`. Молчать нельзя: подмена набора иначе невидима.

    Возвращает True, если у `nucid` сработала запасная ветвь (и запоминает его
    в `level_fallback_hits()`), иначе False. (`T93`) До 05.09.2026 функция не
    возвращала ничего, и единственный признак «набор собран на строках
    соседнего состояния» терялся на всех трёх путях конвейера; теперь его
    читает `check_corpus.check_level_fallback` и он роняет приёмку.
    """
    if nucid not in level_fallback_nucids(c):
        return False
    _FALLBACK_HIT.add(nucid)
    if nucid not in _FALLBACK_SAID:
        _FALLBACK_SAID.add(nucid)
        sys.stderr.write(
            '  ⚠ ЗАПАСНАЯ ВЕТВЬ %s: nuclides.l_seqno в decay_radiations не '
            'встречается, взят самый нижний уровень — строки СОСЕДНЕГО состояния\n'
            % nucid)
    return True


def level_fallback_hits():
    """(`T93`) Родители, у которых запасная ветвь СРАБОТАЛА с начала процесса
    (или с последнего `reset_level_fallback_hits()`), по имени."""
    return sorted(_FALLBACK_HIT)


def reset_level_fallback_hits():
    _FALLBACK_HIT.clear()


def conn():
    return sqlite3.connect(DB)


def pretty(nucid):
    """208TL -> Tl-208, 234PAM1 -> Pa-234m1"""
    m = re.match(r'^(\d+)([A-Za-z]+?)(M\d*)?$', nucid)
    if not m:
        return nucid
    mass, el, iso = m.group(1), m.group(2), m.group(3) or ''
    el = el[0].upper() + el[1:].lower()
    return '%s-%s%s' % (el, mass, iso.lower())


def chain_branches(root, c, min_fraction=1e-6):
    """{nucid: cumulative branching fraction from root}, ground levels only."""
    frac = {root: 1.0}
    order = [root]
    i = 0
    while i < len(order):
        cur = order[i]
        i += 1
        # l_seqno is the level index of the parent WITHIN its own level scheme;
        # the isomer already has its own nucid (234PAm1), so the lowest level
        # present is the physical decay. Rows with a higher l_seqno duplicate the
        # transition with branching of an excited level (212BI: 35.94% at 0,
        # 67% at 5) and must not be followed.
        # ⛔ Само выражение зажима сюда НЕ переписано: оно одно на проект и
        # читается у приложения — `CHAIN_LEVEL_CLAUSE` (`T78`), см. выше.
        rows = c.execute(
            "select daughter_nucid, perc from decay_chain d "
            "where nucid = $n and perc not null" + CHAIN_LEVEL_CLAUSE,
            {LEVEL_PARAM: cur}).fetchall()
        for daughter, perc in rows:
            if daughter == cur:
                continue                      # 238U l_seqno-119 self loop
            try:
                p = float(perc)
            except (TypeError, ValueError):
                continue
            add = frac[cur] * p / 100.0
            if add < min_fraction:
                continue
            if daughter in frac:
                frac[daughter] += add
            else:
                frac[daughter] = add
                order.append(daughter)
            if len(order) > 100:
                return frac
    return frac


def half_life_years(nucid, c):
    row = c.execute("select half_life_sec from nuclides where nucid=? and half_life not null",
                    (nucid,)).fetchone()
    if not row or row[0] is None:
        return 0.0
    return float(row[0]) / 31536000.0


def chain_lines(root, kinds=('G',), e_min=10.0, e_max=3200.0):
    c = conn()
    frac = chain_branches(root, c)
    kind_keys = ['k%d' % i for i in range(len(kinds))]
    placeholders = ','.join(':' + key for key in kind_keys)
    out = []
    for nucid, br in frac.items():
        # Зажим по уровню родителя обязателен и на обычных нуклидах: Pa-234m1
        # несёт линию 1001.03 кэВ на parent_l_seqno = 2, поэтому «= 0» потеряло
        # бы её целиком. Само правило — общее, из DecayParentRule (см. выше).
        warn_level_fallback(nucid, c)
        params = dict(zip(kind_keys, kinds))
        params[LEVEL_PARAM] = nucid
        rows = c.execute(
            "select energy_num, intensity_num, type_a, type_c from decay_radiations "
            "where parent_nucid = $n and type_a in (%s) "
            "and energy_num not null and intensity_num not null" % placeholders
            + LEVEL_CLAUSE, params).fetchall()
        hl = half_life_years(nucid, c)
        for energy, inum, ta, tc in rows:
            if energy is None or inum is None or inum <= 0:
                continue
            if energy < e_min or energy > e_max:
                continue
            out.append(dict(
                nucid=nucid, name='%s (%s)' % (pretty(nucid), pretty(root)),
                energy=float(energy), i_nuc=float(inum), branch=br,
                i_chain=float(inum) * br, half_life_y=hl,
                kind=ta, xtype=(tc or '').strip()))
    c.close()
    out.sort(key=lambda r: (r['nucid'], r['energy']))
    merged = []
    for r in out:
        if merged and merged[-1]['nucid'] == r['nucid'] and abs(merged[-1]['energy'] - r['energy']) < 0.05:
            if r['i_chain'] > merged[-1]['i_chain']:
                merged[-1] = r
            continue
        merged.append(r)
    merged.sort(key=lambda r: r['energy'])
    return merged


# Th-228 — нижняя половина ториевого ряда, от Th-228 до Tl-208. Отдельная
# цепочка, а не Th-232: аттестованный источник Th-228 не содержит Ac-228, и его
# линий 911/969/338 кэВ в спектре нет. Считать такой источник рядом Th-232
# значит записать в знаменатель recall линии, которых там быть не может.
CHAINS = {'Th-232': '232TH', 'Th-228': '228TH', 'Ra-226': '226RA',
          'U-238': '238U', 'U-235': '235U'}

# Anchor lines: strong, clean single peaks used as the gate of the set.
ANCHORS = {
    'Th-232': [2614.51],   # Tl-208, 100% of chain, top of spectrum, no neighbours
    'Th-228': [2614.51],   # тот же Tl-208: он есть и в укороченном ряду
    'Ra-226': [609.32],    # Bi-214, strongest Ra line
    'U-238': [1001.03],    # Pa-234m, classic "uranium" monopeak (Ra-free glass too)
    'U-235': [185.72],     # U-235 itself
}

if __name__ == '__main__':
    # T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
            pass
    _c = conn()
    _fb = level_fallback_nucids(_c)
    _c.close()
    print('правило родителя прочитано из %s' % _RULE_CS)
    print('зажим цепочки: %r' % CHAIN_LEVEL_CLAUSE)
    print('запасная ветвь сработает у %d родителей: %s' %
          (len(_fb), ', '.join(_fb) if _fb else '—'))
    print()
    for label, root in CHAINS.items():
        lines = chain_lines(root)
        c = conn()
        fr = chain_branches(root, c)
        c.close()
        print('=== %s (%s): %d gamma lines' % (label, root, len(lines)))
        print('    members:', ', '.join('%s=%.4f' % (pretty(k), v)
                                        for k, v in sorted(fr.items(), key=lambda kv: -kv[1])))
        for r in sorted(lines, key=lambda r: -r['i_chain'])[:14]:
            print('      %8.2f keV  Ichain=%7.3f%%  Inuc=%7.3f%%  %s' % (
                r['energy'], r['i_chain'], r['i_nuc'], r['name']))
        print('    counts by I_chain threshold:',
              ' '.join('%.2f%%:%d' % (t, sum(1 for r in lines if r['i_chain'] >= t))
                       for t in (0.01, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0)))
        for a in ANCHORS[label]:
            near = [r for r in lines if abs(r['energy'] - a) < 3.0]
            print('    anchor %.2f -> %s' % (a, near[0]['name'] if near else 'NOT FOUND'))
        print()
    # (`T93`) Признак обязан дойти до кода возврата: член какого-то из рядов
    # выше собран на строках соседнего состояния — это отказ, а не заметка.
    _hit = level_fallback_hits()
    if _hit:
        print('⛔ ЗАПАСНАЯ ВЕТВЬ у членов рядов: %s' % ', '.join(_hit))
        sys.exit(1)
    print('запасная ветвь у членов рядов не сработала')
