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
    /// <see cref="ExportCsv"/> — кнопка «Экспорт CSV» конструктора кривой
    /// (свежий расчёт или сохранённая кривая конфигурации).
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
            // (П269, 14.10.2026) Файл читает та же дверь, что у «Открыть»
            // (`DocumentManager.ReadResultDataFile`, П268): свой XmlSerializer здесь
            // отказывал на файле с пустым <Time /> (чужой конвертер) целиком, хотя
            // приложение такой файл открывает. Файл, который читался, читается тем же
            // разбором до байта (первый разбор у двери — прежний); поток — только на
            // чтение с FileShare.ReadWrite (`T266`), а не FileShare.Read: файл, который
            // держит пишущая программа, больше не отказ.
            ResultDataFile file = DocumentManager.ReadResultDataFile(path);
            // (П263) Времён, которых в файле нет, — не «сейчас», по правилам открытия.
            // Голоса `A207` здесь нет: это загрузчик оснастки, человека в этой точке
            // нет, и времена набора его потребители не читают (поправки на распад нет).
            DocumentManager.FillAbsentTimes(file);

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

            // ⛔ (`AMBER269`, 02.11.2026) ПРИБОР — ПРАВИЛОМ ОТКРЫТИЯ ФАЙЛА, а не одним
            //    Guid: `DocumentManager.AttachDeviceConfig` (Guid, затем серийный
            //    номер, затем имя; решения Amber 01.11.2026 вопросником, дословно:
            //    «По серийному номеру (Рекомендую)», «По номеру, затем имени
            //    (Рекомендую)»). Прежде здесь стояло `c.Guid == ссылка.Guid`, и экспорт
            //    RadiaCode (в ссылке имя модели без Guid) загрузчик отвергал «прибора
            //    нет», хотя окно тот же файл открывает с прибором. Найденный прибор
            //    ставится спектру, как при открытии.
            List<DeviceConfigInfo> devices = DeviceConfigManager.GetInstance().DeviceConfigList;
            DocumentManager.DeviceMatch how;
            List<DeviceConfigInfo> candidates;
            DeviceConfigInfo device = DocumentManager.AttachDeviceConfig(data, devices, out how, out candidates);

            // ⛔ (`AMBER269`, 02.11.2026, полоса a269fix6) Встроенная заглушка FWHM 15/3756/103
            //    в файле — не своя кривая спектра, а её отсутствие, как при открытии
            //    (`DocumentManager.ForgetFileStandInFwhm`): дальше то же правило, что у спектра
            //    без кривой. Прежде загрузчик брал заглушку как есть — пики в 1.5–1.8 раза шире.
            DocumentManager.ForgetFileStandInFwhm(data);

            string fwhmRefusal = null;
            if (data.FwhmCalibration == null)
            {
                if (device == null && !string.IsNullOrEmpty(fallbackDeviceGuid))
                {
                    device = devices.FirstOrDefault(c => string.Equals(
                        c.Guid, fallbackDeviceGuid, StringComparison.OrdinalIgnoreCase));
                }

                if (device == null)
                {
                    throw new InvalidOperationException(how == DocumentManager.DeviceMatch.Ambiguous
                        ? DocumentManager.DescribeDeviceAmbiguity(data, resultIndex >= 0 && resultIndex < file.ResultDataList.Count ? resultIndex : 0, candidates)
                        : string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerNoDeviceConfig,
                                        data.DeviceConfigReference != null ? data.DeviceConfigReference.Name : ""));
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
                // ⛔ (`AMBER269`, 02.11.2026) Кривая — ПРАВИЛОМ ОТКРЫТИЯ ФАЙЛА
                //    (`DocumentManager.StandInFwhm` → `FwhmCalibration.StandInCalibration`):
                //    три числа прибора и форма пика его кривой. Прежде здесь бралась
                //    СОХРАНЁННАЯ кривая прибора, а окно тот же файл открывает с кривой по
                //    трём числам: у прибора со своей подогнанной кривой загрузчик и экран
                //    давали спектру разные модели разрешения.
                //    ⛔ (`AMBER269`, 02.11.2026, позже того же дня) Теперь и окно берёт
                //    СОХРАНЁННУЮ кривую прибора (решение Amber вопросником, дословно:
                //    «Кривую прибора (Рекомендую)») — правило одно:
                //    `FwhmCalibration.StandInFromDevice` (копия кривой прибора; на чужой
                //    сетке каналов — перенесённая; если по ней нельзя — три числа).
                data.FwhmCalibration = FwhmCalibration.StandInFromDevice(peakConfig, device.NumberOfChannels,
                                                                         data.EnergySpectrum.EnergyCalibration,
                                                                         data.EnergySpectrum.NumberOfChannels,
                                                                         out fwhmRefusal);
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
        ///
        /// Над таблицей — шапка из строк `# ключ: значение` (решение Amber
        /// 07.10.2026, вопросником: «+ шапка `#`»): откуда кривая и чем
        /// посчитана. Читать файл — с `comment='#'`.
        /// </summary>
        public static void ExportCsv(string path, IEnumerable<ROIEfficiencyData> curve,
                                     IEnumerable<string> header)
        {
            using (StreamWriter writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                if (header != null)
                {
                    foreach (string line in header)
                    {
                        // Перевод строки внутри значения (имя, клеймо) порвал
                        // бы шапку: хвост лёг бы строкой таблицы.
                        writer.WriteLine("# " + (line ?? "").Replace("\r", " ").Replace("\n", " "));
                    }
                }

                writer.WriteLine("E_keV,eps,err_pct");
                foreach (ROIEfficiencyData point in curve)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:G8},{1:G8},{2:G6}", point.Energy, point.Efficiency, point.ErrorPercent));
                }
            }
        }
    }
}
