// Проба полосы П269 (ночь 13→14.10.2026): три мелочи, замеченные полосой П268
// (журнал `handover/handover-2026-10-14-p268-autosave-empty-time.md`). Без окон,
// настоящими дверями приложения; члены, которых в сборке «до» может не быть, зовутся
// ОТРАЖЕНИЕМ, поэтому один exe пробы работает рядом со сборкой ДО правки (тогда он
// обязан показать дефект) и ПОСЛЕ.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут
// пути от каталога входной сборки). config\ — КОПИЯ конфигурации человека.
//
//   SmallFixesProbeP269.exe --mode=countlimit
//       поле «предел счёта» окна «Настройки» (numericUpDown13 → CountLimit, int):
//       набранное число → «ОК» (SaveFormContents) → сглаживание SMA/WMA с записанным
//       пределом. Заодно КАЖДОЕ числовое поле формы получает «3000000000» и «ОК».
//       Положительный контроль: сглаживание с пределом 0 на спектре с пустым каналом
//       обязано бросить в обеих сборках (механизм жив, защищает поле).
//   SmallFixesProbeP269.exe --mode=effio --file=<файл с <Time />> --ref=<обычный> --out=<кат>
//       EfficiencyCurveIo.LoadResultData на вариантах файла с пустыми элементами времени
//       против того же текста с элементом, УБРАННЫМ текстом (полная сериализация).
//       Отрицательные контроли (<Weight />, <Time>вчера</Time>) обязаны отказать.
//   SmallFixesProbeP269.exe --mode=effcorpus --dir=<корпус> --n=50
//       LoadResultData на n файлах корпуса, разложенных по всему списку; печать —
//       sha256 полной сериализации, в которой значения времён, ОТСУТСТВУЮЩИХ в файле,
//       замаскированы (до правки там «сейчас», после — правило П263). Строки двух сборок
//       сверяются снаружи: хэши обязаны совпасть.
//   SmallFixesProbeP269.exe --mode=filltimes --ref=<файл со всеми тремя временами> --out=<кат>
//       расклады «начала нет» дверью OpenDocument (и LoadDocument): проба позже конца,
//       проба внутри набора, проба раньше набора, без конца, без пробы; правило — начало
//       не позже «конец − полное время», остальное — как у П263.
//
// Коды: 0 — дефекта нет и контроли сработали (так обязана отвечать сборка ПОСЛЕ);
// 1 — дефект виден (так обязана отвечать сборка ДО); 3 — контроль НЕ сработал (замер
// пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Utils;

static class SmallFixesProbeP269
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string mode = Arg(args, "--mode=", "countlimit");
        Console.WriteLine(ProbeTargetFramework.Describe());
        string app = typeof(DocumentManager).Assembly.Location;
        Console.WriteLine("приложение: " + app + "  ("
            + File.GetLastWriteTime(app).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Console.WriteLine("окна: " + AppUi.HasWindows);
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
        try
        {
            switch (mode)
            {
                case "countlimit": return CountLimitMode();
                case "effio": return EffIoMode(Arg(args, "--file=", null), Arg(args, "--ref=", null), Arg(args, "--out=", null));
                case "effcorpus": return EffCorpusMode(Arg(args, "--dir=", null), int.Parse(Arg(args, "--n=", "50"), Inv));
                case "filltimes": return FillTimesMode(Arg(args, "--ref=", null), Arg(args, "--out=", null));
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

    static string Arg(string[] args, string key, string fallback)
    {
        foreach (string a in args)
        {
            if (a.StartsWith(key, StringComparison.Ordinal)) return a.Substring(key.Length);
        }
        return fallback;
    }

    static string Short(string s)
    {
        if (s == null) return "null";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 170 ? s.Substring(0, 170) + "…" : s;
    }

    static string Inner(Exception ex)
    {
        Exception e = ex;
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        string text = e.GetType().Name + ": " + e.Message;
        if (e.InnerException != null) text += " ← " + e.InnerException.GetType().Name + ": " + e.InnerException.Message;
        return text;
    }

    // ================================================================ countlimit

    static GlobalConfigInfo CloneConfig(GlobalConfigInfo src)
    {
        var s = new XmlSerializer(typeof(GlobalConfigInfo));
        var sw = new StringWriter(Inv);
        s.Serialize(sw, src);
        return (GlobalConfigInfo)s.Deserialize(new StringReader(sw.ToString()));
    }

    // Спектр с пустыми каналами, единицами и пиком — то, что сглаживание видит всегда.
    static double[] TestSpectrum()
    {
        var x = new double[256];
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = (i % 7 == 0) ? 0.0 : (i % 5 == 0 ? 1.0 : 3.0 + (i % 11));
        }
        for (int i = 120; i < 136; i++) x[i] += 1500.0 * Math.Exp(-0.5 * Math.Pow((i - 128) / 3.0, 2));
        return x;
    }

    // Все четыре сглаживания приложения с данным пределом; null — прошло, иначе отказ.
    static string Smooth(int countlimit)
    {
        double[] d = TestSpectrum();
        int[] n = d.Select(v => (int)Math.Round(v)).ToArray();
        var sa = new SpectrumAriphmetics();
        try
        {
            sa.SMA2((double[])d.Clone(), 5, countlimit: countlimit, progressive: false);
            sa.SMA2((double[])d.Clone(), 5, countlimit: countlimit, progressive: true);
            sa.WMA2((double[])d.Clone(), 5, countlimit: countlimit, progressive: false);
            sa.SMA((int[])n.Clone(), 5, countlimit: countlimit, progressive: false);
            sa.WMA((int[])n.Clone(), 5, countlimit: countlimit, progressive: true);
            return null;
        }
        catch (Exception ex)
        {
            return Inner(ex);
        }
    }

    static string SmoothNull()
    {
        var sa = new SpectrumAriphmetics();
        try
        {
            sa.SMA2(null, 5, countlimit: 10, progressive: false);
            return null;
        }
        catch (Exception ex)
        {
            return Inner(ex);
        }
    }

    static int CountLimitMode()
    {
        GlobalConfigInfo baseCfg = CloneConfig(GlobalConfigManager.GetInstance().GlobalConfig);
        int bad = 0, control = 0;
        MethodInfo validate = typeof(UpDownBase).GetMethod("ValidateEditText", BindingFlags.NonPublic | BindingFlags.Instance);

        // (14.10.2026, после правки распорядителя в SpectrumAriphmetics.GetWindowSize) Предел 0
        // из файла настроек, правленного руками, больше не роняет сглаживание — это теперь
        // ожидание, а не контроль: отказ здесь — дефект. Падение на сборке ДО правки измерено
        // П269 (журнал). Положительный контроль самого замера — заведомо испорченный вход
        // (спектр null): Smooth обязан вернуть отказ, иначе ловушка исключений пуста.
        string zero = Smooth(0);
        Console.WriteLine("сглаживание с пределом 0 — " + (zero == null ? "прошло" : "ОТКАЗ: " + Short(zero)));
        if (zero != null) bad++;
        string spoiled = SmoothNull();
        Console.WriteLine("контроль: сглаживание спектра null — " + (spoiled != null ? "ОТКАЗ (замер жив): " + Short(spoiled) : "прошло — контроль НЕ сработал"));
        if (spoiled == null) control++;
        string big = Smooth(int.MaxValue);
        Console.WriteLine("сглаживание с пределом " + int.MaxValue.ToString(Inv) + " — " + (big == null ? "прошло" : "ОТКАЗ: " + Short(big)));
        if (big != null) bad++;
        Console.WriteLine();

        using (var form = new GlobalConfigForm())
        {
            // (1) Каждое числовое поле формы: «3000000000» → «ОК».
            FieldInfo[] boxes = typeof(GlobalConfigForm).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => typeof(NumericUpDown).IsAssignableFrom(f.FieldType))
                .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
            Console.WriteLine("числовых полей в форме: " + boxes.Length.ToString(Inv));
            Console.WriteLine("поле | Minimum | Maximum | набрано 3000000000 → поле → «ОК»");
            foreach (FieldInfo f in boxes)
            {
                var box = (NumericUpDown)f.GetValue(form);
                GlobalConfigInfo cfg = CloneConfig(baseCfg);
                form.LoadFormContents(cfg);
                box.Text = "3000000000";
                validate.Invoke(box, null);
                string shown = box.Value.ToString(Inv);
                string res;
                try { form.SaveFormContents(cfg); res = "записано"; }
                catch (Exception ex) { res = "ОТКАЗ «ОК»: " + Inner(ex); bad++; }
                Console.WriteLine("  " + f.Name.PadRight(16) + " | " + box.Minimum.ToString(Inv).PadLeft(7) + " | "
                    + box.Maximum.ToString(Inv).PadLeft(30) + " | поле " + shown + " → " + res);
            }
            Console.WriteLine();

            // (2) Поле предела счёта: набранное → «ОК» → сглаживание с записанным.
            var limit = (NumericUpDown)typeof(GlobalConfigForm).GetField("numericUpDown13", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            Console.WriteLine("поле предела счёта: Minimum " + limit.Minimum.ToString(Inv) + ", Maximum " + limit.Maximum.ToString(Inv));
            string[] typed = { "10", "100", "1000", "2147483647", "2147483648", "3000000000", "1", "0", "-5" };
            foreach (string t in typed)
            {
                GlobalConfigInfo cfg = CloneConfig(baseCfg);
                form.LoadFormContents(cfg);
                limit.Text = t;
                validate.Invoke(limit, null);
                string shown = limit.Value.ToString(Inv);
                long v = long.Parse(t, Inv);
                long want = Math.Min(Math.Max(v, 1L), int.MaxValue);
                string res;
                bool ok;
                try
                {
                    form.SaveFormContents(cfg);
                    int stored = cfg.ChartViewConfig.CountLimit;
                    string smooth = Smooth(stored);
                    res = "записано " + stored.ToString(Inv) + "; сглаживание " + (smooth == null ? "прошло" : "ОТКАЗ: " + Short(smooth));
                    ok = smooth == null && stored >= 1;
                    // Разумный диапазон — прежнее значение до единицы.
                    if (v >= 1 && v <= int.MaxValue && stored != v) ok = false;
                    if (stored != want) ok = false;
                }
                catch (Exception ex)
                {
                    res = "ОТКАЗ «ОК»: " + Inner(ex);
                    ok = false;
                }
                if (!ok) bad++;
                Console.WriteLine("  набрано " + t.PadLeft(10) + " → поле " + shown.PadLeft(10) + " → " + res + (ok ? "" : "  ← ДЕФЕКТ"));
            }

            // (3) Файл настроек → окно → «ОК».
            foreach (int fromFile in new[] { 100, int.MaxValue, 0, -5 })
            {
                GlobalConfigInfo cfg = CloneConfig(baseCfg);
                cfg.ChartViewConfig.CountLimit = fromFile;
                string res;
                bool ok;
                try
                {
                    form.LoadFormContents(cfg);
                    form.SaveFormContents(cfg);
                    int stored = cfg.ChartViewConfig.CountLimit;
                    string smooth = Smooth(stored);
                    ok = smooth == null && stored == Math.Max(1, fromFile);
                    res = "записано " + stored.ToString(Inv) + "; сглаживание " + (smooth == null ? "прошло" : "ОТКАЗ: " + Short(smooth));
                }
                catch (Exception ex) { ok = false; res = "ОТКАЗ: " + Inner(ex); }
                if (!ok) bad++;
                Console.WriteLine("  файл настроек " + fromFile.ToString(Inv).PadLeft(10) + " → окно → " + res + (ok ? "" : "  ← ДЕФЕКТ"));
            }
        }
        Console.WriteLine();
        Console.WriteLine("итог: дефектов " + bad.ToString(Inv) + ", несработавших контролей " + control.ToString(Inv));
        if (control > 0) return 3;
        return bad == 0 ? 0 : 1;
    }

    // ================================================================ общее для файлов

    static string CanonData(ResultData d)
    {
        var sw = new StringWriter(Inv);
        new XmlSerializer(typeof(ResultData)).Serialize(sw, d);
        return sw.ToString();
    }

    static string Canon(ResultDataFile f)
    {
        var sw = new StringWriter(Inv);
        new XmlSerializer(typeof(ResultDataFile)).Serialize(sw, f);
        return sw.ToString();
    }

    static string Sha(string s)
    {
        using (var h = SHA256.Create())
        {
            return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(8).Select(b => b.ToString("x2", Inv)));
        }
    }

    static string Instant(DateTime t)
    {
        return t.ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv) + " (" + t.Kind + ")";
    }

    // Элемент по имени внутри родителя — заменить (replacement) или убрать (null).
    static string Patch(string text, string parent, string name, string replacement)
    {
        string scope = parent == null ? null : "<" + parent + ">";
        int from = 0;
        if (scope != null)
        {
            from = text.IndexOf(scope, StringComparison.Ordinal);
            if (from < 0) throw new Exception("нет " + scope);
        }
        var rx = new Regex(@"[ \t]*<" + name + @"(?:\s*/>|>[^<]*</" + name + @">)\r?\n?");
        Match m = rx.Match(text, from);
        if (!m.Success) throw new Exception("нет <" + name + "> после " + (scope ?? "начала"));
        string indent = Regex.Match(m.Value, @"^[ \t]*").Value;
        string nl = m.Value.EndsWith("\r\n") ? "\r\n" : (m.Value.EndsWith("\n") ? "\n" : "");
        return text.Substring(0, m.Index) + (replacement == null ? "" : indent + replacement + nl) + text.Substring(m.Index + m.Length);
    }

    static string DeviceGuidOf(string text)
    {
        Match m = Regex.Match(text, @"<DeviceConfigReference>\s*<Name>[^<]*</Name>\s*<Guid>([^<]+)</Guid>");
        if (!m.Success) m = Regex.Match(text, @"<DeviceConfigReference>[\s\S]*?<Guid>([^<]+)</Guid>");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ================================================================ effio

    sealed class Loaded
    {
        public ResultData Data;
        public string Error;
    }

    static Loaded Load(string path, string fallback)
    {
        var l = new Loaded();
        try { l.Data = EfficiencyCurveIo.LoadResultData(path, 0, fallback); }
        catch (Exception ex) { l.Error = Inner(ex); }
        return l;
    }

    static int EffIoMode(string file, string reference, string outDir)
    {
        if (file == null || reference == null || outDir == null)
        {
            Console.WriteLine("нужны --file=, --ref=, --out=");
            return 2;
        }
        Directory.CreateDirectory(outDir);
        var enc = new UTF8Encoding(false);
        string a0 = File.ReadAllText(file);
        string r0 = File.ReadAllText(reference);
        string fallback = DeviceGuidOf(r0);
        Console.WriteLine("файл: " + file + "; ссылка: " + reference + "; запасной прибор " + fallback);

        var cases = new List<Tuple<string, string, string>>();
        string aRemoved = Patch(a0, "SampleInfo", "Time", null);
        cases.Add(Tuple.Create("A0 файл Amber, <Time />", a0, aRemoved));
        cases.Add(Tuple.Create("A1 <Time></Time>", Patch(a0, "SampleInfo", "Time", "<Time></Time>"), aRemoved));
        cases.Add(Tuple.Create("A2 <Time>  </Time>", Patch(a0, "SampleInfo", "Time", "<Time>  </Time>"), aRemoved));
        cases.Add(Tuple.Create("R0 ссылка как есть", r0, r0));
        cases.Add(Tuple.Create("R1 <StartTime />", Patch(r0, "ResultData", "StartTime", "<StartTime />"), Patch(r0, "ResultData", "StartTime", null)));
        cases.Add(Tuple.Create("R2 <EndTime></EndTime>", Patch(r0, "ResultData", "EndTime", "<EndTime></EndTime>"), Patch(r0, "ResultData", "EndTime", null)));
        cases.Add(Tuple.Create("R3 карточка <Time />", Patch(r0, "SampleInfo", "Time", "<Time />"), Patch(r0, "SampleInfo", "Time", null)));
        string rAll = Patch(Patch(Patch(r0, "SampleInfo", "Time", "<Time />"), "ResultData", "StartTime", "<StartTime />"), "ResultData", "EndTime", "<EndTime />");
        string rAllRemoved = Patch(Patch(Patch(r0, "SampleInfo", "Time", null), "ResultData", "StartTime", null), "ResultData", "EndTime", null);
        cases.Add(Tuple.Create("R4 все три пустые", rAll, rAllRemoved));
        cases.Add(Tuple.Create("N1 <Weight /> (обязан отказать)", Patch(r0, "SampleInfo", "Weight", "<Weight />"), (string)null));
        cases.Add(Tuple.Create("N2 <Time>вчера</Time> (обязан отказать)", Patch(r0, "SampleInfo", "Time", "<Time>вчера</Time>"), (string)null));

        int defect = 0, control = 0, idx = 0;
        foreach (var c in cases)
        {
            idx++;
            string p = Path.Combine(outDir, idx.ToString("D2", Inv) + "_v.xml");
            File.WriteAllText(p, c.Item2, enc);
            Loaded o = Load(p, fallback);
            if (c.Item3 == null)
            {
                bool refused = o.Data == null;
                Console.WriteLine(c.Item1 + ": " + (refused ? "ОТКАЗ (верно) — " + Short(o.Error) : "ЗАГРУЖЕН — отрицательный контроль НЕ сработал"));
                if (!refused) control++;
                continue;
            }
            string pr = Path.Combine(outDir, idx.ToString("D2", Inv) + "_ref.xml");
            File.WriteAllText(pr, c.Item3, enc);
            if (o.Data == null)
            {
                Console.WriteLine(c.Item1 + ": ОТКАЗ — " + Short(o.Error));
                if (c.Item1.StartsWith("R0", StringComparison.Ordinal)) control++;   // обычный файл обязан грузиться в обеих сборках
                else defect++;
                continue;
            }
            Loaded orf = Load(pr, fallback);
            if (orf.Data == null) { Console.WriteLine(c.Item1 + ": эталон без элемента НЕ загрузился — " + Short(orf.Error)); control++; continue; }
            bool same = CanonData(o.Data) == CanonData(orf.Data);
            Console.WriteLine(c.Item1 + ": загружен; проба " + Instant(o.Data.SampleInfo.Time) + ", начало " + Instant(o.Data.StartTime)
                + ", конец " + Instant(o.Data.EndTime) + "; каналов " + o.Data.EnergySpectrum.NumberOfChannels.ToString(Inv)
                + ", ПШПВ-кривая " + (o.Data.FwhmCalibration != null ? "есть" : "нет")
                + "; сериализация = эталону без элемента: " + (same ? "да" : "НЕТ"));
            if (!same) defect++;
        }
        Console.WriteLine();
        Console.WriteLine("итог: дефектов " + defect.ToString(Inv) + ", несработавших контролей " + control.ToString(Inv));
        if (control > 0) return 3;
        return defect == 0 ? 0 : 1;
    }

    // ================================================================ effcorpus

    static int EffCorpusMode(string dir, int n)
    {
        if (dir == null) { Console.WriteLine("нужен --dir="); return 2; }
        string[] all = Directory.GetFiles(dir, "*.xml").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var pick = new List<string>();
        for (int i = 0; i < n && i < all.Length; i++)
        {
            string f = all[(int)((long)i * all.Length / Math.Min(n, all.Length))];
            if (!pick.Contains(f)) pick.Add(f);
        }
        Console.WriteLine("файлов в каталоге " + all.Length.ToString(Inv) + ", взято " + pick.Count.ToString(Inv));
        int loaded = 0, refused = 0, withAbsent = 0;
        foreach (string f in pick)
        {
            var doc = new XmlDocument();
            doc.Load(f);
            XmlNode rd = doc.SelectSingleNode("/ResultDataFile/ResultDataList/ResultData");
            bool noStart = rd == null || rd.SelectSingleNode("StartTime") == null;
            bool noEnd = rd == null || rd.SelectSingleNode("EndTime") == null;
            bool noSample = rd == null || rd.SelectSingleNode("SampleInfo/Time") == null;
            string absent = (noStart ? "S" : "-") + (noEnd ? "E" : "-") + (noSample ? "P" : "-");
            if (absent != "---") withAbsent++;
            Loaded l = Load(f, null);
            string name = Path.GetFileName(f);
            if (l.Data == null)
            {
                refused++;
                Console.WriteLine("ФАЙЛ " + name + " | нет " + absent + " | ОТКАЗ " + Sha(l.Error) + " | " + Short(l.Error));
                continue;
            }
            loaded++;
            string canon = CanonData(l.Data);
            // Значения времён, которых в файле НЕТ, — маска: до правки там «сейчас».
            string masked = canon;
            int top = masked.IndexOf("<EnergySpectrum>", StringComparison.Ordinal);
            if (top < 0) top = masked.Length;
            string head = masked.Substring(0, top), tail = masked.Substring(top);
            if (noStart) head = Regex.Replace(head, @"<StartTime>[^<]*</StartTime>", "<StartTime>НЕТ</StartTime>");
            if (noEnd) head = Regex.Replace(head, @"<EndTime>[^<]*</EndTime>", "<EndTime>НЕТ</EndTime>");
            if (noSample)
            {
                head = Regex.Replace(head, @"(<SampleInfo>[\s\S]*?)<Time>[^<]*</Time>", "$1<Time>НЕТ</Time>");
            }
            masked = head + tail;
            Console.WriteLine("ФАЙЛ " + name + " | нет " + absent + " | " + Sha(masked)
                + " | проба " + Instant(l.Data.SampleInfo.Time) + ", начало " + Instant(l.Data.StartTime) + ", конец " + Instant(l.Data.EndTime));
        }
        Console.WriteLine("загружено " + loaded.ToString(Inv) + ", отказов " + refused.ToString(Inv) + ", с отсутствующими временами " + withAbsent.ToString(Inv));
        return 0;
    }

    // ================================================================ filltimes

    sealed class Opened
    {
        public DocEnergySpectrum Doc;
        public string Error;
        public int Voices;
        public string VoiceText = "";
    }

    static Opened Open(string path)
    {
        var o = new Opened();
        TextWriter err = Console.Error;
        var cap = new StringWriter(Inv);
        Console.SetError(cap);
        try { o.Doc = dm.OpenDocument(path); }
        catch (Exception ex) { o.Error = Inner(ex); }
        finally { Console.SetError(err); }
        string[] lines = cap.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        o.Voices = lines.Count(l => l.StartsWith("BecqMoni:", StringComparison.Ordinal));
        o.VoiceText = string.Join(" | ", lines.Select(l => l.Trim()));
        return o;
    }

    static void Drop(DocEnergySpectrum d)
    {
        if (d == null) return;
        dm.DocumentList.Remove(d);
        try { d.Dispose(); } catch { }
    }

    static string Xs(DateTime t)
    {
        return t.ToString("yyyy-MM-ddTHH:mm:ss", Inv);
    }

    static int FillTimesMode(string reference, string outDir)
    {
        if (reference == null || outDir == null) { Console.WriteLine("нужны --ref=, --out="); return 2; }
        Directory.CreateDirectory(outDir);
        dm = DocumentManager.GetInstance();
        var enc = new UTF8Encoding(false);
        string r0 = File.ReadAllText(reference);
        DateTime end = DateTime.Parse(Regex.Match(r0, @"<EndTime>([^<]+)</EndTime>").Groups[1].Value, Inv, DateTimeStyles.None);
        DateTime start0 = DateTime.Parse(Regex.Match(r0, @"<StartTime>([^<]+)</StartTime>").Groups[1].Value, Inv, DateTimeStyles.None);
        double T = double.Parse(Regex.Match(r0, @"<MeasurementTime>([^<]+)</MeasurementTime>").Groups[1].Value, Inv);
        Console.WriteLine("ссылка: " + reference + "; начало " + Xs(start0) + ", конец " + Xs(end) + ", полное время " + T.ToString("R", Inv) + " с");

        string noStart = Patch(r0, "ResultData", "StartTime", null);
        // Вариант: имя, текст, ожидаемое начало ПОСЛЕ (null — не проверять значение), признак «обязан
        // совпасть с правилом П263 и ДО» (значение то же в обеих сборках).
        var cases = new List<Tuple<string, string, DateTime?, bool>>();
        cases.Add(Tuple.Create("F0 все три времени в файле", r0, (DateTime?)start0, true));
        cases.Add(Tuple.Create("F1 начала нет, проба ПОЗЖЕ конца (как в файле)", noStart, (DateTime?)end.AddSeconds(-T), false));
        DateTime inside = end.AddSeconds(-T / 2);
        cases.Add(Tuple.Create("F2 начала нет, проба ВНУТРИ набора (конец − T/2)",
            Patch(noStart, "SampleInfo", "Time", "<Time>" + Xs(inside) + "</Time>"), (DateTime?)end.AddSeconds(-T), false));
        DateTime before = end.AddSeconds(-2 * T);
        cases.Add(Tuple.Create("F3 начала нет, проба РАНЬШЕ набора (конец − 2T)",
            Patch(noStart, "SampleInfo", "Time", "<Time>" + Xs(before) + "</Time>"), (DateTime?)before, true));
        cases.Add(Tuple.Create("F4 начала и конца нет, проба есть", Patch(noStart, "ResultData", "EndTime", null), (DateTime?)null, true));
        cases.Add(Tuple.Create("F6 начала и пробы нет, конец есть", Patch(noStart, "SampleInfo", "Time", null), (DateTime?)ResultData.UnknownStartTime, true));

        int defect = 0, control = 0, idx = 0;
        string f1Path = null, hostPath = null;
        foreach (var c in cases)
        {
            idx++;
            string p = Path.Combine(outDir, idx.ToString("D2", Inv) + "_f.xml");
            File.WriteAllText(p, c.Item2, enc);
            if (idx == 1) hostPath = p;
            if (idx == 2) f1Path = p;
            Opened o = Open(p);
            if (o.Doc == null)
            {
                Console.WriteLine(c.Item1 + ": ОТКАЗ — " + Short(o.Error));
                control++;
                continue;
            }
            ResultData d = o.Doc.ActiveResultData;
            double co = MeasurementResultManager.DecayToSamplingFactor(5.2714, d.SampleInfo.Time, d.StartTime, d.EndTime);
            double cs = MeasurementResultManager.DecayToSamplingFactor(30.08, d.SampleInfo.Time, d.StartTime, d.EndTime);
            double span = (d.EndTime - d.StartTime).TotalSeconds;
            bool notAfter = d.StartTime <= d.EndTime;
            bool spanOk = !(d.EndTimeInFile && !d.StartTimeInFile && d.SampleInfo.TimeInFile) || span >= T - 1e-3;
            bool valueOk = !c.Item3.HasValue || d.StartTime == c.Item3.Value;
            bool ok = notAfter && spanOk && valueOk;
            Console.WriteLine(c.Item1 + ":");
            Console.WriteLine("    проба " + Instant(d.SampleInfo.Time) + ", начало " + Instant(d.StartTime) + ", конец " + Instant(d.EndTime)
                + "; конец − начало " + span.ToString("F0", Inv) + " с (полное " + T.ToString("F0", Inv) + ")");
            Console.WriteLine("    в файле: начало " + d.StartTimeInFile + ", конец " + d.EndTimeInFile + ", проба " + d.SampleInfo.TimeInFile
                + "; Co-60 ×" + co.ToString("F6", Inv) + ", Cs-137 ×" + cs.ToString("F6", Inv) + "; голосов " + o.Voices.ToString(Inv)
                + (o.Voices > 0 ? " (" + Short(o.VoiceText) + ")" : ""));
            Console.WriteLine("    начало не позже конца: " + (notAfter ? "да" : "НЕТ") + "; набор не короче полного времени: " + (spanOk ? "да" : "НЕТ")
                + "; начало = ожидаемому: " + (c.Item3.HasValue ? (valueOk ? "да" : "НЕТ (ждали " + Instant(c.Item3.Value) + ")") : "—")
                + (ok ? "" : "  ← ДЕФЕКТ"));
            if (!ok)
            {
                if (c.Item4) control++;   // правило П263 обязано держаться в обеих сборках
                else defect++;
            }
            if (idx == 2)
            {
                string saved = Path.Combine(outDir, "F1_saved.xml");
                o.Doc.Filename = saved;
                bool s = dm.SaveDocument(o.Doc);
                string text = File.ReadAllText(saved);
                Console.WriteLine("    сохранение: " + (s ? "записан" : "ОТКАЗ") + "; в файле: "
                    + string.Join(" ", Regex.Matches(text, @"<(StartTime|EndTime|Time)>[^<]*</\1>").Cast<Match>().Take(3).Select(m => m.Value)));
            }
            Drop(o.Doc);
        }

        // LoadDocument — вторая дверь того же правила.
        {
            Opened host = Open(hostPath);
            if (host.Doc == null) { Console.WriteLine("LoadDocument: документ-хозяин не открылся — " + Short(host.Error)); control++; }
            else
            {
                TextWriter err = Console.Error;
                Console.SetError(new StringWriter(Inv));
                try
                {
                    ResultDataFile f = dm.LoadDocument(host.Doc, f1Path);
                    ResultData d = f.ResultDataList[0];
                    bool ok = d.StartTime <= d.EndTime && (d.EndTime - d.StartTime).TotalSeconds >= T - 1e-3;
                    Console.WriteLine("LoadDocument(F1): начало " + Instant(d.StartTime) + ", конец " + Instant(d.EndTime) + (ok ? "" : "  ← ДЕФЕКТ"));
                    if (!ok) defect++;
                }
                catch (Exception ex) { Console.WriteLine("LoadDocument(F1): ОТКАЗ — " + Short(Inner(ex))); control++; }
                finally { Console.SetError(err); }
                Drop(host.Doc);
            }
        }

        // Время набора ноль/не число — прямо через FillAbsentTimes (дверь открытия на нуле
        // спрашивает о калибровке и без окон отказывает раньше).
        {
            MethodInfo read = typeof(DocumentManager).GetMethod("ReadResultDataFile", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo fill = typeof(DocumentManager).GetMethod("FillAbsentTimes", BindingFlags.NonPublic | BindingFlags.Static);
            if (read == null || fill == null) { Console.WriteLine("FillAbsentTimes/ReadResultDataFile не найдены"); control++; }
            else
            {
                foreach (double t in new[] { 0.0, double.NaN })
                {
                    var file = (ResultDataFile)read.Invoke(null, new object[] { f1Path });
                    file.ResultDataList[0].EnergySpectrum.MeasurementTime = t;
                    fill.Invoke(null, new object[] { file });
                    ResultData d = file.ResultDataList[0];
                    bool ok = d.StartTime <= d.EndTime;
                    Console.WriteLine("F5 начала нет, проба позже конца, полное время " + t.ToString("R", Inv) + ": начало " + Instant(d.StartTime)
                        + ", конец " + Instant(d.EndTime) + (ok ? "" : "  ← ДЕФЕКТ"));
                    if (!ok) defect++;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("итог: дефектов " + defect.ToString(Inv) + ", несработавших контролей " + control.ToString(Inv));
        if (control > 0) return 3;
        return defect == 0 ? 0 : 1;
    }
}
