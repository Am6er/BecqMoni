# П187: профиль рампы низа в СЫРЫХ каналах по континууму спектров без линий ниже ~50 кэВ и по фонам:
# S(ch) = отсчёты(ch) / уровень (медиана каналов lvl_lo..lvl_hi); логистика 1/(1+exp(-(ch-c50)/w)) по точкам 0.05<S<0.95
import re, os, sys, math, glob
S = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
def spec(t, tag):
    es = t[t.find('<' + tag + '>'):]
    es = es[:es.find('</' + tag + '>')]
    sp = es[es.find('<Spectrum>'):es.find('</Spectrum>')]
    return [int(x) for x in re.findall(r'<DataPoint>(\d+)</DataPoint>', sp)]
def fit(d, lo=13, hi=18):
    lvl = sorted(d[lo:hi + 1])[(hi - lo) // 2]
    pts = []
    for c in range(4, lo):
        s = d[c] / lvl if lvl > 0 else 0
        if 0.05 < s < 0.95:
            pts.append((c, math.log(s / (1 - s))))
    if len(pts) < 2: return None
    n = len(pts); mx = sum(p[0] for p in pts) / n; my = sum(p[1] for p in pts) / n
    b = sum((p[0] - mx) * (p[1] - my) for p in pts) / sum((p[0] - mx) ** 2 for p in pts)
    c50 = mx - my / b
    return c50, 1 / b, lvl, ' '.join('%.2f' % (d[c] / lvl) for c in range(5, 14))
keys = sys.argv[1:]
seen = {}
for k in keys:
    t = open(os.path.join(S, k + '.xml'), encoding='utf-8-sig').read()
    r = fit(spec(t, 'EnergySpectrum'))
    print('%-18s c50 %.2f w %.2f уровень %7.0f | S(ch5..13) %s' % ((k,) + r) if r else k + ' нет')
    bn = re.search(r'<BackgroundSpectrumFile>([^<]*)<', t)
    bname = bn.group(1) if bn else '?'
    if bname not in seen and '<BackgroundEnergySpectrum>' in t:
        seen[bname] = 1
        r = fit(spec(t, 'BackgroundEnergySpectrum'))
        print('   фон %-40s c50 %.2f w %.2f уровень %7.0f | S %s' % ((bname[-40:],) + r) if r else '   фон ' + bname + ' нет')
