using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OrderProbe
{
    /// <summary>
    /// Чего стоит степень полинома. Одна и та же пачка спектров подгоняется
    /// степенями от 1 до 7, и печатается не только χ²/ndf, но и ФОРМА кривой за
    /// краями измеренного: именно там переопределённый полином уходит в разнос,
    /// а по одному χ² этого не видно — он падает всегда.
    ///
    /// Число свободных параметров = (число серий) + (степень), поэтому вопрос
    /// «какая степень нужна» на деле сводится к тому, сколько линий выжило.
    ///
    ///   orderprobe --ref=&lt;roi.xml&gt; --chain=Th-232 &lt;спектры...&gt;
    /// </summary>
    static class Program
    {
        static readonly double[] Probe = { 40, 60, 100, 200, 400, 662, 1000, 1461, 2000, 2615, 3000 };

        static int Main(string[] args)
        {
            string reference = null, chain = null;
            List<string> files = new List<string>();
            foreach (string arg in args)
            {
                if (arg.StartsWith("--ref=")) reference = arg.Substring(6);
                else if (arg.StartsWith("--chain=")) chain = arg.Substring(8);
                else files.Add(arg);
            }

            if (files.Count == 0 || chain == null)
            {
                Console.Error.WriteLine("orderprobe --ref=<roi.xml> --chain=<набор> <спектры...>");
                return 1;
            }

            // Порядок обязателен: наборы нуклидов подтягиваются последними и
            // опираются на два предыдущих менеджера. Без этого библиотека
            // цепочек выходит пустой, и проба молча меряет ничто.
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            int withYield = nuclides.NuclideDefinitions == null ? 0
                : nuclides.NuclideDefinitions.Count(n => n != null && n.Intencity > 0.0);
            Console.WriteLine("наборов: {0}, линий: {1}, из них с выходом: {2}, цепочек собрано: {3}",
                nuclides.NuclideSets == null ? 0 : nuclides.NuclideSets.Count,
                nuclides.NuclideDefinitions == null ? 0 : nuclides.NuclideDefinitions.Count,
                withYield, EfficiencyLibrary.BuildChains().Count);
            Console.WriteLine("спектров: {0}, набор: {1}", files.Count, chain);
            Console.WriteLine();
            Console.Write("{0,-7}{1,-8}{2,-8}{3,-10}", "степ.", "линий", "своб.", "chi2/ndf");
            foreach (double e in Probe)
            {
                Console.Write("{0,10:F0}", e);
            }

            Console.WriteLine();

            for (int order = 1; order <= 7; order++)
            {
                EfficiencyFitInput input = new EfficiencyFitInput { PolynomialOrder = order };
                input.SpectrumFiles.AddRange(files);
                foreach (string file in files)
                {
                    input.ChainsBySpectrum[file] = new List<string> { chain };
                }

                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    input.Reference = EfficiencyFitter.LoadReferenceCurve(reference);
                    input.ReferencePath = reference;
                }

                EfficiencyFitResult result = EfficiencyFitter.Run(input, s => { }, () => false);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Console.WriteLine("{0,-7}{1}", order, result.Error);
                    continue;
                }

                int lines = result.AcceptedCount;
                int free = result.SeriesKeys.Count + order;
                Console.Write("{0,-7}{1,-8}{2,-8}{3,-10:F1}", order, lines, free, result.Chi2Ndf);
                foreach (double e in Probe)
                {
                    Console.Write("{0,10:E2}", Interpolate(result.Curve, e));
                }

                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("Свободных параметров = серий + степень. Линий должно быть заметно");
            Console.WriteLine("больше: иначе кривая проходит через точки и болтается между ними.");
            return 0;
        }

        static double Interpolate(List<ROIEfficiencyData> curve, double energy)
        {
            ROIEfficiencyData lo = null, hi = null;
            foreach (ROIEfficiencyData p in curve)
            {
                if (!(p.Energy > 0.0) || !(p.Efficiency > 0.0)) continue;
                if (p.Energy <= energy && (lo == null || p.Energy > lo.Energy)) lo = p;
                if (p.Energy >= energy && (hi == null || p.Energy < hi.Energy)) hi = p;
            }

            if (lo == null || hi == null) return 0.0;
            if (lo.Energy == hi.Energy) return lo.Efficiency;
            double f = (Math.Log(energy) - Math.Log(lo.Energy))
                       / (Math.Log(hi.Energy) - Math.Log(lo.Energy));
            return Math.Exp(Math.Log(lo.Efficiency) + f * (Math.Log(hi.Efficiency) - Math.Log(lo.Efficiency)));
        }
    }
}
