# П186: ширина пика данных против модели (в каналах, моменты по окну ±w каналов от центра модели),
# энергия калибровки файла в центре данных и ПШПВ кривой файла в этом канале.
# python width.py <каталог_кривых> спектр:E_линии:E_свет [...]    (E_свет — линия + light_shift из anchors.csv)
import csv, sys, os, re, math
R = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
d = sys.argv[1]
def fcal(key):
    t = open(os.path.join(R, key + '.xml'), encoding='utf-8-sig').read()
    blk = t.split('<EnergyCalibration>')[1].split('</EnergyCalibration>')[0]
    co = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', blk)]
    pw = re.search(r'<PowerFwhmCalibration>.*?<Coefficient>([^<]+)</Coefficient><Coefficient>([^<]+)</Coefficient>', t, re.S)
    return co, (float(pw.group(1)), float(pw.group(2))) if pw else None
print('%-18s %7s %7s | %6s %6s %6s | %7s %7s %6s | %6s %6s' % ('спектр', 'E_лин', 'E_свет', 'ц_дан', 'σ_дан', 'σ_мод', 'E_файл', 'ПШПВ_ф', 'отн', 'σд/σм', '(Eсв/Eф)^p'))
for arg in sys.argv[2:]:
    key, el, els = arg.split(':'); el = float(el); els = float(els)
    rows = list(csv.DictReader(open(os.path.join(d, key + '_curves.csv'))))
    co, pw = fcal(key)
    E = lambda ch: sum(c * ch ** i for i, c in enumerate(co))
    n = [float(r['fit']) for r in rows]; m = [float(r['model']) for r in rows]
    # канал модели, ближайший к световой энергии по калибровке файла — затравка; окно ±3 канала от максимума модели
    ch0 = min(range(len(rows)), key=lambda i: abs(float(rows[i]['keV']) - els))
    top = max(range(max(0, ch0 - 3), ch0 + 4), key=lambda i: m[i])
    w = 3 if top < 40 else int(round(1.2 * pw[0] * top ** pw[1])) if pw else 3
    lo, hi = top - w, top + w
    def mom(v):
        s = sum(v[i] for i in range(lo, hi + 1)); c = sum(i * v[i] for i in range(lo, hi + 1)) / s
        return c, math.sqrt(sum((i - c) ** 2 * v[i] for i in range(lo, hi + 1)) / s)
    cd, sd = mom(n); cm, sm = mom(m)
    ef = E(cd); slope = E(cd + 0.5) - E(cd - 0.5)
    fw = pw[0] * cd ** pw[1] if pw else float('nan')
    p = pw[1] if pw else 0.64
    print('%-18s %7.2f %7.2f | %6.2f %6.3f %6.3f | %7.2f %7.2f %5.1f%% | %6.3f %6.3f' % (key, el, els, cd, sd, sm, ef, fw * slope, 100 * fw * slope / ef, sd / sm, (els / ef) ** p))
