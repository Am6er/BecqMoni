// Приёмочная проба полосы П54 (AMBER25, снятие фита из конструктора кривой).
// Временная: собирается голым csc против каталога проб и живёт в D:\BqMoni_Claude\p54,
// копия исходника — в handover/p54-amber25/. Не входит в tools/effmaker/probes.
//
//   AcceptP54 --geometry=<AS80_point0.in> --out=<curve.txt> [--no-form]
//
// 1. Кривая из геометрии умолчаниями приложения (EfficiencyCalculation.Run с
//    options по умолчанию, physics = null, importance = null — ровно путь окна
//    «Посчитать из геометрии»): узлы печатаются полной точностью ("R"), клеймо,
//    sha256 текста узлов. Сравнивается «до» и «после» побайтно.
// 2. Окно конструктора отражением: число вкладок tabControl и их подписи,
//    кнопки формы и наличие обработчика Click у каждой, поля фита (отсутствие).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

static class AcceptP54
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");

        string geometryPath = null, outPath = "curve.txt";
        bool form = true;
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a == "--no-form") form = false;
            else { Console.WriteLine("не знаю ключа: " + a); return 2; }
        }

        int bad = 0;
        if (geometryPath != null)
        {
            bad += Curve(geometryPath, outPath);
        }

        if (form)
        {
            bad += Form();
        }

        Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
        return bad == 0 ? 0 : 1;
    }

    static int Curve(string geometryPath, string outPath)
    {
        Console.WriteLine("== кривая из геометрии умолчаниями приложения ==");
        GeometryModel geometry = GeometryModel.Load(geometryPath);
        Console.WriteLine("  геометрия: " + geometry.Describe());
        var options = new EfficiencyCalculationOptions();
        Console.WriteLine("  умолчания: {0}-{1} кэВ, {2}, узлов {3}, историй {4}",
                          options.MinEnergyKev, options.MaxEnergyKev, options.GridMode,
                          options.NodeCount, options.Histories);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EfficiencyFitResult result = EfficiencyCalculation.Run(geometry, options, null, null);
        Console.WriteLine("  счёт: {0:F1} с", clock.Elapsed.TotalSeconds);
        if (!result.Ok)
        {
            Console.WriteLine("  РАСЧЁТ НЕ ПОШЁЛ: " + result.Error);
            return 1;
        }

        var text = new StringBuilder();
        text.Append("stamp\t").Append(result.ComputeStamp).Append('\n');
        text.Append("level\t").Append(result.LevelSource).Append('\n');
        text.Append("range\t").Append(result.MinEnergy.ToString("R", CultureInfo.InvariantCulture))
            .Append('\t').Append(result.MaxEnergy.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (ROIEfficiencyData p in result.Curve)
        {
            text.Append(p.Energy.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                .Append(p.Efficiency.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                .Append(p.ErrorPercent.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
        File.WriteAllBytes(outPath, bytes);
        string sha;
        using (var sha256 = SHA256.Create())
        {
            sha = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        Console.WriteLine("  узлов: {0}, клеймо: {1}", result.Curve.Count, result.ComputeStamp);
        Console.WriteLine("  sha256 узлов: " + sha);
        Console.WriteLine("  записано: " + Path.GetFullPath(outPath));
        return 0;
    }

    static int Form()
    {
        Console.WriteLine("== окно конструктора отражением ==");
        int bad = 0;
        using (var form = new EfficiencyMakerForm())
        {
            var tabs = (TabControl)Field(form, "tabControl");
            Console.WriteLine("  вкладок: " + tabs.TabPages.Count);
            foreach (TabPage page in tabs.TabPages)
            {
                Console.WriteLine("    - «{0}» ({1})", page.Text, page.Name);
            }

            if (tabs.TabPages.Count != 2)
            {
                Console.WriteLine("  ⛔ вкладок должно быть ДВЕ (геометрия, расчёт)");
                bad++;
            }

            string[] fitFields =
            {
                "tabPageFit", "spectraGrid", "spectrumFiles", "runButton", "optionsGroupBox",
                "orderNumericUpDown", "minIntensityNumericUpDown", "minSignificanceNumericUpDown",
                "backgroundCheckBox", "anchorEnergyTextBox", "anchorEfficiencyTextBox", "chainNames",
            };
            foreach (string name in fitFields)
            {
                FieldInfo f = typeof(EfficiencyMakerForm).GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Console.WriteLine("  поле {0}: {1}", name, f == null ? "нет" : "ЕСТЬ");
                if (f != null) bad++;
            }

            string[] fitMethods = { "BuildInput", "AskFallbackDevice", "PackGeometryComplaints", "runButton_Click",
                                    "LoadChains", "ReloadNuclideSets", "GuessChain", "UpdateGraphMode" };
            foreach (string name in fitMethods)
            {
                MethodInfo m = typeof(EfficiencyMakerForm).GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Console.WriteLine("  метод {0}: {1}", name, m == null ? "нет" : "ЕСТЬ");
                if (m != null) bad++;
            }

            // Кнопки формы и обработчик Click у каждой: EventHandlerList компонента,
            // ключ — статическое поле Control.EventClick.
            FieldInfo keyField = typeof(Control).GetField("EventClick", BindingFlags.Static | BindingFlags.NonPublic);
            PropertyInfo eventsProp = typeof(System.ComponentModel.Component).GetProperty(
                "Events", BindingFlags.Instance | BindingFlags.NonPublic);
            object key = keyField == null ? null : keyField.GetValue(null);
            var buttons = new List<Button>();
            Collect(form, buttons);
            foreach (Button b in buttons)
            {
                bool has = false;
                if (key != null && eventsProp != null)
                {
                    var list = (System.ComponentModel.EventHandlerList)eventsProp.GetValue(b, null);
                    has = list != null && list[key] != null;
                }

                Console.WriteLine("  кнопка «{0}» ({1}): Click {2}", b.Text, b.Name, has ? "есть" : "НЕТ");
                if (!has) bad++;
            }

            var graph = Field(form, "graph");
            PropertyInfo diff = graph.GetType().GetProperty("ShowDifference");
            Console.WriteLine("  EfficiencyCurveGraph.ShowDifference: " + (diff == null ? "нет" : "ЕСТЬ"));
            if (diff != null) bad++;
        }

        Type fitter = typeof(EfficiencyFitResult).Assembly.GetType("BecquerelMonitor.EfficiencyMaker.EfficiencyFitter");
        Console.WriteLine("  тип EfficiencyFitter в сборке: " + (fitter == null ? "нет" : "ЕСТЬ"));
        if (fitter != null) bad++;
        foreach (string t in new[] { "EfficiencyFitInput", "EfficiencyObservation", "EfficiencyLibrary", "EfficiencyLine" })
        {
            Type type = typeof(EfficiencyFitResult).Assembly.GetType("BecquerelMonitor.EfficiencyMaker." + t);
            Console.WriteLine("  тип {0}: {1}", t, type == null ? "нет" : "ЕСТЬ");
            if (type != null) bad++;
        }

        Console.WriteLine("  EfficiencyLevelSource: " + string.Join(", ", Enum.GetNames(typeof(EfficiencyLevelSource))));
        return bad;
    }

    static void Collect(Control root, List<Button> into)
    {
        foreach (Control c in root.Controls)
        {
            var b = c as Button;
            if (b != null) into.Add(b);
            Collect(c, into);
        }
    }

    static object Field(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) throw new InvalidOperationException("нет поля " + name);
        return f.GetValue(target);
    }
}
