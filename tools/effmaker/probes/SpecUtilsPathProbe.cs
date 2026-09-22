using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SpecUtilsPathProbe
{
    /// <summary>
    /// `AMBER75` (полоса П138, 22.09.2026) — ДВЕРЬ SpecUtils И ПУТЬ С КИРИЛЛИЦЕЙ.
    ///
    /// ⛔ ЧТО МЕРИТСЯ, ЧЕТЫРЬМЯ ЗАМЕРАМИ:
    ///
    /// 1. `--native=<путь>`  НАТИВНАЯ ДВЕРЬ БЕЗ ПРИЛОЖЕНИЯ ВОВСЕ: прямой вызов
    ///    `SpecUtilsNet!Open` и рядом — что отдаёт `GetShortPathNameW` на том же
    ///    пути и весь ли он ASCII. Это и называет МЕХАНИЗМ: короткое имя есть —
    ///    путь открывается; короткого имени нет — NULL.
    /// 2. `--pair=<кириллический>|<латинский>` ДВЕРЬ ПРИЛОЖЕНИЯ
    ///    (`DocumentManager.ImportDocumentSpecUtils`) на ОДНИХ И ТЕХ ЖЕ БАЙТАХ,
    ///    положенных по двум путям. Печатается приговор каждой стороны и
    ///    ПОБИТОВОЕ сравнение разбора: SHA-256 слепка (отсчёты, шкала, времена,
    ///    ПШПВ). Контроль неизменности в том и состоит, что слепки совпадают.
    /// 3. `--doors=<путь>`  ВТОРАЯ ДВЕРЬ (`ImportDocumentN42`) на том же пути —
    ///    она кириллицы не боится и служит положительным контролем самого файла:
    ///    если отказала и она, виноват файл, а не путь.
    /// 4. `--garbage=<каталог>` ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: заведомо битый файл
    ///    кладётся по ЛАТИНСКОМУ и по КИРИЛЛИЧЕСКОМУ пути и обязан быть отвергнут
    ///    ОБОИМИ — с тем же текстом «Unable to load file using SpecUtils. Unknown
    ///    file format.». Обход, который начал бы принимать мусор, виден здесь.
    ///
    /// Ожидания: `--expect-cyr=open|refuse` (приговор кириллической стороны пар),
    /// `--expect-same-snapshot` (слепки сторон обязаны совпасть).
    /// Код возврата 1 — названное ожидание не сошлось. Проба безоконная.
    /// </summary>
    static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        [DllImport("kernel32.dll")]
        static extern uint GetACP();

        [DllImport("SpecUtilsNet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern IntPtr Open(string path, string file_ext);

        [DllImport("SpecUtilsNet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern void Close(IntPtr ptr);

        [DllImport("SpecUtilsNet.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int GetMeasurementsCount(IntPtr ptr);

        static readonly List<string> natives = new List<string>();
        static readonly List<string> pairs = new List<string>();
        static readonly List<string> doorsFiles = new List<string>();
        static string garbageDir = null;
        static string expectCyr = null;
        static bool expectSameSnapshot = false;
        static int bad = 0;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            foreach (string a in args)
            {
                if (a.StartsWith("--native=")) natives.Add(a.Substring(9));
                else if (a.StartsWith("--pair=")) pairs.Add(a.Substring(7));
                else if (a.StartsWith("--doors=")) doorsFiles.Add(a.Substring(8));
                else if (a.StartsWith("--garbage=")) garbageDir = a.Substring(10);
                else if (a.StartsWith("--expect-cyr=")) expectCyr = a.Substring(13);
                else if (a == "--expect-same-snapshot") expectSameSnapshot = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  кодовая страница машины GetACP() = " + GetACP().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  TEMP: " + Path.GetTempPath() + "  (весь ASCII: " + IsAscii(Path.GetTempPath()) + ")");
            Console.WriteLine();

            foreach (string p in natives) Native(p);
            foreach (string p in pairs) Pair(p);
            foreach (string f in doorsFiles) Doors(f);
            if (garbageDir != null) Garbage();

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
        // 1. НАТИВНАЯ ДВЕРЬ БЕЗ ПРИЛОЖЕНИЯ
        // ==================================================================

        static void Native(string path)
        {
            Console.WriteLine("=== 1. НАТИВНАЯ SpecUtilsNet!Open: " + path + " ===");
            Console.WriteLine("  путь весь ASCII: " + IsAscii(path));
            Console.WriteLine("  файл существует: " + File.Exists(path));
            StringBuilder sb = new StringBuilder(1024);
            int n = GetShortPathNameW(path, sb, sb.Capacity);
            string shortPath = n > 0 && n < sb.Capacity ? sb.ToString() : null;
            if (shortPath == null)
            {
                Console.WriteLine("  GetShortPathNameW: ОТКАЗ, err = " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                Console.WriteLine("  GetShortPathNameW: " + shortPath);
                Console.WriteLine("    короткое имя весь ASCII: " + IsAscii(shortPath)
                                  + (shortPath == path ? "  (⚠ РАВНО ДЛИННОМУ — у тома нет имён 8.3)" : ""));
            }
            Console.WriteLine("  Open(путь): " + NativeOpen(path));
            if (shortPath != null && shortPath != path)
            {
                Console.WriteLine("  Open(короткое имя): " + NativeOpen(shortPath));
            }
            Console.WriteLine();
        }

        static string NativeOpen(string path)
        {
            string ext = Path.GetExtension(path);
            if (ext != "") ext = ext.TrimStart('.').ToLowerInvariant();
            IntPtr h = IntPtr.Zero;
            try
            {
                h = Open(path, ext);
                if (h == IntPtr.Zero) return "NULL — ОТКАЗ";
                return "дескриптор есть, измерений " + GetMeasurementsCount(h).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { return "ИСКЛЮЧЕНИЕ " + ex.GetType().Name + ": " + ex.Message; }
            finally { if (h != IntPtr.Zero) Close(h); }
        }

        // ==================================================================
        // 2. ДВЕРЬ ПРИЛОЖЕНИЯ НА ДВУХ ПУТЯХ ОДНИХ БАЙТОВ
        // ==================================================================

        static void Pair(string spec)
        {
            string[] two = spec.Split('|');
            if (two.Length != 2) { Console.Error.WriteLine("--pair=<кириллический>|<латинский>"); bad++; return; }
            string cyr = two[0], lat = two[1];
            Console.WriteLine("=== 2. ДВЕРЬ SpecUtils НА ОДНИХ БАЙТАХ ПО ДВУМ ПУТЯМ ===");
            Console.WriteLine("  кириллица: " + cyr);
            Console.WriteLine("  латиница:  " + lat);
            Console.WriteLine("  байты файлов совпадают: " + SameBytes(cyr, lat));

            string errC, errL;
            string snapC = ImportSnapshot(cyr, out errC);
            string snapL = ImportSnapshot(lat, out errL);

            Console.WriteLine("  кириллица: " + (snapC != null ? "ВВЕЗЁН, слепок " + Sha(snapC) : "ОТКАЗ — " + errC));
            Console.WriteLine("  латиница:  " + (snapL != null ? "ВВЕЗЁН, слепок " + Sha(snapL) : "ОТКАЗ — " + errL));

            if (expectCyr == "open" && snapC == null)
            {
                Console.WriteLine("  ⛔ НЕ СОШЛОСЬ: ждали, что кириллический путь ОТКРОЕТСЯ");
                bad++;
            }
            if (expectCyr == "refuse" && snapC != null)
            {
                Console.WriteLine("  ⛔ НЕ СОШЛОСЬ: ждали, что кириллический путь ОТКАЖЕТ");
                bad++;
            }
            if (snapC != null && snapL != null)
            {
                bool same = snapC == snapL;
                Console.WriteLine("  ⛔ ПОБИТОВОЕ СРАВНЕНИЕ РАЗБОРА: слепки "
                                  + (same ? "СОВПАЛИ" : "РАЗОШЛИСЬ"));
                if (!same)
                {
                    Console.WriteLine("    первое расхождение: " + FirstDiff(snapC, snapL));
                    if (expectSameSnapshot) bad++;
                }
            }
            else if (expectSameSnapshot && (snapC == null || snapL == null))
            {
                Console.WriteLine("  ⛔ НЕ СОШЛОСЬ: слепки сравнить не на чем — одна из сторон отказала");
                bad++;
            }
            Console.WriteLine();
        }

        // ==================================================================
        // 3. ВТОРАЯ ДВЕРЬ — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОГО ФАЙЛА
        // ==================================================================

        static void Doors(string path)
        {
            Console.WriteLine("=== 3. ДВЕРЬ N42 НА ТОМ ЖЕ ПУТИ (файл ли виноват?) ===");
            Console.WriteLine("  " + path);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            string err = null;
            try { DocumentManager.GetInstance().ImportDocumentN42(doc, path); }
            catch (Exception ex) { err = ex.GetType().Name + ": " + One(ex.Message); }
            finally { Console.SetError(realErr); }
            Console.WriteLine("  дверь N42: " + (err == null ? "ВВЕЗЁН" : "ОТКАЗ — " + err));
            Console.WriteLine();
        }

        // ==================================================================
        // 4. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: МУСОР ОБЯЗАН БЫТЬ ОТВЕРГНУТ
        // ==================================================================

        static void Garbage()
        {
            Console.WriteLine("=== 4. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: заведомо битый файл ===");
            string cyrDir = Path.Combine(garbageDir, "кириллица мусор");
            Directory.CreateDirectory(cyrDir);
            Directory.CreateDirectory(garbageDir);
            string lat = Path.Combine(garbageDir, "garbage.n42");
            string cyr = Path.Combine(cyrDir, "мусор.n42");
            string junk = "это не спектр и не XML: " + new string('Z', 300);
            File.WriteAllText(lat, junk, new UTF8Encoding(false));
            File.WriteAllText(cyr, junk, new UTF8Encoding(false));
            foreach (string p in new string[] { lat, cyr })
            {
                string err;
                string snap = ImportSnapshot(p, out err);
                Console.WriteLine("  " + (p == lat ? "латиница:  " : "кириллица: ")
                                  + (snap != null ? "⛔ ВВЕЗЁН — МУСОР ПРИНЯТ" : "отвергнут — " + err));
                if (snap != null) bad++;
                else if (err == null || err.IndexOf("Unknown file format", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("    ⛔ текст отказа НЕ ПРЕЖНИЙ: ждали «Unable to load file using SpecUtils. Unknown file format.»");
                    bad++;
                }
            }
            Console.WriteLine();
        }

        // ==================================================================
        // СЛЕПОК РАЗБОРА
        // ==================================================================

        /// <summary>
        /// Ввоз через `ImportDocumentSpecUtils` и канонический текст всего, что
        /// после него лежит в документе. Сравнивать надо именно ЭТО, а не факт
        /// «открылось»: обход, подсунувший библиотеке другой файл, открылся бы
        /// тоже.
        /// </summary>
        static string ImportSnapshot(string path, out string err)
        {
            err = null;
            DocEnergySpectrum doc = new DocEnergySpectrum();
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try
            {
                DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, path, 3600);
            }
            catch (Exception ex) { err = ex.GetType().Name + ": " + One(ex.Message); return null; }
            finally { Console.SetError(realErr); }

            if (doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null
                || doc.ResultDataFile.ResultDataList.Count == 0)
            {
                err = "документа нет";
                return null;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("spectra=").Append(doc.ResultDataFile.ResultDataList.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < doc.ResultDataFile.ResultDataList.Count; i++)
            {
                ResultData rd = doc.ResultDataFile.ResultDataList[i];
                sb.Append("#").Append(i.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("start=").Append(rd.StartTime.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("end=").Append(rd.EndTime.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("fwhm=").Append(rd.FwhmCalibration == null ? "нет" : rd.FwhmCalibration.ToString()).Append('\n');
                Spectrum(sb, "fg", rd.EnergySpectrum);
                Spectrum(sb, "bg", rd.BackgroundEnergySpectrum);
            }
            return sb.ToString();
        }

        static void Spectrum(StringBuilder sb, string tag, EnergySpectrum es)
        {
            if (es == null) { sb.Append(tag).Append("=нет\n"); return; }
            sb.Append(tag).Append(".channels=").Append(es.NumberOfChannels.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(tag).Append(".total=").Append(es.TotalPulseCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(tag).Append(".valid=").Append(es.ValidPulseCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(tag).Append(".time=").Append(es.MeasurementTime.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            PolynomialEnergyCalibration cal = es.EnergyCalibration as PolynomialEnergyCalibration;
            if (cal != null)
            {
                sb.Append(tag).Append(".cal.order=").Append(cal.PolynomialOrder.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append(tag).Append(".cal.coef=");
                for (int k = 0; k < cal.Coefficients.Length; k++)
                {
                    if (k > 0) sb.Append(' ');
                    sb.Append(cal.Coefficients[k].ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }
            else
            {
                sb.Append(tag).Append(".cal=").Append(es.EnergyCalibration == null ? "нет" : es.EnergyCalibration.GetType().Name).Append('\n');
            }
            sb.Append(tag).Append(".spectrum=");
            int[] y = es.Spectrum;
            if (y == null) sb.Append("нет");
            else for (int k = 0; k < y.Length; k++) { if (k > 0) sb.Append(' '); sb.Append(y[k].ToString(CultureInfo.InvariantCulture)); }
            sb.Append('\n');
        }

        static string Sha(string text)
        {
            using (SHA256 h = SHA256.Create())
            {
                byte[] d = h.ComputeHash(new UTF8Encoding(false).GetBytes(text));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in d) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string FirstDiff(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i])
                {
                    int from = Math.Max(0, i - 40);
                    return "позиция " + i.ToString(CultureInfo.InvariantCulture)
                           + "; «" + One(a.Substring(from, Math.Min(80, a.Length - from)))
                           + "» против «" + One(b.Substring(from, Math.Min(80, b.Length - from))) + "»";
                }
            }
            return "одна строка длиннее: " + a.Length.ToString(CultureInfo.InvariantCulture)
                   + " против " + b.Length.ToString(CultureInfo.InvariantCulture);
        }

        static bool SameBytes(string a, string b)
        {
            try
            {
                byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                return true;
            }
            catch { return false; }
        }

        static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] >= (char)0x80) return false;
            return true;
        }

        static string One(string s)
        {
            return s == null ? "" : s.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
