using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

/// <summary>
/// Свои шаблоны детекторов в редакторе геометрий (`AMBER24`, задача Amber
/// 13.09.2026): «Сохранить», «Клонировать», «Удалить» рядом со списком готовых
/// детекторов, файл `config\GeometryTemplates.xml`.
///
/// Безоконно, ОТРАЖЕНИЕМ панели: файл шаблонов подменён на каталог пробы
/// (`GeometryTemplateStore.PathOverride`), диалоги имени и подтверждения —
/// точками подмены панели (`TemplateNamePrompt`, `TemplateDeleteConfirm`);
/// в живой `%AppData%\BecqMoni` не пишется ничего.
///
/// Плечи (нумерация — по постановке полосы П53):
///   1. круг: вшитый A → «Клонировать» как X → в списке свой X, файл записан
///      и перечитан; детекторные поля X = детекторные поля A, всё ОСТАЛЬНОЕ в
///      геометрии не тронуто; применить вшитый B → выбрать X → поля = A;
///   2. «Сохранить» на X после правки поля кристалла — файл обновлён;
///      ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: на вшитом «Сохранить»/«Удалить» неактивны и
///      отказывают, вшитый после всего тождествен коду; клонировать под именем
///      вшитого или уже своим — отказ, число шаблонов не растёт;
///   3. «Удалить» X — из списка и файла исчез, перечитанный файл без X;
///   4. битый XML — редактор строится, `LoadError` непуст, вшитые на месте,
///      запись поверх битого файла — отказ;
///   5. `config\device` пробы не тронут, файл шаблонов лежит не там;
///   6. снимок панели en/ru: кнопки целиком в колонке, подпись каждой
///      помещается в кнопку; PNG в каталог `--dir=`.
///
///   GeometryTemplateProbe.exe --dir=&lt;каталог пробы&gt;
///
/// Коды возврата: 0 — все плечи сошлись; 1 — есть расхождения (перечислены);
/// 2 — не запустилась (ключи, каталог).
/// </summary>
static class GeometryTemplateProbe
{
    static readonly List<string> Bad = new List<string>();
    static int checks;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Application.EnableVisualStyles();

        string dir = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal))
            {
                dir = a.Substring(6);
            }
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        if (string.IsNullOrEmpty(dir))
        {
            Console.Error.WriteLine("GeometryTemplateProbe.exe --dir=<каталог пробы>");
            return 2;
        }

        Directory.CreateDirectory(Path.Combine(dir, "config"));
        string templates = Path.Combine(dir, "config", "GeometryTemplates.xml");
        if (File.Exists(templates))
        {
            File.Delete(templates);
        }

        GeometryTemplateStore.PathOverride = templates;
        GeometryTemplateStore.Reload();
        Console.WriteLine("файл шаблонов пробы: " + templates);

        // Отказы панели идут в поток ошибок (`AppUi.Report` без окон) —
        // перехватываем, чтобы каждый отказ был виден в выводе и считался.
        StringWriter refusals = new StringWriter();
        TextWriter stderr = Console.Error;
        Console.SetError(refusals);
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en");
            RunRound(templates);
            RunBroken(templates);
            RunDeviceDirUntouched(dir, templates);
            foreach (string culture in new[] { "en", "ru" })
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                RunLayout(dir, culture);
            }
        }
        catch (Exception e)
        {
            Bad.Add("проба оборвалась: " + e);
        }
        finally
        {
            Console.SetError(stderr);
        }

        string said = refusals.ToString();
        Console.WriteLine();
        Console.WriteLine("отказов, названных панелью (строк в потоке ошибок): {0}",
                          CountLines(said));
        foreach (string line in said.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            Console.WriteLine("   | " + line);
        }

        Console.WriteLine();
        if (Bad.Count == 0)
        {
            Console.WriteLine("СОШЛОСЬ: проверок {0}, расхождений нет", checks);
            return 0;
        }

        Console.WriteLine("⛔ РАЗОШЛОСЬ: проверок {0}, расхождений {1}", checks, Bad.Count);
        foreach (string b in Bad)
        {
            Console.WriteLine("  - " + b);
        }

        return 1;
    }

    static int CountLines(string s)
    {
        return s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    static void Check(bool ok, string what)
    {
        checks++;
        Console.WriteLine("  {0} {1}", ok ? "ок" : "⛔", what);
        if (!ok)
        {
            Bad.Add(what);
        }
    }

    // ------------------------------------------------------------------
    // Плечи 1–3: круг клонирования, сохранения, удаления
    // ------------------------------------------------------------------

    static void RunRound(string templates)
    {
        Console.WriteLine();
        Console.WriteLine("== плечи 1–3: круг «клонировать → сохранить → удалить» ==");
        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();

            // Источник — с ОТЛИЧИМЫМИ числами, чтобы «не тронут» было видно.
            GeometryModel start = GeometryEditorPanel.Blank();
            start.SourceType = GeometrySourceType.Cylinder;
            start.PointDistance = 123.5;
            start.BeakerDiameter = 77.0;
            start.SourceHeight = 33.0;
            start.Source = MaterialOf("Water, liquid");
            panel.SetModel(start);

            List<string> rows0 = panel.PresetRowTexts();
            Check(rows0.Count == 1 + GeometryPresets.Items.Count,
                  "список без файла: подсказка + " + GeometryPresets.Items.Count + " вшитых = " + rows0.Count);
            Check(panel.SelectedOwnTemplate == null && panel.SelectedBuiltinPreset == null,
                  "после SetModel выбрана подсказка");

            string aName = GeometryPresets.Items[1].Name;               // RadiaCode-101
            string bName = GeometryPresets.Items[4].Name;               // Atom Spectra Pro 80x80
            string aBefore = Fingerprint(Applied(GeometryPresets.Items[1]));
            string bBefore = Fingerprint(Applied(GeometryPresets.Items[4]));

            // --- 1. вшитый A → клонировать как X ---
            Check(panel.SelectPresetByName(aName), "выбран вшитый A = " + aName);
            Application.DoEvents();
            Check(panel.SelectedBuiltinPreset != null && panel.SelectedBuiltinPreset.Name == aName,
                  "список ПОМНИТ выбранный вшитый (не сброшен на подсказку)");
            Check(!Button(panel, "templateSaveButton").Enabled, "на вшитом «Сохранить» неактивна");
            Check(!Button(panel, "templateDeleteButton").Enabled, "на вшитом «Удалить» неактивна");
            Check(Button(panel, "templateCloneButton").Enabled, "на вшитом «Клонировать» активна");

            string proposed = null;
            panel.TemplateNamePrompt = p => { proposed = p; return "X"; };
            GeometryModel beforeClone = panel.Model.Clone();
            Check(panel.CloneTemplate(), "«Клонировать» от A под именем X — принято");
            Check(proposed == aName + " 2", "предложенное имя свободно: «" + proposed + "» (ждали «" + aName + " 2»)");
            Check(File.Exists(templates), "файл шаблонов записан");
            Check(panel.SelectedOwnTemplate != null && panel.SelectedOwnTemplate.Name == "X",
                  "после клонирования выбран свой X");
            Check(Button(panel, "templateSaveButton").Enabled && Button(panel, "templateDeleteButton").Enabled,
                  "на своём «Сохранить» и «Удалить» активны");
            List<string> rows1 = panel.PresetRowTexts();
            Check(rows1.Count == rows0.Count + 1 && rows1[rows1.Count - 1] == "X (own)",
                  "в списке появилась строка «X (own)» последней: " + rows1[rows1.Count - 1]);

            GeometryTemplateStore.Reload();
            GeometryTemplate x = GeometryTemplateStore.Find("X");
            Check(x != null, "файл перечитан (Reload), X в нём есть");
            Check(x != null && x.Fingerprint() == aBefore,
                  "детекторные поля X = детекторные поля A (по отпечатку)");

            // Всё, что НЕ детектор, после наложения X не тронуто: нейтрализуем
            // детекторную часть в обеих копиях и сравним XML целиком.
            GeometryModel afterX = panel.Model.Clone();
            Check(SameOutsideDetector(beforeClone, afterX),
                  "вне детекторной части геометрия побайтно та же (источник 77/33 мм, вода)");
            Check(afterX.BeakerDiameter == 77.0 && afterX.SourceHeight == 33.0
                  && afterX.Source.Name == "Water, liquid",
                  "поля источника целы: сосуд 77, проба 33, вода");

            // --- применить вшитый B → выбрать X → поля = A ---
            Check(panel.SelectPresetByName(bName), "выбран вшитый B = " + bName);
            Application.DoEvents();
            Check(Fingerprint(panel.Model) == bBefore, "поля = B после выбора B");
            Check(panel.SelectPresetByName("X"), "выбран свой X");
            Application.DoEvents();
            Check(Fingerprint(panel.Model) == aBefore, "поля = A после выбора X");

            // --- 2. «Сохранить» на X после правки поля кристалла ---
            // A — брусок (RadiaCode-101, 10x10x10), поэтому правится сторона
            // бруска: диаметр у бруска — мёртвое поле, его сборка модели
            // обнуляет (`A94`), и правка в нём до файла не доехала бы.
            TextBox side = Field(panel, "CrystalBoxX");
            side.Text = "12.5";
            Application.DoEvents();
            string fileBefore = File.ReadAllText(templates);
            Check(panel.SaveTemplate(), "«Сохранить» на X после правки стороны бруска 12.5 — принято");
            string fileAfter = File.ReadAllText(templates);
            Check(fileBefore != fileAfter, "файл шаблонов изменился");
            GeometryTemplateStore.Reload();
            GeometryTemplate x2 = GeometryTemplateStore.Find("X");
            Check(x2 != null && x2.CrystalBoxX == 12.5, "в перечитанном X сторона 12.5");
            Check(fileAfter.Contains("<CrystalBoxX>12.5</CrystalBoxX>"),
                  "в файле число с ТОЧКОЙ: <CrystalBoxX>12.5</CrystalBoxX>");

            // --- положительный контроль: вшитые не правятся ---
            Check(panel.SelectPresetByName(aName), "снова выбран вшитый A");
            Application.DoEvents();
            Check(!panel.SaveTemplate(), "«Сохранить» на вшитом — отказ (false)");
            panel.TemplateDeleteConfirm = n => true;
            Check(!panel.DeleteTemplate(), "«Удалить» на вшитом — отказ (false)");
            Check(GeometryTemplateStore.Items.Count == 1, "своих по-прежнему один");
            Check(Fingerprint(Applied(GeometryPresets.Items[1])) == aBefore
                  && Fingerprint(Applied(GeometryPresets.Items[4])) == bBefore,
                  "вшитые A и B после всех действий тождественны коду");

            panel.TemplateNamePrompt = p => aName;
            Check(!panel.CloneTemplate(), "клонировать под именем вшитого «" + aName + "» — отказ");
            panel.TemplateNamePrompt = p => aName.ToUpperInvariant();
            Check(!panel.CloneTemplate(), "то же без учёта регистра — отказ");
            panel.TemplateNamePrompt = p => "x";
            Check(!panel.CloneTemplate(), "клонировать под уже своим «x» (регистр другой) — отказ");
            panel.TemplateNamePrompt = p => "   ";
            Check(!panel.CloneTemplate(), "пустое имя — отказ");
            panel.TemplateNamePrompt = p => "";
            Check(!panel.CloneTemplate(), "отказ человека в диалоге — false, без беды");
            Check(GeometryTemplateStore.Items.Count == 1, "число своих не выросло: 1");
            Check(panel.PresetRowTexts().Count == rows0.Count + 1, "строк в списке по-прежнему " + (rows0.Count + 1));

            // Образец файла — в артефакты: каким его увидит человек.
            File.Copy(templates, Path.Combine(Path.GetDirectoryName(templates), "GeometryTemplates.sample.xml"), true);

            // --- 3. «Удалить» X ---
            Check(panel.SelectPresetByName("X"), "выбран X для удаления");
            Application.DoEvents();
            string beforeDelete = Fingerprint(panel.Model);
            bool asked = false;
            panel.TemplateDeleteConfirm = n => { asked = n == "X"; return false; };
            Check(!panel.DeleteTemplate() && GeometryTemplateStore.Items.Count == 1,
                  "отказ в подтверждении — X остался");
            Check(asked, "подтверждение спросили про X");
            panel.TemplateDeleteConfirm = n => true;
            Check(panel.DeleteTemplate(), "«Удалить» X с подтверждением — принято");
            Check(panel.SelectedOwnTemplate == null && panel.SelectedBuiltinPreset == null,
                  "после удаления выбрана подсказка");
            Check(panel.PresetRowTexts().Count == rows0.Count, "строка X из списка исчезла");
            Check(Fingerprint(panel.Model) == beforeDelete, "поля детектора удалением не тронуты (= X, сторона 12.5)");
            GeometryTemplateStore.Reload();
            Check(GeometryTemplateStore.Find("X") == null && GeometryTemplateStore.Items.Count == 0,
                  "перечитанный файл без X, своих 0");
            Check(File.Exists(templates) && !File.ReadAllText(templates).Contains("\"X\""),
                  "файл на месте и имени X в нём нет");

            form.Close();
        }
    }

    // ------------------------------------------------------------------
    // Плечо 4: битый файл
    // ------------------------------------------------------------------

    static void RunBroken(string templates)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 4: битый XML ==");
        File.WriteAllText(templates, "<GeometryTemplates><Templates><Template Name=\"Y\">"
                                     + "<CrystalDiameter>oops</CrystalDiameter></Template>",
                          Encoding.UTF8);
        GeometryTemplateStore.Reload();
        int count = GeometryTemplateStore.Items.Count;
        Check(!string.IsNullOrEmpty(GeometryTemplateStore.LoadError),
              "LoadError непуст: " + Short(GeometryTemplateStore.LoadError));
        Check(count == 0, "своих при битом файле 0");

        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();
            Check(panel.PresetRowTexts().Count == 1 + GeometryPresets.Items.Count,
                  "редактор построился, в списке подсказка и вшитые");
            Check(panel.SelectPresetByName(GeometryPresets.Items[0].Name), "вшитый выбирается");
            string bytesBefore = File.ReadAllText(templates);
            panel.TemplateNamePrompt = p => "Z";
            Check(!panel.CloneTemplate(), "клонировать поверх битого файла — отказ");
            Check(File.ReadAllText(templates) == bytesBefore, "битый файл не перезаписан");
            form.Close();
        }

        File.Delete(templates);
        GeometryTemplateStore.Reload();
        Check(string.IsNullOrEmpty(GeometryTemplateStore.LoadError) && GeometryTemplateStore.Items.Count == 0,
              "без файла: ошибки нет, своих 0 (первое открытие)");
    }

    // ------------------------------------------------------------------
    // Плечо 5: config\device не тронут
    // ------------------------------------------------------------------

    static void RunDeviceDirUntouched(string dir, string templates)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 5: config\\device ==");
        string deviceDir = Path.Combine(dir, "config", "device");
        Directory.CreateDirectory(deviceDir);
        string[] before = Directory.GetFiles(deviceDir);

        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            panel.SelectPresetByName(GeometryPresets.Items[2].Name);
            panel.TemplateNamePrompt = p => "W";
            Check(panel.CloneTemplate(), "клонирован W");
            form.Close();
        }

        string[] after = Directory.GetFiles(deviceDir);
        Check(before.Length == after.Length, "в config\\device файлов столько же: " + after.Length);
        Check(Path.GetDirectoryName(templates).TrimEnd('\\') != deviceDir.TrimEnd('\\'),
              "файл шаблонов лежит не в config\\device: " + Path.GetDirectoryName(templates));
        Check(string.Equals(Path.GetFileName(Path.GetDirectoryName(templates)), "config",
                            StringComparison.OrdinalIgnoreCase),
              "файл шаблонов лежит в config\\ (рядом с GeometryMaterials.xml)");
        string live = Package.GetInstance().GeometryTemplates;
        Check(live.EndsWith("\\config\\GeometryTemplates.xml", StringComparison.OrdinalIgnoreCase)
              && !live.Contains("\\device\\"),
              "штатный путь: " + live);
        File.Delete(templates);
        GeometryTemplateStore.Reload();
    }

    // ------------------------------------------------------------------
    // Плечо 6: разметка en/ru — кнопки целиком, подписи не обрезаны
    // ------------------------------------------------------------------

    static void RunLayout(string dir, string culture)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 6: разметка " + culture + " ==");
        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();

            TabControl tabs = null;
            foreach (Control c in panel.Controls)
            {
                tabs = c as TabControl;
                if (tabs != null) break;
            }

            Check(tabs != null, "вкладки найдены");
            if (tabs == null) return;
            tabs.SelectedIndex = 0;
            Application.DoEvents();

            ComboBox combo = (ComboBox)FieldObject(panel, "presetCombo");
            Control column = combo.Parent;
            int right = 0;
            foreach (string name in new[] { "templateSaveButton", "templateCloneButton", "templateDeleteButton" })
            {
                Button b = Button(panel, name);
                Size text = TextRenderer.MeasureText(b.Text, b.Font);
                Check(b.ClientSize.Width >= text.Width + 6,
                      string.Format(CultureInfo.InvariantCulture,
                                    "{0}: подпись «{1}» {2} px в кнопке {3} px", culture, b.Text, text.Width, b.ClientSize.Width));
                Check(b.Right <= column.ClientSize.Width,
                      string.Format(CultureInfo.InvariantCulture,
                                    "{0}: правый край кнопки «{1}» {2} ≤ колонки {3}", culture, b.Text, b.Right, column.ClientSize.Width));
                Check(b.Parent == column && b.Bottom <= column.ClientSize.Height,
                      string.Format(CultureInfo.InvariantCulture,
                                    "{0}: низ кнопки «{1}» {2} ≤ {3}", culture, b.Text, b.Bottom, column.ClientSize.Height));
                Check(b.Top >= combo.Bottom, culture + ": кнопка «" + b.Text + "» под списком");
                right = Math.Max(right, b.Right);
            }

            // Попутная находка П53: по-русски подпись переключателя бруска
            // налезала на список стороны к пробе (x=336 числом). Меряется
            // здесь же, чтобы не вернулось.
            RadioButton boxRadio = (RadioButton)FieldObject(panel, "boxRadio");
            ComboBox facing = (ComboBox)FieldObject(panel, "facingCombo");
            Check(boxRadio.Right <= facing.Left,
                  string.Format(CultureInfo.InvariantCulture,
                                "{0}: переключатель «{1}» кончается на {2}, список стороны начинается с {3}",
                                culture, boxRadio.Text, boxRadio.Right, facing.Left));
            Check(facing.Right <= column.ClientSize.Width,
                  string.Format(CultureInfo.InvariantCulture,
                                "{0}: правый край списка стороны {1} ≤ колонки {2}", culture, facing.Right, column.ClientSize.Width));

            RadioButton cyl = (RadioButton)FieldObject(panel, "cylinderRadio");
            Check(cyl.Top >= Button(panel, "templateSaveButton").Bottom,
                  string.Format(CultureInfo.InvariantCulture,
                                "{0}: форма кристалла ({1}) ниже кнопок ({2}); правый край ряда {3}",
                                culture, cyl.Top, Button(panel, "templateSaveButton").Bottom, right));

            using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                panel.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.ClientSize));
                string png = Path.Combine(dir, "panel_" + culture + ".png");
                bmp.Save(png, ImageFormat.Png);
                Console.WriteLine("  снимок: " + png);
            }

            form.Close();
        }
    }

    // ------------------------------------------------------------------
    // Помощники
    // ------------------------------------------------------------------

    static Form HiddenForm()
    {
        Form form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
            ClientSize = new Size(950, 720),
        };
        return form;
    }

    static GeometryModel Applied(GeometryPresets.Preset preset)
    {
        GeometryModel g = GeometryEditorPanel.Blank();
        preset.Apply(g);
        return g;
    }

    static string Fingerprint(GeometryModel g)
    {
        return GeometryTemplate.FromModel("", g).Fingerprint();
    }

    /// <summary>
    /// Равны ли две геометрии ВНЕ детекторной части: обеим накладывается один
    /// и тот же нейтральный шаблон, после чего XML обязан совпасть побайтно.
    /// </summary>
    static bool SameOutsideDetector(GeometryModel a, GeometryModel b)
    {
        GeometryTemplate neutral = GeometryTemplate.FromModel("", GeometryEditorPanel.Blank());
        GeometryModel ca = a.Clone(), cb = b.Clone();
        neutral.Apply(ca);
        neutral.Apply(cb);
        return Xml(ca) == Xml(cb);
    }

    static string Xml(GeometryModel g)
    {
        XmlSerializer s = new XmlSerializer(typeof(GeometryModel));
        using (StringWriter w = new StringWriter(CultureInfo.InvariantCulture))
        {
            s.Serialize(w, g);
            return w.ToString();
        }
    }

    static GeometryMaterial MaterialOf(string name)
    {
        GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
        return entry != null ? GeometryMaterialLibrary.Make(entry, entry.Density) : new GeometryMaterial();
    }

    static Button Button(object panel, string field)
    {
        return (Button)FieldObject(panel, field);
    }

    static TextBox Field(object panel, string key)
    {
        Dictionary<string, TextBox> fields = (Dictionary<string, TextBox>)FieldObject(panel, "fields");
        return fields[key];
    }

    static object FieldObject(object target, string name)
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            System.Reflection.FieldInfo f = t.GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            if (f != null)
            {
                return f.GetValue(target);
            }
        }

        throw new InvalidOperationException("нет поля " + name);
    }

    static string Short(string s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s.Substring(0, 90) + "…" : s;
    }
}
