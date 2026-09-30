# П186: калибровки файлов корпуса группы и положения пиков по сырому спектру
# python cals.py <группа>
import re, sys, os, glob, math
root = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
grp = sys.argv[1]
def load(p):
    t = open(p, encoding='utf-8-sig').read()
    blk = t.split('<EnergyCalibration>')[1]
    co = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', blk.split('</EnergyCalibration>')[0])]
    sp = t.split('<Spectrum>')[1].split('</Spectrum>')[0]
    d = [float(x) for x in re.findall(r'<DataPoint>([^<]+)</DataPoint>', sp)]
    return co, d
def E(co, ch):
    return sum(c * ch ** i for i, c in enumerate(co))
chs = [8, 10, 12, 20, 30, 45, 130, 240]
print('%-26s' % 'спектр' + ''.join('%9s' % ('к%d' % c) for c in chs))
for p in sorted(glob.glob(os.path.join(root, grp + '_*.xml'))):
    co, d = load(p)
    print('%-26s' % os.path.basename(p)[:-4] + ''.join('%9.2f' % E(co, c) for c in chs))
