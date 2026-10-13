using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// ГДЕ СТОИТ ПИК ОБРАЗА ЛИНИИ (`AMBER70`, полоса П135 22.09.2026).
///
/// Приёмник образа (<c>FsaAnalyzer.DepositChannels</c>) читает бин `b` как
/// энергию ровно `b·шаг`, а окно пика ставит вокруг `E`
/// (<c>FsaAnalyzer.LinePositionKev</c> = `E + s(E)`). Значит пик полного
/// поглощения линии `E` в образе ОБЯЗАН стоять на `E`, а не на центре
/// ближайшего бина `round(E/шаг)·шаг`.
///
/// Проба читает складскую матрицу, просит у неё образ ОДНОГО канала
/// <see cref="EfficiencySimulator.ResponseChannel.Peak"/> на каждой из
/// названных линий тем же вызовом, каким читает разбор
/// (<see cref="ResponseMatrix.AccumulateChannel"/> при
/// <see cref="ResponseMatrix.TransferByChannel"/> = true), и меряет ЦЕНТР
/// ТЯЖЕСТИ образа в шкале приёмника (`Σ b·шаг·v / Σ v`). Отказ кодом 1, если
/// центр тяжести хоть одной линии отстоит от её энергии дальше
/// <c>--tol</c> кэВ.
///
/// ⚠ Это ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ правки: на коде до неё проба обязана быть
/// КРАСНОЙ — сдвиг пикового канала целый, и центр тяжести садится на
/// `round(E/шаг)·шаг`.
///
/// Плечи (каждое считается на своей длине образа):
///   * `pad0` — длина `PeakBin(E)+1`, ровно та, что выделяет приёмник
///     (`FsaAnalyzer` 4107/8281/11152 при `lightMarginBins = 0`). У линий
///     ВЫШЕ центра своего бина (`δ > 0`, например 80.997 и 1460.82) верхний
///     сосед пика в эту длину не влезает, и <c>ResponseMatrix.Add</c> зажимает
///     его обратно в последний бин — правка переноса таким линиям не видна,
///     пока приёмник не выделит на бин больше;
///   * `pad1` — та же длина плюс один бин: что даёт правка переноса САМА ПО
///     СЕБЕ, без оглядки на длину приёмника.
///
/// Заодно печатается цена смещения для человека: образ сворачивается с
/// гауссианой ПШПВ детектора (`--fwhm=` процентов на 662 кэВ, `∝ √E`) и
/// сравнивается со свёрткой ИДЕАЛЬНОГО образа — той же площади, поставленной
/// ровно на `E`. Максимум разности в долях высоты пика — это и есть диполь,
/// который человек видит в ленте невязки.
///
///     peakcentroidprobe --matrix=&lt;файл.rmx&gt; [--e=59.54,80.997,511,661.657,1460.82]
///                       [--fwhm=7] [--tol=0.05] [--out=&lt;csv&gt;] [--scale]
///
/// `--scale` добавляет справочное плечо прежнего общего масштаба
/// (<see cref="ResponseMatrix.TransferByChannel"/> = false): у него своя, ещё
/// большая ошибка положения пика, и правка П135 его не касается.
/// </summary>
static class PeakCentroidProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string matrixPath = null;
        string outPath = null;
        string dumpPath = null;
        double[] energies = { 59.5409, 80.9979, 511.0, 661.657, 1173.228, 1460.822 };
        double fwhmPercent = 7.0;
        double tol = 0.05;
        bool withScale = false;
        bool withAnchor = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--matrix=", StringComparison.Ordinal))
            {
                matrixPath = a.Substring(9);
            }
            else if (a.StartsWith("--out=", StringComparison.Ordinal))
            {
                outPath = a.Substring(6);
            }
            else if (a.StartsWith("--dump=", StringComparison.Ordinal))
            {
                dumpPath = a.Substring(7);
            }
            else if (a.StartsWith("--fwhm=", StringComparison.Ordinal))
            {
                fwhmPercent = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            }
            else if (a.StartsWith("--tol=", StringComparison.Ordinal))
            {
                tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
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
            else if (a == "--scale")
            {
                withScale = true;
            }
            else if (a == "--anchor")
            {
                withAnchor = true;
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
            Console.Error.WriteLine("⛔ у матрицы нет раскладки по каналам");
            return 1;
        }

        double h = matrix.BinKev;
        Console.WriteLine("матрица: {0}", Path.GetFileName(matrixPath));
        Console.WriteLine("узлов {0}, бин {1} кэВ, клеймо {2}", matrix.Energies.Length, F(h, 3), matrix.Stamp);
        Console.WriteLine("ПШПВ для диполя: {0} % на 662 кэВ, ∝ √E; допуск центра тяжести {1} кэВ",
                          F(fwhmPercent, 2), F(tol, 3));
        Console.WriteLine();

        var rows = new List<string>();
        rows.Add("arm;line_kev;delta_kev;peak_bin;centroid_kev;offset_kev;window_centroid_kev;"
                 + "window_offset_kev;area;in_peak_share;dipole_share;image_width_kev;sigma_growth_percent");

        bool bad = false;
        int clamped = 0;
        foreach (double e in energies)
        {
            int peakBin = EfficiencySimulator.PeakBin(e, h);
            double delta = e - peakBin * h;
            int lo, hi;
            Bracket(matrix, e, out lo, out hi);
            Console.WriteLine("линия {0} кэВ: бин пика {1} (центр {2}), δ = {3} кэВ; узлы {4} … {5}",
                              F(e, 4), peakBin, F(peakBin * h, 3), F(delta, 4),
                              F(matrix.Energies[lo], 3), F(matrix.Energies[hi], 3));

            // Судится плечо `pad1` — то, что делает САМО правило переноса.
            // `pad0` печатается рядом как «приёмник как есть»: у линий с δ > 0
            // он обязан отстать, и это не отказ правила, а счёт лишнего бина
            // приёмнику.
            if (Arm(rows, "pad0", matrix, e, peakBin, delta, 0, true, fwhmPercent, tol, false))
            {
                clamped++;
            }

            bad |= Arm(rows, "pad1", matrix, e, peakBin, delta, 1, true, fwhmPercent, tol, true);
            if (withScale)
            {
                Arm(rows, "scale_pad1", matrix, e, peakBin, delta, 1, false, fwhmPercent, tol, false);
            }

            Console.WriteLine();
        }

        if (withAnchor)
        {
            Anchor(rows, matrix);
        }

        if (outPath != null)
        {
            File.WriteAllLines(outPath, rows, new UTF8Encoding(false));
            Console.WriteLine("выписка: {0}", outPath);
        }

        if (dumpPath != null)
        {
            DumpAll(dumpPath, matrix, energies);
            Console.WriteLine("побитовая выписка каналов: {0} (по файлу на канал)", dumpPath);
        }

        Console.WriteLine(clamped > 0
            ? string.Format(CultureInfo.InvariantCulture,
                            "⚠ линий, которым правка не доедет до приёмника без лишнего бина: {0} из {1} "
                            + "(δ > 0; длина образа обязана быть ResponseMatrix.ImageBins(E, шаг))",
                            clamped, energies.Length)
            : "приёмнику прежней длины хватает на всех проверенных линиях (δ ≤ 0 у всех)");
        Console.WriteLine(bad
            ? "⛔ ПИК ОБРАЗА СТОИТ НЕ НА ЭНЕРГИИ ЛИНИИ — см. плечо pad1 выше"
            : "✅ пик образа каждой линии стоит на её энергии");
        return bad ? 1 : 0;
    }

    /// <summary>
    /// Одно плечо: образ пикового канала на длине `PeakBin(E)+1+pad`, его центр
    /// тяжести в шкале приёмника и цена смещения в ленте. Возвращает true,
    /// если плечо ОБЯЗАНО быть зелёным (<paramref name="judged"/>), а центр
    /// тяжести ушёл дальше допуска.
    /// </summary>
    static bool Arm(List<string> rows, string arm, ResponseMatrix matrix, double e, int peakBin,
                    double delta, int pad, bool byChannel, double fwhmPercent, double tol, bool judged)
    {
        double h = matrix.BinKev;
        int length = peakBin + 1 + pad;
        double[] image = new double[length];
        bool previous = matrix.TransferByChannel;
        matrix.TransferByChannel = byChannel;
        try
        {
            matrix.AccumulateChannel(image, e, 1.0, (int)EfficiencySimulator.ResponseChannel.Peak);
        }
        finally
        {
            matrix.TransferByChannel = previous;
        }

        double area = 0.0;
        double moment = 0.0;
        for (int b = 0; b < length; b++)
        {
            area += image[b];
            moment += b * h * image[b];
        }

        if (!(area > 0.0))
        {
            Console.WriteLine("  {0,-10} образ пуст", arm);
            return false;
        }

        double centroid = moment / area;

        // Окно ±3 бина вокруг бина пика: у пикового канала есть небольшой хвост
        // (истории, недобравшие больше допуска), и общий центр тяжести им
        // смещён. Вопрос задачи — где стоит САМ ПИК, поэтому судится окно.
        int wlo = Math.Max(0, peakBin - 3);
        int whi = Math.Min(length - 1, peakBin + 3);
        double warea = 0.0;
        double wmoment = 0.0;
        for (int b = wlo; b <= whi; b++)
        {
            warea += image[b];
            wmoment += b * h * image[b];
        }

        double wcentroid = warea > 0.0 ? wmoment / warea : double.NaN;

        // Цена дробного сдвига: собственная ширина образа пика. Целый сдвиг
        // держит пик в ОДНОМ бине (ширина 0), дробный делит его между двумя, и
        // в квадратуре к ПШПВ детектора добавляется `√(f(1−f))·шаг ≤ шаг/2`.
        double variance = 0.0;
        for (int b = wlo; b <= whi; b++)
        {
            double d = b * h - wcentroid;
            variance += image[b] * d * d;
        }

        double width = warea > 0.0 ? Math.Sqrt(variance / warea) : 0.0;
        double sigma = 0.01 * fwhmPercent * 662.0 * Math.Sqrt(e / 662.0) / 2.354820045;
        double grow = sigma > 0.0 ? Math.Sqrt(1.0 + width * width / (sigma * sigma)) - 1.0 : 0.0;
        double dipole = Dipole(image, h, e, area, fwhmPercent);
        bool off = warea > 0.0 && Math.Abs(wcentroid - e) > tol;

        Console.WriteLine(
            "  {0,-10} ц.т. {1} кэВ ({2}{3}); окно ±3 бина {4} кэВ ({5}{6}){7}; "
            + "в окне {8} % площади; |Δ| со свёрткой {9} % высоты; ширина образа {10} кэВ (σ +{11} %)",
            arm, F(centroid, 4), centroid - e >= 0.0 ? "+" : "", F(centroid - e, 4),
            F(wcentroid, 4), wcentroid - e >= 0.0 ? "+" : "", F(wcentroid - e, 4),
            judged ? (off ? "  ⛔" : "  ✅") : (off ? "  ⚠" : ""),
            F(100.0 * warea / area, 2), F(100.0 * dipole, 2), F(width, 3), F(100.0 * grow, 2));

        rows.Add(string.Join(";", new[]
        {
            arm, F(e, 4), F(delta, 4), peakBin.ToString(CultureInfo.InvariantCulture),
            F(centroid, 6), F(centroid - e, 6), F(wcentroid, 6), F(wcentroid - e, 6),
            E(area), F(100.0 * warea / area, 4), F(100.0 * dipole, 4), F(width, 4), F(100.0 * grow, 4)
        }));

        return off;
    }

    /// <summary>
    /// ⚠ ВТОРАЯ ПОЛОВИНА `AMBER70` — МАСШТАБ ОСИ САМОЙ СТРОКИ УЗЛА.
    /// <c>EfficiencySimulator.RemapLightScale</c> берёт якорь световой шкалы к
    /// ЦЕНТРУ пикового бина (`anchorPerBin = свет[пик]/вес/(peak·шаг)`), а
    /// средний свет пикового бина отвечает энергии узла `E`. Оттого бин `b`
    /// строки стоит не на `b·шаг`, а на `b·шаг·(peak·шаг/E)`: вся строка (пик,
    /// её комптон, её вылеты) растянута множителем `peak·шаг/E = 1 − δ/E`.
    /// Пик этим не задет — правка переноса (`Transfer`) ставит его на `E` уже
    /// независимо от оси строки; задет континуум и вылеты.
    ///
    /// Проба печатает этот множитель по ВСЕМ узлам матрицы: считается он из
    /// энергии узла и шага бина, обе величины — из файла. Правка тут была бы
    /// правкой СЧЁТА (склад), либо встречным множителем в `Transfer` для
    /// остальных каналов; ни того, ни другого П135 не делает.
    /// </summary>
    static void Anchor(List<string> rows, ResponseMatrix matrix)
    {
        double h = matrix.BinKev;
        double[] grid = matrix.Energies;
        var worst = new List<int>();
        double sum = 0.0;
        double top = 0.0;
        for (int i = 0; i < grid.Length; i++)
        {
            double e = grid[i];
            int peak = EfficiencySimulator.PeakBin(e, h);
            double stretch = peak * h / e - 1.0;
            sum += Math.Abs(stretch);
            if (Math.Abs(stretch) > top)
            {
                top = Math.Abs(stretch);
            }

            rows.Add(string.Join(";", new[]
            {
                "node", F(e, 4), F(e - peak * h, 4), peak.ToString(CultureInfo.InvariantCulture),
                "", "", "", "", "", "", "", "", F(100.0 * stretch, 6)
            }));
            worst.Add(i);
        }

        worst.Sort(delegate(int a, int b)
        {
            double sa = Math.Abs(EfficiencySimulator.PeakBin(grid[a], h) * h / grid[a] - 1.0);
            double sb = Math.Abs(EfficiencySimulator.PeakBin(grid[b], h) * h / grid[b] - 1.0);
            return sb.CompareTo(sa);
        });

        Console.WriteLine("ось строки узла (якорь `RemapLightScale` к центру бина, `AMBER70`, вторая половина):");
        Console.WriteLine("  узлов {0}; |растяжение| в среднем {1} %, худшее {2} %",
                          grid.Length, F(100.0 * sum / grid.Length, 4), F(100.0 * top, 4));
        for (int k = 0; k < Math.Min(5, worst.Count); k++)
        {
            int i = worst[k];
            int peak = EfficiencySimulator.PeakBin(grid[i], h);
            Console.WriteLine("  узел {0} кэВ: бин пика {1} (центр {2}), δ = {3} кэВ, растяжение {4} % "
                              + "= {5} кэВ на верхнем конце строки",
                              F(grid[i], 4), peak, F(peak * h, 2), F(grid[i] - peak * h, 4),
                              F(100.0 * (peak * h / grid[i] - 1.0), 3),
                              F(peak * h - grid[i], 3));
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Побитовая выписка образов КАЖДОГО канала на каждой линии — по файлу на
    /// канал (`&lt;префикс&gt;_ch&lt;N&gt;.csv`), числа в формате «R» (обратимая
    /// запись double). Контроль неизменности: правка пикового канала обязана
    /// оставить остальные файлы совпадающими по sha256 до бита.
    /// </summary>
    static void DumpAll(string prefix, ResponseMatrix matrix, double[] energies)
    {
        double h = matrix.BinKev;
        int channels = matrix.ChannelRows.Length;
        bool previous = matrix.TransferByChannel;
        for (int c = 0; c < channels; c++)
        {
            var lines = new List<string> { "line_kev;bin;value" };
            foreach (double e in energies)
            {
                int length = EfficiencySimulator.PeakBin(e, h) + 2;
                double[] image = new double[length];
                matrix.TransferByChannel = true;
                try
                {
                    matrix.AccumulateChannel(image, e, 1.0, c);
                }
                finally
                {
                    matrix.TransferByChannel = previous;
                }

                for (int b = 0; b < length; b++)
                {
                    if (image[b] != 0.0)
                    {
                        lines.Add(string.Concat(F(e, 4), ";", b.ToString(CultureInfo.InvariantCulture), ";",
                                                image[b].ToString("R", CultureInfo.InvariantCulture)));
                    }
                }
            }

            File.WriteAllLines(prefix + "_ch" + c.ToString(CultureInfo.InvariantCulture) + ".csv",
                               lines, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Цена смещения для человека: образ и ИДЕАЛ (та же площадь ровно на `E`)
    /// сворачиваются с гауссианой ПШПВ детектора, и берётся максимум модуля
    /// разности в долях высоты свёрнутого идеала. Это и есть диполь в ленте
    /// невязки у пика.
    /// </summary>
    static double Dipole(double[] image, double h, double e, double area, double fwhmPercent)
    {
        if (!(fwhmPercent > 0.0) || !(area > 0.0))
        {
            return 0.0;
        }

        double fwhmKev = 0.01 * fwhmPercent * 662.0 * Math.Sqrt(e / 662.0);
        double sigma = fwhmKev / 2.354820045;
        if (!(sigma > 0.0))
        {
            return 0.0;
        }

        // Тонкая шкала свёртки: шаг в двадцать раз мельче бина склада, окно
        // ±5σ вокруг линии — чтобы дискретизация самой свёртки не подменила
        // мерку.
        double step = h / 20.0;
        int half = (int)Math.Ceiling(5.0 * sigma / step);
        int n = 2 * half + 1;
        double[] got = new double[n];
        double[] want = new double[n];
        double norm = 1.0 / (sigma * Math.Sqrt(2.0 * Math.PI));
        for (int i = 0; i < n; i++)
        {
            double x = e + (i - half) * step;
            double sum = 0.0;
            for (int b = 0; b < image.Length; b++)
            {
                double v = image[b];
                if (!(v > 0.0))
                {
                    continue;
                }

                double d = (x - b * h) / sigma;
                if (Math.Abs(d) > 6.0)
                {
                    continue;
                }

                sum += v * norm * Math.Exp(-0.5 * d * d);
            }

            got[i] = sum;
            double di = (x - e) / sigma;
            want[i] = area * norm * Math.Exp(-0.5 * di * di);
        }

        double top = 0.0;
        double gap = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (want[i] > top)
            {
                top = want[i];
            }

            double d = Math.Abs(got[i] - want[i]);
            if (d > gap)
            {
                gap = d;
            }
        }

        return top > 0.0 ? gap / top : 0.0;
    }

    static void Bracket(ResponseMatrix matrix, double e, out int lo, out int hi)
    {
        double[] grid = matrix.Energies;
        lo = 0;
        hi = grid.Length - 1;
        for (int i = 0; i + 1 < grid.Length; i++)
        {
            if (e >= grid[i] && e <= grid[i + 1])
            {
                lo = i;
                hi = i + 1;
                return;
            }
        }
    }

    static string F(double v, int digits)
    {
        return v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static string E(double v)
    {
        return v.ToString("E6", CultureInfo.InvariantCulture);
    }
}
