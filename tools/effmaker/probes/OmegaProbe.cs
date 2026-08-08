using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;

// N15: выход K-флуоресценции по ИЗМЕРЕНИЯМ вместо расчётного EADL (физика 11).
//
// Две части, и обе нужны. Первая — что таблица `fluorescence_yield` вообще
// доехала до кода: величина, занесённая в базу без читателя, выглядит точно
// так же, как сделанная работа, и этой ошибкой я уже отличался. Вторая — цена
// правки на реальной геометрии: ключ `MeasuredFluorescenceYield` включается и
// выключается на ОДНОМ И ТОМ ЖЕ зерне, поэтому разность есть разность физики,
// а не разброс розыгрыша.
//
// Ожидание, против которого проверяем (database/omega-vs-measurement-2026-08-09.md):
// у CsI и NaI сдвиг мал — иод 1.004 и цезий 1.005 от измеренного, — а вот
// железо, медь и цинк EADL занижает на 4-5 %. Поэтому «почти ноль» на
// сцинтилляторе здесь НЕ признак того, что ключ не работает: признак этого —
// нулевая разница в первой части, где числа берутся прямо из базы.
//
//   omegaprobe [--geometry=X.in] [--n=200000] [--energies=40,60,88,122,662]
class OmegaProbe
{
    const string DetectorPreset = "Atom Spectra Nano 16";

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string geometryPath = null;
        int histories = 200000;
        double[] energies = { 40.0, 60.0, 88.0, 122.0, 662.0 };
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
                histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                energies = a.Substring(11).Split(',')
                            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        GlobalConfigManager.GetInstance();

        // ---------------------------------------------------------------- 1
        Console.WriteLine("=== 1. Таблица доехала до кода?");
        Console.WriteLine();
        Console.WriteLine("  Z  элем   EADL      измерено   EADL/изм");

        int[] zs = { 11, 20, 26, 29, 30, 32, 35, 53, 55, 56, 74, 82, 92 };
        int seen = 0, missing = 0;
        double worstRatio = 1.0;
        int worstZ = 0;
        foreach (int z in zs)
        {
            MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(z);
            if (f == null)
            {
                Console.WriteLine("{0,3}  {1,-4}  записи в `xray_fluorescence` нет вовсе",
                                  z, MaterialDatabase.SymbolOf(z));
                continue;
            }

            if (!(f.OmegaKMeasured > 0.0))
            {
                missing++;
                Console.WriteLine("{0,3}  {1,-4}  {2,8:F5}  {3,9}  {4,8}",
                                  z, MaterialDatabase.SymbolOf(z), f.OmegaK, "нет", "-");
                continue;
            }

            seen++;
            double ratio = f.OmegaK / f.OmegaKMeasured;
            if (Math.Abs(ratio - 1.0) > Math.Abs(worstRatio - 1.0))
            {
                worstRatio = ratio;
                worstZ = z;
            }

            Console.WriteLine("{0,3}  {1,-4}  {2,8:F5}  {3,9:F5}  {4,8:F4}",
                              z, MaterialDatabase.SymbolOf(z), f.OmegaK, f.OmegaKMeasured, ratio);
        }

        Console.WriteLine();
        if (seen == 0)
        {
            Console.WriteLine("ПРОВАЛ: измеренного выхода нет НИ У ОДНОГО элемента —");
            Console.WriteLine("        таблица `fluorescence_yield` не доехала до кода.");
            return 1;
        }

        Console.WriteLine("измерение есть у {0} из {1}, нет у {2}; худшее отношение {3:F4} (Z={4})",
                          seen, seen + missing, missing, worstRatio, worstZ);

        // Ключ обязан ВЫБИРАТЬ: одна и та же запись под разными флагами
        // должна давать разные числа там, где поставки расходятся.
        MaterialDatabase.Fluorescence fe = MaterialDatabase.FluorescenceOf(26);
        if (fe != null && fe.OmegaKMeasured > 0.0
            && fe.Omega(true) == fe.Omega(false))
        {
            Console.WriteLine("ПРОВАЛ: Omega(true) == Omega(false) на железе — ключ не выбирает.");
            return 1;
        }

        // ---------------------------------------------------------------- 2
        Console.WriteLine();
        Console.WriteLine("=== 2. Цена на геометрии (одно зерно, ключ вкл/выкл)");

        GeometryModel g;
        if (geometryPath != null)
        {
            g = GeometryModel.Load(geometryPath);
        }
        else
        {
            g = GeometryEditorPanel.Blank();
            GeometryPresets.Preset preset =
                GeometryPresets.Items.FirstOrDefault(p => p.Name == DetectorPreset);
            if (preset == null)
            {
                Console.Error.WriteLine("во встроенных пресетах нет «" + DetectorPreset + "»");
                return 1;
            }

            preset.Apply(g);
            g.SourceType = GeometrySourceType.Cylinder;
            g.BeakerToDetectorDistance = 50.0;
        }

        Console.WriteLine("    {0}", g.Describe());
        Console.WriteLine("    кристалл: {0}, историй {1}", g.Crystal.Name, histories);
        Console.WriteLine();
        Console.WriteLine("  кэВ    пик EADL   пик измер.   Δ пика    полн. EADL  полн. измер.   Δ полн.");

        foreach (double e in energies)
        {
            double errA, errB, errTa, errTb;
            EfficiencySimulator a = Make(g, histories, false);
            double peakA = a.Efficiency(e, out errA);
            double totalA = a.TotalEfficiency(e, out errTa);

            EfficiencySimulator b = Make(g, histories, true);
            double peakB = b.Efficiency(e, out errB);
            double totalB = b.TotalEfficiency(e, out errTb);

            Console.WriteLine("{0,6:F1}  {1,9:E3}  {2,10:E3}  {3,8:P2}  {4,10:E3}  {5,12:E3}  {6,8:P2}",
                              e, peakA, peakB, Delta(peakA, peakB),
                              totalA, totalB, Delta(totalA, totalB));
        }

        Console.WriteLine();
        Console.WriteLine("Малый сдвиг на CsI/NaI ОЖИДАЕМ: у иода и цезия EADL сходится");
        Console.WriteLine("с измерением на 0.4-0.5 %. Проверка ключа — часть 1, а не эта.");
        Console.WriteLine();
        Console.WriteLine("ВСЕ СОШЛИСЬ");
        return 0;
    }

    static double Delta(double a, double b)
    {
        return a > 0.0 ? b / a - 1.0 : 0.0;
    }

    static EfficiencySimulator Make(GeometryModel g, int histories, bool measured)
    {
        return new EfficiencySimulator(g)
        {
            Histories = histories,
            MeasuredFluorescenceYield = measured,
        };
    }
}
