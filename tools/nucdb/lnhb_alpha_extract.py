# -*- coding: utf-8 -*-
u"""Снять с таблиц LNHB/DDEP (PDF «<нуклид>_tables.pdf») α-излучение и β⁻-ветви ряда Ac-227 в TSV
(`AMBER274` Э3, полоса `a274n`, 14.11.2026).

ЗАЧЕМ. Критик `crit274a` нашёл неполноту выходов альф ряда Ac-227 в `nucdb` (Ra-223 Σ 97.8 %, Th-227 99.16 %,
Rn-219 99.8 %; у Ac-227 β 99.0 % + α 1.413 % = 100.41 %). Решение Amber 14.11.2026 вопросником, дословно:
**«Поправить nucdb по LNHB»**. Этот скрипт — только ЧТЕНИЕ источника: PDF → `tools/nucdb/data/lnhb/<нуклид>_alpha.tsv`
и `<нуклид>_beta.tsv` (формат `_beta.tsv` — тот же, что у `correct_lnhb.py`). Базу пишет `correct_lnhb_alpha.py`.

Что снимается:
  * раздел «α Emissions» — энергия АЛЬФА-ЧАСТИЦЫ (не перехода), её погрешность и вероятность на 100 распадов.
    Неразрешённые группы (стрелки ↑ | ↓ у энергий, одна вероятность на группу) — ОДНОЙ строкой на энергии
    последнего члена (↓): так их пишет машинный файл LNHB (LARA, `<нуклид>.lara.txt`). Знак ≈/∼ → признак `AP`
    (приближённое, как в ENSDF), «<» → `LT` (предел);
  * β⁻-ветви — из раздела «Electron Emissions» (строки «β− 0,n max: … avg: …»): граница, вероятность, средняя.

  * (второй круг, 14.11.2026) Th-227 — таблицы у LNHB НЕТ (`Th-227_tables.pdf`, `Th-227.lara.txt` — 404), его α
    есть в машинном файле ЦЕПОЧКИ `Ac-227.lara.txt` (45 строк, Σ 100.32 %). Решение Amber 14.11.2026 вопросником,
    дословно: «LNHB (файл цепочки) (Рекомендую)». Строки α Th-227 этого файла лежат копией в
    `data/lnhb/Ac-227.lara.Th-227.txt`; ключ `--lara` переводит их в `Th-227_alpha.tsv` (погрешности LARA абсолютные
    → в единицах последнего знака, как у ENSDF; метка — `α0,<уровень>`);
  * (второй круг) Pb-211 — β⁻ (решение Amber 14.11.2026 вопросником, дословно: «Да, по LNHB (Рекомендую)»).

Десятичная запятая → точка. Пустое поле TSV пишется «-» (без хвостовых пробелов и табуляций). Шапка TSV: адрес
таблицы, sha256 PDF, оценщик и даты оценки.

    python -I tools/nucdb/lnhb_alpha_extract.py <каталог PDF> [<каталог TSV>]
    python -I tools/nucdb/lnhb_alpha_extract.py --lara [<каталог TSV>]     # Th-227 из файла цепочки

PDF в каталоге называются `<нуклид>.pdf` (Ac-227.pdf …). Нужен `pymupdf`.
Коды: 0 — снято; 1 — раздел не найден или строка не разобралась; 2 — неверный вызов.
"""
from __future__ import print_function
import hashlib
import io
import os
import re
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DEFAULT = os.path.join(HERE, 'data', 'lnhb')

#: нуклид LNHB, nucid базы, снимать ли β⁻
NUCLIDES = [
    ('Ac-227', '227AC', True),
    ('Ra-223', '223RA', False),
    ('Rn-219', '219RN', False),
    ('Po-215', '215PO', False),
    ('Bi-211', '211BI', True),
    ('Tl-207', '207TL', True),
    ('Po-211', '211PO', False),
    ('Pb-211', '211PB', True),       # (второй круг) только β⁻
]

#: (второй круг) Th-227 — из машинного файла цепочки Ac-227 (копия строк — `data/lnhb/Ac-227.lara.Th-227.txt`)
LARA_URL = 'https://www.lnhb.fr/nuclides/Ac-227.lara.txt'
LARA_COPY = 'Ac-227.lara.Th-227.txt'
EMPTY = '-'


def cells(row):
    u"""Пустое поле — «-»: строка TSV без хвостовых пробелов и табуляций."""
    return [c if c else EMPTY for c in row]


def last_digit_units(value_text, unc_text):
    u"""Абсолютная погрешность LARA → в единицах последнего знака значения (как у ENSDF): 5033.5 ± 4.0 → 40."""
    from decimal import Decimal
    if not unc_text:
        return ''
    v, u = Decimal(value_text), Decimal(unc_text)
    exp = v.as_tuple().exponent
    q = u / (Decimal(1).scaleb(exp))
    if q != q.to_integral_value():
        raise ValueError(u'погрешность %s не кратна последнему знаку %s' % (unc_text, value_text))
    return str(int(q))


def lara_alphas(path, emitter):
    rows = []
    with io.open(path, encoding='utf-8') as f:
        for line in f:
            if line.startswith('#'):
                continue
            parts = [p.strip() for p in line.rstrip('\n').split(';')]
            if len(parts) < 9 or parts[4] != 'a' or parts[5] != emitter:
                continue
            e, de, i, di, lvl = parts[0], parts[1], parts[2], parts[3], parts[7]
            rows.append((u'α0,%s' % lvl, e, last_digit_units(e, de), i, last_digit_units(i, di), ''))
    rows.sort(key=lambda r: float(r[1]))
    return rows

URL = 'https://www.lnhb.fr/nuclides/%s_tables.pdf'

RE_LABEL = re.compile(u'^α(\\d+),(\\d+)$')
RE_ENERGY = re.compile(u'^(\\d+(?:,\\d+)?)(?:\\s*\\((\\d+)\\))?\\s*([↑↓|])?$')
RE_PROB = re.compile(u'^([≈∼~<]?)\\s*(\\d+(?:,\\d+)?)(?:\\s*\\((\\d+)\\))?$')
RE_FOOTER = re.compile(u'^[A-Za-z][A-Za-z .\\-]*\\s*/')        # «KRI /V.P.Chechev», «IFIN-HH /A. Luca»
RE_HEADER_END = re.compile(u'^(× 100|per 100 disint\\.)$')
RE_DATES = re.compile(u'(\\d{2}/\\d{2}/\\d{4})\\s*−\\s*(\\d{1,2}/\\d{1,2}/\\d{4})')


def num(text):
    return text.replace(',', '.')


def flag_of(sign):
    return {u'≈': 'AP', u'∼': 'AP', u'~': 'AP', u'<': 'LT'}.get(sign, '')


def text_of(pdf):
    import pymupdf
    doc = pymupdf.open(pdf)
    try:
        return u'\n'.join(page.get_text() for page in doc)
    finally:
        doc.close()


def alpha_emissions(text):
    u"""Строки раздела «α Emissions»: [(метка, энергия, погрешность, вероятность, погрешность, признак)]."""
    start = text.find(u'α Emissions')
    if start < 0:
        return None
    stop = text.find(u'Electron Emissions', start)
    lines = [l.strip() for l in text[start:stop].split('\n')]
    # шапка раздела: до первой строки «× 100» / «per 100 disint.»
    i = 0
    while i < len(lines) and not RE_HEADER_END.match(lines[i]):
        i += 1
    i += 1
    rows, group, label, state = [], [], None, 'label'
    skipping = False
    while i < len(lines):
        line = lines[i]
        i += 1
        if not line:
            continue
        if skipping:
            if RE_HEADER_END.match(line):
                skipping = False
            continue
        if RE_FOOTER.match(line):
            skipping = True
            continue
        if state == 'label':
            m = RE_LABEL.match(line)
            if m:
                label = line
                state = 'energy'
                continue
            if re.match(u'^\\d+$', line) and i < len(lines) and lines[i].strip() == u'Electron Emissions':
                break
            if re.match(u'^\\d+$', line):            # номер следующего раздела «5»
                continue
            raise ValueError(u'ждали метку α, а строка «%s»' % line)
        if state == 'energy':
            m = RE_ENERGY.match(line)
            if not m:
                raise ValueError(u'%s: ждали энергию, а строка «%s»' % (label, line))
            group.append((label, num(m.group(1)), m.group(2) or ''))
            if m.group(3) in (u'↑', u'|'):
                state = 'label'                       # член группы без своей вероятности
            else:
                state = 'prob'
            continue
        if state == 'prob':
            m = RE_PROB.match(line)
            if not m:
                raise ValueError(u'%s: ждали вероятность, а строка «%s»' % (label, line))
            last = group[-1]
            members = u'+'.join(g[0] for g in group)
            rows.append((members, last[1], last[2], num(m.group(2)), m.group(3) or '', flag_of(m.group(1))))
            group = []
            state = 'label'
            continue
    if group:
        raise ValueError(u'незакрытая группа: %s' % group)
    return rows


RE_BMAX = re.compile(u'β−\\s+0,(\\d+)\\s+max:\\s+([\\d,]+)\\s+\\((\\d+)\\)\\s+([≈∼<]?)\\s*([\\d,]+)(?:\\s+\\((\\d+)\\))?')
RE_BAVG = re.compile(u'β−\\s+0,(\\d+)\\s+avg:\\s+([\\d,]+)\\s+\\((\\d+)\\)')


def beta_branches(text):
    start = text.find(u'Electron Emissions')
    if start < 0:
        return None
    stop = text.find(u'Photon Emissions', start)
    flat = u' '.join(l.strip() for l in text[start:stop].split('\n') if l.strip())
    maxes = dict((m.group(1), m) for m in RE_BMAX.finditer(flat))
    avgs = dict((m.group(1), m) for m in RE_BAVG.finditer(flat))
    rows = []
    for lvl in sorted(maxes, key=int):
        m, a = maxes[lvl], avgs.get(lvl)
        if a is None:
            raise ValueError(u'β−0,%s: есть max, нет avg' % lvl)
        rows.append((u'β−0,%s' % lvl, 'B-', num(m.group(2)), m.group(3), num(m.group(5)), m.group(6) or '',
                     num(a.group(2)), a.group(3), flag_of(m.group(4))))
    return rows


def head(name, nucid, pdf, text, what):
    sha = hashlib.sha256(open(pdf, 'rb').read()).hexdigest()
    first = text.split('\n')
    who = ''
    for line in first:
        if RE_FOOTER.match(line.strip()):
            who = line.strip()
            break
    m = RE_DATES.search(text)
    dates = (m.group(1) + ' - ' + m.group(2)) if m else ''
    return [u'# LNHB/DDEP, Table de Radionucleides: %s (%s)' % (name, nucid),
            u'# %s, sha256 %s (снято 14.11.2026, полоса a274n, AMBER274 Э3)' % (URL % name, sha),
            u'# оценка: %s, %s' % (who, dates),
            u'# %s' % what]


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    if argv[1] == '--lara':
        out = argv[2] if len(argv) > 2 else OUT_DEFAULT
        copy = os.path.join(OUT_DEFAULT, LARA_COPY)
        rows = lara_alphas(copy, 'Th-227')
        sha = hashlib.sha256(open(copy, 'rb').read()).hexdigest()
        path = os.path.join(out, 'Th-227_alpha.tsv')
        with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(u'# LNHB/DDEP, машинный файл цепочки Ac-227: Th-227 (227TH) — таблицы самого Th-227 у LNHB нет\n')
            f.write(u'# %s (14.11.2026, полоса a274n, AMBER274 Э3); копия строк — %s, sha256 %s\n' % (LARA_URL, LARA_COPY, sha))
            f.write(u'# «LNHB (файл цепочки) (Рекомендую)» — решение Amber 14.11.2026; погрешности — в единицах последнего знака\n')
            f.write(u'# энергия альфа-частицы, вероятность на 100 распадов Th-227; метка — α0,<уровень дочки по LARA>\n')
            f.write(u'label\tenergy_kev\tenergy_unc\tprob\tprob_unc\tflag\n')
            for r in rows:
                f.write(u'\t'.join(cells(r)) + u'\n')
        print(u'Th-227: %d α-строк, Σ %.5f %% → %s' % (len(rows), sum(float(r[3]) for r in rows), path))
        return 0 if rows else 1
    src = argv[1]
    out = argv[2] if len(argv) > 2 else OUT_DEFAULT
    bad = 0
    for name, nucid, beta in NUCLIDES:
        pdf = os.path.join(src, name + '.pdf')
        if not os.path.isfile(pdf):
            print(u'⛔ нет %s' % pdf)
            bad += 1
            continue
        text = text_of(pdf)
        try:
            alphas = alpha_emissions(text)
            betas = beta_branches(text) if beta else []
        except ValueError as ex:
            print(u'⛔ %s: %s' % (name, ex))
            bad += 1
            continue
        if alphas:
            path = os.path.join(out, name + '_alpha.tsv')
            with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
                for line in head(name, nucid, pdf, text,
                                 u'раздел «α Emissions»: энергия альфа-частицы, вероятность на 100 распадов; '
                                 u'группа ↑|↓ — одной строкой на энергии последнего члена (как LARA); признак AP — ≈/∼'):
                    f.write(line + u'\n')
                f.write(u'label\tenergy_kev\tenergy_unc\tprob\tprob_unc\tflag\n')
                for r in alphas:
                    f.write(u'\t'.join(cells(r)) + u'\n')
            print(u'%s: %d α-строк, Σ %.5f %% → %s' % (name, len(alphas), sum(float(r[3]) for r in alphas), path))
        if betas:
            path = os.path.join(out, name + '_beta.tsv')
            with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
                for line in head(name, nucid, pdf, text,
                                 u'раздел «Electron Emissions», строки β− max/avg (граница, вероятность, средняя); признак AP — ≈'):
                    f.write(line + u'\n')
                f.write(u'label\ttype\tmax_kev\tmax_unc\tprob\tprob_unc\tavg_kev\tavg_unc\tflag\n')
                for r in betas:
                    f.write(u'\t'.join(cells(r)) + u'\n')
            print(u'%s: %d β⁻-ветвей, Σ %.5f %% → %s' % (name, len(betas), sum(float(r[4]) for r in betas), path))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
