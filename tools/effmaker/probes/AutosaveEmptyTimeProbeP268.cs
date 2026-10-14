// Проба полосы П268 (ночь 13→14.10.2026): две находки П263 — ПЕРИОД АВТОСОХРАНЕНИЯ
// (int-переполнение) и ПУСТОЙ ЭЛЕМЕНТ ВРЕМЕНИ в файле спектра. Без окон, настоящими
// дверями приложения; новые члены зовутся ОТРАЖЕНИЕМ, поэтому один и тот же exe пробы
// работает и рядом со сборкой ДО правки (тогда он обязан показать дефект), и ПОСЛЕ.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут
// пути от каталога входной сборки). config\ — КОПИЯ конфигурации человека.
//
//   AutosaveEmptyTimeProbeP268.exe --mode=autosave
//       (1) порог таймера автосохранения: прежнее выражение `AutosavePeriod*60*1000`
//           в int (дословная копия строки MainForm до правки — положительный контроль:
//           обязано уйти в минус при 35792) против MainForm.AutosavePeriodMilliseconds
//           (отражением; в сборке до правки его нет — тогда меряется прежнее);
//       (2) поле периода окна «Настройки»: набранное число → «ОК» (SaveFormContents).
//   AutosaveEmptyTimeProbeP268.exe --mode=emptytime --file=<файл с <Time />>
//                                  --ref=<обычный файл со всеми тремя временами> --out=<кат>
//       варианты файла (пустой <Time />, <Time></Time>, <Time>  </Time>, пустые
//       <StartTime />/<EndTime></EndTime>) открываются OpenDocument и сверяются полной
//       сериализацией с тем же файлом, из которого элемент УБРАН текстом; те же —
//       LoadDocument и загрузка фона; сохранение — побайтно против сохранения файла без
//       элемента. Отрицательные контроли: пустой <Weight />, «<Time>вчера</Time>» и
//       пустой <Time /> вместе с пустым <Weight /> обязаны ОТКАЗАТЬ в обеих сборках.
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;

static class AutosaveEmptyTimeProbeP268
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string mode = Arg(args, "--mode=", "autosave");
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
                case "autosave": return AutosaveMode();
                case "emptytime": return EmptyTimeMode(Arg(args, "--file=", null), Arg(args, "--ref=", null), Arg(args, "--out=", null));
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

    // ================================================================ autosave

    // Дословная копия порога MainForm.OnTimer ДО правки (П263: `MainForm.cs:667`).
    static int OldThreshold(int periodMinutes)
    {
        return periodMinutes * 60 * 1000;
    }

    static int AutosaveMode()
    {
        MethodInfo mi = typeof(MainForm).GetMethod("AutosavePeriodMilliseconds", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        bool fixedBuild = mi != null;
        Console.WriteLine("MainForm.AutosavePeriodMilliseconds: " + (fixedBuild ? "есть (сборка ПОСЛЕ правки)" : "нет (сборка ДО правки — порог прежним выражением)"));
        int[] periods = { 1, 15, 60, 1440, 10080, 35791, 35792, 50000, 71582, 71583, 100000, int.MaxValue, 0, -5 };
        int bad = 0, control = 0;
        Console.WriteLine("период, мин | порог ДО (int) | первое срабатывание ДО | порог сборки | первое срабатывание | верно");
        foreach (int p in periods)
        {
            int oldThr = OldThreshold(p);
            long thr = fixedBuild ? (long)mi.Invoke(null, new object[] { p }) : oldThr;
            long expected = (long)Math.Max(1, p) * 60000L;
            bool ok = thr == expected;
            // Обычные периоды (1…35791 мин) — прежнее значение ДО БИТА.
            if (p >= 1 && p <= 35791 && thr != oldThr) ok = false;
            if (!ok) bad++;
            Console.WriteLine(p.ToString(Inv).PadLeft(11) + " | " + oldThr.ToString(Inv).PadLeft(14) + " | "
                + FirstFire(oldThr).PadLeft(22) + " | " + thr.ToString(Inv).PadLeft(14) + " | "
                + FirstFire(thr).PadLeft(19) + " | " + (ok ? "да" : "НЕТ"));
            if (p == 35792 && oldThr >= 0) control++;
        }
        // Таймер: счётчик +100 мс за такт, срабатывание при счётчике >= порога (код
        // MainForm.OnTimer). Сверка «не раньше и не позже»: с отметки порог−200 мс
        // ровно два такта.
        if (fixedBuild)
        {
            foreach (int p in new[] { 15, 35792, 71582, int.MaxValue })
            {
                long thr = (long)mi.Invoke(null, new object[] { p });
                long counter = thr - 300;
                int ticks = 0;
                bool fired = false;
                while (!fired && ticks < 10)
                {
                    counter += 100;
                    ticks++;
                    fired = counter >= thr;
                }
                bool ok = fired && ticks == 3;
                Console.WriteLine("  такты с отметки «порог − 300 мс», " + p.ToString(Inv) + " мин: сработало на такте " + ticks.ToString(Inv) + (ok ? " — верно" : " — НЕВЕРНО"));
                if (!ok) bad++;
            }
        }
        Console.WriteLine("контроль: прежнее выражение при 35792 мин = " + OldThreshold(35792).ToString(Inv)
            + (control == 0 ? " — минус, дефект в замеряемой полосе виден" : " — НЕ минус, замер пуст"));

        // (2) Поле окна «Настройки».
        bad += FormField();
        if (control > 0) return 3;
        return bad == 0 ? 0 : 1;
    }

    static string FirstFire(long thr)
    {
        long ms = thr <= 100 ? 100 : (thr + 99) / 100 * 100;
        if (ms <= 100) return "1-й такт (100 мс)";
        TimeSpan t = TimeSpan.FromMilliseconds(ms);
        return t.TotalDays >= 1 ? t.TotalDays.ToString("F2", Inv) + " сут" : t.TotalMinutes.ToString("F1", Inv) + " мин";
    }

    static GlobalConfigInfo CloneConfig(GlobalConfigInfo src)
    {
        var s = new XmlSerializer(typeof(GlobalConfigInfo));
        var sw = new StringWriter(Inv);
        s.Serialize(sw, src);
        return (GlobalConfigInfo)s.Deserialize(new StringReader(sw.ToString()));
    }

    static int FormField()
    {
        GlobalConfigInfo baseCfg = CloneConfig(GlobalConfigManager.GetInstance().GlobalConfig);
        int bad = 0;
        using (var form = new GlobalConfigForm())
        {
            var box = (NumericUpDown)typeof(GlobalConfigForm).GetField("numericUpDown10", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            MethodInfo validate = typeof(UpDownBase).GetMethod("ValidateEditText", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine("поле периода: Minimum " + box.Minimum.ToString(Inv) + ", Maximum " + box.Maximum.ToString(Inv));
            // Набранное человеком: текст в поле, затем проверка поля (уход фокуса/Enter), затем «ОК».
            string[] typed = { "15", "60", "35792", "71582", "2147483647", "2147483648", "3000000000" };
            foreach (string t in typed)
            {
                GlobalConfigInfo cfg = CloneConfig(baseCfg);
                form.LoadFormContents(cfg);
                box.Text = t;
                validate.Invoke(box, null);
                string shown = box.Value.ToString(Inv);
                string result;
                bool ok;
                long want = Math.Min(long.Parse(t, Inv), int.MaxValue);
                try
                {
                    form.SaveFormContents(cfg);
                    result = "записано " + cfg.AutosavePeriod.ToString(Inv);
                    ok = cfg.AutosavePeriod == want;
                }
                catch (Exception ex)
                {
                    result = "ОТКАЗ «ОК»: " + ex.GetType().Name;
                    ok = false;
                }
                if (!ok) bad++;
                Console.WriteLine("  набрано " + t.PadLeft(10) + " → поле " + shown.PadLeft(10) + " → " + result + (ok ? "" : "  ← ДЕФЕКТ"));
            }
            // Файл настроек с наибольшим int — окно открывается и пишет его обратно без потерь.
            GlobalConfigInfo big = CloneConfig(baseCfg);
            big.AutosavePeriod = int.MaxValue;
            form.LoadFormContents(big);
            try
            {
                form.SaveFormContents(big);
                bool ok = big.AutosavePeriod == int.MaxValue;
                if (!ok) bad++;
                Console.WriteLine("  файл настроек " + int.MaxValue.ToString(Inv) + " → окно → " + big.AutosavePeriod.ToString(Inv) + (ok ? "" : "  ← ДЕФЕКТ"));
            }
            catch (Exception ex)
            {
                bad++;
                Console.WriteLine("  файл настроек " + int.MaxValue.ToString(Inv) + " → ОТКАЗ " + ex.GetType().Name);
            }
        }
        return bad;
    }

    // ================================================================ emptytime

    static string Canon(ResultDataFile f)
    {
        var sw = new StringWriter(Inv);
        new XmlSerializer(typeof(ResultDataFile)).Serialize(sw, f);
        return sw.ToString();
    }

    static string Instant(DateTime t)
    {
        return t.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv) + " (" + t.Kind + ")";
    }

    static void Drop(DocEnergySpectrum d)
    {
        if (d == null) return;
        dm.DocumentList.Remove(d);
        try { d.Dispose(); } catch { }
    }

    static string Short(string s)
    {
        if (s == null) return "null";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }

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
        catch (Exception ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            o.Error = inner.GetType().Name + ": " + inner.Message;
        }
        finally { Console.SetError(err); }
        string[] lines = cap.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        o.Voices = lines.Count(l => l.StartsWith("BecqMoni:", StringComparison.Ordinal));
        o.VoiceText = string.Join(" | ", lines.Select(l => l.Trim()));
        return o;
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

    static int EmptyTimeMode(string file, string reference, string outDir)
    {
        if (file == null || reference == null || outDir == null)
        {
            Console.WriteLine("нужны --file=, --ref=, --out=");
            return 2;
        }
        Directory.CreateDirectory(outDir);
        dm = DocumentManager.GetInstance();
        bool fixedBuild = typeof(DocumentManager).GetMethod("ReadResultDataFile", BindingFlags.NonPublic | BindingFlags.Static) != null;
        Console.WriteLine("DocumentManager.ReadResultDataFile: " + (fixedBuild ? "есть (ПОСЛЕ)" : "нет (ДО)"));
        var enc = new UTF8Encoding(false);
        string a0 = File.ReadAllText(file);
        string r0 = File.ReadAllText(reference);
        Console.WriteLine("файл: " + file + "; ссылка: " + reference);

        // Варианты: имя, текст, текст «элемента нет» (эталон) либо null для отрицательного контроля.
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
        cases.Add(Tuple.Create("N3 <Time /> и <Weight /> (обязан отказать)", Patch(Patch(r0, "SampleInfo", "Time", "<Time />"), "SampleInfo", "Weight", "<Weight />"), (string)null));

        // Прогрев: первое открытие поднимает менеджеры, и их строки о загрузке конфигурации
        // идут в тот же поток ошибок, что и голоса двери, — без прогрева они засчитались бы
        // первому варианту голосами.
        {
            string warm = Path.Combine(outDir, "00_warmup.xml");
            File.WriteAllText(warm, r0, enc);
            Opened w = Open(warm);
            Console.WriteLine("прогрев (ссылка): " + (w.Doc != null ? "открыт" : "ОТКАЗ — " + Short(w.Error)) + "; строк в потоке ошибок " + w.Voices.ToString(Inv));
            Drop(w.Doc);
        }

        int defect = 0, control = 0, idx = 0;
        string a0Path = null, aRemovedPath = null;
        foreach (var c in cases)
        {
            idx++;
            string p = Path.Combine(outDir, idx.ToString("D2", Inv) + "_v.xml");
            File.WriteAllText(p, c.Item2, enc);
            Opened o = Open(p);
            if (c.Item3 == null)
            {
                bool refused = o.Doc == null;
                Console.WriteLine(c.Item1 + ": " + (refused ? "ОТКАЗ (верно) — " + Short(o.Error) : "ОТКРЫЛСЯ — отрицательный контроль НЕ сработал"));
                if (!refused) control++;
                Drop(o.Doc);
                continue;
            }
            string pr = Path.Combine(outDir, idx.ToString("D2", Inv) + "_ref.xml");
            File.WriteAllText(pr, c.Item3, enc);
            if (o.Doc == null)
            {
                Console.WriteLine(c.Item1 + ": ОТКАЗ — " + Short(o.Error));
                if (idx == 4) control++;   // R0 обязан открываться в обеих сборках
                else defect++;
                continue;
            }
            Opened orf = Open(pr);
            if (orf.Doc == null) { Console.WriteLine(c.Item1 + ": эталон без элемента НЕ открылся — " + Short(orf.Error)); control++; Drop(o.Doc); continue; }
            ResultData d = o.Doc.ActiveResultData;
            double co = MeasurementResultManager.DecayToSamplingFactor(5.2714, d.SampleInfo.Time, d.StartTime, d.EndTime);
            bool same = Canon(o.Doc.ResultDataFile) == Canon(orf.Doc.ResultDataFile);
            bool voicesSame = o.Voices == orf.Voices;
            Console.WriteLine(c.Item1 + ": открыт; проба " + Instant(d.SampleInfo.Time) + ", начало " + Instant(d.StartTime)
                + ", конец " + Instant(d.EndTime) + "; в файле: начало " + d.StartTimeInFile + ", конец " + d.EndTimeInFile
                + ", проба " + d.SampleInfo.TimeInFile + "; Co-60 ×" + co.ToString("F6", Inv)
                + "; голосов " + o.Voices.ToString(Inv) + " (эталон " + orf.Voices.ToString(Inv) + ")"
                + "; сериализация = эталону без элемента: " + (same ? "да" : "НЕТ"));
            if (o.Voices > 0) Console.WriteLine("    голос: " + Short(o.VoiceText));
            if (!same || !voicesSame) defect++;
            if (idx == 1)
            {
                // Запись: сохранённый файл Amber — побайтно как сохранённый эталон без элемента.
                a0Path = Path.Combine(outDir, "A0_saved.xml");
                aRemovedPath = Path.Combine(outDir, "A0_ref_saved.xml");
                o.Doc.Filename = a0Path;
                orf.Doc.Filename = aRemovedPath;
                bool s1 = dm.SaveDocument(o.Doc), s2 = dm.SaveDocument(orf.Doc);
                bool bytesSame = s1 && s2 && File.ReadAllBytes(a0Path).SequenceEqual(File.ReadAllBytes(aRemovedPath));
                Console.WriteLine("    сохранение: побайтно как у эталона без элемента — " + (bytesSame ? "да" : "НЕТ")
                    + " (" + new FileInfo(a0Path).Length.ToString(Inv) + " байт)");
                if (!bytesSame) defect++;
                string saved = File.ReadAllText(a0Path);
                Console.WriteLine("    в сохранённом: " + string.Join(" ", Regex.Matches(saved, @"<(StartTime|EndTime|Time)>[^<]*</\1>").Cast<Match>().Select(m => m.Value)));
            }
            Drop(o.Doc); Drop(orf.Doc);
        }

        // LoadDocument (добавить спектр файла в открытый документ) и загрузка фона.
        string amber = Path.Combine(outDir, "01_v.xml");
        string refPath = Path.Combine(outDir, "04_v.xml");
        {
            Opened host = Open(refPath);
            if (host.Doc == null) { Console.WriteLine("LoadDocument: документ-хозяин не открылся — " + Short(host.Error)); control++; }
            else
            {
                string res;
                bool ok;
                TextWriter err = Console.Error;
                Console.SetError(new StringWriter(Inv));
                try
                {
                    ResultDataFile f = dm.LoadDocument(host.Doc, amber);
                    ok = f != null && f.ResultDataList.Count == 1;
                    res = ok ? "спектр взят, проба " + Instant(f.ResultDataList[0].SampleInfo.Time) : "null";
                }
                catch (Exception ex) { ok = false; res = "ОТКАЗ — " + Short((ex.InnerException ?? ex).Message); }
                finally { Console.SetError(err); }
                Console.WriteLine("LoadDocument(файл Amber): " + res);
                if (!ok) defect++;

                ResultData rd = host.Doc.ActiveResultData;
                rd.BackgroundSpectrumPathname = amber;
                rd.BackgroundEnergySpectrum = null;
                try
                {
                    dm.LoadBackgroundSpectrum(rd);
                    ok = rd.BackgroundEnergySpectrum != null && rd.BackgroundEnergySpectrum.Spectrum.Sum(x => (long)x) > 0;
                    res = ok ? "фон взят, отсчётов " + rd.BackgroundEnergySpectrum.Spectrum.Sum(x => (long)x).ToString(Inv) : "фона нет";
                }
                catch (Exception ex) { ok = false; res = "ОТКАЗ — " + Short((ex.InnerException ?? ex).Message); }
                Console.WriteLine("LoadBackgroundSpectrum(файл Amber): " + res);
                if (!ok) defect++;
                Drop(host.Doc);
            }
        }

        Console.WriteLine();
        Console.WriteLine("итог: дефектов " + defect.ToString(Inv) + ", несработавших контролей " + control.ToString(Inv));
        if (control > 0) return 3;
        return defect == 0 ? 0 : 1;
    }
}
