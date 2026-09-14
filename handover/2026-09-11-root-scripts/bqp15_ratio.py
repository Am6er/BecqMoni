# -*- coding: utf-8 -*-
u"""bqp15_ratio.py — П15 / A307(б): различитель «порог тракта против транспорта».

Отношение данные/модель (y/model_A, y/mB0 = колонки Б при амплитудах А; y = raw − α·bg — то, к чему фит подгоняет
модель; ⚠ raw/model без вычитания фона врёт ×2…3 у спектров на 25 см, где фон — 30…60 % отсчётов) как функция
энергии КАНАЛА — по всем спектрам одного прибора; двумя шкалами: (1) по КАНАЛУ (амплитуда импульса — то,
что видит дискриминатор; у спектров одного прибора один гейн: 662 кэВ на ch 228…233 у G1S16), (2) по кэВ
калибровки спектра, как считала П12 (полосы 2.5 кэВ). Мера совпадения между нуклидами в бине:
χ²_между/(n−1) = Σ_i (q_i − q̄)² / σ_i² / (n−1), σ_i = √raw_i / model_i (пуассон); ≈ 1 — кривые одна,
≫ 1 — расходятся по нуклидам. Группы: A — чистые до 100 кэВ (Co60, Na22, Mn54, Y88, Zn65);
B — рентген 20…45 кэВ (Cs137, Ba133, Ce139, Eu152, Am241, Cd109, Co57); C — рентген 55…90 кэВ
(Lu176, Bi207, Th228, Th232). Контроль (iii): синтетика П12 (syna/synb) — отношение ≡ 1, края нет.
Отдельно — БЕЗМОДЕЛЬНЫЙ край: raw/плато по каналам у группы A (модель тут не нужна вовсе).
"""
import csv, io, os, sys, glob
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, r'C:\Users\moroz')
from bqp15_cut import per_channel, known, EDGE
OUT = r'C:\Users\moroz\bqp15_out'
GROUP = {}
for n in ('Co60', 'Na22', 'Mn54', 'Y88', 'Zn65'): GROUP[n] = 'A'
for n in ('Cs137', 'Ba133', 'Ce139', 'Eu152', 'Am241', 'Cd109', 'Co57'): GROUP[n] = 'B'
for n in ('Lu176', 'Bi207', 'Th228', 'Th232WT20'): GROUP[n] = 'C'
CHRANGE = {'G1S16': range(5, 42), 'G1S24': range(5, 42), 'AS80': range(50, 330, 8), 'ASN16': range(4, 330, 8)}


def spread(qs, sig):
    qs = np.array(qs); sig = np.array(sig)
    ok = np.isfinite(qs) & np.isfinite(sig) & (sig > 0)
    qs, sig = qs[ok], sig[ok]
    if len(qs) < 2: return float('nan'), float('nan'), len(qs)
    wm = (qs / sig ** 2).sum() / (1 / sig ** 2).sum()
    chi = ((qs - wm) ** 2 / sig ** 2).sum() / (len(qs) - 1)
    return wm, chi, len(qs)


def main():
    keys = known()
    per = {k: per_channel(k) for k in keys}
    inst = {}
    for k in keys: inst.setdefault(k.split('_')[0], []).append(k)
    rep = io.open(os.path.join(OUT, 'ratio_tables.txt'), 'w', encoding='utf-8', newline='')
    def P(s=''):
        print(s); rep.write(s + '\n')

    for det, ks in inst.items():
        rng = list(CHRANGE[det])
        # --- (1) по КАНАЛУ: таблица raw/model_A на спектр
        P('\n=== %s: y/model_A по КАНАЛУ (амплитуда), спектры прибора; группа в скобках ===' % det)
        P('%-16s ' % 'канал' + ' '.join('%6d' % c for c in rng))
        for k in ks:
            d = per[k]
            q = np.where(d['mA'] > 0, d['y'] / np.maximum(d['mA'], 1e-9), np.nan)
            P('%-13s(%s) ' % (k[len(det) + 1:], GROUP[k.split('_')[1]]) + ' '.join(('%6.2f' % q[c]) if np.isfinite(q[c]) and abs(q[c]) < 100 and d['raw'][c] >= 5 else '     -' for c in rng))
        # мера совпадения по группам, по каналу
        for grp in 'ABC':
            sub = [k for k in ks if GROUP[k.split('_')[1]] == grp]
            if len(sub) < 2: continue
            P('--- %s группа %s (%d спектров): по каналу — q̄ | χ²_между/(n−1) ; для y/model_A и y/mB0' % (det, grp, len(sub)))
            P('%-6s %8s %10s   %8s %10s   %6s' % ('канал', 'q̄_A', 'χ²/(n−1)', 'q̄_B0', 'χ²/(n−1)', 'n'))
            for c in rng:
                qa, sa, qb, sb = [], [], [], []
                for k in sub:
                    d = per[k]
                    if d['mA'][c] > 0 and d['raw'][c] >= 5:
                        qa.append(d['y'][c] / d['mA'][c]); sa.append(np.sqrt(d['raw'][c]) / d['mA'][c])
                    if d['mB0'][c] > 0 and d['raw'][c] >= 5:
                        qb.append(d['y'][c] / d['mB0'][c]); sb.append(np.sqrt(d['raw'][c]) / d['mB0'][c])
                wa, ca, na = spread(qa, sa); wb, cb, nb = spread(qb, sb)
                P('%-6d %8.3f %10.1f   %8.3f %10.1f   %6d' % (c, wa, ca, wb, cb, na))
        # --- (2) по кэВ калибровки спектра, полосы 2.5 кэВ, 15…120 — как П12
        P('\n=== %s: y/model_A по кэВ КАЛИБРОВКИ спектра (полосы 2.5 кэВ, 15…120), q̄ и χ²_между/(n−1) по группам ===' % det)
        edges = np.arange(15.0, 122.5, 2.5)
        P('%-10s ' % 'кэВ' + '  '.join('%22s' % ('группа %s: q̄ χ²/(n−1) n' % g) for g in 'ABC'))
        for e0, e1 in zip(edges[:-1], edges[1:]):
            line = '%5.1f-%5.1f ' % (e0, e1)
            for grp in 'ABC':
                sub = [k for k in ks if GROUP[k.split('_')[1]] == grp]
                qs, ss = [], []
                for k in sub:
                    d = per[k]
                    m = (d['kev'] >= e0) & (d['kev'] < e1) & (d['mA'] > 0)
                    if m.sum() == 0: continue
                    R = d['raw'][m].sum(); M = d['mA'][m].sum(); Y = d['y'][m].sum()
                    if R >= 5 and M > 0:
                        qs.append(Y / M); ss.append(np.sqrt(R) / M)
                wm, chi, n = spread(qs, ss)
                line += '  %8.3f %8.1f %4d ' % (wm, chi, n)
            P(line)

    # --- безмодельный край: raw/плато по каналам, группа A, G1S16 и G1S24
    P('\n=== БЕЗМОДЕЛЬНЫЙ край: raw/плато по каналам, группа A (плато = среднее raw по ch2…ch3−1 прибора); χ²_между/(n−1) ===')
    for det in ('G1S16', 'G1S24'):
        c1, c2, c3 = EDGE[det]
        sub = [k for k in inst[det] if GROUP[k.split('_')[1]] == 'A']
        P('--- %s, %d спектров: %s' % (det, len(sub), ', '.join(k[len(det) + 1:] for k in sub)))
        P('%-6s ' % 'канал' + ' '.join('%9s' % k[len(det) + 1:] for k in sub) + '   %8s %10s' % ('q̄', 'χ²/(n−1)'))
        for c in range(c1 - 1, c3 + 2):
            qs, ss, txt = [], [], []
            for k in sub:
                raw = per[k]['raw'].astype(float)
                plat = raw[c2:c3].mean()
                q = raw[c] / plat; s = np.sqrt(max(raw[c], 1.0)) / plat
                qs.append(q); ss.append(s); txt.append('%9.3f' % q)
            wm, chi, n = spread(qs, ss)
            P('%-6d ' % c + ' '.join(txt) + '   %8.3f %10.1f' % (wm, chi))

    # --- контроль (iii): синтетика П12
    P('\n=== контроль (iii): синтетика П12 — raw_syn/model по каналам 3…24 (истина из модели Б; порога по построению нет) ===')
    for arm in ('synb', 'syna'):
        d = os.path.join(r'C:\Users\moroz\bqp12_out', arm + '_dump')
        for f in sorted(glob.glob(os.path.join(d, '*_chi.csv'))):
            rows = list(csv.DictReader(io.open(f, encoding='utf-8-sig')))
            raw = np.array([int(r['raw']) for r in rows]); mod = np.array([float(r['model']) for r in rows]); bg = np.array([float(r['bg']) for r in rows])
            y = raw - bg
            q = np.where(mod > 0, y / np.maximum(mod, 1e-9), np.nan)
            sel = [c for c in range(3, 25)]
            P('%-5s %-22s ' % (arm, os.path.basename(f)[:-8]) + ' '.join(('%5.2f' % q[c]) if np.isfinite(q[c]) and raw[c] >= 5 else '    -' for c in sel)
              + '   макс |q−1| при raw≥100 в ch 5…40: %.3f' % np.nanmax(np.abs(q[5:41][raw[5:41] >= 100] - 1)))
    rep.close()


if __name__ == '__main__':
    main()
