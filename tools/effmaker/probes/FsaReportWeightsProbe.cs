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
    ///                           [--limit=0.15] [--wrong=0.05] [--huber=3] [--pool=5] [--diag]
    ///                           [--set=Имя=значение] [--exact] [--dump=файл.csv]
    ///                           [--matrix-any] [--plain]
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
    ///
    /// (`S182`, П154 24.09.2026) После снятия пола мера на верной модели
    /// держалась 0.70…0.76 при μ ≤ 3: пирсоновский член почти пустого
    /// канала несмещён, но его ожидание несут редкие события (вклад ≈ 1/μ).
    /// Мера умолчания считается по ЯЧЕЙКАМ (<c>ReportPoolVariance</c>),
    /// приёмка `S182` — ею (столбец «мера ячейками»); отношение `S180` —
    /// поканально (`--pool=0` у обоих плеч) и только справочно (гейт снят,
    /// довод у печати). `--pool=` меняет порог плеча умолчания; `--diag`
    /// раскладывает поканальную меру по корзинам истинного μ; `--set=`
    /// ставит свойство анализатора всем разборам прогона.
    ///
    /// (`S193`, П156 24.09.2026) До правки нуля съёмки (<c>ZeroFromRun</c>)
    /// приёмка меры шла только с `--set=AnchorScale=false`: с привязкой
    /// истина пробы на μ ≥ 10 копией не воспроизводилась (поканальный член в
    /// каналах μ ≥ 10 — 1.22 / 1.68 при μ = 20 / 50), потому что нуль света
    /// считался прямой в КАНАЛАХ, а карта "adc" ставит свет прямой в ЭНЕРГИИ,
    /// и нуль не был неподвижной точкой. Теперь умолчание даёт 0.94…1.02 на
    /// лестнице 0.2…50 (член в каналах μ ≥ 10: 0.95 / 0.96 / 0.97 при
    /// μ = 5 / 20 / 50). `--exact` — копия без розыгрыша (детерминированное
    /// расхождение разбора с истиной отдельно от шума), `--dump=<файл.csv>` —
    /// поканально истина, средняя модель копий и средний счёт.
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
            double pool = double.NaN;

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
                else if (a.StartsWith("--pool=", StringComparison.Ordinal))
                    pool = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a == "--matrix-any") matrixAny = true;
                else if (a == "--plain") plain = true;
                else if (a == "--diag") diag = true;
                else if (a == "--exact") exact = true;
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    // (`S182`, П154) Разводка причин: `--set=PileUp=false` и т.п. —
                    // свойство анализатора ставится ВСЕМ разборам прогона (и
                    // разбору, дающему истину, и копиям). Имя, которого нет, — отказ.
                    string kv = a.Substring(6);
                    int eq = kv.IndexOf('=');
                    System.Reflection.PropertyInfo pi = eq > 0 ? typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq)) : null;
                    if (pi == null || !pi.CanWrite) { Console.Error.WriteLine("нет свойства анализатора: {0}", kv); return 2; }
                    sets.Add(new KeyValuePair<System.Reflection.PropertyInfo, object>(pi,
                        Convert.ChangeType(kv.Substring(eq + 1), pi.PropertyType, CultureInfo.InvariantCulture)));
                }
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
            FsaResult neyman = Run(rd, rd.EnergySpectrum, spec, matrix, material, false, 0.0);
            FsaResult pearson = Run(rd, rd.EnergySpectrum, spec, matrix, material, true, 0.0);
            FsaResult pooled = Run(rd, rd.EnergySpectrum, spec, matrix, material, true, pool);
            if (neyman == null || pearson == null || pooled == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся");
                return 1;
            }

            double perChannel = MeanPerChannel(rd.EnergySpectrum);
            Console.WriteLine();
            Console.WriteLine("=== СПЕКТР КАК ЕСТЬ ({0} отсч./канал в среднем) ===", F(perChannel, 2));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "", "по наблюдению", "ожид., канал", "ожид., ячейки");
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "chi2ndf_pois",
                              F(neyman.Chi2NdfPoisson, 4), F(pearson.Chi2NdfPoisson, 4), F(pooled.Chi2NdfPoisson, 4));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "model_residual_pct",
                              F(100.0 * neyman.ModelResidual, 3), F(100.0 * pearson.ModelResidual, 3),
                              F(100.0 * pooled.ModelResidual, 3));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "chi2/ndf решателя",
                              F(neyman.Chi2Ndf, 4), F(pearson.Chi2Ndf, 4), F(pooled.Chi2Ndf, 4));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "шкала: gain",
                              F(neyman.Gain, 6), F(pearson.Gain, 6), F(pooled.Gain, 6));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "шкала: сдвиг, кан.",
                              F(neyman.OffsetChannels, 3), F(pearson.OffsetChannels, 3), F(pooled.OffsetChannels, 3));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "свет: beta",
                              F(neyman.AnchorLightBeta, 6), F(pearson.AnchorLightBeta, 6), F(pooled.AnchorLightBeta, 6));
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "шкала: якорей",
                              neyman.ScaleAnchorsUsed, pearson.ScaleAnchorsUsed, pooled.ScaleAnchorsUsed);
            Console.WriteLine("{0,-30} {1,14} {2,14} {3,14}", "компонентов в разборе",
                              neyman.Components.Count, pearson.Components.Count, pooled.Components.Count);
            Console.WriteLine("привязка истины: {0}", pearson.AnchorNote ?? "—");

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
            Console.WriteLine("⚠ отношение «наблюд/ожид» — справочно: у Неймана пол max(N,1) остался, у Пирсона снят (П136),");
            Console.WriteLine("  поэтому оно мерит пол, а не веса; приёмка — столбец «мера ячейками» (S182)");
            Console.WriteLine();
            Console.WriteLine("{0,8} {1,10} {2,14} {3,14} {4,12} {5,12} {6,14}",
                              "mu цель", "mu вышло", "chi2 наблюд", "chi2 ожид", "отношение", "формула", "мера ячейками");

            bool bad = formulaBad;
            var rng = new Random(seed);
            foreach (double mu in mus)
            {
                double scale = mu / Math.Max(MeanOf(truth), 1.0E-12);
                double sumA = 0.0, sumB = 0.0, sumP = 0.0, sumMu = 0.0;
                int done = 0;
                for (int r = 0; r < repeats; r++)
                {
                    EnergySpectrum copy = exact ? ExactCopy(rd.EnergySpectrum, truth, scale)
                                                : PoissonCopy(rd.EnergySpectrum, truth, scale, rng);
                    ResultData copyData = Rewrap(rd, copy, scale);
                    // Отношение `S180` меряется на ОДНИХ ячейках-каналах (`--pool=0`
                    // у обоих плеч): ndf у них один. Приёмка `S182` — плечом
                    // умолчания (ячейки), тем, что печатается в `runs.csv`.
                    FsaResult a = Run(copyData, copy, spec, matrix, material, false, 0.0);
                    FsaResult b = Run(copyData, copy, spec, matrix, material, true, 0.0);
                    FsaResult c = Run(copyData, copy, spec, matrix, material, true, pool);
                    if (a == null || b == null || c == null) continue;
                    sumA += a.Chi2NdfPoisson;
                    sumB += b.Chi2NdfPoisson;
                    sumP += c.Chi2NdfPoisson;
                    sumMu += MeanPerChannel(copy);
                    if (diag) Diag.Add(b, copy, truth, scale);
                    if (diag && r == 0) Console.WriteLine("   привязка копии: {0}", b.AnchorNote ?? "—");
                    if (dumpPath != null) Dump.Add(mu, b, copy, truth, scale);
                    done++;
                }
                if (diag) Diag.Print(mu, done);
                if (dumpPath != null) Dump.Flush(mu, done, dumpPath);

                if (done == 0)
                {
                    Console.WriteLine("{0,8} — разбор копии не состоялся", F(mu, 1));
                    bad = true;
                    continue;
                }

                double ney, pea;
                Expectations(mu, out ney, out pea);
                double ratio = sumB > 0.0 ? sumA / sumB : Double.NaN;
                Console.WriteLine("{0,8} {1,10} {2,14} {3,14} {4,12} {5,12} {6,14}",
                                  F(mu, 1), F(sumMu / done, 2), F(sumA / done, 4), F(sumB / done, 4),
                                  F(ratio, 4), F(ney / pea, 4), F(sumP / done, 4));

                // ⛔ (`S182`, П154 24.09.2026) Гейт отношения `S180` СНЯТ, печать
                // оставлена. Его посылка — «ndf у мер один, разнятся лишь веса» —
                // перестала быть верной 22.09.2026, когда П136 снял пол
                // `max(μ̂, 1)` у Пирсона, а у Неймана пол `max(N, 1)` остался:
                // отношение с тех пор мерит ПОЛ, а не веса (на HEAD 24.09.2026
                // 0.33…0.43 при формуле 1.08…1.64, гейт красен на любом входе).
                // Сама мера по ожиданию проверяется напрямую приёмкой `S182`
                // ниже; плечо Неймана в приложении не живёт (умолчание —
                // `ReportModelWeights`).
                if (ney / pea - 1.0 > 0.2 && ratio < 1.0 + RatioMargin)
                {
                    Console.WriteLine("   (S180, справочно) отношение {0} ниже 1+{1}: мерит пол Неймана, не веса",
                                      F(ratio, 4), F(RatioMargin, 2));
                }

                // (`S182`, П136 22.09.2026) И САМА МЕРА ОБЯЗАНА ДАВАТЬ ЕДИНИЦУ.
                // Отношение говорит лишь о смещении ВЕСОВ; «1 = модель верна»
                // держится только без пола `max(·, 1)` у дисперсии. До правки
                // мера выходила 0.15…0.46 на всей лестнице μ = 0.2…50.
                double pearsonMeasure = sumP / done;
                if (!(Math.Abs(pearsonMeasure - 1.0) <= limit))
                {
                    Console.WriteLine("   ⛔ S182: мера умолчания {0} отошла от 1.00 больше чем на {1}",
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
                FsaResult r = Run(copyData, copy, spec, matrix, material, true, pool);
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

        /// <summary>(`S182`, П154) Разложение меры по каналам — ключ `--diag`.</summary>
        static bool diag;

        /// <summary>
        /// (`S193`, П156) `--exact`: копия БЕЗ розыгрыша — истина, округлённая
        /// до целых. Отделяет детерминированное расхождение разбора с истиной
        /// (не неподвижная точка) от шума копий.
        /// </summary>
        static bool exact;

        /// <summary>(`S193`, П156) `--dump=<файл.csv>`: поканально истина, средняя модель копий и средний счёт.</summary>
        static string dumpPath;

        static EnergySpectrum ExactCopy(EnergySpectrum source, double[] truth, double scale)
        {
            int channels = source.NumberOfChannels;
            int[] counts = new int[channels];
            long total = 0;
            for (int i = 0; i < channels; i++)
            {
                counts[i] = (int)Math.Round(Math.Max(truth[i] * scale, 0.0));
                total += counts[i];
            }

            return new EnergySpectrum
            {
                NumberOfChannels = channels,
                Spectrum = counts,
                EnergyCalibration = source.EnergyCalibration,
                TotalPulseCount = total,
                ValidPulseCount = total,
                MeasurementTime = source.MeasurementTime,
                ChannelPitch = source.ChannelPitch
            };
        }

        /// <summary>(`S193`, П156) Поканальный дамп ключа `--dump=`: средние по копиям одной ступени μ.</summary>
        static class Dump
        {
            static double[] model, count, truthScaled;
            static bool header;

            public static void Add(double mu, FsaResult r, EnergySpectrum copy, double[] truth, double scale)
            {
                if (r == null || r.Model == null) return;
                int n = truth.Length;
                if (model == null) { model = new double[n]; count = new double[n]; truthScaled = new double[n]; }
                for (int i = 0; i < n; i++)
                {
                    model[i] += i < r.Model.Length ? r.Model[i] : 0.0;
                    count[i] += copy.Spectrum[i];
                    truthScaled[i] = truth[i] * scale;
                }
            }

            public static void Flush(double mu, int done, string path)
            {
                if (model == null || done <= 0) { model = null; return; }
                var sb = new StringBuilder();
                if (!header) { sb.Append("mu,channel,truth,model,count").Append(Environment.NewLine); header = true; }
                for (int i = 0; i < model.Length; i++)
                {
                    sb.Append(F(mu, 1)).Append(',').Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(truthScaled[i].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                      .Append((model[i] / done).ToString("R", CultureInfo.InvariantCulture)).Append(',')
                      .Append((count[i] / done).ToString("R", CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                }
                File.AppendAllText(path, sb.ToString());
                model = null;
            }
        }

        /// <summary>
        /// (`S182`, П154 24.09.2026) ГДЕ живёт недобор меры на верной модели.
        /// Истина пробе известна (`truth·scale`), поэтому каждый канал полосы
        /// разбора кладётся в корзину по ИСТИННОМУ ожиданию μ и в ней
        /// сравниваются: отчётный член `(y − μ̂)²/μ̂` (то, что складывает
        /// `Chi2NdfPoisson`), член с истинным знаменателем `(y − μ̂)²/μ` и
        /// чистый член `(y − μ)²/μ` (ожидание ровно 1 — проверка розыгрыша).
        /// Знаменатель меры восстанавливается как χ²/`Chi2NdfPoisson`.
        /// </summary>
        static class Diag
        {
            static readonly double[] Edges = { 0.0, 0.1, 0.3, 1.0, 3.0, 10.0, double.PositiveInfinity };
            static int K { get { return Edges.Length - 1; } }
            static double[] n = new double[K], rep = new double[K], trueDen = new double[K],
                            pure = new double[K], zeroHat = new double[K];
            static double ndfSum, chi2Sum, bandSum;
            static double gainSum, gain2Sum, offSum, anchorsSum, aoffSum, betaSum, beta2Sum;

            public static void Add(FsaResult r, EnergySpectrum copy, double[] truth, double scale)
            {
                if (r == null || r.Model == null) return;
                int lo = r.FirstChannel, hi = r.LastChannel;
                double chi2 = 0.0;
                for (int i = lo; i <= hi && i < r.Model.Length && i < truth.Length; i++)
                {
                    double mu = truth[i] * scale;
                    double hat = r.Model[i];
                    double y = copy.Spectrum[i];
                    int k = 0;
                    while (k < K - 1 && !(mu < Edges[k + 1])) k++;
                    n[k] += 1.0;
                    if (hat > 0.0)
                    {
                        double t = (y - hat) * (y - hat) / hat;
                        rep[k] += t;
                        chi2 += t;
                    }
                    else
                    {
                        zeroHat[k] += 1.0;
                    }
                    if (mu > 0.0)
                    {
                        trueDen[k] += (y - hat) * (y - hat) / mu;
                        pure[k] += (y - mu) * (y - mu) / mu;
                    }
                }
                chi2Sum += chi2;
                ndfSum += r.Chi2NdfPoisson > 0.0 ? chi2 / r.Chi2NdfPoisson : 0.0;
                bandSum += hi - lo + 1;
                gainSum += r.Gain;
                gain2Sum += r.Gain * r.Gain;
                offSum += r.OffsetChannels;
                aoffSum += r.AnchorOffsetKev;
                anchorsSum += r.ScaleAnchorsUsed;
                betaSum += r.AnchorLightBeta;
                beta2Sum += r.AnchorLightBeta * r.AnchorLightBeta;
            }

            public static void Print(double muTarget, int done)
            {
                if (done <= 0) return;
                double d = done;
                Console.WriteLine("   [diag mu {0}] полоса {1} кан., χ² отчёта {2}, знаменатель (NdfBase) {3}, мера {4}",
                                  F(muTarget, 1), F(bandSum / d, 1), F(chi2Sum / d, 2), F(ndfSum / d, 2),
                                  F(chi2Sum / Math.Max(ndfSum, 1e-12), 4));
                Console.WriteLine("   {0,-12} {1,9} {2,9} {3,12} {4,12} {5,12} {6,12}",
                                  "μ истинное", "каналов", "μ̂≤0", "Σ отчёт/кан", "Σ μ-знам/кан", "Σ чистый/кан", "отчёт−чист");
                for (int k = 0; k < K; k++)
                {
                    if (n[k] <= 0.0) continue;
                    string name = "[" + F(Edges[k], 1) + "," + (double.IsInfinity(Edges[k + 1]) ? "∞" : F(Edges[k + 1], 1)) + ")";
                    Console.WriteLine("   {0,-12} {1,9} {2,9} {3,12} {4,12} {5,12} {6,12}",
                                      name, F(n[k] / d, 1), F(zeroHat[k] / d, 1),
                                      F(rep[k] / n[k], 4), F(trueDen[k] / n[k], 4), F(pure[k] / n[k], 4),
                                      F((rep[k] - pure[k]) / d, 2));
                }
                double gm = gainSum / d;
                Console.WriteLine("   шкала копий: gain {0} ± {1} (σ одной), сдвиг {2} кан., якорный сдвиг {3} кэВ, якорей {4}",
                                  F(gm, 6), F(Math.Sqrt(Math.Max(gain2Sum / d - gm * gm, 0.0)), 6),
                                  F(offSum / d, 3), F(aoffSum / d, 3), F(anchorsSum / d, 2));
                double bm = betaSum / d;
                Console.WriteLine("   свет копий: beta {0} ± {1} (σ одной)", F(bm, 6),
                                  F(Math.Sqrt(Math.Max(beta2Sum / d - bm * bm, 0.0)), 6));
                gainSum = gain2Sum = offSum = aoffSum = anchorsSum = betaSum = beta2Sum = 0.0;
                n = new double[K]; rep = new double[K]; trueDen = new double[K];
                pure = new double[K]; zeroHat = new double[K];
                ndfSum = chi2Sum = bandSum = 0.0;
            }
        }

        /// <summary>
        /// (`S182`, П136) Порог М-оценки Хубера этих разборов; NaN — умолчание
        /// анализатора. Диагностика остатка смещения: матрица влияния `H` у
        /// Хубера СЛУЧАЙНА (веса зависят от разыгранных данных), а точный след
        /// `ReportNdf` считает её закреплённой.
        /// </summary>
        static double huberM = double.NaN;

        /// <summary>(`S182`, П154) Свойства анализатора из `--set=Имя=значение`.</summary>
        static readonly List<KeyValuePair<System.Reflection.PropertyInfo, object>> sets =
            new List<KeyValuePair<System.Reflection.PropertyInfo, object>>();

        static FsaResult Run(ResultData rd, EnergySpectrum spectrum, FsaSampleSpec spec,
                             ResponseMatrix matrix, string material, bool reportByModel, double pool)
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
            // (`S182`, П154) NaN — порог ячеек умолчания анализатора.
            if (!double.IsNaN(pool))
            {
                analyzer.ReportPoolVariance = pool;
            }
            foreach (var kv in sets)
            {
                kv.Key.SetValue(analyzer, kv.Value, null);
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
