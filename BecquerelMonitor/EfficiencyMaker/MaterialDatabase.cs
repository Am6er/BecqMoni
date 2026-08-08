using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Данные о ВЕЩЕСТВЕ из `nucdb.sqlite`: атомные веса, сечения
    /// взаимодействия фотона по каналам, символы элементов.
    ///
    /// Раньше всё это лежало таблицами прямо в исходнике — 92 элемента полного
    /// ослабления и ДЕВЯТЬ элементов парциальных сечений, снятых руками через
    /// веб-форму NIST. Девяти не хватало: `EfficiencySimulator` проверяет, все
    /// ли элементы кристалла имеют парциальные сечения, и без них откатывается
    /// на грубое «фотоэффект = всё, что не комптон», которое завышает канал
    /// поглощения в полтора раза. На этом откате сидели CeBr3, CdTe, CZT и GSO.
    ///
    /// В базе лежит полная поставка NIST XCOM 3.1: сто элементов, пять каналов,
    /// 1 кэВ … 100 ГэВ. Сверено с прежними таблицами перед переносом: парциальные
    /// сечения 1026 значений, худшее расхождение 0.069 %, полное ослабление 840
    /// значений, худшее 0.098 % — то есть округление до четырёх знаков, с
    /// которым числа и вписывали в исходник.
    ///
    /// Запасного пути нет нарочно. `nucdb.sqlite` идёт в поставке и лежит в
    /// репозитории; если её нет, считать не по чему, и молчаливый откат на
    /// вшитую копию означал бы расчёт по данным, о происхождении которых никто
    /// уже не скажет.
    /// </summary>
    public static class MaterialDatabase
    {
        /// <summary>Один элемент: сетка энергий и сечения по каналам.</summary>
        public sealed class Element
        {
            /// <summary>Энергии, кэВ, строго по возрастанию.</summary>
            public double[] EnergyKev;

            /// <summary>Атомный вес, г/моль.</summary>
            public double AtomicWeight;

            /// <summary>Каналы, см2/г: 0 когерентное, 1 некогерентное, 2 фотоэффект, 3 пары ядро, 4 пары электрон.</summary>
            public double[][] Channels;

            /// <summary>Сумма каналов, см2/г, — полное ослабление.</summary>
            public double[] Total;
        }

        /// <summary>
        /// Чем атом отвечает на дырку в K-оболочке. Нужно для вылета
        /// характеристического рентгена: выше K-края квант выбивает электрон
        /// оттуда, атом излучает Kα или Kβ, и этот квант может уйти из
        /// кристалла — событие покидает пик полного поглощения.
        ///
        /// Есть не у всех: у лёгких элементов K-край лежит ниже сетки XCOM
        /// (1 кэВ), да и рентген в килоэлектронвольт поглощается на месте.
        /// </summary>
        public sealed class Fluorescence
        {
            /// <summary>Энергия K-края, кэВ. Ниже неё K-оболочка недоступна.</summary>
            public double KEdgeKev;

            /// <summary>Доля фотопоглощений, приходящаяся на K-оболочку.</summary>
            public double KFraction;

            /// <summary>Вероятность ответить квантом, а не оже-электроном.</summary>
            public double OmegaK;

            /// <summary>Энергии линий, кэВ: Kα1, Kα2, Kβ.</summary>
            public double[] LineKev;

            /// <summary>Веса линий, в сумме единица.</summary>
            public double[] LineWeight;
        }

        /// <summary>
        /// Пооболочечный фотоэффект EPICS2017 (таблицы `epics_photo_*`,
        /// втянуты из Geant4 G4EMLOW — `database/scheme.md`, §5б). Нужен,
        /// чтобы доля K-оболочки зависела от энергии, а не бралась константой
        /// со скачка на крае: у иода она растёт с 0.834 на краю до 0.858 к
        /// 90 кэВ, и константа занижала вылет рентгена тем сильнее, чем выше
        /// энергия кванта.
        ///
        /// Устройство то же, что в G4LivermorePhotoElectricModel: от K-края до
        /// <see cref="lowFromKev"/> — табличные векторы по оболочкам, выше —
        /// шестипараметрические фиты σ(E) = Σ aᵢ/Eⁱ (E в МэВ, σ в барнах),
        /// строки которых КУМУЛЯТИВНЫ: строка 0 — K, последняя — полное
        /// сечение фотоэффекта.
        /// </summary>
        public sealed class PhotoShellModel
        {
            internal double kEdgeKev;
            internal double lowFromKev, highFromKev;
            internal double[] lowK, lowTotal, highK, highTotal;   // a1..a6
            internal double[][] tableE;    // [оболочка][узлы], кэВ
            internal double[][] tableCs;   // барн

            /// <summary>
            /// Доля фотопоглощений на K-оболочке при энергии кванта
            /// <paramref name="energyKev"/>. Ниже K-края — ноль.
            /// </summary>
            public double KFraction(double energyKev)
            {
                if (!(energyKev > this.kEdgeKev))
                {
                    return 0.0;
                }

                if (energyKev >= this.lowFromKev)
                {
                    bool high = energyKev >= this.highFromKev;
                    double k = EvalFit(high ? this.highK : this.lowK, energyKev);
                    double total = EvalFit(high ? this.highTotal : this.lowTotal, energyKev);
                    if (!(total > 0.0) || !(k > 0.0))
                    {
                        return 0.0;
                    }

                    return k >= total ? 1.0 : k / total;
                }

                // Зазор между K-краем и началом фитов (у иода его нет, у свинца
                // это 88..187 кэВ): табличные векторы по оболочкам, доля — как
                // отношение оболочки K к сумме всех доступных.
                double num = 0.0, den = 0.0;
                for (int s = 0; s < this.tableE.Length; s++)
                {
                    double v = InterpTable(this.tableE[s], this.tableCs[s], energyKev);
                    den += v;
                    if (s == 0)
                    {
                        num = v;
                    }
                }

                return den > 0.0 ? Math.Min(1.0, num / den) : 0.0;
            }

            /// <summary>σ(E) = Σ aᵢ/Eⁱ; E в кэВ снаружи, в МэВ внутри.</summary>
            static double EvalFit(double[] a, double energyKev)
            {
                double x = 1000.0 / energyKev;      // 1/E, МэВ⁻¹
                double sum = 0.0, p = x;
                for (int i = 0; i < a.Length; i++)
                {
                    sum += a[i] * p;
                    p *= x;
                }

                return sum;
            }

            /// <summary>
            /// Лог-лог внутри домена таблицы, за краями — ноль слева (оболочка
            /// ещё закрыта) и крайнее значение справа.
            /// </summary>
            static double InterpTable(double[] grid, double[] values, double x)
            {
                int n = grid.Length;
                if (n == 0 || x < grid[0])
                {
                    return 0.0;
                }

                if (x >= grid[n - 1])
                {
                    return values[n - 1];
                }

                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (grid[mid] <= x)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                if (!(grid[hi] > grid[lo]))
                {
                    return values[hi];
                }

                double f = (Math.Log(x) - Math.Log(grid[lo]))
                           / (Math.Log(grid[hi]) - Math.Log(grid[lo]));
                if (!(values[lo] > 0.0) || !(values[hi] > 0.0))
                {
                    return values[lo] + f * (values[hi] - values[lo]);
                }

                return Math.Exp(Math.Log(values[lo]) + f * (Math.Log(values[hi]) - Math.Log(values[lo])));
            }
        }

        /// <summary>
        /// Непропорциональность светового выхода сцинтиллятора: относительный
        /// выход L(E)/E для электрона начальной энергии E, единица на 662 кэВ.
        /// Кривые посчитаны из механистической модели Пейна и лежат в таблице
        /// `scint_electron_light_yield` (tools/nucdb/import_light_yield.py —
        /// там же источники параметров). Это шкала СВЕТА, а не потеря событий:
        /// прибор меряет свет, и события с разным составом электронов дают
        /// разный свет при одной поглощённой энергии (TODO F11).
        /// </summary>
        public sealed class LightYieldCurve
        {
            /// <summary>Имя материала в базе, например «CsI:Tl».</summary>
            public string Material;

            internal double[] energyKev;   // строго по возрастанию
            internal double[] yieldRel;

            /// <summary>
            /// Относительный выход для электрона начальной энергии
            /// <paramref name="electronKev"/>. Линейная интерполяция по log E;
            /// за краями сетки — крайние значения (ниже 1 кэВ перенос
            /// электроны всё равно не различает).
            /// </summary>
            public double Of(double electronKev)
            {
                double[] e = this.energyKev;
                int n = e.Length;
                if (!(electronKev > e[0]))
                {
                    return this.yieldRel[0];
                }

                if (electronKev >= e[n - 1])
                {
                    return this.yieldRel[n - 1];
                }

                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (e[mid] <= electronKev)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                double f = (Math.Log(electronKev) - Math.Log(e[lo]))
                           / (Math.Log(e[hi]) - Math.Log(e[lo]));
                return this.yieldRel[lo] + f * (this.yieldRel[hi] - this.yieldRel[lo]);
            }
        }

        static readonly object Gate = new object();
        static Dictionary<int, Element> elements;
        static Dictionary<int, double> atomicMass;
        static Dictionary<int, string> symbols;
        static Dictionary<int, Fluorescence> fluorescence;
        static readonly Dictionary<int, PhotoShellModel> photoShells =
            new Dictionary<int, PhotoShellModel>();
        static readonly Dictionary<string, LightYieldCurve> lightYields =
            new Dictionary<string, LightYieldCurve>();

        /// <summary>Атомные массы, г/моль, по Z. Ключ есть у всех ста элементов.</summary>
        public static Dictionary<int, double> AtomicMass
        {
            get
            {
                Load();
                return atomicMass;
            }
        }

        public static bool TryGet(int z, out Element element)
        {
            Load();
            return elements.TryGetValue(z, out element);
        }

        /// <summary>Ответ атома на дырку в K-оболочке; null, если данных нет.</summary>
        public static Fluorescence FluorescenceOf(int z)
        {
            Load();
            Fluorescence value;
            return fluorescence.TryGetValue(z, out value) ? value : null;
        }

        public static bool Has(int z)
        {
            Load();
            return elements.ContainsKey(z);
        }

        /// <summary>Символ элемента по Z или его номер строкой, если такого нет.</summary>
        public static string SymbolOf(int z)
        {
            Load();
            string symbol;
            return symbols.TryGetValue(z, out symbol)
                ? symbol
                : z.ToString(CultureInfo.InvariantCulture);
        }

        public static int ZOf(string symbol)
        {
            Load();
            foreach (KeyValuePair<int, string> pair in symbols)
            {
                // Без учёта регистра: хранение каноническое, но формулы веществ
                // исторически писались и «TI», и «Ti» — регистр не должен
                // молча превращать элемент в «не найден».
                if (string.Equals(pair.Value, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }

            return 0;
        }

        /// <summary>Каноническое написание символа: «TI» и «ti» → «Ti».</summary>
        static string CanonicalSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                return symbol;
            }

            return char.ToUpperInvariant(symbol[0])
                + (symbol.Length > 1 ? symbol.Substring(1).ToLowerInvariant() : "");
        }

        /// <summary>
        /// Пооболочечный фотоэффект элемента; null, если в базе нет строки
        /// `epics_photo_meta` для этого Z. Грузится лениво и по одному
        /// элементу: таблица `epics_photo_subshell` — 370 тысяч строк на сто
        /// элементов, а нужны из них только элементы кристалла.
        /// </summary>
        public static PhotoShellModel PhotoShellOf(int z)
        {
            lock (Gate)
            {
                PhotoShellModel cached;
                if (photoShells.TryGetValue(z, out cached))
                {
                    return cached;
                }

                PhotoShellModel model = LoadPhotoShell(z);
                photoShells[z] = model;
                return model;
            }
        }

        /// <summary>
        /// Кривая светового выхода по имени материала базы («CsI:Tl»); null,
        /// если строк нет — тогда шкала считается пропорциональной. Грузится
        /// лениво и кэшируется, включая отрицательный ответ.
        /// </summary>
        public static LightYieldCurve LightYieldOf(string material)
        {
            lock (Gate)
            {
                LightYieldCurve cached;
                if (lightYields.TryGetValue(material, out cached))
                {
                    return cached;
                }

                LightYieldCurve curve = LoadLightYield(material);
                lightYields[material] = curve;
                return curve;
            }
        }

        static LightYieldCurve LoadLightYield(string material)
        {
            string path = DatabasePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "nucdb.sqlite не найдена рядом с программой: " + path, path);
            }

            List<double> energies = new List<double>();
            List<double> yields = new List<double>();
            using (SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select energy_kev, yield_rel from scint_electron_light_yield" +
                        " where material = $m order by energy_kev";
                    command.Parameters.AddWithValue("$m", material);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            energies.Add(reader.GetDouble(0));
                            yields.Add(reader.GetDouble(1));
                        }
                    }
                }
            }

            if (energies.Count < 2)
            {
                return null;
            }

            return new LightYieldCurve
            {
                Material = material,
                energyKev = energies.ToArray(),
                yieldRel = yields.ToArray(),
            };
        }

        static PhotoShellModel LoadPhotoShell(int z)
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
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select n_shells, high_from_ev, low_from_ev from epics_photo_meta where z=" + z;
                    int shells;
                    PhotoShellModel model = new PhotoShellModel();
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        shells = reader.GetInt32(0);
                        model.highFromKev = reader.GetDouble(1) / 1000.0;
                        model.lowFromKev = reader.GetDouble(2) / 1000.0;
                    }

                    // Строки фитов кумулятивны: K — строка 0, полное сечение —
                    // последняя. Для доли K другие строки не нужны.
                    command.CommandText =
                        "select kind, shell_seq, edge_ev, a1_b, a2_b, a3_b, a4_b, a5_b, a6_b" +
                        " from epics_photo_fit where z=" + z +
                        " and shell_seq in (0, " + (shells - 1) + ")";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            bool high = reader.GetString(0) == "high";
                            bool total = reader.GetInt32(1) == shells - 1;
                            double[] a = new double[6];
                            for (int i = 0; i < 6; i++)
                            {
                                a[i] = reader.GetDouble(3 + i);
                            }

                            if (!total || shells == 1)
                            {
                                model.kEdgeKev = reader.GetDouble(2) / 1000.0;
                            }

                            if (high)
                            {
                                if (total) model.highTotal = a; else model.highK = a;
                            }
                            else
                            {
                                if (total) model.lowTotal = a; else model.lowK = a;
                            }

                            // у одноболочечных (водород, гелий) K и есть полное
                            if (shells == 1)
                            {
                                if (high) model.highK = a; else model.lowK = a;
                            }
                        }
                    }

                    if (model.lowK == null || model.lowTotal == null
                        || model.highK == null || model.highTotal == null)
                    {
                        return null;
                    }

                    // Табличные векторы нужны только в зазоре между K-краем и
                    // началом фитов; у большинства элементов он пуст.
                    model.tableE = new double[shells][];
                    model.tableCs = new double[shells][];
                    for (int s = 0; s < shells; s++)
                    {
                        model.tableE[s] = new double[0];
                        model.tableCs[s] = new double[0];
                    }

                    if (model.lowFromKev > model.kEdgeKev + 1e-9)
                    {
                        command.CommandText =
                            "select shell_seq, energy_ev, cs_b from epics_photo_subshell" +
                            " where z=" + z + " order by shell_seq, energy_ev";
                        List<double>[] es = new List<double>[shells];
                        List<double>[] cs = new List<double>[shells];
                        for (int s = 0; s < shells; s++)
                        {
                            es[s] = new List<double>();
                            cs[s] = new List<double>();
                        }

                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int s = reader.GetInt32(0);
                                if (s < 0 || s >= shells)
                                {
                                    continue;
                                }

                                es[s].Add(reader.GetDouble(1) / 1000.0);
                                cs[s].Add(reader.GetDouble(2));
                            }
                        }

                        for (int s = 0; s < shells; s++)
                        {
                            model.tableE[s] = es[s].ToArray();
                            model.tableCs[s] = cs[s].ToArray();
                        }
                    }

                    return model;
                }
            }
        }

        /// <summary>
        /// База лежит рядом с программой, а не в текущем каталоге: пробы и
        /// харнессы запускаются откуда попало, а файл всегда рядом с их exe.
        /// </summary>
        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static void Load()
        {
            if (elements != null)
            {
                return;
            }

            lock (Gate)
            {
                if (elements != null)
                {
                    return;
                }

                string path = DatabasePath();
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "nucdb.sqlite не найдена рядом с программой: " + path, path);
                }

                Dictionary<int, Element> loaded = new Dictionary<int, Element>();
                Dictionary<int, double> masses = new Dictionary<int, double>();
                Dictionary<int, string> names = new Dictionary<int, string>();
                Dictionary<int, Fluorescence> fluo = new Dictionary<int, Fluorescence>();

                using (SqliteConnection connection = new SqliteConnection(
                    "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select z, atomic_weight from xcom_elements order by z";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int z = reader.GetInt32(0);
                                masses[z] = reader.GetDouble(1);
                                loaded[z] = new Element { AtomicWeight = reader.GetDouble(1) };
                            }
                        }

                        // Символы — из таблицы нуклидов: она про те же элементы,
                        // и заводить второй список значило бы завести второй
                        // источник правды.
                        //
                        // Без GROUP BY по «голой» колонке: в базе у одного z
                        // лежат разные написания (Li/LI, Ti/TI, Ni/NI), и SQLite
                        // отдавал символ произвольной строки группы — после
                        // пересборки базы элемент мог тихо сменить регистр и
                        // выпасть из разбора формул. Написание приводится к
                        // каноническому здесь. z = 0 (нейтрон, «n»/«NN»)
                        // исключён: его «N» столкнулся бы с азотом.
                        command.CommandText = "select z, symbol from nuclides"
                            + " where symbol is not null and z > 0 order by z, symbol";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int z = reader.GetInt32(0);
                                if (!names.ContainsKey(z))
                                {
                                    names[z] = CanonicalSymbol(reader.GetString(1).Trim());
                                }
                            }
                        }

                        command.CommandText =
                            "select z, k_edge_ev, k_fraction, omega_k, ka1_ev, ka1_weight," +
                            " ka2_ev, ka2_weight, kb_ev, kb_weight from xray_fluorescence";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                fluo[reader.GetInt32(0)] = new Fluorescence
                                {
                                    KEdgeKev = reader.GetDouble(1) / 1000.0,
                                    KFraction = reader.GetDouble(2),
                                    OmegaK = reader.GetDouble(3),
                                    LineKev = new double[]
                                    {
                                        reader.GetDouble(4) / 1000.0,
                                        reader.GetDouble(6) / 1000.0,
                                        reader.GetDouble(8) / 1000.0,
                                    },
                                    LineWeight = new double[]
                                    {
                                        reader.GetDouble(5), reader.GetDouble(7), reader.GetDouble(9),
                                    },
                                };
                            }
                        }

                        command.CommandText =
                            "select z, energy_ev, coherent_b, incoherent_b, photoelectric_b," +
                            " pair_nuclear_b, pair_electron_b from xcom_cross_sections order by z, energy_ev";
                        Dictionary<int, List<double[]>> rows = new Dictionary<int, List<double[]>>();
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int z = reader.GetInt32(0);
                                List<double[]> list;
                                if (!rows.TryGetValue(z, out list))
                                {
                                    list = new List<double[]>();
                                    rows[z] = list;
                                }

                                list.Add(new double[]
                                {
                                    reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3),
                                    reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6),
                                });
                            }
                        }

                        foreach (KeyValuePair<int, List<double[]>> pair in rows)
                        {
                            Element element;
                            if (!loaded.TryGetValue(pair.Key, out element) || !(element.AtomicWeight > 0.0))
                            {
                                continue;
                            }

                            // Барн на атом -> см2/г. Перевод делается ЗДЕСЬ, а не
                            // при импорте: он зависит от атомного веса, а вес
                            // берётся из той же базы, и вморозить в таблицу
                            // результат деления значило бы связать её с сегодняшним
                            // значением веса навсегда.
                            double factor = 1e-24 * 6.02214076e23 / element.AtomicWeight;
                            int n = pair.Value.Count;
                            element.EnergyKev = new double[n];
                            element.Channels = new double[5][];
                            for (int c = 0; c < 5; c++)
                            {
                                element.Channels[c] = new double[n];
                            }

                            element.Total = new double[n];
                            for (int i = 0; i < n; i++)
                            {
                                double[] row = pair.Value[i];
                                element.EnergyKev[i] = row[0] / 1000.0;
                                double sum = 0.0;
                                for (int c = 0; c < 5; c++)
                                {
                                    double value = row[1 + c] * factor;
                                    element.Channels[c][i] = value;
                                    sum += value;
                                }

                                element.Total[i] = sum;
                            }
                        }
                    }
                }

                // Элементы без сечений в наборе не нужны: у них нечего спросить.
                List<int> empty = new List<int>();
                foreach (KeyValuePair<int, Element> pair in loaded)
                {
                    if (pair.Value.EnergyKev == null)
                    {
                        empty.Add(pair.Key);
                    }
                }

                foreach (int z in empty)
                {
                    loaded.Remove(z);
                }

                atomicMass = masses;
                symbols = names;
                fluorescence = fluo;
                elements = loaded;
            }
        }

        /// <summary>
        /// Лог-лог интерполяция по сетке. За краями таблицы держится крайнее
        /// значение: экстраполировать степенной закон фотопоглощения вниз
        /// нельзя, а вверх сечения уже почти постоянны.
        /// </summary>
        public static double Interpolate(double[] grid, double[] values, double x)
        {
            int n = grid.Length;
            if (n == 0)
            {
                return 0.0;
            }

            if (x <= grid[0])
            {
                return values[0];
            }

            if (x >= grid[n - 1])
            {
                return values[n - 1];
            }

            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (grid[mid] <= x)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            double x0 = grid[lo], x1 = grid[hi];
            double y0 = values[lo], y1 = values[hi];
            if (!(x1 > x0))
            {
                // край поглощения: две точки на одной энергии, берётся верхняя
                return y1;
            }

            if (!(y0 > 0.0) || !(y1 > 0.0))
            {
                // канал открывается не с нуля шкалы: рождение пар ниже 1.022 МэВ
                // тождественно нулевое, и логарифм там брать не от чего
                double f = (x - x0) / (x1 - x0);
                return y0 + f * (y1 - y0);
            }

            double t = (Math.Log(x) - Math.Log(x0)) / (Math.Log(x1) - Math.Log(x0));
            return Math.Exp(Math.Log(y0) + t * (Math.Log(y1) - Math.Log(y0)));
        }
    }
}
