using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ComptonMapProbe
{
    /// <summary>
    /// КУДА КАРТА КОМПТОНОВСКОГО КАНАЛА ПЕРЕНОСИТ ЛИНИЮ ПОСТОЯННОЙ ЭНЕРГИИ 511 кэВ —
    /// мерка `AMBER52` (П122, 22.09.2026).
    ///
    /// Посылка строки: `ResponseMatrix.Transfer` (ветвь `Compton`) переносит строку
    /// узла на энергию линии кусочно-линейной картой по узлам `0 → 0`, `back → back'`,
    /// `edge → edge'`, `E_узла → E`, и всё содержимое между узлами карты тянется
    /// наклоном отрезка. Аннигиляционная линия 511 кэВ от пары ВНЕ кристалла (`A52`)
    /// лежит в этом же канале, а её положение от энергии линии не зависит — на
    /// полпути между узлами сетки (шаг 4.71 %) она уезжает на ±2.4 %, и в образе
    /// линии становится ДВУМЯ линиями.
    ///
    /// (`AMBER52`, П125 22.09.2026, формат 10) Линия и всё содержимое постоянной
    /// энергии (511, 1022, их комптон) переехали в СВОЙ канал № 6
    /// (`AnnihilationOutside`), переносимый сдвигом ноль; прибитая точка П122 снята.
    /// Проба получила ключ `--channel=N` (умолчание 1 — комптон, как у П122; 6 — новый
    /// канал), описание особенности ещё и в ПОЛНОМ образе (всеми каналами — то, что
    /// видит разбор) и режим `--compare=` — контроль неизменности между двумя
    /// матрицами одного зерна: побитово каналы 0…5 у узлов ниже порога пар, Σ каналов
    /// каждого узла (в шуме float32), с какого узла непуст канал № 6.
    ///
    ///     comptonmapprobe --matrix=&lt;файл.rmx&gt; [--e=1460.8] [--line=511] [--win=40]
    ///                     [--channel=1] [--out=&lt;префикс&gt;]
    ///     comptonmapprobe --matrix=&lt;файл.rmx&gt; --loo=&lt;от кэВ&gt; [--line=511] [--channel=1]
    ///     comptonmapprobe --matrix=&lt;файл.rmx&gt; --scan [--line=511] [--channel=1]
    ///     comptonmapprobe --matrix=&lt;было.rmx&gt; --compare=&lt;стало.rmx&gt;
    ///
    /// `--scan` — линия `--line` в СОБСТВЕННЫХ строках узлов: сумма трёх бинов у
    /// линии над континуумом соседей и её доля от Σ строки по всем узлам — с какой
    /// энергии линия вообще видна (порог прибитой точки берётся отсюда, а не головой).
    ///
    /// `--loo=` — ВЫБРОС УЗЛА (приёмка карты переноса): строка узла k переносится
    /// картой канала `compton` на энергию соседнего узла и сравнивается с его
    /// собственной строкой (независимый розыгрыш); печатается относительное
    /// ср.-кв. расхождение по континууму и в окне линии `--line`. Разница ДО/ПОСЛЕ
    /// правки карты — цена карты; сама величина содержит и шум двух розыгрышей.
    ///
    /// Матрица читается С ПУТИ (`ResponseMatrix.Load(path)`), склад не трогается.
    /// Печатается: узлы сетки вокруг линии и веса переноса; для каждого узла — куда
    /// карта уводит `--line` (вызов закрытого `ComptonKnots` отражением, тем же
    /// правилом, что у `Transfer`); особенность канала `compton` в окне
    /// `--line ± win` у обоих узлов и у ПЕРЕНЕСЁННОГО образа (`AccumulateChannel`,
    /// канал 1): площадь над линейной подложкой, центр тяжести и ср.-кв. ширина.
    /// В `&lt;out&gt;.csv` — выписка окна у узлов и образа (для ДО/ПОСЛЕ).
    ///
    /// Контроль неизменности: образ на энергии, РАВНОЙ узлу сетки, — тождество
    /// (`lineEnergy == nodeEnergy` — отдельная ветвь `Transfer`), печатается его
    /// контрольная сумма по битам.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string matrixPath = null;
            string outPrefix = null;
            double lineKev = 1460.8;
            double feature = 511.0;
            double win = 40.0;
            double looFrom = 0.0;
            bool scan = false;
            int channel = 1;
            string comparePath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a == "--scan") scan = true;
                else if (a.StartsWith("--loo=", StringComparison.Ordinal)) looFrom = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPrefix = a.Substring(6);
                else if (a.StartsWith("--e=", StringComparison.Ordinal)) lineKev = double.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--line=", StringComparison.Ordinal)) feature = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--win=", StringComparison.Ordinal)) win = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--channel=", StringComparison.Ordinal)) channel = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--compare=", StringComparison.Ordinal)) comparePath = a.Substring(10);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (matrixPath == null || !File.Exists(matrixPath))
            {
                Console.Error.WriteLine("нужен --matrix=<файл .rmx>");
                return 2;
            }

            ResponseMatrix matrix = ResponseMatrix.Load(matrixPath);
            if (matrix == null || !matrix.HasChannels)
            {
                Console.Error.WriteLine("⛔ матрица не прочиталась или без каналов");
                return 1;
            }

            matrix.TransferByChannel = true;
            double bin = matrix.BinKev;
            Console.WriteLine("матрица: {0}", Path.GetFileName(matrixPath));
            Console.WriteLine("клеймо : {0}", matrix.Stamp);
            Console.WriteLine("узлов {0}, каналов {1}, бин {2} кэВ; PhysicsVersion кода = {3}, FormatVersion кода = {4}, ResponseChannelCount = {5}",
                              matrix.Energies.Length, matrix.ChannelRows.Length, F(bin, 2), ResponseMatrix.PhysicsVersion,
                              ResponseMatrix.FormatVersion, EfficiencySimulator.ResponseChannelCount);

            if (comparePath != null)
            {
                return Compare(matrix, comparePath);
            }

            if (channel < 0 || channel >= matrix.ChannelRows.Length)
            {
                Console.Error.WriteLine("⛔ канала {0} у матрицы нет (каналов {1})", channel, matrix.ChannelRows.Length);
                return 1;
            }

            Console.WriteLine("канал  : {0} ({1})", channel, (EfficiencySimulator.ResponseChannel)channel);

            if (scan)
            {
                // Линия постоянной энергии в СОБСТВЕННЫХ строках узлов: пик над
                // соседями в окне бина `feature` (±1 бин, свет сдвигает её на бин
                // вверх) против среднего континуума по ±5…±10 бинов — где линия
                // вообще видна и с какой энергии её стоит прибивать.
                Console.WriteLine();
                Console.WriteLine("{0,10} {1,12} {2,12} {3,10} {4,10}", "узел", "лин+конт", "континуум", "лин/конт", "лин/Σстр");
                for (int i = 0; i < matrix.Energies.Length; i++)
                {
                    float[] row = matrix.ChannelRows[channel][i];
                    int fb = (int)Math.Round(feature / bin);
                    if (row == null || row.Length <= fb + 12) continue;
                    double[] r = new double[row.Length];
                    for (int b = 0; b < row.Length; b++) r[b] = row[b];
                    double core = r[fb - 1] + r[fb] + r[fb + 1];
                    double cont = 0.5 * (Mean(r, fb - 10, fb - 3) + Mean(r, fb + 3, fb + 10));
                    double line = core - 3.0 * cont;
                    double total = 0.0;
                    for (int c = 0; c < matrix.ChannelRows.Length; c++)
                    {
                        float[] cr = matrix.ChannelRows[c][i];
                        if (cr != null) for (int b = 0; b < cr.Length; b++) total += cr[b];
                    }

                    Console.WriteLine("{0,10} {1,12} {2,12} {3,10} {4,10}", F(matrix.Energies[i], 1), E(core), E(3.0 * cont),
                                      F(cont > 0.0 ? line / (3.0 * cont) : double.NaN, 3), F(total > 0.0 ? 100.0 * line / total : double.NaN, 4) + "%");
                }

                return 0;
            }

            if (looFrom > 0.0)
            {
                return LeaveOneOut(matrix, looFrom, feature, channel);
            }

            double[] grid = matrix.Energies;
            int hi = Array.BinarySearch(grid, lineKev);
            if (hi >= 0)
            {
                Console.WriteLine("линия {0} СОВПАДАЕТ с узлом № {1}", F(lineKev, 3), hi);
                return 0;
            }

            hi = ~hi;
            if (hi <= 0 || hi >= grid.Length)
            {
                Console.WriteLine("линия вне сетки");
                return 1;
            }

            int lo = hi - 1;
            double t = (lineKev - grid[lo]) / (grid[hi] - grid[lo]);
            Console.WriteLine("линия {0} кэВ между узлами {1} (№ {2}, вес {3}) и {4} (№ {5}, вес {6})",
                              F(lineKev, 3), F(grid[lo], 4), lo, F(1.0 - t, 4), F(grid[hi], 4), hi, F(t, 4));

            MethodInfo knots = typeof(ResponseMatrix).GetMethod("ComptonKnots", BindingFlags.NonPublic | BindingFlags.Static);
            if (knots == null)
            {
                Console.Error.WriteLine("⛔ ComptonKnots не найден отражением — проба устарела");
                return 1;
            }

            var rows = new List<string>();
            rows.Add("arm;node_kev;bin;dep_kev;compton");
            foreach (int idx in new[] { lo, hi })
            {
                object[] p = { grid[idx], lineKev, bin, null, null };
                int count = (int)knots.Invoke(null, p);
                double[] source = (double[])p[3];
                double[] image = (double[])p[4];
                var sb = new StringBuilder();
                for (int k = 0; k < count; k++)
                {
                    sb.Append(k > 0 ? ", " : "").Append(F(source[k], 1)).Append("→").Append(F(image[k], 1));
                }

                double mapped = MapThrough(source, image, count, feature);
                Console.WriteLine("узел {0}: карта {1}; {2} кэВ → {3} кэВ (сдвиг {4} кэВ, {5} %)",
                                  F(grid[idx], 4), sb, F(feature, 1), F(mapped, 2), F(mapped - feature, 2),
                                  F(100.0 * (mapped - feature) / feature, 3));

                float[] row = matrix.ChannelRows[channel][idx];
                double[] asDouble = new double[row.Length];
                for (int b = 0; b < row.Length; b++) asDouble[b] = row[b];
                Describe("узел " + F(grid[idx], 4), asDouble, bin, feature, win, matrix, idx);
                Dump(rows, "node_" + F(grid[idx], 1), grid[idx], asDouble, bin, feature, win);
            }

            int length = (int)(lineKev / bin + 0.5) + 1;
            double[] img = new double[length];
            matrix.AccumulateChannel(img, lineKev, 1.0, channel);
            Describe("образ " + F(lineKev, 3), img, bin, feature, win, matrix, -1);
            Dump(rows, "image_" + F(lineKev, 1), lineKev, img, bin, feature, win);

            // Контроль неизменности: образ НА узле — тождество, контрольная сумма по битам.
            double[] same = new double[(int)(grid[lo] / bin + 0.5) + 1];
            matrix.AccumulateChannel(same, grid[lo], 1.0, channel);
            Console.WriteLine("контроль: образ на узле {0} (тождество) — fnv {1}", F(grid[lo], 4), Fnv(same));
            // И полный образ линии всеми каналами — тоже контрольной суммой, для ДО/ПОСЛЕ;
            // (`AMBER52`, П125) и описание особенности В НЁМ — это то, что видит разбор,
            // в каком бы канале она ни лежала.
            double[] full = new double[length];
            matrix.Accumulate(full, lineKev, 1.0);
            Console.WriteLine("образ {0} всеми каналами: Σ {1}, fnv {2}", F(lineKev, 3), E(Sum(full)), Fnv(full));
            Describe("ПОЛНЫЙ образ " + F(lineKev, 3), full, bin, feature, win, matrix, -1);
            Dump(rows, "full_" + F(lineKev, 1), lineKev, full, bin, feature, win);

            if (outPrefix != null)
            {
                File.WriteAllLines(outPrefix + ".csv", rows, new UTF8Encoding(false));
                Console.WriteLine("записано: {0}.csv ({1} строк)", outPrefix, rows.Count - 1);
            }

            return 0;
        }

        /// <summary>
        /// ВЫБРОС УЗЛА: строка узла k переносится картой канала `compton` на энергию
        /// узла k+1 (и обратно) закрытым `Transfer` и сравнивается с НАСТОЯЩЕЙ строкой
        /// того узла — независимым розыгрышем. Печатается относительное ср.-кв.
        /// расхождение `√Σ(перенос − узел)² / √Σ узел²` по континууму (от 0.3·E до
        /// края комптона − 20 кэВ, без окна линии) и отдельно в окне линии
        /// `feature ± 20`. Разница ДО/ПОСЛЕ правки карты — цена карты; сама величина
        /// содержит и шум двух розыгрышей.
        ///
        /// (`AMBER52`, П125) Вторая пара столбцов — те же меры ПОСЛЕ НОРМИРОВКИ
        /// перенесённой строки на Σ собственной (`rms_cont_n`, `rms_line_n`): у канала
        /// постоянной энергии перенос — сдвиг ноль, амплитуду в образе даёт
        /// интерполяция между узлами (`Accumulate`), а выброс узла переносит строку
        /// соседа с ЕГО амплитудой — ненормированная мера там меряет рост выхода пар от
        /// узла к узлу, а не форму. Для карты комптона нормировка почти ничего не
        /// меняет (Σ строки соседей близки).
        /// </summary>
        static int LeaveOneOut(ResponseMatrix matrix, double from, double feature, int channel)
        {
            MethodInfo transfer = typeof(ResponseMatrix).GetMethod("Transfer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (transfer == null)
            {
                Console.Error.WriteLine("⛔ Transfer не найден отражением — проба устарела");
                return 1;
            }

            double bin = matrix.BinKev;
            double[] grid = matrix.Energies;
            Console.WriteLine();
            Console.WriteLine("выброс узла: перенос канала {0} ({1}) соседнего узла на энергию узла против его собственной строки",
                              channel, (EfficiencySimulator.ResponseChannel)channel);
            // (`AMBER52`, П125) У канала постоянной энергии свой континуум — комптон квантов
            // 511 ниже их края 340.7 кэВ; окно континуума для него [100, 330] кэВ, а не
            // [0.3·E, edge − 20] комптона первичного кванта.
            bool constant = channel == (int)EfficiencySimulator.ResponseChannel.AnnihilationOutside;
            Console.WriteLine("{0,10} {1,10} {2,4} {3,12} {4,12} {5,10} {6,10} {7,12} {8,12} {9,8}",
                              "с узла", "на узел", "дир", "rms_cont", "rms_line", "c_line_tr", "c_line_own",
                              "rms_cont_n", "rms_line_n", "Σtr/Σown");
            double sumCont = 0.0, sumLine = 0.0, sumContN = 0.0, sumLineN = 0.0; int pairs = 0;
            for (int k = 0; k + 1 < grid.Length; k++)
            {
                if (grid[k] < from) continue;
                foreach (bool up in new[] { true, false })
                {
                    int src = up ? k : k + 1, dst = up ? k + 1 : k;
                    float[] own = matrix.ChannelRows[channel][dst];
                    float[] rowSrc = matrix.ChannelRows[channel][src];
                    if (own == null || rowSrc == null || own.Length == 0 || rowSrc.Length == 0) continue;
                    double[] target = new double[own.Length + 4];
                    transfer.Invoke(matrix, new object[] { rowSrc, grid[src], grid[dst], 1.0, target, 0.0, channel, 1.0 });
                    double e = grid[dst];
                    double edge = e * (2.0 * e / 510.99895) / (1.0 + 2.0 * e / 510.99895);
                    int b0 = (int)Math.Round(0.3 * e / bin), b1 = (int)Math.Round((edge - 20.0) / bin);
                    if (constant) { b0 = (int)Math.Round(100.0 / bin); b1 = (int)Math.Round(330.0 / bin); }
                    int l0 = (int)Math.Round((feature - 20.0) / bin), l1 = (int)Math.Round((feature + 20.0) / bin);
                    double d2 = 0.0, o2 = 0.0, d2l = 0.0, o2l = 0.0, d2n = 0.0, d2ln = 0.0;
                    double m1t = 0.0, w1t = 0.0, m1o = 0.0, w1o = 0.0;
                    double sumT = 0.0, sumO = 0.0;
                    for (int b = 0; b < own.Length; b++) { sumT += target[b]; sumO += own[b]; }
                    double norm = sumT > 0.0 ? sumO / sumT : 1.0;
                    double baseT = 0.5 * (Mean(target, l0 - 10, l0 - 1) + Mean(target, l1 + 1, l1 + 10));
                    double baseO = 0.5 * (MeanF(own, l0 - 10, l0 - 1) + MeanF(own, l1 + 1, l1 + 10));
                    for (int b = 0; b < own.Length; b++)
                    {
                        double d = target[b] - own[b];
                        double dn = target[b] * norm - own[b];
                        if (b >= l0 && b <= l1)
                        {
                            d2l += d * d; o2l += own[b] * (double)own[b]; d2ln += dn * dn;
                            double et = target[b] - baseT, eo = own[b] - baseO;
                            if (et > 0.0) { m1t += et * b * bin; w1t += et; }
                            if (eo > 0.0) { m1o += eo * b * bin; w1o += eo; }
                        }
                        else if (b >= b0 && b <= b1)
                        {
                            d2 += d * d; o2 += own[b] * (double)own[b]; d2n += dn * dn;
                        }
                    }

                    double rmsCont = o2 > 0.0 ? Math.Sqrt(d2 / o2) : double.NaN;
                    double rmsLine = o2l > 0.0 ? Math.Sqrt(d2l / o2l) : double.NaN;
                    double rmsContN = o2 > 0.0 ? Math.Sqrt(d2n / o2) : double.NaN;
                    double rmsLineN = o2l > 0.0 ? Math.Sqrt(d2ln / o2l) : double.NaN;
                    Console.WriteLine("{0,10} {1,10} {2,4} {3,12} {4,12} {5,10} {6,10} {7,12} {8,12} {9,8}",
                                      F(grid[src], 1), F(grid[dst], 1), up ? "↑" : "↓", F(rmsCont, 5), F(rmsLine, 5),
                                      w1t > 0.0 ? F(m1t / w1t, 1) : "—", w1o > 0.0 ? F(m1o / w1o, 1) : "—",
                                      F(rmsContN, 5), F(rmsLineN, 5), F(sumO > 0.0 ? sumT / sumO : double.NaN, 3));
                    if (!double.IsNaN(rmsCont))
                    {
                        sumCont += rmsCont * rmsCont; sumLine += rmsLine * rmsLine; pairs++;
                        sumContN += rmsContN * rmsContN; if (!double.IsNaN(rmsLineN)) sumLineN += rmsLineN * rmsLineN;
                    }
                }
            }

            if (pairs > 0)
            {
                Console.WriteLine("итог по {0} переносам: rms_cont {1}, rms_line {2} (корень из среднего квадрата); "
                                  + "с нормировкой амплитуды: rms_cont_n {3}, rms_line_n {4}",
                                  pairs, F(Math.Sqrt(sumCont / pairs), 5), F(Math.Sqrt(sumLine / pairs), 5),
                                  F(Math.Sqrt(sumContN / pairs), 5), F(Math.Sqrt(sumLineN / pairs), 5));
            }

            return 0;
        }

        /// <summary>
        /// (`AMBER52`, П125 22.09.2026) КОНТРОЛЬ НЕИЗМЕННОСТИ между двумя матрицами ОДНОГО
        /// зерна и рецепта, посчитанными до и после переезда содержимого постоянной энергии
        /// в свой канал (формат 9 → 10). По каждому узлу: каналы 0…5 — число бинов, где
        /// значения различаются побитово (float32); Σ каналов по бинам — наибольшее
        /// относительное расхождение и число бинов с расхождением; канал № 6 (если есть) —
        /// его Σ и доля от Σ строки. Итог: узлы ниже порога пар (1022 кэВ) ОБЯЗАНЫ совпасть
        /// побитово по всем каналам (там канал № 6 пуст по построению); у узлов выше
        /// каналы 0 и 2…5 побитово, канал 1 отличается ровно на содержимое канала № 6,
        /// Σ каналов — в шуме округления float32 (иная группировка слагаемых).
        /// Вторая матрица читается `Load(…, PreviousFormatVersion)` — то есть годится и
        /// файл прежнего формата.
        /// </summary>
        static int Compare(ResponseMatrix a, string otherPath)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix b = ResponseMatrix.Load(otherPath, out refusal, out fileFormat, ResponseMatrix.PreviousFormatVersion);
            if (b == null)
            {
                Console.Error.WriteLine("⛔ вторая матрица не прочиталась: {0} (формат файла {1})", refusal, fileFormat);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("сравнение: A = {0} (каналов {1}), B = {2} (каналов {3}, формат файла {4})",
                              a.Stamp, a.ChannelRows.Length, Path.GetFileName(otherPath), b.ChannelRows.Length, fileFormat);
            if (a.Energies.Length != b.Energies.Length)
            {
                Console.Error.WriteLine("⛔ узлов разное число: {0} против {1}", a.Energies.Length, b.Energies.Length);
                return 1;
            }

            int common = Math.Min(a.ChannelRows.Length, b.ChannelRows.Length);
            int extra = Math.Max(a.ChannelRows.Length, b.ChannelRows.Length) - 1;
            ResponseMatrix wide = a.ChannelRows.Length >= b.ChannelRows.Length ? a : b;
            Console.WriteLine("{0,10} {1,6} {2,6} {3,6} {4,6} {5,6} {6,6} {7,12} {8,6} {9,12} {10,9}",
                              "узел", "Δc0", "Δc1", "Δc2", "Δc3", "Δc4", "Δc5", "maxΔΣ/Σ", "nΔΣ", "Σ(c6)", "c6/Σстр");
            int belowBitwise = 0, belowTotal = 0, aboveBitwiseOthers = 0, aboveTotal = 0;
            double worstSum = 0.0; int worstNode = -1; double firstExtra = double.NaN;
            for (int i = 0; i < a.Energies.Length; i++)
            {
                if (Math.Abs(a.Energies[i] - b.Energies[i]) > 1e-9)
                {
                    Console.Error.WriteLine("⛔ сетки разные у узла {0}: {1} против {2}", i, a.Energies[i], b.Energies[i]);
                    return 1;
                }

                int[] diff = new int[Math.Max(common, 6)];
                int len = 0;
                for (int c = 0; c < common; c++)
                {
                    float[] ra = a.ChannelRows[c][i] ?? new float[0], rb = b.ChannelRows[c][i] ?? new float[0];
                    int m = Math.Max(ra.Length, rb.Length);
                    len = Math.Max(len, m);
                    for (int k = 0; k < m; k++)
                    {
                        float va = k < ra.Length ? ra[k] : 0f, vb = k < rb.Length ? rb[k] : 0f;
                        if (BitConverter.ToInt32(BitConverter.GetBytes(va), 0) != BitConverter.ToInt32(BitConverter.GetBytes(vb), 0)) diff[c]++;
                    }
                }

                // Σ каналов по бинам — в double, порядок каналов по номеру у обеих.
                double maxRel = 0.0; int nRel = 0; double sumA = 0.0, extraSum = 0.0;
                for (int c = 0; c < a.ChannelRows.Length; c++) { var r = a.ChannelRows[c][i]; if (r != null) len = Math.Max(len, r.Length); }
                for (int c = 0; c < b.ChannelRows.Length; c++) { var r = b.ChannelRows[c][i]; if (r != null) len = Math.Max(len, r.Length); }
                for (int k = 0; k < len; k++)
                {
                    double sa = 0.0, sb = 0.0;
                    for (int c = 0; c < a.ChannelRows.Length; c++) { var r = a.ChannelRows[c][i]; if (r != null && k < r.Length) sa += r[k]; }
                    for (int c = 0; c < b.ChannelRows.Length; c++) { var r = b.ChannelRows[c][i]; if (r != null && k < r.Length) sb += r[k]; }
                    sumA += sa;
                    if (sa != sb)
                    {
                        nRel++;
                        double rel = Math.Abs(sa - sb) / Math.Max(Math.Abs(sa), Math.Abs(sb));
                        if (rel > maxRel) maxRel = rel;
                    }
                }

                if (extra >= common)
                {
                    float[] re = wide.ChannelRows[extra][i];
                    if (re != null) for (int k = 0; k < re.Length; k++) extraSum += re[k];
                    if (extraSum > 0.0 && double.IsNaN(firstExtra)) firstExtra = a.Energies[i];
                }

                bool below = a.Energies[i] < 2.0 * 510.99895;
                bool allBitwise = true, othersBitwise = true;
                for (int c = 0; c < common; c++) { if (diff[c] != 0) { allBitwise = false; if (c != 1) othersBitwise = false; } }
                if (below) { belowTotal++; if (allBitwise && nRel == 0) belowBitwise++; }
                else { aboveTotal++; if (othersBitwise) aboveBitwiseOthers++; }
                if (maxRel > worstSum) { worstSum = maxRel; worstNode = i; }

                Console.WriteLine("{0,10} {1,6} {2,6} {3,6} {4,6} {5,6} {6,6} {7,12} {8,6} {9,12} {10,9}",
                                  F(a.Energies[i], 1), diff[0], diff[1], diff[2], diff[3], diff[4], diff[5],
                                  maxRel > 0.0 ? maxRel.ToString("E2", CultureInfo.InvariantCulture) : "0", nRel,
                                  extra >= common ? E(extraSum) : "—",
                                  extra >= common && sumA > 0.0 ? F(100.0 * extraSum / sumA, 4) + "%" : "—");
            }

            Console.WriteLine();
            Console.WriteLine("итог: узлов ниже 1022 кэВ {0}, из них ПОБИТОВО по всем каналам и Σ — {1}; узлов выше {2}, из них каналы 0 и 2…5 побитово — {3}",
                              belowTotal, belowBitwise, aboveTotal, aboveBitwiseOthers);
            Console.WriteLine("      наибольшее относительное расхождение Σ каналов по бину: {0} (узел {1})",
                              worstSum.ToString("E3", CultureInfo.InvariantCulture), worstNode >= 0 ? F(a.Energies[worstNode], 1) : "—");
            Console.WriteLine("      канал № {0} непуст с узла {1} кэВ", extra, double.IsNaN(firstExtra) ? "—" : F(firstExtra, 1));
            return belowBitwise == belowTotal && aboveBitwiseOthers == aboveTotal ? 0 : 1;
        }

        static double MeanF(float[] row, int from, int to)
        {
            double s = 0.0; int n = 0;
            for (int b = Math.Max(0, from); b <= to && b < row.Length; b++) { s += row[b]; n++; }
            return n > 0 ? s / n : 0.0;
        }

        static double MapThrough(double[] source, double[] image, int count, double x)
        {
            int segment = 0;
            while (segment < count - 2 && x >= source[segment + 1]) segment++;
            double slope = (image[segment + 1] - image[segment]) / (source[segment + 1] - source[segment]);
            return image[segment] + (x - source[segment]) * slope;
        }

        /// <summary>
        /// Особенность в окне `feature ± win`: подложка — прямая через средние
        /// значений в полосах `[−2win, −win]` и `[+win, +2win]`; над ней — площадь,
        /// центр тяжести и ср.-кв. ширина. Печатается и доля площади от Σ строки.
        /// </summary>
        static void Describe(string name, double[] row, double bin, double feature, double win, ResponseMatrix matrix, int node)
        {
            int b0 = (int)Math.Round((feature - win) / bin), b1 = (int)Math.Round((feature + win) / bin);
            int l0 = (int)Math.Round((feature - 2 * win) / bin), r1 = (int)Math.Round((feature + 2 * win) / bin);
            double leftMean = Mean(row, l0, b0 - 1), rightMean = Mean(row, b1 + 1, r1);
            double leftX = 0.5 * (l0 + b0 - 1) * bin, rightX = 0.5 * (b1 + 1 + r1) * bin;
            double slope = (rightMean - leftMean) / (rightX - leftX);
            double area = 0.0, m1 = 0.0, m2 = 0.0, peakVal = 0.0; int peakBin = -1;
            for (int b = b0; b <= b1 && b < row.Length; b++)
            {
                double x = b * bin;
                double excess = row[b] - (leftMean + slope * (x - leftX));
                if (row[b] > peakVal) { peakVal = row[b]; peakBin = b; }
                if (excess <= 0.0) continue;
                area += excess; m1 += excess * x; m2 += excess * x * x;
            }

            double centroid = area > 0.0 ? m1 / area : double.NaN;
            double rms = area > 0.0 ? Math.Sqrt(Math.Max(0.0, m2 / area - centroid * centroid)) : double.NaN;
            double total = Sum(row);
            Console.WriteLine("  {0}: строка в окне [{1}, {2}] кэВ — над подложкой площадь {3} ({4} % Σ строки), "
                              + "центр {5} кэВ, ср.-кв. ширина {6} кэВ; максимум бин {7} ({8} кэВ) = {9}",
                              name, F(feature - win, 0), F(feature + win, 0), E(area), F(100.0 * area / total, 3),
                              F(centroid, 2), F(rms, 2), peakBin, F(peakBin * bin, 0), E(peakVal));
        }

        static void Dump(List<string> rows, string arm, double nodeKev, double[] row, double bin, double feature, double win)
        {
            int b0 = (int)Math.Round((feature - 2 * win) / bin), b1 = (int)Math.Round((feature + 2 * win) / bin);
            for (int b = b0; b <= b1 && b < row.Length; b++)
            {
                rows.Add(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4}", arm,
                                       nodeKev.ToString("R", CultureInfo.InvariantCulture), b, F(b * bin, 1),
                                       row[b].ToString("R", CultureInfo.InvariantCulture)));
            }
        }

        static double Mean(double[] row, int from, int to)
        {
            double s = 0.0; int n = 0;
            for (int b = Math.Max(0, from); b <= to && b < row.Length; b++) { s += row[b]; n++; }
            return n > 0 ? s / n : 0.0;
        }

        static double Sum(double[] row)
        {
            double s = 0.0;
            for (int b = 0; b < row.Length; b++) s += row[b];
            return s;
        }

        static string Fnv(double[] row)
        {
            ulong fnv = 14695981039346656037UL;
            for (int i = 0; i < row.Length; i++)
            {
                ulong bits = (ulong)BitConverter.DoubleToInt64Bits(row[i]);
                for (int b = 0; b < 8; b++) { fnv ^= (bits >> (8 * b)) & 0xFF; fnv *= 1099511628211UL; }
            }

            return fnv.ToString("x16");
        }

        // ⛔ Разделитель дробной части — ТОЧКА, культурой инвариантной и явной
        // (правило Amber 05.09.2026).
        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static string E(double value)
        {
            return value.ToString("E6", CultureInfo.InvariantCulture);
        }
    }
}
