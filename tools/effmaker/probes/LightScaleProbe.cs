using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LightScaleProbe
{
    /// <summary>
    /// Поверка непропорциональности светового выхода (F11): фотонная кривая
    /// модели против измеренной.
    ///
    /// Симулятор взвешивает электронные вклады кривой L(E) из nucdb и после
    /// прогона отдаёт <see cref="EfficiencySimulator.LastPhotonLightScale"/> —
    /// средний свет пика полного поглощения на кэВ линии. Это ровно та
    /// величина, которую меряли Ходюк и Доренбос (photon-nPR, 100 % на
    /// 662 кэВ): у CsI:Tl 112 % на 10 кэВ, горб к 10–20 кэВ, провал у
    /// K-краёв иода (33.17 кэВ) и цезия (35.98 кэВ) — arXiv:1204.4350,
    /// таблица I и текст к рис. 15. Электронная кривая в базе НЕ содержит
    /// краёв — провал обязан родиться в переносе, из смены разбиения энергии
    /// между фотоэлектроном и каскадом на крае. Появился — значит, связка
    /// «кривая + перенос» работает; его глубина и положение — сверка модели.
    ///
    ///     lightscaleprobe --geometry=X.in [--energies=10,20,...]
    ///                     [--n=400000] [--bin=1] [--off]
    ///
    /// --off считает с выключенным ключом: колонка света обязана стать
    /// пустой, а кривая эффективности — не измениться ни на бит.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            int histories = 400000;
            double binKev = 1.0;
            bool off = false;
            var energies = new List<double>
            {
                10, 15, 20, 25, 30, 32, 33, 34, 35, 36, 37, 40, 45, 59.5,
                80, 100, 150, 200, 300, 450, 661.657, 1000, 1332.5,
            };

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a == "--off") off = true;
                else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string part in a.Substring(11).Split(','))
                    {
                        energies.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var first = new EfficiencySimulator(geometry.Clone())
            {
                LightNonproportionality = !off,
            };
            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("кривая света: {0}; историй {1}, бин {2:F2} кэВ",
                first.LightYieldName == "" ? "НЕТ (шкала пропорциональна)" : first.LightYieldName,
                histories, binKev);
            Console.WriteLine();
            Console.WriteLine("   E, кэВ    свет пика/E   ±статистика, %    (Khodyuk CsI:Tl: 1.12 на 10 кэВ, провал у 33.2/36.0)");

            foreach (double e in energies)
            {
                var sim = new EfficiencySimulator(geometry.Clone())
                {
                    Histories = histories,
                    PeakHalfWidthKev = 0.0,
                    LightNonproportionality = !off,
                };
                sim.ResetStream((ulong)sim.Seed ^ (ulong)Math.Round(e * 64.0) * 0x9E3779B97F4A7C15UL);
                double err;
                sim.Response(e, binKev, out err);
                double scale = sim.LastPhotonLightScale;
                Console.WriteLine("  {0,7:F1}    {1}         {2,5:F2}",
                    e,
                    scale > 0.0 ? scale.ToString("F4", CultureInfo.InvariantCulture) : "  —  ",
                    err);
            }

            return 0;
        }
    }
}
