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

        static readonly object Gate = new object();
        static Dictionary<int, Element> elements;
        static Dictionary<int, double> atomicMass;
        static Dictionary<int, string> symbols;

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
                if (string.Equals(pair.Value, symbol, StringComparison.Ordinal))
                {
                    return pair.Key;
                }
            }

            return 0;
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
                        command.CommandText = "select z, symbol from nuclides where symbol is not null group by z";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                names[reader.GetInt32(0)] = reader.GetString(1).Trim();
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
