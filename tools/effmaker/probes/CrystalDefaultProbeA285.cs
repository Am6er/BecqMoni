using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// ВЕЩЕСТВО КРИСТАЛЛА ПРИБОРА УМОЛЧАНИЕМ ИЗ ГЕОМЕТРИИ (`AMBER285`, 02.11.2026).
///
///     crystaldefaultprobea285
///
/// Amber, консоль, дословно: «Запиши задачу, если в геометрии у прибора,
/// задаётся кристалл детектора, то брать его умолчанием где указано на
/// картинке. Если создаётся геометрия с другим кристаллом детектора в этом
/// приборе, спрашивать про это умолчание.» Решения вопросником того же дня:
/// «При открытии в окне прибора, файл — вашим «Сохранить» (Рекомендую)» и
/// «При сохранении новой и изменённой геометрии (Рекомендую)».
///
/// Плечи (окна проба не показывает — форма строится вне экрана, как у
/// `CrystalMaterialProbe`; вопросы окна без окон не задаются, это и мерится):
///
/// 1. Разбор кристаллов кривых: один кристалл двумя написаниями — один; кривая
///    без геометрии не в счёт; CsI и NaI — два.
/// 2. Загрузка в окно, поле пустое, один кристалл — список показывает его,
///    прибор помечен изменённым, а поле КОНФИГУРАЦИИ до «Сохранить» пустое;
///    запись формы кладёт имя в поле.
/// 3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: кривая без геометрии — поле не трогается, прибор
///    не изменён (плечо 2 отличает одно состояние от другого).
/// 4. Поле уже задано (NaI), геометрия CsI — загрузка поле не меняет.
/// 5. Разные кристаллы без окон — поле пустое, прибор не изменён, проба не висит.
/// 6. Объект не тот, что правит окно (проба зовёт загрузку отражением на чужом
///    объекте), — поле не заполняется: «заполняет только окно».
/// 7. Отказ («Нет» на «Сохранить?») держится: прибор из набора отказов при
///    повторной загрузке кристалла не получает.
/// 8. Сохранённая геометрия и поле: пустое поле заполняется без вопроса;
///    другое поле без окон не меняется (вопрос задать некому); то же вещество —
///    ничего.
/// 9. «Сохранить» конструктора кривой: признак нового кристалла
///    (`EfficiencyMakerForm.SavedCrystalIsNew`) — да у кривой без геометрии,
///    нет при пересохранении, да при смене вещества кристалла.
///
/// Сборка — общая, `tools/effmaker/probes/build_all.ps1`.
/// Ожидание: «СОШЛОСЬ». Коды возврата: 0 — сошлось; 1 — не сошлось.
/// </summary>
static class CrystalDefaultProbeA285
{
    const string Csi = "Cesium iodide";
    const string Nai = "Sodium iodide";
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static int bad;

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Application.SetCompatibleTextRenderingDefault(false);
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();

        Console.WriteLine("окна: AppUi.HasWindows = {0} (ожидается False — проба)", AppUi.HasWindows);
        Ok(!AppUi.HasWindows, "проба идёт без окон");

        Parse();
        using (var form = new DeviceConfigForm())
        {
            LoadOne(form);
            LoadNoGeometry(form);
            LoadFieldSet(form);
            LoadTwo(form);
            LoadForeign(form);
            LoadDeclined(form);
            SavedGeometry(form);
        }
        MakerFlag();

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: кристалл геометрии — умолчание поля прибора только в окне"
            : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------

    static void Parse()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. кристаллы кривых прибора ===");
        var one = Device("", Csi, Csi.ToLowerInvariant(), null);
        var list = Crystals(one);
        Console.WriteLine("  CsI, cesium iodide, без геометрии: кристаллов {0}, у первого кривых {1}",
                          list.Count, list.Count > 0 ? Curves(list[0]).Count : 0);
        Ok(list.Count == 1 && Curves(list[0]).Count == 2, "одно вещество двумя написаниями — один кристалл, две кривые");

        var two = Device("", Csi, Nai);
        list = Crystals(two);
        Console.WriteLine("  CsI, NaI: кристаллов {0}", list.Count);
        Ok(list.Count == 2, "CsI и NaI — два кристалла");

        var none = Device("", (string)null);
        Ok(Crystals(none).Count == 0, "кривая без геометрии — кристаллов нет");
    }

    static void LoadOne(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. загрузка: поле пустое, у геометрии CsI ===");
        var device = Device("", Csi, Csi);
        Load(form, device);
        string shown = Shown(form);
        Console.WriteLine("  список: «{0}», выбрано «{1}», изменён {2}, поле конфигурации «{3}»",
                          Combo(form).SelectedItem, shown, device.Dirty, device.CrystalMaterialName);
        Ok(shown == Csi, "список показывает кристалл геометрии");
        Ok(device.Dirty, "прибор помечен изменённым");
        Ok(string.IsNullOrEmpty(device.CrystalMaterialName), "поле конфигурации до «Сохранить» пустое");

        bool saved = (bool)Call(form, "SaveFormContents", device);
        Console.WriteLine("  запись формы: {0}, поле конфигурации «{1}»", saved, device.CrystalMaterialName);
        Ok(saved && device.CrystalMaterialName == Csi, "«Сохранить» кладёт имя в поле");

        // Повторная загрузка сохранённого — поле уже задано, изменённым не помечается.
        device.Dirty = false;
        Load(form, device);
        Ok(Shown(form) == Csi && !device.Dirty, "сохранённый прибор: CsI, не изменён");
    }

    static void LoadNoGeometry(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. КОНТРОЛЬ: кривая без геометрии ===");
        var device = Device("", (string)null);
        Load(form, device);
        Console.WriteLine("  выбрано «{0}», изменён {1}", Shown(form), device.Dirty);
        Ok(Shown(form) == "" && !device.Dirty, "поле не тронуто, прибор не изменён");

        var bare = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "без кривых" };
        Load(form, bare);
        Ok(Shown(form) == "" && !bare.Dirty, "прибор без кривых: поле не тронуто");
    }

    static void LoadFieldSet(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. поле задано (NaI), геометрия CsI ===");
        var device = Device(Nai, Csi);
        Load(form, device);
        Console.WriteLine("  выбрано «{0}», изменён {1}", Shown(form), device.Dirty);
        Ok(Shown(form) == Nai && !device.Dirty, "заданное поле загрузка не меняет");
    }

    static void LoadTwo(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. разные кристаллы, без окон ===");
        var device = Device("", Csi, Nai);
        Load(form, device);
        Console.WriteLine("  выбрано «{0}», изменён {1}", Shown(form), device.Dirty);
        Ok(Shown(form) == "" && !device.Dirty, "само не выбрано, спросить некому — поле пустое");
    }

    static void LoadForeign(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 6. загрузка чужого объекта (не активного в окне) ===");
        var device = Device("", Csi);
        form.ActiveDeviceConfig = null;
        Call(form, "LoadFormContents", device);
        Console.WriteLine("  выбрано «{0}», изменён {1}", Shown(form), device.Dirty);
        Ok(Shown(form) == "" && !device.Dirty, "поле не заполнено");
    }

    static void LoadDeclined(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 7. отказ «Нет» держится ===");
        var device = Device("", Csi);
        var declined = (HashSet<string>)typeof(DeviceConfigForm).GetField("crystalDefaultDeclined", Any).GetValue(form);
        declined.Add(device.Guid);
        Load(form, device);
        Console.WriteLine("  выбрано «{0}», изменён {1}", Shown(form), device.Dirty);
        Ok(Shown(form) == "" && !device.Dirty, "прибор с отказом кристалла не получает");
        declined.Remove(device.Guid);
        Load(form, device);
        Ok(Shown(form) == Csi, "контроль: без отказа — получает");
    }

    static void SavedGeometry(DeviceConfigForm form)
    {
        Console.WriteLine();
        Console.WriteLine("=== 8. сохранённая геометрия и поле прибора ===");

        var empty = Device("", (string)null);
        Load(form, empty);
        var curve = new EfficiencyConfigData("новая") { Geometry = Geometry(Nai) };
        empty.EfficiencyConfigs.Add(curve);
        Offer(form, curve);
        Console.WriteLine("  поле пустое, геометрия NaI: выбрано «{0}», изменён {1}", Shown(form), empty.Dirty);
        Ok(Shown(form) == Nai && empty.Dirty, "пустое поле заполнено без вопроса");

        var set = Device(Csi, (string)null);
        Load(form, set);
        var other = new EfficiencyConfigData("другая") { Geometry = Geometry(Nai) };
        set.EfficiencyConfigs.Add(other);
        Offer(form, other);
        Console.WriteLine("  поле CsI, геометрия NaI, без окон: выбрано «{0}», изменён {1}", Shown(form), set.Dirty);
        Ok(Shown(form) == Csi && !set.Dirty, "без окон вопроса нет — поле прежнее");

        var same = new EfficiencyConfigData("та же") { Geometry = Geometry(Csi.ToUpperInvariant()) };
        set.EfficiencyConfigs.Add(same);
        Offer(form, same);
        Ok(Shown(form) == Csi && !set.Dirty, "то же вещество — ничего");
    }

    static void MakerFlag()
    {
        Console.WriteLine();
        Console.WriteLine("=== 9. «Сохранить» конструктора: новый ли кристалл ===");
        var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "конструктор" };
        var curve = new EfficiencyConfigData("кривая");
        device.EfficiencyConfigs.Add(curve);
        using (var maker = new EfficiencyMakerForm())
        {
            maker.BindTo(device, curve);
            bool saved = Save(maker);
            Console.WriteLine("  без геометрии -> заготовка «{0}»: записано {1}, новый {2}",
                              CrystalName(curve.Geometry), saved, maker.SavedCrystalIsNew);
            Ok(saved && maker.SavedCrystalIsNew, "кривая без геометрии — кристалл новый");

            saved = Save(maker);
            Ok(saved && !maker.SavedCrystalIsNew, "пересохранение той же геометрии — не новый");

            string panelCrystal = CrystalName(curve.Geometry);
            curve.Geometry.Crystal.Name = string.Equals(panelCrystal, Nai, StringComparison.OrdinalIgnoreCase) ? Csi : Nai;
            saved = Save(maker);
            Console.WriteLine("  в кривой «{0}», в полях «{1}»: записано {2}, новый {3}",
                              panelCrystal == Nai ? Csi : Nai, panelCrystal, saved, maker.SavedCrystalIsNew);
            Ok(saved && maker.SavedCrystalIsNew, "вещество кристалла сменилось — новый");
        }
    }

    // ------------------------------------------------------------------
    // Оснастка
    // ------------------------------------------------------------------

    static DeviceConfigInfo Device(string field, params string[] crystals)
    {
        var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба A285" };
        device.CrystalMaterialName = field;
        int i = 0;
        foreach (string crystal in crystals)
        {
            var curve = new EfficiencyConfigData("кривая " + (++i).ToString(CultureInfo.InvariantCulture));
            if (crystal != null)
            {
                curve.Geometry = Geometry(crystal);
            }

            device.EfficiencyConfigs.Add(curve);
        }

        return device;
    }

    static GeometryModel Geometry(string crystal)
    {
        var g = new GeometryModel();
        g.Crystal = new GeometryMaterial { Name = crystal };
        return g;
    }

    static void Load(DeviceConfigForm form, DeviceConfigInfo device)
    {
        form.ActiveDeviceConfig = device;
        Call(form, "LoadFormContents", device);
    }

    static void Offer(DeviceConfigForm form, EfficiencyConfigData curve)
    {
        typeof(DeviceConfigForm).GetMethod("OfferCrystalOfSavedGeometry", Any)
            .Invoke(form, new object[] { null, curve });
    }

    static bool Save(EfficiencyMakerForm maker)
    {
        object panel = typeof(EfficiencyMakerForm).GetField("geometryPanel", Any).GetValue(maker);
        Type pt = panel.GetType();
        bool marks = (bool)pt.GetMethod("MarkBadValues", Any).Invoke(panel, null);
        object g = pt.GetMethod("BuildModel", Any).Invoke(panel, null);
        string error = (string)pt.GetMethod("Validate", Any, null, new[] { typeof(GeometryModel) }, null)
            .Invoke(panel, new[] { g });
        if (!marks || error != null)
        {
            Console.WriteLine("  ⛔ поля геометрии негодны: " + (error ?? "неверное число"));
            return false;
        }

        return (bool)typeof(EfficiencyMakerForm).GetMethod("SaveIntoConfig", Any).Invoke(maker, null);
    }

    static string CrystalName(GeometryModel geometry)
    {
        return (string)typeof(DeviceConfigForm).GetMethod("CrystalNameOf", Any)
            .Invoke(null, new object[] { geometry });
    }

    static System.Collections.IList Crystals(DeviceConfigInfo device)
    {
        return (System.Collections.IList)typeof(DeviceConfigForm).GetMethod("GeometryCrystalsOf", Any)
            .Invoke(null, new object[] { device });
    }

    static List<string> Curves(object crystal)
    {
        return (List<string>)crystal.GetType().GetField("Curves", Any).GetValue(crystal);
    }

    static ComboBox Combo(DeviceConfigForm form)
    {
        return (ComboBox)typeof(DeviceConfigForm).GetField("crystalMaterialCombo", Any).GetValue(form);
    }

    static string Shown(DeviceConfigForm form)
    {
        return (string)Call(form, "CrystalMaterialFromForm");
    }

    static object Call(object target, string method, params object[] args)
    {
        try
        {
            return target.GetType().GetMethod(method, Any).Invoke(target, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    static void Ok(bool condition, string what)
    {
        Console.WriteLine("    {0} {1}", condition ? "ok " : "НЕТ", what);
        if (!condition)
        {
            bad++;
        }
    }
}
