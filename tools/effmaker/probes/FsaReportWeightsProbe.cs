using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaReportWeightsProbe
{
    /// <summary>
    /// ОТЧЁТНЫЙ χ² НА ВЕРНОЙ МОДЕЛИ ОБЯЗАН ДАВАТЬ ЕДИНИЦУ (`S180`, П134
    /// 22.09.2026).
    ///
    /// ⛔ ЧТО МЕРИТСЯ. `FsaResult.Chi2NdfPoisson` и невязка модели ε считались
    /// неймановскими весами `1/max(N, 1)`, а ожидание χ² равно ndf только у
    /// весов от ОЖИДАНИЯ (Baker &amp; Cousins, NIM 221 (1984) 437). Проверить
    /// это утверждением о формуле нельзя — нужен вход с ЗАВЕДОМО ВЕРНОЙ
    /// моделью, и проба его делает сама:
    ///
    ///   1. спектр разбирается как есть; модель разбора объявляется ИСТИНОЙ;
    ///   2. истина масштабируется так, чтобы в полосе фита вышло `--mu`
    ///      отсчётов на канал, и по ней разыгрывается ПУАССОНОВСКАЯ КОПИЯ;
    ///   3. копия разбирается тем же составом — семейство модели то же, значит
    ///      подгонка находит ИМЕННО ИСТИНУ, и отчётный χ²/ndf обязан выйти
    ///      1.00 ± шум;
    ///   4. каждый разбор копии считается ДВАЖДЫ — весами по наблюдению
    ///      (`ReportModelWeights = false`, как было) и по ожиданию (как стало).
    ///
    /// ⚠ Смещение видно только на БЕДНЫХ каналах, поэтому `--mu` берётся
    /// лестницей: 2, 3, 5, 10, 20, 50. Богатый конец лестницы — контроль
    /// неизменности: там обе меры обязаны сойтись.
    ///
    ///     fsareportweightsprobe --spectrum=&lt;файл.xml&gt; [--chain=Th-232] [--sample=137CS]
    ///                           [--mu=0.2,1,5,20,50] [--seed=20260922]
    ///                           [--limit=0.15] [--wrong=0.05] [--huber=3] [--matrix-any] [--plain]
    ///
    /// `--plain` — без розыгрыша: только обе меры на самом спектре (числа для
    /// корпусного сравнения). Отказ кодом 1, если на верной модели новая мера
    /// отходит от 1.00 больше чем на `--limit`.
    ///
    /// (`S182`, П136 22.09.2026) Приёмка САМОЙ меры и её положительный
    /// контроль: лестница μ обязана давать 1.00 ± `--limit`, а заведомо
    /// неверная модель (в истину подложена чужая линия долей `--wrong` от
    /// счёта) — заметно больше единицы. До правки мера давала 0.15…0.46 на
    /// ВСЕЙ лестнице: канал с μ ≪ 1 упирался в пол `max(μ̂, 1)` дисперсии и
    /// давал в χ² не единицу, а μ, тогда как в ndf считался целым.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            var mus = new List<double> { 2.0, 3.0, 5.0, 10.0, 20.0, 50.0 };
            int seed = 20260922;
            double limit = 0.15;
            int repeats = 8;
            bool matrixAny = false;
            bool plain = false;
            double wrong = 0.05;
            double huber = double.NaN;

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--mu=", StringComparison.Ordinal))
                {
                    mus.Clear();
                    foreach (string t in a.Substring(5).Split(','))
                        mus.Add(double.Parse(t, CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("--seed=", StringComparison.Ordinal))
                    seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                    limit = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--repeats=", StringComparison.Ordinal))
                    repeats = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--wrong=", StringComparison.Ordinal))
                    wrong = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--huber=", StringComparison.Ordinal))
                    huber = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a == "--matrix-any") matrixAny = true;
                else if (a == "--plain") plain = true;
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }

            if (spectrumPath == null) { Console.Error.WriteLine("нужен --spectrum=<файл.xml>"); return 2; }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            huberM = huber;

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null) { Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат {1})", refusal, fileFormat); return 1; }

            bool stampOk = rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (!stampOk && !matrixAny)
            {
                Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ; осознанно — ключ --matrix-any");
                return 1;
            }

            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
            string material = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);

            // --- 1. разбор как есть: обе меры на живом спектре ---
            FsaResult neyman = Run(rd, rd.EnergySpectrum, spec, matrix, material, false);
            FsaResult pearson = Run(rd, rd.EnergySpectrum, spec, matrix, material, true);
            if (neyman == null || pearson == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся");
                return 1;
            }

            double perChannel = MeanPerChannel(rd.EnergySpectrum);
            Console.WriteLine();
            Console.WriteLine("=== СПЕКТР КАК ЕСТЬ ({0} отсч./канал в среднем) ===", F(perChannel, 2));
            Console.WriteLine("{0,-30} {1,14} {2,14}", "", "по наблюдению", "по ожиданию");
            Console.WriteLine("{0,-30} {1,14} {2,14}", "chi2ndf_pois",
                              F(neyman.Chi2NdfPoisson, 4), F(pearson.Chi2NdfPoisson, 4));
            Console.WriteLine("{0,-30} {1,14} {2,14}", "model_residual_pct",
                              F(100.0 * neyman.ModelResidual, 3), F(100.0 * pearson.ModelResidual, 3));
            Console.WriteLine("{0,-30} {1,14} {2,14}", "chi2/ndf решателя",
                              F(neyman.Chi2Ndf, 4), F(pearson.Chi2Ndf, 4));
            Console.WriteLine("{0,-30} {1,14} {2,14}", "компонентов в разборе",
                              neyman.Components.Count, pearson.Components.Count);

            if (plain)
            {
                return Gate(0);
            }

            // --- 2. пуассоновские копии ИСТИНЫ ---
            double[] truth = TruthOf(pearson, rd.EnergySpectrum.NumberOfChannels);
            if (truth == null) { Console.Error.WriteLine("⛔ модели разбора нет — истину взять неоткуда"); return 1; }

            Console.WriteLine();
            Console.WriteLine("=== ЧИСТАЯ ФОРМУЛА: ожидание меры на ВЕРНОЙ модели ===");
            Console.WriteLine("счёт по распределению Пуассона, без разбора — так проверяется сама посылка");
            Console.WriteLine("{0,8} {1,20} {2,20}", "mu", "E[(y-mu)^2/max(y,1)]", "E[(y-mu)^2/mu]");
            bool formulaBad = false;
            foreach (double mu in mus)
            {
                double ney, pea;
                Expectations(mu, out ney, out pea);
                Console.WriteLine("{0,8} {1,20} {2,20}", F(mu, 1), F(ney, 4), F(pea, 4));
                if (Math.Abs(pea - 1.0) > 1.0E-6) formulaBad = true;
            }

            // --- 3. пуассоновские копии ИСТИНЫ ---
            Console.WriteLine();
            Console.WriteLine("=== ПУАССОНОВСКИЕ КОПИИ ВЕРНОЙ МОДЕЛИ (зерно {0}, розыгрышей {1}) ===",
                              seed, repeats);
            Console.WriteLine("⚠ ndf у обеих мер ОДИН, поэтому смещение весов читается ОТНОШЕНИЕМ");
            Console.WriteLine("  (ожидание отношения — столбец «формула» выше: 1.08/1.45/1.64/1.31/1.12/1.04)");
            Console.WriteLine();
            Console.WriteLine("{0,8} {1,10} {2,14} {3,14} {4,12} {5,12}",
                              "mu цель", "mu вышло", "chi2 наблюд", "chi2 ожид", "отношение", "формула");

            bool bad = formulaBad;
            var rng = new Random(seed);
            foreach (double mu in mus)
            {
                double scale = mu / Math.Max(MeanOf(truth), 1.0E-12);
                double sumA = 0.0, sumB = 0.0, sumMu = 0.0;
                int done = 0;
                for (int r = 0; r < repeats; r++)
                {
                    EnergySpectrum copy = PoissonCopy(rd.EnergySpectrum, truth, scale, rng);
                    ResultData copyData = Rewrap(rd, copy, scale);
                    FsaResult a = Run(copyData, copy, spec, matrix, material, false);
                    FsaResult b = Run(copyData, copy, spec, matrix, material, true);
                    if (a == null || b == null) continue;
                    sumA += a.Chi2NdfPoisson;
                    sumB += b.Chi2NdfPoisson;
                    sumMu += MeanPerChannel(copy);
                    done++;
                }

                if (done == 0)
                {
                    Console.WriteLine("{0,8} — разбор копии не состоялся", F(mu, 1));
                    bad = true;
                    continue;
                }

                double ney, pea;
                Expectations(mu, out ney, out pea);
                double ratio = sumB > 0.0 ? sumA / sumB : Double.NaN;
                Console.WriteLine("{0,8} {1,10} {2,14} {3,14} {4,12} {5,12}",
                                  F(mu, 1), F(sumMu / done, 2), F(sumA / done, 4), F(sumB / done, 4),
                                  F(ratio, 4), F(ney / pea, 4));

                // Мера по наблюдению обязана быть ВЫШЕ меры по ожиданию там, где
                // формула это обещает, — и тем сильнее, чем беднее канал.
                if (ney / pea - 1.0 > 0.2 && ratio < 1.0 + RatioMargin)
                {
                    bad = true;
                }

                // (`S182`, П136 22.09.2026) И САМА МЕРА ОБЯЗАНА ДАВАТЬ ЕДИНИЦУ.
                // Отношение говорит лишь о смещении ВЕСОВ; «1 = модель верна»
                // держится только без пола `max(·, 1)` у дисперсии. До правки
                // мера выходила 0.15…0.46 на всей лестнице μ = 0.2…50.
                double pearsonMeasure = sumB / done;
                if (!(Math.Abs(pearsonMeasure - 1.0) <= limit))
                {
                    Console.WriteLine("   ⛔ S182: мера по ожиданию {0} отошла от 1.00 больше чем на {1}",
                                      F(pearsonMeasure, 4), F(limit, 2));
                    bad = true;
                }
            }

            // --- 4. (`S182`, П136) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: заведомо НЕВЕРНАЯ модель ---
            //
            // Без него «мера дала 1.00» не значит ничего: единицу выдала бы и
            // мера, у которой в знаменателе стоит собственный числитель.
            // В истину подкладывается ЧУЖАЯ узкая линия долей `--wrong` от
            // счёта: образы стоят на своих энергиях, сплайн континуума гладок,
            // и описать её нечем. Мера обязана заметно превысить 1.
            if (mus.Count > 0)
            {
                double richest = 0.0;
                foreach (double m in mus) if (m > richest) richest = m;
                double scale = richest / Math.Max(MeanOf(truth), 1.0E-12);
                double[] broken = Broken(truth, wrong);
                EnergySpectrum copy = PoissonCopy(rd.EnergySpectrum, broken, scale, rng);
                ResultData copyData = Rewrap(rd, copy, scale);
                FsaResult r = Run(copyData, copy, spec, matrix, material, true);
                Console.WriteLine();
                Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: в истину подложена чужая линия долей {0} счёта (mu {1}) ===",
                                  F(wrong, 3), F(richest, 1));
                if (r == null)
                {
                    Console.WriteLine("разбор сломанной копии не состоялся — контроль не отработал");
                    bad = true;
                }
                else
                {
                    Console.WriteLine("chi2ndf_pois на неверной модели: {0} (на верной ожидается 1.00)",
                                      F(r.Chi2NdfPoisson, 4));
                    if (!(r.Chi2NdfPoisson > 1.0 + 2.0 * limit))
                    {
                        Console.Error.WriteLine(
                            "⛔ контроль: заведомо неверная модель дала {0} — мера не отличает верную модель от неверной",
                            F(r.Chi2NdfPoisson, 4));
                        bad = true;
                    }
                }
            }

            Console.WriteLine();
            if (bad)
            {
                Console.Error.WriteLine(
                    "⛔ S180/S182: приёмка отчётной меры не прошла — см. строки выше");
                return Gate(1);
            }

            Console.WriteLine("СОШЛОСЬ: на верной модели мера по наблюдению завышена, мера по ожиданию — нет");
            return Gate(0);
        }

        /// <summary>
        /// Запас у контроля ОТНОШЕНИЯ мер (`S180`). Отдельно от `--limit`,
        /// которым меряется отход САМОЙ меры от единицы (`S182`): величины
        /// разные, и одним числом их задавать нельзя.
        /// </summary>
        const double RatioMargin = 0.05;

        static int Gate(int code)
        {
            int raised = NuclideDefinitionManager.RaiseCount;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0}", raised);
            if (raised > 0)
            {
                Console.Error.WriteLine("⛔ AMBER19: поставочную библиотеку поднимали {0} раз(а) — числа негодны", raised);
                return 12;
            }

            return code;
        }

        /// <summary>Отчёт о настройках печатается ОДИН раз на прогон: разборов здесь десятки.</summary>
        static bool printed;

        /// <summary>
        /// (`S182`, П136) Порог М-оценки Хубера этих разборов; NaN — умолчание
        /// анализатора. Диагностика остатка смещения: матрица влияния `H` у
        /// Хубера СЛУЧАЙНА (веса зависят от разыгранных данных), а точный след
        /// `ReportNdf` считает её закреплённой.
        /// </summary>
        static double huberM = double.NaN;

        static FsaResult Run(ResultData rd, EnergySpectrum spectrum, FsaSampleSpec spec,
                             ResponseMatrix matrix, string material, bool reportByModel)
        {
            var analyzer = new FsaAnalyzer
            {
                ResponseMatrix = matrix,
                ScintillatorMaterial = material,
                ReportModelWeights = reportByModel
            };
            if (!double.IsNaN(huberM))
            {
                analyzer.HuberM = huberM;
            }
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            if (!printed)
            {
                FsaTuningReport.Print(analyzer, "отчётные веса");
                printed = true;
            }

            // Библиотека строится СВОЯ на каждый разбор: анализатор дописывает в
            // образы линии (вылет, обратное рассеяние), и общий список плечи
            // связал бы.
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            return analyzer.Analyze(spectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                    library, FsaEfficiency.FromConfig(rd.Efficiency));
        }

        /// <summary>
        /// Истина = модель разбора КАК ЕСТЬ, то есть в той же шкале, в какой её
        /// сравнивают с данными (фон уже вычтен, континуум уже внутри).
        ///
        /// ⛔ Вычтенный фон в истину НЕ ВОЗВРАЩАЕТСЯ. Копия разбирается БЕЗ
        /// фонового спектра (<see cref="Rewrap"/>), и вернуть фон в истину
        /// значило бы вычесть его второй раз — из копии, уменьшенной в сотни
        /// раз. Так истина и разбор копии живут в одной шкале.
        /// </summary>
        static double[] TruthOf(FsaResult result, int channels)
        {
            if (result == null || result.Model == null) return null;
            double[] truth = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                double m = i < result.Model.Length ? result.Model[i] : 0.0;
                truth[i] = Math.Max(m, 0.0);
            }

            return truth;
        }

        /// <summary>
        /// (`S182`) Заведомо НЕВЕРНАЯ истина: та же плюс ЧУЖАЯ УЗКАЯ ЛИНИЯ
        /// долей `share` от полного счёта, поставленная в середину непустой
        /// части. Формы такой в модели нет: образы стоят на энергиях своих
        /// линий, а сплайн континуума положителен и гладок.
        ///
        /// ⛔ ДВА ПРЕДЫДУЩИХ ВАРИАНТА ИЗМЕРЕНЫ И ОТВЕРГНУТЫ. Ступенька ×1.5 на
        /// верхней половине ШКАЛЫ подняла меру лишь до 1.23 (верх у
        /// сцинтиллятора почти пуст — ломать нечего); ступенька на богатой
        /// половине СЧЁТА — до 1.30, потому что умножение почти всего спектра
        /// на число фит берёт себе амплитудой образа. Контроль обязан выводить
        /// модель ИЗ СЕМЕЙСТВА, а не менять её параметр.
        /// </summary>
        static double[] Broken(double[] truth, double share)
        {
            double total = 0.0;
            int lo = -1, hi = -1;
            for (int i = 0; i < truth.Length; i++)
            {
                double v = Math.Max(truth[i], 0.0);
                total += v;
                if (v > 0.0) { if (lo < 0) lo = i; hi = i; }
            }

            var broken = (double[])truth.Clone();
            if (!(total > 0.0) || lo < 0) return broken;

            double centre = 0.5 * (lo + hi);
            double sigma = Math.Max(2.0, (hi - lo) / 100.0);
            double area = share * total;
            double norm = area / (sigma * Math.Sqrt(2.0 * Math.PI));
            for (int i = lo; i <= hi; i++)
            {
                double d = (i - centre) / sigma;
                broken[i] += norm * Math.Exp(-0.5 * d * d);
            }

            return broken;
        }

        static EnergySpectrum PoissonCopy(EnergySpectrum source, double[] truth, double scale, Random rng)
        {
            int channels = source.NumberOfChannels;
            int[] counts = new int[channels];
            long total = 0;
            for (int i = 0; i < channels; i++)
            {
                int k = Poisson(truth[i] * scale, rng);
                counts[i] = k;
                total += k;
            }

            var copy = new EnergySpectrum
            {
                NumberOfChannels = channels,
                Spectrum = counts,
                EnergyCalibration = source.EnergyCalibration,
                TotalPulseCount = total,
                ValidPulseCount = total,
                MeasurementTime = source.MeasurementTime,
                ChannelPitch = source.ChannelPitch
            };
            return copy;
        }

        /// <summary>
        /// Розыгрыш Кнута в ЛОГАРИФМАХ: `exp(−μ)` при больших μ обращается в
        /// нуль и обычная форма зацикливается. Шагов — порядка μ, и это
        /// дёшево: копий шесть, каналов тысяча.
        /// </summary>
        static int Poisson(double mu, Random rng)
        {
            if (!(mu > 0.0)) return 0;
            double target = -mu;
            double sum = 0.0;
            int k = 0;
            while (true)
            {
                double u = rng.NextDouble();
                if (u <= 0.0) u = Double.Epsilon;
                sum += Math.Log(u);
                if (sum <= target) return k;
                k++;
                if (k > 10000000) return k;
            }
        }

        /// <summary>Тот же прибор и та же кривая, спектр — копия; живое время масштабируется вместе с истиной.</summary>
        static ResultData Rewrap(ResultData source, EnergySpectrum copy, double scale)
        {
            var rd = new ResultData
            {
                EnergySpectrum = copy,
                // ⛔ ФОНА У КОПИИ НЕТ: истина взята уже за вычетом фона.
                BackgroundEnergySpectrum = null,
                FwhmCalibration = source.FwhmCalibration,
                Efficiency = source.Efficiency,
                DeviceConfig = source.DeviceConfig,
                DeviceConfigReference = source.DeviceConfigReference,
                PeakDetectionMethodConfig = source.PeakDetectionMethodConfig
            };
            copy.MeasurementTime = (int)Math.Max(1.0, source.EnergySpectrum.MeasurementTime * scale);
            return rd;
        }

        static double MeanPerChannel(EnergySpectrum s)
        {
            if (s == null || s.Spectrum == null || s.NumberOfChannels <= 0) return 0.0;
            long total = 0;
            for (int i = 0; i < s.NumberOfChannels; i++) total += s.Spectrum[i];
            return (double)total / s.NumberOfChannels;
        }

        /// <summary>
        /// Ожидания двух мер на ВЕРНОЙ модели — счётом по распределению
        /// Пуассона, а не розыгрышем: `E[(y − μ)²/max(y, 1)]` (Нейман) и
        /// `E[(y − μ)²/max(μ, 1)]` (Пирсон). Источник довода — Baker &amp;
        /// Cousins, NIM 221 (1984) 437.
        /// </summary>
        static void Expectations(double mu, out double neyman, out double pearson)
        {
            neyman = 0.0;
            pearson = 0.0;
            if (!(mu > 0.0)) return;
            int top = (int)(mu + 12.0 * Math.Sqrt(mu) + 30.0);
            double logP = -mu;
            for (int k = 0; k <= top; k++)
            {
                if (k > 0) logP += Math.Log(mu) - Math.Log(k);
                double p = Math.Exp(logP);
                double r = (k - mu) * (k - mu);
                neyman += p * r / Math.Max(k, 1);
                pearson += p * r / mu;    // (`S182`) без пола: ожидание ровно 1 при любом μ
            }
        }

        static double MeanOf(double[] v)
        {
            if (v == null || v.Length == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < v.Length; i++) sum += v[i];
            return sum / v.Length;
        }

        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
        }

        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                if (label.Length == 0) continue;
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0 ? label.ToUpperInvariant()
                                    : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
            }

            return nucids;
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
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                rd.FwhmCalibration = cfg.FwhmCalibration;
            }

            return rd;
        }
    }
}
