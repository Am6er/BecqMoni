# -*- coding: utf-8 -*-
u"""О16: счёт живых чисел для T210 / T211 / T227. Ничего не правит."""
from __future__ import print_function

import io
import os
import re
import sys
import glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__))))
REPO = u'C:\\Users\\moroz\\source\\repos\\BQ Eng res .NET 4.8'
PIE_SRC = os.path.join(REPO, u'tools', u'pie', u'Program.cs')
PIE_README = os.path.join(REPO, u'tools', u'pie', u'README.md')
CORPUS_README = os.path.join(REPO, u'tools', u'CORPUS', u'README.md')
APP_SRC = os.path.join(REPO, u'BecquerelMonitor', u'FullSpectrumAnalysis', u'FsaAnalyzer.cs')
SPECTRA = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'spectra')
PARTS = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'parts.csv')

out = []


def say(s=u''):
    out.append(s)
    print(s)


def read(path):
    with io.open(path, u'r', encoding=u'utf-8-sig', newline=u'') as h:
        return h.read()


# ------------------------------------------------------------------ T210 --
say(u'=' * 72)
say(u'T210 — состав библиотеки pie по умолчанию: код против README')
say(u'=' * 72)

src = read(PIE_SRC)
m = re.search(u'public\\s+List<string>\\s+Components\\s*=\\s*new\\s+List<string>\\s*\\{(.*?)\\}\\s*;',
              src, re.S)
if m is None:
    say(u'ОТКАЗ: список Options.Components в исходнике не разобран')
    sys.exit(2)
code_names = re.findall(u'"([^"]+)"', m.group(1))
line_no = src[:m.start()].count(u'\n') + 1
say(u'Options.Components (tools/pie/Program.cs, строка %d): %d имён'
    % (line_no, len(code_names)))
say(u'  ' + u', '.join(code_names))

readme = read(PIE_README)
rl = readme.splitlines()

say(u'')
say(u'-- все вхождения счёта компонентов в tools/pie/README.md --')
for i, line in enumerate(rl):
    if re.search(u'([0-9]+|пятнадцат\\w*|шестнадцат\\w*)\\s+компонент', line):
        say(u'  %4d: %s' % (i + 1, line.strip()))

say(u'')
say(u'-- абзац «Состав библиотеки по умолчанию» --')
idx = [i for i, line in enumerate(rl) if line.startswith(u'Состав библиотеки по умолчанию')]
say(u'  строк-начал абзаца: %d (%s)' % (len(idx), u', '.join(str(i + 1) for i in idx)))
for i in idx:
    j = i
    para = []
    while j < len(rl) and rl[j].strip():
        para.append(rl[j])
        j += 1
    text = u' '.join(para)
    cut = text.split(u'**Lu-176')[0]
    hits = re.findall(u'\\b(?:[A-Z][a-z]?-[0-9]+|Xray-[A-Za-z]+|SE-[0-9]+|DE-[0-9]+|Ann-[0-9]+)\\b', cut)
    named = []
    for n in hits:
        if n not in named:
            named.append(n)
    say(u'  %4d: названо РАЗНЫХ имён: %d (вхождений %d)' % (i + 1, len(named), len(hits)))
    say(u'        ' + u', '.join(named))
    missing = [n for n in code_names if n not in named]
    extra = [n for n in named if n not in code_names]
    say(u'        в коде есть, в тексте НЕТ: %s' % (u', '.join(missing) if missing else u'—'))
    say(u'        в тексте есть, в коде НЕТ: %s' % (u', '.join(extra) if extra else u'—'))

# ------------------------------------------------------------------ T211 --
say(u'')
say(u'=' * 72)
say(u'T211 — пол шага узлов континуума: pie против приложения')
say(u'=' * 72)
mm = re.search(u'double\\s+minStep\\s*=\\s*\\(chHi\\s*-\\s*chLo\\)\\s*/\\s*([0-9.]+)', src)
say(u'pie  Program.cs BuildHatBasis minStep = (chHi-chLo)/%s   (строка %d)'
    % (mm.group(1), src[:mm.start()].count(u'\n') + 1))
app = read(APP_SRC)
ma = re.search(u'this\\.ContinuumKnotDivisor\\s*=\\s*([0-9.]+)\\s*;', app)
say(u'app  FsaAnalyzer.ContinuumKnotDivisor = %s          (строка %d)'
    % (ma.group(1), app[:ma.start()].count(u'\n') + 1))
mp = re.search(u'Math\\.Max\\(([0-9.]+)\\s*\\*\\s*w,\\s*minStep\\)', src)
say(u'pie  множитель на ПШПВ (вбит в BuildHatBasis): %s'
    % (mp.group(1) if mp else u'НЕ РАЗОБРАН'))
mf = re.search(u'public\\s+double\\s+ContinuumKnotFwhm\\s*=\\s*([0-9.]+)\\s*;', app)
say(u'app  множитель на ПШПВ (ContinuumKnotFwhm):    %s'
    % (mf.group(1) if mf else u'НЕ РАЗОБРАН'))
say(u'множители совпадают: %s'
    % (u'да' if (mp and mf and float(mp.group(1)) == float(mf.group(1))) else u'НЕТ'))
say(u'ключ --knot-div (верхний предел шага у pie) есть: %s'
    % (u'да' if u'--knot-div' in src else u'нет'))

say(u'')
say(u'-- упоминания расхождения в tools/CORPUS/README.md --')
crd = read(CORPUS_README).splitlines()
found = 0
for i, line in enumerate(crd):
    if (u'ContinuumKnotDivisor' in line or u'BuildHatBasis' in line
            or u'узлов континуума' in line):
        say(u'  %5d: %s' % (i + 1, line.strip()[:150]))
        found += 1
say(u'  всего: %d' % found)

# ------------------------------------------------------------------ T227 --
say(u'')
say(u'=' * 72)
say(u'T227 — достаёт ли полоса pie при умолчаниях до края шкалы')
say(u'=' * 72)
me = re.search(u'public\\s+double\\s+EMin\\s*=\\s*([0-9.]+)\\s*;', src)
mx = re.search(u'public\\s+double\\s+EMax\\s*=\\s*([0-9.]+)\\s*;', src)
emin, emax = float(me.group(1)), float(mx.group(1))
say(u'умолчания полосы (Program.cs): EMin = %g, EMax = %g кэВ' % (emin, emax))


def parse_spectrum(path):
    t = read(path)
    n = re.search(u'<NumberOfChannels>([0-9]+)</NumberOfChannels>', t)
    if n is None:
        return None
    nch = int(n.group(1))
    cal = re.search(u'<EnergyCalibration>(.*?)</EnergyCalibration>', t, re.S)
    if cal is None:
        return None
    coeffs = [float(x) for x in re.findall(u'<Coefficient>([^<]+)</Coefficient>', cal.group(1))]
    kind = u'polynomial' if u'PolynomialOrder' in cal.group(1) else u'иная'
    spec = re.search(u'<Spectrum>(.*?)</Spectrum>', t, re.S)
    pts = [int(x) for x in re.findall(u'<DataPoint>(-?[0-9]+)</DataPoint>', spec.group(1))] if spec else []
    return nch, coeffs, kind, pts


def energy(coeffs, ch):
    e = 0.0
    for k, c in enumerate(coeffs):
        e += c * (ch ** k)
    return e


def channel_of(coeffs, target, nch):
    u"""Тот же смысл, что у EnergyToChannel: первый канал, где E >= target.
    Считаем перебором — калибровка монотонна на рабочем участке."""
    lo, hi = 0.0, float(nch - 1)
    if energy(coeffs, hi) < target:
        return float(nch)  # не достаёт вовсе
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        if energy(coeffs, mid) < target:
            lo = mid
        else:
            hi = mid
    return hi


def is_overflow(pts, channel):
    n = len(pts)
    if n < 6 or (channel != 0 and channel != n - 1):
        return False, None
    window = min(32, n - 2)
    if window < 4:
        return False, None
    nb = sorted(float(pts[1 + k] if channel == 0 else pts[n - 2 - k]) for k in range(window))
    med = nb[window // 2] if window % 2 == 1 else 0.5 * (nb[window // 2 - 1] + nb[window // 2])
    v = float(pts[channel])
    ok = v >= 20.0 * max(med, 1.0) and v - med >= 8.0 * ((med + 1.0) ** 0.5)
    return ok, med


parts = {}
for line in read(PARTS).splitlines()[1:]:
    if not line.strip():
        continue
    cells = line.split(u',')
    parts[cells[0]] = (cells[1], cells[2])

files = sorted(glob.glob(os.path.join(SPECTRA, u'*.xml')))
say(u'спектров в корпусе: %d' % len(files))
reach_e, reach_round, unparsed = [], [], []
rows = []
for path in files:
    name = os.path.splitext(os.path.basename(path))[0]
    got = parse_spectrum(path)
    if got is None or not got[1]:
        unparsed.append(name)
        continue
    nch, coeffs, kind, pts = got
    elast = energy(coeffs, nch - 1)
    ch = channel_of(coeffs, emax, nch)
    by_e = elast <= emax
    by_round = ch >= (nch - 1) - 0.5
    ovf, med = is_overflow(pts, len(pts) - 1) if pts else (False, None)
    det, part = parts.get(name, (u'?', u'?'))
    rows.append((name, nch, elast, by_e, by_round, ovf, pts[-1] if pts else None, med, det, part, kind))
    if by_e:
        reach_e.append(name)
    if by_round:
        reach_round.append(name)

say(u'калибровку не разобрать: %d %s' % (len(unparsed), unparsed))
say(u'нелинейных калибровок: %d'
    % sum(1 for r in rows if r[10] != u'polynomial'))
say(u'')
say(u'ДОСТАЁТ по критерию E(N-1) <= %g кэВ:            %d из %d' % (emax, len(reach_e), len(rows)))
say(u'ДОСТАЁТ по правилу округления ClampChannel:      %d из %d' % (len(reach_round), len(rows)))
say(u'')
say(u'=== достающие (по округлению — то, что реально делает pie) ===')
say(u'%-24s %-7s %-10s %-6s %-9s %-8s %-9s %s'
    % (u'спектр', u'N', u'E(N-1)', u'E<=', u'округл', u'посл', u'мед32', u'край / прибор / часть'))
germ = 0
plain = []
for r in sorted(rows, key=lambda r: r[0]):
    if not (r[3] or r[4]):
        continue
    verdict = u'ПЕРЕПОЛНЕНИЕ' if r[5] else u'обычный'
    say(u'%-24s %-7d %-10.1f %-6s %-9s %-8s %-9s %s / %s / %s'
        % (r[0], r[1], r[2], u'да' if r[3] else u'нет', u'да' if r[4] else u'нет',
           r[6], (u'%.1f' % r[7]) if r[7] is not None else u'?', verdict, r[8], r[9]))
    if not r[5]:
        plain.append(r)
        if r[9] == u'excluded' or r[8].upper().startswith(u'HPGE'):
            germ += 1

say(u'')
say(u'из достающих крайний канал ОБЫЧНЫЙ у: %d' % len(plain))
say(u'  из них германий (part=excluded / det HPGE*): %d' % germ)
neg = [r for r in plain if not (r[9] == u'excluded' or r[8].upper().startswith(u'HPGE'))]
say(u'  негерманиевых: %d — %s' % (len(neg), u', '.join(u'%s (посл=%s, мед32=%.1f)'
                                                        % (r[0], r[6], r[7]) for r in neg)))

with io.open(sys.argv[1], u'w', encoding=u'utf-8', newline=u'\n') as h:
    h.write(u'\n'.join(out) + u'\n')
