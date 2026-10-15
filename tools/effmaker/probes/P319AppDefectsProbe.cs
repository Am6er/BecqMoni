// Проба полосы П319 (15.10.2026): остаток находок ночи в главном окне, документах и дозе —
// ДО и ПОСЛЕ правки. Журнал — handover/handover-2026-10-15-p319-main-window-dose.md; находки —
// handover/handover-2026-10-15-p312-screen-pass.md (Н5, Н6, Н16, Н18, Н22),
// handover/handover-2026-10-15-p311-data-output-review.md (пустой выбор, N42 с пустой
// конфигурацией), handover/handover-2026-10-15-p315-long-run-leaks.md (п. 7, кэш дозы).
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут пути от
// каталога входной сборки). Проба зовёт приложение только тем, что есть И ДО, И ПОСЛЕ правки
// (пункты меню — PerformClick, поля и обработчики без окон — отражением), поэтому один исходник
// работает на обеих сборках: сборка ДО правки — положительный контроль (обязана показать
// дефекты).
//
//   P319AppDefectsProbe.exe --mode=dose --corpus=<tools\CORPUS\corpus\spectra>
//       Кэш квадратуры Клейна — Нишины мощности дозы: 20 000 плывущих энергий (как
//       представительные энергии диапазонов при наборе) — сколько записей в кэше; коэффициенты
//       на 45 энергиях до и после переполнения — побитово; то же из 8 потоков разом; мощность
//       дозы на спектрах корпуса — побитово до и после переполнения, и строка «sha» для сверки
//       сборок между собой.
//   P319AppDefectsProbe.exe --mode=n42 [--tmp=<каталог>]
//       Импорт N42 при «импортировать с пустой конфигурацией»: файл без RadMeasurement, пустой
//       ChannelData (2012, 2006), Alpha Hound с NumberOfChannels=0 — причина отказа и число
//       каналов документа после отказа; контроли (здоровые файлы) и те же входы без настройки.
//   P319AppDefectsProbe.exe --mode=ui --corpus=<…> [--tmp=<каталог>]
//       Настоящий MainForm (вне экрана, свой такт остановлен), документ — копия
//       AS80_Th232WT20.xml. Окна приложения (#32770) обслуживает поток-автоответчик: пишет
//       заголовок и текст, жмёт «Нет»/«Отмена»/«ОК»; у окна сохранения читает имя файла и жмёт
//       «Отмена»; у окна открытия вписывает заданный файл. Диалоги WinForms («Изменить число
//       каналов», «Ограничить энергию») заполняет таймер потока окон.
//       Н16 Enter в пределе времени без изменения; Н5 «Изменить число каналов» 2000000000;
//       Н6 заголовки отказов (каналы −5, обрезка −5 кэВ, предел −5, предупреждение экспорта N42);
//       Н22 имя по умолчанию «Жёсткого вычета в файл» у изменённого документа; П311 «Экспорт
//       спектра в файл» без выбранных; Н18 объединение с файлом другого числа каналов.
//       (П325, решение Amber 15.10.2026 «64…65536 (Рекомендую)») «Изменить число каналов»:
//       −5, 2000000000, 63, 65537 — отказ из САМОГО окна (окно после «Ввод» открыто, заголовок
//       отказа — `InvalidValueDialogTitle`, после «Закрыть» `SendData` = 0, до MainForm число не
//       доходит, документов не прибавилось); 64 — новый документ (меню берёт те же границы
//       `ChannelCountLimits`, прежнее `> 64` отказывало).
//       ⚠ Признак окон `AppUi.hasWindows` выставляется в true (как `ModalThreadProbeO25`): без
//       него отказы `AppUi.Report` без окон бросают, а не показывают окно.
//
// Строка вывода — «<пункт>  что мерено  значение  ЧИСТО|ДЕФЕКТ|КОНТРОЛЬ|ОБВЯЗКА».
// Коды: 0 — дефектов нет; 4 — есть дефекты (на сборке ДО — ожидаемо); 5 — обвязка; 2 — ключи.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;

namespace P319AppDefectsProbe
{
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static int defects, rig;
        static string tmp, corpus;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            string mode = null;
            tmp = Path.Combine(Path.GetTempPath(), "p319probe");
            foreach (string a in args)
            {
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
                else if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
                else { Console.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (mode != "dose" && mode != "n42" && mode != "ui")
            {
                Console.WriteLine("нужен --mode=dose|n42|ui");
                return 2;
            }
            if ((mode == "dose" || mode == "ui") && (corpus == null || !Directory.Exists(corpus)))
            {
                Console.WriteLine("нужен --corpus=<каталог спектров корпуса>");
                return 2;
            }
            Directory.CreateDirectory(tmp);
            Console.WriteLine(ProbeTargetFramework.Describe());
            string exe = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("приложение: " + exe + "  ("
                + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            try
            {
                if (mode == "ui")
                {
                    if (!SetHasWindows()) return 5;
                }
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();
                if (mode == "dose") Dose();
                else if (mode == "n42") N42();
                else Ui();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ОБВЯЗКА: обрыв: " + ex);
                return 5;
            }
            Console.WriteLine();
            Console.WriteLine(string.Format(Inv, "ИТОГ: дефектов {0}, отказов обвязки {1}", defects, rig));
            if (rig > 0) return 5;
            return defects > 0 ? 4 : 0;
        }

        static void Line(string item, string what, string value, string verdict)
        {
            if (verdict == "ДЕФЕКТ") defects++;
            if (verdict == "ОБВЯЗКА") rig++;
            Console.WriteLine(item + "\t" + what + "\t" + value + "\t" + verdict);
        }

        static void Judge(string item, string what, string value, bool clean)
        {
            Line(item, what, value, clean ? "ЧИСТО" : "ДЕФЕКТ");
        }

        static string R(double v)
        {
            return v.ToString("R", Inv);
        }

        // ================================================================== доза

        static IDictionary ComptonCache()
        {
            FieldInfo f = typeof(DoseRateCoefficients).GetField("comptonCache", BindingFlags.Static | BindingFlags.NonPublic);
            return f != null ? f.GetValue(null) as IDictionary : null;
        }

        static int CacheCount()
        {
            IDictionary d = ComptonCache();
            if (d == null) return -1;
            lock (d) return d.Count;
        }

        static double[] FixedEnergies()
        {
            var list = new List<double>();
            for (int i = 0; i < 40; i++) list.Add(10.0 * Math.Pow(300.0, i / 39.0));
            list.AddRange(new[] { 59.5409, 122.06065, 661.657, 1460.82, 2614.511 });
            return list.ToArray();
        }

        /// <summary>Биты трёх ответов квадратуры и коэффициента перехода на каждой энергии.</summary>
        static long[] CoefficientBits(double[] energies)
        {
            var bits = new List<long>();
            foreach (double e in energies)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(DoseRateCoefficients.DoseRatePerFluenceRate(e)));
                bits.Add(BitConverter.DoubleToInt64Bits(DoseRateCoefficients.ComptonCrossSection(e)));
                bits.Add(BitConverter.DoubleToInt64Bits(DoseRateCoefficients.ComptonTransferFraction(e)));
            }
            return bits.ToArray();
        }

        static string Sha(IEnumerable<long> bits)
        {
            var bytes = new List<byte>();
            foreach (long b in bits) bytes.AddRange(BitConverter.GetBytes(b));
            using (SHA256 h = SHA256.Create())
            {
                return BitConverter.ToString(h.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        static void Flood(int n, double start, double step)
        {
            for (int i = 0; i < n; i++) DoseRateCoefficients.DoseRatePerFluenceRate(start + i * step);
        }

        static void Dose()
        {
            Console.WriteLine("=== доза: кэш Клейна — Нишины (П315 п. 7) ===");
            if (ComptonCache() == null)
            {
                Line("доза", "поле comptonCache", "нет — кэш переименован", "ОБВЯЗКА");
                return;
            }
            double[] fixedE = FixedEnergies();
            long[] first = CoefficientBits(fixedE);
            string shaFirst = Sha(first);
            Line("доза", "коэффициенты на 45 энергиях, свежий кэш", "sha " + shaFirst + " (сверять между сборками)", "КОНТРОЛЬ");
            int before = CacheCount();
            // Представительные энергии диапазонов при наборе плывут со спектром: почти каждый
            // такт — новые ключи (П315: ≈ 9.5 новых записей на такт 200 мс).
            Flood(20000, 600.0, 0.000731);
            int after = CacheCount();
            Judge("доза", "записей в кэше: до / после 20 000 плывущих энергий",
                  before.ToString(Inv) + " / " + after.ToString(Inv), after <= 8192);
            long[] second = CoefficientBits(fixedE);
            bool same = first.SequenceEqual(second);
            Judge("доза", "коэффициенты после переполнения — побитово те же", same ? "совпали (sha " + Sha(second) + ")" : "РАЗОШЛИСЬ", same);

            // 8 потоков разом, ключи плывут: отказов нет, ответы побитово те же, что поодиночке.
            int n = 40000;
            var parallel = new double[n];
            Exception thrown = null;
            try
            {
                Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
                {
                    parallel[i] = DoseRateCoefficients.DoseRatePerFluenceRate(700.0 + i * 0.00071);
                });
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            int mismatch = 0;
            for (int i = 0; i < n; i += 97)
            {
                double one = DoseRateCoefficients.DoseRatePerFluenceRate(700.0 + i * 0.00071);
                if (BitConverter.DoubleToInt64Bits(one) != BitConverter.DoubleToInt64Bits(parallel[i])) mismatch++;
            }
            Judge("доза", "8 потоков × 40 000 энергий: отказ / расхождений с одиночным счётом (из 413)",
                  (thrown == null ? "нет" : thrown.GetType().Name) + " / " + mismatch.ToString(Inv) + "; записей после — " + CacheCount().ToString(Inv),
                  thrown == null && mismatch == 0);

            Console.WriteLine("=== доза: мощность дозы на спектрах корпуса ===");
            string[] names = { "AS80_Th232WT20.xml", "AS80_Cs137_0cm.xml", "AS80_Am241.xml", "ASN16_Cs137.xml", "ASN16_Lu176.xml", "AS80_Lu176.xml" };
            var manager = new DoseRateManager(GlobalConfigManager.GetInstance());
            int numbers = 0;
            foreach (string name in names)
            {
                string path = Path.Combine(corpus, name);
                ResultData rd = Load(path);
                if (rd == null) { Line("доза", name, "не прочитан", "ОБВЯЗКА"); continue; }
                DoseRate d1 = manager.Calculate(rd);
                string s1 = DoseSignature(d1);
                Flood(10000, 900.0 + name.Length, 0.000913);
                DoseRate d2 = manager.Calculate(rd);
                string s2 = DoseSignature(d2);
                if (d1 != null && string.IsNullOrEmpty(d1.Refusal)) numbers++;
                Line("доза", name, s1, "КОНТРОЛЬ");
                Judge("доза", name + ": после переполнения кэша — побитово то же", s1 == s2 ? "совпало" : "РАЗОШЛОСЬ: " + s2, s1 == s2);
            }
            if (numbers == 0) Line("доза", "спектров с числом дозы", "0 — сверять нечего", "ОБВЯЗКА");
        }

        static string DoseSignature(DoseRate d)
        {
            if (d == null) return "строки нет (кривая не выбрана)";
            if (!string.IsNullOrEmpty(d.Refusal)) return "отказ: " + d.Refusal;
            var bits = new List<long>
            {
                BitConverter.DoubleToInt64Bits(d.Rate),
                BitConverter.DoubleToInt64Bits(d.Error),
                BitConverter.DoubleToInt64Bits(d.Coverage),
            };
            foreach (DoseRateRange r in d.Ranges)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(r.RepresentativeKev));
                bits.Add(BitConverter.DoubleToInt64Bits(r.DoseRatePerFluenceRate));
                bits.Add(BitConverter.DoubleToInt64Bits(r.DoseRate));
            }
            return "Ḣ " + R(d.Rate) + " мкЗв/ч, σ " + R(d.Error) + ", диапазонов " + d.Ranges.Count.ToString(Inv) + ", sha " + Sha(bits);
        }

        static ResultData Load(string path)
        {
            if (!File.Exists(path)) return null;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            var pcal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pcal != null) pcal.CheckCalibration(rd.EnergySpectrum.NumberOfChannels);
            ProbeDeviceConfig.Attach(rd);
            rd.FileEfficiency = rd.Efficiency;
            return rd;
        }

        // ================================================================== N42

        static void N42()
        {
            Console.WriteLine("=== N42: импорт при «импортировать с пустой конфигурацией» (П311) ===");
            DocumentManager dm = DocumentManager.GetInstance();
            GlobalConfigInfo gc = GlobalConfigManager.GetInstance().GlobalConfig;
            bool keep = gc.ImportSpectrumWithEmptyConfig;
            var err = new StringWriter();
            TextWriter oldErr = Console.Error;
            Console.SetError(err);
            try
            {
                foreach (bool empty in new[] { true, false })
                {
                    gc.ImportSpectrumWithEmptyConfig = empty;
                    string tag = empty ? "пустая конфигурация" : "без настройки (контроль)";
                    N42Case(dm, tag, "2012 без RadMeasurement", "c2012_nomeas.n42",
                            Regex.Replace(Rad2012(Counts(64)), "  <RadMeasurement[\\s\\S]*</RadMeasurement>\\r\\n", ""),
                            "refuse", "импортируемых измерений в файле нет", empty);
                    N42Case(dm, tag, "2012 пустой ChannelData", "c2012_empty.n42", Rad2012(""), "refuse", "спектр пуст", empty);
                    N42Case(dm, tag, "2006 пустой ChannelData", "c2006_empty.n42", N42_2006(""), "refuse", "спектр пуст", empty);
                    N42Case(dm, tag, "Alpha Hound NumberOfChannels=0", "crad_zero.n42",
                            Rad(1024).Replace("NumberOfChannels=\"1024\"", "NumberOfChannels=\"0\""), "refuse", "спектр пуст", empty);
                    N42Case(dm, tag, "2012 здоровый (контроль)", "c2012_ok.n42", Rad2012(Counts(64)), "import", null, empty);
                    N42Case(dm, tag, "2006 здоровый (контроль)", "c2006_ok.n42", N42_2006(Counts(64)), "import", null, empty);
                    N42Case(dm, tag, "Alpha Hound здоровый (контроль)", "crad_ok.n42", Rad(1024), "import", null, empty);
                    // Находка по ходу (П319): CSV с одной шапкой — та же беда, что пустой ChannelData N42.
                    CsvCase(dm, tag, "CSV каналов: одна шапка", "csv_header_only.csv",
                            "Channel,Counts (TotalTime=3600.3s)\r\n", false, "refuse");
                    CsvCase(dm, tag, "CSV каналов: 64 строки (контроль)", "csv_ok.csv",
                            "Channel,Counts (TotalTime=3600.3s)\r\n" + CsvRows(64, false), false, "import");
                    CsvCase(dm, tag, "CSV энергий: одна шапка", "csve_header_only.csv",
                            "Energy,Count #0d1h0m0s\r\n", true, "refuse");
                    CsvCase(dm, tag, "CSV энергий: 64 строки (контроль)", "csve_ok.csv",
                            "Energy,Count #0d1h0m0s\r\n" + CsvRows(64, true), true, "import");
                }
            }
            finally
            {
                gc.ImportSpectrumWithEmptyConfig = keep;
                Console.SetError(oldErr);
            }
            Console.WriteLine("голоса (поток ошибок): " + err.ToString().Replace("\r", "").Replace("\n", " | "));
        }

        /// <summary>
        /// Один вход N42 в документ со СВОИМ спектром (100 каналов по 9). Судится: исход против
        /// ожидания; у отказа — причина названа (а не NullReferenceException) и документ не
        /// остался со спектром из НУЛЯ каналов (сброс настройки под пустой файл).
        /// </summary>
        static void N42Case(DocumentManager dm, string tag, string title, string name, string content,
                            string expect, string reasonHas, bool judge)
        {
            string path = Path.Combine(tmp, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            DocEnergySpectrum d = dm.CreateDocument();
            var es = new EnergySpectrum(1.0, 100);
            for (int i = 0; i < 100; i++) es.Spectrum[i] = 9;
            es.TotalPulseCount = 900;
            es.ValidPulseCount = 900;
            es.EnergyCalibration = new PolynomialEnergyCalibration();
            d.ActiveResultData.EnergySpectrum = es;
            string outcome;
            string reason = "";
            bool refused = false;
            try
            {
                dm.ImportDocumentN42(d, path);
                outcome = "импортирован";
            }
            catch (Exception ex)
            {
                refused = true;
                reason = Short(ex);
                outcome = "ОТКАЗ: " + reason;
            }
            EnergySpectrum now = d.ActiveResultData.EnergySpectrum;
            int channels = now != null ? now.NumberOfChannels : -1;
            var why = new List<string>();
            if (expect == "refuse")
            {
                if (!refused) why.Add("ждали отказ");
                else if (reason.IndexOf(reasonHas, StringComparison.Ordinal) < 0) why.Add("причина не названа («" + reasonHas + "» нет)");
                if (channels <= 0) why.Add("после отказа документ — спектр из " + channels.ToString(Inv) + " каналов");
            }
            else
            {
                if (refused) why.Add("ждали импорт");
                else if (channels <= 0) why.Add("импортирован спектр из нуля каналов");
            }
            string value = string.Format(Inv, "{0}; документ — каналов {1}", outcome, channels);
            if (judge) Judge("N42 " + tag, title, value + (why.Count == 0 ? "" : " — " + string.Join("; ", why)), why.Count == 0);
            else Line("N42 " + tag, title, value + (why.Count == 0 ? "" : " — " + string.Join("; ", why)), "КОНТРОЛЬ");
        }

        static string CsvRows(int n, bool energy)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string x = energy ? (5.0 + 2.5 * i).ToString("0.###", Inv) : i.ToString(Inv);
                sb.Append(x).Append(',').Append(((i % 7) + 1).ToString(Inv)).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Импорт CSV (дверь каналов или энергий) в документ со СВОИМ спектром (100 каналов по 9).
        /// Файл без строк данных: ждём отказ, документ не тронут (100 каналов, сумма 900).
        /// </summary>
        static void CsvCase(DocumentManager dm, string tag, string title, string name, string content, bool energy, string expect)
        {
            string path = Path.Combine(tmp, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            DocEnergySpectrum d = dm.CreateDocument();
            var es = new EnergySpectrum(1.0, 100);
            for (int i = 0; i < 100; i++) es.Spectrum[i] = 9;
            es.TotalPulseCount = 900;
            es.ValidPulseCount = 900;
            es.EnergyCalibration = new PolynomialEnergyCalibration();
            d.ActiveResultData.EnergySpectrum = es;
            string outcome;
            bool refused = false;
            try
            {
                if (energy) dm.ImportCsvEnergyToDocument(d, 3600, path);
                else dm.ImportCsvToDocument(d, 3600, path);
                outcome = "импортирован";
            }
            catch (Exception ex)
            {
                refused = true;
                outcome = "ОТКАЗ: " + Short(ex);
            }
            EnergySpectrum now = d.ActiveResultData.EnergySpectrum;
            int channels = now != null ? now.NumberOfChannels : -1;
            long sum = 0;
            for (int i = 0; now != null && i < now.NumberOfChannels && i < now.Spectrum.Length; i++) sum += now.Spectrum[i];
            var why = new List<string>();
            if (expect == "refuse")
            {
                if (!refused) why.Add("ждали отказ: файл без строк данных выдан за спектр");
                if (channels != 100 || sum != 900) why.Add("документ испорчен: каналов " + channels.ToString(Inv) + ", сумма " + sum.ToString(Inv));
            }
            else
            {
                if (refused) why.Add("ждали импорт");
                else if (channels <= 0) why.Add("импортирован спектр из нуля каналов");
            }
            string value = string.Format(Inv, "{0}; документ — каналов {1}, сумма {2}", outcome.Length > 260 ? outcome.Substring(0, 260) + "…" : outcome, channels, sum);
            Judge("CSV " + tag, title, value + (why.Count == 0 ? "" : " — " + string.Join("; ", why)), why.Count == 0);
        }

        static string Short(Exception ex)
        {
            Exception e = ex;
            var parts = new List<string>();
            while (e != null)
            {
                parts.Add(e.GetType().Name + ": " + e.Message);
                e = e.InnerException;
            }
            return string.Join(" ← ", parts).Replace("\r", " ").Replace("\n", " ");
        }

        static string Counts(int n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(((i % 7) + 1).ToString(Inv));
            }
            return sb.ToString();
        }

        static string N42_2006(string counts)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<N42InstrumentData xmlns=\"" + BecquerelMonitor.N42.N42InstrumentData.Ns + "\">\r\n"
                 + "  <Measurement>\r\n"
                 + "    <InstrumentInformation>\r\n"
                 + "      <InstrumentType>Spectrometer</InstrumentType>\r\n"
                 + "      <Manufacturer>PROBE</Manufacturer>\r\n"
                 + "      <InstrumentModel>P319</InstrumentModel>\r\n"
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
                 + "      <Coefficients>3.5 0.5</Coefficients>\r\n"
                 + "    </Equation>\r\n"
                 + "  </Calibration>\r\n"
                 + "</N42InstrumentData>\r\n";
        }

        static string Rad(int n)
        {
            var energies = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                double e = 5.0 + 0.5 * i + 1e-6 * i * i + 1e-11 * i * i * i + 1e-14 * i * i * i * i;
                if (i > 0) energies.Append(' ');
                energies.Append(e.ToString("0.######", Inv));
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadiologicalInstrumentData xmlns=\"http://physics.nist.gov/N42/2006/N42\">\r\n"
                 + "  <MeasurementGroup>\r\n"
                 + "    <Measurement>\r\n"
                 + "      <Spectrum>\r\n"
                 + "        <InstrumentInformation>\r\n"
                 + "          <Manufacturer>PROBE</Manufacturer>\r\n"
                 + "        </InstrumentInformation>\r\n"
                 + "        <EnergyCalibration>\r\n"
                 + "          <CalibrationEquation>List</CalibrationEquation>\r\n"
                 + "          <ChannelEnergies>" + energies + "</ChannelEnergies>\r\n"
                 + "        </EnergyCalibration>\r\n"
                 + "        <ChannelData NumberOfChannels=\"" + n.ToString(Inv) + "\">" + Counts(n) + "</ChannelData>\r\n"
                 + "        <LiveTime>295</LiveTime>\r\n"
                 + "      </Spectrum>\r\n"
                 + "    </Measurement>\r\n"
                 + "  </MeasurementGroup>\r\n"
                 + "</RadiologicalInstrumentData>\r\n";
        }

        static string Rad2012(string counts)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\" n42DocUUID=\"p319\">\r\n"
                 + "  <RadInstrumentInformation id=\"RadInstrument\">\r\n"
                 + "    <RadInstrumentManufacturerName>PROBE</RadInstrumentManufacturerName>\r\n"
                 + "    <RadInstrumentModelName>P319</RadInstrumentModelName>\r\n"
                 + "    <RadInstrumentClassCode>Radionuclide Identifier</RadInstrumentClassCode>\r\n"
                 + "    <RadInstrumentVersion>\r\n"
                 + "      <RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\r\n"
                 + "      <RadInstrumentComponentVersion>1</RadInstrumentComponentVersion>\r\n"
                 + "    </RadInstrumentVersion>\r\n"
                 + "  </RadInstrumentInformation>\r\n"
                 + "  <EnergyCalibration id=\"EC1\">\r\n"
                 + "    <CoefficientValues>3.5 12.5</CoefficientValues>\r\n"
                 + "  </EnergyCalibration>\r\n"
                 + "  <RadMeasurement id=\"M1\">\r\n"
                 + "    <MeasurementClassCode>Foreground</MeasurementClassCode>\r\n"
                 + "    <StartDateTime>2026-09-05T12:00:00Z</StartDateTime>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S1\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n"
                 + "</RadInstrumentData>\r\n";
        }

        // ================================================================== окно

        sealed class QuietMainForm : MainForm
        {
            public QuietMainForm() : base(new string[0]) { }
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        /// <summary>
        /// Признак окон приложения — в true, ДО первого обращения к менеджерам: без окон отказ
        /// <c>AppUi.Report</c> бросает исключение, а человек видит окно. `static readonly` —
        /// поэтому значение ЧИТАЕТСЯ ОБРАТНО (у `ModalThreadProbeO25` `FieldInfo.SetValue` на нём
        /// промахивался молча), запасной путь — `stsfld` из DynamicMethod.
        /// </summary>
        static bool SetHasWindows()
        {
            FieldInfo flag = typeof(AppUi).GetField("hasWindows", BindingFlags.NonPublic | BindingFlags.Static);
            if (flag == null)
            {
                Line("окно", "поле AppUi.hasWindows", "нет — признак переименован", "ОБВЯЗКА");
                return false;
            }
            try { flag.SetValue(null, true); } catch (Exception) { }
            if (!(bool)flag.GetValue(null))
            {
                var dmth = new DynamicMethod("P319_SetHasWindows", null, new[] { typeof(bool) }, typeof(AppUi).Module, true);
                ILGenerator il = dmth.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, flag);
                il.Emit(OpCodes.Ret);
                ((Action<bool>)dmth.CreateDelegate(typeof(Action<bool>)))(true);
            }
            bool ok = (bool)flag.GetValue(null) && AppUi.HasWindows;
            Line("окно", "признак окон AppUi.HasWindows", ok ? "true" : "НЕ ВЫСТАВЛЕН", ok ? "КОНТРОЛЬ" : "ОБВЯЗКА");
            return ok;
        }

        static object FieldValue(object owner, string name)
        {
            for (Type t = owner.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any);
                if (f != null) return f.GetValue(owner);
            }
            throw new MissingFieldException(owner.GetType().Name, name);
        }

        static void Pump(int ms)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        static QuietMainForm ShowMainForm()
        {
            var mf = new QuietMainForm();
            mf.StartPosition = FormStartPosition.Manual;
            mf.ShowInTaskbar = false;
            mf.Opacity = 0.0;
            mf.Show();
            Pump(300);
            mf.WindowState = FormWindowState.Normal;
            mf.Location = new Point(-6000, -4000);
            mf.Size = new Size(1400, 900);
            mf.Opacity = 1.0;
            Pump(300);
            var timer = FieldValue(mf, "timer") as System.Windows.Forms.Timer;
            if (timer == null) return null;
            timer.Stop();
            return mf;
        }

        static DocEnergySpectrum OpenInMainForm(MainForm mf, string path)
        {
            MethodInfo open = typeof(MainForm).GetMethod("OpenExistingDocument", Any, null, new[] { typeof(string) }, null);
            int before = DocumentManager.GetInstance().DocumentList.Count;
            open.Invoke(mf, new object[] { path });
            Pump(200);
            List<DocEnergySpectrum> list = DocumentManager.GetInstance().DocumentList;
            if (list.Count != before + 1) return null;
            DocEnergySpectrum d = list[list.Count - 1];
            if (mf.ActiveDocument != d)
            {
                d.Activate();
                Pump(50);
                if (mf.ActiveDocument != d) mf.ActiveDocument = d;
            }
            return d;
        }

        static void CloseExtraDocuments(MainForm mf, DocEnergySpectrum keep)
        {
            foreach (DocEnergySpectrum d in DocumentManager.GetInstance().DocumentList.ToArray())
            {
                if (d == keep) continue;
                d.Dirty = false;
                mf.CloseDocument(d.Filename, true);
            }
            Pump(100);
            if (keep != null && mf.ActiveDocument != keep)
            {
                keep.Activate();
                Pump(50);
                if (mf.ActiveDocument != keep) mf.ActiveDocument = keep;
            }
        }

        static void CloseMainForm(MainForm mf)
        {
            // НЕ Close(): FormClosing пишет раскладку и конфиг. Dispose гасит окно без записи.
            foreach (DocEnergySpectrum d in DocumentManager.GetInstance().DocumentList.ToArray())
            {
                d.Dirty = false;
                mf.CloseDocument(d.Filename, true);
            }
            mf.Hide();
            mf.Dispose();
        }

        /// <summary>
        /// Таймер потока окон: дождаться диалога WinForms нужного типа (он модален — такт
        /// таймера идёт внутри его цикла сообщений), заполнить и нажать.
        /// </summary>
        static void Arm(string typeName, Action<Form> act)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            int ticks = 0;
            timer.Tick += (s, e) =>
            {
                ticks++;
                foreach (Form f in Application.OpenForms)
                {
                    if (f.GetType().Name == typeName && f.Visible)
                    {
                        timer.Stop();
                        timer.Dispose();
                        act(f);
                        return;
                    }
                }
                if (ticks > 100)
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// «Ввод» диалога ввода. Если окно после него ОСТАЛОСЬ открытым (с П322 «Изменить число
        /// каналов» проверяет число в самом окне и при отказе не закрывается), — «Закрыть»
        /// (button2): иначе ShowDialog не вернётся. Отказ при этом уже показан окном и записан
        /// автоответчиком.
        /// </summary>
        static void Accept(Form f)
        {
            dialogStayedOpen = false;
            dialogSent = int.MinValue;
            ((Button)FieldValue(f, "button1")).PerformClick();
            // ⚠ Модальное окно после Close() остаётся Visible до выхода из своего цикла (он ещё
            // в этом такте таймера) — признак «закрывается» здесь DialogResult ≠ None.
            if (!f.IsDisposed && f.Visible && f.DialogResult == DialogResult.None)
            {
                dialogStayedOpen = true;
                Console.WriteLine("окно\t" + f.GetType().Name + ": после «Ввод» осталось открытым — отказ в самом окне, «Закрыть»\t\tКОНТРОЛЬ");
                ((Button)FieldValue(f, "button2")).PerformClick();
            }
            // (П325) То, что окно отдаст MainForm, — его же SendData, тем вызовом, каким его
            // читает `ConcatSpectrumsStripMenuItem_Click` (окно ещё не освобождено: `using`
            // вызывающего снимает его после возврата ShowDialog).
            if (f is ChanNumberChangeDialog) dialogSent = ((ChanNumberChangeDialog)f).SendData();
        }

        /// <summary>(П325) Окно ввода после «Ввод» осталось открытым (отказ в самом окне).</summary>
        static bool dialogStayedOpen;

        /// <summary>(П325) Что окно «Изменить число каналов» отдало MainForm (SendData после закрытия).</summary>
        static int dialogSent = int.MinValue;

        /// <summary>
        /// (П325) Одно число в «Изменить число каналов» — настоящим пунктом меню. Возвращает
        /// окна, увиденные автоответчиком, и число новых документов; печатает, что отдало окно.
        /// </summary>
        static List<string> ChanCase(QuietMainForm mf, string text, out int newDocs, out string thrown, out DocEnergySpectrum made)
        {
            int docs0 = DocumentManager.GetInstance().DocumentList.Count;
            Arm("ChanNumberChangeDialog", f =>
            {
                ((TextBox)FieldValue(f, "textBox1")).Text = text;
                Accept(f);
            });
            Responder.Take();
            thrown = "нет";
            try
            {
                ClickItem(mf, "ConcatSpectrumsStripMenuItem");
            }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                thrown = e.GetType().Name + ": " + e.Message;
            }
            Pump(600);
            List<string> seen = Responder.Take();
            int docs1 = DocumentManager.GetInstance().DocumentList.Count;
            newDocs = docs1 - docs0;
            made = newDocs > 0 ? DocumentManager.GetInstance().DocumentList[docs1 - 1] : null;
            return seen;
        }

        /// <summary>
        /// (П325, решение Amber 15.10.2026 «64…65536 (Рекомендую)») Отказ «Изменить число
        /// каналов» обязан прийти из САМОГО окна: после «Ввод» окно открыто, окно отказа одно и
        /// с заголовком отказа окна (`InvalidValueDialogTitle`, не «Ошибка» меню), после
        /// «Закрыть» окно отдаёт 0 — MainForm выходит на `newChan == 0`, до своей проверки и
        /// до `ConcatSpectrums` число не доходит; исключений и новых документов нет.
        /// </summary>
        static void JudgeDialogRefusal(string item, string text, List<string> seen, int newDocs, string thrown)
        {
            string want = "[" + BecquerelMonitor.Properties.Resources.InvalidValueDialogTitle + "]";
            bool fromDialog = seen.Count == 1 && seen[0].StartsWith(want, StringComparison.Ordinal);
            string value = string.Format(Inv, "окно после «Ввод»: {0}; отдало MainForm: {1}; окна: {2}; новых документов {3}; исключение: {4}",
                dialogStayedOpen ? "открыто" : "ЗАКРЫЛОСЬ",
                dialogSent == int.MinValue ? "не прочитано" : dialogSent.ToString(Inv),
                Caption(seen), newDocs, thrown);
            Judge(item, "«Изменить число каналов» " + text + ": отказ из самого окна, до MainForm не дошло", value,
                  dialogStayedOpen && dialogSent == 0 && fromDialog && newDocs == 0 && thrown == "нет");
        }

        static void ClickItem(MainForm mf, string field)
        {
            var item = (ToolStripItem)FieldValue(mf, field);
            item.Enabled = true;
            item.PerformClick();
            Pump(300);
        }

        static string Caption(List<string> seen)
        {
            return seen.Count == 0 ? "<окна не было>" : string.Join(" ‖ ", seen);
        }

        static long Sum(EnergySpectrum es)
        {
            long s = 0;
            for (int i = 0; i < es.NumberOfChannels && i < es.Spectrum.Length; i++) s += es.Spectrum[i];
            return s;
        }

        static void Ui()
        {
            Application.EnableVisualStyles();
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            string work = Path.Combine(tmp, "ui");
            Directory.CreateDirectory(work);
            string src = Path.Combine(corpus, "AS80_Th232WT20.xml");
            string other = Path.Combine(corpus, "ASN8_Th232_1024.xml");
            if (!File.Exists(src) || !File.Exists(other))
            {
                Line("окно", "спектры корпуса", "нет AS80_Th232WT20.xml / ASN8_Th232_1024.xml", "ОБВЯЗКА");
                return;
            }
            string main = Path.Combine(work, "p319_main.xml");
            File.Copy(src, main, true);
            string same = Path.Combine(work, "p319_same8192.xml");
            File.Copy(src, same, true);
            string small = Path.Combine(work, "p319_other1024.xml");
            File.Copy(other, small, true);

            Responder.Start();
            QuietMainForm mf = ShowMainForm();
            if (mf == null) { Line("окно", "MainForm", "Load не прошёл (нет таймера)", "ОБВЯЗКА"); Responder.Stop(); return; }
            Responder.Take();
            DocEnergySpectrum doc = OpenInMainForm(mf, main);
            if (doc == null) { Line("окно", "документ", "не открылся", "ОБВЯЗКА"); CloseMainForm(mf); Responder.Stop(); return; }
            Line("окно", "документ открыт", doc.Text + "; окна при открытии: " + Caption(Responder.Take()), "КОНТРОЛЬ");
            ResultData rd = doc.ActiveResultData;
            doc.Dirty = false;
            Pump(100);

            // ---------------------------------------------------------------- Н16
            DCControlPanel panel = mf.ControlPanel;
            var limit = (TextBox)FieldValue(panel, "realTimeLimitTextBox");
            MethodInfo keyDown = typeof(DCControlPanel).GetMethod("textBox7_KeyDown", Any);
            int preset = rd.ResultDataStatus.PresetTime;
            Line("Н16", "предел в документе: ResultDataStatus.PresetTime / ResultData.PresetTime / поле панели",
                 preset.ToString(Inv) + " / " + rd.PresetTime.ToString(Inv) + " / " + limit.Text, "КОНТРОЛЬ");
            limit.Text = preset.ToString(Inv);
            doc.Dirty = false;
            keyDown.Invoke(panel, new object[] { limit, new KeyEventArgs(Keys.Return) });
            Judge("Н16", "Enter в пределе времени с тем же значением " + preset.ToString(Inv) + ": документ изменён?",
                  doc.Dirty ? "да («*» во вкладке: " + doc.Text + ")" : "нет", !doc.Dirty);
            limit.Text = (preset + 1).ToString(Inv);
            doc.Dirty = false;
            keyDown.Invoke(panel, new object[] { limit, new KeyEventArgs(Keys.Return) });
            Line("Н16", "контроль: другое значение " + (preset + 1).ToString(Inv) + " — документ изменён?",
                 doc.Dirty ? "да" : "НЕТ", doc.Dirty && rd.ResultDataStatus.PresetTime == preset + 1 ? "КОНТРОЛЬ" : "ОБВЯЗКА");
            limit.Text = preset.ToString(Inv);
            keyDown.Invoke(panel, new object[] { limit, new KeyEventArgs(Keys.Return) });
            doc.Dirty = false;

            // ---------------------------------------------------------------- Н6 (предел −5)
            limit.Text = "-5";
            Responder.Take();
            panel.StartMeasurement();
            Pump(600);
            List<string> seen = Responder.Take();
            Judge("Н6", "«Старт» при пределе −5: заголовок окна отказа", Caption(seen), seen.Count == 1 && !seen[0].StartsWith("[]", StringComparison.Ordinal));
            limit.Text = preset.ToString(Inv);
            rd.ResultDataStatus.PresetTime = preset;
            rd.PresetTime = preset;
            doc.Dirty = false;

            // ---------------------------------------------------------------- Н6 (каналы −5)
            // (П325) С П322 (решение Amber 15.10.2026 «64…65536 (Рекомендую)») −5 и 2000000000
            // отказывает само окно ввода, а не MainForm после его закрытия: мерится, ЧТО отказало
            // (окно открыто, заголовок отказа окна, SendData = 0), а не только что заголовок не пуст.
            int newDocs;
            string thrown;
            DocEnergySpectrum made;
            seen = ChanCase(mf, "-5", out newDocs, out thrown, out made);
            JudgeDialogRefusal("Н6", "−5", seen, newDocs, thrown);
            Line("Н6", "заголовок окна отказа «Изменить число каналов» −5 (П319: не пуст)", Caption(seen),
                 seen.Count == 1 && !seen[0].StartsWith("[]", StringComparison.Ordinal) ? "КОНТРОЛЬ" : "ОБВЯЗКА");
            CloseExtraDocuments(mf, doc);

            // ---------------------------------------------------------------- Н5
            seen = ChanCase(mf, "2000000000", out newDocs, out thrown, out made);
            JudgeDialogRefusal("Н5", "2000000000", seen, newDocs, thrown);
            CloseExtraDocuments(mf, doc);

            // ---------------------------------------------------------------- П325: границы окна = границы меню
            // 63 и 65537 — за границами `ChannelCountLimits`: отказ из окна, как у −5.
            foreach (string outside in new[] { "63", "65537" })
            {
                seen = ChanCase(mf, outside, out newDocs, out thrown, out made);
                JudgeDialogRefusal("П325", outside, seen, newDocs, thrown);
                CloseExtraDocuments(mf, doc);
            }
            // 64 — нижняя граница: окно пропускает, и меню обязано создать документ (прежнее
            // `newChan > 64` в MainForm отвечало на неё «Ошибка»).
            seen = ChanCase(mf, "64", out newDocs, out thrown, out made);
            bool made64 = made != null && newDocs == 1 && made.ActiveResultData.EnergySpectrum.NumberOfChannels == 64
                          && Sum(made.ActiveResultData.EnergySpectrum) == Sum(rd.EnergySpectrum);
            Judge("П325", "«Изменить число каналов» 64 (нижняя граница окна): новый документ, сумма та же, окон нет",
                  string.Format(Inv, "окно после «Ввод»: {0}; отдало MainForm: {1}; {2}; окна: {3}; исключение: {4}",
                      dialogStayedOpen ? "ОТКРЫТО" : "закрылось",
                      dialogSent == int.MinValue ? "не прочитано" : dialogSent.ToString(Inv),
                      made == null ? "документа НЕТ" : made.ActiveResultData.EnergySpectrum.NumberOfChannels.ToString(Inv) + " кан., сумма "
                          + Sum(made.ActiveResultData.EnergySpectrum).ToString(Inv) + " из " + Sum(rd.EnergySpectrum).ToString(Inv),
                      Caption(seen), thrown),
                  made64 && seen.Count == 0 && thrown == "нет");
            CloseExtraDocuments(mf, doc);

            // контроль: 4096 каналов — новый документ, без окна отказа
            seen = ChanCase(mf, "4096", out newDocs, out thrown, out made);
            bool madeOk = made != null && newDocs == 1 && made.ActiveResultData.EnergySpectrum.NumberOfChannels == 4096
                          && Sum(made.ActiveResultData.EnergySpectrum) == Sum(rd.EnergySpectrum);
            Line("Н5", "контроль: 4096 каналов — новый документ, сумма та же, окон нет",
                 (made == null ? "документа нет" : made.ActiveResultData.EnergySpectrum.NumberOfChannels.ToString(Inv) + " кан., сумма "
                  + Sum(made.ActiveResultData.EnergySpectrum).ToString(Inv) + " из " + Sum(rd.EnergySpectrum).ToString(Inv)) + "; окна: " + Caption(seen),
                 madeOk && seen.Count == 0 ? "КОНТРОЛЬ" : "ОБВЯЗКА");
            CloseExtraDocuments(mf, doc);

            // ---------------------------------------------------------------- Н6 (обрезка −5 кэВ)
            int docs3 = DocumentManager.GetInstance().DocumentList.Count;
            Arm("SpectrumCutOffDialog", f =>
            {
                ((RadioButton)FieldValue(f, "energyradioButton")).Checked = true;
                ((TextBox)FieldValue(f, "energytextBox")).Text = "-5";
                Accept(f);
            });
            Responder.Take();
            ClickItem(mf, "CutoffStripMenuItem");
            Pump(600);
            seen = Responder.Take();
            Judge("Н6", "«Ограничить энергию» −5 кэВ: заголовок окна отказа", Caption(seen),
                  seen.Count == 1 && !seen[0].StartsWith("[]", StringComparison.Ordinal) && DocumentManager.GetInstance().DocumentList.Count == docs3);
            CloseExtraDocuments(mf, doc);

            // ---------------------------------------------------------------- Н6 (экспорт N42)
            Responder.Take();
            DocumentManager.GetInstance().ExportDocumentN42(doc);
            Pump(600);
            seen = Responder.Take();
            // первое окно — предупреждение «в N42 уходит не всё», второе — окно сохранения (отменено)
            string warn = seen.Count > 0 ? seen[0] : "<окна не было>";
            bool warnSeen = seen.Count >= 2 && !seen[0].Contains("файл=");
            if (!warnSeen) Line("Н6", "экспорт N42: предупреждения нет (у спектра нет кривой?)", Caption(seen), "ОБВЯЗКА");
            else Judge("Н6", "экспорт N42: заголовок предупреждения «уходит не всё»", warn, !warn.StartsWith("[]", StringComparison.Ordinal));

            // ---------------------------------------------------------------- Н6 (отказ импорта)
            // Корень N42 узнаётся, тело битое: разбор падает ДО записи в документ, отказ — окном.
            string broken = Path.Combine(work, "p319_broken.n42");
            File.WriteAllText(broken, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\"><RadMeasurement id=\"M1\"><Spectrum", new UTF8Encoding(false));
            long sumBefore = Sum(rd.EnergySpectrum);
            Responder.Take();
            string importThrown = "нет";
            try
            {
                DocumentManager.GetInstance().ImportDocumentN42(doc, broken);
            }
            catch (Exception ex)
            {
                importThrown = ex.GetType().Name;
            }
            Pump(600);
            seen = Responder.Take();
            Judge("Н6", "отказ импорта N42 (битый файл): заголовок окна",
                  Caption(seen) + "; исключение мимо окна: " + importThrown + "; сумма документа " + (Sum(doc.ActiveResultData.EnergySpectrum) == sumBefore ? "та же" : "ИЗМЕНИЛАСЬ"),
                  importThrown == "нет" && seen.Count == 1 && !seen[0].StartsWith("[]", StringComparison.Ordinal));

            // ---------------------------------------------------------------- Н22
            doc.Dirty = true;
            Pump(50);
            string tab = doc.Text;
            Responder.Take();
            Responder.TakeFileDialogs();
            Responder.LastFileName = null;
            ClickItem(mf, "hardSubtractToolStripMenuItem");
            Pump(600);
            seen = Responder.Take();
            int fileDialogs = Responder.TakeFileDialogs();
            string file = Responder.LastFileName;
            if (string.IsNullOrEmpty(rd.BackgroundSpectrumFile) || rd.BackgroundEnergySpectrum == null)
            {
                Line("Н22", "у документа нет фона — путь не тот", Caption(seen), "ОБВЯЗКА");
            }
            else if (fileDialogs != 1 || file == null)
            {
                Line("Н22", "окно сохранения не найдено или поле имени не прочитано", Caption(seen), "ОБВЯЗКА");
            }
            else
            {
                Judge("Н22", "«Жёсткий вычет в файл» у изменённого («" + tab + "»): имя по умолчанию", "«" + file + "»",
                      file.IndexOf('*') < 0 && file.Length > 0);
            }
            doc.Dirty = false;

            // ---------------------------------------------------------------- П311 пустой выбор
            foreach (ResultData r in doc.ResultDataFile.ResultDataList) r.Selected = false;
            Responder.Take();
            Responder.TakeFileDialogs();
            ClickItem(mf, "exportToFileStripMenuItem");
            Pump(600);
            seen = Responder.Take();
            bool saveShown = Responder.TakeFileDialogs() > 0;
            Judge("П311", "«Экспорт спектра в файл» без выбранных: окно сохранения / отказ",
                  (saveShown ? "окно сохранения — дальше файл без спектров" : "окна сохранения нет") + "; окна: " + Caption(seen),
                  !saveShown && seen.Count == 1);
            foreach (ResultData r in doc.ResultDataFile.ResultDataList) r.Selected = r == rd;
            Responder.Take();
            ClickItem(mf, "exportToFileStripMenuItem");
            Pump(600);
            seen = Responder.Take();
            bool controlShown = Responder.TakeFileDialogs() > 0;
            Line("П311", "контроль: выбран активный — окно сохранения есть",
                 (controlShown ? "есть" : "НЕТ") + "; окна: " + Caption(seen),
                 controlShown ? "КОНТРОЛЬ" : "ОБВЯЗКА");

            // ---------------------------------------------------------------- Н18
            CombineCase(mf, doc, small, false);
            CombineCase(mf, doc, same, true);

            CloseMainForm(mf);
            Responder.Stop();
        }

        static void CombineCase(MainForm mf, DocEnergySpectrum doc, string path, bool control)
        {
            CloseExtraDocuments(mf, doc);
            EnergySpectrum es = doc.ActiveResultData.EnergySpectrum;
            long sum0 = Sum(es);
            doc.Dirty = false;
            Responder.Take();
            Responder.OpenPath = path;
            Responder.OpenedPath = null;
            string thrown = "нет";
            try
            {
                ClickItem(mf, "CombineSpectrasToolStripMenuItem");
            }
            catch (Exception ex)
            {
                Exception e = ex;
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                thrown = e.GetType().Name + ": " + e.Message;
            }
            Pump(800);
            List<string> seen = Responder.Take();
            long sum1 = Sum(doc.ActiveResultData.EnergySpectrum);
            string value = string.Format(Inv, "файл открыт окном: {0}; исключение: {1}; сумма {2} → {3}; изменён: {4}; окна: {5}",
                Responder.OpenedPath != null ? "да" : "НЕТ", thrown, sum0, sum1, doc.Dirty ? "да (" + doc.Text + ")" : "нет", Caption(seen));
            if (Responder.OpenedPath == null)
            {
                Line("Н18", Path.GetFileName(path), value, "ОБВЯЗКА");
            }
            else if (control)
            {
                Line("Н18", "контроль: тот же спектр (8192) — сложено и помечено", value,
                     doc.Dirty && sum1 == 2 * sum0 && thrown == "нет" ? "КОНТРОЛЬ" : "ОБВЯЗКА");
            }
            else
            {
                // окна: открытие файла и одно окно отказа «разное число каналов»
                int refusals = seen.Count(s => s.IndexOf("файл=", StringComparison.Ordinal) < 0);
                Judge("Н18", "объединение с 1024 каналами (отказ): документ изменён?", value,
                      !doc.Dirty && sum1 == sum0 && thrown == "нет" && refusals == 1);
            }
            doc.Dirty = false;
        }

        /// <summary>
        /// Поток-автоответчик окон приложения (класс #32770 своего процесса). Сообщение:
        /// записать заголовок и текст, нажать «Нет», затем «Отмена», затем «ОК». Окно
        /// сохранения: записать имя файла, нажать «Отмена». Окно открытия при заданном
        /// <see cref="OpenPath"/>: вписать путь, нажать «Открыть».
        /// </summary>
        static class Responder
        {
            delegate bool EnumProc(IntPtr h, IntPtr l);
            [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
            [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
            [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
            [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, StringBuilder l);
            [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);

            const uint WM_COMMAND = 0x0111, WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D;
            static volatile bool run;
            static Thread thread;
            static readonly Dictionary<IntPtr, int> tries = new Dictionary<IntPtr, int>();
            static readonly Dictionary<IntPtr, int> waits = new Dictionary<IntPtr, int>();
            static readonly List<string> seen = new List<string>();
            public static volatile string OpenPath;
            public static volatile string OpenedPath;
            public static volatile string LastFileName;
            static int fileDialogs;

            /// <summary>Окон файла (открытия/сохранения), встреченных с прошлого вызова; счёт обнуляется.</summary>
            public static int TakeFileDialogs()
            {
                return Interlocked.Exchange(ref fileDialogs, 0);
            }

            public static void Start()
            {
                run = true;
                thread = new Thread(Loop) { IsBackground = true };
                thread.Start();
            }

            public static void Stop()
            {
                run = false;
                if (thread != null) thread.Join(1000);
                thread = null;
            }

            public static List<string> Take()
            {
                lock (seen)
                {
                    var copy = new List<string>(seen);
                    seen.Clear();
                    return copy;
                }
            }

            static void Loop()
            {
                uint me = (uint)Process.GetCurrentProcess().Id;
                int[] answers = { 7, 2, 1 }; // IDNO, IDCANCEL, IDOK
                while (run)
                {
                    try
                    {
                        EnumWindows((h, l) =>
                        {
                            uint pid;
                            GetWindowThreadProcessId(h, out pid);
                            if (pid != me || !IsWindowVisible(h)) return true;
                            var cls = new StringBuilder(64);
                            GetClassName(h, cls, 64);
                            if (cls.ToString() != "#32770") return true;
                            Handle(h, answers);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch (Exception)
                    {
                    }
                    Thread.Sleep(120);
                }
            }

            static void Handle(IntPtr h, int[] answers)
            {
                int w;
                waits.TryGetValue(h, out w);
                List<IntPtr> kids = Children(h);
                bool fileDialog = kids.Any(k => { string c = Cls(k); return c == "DirectUIHWND" || c == "DUIViewWndClassName" || c == "ComboBoxEx32" || c == "SHELLDLL_DefView"; });
                IntPtr edit = FileNameEdit(kids);
                // окну дать дорисоваться: первые три прохода только смотрим
                if (w < 3 || (fileDialog && edit == IntPtr.Zero && w < 40))
                {
                    waits[h] = w + 1;
                    return;
                }
                int n;
                tries.TryGetValue(h, out n);
                if (fileDialog)
                {
                    if (n == 0)
                    {
                        string name = edit != IntPtr.Zero ? GetText(edit) : null;
                        lock (seen) seen.Add("[" + Title(h) + "] файл=«" + (name ?? "<поле имени не найдено: " + Dump(kids) + ">") + "»");
                        Interlocked.Increment(ref fileDialogs);
                        string open = OpenPath;
                        if (open != null && edit != IntPtr.Zero)
                        {
                            OpenPath = null;
                            SendMessage(edit, WM_SETTEXT, IntPtr.Zero, open);
                            OpenedPath = open;
                            PostMessage(h, WM_COMMAND, (IntPtr)1, IntPtr.Zero); // IDOK
                        }
                        else
                        {
                            LastFileName = name;
                            PostMessage(h, WM_COMMAND, (IntPtr)2, IntPtr.Zero); // IDCANCEL
                        }
                    }
                    else if (n > 5)
                    {
                        PostMessage(h, WM_COMMAND, (IntPtr)2, IntPtr.Zero);
                    }
                    tries[h] = n + 1;
                    return;
                }
                if (n == 0)
                {
                    lock (seen) seen.Add(Describe(h, kids));
                }
                PostMessage(h, WM_COMMAND, (IntPtr)answers[Math.Min(n, answers.Length - 1)], IntPtr.Zero);
                tries[h] = n + 1;
            }

            static List<IntPtr> Children(IntPtr h)
            {
                var list = new List<IntPtr>();
                EnumChildWindows(h, (c, l) => { list.Add(c); return true; }, IntPtr.Zero);
                return list;
            }

            static string Cls(IntPtr h)
            {
                var sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                return sb.ToString();
            }

            static string Title(IntPtr h)
            {
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, 512);
                return sb.ToString();
            }

            static string GetText(IntPtr h)
            {
                var sb = new StringBuilder(1024);
                SendMessage(h, WM_GETTEXT, (IntPtr)1024, sb);
                return sb.ToString();
            }

            /// <summary>
            /// Поле имени файла: у окна открытия — Edit внутри поля с номером 1148 (cmb13), у окна
            /// сохранения (новый вид Windows) — Edit с номером 1001 (найдено перечнем детей окна).
            /// </summary>
            static IntPtr FileNameEdit(List<IntPtr> kids)
            {
                foreach (IntPtr k in kids)
                {
                    if (GetDlgCtrlID(k) != 1148) continue;
                    if (Cls(k) == "Edit") return k;
                    foreach (IntPtr c in Children(k))
                    {
                        if (Cls(c) == "Edit") return c;
                    }
                }
                foreach (IntPtr k in kids)
                {
                    if (GetDlgCtrlID(k) == 1001 && Cls(k) == "Edit") return k;
                }
                return IntPtr.Zero;
            }

            static string Dump(List<IntPtr> kids)
            {
                var parts = new List<string>();
                foreach (IntPtr k in kids)
                {
                    string c = Cls(k);
                    if (c != "Edit" && c != "ComboBox" && c != "ComboBoxEx32") continue;
                    parts.Add(c + "#" + GetDlgCtrlID(k).ToString(CultureInfo.InvariantCulture) + "=" + GetText(k));
                }
                return string.Join("; ", parts);
            }

            static string Describe(IntPtr h, List<IntPtr> kids)
            {
                var all = new StringBuilder();
                all.Append("[" + Title(h) + "] ");
                foreach (IntPtr c in kids)
                {
                    if (Cls(c) != "Static") continue;
                    string s = Title(c);
                    if (s.Length > 0) all.Append(s.Replace("\r", " ").Replace("\n", " "));
                }
                string text = all.ToString().Trim();
                return text.Length > 220 ? text.Substring(0, 220) + "…" : text;
            }
        }
    }
}
