# -*- coding: utf-8 -*-
"""П101: слить два лога `g4cf ionhist` (разные зёрна) в один — суммы гистограмм, окон и распадов.

    python g4_merge.py <out.log> <in1.log> <in2.log> [...]

Печатает в out.log строки SETUP каждого входа (с пометкой), RESULT decays/any/window и HISTBEGIN/HIST/HISTEND
с суммами. Шаг и число бинов у входов обязаны совпадать — иначе отказ кодом 2.
"""
import io, re, sys
out = sys.argv[1]; ins = sys.argv[2:]
decays = 0; anyc = 0; windows = {}; hist = {}; binkev = None; nbins = None; setups = []
for path in ins:
    with io.open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            if line.startswith('SETUP'):
                setups.append('%s  # %s' % (line.rstrip(), path))
            elif line.startswith('RESULT decays='):
                decays += int(line.split('=')[1])
            elif line.startswith('RESULT any='):
                anyc += int(re.search(r'any=(\d+)', line).group(1))
            elif line.startswith('RESULT window='):
                m = re.search(r'window=([\d.]+) counts=(\d+)', line)
                windows[m.group(1)] = windows.get(m.group(1), 0) + int(m.group(2))
            elif line.startswith('HISTBEGIN'):
                m = re.search(r'bins=(\d+) bin_kev=([\d.]+)', line)
                b, k = int(m.group(1)), float(m.group(2))
                if binkev is None:
                    binkev, nbins = k, b
                elif (k, b) != (binkev, nbins):
                    print('шаг/число бинов расходятся: %s' % path); sys.exit(2)
            elif line.startswith('HIST '):
                _, i, n = line.split(); hist[int(i)] = hist.get(int(i), 0) + int(n)
with io.open(out, 'w', encoding='utf-8') as f:
    for s in setups: f.write(s + '\n')
    f.write('RESULT decays=%d\n' % decays)
    f.write('RESULT any=%d eps_total=%.6e\n' % (anyc, anyc / decays if decays else 0.0))
    for w in sorted(windows, key=float):
        f.write('RESULT window=%s counts=%d eps=%.6e\n' % (w, windows[w], windows[w] / decays if decays else 0.0))
    f.write('HISTBEGIN bins=%d bin_kev=%.6f decays=%d\n' % (nbins, binkev, decays))
    for i in sorted(hist):
        f.write('HIST %d %d\n' % (i, hist[i]))
    f.write('HISTEND\n')
print('слито %d логов: распадов %d, окон %d, бинов с отсчётами %d -> %s' % (len(ins), decays, len(windows), len(hist), out))
