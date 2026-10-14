// Проба полосы П263 (ночь 13→14.10.2026): ФАЙЛЫ СПЕКТРОВ — открытие, сохранение,
// импорт и экспорт настоящими дверями приложения, без окон.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут
// пути от каталога входной сборки). Свой config\ — КОПИЯ конфигурации человека,
// не она сама: проба создаёт документы и пишет файлы только в --out.
//
//   SpectrumFilesProbeP263.exe --mode=xml --dirs=<кат1;кат2> --out=<кат>
//                              [--culture=ru-RU] [--culture2=en-US] [--dot] [--limit=N]
//       круг «Открыть → Сохранить → Открыть → Сохранить» (DocumentManager.OpenDocument /
//       SaveDocument) на всех файлах ResultDataFile каталогов (рекурсивно). Сверка:
//       (1) поля после первого и второго открытия — ВСЕ, побитово ("R");
//       (2) полная сериализация документа после первого и второго открытия;
//       (3) байты первого и второго сохранения (идемпотентность записи);
//       (4) исходный файл (прямой XmlSerializer) против второго открытия — по полям,
//           которые открытие не вправе менять; поля, которые открытие МЕНЯЕТ законно
//           (уставка 0, сумма 0, кривая разрешения) — отдельным счётом.
//       Культура первого открытия/сохранения — --culture, второго — --culture2.
//       Положительные контроли: изменённый отсчёт копии обязан дать расхождение (1);
//       запятая в числе копии обязана дать ОТКАЗ открытия.
//   --mode=n42   круг «Открыть → экспорт N42 → импорт N42 в новый документ» — по полям.
//   --mode=export круги через экспорт Atom Spectra (.txt), CSV и CSV с энергиями.
//   --mode=amp   спектры, собранные программно на конфигурациях AmplitudaSerial и
//                AmplitudaUSB (и Atom Spectra для сравнения): XML, N42, .txt, CSV.
//   --mode=devcfg старый файл конфигурации AmplitudaSerial с <DeadTimeMicroseconds>.
//   --mode=csvdate дата набора после импорта CSV обеими дверями CSV.
//   --mode=atomic запись экспортов поверх существующего файла с отказом посреди записи.
//
// Коды: 0 — расхождений, кроме названных законными, нет и контроли сработали;
// 1 — есть расхождения; 3 — положительный контроль НЕ сработал (замер пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.N42;

static class SpectrumFilesProbeP263
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;
    static int nreShown;
    static string outDir;
    static bool dot;
    static bool keep;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string mode = Arg(args, "--mode=", "xml");
        outDir = Arg(args, "--out=", null);
        dot = args.Contains("--dot");
        keep = args.Contains("--keep");
        string c1 = Arg(args, "--culture=", "ru-RU");
        string c2 = Arg(args, "--culture2=", c1);
        Console.WriteLine(ProbeTargetFramework.Describe());
        Console.WriteLine("приложение: " + typeof(DocumentManager).Assembly.Location + "  ("
            + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Console.WriteLine("окна: " + AppUi.HasWindows + "; культуры: " + c1 + " → " + c2 + (dot ? " [--dot]" : ""));
        Console.WriteLine("часовой пояс: " + TimeZoneInfo.Local.Id + " " + TimeZoneInfo.Local.BaseUtcOffset);
        if (outDir == null)
        {
            Console.WriteLine("нужен --out=<каталог>");
            return 2;
        }
        Directory.CreateDirectory(outDir);
        try
        {
            SetCulture(c1);
            dm = DocumentManager.GetInstance();
            Console.WriteLine("приборов в конфигурации: " + DeviceConfigManager.GetInstance().DeviceConfigList.Count.ToString(Inv));
            int limit = int.Parse(Arg(args, "--limit=", "100000"), Inv);
            switch (mode)
            {
                case "xml": return XmlMode(Collect(Arg(args, "--dirs=", "")).Take(limit).ToList(), c1, c2);
                case "n42": return N42Mode(Collect(Arg(args, "--dirs=", "")).Take(limit).ToList(), c1, c2);
                case "export": return ExportMode(Collect(Arg(args, "--dirs=", "")).Take(limit).ToList(), c1, c2);
                case "amp": return AmpMode(c1, c2);
                case "devcfg": return DevCfgMode();
                case "csvdate": return CsvDateMode();
                case "atomic": return AtomicMode(Collect(Arg(args, "--dirs=", "")).Take(1).ToList());
            }
            Console.WriteLine("неизвестный --mode: " + mode);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
    }

    // ------------------------------------------------------------------ общее

    static string Arg(string[] args, string key, string fallback)
    {
        foreach (string a in args)
        {
            if (a.StartsWith(key, StringComparison.Ordinal)) return a.Substring(key.Length);
        }
        return fallback;
    }

    static void SetCulture(string name)
    {
        CultureInfo ci = (CultureInfo)new CultureInfo(name).Clone();
        if (dot)
        {
            // Строка MainForm: клон культуры с точкой — то, в чём живёт окно.
            // T245: НАРОЧНО — подмена разделителя ЗДЕСЬ и есть предмет замера (плечо
            //    `--dot` повторяет окно приложения); инвариант вместо неё стёр бы плечо.
            ci.NumberFormat.NumberDecimalSeparator = ".";
        }
        Thread.CurrentThread.CurrentCulture = ci;
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(name);
    }

    static List<string> Collect(string dirs)
    {
        var files = new List<string>();
        foreach (string d in dirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(d)) { files.Add(d); continue; }
            foreach (string f in Directory.GetFiles(d, "*.xml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (IsResultDataFile(f)) files.Add(f);
            }
        }
        return files;
    }

    static bool IsResultDataFile(string f)
    {
        try
        {
            using (var r = new StreamReader(f, true))
            {
                char[] buf = new char[600];
                int n = r.Read(buf, 0, buf.Length);
                return new string(buf, 0, n).Contains("<ResultDataFile");
            }
        }
        catch { return false; }
    }

    static void Drop(DocEnergySpectrum d)
    {
        if (d == null) return;
        dm.DocumentList.Remove(d);
        try { d.Dispose(); } catch { }
    }

    static string R(double v) { return v.ToString("R", Inv); }

    // Время с точностью сетки 1 мкс: различие мельче — округление записи xs:duration.
    static string T(double v) { return Math.Round(v, 6).ToString("R", Inv); }

    static string Str(CDATA c) { return c == null ? "null" : (c.ToString() ?? "null"); }

    static string Instant(DateTime t)
    {
        DateTime u;
        if (t.Kind == DateTimeKind.Utc) u = t;
        else if (t.Kind == DateTimeKind.Local) u = t.ToUniversalTime();
        else u = DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime();
        return u.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv) + "Z";
    }

    static string Hash(int[] a)
    {
        if (a == null) return "null";
        unchecked
        {
            ulong h = 1469598103934665603UL;
            long sum = 0;
            foreach (int x in a) { h = (h ^ (uint)x) * 1099511628211UL; sum += x; }
            return a.Length.ToString(Inv) + "/" + sum.ToString(Inv) + "/" + h.ToString("x16", Inv);
        }
    }

    static string Ser<T>(T o)
    {
        if (o == null) return "null";
        try
        {
            var s = new XmlSerializer(o.GetType());
            var sw = new StringWriter(Inv);
            s.Serialize(sw, o);
            string x = sw.ToString();
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (char ch in x) { h = (h ^ ch) * 1099511628211UL; }
                return x.Length.ToString(Inv) + "#" + h.ToString("x16", Inv);
            }
        }
        catch (Exception ex) { return "SER-FAIL " + ex.GetType().Name; }
    }

    static string Cal(BecquerelMonitor.EnergyCalibration c)
    {
        var p = c as PolynomialEnergyCalibration;
        if (p == null) return c == null ? "null" : c.GetType().Name;
        return p.PolynomialOrder.ToString(Inv) + ":" + string.Join(" ", (p.Coefficients ?? new double[0]).Select(R));
    }

    static void Spec(List<KeyValuePair<string, string>> f, string pre, EnergySpectrum e)
    {
        if (e == null) { f.Add(new KeyValuePair<string, string>(pre, "null")); return; }
        f.Add(new KeyValuePair<string, string>(pre + ".NumberOfChannels", e.NumberOfChannels.ToString(Inv)));
        f.Add(new KeyValuePair<string, string>(pre + ".ChannelPitch", R(e.ChannelPitch)));
        f.Add(new KeyValuePair<string, string>(pre + ".Calibration", Cal(e.EnergyCalibration)));
        f.Add(new KeyValuePair<string, string>(pre + ".ValidPulseCount", e.ValidPulseCount.ToString(Inv)));
        f.Add(new KeyValuePair<string, string>(pre + ".TotalPulseCount", e.TotalPulseCount.ToString(Inv)));
        f.Add(new KeyValuePair<string, string>(pre + ".MeasurementTime", R(e.MeasurementTime)));
        f.Add(new KeyValuePair<string, string>(pre + ".LiveTime", R(e.LiveTime)));
        f.Add(new KeyValuePair<string, string>(pre + ".NumberOfSamples", e.NumberOfSamples.ToString(Inv)));
        f.Add(new KeyValuePair<string, string>(pre + ".SerialNumber", e.SerialNumber == null ? "null" : Ser(e.SerialNumber)));
        f.Add(new KeyValuePair<string, string>(pre + ".Spectrum", Hash(e.Spectrum)));
    }

    static List<KeyValuePair<string, string>> Fields(ResultDataFile file)
    {
        var f = new List<KeyValuePair<string, string>>();
        f.Add(new KeyValuePair<string, string>("FormatVersion", file.FormatVersion ?? "null"));
        f.Add(new KeyValuePair<string, string>("Count", file.ResultDataList.Count.ToString(Inv)));
        for (int i = 0; i < file.ResultDataList.Count; i++)
        {
            ResultData d = file.ResultDataList[i];
            string p = "[" + i.ToString(Inv) + "].";
            SampleInfoData s = d.SampleInfo;
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Name", s == null ? "null" : Str(s.Name)));
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Location", s == null ? "null" : s.Location ?? "null"));
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Time", s == null ? "null" : Instant(s.Time)));
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Time.Kind", s == null ? "null" : s.Time.Kind.ToString()));
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Weight", s == null ? "null" : R(s.Weight)));
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Volume", s == null ? "null" : R(s.Volume)));
            f.Add(new KeyValuePair<string, string>(p + "SampleInfo.Note", s == null ? "null" : Str(s.Note)));
            f.Add(new KeyValuePair<string, string>(p + "DeviceConfigReference", d.DeviceConfigReference == null ? "null" : d.DeviceConfigReference.Guid + "|" + d.DeviceConfigReference.Name));
            f.Add(new KeyValuePair<string, string>(p + "Efficiency", Ser(d.Efficiency)));
            f.Add(new KeyValuePair<string, string>(p + "BackgroundSpectrumFile", d.BackgroundSpectrumFile ?? "null"));
            f.Add(new KeyValuePair<string, string>(p + "StartTime", Instant(d.StartTime)));
            f.Add(new KeyValuePair<string, string>(p + "StartTime.Kind", d.StartTime.Kind.ToString()));
            f.Add(new KeyValuePair<string, string>(p + "EndTime", Instant(d.EndTime)));
            f.Add(new KeyValuePair<string, string>(p + "PresetTime", d.PresetTime.ToString(Inv)));
            f.Add(new KeyValuePair<string, string>(p + "Visible", d.Visible.ToString()));
            f.Add(new KeyValuePair<string, string>(p + "FwhmCalibration", Ser(d.FwhmCalibration)));
            f.Add(new KeyValuePair<string, string>(p + "PulseCollection", Ser(d.PulseCollection)));
            Spec(f, p + "ES", d.EnergySpectrum);
            Spec(f, p + "BG", d.BackgroundEnergySpectrum);
        }
        return f;
    }

    static ResultDataFile ReadRaw(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            return (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
        }
    }

    static string Canon(ResultDataFile f)
    {
        var sw = new StringWriter(Inv);
        new XmlSerializer(typeof(ResultDataFile)).Serialize(sw, f);
        return sw.ToString();
    }

    static List<string> Diff(List<KeyValuePair<string, string>> a, List<KeyValuePair<string, string>> b)
    {
        var res = new List<string>();
        var db = b.GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.First().Value);
        foreach (var kv in a)
        {
            string v;
            if (!db.TryGetValue(kv.Key, out v)) res.Add(kv.Key + ": нет во втором");
            else if (v != kv.Value) res.Add(kv.Key + ": «" + Short(kv.Value) + "» → «" + Short(v) + "»");
        }
        var da = new HashSet<string>(a.Select(k => k.Key));
        foreach (var kv in b) if (!da.Contains(kv.Key)) res.Add(kv.Key + ": нет в первом");
        return res;
    }

    static string Short(string s) { if (s == null) return "null"; return s.Length > 90 ? s.Substring(0, 90) + "…" : s; }

    static string FieldName(string diffLine)
    {
        string k = diffLine.Substring(0, diffLine.IndexOf(':'));
        return Regex.Replace(k, @"^\[\d+\]\.", "");
    }

    static void Count(Dictionary<string, int> c, string k) { int n; c.TryGetValue(k, out n); c[k] = n + 1; }

    static void Print(string title, Dictionary<string, int> c)
    {
        Console.WriteLine(title + (c.Count == 0 ? " — нет" : ""));
        foreach (var kv in c.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("    " + kv.Key + ": " + kv.Value.ToString(Inv));
        }
    }

    // ------------------------------------------------------------------ xml

    // Поля, которые OpenDocument меняет ЗАКОННО (у каждого — своя строка двери):
    // уставка 0 → по прибору/времени; сумма 0 → по отсчётам; кривая разрешения —
    // достраивается CheckDocument; число каналов — по длине массива.
    static readonly string[] OpenMayChange = { "PresetTime", "ES.TotalPulseCount", "ES.ValidPulseCount", "FwhmCalibration", "ES.NumberOfChannels", "BG.NumberOfChannels", "FormatVersion" };

    static int XmlMode(List<string> files, string c1, string c2)
    {
        string dir = Path.Combine(outDir, "xml");
        Directory.CreateDirectory(dir);
        Console.WriteLine("=== КРУГ XML: файлов ResultDataFile " + files.Count.ToString(Inv) + " ===");
        int askRefused = 0;
        int opened = 0, refused = 0, bad = 0, canonDiff = 0, bytesDiff = 0, rawBad = 0;
        var cycleFields = new Dictionary<string, int>();
        var rawFields = new Dictionary<string, int>();
        var openChanged = new Dictionary<string, int>();
        var refuses = new Dictionary<string, int>();
        var absent = new Dictionary<string, int>();
        var decayOff = new Dictionary<string, int>();
        var decayAll = new Dictionary<string, int>();
        double decayWorst = 1.0, decayWorstCs = 1.0;
        string decayWorstFile = null;
        int roiRef = 0, noFormat = 0;
        int idx = 0;
        string firstCopy = null;
        foreach (string path in files)
        {
            idx++;
            string text = File.ReadAllText(path);
            if (text.Contains("<ROIConfigReference>")) roiRef++;
            if (!text.Contains("<FormatVersion>")) noFormat++;
            DocEnergySpectrum d1 = null, d2 = null;
            string copy1 = Path.Combine(dir, idx.ToString("D4", Inv) + "_a.xml");
            string copy2 = Path.Combine(dir, idx.ToString("D4", Inv) + "_b.xml");
            try
            {
                ResultDataFile raw = null;
                try { raw = ReadRaw(path); } catch (Exception ex) { Console.WriteLine("  прямое чтение отказало: " + path + " — " + ex.GetType().Name); }
                SetCulture(c1);
                try
                {
                    d1 = dm.OpenDocument(path);
                }
                catch (Exception ex)
                {
                    // Вопрос «сбросить калибровку?» без окон — бросок двери AppUi.AskYesNo:
                    // в окне человек ответил бы сам. Это не отказ чтения, считается отдельно.
                    if (ex.Message.Contains("Reset calibration question") || ex.Message.Contains("сбросу калибровки")) askRefused++;
                    else refused++;
                    string why = ex.InnerException != null ? ex.InnerException.GetType().Name : ex.GetType().Name;
                    string msg = ex.Message.Replace(path, "<файл>").Replace("\n", " ");
                    string reason = Regex.Replace(msg, @"\(<файл>[^)]*\)\s*$", "").Trim();
                    int colon = reason.LastIndexOf(": ", StringComparison.Ordinal);
                    Count(refuses, why + ": " + (colon >= 0 ? reason.Substring(colon + 2) : reason));
                    Console.WriteLine("  ОТКАЗ открытия: " + path + " — " + msg);
                    continue;
                }
                opened++;
                {
                    // Поправка на распад приложения (окно Measurement Result) по
                    // временам документа: Co-60 (5.2714 г) и Cs-137 (30.08 г).
                    ResultData a = d1.ActiveResultData;
                    bool absentTime = !text.Contains("<EndTime>") || !text.Contains("<StartTime>");
                    double co = MeasurementResultManager.DecayToSamplingFactor(5.2714, a.SampleInfo.Time, a.StartTime, a.EndTime);
                    double cs = MeasurementResultManager.DecayToSamplingFactor(30.08, a.SampleInfo.Time, a.StartTime, a.EndTime);
                    string key = absentTime ? "без времени в файле" : "времена в файле есть";
                    if (Math.Abs(co - 1.0) > 1e-3)
                    {
                        Count(decayOff, key);
                        if (Math.Abs(co - 1.0) > Math.Abs(decayWorst - 1.0) && absentTime) { decayWorst = co; decayWorstCs = cs; decayWorstFile = path; }
                    }
                    Count(decayAll, key);
                }
                var f1 = Fields(d1.ResultDataFile);
                string s1 = Canon(d1.ResultDataFile);
                if (raw != null)
                {
                    foreach (string line in Diff(Fields(raw), f1))
                    {
                        string fn = FieldName(line);
                        if (OpenMayChange.Any(m => fn == m)) Count(openChanged, fn);
                        else if ((fn == "EndTime" && !text.Contains("<EndTime>"))
                                 || (fn == "StartTime" && !text.Contains("<StartTime>"))
                                 || (fn == "SampleInfo.Time" && !Regex.IsMatch(text, @"<SampleInfo>(?:(?!</SampleInfo>)[\s\S])*?<Time>")))
                        {
                            // Элемента в файле НЕТ: и прямое чтение, и открытие получают
                            // умолчание ResultData — «сейчас» в момент создания объекта.
                            Count(absent, fn);
                        }
                        else { Count(rawFields, fn); rawBad++; if (rawBad <= 15) Console.WriteLine("  ИСХОДНИК≠ОТКРЫТИЕ " + Path.GetFileName(path) + " " + line); }
                    }
                }
                d1.Filename = copy1;
                if (!dm.SaveDocument(d1)) throw new Exception("SaveDocument вернул false");
                Drop(d1); d1 = null;
                if (firstCopy == null) firstCopy = copy1;

                SetCulture(c2);
                d2 = dm.OpenDocument(copy1);
                var f2 = Fields(d2.ResultDataFile);
                string s2 = Canon(d2.ResultDataFile);
                d2.Filename = copy2;
                if (!dm.SaveDocument(d2)) throw new Exception("SaveDocument (второй) вернул false");
                Drop(d2); d2 = null;

                var diffs = Diff(f1, f2);
                foreach (string line in diffs) Count(cycleFields, FieldName(line));
                if (diffs.Count > 0)
                {
                    bad++;
                    Console.WriteLine("  КРУГ РАЗОШЁЛСЯ " + path);
                    foreach (string line in diffs.Take(6)) Console.WriteLine("     " + line);
                }
                if (s1 != s2) canonDiff++;
                byte[] b1 = File.ReadAllBytes(copy1), b2 = File.ReadAllBytes(copy2);
                if (!b1.SequenceEqual(b2))
                {
                    bytesDiff++;
                    if (bytesDiff <= 5) Console.WriteLine("  БАЙТЫ ВТОРОГО СОХРАНЕНИЯ ДРУГИЕ: " + path + " (" + b1.Length.ToString(Inv) + " / " + b2.Length.ToString(Inv) + ")");
                }
                else if (diffs.Count == 0 && s1 == s2 && idx > 1 && !keep)
                {
                    File.Delete(copy1);
                    File.Delete(copy2);
                }
            }
            catch (Exception ex)
            {
                bad++;
                Console.WriteLine("  ОБРЫВ КРУГА " + path + ": " + ex.GetType().Name + " " + Short(ex.Message));
            }
            finally
            {
                Drop(d1); Drop(d2);
            }
        }
        Console.WriteLine();
        Console.WriteLine("открыто " + opened.ToString(Inv) + ", вопрос «сбросить калибровку?» (без окон — отказ) " + askRefused.ToString(Inv) + ", иной отказ открытия " + refused.ToString(Inv)
            + ", круг разошёлся " + bad.ToString(Inv) + ", сериализация разошлась " + canonDiff.ToString(Inv)
            + ", байты второго сохранения другие " + bytesDiff.ToString(Inv));
        Console.WriteLine("файлов с <ROIConfigReference>: " + roiRef.ToString(Inv) + " (все прочитаны, если отказов нет), без <FormatVersion>: " + noFormat.ToString(Inv));
        Print("причины отказа открытия:", refuses);
        Print("поля, разошедшиеся в круге «открыть → сохранить → открыть»:", cycleFields);
        Print("поля, которые открытие изменило против исходника (НЕ законные):", rawFields);
        Print("поля, которые открытие изменило законно (уставка, сумма, кривая разрешения, каналы):", openChanged);
        Print("времена, которых в файле НЕТ (прямое чтение и открытие ставят «сейчас»):", absent);
        Print("открытых документов по наличию времён в файле:", decayAll);
        Print("из них с множителем распада Co-60 ≠ 1 (больше 0.1 %):", decayOff);
        if (decayWorstFile != null)
        {
            Console.WriteLine("наибольший множитель у файла без времени: Co-60 ×" + decayWorst.ToString("F4", Inv)
                + ", Cs-137 ×" + decayWorstCs.ToString("F4", Inv) + "  (" + decayWorstFile + ")");
        }

        // Положительные контроли — на копии первого файла.
        int control = 0;
        if (firstCopy != null && File.Exists(firstCopy))
        {
            string t = File.ReadAllText(firstCopy);
            // (а) изменённый отсчёт обязан дать расхождение полей.
            int at = t.IndexOf("<DataPoint>", StringComparison.Ordinal);
            int end = t.IndexOf("</DataPoint>", at, StringComparison.Ordinal);
            int val = int.Parse(t.Substring(at + 11, end - at - 11), Inv);
            string tamper = t.Substring(0, at + 11) + (val + 1).ToString(Inv) + t.Substring(end);
            string pcA = Path.Combine(dir, "control_a.xml");
            File.WriteAllText(pcA, tamper, new UTF8Encoding(true));
            DocEnergySpectrum da = dm.OpenDocument(firstCopy);
            var fa = Fields(da.ResultDataFile); Drop(da);
            DocEnergySpectrum db = dm.OpenDocument(pcA);
            var fb = Fields(db.ResultDataFile); Drop(db);
            var dd = Diff(fa, fb);
            bool okA = dd.Any(x => x.Contains("ES.Spectrum"));
            Console.WriteLine("контроль (а) отсчёт +1 в копии: " + (okA ? "расхождение ВИДНО" : "НЕ ВИДНО — замер пуст") + " (" + dd.Count.ToString(Inv) + " полей)");
            if (!okA) control++;
            // (б) запятая в числе — открытие обязано ОТКАЗАТЬ.
            var m = Regex.Match(t, @"<MeasurementTime>([0-9]+)(\.[0-9]+)?</MeasurementTime>");
            string comma = t.Substring(0, m.Index) + "<MeasurementTime>" + m.Groups[1].Value + ",5</MeasurementTime>" + t.Substring(m.Index + m.Length);
            string pcB = Path.Combine(dir, "control_b.xml");
            File.WriteAllText(pcB, comma, new UTF8Encoding(true));
            bool refusedB = false;
            try { Drop(dm.OpenDocument(pcB)); } catch (Exception ex) { refusedB = true; Console.WriteLine("контроль (б) «" + m.Groups[1].Value + ",5» в MeasurementTime: ОТКАЗ — " + Short(ex.Message.Replace("\n", " "))); }
            if (!refusedB) { Console.WriteLine("контроль (б): запятая ПРОШЛА МОЛЧА"); control++; }
        }
        if (control > 0) return 3;
        return (bad + canonDiff + bytesDiff + rawBad + refused) == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ n42

    static void WriteN42(DocEnergySpectrum doc, string path)
    {
        MethodInfo mi = typeof(DocumentManager).GetMethod("WriteN42File");
        if (mi != null) { mi.Invoke(dm, new object[] { doc, path }); return; }
        // Прежняя сборка: тело ExportDocumentN42 дословно (Util + xmlSettings двери).
        var settings = (XmlWriterSettings)typeof(DocumentManager).GetField("xmlSettings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dm);
        RadInstrumentData rad = new Util().ExportToN42(doc);
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = XmlWriter.Create(fs, settings))
        {
            new XmlSerializer(typeof(RadInstrumentData)).Serialize(w, rad);
        }
    }

    static double MaxEnergyDiff(EnergySpectrum a, EnergySpectrum b)
    {
        if (a == null || b == null || a.EnergyCalibration == null || b.EnergyCalibration == null) return double.NaN;
        int n = a.NumberOfChannels;
        double m = 0;
        foreach (int ch in new[] { 0, n / 8, n / 4, n / 2, 3 * n / 4, n - 1 })
        {
            m = Math.Max(m, Math.Abs(a.EnergyCalibration.ChannelToEnergy(ch) - b.EnergyCalibration.ChannelToEnergy(ch)));
        }
        return m;
    }

    // Сверка двух документов по смыслу (а не по сериализации): что пережило круг.
    static List<string> Compare(ResultData a, ResultData b, bool timesOnly = false)
    {
        var d = new List<string>();
        Action<string, string, string> chk = (k, x, y) => { if (x != y) d.Add(k + ": «" + Short(x) + "» → «" + Short(y) + "»"); };
        chk("ES.Spectrum", Hash(a.EnergySpectrum.Spectrum), Hash(b.EnergySpectrum.Spectrum));
        // Времена — с допуском сетки xs:duration (100 нс, `A148`): N42 пишет их 7 знаками.
        chk("ES.MeasurementTime", T(a.EnergySpectrum.MeasurementTime), T(b.EnergySpectrum.MeasurementTime));
        chk("ES.LiveTime", T(a.EnergySpectrum.LiveTime), T(b.EnergySpectrum.LiveTime));
        chk("ES.EffectiveLiveTime", T(a.EnergySpectrum.EffectiveLiveTime), T(b.EnergySpectrum.EffectiveLiveTime));
        double de = MaxEnergyDiff(a.EnergySpectrum, b.EnergySpectrum);
        if (!(de < 1e-6)) d.Add("ES.Energy: |ΔE|max = " + R(de) + " кэВ");
        chk("StartTime", Instant(a.StartTime), Instant(b.StartTime));
        chk("EndTime", Instant(a.EndTime), Instant(b.EndTime));
        if (timesOnly) return d;
        chk("ES.TotalPulseCount", a.EnergySpectrum.TotalPulseCount.ToString(Inv), b.EnergySpectrum.TotalPulseCount.ToString(Inv));
        chk("BG", a.BackgroundEnergySpectrum == null ? "нет" : Hash(a.BackgroundEnergySpectrum.Spectrum), b.BackgroundEnergySpectrum == null ? "нет" : Hash(b.BackgroundEnergySpectrum.Spectrum));
        if (a.BackgroundEnergySpectrum != null && b.BackgroundEnergySpectrum != null)
        {
            chk("BG.MeasurementTime", T(a.BackgroundEnergySpectrum.MeasurementTime), T(b.BackgroundEnergySpectrum.MeasurementTime));
            chk("BG.LiveTime", T(a.BackgroundEnergySpectrum.LiveTime), T(b.BackgroundEnergySpectrum.LiveTime));
            double dbe = MaxEnergyDiff(a.BackgroundEnergySpectrum, b.BackgroundEnergySpectrum);
            if (!(dbe < 1e-6)) d.Add("BG.Energy: |ΔE|max = " + R(dbe) + " кэВ");
        }
        chk("SampleInfo.Name", Str(a.SampleInfo.Name), Str(b.SampleInfo.Name));
        chk("SampleInfo.Note", Str(a.SampleInfo.Note), Str(b.SampleInfo.Note));
        chk("SampleInfo.Location", a.SampleInfo.Location ?? "", b.SampleInfo.Location ?? "");
        chk("SampleInfo.Weight", R(a.SampleInfo.Weight), R(b.SampleInfo.Weight));
        chk("SampleInfo.Volume", R(a.SampleInfo.Volume), R(b.SampleInfo.Volume));
        chk("SampleInfo.Time", Instant(a.SampleInfo.Time), Instant(b.SampleInfo.Time));
        chk("Efficiency", a.Efficiency == null ? "нет" : "есть", b.Efficiency == null ? "нет" : "есть");
        chk("DeviceConfigReference", a.DeviceConfigReference == null ? "" : a.DeviceConfigReference.Guid, b.DeviceConfigReference == null ? "" : b.DeviceConfigReference.Guid);
        return d;
    }

    static int N42Mode(List<string> files, string c1, string c2)
    {
        string dir = Path.Combine(outDir, "n42");
        Directory.CreateDirectory(dir);
        Console.WriteLine("=== КРУГ N42: файлов " + files.Count.ToString(Inv) + " ===");
        var lost = new Dictionary<string, int>();
        int done = 0, fail = 0, countDiff = 0, core = 0;
        var manuf = new Dictionary<string, int>();
        string[] coreFields = { "ES.Spectrum", "ES.MeasurementTime", "ES.LiveTime", "ES.EffectiveLiveTime", "ES.Energy", "StartTime", "BG", "BG.MeasurementTime", "BG.LiveTime", "BG.Energy", "ES.TotalPulseCount" };
        int idx = 0;
        foreach (string path in files)
        {
            idx++;
            DocEnergySpectrum d1 = null, d2 = null;
            string n42 = Path.Combine(dir, idx.ToString("D4", Inv) + ".n42");
            try
            {
                SetCulture(c1);
                d1 = dm.OpenDocument(path);
                WriteN42(d1, n42);
                string nt = File.ReadAllText(n42);
                var mm = Regex.Match(nt, "<RadInstrumentManufacturerName>([^<]*)</RadInstrumentManufacturerName>\\s*<RadInstrumentModelName>([^<]*)</RadInstrumentModelName>");
                Count(manuf, mm.Success ? mm.Groups[1].Value + " / " + mm.Groups[2].Value : "?");
                SetCulture(c2);
                d2 = dm.CreateDocument();
                dm.ImportDocumentN42(d2, n42);
                var la = d1.ResultDataFile.ResultDataList;
                var lb = d2.ResultDataFile.ResultDataList;
                if (la.Count != lb.Count) { countDiff++; Console.WriteLine("  ЧИСЛО СПЕКТРОВ " + la.Count.ToString(Inv) + " → " + lb.Count.ToString(Inv) + ": " + path); }
                bool anyCore = false;
                for (int i = 0; i < Math.Min(la.Count, lb.Count); i++)
                {
                    foreach (string line in Compare(la[i], lb[i]))
                    {
                        string fn = line.Substring(0, line.IndexOf(':'));
                        Count(lost, fn);
                        if (coreFields.Contains(fn))
                        {
                            anyCore = true;
                            Console.WriteLine("  ЯДРО РАЗОШЛОСЬ " + Path.GetFileName(path) + " [" + i.ToString(Inv) + "] " + line);
                        }
                    }
                }
                if (anyCore) core++;
                done++;
                if (!anyCore && idx > 1) File.Delete(n42);
            }
            catch (Exception ex)
            {
                fail++;
                Exception root = ex.InnerException ?? ex;
                Console.WriteLine("  ОБРЫВ " + path + ": " + Short(root.Message.Replace("\n", " ")));
                if (root is NullReferenceException && ++nreShown <= 2) Console.WriteLine(root.ToString());
            }
            finally { Drop(d1); Drop(d2); }
        }
        Console.WriteLine();
        Console.WriteLine("кругов " + done.ToString(Inv) + ", обрывов " + fail.ToString(Inv) + ", файлов с расхождением ядра " + core.ToString(Inv) + ", с другим числом спектров " + countDiff.ToString(Inv));
        Print("производитель / модель в выгруженном N42:", manuf);
        Print("поля, не пережившие круг N42 (по спектрам):", lost);
        return (fail + core + countDiff) == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ экспорты

    static bool Has(string method) { return typeof(DocumentManager).GetMethod(method) != null; }

    // Через отражение — чтобы одна и та же проба работала и на сборке ДО правки,
    // где этих методов нет (прямой вызов уронил бы JIT целого метода пробы).
    static void Invoke(string method, params object[] args)
    {
        MethodInfo mi = typeof(DocumentManager).GetMethod(method);
        if (mi == null) throw new MissingMethodException("DocumentManager." + method + " — нет в этой сборке");
        try { mi.Invoke(dm, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    static int ExportMode(List<string> files, string c1, string c2)
    {
        if (!Has("WriteAtomSpectraFile"))
        {
            Console.WriteLine("в этой сборке нет WriteAtomSpectraFile — экспорт без диалога недоступен, режим пропущен");
            return 0;
        }
        string dir = Path.Combine(outDir, "export");
        Directory.CreateDirectory(dir);
        Console.WriteLine("=== КРУГИ ЭКСПОРТА: файлов " + files.Count.ToString(Inv) + " ===");
        var lost = new Dictionary<string, Dictionary<string, int>>();
        foreach (string k in new[] { "txt", "csv", "ecsv" }) lost[k] = new Dictionary<string, int>();
        int fail = 0, idx = 0;
        double worstLive = 1.0; string worstLiveFile = null;
        foreach (string path in files)
        {
            idx++;
            DocEnergySpectrum d1 = null;
            try
            {
                SetCulture(c1);
                d1 = dm.OpenDocument(path);
                foreach (string kind in new[] { "txt", "csv", "ecsv" })
                {
                    string f = Path.Combine(dir, idx.ToString("D4", Inv) + "." + kind + (kind == "txt" ? "" : ".csv"));
                    DocEnergySpectrum d2 = null;
                    try
                    {
                        SetCulture(c1);
                        if (kind == "txt") Invoke("WriteAtomSpectraFile", d1, f);
                        else if (kind == "csv") Invoke("WriteCsvFile", d1, f);
                        else Invoke("WriteEnergyCsvFile", d1, f, BackgroundMode.Visible, SmoothingMethod.None);
                        SetCulture(c2);
                        d2 = dm.CreateDocument();
                        try
                        {
                            if (kind == "txt") dm.ImportDocumentAtomSpectra(d2, f);
                            else if (kind == "csv") dm.ImportCsvToDocument(d2, 3600, f);
                            else dm.ImportDocumentSpecUtils(d2, f, 3600); // свой ECSV читает дверь SpecUtils («Чего делать НЕ надо»: не дверью CSV с энергиями)
                        }
                        catch (Exception ex)
                        {
                            Count(lost[kind], "ИМПОРТ ОТКАЗАЛ: " + Short((ex.InnerException ?? ex).Message.Replace("\n", " ")));
                            continue;
                        }
                        ResultData a = d1.ActiveResultData, b = d2.ActiveResultData;
                        foreach (string line in Compare(a, b, true))
                        {
                            Count(lost[kind], line.Substring(0, line.IndexOf(':')));
                        }
                        if (kind == "txt" && a.EnergySpectrum.EffectiveLiveTime > 0)
                        {
                            double ratio = b.EnergySpectrum.EffectiveLiveTime / a.EnergySpectrum.EffectiveLiveTime;
                            if (ratio > worstLive) { worstLive = ratio; worstLiveFile = path; }
                        }
                        File.Delete(f);
                    }
                    finally { Drop(d2); }
                }
            }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine("  ОБРЫВ " + path + ": " + Short((ex.InnerException ?? ex).Message.Replace("\n", " ")));
            }
            finally { Drop(d1); }
        }
        foreach (var kv in lost) Print("круг " + kv.Key + ": поля, не пережившие круг (из " + files.Count.ToString(Inv) + "):", kv.Value);
        Console.WriteLine("наибольший рост знаменателя скорости счёта после круга .txt: ×" + worstLive.ToString("F4", Inv) + (worstLiveFile != null ? "  (" + worstLiveFile + ")" : ""));
        return fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ Amplituda

    static DocEnergySpectrum Synth(DeviceConfigInfo dci, double real, double live)
    {
        DocEnergySpectrum d = dm.CreateDocument();
        ResultData rd = d.ActiveResultData;
        rd.DeviceConfig = dci;
        rd.DeviceConfigReference = dci.CreateReference();
        var es = new EnergySpectrum(1.0, 1024);
        var cal = new PolynomialEnergyCalibration();
        cal.PolynomialOrder = 2;
        cal.Coefficients = new double[] { -3.25, 2.9375, 1.25e-5 };
        es.EnergyCalibration = cal;
        long n = 0;
        var rnd = new Random(263);
        for (int i = 0; i < 1024; i++)
        {
            double c = 4000.0 * Math.Exp(-i / 180.0) + 9000.0 * Math.Exp(-0.5 * Math.Pow((i - 226) / 7.5, 2));
            es.Spectrum[i] = (int)Math.Round(c + rnd.NextDouble());
            n += es.Spectrum[i];
        }
        es.TotalPulseCount = n;
        es.ValidPulseCount = n;
        es.MeasurementTime = real;
        es.LiveTime = live;
        rd.EnergySpectrum = es;
        rd.FwhmCalibration = null;
        rd.StartTime = new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Local);
        rd.EndTime = rd.StartTime.AddSeconds(real);
        rd.SampleInfo.Time = rd.StartTime;
        rd.SampleInfo.Name = "P263 " + dci.Name;
        rd.SampleInfo.Weight = 0.5;
        rd.SampleInfo.Volume = 0.45;
        rd.SampleInfo.Note = "проба П263";
        rd.PresetTime = 3600;
        return d;
    }

    static DeviceConfigInfo Cfg(string name, InputDeviceConfig input)
    {
        var dci = new DeviceConfigInfo();
        dci.Guid = Guid.NewGuid().ToString();
        dci.Name = name;
        dci.InputDeviceConfig = input;
        return dci;
    }

    static int AmpMode(string c1, string c2)
    {
        string dir = Path.Combine(outDir, "amp");
        Directory.CreateDirectory(dir);
        Console.WriteLine("=== СПЕКТРЫ AMPLITUDA, СОБРАННЫЕ ПРОГРАММНО ===");
        var usb = new AmplitudaUsbDeviceConfig();
        double usbDead = usb.DeadTime();
        var kinds = new List<Tuple<string, DeviceConfigInfo, double, double>>();
        // Serial: живое время приходит ИЗ БЛОКА (IDeviceLiveTimeSource), DeadTime() = 0.
        kinds.Add(Tuple.Create("AmplitudaSerial", Cfg("P263 AmplitudaSerial", new AmplitudaSerialDeviceConfig()), 3600.25, 3240.5));
        // USB: живое — формулой по DeadTime() конфигурации (её мёртвое время).
        kinds.Add(Tuple.Create("AmplitudaUSB", Cfg("P263 AmplitudaUSB", usb), 3600.25, -1.0));
        kinds.Add(Tuple.Create("AtomSpectra", Cfg("P263 AtomSpectra", new AtomSpectraDeviceConfig()), 3600.25, -1.0));
        int bad = 0;
        foreach (var k in kinds)
        {
            DeviceConfigManager.GetInstance().DeviceConfigList.Add(k.Item2);
            Console.WriteLine();
            Console.WriteLine("--- " + k.Item1 + ": DeadTime() = " + R(k.Item2.InputDeviceConfig.DeadTime()) + " с");
            SetCulture(c1);
            double live = k.Item4;
            DocEnergySpectrum d = Synth(k.Item2, k.Item3, 0);
            if (live < 0) live = BecquerelMonitor.Utils.LiveTime.Calculate(k.Item3, d.ActiveResultData.EnergySpectrum.TotalPulseCount, k.Item2.InputDeviceConfig.DeadTime());
            d.ActiveResultData.EnergySpectrum.LiveTime = live;
            Console.WriteLine("    исходное: real " + R(k.Item3) + " с, live " + R(live) + " с, Σ " + d.ActiveResultData.EnergySpectrum.TotalPulseCount.ToString(Inv));
            string xml = Path.Combine(dir, k.Item1 + ".xml");
            d.Filename = xml;
            dm.SaveDocument(d);
            SetCulture(c2);
            DocEnergySpectrum r = dm.OpenDocument(xml);
            var cx = Compare(d.ActiveResultData, r.ActiveResultData);
            Console.WriteLine("    XML: " + (cx.Count == 0 ? "всё сошлось" : string.Join("; ", cx)));
            Console.WriteLine("    XML: прибор после открытия — " + (r.ActiveResultData.DeviceConfig != null && r.ActiveResultData.DeviceConfig.InputDeviceConfig != null ? r.ActiveResultData.DeviceConfig.InputDeviceConfig.GetType().Name : "нет"));
            if (cx.Count > 0) bad++;
            foreach (string kind in new[] { "n42", "txt", "csv", "ecsv" })
            {
                string f = Path.Combine(dir, k.Item1 + "." + kind + (kind == "ecsv" ? ".csv" : ""));
                DocEnergySpectrum e = null;
                try
                {
                    SetCulture(c1);
                    if (kind == "n42") WriteN42(r, f);
                    else if (!Has("WriteAtomSpectraFile")) { Console.WriteLine("    " + kind + ": нет экспорта без диалога в этой сборке"); continue; }
                    else if (kind == "txt") Invoke("WriteAtomSpectraFile", r, f);
                    else if (kind == "csv") Invoke("WriteCsvFile", r, f);
                    else Invoke("WriteEnergyCsvFile", r, f, BackgroundMode.Visible, SmoothingMethod.None);
                    if (kind == "n42")
                    {
                        string nt = File.ReadAllText(f);
                        var mm = Regex.Match(nt, "<RadInstrumentManufacturerName>([^<]*)</RadInstrumentManufacturerName>\\s*<RadInstrumentModelName>([^<]*)</RadInstrumentModelName>");
                        var hw = Regex.Match(nt, "<RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\\s*<RadInstrumentComponentVersion>([^<]*)</RadInstrumentComponentVersion>");
                        Console.WriteLine("    N42: производитель «" + mm.Groups[1].Value + "», модель «" + mm.Groups[2].Value + "», Hardware «" + hw.Groups[1].Value + "»");
                    }
                    SetCulture(c2);
                    e = dm.CreateDocument();
                    if (kind == "n42") dm.ImportDocumentN42(e, f);
                    else if (kind == "txt") dm.ImportDocumentAtomSpectra(e, f);
                    else if (kind == "csv") dm.ImportCsvToDocument(e, 3600, f);
                    else dm.ImportDocumentSpecUtils(e, f, 3600); // свой ECSV — дверью SpecUtils
                    ResultData a = r.ActiveResultData, b = e.ActiveResultData;
                    var c = Compare(a, b, true);
                    double ratio = b.EnergySpectrum.EffectiveLiveTime / a.EnergySpectrum.EffectiveLiveTime;
                    Console.WriteLine("    " + kind.ToUpperInvariant() + ": live " + R(b.EnergySpectrum.LiveTime) + ", real " + R(b.EnergySpectrum.MeasurementTime)
                        + ", знаменатель ×" + ratio.ToString("F5", Inv) + " (Бк ×" + (1.0 / ratio).ToString("F5", Inv) + ")"
                        + (c.Count == 0 ? "; прочее сошлось" : "; разошлось: " + string.Join("; ", c.Select(x => x.Substring(0, x.IndexOf(':'))))));
                }
                catch (Exception ex)
                {
                    bad++;
                    Console.WriteLine("    " + kind + ": ОБРЫВ " + Short((ex.InnerException ?? ex).Message.Replace("\n", " ")));
                }
                finally { Drop(e); }
            }
            Drop(d); Drop(r);
            DeviceConfigManager.GetInstance().DeviceConfigList.Remove(k.Item2);
        }
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ старые конфиги

    static int DevCfgMode()
    {
        Console.WriteLine("=== СТАРЫЙ КОНФИГ AmplitudaSerial С <DeadTimeMicroseconds> ===");
        var dci = Cfg("P263 old AmplitudaSerial", new AmplitudaSerialDeviceConfig());
        var ser = new XmlSerializer(typeof(DeviceConfigInfo));
        var sw = new StringWriter(Inv);
        ser.Serialize(sw, dci);
        string xml = sw.ToString();
        int at = xml.IndexOf("<Address>", StringComparison.Ordinal);
        Console.WriteLine("элемент <Address> в записи: " + (at >= 0));
        if (at < 0) return 3;
        string old = xml.Insert(at, "<DeadTimeMicroseconds>95</DeadTimeMicroseconds>");
        int bad = 0;
        try
        {
            var back = (DeviceConfigInfo)ser.Deserialize(new StringReader(old));
            var asd = back.InputDeviceConfig as AmplitudaSerialDeviceConfig;
            Console.WriteLine("старый файл: прочитан, тип " + (back.InputDeviceConfig == null ? "null" : back.InputDeviceConfig.GetType().Name)
                + ", DeadTime() = " + (asd == null ? "?" : R(asd.DeadTime())));
            if (asd == null) bad++;
        }
        catch (Exception ex) { bad++; Console.WriteLine("старый файл: ОТКАЗ " + (ex.InnerException ?? ex).Message); }
        string broken = Regex.Replace(xml, "<Address>[^<]*</Address>", "<Address>abc</Address>");
        bool refused = false;
        try { ser.Deserialize(new StringReader(broken)); } catch { refused = true; }
        Console.WriteLine("контроль: <Address>abc</Address> — " + (refused ? "ОТКАЗ (контроль работает)" : "ПРОШЛО — замер пуст"));
        if (!refused) return 3;
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ дата CSV

    static int CsvDateMode()
    {
        Console.WriteLine("=== ДАТА НАБОРА ПОСЛЕ ИМПОРТА CSV ===");
        string dir = Path.Combine(outDir, "csvdate");
        Directory.CreateDirectory(dir);
        DocEnergySpectrum probe = dm.CreateDocument();
        int ch = probe.ActiveResultData.EnergySpectrum.NumberOfChannels;
        Drop(probe);
        var sb = new StringBuilder("Channel,Counts (TotalTime=1234.5s)\r\n");
        var sbe = new StringBuilder("Energy,Count #0d0h20m34s\r\n");
        for (int i = 0; i < ch; i++)
        {
            int c = 100 + (i % 17);
            sb.Append(i.ToString(Inv)).Append(',').Append(c.ToString(Inv)).Append("\r\n");
            sbe.Append((1.5 + 2.9 * i).ToString(Inv)).Append(',').Append(c.ToString(Inv)).Append("\r\n");
        }
        string f1 = Path.Combine(dir, "plain.csv"), f2 = Path.Combine(dir, "energy.csv");
        File.WriteAllText(f1, sb.ToString(), new UTF8Encoding(true));
        File.WriteAllText(f2, sbe.ToString(), new UTF8Encoding(true));
        DateTime before = DateTime.Now;
        int bad = 0;
        foreach (string f in new[] { f1, f2 })
        {
            DocEnergySpectrum d = dm.CreateDocument();
            if (f == f1) dm.ImportCsvToDocument(d, 3600, f); else dm.ImportCsvEnergyToDocument(d, 3600, f);
            ResultData rd = d.ActiveResultData;
            bool now = Math.Abs((rd.StartTime - before).TotalMinutes) < 10;
            bool unknown = rd.StartTime == ResultData.UnknownStartTime && rd.SampleInfo.Time == ResultData.UnknownStartTime;
            Console.WriteLine("  " + Path.GetFileName(f) + ": Start " + rd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", Inv)
                + ", Sample " + rd.SampleInfo.Time.ToString("yyyy-MM-dd HH:mm:ss", Inv)
                + ", End " + rd.EndTime.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv)
                + ", T " + R(rd.EnergySpectrum.MeasurementTime) + " с → "
                + (unknown ? "«времени нет» (1970)" : now ? "«СЕЙЧАС» — день импорта" : "иное"));
            if (!unknown) bad++;
            Drop(d);
        }
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ атомарность экспортов

    static int AtomicMode(List<string> files)
    {
        Console.WriteLine("=== ЗАПИСЬ ЭКСПОРТА ПОВЕРХ СУЩЕСТВУЮЩЕГО ФАЙЛА С ОТКАЗОМ ПОСРЕДИ ===");
        string dir = Path.Combine(outDir, "atomic");
        Directory.CreateDirectory(dir);
        const string marker = "ПРЕЖНИЙ ФАЙЛ ЧЕЛОВЕКА\r\n";
        // Механизм прежнего кода: StreamWriter(имя) обрезает файл при ОТКРЫТИИ.
        string m = Path.Combine(dir, "mechanism.txt");
        File.WriteAllText(m, marker);
        try
        {
            using (var w = new StreamWriter(m))
            {
                w.WriteLine("начало");
                throw new IOException("отказ посреди записи (впрыск)");
            }
        }
        catch (IOException) { }
        string after = File.ReadAllText(m);
        Console.WriteLine("механизм прежнего кода (StreamWriter(имя) + отказ): файл " + (after == marker ? "ЦЕЛ" : "ИСПОРЧЕН («" + after.Trim() + "»)"));
        bool mechanismShown = after != marker;
        if (!Has("WriteAtomSpectraFile"))
        {
            Console.WriteLine("в этой сборке экспорт без диалога недоступен — дальше нечего мерить");
            return mechanismShown ? 0 : 3;
        }
        if (files.Count == 0) return 2;
        DocEnergySpectrum d = dm.OpenDocument(files[0]);
        // Отказ посреди: массив отсчётов короче объявленного числа каналов — запись
        // спотыкается на середине файла.
        EnergySpectrum es = d.ActiveResultData.EnergySpectrum;
        int[] full = es.Spectrum;
        es.Spectrum = full.Take(full.Length / 2).ToArray();
        int bad = 0;
        foreach (string kind in new[] { "txt", "csv", "ecsv" })
        {
            string f = Path.Combine(dir, "target." + kind);
            File.WriteAllText(f, marker);
            string err = null;
            try
            {
                if (kind == "txt") Invoke("WriteAtomSpectraFile", d, f);
                else if (kind == "csv") Invoke("WriteCsvFile", d, f);
                else Invoke("WriteEnergyCsvFile", d, f, BackgroundMode.Visible, SmoothingMethod.None);
            }
            catch (Exception ex) { err = ex.GetType().Name; }
            string now = File.ReadAllText(f);
            bool intact = now == marker;
            Console.WriteLine("  " + kind + ": отказ записи " + (err ?? "НЕ СЛУЧИЛСЯ") + "; файл человека " + (intact ? "ЦЕЛ" : "ИСПОРЧЕН (" + now.Length.ToString(Inv) + " знаков)")
                + "; .tmp рядом: " + File.Exists(f + ".tmp"));
            if (err == null) bad += 100;
            if (!intact) bad++;
        }
        es.Spectrum = full;
        // Удачная запись после отказа — файл новый, .tmp нет.
        string ok = Path.Combine(dir, "target.txt");
        Invoke("WriteAtomSpectraFile", d, ok);
        Console.WriteLine("  удачная запись: " + new FileInfo(ok).Length.ToString(Inv) + " байт, первая строка «" + File.ReadLines(ok).First() + "», BOM: " + (File.ReadAllBytes(ok)[0] == 0xEF));
        Drop(d);
        if (!mechanismShown) return 3;
        return bad == 0 ? 0 : 1;
    }
}
