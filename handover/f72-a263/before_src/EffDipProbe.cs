// Просадка на кривой эффективности: где она и от чего (вопрос Amber 16.08.2026).
//
//     effdipprobe <конфиг-устройства.xml> [имя кривой] [--hist=N] [--fine]
//
// Берёт геометрию ИЗ КОНФИГУРАЦИИ УСТРОЙСТВА (та самая, что в окне) и печатает
// кривую по узлам с шагом к соседу — просадка обязана быть видна числом, а не
// на глаз по картинке. `--fine` считает вокруг подозрительного места частой
// логарифмической сеткой: одиночный провал на штатном узле и настоящий изгиб
// кривой различаются только этим.
//
// Конфигурация читается ТОЛЬКО НА ЧТЕНИЕ и не переписывается: дистрибутив
// Amber трогать нельзя.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

static class EffDipProbe
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread_SetInvariant();
        if (args.Length < 1)
        {
            Console.WriteLine("effdipprobe <конфиг-устройства.xml> [имя кривой] [--hist=N] [--fine]");
            return 2;
        }

        string path = args[0];
        string want = null;
        int histories = 200000;
        bool fine = false;
        double fineLo = 100.0, fineHi = 300.0;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--hist="))
            {
                histories = int.Parse(args[i].Substring(7), CultureInfo.InvariantCulture);
            }
            else if (args[i].StartsWith("--fine"))
            {
                fine = true;
                if (args[i].Length > 7)
                {
                    string[] parts = args[i].Substring(7).Split('-');
                    fineLo = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    fineHi = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }
            }
            else
            {
                want = args[i];
            }
        }

        DeviceConfigInfo device;
        using (FileStream fs = File.OpenRead(path))
        {
            device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
        }

        Console.WriteLine("прибор: {0}", device.Name);
        EfficiencyConfigData chosen = null;
        foreach (EfficiencyConfigData c in device.EfficiencyConfigs)
        {
            Console.WriteLine("  кривая «{0}»: геометрия {1}, точек {2}, клеймо {3}",
                              c.Name, c.HasGeometry ? "есть" : "НЕТ",
                              c.Curve == null ? 0 : c.Curve.Count,
                              string.IsNullOrEmpty(c.ComputeStamp) ? "(нет)" : c.ComputeStamp);
            if (c.HasGeometry && (want == null || c.Name == want) && chosen == null)
            {
                chosen = c;
            }
        }

        if (chosen == null)
        {
            Console.WriteLine("не нашёл кривой с геометрией" + (want == null ? "" : " по имени «" + want + "»"));
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("считаю «{0}»: {1}", chosen.Name, chosen.Geometry.Describe());
        foreach (string w in chosen.Geometry.Warnings)
        {
            Console.WriteLine("  ! {0}", w);
        }

        Print(chosen.Geometry, new EfficiencyCalculationOptions { Histories = histories }, "штатная сетка");

        if (fine)
        {
            Console.WriteLine();
            Print(chosen.Geometry, new EfficiencyCalculationOptions
            {
                Histories = histories,
                MinEnergyKev = fineLo,
                MaxEnergyKev = fineHi,
                GridMode = EfficiencyGridMode.Logarithmic,
                NodeCount = 21,
            }, string.Format(CultureInfo.InvariantCulture, "частая сетка {0}-{1} кэВ", fineLo, fineHi));
        }

        return 0;
    }

    static void Print(GeometryModel geometry, EfficiencyCalculationOptions options, string title)
    {
        Console.WriteLine("== {0}, историй {1} ==", title, options.Histories);
        EfficiencyFitResult result = EfficiencyCalculation.Run(geometry, options, null, null);
        if (result == null || result.Curve == null || result.Curve.Count == 0)
        {
            Console.WriteLine("  пусто: {0}", result == null ? "null" : result.Error);
            return;
        }

        Console.WriteLine("  {0,9} {1,12} {2,10}", "E, кэВ", "eff", "к соседу");
        double prev = 0.0;
        foreach (ROIEfficiencyData p in result.Curve)
        {
            string mark = "";
            if (prev > 0.0)
            {
                double ratio = p.Efficiency / prev;
                mark = ratio.ToString("F3", CultureInfo.InvariantCulture);
                if (ratio < 1.0)
                {
                    mark += "  <-- ВНИЗ";
                }
            }

            Console.WriteLine("  {0,9:F1} {1,12:E4} {2,10}", p.Energy, p.Efficiency, mark);
            prev = p.Efficiency;
        }
    }

    static void Thread_SetInvariant()
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
