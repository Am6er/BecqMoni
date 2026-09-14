# -*- coding: utf-8 -*-
"""П71 (S170): прямой замер пиков Co-60 в спектрах корпуса — без модели.
Для 1173, 1332 и сумм-пика: канал центроида, энергия по калибровке спектра,
ПШПВ, чистая площадь над линейной подложкой; паспортная активность на дату
съёмки; измеренная пиковая эффективность против узла кривой из <Efficiency>.
Запуск: python peaks.py <spectrum.xml> [...]
"""
import io, sys, math, re, datetime
import xml.etree.ElementTree as ET

HALF_CO60_D = 1925.28  # сут (5.2711 лет)
I_1173, I_1332 = 0.9985, 0.99983

def load(path):
    t = ET.parse(path).getroot()
    rd = t.find('.//ResultData')
    es = rd.find('EnergySpectrum')
    coef = [float(c.text) for c in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    data = [int(x.text) for x in es.findall('Spectrum/DataPoint')]
    live = float(es.findtext('LiveTime')); real = float(es.findtext('MeasurementTime'))
    note = rd.findtext('SampleInfo/Note') or ''
    when = rd.findtext('SampleInfo/Time')
    eff = []
    for p in rd.findall('Efficiency/Curve/ROIEfficiencyData'):
        eff.append((float(p.findtext('Energy')), float(p.findtext('Efficiency'))))
    return coef, data, live, real, note, when, eff

def energy(coef, ch):
    return sum(c * ch ** i for i, c in enumerate(coef))

def channel(coef, e):
    lo, hi = 0.0, 4096.0
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        if energy(coef, mid) < e: lo = mid
        else: hi = mid
    return 0.5 * (lo + hi)

def eff_at(eff, e):
    for i in range(1, len(eff)):
        if e <= eff[i][0]:
            e0, v0 = eff[i - 1]; e1, v1 = eff[i]
            t = (math.log(e) - math.log(e0)) / (math.log(e1) - math.log(e0))
            return math.exp(math.log(v0) + t * (math.log(v1) - math.log(v0)))
    return eff[-1][1]

def peak(coef, data, e_guess, half_kev, bg_kev):
    """Центроид, ПШПВ и чистая площадь: окно ±half_kev вокруг максимума в ±half_kev от e_guess,
    подложка — среднее по bg_kev с каждой стороны, линейно."""
    c0 = channel(coef, e_guess)
    w = channel(coef, e_guess + half_kev) - c0
    lo, hi = int(c0 - w), int(c0 + w) + 1
    imax = max(range(lo, hi), key=lambda i: data[i])
    # окно вокруг максимума
    c0 = imax; lo, hi = int(c0 - w), int(c0 + w) + 1
    bw = max(2, int(channel(coef, e_guess + bg_kev) - channel(coef, e_guess)))
    bl = sum(data[lo - bw:lo]) / bw; br = sum(data[hi:hi + bw]) / bw
    n = hi - lo
    net = 0.0; cen = 0.0
    for k, i in enumerate(range(lo, hi)):
        b = bl + (br - bl) * (k + 0.5) / n
        v = data[i] - b
        net += v; cen += v * i
    cen /= net if net else 1.0
    # ПШПВ по полувысоте над подложкой (линейная интерполяция)
    bmax = bl + (br - bl) * (imax - lo + 0.5) / n
    h = data[imax] - bmax
    def cross(step):
        i = imax
        while 0 < i < len(data) - 1:
            b = bl + (br - bl) * (i - lo + 0.5) / n
            if data[i] - b < h / 2:
                i2 = i - step
                b2 = bl + (br - bl) * (i2 - lo + 0.5) / n
                v1, v2 = data[i2] - b2, data[i] - b
                return i2 + (v1 - h / 2) / (v1 - v2) * step
            i += step
        return float('nan')
    fw_ch = cross(1) - cross(-1)
    return dict(lo=lo, hi=hi, imax=imax, cen_ch=cen, cen_kev=energy(coef, cen), net=net,
                fwhm_kev=energy(coef, cen + fw_ch / 2) - energy(coef, cen - fw_ch / 2),
                fwhm_ch=fw_ch, bl=bl, br=br, gross=sum(data[lo:hi]))

def activity(note, when):
    m = re.search(r'A=(\d+)\s*Бк\s*dA=([\d.]+)%\s*(\d\d)-(\d\d)-(\d{4})', note)
    if not m:
        return None, None, None
    a0 = float(m.group(1)); da = float(m.group(2))
    d0 = datetime.date(int(m.group(5)), int(m.group(4)), int(m.group(3)))
    d1 = datetime.datetime.fromisoformat(when[:19]).date()
    dt = (d1 - d0).days
    return a0 * 2 ** (-dt / HALF_CO60_D), da, dt

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    for path in sys.argv[1:]:
        coef, data, live, real, note, when, eff = load(path)
        print('==', path.split('\\')[-1].split('/')[-1])
        print('  калибровка %s; live %.1f, real %.1f, мёртвое %.2f %%; всего %d отсчётов, %.0f имп/с' % (
            ' '.join('%.6g' % c for c in coef), live, real, 100 * (1 - live / real), sum(data), sum(data) / live))
        a, da, dt = activity(note, when)
        if a:
            print('  паспорт: A=%.0f Бк (±%.0f %%) на дату съёмки %s (%d сут распада)' % (a, da, when[:10], dt))
        out = {}
        for name, e, half, bg in (('1173', 1173.2, 60, 30), ('1332', 1332.5, 60, 30), ('sum', 2505.7, 110, 40)):
            p = peak(coef, data, e, half, bg)
            out[name] = p
            print('  %-4s каналы %d..%d (макс %d) центроид %.2f кан = %.1f кэВ; ПШПВ %.1f кэВ (%.2f %%, %.1f кан); '
                  'gross %d, подложка %.1f/%.1f, net %.0f' % (
                      name, p['lo'], p['hi'], p['imax'], p['cen_ch'], p['cen_kev'], p['fwhm_kev'],
                      100 * p['fwhm_kev'] / p['cen_kev'], p['fwhm_ch'], p['gross'], p['bl'], p['br'], p['net']))
        # линейная шкала по двум пикам Co-60: куда падает сумма по каналу
        c1, c2 = out['1173']['cen_ch'], out['1332']['cen_ch']
        g = (1332.492 - 1173.228) / (c2 - c1); z = 1173.228 - g * c1
        cs = out['sum']['cen_ch']
        print('  аффинная шкала по 1173/1332: усиление %.4f кэВ/кан, ноль %.2f кэВ; сумм-пик по ней = %.1f кэВ; '
              'ожидание канала 2505.72 = %.2f; сдвиг сумм-пика %+.2f кан' % (g, z, g * cs + z, (2505.72 - z) / g,
                                                                          cs - (2505.72 - z) / g))
        if a and eff:
            for name, e, I in (('1173', 1173.228, I_1173), ('1332', 1332.492, I_1332)):
                meas = out[name]['net'] / (a * live * I)
                print('  ε_p(%s) измерено = %.5g (без поправки CF); узел кривой %.5g; кривая/измерено = %.3f' % (
                    name, meas, eff_at(eff, e), eff_at(eff, e) / meas))
            ep = eff_at(eff, 1173.228) * eff_at(eff, 1332.492)
            n_sum_model = a * live * ep * I_1173
            print('  сумм-пик: ε_p·ε_p кривой = %.4g → %.0f отсчётов при паспорте (без κ, W, выживания); измерено net %.0f; '
                  'отношение %.3f' % (ep, n_sum_model, out['sum']['net'], n_sum_model / out['sum']['net']))
