using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Чтение спектра с калибровками и запись кривой эффективности в CSV.
    ///
    /// Помощники переехали сюда 13.09.2026 из снятого движка фита по спектрам
    /// (`AMBER25`, решение Amber: эмпирического восстановления кривой из
    /// спектров по вековому равновесию «не должно остаться»). Сам фит снят
    /// целиком; здесь — только то, у чего есть живой потребитель:
    /// <see cref="LoadResultData(string,int,string)"/> читает оснастка
    /// (`tools/effmaker/MeasuredPoint.cs`, проба `FwhmReaderProbeF62`),
    /// <see cref="ExportCsv"/> — кнопка «Экспорт CSV» конструктора кривой.
    /// </summary>
    public static class EfficiencyCurveIo
    {
        public static ResultData LoadResultData(string path, int resultIndex)
        {
            return LoadResultData(path, resultIndex, null);
        }

        /// <summary>
        /// Загрузка спектра. <paramref name="fallbackDeviceGuid"/> — конфигурация
        /// устройства на случай, когда ссылка спектра никуда не ведёт: так бывает
        /// у старых файлов, переживших переименование прибора (пачка КОТ-103
        /// ссылается на «RC-103 (282)» с Guid, которого в конфиге больше нет).
        /// Подставлять что попало нельзя — от конфигурации зависят обе
        /// калибровки, — поэтому замена только явная, по решению вызывающего.
        /// </summary>
        public static ResultData LoadResultData(string path, int resultIndex, string fallbackDeviceGuid)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            if (file.ResultDataList == null || file.ResultDataList.Count == 0)
            {
                // Пустой список результатов — сказать словами, а не голым
                // ArgumentOutOfRangeException из запасного [0].
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, Resources.ERRFileOpenFailure, path));
            }

            ResultData data = resultIndex >= 0 && resultIndex < file.ResultDataList.Count
                ? file.ResultDataList[resultIndex]
                : file.ResultDataList[0];

            PolynomialEnergyCalibration polynomial =
                data.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (polynomial != null)
            {
                polynomial.CheckCalibration(data.EnergySpectrum.NumberOfChannels);
            }

            string fwhmRefusal = null;
            if (data.FwhmCalibration == null)
            {
                List<DeviceConfigInfo> devices = DeviceConfigManager.GetInstance().DeviceConfigList;
                DeviceConfigInfo device = devices
                    .FirstOrDefault(c => c.Guid == data.DeviceConfigReference.Guid);
                if (device == null && !string.IsNullOrEmpty(fallbackDeviceGuid))
                {
                    device = devices.FirstOrDefault(c => string.Equals(
                        c.Guid, fallbackDeviceGuid, StringComparison.OrdinalIgnoreCase));
                }

                if (device == null)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        Resources.EfficiencyMakerNoDeviceConfig, data.DeviceConfigReference.Name));
                }

                FWHMPeakDetectionMethodConfig peakConfig =
                    (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
                // ⛔ ПРИЧИНА ЕДЕТ НАВЕРХ ВМЕСТЕ С ОТКАЗОМ (`A240`, полоса F62,
                //    06.09.2026). Человека в этой точке нет — это загрузчик, —
                //    но у отказа тут ЕСТЬ готовый читатель: бросок тремя
                //    строками ниже, чей текст показывает вызывающий. До правки
                //    этот текст говорил только «у спектра нет ПШПВ-калибровки»,
                //    то есть повторял видимое; три числа настроек прибора
                //    (`FWHM_AT_0`, `Ch_Fwhm`, `Width_Fwhm`) называют, ЧТО именно
                //    чинить, и взять их человеку больше негде — на формах их нет.
                //    ⚠ Голос не добавлен, а дополнен: окно по-прежнему одно.
                data.FwhmCalibration = peakConfig.FwhmCalibration != null
                    ? peakConfig.FwhmCalibration.Clone()
                    : FwhmCalibration.DefaultCalibration(peakConfig, data.EnergySpectrum.EnergyCalibration, out fwhmRefusal);
            }

            if (data.FwhmCalibration == null)
            {
                // Оправа «что: почему» переводу не подлежит — переведены обе её
                // половины по отдельности (`EfficiencyMakerNoFwhm` и
                // `ERRFwhmDefaultNotMonotonic`).
                throw new InvalidOperationException(string.IsNullOrEmpty(fwhmRefusal)
                    ? Resources.EfficiencyMakerNoFwhm
                    : Resources.EfficiencyMakerNoFwhm + " " + fwhmRefusal);
            }

            return data;
        }

        /// <summary>
        /// Кривая в CSV: `E_keV,eps,err_pct`, по строке на узел. Только сама
        /// кривая: второй таблицы — измеренных линий фита — с 13.09.2026 нет,
        /// у кривой из геометрии их не бывает.
        /// </summary>
        public static void ExportCsv(string path, EfficiencyFitResult result)
        {
            using (StreamWriter writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("E_keV,eps,err_pct");
                foreach (ROIEfficiencyData point in result.Curve)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:G8},{1:G8},{2:G6}", point.Energy, point.Efficiency, point.ErrorPercent));
                }
            }
        }
    }
}
