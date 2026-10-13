using BecquerelMonitor;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

/// <summary>
/// ГРАФА «КРИВИЗНА ТРАКТА» В ФОРМЕ ПРИБОРА (`AMBER155` (в), решение Amber
/// 02.10.2026 «Да, после слияния П220 (Рекомендую)», полоса П230).
///
///     tractcurvaturefieldprobe
///
/// Поле `DeviceConfigInfo.TractCurvature` (κ, 1/МэВ, умолчание 0) заведено П220
/// и читается разбором FSA (`FsaAnalyzer.AdoptDevice`), но до П230 задать его
/// можно было только правкой XML. Что мерится, пятью плечами.
///
/// 1. XML. Конфигурация БЕЗ элемента `TractCurvature` читается и даёт 0;
///    значение переживает круг записи-чтения побитово и глубокую копию;
///    в файле число записано точкой.
/// 2. ФОРМА, КРУГ. `LoadFormContents` показывает κ конфигурации в графе тем же
///    числом; правка графы уходит `SaveFormContents` в конфигурацию; пустая
///    графа — умолчание 0.
/// 3. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — ОТКАЗ. «abc», «NaN», «Infinity», «0,5» при
///    инвариантной культуре потока — `SaveFormContents` отказывает, и
///    конфигурация НЕ тронута (ни κ, ни имя: разбор стоит до первой записи,
///    `A6`). Плечо «0,5» при русской культуре печатается отдельно: по `A244`
///    поле читается терпимо, как у соседних графок, и это НЕ отказ.
/// 4. ПОДСКАЗКА. У графы и у подписи есть подсказка в обеих культурах, и
///    английская с русской различаются (перевод доехал).
/// 5. ПОДПИСЬ ВЛЕЗАЕТ до графы (`A127`), единица не наезжает на графу.
///    Контроль — удвоенный текст, он ОБЯЗАН не влезть.
///
/// ⚠ Окон проба не открывает: форма строится, но не показывается.
/// Сборка — общая, `tools/effmaker/probes/build_all.ps1`.
/// Ожидание: «СОШЛОСЬ». Коды возврата: 0 — сошлось; 1 — не сошлось.
/// </summary>
static class TractCurvatureFieldProbe
{
    const double Kappa = 0.0026;

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Application.SetCompatibleTextRenderingDefault(false);

        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();

        int bad = 0;
        bad += Xml();
        bad += FormRound();
        bad += Refusals();
        bad += Hints();
        bad += LabelFits();

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: графа «Кривизна тракта» читает и пишет DeviceConfigInfo.TractCurvature"
            : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. XML
    // ------------------------------------------------------------------

    static int Xml()
    {
        Console.WriteLine("=== 1. XML: старый файл без элемента, круг, копия ===");
        int bad = 0;
        var serializer = new XmlSerializer(typeof(DeviceConfigInfo));

        var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба" };
        device.TractCurvature = Kappa;
        string full;
        using (var writer = new StringWriter(CultureInfo.InvariantCulture))
        {
            serializer.Serialize(writer, device);
            full = writer.ToString();
        }

        bool dot = full.Contains("<TractCurvature>0.0026</TractCurvature>");
        Console.WriteLine("  в записи <TractCurvature>0.0026</TractCurvature>: {0}", dot ? "есть" : "НЕТ");
        bad += dot ? 0 : 1;

        // Старый файл — СНЯТИЕМ элемента из настоящей записи, не рукописью.
        string old = Strip(full, "TractCurvature");
        DeviceConfigInfo back;
        try
        {
            using (var reader = new StringReader(old))
            {
                back = (DeviceConfigInfo)serializer.Deserialize(reader);
            }
        }
        catch (Exception error)
        {
            Console.WriteLine("  ⛔ ОТКАЗ ЧТЕНИЯ старого файла: {0}: {1}", error.GetType().Name, error.Message);
            return bad + 1;
        }

        bool zero = back.TractCurvature == 0.0 && back.Name == "проба";
        Console.WriteLine("  без элемента: κ = {0}, имя «{1}» — {2}",
                          back.TractCurvature.ToString("R", CultureInfo.InvariantCulture), back.Name,
                          zero ? "умолчание 0, прочие поля на месте" : "РАСХОЖДЕНИЕ");
        bad += zero ? 0 : 1;

        DeviceConfigInfo whole;
        using (var reader = new StringReader(full))
        {
            whole = (DeviceConfigInfo)serializer.Deserialize(reader);
        }

        bool same = whole.TractCurvature == Kappa;
        Console.WriteLine("  круг с элементом: κ = {0} — {1}",
                          whole.TractCurvature.ToString("R", CultureInfo.InvariantCulture), same ? "побитово" : "РАСХОЖДЕНИЕ");
        bad += same ? 0 : 1;

        DeviceConfigInfo copy = device.Clone();
        bool copied = copy.TractCurvature == Kappa;
        Console.WriteLine("  глубокая копия : κ = {0} — {1}",
                          copy.TractCurvature.ToString("R", CultureInfo.InvariantCulture), copied ? "сошлось" : "РАСХОЖДЕНИЕ");
        bad += copied ? 0 : 1;
        return bad;
    }

    static string Strip(string xml, string element)
    {
        int open = xml.IndexOf("<" + element + ">", StringComparison.Ordinal);
        if (open < 0)
        {
            return xml;
        }

        int close = xml.IndexOf("</" + element + ">", open, StringComparison.Ordinal);
        if (close < 0)
        {
            return xml;
        }

        close += element.Length + 3;
        while (close < xml.Length && (xml[close] == '\r' || xml[close] == '\n'))
        {
            close++;
        }

        while (open > 0 && (xml[open - 1] == ' ' || xml[open - 1] == '\t'))
        {
            open--;
        }

        return xml.Substring(0, open) + xml.Substring(close);
    }

    // ------------------------------------------------------------------
    // 2. Форма: показ и запись
    // ------------------------------------------------------------------

    static int FormRound()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. форма: показ -> правка -> запись ===");
        int bad = 0;

        using (var form = new DeviceConfigForm())
        {
            var box = (TextBox)Field(form, "tractCurvatureTextBox");
            var label = (Label)Field(form, "tractCurvatureLabel");
            var crystal = (ComboBox)Field(form, "crystalMaterialCombo");
            Console.WriteLine("  графа на вкладке «{0}», подпись «{1}», тип {2}",
                              label.Parent == null ? "НЕТ РОДИТЕЛЯ" : label.Parent.Text, label.Text, box.GetType().Name);
            bad += label.Parent != null && ReferenceEquals(label.Parent, box.Parent)
                   && ReferenceEquals(box.Parent, crystal.Parent) && box is DoubleTextBox ? 0 : 1;

            var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба" };
            device.TractCurvature = Kappa;
            Call(form, "LoadFormContents", device);
            Console.WriteLine("  показ κ = 0.0026      : графа «{0}» — {1}", box.Text,
                              box.Text == "0.0026" ? "сошлось" : "РАСХОЖДЕНИЕ");
            bad += box.Text == "0.0026" ? 0 : 1;

            bad += Save(form, box, device, "0.0031", true, 0.0031);
            bad += Save(form, box, device, "-0.033", true, -0.033);
            bad += Save(form, box, device, "", true, 0.0);
            bad += Save(form, box, device, " 0.0023 ", true, 0.0023);

            var fresh = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "новый" };
            Call(form, "LoadFormContents", fresh);
            Console.WriteLine("  новый прибор          : графа «{0}» — {1}", box.Text,
                              box.Text == "0" ? "умолчание 0" : "РАСХОЖДЕНИЕ");
            bad += box.Text == "0" ? 0 : 1;
        }

        return bad;
    }

    /// <summary>Ввести текст в графу и сохранить; сверить исход и κ конфигурации.</summary>
    static int Save(DeviceConfigForm form, TextBox box, DeviceConfigInfo device, string text,
                    bool expectOk, double expectKappa)
    {
        string nameBefore = device.Name;
        box.Text = text;
        bool ok = (bool)Call(form, "SaveFormContents", device);
        bool kappaOk = device.TractCurvature == expectKappa;
        bool nameOk = device.Name == nameBefore;
        bool pass = ok == expectOk && kappaOk && nameOk;
        Console.WriteLine("  ввод {0,-12}: сохранение {1}, κ = {2}{3} — {4}",
                          "«" + text + "»", ok ? "принято" : "ОТКАЗ",
                          device.TractCurvature.ToString("R", CultureInfo.InvariantCulture),
                          nameOk ? "" : ", ИМЯ ИЗМЕНЕНО",
                          pass ? (expectOk ? "сошлось" : "отказ, конфигурация не тронута — сошлось") : "РАСХОЖДЕНИЕ");
        return pass ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 3. Положительный контроль: отказ
    // ------------------------------------------------------------------

    static int Refusals()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. КОНТРОЛЬ: нечисло — отказ, конфигурация не тронута ===");
        int bad = 0;

        using (var form = new DeviceConfigForm())
        {
            var box = (TextBox)Field(form, "tractCurvatureTextBox");
            var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба" };
            device.TractCurvature = Kappa;
            Call(form, "LoadFormContents", device);

            // Имя в графе имени меняется, а отказ обязан не донести его до
            // конфигурации: разбор κ стоит ДО первой записи.
            var name = (TextBox)Field(form, "textBox1");
            name.Text = "переименовано";

            bad += Save(form, box, device, "abc", false, Kappa);
            bad += Save(form, box, device, "NaN", false, Kappa);
            bad += Save(form, box, device, "Infinity", false, Kappa);
            bad += Save(form, box, device, "0,5", false, Kappa);
            bad += Save(form, box, device, "1 234", false, Kappa);

            // `A244`: при русской культуре системы запятая — законная дробь, как
            // у шага канала. Печатается, а не судится: это решение, не дефект.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            name.Text = "проба";
            box.Text = "0,5";
            bool ok = (bool)Call(form, "SaveFormContents", device);
            Console.WriteLine("  (справка) «0,5» при ru-RU: сохранение {0}, κ = {1} — терпимое чтение A244, как у соседних графок",
                              ok ? "принято" : "ОТКАЗ", device.TractCurvature.ToString("R", CultureInfo.InvariantCulture));
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        return bad;
    }

    // ------------------------------------------------------------------
    // 4. Подсказка в обеих культурах
    // ------------------------------------------------------------------

    static int Hints()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. подсказка графы и подписи в обеих культурах ===");
        int bad = 0;
        string en = HintIn("en", ref bad);
        string ru = HintIn("ru", ref bad);
        bool differ = en.Length > 0 && ru.Length > 0 && en != ru;
        Console.WriteLine("  английская и русская различаются: {0}", differ ? "да" : "НЕТ — перевод не доехал");
        bad += differ ? 0 : 1;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        return bad;
    }

    static string HintIn(string culture, ref int bad)
    {
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        using (var form = new DeviceConfigForm())
        {
            var hints = (ToolTip)Field(form, "hints");
            var box = (Control)Field(form, "tractCurvatureTextBox");
            var label = (Label)Field(form, "tractCurvatureLabel");
            var unit = (Label)Field(form, "tractCurvatureUnitLabel");
            string onBox = hints.GetToolTip(box) ?? "";
            string onLabel = hints.GetToolTip(label) ?? "";
            bool ok = onBox.Length > 0 && onBox == onLabel && onBox.Contains("κ") && onBox.Contains("1/");
            string head = onBox.Split('\n')[0].TrimEnd('\r');
            Console.WriteLine("  {0}: подпись «{1}», единица «{2}», подсказка {3} знаков: «{4}…» — {5}",
                              culture, label.Text, unit.Text, onBox.Length, head, ok ? "есть" : "НЕТ");
            bad += ok ? 0 : 1;
            return onBox;
        }
    }

    // ------------------------------------------------------------------
    // 5. Подпись влезает (`A127`)
    // ------------------------------------------------------------------

    static int LabelFits()
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. подпись влезает до графы, единица не наезжает ===");
        var view = new ComponentResourceManager(typeof(DeviceConfigForm));
        var font = (Font)view.GetObject("$this.Font");
        if (font == null)
        {
            Console.WriteLine("  ⛔ нет $this.Font в ресурсах формы — мерить нечем");
            return 1;
        }

        var labelAt = (Point)view.GetObject("tractCurvatureLabel.Location");
        var boxAt = (Point)view.GetObject("tractCurvatureTextBox.Location");
        var boxSize = (Size)view.GetObject("tractCurvatureTextBox.Size");
        var unitAt = (Point)view.GetObject("tractCurvatureUnitLabel.Location");
        var notesAt = (Point)view.GetObject("textBox19.Location");
        int room = boxAt.X - labelAt.X;
        Console.WriteLine("  подпись с {0}, графа {1}..{2}, единица с {3}, заметки с y={4} (графа до y={5})",
                          labelAt.X, boxAt.X, boxAt.X + boxSize.Width, unitAt.X, notesAt.Y, boxAt.Y + boxSize.Height);

        int bad = 0;
        bool clear = unitAt.X >= boxAt.X + boxSize.Width && notesAt.Y > boxAt.Y + boxSize.Height;
        Console.WriteLine("  единица правее графы, заметки ниже: {0}", clear ? "да" : "НАЕЗД");
        bad += clear ? 0 : 1;
        bad += Fits(view, font, "en", room, false);
        bad += Fits(view, font, "ru", room, false);
        bad += Fits(view, font, "ru", room, true);
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        return bad;
    }

    static int Fits(ComponentResourceManager view, Font font, string culture, int room, bool control)
    {
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var label = new Label { AutoSize = true, Font = font };
        view.ApplyResources(label, "tractCurvatureLabel");
        string text = label.Text ?? "";
        if (control)
        {
            text = text + " " + text;
        }

        Size need = TextRenderer.MeasureText(text, font);
        int spare = room - need.Width;
        Console.WriteLine("  {0,-3}{1} «{2}» — нужно {3} пкс, запас {4}",
                          culture, control ? " КОНТРОЛЬ" : "        ", text, need.Width, spare);
        if (control)
        {
            if (spare >= 0)
            {
                Console.WriteLine("    ⛔ КОНТРОЛЬ ПРОВАЛЕН: удвоенный текст «влез», замер не мерит");
                return 1;
            }

            Console.WriteLine("    контроль отказал, как и должен — замер работает");
            return 0;
        }

        if (spare < 0)
        {
            Console.WriteLine("    ⛔ НЕ ВЛЕЗАЕТ: подпись наедет на графу");
            return 1;
        }

        return 0;
    }

    // ------------------------------------------------------------------

    static object Field(object target, string name)
    {
        Type type = target.GetType();
        while (type != null)
        {
            FieldInfo field = type.GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return field.GetValue(target);
            }

            type = type.BaseType;
        }

        throw new InvalidOperationException("нет поля " + name);
    }

    static object Call(object target, string name, params object[] args)
    {
        Type type = target.GetType();
        while (type != null)
        {
            MethodInfo method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null)
            {
                return method.Invoke(target, args);
            }

            type = type.BaseType;
        }

        throw new InvalidOperationException("нет метода " + name);
    }
}
