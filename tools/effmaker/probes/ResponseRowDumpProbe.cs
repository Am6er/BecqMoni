using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace ResponseRowDumpProbe
{
    /// <summary>
    /// СЫРОЙ ОТКЛИК МАТРИЦЫ НА ОДНУ ЛИНИЮ, ПО КАНАЛАМ, ДО УШИРЕНИЯ.
    ///
    /// Задача Amber 11.09.2026, консоль: внешнему рецензенту нужны «два
    /// отдельных отклика матрицы: на 32,194 и 661,657 кэВ, желательно до
    /// размытия разрешением» — по ним отделяется ошибка физического ядра от
    /// ошибки свёртки или подбора весов.
    ///
    /// ⛔ ПЛЕЧЕЙ НЕСКОЛЬКО НАРОЧНО, и это главное в пробе. Между тем, что
    /// лежит в складской матрице, и тем, что считает нынешний код, за сутки
    /// легло два изменения — пятый канал (`AMBER15`) и включённый умолчанием
    /// допуск пика (`E34`, `PeakToleranceFromGeometry`). Отдавать наружу
    /// только одно из двух значило бы выдать вчерашнее определение «полного
    /// поглощения» за сегодняшнее либо наоборот:
    ///
    ///   * `store_node`   — СТРОКА УЗЛА складской матрицы как есть, без
    ///                      переноса: то, что реально посчитано;
    ///   * `store_interp` — она же, перенесённая на точную энергию линии
    ///                      (`ResponseMatrix.Accumulate`) — ровно то, что
    ///                      получает разбор перед уширением;
    ///   * `direct`       — узел, посчитанный ЗАНОВО нынешним кодом на той же
    ///                      геометрии и тех же настройках. Плечи допуска пика
    ///                      задаёт `--peakw=`.
    ///
    /// Прямое плечо считается ШТАТНЫМ строителем (`ResponseMatrixBuilder.Build`)
    /// с сеткой из двух узлов: при `NodeCount = 2` сетка вырождается в свои
    /// концы (`BuildGrid`: lo и hi), то есть узлы встают ТОЧНО на запрошенные
    /// энергии. Своего вызова симулятора здесь нет нарочно — второй способ
    /// настроить его однажды разойдётся с первым молча (`S37`).
    ///
    ///     responserowdumpprobe --spectrum=&lt;файл.xml&gt; [--e=32.194,661.657]
    ///                          [--out=&lt;префикс&gt;] [--direct] [--peakw=1|0|both]
    ///                          [--matrix-any]
    ///
    /// Матрица берётся из склада по Guid кривой эффективности спектра — тем же
    /// путём, каким её берёт приложение.
    /// </summary>
    static class Program
    {
        static readonly string[] ChannelNames =
        {
            "peak", "compton", "esc_se", "esc_xray", "esc_de"
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null;
            string outPrefix = null;
            double[] energies = { 32.194, 661.657 };
            bool direct = false;
            string peakw = "both";
            bool matrixAny = false;
            var ablations = new List<string>();

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else if (a.StartsWith("--out=", StringComparison.Ordinal))
                {
                    outPrefix = a.Substring(6);
                }
                else if (a.StartsWith("--e=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(4).Split(',');
                    energies = new double[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        energies[i] = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
                    }
                }
                else if (a == "--direct")
                {
                    direct = true;
                }
                else if (a.StartsWith("--peakw=", StringComparison.Ordinal))
                {
                    peakw = a.Substring(8);
                }
                else if (a == "--matrix-any")
                {
                    matrixAny = true;
                }
                else if (a == "--noscat")
                {
                    ablations.Add("scat");
                }
                else if (a.StartsWith("--ablate=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(9).Split(','))
                    {
                        string name = part.Trim();
                        if (name != "scat" && name != "lx" && name != "brem")
                        {
                            Console.Error.WriteLine("--ablate= знает только scat, lx, brem; дано: {0}",
                                                    name);
                            return 2;
                        }

                        ablations.Add(name);
                    }
                }
                else
                {
                    // (`A263`) Неизвестное ИМЯ ключа — отказ, а не молчание.
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (peakw != "1" && peakw != "0" && peakw != "both")
            {
                Console.Error.WriteLine("--peakw= принимает 1, 0 или both");
                return 2;
            }

            Array.Sort(energies);
            if (outPrefix == null)
            {
                outPrefix = "rowdump";
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));

            if (rd.Efficiency == null)
            {
                Console.Error.WriteLine("⛔ у спектра нет кривой эффективности — матрицу не найти");
                return 1;
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency.Guid, out refusal,
                                                            out fileFormat);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат файла {1})", refusal, fileFormat);
                return 1;
            }

            GeometryModel geometry = rd.Efficiency.HasGeometry ? rd.Efficiency.Geometry : null;
            bool stampOk = geometry != null && matrix.IsValidFor(geometry);
            if (!stampOk && !matrixAny)
            {
                Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ: матрица есть, но не для этой геометрии.");
                if (geometry != null)
                {
                    Console.Error.WriteLine("   у матрицы          : {0}", matrix.Stamp);
                    Console.Error.WriteLine("   у геометрии спектра: {0}",
                                            ResponseMatrix.ComputeStamp(geometry, matrix.Options));
                }

                Console.Error.WriteLine("   осознанно — ключ --matrix-any");
                return 1;
            }

            Console.WriteLine("кривая : {0} ({1})", rd.Efficiency.Name, rd.Efficiency.Guid);
            Console.WriteLine("матрица: узлов {0}, каналов {1}, бин {2} кэВ, историй на узел {3}",
                              matrix.Energies != null ? matrix.Energies.Length : 0,
                              matrix.HasChannels ? matrix.ChannelRows.Length : 0,
                              F(matrix.BinKev, 2), matrix.Histories);
            Console.WriteLine("клеймо : {0}", matrix.Stamp);
            Console.WriteLine("допуск пика в файле: PeakToleranceFromGeometry={0}",
                              matrix.Options != null && matrix.Options.PeakToleranceFromGeometry
                                  ? "ВКЛ" : "ВЫКЛ");
            if (geometry != null)
            {
                Console.WriteLine("геометрия: ПШПВ(662) {0} %, допуск пика на {1} кэВ = {2} кэВ, "
                                  + "на {3} кэВ = {4} кэВ",
                                  F(geometry.FwhmAt662Percent, 2),
                                  F(energies[0], 3), F(geometry.PeakHalfWidthKev(energies[0]), 4),
                                  F(energies[energies.Length - 1], 3),
                                  F(geometry.PeakHalfWidthKev(energies[energies.Length - 1]), 4));
            }

            if (!matrix.HasChannels)
            {
                Console.Error.WriteLine("⛔ у складской матрицы нет раскладки по каналам");
                return 1;
            }

            var rows = new List<string>();
            rows.Add("arm;line_kev;node_kev;bin;dep_kev;" + string.Join(";", ChannelNames) + ";total");

            foreach (double e in energies)
            {
                Report(matrix, e);
                DumpNode(rows, "store_node", matrix, e);
                DumpInterpolated(rows, "store_interp", matrix, e);
            }

            if (direct)
            {
                if (geometry == null)
                {
                    Console.Error.WriteLine("⛔ --direct: у кривой нет геометрии, считать нечем");
                    return 1;
                }

                if (energies.Length < 2)
                {
                    Console.Error.WriteLine("⛔ --direct: нужно ровно две энергии (сетка из двух узлов)");
                    return 1;
                }

                if (peakw == "both" || peakw == "1")
                {
                    if (!Direct(rows, geometry, matrix, energies, true, null))
                    {
                        return 1;
                    }
                }

                if (peakw == "both" || peakw == "0")
                {
                    if (!Direct(rows, geometry, matrix, energies, false, null))
                    {
                        return 1;
                    }
                }

                // Контрольные плечи к вопросу «откуда максимум в канале
                // неполного поглощения у самой линии»: каждое снимает ОДНУ
                // статью потери. `scat` — однократное рассеяние ДО кристалла,
                // `lx` — вылет L-рентгена кристалла, `brem` — тормозное.
                // Держится максимум одной из них — здесь он и просядет; не
                // просядет ни в одном плече, значит дело не в физике, а в том,
                // ЧТО записывается в гистограмму.
                foreach (string ablation in ablations)
                {
                    if (!Direct(rows, geometry, matrix, energies, true, ablation))
                    {
                        return 1;
                    }
                }
            }

            string csv = outPrefix + ".csv";
            File.WriteAllLines(csv, rows, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("записано: {0} ({1} строк)", csv, rows.Count - 1);
            return 0;
        }

        /// <summary>
        /// Узлы, между которыми стоит линия, и веса переноса — то самое место,
        /// где «ошибка свёртки» отделяется от «ошибки ядра».
        /// </summary>
        static void Report(ResponseMatrix matrix, double energyKev)
        {
            double[] grid = matrix.Energies;
            Console.WriteLine();
            Console.WriteLine("--- линия {0} кэВ ---", F(energyKev, 3));
            int hi = Array.BinarySearch(grid, energyKev);
            if (hi >= 0)
            {
                Console.WriteLine("узел сетки СОВПАДАЕТ: {0} кэВ (№ {1})", F(grid[hi], 4), hi);
                return;
            }

            hi = ~hi;
            if (hi <= 0 || hi >= grid.Length)
            {
                int at = hi <= 0 ? 0 : grid.Length - 1;
                Console.WriteLine("линия ВНЕ сетки, взят край: {0} кэВ (№ {1})", F(grid[at], 4), at);
                return;
            }

            int lo = hi - 1;
            double span = grid[hi] - grid[lo];
            double t = span > 0.0 ? (energyKev - grid[lo]) / span : 0.0;
            Console.WriteLine("узлы сетки: {0} (№ {1}) и {2} (№ {3}), вес верхнего t = {4}",
                              F(grid[lo], 4), lo, F(grid[hi], 4), hi, F(t, 5));
            Console.WriteLine("перенос: каждая строка растягивается со своего узла на энергию линии "
                              + "(доля от энергии сохраняется), затем смешивается с весами {0} и {1}",
                              F(1.0 - t, 5), F(t, 5));
        }

        /// <summary>Строка ближайшего узла как есть — без переноса.</summary>
        static void DumpNode(List<string> rows, string arm, ResponseMatrix matrix, double energyKev)
        {
            int index = NearestNode(matrix.Energies, energyKev);
            double nodeKev = matrix.Energies[index];
            int channels = matrix.ChannelRows.Length;
            int length = 0;
            for (int c = 0; c < channels; c++)
            {
                float[] row = matrix.ChannelRows[c][index];
                if (row != null && row.Length > length)
                {
                    length = row.Length;
                }
            }

            for (int b = 0; b < length; b++)
            {
                double[] values = new double[channels];
                double total = 0.0;
                for (int c = 0; c < channels; c++)
                {
                    float[] row = matrix.ChannelRows[c][index];
                    values[c] = row != null && b < row.Length ? row[b] : 0.0;
                    total += values[c];
                }

                Emit(rows, arm, energyKev, nodeKev, b, b * matrix.BinKev, values, total, channels);
            }
        }

        /// <summary>
        /// Отклик, перенесённый на точную энергию линии, — то, что получает
        /// разбор: `AccumulateChannel` с единичным весом, канал за каналом.
        /// </summary>
        static void DumpInterpolated(List<string> rows, string arm, ResponseMatrix matrix,
                                     double energyKev)
        {
            int channels = matrix.ChannelRows.Length;
            int length = (int)(energyKev / matrix.BinKev + 0.5) + 1;
            double[][] parts = new double[channels][];
            for (int c = 0; c < channels; c++)
            {
                parts[c] = new double[length];
                matrix.AccumulateChannel(parts[c], energyKev, 1.0, c);
            }

            for (int b = 0; b < length; b++)
            {
                double[] values = new double[channels];
                double total = 0.0;
                for (int c = 0; c < channels; c++)
                {
                    values[c] = parts[c][b];
                    total += values[c];
                }

                Emit(rows, arm, energyKev, energyKev, b, b * matrix.BinKev, values, total, channels);
            }
        }

        /// <summary>
        /// Прямое плечо: узлы считаются заново нынешним кодом, настройки —
        /// складские, кроме допуска пика, который и есть предмет плеча.
        /// </summary>
        static bool Direct(List<string> rows, GeometryModel geometry, ResponseMatrix store,
                           double[] energies, bool peakTolerance, string ablation)
        {
            ResponseMatrixOptions options = store.Options != null
                ? store.Options.Clone()
                : new ResponseMatrixOptions();
            options.NodeCount = 2;
            options.MinEnergyKev = energies[0];
            options.MaxEnergyKev = energies[energies.Length - 1];
            options.ResolveEdges = false;
            options.PeakToleranceFromGeometry = peakTolerance;
            if (ablation == "scat")
            {
                options.SingleScatter = false;
            }
            else if (ablation == "lx")
            {
                options.LXrayEscape = false;
            }
            else if (ablation == "brem")
            {
                options.Bremsstrahlung = false;
            }

            string arm = ablation != null
                ? "direct_no_" + ablation
                : (peakTolerance ? "direct_peakw1" : "direct_peakw0");
            Console.WriteLine();
            Console.WriteLine("--- плечо {0}: счёт нынешним кодом, историй на узел {1}, цель шума {2} % ---",
                              arm, options.Histories, F(options.ContinuumErrorTarget, 2));

            DateTime started = DateTime.Now;
            ResponseMatrix fresh = ResponseMatrixBuilder.Build(geometry, options, null,
                                                               CancellationToken.None);
            if (fresh == null || !fresh.HasChannels)
            {
                Console.Error.WriteLine("⛔ прямое плечо не построилось");
                return false;
            }

            Console.WriteLine("посчитано за {0} с; узлов {1}, каналов {2}, клеймо {3}",
                              F((DateTime.Now - started).TotalSeconds, 1),
                              fresh.Energies.Length, fresh.ChannelRows.Length, fresh.Stamp);
            for (int i = 0; i < fresh.Energies.Length; i++)
            {
                Console.WriteLine("   узел {0}: {1} кэВ, шум {2} %", i, F(fresh.Energies[i], 4),
                                  fresh.NodeErrors != null && i < fresh.NodeErrors.Length
                                      ? F(fresh.NodeErrors[i], 3) : "—");
            }

            foreach (double e in energies)
            {
                DumpNode(rows, arm, fresh, e);
            }

            return true;
        }

        static int NearestNode(double[] grid, double energyKev)
        {
            int best = 0;
            double bestGap = double.MaxValue;
            for (int i = 0; i < grid.Length; i++)
            {
                double gap = Math.Abs(grid[i] - energyKev);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }

            return best;
        }

        static void Emit(List<string> rows, string arm, double lineKev, double nodeKev, int bin,
                         double depositKev, double[] values, double total, int channels)
        {
            var sb = new StringBuilder();
            sb.Append(arm).Append(';');
            sb.Append(F(lineKev, 3)).Append(';');
            sb.Append(F(nodeKev, 4)).Append(';');
            sb.Append(bin.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(F(depositKev, 2));
            for (int c = 0; c < ChannelNames.Length; c++)
            {
                sb.Append(';').Append(c < channels ? E(values[c]) : E(0.0));
            }

            sb.Append(';').Append(E(total));
            rows.Add(sb.ToString());
        }

        // ⛔ Разделитель дробной части — ТОЧКА, культурой инвариантной и явной
        // (правило Amber 05.09.2026): число, напечатанное культурой потока,
        // читается на другой машине как другое число, и молча.
        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
        }

        static string E(double value)
        {
            return value.ToString("E6", CultureInfo.InvariantCulture);
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            return rd;
        }
    }
}
