# -*- coding: utf-8 -*-
# П100: починить строку FOIL — heredoc превратил "\n" в настоящий перевод строки (грабля «обратный слэш не доезжает через heredoc»).
import io
p = r'D:\BqMoni_Claude\p100\g4eta\g4eta.cc'
s = io.open(p, encoding='utf-8').read()
bad = 'P60=%.5f meanTfrac=%.4f\n",'
good = 'P60=%.5f meanTfrac=%.4f\\n",'
assert s.count(bad) == 1, s.count(bad)
s = s.replace(bad, good)
io.open(p, 'w', encoding='utf-8').write(s)
print('ok')
