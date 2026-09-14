# -*- coding: utf-8 -*-
"""Метка ряда → корень и члены подряда — ТО ЖЕ правило, что у приложения (`T259`).

Словарь меток рядов один на проект и живёт в `FsaSampleChain.FromLabel`
(`BecquerelMonitor/FullSpectrumAnalysis/FsaSampleLibrary.cs`, `T257`). Здесь —
его прочтение для читателей на питоне (`tools/pie/score.py`; при следующем
переобъявлении базы — `build_corpus.sample_lines`/`sample_xrays` и
`calibrate.sample_lines`, которые пока берут корень из `chains.CHAINS` и на
метке-члене падают `KeyError`). Правило — три шага, оба места обязаны их
повторять слово в слово:

  1. метка «Xx-NNN» → `nucid` «NNNXX» разбором (`nucid_of`, как
     `FsaSampleLibrary.NucidOf`); особый случай — `U-238u`, урановое стекло:
     голова ряда без хвоста, список членов задан явно (как в `build_corpus`);
  2. корень обязан РАСПАДАТЬСЯ по базе: строка `nuclides` с числовым
     `half_life_sec` (`is_decaying`); неизвестный базе или стабильный — отказ;
  3. подряд — от корня ВНИЗ по `decay_chain` (тем же ребром, что
     `chains.chain_branches`), пока равновесие с корнем возможно: дочерний с
     периодом ДЛИННЕЕ корня в подряд не входит и закрывает всё под собой
     (`equilibrium_members`). У голов рядов (Th-232, Ra-226, U-238, U-235) и
     Th-228 такого члена нет — множество равно всему обходу, состав прежний;
     у «Rn-222» подряд обрывается на Pb-210 (22.2 г против 3.82 сут).

⛔ Имён нуклидов здесь нет (кроме явного списка стекла, повторяющего
`FromLabel`): всё берётся из `nucdb`. Этот модуль НЕ входит в набор
генератора корпуса (`corpus_stamp`: замыкание импортов `build_corpus.py`) —
нарочно: правка здесь не красит сторож `T244`, пока корпус не пересобран.

    python tools/CORPUS/scripts/chain_labels.py Rn-222 Ra-226 Th-228 Xx-999
"""
import os
import re
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import chains                                                   # noqa: E402

#: Урановое стекло — голова ряда без хвоста; список повторяет `FsaSampleChain.FromLabel`
#: и `build_corpus.sample_lines`.
GLASS_LABEL = 'U-238u'
GLASS_ROOT = '238U'
GLASS_ONLY = ('238U', '234TH', '234PAm1', '234PA', '234U')


def nucid_of(label):
    """«Cs-137» → «137CS», «Pa-234m» → «234PAm»; пусто — разобрать не вышло
    (как `FsaSampleLibrary.NucidOf`)."""
    if not label:
        return ''
    text = label.strip()
    dash = text.find('-')
    if dash <= 0 or dash + 1 >= len(text):
        return ''
    symbol, mass = text[:dash], text[dash + 1:]
    isomer = ''
    while mass and mass[-1].isalpha():
        isomer = mass[-1].lower() + isomer
        mass = mass[:-1]
    if not mass or not mass.isdigit():
        return ''
    return mass + symbol.upper() + isomer


def half_life_sec(nucid, c):
    """Период в секундах по самому нижнему уровню; None — нет или стабилен."""
    row = c.execute("select half_life_sec from nuclides where nucid = ? and half_life_sec is not null"
                    " order by l_seqno limit 1", (nucid,)).fetchone()
    return float(row[0]) if row and row[0] is not None else None


def is_decaying(nucid, c):
    hl = half_life_sec(nucid, c)
    return hl is not None and hl > 0.0


def chain_root(label, c=None):
    """Метка → (корень nucid, множество разрешённых членов или None).

    Неизвестная метка — `ValueError` с внятным текстом, не пустой ряд.
    """
    if label == GLASS_LABEL:
        return GLASS_ROOT, set(GLASS_ONLY)
    nucid = nucid_of(label)
    if not nucid:
        raise ValueError('метка ряда %r не вида «Xx-NNN» (T259)' % label)
    own = c is None
    if own:
        c = chains.conn()
    try:
        if not is_decaying(nucid, c):
            raise ValueError('метка ряда %r: нуклида %s в nucdb нет или он стабилен — корнем подряда быть не может (T259)'
                             % (label, nucid))
    finally:
        if own:
            c.close()
    return nucid, None


def equilibrium_members(root, c):
    """Члены ряда, с которыми корень может быть в равновесии (шаг 3 правила)."""
    reachable = {root}
    root_hl = half_life_sec(root, c)
    if root_hl is None:
        return set(chains.chain_branches(root, c).keys())
    order = [root]
    i = 0
    while i < len(order) and len(order) <= 128:
        cur = order[i]
        i += 1
        rows = c.execute("select daughter_nucid, perc from decay_chain d where nucid = $n and perc not null"
                         + chains.CHAIN_LEVEL_CLAUSE, {chains.LEVEL_PARAM: cur}).fetchall()
        for daughter, perc in rows:
            if not daughter or daughter == cur or daughter in reachable:
                continue
            try:
                if not float(perc) > 0.0:
                    continue
            except (TypeError, ValueError):
                continue
            hl = half_life_sec(daughter, c)
            if hl is not None and not hl < root_hl:
                continue                    # длиннее корня: не в равновесии, всё под ним закрыто
            reachable.add(daughter)
            if hl is not None:
                order.append(daughter)
    return reachable


def members_of(label, c=None):
    """Метка → {nucid: доля ветвления от корня} по правилу подряда."""
    own = c is None
    if own:
        c = chains.conn()
    try:
        root, only = chain_root(label, c)
        frac = chains.chain_branches(root, c)
        allowed = equilibrium_members(root, c)
        return dict((k, v) for k, v in frac.items()
                    if k in allowed and (only is None or k in only))
    finally:
        if own:
            c.close()


def chain_lines(label, **kw):
    """Линии подряда метки — `chains.chain_lines(root)`, отфильтрованные правилом."""
    c = chains.conn()
    try:
        root, only = chain_root(label, c)
        allowed = equilibrium_members(root, c)
    finally:
        c.close()
    return [r for r in chains.chain_lines(root, **kw)
            if r['nucid'] in allowed and (only is None or r['nucid'] in only)]


if __name__ == '__main__':
    for _s in (sys.stdout, sys.stderr):
        try:
            _s.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    code = 0
    for _label in sys.argv[1:] or ['Th-232', 'Th-228', 'Ra-226', 'U-238', 'U-235', 'U-238u', 'Rn-222', 'Rn-220']:
        try:
            _m = members_of(_label)
        except ValueError as e:
            print('%-8s ОТКАЗ: %s' % (_label, e))
            code = 1
            continue
        _c = chains.conn()
        _root, _ = chain_root(_label, _c)
        _all = chains.chain_branches(_root, _c)
        _c.close()
        _cut = sorted(set(_all) - set(_m))
        print('%-8s корень %s: членов %d из %d обхода; %s' % (
            _label, _root, len(_m), len(_all),
            ('вне равновесия: ' + ', '.join(chains.pretty(x) for x in _cut)) if _cut else 'обрыва нет'))
        print('         ' + ', '.join('%s=%.4g' % (chains.pretty(k), v) for k, v in sorted(_m.items(), key=lambda kv: -kv[1])))
    sys.exit(code)
