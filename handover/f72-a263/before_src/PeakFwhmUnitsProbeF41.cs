// `A211`, полоса F41 05.09.2026: ЕДИНИЦА ШИРИНЫ ПИКА — ЧИСЛОМ, А НЕ СЛОВАМИ.
//
//     peakfwhmunitsprobef41 [--dump=<файл>] [--spectra=<каталог>]
//
// Что меряется. Решением Amber 05.09.2026 рядом с полем `Peak.FWHM` (оно в
// КАНАЛАХ) заведена дверь `Peak.FwhmKev(EnergyCalibration)` — та же ширина в
// кэВ. Проба показывает ЧИСЛОМ, что дверь отдаёт кэВ, что поле осталось
// канальным, и что чтение старых файлов спектра не сдвинулось ни на байт.
//
// ⛔ ОДИН И ТОТ ЖЕ ИСХОДНИК СОБИРАЕТСЯ ПРОТИВ ОБЕИХ СБОРОК — до правки и
//    после, — поэтому дверь зовётся ОТРАЖЕНИЕМ. Иначе слепок «до» пришлось бы
//    снимать другой пробой, и сравнение двух слепков ничего не значило бы:
//    разойтись они могли бы от разницы самих проб. Отсутствие метода — не
//    отказ, а разряд «сборка ДО правки», и он печатается.
//
// Устройство §1 (целость чтения). Каждый файл корпуса разбирается
// `XmlSerializer(typeof(ResultDataFile))` — ровно тем вызовом, каким его
// открывает `DocumentManager`, — и разобранный объект обходится отражением по
// ПОЛЯМ: все публичные свойства и поля, отсортированные по имени, вглубь до
// шестого уровня; длинные массивы (спектр — 16384 канала) сворачиваются в
// sha256 своего же слепка, а не выбрасываются. Слепок кладётся в файл, файл
// хэшируется. Сравнение «до/после» — побайтное сравнение двух слепков.
//
// ⚠ Слепок нарочно НЕ содержит ни путей, ни дат, ни отпечатка приложения:
//    иначе он расходился бы на каждой пересборке и не значил бы ничего.
//
// Устройство §2 (сцены). Сцена «1 кэВ = 1 канал» отвечает «сошлось» при ЛЮБОМ
// ответе — на ней однажды и проверяли, — поэтому она включена ОТДЕЛЬНО и
// помечена как ничего не различающая, а судят по сценам 0.25 и 4.0 кэВ/канал.
//
// Устройство §3 (положительный контроль). Проверке подставляется заведомо
// неверная калибровка и «ответ неверных проб» (поле, принятое за кэВ). Если
// проверка их не поймает, она не меряет ничего, и проба это скажет.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using BecquerelMonitor;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

static class PeakFwhmUnitsProbeF41
{
    static int failures;
    static readonly List<string> problems = new List<string>();

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string dumpPath = "peak_parse_dump_f41.txt";
        string spectraDir = Path.Combine("tools", "CORPUS", "corpus", "spectra");
        foreach (string a in args)
        {
            if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
            else if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
        }

        Console.WriteLine("=== ПРОБА F41 — `A211`: единица ширины пика ===");
        Assembly app = typeof(Peak).Assembly;
        Console.WriteLine("приложение: " + app.Location);
        Console.WriteLine("        sha256 = " + FileSha(app.Location).Substring(0, 16)
                          + "   записан " + new FileInfo(app.Location).LastWriteTime.ToString(
                              "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        MethodInfo mFwhmKev = typeof(Peak).GetMethod("FwhmKev", BindingFlags.Public | BindingFlags.Instance,
                                                     null, new[] { typeof(EnergyCalibration) }, null);
        bool after = mFwhmKev != null;
        Console.WriteLine("дверь Peak.FwhmKev(EnergyCalibration): " + (after ? "ЕСТЬ" : "НЕТ — сборка ДО правки"));

        // Копий расчёта в приложении быть не должно: две одинаковые формулы в
        // разных файлах — два места, где их можно развести.
        Type tDet = app.GetType("BecquerelMonitor.PeakDetector");
        MethodInfo mDup = tDet == null ? null : tDet.GetMethod(
            "FwhmKev", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(Peak), typeof(EnergyCalibration) }, null);
        Console.WriteLine("копия PeakDetector.FwhmKev(Peak,EnergyCalibration): "
                          + (mDup == null ? "нет" : "ЕСТЬ"));
        if (after && mDup != null)
        {
            Fail("§0", "расчёт ПШПВ в кэВ остался в ДВУХ местах: `Peak.FwhmKev` и частная копия в `PeakDetector`.");
        }

        ParseDump(spectraDir, dumpPath);

        if (after)
        {
            Scenes(mFwhmKev);
            PositiveControl(mFwhmKev);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("--- §2..§3 пропущены: двери нет, мерить нечего (это и есть разряд «сборка ДО») ---");
        }

        XmlReach();

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("СОШЛОСЬ");
            return 0;
        }
        Console.WriteLine("РАЗОШЛОСЬ, поводов " + failures.ToString(CultureInfo.InvariantCulture) + ":");
        foreach (string p in problems) Console.WriteLine("  " + p);
        return 1;
    }

    static void Fail(string where, string what)
    {
        failures++;
        problems.Add(where + ": " + what);
    }

    // ------------------------------------------------------------------
    // §1. Слепок разбора старых файлов спектра
    // ------------------------------------------------------------------
    static void ParseDump(string spectraDir, string dumpPath)
    {
        Console.WriteLine();
        Console.WriteLine("--- §1 Чтение старых файлов спектра: слепок разобранного объекта ПО ПОЛЯМ ---");
        if (!Directory.Exists(spectraDir))
        {
            Fail("§1", "каталога спектров нет: " + spectraDir + " (пробу запускают ИЗ КОРНЯ репозитория)");
            Console.WriteLine("ОТКАЗ: нет каталога " + spectraDir);
            return;
        }

        string[] files = Directory.GetFiles(spectraDir, "*.xml");
        Array.Sort(files, StringComparer.Ordinal);
        Console.WriteLine("файлов: " + files.Length.ToString(CultureInfo.InvariantCulture)
                          + "   каталог: " + spectraDir);

        var ser = new XmlSerializer(typeof(ResultDataFile));
        int ok = 0, bad = 0;
        var sb = new StringBuilder();
        foreach (string f in files)
        {
            string name = Path.GetFileName(f);
            sb.Append("### ").Append(name).Append('\n');
            try
            {
                object parsed;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    parsed = ser.Deserialize(fs);
                }
                Dump(sb, "", parsed, 0, new HashSet<object>(ReferenceComparer.Instance));
                ok++;
            }
            catch (Exception ex)
            {
                sb.Append("  ОТКАЗ РАЗБОРА: ").Append(ex.GetType().Name).Append(": ")
                  .Append(ex.Message).Append('\n');
                bad++;
            }
        }

        // ⚠ `\n` дословно и UTF-8 без BOM: слепок сравнивается ПОБАЙТНО, и
        //    перевод строки, выбранный платформой, добавил бы в сравнение
        //    переменную, к делу не относящуюся.
        File.WriteAllBytes(dumpPath, new UTF8Encoding(false).GetBytes(sb.ToString()));
        lines = sb.ToString().Split('\n').Length - 1;
        Console.WriteLine("разобрано: " + ok.ToString(CultureInfo.InvariantCulture)
                          + "   отказов: " + bad.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("строк слепка (членов объекта): " + lines.ToString(CultureInfo.InvariantCulture)
                          + "   из них меток, изготовленных прогоном: " + masked.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("слепок: " + Path.GetFullPath(dumpPath));
        Console.WriteLine("  байт   = " + new FileInfo(dumpPath).Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  sha256 = " + FileSha(dumpPath));
        if (bad > 0) Fail("§1", "не разобрано файлов: " + bad.ToString(CultureInfo.InvariantCulture));
        if (ok == 0) Fail("§1", "не разобрано НИ ОДНОГО файла — слепок пуст и сравнивать нечего");
    }

    sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
        public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
    }

    const int MaxDepth = 6;
    const int InlineElements = 64;
    static int masked;
    static int lines;
    static readonly DateTime ProcStart = DateTime.Now;

    static void Dump(StringBuilder sb, string prefix, object value, int depth, HashSet<object> seen)
    {
        if (value == null) { sb.Append(prefix).Append(" = null\n"); return; }

        // ⛔ ЧАСТЬ МЕТОК ВРЕМЕНИ ИЗГОТОВЛЕНА САМИМ ПРОГОНОМ, А НЕ ПРОЧИТАНА ИЗ
        //    ФАЙЛА: `ResultData.EndTime`, `DeviceConfigInfo.LastUpdated`,
        //    `ROIConfigData.LastUpdated` ставятся конструктором в
        //    `DateTime.Now`, а корпусные файлы этих элементов не несут.
        //    Измерено: два прогона ОДНОГО И ТОГО ЖЕ бинаря разошлись — сперва
        //    на 690 строках, после маскировки по имени ещё на 174. Правило
        //    поэтому взято НЕ ПО ИМЕНИ, а по существу: метка ПОЗЖЕ старта
        //    процесса прочитана из файла быть не могла. Число замаскированных
        //    печатается — слепок, расходящийся сам с собой, не доказывает
        //    ничего, и молчаливая маска была бы тем же самым.
        if (value is DateTime && (DateTime)value >= ProcStart)
        {
            masked++;
            sb.Append(prefix).Append(" = <изготовлено прогоном, замаскировано>\n");
            return;
        }

        Type t = value.GetType();
        if (t.IsPrimitive || t.IsEnum || value is string || value is decimal || value is DateTime
            || value is TimeSpan || value is Guid)
        {
            sb.Append(prefix).Append(" = ").Append(Scalar(value)).Append('\n');
            return;
        }

        if (depth >= MaxDepth) { sb.Append(prefix).Append(" = <глубже ").Append(MaxDepth).Append(">\n"); return; }
        if (!seen.Add(value)) { sb.Append(prefix).Append(" = <уже встречалось>\n"); return; }

        var list = value as IEnumerable;
        if (list != null)
        {
            var items = new List<object>();
            foreach (object o in list) items.Add(o);
            sb.Append(prefix).Append(".Count = ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (items.Count > InlineElements)
            {
                var inner = new StringBuilder();
                for (int i = 0; i < items.Count; i++)
                    Dump(inner, "[" + i.ToString(CultureInfo.InvariantCulture) + "]", items[i], depth + 1, seen);
                sb.Append(prefix).Append(".sha256 = ").Append(TextSha(inner.ToString())).Append('\n');
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                    Dump(sb, prefix + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", items[i], depth + 1, seen);
            }
            return;
        }

        var members = new List<string>();
        var byName = new Dictionary<string, Func<object>>(StringComparer.Ordinal);
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || !p.CanRead) continue;
            PropertyInfo pc = p;
            object target = value;
            if (byName.ContainsKey(pc.Name)) continue;
            members.Add(pc.Name);
            byName[pc.Name] = () => pc.GetValue(target, null);
        }
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            FieldInfo fc = f;
            object target = value;
            if (byName.ContainsKey(fc.Name)) continue;
            members.Add(fc.Name);
            byName[fc.Name] = () => fc.GetValue(target);
        }
        members.Sort(StringComparer.Ordinal);

        foreach (string name in members)
        {
            object v;
            try { v = byName[name](); }
            catch (Exception ex)
            {
                sb.Append(prefix).Append('.').Append(name).Append(" = <отказ: ")
                  .Append(ex.InnerException != null ? ex.InnerException.GetType().Name : ex.GetType().Name)
                  .Append(">\n");
                continue;
            }
            Dump(sb, prefix + "." + name, v, depth + 1, seen);
        }
    }

    static string Scalar(object v)
    {
        if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
        if (v is float) return ((float)v).ToString("R", CultureInfo.InvariantCulture);
        if (v is DateTime) return ((DateTime)v).ToString("O", CultureInfo.InvariantCulture);
        if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
        return v.ToString();
    }

    // ------------------------------------------------------------------
    // §2. Сцены с известной калибровкой
    // ------------------------------------------------------------------
    static double Call(MethodInfo m, Peak p, EnergyCalibration cal)
    {
        return (double)m.Invoke(p, new object[] { cal });
    }

    static EnergyCalibration Linear(double kevPerChannel)
    {
        var c = new PolynomialEnergyCalibration();
        c.PolynomialOrder = 1;
        c.Coefficients = new double[] { 0.0, kevPerChannel };
        return c;
    }

    static void Scenes(MethodInfo m)
    {
        Console.WriteLine();
        Console.WriteLine("--- §2 FwhmKev на ИЗВЕСТНОЙ калибровке ---");
        Console.WriteLine("сцена                          | ПШПВ,кан | ожид,кэВ | вышло,кэВ |  поле после | итог");

        Scene(m, "линейная 0.25 кэВ/канал", Linear(0.25), 400, 40.0, 10.0, true);
        Scene(m, "линейная 4.00 кэВ/канал", Linear(4.00), 400, 40.0, 160.0, true);
        Scene(m, "линейная 1.00 кэВ/канал", Linear(1.00), 400, 40.0, 40.0, false);

        // Кубическая: ожидание выписано АНАЛИТИЧЕСКИ, а не взято у того же кода.
        //   E(n) = 0.2·n + 1e-7·n³ ; канал 1000, ПШПВ 200 → края 900 и 1100
        //   E(1100) − E(900) = 0.2·200 + 1e-7·(1100³ − 900³)
        //                    = 40 + 1e-7·(1 331 000 000 − 729 000 000) = 40 + 60.2 = 100.2
        var cub = new PolynomialEnergyCalibration();
        cub.PolynomialOrder = 3;
        cub.Coefficients = new double[] { 0.0, 0.2, 0.0, 1e-7 };
        Scene(m, "кубическая 0.2n+1e-7n³", cub, 1000, 200.0, 100.2, true);

        // Наивный множитель «кэВ-на-канал в центре × ПШПВ» на той же сцене.
        double naive = (0.2 + 3.0 * 1e-7 * 1000.0 * 1000.0) * 200.0;
        var pk = new Peak { Channel = 1000, FWHM = 200.0 };
        double real = Call(m, pk, cub);
        Console.WriteLine("  множитель «кэВ-на-канал × ПШПВ» на той же сцене: "
                          + naive.ToString("F4", CultureInfo.InvariantCulture)
                          + " против " + real.ToString("F4", CultureInfo.InvariantCulture)
                          + "  (расхождение "
                          + (100.0 * Math.Abs(naive - real) / real).ToString("F3", CultureInfo.InvariantCulture)
                          + " %) — поэтому множителем не считают");
        if (Math.Abs(naive - real) < 1e-9)
        {
            Fail("§2", "на нелинейной сцене множитель совпал с разностью краёв — сцена ничего не различает");
        }

        // Отказы: ноль значит «не измерена».
        Console.WriteLine("  ПШПВ = 0        -> " + Call(m, new Peak { Channel = 400, FWHM = 0.0 }, Linear(0.25))
                                                       .ToString("F4", CultureInfo.InvariantCulture));
        Console.WriteLine("  ПШПВ = NaN      -> " + Call(m, new Peak { Channel = 400, FWHM = Double.NaN }, Linear(0.25))
                                                       .ToString("F4", CultureInfo.InvariantCulture));
        Console.WriteLine("  калибровки нет  -> " + Call(m, new Peak { Channel = 400, FWHM = 40.0 }, null)
                                                       .ToString("F4", CultureInfo.InvariantCulture));
        foreach (var pair in new[] {
            Tuple.Create("ПШПВ=0", (Peak)new Peak { Channel = 400, FWHM = 0.0 }, (EnergyCalibration)Linear(0.25)),
            Tuple.Create("ПШПВ=NaN", (Peak)new Peak { Channel = 400, FWHM = Double.NaN }, (EnergyCalibration)Linear(0.25)),
            Tuple.Create("без калибровки", (Peak)new Peak { Channel = 400, FWHM = 40.0 }, (EnergyCalibration)null) })
        {
            if (Call(m, pair.Item2, pair.Item3) != 0.0) Fail("§2", pair.Item1 + " дал не ноль");
        }
    }

    static void Scene(MethodInfo m, string title, EnergyCalibration cal,
                      int channel, double fwhmChannels, double expectKev, bool judged)
    {
        var p = new Peak { Channel = channel, FWHM = fwhmChannels };
        double got = Call(m, p, cal);
        bool okKev = Math.Abs(got - expectKev) <= 1e-6 * Math.Max(1.0, Math.Abs(expectKev));
        bool okField = p.FWHM == fwhmChannels;         // поле не тронуто и по-прежнему в КАНАЛАХ
        string verdict = !judged
            ? "не судится: 1 кэВ = 1 канал, сцена отвечает «сошлось» при любом ответе"
            : (okKev && okField ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-30} | {1,8} | {2,8} | {3,9} | {4,11} | {5}",
            title, fwhmChannels.ToString("F1", CultureInfo.InvariantCulture),
            expectKev.ToString("F3", CultureInfo.InvariantCulture),
            got.ToString("F3", CultureInfo.InvariantCulture),
            p.FWHM.ToString("F1", CultureInfo.InvariantCulture) + " кан", verdict));
        if (judged && !okKev) Fail("§2", title + ": ждали " + expectKev.ToString("R", CultureInfo.InvariantCulture)
                                          + " кэВ, вышло " + got.ToString("R", CultureInfo.InvariantCulture));
        if (!okField) Fail("§2", title + ": поле `Peak.FWHM` изменилось вызовом двери");
    }

    // ------------------------------------------------------------------
    // §3. Положительный контроль
    // ------------------------------------------------------------------
    static void PositiveControl(MethodInfo m)
    {
        Console.WriteLine();
        Console.WriteLine("--- §3 Положительный контроль: ловит ли проверка заведомо неверное ---");

        // Сцена: 0.25 кэВ/канал, канал 400, ПШПВ 40 каналов, истина 10.000 кэВ.
        var p = new Peak { Channel = 400, FWHM = 40.0 };
        const double truth = 10.0;
        Func<double, bool> check = x => Math.Abs(x - truth) <= 1e-6 * truth;

        double right = Call(m, p, Linear(0.25));
        Console.WriteLine("  (а) верная калибровка 0.25          -> "
                          + right.ToString("F4", CultureInfo.InvariantCulture)
                          + "   проверка: " + (check(right) ? "ПРОПУСКАЕТ (так и надо)" : "ОТКАЗЫВАЕТ ⛔"));
        if (!check(right)) Fail("§3", "проверка отвергла ВЕРНОЕ число — мерить ею нечего");

        double wrongCal = Call(m, p, Linear(1.00));
        Console.WriteLine("  (б) подменённая калибровка 1.00     -> "
                          + wrongCal.ToString("F4", CultureInfo.InvariantCulture)
                          + "   проверка: " + (check(wrongCal) ? "ПРОПУСТИЛА ⛔" : "ПОЙМАНО"));
        if (check(wrongCal)) Fail("§3", "заведомо неверная калибровка прошла проверку");

        double wrongCal2 = Call(m, p, Linear(4.00));
        Console.WriteLine("  (в) подменённая калибровка 4.00     -> "
                          + wrongCal2.ToString("F4", CultureInfo.InvariantCulture)
                          + "   проверка: " + (check(wrongCal2) ? "ПРОПУСТИЛА ⛔" : "ПОЙМАНО"));
        if (check(wrongCal2)) Fail("§3", "заведомо неверная калибровка прошла проверку");

        // Ответ, который давали неверные пробы: канальное поле принято за кэВ.
        double asProbesDid = p.FWHM;
        Console.WriteLine("  (г) «ответ неверных проб» (поле=кэВ)-> "
                          + asProbesDid.ToString("F4", CultureInfo.InvariantCulture)
                          + "   проверка: " + (check(asProbesDid) ? "ПРОПУСТИЛА ⛔" : "ПОЙМАНО")
                          + "   — ровно на этом ошиблись `bqactivityprobe` и `s109activityprobe`");
        if (check(asProbesDid)) Fail("§3", "канальное поле, принятое за кэВ, прошло проверку");

        Console.WriteLine("  отношение поле/дверь на этой сцене  = "
                          + (p.FWHM / right).ToString("F4", CultureInfo.InvariantCulture)
                          + "   (единицы разведены: 1.0000 значило бы, что сцена ничего не различает)");
    }

    // ------------------------------------------------------------------
    // §4. Доходит ли `Peak` до XML вообще
    // ------------------------------------------------------------------
    static void XmlReach()
    {
        Console.WriteLine();
        Console.WriteLine("--- §4 Достаёт ли `Peak` до XML результата ---");

        // (1) СТРОЕНИЕ, а не удача одного объекта: обход графа типов
        //     `ResultDataFile` с учётом [XmlIgnore]. Если ни один
        //     СЕРИАЛИЗУЕМЫЙ член не имеет типа `Peak`, то имя поля
        //     `Peak.FWHM` частью формата файла не является — независимо от
        //     того, удастся ли сериализовать конкретный экземпляр.
        var reached = new List<string>();
        var seenTypes = new HashSet<Type>();
        WalkSerializable(typeof(ResultDataFile), "ResultDataFile", 0, seenTypes, reached);
        Console.WriteLine("  обойдено типов графа: " + seenTypes.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  сериализуемых членов типа `Peak`: "
                          + reached.Count.ToString(CultureInfo.InvariantCulture)
                          + (reached.Count == 0 ? "" : " ⛔ " + string.Join(", ", reached)));
        foreach (string name in new[] { "DetectedPeaks", "CalibrationPeaks" })
        {
            PropertyInfo pi = typeof(ResultData).GetProperty(name);
            bool ignored = pi != null && pi.GetCustomAttributes(typeof(XmlIgnoreAttribute), true).Length > 0;
            Console.WriteLine("  ResultData." + name + ": " + (pi == null ? "НЕТ СВОЙСТВА ⛔"
                              : (ignored ? "[XmlIgnore] — в файл не пишется" : "СЕРИАЛИЗУЕТСЯ ⛔")));
            if (pi == null || !ignored) Fail("§4", "ResultData." + name + " не помечено [XmlIgnore]");
        }
        if (reached.Count > 0)
            Fail("§4", "`Peak` достижим из XML-графа — имя поля становится частью формата файла");

        // (2) и та же мысль опытом, на живом сериализаторе.
        const double marker = 1234.5;          // метка, каких в файле больше нет
        try
        {
            var rdf = new ResultDataFile();
            var rd = new ResultData();
            rd.DetectedPeaks.Add(new Peak { Channel = 777, FWHM = marker, Energy = 4321.0 });
            rd.CalibrationPeaks.Add(new Peak { Channel = 778, FWHM = marker, Energy = 4322.0 });
            rdf.ResultDataList.Add(rd);

            var ser = new XmlSerializer(typeof(ResultDataFile));
            string xml;
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                ser.Serialize(sw, rdf);
                xml = sw.ToString();
            }
            bool hasMarker = xml.IndexOf("1234.5", StringComparison.Ordinal) >= 0;
            bool hasPeakTag = xml.IndexOf("<Peak", StringComparison.Ordinal) >= 0;
            Console.WriteLine("  XmlSerializer(typeof(ResultDataFile)) построился: да, длина XML "
                              + xml.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  метка 1234.5 в XML: " + (hasMarker ? "ЕСТЬ ⛔" : "нет")
                              + "   элемент <Peak…>: " + (hasPeakTag ? "ЕСТЬ ⛔" : "нет"));
            Console.WriteLine("  вывод: `DetectedPeaks` и `CalibrationPeaks` помечены [XmlIgnore] —"
                              + " поле `Peak.FWHM` в файл НЕ ПОПАДАЕТ, и совместимость чтения именем поля не задета");
            if (hasMarker || hasPeakTag)
                Fail("§4", "`Peak` всё-таки попадает в XML — имя поля становится частью формата файла");
        }
        catch (Exception ex)
        {
            // ⚠ Отказ ЖИВОГО сериализатора на пустышке — не про `Peak`: причина
            //    печатается целиком, и судят по ней, а не по слову «отказ».
            //    Разряд §4 держится обходом графа выше, он от удачи не зависит.
            var chain = new StringBuilder();
            for (Exception e = ex; e != null; e = e.InnerException)
                chain.Append(chain.Length == 0 ? "" : "  <- ").Append(e.GetType().Name).Append(": ").Append(e.Message);
            Console.WriteLine("  живой сериализатор на пустом `ResultDataFile` отказал: " + chain);
            Console.WriteLine("  ⚠ к `Peak` это отношения не имеет — см. обход графа выше;"
                              + " отдельная находка, записана в журнал полосы");
        }
    }

    /// <summary>
    /// Обход графа СЕРИАЛИЗУЕМЫХ членов типа: публичные читаемые-и-пишущиеся
    /// свойства и публичные поля, кроме помеченных [XmlIgnore]. Ровно то
    /// подмножество, которое `XmlSerializer` кладёт в файл.
    /// </summary>
    static void WalkSerializable(Type t, string path, int depth, HashSet<Type> seen, List<string> peakHits)
    {
        if (t == null || depth > 8) return;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
            || t == typeof(DateTime) || t == typeof(TimeSpan) || t == typeof(Guid)) return;
        if (t.IsArray) { WalkSerializable(t.GetElementType(), path + "[]", depth + 1, seen, peakHits); return; }
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
        {
            foreach (Type arg in t.GetGenericArguments())
            {
                if (arg == typeof(Peak)) peakHits.Add(path + "<Peak>");
                WalkSerializable(arg, path + "<>", depth + 1, seen, peakHits);
            }
            return;
        }
        if (!seen.Add(t)) return;

        var members = new List<Tuple<string, Type, object[]>>();
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || !p.CanRead) continue;
            members.Add(Tuple.Create(p.Name, p.PropertyType, p.GetCustomAttributes(typeof(XmlIgnoreAttribute), true)));
        }
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            members.Add(Tuple.Create(f.Name, f.FieldType, f.GetCustomAttributes(typeof(XmlIgnoreAttribute), true)));

        foreach (var m in members)
        {
            if (m.Item3.Length > 0) continue;                 // [XmlIgnore] — в файл не идёт
            if (m.Item2 == typeof(Peak)) peakHits.Add(path + "." + m.Item1);
            WalkSerializable(m.Item2, path + "." + m.Item1, depth + 1, seen, peakHits);
        }
    }

    // ------------------------------------------------------------------
    static string FileSha(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            return string.Concat(sha.ComputeHash(fs).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }

    static string TextSha(string text)
    {
        using (var sha = SHA256.Create())
        {
            return string.Concat(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text))
                                    .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
