# -*- coding: utf-8 -*-
r"""П85 (AMBER42): чтение логов g4cf ион-режима — пары off/on (без ключа corr / с ключом).

    python read_g4.py D:\BqMoni_Claude\p85\g4 [--nuc co60,y88,cs134,eu152]

Для каждого окна: отсчёты ВЫКЛ/ВКЛ, отношение ВКЛ/ВЫКЛ с пуассоновой σ. Для ЛИНИЙ это обратное
отношение CF (CF = p·ε_пик/ε_каж, моно сокращается): CF_ВКЛ/CF_ВЫКЛ = eps_off/eps_on.
Для окна Y-88 2734.1 вычитается собственная линия 2734.0 (p·ε_пик из g4_mono_2734.log, p = 0.00715).
Положительный контроль: строка SETUP correlatedGamma=… обязана быть 0 у off и 1 у on.
"""
import io, math, os, re, sys

SUMS = {'co60': [2505.7], 'y88': [2734.1], 'cs134': [1400.6, 1365.2, 1168.0],
        'eu152': [1529.8, 1123.2, 1233.9, 1085.8]}
P_Y88_2734 = 0.00715

def read(path):
    # path — основной лог; рядом складываются доборы другими зёрнами: <имя>_s<N>.log (разные потоки ГСЧ).
    # В логе может лежать два одинаковых прогона (повтор cmd, §6 журнала) — берётся ПОСЛЕДНИЙ блок RESULT.
    import glob
    files = ([path] if os.path.exists(path) else []) + sorted(glob.glob(path[:-4] + '_s*.log'))
    if not files:
        return None
    decays, wins, setup, rdm, seeds = 0, {}, None, None, []
    for f in files:
        d, w, s = None, {}, None
        for line in io.open(f, encoding='utf-8', errors='replace'):
            m = re.match(r'RESULT decays=(\d+)', line)
            if m: d = int(m.group(1)); w = {}
            m = re.match(r'RESULT window=([\d.]+) counts=(\d+) eps=([\deE.+-]+)', line)
            if m: w[float(m.group(1))] = int(m.group(2))
            m = re.match(r'SETUP correlatedGamma=(\d).*?(?:seed=(\d+))?\s*$', line)
            if m: setup = int(m.group(1)); s = m.group(2)
            m = re.match(r'Enable correlated gamma emission\s+(\d)', line)
            if m: rdm = int(m.group(1))
        if d:
            decays += d; seeds.append(s or '0')
            for k, v in w.items(): wins[k] = wins.get(k, 0) + v
    wins = {k: (v, v / decays if decays else 0.0) for k, v in wins.items()}
    return dict(decays=decays, wins=wins, setup=setup, rdm=rdm, files=len(files), seeds=seeds)

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    d = sys.argv[1]
    nucs = 'co60,y88,cs134,eu152'
    for a in sys.argv[2:]:
        if a.startswith('--nuc='): nucs = a[6:]
    mono = read(os.path.join(d, 'g4_mono_2734.log'))
    eps2734 = None
    if mono and mono['wins']:
        c, e = mono['wins'][min(mono['wins'], key=lambda w: abs(w - 2734.0))]
        eps2734 = e
    for nuc in nucs.split(','):
        off = read(os.path.join(d, 'g4_ion_%s_off.log' % nuc))
        on = read(os.path.join(d, 'g4_ion_%s_on.log' % nuc))
        if not off or not on or not off['wins'] or not on['wins']:
            print('== %s: логов ещё нет или пусты' % nuc); continue
        print('== %s: распадов ВЫКЛ %d (зёрна %s), ВКЛ %d (зёрна %s); SETUP correlatedGamma ВЫКЛ=%s ВКЛ=%s; RDM «Enable correlated» ВЫКЛ=%s ВКЛ=%s'
              % (nuc, off['decays'], ','.join(off['seeds']), on['decays'], ','.join(on['seeds']), off['setup'], on['setup'], off['rdm'], on['rdm']))
        if off['setup'] != 0 or on['setup'] != 1:
            print('   ⛔ ФЛАГ НЕ ДОЕХАЛ: SETUP должен быть 0/1')
        print('   %-8s %10s %10s %8s %8s %8s   %s' % ('окно', 'ВЫКЛ', 'ВКЛ', 'ВКЛ/ВЫКЛ', 'σ', 'σ-ед.', 'что'))
        for w in sorted(off['wins']):
            c0, e0 = off['wins'][w]; c1, e1 = on['wins'].get(w, (0, 0.0))
            n0, n1 = off['decays'], on['decays']
            # на распад
            r0, r1 = c0 / n0, c1 / n1
            s0, s1 = (math.sqrt(c0) / n0 if c0 else 0), (math.sqrt(c1) / n1 if c1 else 0)
            what = 'сумм-пик' if w in SUMS.get(nuc, []) else 'линия'
            if nuc == 'y88' and abs(w - 2734.1) < 0.05 and eps2734:
                own = P_Y88_2734 * eps2734
                r0 -= own; r1 -= own
                what += ' (минус линия 2734: p·ε=%.3e)' % own
            if r0 <= 0 or r1 <= 0:
                continue
            ratio = r1 / r0
            sr = math.hypot(s0 / r0, s1 / r1)
            if what.startswith('линия'):
                # отношение CF: обратное к кажущейся
                cfr = r0 / r1
                print('   %-8.1f %10d %10d %8.4f %8.4f %8.1f   CF ВКЛ/ВЫКЛ (=eps_off/eps_on), окно %s' % (w, c0, c1, cfr, cfr * sr, (cfr - 1) / sr, what))
            else:
                print('   %-8.1f %10d %10d %8.4f %8.4f %8.1f   %s' % (w, c0, c1, ratio, ratio * sr, (ratio - 1) / sr, what))

if __name__ == '__main__':
    main()
