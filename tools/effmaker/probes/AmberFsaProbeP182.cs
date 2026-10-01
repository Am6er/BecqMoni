// П182 (29.09.2026): воспроизведение и приёмка строк AMBER136, AMBER137, AMBER138.
//
// Проба зовёт ТЕКУЩИЕ методы приложения (закрытые — отражением, не подменяя
// их тел) и печатает числа, по которым строка подтверждается или закрывается.
// Одна и та же проба гоняется на сборке «до» и «после» — различие плеч только
// в приложении.
//
//   AmberFsaProbeP182.exe --part=136 --store=<каталог *.rmx> [--max=N]
//   AmberFsaProbeP182.exe --part=137
//   AmberFsaProbeP182.exe --part=138 --curves=<каталог>;<каталог>
//
// 136 — самосогласованность суммирователя и матрицы: пиковая и полная
//       эффективность суммирователя против суммы строки, которую реально
//       кладёт матрица, на серединах и четвертях каждого промежутка узлов;
//       площадь сумм-пика, положенного `AccumulateSumPeaks`, против заданной
//       (искусственная двухузловая матрица из журнала и все матрицы склада).
// 137 — трёхканальный фит `FitOnce` с колонкой наложений под пределом:
//       σ и z сигнала, ndf; контроли — предела нет и предел 0.
// 138 — повтор энергии в кривой `FsaEfficiency`: принята ли кривая и что
//       отвечает `TryEval`; побитовая печать `Eval`/`TryEval` по всем кривым
//       из XML-файлов данных (приёмка «кривые без повтора не изменились»).
//
// Числа — инвариантной культурой.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;

public static class AmberFsaProbeP182
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static string R(double v) { return v.ToString("R", Inv); }

    static object Get(object o, string n)
    {
        FieldInfo f = o.GetType().GetField(n, Any);
        if (f != null) return f.GetValue(o);
        return o.GetType().GetProperty(n, Any).GetValue(o, null);
    }

    public static int Main(string[] args)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = Inv;
        string part = "all", store = null, curves = null;
        int max = int.MaxValue;
        foreach (string a in args)
        {
            if (a.StartsWith("--part=")) part = a.Substring(7);
            else if (a.StartsWith("--store=")) store = a.Substring(8);
            else if (a.StartsWith("--curves=")) curves = a.Substring(9);
            else if (a.StartsWith("--max=")) max = int.Parse(a.Substring(6), Inv);
            else if (a.StartsWith("--nodes=")) { Nodes(a.Substring(8)); return 0; }
            else { Console.Error.WriteLine("незнакомый ключ: " + a); return 2; }
        }

        if (part == "136" || part == "all") Part136(store, max);
        if (part == "137" || part == "all") Part137();
        if (part == "138" || part == "all") Part138(curves);
        return 0;
    }

    // ------------------------------------------------------------------
    // AMBER136
    // ------------------------------------------------------------------

    static void Part136(string store, int max)
    {
        Console.WriteLine("=== AMBER136: искусственная матрица 100/300 кэВ, пики 0.4/0.1, бин 1 кэВ ===");
        var matrix = new ResponseMatrix { BinKev = 1, Energies = new[] { 100.0, 300.0 }, TransferByChannel = true };
        matrix.ChannelRows = new float[EfficiencySimulator.ResponseChannelCount][][];
        for (int c = 0; c < matrix.ChannelRows.Length; c++) matrix.ChannelRows[c] = new[] { new float[101], new float[301] };
        matrix.ChannelRows[0][0][100] = .4f; matrix.ChannelRows[0][1][300] = .1f; matrix.RebuildTotals();
        foreach (double e in new[] { 100.0, 150.0, 200.0, 250.0, 300.0 })
        {
            double actual = SumPeakArea(matrix, FsaCascadeSummer.Create(matrix), e, 0.04);
            Console.WriteLine("synthetic E={0} requested=0.04 actual={1} rel={2}%", R(e), R(actual),
                              (100 * (actual / 0.04 - 1)).ToString("F6", Inv));
        }

        if (store == null) return;
        string[] files = Directory.GetFiles(store, "*.rmx").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        double worstPeak = 0, worstTotal = 0, worstSum = 0;
        string worstPeakAt = "", worstTotalAt = "", worstSumAt = "";
        double[] bandLo = { 0, 20, 40, 100, 300 };
        double[] bandPeak = new double[bandLo.Length], bandTotal = new double[bandLo.Length], bandSum = new double[bandLo.Length];
        string[] bandPeakAt = new string[bandLo.Length];
        int n = 0, points = 0;
        foreach (string path in files)
        {
            if (n >= max) break;
            ResponseMatrix m = ResponseMatrix.Load(path);
            if (m == null || !m.HasChannels) { Console.WriteLine("skip {0}", Path.GetFileName(path)); continue; }
            m.TransferByChannel = true;
            FsaCascadeSummer summer = FsaCascadeSummer.Create(m);
            if (summer == null) { Console.WriteLine("no summer {0}", Path.GetFileName(path)); continue; }
            n++;
            double fileWorstPeak = 0, fileWorstTotal = 0, fileWorstSum = 0;
            double[] grid = m.Energies;
            for (int i = 0; i + 1 < grid.Length; i++)
            {
                foreach (double q in new[] { 0.25, 0.5, 0.75 })
                {
                    double e = grid[i] + q * (grid[i + 1] - grid[i]);
                    int len = ResponseMatrix.ImageBins(e, m.BinKev) + 4;
                    var row = new double[len];
                    m.AccumulateChannel(row, e, 1.0, (int)EfficiencySimulator.ResponseChannel.Peak);
                    double rowPeak = row.Sum();
                    double rowTotal = m.Evaluate(e, len).Sum();
                    double sPeak = summer.PeakEfficiency(e), sTotal = summer.TotalEfficiency(e);
                    points++;
                    int band = 0;
                    while (band + 1 < bandLo.Length && e >= bandLo[band + 1]) band++;
                    if (rowPeak > 0 && sPeak > 0 && Math.Abs(rowPeak / sPeak - 1) > Math.Abs(bandPeak[band]))
                    {
                        bandPeak[band] = rowPeak / sPeak - 1;
                        bandPeakAt[band] = Path.GetFileName(path) + " E=" + R(e) + " row=" + R(rowPeak) + " summer=" + R(sPeak);
                    }
                    if (rowTotal > 0 && sTotal > 0 && Math.Abs(rowTotal / sTotal - 1) > Math.Abs(bandTotal[band]))
                        bandTotal[band] = rowTotal / sTotal - 1;
                    if (rowPeak > 0 && sPeak > 0)
                    {
                        double rel = rowPeak / sPeak - 1;
                        if (Math.Abs(rel) > Math.Abs(fileWorstPeak)) fileWorstPeak = rel;
                        if (Math.Abs(rel) > Math.Abs(worstPeak)) { worstPeak = rel; worstPeakAt = Path.GetFileName(path) + " E=" + R(e); }
                        double area = SumPeakArea(m, summer, e, 1.0e-3);
                        double relSum = area / 1.0e-3 - 1;
                        if (Math.Abs(relSum) > Math.Abs(bandSum[band])) bandSum[band] = relSum;
                        if (Math.Abs(relSum) > Math.Abs(fileWorstSum)) fileWorstSum = relSum;
                        if (Math.Abs(relSum) > Math.Abs(worstSum)) { worstSum = relSum; worstSumAt = Path.GetFileName(path) + " E=" + R(e); }
                    }

                    if (rowTotal > 0 && sTotal > 0)
                    {
                        double rel = rowTotal / sTotal - 1;
                        if (Math.Abs(rel) > Math.Abs(fileWorstTotal)) fileWorstTotal = rel;
                        if (Math.Abs(rel) > Math.Abs(worstTotal)) { worstTotal = rel; worstTotalAt = Path.GetFileName(path) + " E=" + R(e); }
                    }
                }
            }

            Console.WriteLine("matrix {0} nodes={1} worst peak {2}% total {3}% sumpeak {4}%", Path.GetFileName(path), grid.Length,
                              (100 * fileWorstPeak).ToString("F6", Inv), (100 * fileWorstTotal).ToString("F6", Inv),
                              (100 * fileWorstSum).ToString("F6", Inv));
        }

        Console.WriteLine("STORE matrices={0} points={1}", n, points);
        Console.WriteLine("STORE worst peak rel = {0}% at {1}", (100 * worstPeak).ToString("F6", Inv), worstPeakAt);
        Console.WriteLine("STORE worst total rel = {0}% at {1}", (100 * worstTotal).ToString("F6", Inv), worstTotalAt);
        Console.WriteLine("STORE worst sum-peak area rel = {0}% at {1}", (100 * worstSum).ToString("F6", Inv), worstSumAt);
        for (int b = 0; b < bandLo.Length; b++)
            Console.WriteLine("BAND E>={0} keV: worst peak {1}% total {2}% sum-peak area {3}% | {4}", R(bandLo[b]),
                              (100 * bandPeak[b]).ToString("F6", Inv), (100 * bandTotal[b]).ToString("F6", Inv),
                              (100 * bandSum[b]).ToString("F6", Inv), bandPeakAt[b]);
    }

    /// <summary>`--nodes=<файл.rmx>@<от>@<до>`: узлы матрицы в полосе и суммы их строк (пик, всё).</summary>
    static void Nodes(string spec)
    {
        string[] p = spec.Split('@');
        double lo = double.Parse(p[1], Inv), hi = double.Parse(p[2], Inv);
        ResponseMatrix m = ResponseMatrix.Load(p[0]);
        float[][] peak = m.ChannelRows[(int)EfficiencySimulator.ResponseChannel.Peak];
        for (int i = 0; i < m.Energies.Length; i++)
        {
            if (m.Energies[i] < lo || m.Energies[i] > hi) continue;
            double sp = 0, st = 0;
            foreach (float v in peak[i]) sp += v;
            foreach (float v in m.Rows[i]) st += v;
            Console.WriteLine("node {0} E={1} peak={2} total={3}", i, R(m.Energies[i]), R(sp), R(st));
        }
    }

    /// <summary>Площадь, которую `AccumulateSumPeaks` кладёт для одного сумм-пика.</summary>
    static double SumPeakArea(ResponseMatrix matrix, FsaCascadeSummer summer, double e, double area)
    {
        // (`T263`, П193) матрица — через `FsaMatrixBinding.Bind`, как у приложения: с нею едут Q_k угловых корреляций и обстановка (домик); сцена синтетическая — без геометрии
        var analyzer = new FsaAnalyzer();
        FsaMatrixBinding.Bind(analyzer, null, matrix);
        typeof(FsaAnalyzer).GetField("cascade", Any).SetValue(analyzer, summer);
        var correction = new FsaCascadeSummer.Correction
        {
            SumPeaks = new List<FsaCascadeSummer.SumPeak> { new FsaCascadeSummer.SumPeak(e, area) }
        };
        var deposit = new double[ResponseMatrix.ImageBins(e, matrix.BinKev) + 16];
        typeof(FsaAnalyzer).GetMethod("AccumulateSumPeaks", Any)
            .Invoke(analyzer, new object[] { deposit, null, correction, false });
        return deposit.Sum();
    }

    // ------------------------------------------------------------------
    // AMBER137
    // ------------------------------------------------------------------

    static void Part137()
    {
        Console.WriteLine("=== AMBER137: signal=[1,0,0], pileup=[0.9,0.1,0], y=[10,1,0], w=1 ===");
        MethodInfo fitOnce = typeof(FsaAnalyzer).GetMethod("FitOnce", Any);
        MethodInfo columnSigma = typeof(FsaAnalyzer).GetMethod("ColumnSigma", Any);
        foreach (double cap in new[] { double.NaN, 0.0, 1.0, 5.0, 20.0 })
        {
            var analyzer = new FsaAnalyzer();
            var a = new FsaComponent("signal", FsaComponentKind.Single) { FixedTemplate = new[] { 1.0, 0.0, 0.0 } };
            var b = new FsaComponent("pileup", FsaComponentKind.Nuisance) { FixedTemplate = new[] { .9, .1, 0.0 }, AmplitudeCap = cap };
            object fit = fitOnce.Invoke(analyzer, new object[]
            {
                new List<FsaComponent> { a, b }, new List<double[]>(), null, null, null, 1.0, 0.0, 0, 2, 3,
                new[] { 10.0, 1.0, 0.0 }, new[] { 1.0, 1.0, 1.0 }, null
            });
            var amp = (double[])Get(fit, "Amplitude");
            var sig = (double[])Get(fit, "Sigma");
            var z = (double[])Get(fit, "Z");
            var active = (bool[])Get(fit, "Active");
            double cs0 = (double)columnSigma.Invoke(null, new[] { fit, (object)0 });
            double cs1 = (double)columnSigma.Invoke(null, new[] { fit, (object)1 });
            Console.WriteLine("cap={0} amplitude={1},{2} sigma={3},{4} z={5},{6} active={7},{8} ColumnSigma={9},{10} chi2={11} ndf={12} ndfBase={13}",
                              R(cap), R(amp[0]), R(amp[1]), R(sig[0]), R(sig[1]), R(z[0]), R(z[1]), active[0], active[1],
                              R(cs0), R(cs1), R((double)Get(fit, "Chi2")), R((double)Get(fit, "Ndf")),
                              Convert.ToString(Get(fit, "NdfBase"), Inv));
        }
    }

    // ------------------------------------------------------------------
    // AMBER138
    // ------------------------------------------------------------------

    static EfficiencyConfigData Config(params double[] pairs)
    {
        var cfg = new EfficiencyConfigData { Curve = new List<ROIEfficiencyData>() };
        for (int i = 0; i + 1 < pairs.Length; i += 2)
            cfg.Curve.Add(new ROIEfficiencyData { Energy = pairs[i], Efficiency = pairs[i + 1] });
        return cfg;
    }

    static void Report(string name, EfficiencyConfigData cfg, params double[] at)
    {
        FsaEfficiency curve = FsaEfficiency.FromConfig(cfg);
        var sb = new StringBuilder();
        sb.AppendFormat(Inv, "{0}: accepted={1}", name, curve != null);
        if (curve != null)
        {
            foreach (double e in at)
            {
                double eff, err;
                bool ok = curve.TryEval(e, out eff, out err);
                sb.AppendFormat(Inv, " | TryEval({0})={1} eff={2} Eval={3}", R(e), ok, R(eff), R(curve.Eval(e)));
            }
        }

        Console.WriteLine(sb.ToString());
    }

    static void Part138(string curves)
    {
        Console.WriteLine("=== AMBER138: повтор энергии ===");
        Console.WriteLine("exp(log(1000))={0} exp(log(100))={1}", R(Math.Exp(Math.Log(1000.0))), R(Math.Exp(Math.Log(100.0))));
        Report("dup1000", Config(1000, .1, 1000, .2), 1000);
        Report("dup100", Config(100, .1, 100, .2), 100);
        Report("dupLow1000+2000", Config(1000, .1, 1000, .2, 2000, .05), 1000, 1500);
        Report("dupMid", Config(500, .3, 1000, .1, 1000, .2, 2000, .05), 1000, 999.9999999, 1000.0000001, 1500);
        // соседние числа с плавающей точкой: энергии различны, а логарифмы могут совпасть
        double e1 = 1000.0, e2 = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1000.0) + 1);
        Console.WriteLine("nextafter: log(1000)==log(next)? {0}", Math.Log(e1) == Math.Log(e2));
        Report("nextafter1000", Config(e1, .1, e2, .2), e1, e2);
        Report("good2", Config(100, .1, 1000, .02), 100, 316.22776601683796, 1000);
        // Порядок равных энергий: какой из повторов остаётся (детерминизм).
        Report("dupOrderA", Config(100, .1, 100, .2, 1000, .02), 100, 300);
        Report("dupOrderB", Config(100, .2, 100, .1, 1000, .02), 100, 300);

        if (curves == null) return;
        int files = 0, groups = 0, withDup = 0;
        var sha = SHA256.Create();
        var all = new StringBuilder();
        foreach (string dir in curves.Split(';'))
        {
            if (!Directory.Exists(dir)) { Console.WriteLine("нет каталога " + dir); continue; }
            foreach (string path in Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var doc = new XmlDocument();
                try { doc.Load(path); } catch { continue; }
                XmlNodeList nodes = doc.GetElementsByTagName("ROIEfficiencyData");
                if (nodes.Count == 0) continue;
                files++;
                var byParent = new Dictionary<XmlNode, List<ROIEfficiencyData>>();
                var order = new List<XmlNode>();
                foreach (XmlNode node in nodes)
                {
                    XmlNode parent = node.ParentNode;
                    if (!byParent.ContainsKey(parent)) { byParent[parent] = new List<ROIEfficiencyData>(); order.Add(parent); }
                    double en = Parse(node["Energy"]), ef = Parse(node["Efficiency"]), er = Parse(node["ErrorPercent"]);
                    byParent[parent].Add(new ROIEfficiencyData { Energy = en, Efficiency = ef, ErrorPercent = double.IsNaN(er) ? 0 : er });
                }

                foreach (XmlNode parent in order)
                {
                    groups++;
                    List<ROIEfficiencyData> pts = byParent[parent];
                    var distinct = new HashSet<double>(pts.Select(p => p.Energy));
                    if (distinct.Count != pts.Count)
                    {
                        withDup++;
                        Console.WriteLine("DUP {0} ({1} точек, различных {2})", path, pts.Count, distinct.Count);
                    }

                    var cfg = new EfficiencyConfigData { Curve = pts };
                    FsaEfficiency curve = FsaEfficiency.FromConfig(cfg);
                    all.Append(path).Append('|').Append(curve != null).Append('|');
                    if (curve == null) { all.Append('\n'); continue; }
                    all.Append(R(curve.MinEnergy)).Append('|').Append(R(curve.MaxEnergy)).Append('|')
                       .Append(R(curve.FloorAtFraction(0.1))).Append('|');
                    for (double e = 5; e < 5000; e *= 1.013)
                    {
                        double eff, err;
                        bool ok = curve.TryEval(e, out eff, out err);
                        all.Append(R(curve.Eval(e))).Append(',').Append(ok).Append(',').Append(R(eff)).Append(',').Append(R(err)).Append(';');
                    }

                    all.Append('\n');
                }
            }
        }

        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString()));
        Console.WriteLine("CURVES files={0} curves={1} withDuplicates={2} sha256={3}", files, groups, withDup,
                          BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
    }

    static double Parse(XmlElement e)
    {
        double v;
        return e != null && double.TryParse(e.InnerText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
    }
}
