using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// Приёмка П233 (`AMBER162`): что ВИДИТ человек в редакторе геометрии, выбрав
/// встроенный шаблон «RadiaCode-101»/«RadiaCode-103», — строка вещества
/// ОТРАЖАТЕЛЯ (список, плотность, состав), толщины отражателя у торца и бока,
/// зазор, — и что уйдёт в расчёт из полей (`BuildModel`, путь кнопки
/// «Рассчитать»). Родня `PresetUiProbeP223` (там — строка корпуса).
///
///   PresetUiProbeP233.exe --dir=&lt;каталог пробы&gt; [--expect=TiO2 reflective paint]
///
/// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — та же проба на сборке ДО правки: там отражатель
/// шаблона — фторопласт, и проба обязана отказать (код 1) на `--expect`.
///
/// Коды возврата: 0 — у обоих шаблонов на en и ru строка отражателя названа,
/// вещество — ожидаемое, поля дают в расчёт то, что поставил шаблон; 1 — нет;
/// 2 — не запустилась.
/// </summary>
static class PresetUiProbeP233
{
    static int bad;
    static string expect = "TiO2 reflective paint";

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Application.EnableVisualStyles();

        string dir = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--expect=", StringComparison.Ordinal)) expect = a.Substring(9);
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        if (string.IsNullOrEmpty(dir))
        {
            Console.Error.WriteLine("PresetUiProbeP233.exe --dir=<каталог пробы> [--expect=<вещество отражателя>]");
            return 2;
        }

        // Свои шаблоны пробы — в её каталог, чтобы не тронуть конфигурацию
        // пользователя (так же, как `PresetUiProbeP223`).
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        GeometryTemplateStore.PathOverride = Path.Combine(dir, "config", "GeometryTemplates_p233.xml");
        GeometryTemplateStore.Reload();

        foreach (string culture in new[] { "en", "ru" })
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            foreach (string name in new[] { "RadiaCode-101", "RadiaCode-103" })
            {
                Run(name, culture);
            }
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + bad);
        return bad == 0 ? 0 : 1;
    }

    static void Run(string presetName, string culture)
    {
        using (Form form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
            ClientSize = new Size(950, 720),
        })
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();
            if (!panel.SelectPresetByName(presetName))
            {
                Fail(presetName + ": шаблон не выбирается");
                return;
            }

            Application.DoEvents();
            var materials = (Dictionary<string, ComboBox>)Field(panel, "materials");
            var compositions = (Dictionary<string, Label>)Field(panel, "compositions");
            ComboBox combo = materials["Reflector"];
            Label composition = compositions["Reflector"];
            GeometryModel shown = panel.Model;
            GeometryModel built = (GeometryModel)typeof(GeometryEditorPanel)
                .GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                .Invoke(panel, null);

            Console.WriteLine();
            Console.WriteLine("[{0}] шаблон «{1}»", culture, presetName);
            Console.WriteLine("  строка «отражатель»: SelectedIndex {0}, текст «{1}», строк в списке {2}",
                              combo.SelectedIndex, combo.Text, combo.Items.Count);
            Console.WriteLine("  состав рядом       : «{0}»", composition.Text.Replace("\r", " ").Replace("\n", " "));
            Console.WriteLine("  модель шаблона : отражатель «{0}» ρ {1:R}, торец {2:R} мм, бок {3:R} мм; корпус «{4}» торец {5:R} мм; зазор {6:R} мм",
                              shown.Reflector.Name, shown.Reflector.Density, shown.FrontReflectorThickness,
                              shown.SideReflectorThickness, shown.Cladding.Name, shown.FrontCladdingThickness,
                              shown.FrontGapThickness);
            Console.WriteLine("  в расчёт полями: отражатель «{0}» ρ {1:R}, торец {2:R} мм, бок {3:R} мм, долей состава {4}; зазор {5:R} мм",
                              built.Reflector.Name, built.Reflector.Density, built.FrontReflectorThickness,
                              built.SideReflectorThickness, built.Reflector.Fractions.Count, built.FrontGapThickness);

            if (combo.SelectedIndex < 0 || string.IsNullOrEmpty(combo.Text))
            {
                Fail(presetName + " [" + culture + "]: строка вещества отражателя ПУСТА");
            }

            if (shown.Reflector.Name != expect)
            {
                Fail(presetName + " [" + culture + "]: отражатель шаблона «" + shown.Reflector.Name + "», ждали «" + expect + "»");
            }

            if (built.Reflector.Name != shown.Reflector.Name
                || Math.Abs(built.Reflector.Density - shown.Reflector.Density) > 1e-12
                || Math.Abs(built.FrontReflectorThickness - shown.FrontReflectorThickness) > 1e-12
                || Math.Abs(built.SideReflectorThickness - shown.SideReflectorThickness) > 1e-12
                || Math.Abs(built.FrontGapThickness - shown.FrontGapThickness) > 1e-12
                || built.Reflector.Fractions.Count != shown.Reflector.Fractions.Count)
            {
                Fail(presetName + " [" + culture + "]: поля дают в расчёт не то, что поставил шаблон");
            }

            form.Close();
        }
    }

    static object Field(object o, string name)
    {
        return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
    }

    static void Fail(string message)
    {
        bad++;
        Console.WriteLine("  ⛔ " + message);
    }
}
