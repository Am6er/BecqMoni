using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PresetEffProbeP223
{
    /// <summary>
    /// Приёмка П223 (`AMBER153`, решение Amber 02.10.2026 «Оба на пластик 1.5 мм
    /// (Рекомендую)»): кривая эффективности, которую получит человек, выбравший
    /// встроенный шаблон «RadiaCode-101»/«RadiaCode-103» и поставивший точечный
    /// источник, — ε пика на 22, 32, 60, 88 и 662 кэВ ДО и ПОСЛЕ смены торца
    /// (алюминий 1 мм → полиэтилен 1.5 мм, зазор 3.5 → 3.0 мм).
    ///
    /// Шаблон применяется так, как его применяет редактор (`GeometryEditorPanel.Blank`
    /// + `Preset.Apply`), счёт — `EfficiencyCalculation.Run` с умолчаниями приложения
    /// и его зерном; ε в энергии читается ТЕМ ЖЕ читателем, что у разбора
    /// (`FsaEfficiency.FromConfig` → `Eval`), то есть ровно то число, на которое
    /// делится площадь при расчёте активности.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — ключ `--legacy`: поверх шаблона ставится
    /// прежний торец (алюминий 2.7 г/см³ на торец и бок, торец 1.0 мм, зазор
    /// 3.5 мм). На сборке ДО правки он ничего не меняет (шаблон и есть такой),
    /// на сборке ПОСЛЕ — обязан вернуть прежние числа ПОБИТОВО (печать `R`).
    /// Без этого «до/после» могло бы мерить заодно шум или постороннюю правку.
    ///
    ///   PresetEffProbeP223.exe --preset=RadiaCode-103 [--distance=0] [--n=200000]
    ///                          [--threads=4] [--legacy] [--scene=&lt;.in&gt;] [--out=&lt;csv&gt;]
    ///
    /// `--scene=` — взять геометрию из файла `.in` вместо шаблона (сверка шаблона
    /// со сценой корпуса). Коды возврата: 0 — посчитано; 1 — расчёт не пошёл;
    /// 2 — не запустилась.
    /// </summary>
    static class Program
    {
        static readonly double[] Probe = { 22.0, 32.0, 60.0, 88.0, 662.0 };

        static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string presetName = null, scenePath = null, outCsv = null;
            double distance = 0.0;
            bool legacy = false;
            var options = new EfficiencyCalculationOptions { Threads = 4 };
            foreach (string arg in args)
            {
                if (arg.StartsWith("--preset=", StringComparison.Ordinal)) presetName = arg.Substring(9);
                else if (arg.StartsWith("--scene=", StringComparison.Ordinal)) scenePath = arg.Substring(8);
                else if (arg.StartsWith("--out=", StringComparison.Ordinal)) outCsv = arg.Substring(6);
                else if (arg == "--legacy") legacy = true;
                else if (arg.StartsWith("--distance=", StringComparison.Ordinal))
                    distance = double.Parse(arg.Substring(11), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--n=", StringComparison.Ordinal))
                    options.Histories = int.Parse(arg.Substring(4), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--threads=", StringComparison.Ordinal))
                    options.Threads = int.Parse(arg.Substring(10), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + arg);
                    return 2;
                }
            }

            if (presetName == null && scenePath == null)
            {
                Console.Error.WriteLine("PresetEffProbeP223.exe --preset=<имя> | --scene=<.in> [--distance=0] [--n=200000] [--threads=4] [--legacy] [--out=<csv>]");
                return 2;
            }

            GlobalConfigManager.GetInstance();

            GeometryModel g;
            string what;
            if (scenePath != null)
            {
                if (!File.Exists(scenePath))
                {
                    Console.Error.WriteLine("нет файла сцены " + scenePath);
                    return 2;
                }

                g = GeometryModel.Load(scenePath);
                what = "сцена " + Path.GetFileName(scenePath);
            }
            else
            {
                GeometryPresets.Preset preset = null;
                foreach (GeometryPresets.Preset p in GeometryPresets.Items)
                {
                    if (p.Name == presetName) preset = p;
                }

                if (preset == null)
                {
                    Console.Error.WriteLine("во встроенных пресетах нет «" + presetName + "»");
                    return 2;
                }

                g = GeometryEditorPanel.Blank();
                preset.Apply(g);
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = distance;
                what = "шаблон «" + preset.Name + "»";
                if (legacy)
                {
                    // Прежний торец шаблонов RadiaCode (до П223): алюминий на
                    // торец и бок (вещество обкладки в модели одно), торец 1.0 мм,
                    // зазор 3.5 мм. Числа — те, что стояли в GeometryPresets.cs
                    // на 4437b622; вещество — тем же путём, что берёт шаблон.
                    GeometryMaterialLibrary.Entry al = GeometryMaterialLibrary.ByName("Aluminum");
                    if (al == null)
                    {
                        Console.Error.WriteLine("в библиотеке веществ нет «Aluminum»");
                        return 2;
                    }

                    g.Cladding = GeometryMaterialLibrary.Make(al, al.Density);
                    g.FrontCladdingThickness = 1.0;
                    g.FrontGapThickness = 3.5;
                    what += " + прежний торец (--legacy: Al 1.0 мм, зазор 3.5 мм)";
                }
            }

            Say("{0}", what);
            Say("обкладка: «{0}» ρ {1:R} г/см³, торец {2:R} мм, бок {3:R} мм; зазор у торца {4:R} мм («{5}»); отражатель {6:R}/{7:R} мм",
                g.Cladding != null ? g.Cladding.Name : "?", g.Cladding != null ? g.Cladding.Density : 0.0,
                g.FrontCladdingThickness, g.SideCladdingThickness, g.FrontGapThickness,
                g.Gap != null ? g.Gap.Name : "?", g.FrontReflectorThickness, g.SideReflectorThickness);
            Say("глубина кристалла под наружной гранью: {0:R} мм", g.FrontReflectorThickness + g.FrontGapThickness + g.FrontCladdingThickness);
            Say("источник: {0}, точка на {1:R} мм; ПШПВ 662 {2:R} %", g.SourceType, g.PointDistance, g.FwhmAt662Percent);
            Say("историй на узел {0}, потоков {1}", options.Histories, options.EffectiveThreads);
            Say("клеймо матрицы: {0}", ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions()));

            EfficiencyFitResult r = EfficiencyCalculation.Run(g, options, null, null);
            if (!string.IsNullOrEmpty(r.Error))
            {
                Say("⛔ расчёт кривой не пошёл: {0}", r.Error);
                return 1;
            }

            Say("клеймо кривой : {0}", r.ComputeStamp);
            var config = new EfficiencyConfigData();
            config.Curve = new List<ROIEfficiencyData>(r.Curve);
            config.ComputeStamp = r.ComputeStamp;
            string refusal;
            FsaEfficiency eff = FsaEfficiency.FromConfig(config, out refusal);
            if (eff == null)
            {
                Say("⛔ кривая не читается разбором: {0}", refusal ?? "?");
                return 1;
            }

            Say("");
            Say("узлы кривой ({0}):", r.Curve.Count);
            foreach (ROIEfficiencyData node in r.Curve)
            {
                if (node.Energy <= 700.0)
                {
                    Say("  {0,8:0.###} {1:R}", node.Energy, node.Efficiency);
                }
            }

            Say("");
            Say("ε пика (читатель разбора FsaEfficiency.Eval):");
            TextWriter csv = outCsv != null ? new StreamWriter(outCsv, false, new System.Text.UTF8Encoding(false)) : null;
            if (csv != null) csv.WriteLine("energy_kev,eff");
            foreach (double e in Probe)
            {
                double v = eff.Eval(e);
                Say("  E {0,5:0} кэВ  ε {1:R}", e, v);
                if (csv != null) csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R}", e, v));
            }

            if (csv != null) csv.Dispose();
            return 0;
        }

        static void Say(string format, params object[] args)
        {
            Console.WriteLine(args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
