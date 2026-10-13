using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// ЦЕНА КРИВОЙ «ИЗ ГЕОМЕТРИИ» (полоса П147, 24.09.2026, физика 24): штатная
/// кривая `EfficiencyCalculation.Run` по сцене — узлов, секунд на часах, и
/// сами узлы в CSV. Нужна, чтобы назвать, во что человеку обходится правка
/// физики кривой: сгущение сетки ниже 40 кэВ (`AMBER95`) добавляет узлы, класс
/// вне конуса (`AMBER79`) — аналоговый прогон в каждом узле.
///
///     curvecostprobep147 --in=&lt;.in&gt;[,…] [--n=200000] [--threads=N] [--csv=&lt;префикс&gt;]
///
/// Мерить на СВОБОДНОЙ машине: чужой счёт делает число временем очереди.
/// </summary>
static class CurveCostProbeP147
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string[] inputs = null;
        string csv = null;
        int n = 200000, threads = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inputs = a.Substring(5).Split(',');
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
            else
            {
                Console.Error.WriteLine("неизвестный ключ " + a);
                return 2;
            }
        }

        if (inputs == null)
        {
            Console.Error.WriteLine("нужен --in=");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        foreach (string inPath in inputs)
        {
            GeometryModel g = GeometryModel.Load(inPath);
            var options = new EfficiencyCalculationOptions { Histories = n, Threads = threads };
            Stopwatch clock = Stopwatch.StartNew();
            EfficiencyFitResult r = EfficiencyCalculation.Run(g, options, null, null);
            clock.Stop();
            string name = Path.GetFileNameWithoutExtension(inPath);
            Console.WriteLine("{0}: узлов {1}, {2} с на часах, штамп {3}{4}", name, r.Curve.Count,
                              clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
                              r.ComputeStamp, r.Ok ? "" : ", ОТКАЗ: " + r.Error);
            if (csv != null)
            {
                var sb = new StringBuilder("energy_kev;efficiency;error_pct\n");
                foreach (ROIEfficiencyData p in r.Curve)
                {
                    sb.Append(p.Energy.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                      .Append(p.Efficiency.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                      .Append(p.ErrorPercent.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                }

                File.WriteAllText(csv + "_" + name + ".csv", sb.ToString(), new UTF8Encoding(false));
            }
        }

        return 0;
    }
}
