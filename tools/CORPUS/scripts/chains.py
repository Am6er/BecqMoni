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
    body = body.group(1)
    # ⛔ Комментарий ВНУТРИ объявления ломает разбор, и это не выдумка: встречная
    # проверка 26.08.2026 показала опытом, что строка вида
    #     // тут уровень "родителя", а не дочернего
    # перед первым литералом даёт `LEVEL_CLAUSE`, начинающийся словом `родителя` —
    # то есть закавыченное слово из КОММЕНТАРИЯ попадает в SQL. Поэтому комментарии
    # снимаются ДО поиска литералов, а не после (T74).
    body = re.sub(r'//[^\n]*', '', body)
    body = re.sub(r'/\*.*?\*/', '', body, flags=re.S)
    if '\\' in body:
        raise RuntimeError('в LevelClause появились escape-последовательности C#; '
                           'простое склеивание литералов их не разберёт (T74).')
    clause = ''.join(re.findall(r'"([^"]*)"', body))
    for must in ('parent_l_seqno', 'coalesce', 'nuclides', '$' + LEVEL_PARAM):
        if must not in clause:
            raise RuntimeError('LevelClause разобран неправдоподобно (нет %r): %r' %
                               (must, clause))
    return clause


#: Довесок к `where` для запроса к `decay_radiations`. Параметр родителя —
#: `$n`, связывать словарём: ``{'n': nucid}``.
LEVEL_CLAUSE = _level_clause()

_FALLBACK_CACHE = []
_FALLBACK_SAID = set()


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
    `DecayReadersProbe`. Молчать нельзя: подмена набора иначе невидима."""
    if nucid in _FALLBACK_SAID or nucid not in level_fallback_nucids(c):
        return
    _FALLBACK_SAID.add(nucid)
    sys.stderr.write(
        '  ⚠ ЗАПАСНАЯ ВЕТВЬ %s: nuclides.l_seqno в decay_radiations не '
        'встречается, взят самый нижний уровень — строки СОСЕДНЕГО состояния\n'
        % nucid)


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
        rows = c.execute(
            "select daughter_nucid, perc from decay_chain d where nucid = ? and perc not null "
            "and l_seqno = (select min(l_seqno) from decay_chain x where x.nucid = d.nucid "
            "               and x.daughter_nucid = d.daughter_nucid and x.dec_type = d.dec_type)",
            (cur,)).fetchall()
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
    _c = conn()
    _fb = level_fallback_nucids(_c)
    _c.close()
    print('правило родителя прочитано из %s' % _RULE_CS)
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
