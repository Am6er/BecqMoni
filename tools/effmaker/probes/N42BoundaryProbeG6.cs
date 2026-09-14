using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace N42BoundaryProbeG6
{
    /// <summary>
    /// ШКАЛА ИЗ ГРАНИЦ ЭНЕРГИЙ КАНАЛОВ — ОДНА У ДВУХ ДВЕРЕЙ, И ПОРЧЕНЫЕ
    /// ГРАНИЦЫ ОТКАЗЫВАЮТ СЛОВАМИ (`A253`, полоса G6, 06.09.2026).
    ///
    /// ⛔ ЧТО МЕРИТСЯ. Решение Amber 06.09.2026: «читать границы, отказ снять».
    /// Проверка этого решения — не «дверь N42 больше не отказывает», а ДВА
    /// замера сразу:
    ///
    /// 1. на файле с ГОДНЫМИ границами (`case1_boundary`) обе двери приложения —
    ///    ImportDocumentN42 и ImportDocumentSpecUtils — дают ОДИН слепок:
    ///    каналы, сумма, времена и коэффициенты шкалы сравниваются как строки
    ///    «R»-формата, то есть ПОБАЙТНО, а не «примерно 12.5»; сверх того
    ///    печатается наибольшее расхождение энергий по каналам;
    /// 2. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: файлы с ПОРЧЕНЫМИ границами (не
    ///    возрастают, число не сходится с каналами, нечисло) дверь N42 ОБЯЗАНА
    ///    отказывать, и отказ обязан называть причину словами. Снятие отказа,
    ///    которое сняло бы и эти, — не починка, а дыра.
    ///
    /// Сочинённые входы кладутся тем же образцом, что `case1_boundary` у
    /// `N42RoundTripProbe --mode=cases` (Head/Tail воспроизведены дословно):
    /// каждый порченый вход отличается от здорового ТОЛЬКО списком границ,
    /// и отказ поэтому приходит ровно оттуда, откуда назван.
    ///
    /// Ключи:
    ///   --out=&lt;каталог&gt;        каталог с *.n42 (обязателен)
    ///   --cases                положить входы case34..case37
    ///   --compare=&lt;a,b,…&gt;      имена файлов, у которых обе двери обязаны дать
    ///                          ОДИН слепок (обе ввозят, коэффициенты побайтно)
    ///   --expect-refuse=&lt;a,b,…&gt; имена файлов, которые дверь N42 обязана
    ///                          ОТКАЗАТЬ СЛОВАМИ
    ///   --culture=&lt;имя&gt;        культура прогона
    /// Код возврата 1, если хоть одно ожидание не сошлось.
    ///
    /// Проба безоконная: AppUi.HasWindows == false, мерится отказная половина
    /// обеих дверей; голоса AppUi.Report перехватываются из потока ошибок.
    /// </summary>
    static class Program
    {
        static string outDir = null;
        static string culture = null;
        static bool cases = false;
        static string[] compare = new string[0];
        static string[] expectRefuse = new string[0];

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--out=")) outDir = a.Substring(6);
                else if (a.StartsWith("--culture=")) culture = a.Substring(10);
                else if (a == "--cases") cases = true;
                else if (a.StartsWith("--compare=")) compare = a.Substring(10).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                else if (a.StartsWith("--expect-refuse=")) expectRefuse = a.Substring(16).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
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

            if (cases) WriteCases();

            int bad = 0;
            if (compare.Length > 0) bad += Compare();
            if (expectRefuse.Length > 0) bad += ExpectRefuse();

            Console.WriteLine();
            if (bad > 0)
            {
                Console.Error.WriteLine("ОТКАЗ СТОРОЖА: не сошлось ожиданий — " + bad.ToString(CultureInfo.InvariantCulture));
                return 1;
            }
            Console.WriteLine("СТОРОЖ ПРОШЁЛ.");
            return 0;
        }

        // ==================================================================
        // СОЧИНЁННЫЕ ВХОДЫ: ПОРЧЕНЫЕ ГРАНИЦЫ И ГРАНИЦЫ БЕЗ ВЕРХНЕЙ
        // ==================================================================

        static void WriteCases()
        {
            Directory.CreateDirectory(outDir);

            // Границы НЕ ВОЗРАСТАЮТ: на 30-й границе провал (362.5 вместо 375).
            //   Полином 4-го порядка, подогнанный по 64 точкам с одним провалом,
            //   остаётся монотонным — CheckCalibration такого НЕ ловит, ловить
            //   обязан сам разбор границ.
            Write("case34_boundary_nonmono.n42", Boundary(EdgesDip()));

            // Число границ НЕ СХОДИТСЯ с каналами: 60 границ на 64 канала.
            Write("case35_boundary_count.n42", Boundary(Edges(60)));

            // НЕЧИСЛО среди границ: 12-я граница «abc».
            Write("case36_boundary_nan.n42", Boundary(EdgesNaN()));

            // Границ ровно N (без верхней границы последнего канала) — годно:
            //   для подгонки верхняя граница не нужна. Обе двери обязаны
            //   ввезти и дать ту же шкалу, что у case1_boundary.
            Write("case37_boundary_n.n42", Boundary(Edges(64)));
        }

        static string Boundary(string edges)
        {
            return Head("case-g6")
                 + "  <EnergyCalibration id=\"EC1\">\r\n"
                 + "    <EnergyBoundaryValues>" + edges + "</EnergyBoundaryValues>\r\n"
                 + "  </EnergyCalibration>\r\n" + Tail();
        }

        /// <summary>Первые <paramref name="n"/> границ ряда 0, 12.5, 25, … (у case1 их 65).</summary>
        static string Edges(int n)
        {
            StringBuilder edges = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) edges.Append(' ');
                edges.Append((i * 12.5).ToString(CultureInfo.InvariantCulture));
            }
            return edges.ToString();
        }

        static string EdgesDip()
        {
            StringBuilder edges = new StringBuilder();
            for (int i = 0; i <= 64; i++)
            {
                if (i > 0) edges.Append(' ');
                double e = i == 30 ? 362.5 : i * 12.5;
                edges.Append(e.ToString(CultureInfo.InvariantCulture));
            }
            return edges.ToString();
        }

        static string EdgesNaN()
        {
            StringBuilder edges = new StringBuilder();
            for (int i = 0; i <= 64; i++)
            {
                if (i > 0) edges.Append(' ');
                edges.Append(i == 12 ? "abc" : (i * 12.5).ToString(CultureInfo.InvariantCulture));
            }
            return edges.ToString();
        }

        // Образец файла — ДОСЛОВНО из N42RoundTripProbe.Head/Tail (case1_boundary),
        // чтобы порченый вход отличался от здорового только списком границ.
        static string Head(string tag)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\" n42DocUUID=\"probe-a136-"
                 + tag + "\">\r\n"
                 + "  <RadInstrumentInformation id=\"RadInstrument\">\r\n"
                 + "    <RadInstrumentManufacturerName>PROBE</RadInstrumentManufacturerName>\r\n"
                 + "    <RadInstrumentModelName>A136</RadInstrumentModelName>\r\n"
                 + "    <RadInstrumentClassCode>Radionuclide Identifier</RadInstrumentClassCode>\r\n"
                 + "    <RadInstrumentVersion>\r\n"
                 + "      <RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\r\n"
                 + "      <RadInstrumentComponentVersion>1</RadInstrumentComponentVersion>\r\n"
                 + "    </RadInstrumentVersion>\r\n"
                 + "  </RadInstrumentInformation>\r\n";
        }

        static string Tail()
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < 64; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append((i % 7) + 1);
            }
            return "  <RadMeasurement id=\"M1\">\r\n"
                 + "    <MeasurementClassCode>Foreground</MeasurementClassCode>\r\n"
                 + "    <StartDateTime>2026-09-05T12:00:00Z</StartDateTime>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S1\" radDetectorInformationReference=\"D1\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n"
                 + "</RadInstrumentData>\r\n";
        }

        static void Write(string name, string xml)
        {
            string path = Path.Combine(outDir, name);
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            Console.WriteLine("  положен вход " + name);
        }

        // ==================================================================
        // ДВЕ ДВЕРИ — ОДИН СЛЕПОК
        // ==================================================================

        sealed class Verdict
        {
            public bool Imported;
            public string Refusal;
            public string Spoken;
            public string Print;
            public PolynomialEnergyCalibration Scale;
            public int Channels;
        }

        static int Compare()
        {
            int bad = 0;
            Console.WriteLine("=== ОДНА ШКАЛА У ДВУХ ДВЕРЕЙ (побайтно) ===");
            foreach (string name in compare)
            {
                string f = Path.Combine(outDir, name);
                Verdict a = RunN42(f);
                Verdict b = RunSpecUtils(f);
                Console.WriteLine("  " + name);
                Console.WriteLine("    дверь N42:       " + (a.Imported ? "ВВЕЗЁН" : "ОТКАЗ " + a.Refusal)
                                  + (string.IsNullOrEmpty(a.Spoken) ? "" : "   [вслух: " + a.Spoken + "]"));
                Console.WriteLine("      слепок: " + a.Print);
                Console.WriteLine("    дверь SpecUtils: " + (b.Imported ? "ВВЕЗЁН" : "ОТКАЗ " + b.Refusal)
                                  + (string.IsNullOrEmpty(b.Spoken) ? "" : "   [вслух: " + b.Spoken + "]"));
                Console.WriteLine("      слепок: " + b.Print);
                if (!a.Imported || !b.Imported)
                {
                    Console.WriteLine("    ⛔ НЕ СОШЛОСЬ: одна из дверей отказала");
                    bad++;
                    continue;
                }
                bool same = a.Print == b.Print;
                double maxDiff = 0.0;
                int n = Math.Min(a.Channels, b.Channels);
                for (int ch = 0; ch <= n; ch++)
                {
                    double d = Math.Abs(a.Scale.ChannelToEnergy(ch) - b.Scale.ChannelToEnergy(ch));
                    if (d > maxDiff) maxDiff = d;
                }
                Console.WriteLine("    слепки " + (same ? "СОВПАЛИ ПОБАЙТНО" : "РАЗОШЛИСЬ")
                                  + "; наибольшее расхождение энергий по каналам 0…" + n.ToString(CultureInfo.InvariantCulture)
                                  + ": " + maxDiff.ToString("R", CultureInfo.InvariantCulture) + " кэВ"
                                  + "; энергия канала 1 у двери N42: "
                                  + a.Scale.ChannelToEnergy(1).ToString("R", CultureInfo.InvariantCulture) + " кэВ");
                if (!same) bad++;
            }
            Console.WriteLine();
            return bad;
        }

        static int ExpectRefuse()
        {
            int bad = 0;
            Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: ПОРЧЕНЫЕ ГРАНИЦЫ ОБЯЗАНЫ ОТКАЗЫВАТЬ СЛОВАМИ (дверь N42) ===");
            foreach (string name in expectRefuse)
            {
                string f = Path.Combine(outDir, name);
                Verdict a = RunN42(f);
                Verdict b = RunSpecUtils(f);
                bool ok = !a.Imported && !string.IsNullOrEmpty(a.Refusal);
                Console.WriteLine("  " + name + " | дверь N42: " + (a.Imported ? "ВВЕЗЁН  ⛔ ОЖИДАЛСЯ ОТКАЗ" : "ОТКАЗ")
                                  + " | дверь SpecUtils: " + (b.Imported ? "ВВЕЗЁН" : "ОТКАЗ")
                                  + (string.IsNullOrEmpty(b.Spoken) ? (b.Imported ? " (молча)" : "") : " [вслух: " + b.Spoken + "]"));
                Console.WriteLine("      причина: " + (a.Refusal ?? "(нет)"));
                Console.WriteLine("      слепок после отказа: " + a.Print);
                Console.WriteLine("      слепок SpecUtils:    " + b.Print);
                if (!ok) bad++;
            }
            Console.WriteLine();
            return bad;
        }

        static Verdict RunN42(string f)
        {
            return Run(f, (doc, path) => DocumentManager.GetInstance().ImportDocumentN42(doc, path));
        }

        static Verdict RunSpecUtils(string f)
        {
            return Run(f, (doc, path) => DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, path, 3600));
        }

        /// <summary>Один прогон одной двери; приём взят у N42DoorsProbeF54 целиком.</summary>
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
            v.Print = Print(doc, v);
            return v;
        }

        static string Print(DocEnergySpectrum doc, Verdict v)
        {
            try
            {
                ResultData rd = doc.ActiveResultData;
                if (rd == null || rd.EnergySpectrum == null) return "(документа нет)";
                EnergySpectrum es = rd.EnergySpectrum;
                long sum = 0;
                for (int i = 0; i < es.Spectrum.Length; i++) sum += es.Spectrum[i];
                PolynomialEnergyCalibration pol = es.EnergyCalibration as PolynomialEnergyCalibration;
                v.Scale = pol;
                v.Channels = es.NumberOfChannels;
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
