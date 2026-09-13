import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

old = '''        static void DumpResidual(FsaResult replay, int[] drawn, double[] mu0, double[] mu1,
                                 ResultData rd, string label, int run)
        {
            int channels = replay.Model.Length;
            int lo = Math.Max(0, replay.FirstChannel), hi = Math.Min(channels - 1, replay.LastChannel);
            double[] r = new double[channels];
            double num = 0.0, den = 0.0, injectedTotal = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double bg = replay.Background != null ? replay.Background[i] : 0.0;
                r[i] = drawn[i] - bg - replay.Model[i];
                double d = mu1[i] - mu0[i];
                double v = Math.Max(1.0, mu1[i]);
                num += r[i] * d / v;
                den += d * d / v;
                injectedTotal += d;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                            "      невязка {0} #{1}: впрыснуто {2:E3} отсч.; в остатке осталось {3:F3} впрыска (согласованный отклик)",
                            label, run, injectedTotal, den > 0.0 ? num / den : double.NaN);
'''
new = '''        static void DumpResidual(FsaResult replay, int[] drawn, double[] mu0, double[] mu1,
                                 ResultData rd, string label, int run, double injectedAmplitude)
        {
            int channels = replay.Model.Length;
            int lo = Math.Max(0, replay.FirstChannel), hi = Math.Min(channels - 1, replay.LastChannel);
            double[] r = new double[channels];
            double num = 0.0, den = 0.0, injectedTotal = 0.0, w0 = 0.0, c0 = 0.0, g0 = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double bg = replay.Background != null ? replay.Background[i] : 0.0;
                r[i] = drawn[i] - bg - replay.Model[i];
                double d = mu1[i] - mu0[i];
                double v = Math.Max(1.0, mu1[i]);
                num += r[i] * d / v;
                den += d * d / v;
                injectedTotal += d;
                // Те же величины в весах решателя (1/max(raw,1)) и в масштабе
                // колонки φ = d / a_inj — чтобы сравнить с трассой решателя.
                double wi = 1.0 / Math.Max(1.0, drawn[i]);
                double phi = injectedAmplitude > 0.0 ? d / injectedAmplitude : 0.0;
                w0 += wi * phi * r[i];
                c0 += wi * phi * (drawn[i] - bg);
                g0 += wi * phi * phi;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                            "      невязка {0} #{1}: впрыснуто {2:E3} отсч.; в остатке осталось {3:F3} впрыска (согласованный отклик); в весах решателя по форме впрыска: w0={4:E3} c0={5:E3} G00={6:E3} (w0/G00 = {7:E3} = {8:F3} впрыска)",
                            label, run, injectedTotal, den > 0.0 ? num / den : double.NaN,
                            w0, c0, g0, g0 > 0.0 ? w0 / g0 : double.NaN,
                            g0 > 0.0 && injectedAmplitude > 0.0 ? w0 / g0 / injectedAmplitude : double.NaN);
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''                            DumpResidual(replay, drawn, mu0, mu1, rd, "впрыск", run);
'''
new = '''                            DumpResidual(replay, drawn, mu0, mu1, rd, "впрыск", run, mdaAmplitude);
'''
assert s.count(old) == 1; s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
