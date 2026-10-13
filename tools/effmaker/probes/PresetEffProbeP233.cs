using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PresetEffProbeP233
{
    /// <summary>
    /// Перепроверка и подбор П233 (`AMBER162`, постановка Amber 05.10.2026: «У Rc-103,
    /// Rc-101 отражатель TiO2, а не PTFE»): ε пика из шаблона «RadiaCode-101/103» или из
    /// сцены корпуса RC-103 с ПОДМЕНЁННЫМ передним стеком — вещество и толщина отражателя,
    /// толщина пластика торца, зазор при сохранённой глубине кристалла.
    ///
    /// Шаблон применяется так, как его применяет редактор (`GeometryEditorPanel.Blank`
    /// + `Preset.Apply`), сцена — `GeometryModel.Load`; счёт — `EfficiencyCalculation.Run`
    /// с умолчаниями приложения и его зерном; ε в энергии — читателем разбора
    /// (`FsaEfficiency.FromConfig` → `Eval`), то есть число, на которое делится площадь
    /// при расчёте активности. Родня `PresetEffProbeP223` (там — смена торца Al → PE).
    ///
    ///   PresetEffProbeP233.exe (--preset=RadiaCode-103 | --scene=&lt;.in&gt;) [--distance=0]
    ///       [--reflector=&lt;имя из библиотеки&gt;] [--refl=&lt;мм торец и бок&gt;]
    ///       [--refl-front=&lt;мм&gt;] [--refl-side=&lt;мм&gt;] [--pe=&lt;мм торца&gt;]
    ///       [--depth=&lt;мм&gt;] [--energies=22,32,...] [--n=200000] [--threads=4] [--out=&lt;csv&gt;]
    ///
    /// `--depth=` — глубина кристалла под наружной гранью (отражатель + зазор + торец);
    /// зазор выводится из неё ПОСЛЕ подмены отражателя и торца. Без ключа зазор остаётся
    /// тем, что поставил шаблон / сцена (маринелли RC-103 держит ноль — так и надо).
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: без ключей подмены проба обязана повторить
    /// `PresetEffProbeP223` / кривую сцены побитово (то же клеймо матрицы); с
    /// `--reflector=Polytetrafluoroethylene --refl=1` на шаблоне до правки — тоже.
    ///
    /// Коды возврата: 0 — посчитано; 1 — расчёт не пошёл; 2 — не запустилась.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string presetName = null, scenePath = null, outCsv = null, reflector = null;
            double distance = double.NaN, reflFront = double.NaN, reflSide = double.NaN;
            double pe = double.NaN, depth = double.NaN;
            var probe = new List<double> { 22.0, 32.0, 36.0, 55.0, 60.0, 63.0, 88.0, 202.0, 307.0, 662.0, 1461.0 };
            var options = new EfficiencyCalculationOptions { Threads = 4 };
            foreach (string arg in args)
            {
                if (arg.StartsWith("--preset=", StringComparison.Ordinal)) presetName = arg.Substring(9);
                else if (arg.StartsWith("--scene=", StringComparison.Ordinal)) scenePath = arg.Substring(8);
                else if (arg.StartsWith("--out=", StringComparison.Ordinal)) outCsv = arg.Substring(6);
                else if (arg.StartsWith("--reflector=", StringComparison.Ordinal)) reflector = arg.Substring(12);
                else if (arg.StartsWith("--distance=", StringComparison.Ordinal)) distance = D(arg.Substring(11));
                else if (arg.StartsWith("--refl=", StringComparison.Ordinal)) reflFront = reflSide = D(arg.Substring(7));
                else if (arg.StartsWith("--refl-front=", StringComparison.Ordinal)) reflFront = D(arg.Substring(13));
                else if (arg.StartsWith("--refl-side=", StringComparison.Ordinal)) reflSide = D(arg.Substring(12));
                else if (arg.StartsWith("--pe=", StringComparison.Ordinal)) pe = D(arg.Substring(5));
                else if (arg.StartsWith("--depth=", StringComparison.Ordinal)) depth = D(arg.Substring(8));
                else if (arg.StartsWith("--energies=", StringComparison.Ordinal))
                {
                    probe.Clear();
                    foreach (string s in arg.Substring(11).Split(',')) probe.Add(D(s));
                }
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
                Console.Error.WriteLine("PresetEffProbeP233.exe --preset=<имя> | --scene=<.in> [--distance=] [--reflector=] [--refl=] [--pe=] [--depth=] [--energies=] [--n=] [--threads=] [--out=]");
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
                g.PointDistance = 0.0;
                what = "шаблон «" + preset.Name + "»";
            }

            if (!double.IsNaN(distance)) g.PointDistance = distance;

            if (reflector != null)
            {
                GeometryMaterialLibrary.Entry e = GeometryMaterialLibrary.ByName(reflector);
                if (e == null)
                {
                    Console.Error.WriteLine("в библиотеке веществ нет «" + reflector + "»");
                    return 2;
                }

                g.Reflector = GeometryMaterialLibrary.Make(e, e.Density);
                what += " + отражатель «" + reflector + "»";
            }

            if (!double.IsNaN(reflFront)) g.FrontReflectorThickness = reflFront;
            if (!double.IsNaN(reflSide)) g.SideReflectorThickness = reflSide;
            if (!double.IsNaN(pe)) g.FrontCladdingThickness = pe;
            if (!double.IsNaN(depth))
            {
                double gap = depth - g.FrontReflectorThickness - g.FrontCladdingThickness;
                if (gap < 0.0)
                {
                    Console.Error.WriteLine("глубина " + depth.ToString("R", CultureInfo.InvariantCulture) + " мм меньше отражателя и торца");
                    return 2;
                }

                g.FrontGapThickness = gap;
            }

            Say("{0}", what);
            Say("отражатель: «{0}» ρ {1:R} г/см³, торец {2:R} мм, бок {3:R} мм",
                g.Reflector != null ? g.Reflector.Name : "?", g.Reflector != null ? g.Reflector.Density : 0.0,
                g.FrontReflectorThickness, g.SideReflectorThickness);
            Say("обкладка: «{0}» ρ {1:R} г/см³, торец {2:R} мм, бок {3:R} мм; зазор у торца {4:R} мм («{5}»)",
                g.Cladding != null ? g.Cladding.Name : "?", g.Cladding != null ? g.Cladding.Density : 0.0,
                g.FrontCladdingThickness, g.SideCladdingThickness, g.FrontGapThickness,
                g.Gap != null ? g.Gap.Name : "?");
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
            Say("ε пика (читатель разбора FsaEfficiency.Eval):");
            TextWriter csv = outCsv != null ? new StreamWriter(outCsv, false, new System.Text.UTF8Encoding(false)) : null;
            if (csv != null) csv.WriteLine("energy_kev,eff");
            foreach (double e in probe)
            {
                double v = eff.Eval(e);
                Say("  E {0,6:0.#} кэВ  ε {1:R}", e, v);
                if (csv != null) csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R}", e, v));
            }

            if (csv != null) csv.Dispose();
            return 0;
        }

        static double D(string s)
        {
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Say(string format, params object[] args)
        {
            Console.WriteLine(args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
