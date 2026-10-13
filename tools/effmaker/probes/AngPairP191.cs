using System;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
// П191: коэффициенты корреляции пары по ключу нуклида и Q_k матрицы сцены — доезжает ли корреляция до сумм-пика.
public static class AngPairP191
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();
        string matrixPath = args.Length > 0 ? args[0] : null;
        ResponseMatrix matrix = matrixPath != null ? ResponseMatrix.Load(matrixPath) : null;
        if (matrix != null)
        {
            Console.WriteLine("матрица {0}: узлов {1}, каналов {2}, угловая таблица {3}", System.IO.Path.GetFileName(matrixPath), matrix.Energies.Length,
                              matrix.HasChannels ? matrix.ChannelRows.Length : 0, matrix.AngularQk != null ? "есть" : "НЕТ");
            foreach (double e in new[] { 122.0, 344.0, 511.0, 898.0, 1173.2, 1274.5, 1332.5, 1408.0, 1836.1 })
            {
                if (matrix.AngularQk != null)
                    Console.WriteLine("   Q2({0}) = {1:F4}  Q4 = {2:F4}  Q2T = {3:F4}", e, matrix.AngularQk.Q(2, e), matrix.AngularQk.Q(4, e), matrix.AngularQk.QT(2, e));
            }
        }

        foreach (string[] pair in new[] { new[] { "207BI", "569.70", "1063.66" }, new[] { "207BI", "569.70", "1770.23" }, new[] { "88Y", "898.04", "1836.06" }, new[] { "60CO", "1173.23", "1332.49" }, new[] { "152EU", "121.78", "1408.01" },
                                          new[] { "152EU", "344.28", "778.90" }, new[] { "133BA", "81.0", "302.85" }, new[] { "133BA", "81.0", "356.02" },
                                          new[] { "22NA", "511.0", "1274.54" }, new[] { "134CS", "604.72", "795.86" }, new[] { "46SC", "889.28", "1120.55" }, new[] { "154EU", "123.07", "1274.43" }, new[] { "192IR", "316.51", "468.07" }, new[] { "140LA", "487.02", "1596.21" }, new[] { "176LU", "201.83", "306.78" }, new[] { "176LU", "88.34", "201.83" }, new[] { "208TL", "583.19", "2614.51" }, new[] { "208TL", "860.56", "2614.51" }, new[] { "208TL", "277.37", "583.19" }, new[] { "208TL", "510.77", "583.19" }, new[] { "208TL", "510.77", "2614.51" }, new[] { "208TL", "763.13", "2614.51" }, new[] { "214BI", "609.32", "1120.29" }, new[] { "228AC", "911.20", "968.97" }, new[] { "75SE", "136.00", "264.66" }, new[] { "57CO", "14.41", "122.06" }, new[] { "110AG", "884.68", "657.76" }, new[] { "134CS", "569.33", "604.72" }, new[] { "59FE", "1099.25", "192.34" } })
        {
            AngularCorrelation.Coefficients c = AngularCorrelation.ForPair(pair[0], double.Parse(pair[1], CultureInfo.InvariantCulture), double.Parse(pair[2], CultureInfo.InvariantCulture));
            Console.WriteLine("{0,-6} {1} + {2}: A22 {3:F4} A44 {4:F4}{5}", pair[0], pair[1], pair[2], c.A22, c.A44, c.IsIsotropic ? "  (изотропно)" : "");
        }

        foreach (string name in new[] { "Ag-110m", "Ba-137m", "Ag110m1" })
        {
            string key = FsaCascadeSummer.ParentKey(name);
            AngularCorrelation.Coefficients c = AngularCorrelation.ForPair(key, 884.68, 657.76);
            Console.WriteLine("{0,-8} ключ {1,-16} 884.68 + 657.76: A22 {2:F4} A44 {3:F4}{4}  (пар в поставке {5})", name, key ?? "null", c.A22, c.A44, c.IsIsotropic ? "  (изотропно)" : "",
                              FsaCascadeSummer.PairTable(name) == null ? -1 : FsaCascadeSummer.PairTable(name).Count);
        }

        return 0;
    }
}
