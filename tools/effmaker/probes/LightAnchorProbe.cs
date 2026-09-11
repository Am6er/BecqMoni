using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LightAnchorProbe
{
    /// <summary>
    /// ЦЕНА РАСХОЖДЕНИЯ ДВУХ ПРАВИЛ БИНА (`A267`) — числом, из ОДНОГО прогона.
    ///
    /// ЗАЧЕМ. `EfficiencySimulator.Deposit` кладёт вес истории в бин пика
    /// только при `InPeak`, иначе двигает в `peak−1`; `ScoreLight` до
    /// 10.09.2026 такой оговорки не имел и свет той же истории оставлял в
    /// `lightSum[peak]`. А `lightSum[peak]` — ЯКОРЬ всей световой шкалы
    /// (`RemapLightScale`): лишний свет в якоре двигает по бинам ВСЕ строки
    /// матрицы. Величина сдвига до сих пор не была названа.
    ///
    /// ⛔ ПОЧЕМУ НЕ ДВУМЯ СБОРКАМИ. Дерево правят соседние полосы, и разность
    /// «сборка до» против «сборки после» вобрала бы их правки. Симулятор
    /// поэтому считает ОБА якоря сразу — <see
    /// cref="EfficiencySimulator.LastPhotonLightScale"/> (действующее правило) и
    /// <see cref="EfficiencySimulator.LastPhotonLightScaleSplit"/> (единое), —
    /// а проба печатает их отношение. Розыгрыш у обеих оценок один и тот же по
    /// построению.
    ///
    ///     lightanchorprobe --geometry=X.in [--energies=59.5,662,...]
    ///                      [--n=400000] [--bin=1] [--peakw] [--seed=N]
    ///
    /// ⛔ `--peakw` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, а не удобство. Он ставит допуск
    /// пика из геометрии (`E34`, ключ склада `--peakw=1`). Полуширина ПШПВ
    /// обгоняет окно округления уже с ~6 кэВ, поэтому расходящийся класс на
    /// этом плече обязан быть ПУСТ, а отношение якорей — ровно 1. Плечо, где
    /// проба показывает то же самое, что и без ключа, не меряет ничего.
    ///
    /// Столбцы: `свет/E` — действующий якорь; `единое` — он же по единому
    /// правилу; `сдвиг %` — на столько сдвинется шкала бинов после сведения
    /// правила (индекс бина обратно пропорционален якорю); `класс` — доля веса
    /// историй, у которых бин веса и бин света разошлись, от веса бина пика.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            int histories = 400000;
            int seed = 0;
            double binKev = 1.0;
            bool peakw = false;
            var energies = new List<double>
            {
                30, 59.5, 122, 356, 661.657, 1173.2, 1332.5, 2614.5,
            };

            foreach (string a in args)
            {
                if (a == "--peakw") { peakw = true; continue; }
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
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
            var probe = new EfficiencySimulator(geometry.Clone());
            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("кривая света: {0}",
                probe.LightYieldName == "" ? "НЕТ (шкала пропорциональна, замерять нечего)" : probe.LightYieldName);
            Console.WriteLine("историй {0}, бин {1} кэВ, ПШПВ(662) геометрии {2} %",
                histories.ToString(CultureInfo.InvariantCulture),
                binKev.ToString("F2", CultureInfo.InvariantCulture),
                geometry.FwhmAt662Percent.ToString("F2", CultureInfo.InvariantCulture));
            Console.WriteLine("допуск пика: {0}",
                peakw ? "ИЗ ГЕОМЕТРИИ (--peakw, `E34`) — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, класс обязан быть пуст"
                      : "НОЛЬ (как у поставочного склада)");
            Console.WriteLine();
            Console.WriteLine("    E, кэВ   допуск   свет/E     единое     сдвиг %   класс: историй    вес/пик %");

            int nonEmpty = 0;
            foreach (double e in energies)
            {
                var sim = new EfficiencySimulator(geometry.Clone())
                {
                    Histories = histories,
                    PeakHalfWidthKev = peakw ? geometry.PeakHalfWidthKev(e) : 0.0,
                };
                if (seed != 0)
                {
                    sim.Seed = seed;
                }

                sim.ResetStream((ulong)sim.Seed ^ (ulong)Math.Round(e * 64.0) * 0x9E3779B97F4A7C15UL);
                double err;
                double[] response = sim.Response(e, binKev, out err);
                double now = sim.LastPhotonLightScale;
                double one = sim.LastPhotonLightScaleSplit;
                double shift = now > 0.0 && one > 0.0 ? (now / one - 1.0) * 100.0 : 0.0;
                double peakWeight = response != null && response.Length > 0
                    ? response[response.Length - 1] * Math.Max(1000, histories)
                    : 0.0;
                if (sim.CountLightBinSplit > 0)
                {
                    nonEmpty++;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,8:F1}   {1,6:F2}   {2}   {3}   {4,7:F3}   {5,14}   {6,10:F3}",
                    e, sim.PeakHalfWidthKev,
                    now > 0.0 ? now.ToString("F6", CultureInfo.InvariantCulture) : "   —    ",
                    one > 0.0 ? one.ToString("F6", CultureInfo.InvariantCulture) : "   —    ",
                    shift, sim.CountLightBinSplit,
                    peakWeight > 0.0 ? 100.0 * sim.WeightLightBinSplit / peakWeight : 0.0));
            }

            Console.WriteLine();
            if (peakw)
            {
                // ⛔ Контроль СУДИТ, а не украшает: пустой класс на этом плече —
                // условие, при котором числа второго плеча вообще что-то значат.
                if (nonEmpty == 0)
                {
                    Console.WriteLine("КОНТРОЛЬ ПРОШЁЛ: с допуском из геометрии расходящийся класс ПУСТ на всех узлах.");
                    return 0;
                }

                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "⛔ КОНТРОЛЬ ОТКАЗАЛ: класс непуст на {0} узлах из {1} — допуск не перекрывает окно округления,",
                    nonEmpty, energies.Count));
                Console.Error.WriteLine("   и «пусто/непусто» перестало отличать правило от правила.");
                return 1;
            }

            if (nonEmpty == 0)
            {
                Console.Error.WriteLine("⛔ КЛАСС ПУСТ НА ВСЕХ УЗЛАХ БЕЗ КОНТРОЛЬНОГО КЛЮЧА — мерить нечего:");
                Console.Error.WriteLine("   либо кривой света нет, либо допуск пика ненулевой. Числа ниже пусты не по делу.");
                return 1;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "класс непуст на {0} узлах из {1}.", nonEmpty, energies.Count));
            return 0;
        }
    }
}
