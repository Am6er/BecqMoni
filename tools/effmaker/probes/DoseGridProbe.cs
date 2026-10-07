using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace DoseGridProbe
{
    /// <summary>
    /// Полоса П145 (24.09.2026): мощность дозы на СИНТЕТИЧЕСКОМ спектре, у
    /// которого ответ известен руками, — `AMBER77`, `AMBER82`, `AMBER101`.
    ///
    /// Спектр линии E строится из той же матрицы, по которой считает доза:
    /// N квантов/с, испущенных в 4π, дают `N·T·строка(E)` отсчётов (строка —
    /// <see cref="ResponseMatrix.Evaluate"/> на ЭНЕРГИИ ЛИНИИ, с переносом по
    /// каналам, как у входа дозы). Бин `b` строки — это интервал
    /// `[(b − ½)·шаг, (b + ½)·шаг)`, отсчёты в нём разложены равномерно и
    /// раскиданы по каналам спектра корпуса (номер канала — его ЦЕНТР,
    /// `AMBER73`). Ответ руками: `Ḣ = N·G·Ḣ*(10)/φ̇ (E)` — энергия ЛИНИИ, не
    /// диапазона. Печатается `показание / ответ − 1`.
    ///
    ///   §1 ПУТЬ МАТРИЦЫ: полный отклик, без разрешения и с разрешением прибора
    ///      (гаусс, FWHM ∝ √E от ширины на 662 из `manifest.csv`).
    ///   §2 ПУТЬ ПИКОВОЙ КРИВОЙ («≈»): только пик площади `N·T·ε_пик(E)` —
    ///      так видна доля представительной энергии без континуума, который
    ///      путь «≈» заведомо принимает за кванты.
    ///   §3 НАСТОЯЩИЕ СПЕКТРЫ КОРПУСА: доза с матрицей и по пиковой — числа,
    ///      которые видит человек, побитово (`R`), для сверки до/после.
    ///
    ///     dosegridprobe [--dir=&lt;корпус&gt;] [--tol=2.0] [--quiet] [--show=&lt;сцена&gt;:&lt;кэВ&gt;]
    ///                   [--set=&lt;рычаг DoseRateManager&gt;=&lt;число&gt;] (П159, отражением)
    ///
    /// Приёмка: путь матрицы без разрешения — |показание/ответ − 1| ≤ `--tol` %
    /// (2 %) у линий не ближе бина склада к границе диапазона и с центром
    /// тяжести строки, растущим с энергией хотя бы вполовину (прочие
    /// печатаются `[--]` с причиной); путь «≈» на одном пике — 1.5 % без
    /// разрешения и 2.5 % с ним. Положительный контроль — та же проба на
    /// прежнем exe: 76 из 100 проверок красные.
    ///
    /// (`S185`, П153) Путь матрицы С РАЗРЕШЕНИЕМ судится при `--res=own` —
    /// когда синтетика размыта той же шириной, что знает доза (калибровка
    /// ширины спектра): |показание/ответ − 1| ≤ `--tolres` % (1 %) у ВСЕХ
    /// линий, и доза диапазонов, не соседних с диапазоном линии (фантом), —
    /// не больше `--tolres` % показания. При ширине `manifest.csv` (по
    /// умолчанию) модель и синтетика расходятся на разницу двух моделей
    /// ширины (до двух раз у 59.5 кэВ) — это плечо печатается, но не
    /// судится. Положительный контроль судимого плеча — прежний exe (строка
    /// без разрешения): AS80 59.5 +6.39 %, RC-103 1173/1461 +6.20/+5.37 %.
    ///
    /// ⚠ Склад матриц корпуса посчитан прежним поколением физики; клеймо, если
    /// не сходится, ПЕРЕСЧИТЫВАЕТСЯ в памяти (файл не пишется). На мерку это не
    /// влияет: спектр и доза берут ОДНУ и ту же матрицу.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static double tolPercent = 2.0;
        static double tolResPercent = 1.0;
        static bool quiet;
        static string show;

        /// <summary>
        /// (`S185`, П151) `--res=own`: синтетика «с разрешением» размывается
        /// ПШПВ САМОГО спектра (<c>ResultData.FwhmCalibration</c>, каналы → кэВ),
        /// то есть той шириной, которой свёртка строки (попытка П151) её
        /// сворачивала бы. По умолчанию —
        /// ширина из `manifest.csv` ∝ √E, как у П145 (модель и спектр тогда
        /// расходятся на разницу двух моделей ширины).
        /// </summary>
        static bool resOwn;

        sealed class Scene
        {
            public string Spectrum;
            public string Matrix;
            public double Fwhm662Percent;
        }

        static readonly Scene[] Scenes =
        {
            new Scene { Spectrum = "AS80_Cs137_0cm", Matrix = "AS80_point0", Fwhm662Percent = 7.22 },
            new Scene { Spectrum = "G1S16_Cs137_P5", Matrix = "G1S_point5_p16", Fwhm662Percent = 6.61 },
            new Scene { Spectrum = "RC103_Cs137_0cm", Matrix = "RC103_point0", Fwhm662Percent = 8.26 },
            new Scene { Spectrum = "ASN16_Cs137_10cm", Matrix = "ASN16_point10_house", Fwhm662Percent = 6.26 },
        };

        static readonly double[] Lines = { 59.5409, 88.0336, 122.0607, 356.0129, 661.657, 1173.228, 1332.492, 1460.822, 2614.511 };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--tol=", StringComparison.Ordinal))
                    tolPercent = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a == "--quiet") quiet = true;
                else if (a.StartsWith("--show=", StringComparison.Ordinal)) show = a.Substring(7);
                else if (a == "--res=own") resOwn = true;
                else if (a.StartsWith("--tolres=", StringComparison.Ordinal))
                    tolResPercent = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    // (`S194`, П159) Мерный рычаг расчёта: закрытое статическое
                    // поле `DoseRateManager` — отражением, для замера вариантов.
                    string[] kv = a.Substring(6).Split('=');
                    System.Reflection.FieldInfo lever = kv.Length == 2
                        ? typeof(DoseRateManager).GetField(kv[0], System.Reflection.BindingFlags.NonPublic
                                                                   | System.Reflection.BindingFlags.Static)
                        : null;
                    if (lever == null || lever.FieldType != typeof(double))
                    {
                        Console.Error.WriteLine("нет рычага: " + a);
                        return 2;
                    }

                    lever.SetValue(null, double.Parse(kv[1], CultureInfo.InvariantCulture));
                    Console.WriteLine("рычаг " + kv[0] + " = " + kv[1]);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Console.WriteLine("П145: доза на синтетическом спектре линии, ответ руками (AMBER77/82/101)");
            var summary = new List<string>();
            foreach (Scene scene in Scenes)
            {
                ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", scene.Spectrum + ".xml"));
                if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
                {
                    Console.WriteLine("ОСНАСТКА: нет спектра или кривой с геометрией: " + scene.Spectrum);
                    return 2;
                }

                ResponseMatrix matrix = LoadMatrix(Path.Combine(corpusDir, "geometries", scene.Matrix + ".rmx"),
                                                   data.Efficiency.Geometry);
                if (matrix == null)
                {
                    Console.WriteLine("ОСНАСТКА: нет матрицы " + scene.Matrix);
                    return 2;
                }

                Real(scene, data, matrix);
                Synthetic(scene, data, matrix, summary);
            }

            Console.WriteLine();
            Console.WriteLine("СВОДКА (показание/ответ − 1, %):");
            foreach (string s in summary) Console.WriteLine("  " + s);
            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // §3. Настоящий спектр
        // ==================================================================

        static void Real(Scene scene, ResultData data, ResponseMatrix matrix)
        {
            Head("§3. " + scene.Spectrum + " (матрица " + scene.Matrix + "): настоящий спектр");
            var manager = new DoseRateManager(Config());
            DoseRate withMatrix = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, matrix));
            DoseRate peak = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, null));
            Console.WriteLine("  с матрицей: {0}; ПОБИТОВО {1:R}, покрытие {2:R}",
                              Describe(withMatrix), withMatrix.Rate, withMatrix.Coverage);
            Console.WriteLine("  по пиковой: {0}; ПОБИТОВО {1:R}, покрытие {2:R}",
                              Describe(peak), peak.Rate, peak.Coverage);
            // (`S185`, П153) Тот же спектр без калибровки ширины — строка без
            // разрешения; у прежнего exe ширина не читалась вовсе.
            DoseRate plain = null;
            WithoutFwhm(data, () => { plain = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, matrix)); return 0.0; });
            Console.WriteLine("  с матрицей без калибровки ширины: {0}; ПОБИТОВО {1:R}, покрытие {2:R}",
                              Describe(plain), plain.Rate, plain.Coverage);
            // Цена одного расчёта: строка состояния зовёт его каждые 200 мс.
            DoseRateInput timedInput = DoseRateInput.Of(data.Efficiency, matrix);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            const int repeats = 10;
            for (int i = 0; i < repeats; i++) manager.Calculate(data, timedInput);
            Console.WriteLine("  время расчёта с матрицей: {0:F1} мс ({1} каналов)",
                              watch.Elapsed.TotalMilliseconds / repeats, data.EnergySpectrum.NumberOfChannels);
            if (show == scene.Matrix + ":real")
            {
                ShowRanges(withMatrix);
                ShowRanges(peak);
            }
        }

        static string Describe(DoseRate d)
        {
            return string.IsNullOrEmpty(d.Refusal)
                ? string.Format(CultureInfo.InvariantCulture, "{0:F6} мкЗв/ч", d.Rate)
                : "ОТКАЗ «" + d.Refusal + "»";
        }

        // ==================================================================
        // §1, §2. Синтетика
        // ==================================================================

        static void Synthetic(Scene scene, ResultData data, ResponseMatrix matrix, List<string> summary)
        {
            EnergySpectrum spectrum = data.EnergySpectrum;
            EnergyCalibration cal = spectrum.EnergyCalibration;
            int n = spectrum.NumberOfChannels;
            double seconds = spectrum.EffectiveLiveTime;
            DoseRateInput full = DoseRateInput.Of(data.Efficiency, matrix);
            DoseRateInput peakInput = DoseRateInput.Of(data.Efficiency, null);
            var manager = new DoseRateManager(Config());

            var edges = new double[n + 1];
            // Крайние границы — продолжением полуширины: калибровка зажимает
            // номер канала в [0, maxChannels], и `E(−½)` вернула бы `E(0)`.
            for (int i = 1; i < n; i++) edges[i] = cal.ChannelToEnergy(i - 0.5);
            edges[0] = 2.0 * cal.ChannelToEnergy(0.0) - edges[1];
            edges[n] = 2.0 * cal.ChannelToEnergy(n - 1) - edges[n - 1];
            double scaleLow = cal.ChannelToEnergy(0.0), scaleHigh = cal.ChannelToEnergy(n);

            Head(string.Format(CultureInfo.InvariantCulture,
                "§1/§2. {0}: {1} каналов, шкала {2:F2}…{3:F2} кэВ, T = {4:F1} с, G = {5:E4} 1/см²",
                scene.Spectrum, n, scaleLow, scaleHigh, seconds, full.FluencePerPhoton));

            int[] original = (int[])spectrum.Spectrum.Clone();
            double sumAbsRes = 0.0;
            int countRes = 0;
            double gridLow, gridHigh;
            DoseRateEstimator.DeviceRange(null, spectrum, out gridLow, out gridHigh);
            double[] grid = DoseRateEstimator.BuildGrid(Math.Max(gridLow, full.MinKev), Math.Min(gridHigh, full.MaxKev));
            var line = new StringBuilder();
            line.AppendFormat(CultureInfo.InvariantCulture, "{0,-18}", scene.Matrix);
            try
            {
                foreach (double e in Lines)
                {
                    if (e < full.MinKev || e > full.MaxKev || e < scaleLow || e >= scaleHigh) continue;
                    double h = DoseRateCoefficients.DoseRatePerFluenceRate(e);
                    showNow = show != null && show == scene.Matrix + ":" + e.ToString("F0", CultureInfo.InvariantCulture);

                    // --- путь матрицы, полный отклик ---
                    double step = matrix.BinKev;
                    int cells = (int)Math.Ceiling(scaleHigh / step) + 4;
                    double[] row = matrix.Evaluate(e, cells);
                    double rowSum = row.Sum();
                    double emittedPerSecond = 2.0e8 / (seconds * rowSum);     // ~2e8 отсчётов
                    double truth = emittedPerSecond * full.FluencePerPhoton * h;

                    // (`S185`, П151) Спектр БЕЗ разрешения — это спектр без
                    // калибровки ширины: доза, сворачивающая строку с ПШПВ
                    // спектра (попытка П151, откачена — журнал П151), тогда
                    // её не свернёт. Нынешняя доза ширину не читает вовсе.
                    double[] dnone = Place(row, step, edges, null, emittedPerSecond * seconds);
                    double rNone = WithoutFwhm(data, () => Reading(manager, data, full, dnone)) / truth - 1.0;

                    double fwhm662 = scene.Fwhm662Percent / 100.0 * 661.657;
                    Func<double, double> sigma = resOwn && data.FwhmCalibration != null
                        ? OwnSigma(data)
                        : x => fwhm662 * Math.Sqrt(Math.Max(x, 1.0) / 661.657) / 2.3548200450309493;
                    double[] dres = Place(row, step, edges, sigma, emittedPerSecond * seconds);
                    double rRes = Reading(manager, data, full, dres) / truth - 1.0;
                    double phantom = Phantom(lastDose, e);

                    // --- путь пиковой кривой: только пик ---
                    double qNone = double.NaN, qRes = double.NaN;
                    bool peakSkipped = false;
                    if (e < peakInput.MaxKev && e > peakInput.MinKev)
                    {
                        // Линия за краем пиковой кривой диапазона не получает
                        // вовсе (сетка обрезана по кривой) — не судится.
                        double eps = peakInput.EfficiencyAt(e);
                        double area = 2.0e7;
                        double emittedPeak = area / (seconds * eps);
                        double truthPeak = emittedPeak * peakInput.FluencePerPhoton * h;
                        double[] pNone = PlacePoint(e, edges, null, area);
                        double[] pRes = PlacePoint(e, edges, sigma, area);
                        qNone = WithoutFwhm(data, () => Reading(manager, data, peakInput, pNone)) / truthPeak - 1.0;
                        qRes = Reading(manager, data, peakInput, pRes) / truthPeak - 1.0;
                        peakSkipped = lastDose != null
                                      && lastDose.Ranges.Any(r => r.LowKev <= e && e < r.HighKev && r.Skipped);
                        // (`S186`, П151) Пол — только ниже максимума кривой
                        // (решение Amber 24.09.2026): диапазон ВЫШЕ диапазона с
                        // наибольшей своей долей снят быть не может — такая
                        // линия судится (на прежнем exe она −100 %).
                        if (peakSkipped && lastDose.Ranges.Count > 0)
                        {
                            int lineRange = lastDose.Ranges.FindIndex(r => r.LowKev <= e && e < r.HighKev);
                            int top = 0;
                            for (int k = 1; k < lastDose.Ranges.Count; k++)
                                if (lastDose.Ranges[k].OwnEfficiency > lastDose.Ranges[top].OwnEfficiency) top = k;
                            if (lineRange > top)
                            {
                                peakSkipped = false;
                            }
                        }
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,9:F3} кэВ: матрица без разрешения {1,8:+0.000;-0.000} %, с разрешением {2,8:+0.000;-0.000} %;"
                        + " пиковая (пик) без {3,8:+0.000;-0.000} %, с {4,8:+0.000;-0.000} %",
                        e, 100.0 * rNone, 100.0 * rRes, 100.0 * qNone, 100.0 * qRes));
                    line.AppendFormat(CultureInfo.InvariantCulture, " {0:F0}:{1:+0.00;-0.00}/{2:+0.00;-0.00}/{3:+0.00;-0.00}/{4:+0.00;-0.00}",
                                      e, 100.0 * rNone, 100.0 * rRes, 100.0 * qNone, 100.0 * qRes);
                    // Судится путь матрицы без разрешения — там ответ точный, если
                    // линия (а) не ближе бина склада к границе диапазона: иначе её
                    // дельта делится границей, и доля каждой стороны — от долей
                    // кэВ положения; (б) центр тяжести её строки в диапазоне
                    // растёт с её энергией хотя бы вполовину: иначе (свой комптон
                    // входит в диапазон вместе с линией) энергия по центру тяжести
                    // не определена — см. журнал П145; (в) диапазон не снят полом
                    // эффективности.
                    double boundary = double.MaxValue;
                    foreach (double edge in grid) boundary = Math.Min(boundary, Math.Abs(edge - e));
                    double slope = CentroidSlope(matrix, cells, step, grid, e);
                    string why = boundary < step ? string.Format(CultureInfo.InvariantCulture,
                                     "у границы ({0:F2} кэВ)", boundary)
                                 : slope < 0.5 ? string.Format(CultureInfo.InvariantCulture,
                                     "центр тяжести строки в диапазоне едет с наклоном {0:F2}", slope)
                                 : null;
                    if (why == null)
                    {
                        Ok(Math.Abs(rNone) <= tolPercent / 100.0,
                           string.Format(CultureInfo.InvariantCulture,
                               "{0} {1:F1} кэВ: путь матрицы без разрешения в пределах {2} % ({3:+0.000;-0.000} %)",
                               scene.Matrix, e, tolPercent, 100.0 * rNone));
                    }
                    else
                    {
                        Console.WriteLine("  [--]   {0} {1:F1} кэВ: путь матрицы не судится — {2}", scene.Matrix, e, why);
                    }

                    // Путь «≈» на одном пике: ответ точный с точностью до утечки
                    // пика через границу (с разрешением).
                    if (peakSkipped)
                    {
                        Console.WriteLine("  [--]   {0} {1:F1} кэВ: путь пиковой не судится — диапазон линии снят полом эффективности",
                                          scene.Matrix, e);
                    }
                    else if (!double.IsNaN(qNone))
                    {
                        Ok(Math.Abs(qNone) <= PeakTolPercent / 100.0,
                           string.Format(CultureInfo.InvariantCulture,
                               "{0} {1:F1} кэВ: путь пиковой без разрешения в пределах {2} % ({3:+0.000;-0.000} %)",
                               scene.Matrix, e, PeakTolPercent, 100.0 * qNone));
                        Ok(Math.Abs(qRes) <= PeakResTolPercent / 100.0,
                           string.Format(CultureInfo.InvariantCulture,
                               "{0} {1:F1} кэВ: путь пиковой с разрешением в пределах {2} % ({3:+0.000;-0.000} %)",
                               scene.Matrix, e, PeakResTolPercent, 100.0 * qRes));
                    }

                    // (`S185`, П153) Путь матрицы с разрешением — при своей
                    // ширине синтетики судится у всех линий; фантом —
                    // доза диапазонов, не соседних с диапазоном линии.
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,9:F3} кэВ: фантом с разрешением {1:F3} % показания", e, 100.0 * phantom));
                    if (resOwn)
                    {
                        Ok(Math.Abs(rRes) <= tolResPercent / 100.0,
                           string.Format(CultureInfo.InvariantCulture,
                               "{0} {1:F1} кэВ: путь матрицы с разрешением (своя ширина) в пределах {2} % ({3:+0.000;-0.000} %)",
                               scene.Matrix, e, tolResPercent, 100.0 * rRes));
                        Ok(phantom <= tolResPercent / 100.0,
                           string.Format(CultureInfo.InvariantCulture,
                               "{0} {1:F1} кэВ: фантом с разрешением не больше {2} % показания ({3:F3} %)",
                               scene.Matrix, e, tolResPercent, 100.0 * phantom));
                    }

                    sumAbsRes += Math.Abs(rRes);
                    countRes++;
                }
            }
            finally
            {
                Array.Copy(original, spectrum.Spectrum, original.Length);
            }

            line.AppendFormat(CultureInfo.InvariantCulture, "  | среднее |матрица с разрешением| {0:F2} %",
                              countRes > 0 ? 100.0 * sumAbsRes / countRes : 0.0);
            summary.Add(line.ToString());
        }

        /// <summary>
        /// Наклон центра тяжести строки линии внутри её диапазона по энергии
        /// линии, d⟨E⟩/dE (строка без разрешения, бины по центру, ±2 бина).
        /// </summary>
        static double CentroidSlope(ResponseMatrix matrix, int cells, double step, double[] grid, double e)
        {
            int k = 0;
            while (k < grid.Length - 2 && grid[k + 1] <= e) k++;
            double lo = grid[k], hi = grid[k + 1];
            Func<double, double> centroid = x =>
            {
                double[] row = matrix.Evaluate(x, cells);
                double m0 = 0.0, m1 = 0.0;
                for (int b = 0; b < row.Length; b++)
                {
                    double centre = b * step;
                    if (centre >= lo && centre < hi)
                    {
                        m0 += row[b];
                        m1 += row[b] * centre;
                    }
                }

                return m0 > 0.0 ? m1 / m0 : double.NaN;
            };
            double d = 2.0 * step;
            return (centroid(e + d) - centroid(e - d)) / (2.0 * d);
        }

        /// <summary>
        /// (`S185`, П153) Доля показания от диапазонов, не соседних с
        /// диапазоном линии <paramref name="e"/>, — фантомные линии. Отказ или
        /// пустое показание — NaN.
        /// </summary>
        static double Phantom(DoseRate d, double e)
        {
            if (d == null || !string.IsNullOrEmpty(d.Refusal) || !(d.Rate > 0.0))
            {
                return double.NaN;
            }

            int line = d.Ranges.FindIndex(r => r.LowKev <= e && e < r.HighKev);
            double far = 0.0;
            for (int k = 0; k < d.Ranges.Count; k++)
            {
                if (Math.Abs(k - line) > 1 && !d.Ranges[k].Skipped && d.Ranges[k].Attributed > 0.0)
                {
                    far += Math.Abs(d.Ranges[k].DoseRate);
                }
            }

            return far / d.Rate;
        }

        /// <summary>Допуск пути «≈» на одном пике без разрешения, %.</summary>
        const double PeakTolPercent = 1.5;

        /// <summary>Допуск пути «≈» на одном пике с разрешением, %: утечка через границу.</summary>
        const double PeakResTolPercent = 2.5;

        /// <summary>Показание дозы на подложенном спектре, мкЗв/ч; отказ — NaN.</summary>
        static bool showNow;

        static DoseRate lastDose;

        static double Reading(DoseRateManager manager, ResultData data, DoseRateInput input, double[] counts)
        {
            int[] target = data.EnergySpectrum.Spectrum;
            for (int i = 0; i < target.Length; i++)
            {
                double v = Math.Round(counts[i]);
                target[i] = v > int.MaxValue ? int.MaxValue : (int)v;
            }

            DoseRate d = manager.Calculate(data, input);
            lastDose = d;
            if (!string.IsNullOrEmpty(d.Refusal))
            {
                Console.WriteLine("  ОТКАЗ: " + d.Refusal);
                return double.NaN;
            }

            if (showNow)
            {
                // Поле `RepresentativeKev` есть только у исправленного
                // приложения — читается отражением (положительный контроль
                // гоняет пробу и на прежнем exe).
                ShowRanges(d);
            }

            return d.Rate;
        }

        /// <summary>
        /// Представительная энергия диапазона — отражением: у прежнего
        /// приложения (положительный контроль) поля нет, там печатается середина.
        /// </summary>
        static double Representative(DoseRateRange r)
        {
            System.Reflection.FieldInfo f = typeof(DoseRateRange).GetField("RepresentativeKev");
            return f != null ? (double)f.GetValue(r) : r.CenterKev;
        }

        static void ShowRanges(DoseRate d)
        {
            foreach (DoseRateRange r in d.Ranges)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "      {0,9:F3}…{1,9:F3} энергия {2,9:F3}: отсчётов {3,12:F1}, объяснено {4,12:F1}, своя {5:E4}, мкЗв/ч {6:E5}{7}",
                    r.LowKev, r.HighKev, Representative(r), r.Counts, r.Explained, r.OwnEfficiency, r.DoseRate,
                    r.Skipped ? " (вне)" : ""));
            }
        }

        /// <summary>
        /// Строка матрицы → каналы. Бин `b` — отрезок `[(b−½)·шаг, (b+½)·шаг)`
        /// с равномерной плотностью (восемь подточек); подточка идёт в канал,
        /// чей интервал `[E(i−½), E(i+½))` её содержит, либо размывается гауссом.
        /// </summary>
        static double[] Place(double[] row, double step, double[] edges, Func<double, double> sigma, double scale)
        {
            var result = new double[edges.Length - 1];
            const int sub = 8;
            for (int b = 0; b < row.Length; b++)
            {
                double v = row[b];
                if (!(v > 0.0)) continue;
                for (int s = 0; s < sub; s++)
                {
                    double x = (b - 0.5 + (s + 0.5) / sub) * step;
                    Deposit(result, edges, x, sigma, scale * v / sub);
                }
            }

            return result;
        }

        static double[] PlacePoint(double e, double[] edges, Func<double, double> sigma, double area)
        {
            var result = new double[edges.Length - 1];
            Deposit(result, edges, e, sigma, area);
            return result;
        }

        static void Deposit(double[] result, double[] edges, double x, Func<double, double> sigma, double mass)
        {
            int n = result.Length;
            if (sigma == null)
            {
                int k = Array.BinarySearch(edges, x);
                if (k < 0) k = ~k - 1;
                if (k >= 0 && k < n) result[k] += mass;
                return;
            }

            double s = sigma(x);
            int lo = Array.BinarySearch(edges, x - 7.0 * s);
            if (lo < 0) lo = ~lo - 1;
            if (lo < 0) lo = 0;
            double prev = Phi((edges[lo] - x) / s);
            for (int k = lo; k < n; k++)
            {
                double next = Phi((edges[k + 1] - x) / s);
                result[k] += mass * (next - prev);
                prev = next;
                if (edges[k + 1] > x + 7.0 * s) break;
            }
        }

        static double Phi(double z)
        {
            return 0.5 * Erfc(-z / Math.Sqrt(2.0));
        }

        /// <summary>(`S185`, П151) Показание при снятой калибровке ширины спектра.</summary>
        static double WithoutFwhm(ResultData data, Func<double> reading)
        {
            FwhmCalibration keep = data.FwhmCalibration;
            data.FwhmCalibration = null;
            try
            {
                return reading();
            }
            finally
            {
                data.FwhmCalibration = keep;
            }
        }

        /// <summary>
        /// (`S185`, П151) σ(E) в кэВ по ПШПВ самого спектра: канал E — обратной
        /// калибровкой, ширина — `ChannelToFwhm` (каналы), в кэВ — шириной канала
        /// там же. Нулевая ширина (канал 0 у степенной модели) — почти дельта.
        /// </summary>
        static Func<double, double> OwnSigma(ResultData data)
        {
            EnergyCalibration cal = data.EnergySpectrum.EnergyCalibration;
            FwhmCalibration fwhm = data.FwhmCalibration;
            int n = data.EnergySpectrum.NumberOfChannels;
            return x =>
            {
                double ch = cal.EnergyToChannel(x, n);
                double width = cal.ChannelToEnergy(ch + 0.5) - cal.ChannelToEnergy(ch - 0.5);
                double s = fwhm.ChannelToFwhm(ch) * width / 2.3548200450309493;
                return s > 1e-6 && !double.IsNaN(s) ? s : 1e-6;
            };
        }

        /// <summary>erfc по Numerical Recipes (erfcc), относительная ошибка ниже 1.2e-7.</summary>
        static double Erfc(double x)
        {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418
                         + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587
                         + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }

        // ------------------------------------------------------------------

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Console.WriteLine(new string('-', Math.Min(100, text.Length)));
        }

        static void Ok(bool ok, string what)
        {
            checks++;
            if (!ok) failed++;
            if (!quiet || !ok) Console.WriteLine("  [" + (ok ? "ок" : "НЕТ") + "]   " + what);
        }

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static ResponseMatrix LoadMatrix(string path, GeometryModel geometry)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("  матрица {0}: {1} (формат {2})", path, refusal, fileFormat);
                return null;
            }

            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                Console.WriteLine("  ⚠ клеймо матрицы {0} не сходится с геометрией кривой — пересчитано в памяти",
                                  Path.GetFileName(path));
                matrix.Stamp = ResponseMatrix.ComputeStamp(geometry, matrix.Options);
            }

            return matrix;
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }
    }
}
