using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000032 RID: 50
    public class DoseRateManager
    {
        private GlobalConfigManager globalConfigManager;
        public DoseRateManager(GlobalConfigManager globalConfigManager) 
        {
            this.globalConfigManager = globalConfigManager;
        }

        // Token: 0x060002B0 RID: 688 RVA: 0x0000D214 File Offset: 0x0000B414
        public DoseRate Calculate(ResultData resultData, DoseRateConfig config)
        {
            // Доза — свойство ИЗМЕРЕННОГО спектра. Раньше сюда передавался
            // режим отображения графика, и при «фон вычтен» доза считалась по
            // разности — показание дозиметра менялось от галки отрисовки
            // (TODO G6). Дозиметр так себя не ведёт: фон — тоже доза.
            EnergySpectrum energySpectrum = resultData.EnergySpectrum;

            // Базовый тип, не каст к PolynomialEnergyCalibration: у спектра
            // может стоять NonlinearEnergyCalibration — она сестра, а не
            // наследник, и каст валил расчёт InvalidCastException (TODO G5).
            EnergyCalibration calibration = energySpectrum.EnergyCalibration;
            DoseRate doseRate = new DoseRate();

            List<double> errors = new List<double>();
            List<double> doseRates = new List<double>();
            foreach (DoseRateCalibrationPoint point in config.DoseRateCalibrationPoints)
            {
                int startch = (int)calibration.EnergyToChannel(point.LowerBound, energySpectrum.NumberOfChannels);
                int endch = (int)calibration.EnergyToChannel(point.UpperBound, energySpectrum.NumberOfChannels);
                if (startch < 0) startch = 0;
                if (endch >= energySpectrum.Spectrum.Length) endch = energySpectrum.Spectrum.Length - 1;
                double counts = 0.0;
                // Полуоткрыто, [startch, endch): диапазоны в конфигурациях идут
                // ВСТЫК (у поставочной RC-103 их 36, верх одного равен низу
                // следующего), и замкнутая сумма считала граничный канал каждого
                // диапазона дважды — 35 лишних каналов из ~900 на 1024-канальном
                // спектре, доза завышалась. Генератор точек в DeviceConfigForm
                // всегда суммировал полуоткрыто; расходились именно эти два
                // места (W19, решение Amber 08.08.2026 — полуоткрыто везде).
                for (int i = startch; i < endch; i++)
                {
                    counts += energySpectrum.Spectrum[i];
                }
                if (counts == 0) continue;
                double error = Math.Sqrt(counts) / counts;
                double dr = counts * point.Sensitivity;
                doseRates.Add(dr);
                errors.Add(dr * error);
            }

            doseRate.Rate = doseRates.Sum();
            if (double.IsNaN(doseRate.Rate) || double.IsInfinity(doseRate.Rate) || energySpectrum.MeasurementTime == 0.0)
            {
                doseRate.Rate = 0.0;
                return doseRate;
            }

            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            double errorLevel = (double)globalConfig.MeasurementConfig.ErrorLevel;
            doseRate.Error = errorLevel * Math.Sqrt(errors.Sum(e => e * e)) / energySpectrum.MeasurementTime;
            doseRate.Rate /= energySpectrum.MeasurementTime;
            return doseRate;
        }
    }
}
