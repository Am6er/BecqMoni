// Проба полосы П293 (14.10.2026): пункты «Нижний/верхний порог от курсора» контекстного
// меню спектра (`DocEnergySpectrum.setLowerThresholdLToolStripMenuItem` /
// `setUpperThresholdHToolStripMenuItem`) у приборов, где поля порога НЕТ.
//
// Решение Amber 14.10.2026 вопросником, дословно: «Гасить пункт у таких приборов
// (Рекомендую)» — «Пункт меню недоступен, если у прибора нет поля порога. Без новых
// строк ресурсов.»
//
// Меню НЕ показывается: для каждого зарегистрированного типа прибора (`DeviceType`)
// документу ставится конфигурация этого типа, курсор — в середину спектра, и
// обработчик открытия меню `contextMenuStrip1_Opening` зовётся ОТРАЖЕНИЕМ; доступность
// пунктов читается с самих пунктов. «Поле порога есть» берётся НЕ из приложения, а из
// той же двери, через которую щелчок ставит порог: `DeviceConfigForm.SetLowerThreshold`
// пишет в `inputDeviceForm.LowerThresholdTextBox` и молча ничего не делает, если там
// null. Проба создаёт страницу прибора (`DeviceType.DeviceConfigFormType`, конструктор
// без окна настроек — поля создаёт `InitializeComponent` в обоих конструкторах) и
// смотрит, вернёт ли она оба поля.
//
// Новый признак приложения (`InputDeviceConfig.HasThresholdFields`) читается тоже
// ОТРАЖЕНИЕМ, поэтому один и тот же exe пробы работает рядом со сборкой ДО правки
// (признака нет — тогда меряется прежнее поведение) и ПОСЛЕ.
//
// Контроли:
//   * курсор вне спектра (CursorX = −1) — пункты обязаны быть погашены у ВСЕХ, в обеих
//     сборках (прежнее условие меню не должно потеряться);
//   * среди зарегистрированных типов обязан быть хоть один С полем и хоть один БЕЗ —
//     иначе сравнивать нечего, замер пуст.
// Дополнительно печатаются (в приговор не входят, кроме требования «без исключения»):
//   * конфигурация незарегистрированного типа (`SerialInputDeviceConfig`, страницы у
//     неё нет — окно настроек ставит пустую `InputDeviceForm`, полей нет) и пустая
//     (`InputDeviceConfig = null`);
//   * документ «без прибора»: свежий документ и документ, чей прибор не найден в списке
//     (`DocumentManager.PrepareDeviceConfig` оставляет конфигурацию по умолчанию).
//
// ЗАПУСК — из каталога проб, где лежат BecquerelMonitor.exe и config\ (собирает
// `build_all.ps1`):   ThresholdMenuProbeP293.exe
//
// Коды: 0 — пункт доступен РОВНО у приборов с полем (так обязана отвечать сборка ПОСЛЕ);
// 1 — пункт доступен у прибора без поля, либо признак разошёлся со страницей прибора
// (так обязана отвечать сборка ДО); 3 — контроль не сработал (замер пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor;

static class ThresholdMenuProbeP293
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    sealed class Case
    {
        public string Name;
        public bool Registered;
        public InputDeviceConfig Config;
        public bool HasField;
        public string FieldNote;
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine(ProbeTargetFramework.Describe());
        string app = typeof(DocumentManager).Assembly.Location;
        Console.WriteLine("приложение: " + app + "  ("
            + File.GetLastWriteTime(app).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Console.WriteLine("окна: " + AppUi.HasWindows);
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
    }

    static int Run()
    {
        DeviceType.InitializeDeviceTypes();
        PropertyInfo flag = typeof(InputDeviceConfig).GetProperty("HasThresholdFields", Any);
        bool after = flag != null;
        Console.WriteLine("InputDeviceConfig.HasThresholdFields: " + (after
            ? "есть (сборка ПОСЛЕ правки)"
            : "нет (сборка ДО правки — меряется прежнее поведение)"));
        Console.WriteLine();

        var cases = new List<Case>();
        foreach (DeviceType dt in DeviceType.DeviceTypeList)
        {
            var c = new Case { Name = dt.Id, Registered = true };
            c.Config = (InputDeviceConfig)Activator.CreateInstance(dt.DeviceConfigType);
            using (var form = (InputDeviceForm)Activator.CreateInstance(dt.DeviceConfigFormType))
            {
                FieldOf(form, c);
            }
            cases.Add(c);
        }
        {
            // Окно настроек для типа, которого нет в реестре, ставит пустую страницу
            // (`DeviceConfigForm.PrepareDeviceForm`, ветка `type == null`).
            var c = new Case { Name = "SerialInputDeviceConfig (нет в реестре)", Registered = false };
            c.Config = new SerialInputDeviceConfig();
            using (var form = new InputDeviceForm())
            {
                FieldOf(form, c);
            }
            cases.Add(c);
        }
        cases.Add(new Case { Name = "InputDeviceConfig = null", Registered = false, Config = null, HasField = false, FieldNote = "конфигурации нет" });

        var doc = new DocEnergySpectrum();
        ResultData rd = doc.ActiveResultData;
        int channels = rd.EnergySpectrum.NumberOfChannels;
        int cursor = channels / 2;
        Console.WriteLine("документ: каналов " + channels.ToString(Inv) + ", курсор — канал " + cursor.ToString(Inv));
        Console.WriteLine();

        int withField = 0, withoutField = 0, bad = 0, controlBad = 0, defect = 0;
        Console.WriteLine("прибор | поле порога (страница прибора) | признак | меню: нижний/верхний | курсор вне спектра | итог");
        foreach (Case c in cases)
        {
            if (c.Registered)
            {
                if (c.HasField) withField++; else withoutField++;
            }
            string flagText = "—";
            bool flagMismatch = false;
            if (after && c.Config != null)
            {
                bool fv = (bool)flag.GetValue(c.Config, null);
                flagText = fv ? "да" : "нет";
                flagMismatch = fv != c.HasField;
            }

            bool lo, up, loOut, upOut;
            Menu(doc, c.Config, cursor, true, out lo, out up);
            Menu(doc, c.Config, cursor, false, out loOut, out upOut);

            bool rule = lo == c.HasField && up == c.HasField;
            bool outOk = !loOut && !upOut;
            string verdict;
            if (!outOk)
            {
                controlBad++;
                verdict = "КОНТРОЛЬ: курсор вне спектра, а пункт доступен";
            }
            else if (flagMismatch)
            {
                bad++;
                verdict = "ПРИЗНАК РАЗОШЁЛСЯ со страницей прибора";
            }
            else if (rule)
            {
                verdict = "верно";
            }
            else
            {
                bad++;
                if ((lo || up) && !c.HasField) defect++;
                verdict = (lo || up) && !c.HasField
                    ? "ДЕФЕКТ: доступен, а поля нет (щелчок откроет настройки и ничего не сделает)"
                    : "НЕВЕРНО: поле есть, а пункт погашен";
            }
            Console.WriteLine(c.Name + " | " + (c.HasField ? "есть" : "нет") + " (" + c.FieldNote + ") | " + flagText
                + " | " + OnOff(lo) + "/" + OnOff(up) + " | " + OnOff(loOut) + "/" + OnOff(upOut) + " | " + verdict);
        }
        Console.WriteLine();

        // Документ «без прибора».
        Console.WriteLine("документ без прибора:");
        {
            var fresh = new DocEnergySpectrum();
            Orphan("  свежий документ (new DocEnergySpectrum)", fresh, fresh.ActiveResultData.DeviceConfig, cursor);
            var lost = new DocEnergySpectrum();
            // Так выглядит спектр, чей прибор не найден в списке: PrepareDeviceConfig не
            // находит GUID ссылки и оставляет ResultData.DeviceConfig по умолчанию.
            lost.ActiveResultData.DeviceConfig = new DeviceConfigInfo();
            Orphan("  прибор файла не найден в списке (DeviceConfig по умолчанию)", lost, lost.ActiveResultData.DeviceConfig, cursor);
        }
        Console.WriteLine();

        Console.WriteLine(string.Format(Inv, "зарегистрированных типов: с полем {0}, без поля {1}", withField, withoutField));
        if (withField == 0 || withoutField == 0 || controlBad > 0)
        {
            Console.WriteLine("ИТОГ: КОНТРОЛЬ НЕ СРАБОТАЛ — замер пуст (код 3)");
            return 3;
        }
        if (bad == 0)
        {
            Console.WriteLine("ИТОГ: пункт доступен РОВНО у приборов с полем порога (код 0)");
            return 0;
        }
        Console.WriteLine(string.Format(Inv, "ИТОГ: расхождений {0}, из них «доступен без поля» {1} (код 1)", bad, defect));
        return 1;
    }

    static void FieldOf(InputDeviceForm form, Case c)
    {
        bool lower = form.LowerThresholdTextBox != null;
        bool upper = form.UpperThresholdTextBox != null;
        c.HasField = lower && upper;
        c.FieldNote = "нижнее " + (lower ? "есть" : "null") + ", верхнее " + (upper ? "есть" : "null")
            + "; страница " + form.GetType().Name;
    }

    static void Orphan(string title, DocEnergySpectrum doc, DeviceConfigInfo dci, int cursor)
    {
        string type = dci == null ? "DeviceConfig = null"
            : (dci.InputDeviceConfig == null ? "InputDeviceConfig = null" : dci.InputDeviceConfig.GetType().Name);
        bool inList = false;
        if (dci != null)
        {
            foreach (DeviceConfigInfo d in DeviceConfigManager.GetInstance().DeviceConfigList)
            {
                if (d.Guid == dci.Guid) { inList = true; break; }
            }
        }
        doc.EnergySpectrumView.CursorX = 100;
        doc.EnergySpectrumView.CursorChannel = cursor;
        Invoke(doc);
        bool lo = Item(doc, "setLowerThresholdLToolStripMenuItem").Enabled;
        bool up = Item(doc, "setUpperThresholdHToolStripMenuItem").Enabled;
        Console.WriteLine(title + ": тип конфигурации " + type + ", GUID «" + (dci == null ? "" : dci.Guid)
            + "» в списке приборов: " + (inList ? "да" : "нет") + "; меню " + OnOff(lo) + "/" + OnOff(up));
    }

    static void Menu(DocEnergySpectrum doc, InputDeviceConfig config, int cursor, bool cursorInside, out bool lower, out bool upper)
    {
        var dci = new DeviceConfigInfo();
        dci.InputDeviceConfig = config;
        doc.ActiveResultData.DeviceConfig = dci;
        doc.EnergySpectrumView.CursorX = cursorInside ? 100 : -1;
        doc.EnergySpectrumView.CursorChannel = cursorInside ? cursor : 0;
        Invoke(doc);
        lower = Item(doc, "setLowerThresholdLToolStripMenuItem").Enabled;
        upper = Item(doc, "setUpperThresholdHToolStripMenuItem").Enabled;
    }

    static void Invoke(DocEnergySpectrum doc)
    {
        MethodInfo opening = typeof(DocEnergySpectrum).GetMethod("contextMenuStrip1_Opening", Any);
        if (opening == null) throw new InvalidOperationException("нет DocEnergySpectrum.contextMenuStrip1_Opening");
        opening.Invoke(doc, new object[] { null, new CancelEventArgs() });
    }

    static ToolStripMenuItem Item(DocEnergySpectrum doc, string field)
    {
        FieldInfo fi = typeof(DocEnergySpectrum).GetField(field, Any);
        if (fi == null) throw new InvalidOperationException("нет поля DocEnergySpectrum." + field);
        return (ToolStripMenuItem)fi.GetValue(doc);
    }

    static string OnOff(bool b)
    {
        return b ? "доступен" : "погашен";
    }
}
