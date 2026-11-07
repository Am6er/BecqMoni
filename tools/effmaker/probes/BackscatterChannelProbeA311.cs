using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

// (`AMBER311`) Строка «обратное рассеяние» матрицы: подмножество канала `Compton`, только показ.
//
//   --mode=check --matrix=<.rmx>         строка ≤ канала бин к бину; доля и вершина на узлах 662 и 2614 против E/(1+2E/511)
//   --mode=rows --a=<цп.rmx> --b=<гпу.rmx>  площади строк узел к узлу: z, Σχ²/n, отношение (CPU↔GPU)
//   --mode=bodies --a=<.rmx> --b=<.rmx>  тело, клеймо и каналы двух матриц: равны ли; канал Compton побитно
//   --mode=binsdiff --a=<.rmx> --b=<.rmx>  сколько бинов суммарного отклика разошлось
//   --mode=old --matrix=<.rmx>           файл без хвоста `BSCR` читается, строки нет, клеймо и тело те же; пересохранение байт в байт
//   --mode=strip --matrix=<.rmx> --out=<.rmx>     снять строки (плечо «старая матрица»)
//   --mode=inject --matrix=<.rmx> --out=<.rmx>    подложить строки: 10 % канала `Compton` (плечо малой базы, числа не физика)
//   --mode=fsa --corpus=<corpus> --store=<каталог .rmx> --keys=<спектр,…> [--ui=ru]  разбор: галка, старая матрица, подслой
//
// Код: 0 — сошлось, 1 — нет, 2 — ключи.
static class BackscatterChannelProbeA311
{
    static int failures;

    static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Console.WriteLine((ok ? "   OK  " : "   ОТКАЗ ") + what);
    }

    static string Fmt(string format, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

    static ResponseMatrix Load(string path)
    {
        MatrixRefusal refusal;
        int format;
        ResponseMatrix m = ResponseMatrix.Load(path, out refusal, out format);
        if (m == null) throw new InvalidOperationException(path + ": " + refusal);
        return m;
    }

    static double Sum(float[] row)
    {
        double s = 0.0;
        if (row != null) foreach (float v in row) s += v;
        return s;
    }

    static int Compton { get { return (int)EfficiencySimulator.ResponseChannel.Compton; } }

    static void ModeCheck(string path)
    {
        ResponseMatrix m = Load(path);
        Console.WriteLine("== " + Path.GetFileName(path) + ": узлов " + m.NodeCount + ", строки " + (m.HasBackscatter ? "есть" : "нет"));
        Check(m.HasBackscatter, "строки обратного рассеяния есть");
        if (!m.HasBackscatter) return;
        long bins = 0, over = 0;
        double worst = 0.0;
        for (int i = 0; i < m.NodeCount; i++)
        {
            float[] part = m.BackscatterRows[i];
            float[] whole = m.ChannelRows[Compton][i];
            for (int b = 0; b < part.Length; b++)
            {
                bins++;
                double w = b < whole.Length ? whole[b] : 0.0;
                if (part[b] > w) { over++; worst = Math.Max(worst, (part[b] - w) / Math.Max(w, 1e-30)); }
            }
        }

        Console.WriteLine(Fmt("   бинов {0}, строка выше канала в {1}, худшее превышение {2:E2}", bins, over, worst));
        Check(worst <= 1e-6, "строка — часть канала `Compton` бин к бину (до округления float)");
        foreach (double e in new[] { 661.657, 2614.5 })
        {
            int node = 0;
            for (int i = 0; i < m.NodeCount; i++) if (Math.Abs(m.Energies[i] - e) < Math.Abs(m.Energies[node] - e)) node = i;
            float[] row = m.BackscatterRows[node];
            double total = Sum(row), compton = Sum(m.ChannelRows[Compton][node]);
            int top = 0;
            double best = -1.0;
            for (int b = 20; b + 5 < row.Length; b++)
            {
                double s = 0.0;
                for (int d = -5; d <= 5; d++) s += row[b + d];
                if (s > best) { best = s; top = b; }
            }

            double hump = m.Energies[node] / (1.0 + 2.0 * m.Energies[node] / 511.0);
            // Квант, рассеянный на 90° и больше, не несёт больше E/(1+E/511); запас 30 кэВ и 8 % на шкалу света.
            double edge = 1.08 * m.Energies[node] / (1.0 + m.Energies[node] / 511.0) + 30.0;
            double above = 0.0;
            for (int b = (int)(edge / m.BinKev); b < row.Length; b++) above += row[b];
            Console.WriteLine(Fmt("   выше края 90° ({0:F0} кэВ): {1:F2} % строки", edge, total > 0 ? 100.0 * above / total : 0.0));
            // Рентген защиты метку не несёт: у K-линий свинца в строке нет пика, в канале он есть.
            Console.WriteLine(Fmt("   K-рентген Pb: 72…77 кэВ к соседям 60…70 и 80…90: строка {0:F2}, канал {1:F2}",
                                  XrayExcess(row, m.BinKev), XrayExcess(m.ChannelRows[Compton][node], m.BinKev)));
            Check(total <= 0 || above / total < 0.05, "строка несёт горб, а не рассеяние вперёд");
            Console.WriteLine(Fmt("   узел {0:F1} кэВ: строка {1:E3} на испущенный, {2:F2} % канала Compton; вершина {3:F0} кэВ, 180° {4:F1} кэВ", m.Energies[node], total, compton > 0 ? 100.0 * total / compton : 0.0, (top + 0.5) * m.BinKev, hump));
        }
    }

    // Сколько бинов суммарного отклика разошлось и как сильно: сверка тел двух счётов.
    static void ModeBinsDiff(string pathA, string pathB)
    {
        ResponseMatrix a = Load(pathA), b = Load(pathB);
        Check(a.NodeCount == b.NodeCount, "сетка одна");
        if (failures > 0) return;
        long bins = 0, differ = 0;
        double worst = 0.0, sumAbs = 0.0, sumA = 0.0;
        for (int i = 0; i < a.NodeCount; i++)
        {
            float[] x = a.Rows[i], y = b.Rows[i];
            for (int k = 0; k < Math.Min(x.Length, y.Length); k++)
            {
                bins++;
                sumA += x[k];
                if (x[k] != y[k])
                {
                    differ++;
                    double d = Math.Abs((double)x[k] - y[k]);
                    sumAbs += d;
                    if (x[k] > 0f) worst = Math.Max(worst, d / x[k]);
                }
            }
        }

        Console.WriteLine(Fmt("BINS\t{0}\t{1}\t{2}\t{3:E2}\t{4:E2}", Path.GetFileNameWithoutExtension(pathA), bins, differ, worst, sumA > 0 ? sumAbs / sumA : 0.0));
    }

    static double XrayExcess(float[] row, double bin)
    {
        double peak = 0.0, side = 0.0;
        int nPeak = 0, nSide = 0;
        for (int b = 0; b < row.Length; b++)
        {
            double e = (b + 0.5) * bin;
            if (e >= 72.0 && e <= 77.0) { peak += row[b]; nPeak++; }
            else if ((e >= 60.0 && e <= 70.0) || (e >= 80.0 && e <= 90.0)) { side += row[b]; nSide++; }
        }

        return side > 0.0 && nPeak > 0 ? (peak / nPeak) / (side / nSide) : 0.0;
    }

    static void ModeRows(string pathA, string pathB)
    {
        ResponseMatrix a = Load(pathA), b = Load(pathB);
        Check(a.HasBackscatter && b.HasBackscatter && a.NodeCount == b.NodeCount, "строки есть у обеих, сетка одна");
        if (failures > 0) return;
        double chi = 0.0;
        int n = 0;
        double worst = 0.0, sumA = 0.0, sumB = 0.0;
        double worstAt = 0.0;
        for (int i = 0; i < a.NodeCount; i++)
        {
            double fa = Sum(a.BackscatterRows[i]), fb = Sum(b.BackscatterRows[i]);
            long na = a.NodeHistories != null ? a.NodeHistories[i] : a.Histories, nb = b.NodeHistories != null ? b.NodeHistories[i] : b.Histories;
            double var = fa / Math.Max(1, na) + fb / Math.Max(1, nb);
            sumA += fa;
            sumB += fb;
            if (var <= 0.0) continue;
            double z = (fa - fb) / Math.Sqrt(var);
            chi += z * z;
            n++;
            if (Math.Abs(z) > Math.Abs(worst)) { worst = z; worstAt = a.Energies[i]; }
        }

        Console.WriteLine(Fmt("   узлов {0}, Σχ²/n {1:F3}, худший z {2:F2} на {3:F1} кэВ; сумма площадей A/B {4:F5}", n, n > 0 ? chi / n : 0.0, worst, worstAt, sumB > 0 ? sumA / sumB : 0.0));
        Check(n > 0 && chi / n < 2.0 && Math.Abs(worst) < 5.0, "CPU↔GPU в пределах статистики");
    }

    static void ModeBodies(string pathA, string pathB)
    {
        ResponseMatrix a = Load(pathA), b = Load(pathB);
        Console.WriteLine(Fmt("   отпечаток тела A {0}", a.BodyFingerprint));
        Console.WriteLine(Fmt("   отпечаток тела B {0}", b.BodyFingerprint));
        Check(a.Stamp == b.Stamp, "клеймо то же");
        Check(a.BodyFingerprint == b.BodyFingerprint, "тело то же побитно: каналы (и `Compton`) не изменились, случайных чисел метка не тратит");
    }

    static void ModeOld(string path)
    {
        ResponseMatrix m = Load(path);
        Check(!m.HasBackscatter, "прежний файл читается, строк нет");
        string copy = Path.Combine(Path.GetTempPath(), "a311_" + Guid.NewGuid().ToString("N") + ".rmx");
        try
        {
            m.Save(copy);
            ResponseMatrix back = Load(copy);
            Check(back.Stamp == m.Stamp && back.BodyFingerprint == m.BodyFingerprint && !back.HasBackscatter, "пересохранение: клеймо и тело те же, строк нет");
            Check(Sum(new float[0]) == 0.0, "пустая строка не падает");
        }
        finally
        {
            File.Delete(copy);
        }
    }

    static void ModeStrip(string path, string output, bool inject)
    {
        ResponseMatrix m = Load(path);
        if (inject)
        {
            m.BackscatterRows = new float[m.NodeCount][];
            for (int i = 0; i < m.NodeCount; i++)
            {
                float[] whole = m.ChannelRows[Compton][i];
                float[] row = new float[whole.Length];
                for (int b = 0; b < row.Length; b++) row[b] = whole[b] * 0.1f;
                m.BackscatterRows[i] = row;
            }
        }
        else
        {
            m.BackscatterRows = null;
        }

        string body = m.BodyFingerprint;
        m.Save(output);
        ResponseMatrix back = Load(output);
        Check(back.BodyFingerprint == body && back.HasBackscatter == inject, Path.GetFileName(output) + ": тело то же, строки " + (inject ? "подложены" : "сняты"));
    }

    // ---------------------------------------------------------------- разбор

    static ResultData LoadSpectrum(string path)
    {
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        ResultDataFile file;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            file = (ResultDataFile)serializer.Deserialize(stream);
        }

        return file.ResultDataList[0];
    }

    static FsaResult Analyze(ResultData rd, ResponseMatrix matrix, string[] chains, string[] nuclides, bool backscatter)
    {
        var options = new FsaCalculationOptions { DbLookups = true, Backscatter = backscatter };
        FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, options.ChainEquilibrium, options.AtomicXray);
        options.ApplyTo(spec);
        List<FsaComponent> library = FsaSampleLibrary.Build(spec);
        var analyzer = new FsaAnalyzer();
        options.ApplyTo(analyzer);
        analyzer.AdoptSpectrum(rd);
        FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
        analyzer.BackscatterWithMatrix = false;
        if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
        {
            double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
            analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
        }

        if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
        {
            analyzer.MinEnergy = peakConfig.Min_Range;
            analyzer.MaxEnergy = peakConfig.Max_Range;
        }

        FsaTuningReport.Print(analyzer);
        return analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library, FsaEfficiency.FromConfig(rd.Efficiency));
    }

    static bool SameCurves(FsaResult a, FsaResult b, out string where)
    {
        where = null;
        if (a.Components.Count != b.Components.Count) { where = "число компонент"; return false; }
        for (int k = 0; k < a.Components.Count; k++)
        {
            FsaComponentResult x = a.Components[k], y = b.Components[k];
            if (x.Name != y.Name) { where = "имя " + x.Name; return false; }
            if (x.Curve == null || y.Curve == null || x.Curve.Length != y.Curve.Length) { where = "кривая " + x.Name; return false; }
            for (int i = 0; i < x.Curve.Length; i++)
            {
                if (!x.Curve[i].Equals(y.Curve[i])) { where = Fmt("{0}, канал {1}", x.Name, i); return false; }
            }

            if (!x.CountRate.Equals(y.CountRate)) { where = "скорость " + x.Name; return false; }
        }

        return true;
    }

    static int RowCount(FsaResult result)
    {
        int n = 0;
        foreach (FsaReportRow row in FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false).Rows)
        {
            if (row.Kind == FsaReportRowKind.Backscatter || row.Kind == FsaReportRowKind.BackscatterLine)
            {
                n++;
                Console.WriteLine("   ROW\t" + row.Kind + "\t" + row.Name + "\t" + row.Value);
            }
        }

        return n;
    }

    static void ModeFsa(string corpus, string store, string keys)
    {
        var manifest = new Dictionary<string, string[]>();
        string[] header = null;
        foreach (string line in File.ReadAllLines(Path.Combine(corpus, "manifest.csv")))
        {
            string[] parts = line.TrimStart('﻿').Split(',');
            if (header == null) { header = parts; continue; }
            manifest[parts[0]] = parts;
        }

        int chainsAt = Array.IndexOf(header, "chains"), nuclidesAt = Array.IndexOf(header, "nuclides");
        foreach (string key in keys.Split(','))
        {
            Console.WriteLine("== " + key);
            ResultData rd = LoadSpectrum(Path.Combine(corpus, "spectra", key + ".xml"));
            string[] row = manifest[key];
            string[] chains = row[chainsAt].Split(new[] { ';', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] nuclides = row[nuclidesAt].Split(new[] { ';', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            ResponseMatrix matrix = Load(Path.Combine(store, rd.Efficiency.Name + ".rmx"));
            Check(matrix.IsValidFor(rd.Efficiency.Geometry) && matrix.HasBackscatter, "матрица годна и несёт строки");
            ElectronSectionRefusal why;
            bool section = matrix.ElectronSectionFor(rd.Efficiency.Geometry, out why) != null;
            Console.WriteLine("   раздел электрона: " + (section ? "годен" : why.ToString()));
            FsaResult on = Analyze(rd, matrix, chains, nuclides, true);
            Check(on != null, "разбор есть");
            if (on == null) continue;
            int rows = RowCount(on);
            Check(rows > 0, "строки обратного рассеяния есть, " + rows);
            foreach (FsaComponentResult c in on.Components)
            {
                if (c.BackscatterCurve == null) continue;
                double sum = 0.0, rib = 0.0, over = 0.0;
                for (int i = 0; i < c.BackscatterCurve.Length; i++)
                {
                    sum += c.BackscatterCurve[i];
                    rib += c.Curve[i];
                    over = Math.Max(over, c.BackscatterCurve[i] - c.Curve[i]);
                }

                Console.WriteLine(Fmt("   {0}: подслой {1:F0} отсч. из {2:F0} ({3:F2} %), выше ленты на {4:E2}", c.Name, sum, rib, rib > 0 ? 100.0 * sum / rib : 0.0, over));
                Check(over <= 1e-6 * Math.Max(1.0, rib), c.Name + ": подслой внутри ленты");
                double named = 0.0;
                if (c.BackscatterLines != null)
                {
                    foreach (FsaBackscatterLine line in c.BackscatterLines) named += line.Counts;
                }

                Console.WriteLine(Fmt("   {0}: сумма подписей {1:F1}, подслой {2:F1}", c.Name, named, sum));
                Check(Math.Abs(named - sum) <= 1e-6 * Math.Max(1.0, sum), c.Name + ": сумма подписей равна подслою");
                foreach (FsaBackscatterLine line in c.BackscatterLines ?? new List<FsaBackscatterLine>())
                {
                    if (line.Counts > 0.02 * sum)
                    {
                        Console.WriteLine(Fmt("      линия {0:F1} кэВ: центр {1:F0} кэВ, {2:F0} отсч., канал {3:F1}", line.ParentKev, line.EnergyKev, line.Counts, line.Channel));
                    }
                }
            }

            FsaResult off = Analyze(rd, matrix, chains, nuclides, false);
            string where;
            Check(off != null && SameCurves(on, off, out where), "снятая галка: образы и скорости те же побитно");
            int offRows = RowCount(off);
            Check(offRows == 0, "снятая галка: строк нет");
            bool anyCurve = false;
            foreach (FsaComponentResult c in off.Components) anyCurve |= c.BackscatterCurve != null;
            Check(!anyCurve, "снятая галка: подслоя нет");

            float[][] rowsSaved = matrix.BackscatterRows;
            matrix.BackscatterRows = null;
            FsaResult old = Analyze(rd, matrix, chains, nuclides, true);
            matrix.BackscatterRows = rowsSaved;
            Check(old != null && SameCurves(on, old, out where), "старая матрица без строк: образы те же побитно");
            Check(RowCount(old) == 0, "старая матрица без строк: строки нет");
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        FsaTuningReport.Snapshot();
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = null, matrix = null, a = null, b = null, output = null, corpus = null, store = null, keys = null, ui = "en";
        foreach (string s in args)
        {
            if (s.StartsWith("--mode=", StringComparison.Ordinal)) mode = s.Substring(7);
            else if (s.StartsWith("--matrix=", StringComparison.Ordinal)) matrix = s.Substring(9);
            else if (s.StartsWith("--a=", StringComparison.Ordinal)) a = s.Substring(4);
            else if (s.StartsWith("--b=", StringComparison.Ordinal)) b = s.Substring(4);
            else if (s.StartsWith("--out=", StringComparison.Ordinal)) output = s.Substring(6);
            else if (s.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = s.Substring(9);
            else if (s.StartsWith("--store=", StringComparison.Ordinal)) store = s.Substring(8);
            else if (s.StartsWith("--keys=", StringComparison.Ordinal)) keys = s.Substring(7);
            else if (s.StartsWith("--ui=", StringComparison.Ordinal)) ui = s.Substring(5);
            else { Console.WriteLine("не знаю ключа: " + s); return 2; }
        }

        var culture = new CultureInfo(ui == "ru" ? "ru-RU" : "en-US");
        Thread.CurrentThread.CurrentUICulture = culture;
        BecquerelMonitor.Properties.Resources.Culture = culture;
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        try
        {
            switch (mode)
            {
                case "check": ModeCheck(matrix); break;
                case "rows": ModeRows(a, b); break;
                case "bodies": ModeBodies(a, b); break;
                case "binsdiff": ModeBinsDiff(a, b); break;
                case "old": ModeOld(matrix); break;
                case "strip": ModeStrip(matrix, output, false); break;
                case "inject": ModeStrip(matrix, output, true); break;
                case "fsa": ModeFsa(corpus, store, keys); break;
                default: Console.WriteLine("нужен --mode="); return 2;
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("⛔ проба оборвалась: " + ex);
        }

        Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
        return failures == 0 ? 0 : 1;
    }
}
