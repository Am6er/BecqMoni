using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Channels;
using System.Windows.Forms;
using System.Xml;
using Windows.Networking;

namespace BecquerelMonitor.N42
{
    public class Util
    {
        public Util()
        {

        }

        public RadInstrumentData ExportToN42(DocEnergySpectrum doc)
        {
            RadInstrumentData rad = new RadInstrumentData();

            //RadInstrumentInformation
            rad.RadInstrumentInformation = new RadInstrumentInformation[1];
            rad.RadInstrumentInformation[0] = new RadInstrumentInformation();
            rad.RadInstrumentInformation[0].RadInstrumentManufacturerName = "KB Radar";
            rad.RadInstrumentInformation[0].RadInstrumentModelName = "ATOM Spectra";
            rad.RadInstrumentInformation[0].RadInstrumentVersion = new RadInstrumentVersion[3];
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0].RadInstrumentComponentName = "Hardware";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0].RadInstrumentComponentVersion = doc.ActiveResultData.DeviceConfig.Name;
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1].RadInstrumentComponentName = "SoftwareName";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1].RadInstrumentComponentVersion = "BecqMoni";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2].RadInstrumentComponentName = "Software";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2].RadInstrumentComponentVersion = GlobalConfigManager.GetInstance().VersionString;

            int SpectrumCount = doc.ResultDataFile.ResultDataList.Count;

            int bgcount = 0;
            for (int i = 0; i < SpectrumCount; i++)
            {
                if (doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum != null)
                {
                    bgcount++;
                }
            }

            rad.EnergyCalibration = new EnergyCalibration[SpectrumCount + bgcount];
            rad.RadMeasurement = new RadMeasurement[SpectrumCount + bgcount];

            int j = 0;
            for (int i = 0; i < SpectrumCount; i++)
            {
                string calibrationId = "SpectrumCalibration-" + i;
                string idMeasurement = "SpectrumMeasurement-" + i;
                string measurementClassCode = "Foreground";
                string idSpectrum = "SpectrumData";
                string idGrossCounts = "GrossForeground";
                rad.EnergyCalibration[j] = AddCalibration(doc.ResultDataFile.ResultDataList[i].EnergySpectrum, calibrationId);
                rad.RadMeasurement[j] = AddMeasurement(doc.ResultDataFile.ResultDataList[i], idMeasurement, measurementClassCode, idSpectrum, calibrationId, idGrossCounts);
                j++;

                if (doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum != null)
                {
                    calibrationId = "BackgroundCalibration-" + i;
                    idMeasurement = "BackgroundMeasurement-" + i;
                    measurementClassCode = "Background";
                    idSpectrum = "BackgroundData";
                    idGrossCounts = "GrossBackground";
                    rad.EnergyCalibration[j] = AddCalibration(doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum, calibrationId);
                    rad.RadMeasurement[j] = AddMeasurement(doc.ResultDataFile.ResultDataList[i], idMeasurement, measurementClassCode, idSpectrum, calibrationId, idGrossCounts);
                    j++;
                }
            }

            return rad;
        }

        private RadMeasurement AddMeasurement(ResultData data, string idMeasurement, string measurementClassCode, string idSpectrum, string idEnergyCalibration, string idGrossCounts)
        {
            RadMeasurement rad;
            if (measurementClassCode == "Foreground")
            {
                rad = new RadMeasurement
                {
                    id = idMeasurement, //"SpectrumMeasurement",
                    MeasurementClassCode = measurementClassCode, // "Foreground",
                    StartDateTime = data.StartTime.ToString(),
                    RealTimeDuration = "PT" + data.EnergySpectrum.MeasurementTime + "S",
                    //RadMeasurement -> Spectrum
                    Spectrum = new Spectrum[1]
                };
                rad.Spectrum[0] = new Spectrum
                {
                    id = idSpectrum, //"SpectrumData",
                    energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                    LiveTimeDuration = "PT" + data.EnergySpectrum.LiveTime + "S"
                };
                rad.Spectrum[0].ChannelData.SpectrumFromArray(data.EnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.GrossCounts = new GrossCounts[1];
                rad.GrossCounts[0] = new GrossCounts
                {
                    id = idGrossCounts, //"GrossForeground",
                    TotalCounts = data.EnergySpectrum.TotalPulseCount.ToString()
                };
            } else
            {
                rad = new RadMeasurement
                {
                    id = idMeasurement, //"SpectrumMeasurement",
                    MeasurementClassCode = measurementClassCode, // "Background",
                    StartDateTime = data.StartTime.ToString(),
                    RealTimeDuration = "PT" + data.BackgroundEnergySpectrum.MeasurementTime + "S",
                    //RadMeasurement -> Spectrum
                    Spectrum = new Spectrum[1]
                };
                rad.Spectrum[0] = new Spectrum
                {
                    id = idSpectrum, //"SpectrumData",
                    energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                    LiveTimeDuration = "PT" + data.BackgroundEnergySpectrum.MeasurementTime + "S"
                };
                rad.Spectrum[0].ChannelData.SpectrumFromArray(data.BackgroundEnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.GrossCounts = new GrossCounts[1];
                rad.GrossCounts[0] = new GrossCounts
                {
                    id = idGrossCounts, //"GrossBackground",
                    TotalCounts = data.BackgroundEnergySpectrum.TotalPulseCount.ToString()
                };
            }
            return rad;
        }

        private EnergyCalibration AddCalibration(EnergySpectrum input, string id)
        {
            PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)input.EnergyCalibration;
            EnergyCalibration output = new EnergyCalibration();
            output.id = id;
            for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
            {
                output.CoefficientValues = output.CoefficientValues + energyCalibration.Coefficients[i].ToString() + " ";

            }
            output.CoefficientValues = output.CoefficientValues.Replace(',', '.');
            return output;
        }

        public DocEnergySpectrum ImportFromN42(RadiologicalInstrumentData rad, DocEnergySpectrum doc, string filename)
        {
            string SpectrumName = Path.GetFileNameWithoutExtension(filename);
            doc.Filename = SpectrumName + ".xml";
            doc.Text = SpectrumName;

            if (rad.MeasurementGroup.Measurement.Spectrum.EnergyCalibration.CalibrationEquation != "List")
            {
                throw new InvalidOperationException("Unsupported type of calibration. Expected CalibrationEquation = List.");
            }

            string[] chanEnergy = rad.MeasurementGroup.Measurement.Spectrum.EnergyCalibration.ChannelEnergies.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
            chanEnergy = Array.FindAll(chanEnergy, isNotN42SpectrumValid);

            string[] chanData = rad.MeasurementGroup.Measurement.Spectrum.ChannelData.Data.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
            chanData = Array.FindAll(chanData, isNotN42SpectrumValid);

            int NumberOfChanels = rad.MeasurementGroup.Measurement.Spectrum.ChannelData.NumberOfChannels;
            double lifetime = rad.MeasurementGroup.Measurement.Spectrum.LiveTime;
            string spectrumtype = rad.MeasurementGroup.Measurement.Spectrum.SpectrumType;
            AH_InstrumentInformation instrument = rad.MeasurementGroup.Measurement.Spectrum.InstrumentInformation;

            ResultData resultData = doc.ActiveResultData;
            List<CalibrationPoint> listCalibration = new List<CalibrationPoint>();
            long totalpulsecount = 0;
            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            energySpectrum.Initialize();

            for (int i = 0; i < NumberOfChanels; i++)
            {
                int count = int.Parse(chanData[i]);
                // `A142`: разбор ЧИСЛА в файле не зависит от культуры машины.
                decimal energy = decimal.Parse(chanEnergy[i], CultureInfo.InvariantCulture);

                energySpectrum.Spectrum[i] = count;
                totalpulsecount += count;

                CalibrationPoint calibrationPoint = new CalibrationPoint(i, energy, count);
                listCalibration.Add(calibrationPoint);
            }

            energySpectrum.MeasurementTime = lifetime;
            energySpectrum.TotalPulseCount = totalpulsecount;
            energySpectrum.ValidPulseCount = totalpulsecount;
            ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;
            resultDataStatus.TotalTime = TimeSpan.FromSeconds(energySpectrum.MeasurementTime);
            resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(energySpectrum.MeasurementTime);
            resultData.SampleInfo.Time = DateTime.Now;
            resultData.SampleInfo.Note = $"Manufacturer = {instrument.Manufacturer}, Model = {instrument.Model}, SerialNumber = {instrument.SerialNumber}";

            // calibration part
            PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration();
            if (listCalibration.Count >= 5)
            {
                double[] matrix = CalibrationSolver.Solve(listCalibration, 4);
                calibration.Coefficients = new double[matrix.Length];
                calibration.Coefficients = matrix;
                calibration.PolynomialOrder = matrix.Length - 1;

                // ⛔ `A141`: ТА ЖЕ ПРОВЕРКА, ЧТО У ОБОИХ СОСЕДНИХ ПУТЕЙ РАЗБОРА.
                //    Подогнанный полином 4-го порядка здесь НЕ ПРОВЕРЯЛСЯ вовсе,
                //    тогда как разборы 2006 и 2012 годов зовут CheckCalibration и
                //    без окон отказывают. Третьего соглашения в одном файле быть
                //    не должно: негодная шкала задаёт подписи пиков, состав
                //    библиотеки и разложение — по ней получаются правдоподобные
                //    и чужие числа.
                if (!calibration.CheckCalibration(channels: energySpectrum.NumberOfChannels))
                {
                    if (!AppUi.HasWindows)
                    {
                        throw new InvalidOperationException(
                            "BecqMoni: " + Resources.CalibrationFunctionError
                            + " (N42 RadiologicalInstrumentData, " + AppUi.Where(filename)
                            + ", порядок " + calibration.PolynomialOrder
                            + "). Дальше по этому спектру считать нельзя: энергетическая шкала негодна.");
                    }
                    AppUi.Report(Resources.CalibrationFunctionError, "", MessageBoxIcon.None);
                }
            }
            else
            {
                // ⛔ `A141`: y = x ПОДСТАВЛЯЛАСЬ МОЛЧА. Конструктор
                //    PolynomialEnergyCalibration ставит порядок 1 и коэффициенты
                //    { 0, 1 } — то есть номер канала объявляется энергией, и об
                //    этом не говорилось ни слова. Дверь ТА ЖЕ, что у соседей
                //    (`A137`, `A95`): без окон отказ с кодом возврата, в окнах —
                //    вслух, и работа продолжается.
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: в файле N42 (" + AppUi.Where(filename) + ") точек калибровки "
                        + listCalibration.Count + ", а подгонка полинома требует не меньше пяти: "
                        + "подстановка y = x объявляет номер канала энергией — "
                        + "считать по такому спектру нельзя.");
                }
                AppUi.Report(Resources.ERRTooFewCalibrationPointsN42, "", MessageBoxIcon.None);
            }

            energySpectrum.EnergyCalibration = calibration.Clone();
            return doc;
        }

        public int N42_2006_getChannels(N42InstrumentData rad)
        {
            string[] chanData = rad.Measurement.Spectrum.ChannelData.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
            chanData = Array.FindAll(chanData, isNotN42SpectrumValid);
            return chanData.Length;
        }

        public DocEnergySpectrum ImportFromN42(N42InstrumentData rad, DocEnergySpectrum doc, string filename)
        {
            string SpectrumName = Path.GetFileNameWithoutExtension(filename);
            doc.Filename = SpectrumName + ".xml";
            doc.Text = SpectrumName;

            N42_2006_EnergyCalibration calibration = null;

            for(int i = 0; i < rad.Calibration.Length; i++)
            {
                if (rad.Calibration[i].Type == "Energy")
                {
                    calibration = rad.Calibration[i];
                    break;
                }
            }

            if (calibration == null)
            {
                throw new InvalidOperationException("No calibration section found in N42InstrumentData.");
            }

            string[] enCalibrationCoeff = calibration.Equation.Coefficients.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
            enCalibrationCoeff = Array.FindAll(enCalibrationCoeff, isNotN42SpectrumValid);

            string[] chanData = rad.Measurement.Spectrum.ChannelData.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
            chanData = Array.FindAll(chanData, isNotN42SpectrumValid);

            int NumberOfChanels = chanData.Length;
            double lifetime = XmlConvert.ToTimeSpan(rad.Measurement.Spectrum.LiveTime).TotalSeconds;
            double realtime = XmlConvert.ToTimeSpan(rad.Measurement.Spectrum.RealTime).TotalSeconds;
            string starttime = rad.Measurement.Spectrum.StartTime;
            N42_2006_InstrumentInformation instrument = rad.Measurement.InstrumentInformation;

            string spectrumtype = rad.Measurement.Spectrum.Type;

            ResultData resultData = doc.ActiveResultData;
            long totalpulsecount = 0;
            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            energySpectrum.Initialize();

            for (int i = 0; i < NumberOfChanels; i++)
            {
                int count = int.Parse(chanData[i]);
                energySpectrum.Spectrum[i] = count;
                totalpulsecount += count;
            }

            resultData.EnergySpectrum.MeasurementTime = realtime;
            resultData.EnergySpectrum.LiveTime = lifetime;
            resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(realtime);
            resultData.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(realtime);
            resultData.ResultDataStatus.PresetTime = (int)realtime;
            energySpectrum.TotalPulseCount = totalpulsecount;
            energySpectrum.ValidPulseCount = totalpulsecount;
            ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;
            resultData.SampleInfo.Time = XmlConvert.ToDateTime(starttime, XmlDateTimeSerializationMode.Utc);
            resultData.SampleInfo.Note = $"InstrumentType = {instrument.InstrumentType}, " +
                $"Manufacturer = {instrument.Manufacturer}, " +
                $"InstrumentModel = {instrument.InstrumentModel}, " +
                $"InstrumentID = {instrument.InstrumentID}, " +
                $"ProbeType = {instrument.ProbeType}";



            resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
            if (enCalibrationCoeff.Length == 0)
            {
                // ⛔ `A140`: БРОСОК БЕЗ СООБЩЕНИЯ НЕ НАЗЫВАЕТ НИЧЕГО. Отсюда
                //    исключение уходит наружу и ловится дверью AppUi в
                //    DocumentManager.ImportDocumentN42, которая вставляет его
                //    текст в свою строку; пустое сообщение доезжало до человека
                //    как «Выдано исключение типа "System.Exception"».
                //    Второй двери здесь НЕ заводится — заводится ПРИЧИНА для
                //    той, что уже стоит.
                throw new Exception(
                    "в разделе Calibration файла N42 (спецификация 2006) пуст список "
                    + "коэффициентов Equation/Coefficients — энергетической шкалы нет");
            }
            int PolynomialOrder = enCalibrationCoeff.Length - 1;

            // PolynomialEnergyCalibration supports order 4 at most (see ChannelToEnergy);
            // the old check allowed order 5, which later failed outside the import's
            // try/catch at draw time.
            if (PolynomialOrder > 4)
            {
                throw new Exception("Unsupported calibration points number. Got polynom order = " + PolynomialOrder);
            }

            double[] coefficients = new double[PolynomialOrder + 1];

            for (int k = 0; k < coefficients.Length; k++)
            {
                // `A142`: см. пояснение у разбора 2012 года ниже.
                coefficients[k] = double.Parse(enCalibrationCoeff[k], CultureInfo.InvariantCulture);
            }

            PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)resultData.EnergySpectrum.EnergyCalibration;
            energyCalibration.PolynomialOrder = PolynomialOrder;
            energyCalibration.Coefficients = coefficients;

            if (!energyCalibration.CheckCalibration(channels: resultData.EnergySpectrum.NumberOfChannels))
            {
                // ОТКАЗ без окон, а не уведомление: калибровка в файле есть, но
                // она не годится, — а документ отсюда возвращается КАК УДАЧНЫЙ,
                // и дальше по нему считают. Шкала энергий задаёт всё: подписи
                // пиков, состав библиотеки, разложение. Молча посчитать по
                // негодной шкале значит выдать правдоподобные и чужие числа.
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: " + Resources.CalibrationFunctionError
                        + " (N42, " + AppUi.Where(filename) + ", порядок " + PolynomialOrder
                        + "). Дальше по этому спектру считать нельзя: энергетическая шкала негодна.");
                }
                AppUi.Report(Resources.CalibrationFunctionError, "", MessageBoxIcon.None);
            }


            return doc;
        }

        public int N42_2012_getChannels(RadInstrumentData rad)
        {
            // Use ChannelData.SpectrumToArray() so CountedZeroes (N42 run-length) files
            // report the expanded channel count, not the compressed token count.
            return rad.RadMeasurement[0].Spectrum[0].ChannelData.SpectrumToArray().Length;
        }

        public DocEnergySpectrum ImportFromN42(RadInstrumentData rad, DocEnergySpectrum doc, string filename)
        {
            // ⛔ ФАЙЛ БЕЗ ЭЛЕМЕНТА EnergyCalibration — ТО ЖЕ ПОЛОЖЕНИЕ,
            //    что и в catch ниже, и соглашение здесь ТО ЖЕ (`A137`).
            //    Ниже по тексту такому файлу подставляется
            //    CoefficientValues = "0 1", то есть y = x: номер канала
            //    объявляется энергией, спектр выглядит целым и все
            //    числа по нему не о том. До 04.09.2026 это делалось МОЛЧА,
            //    тогда как соседний catch то же самое считал опасным, —
            //    два соглашения об одном положении в одном методе.
            //
            //    Проверка стоит ДО цикла нарочно: отсутствие калибровки —
            //    свойство ВСЕГО файла, а не отдельного измерения,
            //    и говориться оно обязано один раз, а не по числу спектров.
            if (rad.EnergyCalibration == null)
            {
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: в файле N42 нет энергетической калибровки ("
                        + AppUi.Where(filename)
                        + "): подстановка y = x объявляет номер канала энергией — "
                        + "считать по такому спектру нельзя.");
                }
                AppUi.Report(Resources.ERRNoEnergyCalibrationN42, "", MessageBoxIcon.None);
            }

            int SpectrumCount = rad.RadMeasurement.Length;

            string SpectrumName = Path.GetFileNameWithoutExtension(filename);
            doc.Filename = SpectrumName + ".xml";
            doc.Text = SpectrumName;

            for (int i = 0; i < SpectrumCount; i++)
            {
                RadMeasurement radMeasurement = rad.RadMeasurement[i];
                EnergyCalibration radCalibration;
                if (rad.EnergyCalibration == null)
                {
                    // No energy calibration, according N42 spec, create empty one.
                    radCalibration = new EnergyCalibration
                    {
                        CoefficientValues = "0 1",
                        id = "Empty"
                    };
                }
                else if (rad.EnergyCalibration.Length <= i)
                {
                    radCalibration = rad.EnergyCalibration[0];
                } else
                {
                    radCalibration = rad.EnergyCalibration[i];
                }
                ResultData resultData = new ResultData();
                resultData.MeasurementController = doc.ActiveResultData.MeasurementController;
                resultData.SampleInfo.Time = DateTime.Now;

                try
                {
                    if (radMeasurement.StartDateTime != null && radMeasurement.StartDateTime != "")
                    {
                        resultData.SampleInfo.Time = ParseMeasurementStartDateTime(radMeasurement.StartDateTime);
                    }
                }
                catch { }

                resultData.SampleInfo.Name = SpectrumName;
                resultData.SampleInfo.Note = "";

                try
                {
                    foreach (RadInstrumentInformation radIterator in rad.RadInstrumentInformation)
                    {
                        resultData.SampleInfo.Note = resultData.SampleInfo.Note + radIterator.RadInstrumentManufacturerName + " - " + radIterator.RadInstrumentModelName + Environment.NewLine;
                        foreach (RadInstrumentVersion radInsIterator in radIterator.RadInstrumentVersion)
                        {
                            resultData.SampleInfo.Note = resultData.SampleInfo.Note + radInsIterator.RadInstrumentComponentName + " - " + radInsIterator.RadInstrumentComponentVersion + Environment.NewLine;
                        }

                    }

                    if (rad.RadDetectorInformation != null)
                    {
                        foreach (RadDetectorInformation radDetIterator in rad.RadDetectorInformation)
                        {
                            resultData.SampleInfo.Note = resultData.SampleInfo.Note + radDetIterator.RadDetectorCategoryCode + " - " + radDetIterator.RadDetectorKindCode + Environment.NewLine;
                        }
                    }
                }
                catch
                { }
                int LiveTime = 0;
                int ElapsedTime = 0;
                if (radMeasurement.Spectrum[0].LiveTimeDuration != null && radMeasurement.Spectrum[0].LiveTimeDuration != "")
                {
                    LiveTime = (int)XmlConvert.ToTimeSpan(radMeasurement.Spectrum[0].LiveTimeDuration).TotalSeconds;
                }
                if (radMeasurement.RealTimeDuration != null && radMeasurement.RealTimeDuration != "")
                {
                    ElapsedTime = (int)XmlConvert.ToTimeSpan(radMeasurement.RealTimeDuration).TotalSeconds;
                }
                resultData.EnergySpectrum.MeasurementTime = ElapsedTime;
                resultData.EnergySpectrum.LiveTime = LiveTime;
                resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(ElapsedTime);
                resultData.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(ElapsedTime);
                resultData.ResultDataStatus.PresetTime = ElapsedTime;

                // ChannelData.SpectrumToArray() handles compressionCode="CountedZeroes"
                // (N42 run-length); the old manual split ignored it and produced a wrong
                // spectrum for compressed files.
                int[] n42Spectrum = radMeasurement.Spectrum[0].ChannelData.SpectrumToArray();
                int NumberOfChanels = n42Spectrum.Length;
                resultData.EnergySpectrum.Spectrum = new int[NumberOfChanels];
                resultData.EnergySpectrum.NumberOfChannels = NumberOfChanels;
                resultData.EnergySpectrum.ChannelPitch = 1;
                for (int k = 0; k < NumberOfChanels; k++)
                {
                    resultData.EnergySpectrum.Spectrum[k] = n42Spectrum[k];
                }
                resultData.EnergySpectrum.TotalPulseCount = 0;
                // TotalCounts is xs:double per the N42 standard (e.g. "52341.0");
                // long.Parse used to throw on it and was culture-dependent. Fall back to
                // the array sum when the field is missing or unparsable.
                if (radMeasurement.GrossCounts != null && radMeasurement.GrossCounts.Length > 0
                    && double.TryParse(radMeasurement.GrossCounts[0].TotalCounts,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double totalCountsValue))
                {
                    resultData.EnergySpectrum.TotalPulseCount = (long)Math.Round(totalCountsValue);
                } else
                {
                    resultData.EnergySpectrum.TotalPulseCount = resultData.EnergySpectrum.Spectrum.Sum(x => (long)x);
                }
                resultData.EnergySpectrum.ValidPulseCount = resultData.EnergySpectrum.TotalPulseCount;

                try
                {
                    resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();

                    // ⛔ `A136`: ШКАЛА, ЗАДАННАЯ ГРАНИЦАМИ КАНАЛОВ, — ОТДЕЛЬНАЯ
                    //    ПРИЧИНА, а не «что-то с калибровкой». В N42-2011/2012
                    //    объект EnergyCalibration несёт ЛИБО CoefficientValues,
                    //    ЛИБО EnergyBoundaryValues — список энергий границ каналов;
                    //    приложение умеет только полином, и это не меняется.
                    //    ⚠ До 05.09.2026 разбор этого положения НЕ РАЗЛИЧАЛ вовсе:
                    //    поля EnergyBoundaryValues в модели не было, XmlSerializer
                    //    пропускал элемент МОЛЧА, CoefficientValues приходил пустым —
                    //    и файл попадал в тот же catch, что нечисло в коэффициентах и
                    //    порядок полинома больше четырёх. Один текст на три разные
                    //    беды: человеку называлась причина, которой могло не быть.
                    if (HasEnergyBoundaryValues(radCalibration))
                    {
                        throw new Exception(
                            "шкала в файле N42 задана границами энергий каналов "
                            + "(EnergyBoundaryValues), а приложение читает только "
                            + "коэффициенты полинома (CoefficientValues)");
                    }

                    string[] n42CalibrationCoeff = radCalibration.CoefficientValues.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
                    n42CalibrationCoeff = Array.FindAll(n42CalibrationCoeff, isNotN42SpectrumValid);
                    if (n42CalibrationCoeff.Length == 0)
                    {
                        // ⛔ `A140`: то же положение и ТА ЖЕ причина словами, что
                        //    и в разборе 2006 года выше. Сообщение отсюда читает
                        //    соседний catch — и в окне, и без окон, — поэтому
                        //    второго соглашения здесь не заводится.
                        throw new Exception(
                            "в объекте EnergyCalibration файла N42 пуст список "
                            + "коэффициентов CoefficientValues — энергетической шкалы нет");
                    }
                    int PolynomialOrder = n42CalibrationCoeff.Length - 1;

                    // Max supported order is 4 (see PolynomialEnergyCalibration).
                    if (PolynomialOrder > 4)
                    {
                        throw new Exception("Unsupported calibration points number. Got polynom order = " + PolynomialOrder);
                    }

                    double[] coefficients = new double[PolynomialOrder + 1];

                    for (int k = 0; k < coefficients.Length; k++)
                    {
                        // ⛔ `A142`: ИНВАРИАНТНАЯ КУЛЬТУРА, А НЕ КУЛЬТУРА МАШИНЫ.
                        //    В N42 число записано с ТОЧКОЙ по спецификации, а
                        //    double.Parse без культуры читает разделителем то, что
                        //    стоит у машины. Измерено 04.09.2026: под ru-RU ни один
                        //    из 12 выгруженных корпусных файлов не ввозился
                        //    (FormatException), под en-US ввозились все 12.
                        //    ⚠ В окнах это не проявлялось только потому, что
                        //    MainForm.cs:162-164 глобально ставит разделителем точку
                        //    при запуске: разбор ФАЙЛА держался на настройке,
                        //    сделанной в другом месте и по другому поводу.
                        coefficients[k] = double.Parse(n42CalibrationCoeff[k], CultureInfo.InvariantCulture);
                    }
                    
                    PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)resultData.EnergySpectrum.EnergyCalibration;
                    energyCalibration.PolynomialOrder = PolynomialOrder;
                    energyCalibration.Coefficients = coefficients;

                    if (!energyCalibration.CheckCalibration(channels: resultData.EnergySpectrum.NumberOfChannels))
                    {
                        // ОТКАЗ без окон — по той же причине, что и в разборе
                        // 2006 года: спектр уходит в документ и считается по
                        // негодной шкале, а сказать об этом некому.
                        if (!AppUi.HasWindows)
                        {
                            throw new InvalidOperationException(
                                "BecqMoni: " + Resources.CalibrationFunctionError
                                + " (N42-2012, " + AppUi.Where(filename) + ", порядок " + PolynomialOrder
                                + "). Дальше по этому спектру считать нельзя: энергетическая шкала негодна.");
                        }
                        AppUi.Report(Resources.CalibrationFunctionError, "", MessageBoxIcon.None);
                    }
                }
                catch (Exception ex)
                {
                    // ⛔ САМОЕ ОПАСНОЕ МЕСТО ВСЕГО РАЗБОРА N42, и оно же самое
                    //    тихое: калибровка не прочиталась — и подставляется
                    //    y = x, то есть НОМЕР КАНАЛА объявляется энергией.
                    //    Спектр после этого выглядит целым, счёт идёт, числа
                    //    получаются, и все они не о том. В окнах человек хотя
                    //    бы читает предупреждение; без окон читать некому,
                    //    поэтому здесь отказ с кодом возврата.
                    //
                    //    ⚠ Отказ бросается ИЗ catch: `throw` внутри `catch`
                    //    уходит наружу, минуя остаток блока, — подстановка
                    //    y = x при этом не выполняется вовсе.
                    // `A135`: текст берётся из ресурса, а не из литерала:
                    // литерал не переводится, и русское значение ключа годами
                    // лежало в ru.resx без читателя. ⚠ Значение ключа тем же
                    // движением приведено к поведению (`A138`): оно говорило
                    // «Using current calibration», а код подставляет y = x.
                    // `A136`: ПРИЧИНЫ РАЗВЕДЕНЫ. Границы энергий — законное
                    // положение N42 и своя причина, у неё свой переведённый
                    // текст. Всё остальное — нечисло в коэффициентах, пустой
                    // список (`A140`), порядок полинома больше четырёх —
                    // называет себя САМО, сообщением своего исключения.
                    string text = HasEnergyBoundaryValues(radCalibration)
                                  ? Resources.ERRUnsupportedEnergyBoundaryN42
                                  : ex.Message;
                    if (!AppUi.HasWindows)
                    {
                        throw new InvalidOperationException(
                            "BecqMoni: калибровка N42 не прочитана (" + AppUi.Where(filename)
                            + "): " + ex.Message
                            + ". Подстановка y = x объявляет номер канала энергией — "
                            + "считать по такому спектру нельзя.", ex);
                    }
                    AppUi.Report(text, "", MessageBoxIcon.None);
                }
                if (i == 0)
                {
                    doc.ResultDataFile.ResultDataList[0] = resultData;
                } else
                {
                    doc.ResultDataFile.ResultDataList.Add(resultData);
                }
            }
            doc.ResultDataFile.ResultDataList[0].Visible = true;
            return doc;
        }

        /// <summary>
        /// Задана ли шкала ГРАНИЦАМИ ЭНЕРГИЙ КАНАЛОВ (`A136`).
        ///
        /// Спрашивается ДВАЖДЫ и нарочно одним местом: сначала в try — чтобы
        /// бросок назвал причину словами, — потом в catch, чтобы выбрать
        /// переведённый текст. Два разных условия об одном положении уже
        /// расходились в этом дереве молча.
        /// </summary>
        private static bool HasEnergyBoundaryValues(EnergyCalibration radCalibration)
        {
            return radCalibration != null
                   && radCalibration.EnergyBoundaryValues != null
                   && radCalibration.EnergyBoundaryValues.Trim().Length > 0;
        }

        private bool isNotN42SpectrumValid(string str)
        {
            if (str == "" || str == "\n")
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        private static DateTime ParseMeasurementStartDateTime(string startDateTime)
        {
            try
            {
                return XmlConvert.ToDateTime(startDateTime, XmlDateTimeSerializationMode.RoundtripKind);
            }
            catch (FormatException)
            {
                DateTime parsedDateTime;
                if (DateTime.TryParse(startDateTime, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedDateTime))
                {
                    return parsedDateTime;
                }

                if (DateTime.TryParse(startDateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDateTime))
                {
                    return parsedDateTime;
                }

                throw;
            }
        }

    }
}
