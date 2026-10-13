# П180 S198: окна Kα (29–34) и Kβ (34.5–39.5 кэВ) по кривым разбора (--dump-curves): n/m, Σ(n−m)²/m, Δ центра тяжести
# вызов: python kbwin.py <каталог_A> [<каталог_B>]   (n — столбец fit, m — model, как у П179)
import csv, sys, os
specs = ['G1S16_Ba133_P5','G1S24_Ba133_P5','G1S16_Ba133_P25','ASN16_Cs137','G1S16_Cs137_P5','G1S24_Cs137_P5',
         'G1S16_Cs137_P25','G1S24_Cs137_P25','G1S16_Ce139_P5','G1S16_Ce139_P25']
wins = [('Kα', 29.0, 34.0), ('Kβ', 34.5, 39.5)]
def stats(path, lo, hi):
    n = m = c = nw = mw = 0.0
    for r in csv.DictReader(open(path)):
        e = float(r['keV'])
        if lo <= e <= hi:
            a = float(r['fit']); b = float(r['model'])
            n += a; m += b; c += (a - b) ** 2 / max(b, 1.0); nw += a * e; mw += b * e
    return n / m if m else float('nan'), c, (nw / n - mw / m) if n and m else float('nan')
dirs = sys.argv[1:]
print('спектр'.ljust(18) + 'окно ' + ' | '.join(('%s: n/m     χ²    Δцт' % os.path.basename(d)).rjust(30) for d in dirs))
for s in specs:
    for w, lo, hi in wins:
        row = []
        for d in dirs:
            p = os.path.join(d, s + '_curves.csv')
            if not os.path.exists(p): row.append('—'.rjust(30)); continue
            a, b, cshift = stats(p, lo, hi)
            row.append(('%.4f %8.0f %+.3f' % (a, b, cshift)).rjust(30))
        print(s.ljust(18) + w.ljust(5) + ' | '.join(row))
