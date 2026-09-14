# -*- coding: utf-8 -*-
import io
def patch(p, pairs):
    s=io.open(p,encoding='utf-8-sig',newline='').read()
    crlf = '\r\n' in s
    for old,new in pairs:
        if crlf:
            old=old.replace('\n','\r\n'); new=new.replace('\n','\r\n')
        assert s.count(old)==1, (p, old[:60])
        s=s.replace(old,new)
    io.open(p,'w',encoding='utf-8-sig',newline='').write(s)

patch('BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs', [
('''            public double[] Weights;

            /// <summary>Матрица нормальных уравнений всех колонок фита.</summary>''',
'''            public double[] Weights;

            /// <summary>(П9 11.09.2026, временная копия) Отчётные веса 1/variance.</summary>
            public double[] ReportWeights;

            /// <summary>Матрица нормальных уравнений всех колонок фита.</summary>'''),
('''            if (best != null && reportWeights != null)
            {
                double chi2Base = 0.0;''',
'''            if (best != null && reportWeights != null)
            {
                best.ReportWeights = reportWeights;
                double chi2Base = 0.0;'''),
('''                Background = backgroundCurve,
                Continuum = new double[channels],
                Model = new double[channels]
            };''',
'''                Background = backgroundCurve,
                Continuum = new double[channels],
                Model = new double[channels],
                P9Residual = fit.Residual,
                P9SolverWeights = fit.Weights,
                P9ReportWeights = fit.ReportWeights,
                P9NdfSolver = fit.Ndf,
                P9NdfReport = fit.NdfBase
            };'''),
])
patch('BecquerelMonitor/FullSpectrumAnalysis/FsaResult.cs', [
('''        /// <summary>Сумма модели, отсчёты по каналам.</summary>
        public double[] Model { get; set; }''',
'''        /// <summary>Сумма модели, отсчёты по каналам.</summary>
        public double[] Model { get; set; }

        /// <summary>(П9 11.09.2026, временная копия) Остаток фита по каналам.</summary>
        public double[] P9Residual { get; set; }

        /// <summary>(П9) Веса решателя (хуберовские) по каналам.</summary>
        public double[] P9SolverWeights { get; set; }

        /// <summary>(П9) Отчётные веса (пуассон + фон) по каналам.</summary>
        public double[] P9ReportWeights { get; set; }

        /// <summary>(П9) ndf решателя и отчётный.</summary>
        public double P9NdfSolver { get; set; }

        public double P9NdfReport { get; set; }'''),
])
patch('tools/effmaker/probes/CorpusFsaProbe.cs', [
('''            RejectDuplicateColumns(head.ToString(), "--dump-curves");
            string path = Path.Combine(dir, key + "_curves.csv");''',
'''            RejectDuplicateColumns(head.ToString(), "--dump-curves");
            // (П9 11.09.2026, временная копия) остаток и веса по каналам — отдельным
            // файлом, чтобы формат `_curves.csv` остался прежним для чужих читателей.
            int[] rawCounts = spectrum.Spectrum;
            using (var wc = new StreamWriter(Path.Combine(dir, key + "_chi.csv"), false, new UTF8Encoding(false)))
            {
                wc.WriteLine("ch,keV,raw,bg,fit,model,resid,w_rep,w_sol,ndf_sol,ndf_rep,first,last");
                for (int i = 0; i < spectrum.NumberOfChannels; i++)
                {
                    wc.WriteLine(string.Join(",",
                        i.ToString(CultureInfo.InvariantCulture),
                        calibration.ChannelToEnergy(i).ToString("F3", CultureInfo.InvariantCulture),
                        (rawCounts != null && i < rawCounts.Length ? rawCounts[i] : 0).ToString(CultureInfo.InvariantCulture),
                        Cell(result.Background, i),
                        Cell(fit, i),
                        Cell(result.Model, i),
                        result.P9Residual != null && i < result.P9Residual.Length ? result.P9Residual[i].ToString("R", CultureInfo.InvariantCulture) : "",
                        result.P9ReportWeights != null && i < result.P9ReportWeights.Length ? result.P9ReportWeights[i].ToString("R", CultureInfo.InvariantCulture) : "",
                        result.P9SolverWeights != null && i < result.P9SolverWeights.Length ? result.P9SolverWeights[i].ToString("R", CultureInfo.InvariantCulture) : "",
                        result.P9NdfSolver.ToString("R", CultureInfo.InvariantCulture),
                        result.P9NdfReport.ToString("R", CultureInfo.InvariantCulture),
                        result.FirstChannel.ToString(CultureInfo.InvariantCulture),
                        result.LastChannel.ToString(CultureInfo.InvariantCulture)));
                }
            }

            string path = Path.Combine(dir, key + "_curves.csv");'''),
])
print('ok')
