using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GapTemplateProbeE43
{
    /// <summary>
    /// Приёмка `E43` (приказ Amber 14.09.2026: зазор 3.5 мм у торца RC103 — в
    /// шаблон приложения «RadiaCode-103» и в контактные сцены корпуса):
    /// кривая, которую получит человек, выбравший в редакторе геометрии
    /// встроенный шаблон и поставивший точку ВПРИТЫК, обязана совпасть с
    /// кривой корпусной сцены `RC103_point0.in`, построенной генератором из
    /// того же шаблона. Одна модель — из кода (`GeometryPresets`), другая — из
    /// файла (`GeometryModel.Load`); счёт у обеих — `EfficiencyCalculation.Run`
    /// теми же умолчаниями и тем же зерном, что и в приложении и в
    /// `CorpusEffProbe`, поэтому расхождение узлов — это расхождение МОДЕЛЕЙ,
    /// а не шум (порог 0.1 %: файл несёт доли состава шестью знаками).
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ обязателен: тот же шаблон с зазором, сброшенным
    /// в ноль, против той же сцены обязан РАЗОЙТИСЬ — на 662 кэВ впритык
    /// примерно в 2.3 раза (П73: ε 1.2597e-2 при 0 мм, ~5.4e-3 при 3.5 мм).
    /// Без него «совпало» не отличимо от «сравнивал одно с одним».
    ///
    ///   GapTemplateProbeE43.exe --preset=RadiaCode-103 --scene=&lt;.in&gt;
    ///                           [--distance=0] [--n=200000] [--out=&lt;csv&gt;] [--control=0.3:0.6]
    ///
    /// `--control=lo:hi` — окно положительного контроля (сцена / шаблон-без-зазора
    /// на 662 кэВ). Умолчание 0.3…0.6 — постановка ВПРИТЫК (П73: 0.484 при 3 мм,
    /// 0.381 при 4 мм). На расстоянии зазор двигает ε слабее: у точки 50 мм
    /// (`RC103_point50`, `B30`, П99 18.09.2026) П73 мерила 0.917 при 3 мм и 0.857
    /// при 4 мм — окно `--control=0.8:0.95` (измерено 0.882 при 3.5 мм).
    ///
    /// Коды возврата: 0 — совпало и контроль разошёлся; 1 — есть расхождения;
    /// 2 — не запустилась.
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string presetName = null, scenePath = null, outCsv = null;
            double distance = 0.0;
            double controlLo = 0.3, controlHi = 0.6;
            var options = new EfficiencyCalculationOptions();
            foreach (string arg in args)
            {
                if (arg.StartsWith("--preset=", StringComparison.Ordinal)) presetName = arg.Substring(9);
                else if (arg.StartsWith("--scene=", StringComparison.Ordinal)) scenePath = arg.Substring(8);
                else if (arg.StartsWith("--out=", StringComparison.Ordinal)) outCsv = arg.Substring(6);
                else if (arg.StartsWith("--distance=", StringComparison.Ordinal))
                    distance = double.Parse(arg.Substring(11), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--n=", StringComparison.Ordinal))
                    options.Histories = int.Parse(arg.Substring(4), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--control=", StringComparison.Ordinal))
                {
                    // Окно контроля зависит от ПОСТАНОВКИ (расстояния), а не от шаблона:
                    // впритык 0.3…0.6, точка 50 мм 0.8…0.95 (П99, `B30`).
                    string[] lh = arg.Substring(10).Split(':');
                    if (lh.Length != 2)
                    {
                        Console.Error.WriteLine("--control= ждёт lo:hi, например 0.3:0.6");
                        return 2;
                    }
                    controlLo = double.Parse(lh[0], CultureInfo.InvariantCulture);
                    controlHi = double.Parse(lh[1], CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + arg);
                    return 2;
                }
            }

            if (presetName == null || scenePath == null || !File.Exists(scenePath))
            {
                Console.Error.WriteLine("GapTemplateProbeE43.exe --preset=<имя> --scene=<.in> [--distance=0] [--n=200000] [--out=<csv>]");
                return 2;
            }

            GlobalConfigManager.GetInstance();

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

            // Шаблон — так, как его применяет редактор: на заготовку, затем
            // точка впритык. Ровно это делает и генератор сцен.
            GeometryModel fromPreset = GeometryEditorPanel.Blank();
            preset.Apply(fromPreset);
            fromPreset.SourceType = GeometrySourceType.Point;
            fromPreset.PointDistance = distance;

            GeometryModel fromScene = GeometryModel.Load(scenePath);

            GeometryModel control = fromPreset.Clone();
            control.FrontGapThickness = 0.0;

            Say("шаблон «{0}»: зазор у торца {1:F2} мм, наполнитель {2}; точка на {3:F1} мм от торца корпуса",
                preset.Name, fromPreset.FrontGapThickness, fromPreset.Gap.Name, distance);
            Say("сцена {0}: зазор у торца {1:F2} мм, наполнитель \"{2}\", источник {3}, точка на {4:F1} мм",
                Path.GetFileName(scenePath), fromScene.FrontGapThickness, fromScene.Gap.Name,
                fromScene.SourceType, fromScene.PointDistance);
            Say("контроль: тот же шаблон, зазор сброшен в {0:F2} мм", control.FrontGapThickness);
            Say("историй на узел: {0}", options.Histories);

            var physics = new ResponseMatrixOptions();
            Say("клеймо матрицы, шаблон : {0}", ResponseMatrix.ComputeStamp(fromPreset, physics));
            Say("клеймо матрицы, сцена  : {0}", ResponseMatrix.ComputeStamp(fromScene, physics));
            Say("клеймо матрицы, контр. : {0}", ResponseMatrix.ComputeStamp(control, physics));

            List<ROIEfficiencyData> a = Curve(fromPreset, options, "шаблон");
            List<ROIEfficiencyData> b = Curve(fromScene, options, "сцена");
            List<ROIEfficiencyData> c = Curve(control, options, "контроль");
            if (a == null || b == null || c == null) return 1;

            Say("");
            Say("{0,8} {1,14} {2,14} {3,10} {4,14} {5,10}", "E, кэВ", "шаблон", "сцена", "Δ, %", "контроль", "сцена/контр.");
            double worst = 0.0, worstE = 0.0;
            double ratio662 = 0.0;
            int n = Math.Min(a.Count, b.Count);
            if (a.Count != b.Count)
            {
                Fail(string.Format(CultureInfo.InvariantCulture, "число узлов: шаблон {0}, сцена {1}", a.Count, b.Count));
            }

            TextWriter csv = outCsv != null ? new StreamWriter(outCsv, false, new System.Text.UTF8Encoding(false)) : null;
            if (csv != null) csv.WriteLine("energy_kev,eff_preset,eff_scene,delta_pct,eff_control,scene_over_control");
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(a[i].Energy - b[i].Energy) > 1e-9)
                {
                    Fail(string.Format(CultureInfo.InvariantCulture, "узел {0}: энергии разошлись {1} / {2}", i, a[i].Energy, b[i].Energy));
                    continue;
                }

                double d = 100.0 * (b[i].Efficiency - a[i].Efficiency) / a[i].Efficiency;
                double ctl = i < c.Count && Math.Abs(c[i].Energy - a[i].Energy) < 1e-9 ? c[i].Efficiency : double.NaN;
                double r = ctl > 0.0 ? b[i].Efficiency / ctl : double.NaN;
                if (Math.Abs(a[i].Energy - 662.0) < 1e-9) ratio662 = r;
                if (Math.Abs(d) > Math.Abs(worst)) { worst = d; worstE = a[i].Energy; }
                Say("{0,8:F0} {1,14:E5} {2,14:E5} {3,10:F4} {4,14:E5} {5,10:F3}",
                    a[i].Energy, a[i].Efficiency, b[i].Efficiency, d, ctl, r);
                if (csv != null)
                {
                    csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4:R},{5:R}",
                                                a[i].Energy, a[i].Efficiency, b[i].Efficiency, d, ctl, r));
                }
            }

            if (csv != null) csv.Dispose();

            Say("");
            Say("шаблон против сцены: худший узел {0:F4} % на {1:F0} кэВ (узлов {2})", worst, worstE, n);
            Say("положительный контроль: сцена / шаблон-без-зазора на 662 кэВ = {0:F3}", ratio662);
            if (Math.Abs(worst) > 0.1)
            {
                Fail(string.Format(CultureInfo.InvariantCulture,
                    "кривая шаблона и кривая сцены разошлись на {0:F4} % (порог 0.1 %)", worst));
            }

            // Контроль обязан РАЗОЙТИСЬ: при зазоре 3.5 мм впритык ε(662) — около
            // 0.43 от ε без зазора (П73: 0.484 при 3 мм, 0.381 при 4 мм); окно —
            // по постановке (`--control=`, умолчание впритык).
            if (!(ratio662 > controlLo && ratio662 < controlHi))
            {
                Fail(string.Format(CultureInfo.InvariantCulture,
                    "контроль: сцена / шаблон-без-зазора на 662 = {0:F3}, ждали {1:F2}…{2:F2}",
                    ratio662, controlLo, controlHi));
            }

            Say("");
            Say(bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: {0}", bad);
            return bad == 0 ? 0 : 1;
        }

        static List<ROIEfficiencyData> Curve(GeometryModel g, EfficiencyCalculationOptions options, string what)
        {
            EfficiencyFitResult r = EfficiencyCalculation.Run(g, options, null, null);
            if (!string.IsNullOrEmpty(r.Error))
            {
                Fail(what + ": расчёт кривой не пошёл: " + r.Error);
                return null;
            }

            EfficiencyNodeSpread spread = EfficiencyCalculation.NodeSpread(r.Curve, options.Histories);
            Say("{0}: узлов {1}, разброс МК медиана {2:F2} %, худший {3:F2} % на {4:F0} кэВ",
                what, r.Curve.Count, spread.MedianPercent, spread.WorstPercent, spread.WorstEnergy);
            return new List<ROIEfficiencyData>(r.Curve);
        }

        static void Fail(string message)
        {
            bad++;
            Say("  ⛔ {0}", message);
        }

        static void Say(string format, params object[] args)
        {
            Console.WriteLine(args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
