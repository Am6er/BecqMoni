using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// ПЕРЕНОС СТРОКИ МАТРИЦЫ МЕЖДУ УЗЛАМИ: ОБЩИЙ МАСШТАБ ПРОТИВ ПЕРЕНОСА ПО
/// КАНАЛАМ (`AMBER16` п. 4, полоса П8 11.09.2026).
///
/// Одна и та же складская матрица читается на одну и ту же энергию линии
/// двумя правилами — прежним общим масштабом `E/E_узла`
/// (<see cref="ResponseMatrix.TransferByChannel"/> = false) и переносом по
/// каналам (= true) — и обе выписки кладутся в csv того же вида, что у
/// `ResponseRowDumpProbe` (`arm;line_kev;node_kev;bin;dep_kev;каналы…;total`),
/// чтобы сравнивать их с ЕЁ прямым плечом (`direct_*` — узел, посчитанный
/// заново ровно на энергии линии) одним и тем же читателем.
///
/// Плечи:
///   * `interp_scale`   — перенос общим масштабом, то, что получает разбор в поставке;
///   * `interp_channel` — перенос по каналам, то, что получит разбор под ключом;
///   * `node_row`       — строка узла как есть (по `--node=`), и рядом
///     `node_scale` / `node_channel` — та же строка, «перенесённая» на СВОЮ же
///     энергию. Это ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: перенос с множителем 1 — тождество,
///     и оба правила обязаны вернуть строку побитово; проба сверяет сама и
///     отказывает кодом 1, если нет.
///
/// Заодно сверяется площадь: Σ каждого канала после переноса обязана равняться
/// Σ строки узла (перенос сохраняет площадь, края зажимаются) — расхождение
/// больше 1e−9 относительных тоже отказ.
///
///     responsetransferprobe --matrix=&lt;файл.rmx&gt; [--e=32.194,661.657]
///                           [--node=106,107] [--out=&lt;префикс&gt;]
///
/// ⚠ Своих вызовов симулятора нет и не будет: прямое плечо считает
/// `ResponseRowDumpProbe --direct`, а вторая копия настройки разошлась бы
/// с первой молча (`S37`).
/// </summary>
static class ResponseTransferProbe
{
    // Имена каналов — как в `ResponseRowDumpProbe`, чтобы столбцы совпали; шестой
    // (`esc_xray_l`, `AMBER16` п. 1) дописан в конец и у складских матриц пуст;
    // седьмой (`ann_out`, `AMBER52`, формат 10) — аннигиляция вне кристалла,
    // переносится сдвигом ноль.
    static readonly string[] ChannelNames =
    {
        "peak", "compton", "esc_se", "esc_xray", "esc_de", "esc_xray_l", "ann_out"
    };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string matrixPath = null;
        string outPrefix = "transfer";
        double[] energies = { 32.194, 661.657 };
        var nodes = new List<int>();
        foreach (string a in args)
        {
            if (a.StartsWith("--matrix=", StringComparison.Ordinal))
            {
                matrixPath = a.Substring(9);
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
            else if (a.StartsWith("--node=", StringComparison.Ordinal))
            {
                foreach (string part in a.Substring(7).Split(','))
                {
                    nodes.Add(int.Parse(part.Trim(), CultureInfo.InvariantCulture));
                }
            }
            else
            {
                Console.Error.WriteLine("неизвестный ключ: {0}", a);
                return 2;
            }
        }

        if (matrixPath == null)
        {
            Console.Error.WriteLine("нужен --matrix=<файл.rmx>");
            return 2;
        }

        MatrixRefusal refusal;
        int fileFormat;
        ResponseMatrix matrix = ResponseMatrix.Load(matrixPath, out refusal, out fileFormat);
        if (matrix == null)
        {
            Console.Error.WriteLine("⛔ матрица не прочиталась: {0}, формат файла {1}", refusal, fileFormat);
            return 1;
        }

        if (!matrix.HasChannels)
        {
            Console.Error.WriteLine("⛔ у матрицы нет раскладки по каналам — переносить по каналам нечего");
            return 1;
        }

        Console.WriteLine("матрица: {0}", Path.GetFileName(matrixPath));
        Console.WriteLine("узлов {0}, каналов {1}, бин {2} кэВ, историй на узел {3}",
                          matrix.Energies.Length, matrix.ChannelRows.Length, F(matrix.BinKev, 2),
                          matrix.Histories);
        Console.WriteLine("клеймо : {0}", matrix.Stamp);

        var rows = new List<string>();
        rows.Add("arm;line_kev;node_kev;bin;dep_kev;" + string.Join(";", ChannelNames) + ";total");

        bool ok = true;
        foreach (double e in energies)
        {
            Console.WriteLine();
            Console.WriteLine("--- линия {0} кэВ ---", F(e, 3));
            Neighbours(matrix, e);
            ok &= Dump(rows, "interp_scale", matrix, e, false, null);
            ok &= Dump(rows, "interp_channel", matrix, e, true, null);
        }

        foreach (int index in nodes)
        {
            if (index < 0 || index >= matrix.Energies.Length)
            {
                Console.Error.WriteLine("⛔ узла № {0} нет: узлов {1}", index, matrix.Energies.Length);
                return 2;
            }

            double e = matrix.Energies[index];
            Console.WriteLine();
            Console.WriteLine("--- положительный контроль: узел № {0}, {1} кэВ, перенос на СЕБЯ ---",
                              index, F(e, 6));
            double[][] raw = RawRows(matrix, index);
            EmitRaw(rows, "node_row", matrix, index, raw);
            ok &= Dump(rows, "node_scale", matrix, e, false, raw);
            ok &= Dump(rows, "node_channel", matrix, e, true, raw);
        }

        string csv = outPrefix + ".csv";
        File.WriteAllLines(csv, rows, new UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine("записано: {0} ({1} строк)", csv, rows.Count - 1);
        Console.WriteLine(ok ? "ИТОГ: площади сохранены, тождество на узле сошлось"
                             : "⛔ ИТОГ: есть расхождения — см. выше");
        return ok ? 0 : 1;
    }

    /// <summary>Соседние узлы и веса — по тому же правилу, что в `Accumulate`.</summary>
    static void Neighbours(ResponseMatrix matrix, double energyKev)
    {
        double[] grid = matrix.Energies;
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
        double t = (energyKev - grid[lo]) / (grid[hi] - grid[lo]);
        Console.WriteLine("узлы сетки: {0} (№ {1}) и {2} (№ {3}), вес верхнего t = {4}",
                          F(grid[lo], 4), lo, F(grid[hi], 4), hi, F(t, 5));
        foreach (int index in new[] { lo, hi })
        {
            double node = grid[index];
            Console.WriteLine("  узел {0}: масштаб E/E_узла = {1}; край комптона {2} -> масштабом {3}, "
                              + "своим правилом {4}; обратное рассеяние {5} -> масштабом {6}, своим {7}",
                              F(node, 4), F(energyKev / node, 5),
                              F(Edge(node), 2), F(Edge(node) * energyKev / node, 2), F(Edge(energyKev), 2),
                              F(Back(node), 2), F(Back(node) * energyKev / node, 2), F(Back(energyKev), 2));
        }
    }

    // Формулы края и обратного рассеяния — для ПЕЧАТИ ожидаемого положения;
    // судит проба не по ним, а по самой выписке (край ищется читателем по форме).
    static double Edge(double e)
    {
        double twoAlpha = 2.0 * e / 510.99895;
        return e * twoAlpha / (1.0 + twoAlpha);
    }

    static double Back(double e)
    {
        return e / (1.0 + 2.0 * e / 510.99895);
    }

    static double[][] RawRows(ResponseMatrix matrix, int index)
    {
        int channels = matrix.ChannelRows.Length;
        double[][] raw = new double[channels][];
        for (int c = 0; c < channels; c++)
        {
            float[] row = matrix.ChannelRows[c][index];
            raw[c] = new double[row.Length];
            for (int b = 0; b < row.Length; b++)
            {
                raw[c][b] = row[b];
            }
        }

        return raw;
    }

    static void EmitRaw(List<string> rows, string arm, ResponseMatrix matrix, int index, double[][] raw)
    {
        int length = 0;
        foreach (double[] row in raw)
        {
            length = Math.Max(length, row.Length);
        }

        double e = matrix.Energies[index];
        for (int b = 0; b < length; b++)
        {
            double[] values = new double[raw.Length];
            double total = 0.0;
            for (int c = 0; c < raw.Length; c++)
            {
                values[c] = b < raw[c].Length ? raw[c][b] : 0.0;
                total += values[c];
            }

            Emit(rows, arm, e, e, b, b * matrix.BinKev, values, total);
        }
    }

    /// <summary>
    /// Перенос на энергию линии выбранным правилом — тем же
    /// `AccumulateChannel`, каким читает разбор. Возвращает false при потере
    /// площади или при расхождении с `expected` (положительный контроль).
    /// </summary>
    static bool Dump(List<string> rows, string arm, ResponseMatrix matrix, double energyKev,
                     bool byChannel, double[][] expected)
    {
        matrix.TransferByChannel = byChannel;
        int channels = matrix.ChannelRows.Length;
        int length = EfficiencySimulator.PeakBin(energyKev, matrix.BinKev) + 1;
        double[][] parts = new double[channels][];
        for (int c = 0; c < channels; c++)
        {
            parts[c] = new double[length];
            matrix.AccumulateChannel(parts[c], energyKev, 1.0, c);
        }

        // Суммарный путь (`Evaluate`, канал −1) обязан дать ровно сумму
        // каналов: под ключом суммарная строка не переносится, а собирается
        // из каналов — здесь это и проверяется. Без ключа сумма каналов и
        // суммарная строка расходятся на округление float, поэтому сверка
        // только у нового правила.
        bool ok = true;
        if (byChannel)
        {
            double[] whole = matrix.Evaluate(energyKev, length);
            int bad = 0;
            for (int b = 0; b < length; b++)
            {
                double sum = 0.0;
                for (int c = 0; c < channels; c++)
                {
                    sum += parts[c][b];
                }

                if (Math.Abs(whole[b] - sum) > 1e-12 * Math.Max(1.0, Math.Abs(sum)))
                {
                    bad++;
                }
            }

            ok &= bad == 0;
            Console.WriteLine("  суммарный путь (Evaluate) против суммы каналов: {0}",
                              bad == 0 ? "сошёлся" : "⛔ РАСХОЖДЕНИЕ в " + bad + " бинах");
        }

        // Ключ возвращается в исходное, чтобы не оставить матрицу в чужом состоянии.
        matrix.TransferByChannel = false;

        Console.WriteLine("  плечо {0}:", arm);
        for (int c = 0; c < channels; c++)
        {
            double got = 0.0;
            double centroid = 0.0;
            foreach (double v in parts[c])
            {
                got += v;
            }

            for (int b = 0; b < length; b++)
            {
                centroid += b * matrix.BinKev * parts[c][b];
            }

            double want = ExpectedArea(matrix, energyKev, c);
            double gap = want > 0.0 ? Math.Abs(got - want) / want : Math.Abs(got - want);
            bool areaOk = gap <= 1e-9;
            ok &= areaOk;
            Console.WriteLine("    {0,-10} Σ {1} (узлы {2}, {3}){4}  ц.т. {5} кэВ",
                              c < ChannelNames.Length ? ChannelNames[c] : "ch" + c,
                              E(got), E(want), areaOk ? "площадь сохранена" : "⛔ ПЛОЩАДЬ ПОТЕРЯНА",
                              areaOk ? "" : " ⛔", got > 0.0 ? F(centroid / got, 3) : "—");
            if (expected != null)
            {
                int mismatches = 0;
                for (int b = 0; b < length; b++)
                {
                    double e = b < expected[c].Length ? expected[c][b] : 0.0;
                    if (parts[c][b] != e)
                    {
                        mismatches++;
                    }
                }

                if (expected[c].Length > length)
                {
                    mismatches += expected[c].Length - length;
                }

                ok &= mismatches == 0;
                Console.WriteLine("      тождество: {0}",
                                  mismatches == 0 ? "СОШЛОСЬ побитово"
                                                  : "⛔ РАСХОЖДЕНИЕ в " + mismatches + " бинах");
            }
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

            Emit(rows, arm, energyKev, energyKev, b, b * matrix.BinKev, values, total);
        }

        return ok;
    }

    /// <summary>
    /// Ожидаемая площадь канала после переноса: смесь Σ строк соседних узлов с
    /// весами `1−t` и `t` — ровно то, что обязан вернуть перенос, сохраняющий
    /// площадь, при любом правиле.
    /// </summary>
    static double ExpectedArea(ResponseMatrix matrix, double energyKev, int channel)
    {
        double[] grid = matrix.Energies;
        int hi = Array.BinarySearch(grid, energyKev);
        if (hi >= 0)
        {
            return Sum(matrix.ChannelRows[channel][hi]);
        }

        hi = ~hi;
        if (hi <= 0)
        {
            return Sum(matrix.ChannelRows[channel][0]);
        }

        if (hi >= grid.Length)
        {
            return Sum(matrix.ChannelRows[channel][grid.Length - 1]);
        }

        int lo = hi - 1;
        double t = (energyKev - grid[lo]) / (grid[hi] - grid[lo]);
        return (1.0 - t) * Sum(matrix.ChannelRows[channel][lo]) + t * Sum(matrix.ChannelRows[channel][hi]);
    }

    static double Sum(float[] row)
    {
        double sum = 0.0;
        if (row != null)
        {
            foreach (float v in row)
            {
                sum += v;
            }
        }

        return sum;
    }

    static void Emit(List<string> rows, string arm, double lineKev, double nodeKev, int bin,
                     double depositKev, double[] values, double total)
    {
        var sb = new StringBuilder();
        sb.Append(arm).Append(';');
        sb.Append(F(lineKev, 3)).Append(';');
        sb.Append(F(nodeKev, 4)).Append(';');
        sb.Append(bin.ToString(CultureInfo.InvariantCulture)).Append(';');
        sb.Append(F(depositKev, 2));
        for (int c = 0; c < ChannelNames.Length; c++)
        {
            sb.Append(';').Append(E(c < values.Length ? values[c] : 0.0));
        }

        sb.Append(';').Append(E(total));
        rows.Add(sb.ToString());
    }

    // ⛔ Разделитель дробной части — ТОЧКА, культурой инвариантной и явной
    // (правило Amber 05.09.2026).
    static string F(double value, int digits)
    {
        return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                              CultureInfo.InvariantCulture);
    }

    static string E(double value)
    {
        return value.ToString("E6", CultureInfo.InvariantCulture);
    }
}
