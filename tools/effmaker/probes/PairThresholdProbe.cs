using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Приёмка `S121` — пороговой интерполяции сечения рождения пар.
///
/// ЧТО ПРОВЕРЯЕТСЯ И ПОЧЕМУ ИМЕННО ТАК. Истинного сечения между узлами XCOM у
/// нас нет: таблица и есть источник. Поэтому сходимость мерится ВЫБРОСОМ УЗЛА:
/// узел временно убирается из сетки, значение в нём восстанавливается по
/// соседям — сперва прежней веткой («один узел нулевой — интерполируй линейно
/// по log E»), затем новой (интерполяция частного σ/(1 − E₀/E)³), — и обе
/// сравниваются с тем, что в таблице действительно лежит.
///
/// Это проверка независимая: Geant4 в ней не участвует вовсе, а участвует сам
/// XCOM. Ответ у неё числовой, а не «стало красивее».
///
///     pairthresholdprobe [--z=55] [--dump]
///
/// Ожидание: новая схема ближе прежней у подавляющего большинства узлов, и
/// заметнее всего у первого узла над порогом. Печатается по элементу медиана и
/// худший узел обеих схем, в конце — свод.
/// </summary>
static class PairThresholdProbe
{
    const double ThNuc = 1022.0;
    const double ThEl = 2044.0;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        int only = 0;
        bool dump = false;
        foreach (string s in args)
        {
            if (s.StartsWith("--z=", StringComparison.Ordinal))
            {
                only = int.Parse(s.Substring(4), CultureInfo.InvariantCulture);
            }
            else if (s == "--dump")
            {
                dump = true;
            }
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + s);
                return 2;
            }
        }

        Headline();

        Console.WriteLine();
        Console.WriteLine("выброс узла и восстановление по соседям, ошибка в процентах:");
        Console.WriteLine("  Z   узлов   прежняя: медиана  худший     новая: медиана  худший   новая лучше");

        var allOld = new List<double>();
        var allNew = new List<double>();
        int betterNodes = 0, worseNodes = 0, elements = 0;

        for (int z = 1; z <= 100; z++)
        {
            if (only > 0 && z != only)
            {
                continue;
            }

            MaterialDatabase.Element element;
            if (!MaterialDatabase.TryGet(z, out element) || element.EnergyKev == null)
            {
                continue;
            }

            var errOld = new List<double>();
            var errNew = new List<double>();
            int better = 0, nodes = 0;
            for (int k = 0; k < element.EnergyKev.Length; k++)
            {
                double truth = element.Channels[3][k] + element.Channels[4][k];
                if (!(truth > 0.0) || !(element.EnergyKev[k] > ThNuc))
                {
                    continue;
                }

                MaterialDatabase.Element reduced = Without(element, k);
                double x = element.EnergyKev[k];
                double logX = Math.Log(x);
                int lo, hi;
                if (!MaterialDatabase.Bracket(reduced.EnergyKev, x, out lo, out hi))
                {
                    continue;
                }

                double asWas = PartialCrossSections.MassCrossSection(
                    reduced, lo, hi, x, logX, PhotonProcess.PairProduction);
                double asIs = PartialCrossSections.MassCrossSection(
                    reduced, lo, hi, x, logX, PhotonProcess.PairProduction, true);

                double eOld = Math.Abs(asWas / truth - 1.0) * 100.0;
                double eNew = Math.Abs(asIs / truth - 1.0) * 100.0;
                errOld.Add(eOld);
                errNew.Add(eNew);
                allOld.Add(eOld);
                allNew.Add(eNew);
                nodes++;
                if (eNew < eOld)
                {
                    better++;
                    betterNodes++;
                }
                else if (eNew > eOld)
                {
                    worseNodes++;
                }

                if (dump)
                {
                    Console.WriteLine("      Z={0,-3} узел {1,10:F1} кэВ  истина {2:E5}"
                                      + "  прежняя {3,8:F2} %  новая {4,8:F2} %",
                                      z, x, truth, eOld, eNew);
                }
            }

            if (nodes == 0)
            {
                continue;
            }

            elements++;
            Console.WriteLine("  {0,-3} {1,5}      {2,10:F2} {3,8:F1}      {4,10:F2} {5,8:F1}   {6,4}/{7}",
                              z, nodes, Median(errOld), Max(errOld),
                              Median(errNew), Max(errNew), better, nodes);
        }

        Console.WriteLine();
        Console.WriteLine("элементов {0}, узлов {1}", elements, allOld.Count);
        Console.WriteLine("  медиана ошибки: прежняя {0:F2} %, новая {1:F2} %",
                          Median(allOld), Median(allNew));
        Console.WriteLine("  худший узел:    прежняя {0:F1} %, новая {1:F1} %",
                          Max(allOld), Max(allNew));
        Console.WriteLine("  новая ближе у {0} узлов, дальше у {1}", betterNodes, worseNodes);

        bool ok = allOld.Count > 0
                  && Median(allNew) < Median(allOld)
                  && Max(allNew) < Max(allOld)
                  && betterNodes > worseNodes;
        Console.WriteLine();
        Console.WriteLine(ok ? "СОШЛОСЬ: пороговая схема ближе к таблице по всем трём меркам"
                             : "НЕ СОШЛОСЬ: пороговая схема не лучше прежней");
        return ok ? 0 : 1;
    }

    /// <summary>Числа, которыми названа задача: цезий у порога и на 2614 кэВ.</summary>
    static void Headline()
    {
        MaterialDatabase.Element cs;
        if (!MaterialDatabase.TryGet(55, out cs))
        {
            Console.WriteLine("цезия в базе нет");
            return;
        }

        double back = cs.AtomicWeight / (1e-24 * 6.02214076e23);   // см2/г -> барн/атом
        Console.WriteLine("цезий, сечение рождения пар, барн/атом:");
        Console.WriteLine("     E, кэВ      прежняя         новая     отношение");
        foreach (double x in new[] { 1030.0, 1050.0, 1100.0, 1200.0, 1350.0, 1750.0, 2614.5 })
        {
            int lo, hi;
            if (!MaterialDatabase.Bracket(cs.EnergyKev, x, out lo, out hi))
            {
                continue;
            }

            double logX = Math.Log(x);
            double asWas = PartialCrossSections.MassCrossSection(
                cs, lo, hi, x, logX, PhotonProcess.PairProduction) * back;
            double asIs = PartialCrossSections.MassCrossSection(
                cs, lo, hi, x, logX, PhotonProcess.PairProduction, true) * back;
            Console.WriteLine("  {0,9:F1} {1,12:G6} {2,13:G6} {3,13:F3}",
                              x, asWas, asIs, asIs > 0.0 ? asWas / asIs : double.NaN);
        }
    }

    /// <summary>Копия элемента БЕЗ узла <paramref name="drop"/>.</summary>
    static MaterialDatabase.Element Without(MaterialDatabase.Element src, int drop)
    {
        int n = src.EnergyKev.Length;
        var copy = new MaterialDatabase.Element();
        copy.AtomicWeight = src.AtomicWeight;
        copy.EnergyKev = Cut(src.EnergyKev, drop);
        copy.Total = Cut(src.Total, drop);
        copy.Channels = new double[5][];
        for (int c = 0; c < 5; c++)
        {
            copy.Channels[c] = Cut(src.Channels[c], drop);
        }

        copy.LogEnergyKev = Logs(copy.EnergyKev);
        copy.LogTotal = Logs(copy.Total);
        copy.LogChannels = new double[4][];
        for (int c = 0; c < 3; c++)
        {
            copy.LogChannels[c] = Logs(copy.Channels[c]);
        }

        var sum = new double[n - 1];
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] = copy.Channels[3][i] + copy.Channels[4][i];
        }

        copy.LogChannels[3] = Logs(sum);
        copy.LogPairNuclearShape = ShapeLogs(copy.EnergyKev, copy.Channels[3], ThNuc);
        copy.LogPairElectronShape = ShapeLogs(copy.EnergyKev, copy.Channels[4], ThEl);
        return copy;
    }

    static double[] Cut(double[] source, int drop)
    {
        var result = new double[source.Length - 1];
        for (int i = 0, j = 0; i < source.Length; i++)
        {
            if (i != drop)
            {
                result[j++] = source[i];
            }
        }

        return result;
    }

    static double[] Logs(double[] values)
    {
        var logs = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            logs[i] = values[i] > 0.0 ? Math.Log(values[i]) : double.NaN;
        }

        return logs;
    }

    static double[] ShapeLogs(double[] energyKev, double[] sigma, double thresholdKev)
    {
        var logs = new double[sigma.Length];
        for (int i = 0; i < sigma.Length; i++)
        {
            double shape = MaterialDatabase.PairThresholdShape(energyKev[i], thresholdKev);
            logs[i] = sigma[i] > 0.0 && shape > 0.0
                ? Math.Log(sigma[i] / shape) : double.NaN;
        }

        return logs;
    }

    static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = new List<double>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    static double Max(List<double> values)
    {
        double best = 0.0;
        foreach (double v in values)
        {
            if (v > best)
            {
                best = v;
            }
        }

        return best;
    }
}
