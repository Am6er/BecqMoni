// W22: помещается ли текст в надписи формы «Матрица отклика».
//
// Проба заведена по снимку Amber 16.08.2026: под строкой «Computed:» иногда
// печатается текст, и половина его за границей панели. Пятая строка подробностей
// уходила за нижний край панели, у которой высота была прибита под четыре.
//
//     matrixlayoutprobe
//
// Проверяется НЕ «код отработал», а геометрия на экране, и в ОБОИХ языках:
// русские строки этой формы длиннее английских, и прибитая высота, которой
// хватает по-английски, по-русски уже обрезает.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

static class MatrixLayoutProbe
{
    static int failures;

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        DeviceType.InitializeDeviceTypes();
        GlobalConfigManager.GetInstance();

        foreach (string culture in new[] { "en-US", "ru-RU" })
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            Console.WriteLine();
            Console.WriteLine("== {0} ==", culture);

            // Сателлит с русскими строками копируется ОТДЕЛЬНО от
            // BecquerelMonitor.exe, и без папки `ru` рядом с пробой русский
            // прогон молча мерил бы английские строки — то есть проверял бы
            // дважды одно и то же и говорил, что проверил два языка. Это не
            // предупреждение, а расхождение: проба, которая лжёт о том, что
            // она проверила, хуже отсутствующей.
            if (culture.StartsWith("ru") && !Localized())
            {
                Console.WriteLine("  РАСХОЖДЕНИЕ: нет папки `ru` рядом с пробой — "
                                  + "мерились английские строки");
                failures++;
                continue;
            }

            Check(culture);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + failures);
        return failures == 0 ? 0 : 1;
    }

    static void Check(string culture)
    {
        using (ResponseMatrixForm form = new ResponseMatrixForm(Config()))
        {
            // Пять строк, из них одна длинная: ровно тот случай со снимка —
            // четыре строки `ResponseMatrixDetails` плюс приписанное
            // расхождение диапазонов (E18 «б»).
            string details = string.Join(Environment.NewLine, new[]
            {
                "Nodes: 100 from 20 to 3000 keV",
                "Bin: 2 keV, histories per node: 300000",
                "Data: 492 KB, file: 376 KB",
                "Computed: 15.08.2026 13:56, took 139 s",
                RangeDiffersLine(),
            });

            Invoke(form, "SetDetails", details);

            Panel panel = (Panel)Field(form, "detailsPanel");
            Console.WriteLine("  панель подробностей: {0} px, строк {1}",
                              panel.Height, panel.Controls.Count);

            int bottom = 0;
            foreach (Control row in panel.Controls)
            {
                bottom = Math.Max(bottom, row.Bottom);
                Fits(row, "строка подробностей");
            }

            Say(bottom <= panel.Height,
                string.Format("низ строк {0} против высоты панели {1}", bottom, panel.Height));

            // Соседние надписи той же формы — та же беда была бы у них.
            Fits((Control)Field(form, "stateLabel"), "состояние");
            Fits((Control)Field(form, "versionsLabel"), "версии");

            // И ничто не должно вылезти за нижний край окна.
            int lowest = 0;
            string who = "";
            foreach (Control c in form.Controls)
            {
                if (c.Bottom > lowest)
                {
                    lowest = c.Bottom;
                    who = c.GetType().Name + " " + c.Name;
                }
            }

            Say(lowest <= form.ClientSize.Height,
                string.Format("низ формы: {0} ({1}) против окна {2}",
                              lowest, who, form.ClientSize.Height));
        }
    }

    /// <summary>Загрузился ли русский сателлит: строка обязана отличаться.</summary>
    static bool Localized()
    {
        return BecquerelMonitor.Properties.Resources.ResponseMatrixParameters != "Parameters";
    }

    /// <summary>Длинная строка расхождения диапазонов — на языке прогона.</summary>
    static string RangeDiffersLine()
    {
        return string.Format(CultureInfo.CurrentCulture,
                             BecquerelMonitor.Properties.Resources.ResponseMatrixRangeDiffers,
                             20.0, 2000.0, 30.0, 3000.0);
    }

    /// <summary>Помещается ли текст надписи в её собственный прямоугольник.</summary>
    static void Fits(Control control, string what)
    {
        if (control == null || string.IsNullOrEmpty(control.Text))
        {
            return;
        }

        Size need = TextRenderer.MeasureText(control.Text, control.Font,
                                             new Size(control.Width, int.MaxValue),
                                             TextFormatFlags.WordBreak);
        bool ok = need.Height <= control.Height && need.Width <= control.Width;
        Console.WriteLine("  {0,-22} надо {1}x{2}, дано {3}x{4} — {5}",
                          what, need.Width, need.Height, control.Width, control.Height,
                          ok ? "влезает" : "ОБРЕЗАНО");
        if (!ok)
        {
            failures++;
        }
    }

    static void Say(bool ok, string text)
    {
        Console.WriteLine("  {0} — {1}", text, ok ? "ок" : "ПЛОХО");
        if (!ok)
        {
            failures++;
        }
    }

    static EfficiencyConfigData Config()
    {
        EfficiencyConfigData config = new EfficiencyConfigData();
        config.Guid = Guid.NewGuid().ToString();
        config.Name = "проба W22";

        GeometryModel g = GeometryEditorPanel.Blank();
        foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
        {
            if (preset.Name == "Gamma-1S UDS-GC 63x63")
            {
                preset.Apply(g);
                break;
            }
        }

        g.SourceType = GeometrySourceType.Point;
        g.PointDistance = 50.0;
        config.Geometry = g;
        List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
        curve.Add(new ROIEfficiencyData { Energy = 20.0, Efficiency = 0.2 });
        curve.Add(new ROIEfficiencyData { Energy = 2000.0, Efficiency = 0.01 });
        config.Curve = curve;
        return config;
    }

    static object Field(object form, string name)
    {
        return form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                   .GetValue(form);
    }

    static void Invoke(object form, string name, params object[] args)
    {
        form.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(form, args);
    }
}
