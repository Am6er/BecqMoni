using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// ⛔ `AMBER95` (полоса П147, 24.09.2026, физика 24): СЕТКА КРИВОЙ НИЖЕ 40 кэВ
/// И K-КРАЯ КРИСТАЛЛА.
///
/// Штатная сетка кривой начинается с 40 кэВ, ниже её достраивает `Reach` шагом
/// первой пары (10 кэВ): узлы 5, 10, 20, 30, 40. K-края собственного кристалла
/// (иод 33.17, цезий 35.985) в сетку не входили (`AddEdges` перечислял только
/// пробу, стенку, отражатель и оправу), и лог-лог интерполяция между 30 и 40
/// кэВ вела прямую через ступеньку эффективности пика; между 20 и 30 — хорду
/// вогнутой кривой пропускания `exp(−μt)`.
///
/// Проба на каждой сцене `--in=`:
///   (1) печатает узлы штатной сетки ниже 45 кэВ и называет, есть ли пара у
///       каждого K-края кристалла;
///   (2) считает АРБИТРА — ту же кривую тем же путём
///       (<see cref="EfficiencyCalculation.Run(GeometryModel, EfficiencyCalculationOptions, Action{string}, Func{bool}, ResponseMatrixOptions, bool?)"/>)
///       с узлом РОВНО на каждой проверочной энергии — и ШТАТНУЮ кривую в
///       `[5, 60]` кэВ;
///   (3) на энергиях `--e=` печатает лог-лог интерполяцию штатной против
///       арбитра (отношение) — это и есть ошибка, которую видит человек в
///       беккерелях по кривой;
///   (4) пол полосы FSA `FloorAtFraction(0.01)` на штатной кривой против
///       пересечения её лог-лог интерполянта с уровнем 1 % максимума.
///
///     curvegridprobea95 --in=&lt;.in&gt;[,&lt;.in&gt;…] [--n=200000]
///                       [--e=13.9,17.14,20.8,26.34,31.8,32.2,33.3,34.0,35.0,36.4]
///                       [--tol=3]
///
/// Код 0 — у всех сцен |отношение − 1| ≤ max(`--tol` %, 3σ шума двух кривых)
/// на всех энергиях, пары у K-краёв кристалла стоят и пол FSA — пересечение;
/// 1 — нет (на коде физики ≤ 23 — ОЖИДАЕМО).
/// </summary>
static class CurveGridProbeA95
{
    static string F(double v, int d)
    {
        return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static double[] List(string s)
    {
        return Array.ConvertAll(s.Split(','), x => double.Parse(x, CultureInfo.InvariantCulture));
    }

    static bool Interp(List<ROIEfficiencyData> curve, double e, out double eff)
    {
        eff = 0.0;
        var pts = new List<ROIEfficiencyData>();
        foreach (ROIEfficiencyData p in curve)
        {
            if (p.Energy > 0.0 && p.Efficiency > 0.0)
            {
                pts.Add(p);
            }
        }

        pts.Sort((a, b) => a.Energy.CompareTo(b.Energy));
        for (int i = 1; i < pts.Count; i++)
        {
            if (e >= pts[i - 1].Energy && e <= pts[i].Energy)
            {
                double x0 = Math.Log(pts[i - 1].Energy), x1 = Math.Log(pts[i].Energy);
                double y0 = Math.Log(pts[i - 1].Efficiency), y1 = Math.Log(pts[i].Efficiency);
                double t = x1 > x0 ? (Math.Log(e) - x0) / (x1 - x0) : 0.0;
                eff = Math.Exp(y0 + t * (y1 - y0));
                return true;
            }
        }

        return false;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string[] inputs = null;
        int histories = 200000;
        double[] tests = { 13.9, 17.14, 20.8, 26.34, 31.8, 32.2, 33.3, 34.0, 35.0, 36.4 };
        double tol = 3.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inputs = a.Substring(5).Split(',');
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--e=", StringComparison.Ordinal)) tests = List(a.Substring(4));
            else if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else
            {
                Console.Error.WriteLine("неизвестный ключ " + a);
                return 2;
            }
        }

        if (inputs == null)
        {
            Console.Error.WriteLine("нужен --in=");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        int failed = 0;
        foreach (string inPath in inputs)
        {
            if (!File.Exists(inPath))
            {
                Console.Error.WriteLine("нет файла " + inPath);
                return 2;
            }

            GeometryModel g = GeometryModel.Load(inPath);
            Console.WriteLine("== {0}: кристалл «{1}»", Path.GetFileNameWithoutExtension(inPath), g.Crystal.Name);

            // (1) штатная сетка ниже 45 кэВ
            var standard = new EfficiencyCalculationOptions { Histories = histories };
            double[] grid = standard.BuildGrid(g, null);
            var low = new List<string>();
            foreach (double e in grid)
            {
                if (e < 45.0)
                {
                    low.Add(F(e, 2));
                }
            }

            Console.WriteLine("   штатная сетка 5…3000: узлов {0}, ниже 45 кэВ — {1}: {2}", grid.Length, low.Count,
                              string.Join(" ", low));
            foreach (KeyValuePair<int, double> part in g.Crystal.Fractions)
            {
                MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(part.Key);
                if (f == null || !(f.KEdgeKev > 5.0) || f.KEdgeKev > 3000.0)
                {
                    continue;
                }

                bool below = false, above = false;
                foreach (double e in grid)
                {
                    if (e < f.KEdgeKev && e >= f.KEdgeKev * 0.99) below = true;
                    if (e > f.KEdgeKev && e <= f.KEdgeKev * 1.01) above = true;
                }

                bool ok = below && above;
                Console.WriteLine("   K-край кристалла Z={0} {1} кэВ: пара узлов {2}", part.Key, F(f.KEdgeKev, 3),
                                  ok ? "есть" : "НЕТ");
                if (!ok)
                {
                    failed++;
                }
            }

            // (2) арбитр и штатная
            // Арбитр — узел РОВНО на каждой проверочной энергии (вторая точка
            // сетки на 0.1 % выше): густая сетка у края сама врёт, если край
            // между её узлами, а узел на самой энергии — нет.
            var arb = new EfficiencyFitResult();
            string arbError = null;
            foreach (double e in tests)
            {
                var one = new EfficiencyCalculationOptions
                {
                    Histories = histories,
                    MinEnergyKev = e,
                    MaxEnergyKev = e * 1.001,
                    GridMode = EfficiencyGridMode.Logarithmic,
                    NodeCount = 2,
                };
                EfficiencyFitResult r = EfficiencyCalculation.Run(g, one, null, null);
                if (!r.Ok)
                {
                    arbError = r.Error;
                    continue;
                }

                foreach (ROIEfficiencyData p in r.Curve)
                {
                    if (Math.Abs(p.Energy - e) < 1e-9)
                    {
                        arb.Curve.Add(p);
                    }
                }
            }
            var stdOptions = new EfficiencyCalculationOptions
            {
                Histories = histories,
                MinEnergyKev = 5.0,
                MaxEnergyKev = 60.0,
            };
            EfficiencyFitResult std = EfficiencyCalculation.Run(g, stdOptions, null, null);
            if (arbError != null || !std.Ok)
            {
                Console.WriteLine("   ⛔ кривая не посчитана: {0} / {1}", arbError, std.Error);
                failed++;
                continue;
            }

            var stdNodes = new List<string>();
            foreach (ROIEfficiencyData p in std.Curve)
            {
                stdNodes.Add(F(p.Energy, 2));
            }

            Console.WriteLine("   штатная [5, 60]: узлы {0}", string.Join(" ", stdNodes));
            Console.WriteLine("   энергия, кэВ | арбитр ε | штатная лог-лог ε | отношение");
            foreach (double e in tests)
            {
                double ea = 0.0, es, eaErr = 0.0;
                foreach (ROIEfficiencyData p in arb.Curve)
                {
                    if (Math.Abs(p.Energy - e) < 1e-9)
                    {
                        ea = p.Efficiency;
                        eaErr = p.ErrorPercent;
                    }
                }

                // шум штатной в точке — наибольший из двух соседних узлов
                double esErr = 0.0;
                foreach (ROIEfficiencyData p in std.Curve)
                {
                    if (p.Energy > e / 1.2 && p.Energy < e * 1.2)
                    {
                        esErr = Math.Max(esErr, p.ErrorPercent);
                    }
                }

                if (!Interp(std.Curve, e, out es) || !(ea > 0.0))
                {
                    Console.WriteLine("   {0,8} | —", F(e, 2));
                    continue;
                }

                double ratio = es / ea;
                double sigma = Math.Sqrt(eaErr * eaErr + esErr * esErr);
                // Годно, если в допуске ИЛИ в 3σ шума двух кривых: у маринелли
                // на 14 кэВ ε ~ 1e−6, и шум узла там — десятки процентов.
                bool ok = Math.Abs(ratio - 1.0) * 100.0 <= Math.Max(tol, 3.0 * sigma);
                Console.WriteLine("   {0,8} | {1:E4} | {2:E4} | {3} (шум {4} %) {5}", F(e, 2), ea, es, F(ratio, 4),
                                  F(sigma, 1), ok ? "" : "⛔");
                if (!ok)
                {
                    failed++;
                }
            }

            failed += Floor(std.Curve, 0.01);
        }

        Console.WriteLine(failed == 0 ? "✅ все проверки прошли" : "⛔ отказов: " + failed);
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// (4) ПОЛ ПОЛОСЫ FSA (`FsaEfficiency.FloorAtFraction`) на штатной кривой:
    /// обязан быть ПЕРЕСЕЧЕНИЕМ лог-лог интерполянта кривой с уровнем
    /// `доля·max`, а не первым узлом выше него. Пересечение считается здесь
    /// той же лог-лог интерполяцией, что `FsaEfficiency.TryEval`.
    /// </summary>
    static int Floor(List<ROIEfficiencyData> curve, double fraction)
    {
        var config = new EfficiencyConfigData { Curve = curve };
        BecquerelMonitor.FullSpectrumAnalysis.FsaEfficiency fsa =
            BecquerelMonitor.FullSpectrumAnalysis.FsaEfficiency.FromConfig(config);
        if (fsa == null)
        {
            Console.WriteLine("   (4) кривая FSA не собралась");
            return 1;
        }

        double floor = fsa.FloorAtFraction(fraction);
        var pts = new List<ROIEfficiencyData>();
        double top = 0.0;
        foreach (ROIEfficiencyData p in curve)
        {
            if (p.Energy > 0.0 && p.Efficiency > 0.0)
            {
                pts.Add(p);
                top = Math.Max(top, p.Efficiency);
            }
        }

        pts.Sort((a, b) => a.Energy.CompareTo(b.Energy));
        double want = Math.Log(top * fraction), crossing = 0.0;
        for (int i = 0; i < pts.Count; i++)
        {
            double y = Math.Log(pts[i].Efficiency);
            if (y >= want)
            {
                if (i == 0)
                {
                    crossing = pts[0].Energy;
                }
                else
                {
                    double y0 = Math.Log(pts[i - 1].Efficiency);
                    double x0 = Math.Log(pts[i - 1].Energy), x1 = Math.Log(pts[i].Energy);
                    crossing = Math.Exp(x0 + (want - y0) * (x1 - x0) / (y - y0));
                }

                break;
            }
        }

        bool ok = crossing > 0.0 && Math.Abs(floor / crossing - 1.0) < 1e-6;
        Console.WriteLine("   (4) пол полосы FSA при доле {0}: FloorAtFraction {1} кэВ, пересечение кривой {2} кэВ {3}",
                          F(fraction, 3), F(floor, 3), F(crossing, 3), ok ? "✅" : "⛔");
        return ok ? 0 : 1;
    }
}
