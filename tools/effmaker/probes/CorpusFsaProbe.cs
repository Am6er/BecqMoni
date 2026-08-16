using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace CorpusFsaProbe
{
    /// <summary>
    /// Полноспектральный разбор ВСЕГО корпуса кодом ПРИЛОЖЕНИЯ (TODO S1).
    ///
    /// Зачем ещё один обход, когда есть `tools/pie/run_corpus.ps1`: у того
    /// разложение своё, доматричное — `ResponseMatrix` в нём не упоминается ни
    /// разу, и «понятную часть с матрицей» им не измерить. Здесь считает тот же
    /// `FsaAnalyzer`, что работает в окне программы, матрица берётся тем же
    /// `ResponseMatrixStore.Load` по Guid кривой спектра, а библиотека — тем же
    /// `FsaLibrary.BuildFromPeaks` от найденных пиков. Числа этого обхода
    /// относятся к продукту, а не к его копии.
    ///
    /// ⚠ Части корпуса НЕ СМЕШИВАЮТСЯ. `corpus/parts.csv` делит его на
    /// «понятную» часть (геометрия восстановлена, матрица есть) и «непонятную»
    /// (ни того, ни другого); германий помечен `excluded` и не считается вовсе
    /// (приказ Amber 08.08.2026). Это две разные модели, и общее число по ним
    /// было бы средним двух разных вещей. Поэтому итог печатается ПО ЧАСТЯМ, и
    /// имя части идёт в каждую строку `runs.csv`.
    ///
    ///   corpusfsaprobe --corpus=&lt;…\CORPUS\corpus&gt; [--out=out] [--part=all]
    ///                  [--groups=G1S,ASN16] [--only=G1S24_Th232_Denta120_2]
    ///                  [--mode=spline|snip] [--no-matrix] [--no-cascade]
    ///                  [--no-pileup] [--no-background] [--limit=N] [--quiet]
    ///                  [--limits-mc=N [--mc-component=Имя]] [--huber=M] [--refit-z=Z]
    ///                  [--no-escape-gate]
    ///                  [--partial] [--no-pr-gate] [--gamma=G] [--beta=B]
    ///                  [--bg-rebin]
    ///
    /// Файлы на выходе — того же вида, что у `tools/pie`, чтобы считал их тот же
    /// `tools/pie/score.py`: `&lt;группа&gt;_&lt;режим&gt;_components.csv` и
    /// `&lt;группа&gt;_&lt;режим&gt;_runs.csv`; плюс свой
    /// `&lt;группа&gt;_&lt;режим&gt;_limits.csv` — характеристические пределы S9
    /// по ВСЕМ кандидатам библиотеки, включая не вошедших в состав.
    ///
    /// `--limits-mc=N` — Монте-Карло-поверка пределов (S9): для каждого
    /// нуклида состава спектр пересобирается N раз пуассоновским розыгрышем
    /// модели БЕЗ этого нуклида (проверка ложных срабатываний против α) и N раз
    /// с ним на уровне МДА (проверка пропусков против β); библиотека и настройки
    /// не меняются, поиск пиков не перезапускается. Дорого — 2·N разборов на
    /// нуклид: запускать с `--only=` и, при нужде, `--mc-component=`.
    ///
    /// Запускать из каталога, где рядом лежат `config\NuclideDefinition.xml`
    /// (ПОСТАВОЧНЫЙ, а не сеты `mkconfig.py` с обманками), `config\device\*.xml`
    /// корпуса и `config\device\response\*.rmx`. Такой каталог собирает
    /// `tools/CORPUS/scripts/mk_appwd.ps1`. Конфиг Amber (`%AppData%\BecqMoni`)
    /// при этом не задействован: приложение считает себя standalone всегда,
    /// кроме ClickOnce, и пути идут от рабочего каталога.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var o = new Options();
            foreach (string a in args)
            {
                if (a == "--no-matrix") { o.Matrix = false; continue; }
                if (a == "--no-cascade") { o.Cascade = false; continue; }
                if (a == "--no-pileup") { o.PileUp = false; continue; }
                if (a == "--no-backscatter") { o.Backscatter = false; continue; }
                if (a == "--no-background") { o.Background = false; continue; }
                if (a == "--quiet") { o.Quiet = true; continue; }
                if (a == "--peaks") { o.Peaks = true; continue; }
                if (a == "--partial") { o.Partial = true; continue; }
                if (a == "--pr-gate") { o.PartialGate = true; continue; }
                if (a == "--no-pr-gate") { o.PartialGate = false; continue; }
                if (a == "--bg-rebin") { o.RebinBackground = true; continue; }
                if (a == "--no-bg-rebin") { o.RebinBackground = false; continue; }
                if (a.StartsWith("--residuals=", StringComparison.Ordinal))
                {
                    o.Residuals = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--limits-mc=", StringComparison.Ordinal))
                {
                    o.LimitsMc = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--mc-component=", StringComparison.Ordinal))
                {
                    o.McComponent = a.Substring(15);
                    continue;
                }
                if (a.StartsWith("--near=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(7).Split(':');
                    if (parts.Length == 2)
                    {
                        o.NearFrom = double.Parse(parts[0], CultureInfo.InvariantCulture);
                        o.NearTo = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                if (a.StartsWith("--corpus=", StringComparison.Ordinal)) o.Corpus = a.Substring(9);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) o.Out = a.Substring(6);
                else if (a.StartsWith("--part=", StringComparison.Ordinal)) o.Part = a.Substring(7);
                else if (a.StartsWith("--mode=", StringComparison.Ordinal)) o.Mode = a.Substring(7);
                else if (a.StartsWith("--groups=", StringComparison.Ordinal))
                {
                    o.Groups = new List<string>(a.Substring(9).Split(','));
                }
                else if (a.StartsWith("--only=", StringComparison.Ordinal))
                {
                    o.Only = new List<string>(a.Substring(7).Split(','));
                }
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                {
                    o.Limit = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--offset-range=", StringComparison.Ordinal))
                {
                    o.OffsetRangeKev = double.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--offset-steps=", StringComparison.Ordinal))
                {
                    o.OffsetSteps = int.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gain-range=", StringComparison.Ordinal))
                {
                    o.GainRange = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gain-steps=", StringComparison.Ordinal))
                {
                    o.GainSteps = int.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--huber=", StringComparison.Ordinal))
                {
                    o.HuberM = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a == "--no-escape-gate")
                {
                    // S47: вернуть свободные `SE-2614`/`DE-2614` при матрице —
                    // A-сторона A/B. Гейт с 16.08.2026 включён умолчанием,
                    // поэтому мерится его ОТКЛЮЧЕНИЕ, как у Хубера (S41).
                    o.EscapeGate = false;
                }
                else if (a.StartsWith("--refit-z=", StringComparison.Ordinal))
                {
                    // S9 «б»: которая из двух ступеней занижает МДА слабого
                    // компонента — первый NNLS или отсев по значимости. Ноль
                    // снимает отсев целиком, и разница между прогонами и есть
                    // ответ. Ключ, а не пересборка: A/B на одном коде.
                    o.RefitZ = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gamma=", StringComparison.Ordinal))
                {
                    o.NoiseGamma = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--beta=", StringComparison.Ordinal))
                {
                    o.NoiseBeta = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (o.Mode != "spline" && o.Mode != "snip")
            {
                Console.Error.WriteLine("--mode= только spline или snip");
                return 2;
            }

            if (o.Part != "all" && o.Part != "known" && o.Part != "unknown")
            {
                Console.Error.WriteLine("--part= только all, known или unknown");
                return 2;
            }

            string partsPath = Path.Combine(o.Corpus, "parts.csv");
            if (!File.Exists(partsPath))
            {
                Console.Error.WriteLine("нет " + partsPath + " — укажите --corpus=<…\\CORPUS\\corpus>");
                return 2;
            }

            List<Sample> samples = ReadParts(partsPath, o);
            if (samples.Count == 0)
            {
                Console.Error.WriteLine("под отбор не попал ни один спектр");
                return 2;
            }

            Directory.CreateDirectory(o.Out);

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            Console.WriteLine("корпус: {0}", Path.GetFullPath(o.Corpus));
            Console.WriteLine("спектров под отбор: {0} (часть: {1}, режим: {2})",
                              samples.Count, o.Part, o.Mode);
            Console.WriteLine("матрица {0}, суммирование {1}, наложения {2}, рассеяние {3}, фон {4}",
                              o.Matrix ? "по спектру" : "ВЫКЛЮЧЕНА",
                              o.Cascade ? "вкл" : "выкл", o.PileUp ? "вкл" : "выкл",
                              o.Backscatter ? "вкл" : "выкл",
                              o.Background ? "вычитается, если есть" : "НЕ вычитается");
            Console.WriteLine("сетка дрейфа: ноль ±{0:F2} кэВ, узлов {1} (шаг {2:F3} кэВ);"
                              + " усиление ±{3:P2}, узлов {4}",
                              o.OffsetRangeKev > 0.0 ? o.OffsetRangeKev : 3.0,
                              o.OffsetSteps > 0 ? o.OffsetSteps : 9,
                              2.0 * (o.OffsetRangeKev > 0.0 ? o.OffsetRangeKev : 3.0)
                              / ((o.OffsetSteps > 0 ? o.OffsetSteps : 9) - 1),
                              o.GainRange > 0.0 ? o.GainRange : 0.008,
                              o.GainSteps > 0 ? o.GainSteps : 9);
            Console.WriteLine();

            var rows = new List<Row>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (Sample sample in samples)
            {
                rows.Add(RunOne(sample, o, nuclides));
            }

            Write(rows, o);
            Summary(rows, o, clock.Elapsed.TotalSeconds);
            return 0;
        }

        /// <summary>Один спектр: пики, библиотека, матрица, разложение.</summary>
        static Row RunOne(Sample sample, Options o, NuclideDefinitionManager nuclides)
        {
            var row = new Row { Key = sample.Key, Det = sample.Det, Part = sample.Part };
            string path = Path.Combine(o.Corpus, "spectra", sample.Key + ".xml");
            if (!File.Exists(path))
            {
                row.Error = "нет файла спектра";
                Report(row, o);
                return row;
            }

            // Часы и ЦП-время порознь. Разбор однопоточный, поэтому в норме они
            // почти совпадают — и ровно поэтому расхождение говорит о том, что
            // машину делили, а не о том, что разбор подорожал. Сравнивать
            // прогоны между собой надо по ЦП: T28 трое суток числилась
            // «матрица подорожала вдвое», а подорожало ожидание, и той же
            // ошибкой здесь не заметили вчетверо подорожавший разбор (S39).
            var clock = System.Diagnostics.Stopwatch.StartNew();
            TimeSpan cpuBefore = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            try
            {
                ResultData rd = Load(path);
                EnergySpectrum background = o.Background ? rd.BackgroundEnergySpectrum : null;
                row.HasBackground = background != null;

                List<Peak> peaks = new PeakDetector().DetectPeak(
                    rd, BackgroundMode.Invisible, SmoothingMethod.None,
                    nuclides.ActiveSet, nuclides.NuclideDefinitions);
                row.Peaks = peaks.Count;

                List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
                row.LibrarySize = library.Count;

                // Состав ДО фита: без него «компонента нет в разложении» значит
                // разом три разных случая — финдер не нашёл пика, финдер нашёл
                // и подписал ЧУЖИМ именем, гейт выбросил после фита. Числа
                // прогона различить их не позволяют, а разбор S36 упёрся ровно
                // в это.
                if (o.Peaks)
                {
                    Console.WriteLine("  {0}: пиков {1}, компонентов {2}",
                                      sample.Key, peaks.Count, library.Count);
                    foreach (Peak peak in peaks)
                    {
                        Console.WriteLine("      пик {0,9:F2} кэВ  {1}", peak.Energy,
                                          peak.Nuclide != null ? peak.Nuclide.Name : "(без подписи)");
                    }

                    foreach (FsaComponent component in library)
                    {
                        Console.WriteLine("      образ {0,-14} {1,-9} линий {2}",
                                          component.Name, component.Kind, component.Lines.Count);
                    }
                }
                if (library.Count == 0)
                {
                    // Пустая библиотека — не «ошибка счёта», а результат: финдер
                    // не подписал ни одного пика. Молчаливый ноль уже принимали
                    // за «пиков нет» (см. hpge-peak-search-finds-nothing), потому
                    // причина пишется отдельным словом.
                    row.Error = "библиотека пуста (пиков подписано 0)";
                    row.Ms = clock.Elapsed.TotalMilliseconds;
                row.CpuMs = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
                             - cpuBefore).TotalMilliseconds;
                    Report(row, o);
                    return row;
                }

                var analyzer = new FsaAnalyzer();
                analyzer.Mode = o.Mode == "snip"
                    ? FsaAnalyzer.ContinuumMode.Snip
                    : FsaAnalyzer.ContinuumMode.Spline;
                analyzer.CascadeSumming = o.Cascade;
                analyzer.CascadeSumPeaks = o.Cascade;
                analyzer.PileUp = o.PileUp;
                analyzer.Backscatter = o.Backscatter;
                if (o.RefitZ >= 0.0)
                {
                    analyzer.RefitZ = o.RefitZ;
                }

                analyzer.EscapeGate = o.EscapeGate;

                if (o.HuberM >= 0.0)
                {
                    analyzer.HuberM = o.HuberM;
                }

                analyzer.NoiseGamma = o.NoiseGamma;
                analyzer.NoiseBeta = o.NoiseBeta;
                analyzer.PartialResiduals = o.Partial;
                analyzer.PartialResidualGate = o.PartialGate;
                analyzer.RebinBackgroundToSpectrum = o.RebinBackground;

                // Сетка дрейфа — ключами, а не пересборкой (S6): расширять её
                // вслепую нельзя, потому что при том же числе узлов вдвое более
                // широкая сетка вдвое грубее, и цену обеих половин надо мерить
                // вместе.
                if (o.OffsetRangeKev > 0.0)
                {
                    analyzer.OffsetRangeKev = o.OffsetRangeKev;
                }

                if (o.OffsetSteps > 0)
                {
                    analyzer.OffsetSteps = o.OffsetSteps;
                }

                if (o.GainRange > 0.0)
                {
                    analyzer.GainRange = o.GainRange;
                }

                if (o.GainSteps > 0)
                {
                    analyzer.GainSteps = o.GainSteps;
                }
                if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
                {
                    analyzer.MinEnergy = peakConfig.Min_Range;
                    analyzer.MaxEnergy = peakConfig.Max_Range;
                }

                // Матрица — ровно тем же путём, каким её берёт приложение
                // (`FsaOverlay.Launch`): по Guid кривой спектра и только если
                // отпечаток сошёлся с её геометрией. Разница ОДНА: приложение
                // молча работает без матрицы, а здесь причина запоминается —
                // «понятный» спектр, посчитанный без матрицы, обязан быть виден,
                // иначе он смешает две модели внутри одной части.
                if (o.Matrix && rd.Efficiency != null && rd.Efficiency.HasGeometry
                    && rd.Efficiency.UseResponseMatrix)
                {
                    ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency.Guid);
                    if (matrix == null)
                    {
                        row.MatrixNote = "файла нет";
                    }
                    else if (!matrix.IsValidFor(rd.Efficiency.Geometry))
                    {
                        row.MatrixNote = "отпечаток НЕ сошёлся";
                    }
                    else
                    {
                        analyzer.ResponseMatrix = matrix;
                        analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(
                            rd.Efficiency.Geometry);
                        row.MatrixNote = "есть";
                    }
                }
                else if (o.Matrix)
                {
                    row.MatrixNote = rd.Efficiency == null ? "кривой нет"
                        : (rd.Efficiency.HasGeometry ? "выключена в кривой" : "геометрии нет");
                }
                else
                {
                    row.MatrixNote = "выключена ключом";
                }

                FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
                row.EfficiencyName = rd.Efficiency != null ? rd.Efficiency.Name : "";

                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, background,
                                                    rd.FwhmCalibration, library, efficiency);
                row.Ms = clock.Elapsed.TotalMilliseconds;
                row.CpuMs = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
                             - cpuBefore).TotalMilliseconds;
                if (result == null)
                {
                    row.Error = "разложение не получилось (нет калибровок или вырожденный диапазон)";
                    Report(row, o);
                    return row;
                }

                row.Result = result;
                row.Chi2Ndf = result.Chi2Ndf;
                row.Chi2NdfPoisson = result.Chi2NdfPoisson;
                row.ModelResidual = result.ModelResidual;

                // Фон подан и НЕ взят — печатаем причину и снимаем признак
                // «фон есть» (S44). Прежде колонка `background` мерила наличие
                // узла в файле, и одиннадцать спектров G1S годами числились с
                // фоном, который анализатор молча отбрасывал.
                if (result.BackgroundRejected != null)
                {
                    row.HasBackground = false;
                    row.BackgroundNote = result.BackgroundRejected;
                    if (!o.Quiet)
                    {
                        Console.WriteLine("  {0}: ФОН НЕ ВЗЯТ — {1}", row.Key, result.BackgroundRejected);
                    }
                }
                row.Gain = result.Gain;
                row.OffsetChannels = result.OffsetChannels;
                row.GainOnGridEdge = result.GainOnGridEdge;
                row.OffsetOnGridEdge = result.OffsetOnGridEdge;
                row.MatrixUsed = result.ResponseMatrixUsed;
                row.CascadeUsed = result.CascadeSummingUsed;
                row.EfficiencyUsed = result.EfficiencyUsed;

                // Карта невязки: где измерение выше модели. Правило общее с
                // `FsaCascadeProbe` (`ResidualScan`), чтобы числа одного и того
                // же спектра в двух пробах совпадали.
                if (o.Residuals > 0)
                {
                    Console.WriteLine("  {0}: крупнейшие невязки", sample.Key);
                    ResidualScan.Print(rd.EnergySpectrum, result, o.Residuals, "      ");
                }

                if (o.NearTo > o.NearFrom)
                {
                    ResidualScan.Excess near;
                    row.NearExcess = ResidualScan.Near(rd.EnergySpectrum, result,
                                                       o.NearFrom, o.NearTo, out near)
                        ? near.Sigmas : double.NaN;
                    row.NearCounts = double.IsNaN(row.NearExcess) ? 0.0 : near.Counts;
                }

                if (o.LimitsMc > 0)
                {
                    ValidateLimits(rd, background, library, analyzer, result, efficiency, o, sample.Key);
                }
            }
            catch (Exception ex)
            {
                row.Ms = clock.Elapsed.TotalMilliseconds;
                row.CpuMs = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
                             - cpuBefore).TotalMilliseconds;
                row.Error = ex.GetType().Name + ": " + ex.Message;
            }

            Report(row, o);
            return row;
        }

        /// <summary>
        /// Монте-Карло-поверка характеристических пределов (S9) на живом
        /// спектре корпуса — тем же способом, каким Xu-2022 поверяли формулы:
        /// для каждого нуклида состава спектр разыгрывается заново пуассоном
        ///
        ///   * из модели БЕЗ нуклида — доля срабатываний «есть» обязана быть
        ///     около α = 5 % (ложные срабатывания);
        ///   * из модели С нуклидом на уровне его МДА — доля пропусков обязана
        ///     быть около β = 5 %.
        ///
        /// Библиотека, настройки и дрейф-сетка не меняются, поиск пиков не
        /// перезапускается: поверяется формула пределов, а не весь конвейер.
        /// Вне окна фита в розыгрыш идут сами данные — фит их не трогает.
        /// Только режим spline: в snip средние каналов не равны модели.
        /// </summary>
        static void ValidateLimits(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                   FsaAnalyzer analyzer, FsaResult result, FsaEfficiency efficiency,
                                   Options o, string key)
        {
            if (o.Mode != "spline")
            {
                Console.WriteLine("  {0}: --limits-mc работает только с --mode=spline", key);
                return;
            }

            // Зерно фиксировано: прогон обязан воспроизводиться до последней
            // цифры, иначе два запуска дадут «разные» доли на одном коде.
            var rng = new Random(20260814);
            int channels = rd.EnergySpectrum.NumberOfChannels;
            int[] raw = rd.EnergySpectrum.Spectrum;
            double liveTime = result.LiveTime;
            double k1 = analyzer.LimitQuantileK;

            // НЕ вошедшие кандидаты поверяются одной серией: модель их не
            // содержит, то есть сама и есть их нулевая гипотеза, и N розыгрышей
            // полной модели проверяют ложные срабатывания у всех разом.
            var absent = new List<FsaCharacteristicLimit>();
            foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
            {
                if (!limit.Detected && !limit.Degenerate
                    && !double.IsNaN(limit.DecisionThresholdRate)
                    && (o.McComponent == null
                        || string.Equals(limit.Name, o.McComponent, StringComparison.Ordinal)))
                {
                    absent.Add(limit);
                }
            }

            if (absent.Count > 0)
            {
                double[] muFull = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    muFull[i] = i < result.FirstChannel || i > result.LastChannel
                        ? raw[i]
                        : Math.Max(0.0, result.Model[i])
                          + (result.Background != null ? result.Background[i] : 0.0);
                }

                var falseByName = new Dictionary<string, int>(StringComparer.Ordinal);
                int failedRuns = 0;
                for (int run = 0; run < o.LimitsMc; run++)
                {
                    FsaResult replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                                    muFull, rng);
                    if (replay == null)
                    {
                        failedRuns++;
                        continue;
                    }

                    foreach (FsaCharacteristicLimit limit in absent)
                    {
                        double estimate;
                        if (Exceeded(replay, limit.Name, out estimate))
                        {
                            int have;
                            falseByName.TryGetValue(limit.Name, out have);
                            falseByName[limit.Name] = have + 1;
                        }
                    }
                }

                foreach (FsaCharacteristicLimit limit in absent)
                {
                    int fp;
                    falseByName.TryGetValue(limit.Name, out fp);
                    Console.WriteLine("  {0}: {1,-14} НЕ в составе; a*={2:E3} МДА={3:E3} имп/с;"
                                      + " ложных {4}/{5} (ждём ~5 %){6}",
                                      key, limit.Name, limit.DecisionThresholdRate,
                                      limit.DetectionLimitRate, fp, o.LimitsMc - failedRuns,
                                      failedRuns > 0 ? "; отказов " + failedRuns : "");
                }
            }

            foreach (FsaComponentResult c in result.Components)
            {
                if (c.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                if (o.McComponent != null
                    && !string.Equals(c.Name, o.McComponent, StringComparison.Ordinal))
                {
                    continue;
                }

                double amplitude = c.CountRate * liveTime;
                if (!(amplitude > 0.0) || double.IsNaN(c.DecisionThresholdRate)
                    || double.IsNaN(c.DetectionLimitRate))
                {
                    Console.WriteLine("  {0}: {1} — пределов нет (вырождено или не в составе), пропуск",
                                      key, c.Name);
                    continue;
                }

                double mdaAmplitude = c.DetectionLimitRate * liveTime;
                double[] mu0 = new double[channels];
                double[] mu1 = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    if (i < result.FirstChannel || i > result.LastChannel)
                    {
                        // Вне окна фита модель молчит — туда идут сами данные.
                        mu0[i] = raw[i];
                        mu1[i] = raw[i];
                        continue;
                    }

                    double without = result.Model[i] - c.Curve[i];
                    if (without < 0.0)
                    {
                        without = 0.0;
                    }

                    double bg = result.Background != null ? result.Background[i] : 0.0;
                    mu0[i] = without + bg;
                    mu1[i] = mu0[i] + mdaAmplitude * (c.Curve[i] / amplitude);
                }

                int falsePositives = 0, detections = 0, failed = 0;
                var nullEstimates = new List<double>();
                var injectedEstimates = new List<double>();
                for (int run = 0; run < o.LimitsMc; run++)
                {
                    FsaResult replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                                    mu0, rng);
                    if (replay != null)
                    {
                        double estimate;
                        bool exceeded = Exceeded(replay, c.Name, out estimate);
                        nullEstimates.Add(estimate);
                        if (exceeded)
                        {
                            falsePositives++;
                        }
                    }
                    else
                    {
                        failed++;
                    }

                    replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                          mu1, rng);
                    if (replay != null)
                    {
                        double estimate;
                        if (Exceeded(replay, c.Name, out estimate))
                        {
                            detections++;
                        }

                        injectedEstimates.Add(estimate);
                    }
                    else
                    {
                        failed++;
                    }
                }

                // Пропуск пропуску рознь: нулевая оценка значит «отсев убил или
                // NNLS отдал коллинеарным соседям», ненулевая ниже порога —
                // «увидел, но мало». Формула пределов различий не знает, а
                // чинить их пришлось бы по-разному.
                int injectedZeros = 0;
                foreach (double v in injectedEstimates)
                {
                    if (!(v > 0.0))
                    {
                        injectedZeros++;
                    }
                }

                injectedEstimates.Sort();
                double injectedMedian = injectedEstimates.Count == 0 ? 0.0
                    : injectedEstimates[injectedEstimates.Count / 2];

                double meanNull = 0.0, sdNull = 0.0;
                foreach (double v in nullEstimates)
                {
                    meanNull += v;
                }

                if (nullEstimates.Count > 1)
                {
                    meanNull /= nullEstimates.Count;
                    foreach (double v in nullEstimates)
                    {
                        sdNull += (v - meanNull) * (v - meanNull);
                    }

                    sdNull = Math.Sqrt(sdNull / (nullEstimates.Count - 1));
                }

                // Предсказание формулы: σ0 = a*/k. Сравнение с измеренным
                // разбросом нулевых оценок — проверка самой σ0, отдельная от
                // доли срабатываний (порог может быть верен и при перекошенной
                // сигме, если перекос съел квантиль).
                double predictedSigma = c.DecisionThresholdRate / k1;
                Console.WriteLine("  {0}: {1,-14} a*={2:E3} МДА={3:E3} имп/с; ложных {4}/{5} (ждём ~5 %),"
                                  + " пропусков {6}/{7} (ждём ~5 %); σ0: формула {8:E3}, розыгрыш"
                                  + " {9:E3} (сред. {10:E3}); впрыск: нулевых {11}, медиана {12:E3}{13}",
                                  key, c.Name, c.DecisionThresholdRate, c.DetectionLimitRate,
                                  falsePositives, o.LimitsMc,
                                  o.LimitsMc - detections, o.LimitsMc,
                                  predictedSigma, sdNull, meanNull,
                                  injectedZeros, injectedMedian,
                                  failed > 0 ? "; отказов разбора " + failed : "");
            }
        }

        /// <summary>
        /// Один синтетический разбор: розыгрыш каналов пуассоном вокруг
        /// заданных средних, тот же анализатор, та же библиотека. null —
        /// разбор не удался.
        /// </summary>
        static FsaResult RunSynthetic(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                      FsaAnalyzer analyzer, FsaEfficiency efficiency,
                                      double[] mean, Random rng)
        {
            EnergySpectrum synthetic = rd.EnergySpectrum.Clone();
            int[] counts = synthetic.Spectrum;
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = SamplePoisson(rng, mean[i]);
            }

            return analyzer.Analyze(synthetic, background, rd.FwhmCalibration, library, efficiency);
        }

        /// <summary>
        /// Решение теста по строке пределов названного компонента: оценка выше
        /// её же порога решения. Кандидат без строки — «не обнаружен» с нулевой
        /// оценкой: образ не построился, и это честное решение теста.
        /// </summary>
        static bool Exceeded(FsaResult replay, string name, out double estimate)
        {
            estimate = 0.0;
            foreach (FsaCharacteristicLimit limit in replay.CharacteristicLimits)
            {
                if (string.Equals(limit.Name, name, StringComparison.Ordinal))
                {
                    estimate = limit.CountRate;
                    return !double.IsNaN(limit.DecisionThresholdRate)
                           && limit.CountRate > limit.DecisionThresholdRate;
                }
            }

            return false;
        }

        /// <summary>
        /// Пуассонов розыгрыш: точный (Кнут) до среднего 50, дальше нормальное
        /// приближение с округлением — на счетах корпуса (до миллионов в
        /// канале) точный метод стоил бы дороже самого разбора.
        /// </summary>
        static int SamplePoisson(Random rng, double mean)
        {
            if (!(mean > 0.0))
            {
                return 0;
            }

            if (mean > 50.0)
            {
                double u1 = 1.0 - rng.NextDouble();
                double u2 = rng.NextDouble();
                double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                double value = Math.Round(mean + gauss * Math.Sqrt(mean));
                return value < 0.0 ? 0 : (int)value;
            }

            double limit = Math.Exp(-mean);
            double p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= rng.NextDouble();
            }
            while (p > limit);

            return k - 1;
        }

        static void Report(Row row, Options o)
        {
            if (o.Quiet)
            {
                return;
            }

            if (row.Error != null)
            {
                Console.WriteLine("{0,-22} {1,-10} {2,-8} ОШИБКА: {3}",
                                  row.Key, row.Det, row.Part, row.Error);
                return;
            }

            Console.WriteLine("{0,-22} {1,-10} {2,-8} chi2/ndf {3,8:F3}  пиков {4,3}  комп. {5,2}"
                              + "  матрица: {6,-18} {7,6:F0} мс{8}",
                              row.Key, row.Det, row.Part, row.Chi2Ndf, row.Peaks, row.LibrarySize,
                              row.MatrixNote, row.Ms,
                              row.GainOnGridEdge && row.OffsetOnGridEdge ? "  КРАЙ: усиление И ноль"
                              : row.GainOnGridEdge ? "  КРАЙ: усиление"
                              : row.OffsetOnGridEdge ? "  КРАЙ: ноль шкалы" : "");
        }

        /// <summary>
        /// Итог ПО ЧАСТЯМ. Общей строки по всему корпусу здесь нет нарочно:
        /// понятная часть считается с матрицей (образ полный), непонятная — из
        /// одних пиков, и одно число на обе означало бы среднее двух разных
        /// моделей.
        /// </summary>
        static void Summary(List<Row> rows, Options o, double seconds)
        {
            Console.WriteLine();
            // Часы — про машину, ЦП — про код. Печатаются рядом нарочно: этот
            // прогон уже дорожал вчетверо незамеченным (50 -> 236 с, S39),
            // потому что «дольше» списывали на загрузку, а списать было не на
            // чем — числа-то не менялись. Сравнивать прогоны между собой надо
            // по ЦП-времени (T28).
            double cpuSeconds = 0.0;
            foreach (Row r in rows)
            {
                cpuSeconds += r.CpuMs / 1000.0;
            }

            Console.WriteLine("=== итог по частям корпуса ({0:n0} с на часах, {1:n0} с ЦП) ===",
                              seconds, cpuSeconds);
            Console.WriteLine("{0,-10} {1,8} {2,8} {3,10} {4,10} {5,8} {6,8} {7,8}",
                              "часть", "спектров", "с матр.", "sum chi2", "медиана", "ошибок",
                              "кр.усил", "кр.ноль");
            foreach (string part in new[] { "known", "unknown" })
            {
                var of = new List<Row>();
                foreach (Row r in rows)
                {
                    if (r.Part == part)
                    {
                        of.Add(r);
                    }
                }

                if (of.Count == 0)
                {
                    continue;
                }

                int errors = 0, matrix = 0, gainEdge = 0, offsetEdge = 0;
                // `T38`: «нет матрицы» и «матрица есть, но образа ею не
                // построено» — РАЗНЫЕ состояния, и одно число их складывало.
                // Первое — тяжёлый отказ (`B14`: 37 спектров понятной части
                // считались из одних пиков, назвавшись понятными), второе —
                // норма (у спектра уцелели только производные компоненты,
                // матрицу они не трогают по построению). Пока их печатали
                // вместе, отказ можно было принять за норму — так `B14` и
                // прожила.
                int noFile = 0;
                var chi = new List<double>();
                foreach (Row r in of)
                {
                    if (r.Error != null)
                    {
                        errors++;
                        continue;
                    }

                    if (r.MatrixUsed)
                    {
                        matrix++;
                    }
                    else if (r.MatrixNote == "файла нет" || r.MatrixNote == "отпечаток НЕ сошёлся")
                    {
                        // Только эти два — отказ. «Кривой нет» и «геометрии
                        // нет» — НОРМАЛЬНОЕ состояние непонятной части, и
                        // считать его отказом значит кричать на все 36 её
                        // спектров каждый прогон. Признак, который кричит
                        // всегда, читать перестают на второй день.
                        noFile++;
                    }

                    if (r.GainOnGridEdge)
                    {
                        gainEdge++;
                    }

                    if (r.OffsetOnGridEdge)
                    {
                        offsetEdge++;
                    }

                    chi.Add(r.Chi2Ndf);
                }

                chi.Sort();
                double sum = 0.0;
                foreach (double v in chi)
                {
                    sum += v;
                }

                double median = chi.Count == 0 ? 0.0
                    : (chi.Count % 2 == 1 ? chi[chi.Count / 2]
                       : 0.5 * (chi[chi.Count / 2 - 1] + chi[chi.Count / 2]));
                Console.WriteLine("{0,-10} {1,8} {2,8} {3,10:F1} {4,10:F2} {5,8} {6,8} {7,8}",
                                  part, of.Count, matrix, sum, median, errors, gainEdge, offsetEdge);
                if (noFile > 0)
                {
                    // ⛔ Печатается ОТДЕЛЬНОЙ строкой и только когда есть что
                    // печатать: это отказ, а не статистика. Разница с колонкой
                    // «с матр.» в том, что там недостача может быть нормой.
                    Console.WriteLine("{0,-10} ⛔ БЕЗ МАТРИЦЫ (файла нет либо отпечаток не"
                                      + " сошёлся): {1} — узел кривой у них ЕСТЬ, а матрицы под"
                                      + " него нет, и считались они из одних пиков", "", noFile);
                }
            }

            Console.WriteLine();
            Console.WriteLine("⚠ числа каждой строки принадлежат ТОЛЬКО своей части корпуса;");
            Console.WriteLine("  «понятная» считана с матрицей отклика, «непонятная» — из одних пиков.");
            Console.WriteLine("Фантомы и recall — {0}\\..\\score.py по этим же файлам:", o.Out);
            Console.WriteLine("  python tools/pie/score.py --mode={0} --out-dir={1} --part={2}",
                              o.Mode, o.Out, o.Part);
        }

        /// <summary>Файлы того же вида, что пишет `tools/pie`, — для `score.py`.</summary>
        static void Write(List<Row> rows, Options o)
        {
            var groups = new List<string>();
            foreach (Row r in rows)
            {
                if (!groups.Contains(r.Det))
                {
                    groups.Add(r.Det);
                }
            }

            foreach (string group in groups)
            {
                string prefix = Path.Combine(o.Out, group + "_" + o.Mode);
                using (var runs = new StreamWriter(prefix + "_runs.csv", false, new UTF8Encoding(true)))
                using (var comps = new StreamWriter(prefix + "_components.csv", false, new UTF8Encoding(true)))
                using (var limits = new StreamWriter(prefix + "_limits.csv", false, new UTF8Encoding(true)))
                {
                    // Новые колонки — только В КОНЕЦ строки: score.py читает
                    // по именам (DictReader), но чужой разбор по номерам колонок
                    // вставка в середину сломала бы молча.
                    runs.WriteLine("spectrum,det,part,chi2ndf,gain,offset_ch,drift_edge,gain_edge,"
                                   + "offset_edge,matrix,"
                                   + "matrix_note,cascade,efficiency,background,peaks,components,"
                                   + "ms,cpu_ms,near_sigmas,near_counts,error,chi2ndf_pois,bg_rejected,"
                                   + "model_residual_pct");
                    // `share_pct` — доля в «пироге», у служебных образов ноль
                    // НАРОЧНО; `peak_share_pct` — доля пиковых отсчётов среди
                    // ВСЕХ образов, она есть у каждого (S49). Читать «сколько
                    // занимает компонент» надо по второй: `Ann-511` с 3.1 %
                    // отсчётов по первой выглядела отсутствующей.
                    comps.WriteLine("spectrum,det,part,component,kind,share_pct,z,count_rate,peak_counts,"
                                    + "dt_cps,mda_cps,zone_chi2ndf,zone_dd,zone_n,peak_share_pct");
                    // Пределы S9 — по ВСЕМ кандидатам библиотеки, включая не
                    // вошедших в состав: у «не обнаружен» без МДА нет смысла.
                    limits.WriteLine("spectrum,det,part,component,kind,detected,count_rate,"
                                     + "dt_cps,mda_cps,degenerate,collinearity");
                    foreach (Row r in rows)
                    {
                        if (r.Det != group)
                        {
                            continue;
                        }

                        runs.WriteLine(string.Join(",",
                            Csv(r.Key), Csv(r.Det), Csv(r.Part),
                            r.Error != null ? "ERROR" : F(r.Chi2Ndf, "F4"),
                            F(r.Gain, "F6"), F(r.OffsetChannels, "F3"),
                            r.DriftOnGridEdge ? "1" : "0",
                            r.GainOnGridEdge ? "1" : "0", r.OffsetOnGridEdge ? "1" : "0",
                            r.MatrixUsed ? "1" : "0", Csv(r.MatrixNote),
                            r.CascadeUsed ? "1" : "0", r.EfficiencyUsed ? "1" : "0",
                            r.HasBackground ? "1" : "0",
                            r.Peaks.ToString(CultureInfo.InvariantCulture),
                            r.LibrarySize.ToString(CultureInfo.InvariantCulture),
                            F(r.Ms, "F0"), F(r.CpuMs, "F0"),
                            F(r.NearExcess, "F2"), F(r.NearCounts, "F0"),
                            Csv(r.Error ?? ""),
                            r.Error != null ? "" : F(r.Chi2NdfPoisson, "F4"),
                            Csv(r.BackgroundNote),
                            r.Error != null ? "" : F(100.0 * r.ModelResidual, "F3")));

                        if (r.Result == null)
                        {
                            continue;
                        }

                        foreach (FsaComponentResult c in r.Result.Components)
                        {
                            comps.WriteLine(string.Join(",",
                                Csv(r.Key), Csv(r.Det), Csv(r.Part), Csv(c.Name),
                                c.Kind.ToString().ToLowerInvariant(),
                                F(c.SharePercent, "F3"), F(c.Z, "F2"),
                                F(c.CountRate, "E4"), F(c.PeakCounts, "F1"),
                                F(c.DecisionThresholdRate, "E4"), F(c.DetectionLimitRate, "E4"),
                                F(c.ZoneChi2Ndf, "F3"), F(c.ZoneDeltaD, "F2"),
                                c.ZoneChannels.ToString(CultureInfo.InvariantCulture),
                                F(c.PeakSharePercent, "F3")));
                        }

                        foreach (FsaCharacteristicLimit L in r.Result.CharacteristicLimits)
                        {
                            limits.WriteLine(string.Join(",",
                                Csv(r.Key), Csv(r.Det), Csv(r.Part), Csv(L.Name),
                                L.Kind.ToString().ToLowerInvariant(),
                                L.Detected ? "1" : "0",
                                F(L.CountRate, "E4"),
                                F(L.DecisionThresholdRate, "E4"), F(L.DetectionLimitRate, "E4"),
                                L.Degenerate ? "1" : "0", F(L.Collinearity, "F4")));
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("записано групп: {0} -> {1}", groups.Count, Path.GetFullPath(o.Out));
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        static string Csv(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.IndexOfAny(new[] { ',', '"', '\n' }) < 0
                ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>Разбор `parts.csv` с отбором по ключам запуска.</summary>
        static List<Sample> ReadParts(string path, Options o)
        {
            var samples = new List<Sample>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                List<string> cells = SplitCsv(lines[i]);
                if (cells.Count < 3 || cells[0].Length == 0)
                {
                    continue;
                }

                var sample = new Sample { Key = cells[0], Det = cells[1], Part = cells[2] };

                // Германий выброшен здесь, а не отбором вызывающего: приказ
                // Amber 08.08.2026 — новых задач по нему не заводить и в счёт
                // не брать. Ключа, который бы его вернул, нет нарочно.
                if (sample.Part == "excluded")
                {
                    continue;
                }

                if (o.Part != "all" && sample.Part != o.Part)
                {
                    continue;
                }

                if (o.Groups != null && !o.Groups.Contains(sample.Det))
                {
                    continue;
                }

                if (o.Only != null && !o.Only.Contains(sample.Key))
                {
                    continue;
                }

                samples.Add(sample);
                if (o.Limit > 0 && samples.Count >= o.Limit)
                {
                    break;
                }
            }

            return samples;
        }

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            cells.Add(sb.ToString());
            return cells;
        }

        /// <summary>
        /// Спектр читается ровно так же, как его читают `FsaCascadeProbe` и
        /// `FsaPaletteProbe`: с достройкой счёта и ПШПВ-калибровки умолчанием,
        /// как это делает `DocEnergySpectrum`. Иначе числа проб на одном файле
        /// не сойдутся, а разница будет не в том, что мерили.
        /// </summary>
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

            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && rd.DeviceConfig != null
                && rd.DeviceConfig.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fromDevice)
            {
                rd.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fromDevice.Clone();
            }

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }

        sealed class Options
        {
            public string Corpus = "corpus";
            public string Out = "out";
            public string Part = "all";
            public string Mode = "spline";
            public bool Matrix = true;
            public bool Cascade = true;
            public bool PileUp = true;
            public bool Backscatter = true;
            public bool Background = true;
            public bool Quiet;
            public bool Peaks;

            /// <summary>Сколько крупнейших невязок печатать на спектр (0 — не печатать).</summary>
            public int Residuals;

            /// <summary>Розыгрышей Монте-Карло-поверки пределов S9 (0 — не поверять).</summary>
            public int LimitsMc;

            /// <summary>Поверять только этот компонент (`--limits-mc`); null — все.</summary>
            public string McComponent;

            /// <summary>Окно энергий, про которое спрашивают отдельно (V4: ~460 кэВ).</summary>
            public double NearFrom, NearTo;
            public int Limit;
            public double OffsetRangeKev;   // 0 — умолчание анализатора (3.0)
            public int OffsetSteps;         // 0 — умолчание анализатора (9)
            public double GainRange;        // 0 — умолчание анализатора (0.008)
            public int GainSteps;           // 0 — умолчание анализатора (9)

            /// <summary>
            /// Порог Хубера в сигмах; отрицательный — умолчание анализатора
            /// (3.0). Ноль ВЫКЛЮЧАЕТ перевзвешивание — это A-сторона S41:
            /// Хубер в решателе живёт с переноса из pie и входит в базу
            /// корпуса, так что мерится его ОТКЛЮЧЕНИЕ, а не включение.
            /// </summary>
            public double HuberM = -1.0;

            /// <summary>
            /// (S9 «б») Порог значимости отсева перед вторым проходом;
            /// отрицательный — умолчание анализатора (3.0), ноль ВЫКЛЮЧАЕТ
            /// отсев целиком. Заведён, чтобы развести две ступени, каждая из
            /// которых могла занизить МДА слабого компонента: первый NNLS
            /// (сигнал уходит соседям) или «предварительный анализ состава»
            /// (компонент выброшен и второй проход считается без него).
            /// </summary>
            public double RefitZ = -1.0;

            /// <summary>(S47) Гейт образов вылета при матрице; A-сторона — `--no-escape-gate`.</summary>
            public bool EscapeGate = true;

            /// <summary>(S43) γ составного шума D = F + γ²F²; 0 — выключено.</summary>
            public double NoiseGamma;

            /// <summary>(S43) β коррелированности вычитаемого фона; 0 — выключено.</summary>
            public double NoiseBeta;

            /// <summary>(P6) Считать парциальные невязки (дорого: рефит на компонент).</summary>
            public bool Partial;

            /// <summary>
            /// (P6 «б») Гейт по ΔD&lt;0 с перефитом. С 14.08.2026 включён в
            /// анализаторе умолчанием (решение Amber) — A/B-сторона теперь
            /// `--no-pr-gate`.
            /// </summary>
            public bool PartialGate = true;

            /// <summary>
            /// (S45) Перекладывать фон на шкалу спектра перед вычитанием.
            /// Умолчание — ДА с 16.08.2026, вслед за анализатором: после того
            /// как фон той же настройки стал жить в шкале переднего плана
            /// (`build_corpus.same_setting`), перекладка перестала вредить.
            /// A/B-сторона — `--no-bg-rebin`; ключ `--bg-rebin` оставлен, чтобы
            /// прежние замеры воспроизводились дословно.
            ///
            /// ⚠ Умолчание держится ЗДЕСЬ, а не наследуется от анализатора:
            /// строка 16.08.2026 перекрывала `FsaAnalyzer` своим `false`, и
            /// прогон «с новым умолчанием» тихо повторил старые числа.
            /// </summary>
            public bool RebinBackground = true;
            public List<string> Groups;
            public List<string> Only;
        }

        sealed class Sample
        {
            public string Key;
            public string Det;
            public string Part;
        }

        sealed class Row
        {
            public string Key;
            public string Det;
            public string Part;
            public string Error;
            public string MatrixNote = "";
            public string EfficiencyName = "";
            public int Peaks;
            public int LibrarySize;
            public double Chi2Ndf;

            /// <summary>χ²/ndf прежними весами — общая метрика A/B (S41/S43).</summary>
            public double Chi2NdfPoisson;

            /// <summary>Невязка модели ε, доля (в csv печатается в процентах) — S51.</summary>
            public double ModelResidual;
            public double Gain;
            public double OffsetChannels;
            public double Ms;

            /// <summary>
            /// Процессорное время разбора. Разбор однопоточный, поэтому в норме
            /// оно почти равно `Ms`; расхождение значит, что машину делили, а
            /// не что разбор подорожал. Между прогонами сравнивать надо ЭТО
            /// (T28, S39).
            /// </summary>
            public double CpuMs;

            /// <summary>Невязка в запрошенном окне (`--near=`): сигмы и отсчёты.</summary>
            public double NearExcess = double.NaN;
            public double NearCounts;
            public bool GainOnGridEdge;
            public bool OffsetOnGridEdge;

            /// <summary>Любой из двух краёв — для итоговой таблицы.</summary>
            public bool DriftOnGridEdge
            {
                get { return this.GainOnGridEdge || this.OffsetOnGridEdge; }
            }
            public bool MatrixUsed;
            public bool CascadeUsed;
            public bool EfficiencyUsed;
            public bool HasBackground;

            /// <summary>(S44) Причина, по которой поданный фон не взят; пусто — взят.</summary>
            public string BackgroundNote = "";
            public FsaResult Result;
        }
    }
}
