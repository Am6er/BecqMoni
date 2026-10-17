using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

/// <summary>
/// П330 (15.10.2026), `AMBER226`: импорт кривых эффективности ЛСРМ (`.txt`,
/// `.efa`, `.efr`) в редакторе геометрии конструктора кривой.
///
/// Решения Amber 15.10.2026 вопросником: кривая — «Кривая окна»; из `.efa` —
/// «Подгонку ЛСРМ»; из `.efr` — «Точки как есть»; шапка — «Только в журнал».
///
/// Четыре части.
///
/// (1) ПОДГОНКА `.efa`. Каждая геометрия каждого `.efa` каталога поставки
/// (`--dir`) читается приложением (`LsrmEfficiencyFile`), и каждый узел кривой
/// сверяется со СВОИМ пересчётом пробы — свой разбор текста регулярными
/// выражениями, свои полиномы, своё сшивание перекрытия (линейный вес по lg E),
/// — а не с вызовом приложения. Сетка — 50 узлов на декаду ровно от низа до
/// верха зон, без разрывов.
///
/// (2) ТОЧКИ `.efr`. Тем же своим разбором: точки всех источников геометрии,
/// без «No» и без погрешности выше 100 %, одинаковые энергии слиты весами 1/σ².
///
/// (3) ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ — подставные файлы в `--work`: испорченная зона,
/// текст без секций, `.efa` без подгонки обязаны получить ОТКАЗ; слияние,
/// «No» и 100 % — дать известные числа; свой пересчёт с переставленным
/// базисом обязан РАЗОЙТИСЬ с приложением (иначе сверка (1) ничего не мерит).
///
/// (4) ОКНО. Настоящий `EfficiencyMakerForm` вне экрана: `ImportCurveFile`
/// (отражением) и «Сохранить» (`SaveIntoConfig`) кладут кривую в конфигурацию
/// с происхождением LSRM, пустым клеймом, отпечатком сохранённой геометрии и
/// заметкой импорта; отказ выбора и негодный файл кривую окна не трогают.
///
///     p330lsrmcurveimportprobe [--dir=&lt;каталог эталонов&gt;] [--work=&lt;каталог&gt;] [--model=&lt;.in&gt;]
///
/// Код возврата: 0 — всё сошлось; 1 — нет; 2 — плохие ключи или нет данных.
/// </summary>
static class P330LsrmCurveImportProbe
{
    static int bad;
    static int checks;

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok)
        {
            bad++;
            Console.WriteLine("  ✗ " + what);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = @"D:\YandexDisk\Спектры\Спектры источники эталоны";
        string work = @"D:\BqMoni_Claude\p330\work";
        string model = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--work=", StringComparison.Ordinal)) work = a.Substring(7);
            else if (a.StartsWith("--model=", StringComparison.Ordinal)) model = a.Substring(8);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine("нет каталога эталонов: " + dir);
            return 2;
        }

        model = model ?? FindModel();
        if (model == null || !File.Exists(model))
        {
            Console.Error.WriteLine("нет модели .in для окна (--model=): " + model);
            return 2;
        }

        Directory.CreateDirectory(work);
        Application.EnableVisualStyles();
        GlobalConfigManager.GetInstance();

        string[] efa = Directory.GetFiles(dir, "*.efa", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        string[] efr = Directory.GetFiles(dir, "*.efr", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Console.WriteLine("каталог: {0}; .efa {1}, .efr {2}", dir, efa.Length, efr.Length);
        if (efa.Length == 0 || efr.Length == 0)
        {
            Console.Error.WriteLine("нечего мерить: нужны и .efa, и .efr");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine("(1) подгонка .efa против своего пересчёта");
        foreach (string f in efa) FitPart(f);

        Console.WriteLine();
        Console.WriteLine("(2) точки .efr против своего разбора");
        foreach (string f in efr) PointsPart(f);

        Console.WriteLine();
        Console.WriteLine("(3) положительные контроли");
        Controls(efa, work);

        Console.WriteLine();
        Console.WriteLine("(4) окно конструктора: импорт и «Сохранить»");
        Window(efa, efr, work, model);

        Console.WriteLine();
        Console.WriteLine("проверок {0}; {1}", checks, bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: расхождений " + bad);
        return bad == 0 ? 0 : 1;
    }

    static string FindModel()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
        {
            DirectoryInfo d = new DirectoryInfo(start);
            while (d != null)
            {
                string candidate = Path.Combine(d.FullName, @"tools\effmaker\models\AS80_point5.in");
                if (File.Exists(candidate)) return candidate;
                d = d.Parent;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Свой разбор: секции, точки, зоны — регулярными выражениями
    // ------------------------------------------------------------------

    sealed class OwnZone { public int Deg; public double Lo, Hi, S; public double[][] P; public double[] C; }

    sealed class OwnPoint { public double E, Eff, Err; public bool No; }

    sealed class OwnSet { public string Key; public List<OwnPoint> Points = new List<OwnPoint>(); public List<OwnZone> Zones = new List<OwnZone>(); public int Sections; }

    static string Text(string path)
    {
        return Encoding.GetEncoding(1251).GetString(File.ReadAllBytes(path));
    }

    static double N(string s)
    {
        return double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static List<OwnSet> OwnParse(string path, bool mergeByGeometry)
    {
        var sets = new List<OwnSet>();
        OwnSet cur = null;
        var kv = new Dictionary<string, string>();
        Action close = () =>
        {
            if (cur == null || !kv.ContainsKey("Zones")) return;
            int n = int.Parse(kv["Zones"], CultureInfo.InvariantCulture);
            for (int i = 1; i <= n; i++)
            {
                double[] z = kv["Zone_" + i].Split(',').Select(N).ToArray();
                var zone = new OwnZone { Deg = (int)z[0], Lo = z[1], Hi = z[2], S = z[3] };
                zone.P = Enumerable.Range(1, zone.Deg + 1).Select(j => kv["Curve_" + i + "_" + j].Split(',').Select(N).ToArray()).ToArray();
                zone.C = kv["Curve_" + i].Split(',').Select(N).ToArray();
                cur.Zones.Add(zone);
            }
        };
        foreach (string raw in Regex.Split(Text(path), "\r\n|\n|\r"))
        {
            string line = raw.Trim();
            Match sec = Regex.Match(line, @"^\[([^;\]]*);([^;\]]*)(;.*)?\]$");
            if (sec.Success)
            {
                close();
                kv = new Dictionary<string, string>();
                string key = sec.Groups[1].Value.Trim() + ";" + sec.Groups[2].Value.Trim();
                cur = mergeByGeometry ? sets.FirstOrDefault(s => s.Key == key) : null;
                if (cur == null)
                {
                    cur = new OwnSet { Key = key };
                    sets.Add(cur);
                }

                cur.Sections++;
                continue;
            }

            Match pt = Regex.Match(line, @"^([0-9]+(?:\.[0-9]+)?)=([^,]+),([^,]+),(.*)$");
            if (pt.Success && cur != null)
            {
                cur.Points.Add(new OwnPoint
                {
                    E = N(pt.Groups[1].Value),
                    Eff = N(pt.Groups[2].Value),
                    Err = N(pt.Groups[3].Value),
                    No = Regex.IsMatch(pt.Groups[4].Value, @",\s*No\s*$"),
                });
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0) kv[line.Substring(0, eq)] = line.Substring(eq + 1);
        }

        close();
        return sets;
    }

    static double Horner(double[] c, double x)
    {
        double r = 0;
        foreach (double v in c) r = r * x + v;
        return r;
    }

    static double ZoneLog(OwnZone z, double x, bool reversedBasis)
    {
        double y = 0;
        for (int j = 0; j < z.C.Length; j++)
        {
            double[] p = reversedBasis ? z.P[j].Reverse().ToArray() : z.P[j];
            y += z.C[j] * Horner(p, x);
        }

        return y;
    }

    /// <summary>Свой пересчёт: одна зона — её полином; две — линейный вес по lg E в перекрытии.</summary>
    static bool OwnEval(List<OwnZone> zones, double e, bool reversedBasis, out double eff)
    {
        eff = 0;
        double x = Math.Log10(e);
        var z = zones.OrderBy(q => q.Lo).ToList();
        var cover = z.Where(q => x >= q.Lo - 1e-12 && x <= q.Hi + 1e-12).ToList();
        if (cover.Count == 0) return false;
        if (cover.Count == 1)
        {
            eff = Math.Pow(10, ZoneLog(cover[0], Math.Min(Math.Max(x, cover[0].Lo), cover[0].Hi), reversedBasis));
            return true;
        }

        if (cover.Count != 2) throw new InvalidOperationException("три зоны в одной точке — пробой не предусмотрено");
        OwnZone a = cover[0], b = cover[1];
        double w = (x - b.Lo) / (a.Hi - b.Lo);
        w = Math.Min(1, Math.Max(0, w));
        eff = Math.Pow(10, (1 - w) * ZoneLog(a, Math.Min(x, a.Hi), reversedBasis) + w * ZoneLog(b, Math.Max(x, b.Lo), reversedBasis));
        return true;
    }

    // ------------------------------------------------------------------
    // (1) подгонка
    // ------------------------------------------------------------------

    static void FitPart(string path)
    {
        string problem;
        List<LsrmCurveSet> sets = LsrmEfficiencyFile.Read(path, out problem);
        List<OwnSet> own = OwnParse(path, false);
        Check(sets != null, Path.GetFileName(path) + ": приложение отказало — " + problem);
        if (sets == null) return;
        Check(sets.Count == own.Count, string.Format("{0}: геометрий {1}, у пробы {2}", Path.GetFileName(path), sets.Count, own.Count));
        for (int i = 0; i < Math.Min(sets.Count, own.Count); i++)
        {
            LsrmCurveReport report;
            List<ROIEfficiencyData> curve = LsrmEfficiencyFile.BuildCurve(sets[i], true, path, out report, out problem);
            Check(curve != null, sets[i].Title + ": кривой нет — " + problem);
            if (curve == null) continue;
            double lo = own[i].Zones.Min(z => z.Lo), hi = own[i].Zones.Max(z => z.Hi);
            int expected = Math.Max(2, (int)Math.Ceiling((hi - lo) * 50)) + 1;
            Check(curve.Count == expected && report.GapNodes == 0,
                  string.Format("{0}: узлов {1}, ждали {2}; в разрыве {3}", sets[i].Title, curve.Count, expected, report.GapNodes));
            Check(Math.Abs(curve[0].Energy / Math.Pow(10, lo) - 1) < 1e-12 && Math.Abs(curve[curve.Count - 1].Energy / Math.Pow(10, hi) - 1) < 1e-12,
                  sets[i].Title + ": края сетки не на краях зон");
            double worst = 0;
            bool monotone = true;
            for (int k = 0; k < curve.Count; k++)
            {
                double mine;
                if (!OwnEval(own[i].Zones, curve[k].Energy, false, out mine)) { worst = double.PositiveInfinity; continue; }
                worst = Math.Max(worst, Math.Abs(curve[k].Efficiency / mine - 1));
                if (k > 0 && !(curve[k].Energy > curve[k - 1].Energy)) monotone = false;
            }

            Check(worst < 1e-9 && monotone, string.Format("{0}: расхождение с пересчётом пробы {1:E2}, энергии растут: {2}", sets[i].Title, worst, monotone));

            // Справочно: как подгонка ложится на измеренные точки файла.
            var dev = new List<double>();
            foreach (OwnPoint p in own[i].Points)
            {
                double fit, err;
                if (LsrmEfficiencyFile.TryEvalFit(sets[i].Zones, p.E, out fit, out err)) dev.Add(Math.Abs(fit / p.Eff - 1) * 100);
            }

            dev.Sort();
            Console.WriteLine("  {0,-60} зон {1}, узлов {2,3}, {3,6:f1}…{4,6:f1} кэВ, пересчёт {5:E1}; |точка−подгонка| медиана {6:f1} %, макс {7:f1} %",
                Path.GetFileName(path) + " / " + sets[i].Geometry, own[i].Zones.Count, curve.Count,
                curve[0].Energy, curve[curve.Count - 1].Energy, worst,
                dev.Count > 0 ? dev[dev.Count / 2] : double.NaN, dev.Count > 0 ? dev[dev.Count - 1] : double.NaN);
        }
    }

    // ------------------------------------------------------------------
    // (2) точки
    // ------------------------------------------------------------------

    static List<double[]> OwnPointCurve(OwnSet set, out int excluded, out int over, out int merged)
    {
        excluded = set.Points.Count(p => p.No);
        var kept = set.Points.Where(p => !p.No).ToList();
        over = kept.Count(p => !(p.Err <= 100) || !(p.Eff > 0));
        kept = kept.Where(p => p.Err <= 100 && p.Eff > 0).OrderBy(p => p.E).ToList();
        var outList = new List<double[]>();
        merged = 0;
        foreach (OwnPoint p in kept)
        {
            if (outList.Count > 0 && p.E - outList[outList.Count - 1][0] < 0.01)
            {
                double[] last = outList[outList.Count - 1];
                double w1 = 1 / (last[2] * last[2]), w2 = 1 / (p.Err * p.Err);
                last[0] = (last[0] * w1 + p.E * w2) / (w1 + w2);
                last[1] = (last[1] * w1 + p.Eff * w2) / (w1 + w2);
                last[2] = 1 / Math.Sqrt(w1 + w2);
                merged++;
                continue;
            }

            outList.Add(new[] { p.E, p.Eff, p.Err });
        }

        return outList;
    }

    static void PointsPart(string path)
    {
        string problem;
        List<LsrmCurveSet> sets = LsrmEfficiencyFile.Read(path, out problem);
        List<OwnSet> own = OwnParse(path, true);
        Check(sets != null, Path.GetFileName(path) + ": приложение отказало — " + problem);
        if (sets == null) return;
        Check(sets.Count == own.Count, string.Format("{0}: геометрий {1}, у пробы {2}", Path.GetFileName(path), sets.Count, own.Count));
        for (int i = 0; i < Math.Min(sets.Count, own.Count); i++)
        {
            LsrmCurveReport report;
            List<ROIEfficiencyData> curve = LsrmEfficiencyFile.BuildCurve(sets[i], false, path, out report, out problem);
            int excluded, over, merged;
            List<double[]> mine = OwnPointCurve(own[i], out excluded, out over, out merged);
            Check(curve != null, sets[i].Title + ": кривой нет — " + problem);
            if (curve == null) continue;
            bool same = curve.Count == mine.Count;
            for (int k = 0; same && k < curve.Count; k++)
            {
                same = Math.Abs(curve[k].Energy - mine[k][0]) < 1e-9 && Math.Abs(curve[k].Efficiency / mine[k][1] - 1) < 1e-12
                       && Math.Abs(curve[k].ErrorPercent - mine[k][2]) < 1e-9;
            }

            Check(same, string.Format("{0}: точек {1}, у пробы {2} — или числа разошлись", sets[i].Title, curve.Count, mine.Count));
            Check(report.ExcludedByLsrm == excluded && report.OverMaxError == over && report.MergedDuplicates == merged
                  && sets[i].Sources.Count == own[i].Sections,
                  string.Format("{0}: «No» {1}/{2}, за 100 % {3}/{4}, слито {5}/{6}, источников {7}/{8}", sets[i].Title,
                                report.ExcludedByLsrm, excluded, report.OverMaxError, over, report.MergedDuplicates, merged,
                                sets[i].Sources.Count, own[i].Sections));
            Console.WriteLine("  {0,-60} источников {1,2}, точек файла {2,2}, «No» {3}, слито {4}, в кривой {5}",
                Path.GetFileName(path) + " / " + sets[i].Geometry, sets[i].Sources.Count, own[i].Points.Count,
                excluded, merged, curve.Count);
        }
    }

    // ------------------------------------------------------------------
    // (3) положительные контроли
    // ------------------------------------------------------------------

    static string Write(string work, string name, string text)
    {
        string path = Path.Combine(work, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(1251).GetBytes(text));
        return path;
    }

    static void Controls(string[] efa, string work)
    {
        string single = efa.FirstOrDefault(f => Path.GetFileName(f).Contains("Точечная-25см"));
        Check(single != null, "нет «Точечная-25см.efa» для контролей");
        if (single == null) return;
        string text = Text(single);
        string problem;

        // а) испорченная зона — отказ, и отказ называет строку зоны.
        string brokenZone = Regex.Replace(text, @"^Curve_1_2=.*$\r?\n?", "", RegexOptions.Multiline);
        Check(brokenZone != text, "контроль (а) не испортил файл");
        List<LsrmCurveSet> r = LsrmEfficiencyFile.Read(Write(work, "broken_zone.efa", brokenZone), out problem);
        Check(r == null && problem != null && problem.Contains("Curve_1_2"), "(а) без Curve_1_2 файл принят: " + problem);
        Console.WriteLine("  (а) без Curve_1_2: {0}", problem);

        // б) текст без секций — отказ.
        r = LsrmEfficiencyFile.Read(Write(work, "no_sections.efa", "Energy=1\r\nfoo=bar\r\n"), out problem);
        Check(r == null && problem != null, "(б) текст без секций принят");
        Console.WriteLine("  (б) без секций: {0}", problem);

        // в) .efa без подгонки — геометрия читается, кривой нет.
        string noFit = Regex.Replace(text, @"^(Zones|Zone_\d+|Curve_\d+(_\d+)?)=.*$\r?\n?", "", RegexOptions.Multiline);
        string noFitPath = Write(work, "no_fit.efa", noFit);
        r = LsrmEfficiencyFile.Read(noFitPath, out problem);
        LsrmCurveReport report = null;
        List<ROIEfficiencyData> c = r == null ? null : LsrmEfficiencyFile.BuildCurve(r[0], true, noFitPath, out report, out problem);
        Check(r != null && c == null && problem != null && problem.Contains("Zones"), "(в) .efa без подгонки дал кривую");
        Console.WriteLine("  (в) без подгонки: {0}", problem);

        // г) .efr: слияние 1/σ², «No», погрешность выше 100 %.
        string efr = "[D;G;S1]\r\nDistance,cm=5\r\nCs-137=1,1,1\r\n661.657=1.0E-02,1,Cs-137,1,1,85.1\r\n1173.228=6.0E-03,2,Co-60,1,1,99.85\r\n"
                   + "[D;G;S2]\r\nCs-137=1,1,1\r\n661.657=1.2E-02,2,Cs-137,1,1,85.1\r\n59.541=4.0E-02,150,Am-241,1,1,35.9\r\n238.632=3.0E-02,8,Th-228,1,1,43.6, No\r\n";
        string efrPath = Write(work, "merge.efr", efr);
        r = LsrmEfficiencyFile.Read(efrPath, out problem);
        c = r == null ? null : LsrmEfficiencyFile.BuildCurve(r[0], false, efrPath, out report, out problem);
        bool okMerge = c != null && c.Count == 2 && Math.Abs(c[0].Efficiency - 0.0104) < 1e-12
                       && Math.Abs(c[0].ErrorPercent - 1 / Math.Sqrt(1.25)) < 1e-12 && Math.Abs(c[1].Energy - 1173.228) < 1e-9;
        Check(okMerge && report.MergedDuplicates == 1 && report.ExcludedByLsrm == 1 && report.OverMaxError == 1
              && r[0].Sources.Count == 2 && r[0].HeaderValue("Cs-137") == "",
              "(г) слияние/«No»/100 %/шапка разошлись: " + problem);
        Console.WriteLine("  (г) 661.657: ε {0:G6}, ±{1:f4} % (ждали 0.0104, ±0.8944 %); «No» {2}, за 100 % {3}, слито {4}",
            c == null ? double.NaN : c[0].Efficiency, c == null ? double.NaN : c[0].ErrorPercent,
            report == null ? -1 : report.ExcludedByLsrm, report == null ? -1 : report.OverMaxError, report == null ? -1 : report.MergedDuplicates);

        // д) сверка (1) различает модель: переставленный базис обязан разойтись.
        List<LsrmCurveSet> sets = LsrmEfficiencyFile.Read(single, out problem);
        List<OwnSet> own = OwnParse(single, false);
        c = LsrmEfficiencyFile.BuildCurve(sets[0], true, single, out report, out problem);
        double worst = 0;
        foreach (ROIEfficiencyData p in c)
        {
            double wrong;
            if (OwnEval(own[0].Zones, p.Energy, true, out wrong)) worst = Math.Max(worst, Math.Abs(p.Efficiency / wrong - 1));
        }

        Check(worst > 0.01, string.Format("(д) переставленный базис не разошёлся ({0:E2}) — сверка (1) слепа", worst));
        Console.WriteLine("  (д) переставленный базис: расхождение до {0:E2} (доля), порог 0.01", worst);
    }

    // ------------------------------------------------------------------
    // (4) окно
    // ------------------------------------------------------------------

    static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static bool Import(EfficiencyMakerForm form, string path, Func<IList<LsrmCurveSet>, int> choose, out string problem)
    {
        object[] a = { path, choose, null };
        bool ok = (bool)typeof(EfficiencyMakerForm).GetMethod("ImportCurveFile", Any).Invoke(form, a);
        problem = (string)a[2];
        return ok;
    }

    static EfficiencyFitResult Last(EfficiencyMakerForm form)
    {
        return (EfficiencyFitResult)typeof(EfficiencyMakerForm).GetField("lastResult", Any).GetValue(form);
    }

    /// <summary>«Сохранить» — только если поля годны: иначе `TryCommit` поднял бы модальное окно.</summary>
    static bool Save(EfficiencyMakerForm form)
    {
        object panel = typeof(EfficiencyMakerForm).GetField("geometryPanel", Any).GetValue(form);
        Type pt = panel.GetType();
        bool marks = (bool)pt.GetMethod("MarkBadValues", Any).Invoke(panel, null);
        object g = pt.GetMethod("BuildModel", Any).Invoke(panel, null);
        string error = (string)pt.GetMethod("Validate", Any, null, new[] { typeof(GeometryModel) }, null).Invoke(panel, new[] { g });
        Check(marks && error == null, "поля геометрии негодны, «Сохранить» не зовётся: " + error);
        if (!marks || error != null) return false;
        return (bool)typeof(EfficiencyMakerForm).GetMethod("SaveIntoConfig", Any).Invoke(form, null);
    }

    static void Window(string[] efa, string[] efr, string work, string model)
    {
        GeometryModel geometry = GeometryModel.Load(model);
        DeviceConfigInfo device = new DeviceConfigInfo();
        EfficiencyConfigData config = new EfficiencyConfigData("P330") { Geometry = geometry };
        device.EfficiencyConfigs.Add(config);
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            form.BindTo(device, config);
            string problem;

            string single = efa.First(f => Path.GetFileName(f).Contains("Точечная-25см"));
            bool ok = Import(form, single, null, out problem);
            EfficiencyFitResult last = Last(form);
            Check(ok && problem == null && last != null && last.Origin == EfficiencyOrigin.Lsrm && last.Curve.Count > 2,
                  "импорт .efa в окно: " + problem);
            Check(ok && Save(form), "«Сохранить» после импорта .efa");
            Check(config.Origin == EfficiencyOrigin.Lsrm && config.Curve.Count == last.Curve.Count
                  && (config.ComputeStamp ?? "") == ""
                  && config.GeometryFingerprint == ResponseMatrix.GeometryFingerprint(config.Geometry)
                  && !config.CurveGeometryMismatch
                  && ((string)config.Note ?? "").StartsWith("LSRM fit (.efa): " + Path.GetFileName(single), StringComparison.Ordinal),
                  string.Format("конфигурация после «Сохранить»: origin {0}, точек {1}, клеймо «{2}», заметка «{3}»",
                                config.Origin, config.Curve.Count, config.ComputeStamp, (string)config.Note));
            Console.WriteLine("  .efa: origin {0}, точек {1}, отпечаток сходится: {2}; заметка: {3}", config.Origin,
                              config.Curve.Count, !config.CurveGeometryMismatch, (string)config.Note);

            // Отказ выбора геометрии — кривая окна прежняя, причины нет.
            string multi = efa.FirstOrDefault(f => Path.GetFileName(f).EndsWith("_2024.efa", StringComparison.Ordinal));
            Check(multi != null, "нет сводного «…_2024.efa»");
            if (multi != null)
            {
                int offered = 0;
                ok = Import(form, multi, sets => { offered = sets.Count; return -1; }, out problem);
                Check(!ok && problem == null && object.ReferenceEquals(Last(form), last) && offered == 4,
                      string.Format("отказ выбора: ok {0}, причина {1}, предложено {2}", ok, problem, offered));

                ok = Import(form, multi, sets => sets.ToList().FindIndex(s => s.Geometry == "Дента-120мл"), out problem);
                Check(ok && Save(form) && ((string)config.Note ?? "").Contains(";Дента-120мл]"),
                      "выбор «Дента-120мл» сводного файла: " + problem + " / " + (string)config.Note);
                Console.WriteLine("  сводный .efa: предложено геометрий {0}; выбрана «Дента-120мл», точек {1}", offered, config.Curve.Count);
            }

            // .efr — точки.
            string point5 = efr.FirstOrDefault(f => Path.GetFileName(f).Contains("Точечная-5см"));
            Check(point5 != null, "нет «Точечная-5см.efr»");
            if (point5 != null)
            {
                ok = Import(form, point5, null, out problem);
                Check(ok && Save(form) && config.Origin == EfficiencyOrigin.Lsrm && config.Curve.Count == 23
                      && ((string)config.Note ?? "").StartsWith("LSRM points (.efr)", StringComparison.Ordinal),
                      string.Format(".efr «Точечная-5см»: точек {0} (ждали 23 = 24 − 1 «No»), заметка «{1}», {2}",
                                    config.Curve.Count, (string)config.Note, problem));
                Console.WriteLine("  .efr: точек {0}; заметка: {1}", config.Curve.Count, (string)config.Note);
            }

            // Экспорт EffCalcMC — прежним читателем вкладки прибора.
            string txt = Path.Combine(work, "EffCalcMC.txt");
            File.WriteAllText(txt, "Energy, keV\tEfficiency\tUncertainty, %\r\n20\t1.0E-02\t900\r\n100\t5.0E-02\t1.5\r\n662\t1.5E-02\t1.0\r\n1332\t8.0E-03\t1.2\r\n",
                              new UTF8Encoding(false));
            last = Last(form);
            ok = Import(form, txt, null, out problem);
            Check(ok && Last(form).Curve.Count == 3 && Save(form)
                  && ((string)config.Note ?? "").StartsWith("LSRM export: EffCalcMC.txt", StringComparison.Ordinal),
                  "экспорт txt: " + problem + " / " + (string)config.Note);
            Console.WriteLine("  .txt: точек {0} (20 кэВ при 900 % отсечена правилом T174)", config.Curve.Count);

            // Негодный файл — отказ с причиной, кривая окна прежняя.
            last = Last(form);
            string broken = Write(work, "broken.efr", "[D;G;S]\r\n661.657=abc,1,Cs-137\r\n");
            ok = Import(form, broken, null, out problem);
            Check(!ok && problem != null && object.ReferenceEquals(Last(form), last), "негодный .efr принят или сбросил кривую окна");
            Console.WriteLine("  негодный .efr: {0}", problem);

            // Расчёт после импорта: результат счёта несёт происхождение Simulation.
            typeof(EfficiencyMakerForm).GetField("lastResult", Any).SetValue(form, new EfficiencyFitResult
            {
                Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 100, Efficiency = 0.05, ErrorPercent = 0.5 },
                    new ROIEfficiencyData { Energy = 1000, Efficiency = 0.01, ErrorPercent = 0.5 },
                },
                ComputeStamp = "phys=probe",
                GeometryFingerprint = "probe",
            });
            Check(Save(form) && config.Origin == EfficiencyOrigin.Simulation && config.ComputeStamp == "phys=probe"
                  && config.GeometryFingerprint == "probe",
                  "посчитанная кривая после импорта сохраняется не как Simulation: " + config.Origin);
        }

        // Строки окна — в обеих культурах, фильтр — парами «подпись|маска».
        foreach (string culture in new[] { "", "ru" })
        {
            CultureInfo ci = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
            string filter = BecquerelMonitor.Properties.Resources.ResourceManager.GetString("EfficiencyMakerLsrmImportFilter", ci);
            Check(filter != null && filter.Split('|').Length == 8 && filter.Contains("*.efa") && filter.Contains("*.efr"),
                  "фильтр диалога («" + culture + "»): " + filter);
            foreach (string key in new[] { "EfficiencyMakerLsrmCurveFit", "EfficiencyMakerLsrmCurvePoints", "EfficiencyMakerLsrmImported",
                                           "EfficiencyMakerLsrmPickPrompt", "LsrmCurveNoFit", "LsrmCurveFewPoints" })
            {
                Check(!string.IsNullOrEmpty(BecquerelMonitor.Properties.Resources.ResourceManager.GetString(key, ci)),
                      "нет строки " + key + " («" + culture + "»)");
            }
        }

        string sharedFilter = BecquerelMonitor.Properties.Resources.EfficiencyMakerGeometryFilter;
        Check(!sharedFilter.Contains(".efa"), "общий фильтр .in (второй шаг вкладки прибора) тронут: " + sharedFilter);
    }
}
