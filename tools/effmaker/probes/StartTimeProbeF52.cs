using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StartTimeProbeF52
{
    /// <summary>
    /// ОТСУТСТВУЮЩЕЕ ВРЕМЯ НАЧАЛА НАБОРА — `A207`, полоса F52 (06.09.2026).
    ///
    /// Мерится ТРИ вещи, и все три числом:
    ///
    ///   1. ГОЛОС ЗВУЧИТ ОДИН РАЗ НА ФАЙЛ. Вход `f52_multi_nostart` несёт ТРИ
    ///      измерения без записи времени; голосов про отсутствующее время
    ///      обязан быть ОДИН, а не три. Без файла со многими спектрами это
    ///      требование не проверяется вовсе — на одном спектре «один раз на
    ///      файл» и «один раз на измерение» неразличимы.
    ///
    ///   2. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ В ОБЕ СТОРОНЫ. Тот же файл, но с записями
    ///      времени (`f52_multi_withstart`), обязан дать НОЛЬ таких голосов и
    ///      настоящие даты; иначе «голос появился» значило бы лишь «дверь
    ///      кричит всегда». Третий вход (`f52_multi_mixed`) — два измерения без
    ///      времени и одно с ним: голос один, счётчик в тексте 2, а дата
    ///      третьего измерения настоящая.
    ///
    ///   3. ОБЕ ДВЕРИ СТАВЯТ ОДНО ЗНАЧЕНИЕ. Каждый файл ввозится и
    ///      `DocumentManager.ImportDocumentN42`, и
    ///      `DocumentManager.ImportDocumentSpecUtils`; печатаются фактические
    ///      `StartTime`, `SampleInfo.Time`, `EndTime` С МИЛЛИСЕКУНДАМИ — иначе
    ///      прежние 1970-01-01 00:00:03.600 и новое 00:00:00.000 в печати
    ///      слиплись бы. Приговор «СОШЛОСЬ/РАЗОШЛОСЬ» считается пробой, а не
    ///      глазами.
    ///
    /// ⛔ ЗНАЧЕНИЕ ИЗ РЕСУРСА БЕРЁТСЯ ЧЕРЕЗ <c>ResourceManager.GetString</c>, А
    ///    НЕ СВОЙСТВОМ. Проба собирается ПРОТИВ ОБЕИХ сборок — прежней (плечо
    ///    «до») и правленой; свойства <c>Resources.ERRMissingStartDateTime</c> в
    ///    прежней нет вовсе, и обращение к нему не дало бы собрать плечо «до».
    ///    На прежней сборке ключ отвечает <c>null</c>, и голосов про
    ///    отсутствующее время находится 0 — это и есть обратный контроль.
    ///
    /// ⛔ СЛЕПОК ПО ПОЛЯМ С МАСКОЙ. Поля времени, которые ставит КОНСТРУКТОР
    ///    <c>ResultData</c> (<c>DateTime.Now</c>), печатаются как «СЕЙЧАС», а не
    ///    датой: иначе слепок расходился бы сам с собой между двумя запусками
    ///    одного и того же плеча. Приём взят у полосы F41 целиком.
    ///
    /// Проба безоконная (входная сборка не BecquerelMonitor.exe), то есть
    /// <c>AppUi.HasWindows == false</c> и голоса идут в поток ошибок, откуда их
    /// и считает проба.
    ///
    /// Ключи:
    ///   --mode=cases --out=DIR   положить сочинённые входы;
    ///   --mode=run   --out=DIR   ввезти все *.n42 каталога ОБЕИМИ дверьми;
    ///   --culture=en-US          культура прогона (по умолчанию — машинная).
    /// </summary>
    static class Program
    {
        static string mode = "run";
        static string outDir = null;
        static string culture = null;

        /// <summary>Текст ресурса про ОТСУТСТВУЮЩЕЕ время — из ЭТОЙ сборки.</summary>
        static string missingText = null;
        /// <summary>Текст ресурса про НЕЧИТАЕМОЕ время — он в дереве был и раньше.</summary>
        static string unreadableText = null;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--mode=")) mode = a.Substring(7);
                else if (a.StartsWith("--out=")) outDir = a.Substring(6);
                else if (a.StartsWith("--culture=")) culture = a.Substring(10);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (!string.IsNullOrEmpty(culture))
            {
                CultureInfo ci = new CultureInfo(culture);
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;
            }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            missingText = Res("ERRMissingStartDateTime");
            unreadableText = Res("ERRUnreadableStartDateTimeN42");

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  культура прогона: " + Thread.CurrentThread.CurrentCulture.Name);
            Console.WriteLine("  ключ ERRMissingStartDateTime: "
                              + (missingText == null ? "НЕТ В СБОРКЕ (плечо «до»)"
                                                     : "«" + Cut(missingText) + "»"));
            Console.WriteLine("  ключ ERRUnreadableStartDateTimeN42: "
                              + (unreadableText == null ? "НЕТ В СБОРКЕ" : "«" + Cut(unreadableText) + "»"));
            // ⚠ Довод к выбору значения: вкладка пробы кладёт время в
            //   DateTimePicker (DCSampleInfoView.cs:31), а у него есть нижний
            //   предел. Печатается числом, чтобы «MinValue нельзя» не осталось
            //   рассуждением.
            Console.WriteLine("  DateTimePicker.MinimumDateTime = "
                              + DateTimePicker.MinimumDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                              + "; эпоха 1970-01-01 в пределах: "
                              + (new DateTime(1970, 1, 1) >= DateTimePicker.MinimumDateTime)
                              + "; DateTime.MinValue в пределах: "
                              + (DateTime.MinValue >= DateTimePicker.MinimumDateTime));
            Console.WriteLine();

            if (mode == "cases") return Cases();
            if (mode == "run") return Run();
            if (mode == "open") return Open();
            Console.Error.WriteLine("неизвестный --mode: " + mode);
            return 2;
        }

        static string Res(string key)
        {
            try { return BecquerelMonitor.Properties.Resources.ResourceManager.GetString(key); }
            catch (Exception) { return null; }
        }

        static string Cut(string s)
        {
            string one = s.Replace("\r", " ").Replace("\n", " ");
            return one.Length <= 60 ? one : one.Substring(0, 60) + "…";
        }

        /// <summary>
        /// Якорь для счёта голосов: начало текста ресурса ДО первой подстановки.
        /// Считать по всему тексту нельзя — в нём стоят {0}/{1}, уже заменённые
        /// числами в сказанной строке.
        /// </summary>
        static string Anchor(string resourceText)
        {
            if (string.IsNullOrEmpty(resourceText)) return null;
            int brace = resourceText.IndexOf('{');
            string head = brace > 0 ? resourceText.Substring(0, brace) : resourceText;
            head = head.Trim();
            return head.Length == 0 ? null : head;
        }

        // ==================================================================
        // СОЧИНЁННЫЕ ВХОДЫ
        // ==================================================================
        static int Cases()
        {
            if (outDir == null) { Console.Error.WriteLine("нужен --out=<каталог>"); return 2; }
            Directory.CreateDirectory(outDir);

            // ⛔ ТРИ измерения без записи времени — вход, на котором и мерится
            //    «один раз на файл». На одном спектре требование неотличимо от
            //    «один раз на измерение».
            Write("f52_multi_nostart.n42",
                  Multi("f52a", Meas("M1", "Foreground", "", Counts(0))
                              + Meas("M2", "Foreground", "", Counts(1))
                              + Meas("M3", "Foreground", "", Counts(2))));

            // Положительный контроль: те же три измерения, но время есть.
            Write("f52_multi_withstart.n42",
                  Multi("f52b", Meas("M1", "Foreground", "2024-03-14T08:15:00Z", Counts(0))
                              + Meas("M2", "Foreground", "2024-03-14T09:15:00Z", Counts(1))
                              + Meas("M3", "Foreground", "2024-03-14T10:15:00Z", Counts(2))));

            // Смесь: два без времени, одно с ним. Голос один, счёт в нём 2.
            Write("f52_multi_mixed.n42",
                  Multi("f52c", Meas("M1", "Foreground", "", Counts(0))
                              + Meas("M2", "Foreground", "2024-03-14T09:15:00Z", Counts(1))
                              + Meas("M3", "Foreground", "", Counts(2))));

            // ⚠ Элемента StartDateTime НЕТ ВОВСЕ — не пустой, а отсутствует.
            //   Разбору это то же самое (строка пуста), но входы разные, и
            //   считать их одинаковыми без замера нельзя.
            Write("f52_multi_noelement.n42",
                  Multi("f52d", MeasNoStart("M1", "Foreground", Counts(0))
                              + MeasNoStart("M2", "Foreground", Counts(1))));

            // Разбор 2006 года (N42InstrumentData) — третья дверь того же файла.
            Write("f52_2006_nostart.n42", N42_2006("", 64));
            Write("f52_2006_withstart.n42", N42_2006("2024-03-14T08:15:00Z", 64));

            // Формат RadiologicalInstrumentData: элемента времени НЕТ В МОДЕЛИ.
            Write("f52_rad.n42", Rad(1024));

            // ⛔ СОСЕДНЕЕ ПОЛОЖЕНИЕ — ЗАПИСЬ ЕСТЬ, НО НЕЧИТАЕМА (`A157`, `A171`).
            //    Мерится потому, что `A207` свёл к одному значению ОБА случая
            //    неизвестного начала: до 06.09.2026 нечитаемая дата тоже давала
            //    «сейчас», то есть в приложении жило ДВА значения одного и того
            //    же — «времени начала у документа нет». Дата по хиджре: ни
            //    XmlConvert, ни en-US, ни инвариантная культура её не читают.
            //    ⚠ Голос об этом ОСТАЁТСЯ своим (ERRUnreadableStartDateTimeN42):
            //    «не прочитано» и «не записано» — разные вести человеку, хотя
            //    подставляемое значение у них теперь одно.
            Write("f52_baddate_2012.n42",
                  Multi("f52e", Meas("M1", "Foreground", "04/05/44 10:07:57 ص", Counts(0))
                              + Meas("M2", "Foreground", "04/05/44 11:07:57 ص", Counts(1))));
            Write("f52_baddate_2006.n42", N42_2006("04/05/44 10:07:57 ص", 64));

            // ⚠ ЗАПИСЬ СО СМЕЩЕНИЕМ ЧАСОВОГО ПОЯСА И БЕЗ ПОЯСА ВОВСЕ. Оба входа
            //   заведены как ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ к приговору «двери сошлись»:
            //   если бы двери сходились ТОЛЬКО потому, что все прочие входы
            //   пишут «Z», приговор ничего не стоил бы. Дверь N42 читает
            //   RoundtripKind (`A171`), дверь SpecUtils — своим разбором, и
            //   совпадут они или нет, известно только замером.
            Write("f52_offset_2012.n42",
                  Multi("f52f", Meas("M1", "Foreground", "2024-03-14T08:15:00+03:00", Counts(0))));
            Write("f52_nozone_2012.n42",
                  Multi("f52g", Meas("M1", "Foreground", "2024-03-14T08:15:00", Counts(0))));

            Console.WriteLine("сочинённые входы положены в " + Path.GetFullPath(outDir));
            Console.WriteLine("⚠ Они СОЧИНЕНЫ и говорят о поведении РАЗБОРА, а не о том, что такие файлы часты.");
            return 0;
        }

        static void Write(string name, string xml)
        {
            File.WriteAllText(Path.Combine(outDir, name), xml, new UTF8Encoding(false));
            Console.WriteLine("  " + name);
        }

        // ==================================================================
        // ВВОЗ ОБЕИМИ ДВЕРЬМИ
        // ==================================================================
        sealed class Arm
        {
            public string Verdict;
            public int Spectra;
            public List<string> Times = new List<string>();
            public List<string> Voices = new List<string>();
            public int MissingVoices;
            public int UnreadableVoices;
            public string FirstStart;   // StartTime первого спектра, для сверки дверей
        }

        static int Run()
        {
            if (outDir == null) { Console.Error.WriteLine("нужен --out=<каталог с *.n42>"); return 2; }
            string[] files = Directory.GetFiles(outDir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);

            Console.WriteLine("=== ВВОЗ КАТАЛОГА " + Path.GetFullPath(outDir) + " ===");
            Console.WriteLine("  файлов: " + files.Length);
            Console.WriteLine();

            List<string> table = new List<string>();
            int mismatch = 0;

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                Arm n42 = Import(f, false);
                Arm spec = Import(f, true);

                Console.WriteLine("=== " + name + " ===");
                Report("дверь N42     ", n42);
                Report("дверь SpecUtils", spec);

                // ⛔ Приговор о совпадении дверей считает ПРОБА. «Похоже на глаз»
                //    здесь не годится: разница прежних значений была в 3.6 с.
                string same;
                if (n42.FirstStart == null || spec.FirstStart == null)
                {
                    same = "СВЕРИТЬ НЕЧЕГО (одна из дверей не ввезла)";
                }
                else if (n42.FirstStart == spec.FirstStart)
                {
                    same = "СОШЛОСЬ";
                }
                else
                {
                    same = "РАЗОШЛОСЬ";
                    mismatch++;
                }
                Console.WriteLine("  двери о начале набора: " + same);
                Console.WriteLine();

                table.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0,-28} | N42: {1,-7} голос-нет-времени {2}  начало {3,-27} | SpecUtils: {4,-7} голос-нет-времени {5}  начало {6,-27} | {7}",
                    name, n42.Verdict, n42.MissingVoices, n42.FirstStart ?? "—",
                    spec.Verdict, spec.MissingVoices, spec.FirstStart ?? "—", same));
            }

            Console.WriteLine("=== СВОДКА ===");
            foreach (string t in table) Console.WriteLine(t);
            Console.WriteLine();
            Console.WriteLine("ДВЕРИ РАЗОШЛИСЬ: " + mismatch);
            return 0;
        }

        /// <summary>
        /// ⛔ РОДНОЙ ФОРМАТ ДЕРЕВА (`*.xml`, 129 спектров корпуса) — ТРЕТЬЯ
        /// ДВЕРЬ, И ЕЁ НАДО МЕРИТЬ ОТДЕЛЬНО. Правка тронула поле, которое
        /// приходит в родной документ ИЗ ФАЙЛА, а не подставляется; замер тут
        /// проверяет, что «ввоз не сломан» — не посылка, а измерение: слепок
        /// обоих плеч обязан совпасть ПОСИМВОЛЬНО, включая времена, потому что
        /// у корпусных спектров время начала в файле ЕСТЬ.
        /// </summary>
        static int Open()
        {
            if (outDir == null) { Console.Error.WriteLine("нужен --out=<каталог с *.xml>"); return 2; }
            string[] files = Directory.GetFiles(outDir, "*.xml");
            Array.Sort(files, StringComparer.Ordinal);

            Console.WriteLine("=== ОТКРЫТИЕ РОДНОГО ФОРМАТА " + Path.GetFullPath(outDir) + " ===");
            Console.WriteLine("  файлов: " + files.Length);
            Console.WriteLine();

            int ok = 0, failed = 0;
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = null;
                string said = null;
                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                try { doc = DocumentManager.GetInstance().OpenDocument(f); }
                catch (Exception ex) { said = ex.GetType().Name + ": " + Flat(ex.Message); }
                finally { Console.SetError(realErr); }

                if (said != null) { failed++; Console.WriteLine(name + " | ОТКАЗ | " + said); continue; }
                ok++;
                int count = doc == null || doc.ResultDataFile == null ? 0 : doc.ResultDataFile.ResultDataList.Count;
                StringBuilder sb = new StringBuilder();
                sb.Append(name).Append(" | ОТКРЫТ | спектров ").Append(count);
                for (int i = 0; i < count; i++)
                {
                    ResultData rd = doc.ResultDataFile.ResultDataList[i];
                    sb.Append(" | [").Append(i).Append("] начало ").Append(Stamp(rd.StartTime))
                      .Append(", проба ").Append(rd.SampleInfo == null ? "нет" : Stamp(rd.SampleInfo.Time))
                      .Append(", конец ").Append(Stamp(rd.EndTime))
                      .Append(", ").Append(Numbers(rd));
                }
                Console.WriteLine(sb.ToString());
                // ⚠ Документ закрывается, иначе второе открытие того же имени
                //   отказало бы «уже открыт», а список рос бы всю дорогу.
                if (doc != null) DocumentManager.GetInstance().CloseDocument(doc);
            }
            Console.WriteLine();
            Console.WriteLine("ОТКРЫТО: " + ok + "   ОТКАЗАНО: " + failed);
            return 0;
        }

        static void Report(string title, Arm a)
        {
            Console.WriteLine("  " + title + " | " + a.Verdict + " | спектров " + a.Spectra);
            foreach (string t in a.Times) Console.WriteLine("      " + t);
            Console.WriteLine("      голосов всего " + a.Voices.Count
                              + ", из них про ОТСУТСТВУЮЩЕЕ время " + a.MissingVoices
                              + ", про НЕЧИТАЕМОЕ " + a.UnreadableVoices);
            foreach (string v in a.Voices) Console.WriteLine("      сказано: " + v);
        }

        static Arm Import(string file, bool specUtils)
        {
            Arm a = new Arm();
            DocEnergySpectrum doc = new DocEnergySpectrum();

            TextWriter realErr = Console.Error;
            StringWriter caught = new StringWriter();
            Console.SetError(caught);
            try
            {
                if (specUtils) DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, file, 3600);
                else DocumentManager.GetInstance().ImportDocumentN42(doc, file);
                a.Verdict = "ВВЕЗЁН";
            }
            catch (Exception ex)
            {
                a.Verdict = "ОТКАЗ";
                a.Times.Add("отказ: " + ex.GetType().Name + ": " + Flat(ex.Message));
            }
            finally
            {
                Console.SetError(realErr);
            }

            foreach (string line in caught.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string one = Flat(line).Trim();
                if (one.Length == 0) continue;
                a.Voices.Add(one);
                string am = Anchor(missingText);
                string au = Anchor(unreadableText);
                if (am != null && one.IndexOf(am, StringComparison.Ordinal) >= 0) a.MissingVoices++;
                else if (au != null && one.IndexOf(au, StringComparison.Ordinal) >= 0) a.UnreadableVoices++;
            }

            try
            {
                if (doc.ResultDataFile != null)
                {
                    a.Spectra = doc.ResultDataFile.ResultDataList.Count;
                    for (int i = 0; i < a.Spectra; i++)
                    {
                        ResultData rd = doc.ResultDataFile.ResultDataList[i];
                        string st = Stamp(rd.StartTime);
                        if (i == 0) a.FirstStart = st;
                        a.Times.Add("[" + i + "] StartTime " + st
                                    + " | SampleInfo.Time "
                                    + (rd.SampleInfo == null ? "нет" : Stamp(rd.SampleInfo.Time))
                                    + " | EndTime " + Stamp(rd.EndTime)
                                    + " | " + Numbers(rd));
                    }
                }
            }
            catch (Exception ex)
            {
                a.Times.Add("слепок не снялся: " + ex.GetType().Name);
            }
            return a;
        }

        /// <summary>
        /// ⛔ МАСКА НА «СЕЙЧАС». Поле, оставленное конструктором
        /// <c>ResultData</c> (<c>DateTime.Now</c>), печатается меткой, а не
        /// датой: иначе слепок расходился бы сам с собой между двумя запусками
        /// ОДНОГО плеча, и сверять плечи было бы нечем. Всё прочее печатается с
        /// МИЛЛИСЕКУНДАМИ — прежнее 1970-01-01 00:00:03.600 и новое
        /// 00:00:00.000 иначе слились бы в одну строку.
        /// </summary>
        static string Stamp(DateTime t)
        {
            if (Math.Abs((DateTime.Now - t).TotalMinutes) < 1.0) return "СЕЙЧАС(маска)";
            return t.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// ⛔ ЧИСЛА РАЗОБРАННОГО СПЕКТРА — ЧТОБЫ «ВВОЗ НЕ СЛОМАН» БЫЛО ЗАМЕРОМ,
        /// А НЕ ПОСЫЛКОЙ. Слепок одних времён доказал бы только про времена;
        /// правка, попутно сбившая отсчёты или шкалу, прошла бы незамеченной.
        /// Печатается то же, что печатает слепок `N42RoundTripProbe`: отсчёты,
        /// длительности, шкала, фон.
        /// </summary>
        static string Numbers(ResultData rd)
        {
            EnergySpectrum es = rd.EnergySpectrum;
            if (es == null) return "спектра нет";
            long sum = es.Spectrum == null ? -1 : es.Spectrum.Sum(x => (long)x);
            PolynomialEnergyCalibration p = es.EnergyCalibration as PolynomialEnergyCalibration;
            string cal = p == null
                ? (es.EnergyCalibration == null ? "нет" : es.EnergyCalibration.GetType().Name)
                : ("порядок " + p.PolynomialOrder + " ["
                   + string.Join(", ", (p.Coefficients ?? new double[0])
                        .Select(c => c.ToString("0.##########", CultureInfo.InvariantCulture)).ToArray()) + "]");
            EnergySpectrum bg = rd.BackgroundEnergySpectrum;
            return "кан " + es.NumberOfChannels
                 + ", сумма " + sum
                 + ", всего " + es.TotalPulseCount
                 + ", изм " + es.MeasurementTime.ToString("0.######", CultureInfo.InvariantCulture)
                 + ", живое " + es.LiveTime.ToString("0.######", CultureInfo.InvariantCulture)
                 + ", шкала " + cal
                 + (bg == null ? ", фона нет"
                    : ", ФОН кан " + bg.NumberOfChannels + ", сумма " + bg.Spectrum.Sum(x => (long)x));
        }

        static string Flat(string s)
        {
            return (s ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        // ==================================================================
        // ГЕНЕРАТОРЫ ВХОДОВ
        // ==================================================================
        static string Head(string tag)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\" n42DocUUID=\"probe-a207-"
                 + tag + "\">\r\n"
                 + "  <RadInstrumentInformation id=\"RadInstrument\">\r\n"
                 + "    <RadInstrumentManufacturerName>PROBE</RadInstrumentManufacturerName>\r\n"
                 + "    <RadInstrumentModelName>A207</RadInstrumentModelName>\r\n"
                 + "    <RadInstrumentClassCode>Radionuclide Identifier</RadInstrumentClassCode>\r\n"
                 + "    <RadInstrumentVersion>\r\n"
                 + "      <RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\r\n"
                 + "      <RadInstrumentComponentVersion>1</RadInstrumentComponentVersion>\r\n"
                 + "    </RadInstrumentVersion>\r\n"
                 + "  </RadInstrumentInformation>\r\n";
        }

        static string Multi(string tag, string measurements)
        {
            return Head(tag)
                 + "  <EnergyCalibration id=\"EC1\">\r\n"
                 + "    <CoefficientValues>3.5 12.5</CoefficientValues>\r\n"
                 + "  </EnergyCalibration>\r\n"
                 + measurements
                 + "</RadInstrumentData>\r\n";
        }

        static string Meas(string id, string classCode, string startDateTime, string counts)
        {
            return "  <RadMeasurement id=\"" + id + "\">\r\n"
                 + "    <MeasurementClassCode>" + classCode + "</MeasurementClassCode>\r\n"
                 + "    <StartDateTime>" + startDateTime + "</StartDateTime>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S-" + id + "\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n";
        }

        /// <summary>То же измерение, но элемента StartDateTime НЕТ ВОВСЕ.</summary>
        static string MeasNoStart(string id, string classCode, string counts)
        {
            return "  <RadMeasurement id=\"" + id + "\">\r\n"
                 + "    <MeasurementClassCode>" + classCode + "</MeasurementClassCode>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S-" + id + "\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n";
        }

        static string Counts(int shift)
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < 64; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append((i % 7) + 1 + shift);
            }
            return counts.ToString();
        }

        /// <summary>
        /// Файл N42-2006. ⛔ Наклон шкалы 0.5 кэВ/канал, а не 12.5: этот разбор
        /// зовёт CheckCalibration с числом каналов ДОКУМЕНТА, и при 12.5 вход
        /// отказывал бы по посторонней причине (замер полосы C4, 05.09.2026).
        /// </summary>
        static string N42_2006(string startTime, int channels)
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < channels; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append((i % 7) + 1);
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<N42InstrumentData xmlns=\"" + BecquerelMonitor.N42.N42InstrumentData.Ns + "\">\r\n"
                 + "  <Measurement>\r\n"
                 + "    <InstrumentInformation>\r\n"
                 + "      <InstrumentType>Spectrometer</InstrumentType>\r\n"
                 + "      <Manufacturer>PROBE</Manufacturer>\r\n"
                 + "      <InstrumentModel>A207</InstrumentModel>\r\n"
                 + "      <InstrumentID>2006</InstrumentID>\r\n"
                 + "      <ProbeType>NaI</ProbeType>\r\n"
                 + "    </InstrumentInformation>\r\n"
                 + "    <Spectrum Type=\"PHA\">\r\n"
                 + "      <StartTime>" + startTime + "</StartTime>\r\n"
                 + "      <RealTime>PT300S</RealTime>\r\n"
                 + "      <LiveTime>PT295S</LiveTime>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </Measurement>\r\n"
                 + "  <Calibration Type=\"Energy\">\r\n"
                 + "    <Equation Model=\"Polynomial\">\r\n"
                 + "      <Coefficients>3.5 0.5</Coefficients>\r\n"
                 + "    </Equation>\r\n"
                 + "  </Calibration>\r\n"
                 + "</N42InstrumentData>\r\n";
        }

        static string Rad(int n)
        {
            StringBuilder energies = new StringBuilder();
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                double e = 5.0 + 0.5 * i + 1e-6 * i * i + 1e-11 * i * i * i + 1e-14 * i * i * i * i;
                if (i > 0) { energies.Append(' '); counts.Append(' '); }
                energies.Append(e.ToString("0.######", CultureInfo.InvariantCulture));
                counts.Append((i % 7) + 1);
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadiologicalInstrumentData xmlns=\"http://physics.nist.gov/N42/2006/N42\">\r\n"
                 + "  <MeasurementGroup>\r\n"
                 + "    <Measurement>\r\n"
                 + "      <Spectrum>\r\n"
                 + "        <InstrumentInformation>\r\n"
                 + "          <Manufacturer>PROBE</Manufacturer>\r\n"
                 + "          <Model>A207</Model>\r\n"
                 + "          <SerialNumber>" + n + "</SerialNumber>\r\n"
                 + "        </InstrumentInformation>\r\n"
                 + "        <EnergyCalibration>\r\n"
                 + "          <CalibrationEquation>List</CalibrationEquation>\r\n"
                 + "          <ChannelEnergies>" + energies + "</ChannelEnergies>\r\n"
                 + "        </EnergyCalibration>\r\n"
                 + "        <ChannelData NumberOfChannels=\"" + n + "\">" + counts + "</ChannelData>\r\n"
                 + "        <LiveTime>295</LiveTime>\r\n"
                 + "        <SpectrumType>Item</SpectrumType>\r\n"
                 + "      </Spectrum>\r\n"
                 + "    </Measurement>\r\n"
                 + "  </MeasurementGroup>\r\n"
                 + "</RadiologicalInstrumentData>\r\n";
        }
    }
}
