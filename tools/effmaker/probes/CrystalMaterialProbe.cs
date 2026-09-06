using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
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
/// ВЕЩЕСТВО КРИСТАЛЛА У ПРИБОРА, КОГДА ГЕОМЕТРИИ НЕТ (`A276`, решение Amber
/// 06.09.2026: «поле в конфигурации прибора, выбор вещества из библиотеки»).
///
///     crystalmaterialprobe
///
/// Что мерится, шестью плечами.
///
/// 1. ⛔ ПЕРВЕНСТВО ГЕОМЕТРИИ. Сцена с геометрией не меняется, даже когда поле
///    прибора называет ДРУГОЕ вещество. Плечо не пустое: тот же спектр с
///    убранной геометрией берёт вещество поля — то есть проверка отличает одно
///    состояние от другого, а не проходит всегда.
/// 2. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (а): прибор БЕЗ геометрии, поле заполнено — доли
///    появляются и совпадают с теми, что даёт геометрия у ТОГО ЖЕ вещества, до
///    последнего знака. Числа печатаются: у NaI 0.153 и 0.847, и «поровну»
///    ошиблось бы в долю рождения пар примерно в полтора раза.
/// 3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (б): прибор БЕЗ геометрии и с ПУСТЫМ полем —
///    поведение прежнее: долей нет, отказа нет, вещество не выдумано, а
///    родителей образов вылета держит один запас над порогом пар
///    (<c>FsaLibrary.EscapeParentMarginKev</c>).
/// 4. СТАРЫЙ КОНФИГ. Файл конфигурации БЕЗ нового элемента читается, поле
///    получает пусто, а не отказ; новое значение переживает круг записи-чтения
///    и глубокую копию.
/// 5. ФОРМА. Список веществ у прибора наполняется библиотекой (кристаллы плюс
///    пункт «не задано»), выбранное возвращается именем, а имя, пропавшее из
///    библиотеки, СОХРАНЯЕТСЯ, а не стирается молча.
/// 6. ПУТЬ ПРИЛОЖЕНИЯ ЦЕЛИКОМ. `FsaCompositionInference.Infer` — та самая
///    перегрузка, которой пользуется `FsaAnalysisSession`, — доносит имя от
///    конфигурации прибора до спецификации, и доли по нему берутся.
/// 7. ПОДПИСЬ ВЛЕЗАЕТ. Русская подпись поля меряется тем же
///    <see cref="TextRenderer"/>, которым её рисует <c>Label</c>, и сверяется с
///    местом до списка (`A127`: у панелей уже был запас РОВНО НОЛЬ). Контроль —
///    удвоенный текст, он ОБЯЗАН не влезть.
///
/// ⚠ Окон проба не открывает: форма строится, но не показывается, а подпись
/// меряется вне формы.
///
/// Сборка — общая, `tools/effmaker/probes/build_all.ps1`.
///
/// Ожидание: «СОШЛОСЬ».
/// Коды возврата: 0 — сошлось; 1 — не сошлось.
/// </summary>
static class CrystalMaterialProbe
{
    /// <summary>Имена веществ — ДОВОДЫ ЗАМЕРА, а не таблица приложения.</summary>
    const string Nai = "Sodium iodide";
    const string Csi = "Cesium iodide";

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // ⛔ ТОТ ЖЕ РЕЖИМ ОТРИСОВКИ, ЧТО У ПРИЛОЖЕНИЯ (`Program.Main` зовёт то
        // же самое), и зовётся он ЗДЕСЬ, а не в плече замера подписи: вызов
        // законен только ДО первого окна, а плечо 5 форму уже построило —
        // «Необходимо вызвать SetCompatibleTextRenderingDefault до создания
        // первого объекта IWin32Window». Поймано на себе первым же прогоном.
        Application.SetCompatibleTextRenderingDefault(false);

        int bad = 0;
        bad += GeometryFirst();
        bad += NamedWithoutGeometry();
        bad += EmptyWithoutGeometry();
        bad += OldConfig();
        bad += FormList();
        bad += AppPath();
        bad += LabelFits();

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: вещество кристалла у прибора читается, геометрия первична"
            : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. Геометрия первична
    // ------------------------------------------------------------------

    static int GeometryFirst()
    {
        Console.WriteLine("=== 1. ГЕОМЕТРИЯ ПЕРВИЧНА, поле — запасной источник ===");
        int bad = 0;

        Dictionary<int, double> csi = Matter(Csi);
        Dictionary<int, double> nai = Matter(Nai);
        if (csi.Count == 0 || nai.Count == 0)
        {
            Console.WriteLine("  ⛔ в библиотеке нет одного из двух веществ замера — мерить нечем");
            return 1;
        }

        // Сцена С ГЕОМЕТРИЕЙ (доли уже в спецификации) и полем прибора, которое
        // называет ДРУГОЕ вещество. Ответ обязан остаться геометрическим.
        var withGeometry = new FsaSampleSpec();
        foreach (KeyValuePair<int, double> pair in csi)
        {
            withGeometry.CrystalFractions[pair.Key] = pair.Value;
        }

        withGeometry.CrystalMaterialName = Nai;
        Dictionary<int, double> got = FsaSampleLibrary.CrystalFractionsOf(withGeometry);
        bad += Same("геометрия CsI + поле NaI", csi, got);

        // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОГО ЭТОГО ПЛЕЧА: убрать геометрию — и
        // ответ обязан СМЕНИТЬСЯ на вещество поля. Без него «совпало с CsI»
        // означало бы всего лишь, что поле не читается ВООБЩЕ НИКОГДА.
        var withoutGeometry = new FsaSampleSpec { CrystalMaterialName = Nai };
        Dictionary<int, double> fallback = FsaSampleLibrary.CrystalFractionsOf(withoutGeometry);
        bad += Same("та же сцена без геометрии", nai, fallback);

        bool differ = !Equal(csi, nai);
        Console.WriteLine("  вещества замера различимы: {0}", differ ? "да" : "НЕТ — плечо ничего не мерит");
        bad += differ ? 0 : 1;
        return bad;
    }

    // ------------------------------------------------------------------
    // 2. Положительный контроль (а): поле заполнено, геометрии нет
    // ------------------------------------------------------------------

    static int NamedWithoutGeometry()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. КОНТРОЛЬ (а): прибор без геометрии, вещество названо ===");
        int bad = 0;

        var spec = new FsaSampleSpec { CrystalMaterialName = Nai };
        Dictionary<int, double> got = FsaSampleLibrary.CrystalFractionsOf(spec);
        Dictionary<int, double> want = Matter(Nai);

        Console.WriteLine("  {0,-4} {1,10} {2,10}", "Z", "поле", "геометрия");
        var keys = new List<int>(want.Keys);
        keys.Sort();
        foreach (int z in keys)
        {
            double have;
            got.TryGetValue(z, out have);
            Console.WriteLine("  {0,-4} {1,10:F3} {2,10:F3}", z, have, want[z]);
        }

        bad += Same("доли по названному веществу", want, got);

        // ⚠ «Поровну» — не то же самое, и цена названа числом.
        var equal = new Dictionary<int, double>();
        foreach (int z in keys)
        {
            equal[z] = 1.0 / keys.Count;
        }

        double sharpNamed = PairShare(got, 1173.2);
        double sharpEqual = PairShare(equal, 1173.2);
        Console.WriteLine("  доля рождения пар на 1173.2 кэВ: по веществу {0:F4} %, «поровну» {1:F4} % — в {2:F2} раза",
                          100.0 * sharpNamed, 100.0 * sharpEqual,
                          sharpEqual > 0.0 ? sharpNamed / sharpEqual : 0.0);
        return bad;
    }

    // ------------------------------------------------------------------
    // 3. Положительный контроль (б): поле пустое, геометрии нет
    // ------------------------------------------------------------------

    static int EmptyWithoutGeometry()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. КОНТРОЛЬ (б): прибор без геометрии, поле ПУСТОЕ ===");
        int bad = 0;

        var spec = new FsaSampleSpec();
        Dictionary<int, double> got = FsaSampleLibrary.CrystalFractionsOf(spec);
        Console.WriteLine("  долей: {0} — {1}", got.Count,
                          got.Count == 0 ? "вещество не выдумано, сошлось" : "ВЕЩЕСТВО ВЗЯЛОСЬ НИОТКУДА");
        bad += got.Count == 0 ? 0 : 1;

        // Имя, которого в библиотеке нет вовсе, — тоже «источника нет», а не отказ.
        var stray = new FsaSampleSpec { CrystalMaterialName = "нет такого вещества" };
        Dictionary<int, double> none = FsaSampleLibrary.CrystalFractionsOf(stray);
        Console.WriteLine("  имя мимо библиотеки: долей {0} — {1}", none.Count,
                          none.Count == 0 ? "сошлось" : "РАСХОЖДЕНИЕ");
        bad += none.Count == 0 ? 0 : 1;

        // Отбор родителей при этом держится ОДНИМ запасом над порогом пар, и
        // это видно поимённо: линия в 98 кэВ над порогом родителем не берётся,
        // а линия много выше — берётся.
        var composition = new List<FsaComponent>
        {
            Component("Bi-214", 1120.3, 14.9),
            Component("Tl-208", 2614.5, 35.6),
        };

        List<string> parents = Parents(FsaLibrary.EscapeImages(composition, null));
        string shown = parents.Count == 0 ? "(нет)" : string.Join(", ", parents.ToArray());
        bool ok = parents.Count == 1 && parents[0] == "2614";
        Console.WriteLine("  родители образов вылета без вещества: {0} — {1}", shown,
                          ok ? "сошлось (работает запас " + FsaLibrary.EscapeParentMarginKev.ToString("F1", CultureInfo.InvariantCulture) + " кэВ)"
                             : "РАСХОЖДЕНИЕ");
        bad += ok ? 0 : 1;
        return bad;
    }

    // ------------------------------------------------------------------
    // 4. Старый конфиг без нового элемента
    // ------------------------------------------------------------------

    static int OldConfig()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. старая конфигурация БЕЗ нового элемента ===");
        int bad = 0;

        var serializer = new XmlSerializer(typeof(DeviceConfigInfo));

        // Старый файл получается не рукописной строкой, а СНЯТИЕМ элемента из
        // настоящей записи: рукописный XML проверял бы разбор моей выдумки.
        var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба" };
        device.CrystalMaterialName = Nai;
        string full;
        using (var writer = new StringWriter(CultureInfo.InvariantCulture))
        {
            serializer.Serialize(writer, device);
            full = writer.ToString();
        }

        bool present = full.Contains("<CrystalMaterialName>");
        Console.WriteLine("  элемент в записи: {0}", present ? "есть" : "НЕТ — писать нечего");
        bad += present ? 0 : 1;

        string old = Strip(full, "CrystalMaterialName");
        Console.WriteLine("  элемент снят: {0}", old.Contains("CrystalMaterialName") ? "НЕТ" : "да");

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
            Console.WriteLine("  ⛔ ОТКАЗ ЧТЕНИЯ: {0}: {1}", error.GetType().Name, error.Message);
            return bad + 1;
        }

        bool empty = string.IsNullOrEmpty(back.CrystalMaterialName);
        Console.WriteLine("  прочтено, поле = «{0}» — {1}", back.CrystalMaterialName,
                          empty ? "пусто, сошлось" : "РАСХОЖДЕНИЕ");
        bad += empty ? 0 : 1;

        // И круг с элементом на месте — заодно с глубокой копией.
        DeviceConfigInfo whole;
        using (var reader = new StringReader(full))
        {
            whole = (DeviceConfigInfo)serializer.Deserialize(reader);
        }

        Console.WriteLine("  круг с элементом: «{0}» — {1}", whole.CrystalMaterialName,
                          whole.CrystalMaterialName == Nai ? "сошлось" : "РАСХОЖДЕНИЕ");
        bad += whole.CrystalMaterialName == Nai ? 0 : 1;

        DeviceConfigInfo copy = device.Clone();
        Console.WriteLine("  глубокая копия  : «{0}» — {1}", copy.CrystalMaterialName,
                          copy.CrystalMaterialName == Nai ? "сошлось" : "РАСХОЖДЕНИЕ");
        bad += copy.CrystalMaterialName == Nai ? 0 : 1;
        return bad;
    }

    /// <summary>Снять элемент из XML — вместе с содержимым и переводом строки.</summary>
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
    // 5. Список на форме прибора
    // ------------------------------------------------------------------

    static int FormList()
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. список веществ на форме прибора ===");
        int bad = 0;

        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();

        using (var form = new DeviceConfigForm())
        {
            var combo = (ComboBox)Field(form, "crystalMaterialCombo");
            var label = (Label)Field(form, "crystalMaterialLabel");
            Console.WriteLine("  поле на вкладке «{0}», подпись «{1}»",
                              label.Parent == null ? "НЕТ РОДИТЕЛЯ" : label.Parent.Text, label.Text);
            bad += label.Parent != null && ReferenceEquals(label.Parent, combo.Parent) ? 0 : 1;

            int crystals = GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Crystal).Count;

            // Пусто: список — кристаллы плюс пункт «не задано».
            var device = new DeviceConfigInfo();
            Call(form, "LoadCrystalMaterial", device);
            string chosen = (string)Call(form, "CrystalMaterialFromForm");
            Console.WriteLine("  пусто        : строк {0} (кристаллов {1} + 1), выбрано «{2}»",
                              combo.Items.Count, crystals, chosen);
            bad += combo.Items.Count == crystals + 1 && chosen == "" ? 0 : 1;

            // Названное вещество выбирается и возвращается тем же именем.
            device.CrystalMaterialName = Nai;
            Call(form, "LoadCrystalMaterial", device);
            chosen = (string)Call(form, "CrystalMaterialFromForm");
            Console.WriteLine("  «{0}»: строк {1}, показано «{2}», возвращено «{3}» — {4}",
                              Nai, combo.Items.Count, combo.SelectedItem, chosen,
                              chosen == Nai ? "сошлось" : "РАСХОЖДЕНИЕ");
            bad += combo.Items.Count == crystals + 1 && chosen == Nai ? 0 : 1;

            // ⛔ Имя, пропавшее из библиотеки, обязано ПЕРЕЖИТЬ показ: иначе
            // первое же сохранение сотрёт настройку человека молча.
            device.CrystalMaterialName = "вещества такого нет";
            Call(form, "LoadCrystalMaterial", device);
            chosen = (string)Call(form, "CrystalMaterialFromForm");
            Console.WriteLine("  пропавшее    : строк {0}, возвращено «{1}» — {2}",
                              combo.Items.Count, chosen,
                              chosen == "вещества такого нет" ? "сохранилось, сошлось" : "ПОТЕРЯНО");
            bad += combo.Items.Count == crystals + 2 && chosen == "вещества такого нет" ? 0 : 1;
        }

        return bad;
    }

    // ------------------------------------------------------------------
    // 6. Путь приложения целиком: конфигурация прибора -> спецификация
    // ------------------------------------------------------------------

    static int AppPath()
    {
        Console.WriteLine();
        Console.WriteLine("=== 6. путь приложения: вывод состава берёт имя у прибора ===");
        int bad = 0;

        // ⛔ ТА ЖЕ перегрузка, которой зовёт `FsaAnalysisSession.Compute`:
        // меряем умолчание приложения, а не своё представление о нём. Пиков нет
        // нарочно — состав тут ни при чём, речь о ПЕРЕДАЧЕ вещества.
        var withName = new ResultData();
        withName.DeviceConfig.CrystalMaterialName = Nai;
        FsaCompositionInference.Report report;
        FsaSampleSpec spec = FsaCompositionInference.Infer(
            new List<Peak>(), withName, out report);

        bool carried = spec.CrystalMaterialName == Nai;
        Console.WriteLine("  имя доехало до спецификации: «{0}» — {1}",
                          spec.CrystalMaterialName, carried ? "сошлось" : "РАСХОЖДЕНИЕ");
        bad += carried ? 0 : 1;
        bad += Same("доли по пути приложения", Matter(Nai),
                    FsaSampleLibrary.CrystalFractionsOf(spec));

        // Отрицательный контроль: прибор без вещества — спецификация пуста, и
        // ниоткуда имя не берётся.
        var without = new ResultData();
        FsaSampleSpec bare = FsaCompositionInference.Infer(
            new List<Peak>(), without, out report);
        bool empty = string.IsNullOrEmpty(bare.CrystalMaterialName)
                     && FsaSampleLibrary.CrystalFractionsOf(bare).Count == 0;
        Console.WriteLine("  прибор без вещества: имя «{0}», долей {1} — {2}",
                          bare.CrystalMaterialName,
                          FsaSampleLibrary.CrystalFractionsOf(bare).Count,
                          empty ? "сошлось" : "РАСХОЖДЕНИЕ");
        bad += empty ? 0 : 1;
        return bad;
    }

    // ------------------------------------------------------------------
    // 7. Подпись влезает (`A127`)
    // ------------------------------------------------------------------

    static int LabelFits()
    {
        Console.WriteLine();
        Console.WriteLine("=== 7. подпись поля влезает до списка ===");

        var view = new ComponentResourceManager(typeof(DeviceConfigForm));
        var font = (Font)view.GetObject("$this.Font");
        if (font == null)
        {
            Console.WriteLine("  ⛔ нет $this.Font в ресурсах формы — мерить нечем");
            return 1;
        }

        var labelAt = (Point)view.GetObject("crystalMaterialLabel.Location");
        var comboAt = (Point)view.GetObject("crystalMaterialCombo.Location");
        int room = comboAt.X - labelAt.X;
        Console.WriteLine("  шрифт {0} {1}pt; подпись с {2}, список с {3} — места {4} пкс",
                          font.Name, font.SizeInPoints, labelAt.X, comboAt.X, room);

        int bad = 0;
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
        view.ApplyResources(label, "crystalMaterialLabel");

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
            Console.WriteLine("    ⛔ НЕ ВЛЕЗАЕТ: подпись наедет на список");
            return 1;
        }

        return 0;
    }

    // ------------------------------------------------------------------

    static Dictionary<int, double> Matter(string name)
    {
        GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
        return entry == null
            ? new Dictionary<int, double>()
            : new Dictionary<int, double>(GeometryMaterialLibrary.Make(entry, entry.Density).Fractions);
    }

    static bool Equal(Dictionary<int, double> a, Dictionary<int, double> b)
    {
        if (a.Count != b.Count || a.Count == 0)
        {
            return false;
        }

        foreach (KeyValuePair<int, double> pair in a)
        {
            double have;
            if (!b.TryGetValue(pair.Key, out have) || Math.Abs(have - pair.Value) > 1e-12)
            {
                return false;
            }
        }

        return true;
    }

    static int Same(string what, Dictionary<int, double> expected, Dictionary<int, double> actual)
    {
        bool ok = Equal(expected, actual);
        Console.WriteLine("  {0,-28} {1}", what, ok ? "сошлось поячеечно" : "РАСХОЖДЕНИЕ");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Доля рождения пар в полном ослаблении смеси — ТА ЖЕ величина, что судит
    /// отбор, и посчитана она здесь тем же способом, что в `EscapeMarginProbe`.
    /// </summary>
    static double PairShare(Dictionary<int, double> fractions, double energyKev)
    {
        double logEnergyKev = Math.Log(energyKev);
        double pair = 0.0, total = 0.0;
        foreach (KeyValuePair<int, double> f in fractions)
        {
            MaterialDatabase.Element element;
            int lo, hi;
            if (!(f.Value > 0.0) || !MaterialDatabase.TryGet(f.Key, out element)
                || !MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
            {
                continue;
            }

            pair += f.Value * PartialCrossSections.MassCrossSection(
                element, lo, hi, energyKev, logEnergyKev, PhotonProcess.PairProduction, true);
            total += f.Value * MaterialDatabase.Interpolate(
                element.EnergyKev, element.LogEnergyKev,
                element.Total, element.LogTotal, lo, hi, energyKev, logEnergyKev);
        }

        return total > 0.0 ? pair / total : 0.0;
    }

    static FsaComponent Component(string name, params double[] energyAndIntensity)
    {
        var component = new FsaComponent(name, FsaComponentKind.Single);
        for (int i = 0; i + 1 < energyAndIntensity.Length; i += 2)
        {
            component.Lines.Add(new FsaLine(name, energyAndIntensity[i], energyAndIntensity[i + 1]));
        }

        return component;
    }

    static List<string> Parents(List<FsaComponent> extra)
    {
        var tags = new List<string>();
        foreach (FsaComponent component in extra)
        {
            if (component.Name.StartsWith("SE-", StringComparison.OrdinalIgnoreCase))
            {
                tags.Add(component.Name.Substring(3));
            }
        }

        return tags;
    }

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
