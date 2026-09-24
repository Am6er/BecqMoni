using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EscapeAnchorProbeA83
{
    /// <summary>
    /// ⛔ `AMBER83` (полоса П147, 24.09.2026, физика 24): ГДЕ СТОЯТ ПИКИ ВЫЛЕТА В
    /// СТРОКЕ УЗЛА.
    ///
    /// `EfficiencySimulator.RemapLightScale` до физики 24 брал якорь световой
    /// шкалы к ЦЕНТРУ пикового бина (`peak·шаг`), а не к энергии узла `E`: вся
    /// строка, кроме пика, растягивалась множителем `peak·шаг/E = 1 − δ/E`
    /// (`δ = E − peak·шаг`, от −1 до +1 кэВ при шаге 2). Пик читатель ставит на
    /// `E` сам (`AMBER70`, П135), а каналы вылета переносятся СДВИГОМ на
    /// `E_линии − E_узла` — значит растяжка узла доезжает до образа как есть,
    /// и два соседних узла ставят один и тот же вылет в разные места («пила»
    /// по узлам, раздвоение между ними).
    ///
    /// Проба читает строки КАНАЛОВ узлов (`ChannelRows`) и для каждого узла
    /// меряет центр тяжести канала вылета в окне по потере энергии
    /// (K ±10 кэВ вокруг `E − 31.5`, SE/DE ±8 вокруг `E − 511`/`E − 1022`;
    /// канал 6 — ±8 вокруг 511, и offset там от 511, а не от `E`):
    /// `offset = центр − E_узла`. Физически `offset ≈ −E_X` (энергия, унесённая
    /// вылетом; 511, 1022, K-рентген) и меняется по узлам плавно; растяжка
    /// добавляет `−центр·δ/E_узла`, скачущий вместе с `δ`.
    ///
    /// Печатает по каналу: число узлов, разброс `offset` соседних узлов
    /// (СКО разности соседей, «пила»), худшее раздвоение соседей, сколько
    /// «пилы» объясняет растяжка (СКО той же разности после вычитания
    /// `−центр·δ/E`) и НАКЛОН разности offset соседей по разности растяжки
    /// (МНК через ноль): ≈1 — строка растянута, ≈0 — нет. Раздвоение само по
    /// себе в приёмку не годится: в окне канала лежит и континуум (хвост
    /// второго кванта пары, смесь линий K), и шум узла даёт десятые кэВ.
    ///
    ///     escapeanchorprobea83 --matrix=&lt;файл.rmx&gt; [--ch=3,2,4,6] [--emin=] [--emax=]
    ///                          [--tol=0.3] [--csv=&lt;файл&gt;]
    ///
    /// Код 0 — |наклон| ≤ `--tol` у всех каналов с ≥ 10 узлами; 1 — больше
    /// (на матрице физики ≤ 23 — ОЖИДАЕМО, это и есть дефект).
    /// </summary>
    static class Program
    {
        static string F(double v, int d)
        {
            return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static readonly string[] ChannelName =
        {
            "пик", "комптон", "вылет 511 (SE)", "вылет K", "вылет 1022 (DE)", "вылет L", "511 извне"
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null, csv = null;
            int[] channels = { 3, 2, 4, 6 };
            double emin = 0.0, emax = double.MaxValue, tol = 0.3;
            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) path = a.Substring(9);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) emin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) emax = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--ch=", StringComparison.Ordinal))
                {
                    channels = Array.ConvertAll(a.Substring(5).Split(','), x => int.Parse(x, CultureInfo.InvariantCulture));
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ " + a);
                    return 2;
                }
            }

            if (path == null || !File.Exists(path))
            {
                Console.Error.WriteLine("нет матрицы: " + path);
                return 2;
            }

            ResponseMatrix m = ResponseMatrix.Load(path);
            if (m == null || !m.HasChannels)
            {
                Console.Error.WriteLine("матрица не читается или без каналов: " + path);
                return 2;
            }

            double h = m.BinKev;
            Console.WriteLine("матрица {0}: узлов {1}, шаг {2} кэВ, клеймо {3}", Path.GetFileName(path),
                              m.Energies.Length, F(h, 3), m.Stamp);
            var lines = new List<string> { "channel;node_kev;delta_kev;centroid_kev;offset_kev;stretch_kev;weight" };
            double worstBeta = 0.0;
            foreach (int c in channels)
            {
                if (c < 0 || c >= m.ChannelRows.Length)
                {
                    continue;
                }

                var nodeE = new List<double>();
                var offset = new List<double>();
                var stretch = new List<double>();
                for (int k = 0; k < m.Energies.Length; k++)
                {
                    double e = m.Energies[k];
                    if (e < emin || e > emax)
                    {
                        continue;
                    }

                    float[] row = m.ChannelRows[c][k];
                    int peak = EfficiencySimulator.PeakBin(e, h);
                    if (row == null || row.Length < 3)
                    {
                        continue;
                    }

                    // ОКНО ПО ПОТЕРЕ ЭНЕРГИИ, а не по максимуму: у K-вылета CsI
                    // четыре линии (I/Cs Kα, Kβ, 28…36 кэВ), и окно «вокруг
                    // максимума» ловило бы то одну, то другую. Канал постоянной
                    // энергии (№ 6) меряется у 511 кэВ, а не от энергии узла.
                    double center = c == 6 ? 511.0 : e - Loss(c);
                    double half = c == 3 ? 10.0 : 8.0;
                    int lo = Math.Max(0, (int)Math.Floor((center - half) / h));
                    int hi = Math.Min(Math.Min(peak, row.Length) - 1, (int)Math.Ceiling((center + half) / h));
                    if (hi <= lo)
                    {
                        continue;
                    }

                    double w = 0.0, wx = 0.0;
                    for (int b = lo; b <= hi; b++)
                    {
                        w += row[b];
                        wx += row[b] * b * h;
                    }

                    if (!(w > 0.0))
                    {
                        continue;
                    }

                    double centroid = wx / w;
                    double delta = e - peak * h;
                    double reference = c == 6 ? 0.0 : e;
                    nodeE.Add(e);
                    offset.Add(centroid - reference);
                    stretch.Add(-centroid * delta / e);
                    lines.Add(string.Join(";", new[]
                    {
                        c.ToString(CultureInfo.InvariantCulture), F(e, 4), F(delta, 4), F(centroid, 4),
                        F(centroid - reference, 4), F(-centroid * delta / e, 4), w.ToString("R", CultureInfo.InvariantCulture)
                    }));
                }

                if (nodeE.Count < 3)
                {
                    Console.WriteLine("  канал {0} ({1}): узлов с пиком {2} — мерить нечего",
                                      c, c < ChannelName.Length ? ChannelName[c] : "?", nodeE.Count);
                    continue;
                }

                double s1 = 0.0, s2 = 0.0, worst = 0.0, worstAt = 0.0, sxy = 0.0, sxx = 0.0;
                for (int i = 1; i < nodeE.Count; i++)
                {
                    double d = offset[i] - offset[i - 1];
                    double ds = stretch[i] - stretch[i - 1];
                    double dr = d - ds;
                    s1 += d * d;
                    s2 += dr * dr;
                    sxy += d * ds;
                    sxx += ds * ds;
                    if (Math.Abs(d) > worst)
                    {
                        worst = Math.Abs(d);
                        worstAt = Math.Sqrt(nodeE[i] * nodeE[i - 1]);
                    }
                }

                int pairs = nodeE.Count - 1;
                // Наклон разности offset соседей по разности растяжки: у
                // строки, растянутой якорем к центру бина, он около 1; у
                // строки с якорем к энергии узла — около 0 (растяжки нет, и
                // `−центр·δ/E` с offset не связан).
                double beta = sxx > 0.0 ? sxy / sxx : 0.0;
                Console.WriteLine("  канал {0} ({1}): узлов {2}; разность offset соседей СКО {3} кэВ, худшее раздвоение "
                                  + "{4} кэВ (у {5} кэВ); после вычета растяжки −центр·δ/E СКО {6} кэВ; offset от {7} до {8}; "
                                  + "НАКЛОН по растяжке {9}",
                                  c, c < ChannelName.Length ? ChannelName[c] : "?", nodeE.Count,
                                  F(Math.Sqrt(s1 / pairs), 3), F(worst, 3), F(worstAt, 1), F(Math.Sqrt(s2 / pairs), 3),
                                  F(Min(offset), 3), F(Max(offset), 3), F(beta, 3));
                if (nodeE.Count >= 10)
                {
                    worstBeta = Math.Max(worstBeta, Math.Abs(beta));
                }
            }

            if (csv != null)
            {
                File.WriteAllLines(csv, lines, new UTF8Encoding(false));
            }

            bool ok = worstBeta <= tol;
            Console.WriteLine("{0} наибольший |наклон| offset по растяжке {1} (допуск {2}; ≈1 — строка растянута якорем "
                              + "к центру бина, ≈0 — якорь к энергии узла)", ok ? "✅" : "⛔", F(worstBeta, 3), F(tol, 3));
            return ok ? 0 : 1;
        }

        /// <summary>Потеря энергии канала вылета, кэВ (середина окна).</summary>
        static double Loss(int c)
        {
            switch (c)
            {
                case 2: return 511.0;
                case 3: return 31.5;
                case 4: return 1022.0;
                case 5: return 4.5;
                default: return 0.0;
            }
        }

        static double Min(List<double> v)
        {
            double r = double.MaxValue;
            foreach (double x in v) r = Math.Min(r, x);
            return r;
        }

        static double Max(List<double> v)
        {
            double r = double.MinValue;
            foreach (double x in v) r = Math.Max(r, x);
            return r;
        }
    }
}
