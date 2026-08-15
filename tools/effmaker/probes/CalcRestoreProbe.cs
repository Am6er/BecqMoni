// E23: поля расчёта кривой восстанавливаются при открытии готовой конфигурации.
//
// Проверяется то, на чём попались: кривую строили от 20 кэВ, а при следующем
// открытии в поле предлагалось заводское 40. Проба ходит теми же путями, что
// форма, — отражением, своей копии логики у неё нет НАРОЧНО: копия проверяла бы
// себя.
//
//     calcrestoreprobe
//
// Восемь случаев: разбор клейма (штатная сетка, логарифмическая, дробные края,
// чужая культура), отсутствие клейма с откатом на края кривой, отсутствие и
// того и другого (поля обязаны остаться заводскими), мусорное клеймо, и
// обрезка по границам поля.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace BecquerelMonitor.Probes
{
    static class CalcRestoreProbe
    {
        static int failures;

        [STAThread]
        static void Main()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            ParseCases();
            RestoreCases();

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + failures);
            Environment.Exit(failures == 0 ? 0 : 1);
        }

        // --- разбор клейма ------------------------------------------------
        static readonly MethodInfo Parse = typeof(EfficiencyMakerForm).GetMethod(
            "TryParseComputeStamp", BindingFlags.NonPublic | BindingFlags.Static);

        static void ParseCases()
        {
            Console.WriteLine("== разбор клейма ==");
            Case("phys=6; hist=200000; grid=20-3000 keV/34 std", true, 20, 3000, 200000, 34, false);
            Case("phys=6; hist=500000; grid=40.5-2800 keV/60 log", true, 40.5, 2800, 500000, 60, true);
            // Клеймо кривой, восстановленной по измерениям, пустое по построению.
            Case("", false, 0, 0, 0, 0, false);
            Case("phys=6; hist=200000", false, 0, 0, 0, 0, false);
            // Перевёрнутый диапазон — не «половина разобранного», а отказ.
            Case("phys=6; hist=100; grid=3000-20 keV/34 std", false, 0, 0, 0, 0, false);
        }

        static void Case(string stamp, bool expected, double lo, double hi,
                         double hist, double nodes, bool log)
        {
            object[] args = { stamp, 0.0, 0.0, 0.0, 0.0, false };
            bool ok = (bool)Parse.Invoke(null, args);
            bool good = ok == expected;
            if (ok && expected)
            {
                good = Near((double)args[1], lo) && Near((double)args[2], hi)
                       && Near((double)args[3], hist) && Near((double)args[4], nodes)
                       && (bool)args[5] == log;
            }

            Console.WriteLine("  {0,-46} -> {1}{2}", Short(stamp), ok ? "разобрано" : "отказ",
                              ok && expected
                                  ? string.Format(CultureInfo.InvariantCulture,
                                                  " {0}-{1} кэВ, hist {2}, узлов {3}, {4}",
                                                  args[1], args[2], args[3], args[4],
                                                  (bool)args[5] ? "log" : "std")
                                  : "");
            Check(good, "разбор");
        }

        // --- восстановление полей -----------------------------------------
        static void RestoreCases()
        {
            Console.WriteLine();
            Console.WriteLine("== поля формы ==");

            // Клеймо есть — восстанавливается ВСЁ.
            WithForm("клеймо 20 кэВ", Config("phys=6; hist=750000; grid=20-2800 keV/34 std", 30, 2000),
                     20m, 2800m, 750000m, 0);

            // Клейма нет (кривая из измерений) — откат на края самой кривой.
            WithForm("без клейма, кривая 30-2000", Config("", 30, 2000), 30m, 2000m, 0m, 0);

            // Ни клейма, ни кривой — поля обязаны остаться заводскими.
            WithForm("пусто", Config("", 0, 0), 40m, 3000m, 200000m, 0);

            // Логарифмическая сетка отпирает поле числа узлов.
            WithForm("log/60", Config("phys=6; hist=200000; grid=25-1500 keV/60 log", 0, 0),
                     25m, 1500m, 200000m, 1, 60m);
        }

        static EfficiencyConfigData Config(string stamp, double lo, double hi)
        {
            EfficiencyConfigData config = new EfficiencyConfigData();
            config.ComputeStamp = stamp;
            if (hi > lo)
            {
                List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
                curve.Add(new ROIEfficiencyData { Energy = lo, Efficiency = 0.1 });
                curve.Add(new ROIEfficiencyData { Energy = 0.5 * (lo + hi), Efficiency = 0.05 });
                curve.Add(new ROIEfficiencyData { Energy = hi, Efficiency = 0.01 });
                config.Curve = curve;
            }

            return config;
        }

        static void WithForm(string title, EfficiencyConfigData config,
                             decimal lo, decimal hi, decimal hist, int gridIndex,
                             decimal nodes = 0m)
        {
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                MethodInfo apply = typeof(EfficiencyMakerForm).GetMethod(
                    "ApplyCalcOptions", BindingFlags.NonPublic | BindingFlags.Instance);
                string said = (string)apply.Invoke(form, new object[] { config });

                decimal gotLo = Box(form, "calcMinEnergyBox");
                decimal gotHi = Box(form, "calcMaxEnergyBox");
                decimal gotHist = Box(form, "calcHistoriesBox");
                ComboBox grid = (ComboBox)Field(form, "calcGridBox");
                NumericUpDown points = (NumericUpDown)Field(form, "calcPointsBox");

                Console.WriteLine("  {0,-26} {1}-{2} кэВ, hist {3}, сетка {4}, узлов {5} ({6})",
                                  title, gotLo, gotHi, gotHist, grid.SelectedIndex,
                                  points.Value, points.Enabled ? "отперто" : "заперто");
                Console.WriteLine("      сказано: {0}", string.IsNullOrEmpty(said) ? "(молчит)" : said);

                Check(gotLo == lo && gotHi == hi, "границы");
                if (hist > 0m)
                {
                    Check(gotHist == hist, "историй");
                }

                Check(grid.SelectedIndex == gridIndex, "сетка");

                // Число узлов имеет смысл только при логарифмической сетке —
                // штатная считает его сама, и поле при ней заперто. Печатать
                // его и не сверять значило бы завести признак без читателя.
                Check(points.Enabled == (gridIndex == 1), "поле узлов отперто по сетке");
                if (nodes > 0m)
                {
                    Check(points.Value == nodes, "узлов");
                }

                // Молчать о подмене нельзя: подставленный диапазон обязан быть
                // неотличим от выбранного человеком только на вид, а не в
                // журнале.
                bool shouldSpeak = lo != 40m || hi != 3000m;
                Check(shouldSpeak == !string.IsNullOrEmpty(said), "сказано вслух");
            }
        }

        static decimal Box(EfficiencyMakerForm form, string name)
        {
            return ((NumericUpDown)Field(form, name)).Value;
        }

        static object Field(EfficiencyMakerForm form, string name)
        {
            return typeof(EfficiencyMakerForm)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(form);
        }

        static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < 1e-9;
        }

        static string Short(string s)
        {
            return string.IsNullOrEmpty(s) ? "(пусто)" : s;
        }

        static void Check(bool ok, string what)
        {
            if (!ok)
            {
                failures++;
                Console.WriteLine("      РАСХОЖДЕНИЕ: {0}", what);
            }
        }
    }
}
