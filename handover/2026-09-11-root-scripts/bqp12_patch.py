# -*- coding: utf-8 -*-
# П12 11.09.2026: дамп колонок плана фита (образы, lowTail, шапки сплайна), амплитуд и y
# во временной копии C:\Users\moroz\bqp12 — поверх патча П9.
import io, os, sys
root = r'C:\Users\moroz\bqp12'
def patch(p, pairs):
    p = os.path.join(root, p)
    s = io.open(p, encoding='utf-8-sig', newline='').read()
    crlf = '\r\n' in s
    for old, new in pairs:
        if crlf:
            old = old.replace('\n', '\r\n'); new = new.replace('\n', '\r\n')
        assert s.count(old) == 1, (p, old[:70], s.count(old))
        s = s.replace(old, new)
    io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)

patch('BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs', [
('''            public double[] Residual;
''',
'''            public double[] Residual;

            /// <summary>(П12 11.09.2026, временная копия) Подгоняемый вектор y = raw − фон.</summary>
            public double[] Y;
'''),
('''                Residual = residual,
''',
'''                Residual = residual,
                Y = y,
'''),
('''                P9NdfReport = fit.NdfBase
''',
'''                P9NdfReport = fit.NdfBase,
                P12Y = fit.Y,
                P12Amplitude = fit.Amplitude,
                P12Sigma = fit.Sigma,
                P12Z = fit.Z,
                P12Active = fit.Active,
                P12FixedFirst = fit.FixedFirst,
                P12ColumnNames = P12Names(fit),
                P12Columns = P12Values(fit)
'''),
('''        FitResult FitHuber(List<FsaComponent> library, List<double[]> fixedColumns,
''',
'''        /// <summary>(П12) Имена колонок плана: образ — имя компонента; хвост —
        /// `tail:` + имя компонента, за которым он идёт; шапка — `spline#k`.</summary>
        static List<string> P12Names(FitResult fit)
        {
            var names = new List<string>();
            string last = "?";
            for (int k = 0; k < fit.Columns.Count; k++)
            {
                FitColumn c = fit.Columns[k];
                if (k >= fit.FixedFirst)
                {
                    names.Add("spline#" + (k - fit.FixedFirst).ToString(CultureInfo.InvariantCulture));
                }
                else if (c.Component != null)
                {
                    last = c.Component.Name;
                    names.Add(last);
                }
                else
                {
                    names.Add("tail:" + last);
                }
            }

            return names;
        }

        static List<double[]> P12Values(FitResult fit)
        {
            var values = new List<double[]>();
            foreach (FitColumn c in fit.Columns)
            {
                values.Add(c.Values);
            }

            return values;
        }

        FitResult FitHuber(List<FsaComponent> library, List<double[]> fixedColumns,
'''),
])
patch('BecquerelMonitor/FullSpectrumAnalysis/FsaResult.cs', [
('''        public double P9NdfReport { get; set; }
''',
'''        public double P9NdfReport { get; set; }

        /// <summary>(П12 11.09.2026, временная копия) План последнего фита: y, колонки, амплитуды.</summary>
        public double[] P12Y { get; set; }

        public double[] P12Amplitude { get; set; }

        public double[] P12Sigma { get; set; }

        public double[] P12Z { get; set; }

        public bool[] P12Active { get; set; }

        public int P12FixedFirst { get; set; }

        public List<string> P12ColumnNames { get; set; }

        public List<double[]> P12Columns { get; set; }
'''),
])
patch('tools/effmaker/probes/CorpusFsaProbe.cs', [
('''            string path = Path.Combine(dir, key + "_curves.csv");
''',
'''            // (П12 11.09.2026, временная копия) План фита: y, веса и ВСЕ колонки
            // (образы, хвосты `tail:`, шапки `spline#`) — для счёта вне решателя.
            if (result.P12Columns != null)
            {
                var hc = new StringBuilder("ch,keV,y,w_rep,w_sol,resid");
                foreach (string name in result.P12ColumnNames)
                {
                    hc.Append(',').Append(name.Replace(',', ';'));
                }

                RejectDuplicateColumns(hc.ToString(), "--dump-curves (_cols)");
                using (var wc = new StreamWriter(Path.Combine(dir, key + "_cols.csv"), false, new UTF8Encoding(false)))
                {
                    wc.WriteLine(hc.ToString());
                    for (int i = 0; i < spectrum.NumberOfChannels; i++)
                    {
                        var line = new StringBuilder();
                        line.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                            .Append(calibration.ChannelToEnergy(i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                            .Append(R(result.P12Y, i)).Append(',')
                            .Append(R(result.P9ReportWeights, i)).Append(',')
                            .Append(R(result.P9SolverWeights, i)).Append(',')
                            .Append(R(result.P9Residual, i));
                        foreach (double[] col in result.P12Columns)
                        {
                            line.Append(',').Append(R(col, i));
                        }

                        wc.WriteLine(line.ToString());
                    }
                }

                using (var wa = new StreamWriter(Path.Combine(dir, key + "_amps.csv"), false, new UTF8Encoding(false)))
                {
                    wa.WriteLine("index,name,kind,amp,sigma,z,active,fixed_first,first,last");
                    for (int k = 0; k < result.P12ColumnNames.Count; k++)
                    {
                        string name = result.P12ColumnNames[k];
                        string kind = k >= result.P12FixedFirst ? "spline" : (name.StartsWith("tail:", StringComparison.Ordinal) ? "tail" : "image");
                        wa.WriteLine(string.Join(",",
                            k.ToString(CultureInfo.InvariantCulture),
                            name.Replace(',', ';'),
                            kind,
                            result.P12Amplitude[k].ToString("R", CultureInfo.InvariantCulture),
                            result.P12Sigma[k].ToString("R", CultureInfo.InvariantCulture),
                            result.P12Z[k].ToString("R", CultureInfo.InvariantCulture),
                            result.P12Active[k] ? "1" : "0",
                            result.P12FixedFirst.ToString(CultureInfo.InvariantCulture),
                            result.FirstChannel.ToString(CultureInfo.InvariantCulture),
                            result.LastChannel.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }

            string path = Path.Combine(dir, key + "_curves.csv");
'''),
('''        static string Cell(double[] a, int i)
''',
'''        static string R(double[] a, int i)
        {
            double v = a != null && i < a.Length ? a[i] : 0.0;
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Cell(double[] a, int i)
'''),
])
print('ok')
