// Проба полосы П320 (15.10.2026): КЛАСС «0,5 ЧИТАЕТСЯ КАК 5» — разбор чисел, НАБРАННЫХ ЧЕЛОВЕКОМ.
//
// Находка П318 (В1): порог звукового входа «0,5» на русской системе записывался как 5.
// `AudioInputDeviceForm.SaveFormContents` разбирал поля `double.Parse(text, InvariantCulture)`:
// умолчание стиля этой перегрузки — `Float | AllowThousands`, а запятая инварианта —
// разделитель РАЗРЯДОВ, и «0,5» превращалось в 5, «1,234» — в 1234, молча. Поле
// (`DoubleTextBox`, `UserNumber.TryParseDouble`) тем временем принимало «0,5» как 0.5.
//
// Проба зовёт НАСТОЯЩИЕ методы приложения (отражением, без окна) по каждому месту, где
// число набирает человек, и печатает «ввод → текст в поле → значение» под тремя культурами
// (ru-RU, en-US, de-DE — без пользовательских настроек) и под культурой системы как есть:
//
//   A  `AudioInputDeviceForm.SaveFormContents` — пять полей: doubleTextBox1..4 (пороги) и
//      maskedTextBox2 (порог корреляции, проценты, маска «999», значение /100);
//   B  `UserNumber.TryParseDouble/TryParseDecimal/TryParseInt` — общий разбор приложения;
//   C  `InvariantNumericUpDown.ValidateEditText` — поле со стрелками;
//   D  XPTable `DoubleCellEditor.ParseEditText` / `NumberCellEditor.ParseEditText` — ячейки;
//   E  `DeviceConfigForm.fromStringtoDouble` — СНЯТО (`AMBER280`): поля коэффициентов окна
//      прибора стали только показом, разбора у них нет;
//   F  `GeometryMaterialEditorForm.TryReadNumber`, `GeometryEditorPanel.TryGet` — геометрия.
//
// МЕРКА. «Честное прочтение» текста — запятая и точка обе как разделитель ДРОБНОЙ части
// (`text.Replace(',', '.')`, `NumberStyles.Float`, инвариант). Место ДЕФЕКТНО, если принятое
// им конечное значение не равно честному прочтению того, что стоит в поле (или честного
// прочтения нет вовсе). Отказ дефектом НЕ считается: «0,5» на английской системе вправе
// не приниматься. Принятое «не-число» (NaN, ∞) считается отдельно — это другой класс.
//
// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ мерки — в самой пробе: прежнее выражение
// `double.Parse("0,5", InvariantCulture)` обязано дать 5 и быть признано дефектом; иначе
// мерка слепа (код 3). Контроль обвязки: ввод «2» обязан пройти в каждом поле формы A как 2
// (в процентном — как 0.02).
//
// Коды: 0 — дефектов класса нет ни в одном месте; 1 — есть (так обязана отвечать сборка
// ДО П320 в плече A); 3 — мерка или обвязка слепы; 5 — обрыв.
//
//   P320UserNumberProbe.exe    (из каталога, где лежит BecquerelMonitor.exe)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using XPTable.Editors;
using WinMM;

static class P320UserNumberProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    static readonly string[] Inputs = { "2", "0.5", "0,5", "1,5", "1,234", "1.234", "abc", "NaN" };

    static int defects, nonNumbers, rig, lines;
    static readonly Dictionary<string, int> defectsByPlace = new Dictionary<string, int>();

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        try
        {
            CultureInfo system = Thread.CurrentThread.CurrentCulture;
            Console.WriteLine("приложение: " + typeof(UserNumber).Assembly.Location);
            Console.WriteLine("культура системы: " + system.Name + ", дробная «" + system.NumberFormat.NumberDecimalSeparator
                              + "», разряды «" + Escape(system.NumberFormat.NumberGroupSeparator) + "»");

            // Положительный контроль мерки: прежнее выражение формы звукового входа.
            double old = double.Parse("0,5", Inv);
            string selfVerdict = Classify("0,5", true, old);
            Console.WriteLine("КОНТРОЛЬ\tdouble.Parse(\"0,5\", InvariantCulture) = " + old.ToString("R", Inv) + " → мерка: " + selfVerdict);
            if (selfVerdict != "ДЕФЕКТ")
            {
                Console.WriteLine("⛔ ИТОГ: мерка не видит «0,5 → 5» — замер слеп (код 3)");
                return 3;
            }
            defects = 0;
            defectsByPlace.Clear();

            var cultures = new List<KeyValuePair<string, CultureInfo>>
            {
                new KeyValuePair<string, CultureInfo>("ru-RU", CultureInfo.GetCultureInfo("ru-RU")),
                new KeyValuePair<string, CultureInfo>("en-US", CultureInfo.GetCultureInfo("en-US")),
                new KeyValuePair<string, CultureInfo>("de-DE", CultureInfo.GetCultureInfo("de-DE")),
                new KeyValuePair<string, CultureInfo>("система(" + system.Name + ")", system),
            };

            foreach (var c in cultures)
            {
                Thread.CurrentThread.CurrentCulture = c.Value;
                Console.WriteLine();
                Console.WriteLine("=== культура " + c.Key);
                ArmAudio(c.Key);
                ArmUserNumber(c.Key);
                ArmUpDown(c.Key);
                ArmCells(c.Key);
                ArmGeometry(c.Key);
            }
            Thread.CurrentThread.CurrentCulture = system;

            Console.WriteLine();
            Console.WriteLine(string.Format(Inv, "строк {0}; дефектов класса {1}; принятых не-чисел {2}; сбоев обвязки {3}",
                                            lines, defects, nonNumbers, rig));
            foreach (var p in defectsByPlace)
            {
                Console.WriteLine(string.Format(Inv, "  дефекты по месту: {0} — {1}", p.Key, p.Value));
            }
            if (rig > 0)
            {
                Console.WriteLine("⛔ ИТОГ: обвязка слепа (контрольный ввод «2» не прошёл) — код 3");
                return 3;
            }
            if (defects > 0)
            {
                Console.WriteLine("⛔ ИТОГ: ДЕФЕКТ — человеческий ввод с запятой молча читается другим числом (код 1; так обязана отвечать сборка ДО П320)");
                return 1;
            }
            Console.WriteLine("ИТОГ: ни одно место не читает набранное другим числом (код 0)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ ОБРЫВ: " + ex);
            return 5;
        }
    }

    // ---- мерка ---------------------------------------------------------------

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            if (ch < 32 || ch > 126) sb.Append("U+" + ((int)ch).ToString("X4", Inv));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    static bool Honest(string text, out double value)
    {
        value = double.NaN;
        if (text == null) return false;
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out value)) return false;
        return !(double.IsNaN(value) || double.IsInfinity(value));
    }

    static string Classify(string fieldText, bool accepted, double value)
    {
        if (!accepted) return "отказ";
        if (double.IsNaN(value) || double.IsInfinity(value)) return "НЕ-ЧИСЛО";
        double h;
        if (!Honest(fieldText, out h)) return "ДЕФЕКТ";
        double tol = 1e-9 * Math.Max(1.0, Math.Abs(h));
        return Math.Abs(value - h) <= tol ? "верно" : "ДЕФЕКТ";
    }

    static void Report(string culture, string place, string input, string fieldText, bool accepted, double value, string note = null)
    {
        string verdict = Classify(fieldText, accepted, value);
        lines++;
        if (verdict == "ДЕФЕКТ")
        {
            defects++;
            int n;
            defectsByPlace.TryGetValue(place, out n);
            defectsByPlace[place] = n + 1;
        }
        else if (verdict == "НЕ-ЧИСЛО")
        {
            nonNumbers++;
        }
        string shown = fieldText == input ? "" : " (в поле «" + fieldText + "»)";
        string result = accepted ? value.ToString("R", Inv) : "отказ";
        Console.WriteLine(culture + "\t" + place + "\t«" + input + "»" + shown + " → " + result + "\t" + verdict
                          + (note == null ? "" : "\t" + note));
    }

    static object Field(object o, string name)
    {
        FieldInfo fi = o.GetType().GetField(name, F);
        if (fi == null) throw new MissingFieldException(o.GetType().Name, name);
        return fi.GetValue(o);
    }

    static void SetField(object o, string name, object value)
    {
        FieldInfo fi = o.GetType().GetField(name, F);
        if (fi == null) throw new MissingFieldException(o.GetType().Name, name);
        fi.SetValue(o, value);
    }

    static object Call(object o, string name, params object[] a)
    {
        Type t = o as Type ?? o.GetType();
        MethodInfo mi = t.GetMethod(name, F);
        if (mi == null) throw new MissingMethodException(t.Name, name);
        try
        {
            return mi.Invoke(o is Type ? null : o, a);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException;
        }
    }

    // ---- A: форма звукового входа ---------------------------------------------

    static AudioInputDeviceForm audioForm;

    static AudioInputDeviceConfig BaseAudio()
    {
        AudioInputDeviceConfig src = new AudioInputDeviceConfig();
        src.AudioInputDevice = new WaveInDeviceCaps { DeviceId = 0, Name = "p320" };
        PRAHomageMethodConfig pra = (PRAHomageMethodConfig)src.PulseDetectionMethodConfig;
        pra.LowerThreshold = 0.0;
        pra.UpperThreshold = 100000.0;
        pra.PulseLowerThreshold = 0.0;
        pra.PulseUpperThreshold = 100000.0;
        pra.PulseThreshold = 0.5;
        return src;
    }

    static void ArmAudio(string culture)
    {
        if (audioForm == null)
        {
            audioForm = new AudioInputDeviceForm();
            SetField(audioForm, "waveDeviceList", new ReadOnlyCollection<WaveInDeviceCaps>(new List<WaveInDeviceCaps>()));
        }

        var fields = new[]
        {
            new { Box = "doubleTextBox1", Name = "AudioInputDeviceForm.cs:180 нижний порог (LowerThreshold)", Pct = false },
            new { Box = "doubleTextBox2", Name = "AudioInputDeviceForm.cs:181 верхний порог (UpperThreshold)", Pct = false },
            new { Box = "maskedTextBox2", Name = "AudioInputDeviceForm.cs:187 порог корреляции, % (PulseThreshold·100)", Pct = true },
            new { Box = "doubleTextBox3", Name = "AudioInputDeviceForm.cs:190 нижний порог импульса (PulseLowerThreshold)", Pct = false },
            new { Box = "doubleTextBox4", Name = "AudioInputDeviceForm.cs:191 верхний порог импульса (PulseUpperThreshold)", Pct = false },
        };

        foreach (var fd in fields)
        {
            TextBoxBase box = (TextBoxBase)Field(audioForm, fd.Box);
            foreach (string input in Inputs)
            {
                audioForm.LoadFormContents(BaseAudio());
                box.Text = input;
                string inField = box.Text;
                AudioInputDeviceConfig dst = new AudioInputDeviceConfig();
                bool ok = audioForm.SaveFormContents(dst);
                PRAHomageMethodConfig pra = (PRAHomageMethodConfig)dst.PulseDetectionMethodConfig;
                double v;
                switch (fd.Box)
                {
                    case "doubleTextBox1": v = pra.LowerThreshold; break;
                    case "doubleTextBox2": v = pra.UpperThreshold; break;
                    case "maskedTextBox2": v = pra.PulseThreshold * 100.0; break;
                    case "doubleTextBox3": v = pra.PulseLowerThreshold; break;
                    default: v = pra.PulseUpperThreshold; break;
                }
                string note = null;
                if (!fd.Pct)
                {
                    double fieldValue;
                    bool fieldOk = UserNumber.TryParseDouble(inField, out fieldValue);
                    note = "поле (UserNumber): " + (fieldOk ? fieldValue.ToString("R", Inv) : "отказ");
                }
                Report(culture, "A " + fd.Name, input, inField, ok, v, note);
                if (input == "2" && !(ok && Math.Abs(v - 2.0) < 1e-9))
                {
                    Console.WriteLine("⛔ обвязка: контрольный ввод «2» не прошёл в " + fd.Box);
                    rig++;
                }
            }
        }
    }

    // ---- B: общий разбор ------------------------------------------------------

    static void ArmUserNumber(string culture)
    {
        foreach (string input in Inputs)
        {
            double d;
            bool ok = UserNumber.TryParseDouble(input, out d);
            Report(culture, "B UserNumber.TryParseDouble (DoubleTextBox.cs:30)", input, input, ok, d);
            decimal m;
            ok = UserNumber.TryParseDecimal(input, out m);
            Report(culture, "B UserNumber.TryParseDecimal (DoubleTextBox.cs:60)", input, input, ok, ok ? (double)m : 0.0);
            int i;
            ok = UserNumber.TryParseInt(input, out i);
            Report(culture, "B UserNumber.TryParseInt (DoubleTextBox.cs:51)", input, input, ok, i);
        }
    }

    // ---- C: поле со стрелками -------------------------------------------------

    static void ArmUpDown(string culture)
    {
        foreach (string input in Inputs)
        {
            using (InvariantNumericUpDown box = new InvariantNumericUpDown())
            {
                box.Minimum = -1000000m;
                box.Maximum = 1000000m;
                box.DecimalPlaces = 3;
                box.Value = 7m;
                box.Text = input;
                string inField = box.Text;
                Call(box, "ValidateEditText");
                decimal v = box.Value;
                bool accepted = v != 7m;
                Report(culture, "C InvariantNumericUpDown.ValidateEditText (InvariantNumericUpDown.cs:97)", input, inField, accepted, (double)v);
            }
        }
    }

    // ---- D: ячейки XPTable ----------------------------------------------------

    static void ArmCells(string culture)
    {
        foreach (string input in Inputs)
        {
            DoubleCellEditor de = new DoubleCellEditor();
            SetField(de, "minimum", -1000000.0);
            SetField(de, "maximum", 1000000.0);
            SetField(de, "currentValue", 7.0);
            de.TextBox.Text = input;
            Call(de, "ParseEditText");
            double dv = (double)Field(de, "currentValue");
            bool dAccepted = !(dv == 7.0);
            Report(culture, "D XPTable DoubleCellEditor.ParseEditText (DoubleCellEditor.cs:418)", input, input, dAccepted, dv);
            de.TextBox.Dispose();

            NumberCellEditor ne = new NumberCellEditor();
            SetField(ne, "minimum", -1000000m);
            SetField(ne, "maximum", 1000000m);
            SetField(ne, "currentValue", 7m);
            ne.TextBox.Text = input;
            Call(ne, "ParseEditText");
            decimal nv = (decimal)Field(ne, "currentValue");
            bool nAccepted = nv != 7m;
            Report(culture, "D XPTable NumberCellEditor.ParseEditText (NumberCellEditor.cs:409)", input, input, nAccepted, (double)nv);
            ne.TextBox.Dispose();
        }
    }

    // ---- E: коэффициенты калибровки в окне прибора — снято (`AMBER280`, поля — только показ)

    // ---- F: геометрия ---------------------------------------------------------

    static void ArmGeometry(string culture)
    {
        object panel = FormatterServices.GetUninitializedObject(typeof(GeometryEditorPanel));
        GC.SuppressFinalize(panel);
        var fields = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        TextBox tb = new TextBox();
        fields["p320"] = tb;
        SetField(panel, "fields", fields);

        foreach (string input in Inputs)
        {
            object[] a = { input, 0.0 };
            MethodInfo mi = typeof(GeometryMaterialEditorForm).GetMethod("TryReadNumber", F);
            bool ok = (bool)mi.Invoke(null, a);
            Report(culture, "F GeometryMaterialEditorForm.TryReadNumber (GeometryMaterialEditorForm.cs:798)", input, input, ok, (double)a[1]);

            tb.Text = input;
            object[] b = { "p320", 0.0 };
            MethodInfo tg = typeof(GeometryEditorPanel).GetMethod("TryGet", F, null,
                                                                  new[] { typeof(string), typeof(double).MakeByRefType() }, null);
            ok = (bool)tg.Invoke(panel, b);
            Report(culture, "F GeometryEditorPanel.TryGet (GeometryEditorPanel.cs:2817)", input, input, ok, (double)b[1]);
        }
        tb.Dispose();
    }
}
