using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace N42PolynomialCentreProbe
{
    /// <summary>
    /// `AMBER73`, ВТОРАЯ ПОЛОВИНА (полоса П138, 22.09.2026) — ПОЛИНОМ
    /// `CoefficientValues`: КРАЯ КАНАЛОВ ИЛИ ИХ ЦЕНТРЫ.
    ///
    /// П137 закрыла первую половину (список границ `EnergyBoundaryValues`
    /// переложен на центры каналов в обеих дверях) и назвала цену оставшейся:
    /// ДВОЙНЯШКИ — одно измерение, записанное и границами, и полиномом, —
    /// разошлись на h/2, потому что границы стали читаться центрами, а полином
    /// остался «номер канала = центр».
    ///
    /// ⛔ ЧТО МЕРИТСЯ:
    ///
    /// 1. `--twins=<границы>|<полином>`  ДВОЙНЯШКИ. Оба файла ввозятся дверью
    ///    N42 и дверью SpecUtils, и сравниваются ШКАЛЫ: наибольшее
    ///    |E_границы(ch) − E_полином(ch)| по каналам и то же на канале 662 кэВ.
    ///    Это и есть мера захода: до правки — h/2, после — ноль.
    /// 2. `--scale=<файл>`  ЧТО ЧИТАЕТСЯ ИЗ ФАЙЛА: коэффициенты обеих дверей
    ///    форматом «R» и энергии пяти опорных каналов. Отсюда берётся таблица
    ///    «двигается ли шкала» для корпусных `.n42` и для файлов Amber; и отсюда
    ///    же — контроль неизменности: у файла, чья калибровка задана ГРАНИЦАМИ,
    ///    строки обязаны совпасть знак в знак до и после.
    /// 3. `--roundtrip=<файл>`  КРУГ ПРИЛОЖЕНИЯ: ввоз → `Util.ExportToN42` →
    ///    ввоз. Шкала обязана вернуться ТОЙ ЖЕ: ввоз и вывоз согласованы, иначе
    ///    наши собственные выгрузки уезжают на полканала. Печатается и сам
    ///    записанный `CoefficientValues` — по нему видно, чем пишет вывоз.
    ///
    /// Ожидания: `--expect-twins-kev=<x>` (наибольшее расхождение двойняшек не
    /// больше x), `--expect-roundtrip-kev=<x>`, `--expect-doors-kev=<x>`.
    /// Код возврата 1 — названное ожидание не сошлось. Проба безоконная.
    /// </summary>
    static class Program
    {
        static readonly List<string> twins = new List<string>();
        static readonly List<string> scales = new List<string>();
        static readonly List<string> roundTrips = new List<string>();
        static double expectTwinsKev = double.NaN;
        static double expectRoundTripKev = double.NaN;
        static double expectDoorsKev = double.NaN;
        static string dir = ".";
        static int bad = 0;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) dir = a.Substring(6);
                else if (a.StartsWith("--twins=")) twins.Add(a.Substring(8));
                else if (a.StartsWith("--scale=")) scales.Add(a.Substring(8));
                else if (a.StartsWith("--roundtrip=")) roundTrips.Add(a.Substring(12));
                else if (a.StartsWith("--expect-twins-kev=")) expectTwinsKev = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--expect-roundtrip-kev=")) expectRoundTripKev = double.Parse(a.Substring(23), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--expect-doors-kev=")) expectDoorsKev = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine();

            foreach (string t in twins) Twins(t);
            foreach (string s in scales) Scale(s);
            foreach (string r in roundTrips) RoundTrip(r);

            Console.WriteLine();
            if (bad > 0)
            {
                Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad.ToString(CultureInfo.InvariantCulture));
                return 1;
            }
            Console.WriteLine("ЗАМЕР СНЯТ.");
            return 0;
        }

        // ==================================================================
        // 1. ДВОЙНЯШКИ — ОДНО ИЗМЕРЕНИЕ ДВУМЯ СПОСОБАМИ
        // ==================================================================

        static void Twins(string spec)
        {
            string[] two = spec.Split('|');
            if (two.Length != 2) { Console.Error.WriteLine("--twins=<файл границ>|<файл полинома>"); bad++; return; }
            string fb = Path.Combine(dir, two[0]);
            string fc = Path.Combine(dir, two[1]);
            Console.WriteLine("=== 1. ДВОЙНЯШКИ: одно измерение границами и полиномом ===");
            Console.WriteLine("  границы: " + two[0]);
            Console.WriteLine("  полином: " + two[1]);
            foreach (bool n42Door in new bool[] { true, false })
            {
                string err;
                int chB, chC;
                PolynomialEnergyCalibration cb = Import(fb, n42Door, out chB, out err);
                if (cb == null) { Console.WriteLine("    " + Door(n42Door) + ": ОТКАЗ на файле границ — " + err); bad++; continue; }
                PolynomialEnergyCalibration cc = Import(fc, n42Door, out chC, out err);
                if (cc == null) { Console.WriteLine("    " + Door(n42Door) + ": ОТКАЗ на файле полинома — " + err); bad++; continue; }
                int n = Math.Min(chB, chC);
                double max = 0.0; int arg = 0;
                for (int ch = 0; ch < n; ch++)
                {
                    double d = Math.Abs(cb.ChannelToEnergy(ch) - cc.ChannelToEnergy(ch));
                    if (d > max) { max = d; arg = ch; }
                }
                int ch662 = NearestChannel(cb, n, 662.0);
                double at662 = Math.Abs(cb.ChannelToEnergy(ch662) - cc.ChannelToEnergy(ch662));
                double h = HalfStep(cb, ch662, n) * 2.0;
                Console.WriteLine("    " + Door(n42Door) + ": границы [" + Join(cb.Coefficients) + "]");
                Console.WriteLine("    " + Door(n42Door) + ": полином [" + Join(cc.Coefficients) + "]");
                Console.WriteLine("    " + Door(n42Door) + ": наибольшее расхождение ШКАЛ = " + F(max)
                                  + " кэВ (канал " + arg.ToString(CultureInfo.InvariantCulture) + ")");
                Console.WriteLine("    " + Door(n42Door) + ": на 662 кэВ (канал " + ch662.ToString(CultureInfo.InvariantCulture)
                                  + ") = " + F(at662) + " кэВ, при h = " + F(h) + " (h/2 = " + F(h / 2.0) + ")");
                if (!double.IsNaN(expectTwinsKev) && max > expectTwinsKev)
                {
                    Console.WriteLine("    ⛔ НЕ СОШЛОСЬ: ждали не больше " + F(expectTwinsKev) + " кэВ");
                    bad++;
                }
            }
            Console.WriteLine();
        }

        // ==================================================================
        // 2. ЧТО ЧИТАЕТСЯ ИЗ ФАЙЛА — ОБЕИМИ ДВЕРЬМИ
        // ==================================================================

        static void Scale(string name)
        {
            string f = Path.Combine(dir, name);
            Console.WriteLine("=== 2. ШКАЛА ФАЙЛА: " + name + " ===");
            int chA, chB;
            string errA, errB;
            PolynomialEnergyCalibration a = Import(f, true, out chA, out errA);
            PolynomialEnergyCalibration b = Import(f, false, out chB, out errB);
            if (a != null)
            {
                Console.WriteLine("  дверь N42:       каналов " + chA.ToString(CultureInfo.InvariantCulture)
                                  + ", порядок " + a.PolynomialOrder.ToString(CultureInfo.InvariantCulture)
                                  + " [" + Join(a.Coefficients) + "]");
                Console.WriteLine("  дверь N42: E(ch) " + Probe(a, chA));
            }
            else Console.WriteLine("  дверь N42: ОТКАЗ — " + errA);
            if (b != null)
            {
                Console.WriteLine("  дверь SpecUtils: каналов " + chB.ToString(CultureInfo.InvariantCulture)
                                  + ", порядок " + b.PolynomialOrder.ToString(CultureInfo.InvariantCulture)
                                  + " [" + Join(b.Coefficients) + "]");
                Console.WriteLine("  дверь SpecUtils: E(ch) " + Probe(b, chB));
            }
            else Console.WriteLine("  дверь SpecUtils: ОТКАЗ — " + errB);
            if (a != null && b != null)
            {
                int n = Math.Min(chA, chB);
                double max = 0.0;
                for (int ch = 0; ch < n; ch++)
                {
                    double d = Math.Abs(a.ChannelToEnergy(ch) - b.ChannelToEnergy(ch));
                    if (d > max) max = d;
                }
                Console.WriteLine("  ⛔ `A253` СХОЖДЕНИЕ ДВЕРЕЙ: " + F(max) + " кэВ");
                if (!double.IsNaN(expectDoorsKev) && max > expectDoorsKev)
                {
                    Console.WriteLine("  ⛔ НЕ СОШЛОСЬ: ждали не больше " + F(expectDoorsKev) + " кэВ");
                    bad++;
                }
            }
            Console.WriteLine();
        }

        static string Probe(PolynomialEnergyCalibration c, int n)
        {
            int[] chs = new int[] { 0, n / 4, n / 2, (3 * n) / 4, n - 1 };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < chs.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(chs[i].ToString(CultureInfo.InvariantCulture)).Append('=').Append(F(c.ChannelToEnergy(chs[i])));
            }
            return sb.ToString();
        }

        // ==================================================================
        // 3. КРУГ «ВВОЗ → ВЫВОЗ → ВВОЗ»
        // ==================================================================

        static void RoundTrip(string name)
        {
            string f = Path.Combine(dir, name);
            Console.WriteLine("=== 3. КРУГ ввоз → вывоз → ввоз: " + name + " ===");
            DocEnergySpectrum doc = new DocEnergySpectrum();
            string err = null;
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try { DocumentManager.GetInstance().ImportDocumentN42(doc, f); }
            catch (Exception ex) { err = ex.GetType().Name + ": " + One(ex.Message); }
            finally { Console.SetError(realErr); }
            if (err != null) { Console.WriteLine("  ввоз отказал: " + err); bad++; Console.WriteLine(); return; }

            PolynomialEnergyCalibration before = doc.ActiveResultData.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            int channels = doc.ActiveResultData.EnergySpectrum.NumberOfChannels;
            string outFile = Path.Combine(Path.GetTempPath(), "p138-roundtrip-" + Guid.NewGuid().ToString("N") + ".n42");
            try
            {
                // Вывоз ТЕМ ЖЕ путём, что у приложения (`A156`): Util.ExportToN42
                // плюс XmlSerializer с настройками DocumentManager.
                BecquerelMonitor.N42.RadInstrumentData radObject = new BecquerelMonitor.N42.Util().ExportToN42(doc);
                System.Xml.Serialization.XmlSerializer xs =
                    new System.Xml.Serialization.XmlSerializer(typeof(BecquerelMonitor.N42.RadInstrumentData));
                System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                settings.Encoding = Encoding.UTF8;
                settings.Indent = true;
                using (FileStream fs = File.Create(outFile))
                using (System.Xml.XmlWriter w = System.Xml.XmlWriter.Create(fs, settings))
                {
                    xs.Serialize(w, radObject);
                    w.Flush();
                }
                string written = ExtractFirst(File.ReadAllText(outFile), "CoefficientValues");
                Console.WriteLine("  ввоз дал     [" + Join(before.Coefficients) + "]");
                Console.WriteLine("  вывоз записал CoefficientValues = «" + written + "»");
                DocEnergySpectrum back = new DocEnergySpectrum();
                Console.SetError(new StringWriter());
                try { DocumentManager.GetInstance().ImportDocumentN42(back, outFile); }
                catch (Exception ex) { err = ex.GetType().Name + ": " + One(ex.Message); }
                finally { Console.SetError(realErr); }
                if (err != null) { Console.WriteLine("  обратный ввоз отказал: " + err); bad++; return; }
                PolynomialEnergyCalibration after = back.ActiveResultData.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
                Console.WriteLine("  обратный ввоз [" + Join(after.Coefficients) + "]");
                double max = 0.0;
                for (int ch = 0; ch < channels; ch++)
                {
                    double d = Math.Abs(before.ChannelToEnergy(ch) - after.ChannelToEnergy(ch));
                    if (d > max) max = d;
                }
                Console.WriteLine("  ⛔ ШКАЛА ПОСЛЕ КРУГА: наибольшее расхождение " + F(max) + " кэВ");
                if (!double.IsNaN(expectRoundTripKev) && max > expectRoundTripKev)
                {
                    Console.WriteLine("  ⛔ НЕ СОШЛОСЬ: ждали не больше " + F(expectRoundTripKev) + " кэВ");
                    bad++;
                }
            }
            catch (Exception ex) { Console.WriteLine("  вывоз отказал: " + ex.GetType().Name + ": " + One(ex.Message)); bad++; }
            finally { try { File.Delete(outFile); } catch { } }
            Console.WriteLine();
        }

        // ==================================================================

        static string Door(bool n42) { return n42 ? "дверь N42      " : "дверь SpecUtils"; }

        static PolynomialEnergyCalibration Import(string path, bool n42Door, out int channels, out string err)
        {
            channels = 0; err = null;
            DocEnergySpectrum doc = new DocEnergySpectrum();
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try
            {
                if (n42Door) DocumentManager.GetInstance().ImportDocumentN42(doc, path);
                else DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, path, 3600);
            }
            catch (Exception ex) { err = ex.GetType().Name + ": " + One(ex.Message); return null; }
            finally { Console.SetError(realErr); }
            ResultData rd = doc.ActiveResultData;
            if (rd == null || rd.EnergySpectrum == null) { err = "документа нет"; return null; }
            channels = rd.EnergySpectrum.NumberOfChannels;
            PolynomialEnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (cal == null) { err = "шкала не полиномиальная"; return null; }
            cal.CheckCalibration(channels: channels);
            return cal;
        }

        static int NearestChannel(PolynomialEnergyCalibration c, int n, double energy)
        {
            int best = 0; double bestD = double.MaxValue;
            for (int ch = 0; ch < n; ch++)
            {
                double d = Math.Abs(c.ChannelToEnergy(ch) - energy);
                if (d < bestD) { bestD = d; best = ch; }
            }
            return best;
        }

        static double HalfStep(PolynomialEnergyCalibration c, int ch, int n)
        {
            int lo = Math.Max(0, ch - 1), hi = Math.Min(n - 1, ch + 1);
            return (c.ChannelToEnergy(hi) - c.ChannelToEnergy(lo)) / (2.0 * Math.Max(1, hi - lo));
        }

        static string ExtractFirst(string xml, string tag)
        {
            int i = xml.IndexOf("<" + tag, StringComparison.Ordinal);
            if (i < 0) return "<нет>";
            int s = xml.IndexOf('>', i);
            int e = xml.IndexOf("</" + tag, StringComparison.Ordinal);
            return s < 0 || e < 0 || e <= s ? "<нет>" : xml.Substring(s + 1, e - s - 1).Trim();
        }

        static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        static string Join(double[] v)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(v[i].ToString("R", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        static string One(string s) { return s == null ? "" : s.Replace("\r", " ").Replace("\n", " "); }
    }
}
