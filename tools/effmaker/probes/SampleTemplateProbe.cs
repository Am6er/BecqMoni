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
/// Свои шаблоны образца в редакторе геометрий (`AMBER230`, задача Amber
/// 16.10.2026): «Сохранить», «Клонировать», «Удалить» у списка шаблонов на
/// вкладке «Источник», файл `config\SampleTemplates.xml`. Зеркало
/// <c>GeometryTemplateProbe</c> (`AMBER24`).
///
/// Безоконно, ОТРАЖЕНИЕМ панели: оба файла шаблонов подменены на каталог
/// пробы (`SampleTemplateStore.PathOverride`, `GeometryTemplateStore.PathOverride`),
/// диалоги — точками подмены панели; в живой `%AppData%\BecqMoni` не пишется
/// ничего.
///
/// Плечи:
///   1. круг: образец S (маринелли, вода 1.234, защита) → «Клонировать» как S →
///      файл записан и перечитан; поля образца S = поля образца геометрии,
///      детектор и всё прочее не тронуты; другой образец (точка 250 мм) и
///      другой детектор → выбрать S → образец = S, детектор = другой;
///   2. «Сохранить» на S после правки высоты пробы 70.5 — файл обновлён, число
///      точкой; ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: на подсказке «Сохранить»/«Удалить»
///      неактивны и отказывают; имя занятое (другой регистр), пустое, отказ
///      человека — число шаблонов не растёт; файл шаблонов ДЕТЕКТОРА не возник;
///   3. «Удалить» S — отказ в подтверждении, затем удаление; поля не тронуты;
///   4. полевая сцена: шаблон «грунт», снятый при одном детекторе, на другом
///      пересчитывается по нему — размеры те же, что у выбора строки «грунт»;
///   5. битый XML — редактор строится, `LoadError` непуст, запись — отказ;
///   6. путь: `config\SampleTemplates.xml`, не `GeometryTemplates.xml`;
///   7. разметка en/ru вкладки «Источник»: кнопки целиком в колонке, подписи
///      помещаются, список типа источника — ниже кнопок; PNG в `--dir=`.
///
///   SampleTemplateProbe.exe --dir=&lt;каталог пробы&gt;
///
/// Коды возврата: 0 — все плечи сошлись; 1 — есть расхождения (перечислены);
/// 2 — не запустилась (ключи, каталог).
/// </summary>
static class SampleTemplateProbe
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
            Console.Error.WriteLine("SampleTemplateProbe.exe --dir=<каталог пробы>");
            return 2;
        }

        Directory.CreateDirectory(Path.Combine(dir, "config"));
        string samples = Path.Combine(dir, "config", "SampleTemplates.xml");
        string detectors = Path.Combine(dir, "config", "GeometryTemplates.xml");
        foreach (string f in new[] { samples, detectors })
        {
            if (File.Exists(f))
            {
                File.Delete(f);
            }
        }

        SampleTemplateStore.PathOverride = samples;
        SampleTemplateStore.Reload();
        GeometryTemplateStore.PathOverride = detectors;
        GeometryTemplateStore.Reload();
        Console.WriteLine("файл шаблонов образцов пробы: " + samples);

        StringWriter refusals = new StringWriter();
        TextWriter stderr = Console.Error;
        Console.SetError(refusals);
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en");
            RunRound(samples, detectors);
            RunFieldScene(samples);
            RunBroken(samples);
            RunPath();
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
                          said.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
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

    static void RunRound(string samples, string detectors)
    {
        Console.WriteLine();
        Console.WriteLine("== плечи 1–3: круг «клонировать → сохранить → удалить» ==");
        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();

            // Образец с ОТЛИЧИМЫМИ числами на детекторе A.
            GeometryPresets.Preset presetA = GeometryPresets.Items[1];          // RadiaCode-101
            GeometryPresets.Preset presetB = GeometryPresets.Items[4];          // Atom Spectra Pro 80x80
            GeometryModel start = Applied(presetA);
            start.SourceType = GeometrySourceType.Marinelli;
            start.MarinelliBeakerDiameter = 111.0;
            start.MarinelliBeakerHeight = 88.0;
            start.MarinelliHoleDiameter = 63.0;
            start.MarinelliHoleHeight = 51.0;
            start.MarinelliSourceHeight = 80.0;
            start.MarinelliToDetectorDistance = 1.5;
            start.PointDistance = 123.5;
            start.BeakerDiameter = 77.0;
            start.SourceHeight = 33.0;
            start.InShield = true;
            start.Source = MaterialOf("Water, liquid");
            start.Source.Density = 1.234;
            panel.SetModel(start);
            Application.DoEvents();

            List<string> rows0 = panel.SampleRowTexts();
            Check(rows0.Count == 1, "список без файла: одна подсказка (вшитых образцов нет) = " + rows0.Count);
            Check(panel.SelectedSampleTemplate == null, "после SetModel выбрана подсказка");
            Check(!Button(panel, "sampleSaveButton").Enabled, "на подсказке «Сохранить» неактивна");
            Check(!Button(panel, "sampleDeleteButton").Enabled, "на подсказке «Удалить» неактивна");
            Check(Button(panel, "sampleCloneButton").Enabled, "на подсказке «Клонировать» активна");
            Check(!panel.SaveSampleTemplate(), "«Сохранить» на подсказке — отказ (false)");
            panel.TemplateDeleteConfirm = n => true;
            Check(!panel.DeleteSampleTemplate(), "«Удалить» на подсказке — отказ (false)");

            GeometryModel before = panel.Model.Clone();
            string sampleBefore = SampleTemplate.SampleFingerprint(before);
            string detectorA = DetectorPrint(before);

            // --- 1. клонировать как S ---
            string proposed = null;
            panel.TemplateNamePrompt = p => { proposed = p; return "S"; };
            Check(panel.CloneSampleTemplate(), "«Клонировать» под именем S — принято");
            Console.WriteLine("  предложенное имя: «" + proposed + "»");
            Check(!string.IsNullOrEmpty(proposed) && proposed.IndexOf("arinelli", StringComparison.OrdinalIgnoreCase) >= 0,
                  "предложенное имя — от типа источника: «" + proposed + "»");
            Check(File.Exists(samples), "файл шаблонов образцов записан");
            Check(!File.Exists(detectors), "файл шаблонов ДЕТЕКТОРОВ не возник");
            Check(panel.SelectedSampleTemplate != null && panel.SelectedSampleTemplate.Name == "S",
                  "после клонирования выбран S");
            Check(Button(panel, "sampleSaveButton").Enabled && Button(panel, "sampleDeleteButton").Enabled,
                  "на своём «Сохранить» и «Удалить» активны");
            List<string> rows1 = panel.SampleRowTexts();
            Check(rows1.Count == 2 && rows1[1] == "S", "в списке строка «S» последней: " + rows1[rows1.Count - 1]);
            Check(panel.PresetRowTexts().Count == 1 + GeometryPresets.Items.Count,
                  "список детекторов не тронут: " + panel.PresetRowTexts().Count + " строк");

            SampleTemplateStore.Reload();
            SampleTemplate s = SampleTemplateStore.Find("S");
            Check(s != null, "файл перечитан (Reload), S в нём есть");
            Check(s != null && s.Fingerprint() == sampleBefore, "поля образца S = поля образца геометрии (по отпечатку)");
            Check(s != null && s.SourceType == GeometrySourceType.Marinelli && s.InShield
                  && s.Source.Name == "Water, liquid" && s.Source.Density == 1.234
                  && s.MarinelliSourceHeight == 80.0 && s.PointDistance == 123.5,
                  "в S маринелли, защита, вода 1.234, проба 80, точка 123.5");
            Check(SameOutsideSample(before, panel.Model.Clone()),
                  "вне части образца геометрия побайтно та же (клонирование ничего не тронуло)");

            // --- другой образец и другой детектор → выбрать S ---
            GeometryModel other = Applied(presetB);
            other.SourceType = GeometrySourceType.Point;
            other.PointDistance = 250.0;
            other.InShield = false;
            panel.SetModel(other);
            Application.DoEvents();
            GeometryModel beforeApply = panel.Model.Clone();
            string detectorB = DetectorPrint(beforeApply);
            Check(detectorB != detectorA, "детектор B отличим от A");
            Check(SampleTemplate.SampleFingerprint(beforeApply) != sampleBefore, "образец до выбора S другой (точка 250)");
            Check(panel.SelectSampleByName("S"), "выбран S");
            Application.DoEvents();
            GeometryModel afterS = panel.Model.Clone();
            Check(SampleTemplate.SampleFingerprint(afterS) == sampleBefore, "после выбора S образец = S");
            Check(DetectorPrint(afterS) == detectorB, "детектор остался B (шаблон образца его не трогает)");
            Check(SameOutsideSample(beforeApply, afterS), "вне части образца геометрия побайтно та же");
            Check(panel.Dirty, "выбор шаблона — правка (Dirty)");
            // Спрашиваем СПИСОК, а не `Visible` панелей: вкладка источника не
            // выбрана, и у WinForms видимость её детей — false разом.
            Check(((ComboBox)FieldObject(panel, "sourceTypeCombo")).SelectedIndex == 2,
                  "в списке типа источника выбран маринелли");
            Check(((CheckBox)FieldObject(panel, "shieldCheck")).Checked, "галка «в защите» поставлена");

            // --- 2. «Сохранить» на S после правки ---
            Field(panel, "MarinelliSourceHeight").Text = "70.5";
            Application.DoEvents();
            string fileBefore = File.ReadAllText(samples);
            Check(panel.SaveSampleTemplate(), "«Сохранить» на S после правки высоты пробы 70.5 — принято");
            string fileAfter = File.ReadAllText(samples);
            Check(fileBefore != fileAfter, "файл шаблонов образцов изменился");
            Check(fileAfter.Contains("<MarinelliSourceHeight>70.5</MarinelliSourceHeight>"),
                  "в файле число с ТОЧКОЙ: <MarinelliSourceHeight>70.5</MarinelliSourceHeight>");
            Check(fileAfter.Contains("<Density>1.234</Density>"), "плотность пробы в файле точкой: 1.234");
            SampleTemplateStore.Reload();
            SampleTemplate s2 = SampleTemplateStore.Find("S");
            Check(s2 != null && s2.MarinelliSourceHeight == 70.5, "в перечитанном S проба 70.5");
            Check(!File.Exists(detectors), "файл шаблонов детекторов по-прежнему не возник");

            panel.TemplateNamePrompt = p => "s";
            Check(!panel.CloneSampleTemplate(), "клонировать под уже своим «s» (регистр другой) — отказ");
            panel.TemplateNamePrompt = p => "   ";
            Check(!panel.CloneSampleTemplate(), "пустое имя — отказ");
            panel.TemplateNamePrompt = p => "";
            Check(!panel.CloneSampleTemplate(), "отказ человека в диалоге — false, без беды");
            Check(SampleTemplateStore.Items.Count == 1, "число своих не выросло: 1");
            Check(panel.SampleRowTexts().Count == 2, "строк в списке по-прежнему 2");

            // Имя образца может совпадать с вшитым детектором: это два разных списка.
            panel.TemplateNamePrompt = p => presetA.Name;
            Check(panel.CloneSampleTemplate(), "образец под именем вшитого детектора «" + presetA.Name + "» — принят");
            Check(SampleTemplateStore.Items.Count == 2, "своих образцов 2");

            File.Copy(samples, Path.Combine(Path.GetDirectoryName(samples), "SampleTemplates.sample.xml"), true);

            // --- 3. «Удалить» S ---
            Check(panel.SelectSampleByName("S"), "выбран S для удаления");
            Application.DoEvents();
            string beforeDelete = SampleTemplate.SampleFingerprint(panel.Model);
            bool asked = false;
            panel.TemplateDeleteConfirm = n => { asked = n == "S"; return false; };
            Check(!panel.DeleteSampleTemplate() && SampleTemplateStore.Items.Count == 2,
                  "отказ в подтверждении — S остался");
            Check(asked, "подтверждение спросили про S");
            panel.TemplateDeleteConfirm = n => true;
            Check(panel.DeleteSampleTemplate(), "«Удалить» S с подтверждением — принято");
            Check(panel.SelectedSampleTemplate == null, "после удаления выбрана подсказка");
            Check(panel.SampleRowTexts().Count == 2, "строка S из списка исчезла (осталась подсказка и второй)");
            Check(SampleTemplate.SampleFingerprint(panel.Model) == beforeDelete, "поля образца удалением не тронуты");
            SampleTemplateStore.Reload();
            Check(SampleTemplateStore.Find("S") == null && SampleTemplateStore.Items.Count == 1,
                  "перечитанный файл без S, своих 1");
            Check(!File.Exists(detectors), "файл шаблонов детекторов не возник за весь круг");

            form.Close();
        }

        File.Delete(samples);
        SampleTemplateStore.Reload();
    }

    // ------------------------------------------------------------------
    // Плечо 4: полевая сцена пересчитывается по нынешнему детектору
    // ------------------------------------------------------------------

    static void RunFieldScene(string samples)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 4: шаблон с полевой сценой ==");
        GeometryPresets.Preset small = GeometryPresets.Items[1];               // RadiaCode-101
        GeometryPresets.Preset large = GeometryPresets.Items[4];               // Atom Spectra Pro 80x80
        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();
            ComboBox types = (ComboBox)FieldObject(panel, "sourceTypeCombo");
            const int ground = 4;          // строка «на земле» списка SourceKinds

            // Эталон: сцена «на земле», выбранная строкой при БОЛЬШОМ детекторе.
            panel.SetModel(Applied(large));
            Application.DoEvents();
            types.SelectedIndex = ground;
            Application.DoEvents();
            string reference = SampleTemplate.SampleFingerprint(panel.Model);
            Check(panel.Model.Scene == GeometrySceneKind.Ground, "эталон: сцена «на земле» при большом детекторе");

            // Шаблон «грунт» снят при МАЛОМ детекторе.
            panel.SetModel(Applied(small));
            Application.DoEvents();
            types.SelectedIndex = ground;
            Application.DoEvents();
            string smallScene = SampleTemplate.SampleFingerprint(panel.Model);
            Check(smallScene != reference, "при малом детекторе размеры сцены другие (иначе плечо ничего не мерит)");
            panel.TemplateNamePrompt = p => "G";
            Check(panel.CloneSampleTemplate(), "шаблон G (грунт при малом детекторе) — записан");

            // Большой детектор с точкой → выбрать G.
            GeometryModel point = Applied(large);
            point.SourceType = GeometrySourceType.Point;
            panel.SetModel(point);
            Application.DoEvents();
            Check(panel.SelectSampleByName("G"), "выбран G");
            Application.DoEvents();
            Check(panel.Model.Scene == GeometrySceneKind.Ground, "после G сцена «на земле»");
            Check(SampleTemplate.SampleFingerprint(panel.Model) == reference,
                  "размеры сцены пересчитаны по большому детектору = эталон выбора строки");
            form.Close();
        }

        File.Delete(samples);
        SampleTemplateStore.Reload();
    }

    // ------------------------------------------------------------------
    // Плечо 5: битый файл
    // ------------------------------------------------------------------

    static void RunBroken(string samples)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 5: битый XML ==");
        File.WriteAllText(samples, "<SampleTemplates><Templates><Template Name=\"Y\">"
                                   + "<PointDistance>oops</PointDistance></Template>",
                          Encoding.UTF8);
        SampleTemplateStore.Reload();
        int count = SampleTemplateStore.Items.Count;
        Check(!string.IsNullOrEmpty(SampleTemplateStore.LoadError),
              "LoadError непуст: " + Short(SampleTemplateStore.LoadError));
        Check(count == 0, "своих при битом файле 0");

        using (Form form = HiddenForm())
        {
            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();
            Check(panel.SampleRowTexts().Count == 1, "редактор построился, в списке одна подсказка");
            string bytesBefore = File.ReadAllText(samples);
            panel.TemplateNamePrompt = p => "Z";
            Check(!panel.CloneSampleTemplate(), "клонировать поверх битого файла — отказ");
            Check(File.ReadAllText(samples) == bytesBefore, "битый файл не перезаписан");
            form.Close();
        }

        File.Delete(samples);
        SampleTemplateStore.Reload();
        Check(string.IsNullOrEmpty(SampleTemplateStore.LoadError) && SampleTemplateStore.Items.Count == 0,
              "без файла: ошибки нет, своих 0 (первое открытие)");
    }

    // ------------------------------------------------------------------
    // Плечо 6: путь
    // ------------------------------------------------------------------

    static void RunPath()
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 6: путь файла ==");
        string live = Package.GetInstance().SampleTemplates;
        string det = Package.GetInstance().GeometryTemplates;
        Check(live.EndsWith("\\config\\SampleTemplates.xml", StringComparison.OrdinalIgnoreCase)
              && !live.Contains("\\device\\"), "штатный путь: " + live);
        Check(string.Equals(Path.GetDirectoryName(live), Path.GetDirectoryName(det), StringComparison.OrdinalIgnoreCase),
              "рядом с GeometryTemplates.xml: " + Path.GetDirectoryName(det));
    }

    // ------------------------------------------------------------------
    // Плечо 7: разметка en/ru вкладки «Источник»
    // ------------------------------------------------------------------

    static void RunLayout(string dir, string culture)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 7: разметка " + culture + " ==");
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
            tabs.SelectedIndex = 1;
            Application.DoEvents();

            ComboBox combo = (ComboBox)FieldObject(panel, "sampleCombo");
            ComboBox types = (ComboBox)FieldObject(panel, "sourceTypeCombo");
            Control column = combo.Parent;
            Label caption = null;
            foreach (Control c in column.Controls)
            {
                Label l = c as Label;
                if (l != null && l.Top < combo.Bottom && l.Left < combo.Left)
                {
                    caption = l;
                }
            }

            Check(caption != null && caption.Right <= combo.Left,
                  string.Format(CultureInfo.InvariantCulture, "{0}: подпись «{1}» кончается на {2}, список с {3}",
                                culture, caption != null ? caption.Text : "?", caption != null ? caption.Right : -1, combo.Left));
            Check(combo.Right <= column.ClientSize.Width,
                  string.Format(CultureInfo.InvariantCulture, "{0}: правый край списка {1} ≤ колонки {2}",
                                culture, combo.Right, column.ClientSize.Width));
            int bottom = 0;
            foreach (string name in new[] { "sampleSaveButton", "sampleCloneButton", "sampleDeleteButton" })
            {
                Button b = Button(panel, name);
                Size text = TextRenderer.MeasureText(b.Text, b.Font);
                Check(b.ClientSize.Width >= text.Width + 6,
                      string.Format(CultureInfo.InvariantCulture,
                                    "{0}: подпись «{1}» {2} px в кнопке {3} px", culture, b.Text, text.Width, b.ClientSize.Width));
                Check(b.Right <= column.ClientSize.Width && b.Parent == column,
                      string.Format(CultureInfo.InvariantCulture,
                                    "{0}: правый край кнопки «{1}» {2} ≤ колонки {3}", culture, b.Text, b.Right, column.ClientSize.Width));
                Check(b.Top >= combo.Bottom, culture + ": кнопка «" + b.Text + "» под списком");
                bottom = Math.Max(bottom, b.Bottom);
            }

            Check(types.Top >= bottom,
                  string.Format(CultureInfo.InvariantCulture, "{0}: список типа источника ({1}) ниже кнопок ({2})",
                                culture, types.Top, bottom));
            Label sceneLabel = (Label)FieldObject(panel, "sceneLabel");
            Panel point = (Panel)FieldObject(panel, "pointPanel");
            Check(sceneLabel.Top >= types.Bottom && point.Top >= sceneLabel.Top,
                  string.Format(CultureInfo.InvariantCulture, "{0}: подсказка сцены {1} под списком ({2}), поля с {3}",
                                culture, sceneLabel.Top, types.Bottom, point.Top));

            using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                panel.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.ClientSize));
                string png = Path.Combine(dir, "source_tab_" + culture + ".png");
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
        return new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
            ClientSize = new Size(950, 720),
        };
    }

    static GeometryModel Applied(GeometryPresets.Preset preset)
    {
        GeometryModel g = GeometryEditorPanel.Blank();
        preset.Apply(g);
        return g;
    }

    static string DetectorPrint(GeometryModel g)
    {
        return GeometryTemplate.FromModel("", g).Fingerprint() + ";facing=" + g.Facing;
    }

    /// <summary>
    /// Равны ли две геометрии ВНЕ части образца: обеим накладывается один и
    /// тот же нейтральный шаблон образца, после чего XML обязан совпасть побайтно.
    /// </summary>
    static bool SameOutsideSample(GeometryModel a, GeometryModel b)
    {
        SampleTemplate neutral = SampleTemplate.FromModel("", GeometryEditorPanel.Blank());
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
