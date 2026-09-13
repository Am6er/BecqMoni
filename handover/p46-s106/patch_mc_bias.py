import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

old = '''            double num = 0.0, den = 0.0, injectedTotal = 0.0, w0 = 0.0, c0 = 0.0, g0 = 0.0;
'''
new = '''            double num = 0.0, den = 0.0, injectedTotal = 0.0, w0 = 0.0, c0 = 0.0, g0 = 0.0;
            double w0True = 0.0, g0True = 0.0, biasPred = 0.0;
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''                w0 += wi * phi * r[i];
                c0 += wi * phi * (drawn[i] - bg);
                g0 += wi * phi * phi;
            }
'''
new = '''                w0 += wi * phi * r[i];
                c0 += wi * phi * (drawn[i] - bg);
                g0 += wi * phi * phi;
                // Те же суммы с весами по ОЖИДАНИЮ (1/μ) и предсказанный сдвиг
                // градиента от весов по данным: E[(y−μ)/y] ≈ −1/μ ⇒ −Σ φ/μ.
                w0True += phi * r[i] / v;
                g0True += phi * phi / v;
                biasPred += phi / v;
            }
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''                            "      невязка {0} #{1}: впрыснуто {2:E3} отсч.; в остатке осталось {3:F3} впрыска (согласованный отклик); в весах решателя по форме впрыска: w0={4:E3} c0={5:E3} G00={6:E3} (w0/G00 = {7:E3} = {8:F3} впрыска)",
                            label, run, injectedTotal, den > 0.0 ? num / den : double.NaN,
                            w0, c0, g0, g0 > 0.0 ? w0 / g0 : double.NaN,
                            g0 > 0.0 && injectedAmplitude > 0.0 ? w0 / g0 / injectedAmplitude : double.NaN);
'''
new = '''                            "      невязка {0} #{1}: впрыснуто {2:E3} отсч.; в остатке осталось {3:F3} впрыска (согласованный отклик); в весах решателя по форме впрыска: w0={4:E3} c0={5:E3} G00={6:E3} (w0/G00 = {7:E3} = {8:F3} впрыска); веса 1/μ: w0={9:E3} G00={10:E3} ({11:F3} впрыска); предсказанный сдвиг весов по данным −Σφ/μ = {12:E3} (измерено w0(1/y) − w0(1/μ) = {13:E3}), в отсчётах впрыска {14:F0}",
                            label, run, injectedTotal, den > 0.0 ? num / den : double.NaN,
                            w0, c0, g0, g0 > 0.0 ? w0 / g0 : double.NaN,
                            g0 > 0.0 && injectedAmplitude > 0.0 ? w0 / g0 / injectedAmplitude : double.NaN,
                            w0True, g0True, g0True > 0.0 && injectedAmplitude > 0.0 ? w0True / g0True / injectedAmplitude : double.NaN,
                            -biasPred, w0 - w0True,
                            g0True > 0.0 && injectedAmplitude > 0.0 ? biasPred / g0True / injectedAmplitude * injectedTotal : double.NaN);
'''
assert s.count(old) == 1; s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
