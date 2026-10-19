# -*- coding: utf-8 -*-
u"""П342 (AMBER153, остаток): копии корпуса для плеч А/Б — ТОЛЬКО в `D:\\BqMoni_Claude\\p342\\corpus_<плечо>`.

Корпус дерева не пишется: копируются `*.csv`, `*.json`, `spectra/`, `devices/`, `pinned/`, затем в КОПИИ
правится узел фона (`<BackgroundEnergySpectrum>`, хирургически, `corpus_xml.replace_background`) или графы
`chains`/`nuclides` манифеста. Разбор того же кода гоняется по копии ключом пробы `--corpus=`.

Плечи:
  A   — копия как есть (контроль копии: обязана дать ТЕ ЖЕ числа, что штатный корпус, побитово);
  Be  — G1S16 в сосудах: фон взят заново из `.spe` пустой защиты 2016 (контроль хирургии: обязан дать A побитово);
  Bw  — G1S16 в сосудах: фон = ИЗМЕРЕННЫЙ фон воды в маринелли 2016 (`фон вода_15.spe`, тот же прибор, сутки раньше);
  Ce  — G1S24 в цилиндрах (Дента/Петри): фон воды в маринелли 2024 × (пусто/вода)₂₀₁₆(E) — оценка фона ПУСТОЙ
        защиты, каким ЛСРМ в 2016 вычитала Денту и Петри (`фон пустая защита_15_Дента-100.spe`, `_Петри-60.spe`);
  D   — гипотеза П338 / `AMBER235` как она есть: фон × r(E) чистого ослабления пробой против среды фона,
        r = f·2E₃(μ(E)·t) + (1 − f), μ — XCOM из `matdb` (только чтение), t и f — по сосуду (см. `R_MODEL`);
  E0  — ряд Ra-226 БЕЗ Pb-210: `chains` Ra-226 → Rn-222 (подряд до Pb-210, T259), Ra-226 отдельным нуклидом;
  E1  — то же, плюс Pb-210 отдельным нуклидом со СВОЕЙ амплитудой (сколько Pb-210 в эталоне — скажет фит).

    python handover/p342-amber153/mk_arms.py <плечо> [<плечо> …]
"""
import csv
import io
import math
import os
import shutil
import sqlite3
import sys

import numpy as np
from scipy.special import expn

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, 'tools', 'CORPUS', 'scripts'))
import corpus_xml as cx  # noqa: E402
from spe_import import read_spe  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

CORPUS = os.path.join(REPO, 'tools', 'CORPUS', 'corpus')
LANE = r'D:\BqMoni_Claude\p342'
POV = r'D:\YandexDisk\Спектры\Спектры источники эталоны\Spe - поверки'
WATER16 = os.path.join(POV, 'Поверка 2016', 'Фон вода', 'фон вода_15.spe')
EMPTY16 = os.path.join(POV, 'Поверка 2016', 'фон пустая защита', 'фон пустая защита_15.spe')
MATDB = os.path.join(REPO, 'BecquerelMonitor', 'matdb.sqlite')
GEO_CSV = os.path.join(REPO, 'tools', 'CORPUS', 'data', 'lsrm_spectrum_geometry.csv')
LIST = os.path.join(HERE, 'list_vessels.csv')

#: Модель чистого ослабления (плечо D). Маринелли ОМАСН: слой пробы сбоку 24.5 мм (75 − 50.5), над колодцем
#: 19 мм — берётся 2.2 см; доля внешнего поля, идущая к кристаллу СКВОЗЬ пробу, f = 0.8 (кристалл весь в колодце,
#: мимо пробы — только крутые лучи снизу). Цилиндр на торце: слой = высота засыпки сцены, f = 0.25 (торец 3117 мм²
#: против бока 12469 мм² при изотропном поле — 0.2, плюс крутые лучи сверху сквозь бок). Погрешность модели —
#: см. журнал: проверка той же формулой «вода против пусто» против ИЗМЕРЕННОГО отношения 2016.
R_MODEL = dict(mar_t_cm=2.2, mar_f=0.8, cyl_f=0.25)
WATER = {1: 0.111899, 8: 0.888101}


class Xcom(object):
    u"""μ/ρ (см²/г) смеси по `matdb.xcom_cross_sections` — только чтение; полное ослабление с когерентным."""

    def __init__(self):
        self.c = sqlite3.connect('file:%s?mode=ro' % MATDB.replace('\\', '/'), uri=True)
        self.cache = {}

    def mu_rho_z(self, z, e_kev):
        if z not in self.cache:
            a = self.c.execute('select atomic_weight from xcom_elements where z=?', (z,)).fetchone()[0]
            rows = self.c.execute('select energy_ev, coherent_b+incoherent_b+photoelectric_b+pair_nuclear_b+'
                                  'pair_electron_b from xcom_cross_sections where z=? order by energy_ev',
                                  (z,)).fetchall()
            e = np.array([r[0] for r in rows]) / 1000.0
            s = np.array([r[1] for r in rows])
            self.cache[z] = (a, e, s)
        a, e, s = self.cache[z]
        # лог-лог между узлами; у края поглощения два узла одной энергии — берётся верхняя ветвь
        i = np.searchsorted(e, e_kev, side='right')
        i = min(max(i, 1), len(e) - 1)
        e0, e1, s0, s1 = e[i - 1], e[i], s[i - 1], s[i]
        if e1 == e0:
            sig = s1
        else:
            sig = math.exp(math.log(s0) + (math.log(s1) - math.log(s0)) * (math.log(e_kev) - math.log(e0)) /
                           (math.log(e1) - math.log(e0)))
        return sig * 0.60221408 / a

    def mu_rho(self, comp, e_kev):
        tot = sum(comp.values())
        return sum(w / tot * self.mu_rho_z(z, e_kev) for z, w in comp.items())


def t_slab(mu_t):
    u"""Пропускание слоя при косинусном (изотропном) падении: 2·E₃(μt)."""
    return 2.0 * expn(3, mu_t) if mu_t > 0 else 1.0


def compositions():
    out = {}
    for r in csv.DictReader(io.open(GEO_CSV, encoding='utf-8-sig')):
        comp = {}
        for part in (r['состав_Z_доля'] or '').split():
            z, w = part.split(':')
            comp[int(z)] = float(w)
        rho = float(r['плотность']) if r['плотность'] else None
        out[r['спектр']] = (comp, rho)
    return out


def keys_of_list():
    rows = [r for r in csv.DictReader(io.open(LIST, encoding='utf-8')) if r['part'] == 'known']
    return [r['spectrum'] for r in rows if '_P5' not in r['spectrum']]


def copy_corpus(dst):
    if os.path.exists(dst):
        raise SystemExit('⛔ каталог плеча уже есть: %s — снять руками или взять другое имя' % dst)
    os.makedirs(dst)
    for name in os.listdir(CORPUS):
        src = os.path.join(CORPUS, name)
        if os.path.isfile(src):
            shutil.copy2(src, os.path.join(dst, name))
    for sub in ('spectra', 'devices', 'pinned'):
        shutil.copytree(os.path.join(CORPUS, sub), os.path.join(dst, sub))


def bg_energies(d):
    cal = d['bg']['cal']
    return np.array([cx.energy(cal, float(ch)) for ch in range(len(d['bg']['counts']))])


def ratio_16():
    u"""(пусто/вода)₂₀₁₆ по энергии — полосами, сглажено; вне 20…2800 кэВ — края полос."""
    hw, cw = read_spe(WATER16, require_check=False)
    he, ce = read_spe(EMPTY16, require_check=False)
    lw, le = float(hw['TLIVE']), float(he['TLIVE'])
    cal = [float(x) for x in he['ENERGY'].replace(',', ' ').split()][1:]
    e = np.array([cal[0] + cal[1] * ch for ch in range(len(ce))])
    edges = [20, 30, 40, 50, 60, 70, 80, 90, 100, 120, 150, 200, 250, 300, 400, 500, 700, 1000, 1300, 1700,
             2200, 2800]
    cen, val = [], []
    for e0, e1 in zip(edges[:-1], edges[1:]):
        m = (e >= e0) & (e < e1)
        sw, se = float(np.sum(np.array(cw)[m])), float(np.sum(np.array(ce)[m]))
        cen.append(math.sqrt(e0 * e1))
        val.append((se / le) / (sw / lw))
    cen, val = np.array(cen), np.array(val)
    return lambda x: np.interp(np.log(np.clip(x, 1.0, None)), np.log(cen), val, left=val[0], right=val[-1])


def arm_be_bw(dst, which):
    path = EMPTY16 if which == 'Be' else WATER16
    head, counts = read_spe(path, require_check=False)
    live, real = float(head['TLIVE']), float(head['TREAL'])
    rows = []
    for key in keys_of_list():
        if not key.startswith('G1S16'):
            continue
        p = os.path.join(dst, 'spectra', key + '.xml')
        d = cx.read(p)
        old = d['bg']['counts']
        text, bom = cx.load_text(p)
        text = cx.replace_background(text, counts, live=live, real=real)
        cx.save_text(p, text, bom)
        same = all(int(a) == int(b) for a, b in zip(old, counts))
        rows.append([key, os.path.basename(path), '%.3f' % live, int(sum(old)), int(sum(counts)), int(same)])
        print('  %-24s фон ← %s (LT %.1f с), отсчётов %d → %d, тот же массив: %s' % (
            key, os.path.basename(path), live, sum(old), sum(counts), 'да' if same else 'нет'))
    return rows


def arm_ce(dst):
    s = ratio_16()
    rows = []
    for key in keys_of_list():
        if not (key.startswith('G1S24') and ('Denta' in key or 'Petri' in key)):
            continue
        p = os.path.join(dst, 'spectra', key + '.xml')
        d = cx.read(p)
        e = bg_energies(d)
        old = np.array(d['bg']['counts'])
        new = np.rint(old * s(e)).astype(int)
        text, bom = cx.load_text(p)
        cx.save_text(p, cx.replace_background(text, list(new)), bom)
        probe = [float(s(np.array([x]))[0]) for x in (40, 60, 100, 200, 662, 1461, 2614)]
        rows.append([key] + ['%.4f' % v for v in probe] + [int(old.sum()), int(new.sum())])
        print('  %-24s фон × (пусто/вода)₂₀₁₆: 40/60/100/200/662/1461/2614 кэВ — %s; отсчётов %d → %d' % (
            key, ' '.join('%.3f' % v for v in probe), old.sum(), new.sum()))
    return rows


def r_model(xc, comp, rho, t_cm, f, e):
    return f * t_slab(xc.mu_rho(comp, e) * rho * t_cm) + (1.0 - f)


def arm_d(dst):
    xc = Xcom()
    comps = compositions()
    rows = []
    for key in keys_of_list():
        p = os.path.join(dst, 'spectra', key + '.xml')
        d = cx.read(p)
        comp, rho = comps[key]
        if not comp or not rho:
            print('  ⚠ %s: нет состава/плотности в lsrm_spectrum_geometry.csv — не тронут' % key)
            continue
        mar = '_Mar' in key
        if mar:
            t_cm, f = R_MODEL['mar_t_cm'], R_MODEL['mar_f']
        else:
            t_cm, f = float(d['geo']['SourceHeight']) / 10.0, R_MODEL['cyl_f']
        epoch24 = key.startswith('G1S24')
        e = bg_energies(d)
        scale = []
        for x in e:
            x = max(x, 15.0)
            rs = r_model(xc, comp, rho, t_cm, f, x)
            if epoch24:
                # среда фона 2024 — литр воды в маринелли: делим на её пропускание той же формулой
                rw = r_model(xc, WATER, 1.0, R_MODEL['mar_t_cm'], R_MODEL['mar_f'], x)
                rs = rs / rw
            scale.append(rs)
        scale = np.array(scale)
        old = np.array(d['bg']['counts'])
        new = np.rint(old * scale).astype(int)
        text, bom = cx.load_text(p)
        cx.save_text(p, cx.replace_background(text, list(new)), bom)
        pts = (40, 60, 100, 200, 662, 1461, 2614)
        probe = []
        for x in pts:
            rs = r_model(xc, comp, rho, t_cm, f, x)
            if epoch24:
                rs /= r_model(xc, WATER, 1.0, R_MODEL['mar_t_cm'], R_MODEL['mar_f'], x)
            probe.append(rs)
        rows.append([key, '%.3f' % rho, '%.2f' % t_cm, '%.2f' % f] + ['%.4f' % v for v in probe] +
                    [int(old.sum()), int(new.sum())])
        print('  %-24s ρ %.2f t %.2f см f %.2f: r(40/60/100/200/662/1461/2614) %s; отсчётов %d → %d' % (
            key, rho, t_cm, f, ' '.join('%.3f' % v for v in probe), old.sum(), new.sum()))
    # проверка модели на измеренном: вода против пусто 2016
    print('  проверка модели: «вода/пусто» формулой r(E) против ИЗМЕРЕННОГО 2016 — см. bg_water_vs_empty.txt')
    for x in (35, 55, 75, 95, 135, 250, 350, 600, 850, 1250, 1750, 2400):
        print('    %5d кэВ: модель %.3f' % (x, r_model(xc, WATER, 1.0, R_MODEL['mar_t_cm'], R_MODEL['mar_f'], x)))
    return rows


def arm_e(dst, with_pb):
    man = os.path.join(dst, 'manifest.csv')
    raw = io.open(man, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    lines = text.splitlines(True)
    rdr = list(csv.reader(io.StringIO(text)))
    hdr = rdr[0]
    ic, inn = hdr.index('chains'), hdr.index('nuclides')
    targets = [k for k in keys_of_list() if 'Ra226' in k]
    out_rows = [hdr]
    changed = []
    for r in rdr[1:]:
        if r and r[0] in targets:
            if r[ic] != 'Ra-226' or r[inn]:
                raise SystemExit('⛔ %s: chains=%r nuclides=%r — ждал «Ra-226» и пусто' % (r[0], r[ic], r[inn]))
            r = list(r)
            r[ic] = 'Rn-222'
            r[inn] = '226RA;210PB' if with_pb else '226RA'
            changed.append(r[0])
        out_rows.append(r)
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator='\r\n' if lines[0].endswith('\r\n') else '\n')
    w.writerows(out_rows)
    data = buf.getvalue().encode('utf-8')
    io.open(man, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + data)
    for k in changed:
        print('  %-24s chains Ra-226 → Rn-222, nuclides → %s' % (k, '226RA;210PB' if with_pb else '226RA'))
    return [[k] for k in changed]


def main():
    for arm in sys.argv[1:]:
        dst = os.path.join(LANE, 'corpus_' + arm)
        print('== плечо %s → %s' % (arm, dst))
        copy_corpus(dst)
        if arm == 'A':
            rows = []
        elif arm in ('Be', 'Bw'):
            rows = arm_be_bw(dst, arm)
        elif arm == 'Ce':
            rows = arm_ce(dst)
        elif arm == 'D':
            rows = arm_d(dst)
        elif arm == 'E0':
            rows = arm_e(dst, False)
        elif arm == 'E1':
            rows = arm_e(dst, True)
        else:
            raise SystemExit('неизвестное плечо ' + arm)
        with io.open(os.path.join(HERE, 'arm_%s.csv' % arm), 'w', encoding='utf-8-sig', newline='') as fh:
            csv.writer(fh).writerows(rows)


if __name__ == '__main__' and not (len(sys.argv) > 1 and sys.argv[1] == 'P2'):
    main()


def arm_p2(dst):
    u"""Положительный контроль чувствительности: фон ×2 у ВСЕХ сосудов списка (заведомо плохой вход)."""
    rows = []
    for key in keys_of_list():
        p = os.path.join(dst, 'spectra', key + '.xml')
        d = cx.read(p)
        new = [int(round(2 * c)) for c in d['bg']['counts']]
        text, bom = cx.load_text(p)
        cx.save_text(p, cx.replace_background(text, new), bom)
        rows.append([key, int(sum(d['bg']['counts'])), int(sum(new))])
    print('  фон ×2 у %d спектров' % len(rows))
    return rows


if __name__ == '__main__' and len(sys.argv) > 1 and sys.argv[1] == 'P2':
    dst = os.path.join(LANE, 'corpus_P2')
    copy_corpus(dst)
    rows = arm_p2(dst)
    with io.open(os.path.join(HERE, 'arm_P2.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
        csv.writer(fh).writerows(rows)
