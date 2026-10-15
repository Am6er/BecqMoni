// Проба полосы П322 (15.10.2026): ГРАНИЦЫ ЧИСЛА КАНАЛОВ И ЛИМИТ ВРЕМЕНИ 0 — ДО и ПОСЛЕ правки.
// Журнал — handover/handover-2026-10-15-p322-n42-live-and-limits.md. Решения Amber 15.10.2026
// вопросником, дословно: «64…65536 (Рекомендую)» — конфигурация прибора и «Изменить число
// каналов» вне 64…65536 отказывают словами до записи / до создания документа; «0 — отказ
// (Рекомендую)» — форма конфигурации не принимает лимит времени 0, как отрицательный.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут пути от
// каталога входной сборки). Проба зовёт приложение только тем, что есть И ДО, И ПОСЛЕ правки
// (поля и методы форм — отражением, диалог — настоящим ShowDialog), поэтому один исходник
// собирается на обеих сборках: сборка ДО — положительный контроль (обязана показать дефекты).
//
//   P322ChannelLimitsProbe.exe
//
// Строка вывода — «что мерено  значение  ЧИСТО|ДЕФЕКТ». Окна строятся вне экрана (−4000,
// −4000). Без окон приложения (входная сборка — проба) отказ AppUi идёт в поток ошибок — он
// перехватывается и печатается как «сказано». Файлы конфигурации проба не пишет:
// SaveFormContents переносит поля формы в объект в памяти, запись на диск — у вызывающего.
//
// Коды: 0 — дефектов нет; 4 — есть дефекты (на сборке ДО — ожидаемо); 5 — обвязка.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using XPTable.Models;

static class P322ChannelLimitsProbe
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int defects, rig;
    static readonly StringWriter errors = new StringWriter();

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine(ProbeTargetFramework.Describe());
        string exe = typeof(MainForm).Assembly.Location;
        Console.WriteLine("приложение: " + exe + "  (" + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
        Application.EnableVisualStyles();
        TextWriter realErr = Console.Error;
        Console.SetError(errors);
        try
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            Console.WriteLine();
            Console.WriteLine("=== конфигурация прибора, «Основное»: SaveFormContents");
            DeviceConfig();
            Console.WriteLine();
            Console.WriteLine("=== «Изменить число каналов» (ChanNumberChangeDialog, настоящий ShowDialog)");
            Dialog();
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ обвязка: " + ex);
            rig++;
        }
        finally
        {
            Console.SetError(realErr);
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ ПРОБЫ: дефектов {0}, отказов обвязки {1}", defects, rig);
        if (rig > 0) return 5;
        return defects == 0 ? 0 : 4;
    }

    // ------------------------------------------------------------------ конфигурация прибора
    static void DeviceConfig()
    {
        DeviceConfigForm f = new DeviceConfigForm();
        ShowOff(f);
        TableModel list = (TableModel)Field(f, "tableModel1");
        if (list.Rows.Count == 0)
        {
            Console.WriteLine("⛔ обвязка: в каталоге проб нет конфигураций прибора");
            rig++;
            f.Dispose();
            return;
        }
        list.Selections.SelectCell(0, 0);
        Pump(100);
        DeviceConfigInfo cfg = (DeviceConfigInfo)Field(f, "activeDeviceConfig");
        if (cfg == null)
        {
            Console.WriteLine("⛔ обвязка: конфигурация не стала активной");
            rig++;
            f.Dispose();
            return;
        }
        TextBox ch = (TextBox)Field(f, "integerTextBox1");
        TextBox tm = (TextBox)Field(f, "doubleTextBox5");
        string ch0 = ch.Text, tm0 = tm.Text;
        int cfgCh0 = cfg.NumberOfChannels, cfgTm0 = cfg.DefaultMeasurementTime;
        Console.WriteLine("SETUP\tприбор «{0}»: каналов {1}, лимит {2} с", cfg.Name, ch0, tm0);

        // { каналы, лимит, принять? } — ожидание ПОСЛЕ правки.
        object[][] cases =
        {
            new object[] { "63", tm0, false },
            new object[] { "64", tm0, true },
            new object[] { "4096", tm0, true },
            new object[] { "65536", tm0, true },
            new object[] { "65537", tm0, false },
            new object[] { "2000000000", tm0, false },
            new object[] { "0", tm0, false },      // контроль П316
            new object[] { "-5", tm0, false },     // контроль П316
            new object[] { ch0, "0", false },      // решение «0 — отказ»
            new object[] { ch0, "-100", false },   // контроль П316
            new object[] { ch0, "1", true },
            new object[] { ch0, tm0, true },       // контроль: прежние числа
        };
        foreach (object[] c in cases)
        {
            string chText = (string)c[0], tmText = (string)c[1];
            bool want = (bool)c[2];
            ch.Text = chText;
            tm.Text = tmText;
            int errBefore = errors.ToString().Length;
            bool ok = (bool)Call(f, "SaveFormContents", cfg);
            string said = Flat(errors.ToString().Substring(errBefore).Trim());
            int expCh = want ? int.Parse(chText, Inv) : cfgCh0;
            int expTm = want ? int.Parse(tmText, Inv) : cfgTm0;
            bool clean = ok == want && cfg.NumberOfChannels == expCh && cfg.DefaultMeasurementTime == expTm
                         && (want ? said.Length == 0 : said.Length > 0);
            Verdict("каналы «" + chText + "», лимит «" + tmText + "» → " + (want ? "принять" : "отказ словами, конфигурация прежняя"),
                    "принято=" + ok + ", в конфигурации " + cfg.NumberOfChannels.ToString(Inv) + " кан. / "
                    + cfg.DefaultMeasurementTime.ToString(Inv) + " с; сказано: " + (said.Length == 0 ? "—" : said),
                    clean);
            cfg.NumberOfChannels = cfgCh0;
            cfg.DefaultMeasurementTime = cfgTm0;
        }
        ch.Text = ch0;
        tm.Text = tm0;
        try { Call(f, "ResetActiveDeviceConfigDirty"); } catch (MissingMethodException) { }
        f.Dispose();
    }

    // ------------------------------------------------------------------ диалог
    static void Dialog()
    {
        // { текст, способ, принять? } — ожидание ПОСЛЕ правки. «Ввод» = AcceptButton (кнопка),
        // «Enter» = обработчик клавиши поля.
        object[][] cases =
        {
            new object[] { "63", "Ввод", false },
            new object[] { "64", "Ввод", true },
            new object[] { "4096", "Ввод", true },
            new object[] { "65536", "Ввод", true },
            new object[] { "65537", "Ввод", false },
            new object[] { "2000000000", "Ввод", false },
            new object[] { "-5", "Ввод", false },
            new object[] { "abc", "Ввод", false },
            new object[] { "", "Ввод", false },
            new object[] { "2000000000", "Enter", false },
            new object[] { "8192", "Enter", true },
        };
        foreach (object[] c in cases)
        {
            string typed = (string)c[0], how = (string)c[1];
            bool want = (bool)c[2];
            int errBefore = errors.ToString().Length;
            bool openAfter;
            int sent;
            string trouble = RunDialog(typed, how == "Enter", out openAfter, out sent);
            string said = Flat(errors.ToString().Substring(errBefore).Trim());
            if (trouble != null)
            {
                Console.WriteLine("⛔ обвязка: «" + typed + "»: " + trouble);
                rig++;
                continue;
            }
            int expSent = want ? int.Parse(typed, Inv) : 0;
            bool clean = want
                ? (!openAfter && sent == expSent && said.Length == 0)
                : (openAfter && sent == 0 && said.Length > 0);
            Verdict("«" + typed + "» + " + how + " → " + (want ? "окно закрыто, отдано " + expSent.ToString(Inv)
                                                              : "отказ словами, окно открыто, дальше ничего не уходит"),
                    "окно после нажатия " + (openAfter ? "ОТКРЫТО" : "закрыто") + ", SendData=" + sent.ToString(Inv)
                    + "; сказано: " + (said.Length == 0 ? "—" : said),
                    clean);
        }
    }

    /// <summary>
    /// Настоящий модальный показ. Шаг 1 таймера — ввести текст и нажать; шаг 4 — записать,
    /// открыто ли окно, и закрыть его «Закрыть», если открыто. Возвращает null или беду обвязки.
    /// </summary>
    static string RunDialog(string typed, bool viaEnter, out bool openAfter, out int sent)
    {
        bool open = false;
        string trouble = null;
        var f = new ChanNumberChangeDialog();
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.ShowInTaskbar = false;
        TextBox tb = (TextBox)Field(f, "textBox1");
        Button accept = (Button)Field(f, "button1");
        Button close = (Button)Field(f, "button2");
        int step = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 60 };
        timer.Tick += (s, e) =>
        {
            step++;
            try
            {
                if (step == 1)
                {
                    tb.Text = typed;
                    if (viaEnter)
                    {
                        Call(f, "textBox1_KeyDown", tb, new KeyEventArgs(Keys.Enter));
                    }
                    else
                    {
                        accept.PerformClick();
                    }
                }
                else if (step == 4)
                {
                    open = !f.IsDisposed && f.Visible;
                    if (open) close.PerformClick();
                }
                else if (step > 40)
                {
                    trouble = "окно не закрылось за 40 шагов";
                    timer.Stop();
                    f.Close();
                }
            }
            catch (Exception ex)
            {
                trouble = Reason(ex);
                timer.Stop();
                try { f.Close(); } catch { }
            }
        };
        timer.Start();
        f.ShowDialog();
        timer.Stop();
        timer.Dispose();
        openAfter = open;
        sent = f.SendData();
        f.Dispose();
        return trouble;
    }

    // ------------------------------------------------------------------ помощники
    static void Verdict(string what, string got, bool clean)
    {
        if (!clean) defects++;
        Console.WriteLine("{0}\t{1}\t{2}", what, got ?? "(null)", clean ? "ЧИСТО" : "ДЕФЕКТ");
    }

    static void ShowOff(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.ShowInTaskbar = false;
        f.Show();
        Pump(50);
    }

    static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
    }

    static object Field(object o, string name)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo fi = t.GetField(name, Any);
            if (fi != null) return fi.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static object Call(object o, string name, params object[] args)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            foreach (MethodInfo m in t.GetMethods(Any))
            {
                if (m.Name == name && m.GetParameters().Length == args.Length)
                {
                    try { return m.Invoke(o, args); }
                    catch (TargetInvocationException ex) { throw ex.InnerException; }
                }
            }
        }
        throw new MissingMethodException(o.GetType().Name, name);
    }

    static string Reason(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.GetType().Name + ": " + ex.Message;
    }

    static string Flat(string s)
    {
        return (s ?? "").Replace("\r", " ").Replace("\n", " ");
    }
}
