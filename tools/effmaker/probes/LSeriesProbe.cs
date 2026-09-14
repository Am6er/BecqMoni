using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Text;

namespace LSeriesProbe
{
    /// <summary>
    /// `A60`: ЧТО ЗНАЕТ РАСЧЁТ ПРО L-СЕРИЮ ЭЛЕМЕНТА — данные и доли по энергии.
    ///
    /// Заведена потому, что счётчик L-квантов в переносе показал РОВНО НОЛЬ, а
    /// причин у нуля может быть четыре: не загрузились данные, нет модели
    /// оболочек, подоболочка закрыта на этой энергии, либо доля вышла нулевой.
    /// Разбирать их рассуждением дороже, чем напечатать.
    ///
    ///     lseriesprobe --z=53 [--energy=59.541]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            int z = 53;
            double energy = 59.541;
            foreach (string a in args)
            {
                if (a.StartsWith("--z=", StringComparison.Ordinal)) z = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal)) energy = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // Load() внутренний — база поднимается первым же обращением.
            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(z);
            Console.WriteLine("Z = {0}, энергия {1:F3} кэВ", z, energy);
            if (f == null)
            {
                Console.WriteLine("  записи флуоресценции НЕТ ВОВСЕ");
                return 1;
            }

            Console.WriteLine("  K: край {0:F3} кэВ, ω {1:F4}, линий {2}",
                              f.KEdgeKev, f.OmegaK, f.LineKev != null ? f.LineKev.Length : 0);
            Console.WriteLine("  HasL: {0}", f.HasL);
            if (!f.HasL)
            {
                Console.WriteLine("  ⛔ L-данные не загрузились — искать в загрузчике MaterialDatabase");
                return 1;
            }

            MaterialDatabase.PhotoShellModel shells = MaterialDatabase.PhotoShellOf(z);
            Console.WriteLine("  модель оболочек: {0}", shells == null ? "НЕТ" : "есть");
            Console.WriteLine("  доля K по энергии: {0:F4}",
                              shells != null ? shells.KFraction(energy) : double.NaN);

            if (shells != null)
            {
                double den = 0.0;
                for (int s = 1; s < shells.ShellCount; s++)
                {
                    double v = shells.ShellCrossSection(s, energy);
                    if (v > 0.0) den += v;
                }

                Console.WriteLine("  оболочек в таблице {0}, сумма сечений НЕ-K при этой энергии {1:E4} барн",
                                  shells.ShellCount, den);
                for (int s = 0; s <= 3 && s < shells.ShellCount; s++)
                {
                    Console.WriteLine("     seq {0}: сечение {1:E4} барн", s, shells.ShellCrossSection(s, energy));
                }
            }

            string[] names = { "L1", "L2", "L3" };
            double sum = 0.0;
            for (int li = 0; li < f.OmegaL.Length; li++)
            {
                double frac = shells != null ? shells.LFraction(energy, li) : 0.0;
                bool open = energy > f.LEdgeKev[li];
                double add = open ? frac * f.OmegaL[li] : 0.0;
                sum += add;
                Console.WriteLine("  {0}: край {1:F4} кэВ, ω {2:F4}, линий {3,2}, доля {4:F5}, открыта {5}, вклад {6:F6}",
                                  names[li], f.LEdgeKev[li], f.OmegaL[li],
                                  f.LineKevL[li] != null ? f.LineKevL[li].Length : 0,
                                  frac, open ? "да" : "НЕТ", add);
                if (f.LineKevL[li] != null && f.LineKevL[li].Length > 0)
                {
                    Console.WriteLine("      линии, кэВ: {0:F3} … {1:F3}",
                                      f.LineKevL[li][0], f.LineKevL[li][f.LineKevL[li].Length - 1]);
                }
            }

            Console.WriteLine("  СУММАРНАЯ вероятность ответить L-квантом: {0:F6}", sum);
            return 0;
        }
    }
}
