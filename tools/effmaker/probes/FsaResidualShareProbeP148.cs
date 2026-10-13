using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FsaResidualShareProbeP148
{
    /// <summary>
    /// (`AMBER91`, П148 24.09.2026) СТРОКА НЕВЯЗКИ ПРИ ПРОБЕ НА УРОВНЕ ФОНА.
    /// Ревизия П143: при Σ(проба − фон) ≤ 0 `FsaResult.ComputeResidualShares`
    /// подменял неопределённую долю нулём, и строка отчёта печатала
    /// «+0.0 % / −0.0 %» — «модель идеальна» — при нарисованной ленте.
    ///
    /// Игрушка ревизии: пуассоновские копии слабой пробы с фоном той же
    /// длины (множитель фона 1), модель ЗАВЕДОМО ВЕРНА — это истинное ожидание
    /// источника. Результат собирается руками (`Model`, `Background`, полоса) и
    /// идёт тем же `ComputeResidualShares` и той же сборкой строки
    /// (`FsaPresentationBuilder.Build`), что у приложения. Печатается, сколько
    /// копий дали «0.0 / 0.0», медиана долей остальных и строка отчёта.
    ///
    ///   fsaresidualshareprobep148 [--copies=400] [--seed=1] [--source=0,3]
    ///
    /// `--source=` — площадь источника в единицах σ чистого счёта
    /// (σ = √(2·Σфона)): 0 — проба без источника (ровно игрушка ревизии), 3 —
    /// слабый, но уверенно видимый источник, 30 — сильный (контроль: строка
    /// обязана остаться числом). Приговор (код 1): после правки (свойство
    /// `FsaResult.ResidualSharesDefined` есть) ни одна копия не печатает
    /// «0.0 / 0.0» при неопределённом знаменателе, неопределённая строка
    /// несёт «—» и пометку, а у источника 30σ строка — число у всех копий.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            int copies = 400, seed = 1;
            var sources = new List<double> { 0.0, 3.0, 30.0 };
            string culture = "ru-RU";
            foreach (string a in args)
            {
                if (a.StartsWith("--copies=", StringComparison.Ordinal)) copies = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--culture=", StringComparison.Ordinal)) culture = a.Substring(10);
                else if (a.StartsWith("--source=", StringComparison.Ordinal))
                {
                    sources.Clear();
                    foreach (string s in a.Substring(9).Split(','))
                    {
                        sources.Add(double.Parse(s, CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            PropertyInfo defined = typeof(FsaResult).GetProperty("ResidualSharesDefined");
            Console.WriteLine("SETUP\tFsaResult.ResidualSharesDefined {0}; копий {1}; зерно {2}; культура {3}",
                              defined != null ? "есть" : "НЕТ (сборка до П148)", copies, seed, culture);

            const int channels = 1024;
            double[] bg = new double[channels];
            double bgTotal = 0.0;
            for (int i = 0; i < channels; i++)
            {
                bg[i] = 2.0 + 8.0 * Math.Exp(-i / 300.0);
                bgTotal += bg[i];
            }

            double sigmaNet = Math.Sqrt(2.0 * bgTotal);
            int bad = 0;
            foreach (double sourceSigma in sources)
            {
                double sourceArea = sourceSigma * sigmaNet;
                double[] src = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    double x = (i - 500.0) / 12.0;
                    src[i] = sourceArea * Math.Exp(-0.5 * x * x) / (12.0 * Math.Sqrt(2.0 * Math.PI));
                }

                var rng = new Random(seed);
                int zeroZero = 0, undefined = 0, defs = 0, numeric = 0, dash = 0;
                var missing = new List<double>();
                var excess = new List<double>();
                string exampleZero = null, exampleUndefined = null, exampleNumber = null, exampleHint = null;
                for (int k = 0; k < copies; k++)
                {
                    int[] raw = new int[channels];
                    double[] background = new double[channels];
                    for (int i = 0; i < channels; i++)
                    {
                        raw[i] = Poisson(rng, bg[i] + src[i]);
                        background[i] = Poisson(rng, bg[i]);
                    }

                    var result = new FsaResult
                    {
                        Model = (double[])src.Clone(),
                        Background = background,
                        FirstChannel = 0,
                        LastChannel = channels - 1,
                        ResidualFloorChannel = 0,
                        Components = new List<FsaComponentResult>()
                    };
                    PropertyInfo scale = typeof(FsaResult).GetProperty("BackgroundScale");
                    if (scale != null)
                    {
                        scale.SetValue(result, 1.0, null);
                    }

                    result.ComputeResidualShares(raw);
                    bool isDefined = defined == null || (bool)defined.GetValue(result, null);
                    if (!isDefined) undefined++; else defs++;

                    FsaPresentation presentation = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
                    FsaReportRow row = null;
                    foreach (FsaReportRow r in presentation.Rows)
                    {
                        if (r.Kind == FsaReportRowKind.Residual) row = r;
                    }

                    string value = row != null ? row.Value : "(строки невязки нет)";
                    bool isZero = value.Contains("0.0") && result.ResidualExcessShare == 0.0 && result.ResidualMissingShare == 0.0;
                    if (isZero)
                    {
                        zeroZero++;
                        exampleZero = exampleZero ?? value;
                    }
                    else if (value.IndexOf('—') >= 0 && !value.Contains("%"))
                    {
                        dash++;
                        exampleUndefined = exampleUndefined ?? value;
                        exampleHint = exampleHint ?? (row.Hint ?? "(без подсказки)") + (row.Warning ? " [Warning]" : "");
                    }
                    else
                    {
                        numeric++;
                        missing.Add(result.ResidualMissingShare);
                        excess.Add(result.ResidualExcessShare);
                        exampleNumber = exampleNumber ?? value;
                    }
                }

                Console.WriteLine();
                Console.WriteLine("=== источник {0}σ ({1} отсч.; фон {2} отсч., σ чистого {3}) ===",
                                  F(sourceSigma, "F1"), F(sourceArea, "F0"), F(bgTotal, "F0"), F(sigmaNet, "F1"));
                Console.WriteLine("RESID\t{0}\tкопий {1}\t«0.0/0.0» {2}\t«—» {3}\tчисло {4}\tнеопределено {5}\tмедиана «не описано» {6} %\tмедиана «лишнее» {7} %",
                                  F(sourceSigma, "F1"), copies, zeroZero, dash, numeric, undefined,
                                  F(100.0 * Median(missing), "F1"), F(100.0 * Median(excess), "F1"));
                Console.WriteLine("   пример «0.0/0.0»: {0}", exampleZero ?? "—нет—");
                Console.WriteLine("   пример неопределённой строки: {0}; подсказка: {1}", exampleUndefined ?? "—нет—", exampleHint ?? "—нет—");
                Console.WriteLine("   пример числовой строки: {0}", exampleNumber ?? "—нет—");

                if (defined != null)
                {
                    if (zeroZero > 0)
                    {
                        Console.WriteLine("   ⛔ ПРИГОВОР: «0.0/0.0» у {0} копий при неопределённом знаменателе", zeroZero);
                        bad++;
                    }

                    if (dash != undefined)
                    {
                        Console.WriteLine("   ⛔ ПРИГОВОР: неопределённых {0}, а строк «—» {1}", undefined, dash);
                        bad++;
                    }

                    if (sourceSigma >= 30.0 && numeric != copies)
                    {
                        Console.WriteLine("   ⛔ КОНТРОЛЬ: сильный источник, а числом строка у {0} из {1}", numeric, copies);
                        bad++;
                    }

                    if (dash > 0 && (exampleHint == null || exampleHint.StartsWith("(без", StringComparison.Ordinal)))
                    {
                        Console.WriteLine("   ⛔ ПРИГОВОР: у «—» нет подсказки");
                        bad++;
                    }
                }
            }

            Console.WriteLine();
            if (defined == null)
            {
                Console.WriteLine("сборка до П148: приговора нет, печатается замер");
                return 0;
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static int Poisson(Random rng, double mean)
        {
            if (mean > 30.0)
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * z));
            }

            double l = Math.Exp(-mean), p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= rng.NextDouble();
            }
            while (p > l);
            return k - 1;
        }

        static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            values.Sort();
            int n = values.Count;
            return n % 2 == 1 ? values[n / 2] : 0.5 * (values[n / 2 - 1] + values[n / 2]);
        }

        static string F(double v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }
    }
}
