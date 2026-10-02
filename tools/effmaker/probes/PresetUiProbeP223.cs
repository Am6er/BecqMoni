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
/// Приёмка П223 (`AMBER153`): что ВИДИТ человек в редакторе геометрии, выбрав
/// встроенный шаблон «RadiaCode-101»/«RadiaCode-103», — строка вещества корпуса
/// (список, плотность, состав), толщины торца и зазора, — и что уйдёт в расчёт
/// из полей (`BuildModel`, тот же путь, что у кнопки «Рассчитать»).
///
/// Шаблон с П223 ставит корпусу полиэтилен, а полиэтилен в библиотеке веществ —
/// вида «стенка сосуда», и в список корпуса (вид «корпус» + «прочие») он не
/// входит: проба печатает, что из этого выходит на экране.
///
///   PresetUiProbeP223.exe --dir=&lt;каталог пробы&gt;
///
/// Коды возврата: 0 — у обоих шаблонов строка корпуса названа (список не пуст)
/// и поля дают в расчёт то, что поставил шаблон; 1 — нет; 2 — не запустилась.
/// </summary>
static class PresetUiProbeP223
{
    static int bad;

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
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        if (string.IsNullOrEmpty(dir))
        {
            Console.Error.WriteLine("PresetUiProbeP223.exe --dir=<каталог пробы>");
            return 2;
        }

        // Свои шаблоны пробы — в её каталог, чтобы не тронуть конфигурацию
        // пользователя (так же, как `GeometryTemplateProbe`).
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        GeometryTemplateStore.PathOverride = Path.Combine(dir, "config", "GeometryTemplates_p223.xml");
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
            ComboBox combo = materials["Cladding"];
            Label composition = compositions["Cladding"];
            GeometryModel shown = panel.Model;
            GeometryModel built = (GeometryModel)typeof(GeometryEditorPanel)
                .GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                .Invoke(panel, null);

            Console.WriteLine();
            Console.WriteLine("[{0}] шаблон «{1}»", culture, presetName);
            Console.WriteLine("  строка «корпус»: список SelectedIndex {0}, текст «{1}», строк в списке {2}",
                              combo.SelectedIndex, combo.Text, combo.Items.Count);
            Console.WriteLine("  состав рядом   : «{0}»", composition.Text.Replace("\r", " ").Replace("\n", " "));
            Console.WriteLine("  модель шаблона : корпус «{0}» ρ {1:R}, торец {2:R} мм, бок {3:R} мм, зазор {4:R} мм",
                              shown.Cladding.Name, shown.Cladding.Density, shown.FrontCladdingThickness,
                              shown.SideCladdingThickness, shown.FrontGapThickness);
            Console.WriteLine("  в расчёт полями: корпус «{0}» ρ {1:R}, торец {2:R} мм, бок {3:R} мм, зазор {4:R} мм, долей состава {5}",
                              built.Cladding.Name, built.Cladding.Density, built.FrontCladdingThickness,
                              built.SideCladdingThickness, built.FrontGapThickness, built.Cladding.Fractions.Count);

            if (combo.SelectedIndex < 0 || string.IsNullOrEmpty(combo.Text))
            {
                Fail(presetName + " [" + culture + "]: строка вещества корпуса ПУСТА — человек не видит, из чего корпус");
            }

            if (built.Cladding.Name != shown.Cladding.Name
                || Math.Abs(built.Cladding.Density - shown.Cladding.Density) > 1e-12
                || Math.Abs(built.FrontCladdingThickness - shown.FrontCladdingThickness) > 1e-12
                || Math.Abs(built.FrontGapThickness - shown.FrontGapThickness) > 1e-12
                || built.Cladding.Fractions.Count != shown.Cladding.Fractions.Count)
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
