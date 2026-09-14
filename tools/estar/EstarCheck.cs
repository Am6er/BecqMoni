using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace EstarCheck
{
    /// <summary>
    /// Приёмка `N9`: счёт ESTAR из базы против вшитой таблицы и против эталона
    /// самого NIST.
    ///
    ///     estarcheck [--n=2000000]
    ///
    /// Три раздела, и они меряют РАЗНОЕ.
    ///
    /// 1. Пробег и выход, посчитанные <see cref="EstarCalculator"/> из
    ///    `matdb.sqlite`, против чисел, вшитых в <see cref="ElectronData"/>.
    ///    Все тринадцать веществ, все 55 узлов сетки. Это сверка НА
    ///    САМОСОГЛАСОВАННОСТЬ: вшитые числа сами получены тем же алгоритмом
    ///    (`tools/estar/estar.py`) и записаны с тремя значащими цифрами, так что
    ///    ниже ~0.05 % такая сверка не разрешает в принципе.
    ///
    /// 2. Тормозная способность против `estar_collision_stopping` — это ВЫХОД
    ///    настоящего ESTAR, а не наш. Он есть только у CsI (141) и NaI (252),
    ///    зато на всех 82 точках ОТ 1 кэВ. Пробег и выход — интегралы ровно
    ///    этих двух кривых, поэтому раздел 2 и есть внешняя поверка низа шкалы:
    ///    у пробега и выхода внешнего эталона ниже 10 кэВ нет вовсе
    ///    (`tools/estar/reference.py` — 39 точек со старой сетки от 10 кэВ).
    ///
    /// 3. Цена. Таблица строится один раз на вещество, дальше горячий путь
    ///    (<see cref="ElectronData.RangeOf"/>) не изменился — раздел это и меряет.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            int calls = 2000000;
            foreach (string a in args)
            {
                if (a.StartsWith("--n=", StringComparison.Ordinal))
                {
                    calls = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            string[] names = ElectronData.Names();
            Console.WriteLine("1. Счёт из базы против вшитой таблицы, {0} веществ × 55 узлов",
                              names.Length);
            Console.WriteLine();
            Console.WriteLine("   ⚠ Вшитые числа записаны с ТРЕМЯ значащими цифрами, и их");
            Console.WriteLine("   округление само по себе даёт до 0.05 %. Это сверка на");
            Console.WriteLine("   самосогласованность, а не с NIST — с NIST раздел 2.");
            Console.WriteLine();
            Console.WriteLine("   вещество   I, эВ   пробег: медиана / макс, %   выход: медиана / макс, %");

            double worstRange = 0.0, worstYield = 0.0;
            string worstRangeAt = "", worstYieldAt = "";
            double worstLow = 0.0;

            // Первое же обращение строит ВСЕ тринадцать таблиц разом — вот его
            // и меряем, а не цикл сверки вокруг.
            Stopwatch build = Stopwatch.StartNew();
            ElectronData.ByName(names[0]);
            build.Stop();

            foreach (string name in names)
            {
                ElectronData.Material live = ElectronData.ByName(name);
                ElectronData.Material builtin = ElectronData.BuiltinByName(name);
                if (live == null || builtin == null)
                {
                    Console.Error.WriteLine("нет вещества: " + name);
                    return 1;
                }

                double[] dr = new double[live.Range.Length];
                double[] dy = new double[live.Yield.Length];
                for (int i = 0; i < dr.Length; i++)
                {
                    dr[i] = 100.0 * Math.Abs(live.Range[i] / builtin.Range[i] - 1.0);
                    dy[i] = 100.0 * Math.Abs(live.Yield[i] / builtin.Yield[i] - 1.0);
                    if (live.Energy[i] < 0.01)
                    {
                        worstLow = Math.Max(worstLow, Math.Max(dr[i], dy[i]));
                    }
                }

                double maxR = Max(dr), maxY = Max(dy);
                if (maxR > worstRange) { worstRange = maxR; worstRangeAt = name; }
                if (maxY > worstYield) { worstYield = maxY; worstYieldAt = name; }

                double potential = 0.0;
                EstarCalculator.Compound compound = ElectronData.CompoundByName(name);
                if (compound != null)
                {
                    double[] t, coll, rad;
                    EstarCalculator.Stopping(compound, out t, out coll, out rad, out potential);
                }

                Console.WriteLine("   {0,-9}  {1,6:F1}      {2,7:F4} / {3,7:F4}          {4,7:F4} / {5,7:F4}",
                                  name, potential, Median(dr), maxR, Median(dy), maxY);
            }

            Console.WriteLine();
            Console.WriteLine("   худшее по всем: пробег {0:F4} % ({1}), выход {2:F4} % ({3})",
                              worstRange, worstRangeAt, worstYield, worstYieldAt);
            Console.WriteLine("   из них ниже 10 кэВ (16 узлов, внешнего эталона НЕТ): {0:F4} %",
                              worstLow);
            bool ok = worstRange <= 0.05 && worstYield <= 0.05;
            Console.WriteLine("   порог приёмки 0.05 % — {0}", ok ? "пройден" : "НЕ ПРОЙДЕН");

            Console.WriteLine();
            Console.WriteLine("2. Тормозная способность против эталона NIST `estar_collision_stopping`");
            Console.WriteLine();
            Console.WriteLine("   вещество   точек   ниже 10 кэВ   столкновительная, макс %   радиационная, макс %");

            bool nistOk = true;
            foreach (string pair in new string[] { "CsI:141", "NaI:252" })
            {
                string[] parts = pair.Split(':');
                EstarCalculator.Compound compound = ElectronData.CompoundByName(parts[0]);
                double[] t, coll, rad;
                double potential;
                EstarCalculator.Stopping(compound, out t, out coll, out rad, out potential);

                Dictionary<double, double[]> reference = Reference(int.Parse(parts[1]));
                double maxC = 0.0, maxR = 0.0;
                int n = 0, low = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    double[] have;
                    if (!reference.TryGetValue(t[i], out have))
                    {
                        continue;
                    }

                    n++;
                    if (t[i] < 0.01)
                    {
                        low++;
                    }

                    maxC = Math.Max(maxC, 100.0 * Math.Abs(coll[i] / have[0] - 1.0));
                    maxR = Math.Max(maxR, 100.0 * Math.Abs(rad[i] / have[1] - 1.0));
                }

                if (n == 0)
                {
                    Console.Error.WriteLine("в estar_collision_stopping нет " + pair);
                    return 1;
                }

                nistOk &= maxC <= 0.05 && maxR <= 0.05;
                Console.WriteLine("   {0,-9}  {1,5}   {2,11}   {3,22:F4}   {4,19:F4}",
                                  parts[0], n, low, maxC, maxR);
            }

            Console.WriteLine();
            Console.WriteLine("   ⚠ Эталон NIST записан с ЧЕТЫРЬМЯ значащими цифрами — порог тот же 0.05 %: {0}",
                              nistOk ? "пройден" : "НЕ ПРОЙДЕН");

            Console.WriteLine();
            Console.WriteLine("3. Цена");
            Console.WriteLine();
            Console.WriteLine("   постройка {0} таблиц из базы (один раз за прогон): {1:F0} мс",
                              names.Length, build.Elapsed.TotalMilliseconds);

            ElectronData.Material csi = ElectronData.ByName("CsI");
            double sink = 0.0;
            Stopwatch hot = Stopwatch.StartNew();
            for (int i = 0; i < calls; i++)
            {
                sink += ElectronData.RangeOf(csi, 1.0 + (i % 2600));
            }

            hot.Stop();
            Console.WriteLine("   {0} вызовов RangeOf: {1:F0} мс ({2:F1} нс на вызов), сумма {3:E3}",
                              calls, hot.Elapsed.TotalMilliseconds,
                              1.0e6 * hot.Elapsed.TotalMilliseconds / calls, sink);

            Console.WriteLine();
            return ok && nistOk ? 0 : 1;
        }

        /// <summary>
        /// Эталон NIST: столкновительная и радиационная тормозная одного из двух
        /// веществ, для которых поставка возит СВОЙ выход ESTAR.
        /// </summary>
        static Dictionary<double, double[]> Reference(int starId)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("matdb.sqlite не найдена: " + path, path);
            }

            Dictionary<double, double[]> rows = new Dictionary<double, double[]>();
            using (SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select energy_mev, collision_mev_cm2_g, radiative_mev_cm2_g, delta"
                        + " from estar_collision_stopping where material_star_id=" + starId;
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rows[reader.GetDouble(0)] = new double[]
                            {
                                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3),
                            };
                        }
                    }
                }
            }

            return rows;
        }

        static double Max(double[] values)
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

        static double Median(double[] values)
        {
            double[] copy = (double[])values.Clone();
            Array.Sort(copy);
            int n = copy.Length;
            return n % 2 == 1 ? copy[n / 2] : 0.5 * (copy[n / 2 - 1] + copy[n / 2]);
        }
    }
}
