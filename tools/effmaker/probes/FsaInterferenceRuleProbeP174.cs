using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FsaInterferenceRuleProbeP174
{
    /// <summary>
    /// (`S199`, полоса П174 28.09.2026) ПОБИТОВЫЙ КОНТРОЛЬ правила строки FSA
    /// (<see cref="FsaSampleLibrary.NaturalCompanionInterference"/>) после выноса
    /// поиска спутника и линий его ряда в общие методы с зоной ROI. Библиотека —
    /// по одному компоненту на нуклид с его линиями распада
    /// (<see cref="FsaSampleLibrary.DecayLines"/>), ПШПВ 6.44 %·√(662·E),
    /// эффективность — единица и 1/E; множители печатаются форматом R, чтобы
    /// сборки до и после сравнивались побайтно. Только открытые методы,
    /// существующие в обеих сборках.
    ///
    ///   FsaInterferenceRuleProbeP174 [--nuclides=226RA,234TH,235U,214PB,238U]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo inv = CultureInfo.InvariantCulture;
            string list = "226RA,234TH,235U,214PB,214BI,210PB,231TH,238U,232TH,40K";
            foreach (string a in args)
            {
                if (a.StartsWith("--nuclides=", StringComparison.Ordinal)) list = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            Func<double, double> fwhm = e => 0.0644 * Math.Sqrt(662.0 * e);
            foreach (string effName in new[] { "единица", "1/E" })
            {
                Func<double, double> eff = effName == "1/E" ? (Func<double, double>)(e => 100.0 / e) : null;
                var library = new List<FsaComponent>();
                var detected = new List<string>();
                foreach (string nucid in list.Split(','))
                {
                    string name = FsaSampleLibrary.PrettyName(nucid);
                    var component = new FsaComponent(name, FsaComponentKind.Single);
                    foreach (double[] line in FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report()))
                    {
                        component.Lines.Add(new FsaLine(name, line[0], line[1]));
                    }

                    // По одному компоненту в библиотеке: иначе чужие линии в
                    // библиотеке глушат спутника правилом «присутствует».
                    List<FsaLineInterference> found = FsaSampleLibrary.NaturalCompanionInterference(
                        new List<FsaComponent> { component }, new List<string> { name }, fwhm, eff, 20.0, 3000.0);
                    Console.WriteLine("эфф. {0}\t{1}\tлиний {2}\tпомех {3}", effName, name,
                                      component.Lines.Count.ToString(inv), found.Count.ToString(inv));
                    foreach (FsaLineInterference hit in found)
                    {
                        Console.WriteLine("\t{0}\t{1}\t{2}\t{3}\t{4}", hit.LineKev.ToString("R", inv), hit.Companion,
                                          hit.Reference, hit.ActivityRatio.ToString("R", inv), hit.Factor.ToString("R", inv));
                    }
                }
            }

            return 0;
        }
    }
}
