using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;

// П199 (`AMBER145`): кривая эффективности пути `EfficiencyCalculation.Run`
// (заново, на геометрии сцены `.in`) против двух пиковых эффективностей
// матрицы: Σ канала `Peak` (то, что сегодня берёт суммирователь —
// `FsaCascadeSummer.PeakEfficiency`) и пиковой по разрешению формата 11
// (`ResponseMatrix.ResolutionPeakEfficiency`, блок EPRS). Приёмка строки:
// кривая / ε_p по разрешению в ±1.5 % на 32…122 кэВ.
//
//   CurveVsMatrixP199 --in=<сцена.in> --rmx=<матрица.rmx> [--kev=32.19,45,59.54,80,122,200,356,661.66,1332.5] [--hist=400000]
public static class CurveVsMatrixP199
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null, rmxPath = null;
        string kevs = "32.19,45,59.54,80,122,200,356,661.66,1332.5";
        int hist = 400000;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=")) inPath = a.Substring(5);
            else if (a.StartsWith("--rmx=")) rmxPath = a.Substring(6);
            else if (a.StartsWith("--kev=")) kevs = a.Substring(6);
            else if (a.StartsWith("--hist=")) hist = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (inPath == null || rmxPath == null) { Console.Error.WriteLine("нужны --in= и --rmx="); return 2; }
        GlobalConfigManager.GetInstance();
        GeometryModel geometry = GeometryModel.Load(inPath);
        MatrixRefusal refusal; int format;
        ResponseMatrix matrix = ResponseMatrix.Load(rmxPath, out refusal, out format);
        if (matrix == null) { Console.Error.WriteLine("матрица не читается: " + refusal + " формат " + format); return 1; }
        FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, EfficiencySimulator.ScintillatorNameOf(geometry));
        Console.WriteLine("сцена {0}; матрица формат {1}, узлов {2}, годна для сцены: {3}; ε_p по разрешению: {4}",
                          Path.GetFileNameWithoutExtension(inPath), format, matrix.Energies.Length, matrix.IsValidFor(geometry),
                          matrix.PeakEfficiencyResolution != null ? (matrix.PeakResolutionFromGeometry ? "ПШПВ/2 геометрии" : "Σ Peak (нет разрешения)") : "НЕТ");
        Console.WriteLine("{0,9} {1,7} {2,13} {3,13} {4,13} {5,10} {6,10}", "кэВ", "ПШПВ/2", "кривая заново", "Σ канала Peak", "ε_p разр.", "кр/Peak", "кр/разр");
        int bad = 0;
        foreach (string s in kevs.Split(','))
        {
            double e = double.Parse(s, CultureInfo.InvariantCulture);
            var opt = new EfficiencyCalculationOptions { Histories = hist, MinEnergyKev = e, MaxEnergyKev = e + 1.0, GridMode = EfficiencyGridMode.Logarithmic, NodeCount = 2 };
            var lines = new List<string>();
            EfficiencyFitResult fresh = EfficiencyCalculation.Run(geometry, opt, lines.Add, () => false);
            double fv = double.NaN;
            if (fresh != null && fresh.Curve != null)
            {
                double best = double.MaxValue;
                foreach (ROIEfficiencyData p in fresh.Curve)
                {
                    if (Math.Abs(p.Energy - e) < best) { best = Math.Abs(p.Energy - e); fv = p.Efficiency; }
                }
            }
            else
            {
                foreach (string l in lines) Console.WriteLine("      " + l);
            }

            double peak = summer != null ? summer.PeakEfficiency(e) : double.NaN;
            double res = matrix.ResolutionPeakEfficiency(e);
            double r = fv / res;
            bool inBand = e >= 32.0 && e <= 122.5;
            string mark = inBand ? (Math.Abs(r - 1.0) <= 0.015 ? "  в ±1.5 %" : "  ВНЕ ±1.5 %") : "";
            if (inBand && !(Math.Abs(r - 1.0) <= 0.015)) bad++;
            Console.WriteLine("{0,9:F2} {1,7:F2} {2,13:E4} {3,13:E4} {4,13:E4} {5,10:F4} {6,10:F4}{7}",
                              e, geometry.PeakHalfWidthKev(e), fv, peak, res, fv / peak, r, mark);
        }

        Console.WriteLine(bad == 0 ? "ПРИНЯТО: кривая / ε_p по разрешению в ±1.5 % на 32…122 кэВ" : "НЕ ПРИНЯТО: вне ±1.5 % — " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }
}
