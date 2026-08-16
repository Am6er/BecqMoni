using System;
using System.IO;
using System.Xml;

namespace BecquerelMonitor.Utils
{
    /// <summary>
    /// Заглянуть в файл спектра, не читая его целиком.
    ///
    /// Нужно ровно одно: узнать заранее, потребуется ли спектру конфигурация
    /// прибора, — и если да, то какая. Спросить об этом до долгого прогона
    /// можно только так: полная десериализация пачки в двадцать файлов ради
    /// двух строк заняла бы столько же, сколько сам прогон.
    ///
    /// Читается ПЕРВЫЙ ResultData — тот же, что берёт подгонка по умолчанию.
    /// Ряды отсчётов пропускаются поддеревом: в них весь объём файла.
    /// </summary>
    public static class SpectrumScout
    {
        /// <summary>
        /// true — у спектра нет своей калибровки ПШПВ, и она будет взята у
        /// конфигурации прибора; <paramref name="deviceGuid"/> — на какую он
        /// ссылается (пусто, если ссылки нет вовсе).
        ///
        /// Файл нечитаем или устроен иначе — false: разбираться с этим будет
        /// сама загрузка и скажет своими словами, а не эта разведка.
        /// </summary>
        public static bool NeedsDeviceConfig(string path, out string deviceGuid)
        {
            deviceGuid = "";
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings
                {
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    IgnoreProcessingInstructions = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                };

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (XmlReader reader = XmlReader.Create(stream, settings))
                {
                    if (!Advance(reader, "ResultData"))
                    {
                        return false;
                    }

                    int inside = reader.Depth;
                    bool hasFwhm = false;
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.Depth <= inside)
                        {
                            break;
                        }

                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        // Калибровка ПШПВ и ссылка на прибор лежат прямо в
                        // ResultData. Глубже — чужие: у фонового спектра своя
                        // калибровка энергии, и путать их нельзя.
                        if (reader.Depth != inside + 1)
                        {
                            continue;
                        }

                        switch (reader.Name)
                        {
                            case "SimpleSqrtFwhmCalibration":
                            case "SqrtFwhmCalibration":
                            case "PowerFwhmCalibration":
                                hasFwhm = true;
                                reader.Skip();
                                break;
                            case "DeviceConfigReference":
                                deviceGuid = ReadGuid(reader);
                                break;
                            case "EnergySpectrum":
                            case "BackgroundEnergySpectrum":
                                // Здесь весь объём файла и ничего нужного.
                                reader.Skip();
                                break;
                        }
                    }

                    return !hasFwhm;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool Advance(XmlReader reader, string name)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        static string ReadGuid(XmlReader reader)
        {
            int inside = reader.Depth;
            if (reader.IsEmptyElement)
            {
                return "";
            }

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth <= inside)
                {
                    break;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.Name == "Guid")
                {
                    return reader.ReadElementContentAsString().Trim();
                }
            }

            return "";
        }
    }
}
