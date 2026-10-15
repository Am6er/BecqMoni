// Проба полосы П320 (15.10.2026): НЕЧИСЛО В ЧИСЛОВОМ ПОЛЕ — «ВЕРНУТЬ ПРЕЖНЕЕ».
//
// Решение Amber вопросником 15.10.2026, дословно: «Вернуть прежнее (Рекомендую)» — на вопрос
// «Нечисло в числовом поле: сейчас поле при уходе фокуса держит фокус окном ошибки; на панели
// „Управление измерением“ так запирается переключение документов» (находки П312 Н14/Н15).
// Общие поля `DoubleTextBox` и `IntegerTextBox` при уходе фокуса с нечислом прежде ставили
// `e.Cancel = true` и поднимали окно «Введите число» — фокус оставался в поле, и вместе с ним
// запиралось всё окно. Теперь поле МОЛЧА возвращает прежнее значение и фокус отпускает.
// ⛔ Запись по-прежнему не пропускает нечисло: перед записью окно прибора зовёт `FieldsValid`
// (П318), и поле с нечислом к записи уже держит прежнее число.
//
// Проба на НАСТОЯЩИХ контролах в настоящей (спрятанной за край экрана) форме, фокус двигается
// так же, как щелчком: `ActiveControl` = кнопка с `CausesValidation`. Сообщения полей без окон
// идут дверью `AppUi.Report` в поток ошибок — проба его перехватывает и считает.
//
//   L1  поле со значением, поставленным кодом (2.5 / 25), человек набирает «1» → «12» → «12x»,
//       уходит: фокус ушёл? в поле — прежнее «2.5»/«25» (не промежуточное «12»)? окон 0?
//   L2  человек ввёл годное 3.75 / 37 и ушёл, затем «abc» и ушёл — в поле «3.75» / «37»;
//   L3  пустое поле → «0» (как и прежде, обе сборки);
//   L4  «0,5» под ru-RU в поле дробей — принято (0.5), фокус ушёл (обе сборки);
//   L5  поле со стрелками (`InvariantNumericUpDown`) с «12x» — значение прежнее, фокус ушёл
//       (обёртка так вела себя и прежде — контроль);
//   D   окно прибора: «шаг канала» и «число каналов» = abc без проверки (как после «Закрыть»
//       у которой проверка снята), затем `FieldsValid` и `SaveFormContents`: нечисло в
//       конфигурацию НЕ попадает ни в одной сборке (ДО — отказ записи, ПОСЛЕ — прежнее число).
//
// Коды: 0 — решение выполнено во всех плечах (сборка ПОСЛЕ); 1 — поле держит фокус, ругается
// окном или не возвращает прежнее (сборка ДО обязана дать 1 на L1/L2); 4 — НЕЧИСЛО УШЛО В
// ЗАПИСЬ (провал в любой сборке); 3 — обвязка слепа; 5 — обрыв.
//
//   P320FieldRevertProbe.exe    (из каталога, где лежит BecquerelMonitor.exe)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using XPTable.Models;

static class P320FieldRevertProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly StringWriter errors = new StringWriter();
    static int fails, rig, breaches, verdicts;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Thread watchdog = new Thread(() =>
        {
            Thread.Sleep(180000);
            Console.WriteLine("⛔ ОБРЫВ: проба не уложилась в 180 с (окно?)");
            Console.Out.Flush();
            Environment.Exit(5);
        });
        watchdog.IsBackground = true;
        watchdog.Start();

        string exe = typeof(DoubleTextBox).Assembly.Location;
        Console.WriteLine("приложение: " + exe + "  (" + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Thread.CurrentThread.CurrentCulture = Inv;
        Application.EnableVisualStyles();
        string cfgDir = Path.Combine(Path.GetDirectoryName(exe), "config");
        string amberDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BecqMoni", "config");
        string cfgBefore = HashTree(cfgDir), amberBefore = HashTree(amberDir);
        Console.WriteLine("SETUP\tотпечаток config пробы " + cfgBefore + "; живого конфига " + amberBefore);

        TextWriter stdErr = Console.Error;
        Console.SetError(errors);
        try
        {
            Console.WriteLine();
            Console.WriteLine("=== L. общие поля в форме");
            FieldCases("DoubleTextBox", () => new DoubleTextBox(), "2.5", "3.75");
            FieldCases("IntegerTextBox", () => new IntegerTextBox(), "25", "37");
            UpDownCase();

            Console.WriteLine();
            Console.WriteLine("=== D. окно прибора: нечисло к записи");
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            DeviceCase("doubleTextBox6", "шаг канала", cfg => cfg.ChannelPitch.ToString("R", Inv));
            DeviceCase("integerTextBox1", "число каналов", cfg => cfg.NumberOfChannels.ToString(Inv));
        }
        catch (Exception ex)
        {
            Console.SetError(stdErr);
            Console.WriteLine("⛔ ОБРЫВ: " + ex);
            return 5;
        }
        Console.SetError(stdErr);

        string cfgAfter = HashTree(cfgDir), amberAfter = HashTree(amberDir);
        Console.WriteLine();
        Console.WriteLine("ИТОГ\tотпечаток config пробы " + cfgAfter + (cfgAfter == cfgBefore ? " (тот же)" : " ⛔ ИЗМЕНИЛСЯ")
                          + "; живого конфига " + amberAfter + (amberAfter == amberBefore ? " (тот же)" : " ⛔ ИЗМЕНИЛСЯ"));
        if (amberAfter != amberBefore) rig++;
        Console.WriteLine(string.Format(Inv, "вердиктов {0}; не выполнено решение {1}; нечисло в записи {2}; сбоев обвязки {3}",
                                        verdicts, fails, breaches, rig));
        if (rig > 0)
        {
            Console.WriteLine("⛔ ИТОГ: обвязка слепа (код 3)");
            return 3;
        }
        if (breaches > 0)
        {
            Console.WriteLine("⛔ ИТОГ: НЕЧИСЛО УШЛО В ЗАПИСЬ (код 4)");
            return 4;
        }
        if (fails > 0)
        {
            Console.WriteLine("⛔ ИТОГ: поле держит фокус / ругается окном / не возвращает прежнее (код 1; так обязана отвечать сборка ДО П320)");
            return 1;
        }
        Console.WriteLine("ИТОГ: нечисло при уходе молча заменено прежним, фокус отпущен, в запись не уходит (код 0)");
        return 0;
    }

    // ---- обвязка --------------------------------------------------------------

    static void Verdict(string id, string what, string got, bool ok)
    {
        verdicts++;
        if (!ok) fails++;
        Console.WriteLine(id + "\t" + what + "\t" + got + "\t" + (ok ? "выполнено" : "⛔ НЕ ВЫПОЛНЕНО"));
    }

    static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
    }

    static void ShowOff(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.ShowInTaskbar = false;
        f.Show();
        Pump(50);
    }

    static bool ContainsActive(Form f, Control box)
    {
        ContainerControl c = f;
        while (c != null)
        {
            if (ReferenceEquals(c.ActiveControl, box)) return true;
            c = c.ActiveControl as ContainerControl;
        }
        return false;
    }

    static bool Enter(Form f, Control box)
    {
        TabPage page = Parent<TabPage>(box);
        if (page != null) ((TabControl)page.Parent).SelectedTab = page;
        Pump(20);
        f.ActiveControl = box;
        Pump(20);
        bool ok = box.Focused || ContainsActive(f, box);
        if (!ok)
        {
            Console.WriteLine("⛔ обвязка: поле " + box.Name + " не стало активным");
            rig++;
        }
        return ok;
    }

    /// <summary>Уход с поля «щелчком» по кнопке с проверкой: true — фокус ушёл.</summary>
    static bool Leave(Form f, Control box, Button to)
    {
        f.ActiveControl = to;
        Pump(30);
        return !ContainsActive(f, box) && !box.Focused;
    }

    /// <summary>
    /// НАБОР ЧЕЛОВЕКА, а не присваивание `Text`: первый «удар» заменяет всё выделенное, следующие
    /// вставляются у каретки (`EM_REPLACESEL`, как у клавиатуры и Ctrl+V). Свойство `Text` в
    /// пробе — это загрузка формы кодом, и поле вправе считать его «прежним значением».
    /// </summary>
    static void Typed(TextBox box, params string[] strokes)
    {
        box.SelectAll();
        foreach (string s in strokes)
        {
            box.Paste(s);
        }
        Pump(10);
    }

    static T Parent<T>(Control c) where T : Control
    {
        for (Control p = c.Parent; p != null; p = p.Parent)
        {
            if (p is T) return (T)p;
        }
        return null;
    }

    static int Reports()
    {
        string all = errors.ToString();
        return Count(all, BecquerelMonitor.Properties.Resources.ERRInputNumber)
               + Count(all, BecquerelMonitor.Properties.Resources.ERRInputInteger);
    }

    static int Count(string all, string what)
    {
        if (string.IsNullOrEmpty(what)) return 0;
        int n = 0;
        for (int i = all.IndexOf(what, StringComparison.Ordinal); i >= 0; i = all.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    static string HashTree(string dir)
    {
        if (!Directory.Exists(dir)) return "нет каталога";
        var files = new List<string>(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
        files.Sort(StringComparer.Ordinal);
        using (SHA256 sha = SHA256.Create())
        {
            var sb = new StringBuilder();
            foreach (string file in files)
            {
                byte[] h;
                try { using (FileStream s = File.OpenRead(file)) h = sha.ComputeHash(s); }
                catch (IOException) { h = new byte[0]; }
                sb.Append(file.Substring(dir.Length)).Append(':').Append(BitConverter.ToString(h)).Append('\n');
            }
            byte[] all = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return files.Count.ToString(Inv) + " файлов, " + BitConverter.ToString(all, 0, 6).Replace("-", "");
        }
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

    static object Call(object o, string name, params object[] a)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            MethodInfo mi = t.GetMethod(name, Any);
            if (mi != null)
            {
                try { return mi.Invoke(o, a); }
                catch (TargetInvocationException ex) { throw ex.InnerException; }
            }
        }
        throw new MissingMethodException(o.GetType().Name, name);
    }

    // ---- L: общие поля ----------------------------------------------------------

    sealed class Rig : IDisposable
    {
        public Form Form;
        public TextBox Box;
        public Button Button;

        public Rig(Func<TextBox> make)
        {
            this.Form = new Form { Text = "P320", ClientSize = new Size(300, 120) };
            this.Box = make();
            this.Box.Name = "probeField";
            this.Box.Location = new Point(10, 10);
            this.Button = new Button { Name = "probeButton", Text = "OK", Location = new Point(10, 50) };
            this.Form.Controls.Add(this.Box);
            this.Form.Controls.Add(this.Button);
            ShowOff(this.Form);
        }

        public void Dispose()
        {
            this.Form.AutoValidate = AutoValidate.Disable;
            this.Form.Dispose();
        }
    }

    static void FieldCases(string kind, Func<TextBox> make, string loaded, string typedGood)
    {
        // L1: значение поставлено кодом, человек набирает с промежуточными годными.
        using (Rig r = new Rig(make))
        {
            r.Box.Text = loaded;
            if (Enter(r.Form, r.Box))
            {
                int before = Reports();
                Typed(r.Box, "1", "2", "x");
                bool left = Leave(r.Form, r.Box, r.Button);
                int reports = Reports() - before;
                Verdict("L1", kind + ": загружено «" + loaded + "», набрано «1»→«12»→«12x», уход",
                        "фокус " + (left ? "ушёл" : "ДЕРЖИТ") + ", в поле «" + r.Box.Text + "», окон " + reports.ToString(Inv),
                        left && r.Box.Text == loaded && reports == 0);
            }
        }

        // L2: человек ввёл годное и ушёл, затем нечисло и ушёл.
        using (Rig r = new Rig(make))
        {
            r.Box.Text = loaded;
            if (Enter(r.Form, r.Box))
            {
                Typed(r.Box, typedGood);
                bool left1 = Leave(r.Form, r.Box, r.Button);
                int before = Reports();
                bool left2 = false;
                if (Enter(r.Form, r.Box))
                {
                    Typed(r.Box, "abc");
                    left2 = Leave(r.Form, r.Box, r.Button);
                }
                int reports = Reports() - before;
                Verdict("L2", kind + ": набрано «" + typedGood + "», уход; затем «abc», уход",
                        "первый уход " + (left1 ? "да" : "НЕТ") + "; второй — фокус " + (left2 ? "ушёл" : "ДЕРЖИТ")
                        + ", в поле «" + r.Box.Text + "», окон " + reports.ToString(Inv),
                        left1 && left2 && r.Box.Text == typedGood && reports == 0);
            }
        }

        // L3: пустое поле — «0», как и прежде.
        using (Rig r = new Rig(make))
        {
            r.Box.Text = loaded;
            if (Enter(r.Form, r.Box))
            {
                int before = Reports();
                r.Box.SelectAll();
                r.Box.Paste("");
                bool left = Leave(r.Form, r.Box, r.Button);
                int reports = Reports() - before;
                Verdict("L3", kind + ": стёрто до пустого, уход (контроль — прежнее поведение «0»)",
                        "фокус " + (left ? "ушёл" : "ДЕРЖИТ") + ", в поле «" + r.Box.Text + "», окон " + reports.ToString(Inv),
                        left && r.Box.Text == "0" && reports == 0);
            }
        }

        // L4: русская запятая в поле дробей принимается (контроль обеих сборок).
        if (kind == "DoubleTextBox")
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            try
            {
                using (Rig r = new Rig(make))
                {
                    r.Box.Text = loaded;
                    if (Enter(r.Form, r.Box))
                    {
                        int before = Reports();
                        Typed(r.Box, "0,5");
                        bool left = Leave(r.Form, r.Box, r.Button);
                        int reports = Reports() - before;
                        double v = ((DoubleTextBox)r.Box).GetValue();
                        Verdict("L4", kind + ": ru-RU, набрано «0,5», уход (контроль — принимается)",
                                "фокус " + (left ? "ушёл" : "ДЕРЖИТ") + ", в поле «" + r.Box.Text + "» = " + v.ToString("R", Inv)
                                + ", окон " + reports.ToString(Inv),
                                left && v == 0.5 && reports == 0);
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }

    static void UpDownCase()
    {
        Form f = new Form { Text = "P320", ClientSize = new Size(300, 120) };
        InvariantNumericUpDown nud = new InvariantNumericUpDown
        {
            Name = "probeUpDown", Minimum = -1000000m, Maximum = 1000000m, DecimalPlaces = 3, Location = new Point(10, 10)
        };
        Button b = new Button { Name = "probeButton", Text = "OK", Location = new Point(10, 50) };
        f.Controls.Add(nud);
        f.Controls.Add(b);
        ShowOff(f);
        try
        {
            nud.Value = 2.5m;
            if (Enter(f, nud))
            {
                int before = Reports();
                nud.Text = "12x";
                bool left = Leave(f, nud, b);
                int reports = Reports() - before;
                Verdict("L5", "InvariantNumericUpDown: значение 2.5, набрано «12x», уход (контроль — обёртка и прежде так)",
                        "фокус " + (left ? "ушёл" : "ДЕРЖИТ") + ", значение " + nud.Value.ToString(Inv) + ", в поле «" + nud.Text
                        + "», окон " + reports.ToString(Inv),
                        left && nud.Value == 2.5m && reports == 0);
            }
        }
        finally
        {
            f.AutoValidate = AutoValidate.Disable;
            f.Dispose();
        }
    }

    // ---- D: окно прибора ------------------------------------------------------

    static void DeviceCase(string fieldName, string title, Func<DeviceConfigInfo, string> read)
    {
        DeviceConfigForm f = new DeviceConfigForm();
        ShowOff(f);
        try
        {
            TableModel list = (TableModel)Field(f, "tableModel1");
            if (list.Rows.Count == 0)
            {
                Console.WriteLine("⛔ обвязка: в каталоге проб нет конфигураций прибора");
                rig++;
                return;
            }
            list.Selections.SelectCell(0, 0);
            Pump(100);
            DeviceConfigInfo cfg = (DeviceConfigInfo)Field(f, "activeDeviceConfig");
            if (cfg == null)
            {
                Console.WriteLine("⛔ обвязка: конфигурация не стала активной");
                rig++;
                return;
            }
            TextBox box = (TextBox)Field(f, fieldName);
            string orig = box.Text;
            string stored = read(cfg);
            Console.WriteLine("SETUP\t" + title + ": в поле «" + orig + "», в конфигурации " + stored);

            int before = Reports();
            Typed(box, "abc");            // набрано и ушли «Закрыть» — проверки не было
            bool valid = (bool)Call(f, "FieldsValid");
            int reports = Reports() - before;
            string shown = box.Text;
            bool saved = false;
            string written = "—";
            if (valid)
            {
                saved = (bool)Call(f, "SaveFormContents", cfg);
                written = read(cfg);
            }
            bool leak = valid && saved && written != stored;
            if (leak) breaches++;
            Console.WriteLine("D\t" + title + " = abc → FieldsValid=" + valid + ", окон " + reports.ToString(Inv)
                              + ", в поле «" + shown + "»; запись " + (valid ? (saved ? "выполнена, в конфигурации " + written : "отказала") : "не допущена")
                              + "\t" + (leak ? "⛔ НЕЧИСЛО/ДРУГОЕ ЧИСЛО УШЛО В ЗАПИСЬ" : "нечисло в запись не ушло"));
            Verdict("D", title + ": «abc» к записи — без окна, поле вернуло прежнее, записано прежнее",
                    "FieldsValid=" + valid + ", окон " + reports.ToString(Inv) + ", в поле «" + shown + "», записано " + written,
                    valid && reports == 0 && shown == orig && saved && written == stored);
        }
        finally
        {
            f.AutoValidate = AutoValidate.Disable;
            f.Dispose();
        }
    }
}
