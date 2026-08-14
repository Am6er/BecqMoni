using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace RoiActivityProbe
{
    /// <summary>
    /// Сквозная сверка АКТИВНОСТИ, считаемой через конфигурацию ROI (V9,
    /// указание Amber 08.08.2026): не звенья порознь, а само число, которое
    /// видит пользователь, — зона, чистый счёт, cps, коэффициент K, беккерели.
    ///
    /// Путь счёта — РОВНО тот же код, что в приложении:
    /// `MeasurementResultManager.Calculate` (примитивы зоны, вычитание фона)
    /// и `Translate` (cps → Бк через `BecquerelCoefficient.Resolve`).
    /// Своего счёта у пробы нет — иначе она мерила бы не продукт.
    ///
    ///   roiactivityprobe --spectrum=&lt;xml&gt; --roi=&lt;ROIConfig.xml&gt;
    ///                    [--passport=&lt;Бк&gt;]
    ///
    /// Дополнительно печатаются ВАРИАНТЫ K для каждой зоны с энергией и
    /// выходом: запасённый (поле зоны), по кривой СПЕКТРА (узел Efficiency —
    /// у корпуса это наш МК) и по легаси-кривой самого ROI-файла (узел
    /// &lt;ROIEfficiency&gt;, который после переезда кривой в конфиг
    /// устройства при загрузке сбрасывается — читается здесь напрямую из
    /// XML; у поставочных ROI-файлов это кривая ЛСРМ). Если задан --passport,
    /// печатается и ожидаемый cps = A·(I/100)·ε по каждой кривой.
    ///
    /// Запускать из каталога с конфигом (`wd_app`): менеджерам нужны
    /// `config\BecquerelMonitor.xml` и конфигурации приборов.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, roiPath = null;
            double passport = 0.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--roi=", StringComparison.Ordinal)) roiPath = a.Substring(6);
                else if (a.StartsWith("--passport=", StringComparison.Ordinal))
                {
                    passport = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null || roiPath == null)
            {
                Console.Error.WriteLine("нужно --spectrum= и --roi=");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            // Порядок старта как в Program.cs: карту операций заполняет
            // отдельный вызов, без него конструктор менеджера падает.
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            ResultData rd = Load(spectrumPath);
            ROIConfigData roi;
            var serializer = new XmlSerializer(typeof(ROIConfigData));
            using (var stream = new FileStream(roiPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                roi = (ROIConfigData)serializer.Deserialize(stream);
            }

            // То же, что делает ROIConfigManager после загрузки: примитивы
            // хранят операцию строкой, а считает менеджер по ссылке.
            foreach (ROIDefinitionData definition in roi.ROIDefinitions)
            {
                foreach (ROIPrimitiveData primitive in definition.ROIPrimitives)
                {
                    primitive.Operation = ROIPrimitiveOperation.OperationsMap[primitive.OperationType];
                }
            }

            rd.ROIConfig = roi;

            Console.WriteLine("спектр: {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("  живое время {0:F0} с, фон {1}, кривая спектра: {2}",
                              rd.EnergySpectrum.MeasurementTime,
                              rd.BackgroundEnergySpectrum != null ? "есть" : "НЕТ",
                              rd.Efficiency != null ? rd.Efficiency.Name : "НЕТ");
            Console.WriteLine("ROI-конфиг: {0} (зон {1})", roi.Name, roi.ROIDefinitions.Count);
            if (passport > 0.0)
            {
                Console.WriteLine("паспорт: {0:F0} Бк", passport);
            }

            Console.WriteLine();

            var manager = new MeasurementResultManager();
            MeasurementResultCollection counts = manager.Calculate(rd);
            if (counts == null)
            {
                Console.Error.WriteLine("Calculate вернул null — нет ROI-конфига или спектра");
                return 1;
            }

            MeasurementResultCollection cps = manager.Translate(counts, ResultTranslation.CountsPerSecond);
            MeasurementResultCollection bq = manager.Translate(counts, ResultTranslation.Becquerels);

            // Легаси-кривая ROI-файла: после переезда кривой в конфиг
            // устройства ROIConfigData её не читает (UnknownElement), поэтому
            // добываем прямо из XML.
            EfficiencyConfigData legacy = ReadLegacyCurve(roiPath);

            for (int i = 0; i < counts.ResultList.Count; i++)
            {
                MeasurementResult raw = counts.ResultList[i];
                ROIDefinitionData zone = raw.ROIDefinition;
                Console.WriteLine("зона {0}  [{1:F0}–{2:F0} кэВ], линия {3:F2} кэВ, выход {4:F2} %",
                                  zone.Name, zone.LowerLimit, zone.UpperLimit,
                                  zone.PeakEnergy, zone.Intencity);
                if (!raw.IsValid)
                {
                    Console.WriteLine("  НЕ СЧИТАЕТСЯ: {0}", raw.StatusText);
                    continue;
                }

                MeasurementResult rateRow = cps.ResultList[i];
                Console.WriteLine("  чистый счёт {0:F0} ± {1:F0}; {2:F4} ± {3:F4} имп/с",
                                  raw.ResultValue, raw.ResultError,
                                  rateRow.ResultValue, rateRow.ResultError);

                // Как считает ПРИЛОЖЕНИЕ сегодня.
                BecquerelCoefficient.Result active =
                    BecquerelCoefficient.Resolve(zone, rd.Efficiency);
                MeasurementResult bqRow = bq.ResultList[i];
                if (bqRow.IsValid)
                {
                    Console.WriteLine("  ПРИЛОЖЕНИЕ: {0:F0} ± {1:F0} Бк (K={2:F0}, {3}); МДА {4:F0} Бк",
                                      bqRow.ResultValue, bqRow.ResultError, active.Value,
                                      active.From == BecquerelCoefficient.Source.Efficiency
                                          ? "по кривой" : "запасённый",
                                      bqRow.MDA);
                }
                else
                {
                    Console.WriteLine("  ПРИЛОЖЕНИЕ: {0}", bqRow.StatusText);
                }

                // Варианты K по всем доступным кривым.
                PrintVariant("запасённый K зоны", zone.BecquerelCoefficient,
                             zone.BecquerelCoefficientError, rateRow.ResultValue,
                             double.NaN, passport, zone.Intencity);
                PrintCurveVariant("кривая СПЕКТРА", rd.Efficiency, zone, rateRow.ResultValue, passport);
                PrintCurveVariant("легаси-кривая ROI-файла", legacy, zone, rateRow.ResultValue, passport);
                Console.WriteLine();
            }

            return 0;
        }

        static void PrintCurveVariant(string title, EfficiencyConfigData config,
                                      ROIDefinitionData zone, double measuredCps, double passport)
        {
            double value, error;
            if (config == null
                || !BecquerelCoefficient.TryForLine(zone.PeakEnergy, zone.Intencity, config,
                                                    out value, out error))
            {
                Console.WriteLine("    {0,-26} кривой нет или линия вне сетки", title);
                return;
            }

            PrintVariant(title, value, error, measuredCps, 100.0 / (value * zone.Intencity),
                         passport, zone.Intencity);
        }

        static void PrintVariant(string title, double k, double kError, double measuredCps,
                                 double eps, double passport, double intensity)
        {
            string line = string.Format(CultureInfo.InvariantCulture,
                                        "    {0,-26} K={1,10:F0} ± {2:F0}  ->  {3,8:F0} Бк",
                                        title, k, kError, measuredCps * k);
            if (!double.IsNaN(eps))
            {
                line += string.Format(CultureInfo.InvariantCulture, "   (eps={0:E3})", eps);
            }

            if (passport > 0.0)
            {
                line += string.Format(CultureInfo.InvariantCulture, "   Бк/паспорт = {0:F3}",
                                      measuredCps * k / passport);
                if (!double.IsNaN(eps) && intensity > 0.0)
                {
                    line += string.Format(CultureInfo.InvariantCulture,
                                          "; ожидаемый cps = {0:F4}, измеренный/ожидаемый = {1:F3}",
                                          passport * intensity / 100.0 * eps,
                                          measuredCps / (passport * intensity / 100.0 * eps));
                }
            }

            Console.WriteLine(line);
        }

        /// <summary>Точки узла &lt;ROIEfficiency&gt; прямо из XML файла.</summary>
        static EfficiencyConfigData ReadLegacyCurve(string path)
        {
            string text = File.ReadAllText(path);
            Match section = Regex.Match(text, "<ROIEfficiency>(.*?)</ROIEfficiency>", RegexOptions.Singleline);
            if (!section.Success)
            {
                return null;
            }

            var config = new EfficiencyConfigData { Name = "legacy ROIEfficiency" };
            foreach (Match m in Regex.Matches(section.Groups[1].Value,
                     @"<ROIEfficiencyData>\s*<Energy>(.*?)</Energy>\s*<Efficiency>(.*?)</Efficiency>"
                     + @"\s*<ErrorPercent>(.*?)</ErrorPercent>\s*</ROIEfficiencyData>",
                     RegexOptions.Singleline))
            {
                config.Curve.Add(new ROIEfficiencyData
                {
                    Energy = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    Efficiency = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    ErrorPercent = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                });
            }

            return config.Curve.Count >= 2 ? config : null;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            return file.ResultDataList[0];
        }
    }
}
