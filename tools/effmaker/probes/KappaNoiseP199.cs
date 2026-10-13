using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// П199 (`AMBER147`): замер таблицы κ сцены БЕЗ построения строк матрицы —
// шум ячеек и цена при разном числе точек и историй на точку, и итог
// полного правила (добор до цели, подстановка, зажим).
//
//   KappaNoiseP199 --in=<сцена.in> [--jn=200000] [--per=4] [--target=5]
//                  [--threads=10] [--pilot-only] [--dump]
//
// `--pilot-only` — только проба (цель ставится недостижимо мягкой, ячейки не
// подставляются): мерка шума и цены одного прохода.
public static class KappaNoiseP199
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null;
        var options = new ResponseMatrixOptions();
        int threads = 10;
        bool pilotOnly = false, dump = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=")) inPath = a.Substring(5);
            else if (a.StartsWith("--jn=")) options.JointHistories = int.Parse(a.Substring(5), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--per=")) options.JointHistoriesPerPoint = int.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--target=")) options.JointNoiseTarget = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--threads=")) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a == "--pilot-only") pilotOnly = true;
            else if (a == "--dump") dump = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (inPath == null || !File.Exists(inPath)) { Console.Error.WriteLine("нужен --in=<сцена.in>"); return 2; }
        GlobalConfigManager.GetInstance();
        GeometryModel geometry = GeometryModel.Load(inPath);
        // как CorpusMatrixProbe по умолчанию: конус по свойству сцены не включается (`--cone` не задан)
        if (pilotOnly) options.JointNoiseTarget = 1.0e6;
        double[] grid = options.BuildGrid(geometry);
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = CancellationToken.None };
        TimeSpan cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        ResponseMatrixBuilder.JointTable t = ResponseMatrixBuilder.MeasureJointTable(geometry, options, grid, parallel);
        watch.Stop();
        double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalSeconds;
        if (t == null) { Console.WriteLine("таблицы нет"); return 1; }
        var m = new ResponseMatrix { JointEnergies = t.Energies, JointKappa = t.Kappa, JointKappaError = t.Error };
        Console.WriteLine("{0}: точечная={1}, режим {2}, точек проба/итог {3}/{4} по {5} ист.; шум ≥100 кэВ проба мед. {6:F2} %, итог мед. {7:F2} %, max {8:F1} %; ячеек {9}, подставлено {10}, зажато {11}; {12:F1} с часов, {13:F1} с ЦП",
            Path.GetFileNameWithoutExtension(inPath), ResponseMatrixBuilder.IsPointScene(geometry), t.Mode, t.PilotPoints, t.Points,
            options.JointHistoriesPerPoint, t.PilotMedianNoise, t.RawMedianNoise, t.RawMaxNoise, t.CellsCounted, t.Substituted, t.Clamped,
            watch.Elapsed.TotalSeconds, cpu);
        double[][] pairs = { new[] { 1173.2, 1332.5 }, new[] { 81.0, 356.0 }, new[] { 122.0, 1408.0 }, new[] { 32.0, 662.0 }, new[] { 898.0, 1836.1 }, new[] { 569.7, 1063.7 }, new[] { 88.34, 201.83 }, new[] { 201.83, 306.78 }, new[] { 32.0, 81.0 } };
        foreach (double[] p in pairs)
            Console.Write("  {0:F0}+{1:F0}={2:F3}", p[0], p[1], m.JointFactor(p[0], p[1]));
        Console.WriteLine();
        if (dump)
        {
            for (int i = 0; i < t.Energies.Length; i++)
            {
                Console.Write("  {0,7:F1} |", t.Energies[i]);
                for (int j = 0; j < t.Energies.Length; j++) Console.Write(" {0,5:F2}/{1,3:F0}", t.Kappa[i][j], Math.Min(999.0, t.Error[i][j]));
                Console.WriteLine();
            }
        }

        return 0;
    }
}
