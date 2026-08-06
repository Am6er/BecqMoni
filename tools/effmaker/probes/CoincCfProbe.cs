using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace EffMakerProbes
{
    /// <summary>
    /// Первая версия каскадного суммирования (TODO F1) — детерминированный
    /// путь EFFTRAN, без розыгрыша схем распада:
    ///
    ///     CF(k) = 1 / (1 − Σ_j P(j|k) · ε_T(E_j))
    ///
    /// где P(j|k) — вероятность того, что вместе с квантом линии k вылетает
    /// квант j (пары `gamma_coincidence` из SandiaDecay, 128 429 штук), а
    /// ε_T — ПОЛНАЯ эффективность: вероятность кванту j оставить в кристалле
    /// хоть что-нибудь. Она считается нашим же переносом: сумма гистограммы
    /// <see cref="EfficiencySimulator.Response"/> по всем бинам — это в
    /// точности «что-то поглотилось» (нулевые вклады туда не кладутся).
    ///
    /// Пары в базе хранятся НАПРАВЛЕННО, один раз: P(coinc|energy) = fraction.
    /// Обратная условная восстанавливается как P(A|B) = P(B|A)·I(A)/I(B)
    /// (database/scheme.md, §8) — здесь это сделано по обеим колонкам.
    ///
    /// Чего в первой версии НЕТ, сознательно:
    ///   * сумм-эффекта «в пик» (summing-in) — для главных линий он на
    ///     порядки меньше выноса;
    ///   * рентгена и аннигиляционных квантов в парах — пары только γ-γ, и у
    ///     EC-нуклидов (Ba-133) CF будет занижен — это видно в сверке;
    ///   * угловых корреляций (TODO N5) — совпадения изотропны.
    ///
    /// Сверка — новая TCCFCALC (NuclideMasterPlus 2.10): она считает CF
    /// розыгрышем настоящих схем, это её родная величина.
    ///
    ///   CoincCfProbe --geometry=X.in --nucid=60CO [--n=200000] [--min-i=1]
    /// </summary>
    static class CoincCfProbe
    {
        static int Main(string[] args)
        {
            string geometryPath = null, nucid = null;
            int histories = 200000;
            double minIntensity = 1.0;
            foreach (string arg in args)
            {
                int eq = arg.IndexOf('=');
                string key = eq > 0 ? arg.Substring(0, eq) : arg;
                string value = eq > 0 ? arg.Substring(eq + 1) : "";
                switch (key)
                {
                    case "--geometry": geometryPath = value; break;
                    case "--nucid": nucid = value; break;
                    case "--n": histories = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--min-i": minIntensity = double.Parse(value, CultureInfo.InvariantCulture); break;
                    default: Console.Error.WriteLine("неизвестный ключ: " + arg); return 2;
                }
            }

            if (geometryPath == null || nucid == null)
            {
                Console.Error.WriteLine("нужно: --geometry=X.in --nucid=60CO");
                return 2;
            }

            // Линии и пары нуклида — из тех же представлений, которыми
            // документирована укладка (кэВ и доли уже возвращены на место).
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            var lines = new List<Tuple<double, double>>();          // E, I%
            var pairs = new List<Tuple<double, double, double>>();  // E, Ecoinc, P
            var lineIntensity = new Dictionary<double, double>();
            using (var connection = new SqliteConnection("Data Source=" + dbPath + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select energy_kev, intensity_pct from v_gamma_coincidence_line" +
                        " where nucid = $n and isomer = 0";
                    command.Parameters.AddWithValue("$n", nucid);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            double e = reader.GetDouble(0), i = reader.GetDouble(1);
                            lineIntensity[e] = i;
                            if (i >= minIntensity)
                            {
                                lines.Add(Tuple.Create(e, i));
                            }
                        }
                    }

                    command.CommandText =
                        "select energy_kev, coinc_energy_kev, fraction from v_gamma_coincidence" +
                        " where nucid = $n and isomer = 0";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            pairs.Add(Tuple.Create(reader.GetDouble(0), reader.GetDouble(1),
                                                   reader.GetDouble(2)));
                        }
                    }
                }
            }

            if (lines.Count == 0)
            {
                Console.Error.WriteLine("у " + nucid + " нет линий в gamma_coincidence_line");
                return 1;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            Console.WriteLine("=== CF (EFFTRAN-путь) для {0}, {1}", nucid,
                              Path.GetFileNameWithoutExtension(geometryPath));
            Console.WriteLine("    {0}", geometry.Describe());
            Console.WriteLine("    историй на энергию: {0}; линии с I >= {1} %", histories, minIntensity);

            // Полная и пиковая эффективность всех участвующих энергий: сами
            // линии плюс их партнёры по совпадениям.
            var energies = new SortedSet<double>();
            foreach (var line in lines)
            {
                energies.Add(line.Item1);
            }

            foreach (var pair in pairs)
            {
                // партнёры нужны только для линий, у которых считаем CF
                if (lines.Any(l => Same(l.Item1, pair.Item1)))
                {
                    energies.Add(pair.Item2);
                }

                if (lines.Any(l => Same(l.Item1, pair.Item2)))
                {
                    energies.Add(pair.Item1);
                }
            }

            var totalEff = new Dictionary<double, double>();
            var peakEff = new Dictionary<double, double>();
            var simulator = new EfficiencySimulator(geometry) { Histories = histories };
            int index = 0;
            foreach (double e in energies)
            {
                // Полная эффективность — АНАЛОГОВЫМ оценщиком: взвешенная
                // проводка пиковой ветки занижала её на 12-15 % на упоре
                // (нет многократного рассеяния), и весь недобор CF был отсюда.
                simulator.ResetStream((ulong)simulator.Seed
                                      ^ ((ulong)(++index) * 0x9E3779B97F4A7C15UL));
                double totalError;
                double total = simulator.TotalEfficiency(e, out totalError);
                simulator.ResetStream((ulong)simulator.Seed
                                      ^ ((ulong)(index + 1000) * 0x9E3779B97F4A7C15UL));
                double peakError;
                double peak = simulator.Efficiency(e, out peakError);
                totalEff[e] = total;
                peakEff[e] = peak;
                Console.WriteLine("    eps({0,7:F1}) полная {1:E3} ±{2:F1} %  пик {3:E3} ±{4:F1} %",
                                  e, total, totalError, peak, peakError);
            }

            Console.WriteLine();
            Console.WriteLine("    {0,8} {1,8} {2,10} {3,8}", "E, кэВ", "I, %", "потеря L", "CF");
            foreach (var line in lines.OrderBy(l => l.Item1))
            {
                double k = line.Item1;
                double loss = 0.0;
                foreach (var pair in pairs)
                {
                    if (Same(pair.Item1, k))
                    {
                        loss += pair.Item3 * Total(totalEff, pair.Item2);
                    }
                    else if (Same(pair.Item2, k))
                    {
                        // обратная условная: P(A|B) = P(B|A)·I(A)/I(B)
                        double ia, ib;
                        if (lineIntensity.TryGetValue(pair.Item1, out ia)
                            && lineIntensity.TryGetValue(pair.Item2, out ib) && ib > 0.0)
                        {
                            loss += pair.Item3 * ia / ib * Total(totalEff, pair.Item1);
                        }
                    }
                }

                double cf = loss < 1.0 ? 1.0 / (1.0 - loss) : double.PositiveInfinity;
                Console.WriteLine("    {0,8:F1} {1,8:F3} {2,10:F4} {3,8:F4}", k, line.Item2, loss, cf);
            }

            return 0;
        }

        /// <summary>Энергии одной линии в базе и в парах совпадают до 0.001 кэВ.</summary>
        static bool Same(double a, double b)
        {
            return Math.Abs(a - b) < 0.05;
        }

        static double Total(Dictionary<double, double> table, double e)
        {
            double value;
            return table.TryGetValue(e, out value) ? value : 0.0;
        }
    }
}
