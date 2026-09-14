using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// `E34`, полоса F76: СКОЛЬКО ТЕРЯЕТ ПИК МАТРИЦЫ от нулевого допуска в сборщике.
///
/// Зачем отдельная проба, когда есть <c>MatrixDiffProbe</c>. Та печатает
/// МЕДИАНУ и ХУДШИЙ узел по всей сетке — мерка правки, которая обязана считать
/// то же самое. Здесь вопрос другой и на двух узлах: НАСКОЛЬКО и В КАКУЮ
/// СТОРОНУ изменился ПИК каждого из них, и не тонет ли это изменение в шуме
/// двух зёрен одного кода. Медиана по двум узлам такого не отвечает.
///
/// Печатается по каждому узлу: энергия, последний бин строки (это и есть пик
/// полного поглощения — длина строки считается так, что последний бин всегда
/// пик), сумма строки (эффективность узла) и доля пика в сумме. При двух
/// матрицах — то же самое парами плюс относительная разность.
///
/// ⛔ ПОБИТОВОЕ РАВЕНСТВО СТРОК проверяется ОТДЕЛЬНЫМ числом (`несовпавших
/// бинов`), а не порогом на разности: приёмка «ключ выключен — матрица та же»
/// требует именно тождества, а «0.000 %» его не доказывает — так печатается и
/// расхождение в последнем разряде float.
///
///   matrixpeakprobef76 --a=было.rmx [--b=стало.rmx] [--csv=файл.csv]
/// </summary>
static class MatrixPeakProbeF76
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string aPath = null, bPath = null, csv = null;
        foreach (string s in args)
        {
            if (s.StartsWith("--a=", StringComparison.Ordinal)) aPath = s.Substring(4);
            else if (s.StartsWith("--b=", StringComparison.Ordinal)) bPath = s.Substring(4);
            else if (s.StartsWith("--csv=", StringComparison.Ordinal)) csv = s.Substring(6);
            else { Console.Error.WriteLine("неизвестный ключ: " + s); return 2; }
        }

        if (aPath == null)
        {
            Console.Error.WriteLine("нужен --a=");
            return 2;
        }

        ResponseMatrix a = ReadOrDie(aPath);
        if (a == null) return 2;
        ResponseMatrix b = null;
        if (bPath != null)
        {
            b = ReadOrDie(bPath);
            if (b == null) return 2;
            if (a.Energies.Length != b.Energies.Length)
            {
                Console.Error.WriteLine(string.Format(
                    "узлов разное число: {0} против {1} — сравнивать нечего",
                    a.Energies.Length, b.Energies.Length));
                return 2;
            }
        }

        Console.WriteLine("A: {0}", aPath);
        Console.WriteLine("   клеймо {0}", a.Stamp);
        Console.WriteLine("   узлов {0}, бин {1} кэВ, историй {2}",
                          a.Energies.Length,
                          a.BinKev.ToString("R", CultureInfo.InvariantCulture),
                          a.HistoriesSpent);
        if (b != null)
        {
            Console.WriteLine("B: {0}", bPath);
            Console.WriteLine("   клеймо {0}", b.Stamp);
            Console.WriteLine("   узлов {0}, бин {1} кэВ, историй {2}",
                              b.Energies.Length,
                              b.BinKev.ToString("R", CultureInfo.InvariantCulture),
                              b.HistoriesSpent);
            Console.WriteLine("   клейма {0}", string.Equals(a.Stamp, b.Stamp, StringComparison.Ordinal)
                                               ? "СОВПАЛИ" : "РАЗНЫЕ");
        }

        Console.WriteLine();
        StreamWriter w = null;
        if (csv != null)
        {
            w = new StreamWriter(csv, false, new UTF8Encoding(true));
            w.WriteLine("node,energy_kev,peak_a,sum_a,frac_a,peak_b,sum_b,frac_b,d_peak_pct,d_sum_pct,bins_differ,bins_total");
        }

        try
        {
            for (int i = 0; i < a.Energies.Length; i++)
            {
                float[] ra = Row(a, i);
                double peakA = ra.Length > 0 ? ra[ra.Length - 1] : 0.0;
                double sumA = Sum(ra);
                double fracA = sumA > 0.0 ? peakA / sumA : 0.0;

                if (b == null)
                {
                    Console.WriteLine("узел {0,3}  {1,9:F2} кэВ   пик {2:E6}   сумма {3:E6}   доля {4:F4}",
                                      i, a.Energies[i], peakA, sumA, fracA);
                    if (w != null)
                    {
                        w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1:F3},{2:E9},{3:E9},{4:F6},,,,,,,{5}",
                            i, a.Energies[i], peakA, sumA, fracA, ra.Length));
                    }

                    continue;
                }

                float[] rb = Row(b, i);
                double peakB = rb.Length > 0 ? rb[rb.Length - 1] : 0.0;
                double sumB = Sum(rb);
                double fracB = sumB > 0.0 ? peakB / sumB : 0.0;

                int differ = 0;
                int bins = Math.Max(ra.Length, rb.Length);
                for (int k = 0; k < bins; k++)
                {
                    float va = k < ra.Length ? ra[k] : 0.0f;
                    float vb = k < rb.Length ? rb[k] : 0.0f;
                    if (va != vb)
                    {
                        differ++;
                    }
                }

                double dPeak = peakA > 0.0 ? 100.0 * (peakB - peakA) / peakA : 0.0;
                double dSum = sumA > 0.0 ? 100.0 * (sumB - sumA) / sumA : 0.0;
                Console.WriteLine("узел {0,3}  {1,9:F2} кэВ", i, a.Energies[i]);
                Console.WriteLine("   пик   A {0:E6}  B {1:E6}   {2,8:F3} %", peakA, peakB, dPeak);
                Console.WriteLine("   сумма A {0:E6}  B {1:E6}   {2,8:F3} %", sumA, sumB, dSum);
                Console.WriteLine("   доля  A {0:F5}      B {1:F5}", fracA, fracB);
                Console.WriteLine("   несовпавших бинов {0} из {1}", differ, bins);
                if (w != null)
                {
                    w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F3},{2:E9},{3:E9},{4:F6},{5:E9},{6:E9},{7:F6},{8:F4},{9:F4},{10},{11}",
                        i, a.Energies[i], peakA, sumA, fracA, peakB, sumB, fracB,
                        dPeak, dSum, differ, bins));
                }
            }
        }
        finally
        {
            if (w != null)
            {
                w.Dispose();
            }
        }

        if (csv != null)
        {
            Console.WriteLine();
            Console.WriteLine("раскладка: {0}", csv);
        }

        return 0;
    }

    static float[] Row(ResponseMatrix m, int index)
    {
        if (m.Rows == null || index >= m.Rows.Length || m.Rows[index] == null)
        {
            return new float[0];
        }

        return m.Rows[index];
    }

    static double Sum(float[] row)
    {
        double s = 0.0;
        for (int i = 0; i < row.Length; i++)
        {
            s += row[i];
        }

        return s;
    }

    static ResponseMatrix ReadOrDie(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("нет файла: " + path);
            return null;
        }

        MatrixRefusal refusal;
        int format;
        ResponseMatrix m = ResponseMatrix.Load(path, out refusal, out format);
        if (m == null)
        {
            Console.Error.WriteLine(string.Format("матрица не прочиталась: {0}{1} (читаем формат {2}): {3}",
                refusal, refusal == MatrixRefusal.OldFormat ? " формат " + format : "",
                ResponseMatrix.FormatVersion, path));
        }

        return m;
    }
}
