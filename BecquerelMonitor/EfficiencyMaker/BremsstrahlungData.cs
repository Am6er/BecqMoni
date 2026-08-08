using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Дифференциальные сечения тормозного излучения Зельцера — Бергера из
    /// `nucdb.sqlite` (таблицы `seltzer_berger`, `seltzer_berger_grid`,
    /// `database/scheme.md` §5б).
    ///
    /// Хранится безразмерная χ(Z, T, κ) = (β²/Z²)·k·dσ/dk в миллибарнах, где
    /// T — кинетическая энергия электрона, k — энергия кванта, κ = k/T.
    /// Отсюда сечение: dσ/dk = χ·Z²/(β²·k).
    /// </summary>
    public static class SeltzerBergerData
    {
        /// <summary>Таблица одного элемента: сетки общие, значения свои.</summary>
        public sealed class Element
        {
            public int Z;
            internal double[][] chi;      // [e_idx][kappa_idx], мб

            /// <summary>
            /// χ(T, κ), интерполяция логарифмическая по энергии и линейная по
            /// κ — как в `G4SeltzerBergerModel`. За краями сетки берутся
            /// крайние значения: снизу это 1 кэВ (ниже электрон уже не
            /// излучает наружу), сверху 10 ГэВ.
            /// </summary>
            public double Chi(double teKev, double kappa)
            {
                double[] grid = energyKev;
                int n = grid.Length;
                int lo;
                double f;
                if (teKev <= grid[0])
                {
                    lo = 0;
                    f = 0.0;
                }
                else if (teKev >= grid[n - 1])
                {
                    lo = n - 2;
                    f = 1.0;
                }
                else
                {
                    lo = 0;
                    int hi = n - 1;
                    while (hi - lo > 1)
                    {
                        int mid = (lo + hi) / 2;
                        if (grid[mid] <= teKev)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }

                    f = (Math.Log(teKev) - Math.Log(grid[lo]))
                        / (Math.Log(grid[lo + 1]) - Math.Log(grid[lo]));
                }

                double a = AtKappa(this.chi[lo], kappa);
                double b = AtKappa(this.chi[lo + 1], kappa);
                return a + f * (b - a);
            }

            static double AtKappa(double[] row, double kappa)
            {
                double[] k = kappaGrid;
                int n = k.Length;
                if (kappa <= k[0])
                {
                    return row[0];
                }

                if (kappa >= k[n - 1])
                {
                    return row[n - 1];
                }

                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (k[mid] <= kappa)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                double f = (kappa - k[lo]) / (k[hi] - k[lo]);
                return row[lo] + f * (row[hi] - row[lo]);
            }
        }

        static double[] energyKev;
        static double[] kappaGrid;
        static readonly object Gate = new object();
        static readonly Dictionary<int, Element> cache = new Dictionary<int, Element>();

        /// <summary>Таблица элемента; null — этого Z в поставке нет (взяты 1…92).</summary>
        public static Element Of(int z)
        {
            lock (Gate)
            {
                Element found;
                if (cache.TryGetValue(z, out found))
                {
                    return found;
                }

                Element loaded = Load(z);
                cache[z] = loaded;
                return loaded;
            }
        }

        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static Element Load(int z)
        {
            string path = DatabasePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "nucdb.sqlite не найдена рядом с программой: " + path, path);
            }

            using (SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
            {
                connection.Open();
                LoadGrids(connection);
                if (energyKev == null || kappaGrid == null)
                {
                    return null;
                }

                double[][] chi = new double[energyKev.Length][];
                for (int i = 0; i < chi.Length; i++)
                {
                    chi[i] = new double[kappaGrid.Length];
                }

                bool any = false;
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select e_idx, kappa_idx, chi_mb from seltzer_berger where z=" + z;
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int e = reader.GetInt32(0);
                            int k = reader.GetInt32(1);
                            if (e >= 0 && e < chi.Length && k >= 0 && k < kappaGrid.Length)
                            {
                                chi[e][k] = reader.GetDouble(2);
                                any = true;
                            }
                        }
                    }
                }

                return any ? new Element { Z = z, chi = chi } : null;
            }
        }

        static void LoadGrids(SqliteConnection connection)
        {
            if (energyKev != null && kappaGrid != null)
            {
                return;
            }

            List<double> e = new List<double>();
            List<double> k = new List<double>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select kind, value from seltzer_berger_grid order by kind, idx";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(0) == "energy")
                        {
                            e.Add(reader.GetDouble(1) / 1000.0);      // эВ → кэВ
                        }
                        else
                        {
                            k.Add(reader.GetDouble(1));
                        }
                    }
                }
            }

            if (e.Count > 1 && k.Count > 1)
            {
                energyKev = e.ToArray();
                kappaGrid = k.ToArray();
            }
        }
    }

    /// <summary>
    /// Спектр тормозного излучения ТОЛСТОЙ МИШЕНИ для конкретного вещества:
    /// сколько квантов какой энергии рождает электрон, тормозящийся в этом
    /// веществе от начальной энергии T до нуля.
    ///
    /// ЗАЧЕМ. До сих пор спектр брался приближением Крамерса dN/dk = C/k с
    /// нормировкой на радиационный выход ESTAR (TODO M3). Приближение
    /// разумное — χ(κ) и правда почти плоская, — но оно ни разу не проверялось
    /// данными, а форма спектра решает, вылетит квант или сядет на месте.
    /// У ЛСРМ на этом месте готовые таблицы толстой мишени на девять веществ
    /// (`Lib\Ttb`), и иодистого цезия среди них нет; здесь спектр СЧИТАЕТСЯ из
    /// сечений для ЛЮБОГО состава.
    ///
    /// КАК СЧИТАЕТСЯ. Электрон рождает квант энергии k на каждом участке пути,
    /// пока его энергия T' выше k:
    ///
    ///     dN/dk = Σᵢ wᵢ·(N_A/Aᵢ) ∫ from k to T  (dσᵢ/dk)(T', k) · dR/dT' · dT'
    ///
    /// где wᵢ — массовая доля элемента, dR/dT' = 1/S(T') — обратная тормозная
    /// способность, то есть пробег ESTAR, продифференцированный по энергии.
    /// Сечение — Зельцера — Бергера: dσ/dk = χ(Z,T',k/T')·Z²/(β'²·k).
    ///
    /// ЧТО ЭТО ПРИБЛИЖЕНИЕ. Пробег ESTAR — это CSDA, то есть путь без учёта
    /// того, что электрон уже вылетел; для кванта, рождённого в глубине
    /// кристалла, это верно, у границы — завышает. Точка рождения кванта
    /// по-прежнему совпадает с точкой рождения электрона (остаток M3). Ниже
    /// 10 кэВ таблица пробега ESTAR кончается, и интеграл там обрезан: на
    /// энергию квантов выше 5 кэВ это не влияет.
    /// </summary>
    public sealed class ThickTargetBrem
    {
        /// <summary>Ниже этой энергии кванты не разыгрываются: не выйдут ниоткуда.</summary>
        public double MinKev { get; private set; }

        double[] node;            // сетка, кэВ: и по T, и по k — одна и та же
        double[][] cumulative;    // [T][k]: доля квантов ВЫШЕ node[k], от 1 до 0
        double[] photons;         // среднее число квантов выше MinKev
        double[] radiatedKev;     // средняя энергия этих квантов
        double[] anchorFactor;    // во сколько раз уровень подтянут к ESTAR

        static readonly object Gate = new object();
        static readonly Dictionary<string, ThickTargetBrem> cache =
            new Dictionary<string, ThickTargetBrem>();

        /// <summary>
        /// Таблица для вещества; null — сечений нет ни у одного элемента или
        /// нет пробега. Кэш общий по имени вещества и веществу электрона:
        /// таблица строится долго (двойной интеграл), а веществ в сцене мало.
        /// </summary>
        public static ThickTargetBrem For(GeometryMaterial material,
                                          ElectronData.Material electron,
                                          double minKev)
        {
            if (material == null || electron == null || !(minKev > 0.0))
            {
                return null;
            }

            // Ключ — по СОСТАВУ, а не по имени: имена веществ в библиотеке
            // повторяются, а таблица зависит от Z и долей. Совпадение имён при
            // разном составе дало бы чужой спектр молча.
            var key = new System.Text.StringBuilder();
            key.Append(electron.Name).Append('|')
               .Append(minKev.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            List<int> ordered = new List<int>(material.Fractions.Keys);
            ordered.Sort();
            foreach (int z in ordered)
            {
                key.Append('|').Append(z).Append(':')
                   .Append(material.Fractions[z].ToString(
                       "R", System.Globalization.CultureInfo.InvariantCulture));
            }
            string cacheKey = key.ToString();
            lock (Gate)
            {
                ThickTargetBrem found;
                if (cache.TryGetValue(cacheKey, out found))
                {
                    return found;
                }

                ThickTargetBrem built = Build(material, electron, minKev);
                cache[cacheKey] = built;
                return built;
            }
        }

        /// <summary>Среднее число квантов выше <see cref="MinKev"/> у электрона T.</summary>
        public double Photons(double teKev)
        {
            return Interpolate(this.photons, teKev);
        }

        /// <summary>Средняя энергия этих квантов, кэВ — для сверки с выходом ESTAR.</summary>
        public double Radiated(double teKev)
        {
            return Interpolate(this.radiatedKev, teKev);
        }

        /// <summary>
        /// Во сколько раз уровень спектра подтянут к радиационному выходу
        /// ESTAR (см. <see cref="Build"/>). Единица — интеграл сечений сошёлся
        /// с выходом сам; отличие от единицы — размер невязки, которую
        /// подтяжка закрывает. Читается пробой `BremSpectrumProbe`.
        /// </summary>
        public double Anchor(double teKev)
        {
            double v = Interpolate(this.anchorFactor, teKev);
            return v > 0.0 ? v : 1.0;
        }

        /// <summary>
        /// Энергия одного кванта по равномерному числу. Форма берётся с
        /// ближайшего снизу узла сетки T (шаг сетки 7 % по энергии, а спектр
        /// по T меняется гладко); число квантов и излучённая энергия при этом
        /// интерполируются, потому что от них зависит баланс.
        /// </summary>
        public double SampleKev(double teKev, double u)
        {
            int j = IndexBelow(teKev);
            double[] cum = this.cumulative[j];
            // cum убывает от 1 (на MinKev) до 0 (на node[j]) — ищем, где u
            int lo = 0, hi = j;
            if (hi <= lo)
            {
                return this.node[0];
            }

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cum[mid] >= u)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            double c0 = cum[lo], c1 = cum[hi];
            double f = c0 > c1 ? (c0 - u) / (c0 - c1) : 0.0;
            double e0 = Math.Log(this.node[lo]), e1 = Math.Log(this.node[hi]);
            return Math.Exp(e0 + f * (e1 - e0));
        }

        // ------------------------------------------------------------------
        // Построение
        // ------------------------------------------------------------------

        const double ElectronMassKev = 510.99895;
        const double Avogadro = 6.02214076e23;
        const double MilliBarnCm2 = 1e-27;

        static ThickTargetBrem Build(GeometryMaterial material,
                                     ElectronData.Material electron,
                                     double minKev)
        {
            List<int> zs = new List<int>();
            List<double> weights = new List<double>();
            List<SeltzerBergerData.Element> tables = new List<SeltzerBergerData.Element>();
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                double mass;
                if (!(pair.Value > 0.0)
                    || !MaterialDatabase.AtomicMass.TryGetValue(pair.Key, out mass)
                    || !(mass > 0.0))
                {
                    continue;
                }

                SeltzerBergerData.Element table = SeltzerBergerData.Of(pair.Key);
                if (table == null)
                {
                    continue;
                }

                zs.Add(pair.Key);
                // атомов на грамм вещества, с массовой долей элемента
                weights.Add(pair.Value * Avogadro / mass);
                tables.Add(table);
            }

            if (tables.Count == 0)
            {
                return null;
            }

            double topKev = electron.Energy[electron.Energy.Length - 1] * 1000.0;
            if (!(topKev > minKev * 2.0))
            {
                return null;
            }

            const int Nodes = 96;
            double[] node = new double[Nodes];
            double logLo = Math.Log(minKev), logHi = Math.Log(topKev);
            for (int i = 0; i < Nodes; i++)
            {
                node[i] = Math.Exp(logLo + (logHi - logLo) * i / (Nodes - 1));
            }

            // Дифференциальный спектр dN/dk в узлах: [T][k], нули при k >= T.
            double[][] diff = new double[Nodes][];
            for (int j = 0; j < Nodes; j++)
            {
                diff[j] = new double[Nodes];
                for (int i = 0; i < j; i++)
                {
                    diff[j][i] = Differential(zs, weights, tables, electron,
                                              node[i], node[j]);
                }
            }

            double[][] cumulative = new double[Nodes][];
            double[] photons = new double[Nodes];
            double[] radiated = new double[Nodes];
            for (int j = 0; j < Nodes; j++)
            {
                double[] cum = new double[Nodes];
                cumulative[j] = cum;
                if (j < 2)
                {
                    continue;
                }

                // интегрируем dN/dk от узла к узлу, трапеция по k
                double total = 0.0, energy = 0.0;
                double[] above = new double[Nodes];
                for (int i = j - 1; i >= 0; i--)
                {
                    double dk = node[i + 1] - node[i];
                    double d0 = diff[j][i];
                    double d1 = i + 1 < j ? diff[j][i + 1] : 0.0;
                    total += 0.5 * (d0 + d1) * dk;
                    energy += 0.5 * (d0 * node[i] + d1 * node[i + 1]) * dk;
                    above[i] = total;
                }

                photons[j] = total;
                radiated[j] = energy;
                if (total > 0.0)
                {
                    for (int i = 0; i < j; i++)
                    {
                        cum[i] = above[i] / total;
                    }
                }
            }

            // УРОВЕНЬ подтягивается к радиационному выходу ESTAR, форма
            // остаётся от Зельцера — Бергера. Причина: интеграл сечений по
            // пути торможения обязан дать ровно Y(T)·T (это одно и то же
            // число, посчитанное с двух концов), а сходится он на 0.92 (100
            // кэВ) … 0.99 (2614 кэВ) — виноваты обрез пути ниже 10 кэВ (там
            // кончается таблица пробега) и оценка энергии квантов ниже
            // отсечки. Правка, которая здесь делалась, — про ФОРМУ спектра;
            // подменять заодно и его уровень, да ещё на менее надёжный,
            // значило бы смешать две правки в одну и потерять обе.
            // Величина подтяжки хранится и печатается пробой — это размер
            // невязки, а не спрятанный коэффициент (TODO M7).
            double[] anchor = new double[Nodes];
            for (int j = 0; j < Nodes; j++)
            {
                anchor[j] = 1.0;
                if (!(radiated[j] > 0.0))
                {
                    continue;
                }

                double yieldKev = ElectronData.YieldOf(electron, node[j]) * node[j];
                // энергия квантов НИЖЕ отсечки: при k·dN/dk ≈ const это
                // доля minKev/(T − minKev) от посчитанной выше отсечки
                double below = radiated[j] * minKev / Math.Max(1.0, node[j] - minKev);
                double whole = radiated[j] + below;
                if (!(whole > 0.0) || !(yieldKev > 0.0))
                {
                    continue;
                }

                anchor[j] = yieldKev / whole;
                photons[j] *= anchor[j];
                radiated[j] *= anchor[j];
            }

            return new ThickTargetBrem
            {
                MinKev = minKev,
                node = node,
                cumulative = cumulative,
                photons = photons,
                radiatedKev = radiated,
                anchorFactor = anchor
            };
        }

        /// <summary>
        /// dN/dk на единицу энергии кванта: интеграл по пути торможения от k
        /// до T. Сетка интегрирования логарифмическая — сечение и обратная
        /// тормозная способность меняются по энергии степенным образом.
        /// </summary>
        static double Differential(List<int> zs, List<double> weights,
                                   List<SeltzerBergerData.Element> tables,
                                   ElectronData.Material electron,
                                   double kKev, double teKev)
        {
            const int Steps = 24;
            double lo = Math.Max(kKev, 10.0);        // ниже 10 кэВ пробега ESTAR нет
            if (!(teKev > lo))
            {
                return 0.0;
            }

            double logLo = Math.Log(lo), logHi = Math.Log(teKev);
            double sum = 0.0;
            for (int s = 0; s < Steps; s++)
            {
                // середина логарифмического шага
                double f = (s + 0.5) / Steps;
                double t = Math.Exp(logLo + (logHi - logLo) * f);
                double width = t * (logHi - logLo) / Steps;      // dT'

                double gamma = 1.0 + t / ElectronMassKev;
                double beta2 = 1.0 - 1.0 / (gamma * gamma);
                if (!(beta2 > 0.0))
                {
                    continue;
                }

                double inverseStopping = InverseStopping(electron, t);
                if (!(inverseStopping > 0.0))
                {
                    continue;
                }

                double kappa = kKev / t;
                double perGram = 0.0;
                for (int i = 0; i < tables.Count; i++)
                {
                    double z = zs[i];
                    double chi = tables[i].Chi(t, kappa);
                    perGram += weights[i] * chi * MilliBarnCm2 * z * z / (beta2 * kKev);
                }

                sum += perGram * inverseStopping * width;
            }

            return sum;
        }

        /// <summary>
        /// dR/dT — обратная тормозная способность, г/(см²·кэВ), из пробега
        /// CSDA ESTAR численным дифференцированием по логарифмической
        /// полуразности.
        /// </summary>
        static double InverseStopping(ElectronData.Material electron, double teKev)
        {
            double h = 0.02;
            double up = ElectronData.RangeOf(electron, teKev * Math.Exp(h));
            double down = ElectronData.RangeOf(electron, teKev * Math.Exp(-h));
            double dt = teKev * (Math.Exp(h) - Math.Exp(-h));
            return dt > 0.0 ? (up - down) / dt : 0.0;
        }

        int IndexBelow(double teKev)
        {
            double[] g = this.node;
            int n = g.Length;
            if (teKev <= g[1])
            {
                return 1;
            }

            if (teKev >= g[n - 1])
            {
                return n - 1;
            }

            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (g[mid] <= teKev)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        double Interpolate(double[] values, double teKev)
        {
            double[] g = this.node;
            int n = g.Length;
            if (teKev <= g[0])
            {
                return 0.0;
            }

            if (teKev >= g[n - 1])
            {
                return values[n - 1];
            }

            int lo = IndexBelow(teKev);
            int hi = Math.Min(n - 1, lo + 1);
            if (hi == lo)
            {
                return values[lo];
            }

            double f = (Math.Log(teKev) - Math.Log(g[lo])) / (Math.Log(g[hi]) - Math.Log(g[lo]));
            return values[lo] + f * (values[hi] - values[lo]);
        }
    }
}
