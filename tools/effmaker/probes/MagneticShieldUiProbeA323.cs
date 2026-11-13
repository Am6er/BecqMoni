using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// (`AMBER323`, полоса `a323` 13.11.2026) Редактор геометрии: поля МАГНИТНОГО ЭКРАНА глазами человека.
/// Запускать ТОЛЬКО со скрытого стола (`HiddenRun.exe`): окно создаётся по-настоящему.
///
///   MagneticShieldUiProbeA323.exe --dir=&lt;каталог пробы&gt; [--snap=&lt;каталог снимков&gt;]
///
/// На en и ru: шаблон «AtomSpectra Pro LaBr3(Ce)» ставит в поля толщину 0.25, длину 25 и вещество
/// «Permalloy»; подписи строк на языке окна и не обрезаны панелью; путь кнопки «Рассчитать»
/// (`BuildModel`) отдаёт экран в расчёт; шаблон «Atom Spectra Pro 80x80» после него экран снимает;
/// чертёж рисуется (снимок формы — в `--snap`, вне git). Коды: 0 — сошлось, 1 — нет, 2 — не запустилась.
/// </summary>
static class MagneticShieldUiProbeA323
{
    static int bad;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Application.EnableVisualStyles();
        string dir = null, snap = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--snap=", StringComparison.Ordinal)) snap = a.Substring(7);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (string.IsNullOrEmpty(dir))
        {
            Console.Error.WriteLine("MagneticShieldUiProbeA323.exe --dir=<каталог пробы> [--snap=<каталог снимков>]");
            return 2;
        }

        Directory.CreateDirectory(Path.Combine(dir, "config"));
        GeometryTemplateStore.PathOverride = Path.Combine(dir, "config", "GeometryTemplates_a323.xml");
        GeometryTemplateStore.Reload();
        foreach (string culture in new[] { "en", "ru" })
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            Run(culture, snap);
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + bad);
        return bad == 0 ? 0 : 1;
    }

    static void Run(string culture, string snap)
    {
        using (Form form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0),
                                      ShowInTaskbar = false, ClientSize = new Size(1100, 900) })
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();
            Console.WriteLine();
            Console.WriteLine("[{0}]", culture);
            if (!panel.SelectPresetByName("AtomSpectra Pro LaBr3(Ce)"))
            {
                Fail("шаблон LaBr3 не выбирается");
                return;
            }

            Application.DoEvents();
            var fields = (Dictionary<string, TextBox>)Field(panel, "fields");
            var materials = (Dictionary<string, ComboBox>)Field(panel, "materials");
            string thick = fields["MagneticShieldThickness"].Text, len = fields["MagneticShieldLength"].Text;
            ComboBox combo = materials["MagneticShield"];
            Console.WriteLine("  поля: толщина «{0}», длина «{1}», вещество «{2}», плотность «{3}»",
                              thick, len, combo.Text, fields["MagneticShield.Density"].Text);
            Check(thick == "0.25" && len == "25", "поля толщины и длины — 0.25 и 25");
            Check(combo.SelectedIndex >= 0 && combo.Text.Contains("Permalloy"), "вещество экрана — Permalloy");
            Check(fields["MagneticShield.Density"].Text == "8.7", "плотность экрана — 8.7");
            // (`AMBER323`, Amber 13.11.2026 «Разрешить, умолчание 0») Боковой зазор у цилиндра — в окне.
            string sideCaption = BecquerelMonitor.Properties.Resources.ResourceManager.GetString(
                "GeometryEditorSideGap", Thread.CurrentThread.CurrentUICulture);
            Label sideLabel = FindLabel(panel, sideCaption);
            Console.WriteLine("  зазор сбоку: «{0}», подпись «{1}», строка показана: {2}", fields["SideGapThickness"].Text,
                              sideCaption, sideLabel != null && Shown(sideLabel));
            Check(fields["SideGapThickness"].Text == "9.75" && sideLabel != null && Shown(sideLabel) && Visible(sideLabel),
                  "цилиндр: строка «зазор сбоку» показана, 9.75 мм");

            foreach (string key in new[] { "GeometryEditorMagneticShield", "GeometryEditorMagneticShieldLength",
                                           "GeometryEditorMagneticShieldMaterial" })
            {
                string caption = BecquerelMonitor.Properties.Resources.ResourceManager.GetString(key, Thread.CurrentThread.CurrentUICulture);
                bool cyr = false;
                foreach (char c in caption ?? "") cyr |= c >= 'А' && c <= 'я';
                Label label = FindLabel(panel, caption);
                bool visible = label != null && Visible(label);
                Console.WriteLine("  подпись {0}: «{1}», видна целиком: {2}", key, caption, visible);
                Check(!string.IsNullOrEmpty(caption) && cyr == (culture == "ru"), key + ": строка на языке окна");
                Check(visible, key + ": подпись на панели и не обрезана");
            }

            GeometryModel built = Build(panel);
            Console.WriteLine("  в расчёт: экран {0:R} мм, длина {1:R} мм, «{2}» ρ {3:R}, долей {4}",
                              built.MagneticShieldThickness, built.MagneticShieldLength, built.MagneticShield.Name,
                              built.MagneticShield.Density, built.MagneticShield.Fractions.Count);
            Check(built.SideGapThickness == 9.75, "цилиндр: зазор сбоку уходит в расчёт как набран (9.75)");
            Check(built.HasMagneticShield && built.MagneticShieldThickness == 0.25 && built.MagneticShieldLength == 25.0
                  && built.MagneticShield.Name == "Permalloy" && built.MagneticShield.Density == 8.7
                  && built.MagneticShield.Fractions.Count == 2, "поля отдают экран в расчёт");
            Check(GeometryWriter.Render(built) == GeometryWriter.Render(panel.Model), "текст `.in` полей = текст модели шаблона");

            if (snap != null)
            {
                Directory.CreateDirectory(snap);
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    string file = Path.Combine(snap, "a323_editor_labr3_" + culture + ".png");
                    bmp.Save(file, ImageFormat.Png);
                    Console.WriteLine("  снимок: " + file);
                }
            }

            panel.SelectPresetByName("Atom Spectra Pro 80x80");
            Application.DoEvents();
            GeometryModel other = Build(panel);
            Console.WriteLine("  после 80x80: поле толщины «{0}», в расчёт {1:R} мм", fields["MagneticShieldThickness"].Text,
                              other.MagneticShieldThickness);
            Check(!other.HasMagneticShield && !GeometryWriter.Render(other).Contains("MagneticShield"), "80x80 после LaBr3 — без экрана");
            form.Close();
        }
    }

    static GeometryModel Build(GeometryEditorPanel panel)
    {
        return (GeometryModel)typeof(GeometryEditorPanel)
            .GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            .Invoke(panel, null);
    }

    static Label FindLabel(Control root, string text)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Label && c.Text == text) return (Label)c;
            Label inner = FindLabel(c, text);
            if (inner != null) return inner;
        }

        return null;
    }

    /// <summary>Контрол показан и целиком внутри родителя (панель детей обрезает молча).</summary>
    static bool Visible(Control c)
    {
        // Флаг Visible здесь не годится: он ложен у всего на невыбранной вкладке.
        Control p = c.Parent;
        return p == null || (c.Bottom <= p.ClientSize.Height && c.Right <= p.ClientSize.Width + 400);
    }

    /// <summary>Свой флаг показа (без родителей): строку снимают `ShowRow` через Visible самого контрола.</summary>
    static bool Shown(Control c)
    {
        return (bool)typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(c, new object[] { 2 });
    }

    static object Field(object o, string name)
    {
        return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
    }

    static void Check(bool ok, string what)
    {
        Console.WriteLine("  {0} {1}", ok ? "ок   " : "ПРОВАЛ", what);
        if (!ok) bad++;
    }

    static void Fail(string message)
    {
        bad++;
        Console.WriteLine("  ⛔ " + message);
    }
}
