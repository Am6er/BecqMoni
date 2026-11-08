using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

// (`AMBER318`, 07.11.2026) Останов строк раздела электрона по шуму: приёмка (б) и (в).
//
//   ElectronStopProbeA318 --mode=fsa --corpus=<tools\CORPUS\corpus> --key=<спектр> --matrices=<a.rmx;b.rmx;…> [--target=K-40]
//       разбор спектра корпуса (состав — его строка `manifest.csv`, фон и калибровка — его) на каждой
//       матрице списка; печатает скорость строки-цели (активность в Бк: образ на распад), её z и χ²/ndf,
//       и по списку — среднее и СКО скорости цели. Матрицы — одна сцена с разными разделами
//       (зёрна, плоско или с остановом); фотонная часть у них общая, разница — только раздел.
//   ElectronStopProbeA318 --mode=rows --matrix=<.rmx>
//       истории строк раздела (узлы, линии, ветви): сколько раньше номинала, на номинале, сверх него.
//   ElectronStopProbeA318 --mode=progress --scene=<.in> --matrix=<.rmx> [--n=100000] [--stop=3] [--threads=15] [--out=<след>]
//   ElectronStopProbeA318 --mode=progress --trace=<след> [--expect-fail]
//       (пятый заход) ход раздела на ЦП с остановом путём окна: полоса не пятится, шаг ≤ 15 п.п., простой ≤ 25 %
//       счёта; по готовому следу (формат пробы критика `SectionProgressCrit`) — тот же судья, контроль.
//
// Код: 0 — сошлось; 1 — нет; 2 — ключи или данные.
static class ElectronStopProbeA318
{
    static int failures;

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

    static ResultData LoadSpectrum(string path)
    {
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            return ((ResultDataFile)serializer.Deserialize(stream)).ResultDataList[0];
        }
    }

    static FsaResult Analyze(ResultData rd, ResponseMatrix matrix, string[] chains, string[] nuclides)
    {
        var options = new FsaCalculationOptions { DbLookups = true };
        FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, options.ChainEquilibrium, options.AtomicXray);
        options.ApplyTo(spec);
        List<FsaComponent> library = FsaSampleLibrary.Build(spec);
        var analyzer = new FsaAnalyzer();
        options.ApplyTo(analyzer);
        analyzer.AdoptSpectrum(rd);
        FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
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
        return analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library,
                                FsaEfficiency.FromConfig(rd.Efficiency));
    }

    static string Norm(string name)
    {
        return (name ?? "").Replace("-", "").ToUpperInvariant();
    }

    static void ModeFsa(string corpus, string key, string matrices, string target)
    {
        var manifest = new Dictionary<string, string[]>();
        string[] header = null;
        foreach (string line in File.ReadAllLines(Path.Combine(corpus, "manifest.csv")))
        {
            string[] parts = line.TrimStart('﻿').Split(',');
            if (header == null) { header = parts; continue; }
            manifest[parts[0]] = parts;
        }

        string[] row = manifest[key];
        string[] chains = row[Array.IndexOf(header, "chains")].Split(new[] { ';', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string[] nuclides = row[Array.IndexOf(header, "nuclides")].Split(new[] { ';', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        ResultData rd = LoadSpectrum(Path.Combine(corpus, "spectra", key + ".xml"));
        Console.WriteLine(Fmt("== {0}: сцена {1}, цель {2}", key, rd.Efficiency.Name, target));
        var rates = new List<double>();
        foreach (string path in matrices.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            ResponseMatrix matrix = Load(path);
            ElectronSectionRefusal why;
            ResponseElectronSection section = matrix.ElectronSectionFor(rd.Efficiency.Geometry, out why);
            if (section == null)
            {
                failures++;
                Console.WriteLine(Fmt("   ОТКАЗ {0}: раздел не годен ({1})", Path.GetFileName(path), why));
                continue;
            }

            long spent = 0;
            foreach (long h in section.Histories) spent += h;
            if (section.LineHistories != null) foreach (long h in section.LineHistories) spent += h;
            if (section.BranchHistories != null) foreach (long h in section.BranchHistories) spent += h;
            FsaResult result = Analyze(rd, matrix, chains, nuclides);
            FsaComponentResult hit = null;
            foreach (FsaComponentResult c in result.Components)
            {
                if (Norm(c.Name) == Norm(target) || Norm(c.Name).StartsWith(Norm(target), StringComparison.Ordinal)) { hit = c; break; }
            }

            if (hit == null)
            {
                failures++;
                Console.WriteLine(Fmt("   ОТКАЗ {0}: строки цели нет", Path.GetFileName(path)));
                continue;
            }

            rates.Add(hit.CountRate);
            Console.WriteLine(Fmt("   {0,-28} {1}={2:F3} Бк z {3:F1}, бета {4:F0} отсч., χ²/ndf {5:F4}; раздел n={6}, историй {7}",
                                  Path.GetFileName(path), hit.Name, hit.CountRate, hit.Z, hit.BetaCounts, result.Chi2Ndf,
                                  section.StampField("n"), spent));
        }

        if (rates.Count > 1)
        {
            double mean = 0.0;
            foreach (double r in rates) mean += r;
            mean /= rates.Count;
            double var = 0.0;
            foreach (double r in rates) var += (r - mean) * (r - mean);
            double sd = Math.Sqrt(var / (rates.Count - 1));
            Console.WriteLine(Fmt("   ИТОГ: зёрен {0}, среднее {1:F3} Бк, СКО {2:F3} Бк = {3:F3} %", rates.Count, mean, sd, 100.0 * sd / mean));
        }
    }

    static void ModeRows(string path)
    {
        ResponseMatrix m = Load(path);
        ResponseElectronSection s = m.ElectronSection;
        if (s == null) { failures++; Console.WriteLine("ОТКАЗ: раздела нет"); return; }
        int nominal = int.Parse(s.StampField("n"), CultureInfo.InvariantCulture);
        var all = new List<KeyValuePair<string, long>>();
        for (int i = 0; i < s.Histories.Length; i++) all.Add(new KeyValuePair<string, long>(Fmt("узел {0} {1:F1} кэВ", i, s.Energies[i]), s.Histories[i]));
        if (s.LineHistories != null) for (int j = 0; j < s.LineHistories.Length; j++) all.Add(new KeyValuePair<string, long>(Fmt("линия {0} {1:F1} кэВ", j, s.LineEnergies[j]), s.LineHistories[j]));
        if (s.BranchHistories != null) for (int j = 0; j < s.BranchHistories.Length; j++) all.Add(new KeyValuePair<string, long>(Fmt("ветвь {0} {1} {2:F1} кэВ", j, s.BranchNuclides[j], s.BranchEndpoints[j]), s.BranchHistories[j]));
        int below = 0, at = 0, above = 0;
        long spent = 0, min = long.MaxValue, max = 0;
        foreach (var kv in all)
        {
            spent += kv.Value;
            min = Math.Min(min, kv.Value);
            max = Math.Max(max, kv.Value);
            if (kv.Value < nominal) below++; else if (kv.Value > nominal) above++; else at++;
            Console.WriteLine(Fmt("   {0,-36} {1,9}", kv.Key, kv.Value));
        }

        Console.WriteLine(Fmt("ИТОГ {0}: строк {1}, номинал {2}: раньше номинала {3}, на номинале {4}, сверх {5}; меньше всех {6}, больше всех {7}; всего {8} = {9:F3} плоского",
                              Path.GetFileName(path), all.Count, nominal, below, at, above, min, max, spent, (double)spent / ((double)all.Count * nominal)));
    }

    // ------------------------------------------------------------------
    // (`AMBER318`, пятый заход) Ход раздела на ЦП с остановом — растёт ли ровно
    // ------------------------------------------------------------------

    /// <summary>Отчёты хода раздела по времени — тем же форматом, что проба критика `SectionProgressCrit`.</summary>
    sealed class Trace : IProgress<ResponseMatrixProgress>
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public readonly List<string> Lines = new List<string>();

        public void Report(ResponseMatrixProgress p)
        {
            lock (this)
            {
                if (p.SectionTotal <= 0) return;
                this.Lines.Add(Fmt("{0,8:F1} s  SectionDone {1}/{2}  passes {3}/{4}  hist {5}/{6}  pct {7:F1}",
                                   this.Clock.Elapsed.TotalSeconds, p.SectionDone, p.SectionTotal, p.Done, p.Total,
                                   p.DoneHistories, p.TotalHistories, p.Percent));
            }
        }
    }

    /// <summary>
    /// Судья хода по строкам формата `SectionProgressCrit`: полоса не пятится; ни один отчёт не прибавляет
    /// больше <paramref name="maxStep"/> процентных пунктов; ни один отрезок времени без прироста полосы не
    /// длиннее <paramref name="maxStall"/> доли всего счёта.
    /// </summary>
    static bool JudgeTrace(IList<string> lines, double maxStep, double maxStall, out string why)
    {
        var t = new List<double>();
        var pct = new List<double>();
        foreach (string line in lines)
        {
            string s = line.Trim();
            int sPos = s.IndexOf(" s ", StringComparison.Ordinal), pPos = s.LastIndexOf("pct ", StringComparison.Ordinal);
            if (sPos < 0 || pPos < 0) continue;
            double tt, pp;
            if (!double.TryParse(s.Substring(0, sPos).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out tt)) continue;
            if (!double.TryParse(s.Substring(pPos + 4).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out pp)) continue;
            t.Add(tt);
            pct.Add(pp);
        }

        if (t.Count < 2)
        {
            why = "отчётов меньше двух";
            return false;
        }

        double total = t[t.Count - 1] - t[0];
        double worstStep = 0.0, worstStall = 0.0, stallFrom = t[0];
        int back = 0;
        for (int i = 1; i < t.Count; i++)
        {
            double d = pct[i] - pct[i - 1];
            if (d < -1e-9) back++;
            worstStep = Math.Max(worstStep, d);
            if (d > 1e-9)
            {
                worstStall = Math.Max(worstStall, t[i - 1] - stallFrom);
                stallFrom = t[i];
            }
        }

        worstStall = Math.Max(worstStall, t[t.Count - 1] - stallFrom);
        double stallShare = total > 0.0 ? worstStall / total : 0.0;
        why = Fmt("отчётов {0}, счёт {1:F1} с; полоса пятилась {2} раз; наибольший шаг {3:F1} п.п. (порог {4:F0}); наибольший простой {5:F1} с = {6:F3} счёта (порог {7:F2})",
                  t.Count, total, back, worstStep, maxStep, worstStall, stallShare, maxStall);
        return back == 0 && worstStep <= maxStep && stallShare <= maxStall;
    }

    /// <summary>
    /// `--mode=progress --scene= --matrix= --n= [--stop=3] [--threads=15] [--out=<след.txt>]` — путь окна
    /// (`AttachElectronSection` с ходом) и судья хода; `--mode=progress --trace=<файл>` — тот же судья по
    /// готовому следу (положительный контроль: след критика на прежнем коде обязан не пройти).
    /// </summary>
    static void ModeProgress(string scene, string matrixPath, int n, double stop, int threads, string trace, string outPath, bool expectFail)
    {
        IList<string> lines;
        if (trace != null)
        {
            lines = File.ReadAllLines(trace);
            Console.WriteLine("== ход по следу " + Path.GetFileName(trace));
        }
        else
        {
            GeometryModel g = GeometryModel.Load(scene);
            ResponseMatrix m = Load(matrixPath);
            m.ElectronSection = null;
            var sink = new Trace();
            ElectronSectionOutcome outcome = ResponseMatrixBuilder.AttachElectronSection(
                g, m, null, null, threads, sink, System.Threading.CancellationToken.None, null, n, stop);
            Console.WriteLine(Fmt("== ход раздела: {0}, n={1}, цель {2} %, потоков {3}: {4}, {5:F1} с",
                                  Path.GetFileName(scene), n, stop, threads, outcome, sink.Clock.Elapsed.TotalSeconds));
            lines = sink.Lines;
            if (outPath != null)
            {
                File.WriteAllLines(outPath, sink.Lines);
            }
        }

        string why;
        bool ok = JudgeTrace(lines, 15.0, 0.25, out why);
        Console.WriteLine("   " + why);
        if (expectFail)
        {
            Console.WriteLine(ok ? "   ОТКАЗ контроля: ступенчатый след признан ровным" : "   OK  контроль: ступенчатый след НЕ признан ровным");
            if (ok) failures++;
        }
        else
        {
            Console.WriteLine(ok ? "   OK  ход растёт ровно" : "   ОТКАЗ ход стоит или прыгает");
            if (!ok) failures++;
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        FsaTuningReport.Snapshot();
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = null, corpus = null, key = null, matrices = null, matrix = null, target = "K-40";
        string scene = null, trace = null, outPath = null;
        int n = 100000, threads = 15;
        double stop = 3.0;
        bool expectFail = false;
        foreach (string s in args)
        {
            if (s.StartsWith("--mode=", StringComparison.Ordinal)) mode = s.Substring(7);
            else if (s.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = s.Substring(9);
            else if (s.StartsWith("--key=", StringComparison.Ordinal)) key = s.Substring(6);
            else if (s.StartsWith("--matrices=", StringComparison.Ordinal)) matrices = s.Substring(11);
            else if (s.StartsWith("--matrix=", StringComparison.Ordinal)) matrix = s.Substring(9);
            else if (s.StartsWith("--target=", StringComparison.Ordinal)) target = s.Substring(9);
            else if (s.StartsWith("--scene=", StringComparison.Ordinal)) scene = s.Substring(8);
            else if (s.StartsWith("--trace=", StringComparison.Ordinal)) trace = s.Substring(8);
            else if (s.StartsWith("--out=", StringComparison.Ordinal)) outPath = s.Substring(6);
            else if (s.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(s.Substring(4), CultureInfo.InvariantCulture);
            else if (s.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(s.Substring(10), CultureInfo.InvariantCulture);
            else if (s.StartsWith("--stop=", StringComparison.Ordinal)) stop = double.Parse(s.Substring(7), CultureInfo.InvariantCulture);
            else if (s == "--expect-fail") expectFail = true;
            else { Console.WriteLine("не знаю ключа: " + s); return 2; }
        }

        Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        switch (mode)
        {
            case "fsa": ModeFsa(corpus, key, matrices, target); break;
            case "rows": ModeRows(matrix); break;
            case "progress": ModeProgress(scene, matrix, n, stop, threads, trace, outPath, expectFail); break;
            default: Console.WriteLine("нужен --mode=fsa|rows|progress"); return 2;
        }

        Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : Fmt("ОТКАЗОВ {0}", failures));
        return failures == 0 ? 0 : 1;
    }
}
