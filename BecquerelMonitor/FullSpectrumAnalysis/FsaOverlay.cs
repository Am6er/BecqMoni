using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Разложение, показываемое поверх спектра: считает его в фоне и держит
    /// последний готовый результат.
    ///
    /// Считать синхронно нельзя: полный проход по сетке дрейфа занимает
    /// десятые доли секунды, а перерисовка графика идёт по таймеру набора,
    /// и фит на UI-потоке подвесил бы окно на каждом обновлении. Поэтому
    /// результат кэшируется по «отпечатку» спектра (сам ResultData, число
    /// отсчётов, режим фона) и пересчитывается только когда отпечаток сменился.
    /// </summary>
    public sealed class FsaOverlay
    {
        readonly object sync = new object();

        FsaResult result;
        string stamp = "";
        string pendingStamp;
        bool running;
        string status;

        // Поколение результата. Сброс (смена активного спектра) его увеличивает,
        // и уже запущенный счёт, вернувшись, увидит чужой номер и промолчит:
        // иначе разложение прежнего спектра воскресало бы поверх нового уже
        // ПОСЛЕ сброса, и забыть его было бы нечем.
        int generation;

        /// <summary>Готовое разложение или null, пока его нет.</summary>
        public FsaResult Result
        {
            get
            {
                lock (this.sync)
                {
                    return this.result;
                }
            }
        }

        /// <summary>Идёт расчёт.</summary>
        public bool IsRunning
        {
            get
            {
                lock (this.sync)
                {
                    return this.running;
                }
            }
        }

        /// <summary>Сообщение для полки графика: «считается», причина отказа.</summary>
        public string Status
        {
            get
            {
                lock (this.sync)
                {
                    return this.status;
                }
            }
        }

        /// <summary>Расчёт закончился — потребителю пора перерисоваться.</summary>
        public event EventHandler Completed;

        public void Reset()
        {
            lock (this.sync)
            {
                this.result = null;
                this.stamp = "";
                this.pendingStamp = null;
                this.status = null;
                this.generation++;
            }
        }

        /// <summary>
        /// Убедиться, что разложение соответствует текущему спектру, и запустить
        /// расчёт, если нет. Вызывать с UI-потока: снимок списка нуклидов и
        /// конфигураций снимается здесь, в фон уходят уже копии.
        /// </summary>
        public void EnsureUpToDate(ResultData resultData, bool subtractBackground)
        {
            if (resultData == null || resultData.EnergySpectrum == null || resultData.EnergySpectrum.Spectrum == null)
            {
                return;
            }

            string currentStamp = BuildStamp(resultData, subtractBackground);
            int myGeneration;
            lock (this.sync)
            {
                if (this.running || currentStamp == this.stamp || currentStamp == this.pendingStamp)
                {
                    return;
                }

                this.running = true;
                this.pendingStamp = currentStamp;
                this.status = Properties.Resources.FSACalculating;
                myGeneration = this.generation;
            }

            // Со снятого флага и до Task.Run всё идёт под try: снимок трогает
            // менеджеры и списки, и брошенное здесь исключение оставило бы
            // running навсегда взведённым — разложение застряло бы на
            // «считается» до конца жизни вида, а сама ошибка ушла бы в
            // отрисовку, у которой обработчика нет.
            try
            {
                this.Launch(resultData, subtractBackground, myGeneration);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FSA start failed: " + ex);
                lock (this.sync)
                {
                    this.running = false;
                    this.pendingStamp = null;
                    this.status = Properties.Resources.FSAFailed;
                }
            }
        }

        void Launch(ResultData resultData, bool subtractBackground, int myGeneration)
        {
            // Снимок самого измерения тоже обязателен: во время набора UI
            // перезаписывает Spectrum[], а FSA ниже читает его в Task.Run.
            // Список нуклидов уже снимался, но без этой копии один фит мог
            // собрать левую половину модели по старому спектру, правую — по
            // новому. Калибровки входят в тот же снимок по той же причине.
            EnergySpectrum spectrum = resultData.EnergySpectrum.Clone();
            EnergySpectrum background = subtractBackground && resultData.BackgroundEnergySpectrum != null
                ? resultData.BackgroundEnergySpectrum.Clone()
                : null;
            FwhmCalibration fwhmCalibration = resultData.FwhmCalibration != null
                ? resultData.FwhmCalibration.Clone()
                : null;
            // Кривая эффективности: сначала СВОЯ кривая спектра — та, что
            // выбрана в панели измерения и лежит в его файле. Кривая переехала
            // из набора зон в конфигурацию прибора, и разложение обязано брать
            // её оттуда же, откуда её берёт активность: две разные кривые в
            // одном спектре — два разных ответа на один вопрос.
            EfficiencyConfigData efficiencyConfig = resultData.Efficiency != null
                ? resultData.Efficiency.Copy()
                : null;
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(efficiencyConfig);
            ResultData compositionInput = CompositionInput(resultData.PeakDetectionMethodConfig,
                                                            efficiencyConfig, spectrum, fwhmCalibration);

            // Снимок списков: их правит UI-поток (конструктор сетов, NucBase),
            // а перечисление живого списка в фоне ловит «Collection was modified».
            NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
            List<NuclideDefinition> definitions = new List<NuclideDefinition>(nuclideManager.NuclideDefinitions);
            List<Peak> peaks = resultData.DetectedPeaks != null
                ? new List<Peak>(resultData.DetectedPeaks)
                : new List<Peak>();

            FsaAnalyzer analyzer = new FsaAnalyzer();

            // Матрица отклика берётся у ТОЙ ЖЕ кривой, что и эффективность, и
            // только если её отпечаток сходится с нынешней геометрией. Не
            // сошёлся — работаем без неё, старым путём: посчитать спектр по
            // матрице чужой геометрии хуже, чем не посчитать вовсе.
            // UseResponseMatrix — выключатель пользователя (W11, галка в форме
            // «Матрица отклика»): выключено — считаем без матрицы, файл даже
            // не читаем.
            if (efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.UseResponseMatrix)
            {
                EfficiencyMaker.ResponseMatrix matrix =
                    EfficiencyMaker.ResponseMatrixStore.Load(efficiencyConfig.Guid);
                if (matrix != null && matrix.IsValidFor(efficiencyConfig.Geometry))
                {
                    analyzer.ResponseMatrix = matrix;

                    // Вещество кристалла идёт вместе с матрицей и только с ней:
                    // им каскадное суммирование ставит сумм-пики по сумме СВЕТА
                    // (S20), а без матрицы суммирования нет вовсе.
                    analyzer.ScintillatorMaterial =
                        EfficiencyMaker.EfficiencySimulator.ScintillatorNameOf(
                            efficiencyConfig.Geometry);
                }
            }

            analyzer.CoincidenceWindowSec = DeadTimeOf(resultData);

            if (resultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                // Диапазон поиска пиков передаётся анализатору, но при
                // FitWholeSpectrum (умолчание) он им не пользуется — читает его
                // только запасной знаменатель при вырожденной калибровке.
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            // Галки читаются ЗДЕСЬ, на UI-потоке, вместе со всеми прочими
            // снимками: те же самые флаги решают и отпечаток, и ветку счёта, и
            // прочитанные дважды в разные моменты они развели бы их.
            bool dbLookups = DbLookups(resultData);
            bool equilibrium = ChainEquilibrium(resultData);

            // Вещество кристалла — СНИМКОМ и на UI-потоке, как всё прочее
            // (`S119`). Образам вылета от него нужна только доля рождения пар
            // (`S122`), а она считается по массовым долям элементов; плотность
            // в отношение не входит. Геометрии нет — снимка нет, и отбор
            // родителей остаётся прежним.
            Dictionary<int, double> crystalFractions = null;
            if (efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.Geometry.Crystal != null)
            {
                crystalFractions = new Dictionary<int, double>(
                    efficiencyConfig.Geometry.Crystal.Fractions);
            }

            Task.Run(() =>
            {
                FsaResult computed = null;
                string message = null;
                try
                {
                    // ⛔ Сборка библиотеки здесь ОДНА на обе ветки, и вторую
                    // заводить нельзя. Развилка касается только того, ОТКУДА
                    // берётся состав: подписи пиков как есть (прежний путь) или
                    // цепочка родителя из баз (`S57`). Собирает образы в обоих
                    // случаях `FsaSampleLibrary`/`FsaLibrary` — двух сборок с
                    // разными правилами о линиях и рентгене в проекте быть не
                    // должно.
                    List<FsaComponent> library;
                    if (dbLookups)
                    {
                        FsaCompositionInference.Report inferred;
                        FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, compositionInput, out inferred);
                        spec.Equilibrium = equilibrium;
                        Trace.WriteLine("FSA composition: " + inferred);
                        library = FsaSampleLibrary.Build(spec);
                    }
                    else
                    {
                        library = FsaLibrary.BuildFromPeaks(
                            peaks, definitions, crystalFractions);
                    }

                    if (library.Count == 0)
                    {
                        message = Properties.Resources.FSANoComponents;
                    }
                    else
                    {
                        computed = analyzer.Analyze(spectrum, background, fwhmCalibration, library, efficiency);
                        if (computed == null)
                        {
                            message = Properties.Resources.FSANotPossible;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("FSA failed: " + ex);
                    message = Properties.Resources.FSAFailed;
                }

                lock (this.sync)
                {
                    this.running = false;
                    if (myGeneration == this.generation)
                    {
                        this.stamp = this.pendingStamp;
                        this.result = computed;
                        this.status = message;
                    }

                    // Сброс уже случился: считали прежний спектр, публиковать
                    // нечего. Отпечаток остаётся пустым, и следующий проход
                    // подготовки вида закажет счёт заново.
                    this.pendingStamp = null;
                }

                EventHandler handler = this.Completed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            });
        }

        /// <summary>
        /// Минимальный снимок <see cref="ResultData"/>, который нужен выводу
        /// состава из баз. Полный <c>ResultData.Clone</c> здесь избыточен: FSA
        /// не читает импульсы, ROI и метаданные, а три используемых входа уже
        /// сняты до фоновой задачи.
        /// </summary>
        static ResultData CompositionInput(PeakDetectionMethodConfig peakConfig,
                                           EfficiencyConfigData efficiency,
                                           EnergySpectrum spectrum,
                                           FwhmCalibration fwhmCalibration)
        {
            return new ResultData
            {
                PeakDetectionMethodConfig = PeakConfigInput(peakConfig),
                Efficiency = efficiency,
                EnergySpectrum = spectrum,
                FwhmCalibration = fwhmCalibration
            };
        }

        /// <summary>
        /// Выводу состава от конфигурации поиска нужны только полоса и порог
        /// SNR. Остальные настройки остаются вне фонового снимка намеренно.
        /// </summary>
        static PeakDetectionMethodConfig PeakConfigInput(PeakDetectionMethodConfig source)
        {
            FWHMPeakDetectionMethodConfig fwhm = source as FWHMPeakDetectionMethodConfig;
            if (fwhm == null)
            {
                return null;
            }

            return new FWHMPeakDetectionMethodConfig
            {
                Min_Range = fwhm.Min_Range,
                Max_Range = fwhm.Max_Range,
                Min_SNR = fwhm.Min_SNR
            };
        }

        /// <summary>
        /// Состав библиотеки выводить из баз по цепочке родителя, а не брать
        /// подписями пиков (`S57`). Настройка принадлежит спектру, а не
        /// разбору — см. <see cref="FWHMPeakDetectionMethodConfig.DbLookupsForFsa"/>.
        /// </summary>
        static bool DbLookups(ResultData resultData)
        {
            var config = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            return config != null && config.DbLookupsForFsa;
        }

        /// <summary>
        /// Ряд связывать равновесием — одна колонка, одна свободная амплитуда
        /// (`S70`). Настройка живёт там же, где и соседняя, — см.
        /// <see cref="FWHMPeakDetectionMethodConfig.ChainEquilibrium"/>.
        ///
        /// ⚠ Умолчание ВКЛЮЧЕНО, поэтому отсутствие конфигурации у спектра
        /// читается как «связывать», а не как «нет».
        /// </summary>
        static bool ChainEquilibrium(ResultData resultData)
        {
            var config = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            return config == null || config.ChainEquilibrium;
        }

        static string BuildStamp(ResultData resultData, bool subtractBackground)
        {
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            EnergySpectrum background = resultData.BackgroundEnergySpectrum;
            // Состав библиотеки задаётся найденными пиками, поэтому их набор
            // входит в отпечаток: сменился список пиков — разложение устарело.
            StringBuilder peakStamp = new StringBuilder();
            if (resultData.DetectedPeaks != null)
            {
                foreach (Peak peak in resultData.DetectedPeaks)
                {
                    if (peak != null && peak.Nuclide != null)
                    {
                        // (`A36`) В отпечаток идёт и ПОЛОЖЕНИЕ пика, а не только
                        // имя: на найденных пиках держится шкала модели, и два
                        // разбора с одинаковым списком имён, но разными
                        // центроидами — это два разных разбора.
                        peakStamp.Append(peak.Nuclide.Name).Append('@')
                                 .Append(peak.Energy.ToString("F1",
                                     System.Globalization.CultureInfo.InvariantCulture))
                                 .Append(';');
                    }
                }
            }

            return string.Concat(
                resultData.GetHashCode().ToString(),
                "|", spectrum.NumberOfChannels.ToString(),
                "|", spectrum.TotalPulseCount.ToString(),
                "|", spectrum.MeasurementTime.ToString("F1"),
                "|", subtractBackground ? "bg" : "nobg",
                "|", background != null ? background.TotalPulseCount.ToString() : "-",
                "|", EfficiencyStamp(resultData.Efficiency),
                "|", MatrixFileStamp(resultData.Efficiency),
                "|", CalibrationStamp(spectrum, resultData.FwhmCalibration),
                // Галка «состав из баз» (S57) — часть отпечатка по той же
                // причине, что и выключатель матрицы: она меняет СОСТАВ
                // библиотеки, и без неё готовое разложение по подписям висело
                // бы на экране после включения вывода (и наоборот).
                "|", DbLookups(resultData) ? "db" : "peaks",
                // Галка «Равновесие» (S70) — по той же причине: ею ряд из
                // нескольких свободных колонок становится одной, то есть меняется
                // САМА библиотека, и без неё в отпечатке на экране висело бы
                // прежнее разложение.
                "|", ChainEquilibrium(resultData) ? "eq" : "free",
                // (`A31`) Набор нуклидов — часть отпечатка. Состав библиотеки
                // идёт от ПОДПИСЕЙ пиков, а подписи ставит набор, и пока
                // менялись только его члены, отпечаток оставался прежним:
                // добавленный в набор нуклид на экране не появлялся, пока
                // человек не трогал что-нибудь ещё.
                "|", NuclideSetStamp(),
                "|", peakStamp.ToString());
        }

        /// <summary>
        /// Отпечаток активного набора: его имя, галка «прятать неопознанные» и
        /// СОСТАВ — число нуклидов, у которых этот набор отмечен. Правка
        /// членства меняет число, а переименование и смена набора — имя.
        ///
        /// ⚠ Считается перебором определений (полторы сотни записей), потому
        /// что членство хранится у НУКЛИДА (<see cref="NuclideDefinition.Sets"/>),
        /// а не у набора. Проход идёт на UI-потоке, там же, где снимаются
        /// остальные части отпечатка.
        /// </summary>
        static string NuclideSetStamp()
        {
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            NuclideSet active = manager != null ? manager.ActiveSet : null;
            if (active == null)
            {
                return "all";
            }

            int members = 0;
            List<NuclideDefinition> definitions = manager.NuclideDefinitions;
            if (definitions != null)
            {
                foreach (NuclideDefinition definition in definitions)
                {
                    if (definition != null && definition.Sets != null
                        && definition.Sets.Contains(active.Id))
                    {
                        members++;
                    }
                }
            }

            return string.Concat(active.Name, ":", members.ToString(),
                                 active.HideUnknownPeaks ? ":hide" : "");
        }

        /// <summary>
        /// Файл матрицы отклика в отпечатке. Счёт решает «с матрицей или без»
        /// по файлу `.rmx` кривой — значит, устаревание обязано видеть его
        /// появление, пересчёт и удаление, иначе разложение «без матрицы»
        /// висит на экране и после того, как матрицу посчитали (и наоборот).
        /// Сам файл не читается — в отпечаток идут время записи и размер.
        /// </summary>
        static string MatrixFileStamp(EfficiencyConfigData efficiency)
        {
            if (efficiency == null || !efficiency.HasGeometry)
            {
                return "-";
            }

            // Выключатель (W11) — часть отпечатка: переключение галки обязано
            // устаревать готовое разложение, иначе «с матрицей» висит на
            // экране и после выключения (и наоборот).
            if (!efficiency.UseResponseMatrix)
            {
                return "off";
            }

            try
            {
                var file = new System.IO.FileInfo(
                    EfficiencyMaker.ResponseMatrixStore.PathOf(efficiency.Guid));
                return file.Exists
                    ? file.LastWriteTimeUtc.Ticks.ToString() + ":" + file.Length.ToString()
                    : "-";
            }
            catch (Exception)
            {
                // недоступный файл — то же, что отсутствующий: счёт его не прочтёт
                return "-";
            }
        }

        /// <summary>
        /// Кривая эффективности в отпечатке. Счёт берёт её из
        /// resultData.Efficiency, значит и устаревание обязано на неё смотреть:
        /// выбранная в панели измерения кривая раньше в отпечаток не входила, и
        /// разложение «без кривой» держалось на экране, пока не менялось
        /// что-нибудь постороннее.
        /// </summary>
        static string EfficiencyStamp(EfficiencyConfigData efficiency)
        {
            if (efficiency == null)
            {
                return "-";
            }

            return string.Concat(
                efficiency.Guid,
                ":", efficiency.LastUpdated.Ticks.ToString(),
                ":", efficiency.Curve != null ? efficiency.Curve.Count.ToString() : "0");
        }

        /// <summary>
        /// Обе калибровки в отпечатке — от них зависят и положения, и ширины
        /// линий образа. Энергетическая снимается пробами по трём каналам, а не
        /// коэффициентами: у неё несколько представлений (полином, нелинейная),
        /// и пробы покрывают любое.
        /// </summary>
        static string CalibrationStamp(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration)
        {
            StringBuilder sb = new StringBuilder();
            EnergyCalibration energy = spectrum.EnergyCalibration;
            int channels = spectrum.NumberOfChannels;
            if (energy != null && channels > 0)
            {
                sb.Append(energy.ChannelToEnergy(0.0).ToString("R"));
                sb.Append(',').Append(energy.ChannelToEnergy(channels / 2.0).ToString("R"));
                sb.Append(',').Append(energy.ChannelToEnergy(channels - 1.0).ToString("R"));
            }

            double[] fwhm = fwhmCalibration != null ? fwhmCalibration.Coefficients : null;
            if (fwhm != null)
            {
                foreach (double coefficient in fwhm)
                {
                    sb.Append(';').Append(coefficient.ToString("R"));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Мёртвое время прибора, снявшего спектр, — оно же ОКНО СОВПАДЕНИЯ
        /// каскадного суммирования (S27): длительность импульса и есть тот
        /// промежуток, внутри которого два кванта складываются в один отсчёт.
        /// Ноль — прибор его не назвал, суммирователь возьмёт своё умолчание.
        ///
        /// ⚠ Вызов обёрнут НЕ на всякий случай: `SerialInputDeviceConfig.DeadTime()`
        /// — заглушка декомпилятора и бросает `NotImplementedException`, а класс
        /// объявлен одним из вариантов `[XmlElement]` для `InputDeviceConfig`,
        /// то есть такая конфигурация читается штатно (TODO T48). Ронять из-за
        /// этого разложение нельзя: мёртвое время — уточнение поправки, а не
        /// условие её существования.
        /// </summary>
        static double DeadTimeOf(ResultData resultData)
        {
            try
            {
                if (resultData == null || resultData.DeviceConfig == null
                    || resultData.DeviceConfig.InputDeviceConfig == null)
                {
                    return 0.0;
                }

                double deadTime = resultData.DeviceConfig.InputDeviceConfig.DeadTime();
                return deadTime > 0.0 ? deadTime : 0.0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FSA: мёртвое время недоступно, окно совпадения по умолчанию: " + ex.Message);
                return 0.0;
            }
        }
    }
}
