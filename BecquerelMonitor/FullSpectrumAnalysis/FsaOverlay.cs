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
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            EnergySpectrum background = subtractBackground ? resultData.BackgroundEnergySpectrum : null;
            FwhmCalibration fwhmCalibration = resultData.FwhmCalibration;
            // Кривая эффективности: сначала СВОЯ кривая спектра — та, что
            // выбрана в панели измерения и лежит в его файле. Кривая переехала
            // из набора зон в конфигурацию прибора, и разложение обязано брать
            // её оттуда же, откуда её берёт активность: две разные кривые в
            // одном спектре — два разных ответа на один вопрос.
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(resultData.Efficiency);

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
            if (resultData.Efficiency != null && resultData.Efficiency.HasGeometry
                && resultData.Efficiency.UseResponseMatrix)
            {
                EfficiencyMaker.ResponseMatrix matrix =
                    EfficiencyMaker.ResponseMatrixStore.Load(resultData.Efficiency.Guid);
                if (matrix != null && matrix.IsValidFor(resultData.Efficiency.Geometry))
                {
                    analyzer.ResponseMatrix = matrix;
                }
            }

            if (resultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                // Диапазон поиска пиков передаётся анализатору, но при
                // FitWholeSpectrum (умолчание) он им не пользуется — читает его
                // только запасной знаменатель при вырожденной калибровке.
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            Task.Run(() =>
            {
                FsaResult computed = null;
                string message = null;
                try
                {
                    List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, definitions);
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
                        peakStamp.Append(peak.Nuclide.Name).Append(';');
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
                "|", peakStamp.ToString());
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
    }
}
