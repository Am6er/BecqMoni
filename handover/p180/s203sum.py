# П180 S203: свести набор прогонов G4RawProbe: полная суммой отклика против второго обхода (TotalEfficiency)
import re, sys, glob, math
pat = sys.argv[1]
S = []; T = []; TE = []; P = []; PA = []
for f in sorted(glob.glob(pat)):
    t = open(f, encoding='utf-8', errors='replace').read()
    m1 = re.search(r'пик ([\d.E+-]+), полная ([\d.E+-]+)', t)
    m2 = re.search(r'полная вторым обходом \(TotalEfficiency\) ([\d.E+-]+) ± ([\d.]+) %', t)
    m3 = re.search(r'пик аналоговой ветви ([\d.E+-]+)', t)
    if not (m1 and m2): print('нет чисел:', f); continue
    P.append(float(m1.group(1))); S.append(float(m1.group(2))); T.append(float(m2.group(1))); TE.append(float(m2.group(2))); PA.append(float(m3.group(1)))
k = len(S)
def mean_se(a):
    m = sum(a) / len(a); sd = math.sqrt(sum((x - m) ** 2 for x in a) / (len(a) - 1)); return m, sd / math.sqrt(len(a))
ms, ss = mean_se(S); mt, st = mean_se(T)
r = [s / t for s, t in zip(S, T)]
mr, sr = mean_se(r)
ma = sum(s - p + pa for s, p, pa in zip(S, P, PA)) / k
print(f'{pat}: прогонов {k}')
print(f'  полная суммой отклика   {ms:.6e} ± {100*ss/ms:.2f} % (разброс зёрен)')
print(f'  полная вторым обходом   {mt:.6e} ± {100*st/mt:.2f} % (разброс зёрен; печать пробы ±{sum(TE)/k/math.sqrt(k):.2f} %)')
print(f'  отклик/обход = {ms/mt:.4f} ± {math.sqrt((ss/ms)**2+(st/mt)**2)*100:.2f} %   (среднее отношений {mr:.4f} ± {100*sr:.2f} %)')
print(f'  аналоговая полная (континуум + аналоговый пик) / обход = {ma/mt:.4f}')
