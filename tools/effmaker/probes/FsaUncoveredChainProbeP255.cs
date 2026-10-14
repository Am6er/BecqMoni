using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П255 14.10.2026) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРАВКИ ПОКРЫТИЯ ПИКОВ ВНЕ СОСТАВА РЯДОМ
    /// РАВНОВЕСИЯ (`AMBER210`, <c>FsaAnalyzer.FindUncoveredPeaks</c>).
    ///
    /// Колонка ряда, связанного равновесием, отчитывается строками ЧЛЕНОВ (имя — член,
    /// <see cref="FsaComponentResult.ChainRoot"/> — колонка), а компонент библиотеки назван
    /// именем колонки. До П255 «вошедшими» считались только имена строк, и ряд, у корня
    /// которого строки нет (линий корня в полосе нет), своими линиями не покрывал ничего:
    /// избыток у линии члена получал имя чужой линии библиотеки рядом.
    ///
    /// Синтетика без спектра: шкала 1 кэВ/кан, ПШПВ √кан, след фита — фон 1000 отсч. и
    /// гаусс избытка на 911.2 кэВ; ряд «Ra-228» несёт одну линию члена «Ac-228» 911.2; в
    /// библиотеке приложения — та же линия и ПРИМАНКА «Xx-915 (проба)» на 915.0 кэВ. Метод
    /// зовётся отражением, вход — как у разбора. Плечи:
    ///   A — строка члена с ChainRoot, строки корня нет (дефект: до правки — пик «Xx-915»);
    ///   B — строка корня есть (покрыто и до правки — контроль, что линия покрывает);
    ///   C — ряда в составе нет вовсе (пик обязан назваться ближайшей линией библиотеки —
    ///       «Ac-228 (Th-232)» 911.2: контроль, что серия проходит пороги и имя ставится).
    /// Ожидание после правки: A — CoveredLine, B — CoveredLine, C — пик «Ac-228 (Th-232)».
    /// Замер П255: сборка до правки — A «пик Xx-915» (дефект), B и C — по ожиданию.
    /// Код 0 — все три по ожиданию; 1 — нет; 2 — метод не найден.
    /// </summary>
    static class FsaUncoveredChainProbeP255
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            MethodInfo find = typeof(FsaAnalyzer).GetMethod("FindUncoveredPeaks", BindingFlags.Instance | BindingFlags.NonPublic);
            Type fitType = typeof(FsaAnalyzer).GetNestedType("FitResult", BindingFlags.NonPublic);
            if (find == null || fitType == null)
            {
                Console.WriteLine("метод или тип следа фита не найден");
                return 2;
            }

            string a = Run(find, fitType, "A", rootRow: false, chainInComposition: true);
            string b = Run(find, fitType, "B", rootRow: true, chainInComposition: true);
            string c = Run(find, fitType, "C", rootRow: false, chainInComposition: false);
            bool ok = a == "CoveredLine" && b == "CoveredLine" && c == "peak:Ac-228 (Th-232)";
            Console.WriteLine(ok ? "ВСЕ ТРИ ПЛЕЧА ПО ОЖИДАНИЮ" : "РАСХОЖДЕНИЕ С ОЖИДАНИЕМ (A CoveredLine, B CoveredLine, C peak:Ac-228 (Th-232))");
            return ok ? 0 : 1;
        }

        static string Run(MethodInfo find, Type fitType, string arm, bool rootRow, bool chainInComposition)
        {
            const int channels = 2000;
            const double centre = 911.2;
            var calibration = new PolynomialEnergyCalibration();
            var fwhmCalibration = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 0.0, 1.0 } };
            double sigma = fwhmCalibration.ChannelToFwhm(centre) / 2.3548;
            double[] residual = new double[channels], variance = new double[channels], model = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                double g = Math.Exp(-0.5 * (i - centre) * (i - centre) / (sigma * sigma));
                model[i] = 1000.0 + 2000.0 * g;
                variance[i] = model[i];
                residual[i] = 1000.0 * g;
            }

            object fit = Activator.CreateInstance(fitType, true);
            fitType.GetField("Residual").SetValue(fit, residual);
            fitType.GetField("Variance").SetValue(fit, variance);
            fitType.GetField("Model").SetValue(fit, model);

            var analyzer = new FsaAnalyzer { EscapeAndAnnihilation = false };
            analyzer.LibraryLines = new List<FsaLine>
            {
                new FsaLine("Ac-228 (Th-232)", 911.2, 0.0),
                new FsaLine("Xx-915 (проба)", 915.0, 0.0)
            };

            var library = new List<FsaComponent>();
            var result = new FsaResult { ResidualFloorChannel = 0 };
            if (chainInComposition)
            {
                var chain = new FsaComponent("Ra-228", FsaComponentKind.Chain);
                chain.Lines.Add(new FsaLine("Ac-228", 911.2, 25.8));
                library.Add(chain);
                result.Components.Add(new FsaComponentResult
                {
                    Name = "Ac-228", Kind = FsaComponentKind.Single, ChainRoot = "Ra-228", CountRate = 1.0
                });
                if (rootRow)
                {
                    result.Components.Add(new FsaComponentResult
                    {
                        Name = "Ra-228", Kind = FsaComponentKind.Single, ChainRoot = "Ra-228", CountRate = 1.0
                    });
                }
            }

            find.Invoke(analyzer, new object[] { result, fit, library, calibration, fwhmCalibration, 1, channels - 2, channels });
            string verdict = "нет серии";
            foreach (FsaUncoveredRejection j in result.UncoveredRejections)
            {
                verdict = j.Reason.ToString();
                Console.WriteLine("  {0}: отвергнута {1} кэВ — {2}, ближайшая линия состава {3}", arm,
                                  j.EnergyKev.ToString("F1", CultureInfo.InvariantCulture), j.Reason,
                                  double.IsNaN(j.NearestCoveredKev) ? "—" : j.NearestCoveredKev.ToString("F1", CultureInfo.InvariantCulture));
            }

            foreach (FsaUncoveredPeak p in result.UncoveredPeaks)
            {
                verdict = "peak:" + (p.Candidates.Count > 0 ? p.Candidates[0].Nuclide : "—");
                Console.WriteLine("  {0}: ПИК ВНЕ СОСТАВА {1} кэВ, избыток {2} %, z {3}, библиотека: {4}", arm,
                                  p.EnergyKev.ToString("F1", CultureInfo.InvariantCulture),
                                  (100.0 * p.ExcessShare).ToString("F1", CultureInfo.InvariantCulture),
                                  p.Z.ToString("F1", CultureInfo.InvariantCulture),
                                  p.Candidates.Count > 0 ? p.Candidates[0].Nuclide + " " + p.Candidates[0].EnergyKev.ToString("F1", CultureInfo.InvariantCulture) : "—");
            }

            Console.WriteLine("{0}: {1}", arm, verdict);
            return verdict;
        }
    }
}
