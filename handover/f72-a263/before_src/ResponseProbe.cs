using BecquerelMonitor.EfficiencyMaker;
using GadrasShared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ResponseProbe
{
    /// <summary>
    /// Отклик детектора целиком: не одна точка в пике, а всё распределение
    /// поглощённой энергии. Сверяется с колонками GADRAS — комптон, одиночный и
    /// двойной вылет, полная эффективность (пункт A2 плана в
    /// `tools/effmaker/handover-2026-08-05.md`).
    ///
    /// ПРИЛОЖЕНИЕ НЕ ТРОГАЕТСЯ, и это не самоограничение, а следствие того, как
    /// устроен симулятор. `Efficiency(E)` считает истории с условием
    /// «вылетело ≤ PeakHalfWidthKev», то есть это **функция распределения
    /// вылетевшей энергии** F(w). Сканируя порог, получаем всё распределение —
    /// тем же кодом, той же физикой, без второго источника правды. Поглощённая
    /// энергия равна E − вылетело, поэтому спектр отклика — это F, посчитанная
    /// в узлах и продифференцированная.
    ///
    /// `ResetStream` перед каждым порогом даёт ОДНИ И ТЕ ЖЕ истории, поэтому
    /// разности считаются по общим случайным числам: F монотонна по построению,
    /// а шум разностей на порядок меньше, чем у независимых прогонов.
    ///
    /// Что извлекается и с чем сверяется:
    ///
    ///   пик полного поглощения   F(0)                        -> колонка Peak
    ///   одиночный вылет          пик F вокруг w = 511        -> SE
    ///   двойной вылет            пик F вокруг w = 1022       -> DE
    ///   всё, что дало отсчёт     F(E - ε)                    -> PTOT
    ///   комптон                  всё минус пик минус вылеты  -> PCOM
    ///
    /// Сверяются ОТНОШЕНИЯ к пику, а не абсолютные величины: как именно
    /// нормированы колонки GADRAS, из поставки не следует (у `PTOT` значения
    /// выше 100 %), а отношение от соглашения о нормировке не зависит.
    ///
    /// Германий не считается вовсе — вне предмета (§1 завещания).
    ///
    ///     responseprobe [каталог с gadras] [--n=200000] [--csv=out.csv]
    ///                   [--spectrum=NaI 3x3@662] [--png=<файл>]
    ///                   [--no-xray] [--no-coherent-pass] [--no-brems] [--no-scatter]
    ///
    /// Ключи поправок пробрасываются: без контрольного прогона с выключенной
    /// поправкой нельзя отличить своё влияние от чужого.
    /// </summary>
    static class Program
    {
        /// <summary>Энергии сверки. Ниже 200 кэВ рождения пар нет, вылетов тоже.</summary>
        static readonly double[] Grid = { 200, 400, 662, 1000, 1500, 2000, 2614 };

        const double ElectronMassKev = 510.99895;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string dir = null, csvPath = null, pngPath = null, spectrumSpec = null;
            var geometryFiles = new List<string>();
            int histories = 200000;
            bool xray = true, coherent = true, brems = true, scatter = true;
            bool fast = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--n=", StringComparison.Ordinal))
                    histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--png=", StringComparison.Ordinal)) pngPath = a.Substring(6);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumSpec = a.Substring(11);
                else if (a == "--no-xray") xray = false;
                else if (a == "--no-coherent-pass") coherent = false;
                else if (a == "--no-brems") brems = false;
                else if (a == "--no-scatter") scatter = false;
                else if (a == "--fast") fast = true;
                else if (a.StartsWith("--geometry=", StringComparison.Ordinal))
                    geometryFiles.Add(a.Substring(11));
                else if (!a.StartsWith("--", StringComparison.Ordinal)) dir = a;
            }

            // Свои геометрии из файлов `.in` — для вопроса «одна матрица на
            // кристалл или на каждую геометрию»: у моделей Nano16Pro кристалл
            // один и тот же, а расположение источника разное.
            if (geometryFiles.Count > 0)
            {
                double energy = 662.0;
                if (spectrumSpec != null)
                {
                    int at = spectrumSpec.LastIndexOf('@');
                    if (at >= 0)
                    {
                        energy = double.Parse(spectrumSpec.Substring(at + 1),
                                              CultureInfo.InvariantCulture);
                    }
                }

                foreach (string file in geometryFiles)
                {
                    GeometryModel own = GeometryModel.Load(file);
                    EfficiencySimulator ownSim = Build(own, histories, xray, coherent, brems, scatter);
                    string outPath = pngPath != null
                        ? Path.Combine(pngPath, Path.GetFileNameWithoutExtension(file) + "_"
                              + energy.ToString("F0", CultureInfo.InvariantCulture) + ".csv")
                        : Path.ChangeExtension(file, null) + "_response.csv";
                    DumpOwn(ownSim, own, energy, outPath);
                }

                return 0;
            }

            if (dir == null)
            {
                dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                   @"..\..\tools\interspec\gadras");
            }

            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine("нет каталога {0}", dir);
                return 2;
            }

            var csv = new StringBuilder();
            csv.AppendLine("detector,energy_kev,quantity,gadras_over_peak,ours_over_peak,ratio");

            foreach (string sub in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                GadrasDetector det;
                try
                {
                    det = GadrasDetector.Read(sub, name);
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0}: пропущен — {1}", name, e.Message);
                    continue;
                }

                if (det.IsGermanium)
                {
                    continue;   // вне предмета
                }

                if (det.CrystalLengthCm <= 0.0 || det.CrystalWidthCm <= 0.0)
                {
                    Console.WriteLine("{0}: пропущен — в Detector.dat нет размера кристалла", name);
                    continue;
                }

                if (spectrumSpec != null && !spectrumSpec.StartsWith(name + "@", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GeometryModel model = det.ToModel();
                EfficiencySimulator sim = Build(model, histories, xray, coherent, brems, scatter);
                double omega = det.SolidAngleFraction;

                if (spectrumSpec != null)
                {
                    double energy = double.Parse(spectrumSpec.Substring(name.Length + 1),
                                                 CultureInfo.InvariantCulture);
                    if (fast)
                    {
                        CompareFast(sim, det, energy, omega, pngPath);
                    }
                    else
                    {
                        DumpSpectrum(sim, det, energy, omega, pngPath ?? (name + "_" + energy + ".csv"));
                    }

                    return 0;
                }

                Console.WriteLine();
                Console.WriteLine("=== {0}: {1}, D {2:F2} см, H {3:F2} см, Ω/4π {4:E3}",
                                  name, det.CrystalName, det.CrystalWidthCm,
                                  det.CrystalLengthCm, omega);
                Console.WriteLine("  энергия      величина    GADRAS/пик    наш/пик   наш/GADRAS");

                foreach (double e in Grid)
                {
                    double refPeak = det.Reference(GadrasDetector.Column.Peak, e);
                    if (double.IsNaN(refPeak) || !(refPeak > 0.0))
                    {
                        continue;
                    }

                    Shares ours = Measure(sim, e);
                    if (!(ours.Peak > 0.0))
                    {
                        continue;
                    }

                    // Доля провзаимодействовавших — проверка не по GADRAS, а по
                    // нашей же таблице ослабления: для далёкого точечного
                    // источника путь в кристалле почти у всех лучей равен его
                    // высоте, и доля обязана сойтись с 1 − exp(−μ·H). Коэффициент
                    // берётся БЕЗ когерентного: рэлеевское рассеяние энергии не
                    // передаёт и отсчёта не даёт.
                    double reached = Reached(sim, e);
                    double mu = model.Crystal.LinearAttenuationWithoutCoherent(e);
                    double thickness = model.CrystalHeight / GeometryModel.MmPerCm;
                    double expected = 1.0 - Math.Exp(-mu * thickness);
                    Report(csv, name, e, "провзаим.", expected,
                           reached > 0.0 ? ours.Total / reached : double.NaN);

                    Report(csv, name, e, "комптон/пик", double.NaN,
                           (ours.Total - ours.Peak - ours.Single - ours.Double) / ours.Peak);
                    if (e > 2.0 * ElectronMassKev + 100.0)
                    {
                        Report(csv, name, e, "вылет 1",
                               det.Reference(GadrasDetector.Column.SingleEscape, e) / refPeak,
                               ours.Single / ours.Peak);
                        Report(csv, name, e, "вылет 2",
                               det.Reference(GadrasDetector.Column.DoubleEscape, e) / refPeak,
                               ours.Double / ours.Peak);
                    }

                    // Пик в абсолюте — контроль, что сцена та же, что в GadrasProbe.
                    Console.WriteLine("  {0,7:F0}  {1,12}  {2,10}  {3,10:F4}  {4,10:F3}",
                                      e, "пик, %", "", 100.0 * ours.Peak / omega,
                                      100.0 * ours.Peak / omega / refPeak);
                }
            }

            if (csvPath != null)
            {
                File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("записано {0}", csvPath);
            }

            return 0;
        }

        static void Report(StringBuilder csv, string detector, double energy, string quantity,
                           double reference, double ours)
        {
            double ratio = reference > 0.0 ? ours / reference : double.NaN;
            Console.WriteLine("  {0,7:F0}  {1,12}  {2,10:F4}  {3,10:F4}  {4,10:F3}",
                              energy, quantity, reference, ours, ratio);
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:R},{4:R},{5:R}", detector, energy, quantity, reference, ours, ratio));
        }

        static EfficiencySimulator Build(GeometryModel model, int histories,
                                         bool xray, bool coherent, bool brems, bool scatter)
        {
            var sim = new EfficiencySimulator(model) { Histories = histories };
            SetIfPresent(sim, "XrayEscape", xray);
            SetIfPresent(sim, "CoherentPassesThrough", coherent);
            SetIfPresent(sim, "Bremsstrahlung", brems);
            SetIfPresent(sim, "SingleScatter", scatter);
            return sim;
        }

        /// <summary>
        /// Ключи поправок ставятся отражением: набор их растёт от сессии к
        /// сессии, и проба не должна разваливаться от появления или исчезновения
        /// одного из них — она должна СКАЗАТЬ об этом.
        /// </summary>
        static void SetIfPresent(EfficiencySimulator sim, string name, bool value)
        {
            var field = typeof(EfficiencySimulator).GetField(name);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(sim, value);
                return;
            }

            var property = typeof(EfficiencySimulator).GetProperty(name);
            if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            {
                property.SetValue(sim, value, null);
                return;
            }

            if (!value)
            {
                throw new InvalidOperationException(
                    "ключ " + name + " у симулятора не найден, а выключить его просили");
            }

            Console.Error.WriteLine("предупреждение: ключа {0} у симулятора нет", name);
        }

        struct Shares
        {
            public double Peak;     // вылетело 0
            public double Single;   // вылетело ~511 сверх подложки
            public double Double;   // вылетело ~1022 сверх подложки
            public double Total;    // вылетело меньше всей энергии, то есть отсчёт был
        }

        /// <summary>
        /// Доли по функции распределения вылетевшей энергии. Каждое значение F
        /// снимается на СВОЁМ прогоне с одинаковым потоком случайных чисел.
        /// </summary>
        static Shares Measure(EfficiencySimulator sim, double energyKev)
        {
            const double Window = 25.0;   // полуширина окна вокруг линии вылета
            var s = new Shares();
            s.Peak = At(sim, energyKev, 0.0);
            s.Total = At(sim, energyKev, energyKev - 1.0);

            if (energyKev > 2.0 * ElectronMassKev + 100.0)
            {
                s.Single = PeakOver(sim, energyKev, ElectronMassKev, Window);
                s.Double = PeakOver(sim, energyKev, 2.0 * ElectronMassKev, Window);
            }

            return s;
        }

        /// <summary>
        /// Площадь пика функции распределения вокруг <paramref name="centre"/>
        /// за вычетом подложки. Подложка — комптоновский континуум вылетевшей
        /// энергии, он под пиком есть всегда, и без её вычитания «вылет» вбирал
        /// бы часть континуума. Оценивается по двум соседним окнам той же
        /// ширины, как это делается с площадью обычного пика.
        /// </summary>
        static double PeakOver(EfficiencySimulator sim, double energyKev, double centre, double window)
        {
            double lo = centre - window, hi = centre + window;
            if (lo <= 0.0 || hi >= energyKev)
            {
                return 0.0;
            }

            double inside = At(sim, energyKev, hi) - At(sim, energyKev, lo);
            double leftLo = Math.Max(0.0, lo - 2.0 * window);
            double rightHi = Math.Min(energyKev - 1.0, hi + 2.0 * window);
            double left = At(sim, energyKev, lo) - At(sim, energyKev, leftLo);
            double right = At(sim, energyKev, rightHi) - At(sim, energyKev, hi);

            double leftWidth = lo - leftLo, rightWidth = rightHi - hi;
            double density = 0.0;
            int used = 0;
            if (leftWidth > 0.0) { density += left / leftWidth; used++; }
            if (rightWidth > 0.0) { density += right / rightWidth; used++; }
            if (used > 0) { density /= used; }

            return Math.Max(0.0, inside - density * (hi - lo));
        }

        /// <summary>
        /// Доля квантов, дошедших до кристалла без ослабления. Считается тем же
        /// симулятором с `ScoreEntranceOnly`: это знаменатель, относительно
        /// которого доля провзаимодействовавших сравнима с 1 − exp(−μ·H).
        /// </summary>
        static double Reached(EfficiencySimulator sim, double energyKev)
        {
            sim.ScoreEntranceOnly = true;
            sim.ResetStream((ulong)sim.Seed);
            double relErr;
            double value = sim.Efficiency(energyKev, out relErr);
            sim.ScoreEntranceOnly = false;
            return value;
        }

        /// <summary>F(w) — доля историй, у которых вылетело не больше w.</summary>
        static double At(EfficiencySimulator sim, double energyKev, double threshold)
        {
            sim.PeakHalfWidthKev = threshold;
            // Один и тот же поток на всех порогах: разности берутся по общим
            // случайным числам, иначе шум разности вдвое больше самой разности.
            sim.ResetStream((ulong)sim.Seed);
            double relErr;
            return sim.Efficiency(energyKev, out relErr);
        }

        /// <summary>
        /// Отклик за ОДИН прогон (`Response`) против отклика, снятого
        /// сканированием порога. Оба должны дать одно и то же: гистограмма не
        /// тянет случайных чисел, значит истории у них общие. Расхождение здесь
        /// означало бы, что раскладывание по бинам считает не ту величину,
        /// которую отсекает порог, — а это единственное, что новый метод делает
        /// самостоятельно.
        /// </summary>
        static void CompareFast(EfficiencySimulator sim, GadrasDetector det,
                                double energyKev, double omega, string outDir)
        {
            const double Bin = 10.0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double relErr;
            sim.PeakHalfWidthKev = 0.0;
            sim.ResetStream((ulong)sim.Seed);
            double[] fastHistogram = sim.Response(energyKev, Bin, out relErr);
            double fastMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            int bins = fastHistogram.Length;
            double[] scanned = new double[bins];

            // Порог идёт по ВЫЛЕТЕВШЕЙ энергии, бин — по ПОГЛОЩЁННОЙ, поэтому
            // разность между соседними порогами кладётся в зеркальный бин.
            double previous = 0.0;
            for (int b = 0; b < bins; b++)
            {
                double threshold = b * Bin;
                double f = At(sim, energyKev, threshold);
                int target = (int)((energyKev - threshold) / Bin + 0.5);
                if (target >= 0 && target < bins)
                {
                    scanned[target] = f - previous;
                }

                previous = f;
            }

            double scanMs = sw.Elapsed.TotalMilliseconds;

            double sumFast = 0.0, sumScan = 0.0, worst = 0.0;
            int worstBin = -1;
            for (int b = 0; b < bins; b++)
            {
                sumFast += fastHistogram[b];
                sumScan += scanned[b];
                double diff = Math.Abs(fastHistogram[b] - scanned[b]);
                if (diff > worst) { worst = diff; worstBin = b; }
            }

            Console.WriteLine("{0} @ {1:F0} кэВ, бин {2:F0} кэВ, {3} бинов", det.Name, energyKev, Bin, bins);
            Console.WriteLine("  один прогон:        сумма {0:E4}, {1:F0} мс", sumFast / omega, fastMs);
            Console.WriteLine("  сканирование порога: сумма {0:E4}, {1:F0} мс", sumScan / omega, scanMs);
            Console.WriteLine("  выигрыш по времени: {0:F0} раз", scanMs / Math.Max(1.0, fastMs));
            Console.WriteLine("  худшее расхождение бина: {0:E3} (бин {1}), от суммы {2:P3}",
                              worst / omega, worstBin, worst / Math.Max(1e-30, sumFast));

            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                var rows = new List<string> { "deposited_kev,fast,scanned" };
                for (int b = 0; b < bins; b++)
                {
                    rows.Add(string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}",
                                           b * Bin, fastHistogram[b] / omega, scanned[b] / omega));
                }

                File.WriteAllLines(Path.Combine(outDir, "fast_vs_scan.csv"), rows, new UTF8Encoding(false));
            }
        }

        /// <summary>Спектр отклика по нашему файлу геометрии.</summary>
        static void DumpOwn(EfficiencySimulator sim, GeometryModel model,
                            double energyKev, string path)
        {
            const double Step = 10.0;
            var rows = new List<string> { "escaped_kev,deposited_kev,cdf,density_per_kev" };
            double previous = 0.0;
            for (double w = 0.0; w <= energyKev; w += Step)
            {
                double f = At(sim, energyKev, w);
                double density = w > 0.0 ? (f - previous) / Step : f;
                rows.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:R},{1:R},{2:R},{3:R}", w, energyKev - w, f, density));
                previous = f;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllLines(path, rows, new UTF8Encoding(false));
            Console.WriteLine("{0} @ {1:F0} кэВ -> {2}", model.Name, energyKev, path);
        }

        /// <summary>
        /// Полный спектр отклика одной точки — чтобы посмотреть глазами на
        /// комптоновский край, плато и пики вылета. Пишется CSV.
        /// </summary>
        static void DumpSpectrum(EfficiencySimulator sim, GadrasDetector det,
                                 double energyKev, double omega, string path)
        {
            const double Step = 10.0;
            var rows = new List<string>();
            rows.Add("escaped_kev,deposited_kev,cdf,density_per_kev");
            double previous = 0.0;
            for (double w = 0.0; w <= energyKev; w += Step)
            {
                double f = At(sim, energyKev, w);
                double density = w > 0.0 ? (f - previous) / Step : f;
                rows.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:R},{1:R},{2:R},{3:R}", w, energyKev - w, f / omega, density / omega));
                previous = f;
            }

            File.WriteAllLines(path, rows, new UTF8Encoding(false));
            Console.WriteLine("{0} @ {1:F0} кэВ: спектр отклика записан в {2}",
                              det.Name, energyKev, path);
        }
    }
}
