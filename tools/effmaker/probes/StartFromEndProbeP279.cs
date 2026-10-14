// Проба полосы П279 (14.10.2026): НАЧАЛО НАБОРА ИЗ КОНЦА. Решение Amber 14.10.2026
// вопросником, дословно: «Конец − полное время (Рекомендую)» — файл без начала набора
// и без времени пробы, но с концом: начало = конец − полное (реальное) время, проба =
// начало (правило П263 «времени пробы нет — время начала»), голоса `A207` нет. Нет
// полного времени (или конца) — прежнее правило `A207`: 1970-01-01 и голос.
// Без окон, настоящими дверями приложения (OpenDocument, LoadDocument, SaveDocument,
// EfficiencyCurveIo.LoadResultData; FillAbsentTimes — отражением). Один exe пробы
// работает и со сборкой ДО правки (тогда обязан показать дефект), и ПОСЛЕ.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут
// пути от каталога входной сборки). config\ — КОПИЯ конфигурации человека.
//
//   StartFromEndProbeP279.exe --refs=<полный1;полный2> --notimes=<файл без времён> --out=<кат>
//       --refs     файлы, где есть все три времени (начало, конец, время пробы); из каждого
//                  текстом убираются элементы и получаются расклады H1…H5 (см. ниже);
//       --notimes  настоящий файл без всех трёх времён (выгрузка RadiaCode у Amber) —
//                  контроль: 1970 и голос в обеих сборках.
//   Расклады на каждую ссылку:
//       H0 все три времени — эталон множителя распада;
//       H1 начала и пробы нет, конец есть           — ПРАВКА: начало = конец − T;
//       H2 начала и всей карточки пробы нет, конец есть — ПРАВКА: то же;
//       H3 начала, пробы и конца нет                 — КОНТРОЛЬ: 1970 и голос (обе сборки);
//       H5 начала нет, проба и конец есть            — КОНТРОЛЬ: правило П269 (обе сборки);
//       сохранение H1 и повторное открытие, состав элементов записи против H0;
//       LoadDocument(H1), EfficiencyCurveIo.LoadResultData(H1);
//       FillAbsentTimes(H1) при T = 0, NaN, −5, +∞, 1e13 с — КОНТРОЛЬ: 1970 и счёт 1;
//       продолжение набора — ПОДРАЖАНИЕ правилу приборов, у которых начало загруженного
//       документа сохраняется, а конец ставится по часам (AmplitudaSerial/USB): конец =
//       «сейчас», печать длины набора и множителя Cs-137. Путь продолжения самого прибора
//       без железа недоступен — это не дверь прибора, а её арифметика.
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
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

static class StartFromEndProbeP279
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;
    static string voicePrefix;
    static readonly DateTime Now = DateTime.Now;
    static int defect, control;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine(ProbeTargetFramework.Describe());
        string app = typeof(DocumentManager).Assembly.Location;
        Console.WriteLine("приложение: " + app + "  ("
            + File.GetLastWriteTime(app).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Console.WriteLine("окна: " + AppUi.HasWindows + "; «сейчас» для подражания продолжению: " + Instant(Now));
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
        string refs = Arg(args, "--refs=", null);
        string notimes = Arg(args, "--notimes=", null);
        string outDir = Arg(args, "--out=", null);
        if (refs == null || notimes == null || outDir == null)
        {
            Console.WriteLine("нужны --refs=, --notimes=, --out=");
            return 2;
        }
        try
        {
            Directory.CreateDirectory(outDir);
            string res = BecquerelMonitor.Properties.Resources.ERRMissingStartDateTime;
            int brace = res.IndexOf("{0}", StringComparison.Ordinal);
            voicePrefix = brace > 0 ? res.Substring(0, brace) : res;
            Console.WriteLine("голос A207 узнаётся по началу: «" + voicePrefix + "»");
            dm = DocumentManager.GetInstance();

            int idx = 0;
            foreach (string r in refs.Split(';').Where(s => s.Length > 0))
            {
                idx++;
                Reference(r, Path.Combine(outDir, "ref" + idx.ToString(Inv)));
            }
            NoTimes(notimes);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
        Console.WriteLine();
        Console.WriteLine("итог: дефектов " + defect.ToString(Inv) + ", несработавших контролей " + control.ToString(Inv));
        if (control > 0) return 3;
        return defect == 0 ? 0 : 1;
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
        return e.GetType().Name + ": " + e.Message;
    }

    static string Instant(DateTime t)
    {
        return t.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", Inv) + " (" + t.Kind + ")";
    }

    // Элемент без дочерних по имени внутри родителя — заменить (replacement) или убрать (null).
    static string Patch(string text, string parent, string name, string replacement)
    {
        int from = 0;
        if (parent != null)
        {
            from = text.IndexOf("<" + parent + ">", StringComparison.Ordinal);
            if (from < 0) throw new Exception("нет <" + parent + ">");
        }
        var rx = new Regex(@"[ \t]*<" + name + @"(?:\s*/>|>[^<]*</" + name + @">)\r?\n?");
        Match m = rx.Match(text, from);
        if (!m.Success) throw new Exception("нет <" + name + "> после " + (parent ?? "начала"));
        string indent = Regex.Match(m.Value, @"^[ \t]*").Value;
        string nl = m.Value.EndsWith("\r\n") ? "\r\n" : (m.Value.EndsWith("\n") ? "\n" : "");
        return text.Substring(0, m.Index) + (replacement == null ? "" : indent + replacement + nl) + text.Substring(m.Index + m.Length);
    }

    // Блок с дочерними (вся карточка пробы) — убрать.
    static string RemoveBlock(string text, string name)
    {
        var rx = new Regex(@"[ \t]*<" + name + @">[\s\S]*?</" + name + @">\r?\n?");
        Match m = rx.Match(text);
        if (!m.Success) throw new Exception("нет блока <" + name + ">");
        return text.Substring(0, m.Index) + text.Substring(m.Index + m.Length);
    }

    sealed class Opened
    {
        public DocEnergySpectrum Doc;
        public string Error;
        public int Voices;
        public string VoiceText = "";
    }

    static Opened Capture(Func<DocEnergySpectrum> act)
    {
        var o = new Opened();
        TextWriter err = Console.Error;
        var cap = new StringWriter(Inv);
        Console.SetError(cap);
        try { o.Doc = act(); }
        catch (Exception ex) { o.Error = Inner(ex); }
        finally { Console.SetError(err); }
        string[] lines = cap.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var a207 = lines.Where(l => l.Contains(voicePrefix)).ToArray();
        o.Voices = a207.Length;
        o.VoiceText = string.Join(" | ", a207.Select(l => l.Trim()));
        return o;
    }

    static Opened Open(string path)
    {
        return Capture(() => dm.OpenDocument(path));
    }

    static void Drop(DocEnergySpectrum d)
    {
        if (d == null) return;
        dm.DocumentList.Remove(d);
        try { d.Dispose(); } catch { }
    }

    static double Co(ResultData d) { return MeasurementResultManager.DecayToSamplingFactor(5.2714, d.SampleInfo.Time, d.StartTime, d.EndTime); }
    static double Cs(ResultData d) { return MeasurementResultManager.DecayToSamplingFactor(30.08, d.SampleInfo.Time, d.StartTime, d.EndTime); }

    static void Print(string title, ResultData d, int voices, string voiceText)
    {
        double T = d.EnergySpectrum.MeasurementTime;
        Console.WriteLine(title + ":");
        Console.WriteLine("    проба " + Instant(d.SampleInfo.Time) + ", начало " + Instant(d.StartTime) + ", конец " + Instant(d.EndTime));
        Console.WriteLine("    конец − начало " + (d.EndTime - d.StartTime).TotalSeconds.ToString("F3", Inv) + " с (полное " + T.ToString("R", Inv)
            + "); в файле: начало " + d.StartTimeInFile + ", конец " + d.EndTimeInFile + ", проба " + d.SampleInfo.TimeInFile);
        Console.WriteLine("    Co-60 ×" + Co(d).ToString("F6", Inv) + ", Cs-137 ×" + Cs(d).ToString("F6", Inv)
            + "; голосов A207 " + voices.ToString(Inv) + (voices > 0 ? " (" + Short(voiceText) + ")" : ""));
    }

    static void Judge(string what, bool ok, bool isControl)
    {
        Console.WriteLine("    " + what + ": " + (ok ? "да" : (isControl ? "НЕТ  ← КОНТРОЛЬ НЕ СРАБОТАЛ" : "НЕТ  ← ДЕФЕКТ")));
        if (!ok)
        {
            if (isControl) control++;
            else defect++;
        }
    }

    // Состав записи: тот же текст с замаскированными значениями трёх времён.
    static string Shape(string text)
    {
        text = Regex.Replace(text, @"<(StartTime|EndTime|Time)>[^<]*</\1>", "<$1>…</$1>");
        return text;
    }

    static void Reference(string reference, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var enc = new UTF8Encoding(false);
        string r0 = File.ReadAllText(reference);
        Console.WriteLine();
        Console.WriteLine("================ ссылка: " + reference);

        string p0 = Path.Combine(outDir, "H0.xml");
        File.WriteAllText(p0, r0, enc);
        Opened h0 = Open(p0);
        if (h0.Doc == null) { Console.WriteLine("H0: ОТКАЗ — " + Short(h0.Error)); control++; return; }
        ResultData d0 = h0.Doc.ActiveResultData;
        Print("H0 все три времени в файле", d0, h0.Voices, h0.VoiceText);
        double T = d0.EnergySpectrum.MeasurementTime;
        DateTime end = d0.EndTime;
        DateTime expected = end.AddSeconds(-T);
        double co0 = Co(d0), cs0 = Cs(d0);
        Console.WriteLine("    ожидаемое начало без начала и пробы: конец − T = " + Instant(expected));
        string saved0 = Path.Combine(outDir, "H0_saved.xml");
        h0.Doc.Filename = saved0;
        Judge("H0 сохранён", dm.SaveDocument(h0.Doc), true);
        Drop(h0.Doc);

        string noStart = Patch(r0, "ResultData", "StartTime", null);
        string h1 = Patch(noStart, "SampleInfo", "Time", null);
        string h2 = RemoveBlock(noStart, "SampleInfo");
        string h3 = Patch(h1, "ResultData", "EndTime", null);
        string h5 = noStart;

        // H1 и H2 — правка.
        string p1 = Path.Combine(outDir, "H1.xml");
        File.WriteAllText(p1, h1, enc);
        foreach (var c in new[] { Tuple.Create("H1 начала и пробы нет, конец есть", p1, h1), Tuple.Create("H2 начала и карточки пробы нет, конец есть", Path.Combine(outDir, "H2.xml"), h2) })
        {
            if (c.Item2 != p1) File.WriteAllText(c.Item2, c.Item3, enc);
            Opened o = Open(c.Item2);
            if (o.Doc == null) { Console.WriteLine(c.Item1 + ": ОТКАЗ — " + Short(o.Error)); control++; continue; }
            ResultData d = o.Doc.ActiveResultData;
            Print(c.Item1, d, o.Voices, o.VoiceText);
            Judge("начало = конец − T", d.StartTime == expected && d.StartTime.Kind == end.Kind, false);
            Judge("проба = начало", d.SampleInfo.Time == d.StartTime, false);
            Judge("конец — из файла", d.EndTime == end, true);
            Judge("голоса A207 нет", o.Voices == 0, false);
            Console.WriteLine("    множитель против H0: Co-60 " + (Co(d) / co0 - 1.0).ToString("+0.000000;-0.000000", Inv)
                + ", Cs-137 " + (Cs(d) / cs0 - 1.0).ToString("+0.000000;-0.000000", Inv) + " (относительно)");
            Judge("Co-60 не дальше 0.1 % от H0", Math.Abs(Co(d) / co0 - 1.0) < 1e-3, false);

            if (c.Item2 == p1)
            {
                // Подражание продолжению: начало сохраняется, конец — по часам.
                double years = (Now - d.StartTime).TotalDays / 365.25;
                double csCont = MeasurementResultManager.DecayToSamplingFactor(30.08, d.SampleInfo.Time, d.StartTime, Now);
                Console.WriteLine("    продолжение (подражание: начало то же, конец = «сейчас»): набор " + years.ToString("F2", Inv)
                    + " лет, Cs-137 ×" + csCont.ToString("F4", Inv));
                Judge("продолжение — не от 1970 (набор короче 50 лет)", years < 50.0, false);

                // Сохранение и повторное открытие.
                string saved = Path.Combine(outDir, "H1_saved.xml");
                o.Doc.Filename = saved;
                bool s = dm.SaveDocument(o.Doc);
                Judge("H1 сохранён", s, true);
                string text = File.ReadAllText(saved);
                Console.WriteLine("    в сохранённом: " + string.Join(" ", Regex.Matches(text, @"<(StartTime|EndTime|Time)>[^<]*</\1>").Cast<Match>().Select(m => m.Value)));
                string text0 = File.ReadAllText(saved0);
                Judge("состав записи = H0 (значения трёх времён замаскированы)", Shape(text) == Shape(text0), true);
                // Документ уже носит имя сохранённого файла — закрыть, иначе второе открытие отказывает.
                DateTime sampleH1 = d.SampleInfo.Time;
                Drop(o.Doc);
                o.Doc = null;
                Opened re = Open(saved);
                if (re.Doc == null) { Console.WriteLine("    повторное открытие: ОТКАЗ — " + Short(re.Error)); control++; }
                else
                {
                    ResultData dr = re.Doc.ActiveResultData;
                    Print("    H1 сохранённый, открыт снова", dr, re.Voices, re.VoiceText);
                    Judge("после круга начало = конец − T", dr.StartTime == expected, false);
                    Judge("после круга конец и проба те же", dr.EndTime == end && dr.SampleInfo.Time == sampleH1, true);
                    Drop(re.Doc);
                }
            }
            Drop(o.Doc);
        }

        // H3 — контроль: ни начала, ни пробы, ни конца.
        {
            string p3 = Path.Combine(outDir, "H3.xml");
            File.WriteAllText(p3, h3, enc);
            Opened o = Open(p3);
            if (o.Doc == null) { Console.WriteLine("H3: ОТКАЗ — " + Short(o.Error)); control++; }
            else
            {
                ResultData d = o.Doc.ActiveResultData;
                Print("H3 начала, пробы и конца нет (контроль A207)", d, o.Voices, o.VoiceText);
                Judge("начало = проба = 1970-01-01", d.StartTime == ResultData.UnknownStartTime && d.SampleInfo.Time == ResultData.UnknownStartTime, true);
                Judge("конец = 1970 + T", d.EndTime == ResultData.UnknownStartTime.AddSeconds(T), true);
                Judge("голос A207 один", o.Voices == 1, true);
                Drop(o.Doc);
            }
        }

        // H5 — контроль: правило П269 (проба есть) не тронуто.
        {
            string p5 = Path.Combine(outDir, "H5.xml");
            File.WriteAllText(p5, h5, enc);
            Opened o = Open(p5);
            if (o.Doc == null) { Console.WriteLine("H5: ОТКАЗ — " + Short(o.Error)); control++; }
            else
            {
                ResultData d = o.Doc.ActiveResultData;
                DateTime sample = d.SampleInfo.Time;
                DateTime want = sample > expected ? expected : sample;
                Print("H5 начала нет, проба и конец есть (контроль П269)", d, o.Voices, o.VoiceText);
                Judge("начало = min(проба, конец − T)", d.StartTime == want, true);
                Judge("голоса A207 нет", o.Voices == 0, true);
                Drop(o.Doc);
            }
        }

        // LoadDocument(H1) в документ H0.
        {
            Opened host = Open(p0);
            if (host.Doc == null) { Console.WriteLine("LoadDocument: хозяин не открылся — " + Short(host.Error)); control++; }
            else
            {
                ResultDataFile f = null;
                Opened l = Capture(() => { f = dm.LoadDocument(host.Doc, p1); return host.Doc; });
                if (f == null) { Console.WriteLine("LoadDocument(H1): ОТКАЗ — " + Short(l.Error)); control++; }
                else
                {
                    ResultData d = f.ResultDataList[0];
                    Print("LoadDocument(H1)", d, l.Voices, l.VoiceText);
                    Judge("начало = конец − T, проба = начало", d.StartTime == expected && d.SampleInfo.Time == expected, false);
                    Judge("голоса A207 нет", l.Voices == 0, false);
                }
                Drop(host.Doc);
            }
        }

        // EfficiencyCurveIo.LoadResultData(H1). Прибора ссылки может не быть в конфигурации
        // (старый файл) — тогда явная замена первым прибором конфигурации: время от неё не зависит.
        try
        {
            string fallback = null;
            Match g = Regex.Match(r0, @"<DeviceConfigReference>[\s\S]*?<Guid>([^<]+)</Guid>");
            if (!g.Success || !DeviceConfigManager.GetInstance().DeviceConfigMap.ContainsKey(g.Groups[1].Value))
            {
                fallback = DeviceConfigManager.GetInstance().DeviceConfigList[0].Guid;
                Console.WriteLine("EfficiencyCurveIo: прибора ссылки нет в конфигурации, замена — " + fallback);
            }
            ResultData d = EfficiencyCurveIo.LoadResultData(p1, 0, fallback);
            Print("EfficiencyCurveIo.LoadResultData(H1)", d, 0, "");
            Judge("начало = конец − T, проба = начало", d.StartTime == expected && d.SampleInfo.Time == expected, false);
        }
        catch (Exception ex) { Console.WriteLine("EfficiencyCurveIo.LoadResultData(H1): ОТКАЗ — " + Short(Inner(ex))); control++; }

        // FillAbsentTimes без полного времени — контроль A207.
        MethodInfo read = typeof(DocumentManager).GetMethod("ReadResultDataFile", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo fill = typeof(DocumentManager).GetMethod("FillAbsentTimes", BindingFlags.NonPublic | BindingFlags.Static);
        if (read == null || fill == null) { Console.WriteLine("FillAbsentTimes/ReadResultDataFile не найдены"); control++; return; }
        foreach (double t in new[] { 0.0, double.NaN, -5.0, double.PositiveInfinity, 1e13 })
        {
            var file = (ResultDataFile)read.Invoke(null, new object[] { p1 });
            file.ResultDataList[0].EnergySpectrum.MeasurementTime = t;
            int unknown = (int)fill.Invoke(null, new object[] { file });
            ResultData d = file.ResultDataList[0];
            bool ok = d.StartTime == ResultData.UnknownStartTime && d.SampleInfo.Time == ResultData.UnknownStartTime && unknown == 1;
            Console.WriteLine("FillAbsentTimes(H1), полное время " + t.ToString("R", Inv) + ": начало " + Instant(d.StartTime)
                + ", проба " + Instant(d.SampleInfo.Time) + ", конец " + Instant(d.EndTime) + ", неизвестных " + unknown.ToString(Inv));
            Judge("1970 и счёт 1 (контроль A207)", ok, true);
        }
        {
            var file = (ResultDataFile)read.Invoke(null, new object[] { p1 });
            int unknown = (int)fill.Invoke(null, new object[] { file });
            ResultData d = file.ResultDataList[0];
            Console.WriteLine("FillAbsentTimes(H1), полное время из файла: начало " + Instant(d.StartTime) + ", неизвестных " + unknown.ToString(Inv));
            Judge("начало = конец − T, счёт 0", d.StartTime == expected && unknown == 0, false);
        }
    }

    static void NoTimes(string path)
    {
        Console.WriteLine();
        Console.WriteLine("================ настоящий файл без всех трёх времён: " + path);
        Opened o = Open(path);
        if (o.Doc == null) { Console.WriteLine("ОТКАЗ — " + Short(o.Error)); control++; return; }
        ResultData d = o.Doc.ActiveResultData;
        Print("N0 как есть", d, o.Voices, o.VoiceText);
        Judge("начало = проба = 1970-01-01 (A207, конца нет — выводить не из чего)", d.StartTime == ResultData.UnknownStartTime && d.SampleInfo.Time == ResultData.UnknownStartTime, true);
        Judge("голос A207 один", o.Voices == 1, true);
        double years = (Now - d.StartTime).TotalDays / 365.25;
        Console.WriteLine("    продолжение (подражание: начало то же, конец = «сейчас»): набор " + years.ToString("F2", Inv)
            + " лет, Cs-137 ×" + MeasurementResultManager.DecayToSamplingFactor(30.08, d.SampleInfo.Time, d.StartTime, Now).ToString("F4", Inv)
            + " — у такого файла правка не действует (конца нет)");
        Drop(o.Doc);
    }
}
