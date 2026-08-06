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
    /// Каскадное суммирование (TODO F1) — детерминированный путь EFFTRAN,
    /// без розыгрыша схем распада. Вынос из пика (summing-out) и влёт в пик
    /// (summing-in):
    ///
    ///     CF(k) = 1 / [ (1 − L_out) + Σ_in / (p_k · ε_p(k)) ]
    ///     L_out = Σ_j P(j|k) · ε_T(E_j)
    ///     Σ_in  = Σ_{(i,j): E_i+E_j ≈ E_k} p_ij · ε_p(i) · ε_p(j) · S_ij
    ///
    /// где P(j|k) — вероятность того, что вместе с квантом линии k вылетает
    /// квант j (пары `gamma_coincidence` из SandiaDecay, 128 429 штук), ε_T —
    /// ПОЛНАЯ эффективность (вероятность оставить в кристалле хоть что-нибудь,
    /// аналоговый <see cref="EfficiencySimulator.TotalEfficiency"/>), ε_p —
    /// пиковая, p_ij = I_i·P(j|i) — абсолютная вероятность пары на распад, а
    /// S_ij — выживание сумм-события против ТРЕТЬЕГО кванта каскада:
    /// S_ij = 1 − Σ_{m∉{i,j}} P(m|i∧j)·ε_T(m). Тройная условная из парных
    /// данных невосстановима; берём P(m|i∧j) ≈ max(P(m|i), P(m|j)) — для
    /// каскада i→j квант m ниже j этим воспроизводится точно (условие на i
    /// ничего не добавляет), выше i — консервативно. Пример, ради которого
    /// всё это: Cs-134 1365.2 = 569.3+795.9, третий квант — 604.7 (у новой
    /// TCCFCALC CF там 0.807 — влёт вдвое больше собственной линии).
    ///
    /// Пары в базе хранятся НАПРАВЛЕННО, один раз: P(coinc|energy) = fraction.
    /// Обратная условная восстанавливается как P(A|B) = P(B|A)·I(A)/I(B)
    /// (database/scheme.md, §8) — здесь это сделано по обеим колонкам.
    ///
    /// Чего НЕТ, сознательно:
    ///   * тройных влётов (E_i+E_j+E_m = E_k) — ещё на два порядка мельче;
    ///   * рентгена и аннигиляционных квантов в парах — пары только γ-γ, и у
    ///     EC-нуклидов (Ba-133) CF будет занижен — это видно в сверке;
    ///   * угловых корреляций (TODO N5) — совпадения изотропны.
    ///
    /// Сверка — новая TCCFCALC (NuclideMasterPlus 2.10) и ион-режим
    /// Geant4-арбитра `tools/g4cf` (розыгрыш настоящих схем).
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

            // Условные вероятности в обе стороны: partnersOf[a][m] = P(m|a).
            // Прямая — как хранится, обратная — через отношение интенсивностей.
            var partnersOf = new Dictionary<double, Dictionary<double, double>>();
            Action<double, double, double> put = (a, m, p) =>
            {
                Dictionary<double, double> bag;
                if (!partnersOf.TryGetValue(a, out bag))
                {
                    partnersOf[a] = bag = new Dictionary<double, double>();
                }

                bag[m] = p;
            };
            foreach (var pair in pairs)
            {
                put(pair.Item1, pair.Item2, pair.Item3);
                double ia, ib;
                if (lineIntensity.TryGetValue(pair.Item1, out ia)
                    && lineIntensity.TryGetValue(pair.Item2, out ib) && ib > 0.0)
                {
                    put(pair.Item2, pair.Item1, pair.Item3 * ia / ib);
                }
            }

            // Полная и пиковая эффективность всех участвующих энергий: сами
            // линии, их партнёры по совпадениям, а для пар, суммирующихся в
            // линию (влёт), — оба члена пары и их партнёры (третий квант).
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

                if (lines.Any(l => SameWindow(pair.Item1 + pair.Item2, l.Item1)))
                {
                    energies.Add(pair.Item1);
                    energies.Add(pair.Item2);
                    foreach (var third in Partners(partnersOf, pair.Item1))
                    {
                        energies.Add(third.Key);
                    }

                    foreach (var third in Partners(partnersOf, pair.Item2))
                    {
                        energies.Add(third.Key);
                    }
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
            Console.WriteLine("    {0,8} {1,8} {2,10} {3,10} {4,8}",
                              "E, кэВ", "I, %", "потеря L", "влёт/пик", "CF");
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

                // Влёт: каскадные пары, суммирующиеся в окно линии k.
                double sumIn = 0.0;
                foreach (var pair in pairs)
                {
                    if (!SameWindow(pair.Item1 + pair.Item2, k))
                    {
                        continue;
                    }

                    double ia;
                    if (!lineIntensity.TryGetValue(pair.Item1, out ia))
                    {
                        continue;
                    }

                    double pij = ia / 100.0 * pair.Item3;
                    double survive = 1.0;
                    foreach (var third in MergedThird(partnersOf, pair.Item1, pair.Item2))
                    {
                        survive -= third.Value * Total(totalEff, third.Key);
                    }

                    double term = pij * Total(peakEff, pair.Item1) * Total(peakEff, pair.Item2)
                                  * Math.Max(survive, 0.0);
                    sumIn += term;
                    Console.WriteLine("    // влёт в {0:F1}: {1:F1}+{2:F1}, p_ij {3:E2}, S {4:F3}, вклад {5:E2}",
                                      k, pair.Item1, pair.Item2, pij, survive, term);
                }

                double direct = line.Item2 / 100.0 * Total(peakEff, k);
                double inShare = direct > 0.0 ? sumIn / direct : 0.0;
                double denom = (1.0 - loss) + inShare;
                double cf = denom > 0.0 ? 1.0 / denom : double.PositiveInfinity;
                Console.WriteLine("    {0,8:F1} {1,8:F3} {2,10:F4} {3,10:F4} {4,8:F4}",
                                  k, line.Item2, loss, inShare, cf);
            }

            return 0;
        }

        /// <summary>
        /// Слияние партнёров пары (i, j) для третьего кванта: P(m|i∧j) ≈
        /// max(P(m|i), P(m|j)), сами i и j исключены.
        /// </summary>
        static Dictionary<double, double> MergedThird(
            Dictionary<double, Dictionary<double, double>> partnersOf, double i, double j)
        {
            var merged = new Dictionary<double, double>();
            foreach (var side in new[] { Partners(partnersOf, i), Partners(partnersOf, j) })
            {
                foreach (var entry in side)
                {
                    if (Same(entry.Key, i) || Same(entry.Key, j))
                    {
                        continue;
                    }

                    double have;
                    if (!merged.TryGetValue(entry.Key, out have) || entry.Value > have)
                    {
                        merged[entry.Key] = entry.Value;
                    }
                }
            }

            return merged;
        }

        static Dictionary<double, double> Partners(
            Dictionary<double, Dictionary<double, double>> partnersOf, double e)
        {
            Dictionary<double, double> bag;
            return partnersOf.TryGetValue(e, out bag) ? bag : new Dictionary<double, double>();
        }

        /// <summary>
        /// Сумма пары попадает в окно линии: полуширина окна как у g4cf
        /// (±0.5 кэВ); в реальном спектре всё это давно слито разрешением.
        /// </summary>
        static bool SameWindow(double sum, double lineEnergy)
        {
            return Math.Abs(sum - lineEnergy) < 0.5;
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
