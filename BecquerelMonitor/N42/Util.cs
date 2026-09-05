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
                string calibrationId = "SpectrumCalibration-" + i.ToString(CultureInfo.InvariantCulture);
                string idMeasurement = "SpectrumMeasurement-" + i.ToString(CultureInfo.InvariantCulture);
                string measurementClassCode = "Foreground";
                string idSpectrum = "SpectrumData";
                string idGrossCounts = "GrossForeground";
                rad.EnergyCalibration[j] = AddCalibration(doc.ResultDataFile.ResultDataList[i].EnergySpectrum, calibrationId);
                rad.RadMeasurement[j] = AddMeasurement(doc.ResultDataFile.ResultDataList[i], idMeasurement, measurementClassCode, idSpectrum, calibrationId, idGrossCounts);
                j++;

                if (doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum != null)
                {
                    calibrationId = "BackgroundCalibration-" + i.ToString(CultureInfo.InvariantCulture);
                    idMeasurement = "BackgroundMeasurement-" + i.ToString(CultureInfo.InvariantCulture);
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
        /// Длительность из файла, записанная ЛИБО как xs:duration («PT295S»),
        /// ЛИБО голыми секундами («295», «295.5») — извод Alpha Hound (05.09.2026).
        ///
        /// ⛔ ДВЕ ЗАПИСИ ПРИНИМАЮТСЯ НЕ ИЗ ВЕЖЛИВОСТИ. В том же элементе
        /// Spectrum этот прибор пишет LiveTime ГОЛЫМ ЧИСЛОМ (модель читает его
        /// как <c>double</c> и годами читала успешно), а спецификация N42-2006
        /// для обоих времён требует xs:duration. Читатель, знающий одну запись,
        /// на половине настоящих файлов отказал бы — и отказал бы разбором
        /// ВСЕГО документа, а не одного поля.
        ///
        /// ⛔ НЕЧИТАЕМАЯ ЗАПИСЬ — ОТКАЗ, НАЗЫВАЮЩИЙ СЕБЯ, а не молчаливая
        /// подстановка. Оба соседних разбора этого файла на негодной
        /// длительности отказывают (<c>N42Seconds</c> бросает), и третьего
        /// соглашения тут быть не должно; отличие лишь в том, что причина
        /// названа словами (`A140`), а не оставлена именем исключения.
        /// </summary>
        private static double N42SecondsLoose(string value, string what, string filename)
        {
            string s = value.Trim();
            try
            {
                // xs:duration всегда начинается с 'P' (или '-P' у отрицательной).
                if (s.StartsWith("P", StringComparison.Ordinal)
                    || s.StartsWith("-P", StringComparison.Ordinal))
                {
                    return N42Seconds(s);
                }
                return double.Parse(s, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "в файле N42 (" + AppUi.Where(filename) + ") запись " + what
                    + " «" + value + "» не читается ни как xs:duration (вида PT295S), "
                    + "ни как число секунд (вида 295): " + ex.GetType().Name);
            }
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

            // ⛔ ОБЪЯВЛЕННОЕ ЧИСЛО КАНАЛОВ СВЕРЯЕТСЯ С ЗАПИСАННЫМ, И СВЕРЯЕТСЯ
            //    ДО ПЕРВОЙ ЗАПИСИ В ДОКУМЕНТ (05.09.2026).
            //
            //    Длину массива этот разбор берёт у АТРИБУТА NumberOfChannels, а
            //    отсчёты и энергии читает ПО ИНДЕКСУ из двух отдельных списков.
            //    Файл, объявивший 9000 каналов и записавший 64, выходил за конец
            //    обоих: человек читал «Index was outside the bounds of the array»
            //    и не мог узнать ни что не так с файлом, ни какой файл виноват.
            //    Тот же разряд, что `A140`: второй двери здесь НЕ заводится —
            //    заводится ПРИЧИНА для той, что уже стоит в
            //    DocumentManager.ImportDocumentN42.
            //
            //    ⛔ МЕСТО ПРОВЕРКИ — ЧАСТЬ ПОЧИНКИ, А НЕ ВКУС. Ниже идут
            //    energySpectrum.NumberOfChannels = … и Initialize(), то есть
            //    ПЕРЕЗАПИСЬ открытого документа. Пока проверка стояла после них,
            //    отказавший ввоз оставлял в документе числа отказавшего файла:
            //    измерено 05.09.2026 входом case23_rad_declared — «кан 9000,
            //    сумма 253» ПОСЛЕ отказа. Снаружи такой документ неотличим от
            //    удачного ввоза.
            //
            //    ⚠ Сверяется ТОЛЬКО НЕДОСТАЧА (записано МЕНЬШЕ объявленного).
            //    Обратный случай — записано больше объявленного — сегодня
            //    молча отбрасывает хвост; отказывать на нём значило бы закрыть
            //    файлы, которые сейчас ввозятся, а такого замера у нас нет.
            //    Остаток вынесен отдельной строкой реестра.
            if (NumberOfChanels < 0
                || chanData.Length < NumberOfChanels
                || chanEnergy.Length < NumberOfChanels)
            {
                throw new Exception(
                    "в файле N42 (" + AppUi.Where(filename) + ") объявлено каналов "
                    + NumberOfChanels.ToString(CultureInfo.InvariantCulture) + " (атрибут ChannelData/@NumberOfChannels), а записано "
                    + "отсчётов " + chanData.Length.ToString(CultureInfo.InvariantCulture) + " и энергий " + chanEnergy.Length.ToString(CultureInfo.InvariantCulture)
                    + " — читать нечего, и достраивать недостающие каналы нулями нельзя: "
                    + "это был бы выдуманный спектр");
            }

            // ⛔ ЖИВОЕ ВРЕМЯ — В ПОЛЕ ЖИВОГО, ПОЛНОЕ — В ПОЛЕ ПОЛНОГО (05.09.2026).
            //
            //    Прежде здесь была ОДНА величина: Spectrum.LiveTime файла
            //    записывалась в energySpectrum.MeasurementTime, то есть в поле
            //    ПОЛНОГО времени, а energySpectrum.LiveTime не заполнялась вовсе
            //    и оставалась нулём. Измерено 05.09.2026 входами
            //    case8_rad_many и case9_rad_over: слепок давал «изм 295, живое 0».
            //    Оба соседних разбора этого же файла (2006 и 2012 годов) держат
            //    две величины ОТДЕЛЬНО — третьего соглашения тут быть не должно.
            //
            //    ⚠ ЦЕНА ИЗМЕРИМА И УЕЗЖАЕТ ИЗ ПРИЛОЖЕНИЯ. Живое время стоит в
            //    знаменателе скорости счёта; нулём оно попадало в поле «Live
            //    time» окна (DCControlPanel.cs:366 — «0.00»), складывалось нулём
            //    при сложении спектров (SpectrumAriphmetics.cs:303) и, что хуже
            //    всего, УХОДИЛО В ФАЙЛ: ExportToN42 пишет
            //    LiveTimeDuration = N42Duration(LiveTime), то есть ввезённый
            //    Alpha Hound выгружался обратно с «PT0S». Получатель такого
            //    файла восстановить живое время уже ниоткуда не может.
            //
            //    ⚠ ЧЕСТНО: ПОЛНОГО ВРЕМЕНИ В ЭТОМ ИЗВОДЕ ЧАЩЕ ВСЕГО НЕТ. Если
            //    элемента RealTime в файле нет, полное время приравнивается
            //    живому — ровно то число, что стояло в этом поле и до правки,
            //    то есть НИ ОДНО сегодняшнее число не сдвигается. Выдумать
            //    мёртвое время не из чего, а обнулить полное нельзя: на него
            //    делит весь показ спектра (EnergySpectrumView), доза
            //    (DoseRateManager) и нормировка фона.
            double lifetime = rad.MeasurementGroup.Measurement.Spectrum.LiveTime;
            double realtime = lifetime;
            string realTimeText = rad.MeasurementGroup.Measurement.Spectrum.RealTime;
            if (realTimeText != null && realTimeText.Trim().Length > 0)
            {
                realtime = N42SecondsLoose(realTimeText, "RealTime", filename);
            }
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

            // См. пояснение выше: полное — в поле полного, живое — в поле живого.
            energySpectrum.MeasurementTime = realtime;
            energySpectrum.LiveTime = lifetime;
            energySpectrum.TotalPulseCount = totalpulsecount;
            energySpectrum.ValidPulseCount = totalpulsecount;
            ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;
            resultDataStatus.TotalTime = TimeSpan.FromSeconds(energySpectrum.MeasurementTime);
            resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(energySpectrum.MeasurementTime);
            // ⛔ `A207`: ВРЕМЕНИ НАЧАЛА У ЭТОГО ФОРМАТА НЕТ ВОВСЕ, И ТЕПЕРЬ ЭТО
            //    ВИДНО. Модель RadiologicalInstrumentData такого элемента не
            //    несёт; до 06.09.2026 сюда молча ложилось «сейчас» — то есть
            //    спектр, набранный два года назад, получал СЕГОДНЯШНЮЮ дату,
            //    неотличимую от измеренной, и уезжал с ней в отчёт и в вывоз.
            //    Теперь ставится единое значение «время начала неизвестно»
            //    (ResultData.UnknownStartTime), и об этом говорится один раз на
            //    файл — решение Amber 06.09.2026.
            resultData.StartTime = ResultData.UnknownStartTime;
            resultData.SampleInfo.Time = ResultData.UnknownStartTime;
            // ⚠ `A177`: КОНЕЦ НАБОРА — то же правило, что у двух других разборов
            //    и у соседей в DocumentManager: начало плюс полное время набора.
            //    Начало неизвестно, поэтому и конец неизвестен вместе с ним, но
            //    ДЛИТЕЛЬНОСТЬ в паре полей сохраняется — она из файла честная.
            resultData.EndTime = resultData.StartTime.AddSeconds(energySpectrum.MeasurementTime);
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
                            + ", порядок " + calibration.PolynomialOrder.ToString(CultureInfo.InvariantCulture)
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

            // ⛔ `A207`: ГОЛОС ОДИН РАЗ НА ФАЙЛ И В КОНЦЕ — как у двух соседних
            //    разборов. Файл этого формата несёт ровно один спектр, поэтому
            //    «один раз на файл» здесь и есть «один раз»; счётчик всё равно
            //    печатается числом, чтобы текст был тем же самым у всех дверей.
            //    ⚠ Голос стоит ПОСЛЕ отказов шкалы нарочно: если файл не доехал
            //    сюда, человеку названа та беда, из-за которой он не доехал.
            AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRMissingStartDateTime, 1),
                         "", MessageBoxIcon.None);
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

            // ⛔ ШКАЛА РАЗБИРАЕТСЯ ДО ТОГО, КАК ДОКУМЕНТ ПЕРЕЗАПИСАН (05.09.2026).
            //
            //    Ниже идут energySpectrum.NumberOfChannels = … , Initialize() и
            //    цикл записи отсчётов — то есть ПЕРЕЗАПИСЬ открытого документа.
            //    Все три проверки шкалы (пустой список `A140`, порядок больше
            //    четырёх `A151`, нечисло в коэффициентах `A142`) стояли ПОСЛЕ
            //    неё, и отказ оставлял в документе весь спектр отказавшего файла:
            //    измерено 05.09.2026 входом case29_2006_nocoeff — после ОТКАЗА
            //    слепок показывал «кан 64, сумма 253, изм 300, живое 295, шкала
            //    порядок 1 [0, 1]». Снаружи это неотличимо от удачного ввоза, а
            //    шкала при этом y = x, то есть номер канала объявлен энергией.
            //
            //    ⚠ Ни одна из трёх проверок не смотрит на число каналов — им
            //    нужен только разобранный список коэффициентов, — поэтому
            //    перенос ничего не меняет ни в одном исходе, кроме состояния
            //    документа после отказа. CheckCalibration остаётся ниже: ей
            //    число каналов НУЖНО (`A141`, `A172`).
            //
            //    ⚠ ЧЕСТНО: сдвинуть за эту черту удалось не всё. Отказ
            //    CheckCalibration и отказ разбора даты по-прежнему случаются
            //    после перезаписи, и остаток вынесен отдельной строкой реестра.
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
                throw new Exception(string.Format(CultureInfo.InvariantCulture, Resources.ERRUnsupportedPolynomialOrderN42, PolynomialOrder));
            }

            double[] coefficients = new double[PolynomialOrder + 1];

            for (int k = 0; k < coefficients.Length; k++)
            {
                // `A142`: см. пояснение у разбора 2012 года ниже.
                coefficients[k] = double.Parse(enCalibrationCoeff[k], CultureInfo.InvariantCulture);
            }

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
            // ⛔ `A172`: ЧИСЛО КАНАЛОВ ФАЙЛА ПИШЕТСЯ В СПЕКТР — ТА ЖЕ ПРАВКА, ЧТО
            //    `A152` СДЕЛАЛА У РАЗБОРА RadiologicalInstrumentData. Здесь её не
            //    было, и разборов с ТРЕТЬИМ соглашением о числе каналов
            //    оставалось два из трёх.
            //    ⛔ Цена измерена 05.09.2026 двумя видами беды сразу:
            //    файл на 9000 и на 16384 канала РОНЯЛ ввоз
            //    IndexOutOfRangeException (цикл ниже пишет по индексу в массив
            //    документа, а у пустого DocEnergySpectrum он на 8192), а файл
            //    на 64 и на 1024 канала не падал вовсе и ложился в документ,
            //    объявляющий 8192, — каналы файла кончались, остаток оставался
            //    нулями МОЛЧА. Вторая половина хуже первой: падение видно, а
            //    лишние семь тысяч нулевых каналов выглядят как спектр.
            //    ⚠ И это же чинит происхождение шкалы: CheckCalibration ниже
            //    зовётся с energySpectrum.NumberOfChannels, то есть до правки
            //    проверяла полином на 8192 каналах у файла, где их 64, —
            //    судила экстраполяцию, а не то, что прочитано.
            //    ⚠ ChannelPitch не трогается: у разбора 2012 года он ставится
            //    в 1, здесь исторически не ставится вовсе, и менять шаг шкалы
            //    у файлов, которые ввозились и раньше, эта строка не просит.
            energySpectrum.NumberOfChannels = NumberOfChanels;
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
            //
            // ⛔ `A171`: ОТКАЗ РАЗБОРА ДАТЫ БОЛЬШЕ НЕ ВАЛИТ ВЕСЬ ВВОЗ. Прежде здесь
            //    стоял голый XmlConvert.ToDateTime, и негодная запись времени
            //    уносила файл ЦЕЛИКОМ: измерено 05.09.2026 входом
            //    case19_2006_baddate — ОТКАЗ FormatException, тогда как тот же
            //    файл в записи 2012 года ввозился и говорил вслух (`A157`). Два
            //    соглашения об одном положении в одном файле — тот же разряд, что
            //    `A137` и `A141`. Соглашение взято У РАЗБОРА 2012 ГОДА ЦЕЛИКОМ:
            //    та же попытка чтения (ParseMeasurementStartDateTime), тот же
            //    счётчик, тот же ключ ресурса, тот же голос один раз на файл.
            //
            //    ⚠ Довод «ввозим, сказав вслух, а не отказываем» тот же, что у
            //    `A157`, и здесь он ЕЩЁ прямее: время начала не входит ни в одно
            //    вычисление, а нечитаемую дату несут файлы, которые BecqMoni сам
            //    выгружал до `A146`.
            //
            //    ⚠ Читатель сменился с XmlDateTimeSerializationMode.Utc на
            //    ParseMeasurementStartDateTime (RoundtripKind + запасные попытки).
            //    Для записи без часового пояса и для записи с «Z» это ОДНО И ТО ЖЕ
            //    значение — проверено замером на входе case18_n42_2006; расходятся
            //    они только на записи со смещением вида «+03:00», где Utc приводил
            //    к UTC, а RoundtripKind оставляет местное. Второго соглашения о
            //    чтении даты в одном файле быть не должно, и выбрано то, которым
            //    читают два других разбора и пишет наш собственный вывоз.
            //
            // ⛔ `A207`: НЕИЗВЕСТНОЕ ВРЕМЯ НАЧАЛА — ОДНО ЗНАЧЕНИЕ НА ВСЁ
            //    ПРИЛОЖЕНИЕ, И ОБ ЭТОМ ГОВОРИТСЯ. Здесь стояло «сейчас» и для
            //    отсутствующей записи (молча), и для нечитаемой; вторая дверь
            //    того же файла (DocumentManager.ImportDocumentSpecUtils) для
            //    того же положения ставила 1970-01-01. Оба случая кончаются
            //    одним и тем же — времени начала у документа НЕТ, — и значение
            //    теперь одно: ResultData.UnknownStartTime.
            DateTime startTime = ResultData.UnknownStartTime;
            int badStart = 0;
            int noStart = 0;
            string badStartSample = null;
            if (!string.IsNullOrEmpty(starttime))
            {
                try
                {
                    startTime = ParseMeasurementStartDateTime(starttime);
                }
                catch (Exception ex)
                {
                    badStart++;
                    badStartSample = starttime + " — " + ex.GetType().Name;
                }
            }
            else
            {
                noStart++;
            }
            resultData.SampleInfo.Time = startTime;
            resultData.StartTime = startTime;
            // ⚠ `A177`: КОНЕЦ НАБОРА — ТО ЖЕ ПРАВИЛО, ЧТО У СОСЕДЕЙ.
            //    DocumentManager при ввозе GBS и через SpecUtils ставит
            //    EndTime = StartTime + полное время набора; ввоз N42 не ставил его
            //    ВООБЩЕ, и поле оставалось умолчанием ResultData, то есть
            //    «сейчас». Берётся полное время (realtime), а не живое: EndTime —
            //    момент по часам, а не сумма зачтённого времени.
            resultData.EndTime = startTime.AddSeconds(realtime);
            resultData.SampleInfo.Note = $"InstrumentType = {instrument.InstrumentType}, " +
                $"Manufacturer = {instrument.Manufacturer}, " +
                $"InstrumentModel = {instrument.InstrumentModel}, " +
                $"InstrumentID = {instrument.InstrumentID}, " +
                $"ProbeType = {instrument.ProbeType}";



            // Порядок полинома и коэффициенты разобраны ВЫШЕ, до перезаписи
            // документа (см. пояснение там); здесь они только ставятся на место.
            resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
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
                        + " (N42, " + AppUi.Where(filename) + ", порядок " + PolynomialOrder.ToString(CultureInfo.InvariantCulture)
                        + "). Дальше по этому спектру считать нельзя: энергетическая шкала негодна.");
                }
                AppUi.Report(Resources.CalibrationFunctionError, "", MessageBoxIcon.None);
            }

            // `A171`: дата не прочитана — спектр ввезён, но с чужим временем.
            //   Голос стоит В КОНЦЕ, как у разбора 2012 года: если файл не
            //   доехал до этой точки, человеку названа та беда, из-за которой
            //   он не доехал, а не эта.
            if (badStart > 0)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRUnreadableStartDateTimeN42,
                                           badStart, badStartSample),
                             "", MessageBoxIcon.None);
            }
            // `A207`: записи времени начала в файле НЕТ — тот же голос, что у
            //   двух других дверей, и тоже один раз на файл.
            if (noStart > 0)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRMissingStartDateTime, noStart),
                             "", MessageBoxIcon.None);
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
            // `A207`: у скольких измерений записи времени начала НЕТ вовсе —
            //   счётчик отдельный от badStart: положение то же (времени нет), а
            //   сказать о нём надо иначе, чем о нечитаемой записи.
            int noStart = 0;
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
                //
                // ⛔ `A207`: ЗАПИСИ ВРЕМЕНИ НЕТ ЛИБО ОНА ПУСТА — ТО ЖЕ САМОЕ
                //    ПОЛОЖЕНИЕ, И ЗНАЧЕНИЕ У НЕГО ОДНО. Прежде здесь стояло
                //    «сейчас» и об этом не говорилось ни слова: измерено
                //    05.09.2026 входом case24_nostart — ВВЕЗЁН, начало «сейчас»,
                //    молчание. Теперь ставится ResultData.UnknownStartTime, а
                //    голос звучит один раз на файл, ниже по тексту.
                DateTime startTime = ResultData.UnknownStartTime;
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
                else
                {
                    // `A207`: считается ЗДЕСЬ, а говорится один раз на файл — как
                    //   и всё прочее, что есть свойство файла, а не измерения.
                    noStart++;
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
                // ⚠ `A177`: КОНЕЦ НАБОРА, соглашение соседей целиком —
                //    EndTime = StartTime + ПОЛНОЕ время набора (см. пояснение у
                //    разбора 2006 года выше). Стоит после ElapsedTime нарочно:
                //    полное время читается двумя строками выше, и поставить
                //    EndTime раньше значило бы прибавить ноль.
                resultData.EndTime = resultData.StartTime.AddSeconds(ElapsedTime);

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
                        throw new Exception(string.Format(CultureInfo.InvariantCulture, Resources.ERRUnsupportedPolynomialOrderN42, PolynomialOrder));
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
                                + " (N42-2012, " + AppUi.Where(filename) + ", порядок " + PolynomialOrder.ToString(CultureInfo.InvariantCulture)
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
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRSkippedMeasurementClassN42,
                                           string.Join(", ", skippedClasses.ToArray()),
                                           added),
                             "", MessageBoxIcon.None);
            }
            if (badStart > 0)
            {
                // `A157`: дата не прочитана — спектр ввезён, но с чужим временем.
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRUnreadableStartDateTimeN42,
                                           badStart, badStartSample),
                             "", MessageBoxIcon.None);
            }
            if (noStart > 0)
            {
                // `A207`: записи времени начала в файле нет — один голос на файл,
                //   сколько бы измерений без неё ни было.
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRMissingStartDateTime, noStart),
                             "", MessageBoxIcon.None);
            }

            // ⛔ `A176`: ПУСТОЙ ДОКУМЕНТ НЕ ВЫДАЁТСЯ ЗА ВВЕЗЁННЫЙ СПЕКТР.
            //
            //    Файл из одних калибровочных измерений после `A160` отдавал
            //    документ, в котором НИЧЕГО не прочитано: 8192 канала умолчания,
            //    сумма 0, шкала от прежнего состояния, — и дальше по нему
            //    считали. Измерено 05.09.2026 входом case12_calibonly.
            //    Тот же разряд, что `A137`: молчаливая подстановка негодных
            //    данных вместо отказа.
            //
            //    ⛔ ВТОРОЙ ДВЕРИ ЗДЕСЬ НЕ ЗАВОДИТСЯ — заводится ПРИЧИНА для той,
            //    что уже стоит, ровно как у `A140`: бросок отсюда ловит
            //    DocumentManager.ImportDocumentN42, и он же без окон отказывает
            //    кодом возврата, а в окнах называет причину человеку. Свой
            //    AppUi.Report тут был бы ТРЕТЬИМ соглашением об одном положении.
            //
            //    ⚠ Голос `A160` («Спектров ввезено: 0») звучит ПЕРЕД этим и
            //    остаётся: он говорит, ЧТО выброшено, а бросок — что показывать
            //    после этого нечего.
            if (added == 0)
            {
                throw new Exception(
                    "в файле N42 не осталось ни одного ввозимого спектра"
                    + (skippedClasses.Count > 0
                       ? ": все измерения файла имеют класс "
                         + string.Join(", ", skippedClasses.ToArray())
                         + ", а приложение ввозит только передние и фоновые"
                       : ": ввозимых измерений в файле нет")
                    + " — документ остался бы пустым, и считать по нему нечего");
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
