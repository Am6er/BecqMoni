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
                    StartDateTime = N42DateTime(data.StartTime),
                    RealTimeDuration = N42Duration(data.EnergySpectrum.MeasurementTime),
                    //RadMeasurement -> Spectrum
                    Spectrum = new Spectrum[1]
                };
                rad.Spectrum[0] = new Spectrum
                {
                    id = idSpectrum, //"SpectrumData",
                    energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                    LiveTimeDuration = N42Duration(data.EnergySpectrum.LiveTime)
                };
                rad.Spectrum[0].ChannelData.SpectrumFromArray(data.EnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.GrossCounts = new GrossCounts[1];
                rad.GrossCounts[0] = new GrossCounts
                {
                    id = idGrossCounts, //"GrossForeground",
                    TotalCounts = N42Count(data.EnergySpectrum.TotalPulseCount)
                };
            } else
            {
                rad = new RadMeasurement
                {
                    id = idMeasurement, //"SpectrumMeasurement",
                    MeasurementClassCode = measurementClassCode, // "Background",
                    StartDateTime = N42DateTime(data.StartTime),
                    RealTimeDuration = N42Duration(data.BackgroundEnergySpectrum.MeasurementTime),
                    //RadMeasurement -> Spectrum
                    Spectrum = new Spectrum[1]
                };
                rad.Spectrum[0] = new Spectrum
                {
                    id = idSpectrum, //"SpectrumData",
                    energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                    // ⛔ `A155`: ЖИВОЕ ВРЕМЯ ФОНА — ЖИВОЕ, А НЕ ПОЛНОЕ. Прежде сюда
                    //    писалось BackgroundEnergySpectrum.MeasurementTime, то есть
                    //    ПОЛНОЕ время фона, а поле LiveTime фона не выгружалось
                    //    ВООБЩЕ. Измерено 05.09.2026 на 11 файлах дерева с фоном: у
                    //    вывезенного фона «изм» и «живое» после круга равнялись друг
                    //    другу, потому что в файл уходило одно и то же число.
                    //    ⚠ Это не косметика: живое время стоит в ЗНАМЕНАТЕЛЕ скорости
                    //    счёта фона, и подмена его полным занижает скорость ровно на
                    //    мёртвое время прибора — у переднего спектра то же поле
                    //    выгружается честно (ветка Foreground выше), то есть разница
                    //    вносилась ТОЛЬКО в фон и вычитание фона переставало быть
                    //    вычитанием той же величины.
                    LiveTimeDuration = N42Duration(data.BackgroundEnergySpectrum.LiveTime)
                };
                rad.Spectrum[0].ChannelData.SpectrumFromArray(data.BackgroundEnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.GrossCounts = new GrossCounts[1];
                rad.GrossCounts[0] = new GrossCounts
                {
                    id = idGrossCounts, //"GrossBackground",
                    TotalCounts = N42Count(data.BackgroundEnergySpectrum.TotalPulseCount)
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
                // ⛔ `A148`: ФОРМАТ "R", А НЕ ToString(). Без формата double
                //    печатается как «G15», то есть пятнадцатью значащими
                //    цифрами, а для точного обратного чтения их нужно
                //    семнадцать. Измерено 05.09.2026 на 12 выгруженных файлах:
                //    коэффициенты расходились у 9 из 12, относительно до
                //    3.451E-15, а по шкале — до 7.7E-12 кэВ. Величина физически
                //    ничтожна, но круг «вывоз → ввоз» из-за неё не сходился
                //    ТОЧНО, и всякая побайтовая приёмка на нём слепла.
                output.CoefficientValues = output.CoefficientValues
                    + N42Number(energyCalibration.Coefficients[i]) + " ";

            }
            // ⛔ `A146`: ЗДЕСЬ БЫЛА .Replace(',', '.') — ПОЛОВИНА ПОЧИНКИ, И ЕЁ БОЛЬШЕ НЕТ.
            //    Про запятую в этом классе знали и правили её ровно у коэффициентов,
            //    а у длительностей и даты нет. Подмена символа лечила только те
            //    культуры, у которых разделитель дробной части — запятая: у fa-IR
            //    это U+066B, и там она не помогала вовсе. Теперь число печатается
            //    инвариантной культурой ИЗНАЧАЛЬНО, и подменять в нём нечего.
            return output;
        }

        // ==================================================================
        // ЧИСЛО, ДЛИТЕЛЬНОСТЬ И ДАТА В ФАЙЛ N42 (`A146`, `A148`)
        //
        // ⛔ ФАЙЛ ПИШЕТСЯ ИНВАРИАНТНОЙ КУЛЬТУРОЙ, А НЕ КУЛЬТУРОЙ МАШИНЫ.
        //    В N42 число записано с ТОЧКОЙ по спецификации, а double.ToString()
        //    без культуры печатает разделителем то, что стоит у машины. Измерено
        //    05.09.2026: под ru-RU вывоз давал
        //    <LiveTimeDuration>PT356491,21293S</LiveTimeDuration>, и такой файл не
        //    читается ни нами, ни кем-либо ещё.
        //
        // ⛔ ЭТО ХУЖЕ ЧТЕНИЯ (`A142`). Там отказывал ВВОЗ, и человек это видел
        //    сразу; здесь порченый файл молча ложится на диск и обнаруживается у
        //    получателя, когда исходного спектра под рукой уже нет.
        //
        // ⚠ В окнах дефект не проявлялся только потому, что MainForm.cs:162-164
        //    при запуске глобально ставит разделителем точку: вывоз ФАЙЛА держался
        //    на настройке, сделанной в другом месте и по другому поводу. Всякий
        //    безоконный вызывающий этой подпорки не имеет.
        // ==================================================================

        /// <summary>
        /// Вещественное число в файл N42: инвариантная культура и формат "R".
        /// </summary>
        private static string N42Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Целое число отсчётов в файл N42.
        ///
        /// ⚠ У целого «G» разделителей разрядов не ставит ни в одной культуре, то
        /// есть отличие от прежнего <c>ToString()</c> тут только в знаке минуса —
        /// а отсчёты неотрицательны. Инвариантная культура ставится всё равно:
        /// правило «в файл пишем инвариантно» дороже поштучного разбора, у какого
        /// именно поля культура сегодня не видна.
        /// </summary>
        private static string N42Count(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Длительность в файл N42 — xs:duration вида <c>PT1234.5S</c>.
        ///
        /// ⛔ ФОРМАТ ЗДЕСЬ НЕ "R", И ЭТО ИЗМЕРЕНО, А НЕ ВЫБРАНО (05.09.2026).
        /// Две причины, каждая — отказ:
        ///
        /// 1. "R" ПЕЧАТАЕТ ПОКАЗАТЕЛЬ СТЕПЕНИ, а xs:duration его не принимает.
        ///    Замер: <c>(1e-7).ToString("R")</c> даёт «1E-07», и
        ///    <c>XmlConvert.ToTimeSpan("PT1E-07S")</c> бросает FormatException —
        ///    то есть «R» у длительностей завело бы ровно тот же дефект, ради
        ///    которого затевалась `A146`: нечитаемый файл на диске.
        /// 2. ЧИТАТЕЛЬ ЖИВЁТ НА СЕТКЕ 100 нс. TimeSpan хранит целые «тики» по
        ///    100 нс, и цифры мельче отбрасываются ОБРЕЗАНИЕМ, а не округлением:
        ///    55470.709087999996 доезжало обратно как 55470.7090879, то есть
        ///    потеря 1E-7 с. Семь знаков после запятой — это ровно сетка тика:
        ///    больше писать нечего, и округление до неё даёт остаток 7E-12 с
        ///    вместо 1E-7 с.
        ///
        /// ⚠ ЧЕСТНО: круг «вывоз → ввоз» сходится ТОЧНО только для значений,
        /// уже лежащих на сетке 100 нс. У корпусных времён в XML записаны все
        /// 17 значащих цифр double (55470.709087999996), и такое значение на
        /// сетку не ложится — остаётся 7E-12 с, предел самой сетки. Со ВТОРОГО
        /// круга значение уже на сетке и сходится точно.
        /// </summary>
        private static string N42Duration(double seconds)
        {
            return "PT" + seconds.ToString("0.#######", CultureInfo.InvariantCulture) + "S";
        }

        /// <summary>
        /// Длительность ИЗ файла N42 в секунды (`A147`).
        ///
        /// ⛔ ДЕЛЕНИЕ, А НЕ <c>TotalSeconds</c>, и это тоже замер. Свойство
        /// <c>TimeSpan.TotalSeconds</c> считает УМНОЖЕНИЕМ на 1E-7, а само 1E-7
        /// в double неточно — поэтому обратное чтение промахивалось на единицу
        /// младшего разряда даже там, где строка была короткой и на сетку
        /// ложилась: «PT3599.996S» давало 3599.9959999999996 вместо
        /// 3599.9960000000001. Деление целого числа тиков на 10000000.0
        /// округляется правильно и промаха не даёт. Измерено 05.09.2026 на 18
        /// значениях: умножение промахнулось у 8 из 18, деление — ни у одного.
        /// </summary>
        private static double N42Seconds(string duration)
        {
            return XmlConvert.ToTimeSpan(duration).Ticks / (double)TimeSpan.TicksPerSecond;
        }

        /// <summary>
        /// Дата и время начала набора в файл N42 — xs:dateTime.
        ///
        /// ⛔ Прежде здесь стояло <c>data.StartTime.ToString()</c>, и это было
        /// негодно во ВСЯКОЙ культуре, а не только в культуре с запятой. Измерено
        /// 05.09.2026 на одном и том же спектре: под ru-RU выходило
        /// «28.11.2022 10:07:57», под en-US «11/28/2022 10:07:57 AM». Ни то, ни
        /// другое не есть xs:dateTime, которого ждёт спецификация, и хуже того —
        /// эти две записи ЧИТАЮТСЯ ПО-РАЗНОМУ: «28.11.2022», прочитанное культурой
        /// с порядком месяц-день, либо даёт другую дату, либо не читается вовсе, а
        /// разбор в <c>ParseMeasurementStartDateTime</c> обёрнут в пустой
        /// <c>catch</c> — то есть время начала подменяется на «сейчас» МОЛЧА.
        ///
        /// <c>RoundtripKind</c> взят затем, что ровно им и читает
        /// <c>ParseMeasurementStartDateTime</c> ПЕРВОЙ попыткой: пишущий и
        /// читающий спрашивают об одном одинаково.
        /// </summary>
        private static string N42DateTime(DateTime value)
        {
            return XmlConvert.ToString(value, XmlDateTimeSerializationMode.RoundtripKind);
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

            // ⛔ `A152`: ЧИСЛО КАНАЛОВ БЕРЁТСЯ У ФАЙЛА, КАК У СОСЕДА.
            //    Прежде этой строки здесь не было: файл на 1024 канала ложился в
            //    документ, объявляющий 8192, каналы 1024…8191 оставались нулями, а
            //    длина шкалы у документа была чужая. Разбор 2012 года в этом же
            //    файле число каналов ФАЙЛА записывает — третьего соглашения быть
            //    не должно.
            //    ⛔ И это портило заведённую `A141` проверку: CheckCalibration
            //    судит по числу каналов ДОКУМЕНТА, а полином подгонялся по точкам
            //    ФАЙЛА, — то есть проверялась экстраполяция за край подгонки, а не
            //    то, что подгоняли.
            //    ⚠ Сверх того снимается падение: файл, у которого каналов БОЛЬШЕ,
            //    чем у документа, ронял разбор IndexOutOfRangeException в цикле
            //    ниже.
            //    ⚠ ChannelPitch НЕ трогается нарочно: строка про него не говорит, а
            //    у файлов, которые ввозились и раньше, это сдвинуло бы шаг шкалы.
            energySpectrum.NumberOfChannels = NumberOfChanels;
            energySpectrum.Initialize();

            for (int i = 0; i < NumberOfChanels; i++)
            {
                // `A158`: ОТСЧЁТЫ ЧИТАЮТСЯ ИНВАРИАНТНОЙ КУЛЬТУРОЙ — как и энергии
                //   строкой ниже. Разбор ЧИСЛА в файле не смеет зависеть от того,
                //   какая культура стоит у машины: у целого расходится знак минуса
                //   (NegativeSign), а у чтения вообще — правило одно на весь файл.
                int count = int.Parse(chanData[i], CultureInfo.InvariantCulture);
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
            // `A147`: тот же читатель длительности, что и у разбора 2012 года, —
            //   деление вместо TotalSeconds. Здесь дробная часть не терялась
            //   (приведения к int не было), но промах на единицу младшего разряда
            //   был тот же, и второго соглашения о чтении времени в одном файле
            //   быть не должно.
            double lifetime = N42Seconds(rad.Measurement.Spectrum.LiveTime);
            double realtime = N42Seconds(rad.Measurement.Spectrum.RealTime);
            string starttime = rad.Measurement.Spectrum.StartTime;
            N42_2006_InstrumentInformation instrument = rad.Measurement.InstrumentInformation;

            string spectrumtype = rad.Measurement.Spectrum.Type;

            ResultData resultData = doc.ActiveResultData;
            long totalpulsecount = 0;
            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            energySpectrum.Initialize();

            for (int i = 0; i < NumberOfChanels; i++)
            {
                // `A158`: инвариантная культура — то же правило, что у коэффициентов
                //   этого же разбора ниже (`A142`) и у отсчётов разбора 2012 года.
                int count = int.Parse(chanData[i], CultureInfo.InvariantCulture);
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
            // `A156`: время начала кладётся в ОБА поля — то же соглашение, что у
            //   разбора 2012 года ниже и у соседнего ввоза GBS в DocumentManager.
            //   Прежде здесь заполнялся только SampleInfo.Time, а ResultData.StartTime
            //   оставался «сейчас» — то есть вывоз этого же документа записал бы в
            //   файл текущее время вместо прочитанного.
            resultData.SampleInfo.Time = XmlConvert.ToDateTime(starttime, XmlDateTimeSerializationMode.Utc);
            resultData.StartTime = resultData.SampleInfo.Time;
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
            //
            // ⛔ `A151`: ТЕКСТ ИЗ РЕСУРСА, А НЕ АНГЛИЙСКИЙ ЛИТЕРАЛ. После `A136`
            //    оконная дверь берёт причину из сообщения исключения, а причины
            //    `A140` написаны по-русски — то есть в ОДНОМ окне человек читал то
            //    русскую причину, то английскую, смотря какая беда с файлом. Ключ
            //    заводится в обе культуры, как у `A135`.
            if (PolynomialOrder > 4)
            {
                throw new Exception(string.Format(Resources.ERRUnsupportedPolynomialOrderN42, PolynomialOrder));
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

            // Сколько спектров УЖЕ положено в список. Это не то же самое, что
            // номер измерения i (`A150`): фоновое измерение своей строки в списке
            // не занимает, оно цепляется к предыдущему спектру.
            int added = 0;

            // `A160`: какие классы измерений пропущены — говорится ОДИН раз на файл.
            // `A157`: у скольких измерений не прочиталось время начала — тоже.
            List<string> skippedClasses = new List<string>();
            int badStart = 0;
            string badStartSample = null;

            for (int i = 0; i < SpectrumCount; i++)
            {
                RadMeasurement radMeasurement = rad.RadMeasurement[i];

                // ⛔ `A160`: КЛАСС ИЗМЕРЕНИЯ СУДИТСЯ ТАК ЖЕ, КАК У СОСЕДА.
                //    DocumentManager.ImportDocumentSpecUtils источники вида
                //    Calibration и IntrinsicActivity ПРОПУСКАЕТ (`continue`), а этот
                //    разбор делал из них равноправные спектры списка: два соглашения
                //    об одном положении в одном приложении — тот же разряд, что
                //    `A137` и `A141`. Соглашение взято У СОСЕДА, своего не заводится.
                //    ⚠ Всё, что не Background и не в этом списке (Foreground,
                //    NotSpecified, пустое поле, чужое значение), остаётся передним
                //    спектром — у соседа «Unknown» тоже идёт в передний.
                //    ⚠ Пропуск — НЕ молчаливый: разбор, отбрасывающий часть файла
                //    без единого слова, есть отказ без читателя, а список ввозимого
                //    здесь меняется НАРОЧНО.
                if (radMeasurement.MeasurementClassCode == "Calibration"
                    || radMeasurement.MeasurementClassCode == "IntrinsicActivity")
                {
                    if (!skippedClasses.Contains(radMeasurement.MeasurementClassCode))
                    {
                        skippedClasses.Add(radMeasurement.MeasurementClassCode);
                    }
                    continue;
                }
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

                // ⛔ `A156`: ВРЕМЯ НАЧАЛА КЛАДЁТСЯ В ТО ЖЕ ПОЛЕ, ИЗ КОТОРОГО ЕГО
                //    БЕРЁТ ВЫВОЗ. Прежде ввоз клал прочитанное ТОЛЬКО в
                //    SampleInfo.Time, а ExportToN42 пишет в файл ResultData.StartTime,
                //    у ввезённого документа так и остававшееся «сейчас»
                //    (умолчание ResultData.cs). Круг «вывоз → ввоз → вывоз» терял
                //    дату на ВТОРОМ обороте независимо от того, что починила `A146`.
                //    Соглашение взято У СОСЕДА ЦЕЛИКОМ: DocumentManager при ввозе
                //    формата GBS пишет прочитанное И в SampleInfo.Time, И в
                //    StartTime одним движением (`info.Time = …; StartTime = info.Time`),
                //    а ввоз через SpecUtils заполняет StartTime. Здесь заполняются
                //    оба поля: у них разный смысл (время ПРОБЫ и время НАБОРА), но
                //    файл N42 несёт одно StartDateTime, и терять его нельзя ни в том,
                //    ни в другом.
                //
                // ⛔ `A157`: ОТКАЗ РАЗБОРА ДАТЫ БОЛЬШЕ НЕ СЪЕДАЕТСЯ. Прежде здесь
                //    стоял пустой `catch { }`: время начала молча подменялось на
                //    «сейчас». Измерено 05.09.2026 перекрёстной культурой — файл,
                //    выгруженный старой сборкой под ar-SA (год по хиджре), ввозился
                //    под en-US без единого слова и с чужой датой.
                //
                //    ⚠ ВВОЗИМ, СКАЗАВ ВСЛУХ, а не отказываем — и это выбор с ценой,
                //    а не осторожность. Соседние ветви ЭТОГО файла (`A137`, `A141`,
                //    CheckCalibration) без окон бросают, потому что там негодна
                //    ЭНЕРГЕТИЧЕСКАЯ ШКАЛА: по ней считают всё, и молчаливый счёт по
                //    ней даёт правдоподобные чужие числа. Время начала в счёт не
                //    входит ни одним числом — оно паспортное. А цена отказа
                //    измерима: нечитаемую дату несут файлы, которые BecqMoni сам
                //    выгружал ДО `A146` (под ru-RU дата «28.11.2022 10:07:57»
                //    культурой с порядком месяц-день не читается вовсе), — отказ
                //    сделал бы собственные старые файлы приложения неввозимыми.
                //    Дверь та же, что у соседей, — AppUi; второго соглашения не
                //    заводится, меняется только строгость.
                DateTime startTime = DateTime.Now;
                if (!string.IsNullOrEmpty(radMeasurement.StartDateTime))
                {
                    try
                    {
                        startTime = ParseMeasurementStartDateTime(radMeasurement.StartDateTime);
                    }
                    catch (Exception ex)
                    {
                        badStart++;
                        if (badStartSample == null)
                        {
                            badStartSample = radMeasurement.StartDateTime + " — "
                                             + ex.GetType().Name;
                        }
                    }
                }
                resultData.SampleInfo.Time = startTime;
                resultData.StartTime = startTime;

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
                // ⛔ `A147`: ВРЕМЯ ХРАНИТСЯ ДРОБНЫМ, А НЕ ОБРЕЗАЕТСЯ ДО СЕКУНД.
                //    Прежде здесь стояло (int)…TotalSeconds, то есть 356 491.213
                //    превращалось в 356 491, а 3602.675 — в 3602. Измерено
                //    05.09.2026 на 9 файлах из 12 в дереве.
                //    ⚠ Это НЕ косметика: живое время стоит в знаменателе скорости
                //    счёта, и потеря доезжает до чисел на экране — до 1 с потери,
                //    то есть 0.03 % на часовом наборе и до 100 % на секундном.
                //    Оба соседних разбора N42 в этом же файле хранят время дробным
                //    (2006 — double lifetime/realtime, RadiologicalInstrumentData —
                //    double LiveTime), третьего соглашения тут быть не должно.
                //    ⚠ PresetTime остаётся целым: у ResultDataStatus это поле int
                //    по смыслу (заданная человеком уставка в секундах), и менять
                //    его вид — не эта работа.
                double LiveTime = 0.0;
                double ElapsedTime = 0.0;
                if (radMeasurement.Spectrum[0].LiveTimeDuration != null && radMeasurement.Spectrum[0].LiveTimeDuration != "")
                {
                    LiveTime = N42Seconds(radMeasurement.Spectrum[0].LiveTimeDuration);
                }
                if (radMeasurement.RealTimeDuration != null && radMeasurement.RealTimeDuration != "")
                {
                    ElapsedTime = N42Seconds(radMeasurement.RealTimeDuration);
                }
                resultData.EnergySpectrum.MeasurementTime = ElapsedTime;
                resultData.EnergySpectrum.LiveTime = LiveTime;
                resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(ElapsedTime);
                resultData.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(ElapsedTime);
                resultData.ResultDataStatus.PresetTime = (int)ElapsedTime;

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
                    // `A151`: тот же ключ, что и у разбора 2006 года выше, — беда
                    // одна, и текст у неё обязан быть один.
                    if (PolynomialOrder > 4)
                    {
                        throw new Exception(string.Format(Resources.ERRUnsupportedPolynomialOrderN42, PolynomialOrder));
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
                // ⛔ `A150`: ФОН ВОЗВРАЩАЕТСЯ ФОНОМ, А НЕ ВТОРЫМ ОБЫЧНЫМ СПЕКТРОМ.
                //
                //    Признак фона в N42 ЕСТЬ и он законный: MeasurementClassCode
                //    со значением Background — и наш собственный вывоз его пишет
                //    (см. ExportToN42 выше). Прежде разбор это поле не читал вовсе,
                //    и всякий RadMeasurement становился равноправным спектром
                //    списка: круг «вывоз → ввоз» превращал «спектр + фон» в «два
                //    спектра» у 11 файлов из 12, и человеку об этом не говорилось.
                //    Поэтому правильная правка здесь — ПЕРЕНОСИТЬ признак, а не
                //    объявлять его невыразимым: невыразимым он не был.
                //
                //    Соглашение взято У СОСЕДА ЦЕЛИКОМ, а не заведено своё:
                //    DocumentManager.ImportDocumentSpecUtils на источнике вида
                //    «Background» кладёт спектр в ResultData.BackgroundEnergySpectrum
                //    и подписывает его BackgroundSpectrumFile тем же образом. Модель
                //    приложения такой признак носит; выдумывать поле не требуется.
                //
                //    ⚠ Фон, которому не к чему прицепиться (файл начинается с
                //    Background или у хозяина фон уже есть), кладётся ОБЫЧНЫМ
                //    спектром — так же, как у соседа. Иначе файл из одних только
                //    фоновых измерений ввозился бы пустым.
                bool isBackground = radMeasurement.MeasurementClassCode == "Background";
                ResultData host = added > 0 ? doc.ResultDataFile.ResultDataList[added - 1] : null;
                if (isBackground && host != null && host.BackgroundEnergySpectrum == null)
                {
                    host.BackgroundEnergySpectrum = resultData.EnergySpectrum;
                    host.BackgroundSpectrumFile = "BackgroundEnergySpectrum" + " (" + (added - 1) + ")";
                }
                else
                {
                    if (added == 0)
                    {
                        doc.ResultDataFile.ResultDataList[0] = resultData;
                    } else
                    {
                        doc.ResultDataFile.ResultDataList.Add(resultData);
                    }
                    added++;
                }
            }

            // ⛔ ГОЛОСА ЗВУЧАТ ОДИН РАЗ НА ФАЙЛ, А НЕ ПО ЧИСЛУ ИЗМЕРЕНИЙ.
            //    Соглашение то же, что у проверки «нет калибровки» выше (`A137`):
            //    свойство ВСЕГО файла говорится один раз. Модальное окно на каждое
            //    измерение файла из десяти спектров было бы наказанием, а не
            //    сообщением.
            if (skippedClasses.Count > 0)
            {
                // `A160`: что именно выброшено из файла и сколько осталось.
                AppUi.Report(string.Format(Resources.ERRSkippedMeasurementClassN42,
                                           string.Join(", ", skippedClasses.ToArray()),
                                           added),
                             "", MessageBoxIcon.None);
            }
            if (badStart > 0)
            {
                // `A157`: дата не прочитана — спектр ввезён, но с чужим временем.
                AppUi.Report(string.Format(Resources.ERRUnreadableStartDateTimeN42,
                                           badStart, badStartSample),
                             "", MessageBoxIcon.None);
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
