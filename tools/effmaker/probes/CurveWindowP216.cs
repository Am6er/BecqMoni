using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// П216 (`S208`): разбор пика кривой «окном полной строки ±ПШПВ/2». Симулятор
// узла — тот же, что у построителя (`ResponseMatrixBuilder.MakeSimulator`
// отражением, физика склада), допуск — ПШПВ/2 геометрии, как у кривой.
// Оценки одного узла:
//   OLD — прежняя кривая: взвешенная ветвь допуском ПШПВ/2 + класс вне конуса
//         (рычаг `CurvePeakResolutionWindow` ВЫКЛ — положительный контроль);
//   NEW — кривая с окном: взвешенная тесным допуском + аналоговая полоса в
//         конусе + класс вне конуса; печатаются её слагаемые;
//   AN  — аналоговая ветвь в конусе ЦЕЛИКОМ (тесный + полоса) + класс вне
//         конуса: полный аналоговый перенос — арбитр;
//   B   — то, что пишет склад: Σ канала Peak + второй счёт (EPRS).
// Время OLD и NEW — секунды одного потока.
//
//   CurveWindowP216 --in=<сцена.in> --kev=32.19,45,59.54 [--n=400000] [--nomatrix]
public static class CurveWindowP216
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null; string kevs = "32.19,45,59.54,80,122"; int n = 400000;
        bool noMatrix = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=")) inPath = a.Substring(5);
            else if (a.StartsWith("--kev=")) kevs = a.Substring(6);
            else if (a.StartsWith("--n=")) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a == "--nomatrix") noMatrix = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (inPath == null) { Console.Error.WriteLine("нужен --in="); return 2; }
        GlobalConfigManager.GetInstance();
        GeometryModel g = GeometryModel.Load(inPath);
        var options = new ResponseMatrixOptions();
        MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
        Console.WriteLine("сцена {0}, историй {1}", System.IO.Path.GetFileNameWithoutExtension(inPath), n);
        Console.WriteLine("{0,8} {1,6} {2,11} {3,11} {4,11} {5,7} {6,7} {7,7} {8,7} {9,7} {10,7} {11,6} {12,6}",
            "кэВ", "ПШПВ/2", "OLD", "NEW", "AN", "NEW/OLD", "AN/NEW", "B/NEW", "B/OLD", "полоса%", "±пол%", "t_OLD", "t_NEW");
        foreach (string s in kevs.Split(','))
        {
            double kev = double.Parse(s, CultureInfo.InvariantCulture);
            double hw = g.PeakHalfWidthKev(kev);

            var simOld = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7, kev });
            simOld.Histories = n;
            simOld.PeakHalfWidthKev = hw;
            simOld.CurvePeakResolutionWindow = false;
            double eOld;
            var sw = Stopwatch.StartNew();
            double old = simOld.Efficiency(kev, out eOld);
            double tOld = sw.Elapsed.TotalSeconds;

            var simNew = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7, kev });
            simNew.Histories = n;
            simNew.PeakHalfWidthKev = hw;
            double eNew;
            sw.Restart();
            double fresh = simNew.Efficiency(kev, out eNew);
            double tNew = sw.Elapsed.TotalSeconds;
            double wTight = simNew.LastCurveWeightedTight;
            double band = simNew.LastCurveBand;
            double aTight = simNew.LastCurveAnalogTight;
            double outside = fresh - wTight - band;
            double analog = aTight + band + outside;

            double b = double.NaN;
            if (!noMatrix)
            {
                var simB = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7, kev });
                simB.Histories = n;
                simB.ResolutionPeakHalfWidthKev = hw;
                double err;
                double[][] rows = simB.ResponseByChannel(kev, options.BinKev, out err);
                double peak = 0.0;
                foreach (double v in rows[(int)EfficiencySimulator.ResponseChannel.Peak]) peak += v;
                b = peak + simB.LastResolutionPeakExtra;
            }

            Console.WriteLine("{0,8:F2} {1,6:F2} {2,11:E4} {3,11:E4} {4,11:E4} {5,7:F4} {6,7:F4} {7,7:F4} {8,7:F4} {9,7:F2} {10,7:F2} {11,6:F1} {12,6:F1}",
                kev, hw, old, fresh, analog, fresh / old, analog / fresh, b / fresh, b / old,
                100.0 * band / fresh, simNew.LastCurveBandErrorPercent, tOld, tNew);
            Console.WriteLine("         ± OLD {0:F2} %, NEW {1:F2} %; взвеш.тесный {2:E4}, аналог.тесный {3:E4} (аналог/взвеш {4:F4}), вне конуса {5:E4}",
                eOld, eNew, wTight, aTight, aTight / wTight, outside);
        }

        return 0;
    }
}
