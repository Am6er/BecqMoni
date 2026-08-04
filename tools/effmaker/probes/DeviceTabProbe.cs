using System;
using System.Reflection;
using System.Windows.Forms;
using BecquerelMonitor;

/// <summary>
/// Есть ли вкладка «Эффективность» в конфигурации прибора, и на своём ли месте.
///
/// Заведена по конкретному случаю: вкладка создавалась, получала родителя и
/// десять контролов — и НЕ ПОЯВЛЯЛАСЬ на форме. TabPages.Insert до создания
/// дескриптора окна кладёт страницу только в Controls, а в TabPages она не
/// попадает: семь контролов против шести вкладок, ни исключения, ни
/// предупреждения. Глазами это ловится только запуском приложения, а по коду
/// не ловится вовсе — поэтому проба сверяет ОБА счётчика.
///
/// Спутниковую сборку ru надо копировать рядом: без неё новые строки читаются
/// нейтральными, и «Efficiency» вместо «Эффективность» выглядит как потерянный
/// перевод, хотя перевод на месте.
///
/// Сборка (после сборки основного проекта):
///   csc /target:exe /langversion:7.3 /out:&lt;wd&gt;\devtabprobe.exe ^
///       /r:&lt;wd&gt;\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll ^
///       /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
///       tools\effmaker\probes\DeviceTabProbe.cs
///
/// Ожидание: вкладок 7, Controls 7, «Эффективность» сразу за «Калибровкой энергии».
/// </summary>
static class DeviceTabProbe
{
    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();
        using (DeviceConfigForm form = new DeviceConfigForm())
        {
            TabPage page = (TabPage)Field(form, "efficiencyTabPage");
            Console.WriteLine("efficiencyTabPage: {0}", page == null ? "NULL" : "создан");
            if (page != null)
            {
                Console.WriteLine("  Text   = '{0}'", page.Text);
                Console.WriteLine("  Parent = {0}", page.Parent == null ? "NULL" : page.Parent.Name);
                Console.WriteLine("  контролов внутри: {0}", page.Controls.Count);
            }

            TabControl tc = (TabControl)Field(form, "tabControl1");
            Console.WriteLine("tabControl1: вкладок {0}, Controls {1}", tc.TabPages.Count, tc.Controls.Count);
            foreach (Control c in tc.Controls)
            {
                Console.WriteLine("  Controls: {0} '{1}'", c.GetType().Name, c.Text);
            }
        }

        return 0;
    }

    static object Field(object target, string name)
    {
        Type t = target.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) return f.GetValue(target);
            t = t.BaseType;
        }
        throw new InvalidOperationException("нет поля " + name);
    }
}
