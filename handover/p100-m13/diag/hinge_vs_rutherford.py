# -*- coding: utf-8 -*-
# П100 (M13): диагноз ЧИСЛОМ — вероятность отклонения за ОДИН ШАГ переноса (0.1 остаточного пробега CSDA)
# у гауссова шарнира Хайленда (экспонента по 1-cos, среднее theta0^2) против однократного упругого
# рассеяния по экранированному Резерфорду (Вентцель, экранирование Мольера, Z(Z+1)) на тот же шаг.
# Пробеги CSDA — таблица ESTAR из matdb (star_stopping_powers), радиационная длина — Цай (как в коде).
import sqlite3, math, sys, io
sys.stdout.reconfigure(encoding='utf-8')
DB = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\matdb.sqlite'
ME = 510.99895; RE2 = (2.8179403e-13)**2; NA = 6.02214076e23; ALPHA = 1/137.035999
# состав: (Z, A, массовая доля), плотность
MATS = {
 'PTFE (C2F4)': ({6: (12.011, 0.24018), 9: (18.998, 0.75982)}, 2.2, 'POLYTETRAFLUOROETHYLENE (TEFLON)'),
 'Al':          ({13: (26.982, 1.0)}, 2.699, 'ALUMINUM'),
 'MgO':         ({12: (24.305, 0.60304), 8: (15.999, 0.39696)}, 3.58, 'MAGNESIUM OXIDE'),
 'CsI':         ({55: (132.905, 0.51155), 53: (126.904, 0.48845)}, 4.51, 'CESIUM IODIDE'),
}
def tsai_x0(z, a):
    if z == 1: lrad, lradp = 5.31, 6.144
    elif z == 2: lrad, lradp = 4.79, 5.621
    elif z == 3: lrad, lradp = 4.74, 5.805
    elif z == 4: lrad, lradp = 4.71, 5.924
    else: lrad, lradp = math.log(184.15*z**(-1/3)), math.log(1194.0*z**(-2/3))
    al = z/137.035999; a2 = al*al
    f = a2*(1/(1+a2) + 0.20206 - 0.0369*a2 + 0.0083*a2*a2 - 0.002*a2**3)
    return 716.408*a/(z*z*(lrad-f) + z*lradp)
def x0_of(comp):
    return 1.0/sum(w/tsai_x0(z, a) for z, (a, w) in comp.items())
def highland(t, xx0):
    g = 1 + t/ME; b2 = 1 - 1/g**2; p = math.sqrt(t*(t+2*ME)); bp = math.sqrt(b2)*p
    br = max(0.25, 1 + 0.038*math.log(xx0/b2))
    return 13600.0/bp*math.sqrt(xx0)*br
# ⚠ В matdb таблица STAR несёт только протоны и альфы (ESTAR считается кодом EstarCalculator), поэтому здесь —
# пробеги CSDA электронов ПРИБЛИЖЁННО (NIST ESTAR, по памяти, ±5 %); точные — печатает LayerReturnProbe --diag.
RANGES = {  # г/см²: 100 / 500 / 1000 кэВ
 'POLYTETRAFLUOROETHYLENE (TEFLON)': {100: 1.62e-2, 500: 2.00e-1, 1000: 5.00e-1},
 'ALUMINUM':                          {100: 1.87e-2, 500: 2.24e-1, 1000: 5.55e-1},
 'MAGNESIUM OXIDE':                   {100: 1.75e-2, 500: 2.15e-1, 1000: 5.35e-1},
 'CESIUM IODIDE':                     {100: 2.86e-2, 500: 3.08e-1, 1000: 7.30e-1},
}
def range_csda(star, t_kev):
    return RANGES[star][int(t_kev)]
def screening_A(z, t):
    g = 1 + t/ME; b2 = 1 - 1/g**2; b = math.sqrt(b2); bg = b*g
    chi0_half = ALPHA*z**(1/3)/(1.77*bg)       # hbar/(2 p a), a = 0.885 a0 Z^-1/3
    return chi0_half**2*(1.13 + 3.76*(ALPHA*z/b)**2)
def sigma_prefactor(z, t):
    g = 1 + t/ME; b2 = 1 - 1/g**2; p2 = t*(t+2*ME)
    return 2*math.pi*z*(z+1)*RE2*ME*ME/(b2*p2)   # dσ/dx = pref/(x+2A)^2, x = 1-cosθ
def per_step(name, t):
    comp, rho, star = MATS[name]
    R = range_csda(star, t); step_g = 0.1*R; step_cm = step_g/rho
    x0 = x0_of(comp); th0 = highland(t, step_g/x0); s = th0*th0
    out = ['%-12s %6.0f кэВ: пробег %.4f г/см², шаг %.5f см, θ0 = %.3f рад' % (name, t, R, step_cm, th0)]
    hdr = '   %-14s %10s %10s %10s %10s' % ('угол >', '30°', '60°', '90°', '120°')
    out.append(hdr)
    hinge, ruth, mfp = [], [], []
    for deg in (30, 60, 90, 120):
        xc = 1 - math.cos(math.radians(deg))
        ph = (math.exp(-xc/s) - math.exp(-2/s))/(1 - math.exp(-2/s)) if s < 50 else 1 - xc/2
        hinge.append(ph)
        nsig = 0.0
        for z, (a, w) in comp.items():
            n = rho*w/a*NA
            A = screening_A(z, t)
            nsig += n*sigma_prefactor(z, t)*(1/(xc+2*A) - 1/(2+2*A))
        ruth.append(1 - math.exp(-nsig*step_cm))
        mfp.append(1/nsig if nsig > 0 else float('inf'))
    out.append('   %-14s' % 'шарнир' + ''.join(' %10.2e' % v for v in hinge))
    out.append('   %-14s' % 'Резерфорд' + ''.join(' %10.2e' % v for v in ruth))
    out.append('   %-14s' % 'отношение' + ''.join(' %10.3g' % (r/h if h > 0 else float('inf')) for h, r in zip(hinge, ruth)))
    out.append('   %-14s' % 'λ_hard, см' + ''.join(' %10.4f' % v for v in mfp))
    # доля транспортного сечения выше отсечки (для смешанной схемы), Z(Z+1), экранирование Мольера
    parts = []
    for deg in (10, 20, 30):
        xc = 1 - math.cos(math.radians(deg))
        num = den = 0.0
        for z, (a, w) in comp.items():
            n = rho*w/a*NA; A = screening_A(z, t); pref = n*sigma_prefactor(z, t)
            den += pref*(math.log(1+1/A) - 1/(1+A))
            num += pref*(math.log((2+2*A)/(xc+2*A)) + 2*A/(2+2*A) - 2*A/(xc+2*A))
        parts.append('%d°: f_hard=%.3f' % (deg, num/den))
    # средний квадрат угла шага по Резерфорду (1-exp(-s/λ1)) против θ0²
    ntr = 0.0
    for z, (a, w) in comp.items():
        n = rho*w/a*NA; A = screening_A(z, t)
        ntr += n*sigma_prefactor(z, t)*(math.log(1+1/A) - 1/(1+A))
    out.append('   доля транспортного сечения выше отсечки: ' + ', '.join(parts) +
               ';  ⟨1−cos⟩ шага: Хайленд θ0²=%.4f, Резерфорд 1−exp(−s/λ₁)=%.4f (отн. %.2f)' % (s, 1-math.exp(-ntr*step_cm), (1-math.exp(-ntr*step_cm))/s))
    return '\n'.join(out)
if __name__ == '__main__':
    print('Вероятность отклонения за ОДИН шаг (0.1 CSDA) больше угла: гауссов шарнир Хайленда против однократного рассеяния по экранированному Резерфорду (Z(Z+1), Мольер)')
    for name in ('PTFE (C2F4)', 'Al', 'MgO', 'CsI'):
        for t in (100, 500, 1000):
            print(per_step(name, t))
        print()
