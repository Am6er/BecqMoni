# П187: низ спектра — первые каналы, энергия по калибровке файла
import re, sys, os
S = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
def load(k):
    t = open(os.path.join(S, k + '.xml'), encoding='utf-8-sig').read()
    es = t[t.find('<EnergySpectrum>'):]
    es = es[:es.find('</EnergySpectrum>')]
    cal = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', es[es.find('<EnergyCalibration>'):es.find('</EnergyCalibration>')])]
    sp = es[es.find('<Spectrum>'):es.find('</Spectrum>')]
    d = [int(x) for x in re.findall(r'<DataPoint>(\d+)</DataPoint>', sp)]
    lt = float(re.search(r'<LiveTime>([^<]+)', es).group(1))
    return cal, d, lt
if __name__ == "__main__":
  for k in sys.argv[1:]:
      cal, d, lt = load(k)
      E = lambda c: sum(a * c ** i for i, a in enumerate(cal))
      first = next(i for i, v in enumerate(d) if v > 0)
      print('%s cal=%s lt=%.0f первый ненулевой канал %d (%.1f кэВ)' % (k, ['%.4g' % c for c in cal], lt, first, E(first)))
      print('  ' + ' '.join('%d:%.1f:%d' % (i, E(i), d[i]) for i in range(first, first + 22)))
