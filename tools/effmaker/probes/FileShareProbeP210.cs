// Проба `T266` (полоса П210, 02.10.2026): как менеджеры-одиночки и документная
// прослойка ОТКРЫВАЮТ НА ЧТЕНИЕ свои файлы — конфиг, библиотеку нуклидов,
// конфигурации приборов и ROI, файл спектра, файл фона.
//
// Дефект, который она мерит: `new FileStream(путь, FileMode.Open)` без
// `FileAccess` — это доступ на ЗАПИСЬ (умолчание `FileAccess.ReadWrite`) и
// `FileShare.Read`. Следствия: файл с атрибутом «только чтение» не
// открывается вовсе; второй читатель тем же приёмом (второй экземпляр
// BecqMoni, параллельная проба) получает `IOException`; любой держатель с
// правом записи (синхронизация облака, редактор) тоже.
//
// ЗАПУСК. Проба кладётся в каталог, где лежат `BecquerelMonitor.exe` и
// `config\` (менеджеры берут пути от каталога входной сборки, `Package.AppDir`),
// и зовётся оттуда:
//
//   FileShareProbeP210.exe --mode=matrix --spec=<спектр.xml>
//       таблица «цель × сцена»: цели — global, nuclide, device, roi, open,
//       load, bg; сцены — plain (без помех), readonly (атрибут «только
//       чтение»), holdRW_shRW (держатель: запись, делит чтение и запись —
//       так держат файл синхронизаторы и редакторы), holdRW_shR (держатель —
//       ПРЕЖНИЙ приём чтения самого приложения: второй экземпляр BecqMoni),
//       holdR_shR (держатель — простой читатель, `File.OpenRead`),
//       holdRW_shNone (держатель не делит ничего — КОНТРОЛЬ: обязан отказать
//       при любом коде, иначе проба ничего не держит).
//       Код возврата: 0 — все сцены, кроме контроля, прошли и контроль
//       отказал у всех целей; 1 — есть отказ в рабочей сцене; 3 — контроль
//       НЕ отказал (держатель не держит — замер пуст).
//
//   FileShareProbeP210.exe --mode=loop --seconds=<N>
//       N секунд подряд: сбросить и заново прочитать главный конфиг
//       (`GlobalConfigManager.LoadConfigFile`), считая отказы. Запускается
//       несколькими процессами СРАЗУ — так мерится «два процесса читают
//       конфиг одновременно». Печатает «загрузок K, отказов F»; код 0 при F=0.
//
// Без окон: отказ загрузки менеджеры бросают (`AppUi.HasWindows = false`),
// поэтому «прочитал / не прочитал» здесь видно исключением или счётом.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

static class FileShareProbeP210
{
    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string mode = Arg(args, "--mode=", "matrix");
        Console.WriteLine(ProbeTargetFramework.Describe());
        Console.WriteLine("приложение: " + typeof(GlobalConfigManager).Assembly.Location
            + "  (" + File.GetLastWriteTime(typeof(GlobalConfigManager).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ")");
        try
        {
            if (mode == "loop")
            {
                return Loop(int.Parse(Arg(args, "--seconds=", "10"), CultureInfo.InvariantCulture));
            }
            if (mode == "save")
            {
                return Save();
            }
            return Matrix(Arg(args, "--spec=", null));
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
            if (a.StartsWith(key, StringComparison.Ordinal))
            {
                return a.Substring(key.Length);
            }
        }
        return fallback;
    }

    // ---------------------------------------------------------------- save

    // Сохранение поверх НЕПРОЧИТАННОГО файла (`T266`, вторая половина):
    //  (а) библиотека нуклидов: держатель без разделения -> чтение отказывает ->
    //      держатель отпущен -> в памяти пустая библиотека -> SaveDefinitionFile.
    //      Старый код пишет её поверх файла человека (положительный контроль),
    //      новый отказывает и файл не трогает;
    //  (б) имя файла новой конфигурации прибора/ROI: файл на диске, не попавший
    //      в список (дубль GUID, битый, занятый), — занято ли его имя.
    //      `IsFilenameTaken` берётся отражением: в старой сборке его нет, и там
    //      печатается только «по списку свободно» — прежняя логика формы.
    static int Save()
    {
        string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
        string lib = Path.Combine(cfg, "NuclideDefinition.xml");
        int bad = 0;

        NuclideDefinitionManager m = NuclideDefinitionManager.GetInstance();
        string shaBefore = Sha(lib);
        bool loaded;
        using (new FileStream(lib, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            loaded = m.LoadDefinitionFile(true);
        }
        m.NuclideDefinitionFile = new NuclideDefinitionFile();
        bool saved = m.SaveDefinitionFile();
        string shaAfter = Sha(lib);
        bool intact = shaBefore == shaAfter;
        Console.WriteLine("(а) библиотека: чтение под держателем = " + loaded
            + ", SaveDefinitionFile = " + saved + ", файл " + (intact ? "ЦЕЛ" : "ПЕРЕПИСАН")
            + " (sha " + shaBefore.Substring(0, 12) + " -> " + shaAfter.Substring(0, 12)
            + ", длина " + new FileInfo(lib).Length.ToString(CultureInfo.InvariantCulture) + ")");
        if (!intact) bad++;

        bad += Taken("device", DeviceConfigManager.GetInstance(), Path.Combine(cfg, "device"),
            DeviceConfigManager.GetInstance().DeviceConfigList.Select(c => c.Filename));
        Console.WriteLine(bad == 0 ? "ИТОГ: поверх непрочитанного не пишется" : "ИТОГ: ДЕФЕКТ, мест " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    static int Taken(string what, object manager, string dir, IEnumerable<string> listed)
    {
        var inList = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);
        string[] orphans = Directory.GetFiles(dir, "*.xml").Select(Path.GetFileName).Where(n => !inList.Contains(n)).ToArray();
        if (orphans.Length == 0)
        {
            Console.WriteLine("(б) " + what + ": файлов вне списка нет — сцена пуста");
            return 0;
        }
        MethodInfo mi = manager.GetType().GetMethod("IsFilenameTaken");
        int bad = 0;
        foreach (string o in orphans)
        {
            string verdict = mi == null ? "метода IsFilenameTaken нет (старая сборка): форма сочла бы имя СВОБОДНЫМ"
                : ((bool)mi.Invoke(manager, new object[] { o }) ? "IsFilenameTaken = true (занято)" : "IsFilenameTaken = false — ДЕФЕКТ");
            Console.WriteLine("(б) " + what + ": «" + o + "» на диске, в списке нет; " + verdict);
            if (mi == null || verdict.Contains("ДЕФЕКТ")) bad++;
        }
        return bad;
    }

    static string Sha(string path)
    {
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var h = System.Security.Cryptography.SHA256.Create())
        {
            return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
        }
    }

    // ---------------------------------------------------------------- loop

    static int Loop(int seconds)
    {
        Stopwatch sw = Stopwatch.StartNew();
        int loads = 0, fails = 0;
        string firstFail = null;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            loads++;
            try
            {
                ResetGlobal();
                GlobalConfigManager.GetInstance();
            }
            catch (Exception ex)
            {
                fails++;
                if (firstFail == null)
                {
                    firstFail = Innermost(ex);
                }
            }
        }
        Console.WriteLine("pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
            + ": загрузок " + loads.ToString(CultureInfo.InvariantCulture)
            + ", отказов " + fails.ToString(CultureInfo.InvariantCulture)
            + (firstFail != null ? "; первый: " + firstFail : ""));
        return fails == 0 ? 0 : 1;
    }

    // -------------------------------------------------------------- matrix

    sealed class Target
    {
        public string Name;
        public Func<IEnumerable<string>> Files;
        public Func<string> Load;   // null — прочитал; иначе причина отказа
    }

    static int Matrix(string spec)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string cfg = Path.Combine(baseDir, "config");
        if (spec == null || !File.Exists(spec))
        {
            Console.WriteLine("нет спектра: --spec=<файл BecqMoni .xml>");
            return 2;
        }
        // Сперва подняты все менеджеры — открытие спектра зовёт их сам, и
        // сцена «держатель на спектре» не должна мерить заодно конфиги.
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        NuclideDefinitionManager.GetInstance();

        string deviceDir = Path.Combine(cfg, "device");
        // Сколько читается БЕЗ помех — то и эталон: в каталоге могут быть файлы,
        // которые не берутся по своей причине (дубль GUID), а не из-за доступа.
        int deviceFiles = DeviceConfigManager.GetInstance().DeviceConfigList.Count;

        var targets = new List<Target>
        {
            new Target
            {
                Name = "global",
                Files = () => new[] { Path.Combine(cfg, "BecquerelMonitor.xml") },
                Load = () => Try(() => { ResetGlobal(); GlobalConfigManager.GetInstance(); })
            },
            new Target
            {
                Name = "nuclide",
                Files = () => new[] { Path.Combine(cfg, "NuclideDefinition.xml") },
                Load = () =>
                {
                    bool ok = false;
                    string err = Try(() => { ok = NuclideDefinitionManager.GetInstance().LoadDefinitionFile(true); });
                    return err ?? (ok ? null : "LoadDefinitionFile = false");
                }
            },
            new Target
            {
                Name = "device",
                Files = () => Directory.GetFiles(deviceDir, "*.xml"),
                Load = () =>
                {
                    int n = -1;
                    string err = Try(() =>
                    {
                        DeviceConfigManager m = DeviceConfigManager.GetInstance();
                        SetField(m, "listLoaded", false);
                        m.LoadAllConfigFiles();
                        n = m.DeviceConfigList.Count;
                    });
                    return err ?? (n == deviceFiles ? null
                        : "загружено " + n.ToString(CultureInfo.InvariantCulture) + " из " + deviceFiles.ToString(CultureInfo.InvariantCulture));
                }
            },
            new Target
            {
                Name = "open",
                Files = () => new[] { spec },
                Load = () => Try(() =>
                {
                    DocumentManager dm = DocumentManager.GetInstance();
                    DocEnergySpectrum doc = dm.OpenDocument(spec);
                    if (doc == null) throw new InvalidOperationException("OpenDocument = null");
                    dm.CloseDocument(doc);
                })
            },
            new Target
            {
                Name = "load",
                Files = () => new[] { spec },
                Load = () => Try(() =>
                {
                    DocumentManager dm = DocumentManager.GetInstance();
                    ResultDataFile f = dm.LoadDocument(new DocEnergySpectrum(spec), spec);
                    if (f == null) throw new InvalidOperationException("LoadDocument = null");
                })
            },
            new Target
            {
                Name = "bg",
                Files = () => new[] { spec },
                Load = () => Try(() =>
                {
                    ResultData rd = new ResultData();
                    rd.BackgroundSpectrumPathname = spec;
                    DocumentManager.GetInstance().LoadBackgroundSpectrum(rd);
                    if (rd.BackgroundEnergySpectrum == null) throw new InvalidOperationException("фон не загружен (BackgroundEnergySpectrum = null)");
                })
            },
        };

        string[] scenes = { "plain", "readonly", "holdRW_shRW", "holdRW_shR", "holdR_shR", "holdRW_shNone" };
        int workFails = 0, controlPasses = 0;
        Console.WriteLine();
        Console.WriteLine("цель     | " + string.Join(" | ", scenes));
        foreach (Target t in targets)
        {
            var cells = new List<string>();
            var reasons = new List<string>();
            foreach (string scene in scenes)
            {
                string[] files = t.Files().ToArray();
                string err = RunScene(scene, files, t.Load);
                bool control = scene == "holdRW_shNone";
                if (err == null)
                {
                    cells.Add("ok");
                    if (control) controlPasses++;
                }
                else
                {
                    cells.Add("ОТКАЗ");
                    if (!control) workFails++;
                    reasons.Add("    " + scene + ": " + err);
                }
            }
            Console.WriteLine(t.Name.PadRight(8) + " | " + string.Join(" | ", cells));
            foreach (string r in reasons) Console.WriteLine(r);
        }
        Console.WriteLine();
        Console.WriteLine("рабочих отказов " + workFails.ToString(CultureInfo.InvariantCulture)
            + ", контроль (holdRW_shNone) прошёл у " + controlPasses.ToString(CultureInfo.InvariantCulture)
            + " целей из " + targets.Count.ToString(CultureInfo.InvariantCulture) + " (обязан у 0)");
        if (controlPasses > 0) return 3;
        return workFails == 0 ? 0 : 1;
    }

    static string RunScene(string scene, string[] files, Func<string> load)
    {
        var holders = new List<FileStream>();
        try
        {
            switch (scene)
            {
                case "readonly":
                    foreach (string f in files) File.SetAttributes(f, File.GetAttributes(f) | FileAttributes.ReadOnly);
                    break;
                case "holdRW_shRW":
                    foreach (string f in files) holders.Add(new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
                    break;
                case "holdRW_shR":
                    foreach (string f in files) holders.Add(new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.Read));
                    break;
                case "holdR_shR":
                    foreach (string f in files) holders.Add(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read));
                    break;
                case "holdRW_shNone":
                    foreach (string f in files) holders.Add(new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                    break;
            }
            return load();
        }
        finally
        {
            foreach (FileStream h in holders) h.Dispose();
            if (scene == "readonly")
            {
                foreach (string f in files) File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly);
            }
        }
    }

    static string Try(Action a)
    {
        try
        {
            a();
            return null;
        }
        catch (Exception ex)
        {
            return Innermost(ex);
        }
    }

    static string Innermost(Exception ex)
    {
        Exception e = ex;
        while (e.InnerException != null) e = e.InnerException;
        string msg = e.Message.Replace("\r", " ").Replace("\n", " ");
        if (msg.Length > 160) msg = msg.Substring(0, 160) + "…";
        return e.GetType().Name + ": " + msg;
    }

    static void ResetGlobal()
    {
        FieldInfo inst = typeof(GlobalConfigManager).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        SetField(inst.GetValue(null), "isLoaded", false);
    }

    static void SetField(object o, string name, object value)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) throw new MissingFieldException(o.GetType().Name, name);
        f.SetValue(o, value);
    }
}
