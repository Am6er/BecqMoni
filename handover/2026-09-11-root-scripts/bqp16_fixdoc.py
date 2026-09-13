# -*- coding: utf-8 -*-
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8')
p = r'C:\Users\moroz\bqp16\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs'
s = io.open(p, encoding='utf-8-sig', newline='').read()
n = 0
a = '/// (П16) β: 1 — пик модели стоит там, где его ставит свет; 0 —'
b = '/// (П16) β: единица — пик модели стоит там, где его ставит свет; нуль —'
n += s.count(a); s = s.replace(a, b)
a = '/// Рычаг — `--anchor-beta=`.\n        /// </summary>\n        public double AnchorLightBeta'
b = '/// Умолчание — в конструкторе; рычаг — `--anchor-beta=`.\n        /// </summary>\n        public double AnchorLightBeta'
n += s.count(a); s = s.replace(a, b)
m = re.search(r'/// <summary>\(П16\) E₀[^\n]*\n', s)
n += 1 if m else 0
s = s.replace(m.group(0), '/// <summary>(П16) E₀ — энергия, на которой s(E₀) = 0 (калибровочная линия прибора, Cs-137); число — в конструкторе.</summary>\n')
print('замен', n)
assert n == 3
io.open(p, 'w', encoding='utf-8', newline='').write(s)
