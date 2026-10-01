using System;
using System.Collections.Generic;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;

// П193 (`AMBER146`, 01.10.2026): приёмка угловой корреляции каскада у нуклидов без
// рентгена и β⁺ в поставке (Sc-46, Na-24, Mn-56) и у изомерных родителей (Ag-110m).
// Коэффициенты A22/A44 берутся ТЕМ ЖЕ путём, что у счёта (`FsaCascadeSummer.PairCoefficients`
// = `CoefficientsOf`), и сверяются с табличными 0.1020/0.0091 каскада 4⁺(E2)2⁺(E2)0⁺;
// Co-60 и Cs-134 — без изменений против базы (0.1005/0.0094 и 0.1020/0.0091).
// Положительный контроль: старый вход `CascadeAtomicData.Of` у 46SC по-прежнему null
// (договор не менялся), прямой `AngularCorrelation.ForPair` по ключу изомера — изотропно
// (именно этот путь был дефектом), а пара разных ветвей/несмежных переходов изотропна.
//   AngPairP193.exe        — код 0: всё сошлось; 1 — расхождение (напечатано поимённо).
public static class AngPairP193
{
    static int failures;

    static void Expect(string what, double a22, double a44, double wantA22, double wantA44, double tol)
    {
        bool ok = Math.Abs(a22 - wantA22) <= tol && Math.Abs(a44 - wantA44) <= tol;
        Console.WriteLine("  {0,-42} A22 {1,8:F4} A44 {2,8:F4}   ждём {3:F4}/{4:F4}  {5}",
                          what, a22, a44, wantA22, wantA44, ok ? "СОШЛОСЬ" : "РАСХОЖДЕНИЕ");
        if (!ok)
        {
            failures++;
        }
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();

        Console.WriteLine("== путь счёта (FsaCascadeSummer.PairCoefficients):");
        var cases = new[]
        {
            new object[] { "Sc-46", 889.28, 1120.55, 0.1020, 0.0091 },
            new object[] { "Na-24", 1368.63, 2754.01, 0.1020, 0.0091 },
            new object[] { "Ag-110m", 884.68, 657.76, 0.1020, 0.0091 },
            new object[] { "Co-60", 1173.23, 1332.49, 0.1005, 0.0094 },
            new object[] { "Cs-134", 604.72, 795.86, 0.1020, 0.0091 },
        };
        foreach (object[] c in cases)
        {
            AngularCorrelation.Coefficients w = FsaCascadeSummer.PairCoefficients((string)c[0], (double)c[1], (double)c[2]);
            Expect(string.Format(CultureInfo.InvariantCulture, "{0} {1} + {2}", c[0], c[1], c[2]),
                   w.A22, w.A44, (double)c[3], (double)c[4], 0.0006);
        }

        // Печать без ожидания — для журнала.
        foreach (object[] c in new[]
                 {
                     new object[] { "Mn-56", 846.77, 1810.73 },
                     new object[] { "Mn-56", 846.77, 2113.09 },
                     new object[] { "Ag-110m", 1384.29, 884.68 },
                     new object[] { "Ag-110m", 937.49, 884.68 },
                     new object[] { "Bi-207", 569.70, 1063.66 },
                     new object[] { "Ba-137m", 661.66, 32.0 },
                 })
        {
            AngularCorrelation.Coefficients w = FsaCascadeSummer.PairCoefficients((string)c[0], (double)c[1], (double)c[2]);
            Console.WriteLine("  {0,-42} A22 {1,8:F4} A44 {2,8:F4}{3}",
                              string.Format(CultureInfo.InvariantCulture, "{0} {1} + {2}", c[0], c[1], c[2]),
                              w.A22, w.A44, w.IsIsotropic ? "  (изотропно)" : "");
        }

        Console.WriteLine("== положительный контроль:");
        CascadeAtomicData old = CascadeAtomicData.Of("46SC");
        Console.WriteLine("  CascadeAtomicData.Of(46SC) = {0}  {1}", old == null ? "null" : "НЕ null",
                          old == null ? "СОШЛОСЬ (договор Of прежний)" : "РАСХОЖДЕНИЕ");
        if (old != null)
        {
            failures++;
        }

        CascadeAtomicData nuclear = CascadeAtomicData.Nuclear("46SC");
        Console.WriteLine("  CascadeAtomicData.Nuclear(46SC): {0}", nuclear == null ? "null — РАСХОЖДЕНИЕ"
                          : "ветвей " + nuclear.Branches.Count + ", строк γ " + nuclear.GammaIntensity.Count);
        if (nuclear == null)
        {
            failures++;
        }

        AngularCorrelation.Coefficients direct = AngularCorrelation.ForPair(FsaCascadeSummer.ParentKey("Ag-110m"), 884.68, 657.76);
        Console.WriteLine("  AngularCorrelation.ForPair({0}) — прямой путь без схем изомера: {1}",
                          FsaCascadeSummer.ParentKey("Ag-110m"), direct.IsIsotropic ? "изотропно, СОШЛОСЬ (дефектный путь виден)" : "НЕ изотропно — РАСХОЖДЕНИЕ");
        if (!direct.IsIsotropic)
        {
            failures++;
        }

        AngularCorrelation.Coefficients apart = FsaCascadeSummer.PairCoefficients("Sc-46", 889.28, 1173.23);
        Console.WriteLine("  Sc-46 889.28 + 1173.23 (чужая линия): {0}", apart.IsIsotropic ? "изотропно, СОШЛОСЬ" : "НЕ изотропно — РАСХОЖДЕНИЕ");
        if (!apart.IsIsotropic)
        {
            failures++;
        }

        Console.WriteLine("== доли пар Ag-110m (S176 по схеме Cd-110; до правки 0.957 на уровень):");
        List<double[]> table = FsaCascadeSummer.PairTable("Ag-110m");
        table.Sort((a, b) => b[2].CompareTo(a[2]));
        int shown = 0;
        foreach (double[] p in table)
        {
            if (shown++ >= 12)
            {
                break;
            }

            Console.WriteLine("  {0,9:F2} + {1,9:F2}  P = {2:F5}", p[0], p[1], p[2]);
        }

        Console.WriteLine(failures == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + failures.ToString(CultureInfo.InvariantCulture));
        return failures == 0 ? 0 : 1;
    }
}
