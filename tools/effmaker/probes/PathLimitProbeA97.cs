using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// ⛔ `AMBER97` (полоса П147, 24.09.2026, физика 24): ПРЕДЕЛ ПУТИ ЛУЧА.
///
/// До физики 24 симулятор обрывал луч, прошедший `40·sphereR + 200` см, и в
/// этот путь входил ПЕРВЫЙ шаг — от источника или от сферы поля до сцены. У
/// RC-103 (`sphereR` 1.44 см) предел 257.7 см: точечный источник дальше и поле
/// ISO большего радиуса давали ноль во всех узлах; человек видел «нет кривой»
/// без причины.
///
/// Проба меряет:
///   (1) ПОЛЕ ISO: эффективная площадь пика и полная на радиусах `--r=` против
///       первого — от радиуса зависеть не должна (3σ);
///   (2) ТОЧКА НА ОСИ на расстояниях `--d=`: `ε·(d + c)²` против первого, где
///       `c` — от торца до центра кристалла (дальнее поле: закон обратных
///       квадратов, 3σ плюс 1 % на неточность `c`);
///   (3) РЕДАКТОР: радиус поля больше `GeometryScenes.MaxFieldRadiusMm`
///       обязан дать отказ `GeometryEditorErrorFieldRadiusLarge` (ищется
///       отражением — на коде до правки поля нет), ровно предел — не обязан;
///   (4) СИМУЛЯТОР: радиус больше предела — исключение со словами, а не ноль;
///   (5) счётчик срезов `CountPathLimitCut` (отражением) — сколько лучей
///       оборвал предел на каждом плече.
///
///     pathlimitprobea97 [--in=tools\CORPUS\corpus\geometries\RC103_point0.in]
///                       [--n=200000] [--e=661.657] [--r=50,300,1000,10000]
///                       [--d=100,300,1000] [--seed=N]
///
/// Код 0 — все проверки прошли; 1 — есть отказ (на коде до правки ОЖИДАЕМО).
/// </summary>
static class PathLimitProbeA97
{
    static int failed;

    static string F(double v, int d)
    {
        return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static void Check(string what, bool ok, string detail)
    {
        Console.WriteLine("   {0,-62} {1}{2}", what, ok ? "ок" : "ПРОВАЛ", detail != null ? "  " + detail : "");
        if (!ok)
        {
            failed++;
        }
    }

    static double[] List(string s)
    {
        return Array.ConvertAll(s.Split(','), x => double.Parse(x, CultureInfo.InvariantCulture));
    }

    static long Cuts(EfficiencySimulator sim)
    {
        FieldInfo f = typeof(EfficiencySimulator).GetField("CountPathLimitCut");
        return f == null ? -1 : (long)f.GetValue(sim);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = Path.Combine("tools", "CORPUS", "corpus", "geometries", "RC103_point0.in");
        int histories = 200000;
        double energy = 661.657;
        double[] radii = { 50, 300, 1000, 10000 };
        double[] dists = { 100, 300, 1000 };
        int seed = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--e=", StringComparison.Ordinal)) energy = double.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--r=", StringComparison.Ordinal)) radii = List(a.Substring(4));
            else if (a.StartsWith("--d=", StringComparison.Ordinal)) dists = List(a.Substring(4));
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else
            {
                Console.Error.WriteLine("неизвестный ключ " + a);
                return 2;
            }
        }

        if (!File.Exists(inPath))
        {
            Console.Error.WriteLine("нет файла геометрии: " + inPath);
            return 2;
        }

        GlobalConfigManager.GetInstance();
        GeometryModel original = GeometryModel.Load(inPath);
        Console.WriteLine("предел пути (AMBER97): {0}; историй {1}, {2} кэВ", inPath, histories, F(energy, 3));

        // (1) поле ISO
        Console.WriteLine("(1) поле ISO, эффективная площадь, см²:");
        GeometryModel iso = original.Clone();
        iso.Scene = GeometrySceneKind.Iso;
        GeometryScenes.Apply(iso, 3000.0);
        double[] pk = new double[radii.Length], pkE = new double[radii.Length];
        double[] tt = new double[radii.Length], ttE = new double[radii.Length];
        for (int i = 0; i < radii.Length; i++)
        {
            GeometryModel g = iso.Clone();
            g.FieldRadius = radii[i] * GeometryModel.MmPerCm;
            GeometryScenes.Iso(g);
            try
            {
                EfficiencySimulator sim = Make(g, histories, seed);
                double e1;
                pk[i] = sim.Efficiency(energy, out e1);
                pkE[i] = e1;
                long c1 = Cuts(sim);
                EfficiencySimulator sim2 = Make(g, histories, seed);
                double e2;
                tt[i] = sim2.TotalEfficiency(energy, out e2);
                ttE[i] = e2;
                Console.WriteLine("   R = {0,7} см: A_пик {1} ± {2} %, A_полн {3} ± {4} %; срезов пределом {5} / {6}",
                                  F(radii[i], 0), F(pk[i], 4), F(pkE[i], 2), F(tt[i], 4), F(ttE[i], 2), c1, Cuts(sim2));
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("   R = {0,7} см: отказ «{1}»", F(radii[i], 0), ex.Message);
                pk[i] = double.NaN;
            }
        }

        for (int i = 1; i < radii.Length; i++)
        {
            if (double.IsNaN(pk[i]))
            {
                Check("R = " + F(radii[i], 0) + " см посчитан", false, "отказ");
                continue;
            }

            Pull("A_пик(" + F(radii[0], 0) + ") = A_пик(" + F(radii[i], 0) + ")", pk[0], pkE[0], pk[i], pkE[i], 0.0);
            Pull("A_полн(" + F(radii[0], 0) + ") = A_полн(" + F(radii[i], 0) + ")", tt[0], ttE[0], tt[i], ttE[i], 0.0);
        }

        // (2) точка на оси
        Console.WriteLine("(2) точка на оси, ε·(d + c)²:");
        double c = original.CrystalHeight / GeometryModel.MmPerCm * 0.5
                   + (original.FrontReflectorThickness + original.FrontGapThickness
                      + original.FrontCladdingThickness) / GeometryModel.MmPerCm;
        double[] ed = new double[dists.Length], edE = new double[dists.Length];
        for (int i = 0; i < dists.Length; i++)
        {
            GeometryModel g = original.Clone();
            g.SourceType = GeometrySourceType.Point;
            g.PointDistance = dists[i] * GeometryModel.MmPerCm;
            EfficiencySimulator sim = Make(g, histories, seed);
            double err;
            double eff = sim.Efficiency(energy, out err);
            double dc = dists[i] + c;
            ed[i] = eff * dc * dc;
            edE[i] = err;
            Console.WriteLine("   d = {0,6} см: ε {1:E4} ± {2} %, ε·(d+c)² = {3} см²; срезов пределом {4}",
                              F(dists[i], 0), eff, F(err, 2), F(ed[i], 5), Cuts(sim));
        }

        for (int i = 1; i < dists.Length; i++)
        {
            Pull("ε·(d+c)² при " + F(dists[0], 0) + " и " + F(dists[i], 0) + " см", ed[0], edE[0], ed[i], edE[i], 0.01);
        }

        // (3) редактор
        Console.WriteLine("(3) редактор:");
        FieldInfo maxField = typeof(GeometryScenes).GetField("MaxFieldRadiusMm");
        if (maxField == null)
        {
            Check("есть GeometryScenes.MaxFieldRadiusMm", false, "верхней границы радиуса поля нет");
        }
        else
        {
            double max = (double)maxField.GetValue(null);
            GeometryModel big = iso.Clone();
            big.FieldRadius = 1.5 * max;
            List<GeometryScenes.Issue> issues = GeometryScenes.Inconsistencies(big);
            Check("радиус 1.5×предела — отказ GeometryEditorErrorFieldRadiusLarge",
                  issues.Count == 1 && issues[0].Field == "FieldRadius"
                  && issues[0].Resource == "GeometryEditorErrorFieldRadiusLarge",
                  "предел " + F(max, 0) + " мм");
            GeometryModel edge = iso.Clone();
            edge.FieldRadius = max;
            Check("радиус ровно на пределе — отказа нет", GeometryScenes.Inconsistencies(edge).Count == 0, null);
            string text = BecquerelMonitor.Properties.Resources.ResourceManager.GetString(
                "GeometryEditorErrorFieldRadiusLarge", CultureInfo.InvariantCulture);
            string textRu = BecquerelMonitor.Properties.Resources.ResourceManager.GetString(
                "GeometryEditorErrorFieldRadiusLarge", new CultureInfo("ru"));
            Check("строка отказа есть в обоих языках", !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(textRu)
                  && text != textRu, text);

            // (4) симулятор
            Console.WriteLine("(4) симулятор:");
            GeometryModel g4 = iso.Clone();
            g4.FieldRadius = 1.5 * max;
            GeometryScenes.Iso(g4);
            string refused = null;
            try
            {
                EfficiencySimulator sim = Make(g4, 2000, seed);
                double err;
                sim.Efficiency(energy, out err);
            }
            catch (InvalidOperationException ex)
            {
                refused = ex.Message;
            }

            Check("радиус 1.5×предела — исключение со словами", refused != null && refused.Contains("maximum"), refused);
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "✅ все проверки прошли" : "⛔ отказов: " + failed);
        return failed == 0 ? 0 : 1;
    }

    static void Pull(string what, double a, double aErr, double b, double bErr, double relSlack)
    {
        double sigma = Math.Sqrt(Math.Pow(a * aErr / 100.0, 2.0) + Math.Pow(b * bErr / 100.0, 2.0)
                                 + Math.Pow(relSlack * a, 2.0));
        double pull = sigma > 0.0 ? Math.Abs(b - a) / sigma : double.PositiveInfinity;
        Check(what + " в пределах 3σ", pull <= 3.0,
              string.Format(CultureInfo.InvariantCulture, "отношение {0}, {1} σ",
                            a > 0.0 ? F(b / a, 4) : "—", F(pull, 2)));
    }

    static EfficiencySimulator Make(GeometryModel g, int histories, int seed)
    {
        EfficiencySimulator sim = new EfficiencySimulator(g) { Histories = histories };
        if (seed != 0)
        {
            sim.ResetStream((ulong)seed);
        }

        return sim;
    }
}
