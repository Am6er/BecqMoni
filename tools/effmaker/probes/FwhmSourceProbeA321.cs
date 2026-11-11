using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

/// <summary>
/// ОТКУДА РАЗРЕШЕНИЕ ГЕОМЕТРИИ: ПРЕСЕТ ПО ВЕЩЕСТВУ КРИСТАЛЛА И ВОПРОС ИЗ КАЛИБРОВКИ ПРИБОРА
/// (`AMBER321`, полоса a321, 11.11.2026).
///
/// Постановка Amber 07.11.2026, дословно: «Это нужно проверить. Главный вопрос - откуда брать
/// разрешение. На нулевом этапе (нет ничего) нужно знать разрешение - нужно иметь пресет готовых
/// разрешений из химии кристалла. При редактировании - должно подтягиваться из калибровки
/// устройства с вопросом.»
///
///     FwhmSourceProbeA321.exe
///         самопроверка (без окон): пресет и вопрос. Ожидание «СОШЛОСЬ», код 0; иначе 1.
///     FwhmSourceProbeA321.exe --measure --corpus=&lt;tools\CORPUS\corpus&gt; [--cfg=&lt;каталог device\*.xml&gt;] --out=&lt;каталог&gt;
///         замер: число кнопки «Из калибровки прибора» против FWHM пика по спектру;
///         числа шаблонов против групп корпуса. Таблицы — в &lt;out&gt;\*.csv и в консоль.
///
/// Плечи самопроверки:
///   А. Пресет: новая геометрия получает число своего кристалла; смена вещества — число нового;
///      вещество без пресета (германий) — ноль; тот же кристалл повторно — без замены.
///   Б. НЕ перетирает: число человека, калибровки (кнопка и ответ «Да»), открытой геометрии.
///      Шаблон детектора — заменяется при смене вещества.
///   В. Вопрос: расхождение больше 0.1 п.п. — один вопрос с обоими числами; ровно 0.1 и меньше —
///      нет; без калибровки и при встроенной заглушке FWHM — нет; второй раз в том же
///      открытии — нет.
///   Г. «Да» меняет поле и помечает кривую к пересчёту: после «Сохранить» отпечаток расходится,
///      `EfficiencyStaleMark.CurveStale` — true. «Нет» — поле прежнее, окно не изменено.
///   Д. (решение Amber 11.11.2026 «Писать признак в конфиг») Признак `GeometryModel.FwhmOrigin`
///      пишется в XML, кроме «неизвестно»; старый файл без элемента — «неизвестно», число задано;
///      клеймо матрицы и отпечаток кривой от признака не зависят; после записи-чтения пресет и
///      шаблон сменой вещества заменяются, число человека и калибровки — нет.
///   Е. (решение Amber 11.11.2026 «Только при кривой из 3+ опор») «Три числа» и кривая из двух
///      опор — без вопроса (кнопка есть); кривая из трёх и четырёх опор — с вопросом.
///   Положительный контроль — сами плечи с ожиданием «есть»: испорченный код (пресет поверх
///   числа человека, порог «не меньше», признак не пишется, вопрос при «трёх числах») проба
///   обязана поймать (замер в журнале полосы).
/// </summary>
static class FwhmSourceProbeA321
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly double Nai = CrystalResolutionPresets.Find("Sodium iodide").FwhmAt662Percent;
    static int bad;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = Inv;
        // Тексты вопроса проверяются по-английски: машина может стоять на русском интерфейсе.
        System.Threading.Thread.CurrentThread.CurrentUICulture = Inv;
        Application.SetCompatibleTextRenderingDefault(false);
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();

        if (args.Contains("--measure"))
        {
            return Measure(args);
        }

        if (args.Contains("--shown"))
        {
            return ShownArm();
        }

        Presets();
        Question();
        OriginInConfig();
        Anchors();

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: пресет вещества и вопрос из калибровки ведут себя по постановке"
            : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(Inv));
        return bad == 0 ? 0 : 1;
    }

    static void Ok(bool condition, string what)
    {
        Console.WriteLine("  {0} {1}", condition ? "да " : "НЕТ", what);
        if (!condition)
        {
            bad++;
        }
    }

    static string F(double v)
    {
        return double.IsNaN(v) ? "NaN" : v.ToString("0.###", Inv);
    }

    // ------------------------------------------------------------------
    // А, Б — пресет в редакторе
    // ------------------------------------------------------------------

    static void Presets()
    {
        Console.WriteLine("=== А. пресет по веществу кристалла ===");
        foreach (CrystalResolutionPresets.Entry e in CrystalResolutionPresets.Items)
        {
            Console.WriteLine("  таблица: {0,-24} {1,5} %  {2}", e.Material, F(e.FwhmAt662Percent), e.Source);
        }

        Ok(CrystalResolutionPresets.Find("Germanium") == null, "германия в таблице нет");
        Ok(CrystalResolutionPresets.Find("sodium IODIDE") != null, "имя ищется без учёта регистра");

        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            panel.CreateControl();
            double csi = CrystalResolutionPresets.Find("Cesium iodide").FwhmAt662Percent;
            Console.WriteLine("  новая геометрия: кристалл {0}, поле {1}, откуда {2}, изменена {3}",
                              panel.Model.Crystal.Name, F(panel.FwhmFieldPercent), panel.FwhmOrigin, panel.Dirty);
            Ok(Near(panel.FwhmFieldPercent, csi) && panel.FwhmOrigin == GeometryFwhmOrigin.Preset,
               "новая геометрия — пресет её кристалла (CsI)");
            Ok(Near(panel.Model.FwhmAt662Percent, csi), "модель новой геометрии несёт то же число");
            Ok(!panel.Dirty, "пресет новой геометрии — не правка (окно не «изменено»)");

            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, Nai) && panel.FwhmOrigin == GeometryFwhmOrigin.Preset,
               "смена на NaI — пресет NaI " + F(Nai) + " (поле " + F(panel.FwhmFieldPercent) + ")");
            SelectCrystal(panel, "Bismuth germanate");
            Ok(Near(panel.FwhmFieldPercent, 10.0), "смена на BGO — 10 (поле " + F(panel.FwhmFieldPercent) + ")");
            SelectCrystal(panel, "Germanium");
            Ok(Near(panel.FwhmFieldPercent, 0.0) && panel.FwhmOrigin == GeometryFwhmOrigin.Unset,
               "германий — пресета нет, ноль (поле " + F(panel.FwhmFieldPercent) + ", " + panel.FwhmOrigin + ")");
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, Nai), "с нуля снова NaI — пресет");

            Console.WriteLine("=== Б. число человека, калибровки и файла пресет не трогает ===");
            Field(panel).Text = "8.1";
            Ok(panel.FwhmOrigin == GeometryFwhmOrigin.User, "набор руками — «человек»");
            SelectCrystal(panel, "Cesium iodide");
            Ok(Near(panel.FwhmFieldPercent, 8.1), "человек 8.1, смена на CsI — осталось " + F(panel.FwhmFieldPercent));

            panel.ApplyFwhmFromCalibration(7.4);
            Ok(panel.FwhmOrigin == GeometryFwhmOrigin.Calibration && Near(panel.FwhmFieldPercent, 7.4),
               "из калибровки 7.4 — «калибровка»");
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, 7.4), "калибровка 7.4, смена на NaI — осталось " + F(panel.FwhmFieldPercent));

            // Кнопка «Из калибровки прибора» — тем же ходом.
            panel.SetModel(null);
            panel.SetFwhmSuggestion(6.123);
            Button button = (Button)typeof(GeometryEditorPanel).GetField("fwhmSuggestButton", Any).GetValue(panel);
            button.PerformClick();
            Ok(Near(panel.FwhmFieldPercent, 6.123) && panel.FwhmOrigin == GeometryFwhmOrigin.Calibration,
               "кнопка ставит 6.123 и «калибровку»");
            SelectCrystal(panel, "Bismuth germanate");
            Ok(Near(panel.FwhmFieldPercent, 6.123), "после кнопки смена на BGO — осталось " + F(panel.FwhmFieldPercent));

            // Открытая геометрия со своим числом (старый конфиг) — задано.
            GeometryModel old = GeometryEditorPanel.Blank();
            old.FwhmAt662Percent = 8.49;
            panel.SetModel(old);
            Ok(panel.FwhmOrigin == GeometryFwhmOrigin.Loaded && Near(panel.FwhmFieldPercent, 8.49),
               "старый конфиг 8.49 — «из файла», поле 8.49");
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, 8.49), "старый конфиг, смена на NaI — осталось " + F(panel.FwhmFieldPercent));

            // Открытая геометрия с нулём — НЕ подставляется при открытии, подставляется при смене.
            GeometryModel zero = GeometryEditorPanel.Blank();
            zero.FwhmAt662Percent = 0.0;
            panel.SetModel(zero);
            Ok(panel.FwhmOrigin == GeometryFwhmOrigin.Unset && Near(panel.FwhmFieldPercent, 0.0),
               "открытая геометрия с нулём — открытие поле не меняет");
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, Nai), "та же, смена на NaI — пресет " + F(Nai));

            // Шаблон: его число; смена вещества — пресет нового.
            SelectTemplate(panel, "RadiaCode-103");
            Ok(Near(panel.FwhmFieldPercent, 8.49) && panel.FwhmOrigin == GeometryFwhmOrigin.Template,
               "шаблон RadiaCode-103 — 8.49, «шаблон»");
            SelectCrystal(panel, "Cesium iodide");
            Ok(Near(panel.FwhmFieldPercent, 8.49), "тот же кристалл повторно — число шаблона на месте");
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, Nai), "шаблон, смена на NaI — пресет " + F(Nai));
        }
    }

    static bool Near(double a, double b)
    {
        return Math.Abs(a - b) < 1e-9;
    }

    static TextBox Field(GeometryEditorPanel panel)
    {
        var fields = (Dictionary<string, TextBox>)typeof(GeometryEditorPanel).GetField("fields", Any).GetValue(panel);
        return fields["FwhmAt662Percent"];
    }

    static void SelectCrystal(GeometryEditorPanel panel, string name)
    {
        var combos = (Dictionary<string, ComboBox>)typeof(GeometryEditorPanel).GetField("materials", Any).GetValue(panel);
        ComboBox combo = combos["Crystal"];
        for (int i = 0; i < combo.Items.Count; i++)
        {
            var entry = combo.Items[i] as GeometryMaterialLibrary.Entry;
            if (entry != null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (combo.SelectedIndex == i)
                {
                    // Тот же пункт: событие руками, как при перезаполнении списка.
                    typeof(GeometryEditorPanel).GetMethod("MaterialChanged", Any).Invoke(panel, new object[] { "Crystal" });
                }
                else
                {
                    combo.SelectedIndex = i;
                }

                return;
            }
        }

        throw new InvalidOperationException("в списке кристаллов нет «" + name + "»");
    }

    static void SelectTemplate(GeometryEditorPanel panel, string name)
    {
        ComboBox combo = (ComboBox)typeof(GeometryEditorPanel).GetField("presetCombo", Any).GetValue(panel);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            var preset = combo.Items[i] as GeometryPresets.Preset;
            if (preset != null && preset.Name == name)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        throw new InvalidOperationException("в списке шаблонов нет «" + name + "»");
    }

    // ------------------------------------------------------------------
    // В, Г — вопрос при открытии
    // ------------------------------------------------------------------

    static readonly List<string> Asked = new List<string>();
    static bool answer;

    static void Question()
    {
        Console.WriteLine("=== В. вопрос: порог ===");
        MethodInfo needed = typeof(EfficiencyMakerForm).GetMethod("FwhmQuestionNeeded", Any);
        Func<double, double, bool> need = (c, f) => (bool)needed.Invoke(null, new object[] { c, f });
        Ok(need(7.75, 7.65) == false, "7.75 против 7.65 (ровно 0.1) — нет");
        Ok(need(7.76, 7.65) == true, "7.76 против 7.65 (0.11) — да");
        Ok(need(7.65, 7.70) == false, "7.65 против 7.70 (0.05) — нет");
        Ok(need(7.65, 0.0) == true, "калибровка 7.65, поле 0 — да");
        Ok(need(0.0, 7.65) == false, "калибровки нет — нет");
        Ok(need(7.65, double.NaN) == true, "поле не читается — как ноль, да");

        AppUi.Probe = (kind, text, caption) =>
        {
            Asked.Add(kind + ": " + text.Replace("\r", "").Replace("\n", " / "));
            return answer;
        };
        try
        {
            // Вопрос задаётся только при кривой из 3+ опор (решение Amber 11.11.2026) — прибор с кривой.
            DeviceConfigInfo device = DeviceCurve(4, out double calibration);
            Console.WriteLine("  прибор пробы: кривая из 4 опор, калибровка даёт {0} % на 662 кэВ", F(calibration));
            Ok(calibration > 3.0 && calibration < 15.0, "число калибровки правдоподобно");

            // Нет вопроса при совпадении до 0.1 п.п.
            Open(device, Math.Round(calibration, 3) + 0.05, false, out _, out _);
            Ok(Asked.Count == 0, "расхождение 0.05 — вопроса нет");
            Open(device, calibration + 0.1, false, out _, out _);
            Ok(Asked.Count == 0, "расхождение 0.1 — вопроса нет");

            // Нет калибровки — нет вопроса.
            DeviceConfigInfo stand = Device(out _);
            ((FWHMPeakDetectionMethodConfig)stand.PeakDetectionMethodConfig).FwhmCalibration =
                new FWHMPeakDetectionMethodConfig().FwhmCalibration;
            Open(stand, 1.0, false, out _, out _);
            Ok(Asked.Count == 0, "встроенная заглушка FWHM — не калибровка, вопроса нет");
            Open(null, 1.0, false, out _, out _);
            Ok(Asked.Count == 0, "прибора нет — вопроса нет");

            Console.WriteLine("=== Г. ответ «Нет» и «Да» ===");
            answer = false;
            Open(device, 8.49, true, out EfficiencyMakerForm noForm, out EfficiencyConfigData noCurve);
            Ok(Asked.Count == 1, "расхождение — один вопрос (" + Asked.Count + ")");
            if (Asked.Count > 0)
            {
                Console.WriteLine("  вопрос: " + Asked[0]);
                Ok(Asked[0].Contains("8.49") && Asked[0].Contains(CrystalResolutionPresets.FieldText(calibration) + " %"),
                   "в вопросе оба числа");
                Ok(Asked[0].Contains("recalculated"), "кривая есть — в вопросе о пересчёте");
            }
            using (noForm)
            {
                GeometryEditorPanel p = Panel(noForm);
                Ok(Near(p.FwhmFieldPercent, 8.49) && !Dirty(noForm), "«Нет» — поле 8.49, окно не изменено");
                Invoke(noForm, "OfferFwhmFromCalibration");
                Ok(Asked.Count == 1, "второй раз в том же открытии — не спрашивается");
                noForm.Rebind(noForm.BoundDevice, noCurve);
                Invoke(noForm, "OfferFwhmFromCalibration");
                Ok(Asked.Count == 1, "после перепривязки — не спрашивается");
            }

            Asked.Clear();
            answer = true;
            Open(device, 8.49, true, out EfficiencyMakerForm yesForm, out EfficiencyConfigData curve);
            using (yesForm)
            {
                GeometryEditorPanel p = Panel(yesForm);
                Ok(Asked.Count == 1, "вопрос задан");
                Ok(Math.Abs(p.FwhmFieldPercent - calibration) < 0.0006 && p.FwhmOrigin == GeometryFwhmOrigin.Calibration,
                   "«Да» — поле " + F(p.FwhmFieldPercent) + " из калибровки");
                Ok(Dirty(yesForm), "«Да» — окно изменено, «Сохранить» нужна");
                Ok(!curve.CurveGeometryMismatch, "до «Сохранить» конфигурация не тронута");
                bool saved = (bool)typeof(EfficiencyMakerForm).GetMethod("SaveIntoConfig", Any).Invoke(yesForm, null);
                bool stale = (bool)Type.GetType("BecquerelMonitor.EfficiencyStaleMark, BecquerelMonitor")
                    .GetMethod("CurveStale", Any).Invoke(null, new object[] { curve });
                Console.WriteLine("  сохранено {0}, FWHM в конфигурации {1}, отпечаток расходится {2}, «!» {3}",
                                  saved, F(curve.Geometry.FwhmAt662Percent), curve.CurveGeometryMismatch, stale);
                Ok(saved && Math.Abs(curve.Geometry.FwhmAt662Percent - calibration) < 0.0006, "в конфигурации — число калибровки");
                Ok(curve.Geometry.FwhmOrigin == GeometryFwhmOrigin.Calibration,
                   "в конфигурации — признак «Calibration» (" + curve.Geometry.FwhmOrigin + ")");
                Ok(curve.CurveGeometryMismatch && stale, "кривая помечена к пересчёту («!» вкладки прибора)");
            }

            // Новая геометрия (кривой нет): вопрос без строки о пересчёте.
            Asked.Clear();
            answer = false;
            Open(device, double.NaN, false, out EfficiencyMakerForm fresh, out _);
            using (fresh)
            {
                Ok(Asked.Count == 1 && !Asked[0].Contains("recalculated"),
                   "новая геометрия (пресет CsI против калибровки) — вопрос без строки о пересчёте");
            }
        }
        finally
        {
            AppUi.Probe = null;
        }
    }

    static GeometryEditorPanel Panel(EfficiencyMakerForm form)
    {
        return (GeometryEditorPanel)typeof(EfficiencyMakerForm).GetField("geometryPanel", Any).GetValue(form);
    }

    static bool Dirty(EfficiencyMakerForm form)
    {
        return (bool)typeof(EfficiencyMakerForm).GetField("dirty", Any).GetValue(form);
    }

    static void Invoke(EfficiencyMakerForm form, string method)
    {
        typeof(EfficiencyMakerForm).GetMethod(method, Any).Invoke(form, null);
    }

    /// <summary>
    /// Открыть конструктор на кривой с геометрией FWHM = <paramref name="fwhm"/> (NaN — без
    /// геометрии) и сделать то, что делает показ окна: <c>Shown</c> зовёт вопрос.
    /// </summary>
    static void Open(DeviceConfigInfo device, double fwhm, bool keep, out EfficiencyMakerForm form, out EfficiencyConfigData curve)
    {
        curve = new EfficiencyConfigData("кривая A321");
        if (!double.IsNaN(fwhm))
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            g.FwhmAt662Percent = fwhm;
            curve.Geometry = g;
            // Кривая с отпечатком своей геометрии — как посчитанная.
            curve.Curve = new List<ROIEfficiencyData> { new ROIEfficiencyData { Energy = 100.0, Efficiency = 0.1 }, new ROIEfficiencyData { Energy = 1000.0, Efficiency = 0.01 } };
            curve.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(g);
        }

        if (device != null)
        {
            device.EfficiencyConfigs.Add(curve);
        }

        form = new EfficiencyMakerForm();
        form.BindTo(device ?? new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "без калибровки" }, curve);
        if (device == null)
        {
            typeof(EfficiencyMakerForm).GetField("fwhmCalibrationPercent", Any).SetValue(form, 0.0);
        }

        Invoke(form, "OfferFwhmFromCalibration");
        if (!keep)
        {
            form.Dispose();
            form = null;
        }
    }

    /// <summary>Прибор пробы: шкала 3 кэВ/канал на 1024 канала, кривая FWHM по трём числам.</summary>
    static DeviceConfigInfo Device(out double calibration)
    {
        var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба A321" };
        var energy = new PolynomialEnergyCalibration();
        energy.PolynomialOrder = 1;
        energy.Coefficients = new[] { 0.0, 3.0 };
        device.EnergyCalibration = energy;
        device.NumberOfChannels = 1024;
        var cfg = new FWHMPeakDetectionMethodConfig { FWHM_AT_0 = 4.0, Ch_Fwhm = 512, Width_Fwhm = 22.0 };
        cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, energy);
        device.PeakDetectionMethodConfig = cfg;
        calibration = (double)typeof(EfficiencyMakerForm).GetMethod("FwhmPercentAt662", Any, null,
            new[] { typeof(DeviceConfigInfo) }, null).Invoke(null, new object[] { device });
        return device;
    }

    /// <summary>
    /// Тот же прибор, но FWHM-калибровка — кривая из <paramref name="anchors"/> опор: ширины
    /// сняты с кривой «трёх чисел» на равных шагах каналов, кривая заново построена по ним.
    /// </summary>
    static DeviceConfigInfo DeviceCurve(int anchors, out double calibration)
    {
        DeviceConfigInfo device = Device(out _);
        var cfg = (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
        FwhmCalibration three = cfg.FwhmCalibration;
        var curve = new SimpleSqrtFwhmCalibration();
        for (int i = 0; i < anchors; i++)
        {
            int ch = 100 + i * 800 / Math.Max(1, anchors - 1);
            curve.CalibrationPeaks.Add(new CalibrationPeak
            {
                Channel = ch, Energy = device.EnergyCalibration.ChannelToEnergy(ch), FWHM = three.ChannelToFwhm(ch),
            });
        }

        if (!curve.PerformCalibration(1024))
        {
            throw new InvalidOperationException("кривая из " + anchors + " опор не строится");
        }

        cfg.FwhmCalibration = curve;
        calibration = (double)typeof(EfficiencyMakerForm).GetMethod("FwhmPercentAt662", Any, null,
            new[] { typeof(DeviceConfigInfo) }, null).Invoke(null, new object[] { device });
        return device;
    }

    static int Anchors(DeviceConfigInfo device)
    {
        return (int)typeof(EfficiencyMakerForm).GetMethod("FwhmCalibrationAnchors", Any).Invoke(null, new object[] { device });
    }

    // ------------------------------------------------------------------
    // Ж — вопрос через настоящий показ окна (замечание критика crit321)
    // ------------------------------------------------------------------

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetThreadDesktop(uint threadId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);

    static void Pump(int ms)
    {
        for (int t = 0; t < ms; t += 50)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }
    }

    /// <summary>
    /// ⛔ ТОЛЬКО НА СКРЫТОМ СТОЛЕ: <c>HiddenRun.exe 300 FwhmSourceProbeA321.exe --shown</c>. Окно конструктора
    /// показывается (<c>Show</c>), и вопрос обязан прийти сам — по событию <c>Shown</c>; второе плечо —
    /// привязка к уже показанному окну (<c>BeginInvoke</c> из <c>BindTo</c>); третье — «три числа» без вопроса.
    /// На обычном столе (имя «Default») плечо отказывает кодом 3 и окна не открывает.
    /// </summary>
    static int ShownArm()
    {
        var name = new StringBuilder(256);
        int needed;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out needed);
        Console.WriteLine("=== Ж. вопрос через показ окна; стол «{0}» ===", name);
        if (string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("ОТКАЗ: плечо открывает окна и идёт только на скрытом столе (HiddenRun.exe)");
            return 3;
        }

        Application.EnableVisualStyles();
        AppUi.Probe = (kind, text, caption) => { Asked.Add(kind + ": " + text); return true; };
        try
        {
            DeviceConfigInfo device = DeviceCurve(4, out double calibration);

            // 1. Привязка до показа, вопрос — по Shown.
            Asked.Clear();
            var curve = new EfficiencyConfigData("показ");
            GeometryModel g = GeometryEditorPanel.Blank();
            g.FwhmAt662Percent = 8.49;
            curve.Geometry = g;
            device.EfficiencyConfigs.Add(curve);
            var form = new EfficiencyMakerForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0) };
            form.BindTo(device, curve);
            Ok(Asked.Count == 0, "до показа вопроса нет");
            form.Show();
            Pump(1500);
            GeometryEditorPanel panel = Panel(form);
            Ok(Asked.Count == 1, "Show → Shown → один вопрос (" + Asked.Count + ")");
            Ok(Math.Abs(panel.FwhmFieldPercent - calibration) < 0.0006 && panel.FwhmOrigin == GeometryFwhmOrigin.Calibration,
               "«Да» по показу — поле " + F(panel.FwhmFieldPercent));
            AppUi.Probe = null;
            form.Close();
            form.Dispose();

            // 2. Привязка к уже показанному окну — BeginInvoke из BindTo.
            AppUi.Probe = (kind, text, caption) => { Asked.Add(kind + ": " + text); return false; };
            Asked.Clear();
            var curve2 = new EfficiencyConfigData("после показа");
            curve2.Geometry = g.Clone();
            device.EfficiencyConfigs.Add(curve2);
            var form2 = new EfficiencyMakerForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0) };
            form2.Show();
            Pump(500);
            int beforeBind = Asked.Count;
            form2.BindTo(device, curve2);
            Pump(1000);
            Ok(beforeBind == 0 && Asked.Count == 1, "привязка к показанному окну — один вопрос (" + beforeBind + " → " + Asked.Count + ")");
            AppUi.Probe = null;
            form2.Close();
            form2.Dispose();

            // 3. «Три числа» — показ без вопроса.
            AppUi.Probe = (kind, text, caption) => { Asked.Add(kind + ": " + text); return false; };
            Asked.Clear();
            DeviceConfigInfo three = Device(out _);
            var curve3 = new EfficiencyConfigData("три числа");
            curve3.Geometry = g.Clone();
            three.EfficiencyConfigs.Add(curve3);
            var form3 = new EfficiencyMakerForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0) };
            form3.BindTo(three, curve3);
            form3.Show();
            Pump(1000);
            Ok(Asked.Count == 0, "«три числа» — показ без вопроса");
            AppUi.Probe = null;
            form3.Close();
            form3.Dispose();
        }
        finally
        {
            AppUi.Probe = null;
        }

        Console.WriteLine(bad == 0 ? "СОШЛОСЬ: вопрос приходит с показом окна" : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(Inv));
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // Д — признак происхождения в конфигурации (решение Amber 11.11.2026)
    // ------------------------------------------------------------------

    static string Xml(GeometryModel g)
    {
        var s = new XmlSerializer(typeof(GeometryModel));
        using (var w = new StringWriter(Inv))
        {
            s.Serialize(w, g);
            return w.ToString();
        }
    }

    static GeometryModel FromXml(string xml)
    {
        var s = new XmlSerializer(typeof(GeometryModel));
        using (var r = new StringReader(xml))
        {
            return (GeometryModel)s.Deserialize(r);
        }
    }

    /// <summary>Положить поля панели в модель (как «Сохранить») и пропустить её через XML.</summary>
    static GeometryModel Commit(GeometryEditorPanel panel)
    {
        bool ok = panel.TryCommit();
        if (!ok) throw new InvalidOperationException("TryCommit отказал");
        return FromXml(Xml(panel.Model));
    }

    static void OriginInConfig()
    {
        Console.WriteLine("=== Д. признак происхождения в конфигурации ===");
        GeometryModel g = GeometryEditorPanel.Blank();
        g.FwhmAt662Percent = 7.0;
        string legacy = Xml(g);
        Ok(!legacy.Contains("FwhmOrigin"), "«неизвестно» не пишется — старый формат побайтно прежний");
        Ok(FromXml(legacy).FwhmOrigin == GeometryFwhmOrigin.Unknown, "файл без элемента читается как «неизвестно»");

        // Клеймо и отпечаток кривой от признака не зависят.
        var options = new ResponseMatrixOptions();
        string stamp0 = ResponseMatrix.ComputeStamp(g, options), print0 = ResponseMatrix.GeometryFingerprint(g);
        bool same = true;
        foreach (GeometryFwhmOrigin o in Enum.GetValues(typeof(GeometryFwhmOrigin)))
        {
            GeometryModel c = g.Clone();
            c.FwhmOrigin = o;
            same &= ResponseMatrix.ComputeStamp(c, options) == stamp0 && ResponseMatrix.GeometryFingerprint(c) == print0;
            GeometryModel back = FromXml(Xml(c));
            same &= back.FwhmOrigin == o && ResponseMatrix.ComputeStamp(back, options) == stamp0;
        }

        Ok(same, "клеймо матрицы и отпечаток кривой одинаковы при всех семи значениях признака, и после записи-чтения");

        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            panel.CreateControl();
            // Пресет новой геометрии — сохранить, открыть — остаётся пресетом и заменяется.
            GeometryModel saved = Commit(panel);
            Ok(saved.FwhmOrigin == GeometryFwhmOrigin.Preset && Xml(saved).Contains("<FwhmOrigin>Preset</FwhmOrigin>"),
               "новая геометрия: в конфигурации «Preset» (" + saved.FwhmOrigin + ")");
            panel.SetModel(saved);
            Ok(panel.FwhmOrigin == GeometryFwhmOrigin.Preset, "после открытия — снова пресет");
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, Nai), "пресет из файла сменой на NaI заменён — " + F(panel.FwhmFieldPercent));

            // Число человека — сохранить, открыть — не заменяется.
            Field(panel).Text = "8.1";
            saved = Commit(panel);
            Ok(saved.FwhmOrigin == GeometryFwhmOrigin.User, "число человека: в конфигурации «User» (" + saved.FwhmOrigin + ")");
            panel.SetModel(saved);
            SelectCrystal(panel, "Cesium iodide");
            Ok(Near(panel.FwhmFieldPercent, 8.1) && panel.FwhmOrigin == GeometryFwhmOrigin.User,
               "число человека из файла сменой вещества не заменено — " + F(panel.FwhmFieldPercent));

            // Калибровка — так же.
            panel.ApplyFwhmFromCalibration(7.4);
            saved = Commit(panel);
            Ok(saved.FwhmOrigin == GeometryFwhmOrigin.Calibration, "калибровка: в конфигурации «Calibration»");
            panel.SetModel(saved);
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, 7.4), "калибровка из файла сменой вещества не заменена — " + F(panel.FwhmFieldPercent));

            // Шаблон — заменяется и после записи-чтения.
            SelectTemplate(panel, "RadiaCode-103");
            saved = Commit(panel);
            Ok(saved.FwhmOrigin == GeometryFwhmOrigin.Template, "шаблон: в конфигурации «Template»");
            panel.SetModel(saved);
            SelectCrystal(panel, "Sodium iodide");
            Ok(Near(panel.FwhmFieldPercent, Nai), "шаблон из файла сменой вещества заменён — " + F(panel.FwhmFieldPercent));

            // Старый конфиг без признака, число 8.49 — задано.
            GeometryModel old = FromXml(legacy);
            old.FwhmAt662Percent = 8.49;
            panel.SetModel(old);
            SelectCrystal(panel, "Sodium iodide");
            Ok(panel.FwhmOrigin == GeometryFwhmOrigin.Loaded && Near(panel.FwhmFieldPercent, 8.49),
               "старый конфиг без элемента: «из файла», 8.49 не заменено");
        }
    }

    // ------------------------------------------------------------------
    // Е — вопрос только при кривой из 3+ опор (решение Amber 11.11.2026)
    // ------------------------------------------------------------------

    static void Anchors()
    {
        Console.WriteLine("=== Е. вопрос только при кривой из 3+ опор ===");
        AppUi.Probe = (kind, text, caption) => { Asked.Add(kind + ": " + text); return false; };
        try
        {
            DeviceConfigInfo three = Device(out double c3);
            DeviceConfigInfo two = DeviceCurve(2, out double c2);
            DeviceConfigInfo four = DeviceCurve(4, out double c4);
            DeviceConfigInfo stand = Device(out _);
            ((FWHMPeakDetectionMethodConfig)stand.PeakDetectionMethodConfig).FwhmCalibration =
                new FWHMPeakDetectionMethodConfig().FwhmCalibration;
            Console.WriteLine("  опор: «три числа» {0}, кривая-2 {1}, кривая-4 {2}, заглушка {3}",
                              Anchors(three), Anchors(two), Anchors(four), Anchors(stand));
            Ok(Anchors(three) == 2 && Anchors(two) == 2 && Anchors(four) == 4 && Anchors(stand) == 0, "счёт опор");

            Asked.Clear();
            Open(three, 8.49, true, out EfficiencyMakerForm f3, out _);
            using (f3)
            {
                double suggestion = (double)typeof(GeometryEditorPanel).GetField("fwhmSuggestionPercent", Any).GetValue(Panel(f3));
                Ok(Asked.Count == 0, "«три числа» (" + F(c3) + " против 8.49) — вопроса нет");
                Ok(Near(suggestion, c3), "«три числа» — кнопка «Из калибровки прибора» есть (" + F(suggestion) + ")");
            }

            // Встроенная заглушка FWHM — не калибровка: и кнопки нет (число из заглушки было бы выдуманным).
            Open(stand, 8.49, true, out EfficiencyMakerForm fs, out _);
            using (fs)
            {
                double suggestion = (double)typeof(GeometryEditorPanel).GetField("fwhmSuggestionPercent", Any).GetValue(Panel(fs));
                Ok(suggestion == 0.0, "встроенная заглушка — кнопки «Из калибровки прибора» нет (" + F(suggestion) + ")");
            }

            Asked.Clear();
            Open(two, 8.49, false, out _, out _);
            Ok(Asked.Count == 0, "кривая из 2 опор — вопроса нет");

            Asked.Clear();
            Open(four, 8.49, false, out _, out _);
            Ok(Asked.Count == 1, "кривая из 4 опор (" + F(c4) + " против 8.49) — вопрос");

            Asked.Clear();
            DeviceConfigInfo exactly3 = DeviceCurve(3, out _);
            Open(exactly3, 8.49, false, out _, out _);
            Ok(Anchors(exactly3) == 3 && Asked.Count == 1, "кривая из 3 опор — вопрос");
        }
        finally
        {
            AppUi.Probe = null;
        }
    }

    // ------------------------------------------------------------------
    // Замер: кнопка против спектров, шаблоны против групп
    // ------------------------------------------------------------------

    static int Measure(string[] args)
    {
        string corpus = Arg(args, "--corpus=");
        string cfg = Arg(args, "--cfg=");
        string outDir = Arg(args, "--out=");
        if (corpus == null || outDir == null)
        {
            Console.Error.WriteLine("нужны --corpus= и --out=");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        Dictionary<string, Dictionary<string, string>> manifest = Csv(Path.Combine(corpus, "manifest.csv"), "key");
        Dictionary<string, Dictionary<string, string>> groups = Csv(Path.Combine(corpus, "detectors.csv"), "det");
        Dictionary<string, DeviceConfigInfo> devices = LoadDevices(Path.Combine(corpus, "devices"));

        // --- 1. Кнопка против пика по спектрам корпуса ---
        var rows = new List<string>
        {
            "spectrum,group,line_keV,channels,ecal,ecal_order,fwhm_spec_calib_kind,button_device_pct,device_calib_kind,spectrum_calib_pct_at_line,"
            + "peak_fwhm_keV,peak_fwhm_pct,peak_err_pct,net_counts,chi2_dof,diff_device_pp,diff_spectrum_pp,implied_662_pct"
        };
        var peaks662 = new Dictionary<string, List<double>>();
        foreach (string path in Directory.GetFiles(Path.Combine(corpus, "spectra"), "*.xml").OrderBy(p => p, StringComparer.Ordinal))
        {
            string key = Path.GetFileNameWithoutExtension(path);
            Dictionary<string, string> m;
            if (!manifest.TryGetValue(key, out m))
            {
                continue;
            }

            string det = m["det"];
            if (det.StartsWith("HPGE", StringComparison.Ordinal))
            {
                continue;   // германий — вне работы
            }

            foreach (double line in Lines(m))
            {
                ResultData rd;
                try
                {
                    rd = LoadSpectrum(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(key + ": не прочитан — " + ex.Message);
                    continue;
                }

                EnergySpectrum s = rd.EnergySpectrum;
                EnergyCalibration ecal = s.EnergyCalibration;
                int n = s.Spectrum.Length;
                double specAtLine = PercentAt(rd.FwhmCalibration, ecal, n, line);
                DeviceConfigInfo dev = FindDevice(devices, rd);
                double button = dev == null ? 0.0 : Button(dev);
                string devKind = dev == null ? "нет прибора" : Kind(dev);
                double guess = specAtLine > 0 ? specAtLine : 7.0;
                Fit fit = FitPeak(s.Spectrum, ecal, line, guess / 100.0 * line);
                double implied = fit == null ? double.NaN : fit.FwhmKev / 662.0 / Math.Pow(line / 662.0, GeometryModel.FwhmPowerLaw) * 100.0;
                double peakPct = fit == null ? double.NaN : fit.FwhmKev / line * 100.0;
                double diffDev = Math.Abs(line - 661.66) < 1 && fit != null ? button - peakPct : double.NaN;
                double diffSpec = fit != null ? specAtLine - peakPct : double.NaN;
                if (fit != null && Math.Abs(line - 661.66) < 1)
                {
                    if (!peaks662.ContainsKey(det)) peaks662[det] = new List<double>();
                    peaks662[det].Add(peakPct);
                }

                rows.Add(string.Join(",", key, det, F(line), n.ToString(Inv), ecal.GetType().Name, Order(ecal),
                    rd.FwhmCalibration == null ? "нет" : rd.FwhmCalibration.GetType().Name,
                    G(button), devKind, G(specAtLine),
                    fit == null ? "" : G(fit.FwhmKev), G(peakPct), fit == null ? "" : G(fit.ErrPct),
                    fit == null ? "" : fit.Net.ToString("0", Inv), fit == null ? "" : G(fit.Chi2Dof),
                    G(diffDev), G(diffSpec), G(implied)));
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "button_vs_peaks.csv"), rows, new UTF8Encoding(false));
        Console.WriteLine("=== кнопка против пиков ({0} строк) ===", rows.Count - 1);
        foreach (string r in rows) Console.WriteLine(r);

        // --- 2. Шаблоны против групп ---
        var templ = new List<string> { "template,template_pct,crystal,group,group_model_662_pct,diff_pp,peak_662_median_pct,diff_peak_pp" };
        var map = new Dictionary<string, string>
        {
            { "Atom Spectra Nano 16", "ASN16" }, { "RadiaCode-101", "RC101" }, { "RadiaCode-103", "RC103" },
            { "Obsidian", "OBS" }, { "Atom Spectra Pro 80x80", "AS80x80" }, { "Gamma-1S UDS-GC 63x63", "G1S16;G1S24" },
            { "Atom Spectra Pro 40x40", "" }, { "Atom Spectra Nano 3", "ASN3" }, { "RadiaCode-110", "" },
        };
        foreach (GeometryPresets.Preset p in GeometryPresets.Items)
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            p.Apply(g);
            string grp;
            map.TryGetValue(p.Name, out grp);
            foreach (string one in (grp ?? "").Split(';'))
            {
                double model = one.Length > 0 && groups.ContainsKey(one) ? double.Parse(groups[one]["fwhm_662_pct"], Inv) : double.NaN;
                double med = one.Length > 0 && peaks662.ContainsKey(one) ? Median(peaks662[one]) : double.NaN;
                templ.Add(string.Join(",", "\"" + p.Name + "\"", G(g.FwhmAt662Percent), "\"" + g.Crystal.Name + "\"", one.Length > 0 ? one : "нет группы",
                                      G(model), G(g.FwhmAt662Percent - model), G(med), G(g.FwhmAt662Percent - med)));
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "templates_vs_groups.csv"), templ, new UTF8Encoding(false));
        Console.WriteLine("=== шаблоны против групп ===");
        foreach (string r in templ) Console.WriteLine(r);

        // --- 3. Кнопка у приборов копии конфига ---
        if (cfg != null && Directory.Exists(cfg))
        {
            var mine = new List<string> { "device_file_index,channels,ecal,ecal_order,fwhm_calib_kind,button_pct,button_linear_scale_pct" };
            int i = 0;
            foreach (KeyValuePair<string, DeviceConfigInfo> d in LoadDevices(cfg).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                i++;
                mine.Add(string.Join(",", "\"" + Anon(d.Value) + "#" + i.ToString(Inv) + "\"", d.Value.NumberOfChannels.ToString(Inv),
                                     d.Value.EnergyCalibration == null ? "нет" : d.Value.EnergyCalibration.GetType().Name,
                                     d.Value.EnergyCalibration == null ? "" : Order(d.Value.EnergyCalibration),
                                     Kind(d.Value), G(Button(d.Value)), G(Linear(d.Value))));
            }

            File.WriteAllLines(Path.Combine(outDir, "button_config_copy.csv"), mine, new UTF8Encoding(false));
            Console.WriteLine("=== кнопка у приборов копии конфига ===");
            foreach (string r in mine) Console.WriteLine(r);
        }

        return 0;
    }

    /// <summary>Модель прибора без серийного и подписи владельца: первое слово типа и число каналов.</summary>
    static string Anon(DeviceConfigInfo d)
    {
        return (d.DeviceType ?? "?") + "/" + d.NumberOfChannels.ToString(Inv);
    }

    /// <summary>
    /// То же число кнопки при ЛИНЕЙНОЙ шкале в точке 662 кэВ (ширина в каналах × кэВ на канал):
    /// разность с <see cref="Button"/> — вклад кривизны полинома шкалы на ширине пика.
    /// </summary>
    static double Linear(DeviceConfigInfo d)
    {
        var cfg = d.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
        EnergyCalibration e = d.EnergyCalibration;
        if (cfg == null || cfg.FwhmCalibration == null || e == null) return double.NaN;
        double ch = e.EnergyToChannel(662.0, d.NumberOfChannels);
        double w = cfg.FwhmCalibration.ChannelToFwhm(ch);
        double slope = e.ChannelToEnergy(ch + 0.5) - e.ChannelToEnergy(ch - 0.5);
        return w * slope / 662.0 * 100.0;
    }

    static string Arg(string[] args, string key)
    {
        string a = args.FirstOrDefault(x => x.StartsWith(key, StringComparison.Ordinal));
        return a == null ? null : a.Substring(key.Length);
    }

    static string G(double v)
    {
        return double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.###", Inv);
    }

    static double Median(List<double> list)
    {
        var s = list.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
        if (s.Count == 0) return double.NaN;
        return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
    }

    /// <summary>Чистые линии, по которым мерится ширина: по составу спектра в манифесте.</summary>
    static IEnumerable<double> Lines(Dictionary<string, string> m)
    {
        string all = (m["chains"] + ";" + m["nuclides"] + ";" + m["key"]).ToUpperInvariant().Replace("-", "");
        var lines = new List<double>();
        if (all.Contains("137CS") || all.Contains("CS137")) lines.Add(661.66);
        if (all.Contains("40K") || all.Contains("K40")) lines.Add(1460.82);
        if (all.Contains("TH232")) lines.Add(2614.51);
        if (all.Contains("RA226")) lines.Add(1764.49);
        if (all.Contains("131I") || all.Contains("I131")) lines.Add(364.49);
        if (all.Contains("176LU") || all.Contains("LU176")) lines.Add(306.78);
        if (all.Contains("U238U")) lines.Add(1001.03);
        return lines.Distinct();
    }

    static string Order(EnergyCalibration e)
    {
        var p = e as PolynomialEnergyCalibration;
        return p != null ? p.PolynomialOrder.ToString(Inv) : "-";
    }

    static double Button(DeviceConfigInfo d)
    {
        return (double)typeof(EfficiencyMakerForm).GetMethod("FwhmPercentAt662", Any, null,
            new[] { typeof(DeviceConfigInfo) }, null).Invoke(null, new object[] { d });
    }

    /// <summary>
    /// Вид FWHM-калибровки прибора: «кривая N опор» (своя, из файла) или «три числа» (слабая:
    /// построена из FWHM_AT_0 / Ch_Fwhm / Width_Fwhm, когда кривой в файле нет).
    /// </summary>
    static string Kind(DeviceConfigInfo d)
    {
        var cfg = d.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
        if (cfg == null || cfg.FwhmCalibration == null) return "нет";
        object tag;
        if (ThreeNumber.TryGetValue(d, out tag)) return "три числа (" + cfg.FwhmCalibration.GetType().Name + ")";
        return "кривая " + cfg.FwhmCalibration.GetType().Name + " опор " + cfg.FwhmCalibration.CalibrationPeaks.Count.ToString(Inv);
    }

    static readonly Dictionary<DeviceConfigInfo, object> ThreeNumber = new Dictionary<DeviceConfigInfo, object>();

    static Dictionary<string, DeviceConfigInfo> LoadDevices(string dir)
    {
        var result = new Dictionary<string, DeviceConfigInfo>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return result;
        var serializer = new XmlSerializer(typeof(DeviceConfigInfo));
        foreach (string path in Directory.GetFiles(dir, "*.xml"))
        {
            try
            {
                DeviceConfigInfo d;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    d = (DeviceConfigInfo)serializer.Deserialize(fs);
                }

                // Как `DeviceConfigManager`: встроенная заглушка → кривая по трём числам.
                if (d.ResolveBuiltInFwhm())
                {
                    ThreeNumber[d] = null;
                }

                result[Path.GetFileName(path)] = d;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Path.GetFileName(path) + ": не прочитан — " + ex.Message);
            }
        }

        return result;
    }

    static DeviceConfigInfo FindDevice(Dictionary<string, DeviceConfigInfo> devices, ResultData rd)
    {
        DeviceConfigReference r = rd.DeviceConfigReference;
        if (r == null) return null;
        DeviceConfigInfo byGuid = devices.Values.FirstOrDefault(d => string.Equals(d.Guid, r.Guid, StringComparison.OrdinalIgnoreCase));
        return byGuid ?? devices.Values.FirstOrDefault(d => string.Equals(d.Name, r.Name, StringComparison.Ordinal));
    }

    static ResultData LoadSpectrum(string path)
    {
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            return ((ResultDataFile)serializer.Deserialize(stream)).ResultDataList[0];
        }
    }

    /// <summary>FWHM калибровки на энергии <paramref name="e"/>, % — тем же ходом, что кнопка на 662.</summary>
    static double PercentAt(FwhmCalibration fwhm, EnergyCalibration energy, int channels, double e)
    {
        if (fwhm == null || energy == null) return double.NaN;
        double ch = energy.EnergyToChannel(e, channels);
        double w = fwhm.ChannelToFwhm(ch);
        if (!(w > 0)) return double.NaN;
        return (energy.ChannelToEnergy(ch + w / 2) - energy.ChannelToEnergy(ch - w / 2)) / e * 100.0;
    }

    static Dictionary<string, Dictionary<string, string>> Csv(string path, string keyColumn)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        string[] head = Split(lines[0].TrimStart('﻿'));
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            string[] cells = Split(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int j = 0; j < head.Length; j++) row[head[j]] = j < cells.Length ? cells[j] : "";
            result[row[keyColumn]] = row;
        }

        return result;
    }

    static string[] Split(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }

        cells.Add(sb.ToString());
        return cells.ToArray();
    }

    sealed class Fit
    {
        public double Mu, Sigma, FwhmKev, ErrPct, Net, Chi2Dof;
    }

    /// <summary>
    /// Гаусс плюс прямая подложка в окне ±1.5 ожидаемой FWHM вокруг линии: (μ, σ) — сеткой с
    /// уточнением, амплитуда и прямая — линейным МНК весом 1/max(y,1) при каждой паре.
    /// Ошибка FWHM — по профилю χ² (Δχ² = 1 по σ при лучшем μ), умноженному на √(χ²/dof),
    /// если тот больше единицы. FWHM в кэВ — через шкалу спектра, как у кнопки.
    /// </summary>
    static Fit FitPeak(int[] y, EnergyCalibration ecal, double line, double fwhmGuessKev)
    {
        int n = y.Length;
        double c0 = ecal.EnergyToChannel(line, n);
        double kevPerCh = ecal.ChannelToEnergy(c0 + 0.5) - ecal.ChannelToEnergy(c0 - 0.5);
        if (!(kevPerCh > 0)) return null;
        double fwCh = fwhmGuessKev / kevPerCh;
        int lo = (int)Math.Floor(c0 - 1.5 * fwCh), hi = (int)Math.Ceiling(c0 + 1.5 * fwCh);
        if (lo < 1 || hi >= n - 1 || hi - lo < 8) return null;
        double sGuess = fwCh / 2.3548;
        double bestChi = double.MaxValue, bestMu = c0, bestS = sGuess;
        double muLo = c0 - 0.6 * fwCh, muHi = c0 + 0.6 * fwCh, sLo = 0.4 * sGuess, sHi = 2.2 * sGuess;
        for (int pass = 0; pass < 4; pass++)
        {
            int k = 40;
            for (int a = 0; a <= k; a++)
            {
                double mu = muLo + (muHi - muLo) * a / k;
                for (int b = 0; b <= k; b++)
                {
                    double s = sLo + (sHi - sLo) * b / k;
                    double chi = Chi(y, lo, hi, mu, s, c0, out _);
                    if (chi < bestChi) { bestChi = chi; bestMu = mu; bestS = s; }
                }
            }

            double dm = (muHi - muLo) / k * 2, ds = (sHi - sLo) / k * 2;
            muLo = bestMu - dm; muHi = bestMu + dm; sLo = Math.Max(0.2, bestS - ds); sHi = bestS + ds;
        }

        double amp;
        Chi(y, lo, hi, bestMu, bestS, c0, out amp);
        int dof = hi - lo + 1 - 5;
        double chi2dof = bestChi / Math.Max(1, dof);
        // Профиль по σ: μ — лучший при каждом σ (узкой сеткой).
        double sMinus = bestS, sPlus = bestS, step = bestS / 400.0;
        for (double s = bestS; s > bestS * 0.3; s -= step)
        {
            if (Profile(y, lo, hi, bestMu, s, c0) > bestChi + 1) break;
            sMinus = s;
        }

        for (double s = bestS; s < bestS * 3; s += step)
        {
            if (Profile(y, lo, hi, bestMu, s, c0) > bestChi + 1) break;
            sPlus = s;
        }

        double fwhmCh = 2.3548 * bestS;
        double fwKev = ecal.ChannelToEnergy(bestMu + fwhmCh / 2) - ecal.ChannelToEnergy(bestMu - fwhmCh / 2);
        double relErr = 0.5 * (sPlus - sMinus) / bestS * Math.Sqrt(Math.Max(1.0, chi2dof));
        return new Fit
        {
            Mu = bestMu, Sigma = bestS, FwhmKev = fwKev,
            ErrPct = relErr * fwKev / line * 100.0,
            Net = amp * bestS * Math.Sqrt(2 * Math.PI), Chi2Dof = chi2dof,
        };
    }

    static double Profile(int[] y, int lo, int hi, double mu0, double s, double c0)
    {
        double best = double.MaxValue;
        for (int a = -20; a <= 20; a++)
        {
            double chi = Chi(y, lo, hi, mu0 + a * s / 40.0, s, c0, out _);
            if (chi < best) best = chi;
        }

        return best;
    }

    /// <summary>χ² при заданных μ, σ: амплитуда и прямая подложка — линейным МНК (3×3).</summary>
    static double Chi(int[] y, int lo, int hi, double mu, double s, double c0, out double amp)
    {
        double[,] m = new double[3, 3];
        double[] v = new double[3];
        for (int i = lo; i <= hi; i++)
        {
            double w = 1.0 / Math.Max(1.0, y[i]);
            double g = Math.Exp(-0.5 * (i - mu) * (i - mu) / (s * s));
            double[] f = { g, 1.0, i - c0 };
            for (int r = 0; r < 3; r++)
            {
                v[r] += w * f[r] * y[i];
                for (int c = 0; c < 3; c++) m[r, c] += w * f[r] * f[c];
            }
        }

        double[] x = Solve3(m, v);
        amp = x[0];
        double chi = 0;
        for (int i = lo; i <= hi; i++)
        {
            double g = Math.Exp(-0.5 * (i - mu) * (i - mu) / (s * s));
            double model = x[0] * g + x[1] + x[2] * (i - c0);
            double d = y[i] - model;
            chi += d * d / Math.Max(1.0, y[i]);
        }

        return amp > 0 ? chi : chi + 1e12;
    }

    static double[] Solve3(double[,] a, double[] b)
    {
        double det = Det(a);
        var x = new double[3];
        for (int k = 0; k < 3; k++)
        {
            var t = (double[,])a.Clone();
            for (int r = 0; r < 3; r++) t[r, k] = b[r];
            x[k] = Math.Abs(det) > 0 ? Det(t) / det : 0;
        }

        return x;
    }

    static double Det(double[,] a)
    {
        return a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
             - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
             + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
    }
}
