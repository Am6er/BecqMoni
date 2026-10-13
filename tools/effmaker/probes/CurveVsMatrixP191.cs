using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
// П191: кривая эффективности пути `EfficiencyCalculation.Run` против пиковой строки матрицы склада на тех же узлах.
//   CurveVsMatrixP191 --spectrum=<файл.xml> --kev=32.19,59.54,661.66 [--hist=400000]
public static class CurveVsMatrixP191
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string spectrumPath = null; string kevs = "32.19,59.54,661.66"; int hist = 400000;
        foreach (string a in args)
        {
            if (a.StartsWith("--spectrum=")) spectrumPath = a.Substring(11);
            else if (a.StartsWith("--kev=")) kevs = a.Substring(6);
            else if (a.StartsWith("--hist=")) hist = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
        }

        if (spectrumPath == null) { Console.Error.WriteLine("нужен --spectrum="); return 2; }
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        ResultData rd;
        using (FileStream fs = File.OpenRead(spectrumPath))
        {
            ResultDataFile file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
            rd = file.ResultDataList[0];
        }

        Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));
        if (rd.Efficiency == null || !rd.Efficiency.HasGeometry) { Console.Error.WriteLine("у спектра нет геометрии"); return 1; }
        GeometryModel geometry = rd.Efficiency.Geometry;
        MatrixRefusal refusal; int fileFormat;
        ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency.Guid, out refusal, out fileFormat);
        FsaCascadeSummer summer = matrix != null ? FsaCascadeSummer.Create(matrix, EfficiencySimulator.ScintillatorNameOf(geometry)) : null;
        FsaEfficiency stored = FsaEfficiency.FromConfig(rd.Efficiency);
        Console.WriteLine("сцена  : {0}; матрица {1}", rd.Efficiency.Name, matrix != null ? "есть, узлов " + matrix.Energies.Length : "НЕТ (" + refusal + ")");
        Console.WriteLine("{0,10} {1,14} {2,14} {3,14} {4,10} {5,10}", "кэВ", "кривая файла", "кривая заново", "матрица ε_p", "заново/матр", "файл/матр");
        foreach (string s in kevs.Split(','))
        {
            double e = double.Parse(s, CultureInfo.InvariantCulture);
            var opt = new EfficiencyCalculationOptions { Histories = hist, MinEnergyKev = e, MaxEnergyKev = e + 1.0, GridMode = EfficiencyGridMode.Logarithmic, NodeCount = 2 };
            var lines = new List<string>();
            EfficiencyFitResult fresh = EfficiencyCalculation.Run(geometry, opt, lines.Add, () => false);
            double fv = double.NaN;
            if (fresh != null && fresh.Curve != null)
            {
                // ближайший узел к e
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

            double sv = stored != null ? stored.Eval(e) : double.NaN;
            double mv = summer != null ? summer.PeakEfficiency(e) : double.NaN;
            Console.WriteLine("{0,10:F2} {1,14:E4} {2,14:E4} {3,14:E4} {4,10:F3} {5,10:F3}", e, sv, fv, mv, fv / mv, sv / mv);
        }

        return 0;
    }
}
