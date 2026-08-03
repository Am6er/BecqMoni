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
                this.status = null;
            }
        }

        /// <summary>
        /// Убедиться, что разложение соответствует текущему спектру, и запустить
        /// расчёт, если нет. Вызывать с UI-потока: снимок списка нуклидов и
        /// конфигураций снимается здесь, в фон уходят уже копии.
        /// </summary>
        public void EnsureUpToDate(ResultData resultData, bool subtractBackground, ROIConfigData efficiencyRoi)
        {
            if (resultData == null || resultData.EnergySpectrum == null || resultData.EnergySpectrum.Spectrum == null)
            {
                return;
            }

            string currentStamp = BuildStamp(resultData, subtractBackground, efficiencyRoi);
            lock (this.sync)
            {
                if (this.running || currentStamp == this.stamp || currentStamp == this.pendingStamp)
                {
                    return;
                }

                this.running = true;
                this.pendingStamp = currentStamp;
                this.status = Properties.Resources.FSACalculating;
            }

            EnergySpectrum spectrum = resultData.EnergySpectrum;
            EnergySpectrum background = subtractBackground ? resultData.BackgroundEnergySpectrum : null;
            FwhmCalibration fwhmCalibration = resultData.FwhmCalibration;
            // Кривая эффективности: сначала выбранная пользователем область,
            // иначе та, что привязана к самому спектру.
            FsaEfficiency efficiency = FsaEfficiency.FromRoiConfig(efficiencyRoi)
                                       ?? FsaEfficiency.FromRoiConfig(resultData.ROIConfig);

            // Снимок списков: их правит UI-поток (конструктор сетов, NucBase),
            // а перечисление живого списка в фоне ловит «Collection was modified».
            NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
            List<NuclideDefinition> definitions = new List<NuclideDefinition>(nuclideManager.NuclideDefinitions);
            List<Peak> peaks = resultData.DetectedPeaks != null
                ? new List<Peak>(resultData.DetectedPeaks)
                : new List<Peak>();

            FsaAnalyzer analyzer = new FsaAnalyzer();
            if (resultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                // Диапазон берётся тот же, в котором работает поиск пиков;
                // ниже LowEnergyFloorKev его опустит не даст сам анализатор.
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
                    this.stamp = this.pendingStamp;
                    this.pendingStamp = null;
                    this.result = computed;
                    this.status = message;
                }

                EventHandler handler = this.Completed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            });
        }

        static string BuildStamp(ResultData resultData, bool subtractBackground, ROIConfigData efficiencyRoi)
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
                "|", efficiencyRoi != null ? efficiencyRoi.Guid : "-",
                "|", peakStamp.ToString());
        }
    }
}
