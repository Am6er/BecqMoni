using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace N42DoorsProbeF54
{
    /// <summary>
    /// ДВЕ ДВЕРИ ОДНОГО ФАЙЛА — ПРИГОВОРЫ ПОИМЁННО И СТОРОЖ МОЛЧАЛИВОГО
    /// РАСХОЖДЕНИЯ (`A216`, полоса F54, 06.09.2026).
    ///
    /// ⛔ ЧТО ИМЕННО МЕРИТСЯ И ПОЧЕМУ ЭТО НЕ ТО ЖЕ, ЧТО ДВА ОТДЕЛЬНЫХ ПРОГОНА.
    /// `N42RoundTripProbe` умеет гонять каждую дверь по отдельности
    /// (`--mode=import` и `--mode=specutils`), и до 06.09.2026 расхождение
    /// дверей считалось ВРУЧНУЮ по двум спискам: журнал `C12` знает только
    /// итоги «19/10 против 26/3», а какие ИМЕННО файлы разошлись — не знает
    /// никто. Сводка руками не есть сторож: она не отказывает и её никто не
    /// зовёт. Здесь обе двери идут по ОДНОМУ списку файлов в ОДНОМ прогоне,
    /// расхождение называется поимённо, а у сводки есть КОД ВОЗВРАТА.
    ///
    /// ⛔ СТОРОЖИТСЯ НЕ ВСЯКОЕ РАСХОЖДЕНИЕ, А МОЛЧАЛИВОЕ. Двери имеют право
    /// судить файл по-разному: одна читает три извода N42 своим разбором,
    /// другая отдаёт файл чужой библиотеке, и совпасть им нечем. Беда не в
    /// том, что приговоры разные, а в том, что человек об этом НЕ УЗНАЁТ:
    /// дверь N42 отказывает СЛОВАМИ, а дверь SpecUtils в том же положении
    /// ввозит МОЛЧА — и разница видна только тому, кто откроет один файл
    /// двумя пунктами меню подряд. Поэтому сторож считает файлы, у которых
    /// приговоры разошлись И ввозящая дверь не сказала НИ СЛОВА, и отказывает
    /// кодом возврата, когда таких больше объявленного (`--expect-silent`).
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ЗАШИТ В САМ ЗАМЕР. Проверка, у которой
    /// молчаливых расхождений ноль на всяком входе, не мерит ничего. Поэтому
    /// объявленное число НЕ НОЛЬ: одно молчаливое расхождение оставлено
    /// нарочно (`case1_boundary` — там SpecUtils читает границы энергий
    /// каналов и восстанавливает НАСТОЯЩУЮ шкалу, а отказывает дверь N42;
    /// решение, чинить ли это, за Amber). Сторож, поставленный на ноль,
    /// пришлось бы обходить исключением; поставленный на измеренное число, он
    /// ловит и НОВОЕ молчание, и ПОТЕРЮ уже заведённого голоса.
    ///
    /// Ключи:
    ///   --out=&lt;каталог&gt;      каталог с *.n42 (обязателен)
    ///   --extra              дописать в каталог сочинённые входы полосы F54
    ///                        (case30_rad_live_iso — `A214`,
    ///                         case31_rad_extra — `A213`)
    ///   --expect-silent=&lt;n&gt;  сколько молчаливых расхождений ожидается
    ///                        (по умолчанию 1); больше — код возврата 1
    ///   --culture=&lt;имя&gt;      культура прогона
    ///
    /// Проба безоконная: AppUi.HasWindows == false, то есть мерится отказная
    /// половина обеих дверей, а голоса `AppUi.Report` уходят в поток ошибок и
    /// перехватываются здесь.
    /// </summary>
    static class Program
    {
        static string outDir = null;
        static string culture = null;
        static bool extra = false;
        static int expectSilent = 1;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--out=")) outDir = a.Substring(6);
                else if (a.StartsWith("--culture=")) culture = a.Substring(10);
                else if (a == "--extra") extra = true;
                else if (a.StartsWith("--expect-silent="))
                {
                    expectSilent = int.Parse(a.Substring(16), CultureInfo.InvariantCulture);
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=<каталог с n42>");
                return 2;
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

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  культура прогона: " + Thread.CurrentThread.CurrentCulture.Name);
            Console.WriteLine();

            if (extra) WriteExtraCases();

            return Doors();
        }

        // ==================================================================
        // СОЧИНЁННЫЕ ВХОДЫ ПОЛОСЫ F54
        // ==================================================================

        /// <summary>
        /// Два входа, которых у `N42RoundTripProbe --mode=cases` нет.
        ///
        /// ⛔ Оба СОЧИНЕНЫ и доказывают поведение РАЗБОРА, а не совместимость
        /// с прибором: настоящего файла Alpha Hound
        /// (<c>RadiologicalInstrumentData</c>) в дереве НЕТ вовсе (`T184`).
        /// </summary>
        static void WriteExtraCases()
        {
            Directory.CreateDirectory(outDir);

            // `A214`: ЖИВОЕ ВРЕМЯ ПО СПЕЦИФИКАЦИИ. Извод Alpha Hound пишет
            //   LiveTime голыми секундами («295»), а N42-2006 требует
            //   xs:duration («PT295S»). Поле было объявлено double, и такой
            //   файл ронял разбор ВСЕГО документа ещё в XmlSerializer.
            //   ⛔ Это положительный контроль правки: до неё вход ОБЯЗАН
            //   отказывать, после — ввозиться с живым временем 295.
            Write("case30_rad_live_iso.n42", Rad(1024, 1024).Replace(
                      "<LiveTime>295</LiveTime>", "<LiveTime>PT295S</LiveTime>"));

            // `A213`: ЗАПИСАНО БОЛЬШЕ ОБЪЯВЛЕННОГО. Обратная половина беды,
            //   закрытой `A208` (объявлено больше записанного → отказ словами).
            //   Объявлено 64, записано 100: в документ ложатся 64 канала, хвост
            //   из 36 каналов пропадает вместе со своими отсчётами.
            Write("case31_rad_extra.n42", Rad(100, 64));

            // ⛔ `A217`: ДВА ВХОДА, У КОТОРЫХ ОТКАЗЫВАЕТ ИМЕННО CheckCalibration.
            //    Ни один из 29 прежних сочинённых входов до этой проверки не
            //    доезжает: их отвергают более ранние сторожа (пустой список
            //    коэффициентов, порядок больше четвёртого, нехватка каналов).
            //    То есть ОСТАТОК `A210` — «отказ CheckCalibration случается уже
            //    после перезаписи документа» — не мерился НИЧЕМ, и утверждать
            //    про него можно было только по исходнику.
            //
            //    Оба входа делают шкалу УБЫВАЮЩЕЙ: CheckCalibration отвергает
            //    полином, у которого энергия канала i+1 меньше энергии канала i
            //    (PolynomialEnergyCalibration.cs, цикл по каналам). Всё
            //    остальное в файлах здоровое — значит отказ приходит ровно
            //    оттуда, откуда назван.
            //
            //    ⛔ Мерка — СЛЕПОК ДОКУМЕНТА ПОСЛЕ ОТКАЗА. Ожидание: сумма 0,
            //    времена 0, то есть чисел отказавшего файла в документе НЕТ.
            Write("case32_2006_badscale.n42", N42_2006_Decreasing(64));
            Write("case33_rad_badscale.n42", RadDecreasing(64));
        }

        /// <summary>
        /// Файл спецификации 2006 года со здоровым спектром и УБЫВАЮЩЕЙ шкалой
        /// (двучлен 3.5 − 0.5·ch): на 63-м канале энергия −28 кэВ.
        /// CheckCalibration такую шкалу отвергает, и до `A217` отсчёты успевали
        /// лечь в документ раньше отказа.
        /// </summary>
        static string N42_2006_Decreasing(int channels)
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
                 + "      <InstrumentModel>A217</InstrumentModel>\r\n"
                 + "      <InstrumentID>2006</InstrumentID>\r\n"
                 + "      <ProbeType>NaI</ProbeType>\r\n"
                 + "    </InstrumentInformation>\r\n"
                 + "    <Spectrum Type=\"PHA\">\r\n"
                 + "      <StartTime>2026-09-05T12:00:00Z</StartTime>\r\n"
                 + "      <RealTime>PT300S</RealTime>\r\n"
                 + "      <LiveTime>PT295S</LiveTime>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </Measurement>\r\n"
                 + "  <Calibration Type=\"Energy\">\r\n"
                 + "    <Equation Model=\"Polynomial\">\r\n"
                 + "      <Coefficients>3.5 -0.5</Coefficients>\r\n"
                 + "    </Equation>\r\n"
                 + "  </Calibration>\r\n"
                 + "</N42InstrumentData>\r\n";
        }

        /// <summary>
        /// Тот же вход <c>RadiologicalInstrumentData</c>, но список энергий
        /// каналов УБЫВАЕТ (5000 − 0.5·ch). Подогнанный по нему полином 4-го
        /// порядка убывает вместе с ним, и CheckCalibration его отвергает.
        /// </summary>
        static string RadDecreasing(int n)
        {
            StringBuilder energies = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                double e = 5000.0 - 0.5 * i;
                if (i > 0) energies.Append(' ');
                energies.Append(e.ToString("0.######", CultureInfo.InvariantCulture));
            }
            string full = Rad(n, n);
            int a = full.IndexOf("<ChannelEnergies>", StringComparison.Ordinal);
            int b = full.IndexOf("</ChannelEnergies>", StringComparison.Ordinal);
            if (a < 0 || b < 0) throw new Exception("RadDecreasing: образец не найден");
            a += "<ChannelEnergies>".Length;
            return full.Substring(0, a) + energies + full.Substring(b);
        }

        static void Write(string name, string xml)
        {
            string path = Path.Combine(outDir, name);
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            Console.WriteLine("  положен вход " + name);
        }

        /// <summary>
        /// Вход <c>RadiologicalInstrumentData</c>: <paramref name="n"/> каналов
        /// записано, <paramref name="declared"/> объявлено атрибутом.
        /// Шкала списком энергий (иного этот разбор не принимает), полином под
        /// ней тот же, что у `N42RoundTripProbe.Rad` — чтобы слепки сходились.
        /// </summary>
        static string Rad(int n, int declared)
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
                 + "          <Model>F54</Model>\r\n"
                 + "          <SerialNumber>" + n.ToString(CultureInfo.InvariantCulture) + "</SerialNumber>\r\n"
                 + "        </InstrumentInformation>\r\n"
                 + "        <EnergyCalibration>\r\n"
                 + "          <CalibrationEquation>List</CalibrationEquation>\r\n"
                 + "          <ChannelEnergies>" + energies + "</ChannelEnergies>\r\n"
                 + "        </EnergyCalibration>\r\n"
                 + "        <ChannelData NumberOfChannels=\"" + declared.ToString(CultureInfo.InvariantCulture) + "\">" + counts + "</ChannelData>\r\n"
                 + "        <LiveTime>295</LiveTime>\r\n"
                 + "        <SpectrumType>Item</SpectrumType>\r\n"
                 + "      </Spectrum>\r\n"
                 + "    </Measurement>\r\n"
                 + "  </MeasurementGroup>\r\n"
                 + "</RadiologicalInstrumentData>\r\n";
        }

        // ==================================================================
        // ОБЕ ДВЕРИ ПО ОДНОМУ СПИСКУ
        // ==================================================================

        sealed class Verdict
        {
            public bool Imported;
            public string Refusal;   // null, если ввезён
            public string Spoken;    // то, что дверь сказала человеку
            public string Print;     // слепок документа
        }

        static int Doors()
        {
            string[] files = Directory.GetFiles(outDir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);

            Console.WriteLine("=== ДВЕ ДВЕРИ КАТАЛОГА " + Path.GetFullPath(outDir) + " ===");
            Console.WriteLine("  файлов: " + files.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine();

            List<string> diverged = new List<string>();
            List<string> silent = new List<string>();
            int n42Ok = 0, suOk = 0;

            Console.WriteLine("=== ПРИГОВОРЫ ПОИМЁННО (дверь N42 | дверь SpecUtils) ===");
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                Verdict a = RunN42(f);
                Verdict b = RunSpecUtils(f);
                if (a.Imported) n42Ok++;
                if (b.Imported) suOk++;

                string va = a.Imported ? "ВВЕЗЁН" : "ОТКАЗ ";
                string vb = b.Imported ? "ВВЕЗЁН" : "ОТКАЗ ";
                bool differ = a.Imported != b.Imported;

                // ⛔ «МОЛЧА» — это про ТУ дверь, которая файл ВВЕЗЛА. Дверь,
                //    отказавшая словами, свою работу сделала; вопрос в том,
                //    узнал ли человек хоть что-нибудь, открыв файл ДРУГИМ
                //    пунктом меню.
                string importerSaid = null;
                if (differ) importerSaid = a.Imported ? a.Spoken : b.Spoken;
                bool silentDiff = differ && string.IsNullOrEmpty(importerSaid);

                Console.WriteLine("  " + name.PadRight(30) + " | " + va + " | " + vb
                                  + (differ ? (silentDiff ? "  <== РАЗОШЛИСЬ МОЛЧА" : "  <== разошлись, ввозящая дверь сказала")
                                            : ""));
                if (differ)
                {
                    diverged.Add(name);
                    if (silentDiff) silent.Add(name);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== СВОДКА ===");
            Console.WriteLine("  дверь N42:       ВВЕЗЕНО " + n42Ok.ToString(CultureInfo.InvariantCulture)
                              + " / ОТКАЗАНО " + (files.Length - n42Ok).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  дверь SpecUtils: ВВЕЗЕНО " + suOk.ToString(CultureInfo.InvariantCulture)
                              + " / ОТКАЗАНО " + (files.Length - suOk).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  ДВЕРИ РАЗОШЛИСЬ: " + diverged.Count.ToString(CultureInfo.InvariantCulture)
                              + (diverged.Count > 0 ? " — " + string.Join(", ", diverged.ToArray()) : ""));
            Console.WriteLine("  ИЗ НИХ МОЛЧА:    " + silent.Count.ToString(CultureInfo.InvariantCulture)
                              + (silent.Count > 0 ? " — " + string.Join(", ", silent.ToArray()) : ""));
            Console.WriteLine("  объявлено молчаливых (--expect-silent): "
                              + expectSilent.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine();

            Console.WriteLine("=== ГОЛОСА ДВЕРИ N42 ===");
            foreach (string f in files) PrintVoice(f, RunN42(f));
            Console.WriteLine();
            Console.WriteLine("=== ГОЛОСА ДВЕРИ SpecUtils ===");
            foreach (string f in files) PrintVoice(f, RunSpecUtils(f));
            Console.WriteLine();

            if (silent.Count > expectSilent)
            {
                Console.Error.WriteLine("ОТКАЗ СТОРОЖА: молчаливых расхождений "
                                        + silent.Count.ToString(CultureInfo.InvariantCulture)
                                        + ", объявлено " + expectSilent.ToString(CultureInfo.InvariantCulture)
                                        + ". Файл, который одна дверь отказывает словами, а другая ввозит "
                                        + "БЕЗ ЕДИНОГО СЛОВА, даёт человеку разный исход по пункту меню.");
                return 1;
            }
            if (silent.Count < expectSilent)
            {
                Console.WriteLine("⚠ молчаливых расхождений МЕНЬШЕ объявленного ("
                                  + silent.Count.ToString(CultureInfo.InvariantCulture) + " < "
                                  + expectSilent.ToString(CultureInfo.InvariantCulture)
                                  + ") — сторож ослаб: понизьте --expect-silent, иначе он перестанет ловить возврат немоты.");
            }
            Console.WriteLine("СТОРОЖ ПРОШЁЛ.");
            return 0;
        }

        static void PrintVoice(string f, Verdict v)
        {
            Console.WriteLine("  " + Path.GetFileName(f) + " | " + (v.Imported ? "ВВЕЗЁН" : "ОТКАЗ")
                              + " | " + (v.Imported
                                         ? (string.IsNullOrEmpty(v.Spoken) ? "(молча)" : v.Spoken)
                                         : v.Refusal + (string.IsNullOrEmpty(v.Spoken) ? "" : "   [вслух: " + v.Spoken + "]")));
            Console.WriteLine("      слепок: " + v.Print);
        }

        static Verdict RunN42(string f)
        {
            return Run(f, (doc, path) => DocumentManager.GetInstance().ImportDocumentN42(doc, path));
        }

        static Verdict RunSpecUtils(string f)
        {
            return Run(f, (doc, path) => DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, path, 3600));
        }

        /// <summary>
        /// Один прогон одной двери.
        ///
        /// ⛔ ПОТОК ОШИБОК ПОДМЕНЯЕТСЯ НА ВРЕМЯ ВВОЗА. `AppUi.Report` без окон
        /// пишет туда и работу ПРОДОЛЖАЕТ — то есть приложение говорит, а
        /// проба, читающая только исключения, видела бы «(молча)». Признак без
        /// читателя не есть признак; приём взят у `N42RoundTripProbe` целиком,
        /// второго не заводится.
        /// </summary>
        static Verdict Run(string path, Action<DocEnergySpectrum, string> door)
        {
            Verdict v = new Verdict();
            DocEnergySpectrum doc = new DocEnergySpectrum();
            TextWriter realErr = Console.Error;
            StringWriter caught = new StringWriter();
            Console.SetError(caught);
            try
            {
                door(doc, path);
                v.Imported = true;
            }
            catch (Exception ex)
            {
                v.Imported = false;
                v.Refusal = ex.GetType().Name + ": " + Flat(ex.Message);
            }
            finally
            {
                Console.SetError(realErr);
            }
            v.Spoken = Flat(caught.ToString()).Trim();
            v.Print = Print(doc);
            return v;
        }

        static string Print(DocEnergySpectrum doc)
        {
            try
            {
                ResultData rd = doc.ActiveResultData;
                if (rd == null || rd.EnergySpectrum == null) return "(документа нет)";
                EnergySpectrum es = rd.EnergySpectrum;
                long sum = 0;
                for (int i = 0; i < es.Spectrum.Length; i++) sum += es.Spectrum[i];
                PolynomialEnergyCalibration pol = es.EnergyCalibration as PolynomialEnergyCalibration;
                string scale = pol == null ? "(шкалы нет)"
                    : "порядок " + pol.PolynomialOrder.ToString(CultureInfo.InvariantCulture)
                      + " [" + string.Join(", ", Array.ConvertAll(pol.Coefficients,
                            x => x.ToString("R", CultureInfo.InvariantCulture))) + "]";
                return "кан " + es.NumberOfChannels.ToString(CultureInfo.InvariantCulture)
                     + ", сумма " + sum.ToString(CultureInfo.InvariantCulture)
                     + ", изм " + es.MeasurementTime.ToString("R", CultureInfo.InvariantCulture)
                     + ", живое " + es.LiveTime.ToString("R", CultureInfo.InvariantCulture)
                     + ", начало " + rd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                     + ", шкала " + scale;
            }
            catch (Exception ex)
            {
                return "(слепок не снят: " + ex.GetType().Name + ")";
            }
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")
                    .Replace("     ", " ").Replace("   ", " ").Replace("  ", " ").Trim();
        }
    }
}
