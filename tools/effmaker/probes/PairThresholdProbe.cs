using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Приёмка `S121` — пороговой интерполяции сечения рождения пар, и разбор
/// возражения `S131` («множитель взят в обратную сторону»).
///
/// ЧТО ПРОВЕРЯЕТСЯ И ПОЧЕМУ ИМЕННО ТАК. Истинного сечения между узлами XCOM у
/// нас нет: таблица и есть источник. Поэтому сходимость мерится ВЫБРОСОМ УЗЛА:
/// узел временно убирается из сетки, значение в нём восстанавливается по
/// соседям — и обе (с `S131` — три) схемы сравниваются с тем, что в таблице
/// действительно лежит. Geant4 в этой проверке не участвует вовсе, участвует
/// сам XCOM.
///
/// ТРИ СХЕМЫ:
///
///   * `прежняя` — как было: участок с нулевым узлом интерполируется линейно
///     по значению, остальные лог-лог по САМОМУ сечению (сумме каналов);
///   * `новая` — `S121`: интерполируется частное σ/(1 − E₀/E)³, потом
///     умножается обратно на множитель (то, что стоит в коде);
///   * `обратная` — прочтение `S131`: интерполируется произведение
///     (1 − E₀/E)³·σ, потом делится на множитель.
///
/// ⛔ Спор о том, что написано в главе 3 XCOM, эта проба решает НЕ ЧТЕНИЕМ.
/// Разметка страницы теряет знак показателя, и обе стороны цитируют одну и ту
/// же строку. Мерка одна: какая схема воспроизводит ЧИСЛА таблицы.
///
/// Дополнительно печатается ПОКАЗАТЕЛЬ ПОРОГА p, снятый с самой таблицы по двум
/// нижним открытым узлам: σ ∝ (1 − E₀/E)^p. Он говорит, на что вообще делить.
///
///     pairthresholdprobe [--z=55] [--dump]
///
/// Ожидание: новая схема ближе прежней у подавляющего большинства узлов,
/// обратная — ХУЖЕ обеих, а показатель порога около 3.
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
        Console.WriteLine("  Z   узлов   прежняя: мед/худш    новая: мед/худш    обратная: мед/худш   p");

        var allOld = new List<double>();
        var allNew = new List<double>();
        var allInv = new List<double>();
        var exponents = new List<double>();
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
            var errInv = new List<double>();
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
                double inverted = Inverted(reduced, lo, hi, x, logX);

                double eOld = Math.Abs(asWas / truth - 1.0) * 100.0;
                double eNew = Math.Abs(asIs / truth - 1.0) * 100.0;
                double eInv = Math.Abs(inverted / truth - 1.0) * 100.0;
                errOld.Add(eOld);
                errNew.Add(eNew);
                errInv.Add(eInv);
                allOld.Add(eOld);
                allNew.Add(eNew);
                allInv.Add(eInv);
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
                                      + "  прежняя {3,8:F2} %  новая {4,8:F2} %  обратная {5,10:F2} %",
                                      z, x, truth, eOld, eNew, eInv);
                }
            }

            if (nodes == 0)
            {
                continue;
            }

            double p = ThresholdExponent(element);
            if (!double.IsNaN(p))
            {
                exponents.Add(p);
            }

            elements++;
            Console.WriteLine("  {0,-3} {1,5}   {2,8:F2} /{3,8:F1}   {4,8:F2} /{5,7:F1}   {6,9:F2} /{7,9:F1}  {8,5:F2}",
                              z, nodes, Median(errOld), Max(errOld),
                              Median(errNew), Max(errNew),
                              Median(errInv), Max(errInv), p);
        }

        Console.WriteLine();
        Console.WriteLine("элементов {0}, узлов {1}", elements, allOld.Count);
        Console.WriteLine("  медиана ошибки: прежняя {0:F2} %, новая {1:F2} %, обратная {2:F2} %",
                          Median(allOld), Median(allNew), Median(allInv));
        Console.WriteLine("  худший узел:    прежняя {0:F1} %, новая {1:F1} %, обратная {2:F1} %",
                          Max(allOld), Max(allNew), Max(allInv));
        Console.WriteLine("  новая ближе прежней у {0} узлов, дальше у {1}", betterNodes, worseNodes);
        Console.WriteLine("  показатель порога по таблице: медиана p = {0:F2} (ожидание около 3)",
                          Median(exponents));

        bool ok = allOld.Count > 0
                  && Median(allNew) < Median(allOld)
                  && Max(allNew) < Max(allOld)
                  && betterNodes > worseNodes;
        bool invWorse = Median(allInv) > Median(allOld) && Median(allInv) > Median(allNew);

        Console.WriteLine();
        Console.WriteLine(ok ? "СОШЛОСЬ: пороговая схема ближе к таблице по всем трём меркам"
                             : "НЕ СОШЛОСЬ: пороговая схема не лучше прежней");
        Console.WriteLine(invWorse
            ? "S131 ОТВЕРГНУТА ЗАМЕРОМ: обратный множитель хуже и прежней схемы, и нынешней"
            : "⚠ S131 ЗАМЕРОМ НЕ ОТВЕРГНУТА: обратный множитель не хуже — разбираться");
        return ok && invWorse ? 0 : 1;
    }

    /// <summary>
    /// Схема из возражения `S131`: интерполируется ПРОИЗВЕДЕНИЕ
    /// (1 − E₀/E)³·σ, а сечение восстанавливается делением на множитель.
    /// Считается здесь, а не в коде приложения: в код она не ставилась.
    /// </summary>
    static double Inverted(MaterialDatabase.Element element, int lo, int hi,
                           double energyKev, double logEnergyKev)
    {
        return InvertedChannel(element, lo, hi, energyKev, logEnergyKev, element.Channels[3], ThNuc)
             + InvertedChannel(element, lo, hi, energyKev, logEnergyKev, element.Channels[4], ThEl);
    }

    static double InvertedChannel(MaterialDatabase.Element element, int lo, int hi,
                                  double energyKev, double logEnergyKev,
                                  double[] sigma, double thresholdKev)
    {
        double shape = MaterialDatabase.PairThresholdShape(energyKev, thresholdKev);
        if (!(shape > 0.0))
        {
            return 0.0;
        }

        double[] grid = element.EnergyKev;
        double[] logGrid = element.LogEnergyKev;
        var logs = new double[sigma.Length];
        for (int i = 0; i < sigma.Length; i++)
        {
            double f = MaterialDatabase.PairThresholdShape(grid[i], thresholdKev);
            logs[i] = sigma[i] > 0.0 && f > 0.0 ? Math.Log(sigma[i] * f) : double.NaN;
        }

        int p, q;
        if (lo != hi && Open(logs, lo) && Open(logs, hi) && logGrid[hi] > logGrid[lo])
        {
            p = lo;
            q = hi;
        }
        else if (Open(logs, hi))
        {
            p = hi;
            q = hi + 1;
            if (!Open(logs, q) || !(logGrid[q] > logGrid[p]))
            {
                return Math.Exp(logs[p]) / shape;
            }
        }
        else
        {
            return 0.0;
        }

        double t = (logEnergyKev - logGrid[p]) / (logGrid[q] - logGrid[p]);
        return Math.Exp(logs[p] + t * (logs[q] - logs[p])) / shape;
    }

    /// <summary>
    /// Показатель порога p из САМОЙ таблицы: σ ∝ (1 − E₀/E)^p по двум нижним
    /// открытым узлам ядерного канала. На что делить — говорит он, а не разметка
    /// страницы.
    /// </summary>
    static double ThresholdExponent(MaterialDatabase.Element element)
    {
        double[] grid = element.EnergyKev;
        double[] sigma = element.Channels[3];
        int first = -1, second = -1;
        for (int i = 0; i < grid.Length; i++)
        {
            if (grid[i] > ThNuc && sigma[i] > 0.0)
            {
                if (first < 0)
                {
                    first = i;
                }
                else
                {
                    second = i;
                    break;
                }
            }
        }

        if (first < 0 || second < 0)
        {
            return double.NaN;
        }

        double t1 = 1.0 - ThNuc / grid[first];
        double t2 = 1.0 - ThNuc / grid[second];
        if (!(t1 > 0.0) || !(t2 > 0.0) || t1 == t2)
        {
            return double.NaN;
        }

        return (Math.Log(sigma[second]) - Math.Log(sigma[first]))
             / (Math.Log(t2) - Math.Log(t1));
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
        Console.WriteLine("     E, кэВ      прежняя         новая      обратная    прежн/новая");
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
            double inv = Inverted(cs, lo, hi, x, logX) * back;
            Console.WriteLine("  {0,9:F1} {1,12:G6} {2,13:G6} {3,13:G6} {4,13:F3}",
                              x, asWas, asIs, inv, asIs > 0.0 ? asWas / asIs : double.NaN);
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

    static bool Open(double[] logs, int i)
    {
        return i >= 0 && i < logs.Length
            && !double.IsNaN(logs[i]) && !double.IsInfinity(logs[i]);
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
