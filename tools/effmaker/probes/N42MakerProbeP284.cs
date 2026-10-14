// Проба полосы П284 (14.10.2026): ЭКСПОРТ N42 — производитель и модель по типу
// прибора и предупреждение, называющее потери. Решение Amber 14.10.2026
// вопросником, дословно: «Производитель по прибору + назвать потери (Рекомендую)».
// Журнал — handover/handover-2026-10-14-p284-n42-maker-losses.md.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут
// пути от каталога входной сборки). Свой config\ — КОПИЯ конфигурации человека.
// Проба зовёт приложение только тем, что есть И ДО, И ПОСЛЕ правки
// (`DocumentManager.WriteN42File`, `DocumentManager.N42ExportWarning`), поэтому
// один и тот же exe пробы работает на обеих сборках — «до» служит контролем.
//
//   N42MakerProbeP284.exe --mode=dump --dirs=<кат1;кат2> --out=<кат> [--limit=N]
//       каждый файл ResultDataFile каталогов (рекурсивно) — «Открыть», затем экспорт
//       N42 дверью меню без диалога в <out>\NNNN.n42; плюс документы, собранные
//       программно на каждом из шести типов приборов и на ненайденном приборе, —
//       <out>\synth_<тип>.n42. Печать: таблица «тип прибора → производитель /
//       модель», сколько документов получили бы предупреждение. Перечень —
//       <out>\manifest.tsv (номер, тип, найден ли прибор, имя конфигурации,
//       производитель, модель, предупреждение, путь).
//   N42MakerProbeP284.exe --mode=warn
//       предупреждение: текст на en и ru (что названо), плечи условия показа —
//       решение Amber 14.10.2026 «Как раньше — только при кривой»: говорит
//       ТОЛЬКО документ с кривой эффективности; документ без потерь и документ с
//       любой ДРУГОЙ потерей (карточка, калибровка разрешения, прибор, серийный
//       номер) молчат. Первая редакция П284 (говорила при любой потере) на этих
//       плечах даёт расхождения — она и служит контролем.
//   N42MakerProbeP284.exe --mode=import --dir=<кат с *.n42> --out=<файл.tsv>
//       каждый *.n42 каталога — импорт в новый документ ОБЕИМИ дверями приложения
//       (N42 и SpecUtils); в tsv — отпечаток каждого спектра (отсчёты, шкала в
//       трёх каналах, времена, начало набора) и отдельным столбцом примечание.
//       Сверка «до/после» отпечатков — тем, что изготовитель в файле не меняет
//       прочтения ни одной дверью.
//
// Коды: 0 — прогон прошёл (в warn — все ожидания «после» сошлись); 1 — есть
// расхождения с ожиданием; 3 — положительный контроль не сработал; 5 — обрыв.
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

static class N42MakerProbeP284
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string mode = Arg(args, "--mode=", "dump");
        Console.WriteLine(ProbeTargetFramework.Describe());
        Console.WriteLine("приложение: " + typeof(DocumentManager).Assembly.Location + "  ("
            + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            dm = DocumentManager.GetInstance();
            Console.WriteLine("приборов в конфигурации: " + DeviceConfigManager.GetInstance().DeviceConfigList.Count.ToString(Inv));
            if (mode == "dump")
            {
                string outDir = Arg(args, "--out=", null);
                if (outDir == null) { Console.WriteLine("нужен --out=<каталог>"); return 2; }
                Directory.CreateDirectory(outDir);
                int limit = int.Parse(Arg(args, "--limit=", "100000"), Inv);
                return Dump(Collect(Arg(args, "--dirs=", "")).Take(limit).ToList(), outDir);
            }
            if (mode == "warn") return Warn();
            if (mode == "import") return Import(Arg(args, "--dir=", null), Arg(args, "--out=", null));
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

    static void Count(Dictionary<string, int> c, string k) { int n; c.TryGetValue(k, out n); c[k] = n + 1; }

    static string Short(string s) { if (s == null) return "null"; s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > 110 ? s.Substring(0, 110) + "…" : s; }

    static void WriteN42(DocEnergySpectrum doc, string path)
    {
        MethodInfo mi = typeof(DocumentManager).GetMethod("WriteN42File");
        if (mi == null) throw new MissingMethodException("DocumentManager.WriteN42File — нет в этой сборке");
        try { mi.Invoke(dm, new object[] { doc, path }); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    static string Warning(ResultDataFile f)
    {
        return DocumentManager.N42ExportWarning(f);
    }

    static string TypeOf(ResultData rd, out bool resolved, out string cfgName)
    {
        DeviceConfigInfo c = rd.DeviceConfig;
        resolved = c != null && !string.IsNullOrEmpty(c.Guid);
        cfgName = resolved ? c.Name : (rd.DeviceConfigReference != null ? rd.DeviceConfigReference.Name : null);
        if (!resolved) return "(прибор не найден)";
        return c.InputDeviceConfig == null ? "(нет InputDeviceConfig)" : c.InputDeviceConfig.GetType().Name;
    }

    static void MakerModel(string n42, out string maker, out string model)
    {
        string t = File.ReadAllText(n42);
        var mm = Regex.Match(t, "<RadInstrumentManufacturerName>([^<]*)</RadInstrumentManufacturerName>\\s*<RadInstrumentModelName>([^<]*)</RadInstrumentModelName>");
        maker = mm.Success ? mm.Groups[1].Value : "?";
        model = mm.Success ? mm.Groups[2].Value : "?";
    }

    // ------------------------------------------------------------------ dump

    static int Dump(List<string> files, string outDir)
    {
        var table = new SortedDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var manifest = new StringBuilder();
        manifest.Append("idx\ttype\tresolved\tconfig\tmanufacturer\tmodel\twarning\tsource\r\n");
        int done = 0, fail = 0, warned = 0, warnedWithCurve = 0;
        int idx = 0;
        Console.WriteLine("=== ЭКСПОРТ N42: файлов " + files.Count.ToString(Inv) + " ===");
        foreach (string path in files)
        {
            idx++;
            DocEnergySpectrum d = null;
            string n42 = Path.Combine(outDir, idx.ToString("D4", Inv) + ".n42");
            try
            {
                d = dm.OpenDocument(path);
                Record(d, n42, idx.ToString("D4", Inv), path, table, manifest, ref warned, ref warnedWithCurve);
                done++;
            }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine("  ОБРЫВ " + path + ": " + Short((ex.InnerException ?? ex).Message));
            }
            finally { Drop(d); }
        }

        Console.WriteLine();
        Console.WriteLine("=== ДОКУМЕНТЫ, СОБРАННЫЕ ПРОГРАММНО (по одному на тип прибора) ===");
        var synth = new List<Tuple<string, DeviceConfigInfo>>
        {
            Tuple.Create("AudioInputDevice", Cfg("P284 Audio", new AudioInputDeviceConfig())),
            Tuple.Create("AtomSpectraVCP", Cfg("P284 AtomSpectra", new AtomSpectraDeviceConfig())),
            Tuple.Create("RadiaCode", Cfg("P284 RadiaCode", new RadiaCodeDeviceConfig())),
            Tuple.Create("Obsidian", Cfg("P284 Obsidian", new ObsidianDeviceConfig())),
            Tuple.Create("AmplitudaUSB", Cfg("P284 AmplitudaUSB", new AmplitudaUsbDeviceConfig())),
            Tuple.Create("AmplitudaSerial", Cfg("P284 AmplitudaSerial", new AmplitudaSerialDeviceConfig())),
            // Прибор не найден: заготовка без Guid, в файле осталась ссылка с именем.
            Tuple.Create("unresolved_ref", (DeviceConfigInfo)null),
            // Прибор не найден, и ссылки нет вовсе.
            Tuple.Create("unresolved_none", (DeviceConfigInfo)null),
        };
        int s = 0;
        foreach (var k in synth)
        {
            DocEnergySpectrum d = null;
            try
            {
                d = Synth(k.Item2);
                if (k.Item1 == "unresolved_ref")
                {
                    d.ActiveResultData.DeviceConfig = new DeviceConfigInfo();
                    d.ActiveResultData.DeviceConfigReference = new DeviceConfigReference { Name = "P284 чужой прибор", Guid = Guid.NewGuid().ToString() };
                }
                else if (k.Item1 == "unresolved_none")
                {
                    d.ActiveResultData.DeviceConfig = new DeviceConfigInfo();
                    d.ActiveResultData.DeviceConfigReference = null;
                }
                string n42 = Path.Combine(outDir, "synth_" + k.Item1 + ".n42");
                Record(d, n42, "synth_" + k.Item1, "(программно: " + k.Item1 + ")", table, manifest, ref warned, ref warnedWithCurve);
                s++;
            }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine("  ОБРЫВ synth " + k.Item1 + ": " + Short((ex.InnerException ?? ex).ToString()));
            }
            finally { Drop(d); }
        }

        File.WriteAllText(Path.Combine(outDir, "manifest.tsv"), manifest.ToString(), new UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine("экспортировано файлов " + done.ToString(Inv) + ", программных " + s.ToString(Inv) + ", обрывов открытия " + fail.ToString(Inv));
        Console.WriteLine("предупреждение показалось бы: " + warned.ToString(Inv) + " из " + (done + s).ToString(Inv)
            + " (из них с кривой эффективности " + warnedWithCurve.ToString(Inv) + ")");
        Console.WriteLine("тип прибора → производитель / модель в выгруженном N42:");
        foreach (var kv in table)
        {
            foreach (var mm in kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                Console.WriteLine("    " + kv.Key + " → " + mm.Key + ": " + mm.Value.ToString(Inv));
            }
        }
        return 0;
    }

    static void Record(DocEnergySpectrum d, string n42, string id, string source,
                       SortedDictionary<string, Dictionary<string, int>> table, StringBuilder manifest,
                       ref int warned, ref int warnedWithCurve)
    {
        WriteN42(d, n42);
        string maker, model;
        MakerModel(n42, out maker, out model);
        bool resolved;
        string cfg;
        string type = TypeOf(d.ActiveResultData, out resolved, out cfg);
        // Имя конфигурации прибора — в ключ таблицы только у «запасной» записи, где
        // модель и есть это имя; у названных типов — нет (иначе таблица дробится).
        string mmKey = maker + " / " + (model == cfg && !string.IsNullOrEmpty(cfg) ? "<имя конфигурации>" : model);
        Dictionary<string, int> row;
        if (!table.TryGetValue(type, out row)) { row = new Dictionary<string, int>(); table[type] = row; }
        Count(row, mmKey);
        string w = Warning(d.ResultDataFile);
        bool curve = d.ResultDataFile.ResultDataList.Any(r => r != null && r.Efficiency != null);
        if (w != null) { warned++; if (curve) warnedWithCurve++; }
        manifest.Append(id).Append('\t').Append(type).Append('\t').Append(resolved ? "1" : "0").Append('\t')
                .Append(cfg ?? "").Append('\t').Append(maker).Append('\t').Append(model).Append('\t')
                .Append(w != null ? "1" : "0").Append('\t').Append(source).Append("\r\n");
    }

    static DeviceConfigInfo Cfg(string name, InputDeviceConfig input)
    {
        var dci = new DeviceConfigInfo();
        dci.Guid = Guid.NewGuid().ToString();
        dci.Name = name;
        dci.InputDeviceConfig = input;
        return dci;
    }

    static DocEnergySpectrum Synth(DeviceConfigInfo dci)
    {
        DocEnergySpectrum d = dm.CreateDocument();
        ResultData rd = d.ActiveResultData;
        if (dci != null)
        {
            rd.DeviceConfig = dci;
            rd.DeviceConfigReference = dci.CreateReference();
        }
        var es = new EnergySpectrum(1.0, 1024);
        var cal = new PolynomialEnergyCalibration();
        cal.PolynomialOrder = 2;
        cal.Coefficients = new double[] { -3.25, 2.9375, 1.25e-5 };
        es.EnergyCalibration = cal;
        long n = 0;
        var rnd = new Random(284);
        for (int i = 0; i < 1024; i++)
        {
            double c = 4000.0 * Math.Exp(-i / 180.0) + 9000.0 * Math.Exp(-0.5 * Math.Pow((i - 226) / 7.5, 2));
            es.Spectrum[i] = (int)Math.Round(c + rnd.NextDouble());
            n += es.Spectrum[i];
        }
        es.TotalPulseCount = n;
        es.ValidPulseCount = n;
        es.MeasurementTime = 3600.25;
        es.LiveTime = 3590.5;
        rd.EnergySpectrum = es;
        rd.StartTime = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Local);
        rd.EndTime = rd.StartTime.AddSeconds(3600.25);
        rd.SampleInfo.Time = rd.StartTime;
        return d;
    }

    // ------------------------------------------------------------------ import

    static int Import(string dir, string outFile)
    {
        if (dir == null || outFile == null) { Console.WriteLine("нужны --dir= и --out="); return 2; }
        var sb = new StringBuilder();
        sb.Append("file\tdoor\tidx\tfingerprint\tnote\r\n");
        int ok = 0, fail = 0;
        foreach (string f in Directory.GetFiles(dir, "*.n42").OrderBy(x => x, StringComparer.Ordinal))
        {
            foreach (string door in new[] { "n42", "su" })
            {
                DocEnergySpectrum d = null;
                try
                {
                    d = dm.CreateDocument();
                    if (door == "n42") dm.ImportDocumentN42(d, f);
                    else dm.ImportDocumentSpecUtils(d, f, 3600);
                    var list = d.ResultDataFile.ResultDataList;
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append(Path.GetFileName(f)).Append('\t').Append(door).Append('\t').Append(i.ToString(Inv)).Append('\t')
                          .Append(Fingerprint(list[i])).Append('\t')
                          .Append(list[i].SampleInfo == null || list[i].SampleInfo.Note == null ? "" : list[i].SampleInfo.Note.ToString().Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", " "))
                          .Append("\r\n");
                    }
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    sb.Append(Path.GetFileName(f)).Append('\t').Append(door).Append("\t-\tОТКАЗ ").Append(Short((ex.InnerException ?? ex).Message)).Append("\t\r\n");
                }
                finally { Drop(d); }
            }
        }
        File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("импортов " + ok.ToString(Inv) + ", отказов " + fail.ToString(Inv) + " → " + outFile);
        return 0;
    }

    static string Fingerprint(ResultData rd)
    {
        var s = new StringBuilder();
        Spec(s, "fg", rd.EnergySpectrum);
        Spec(s, "bg", rd.BackgroundEnergySpectrum);
        s.Append(" start=").Append((rd.StartTime.Kind == DateTimeKind.Utc ? rd.StartTime : rd.StartTime.ToUniversalTime()).ToString("o", Inv)).Append(' ');
        s.Append(" fwhm=").Append(rd.FwhmCalibration == null ? "нет" : rd.FwhmCalibration.GetType().Name);
        return s.ToString();
    }

    static void Spec(StringBuilder s, string tag, EnergySpectrum es)
    {
        s.Append(tag).Append('[');
        if (es == null) { s.Append("нет] "); return; }
        long sum = 0; ulong h = 1469598103934665603UL;
        unchecked { foreach (int x in es.Spectrum) { sum += x; h = (h ^ (uint)x) * 1099511628211UL; } }
        int n = es.NumberOfChannels;
        s.Append("n=").Append(n.ToString(Inv)).Append(" sum=").Append(sum.ToString(Inv)).Append(" h=").Append(h.ToString("x16", Inv))
         .Append(" real=").Append(es.MeasurementTime.ToString("R", Inv)).Append(" live=").Append(es.LiveTime.ToString("R", Inv))
         .Append(" total=").Append(es.TotalPulseCount.ToString(Inv));
        if (es.EnergyCalibration != null && n > 0)
        {
            foreach (int ch in new[] { 0, n / 2, n - 1 })
            {
                s.Append(" E").Append(ch.ToString(Inv)).Append('=').Append(es.EnergyCalibration.ChannelToEnergy(ch).ToString("R", Inv));
            }
        }
        s.Append("] ");
    }

    // ------------------------------------------------------------------ warn

    // Что текст обязан назвать: предметы потерь по коду писателя (журнал П284).
    static readonly string[] MustNameEn =
    {
        "efficiency curve", "detector geometry", "response matrix",
        "resolution (FWHM) calibration", "device configuration", "serial number",
        "sample information", "name", "location", "sampling date", "weight", "volume", "notes",
        "{0} of {1}"
    };

    static readonly string[] MustNameRu =
    {
        "кривая эффективности", "геометрия детектора", "матрице отклика",
        "калибровка разрешения", "конфигурации прибора", "серийный номер",
        "информация об образце", "имя", "место отбора", "дата отбора", "вес", "объём", "заметки",
        "{0} из {1}"
    };

    static int Warn()
    {
        int bad = 0, control = 0;
        Console.WriteLine("=== ТЕКСТ ПРЕДУПРЕЖДЕНИЯ ===");
        foreach (string ui in new[] { "en-US", "ru-RU" })
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(ui);
            string raw = BecquerelMonitor.Properties.Resources.MSGN42ExportLosesEfficiency;
            Console.WriteLine("--- " + ui + " (ресурс как есть):");
            Console.WriteLine(raw);
            string[] must = ui == "en-US" ? MustNameEn : MustNameRu;
            var missing = must.Where(m => raw.IndexOf(m, StringComparison.Ordinal) < 0).ToList();
            Console.WriteLine("названо " + (must.Length - missing.Count).ToString(Inv) + " из " + must.Length.ToString(Inv)
                + (missing.Count == 0 ? "" : "; НЕ названо: " + string.Join(", ", missing)));
            if (missing.Count > 0) bad++;
            bool vvoz = raw.IndexOf("ввоз", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("вывоз", StringComparison.OrdinalIgnoreCase) >= 0;
            if (vvoz) { Console.WriteLine("⛔ в тексте «ввоз/вывоз»"); bad++; }
        }
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");

        Console.WriteLine();
        Console.WriteLine("=== УСЛОВИЕ ПОКАЗА: документ из одного спектра, одна потеря за раз ===");
        // Ожидание — решение Amber 14.10.2026 «Как раньше — только при кривой»:
        // говорит только плечо с кривой; прочие потери текст называет, но окна
        // не поднимают. Плечо «без потерь» — положительный контроль тишины.
        var arms = new List<Tuple<string, Action<ResultData>, bool>>
        {
            Tuple.Create<string, Action<ResultData>, bool>("без потерь", rd => { }, false),
            Tuple.Create<string, Action<ResultData>, bool>("кривая эффективности", rd => rd.Efficiency = new EfficiencyConfigData(), true),
            Tuple.Create<string, Action<ResultData>, bool>("калибровка разрешения", rd => rd.FwhmCalibration = new SqrtFwhmCalibration(), false),
            Tuple.Create<string, Action<ResultData>, bool>("привязка к прибору", rd => rd.DeviceConfigReference = new DeviceConfigReference { Name = "P284", Guid = Guid.NewGuid().ToString() }, false),
            Tuple.Create<string, Action<ResultData>, bool>("серийный номер", rd => rd.EnergySpectrum.SerialNumber = "RC-103-000282", false),
            Tuple.Create<string, Action<ResultData>, bool>("имя образца", rd => rd.SampleInfo.Name = "проба", false),
            Tuple.Create<string, Action<ResultData>, bool>("место отбора", rd => rd.SampleInfo.Location = "сад", false),
            Tuple.Create<string, Action<ResultData>, bool>("заметки", rd => rd.SampleInfo.Note = "П284", false),
            Tuple.Create<string, Action<ResultData>, bool>("вес 0.5", rd => rd.SampleInfo.Weight = 0.5, false),
            Tuple.Create<string, Action<ResultData>, bool>("объём 0.45", rd => rd.SampleInfo.Volume = 0.45, false),
            Tuple.Create<string, Action<ResultData>, bool>("дата отбора на сутки раньше начала", rd => rd.SampleInfo.Time = rd.StartTime.AddDays(-1), false),
            Tuple.Create<string, Action<ResultData>, bool>("дата отбора на 0.5 с раньше начала", rd => rd.SampleInfo.Time = rd.StartTime.AddSeconds(-0.5), false),
        };
        foreach (var arm in arms)
        {
            ResultDataFile f = EmptyFile();
            arm.Item2(f.ResultDataList[0]);
            string w = Warning(f);
            bool spoke = w != null;
            bool ok = spoke == arm.Item3;
            Console.WriteLine("  " + (ok ? "сошлось" : "⛔ НЕ СОШЛОСЬ") + " — " + arm.Item1 + ": " + (spoke ? "говорит" : "молчит")
                + " (ждали: " + (arm.Item3 ? "говорит" : "молчит") + ")"
                + (spoke ? "; числа: «" + NumbersOf(w) + "»" : ""));
            if (!ok)
            {
                if (arm.Item1 == "без потерь") control++;
                else bad++;
            }
        }

        // Два спектра, кривая у одного: числа «1 из 2».
        ResultDataFile two = EmptyFile();
        two.ResultDataList.Add(EmptyData());
        two.ResultDataList[1].Efficiency = new EfficiencyConfigData();
        string w2 = Warning(two);
        bool ok2 = w2 != null && w2.Contains("1 of 2");
        Console.WriteLine("  " + (ok2 ? "сошлось" : "⛔ НЕ СОШЛОСЬ") + " — два спектра, кривая у одного: " + (w2 == null ? "молчит" : "«" + NumbersOf(w2) + "»"));
        if (!ok2) bad++;

        Console.WriteLine();
        Console.WriteLine(control > 0 ? "⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: документ без потерь не дал тишины — замер пуст"
            : (bad == 0 ? "ВСЕ ОЖИДАНИЯ СОШЛИСЬ" : "расхождений с ожиданием: " + bad.ToString(Inv)));
        return control > 0 ? 3 : (bad == 0 ? 0 : 1);
    }

    static string NumbersOf(string w)
    {
        var m = Regex.Match(w, "\\d+ of \\d+");
        return m.Success ? m.Value : "—";
    }

    // Спектр, у которого терять нечего: ни кривой, ни калибровки разрешения, ни
    // ссылки на прибор, ни серийного номера; карточка пуста, дата отбора = начало.
    static ResultData EmptyData()
    {
        var rd = new ResultData();
        rd.Efficiency = null;
        rd.FwhmCalibration = null;
        rd.DeviceConfigReference = null;
        rd.EnergySpectrum = new EnergySpectrum(1.0, 16);
        rd.EnergySpectrum.SerialNumber = null;
        rd.StartTime = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Local);
        rd.EndTime = rd.StartTime.AddSeconds(60);
        rd.SampleInfo = new SampleInfoData();
        rd.SampleInfo.Name = "";
        rd.SampleInfo.Location = "";
        rd.SampleInfo.Note = "";
        rd.SampleInfo.Time = rd.StartTime;
        return rd;
    }

    static ResultDataFile EmptyFile()
    {
        var f = new ResultDataFile();
        f.ResultDataList.Clear();
        f.ResultDataList.Add(EmptyData());
        return f;
    }
}
