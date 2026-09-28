using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

/// <summary>
/// П150 (24.09.2026): три строки ревизии «Дубль 3» по редактору геометрии —
/// `AMBER96` (разбор формулы вещества), `AMBER98` (отрицательные длины),
/// `AMBER94` (полевая сцена не идёт за веществом, плотностью и верхней
/// энергией). Одна проба ДО и ПОСЛЕ правки: новое API берётся отражением,
/// чтобы проба собиралась и на старом приложении.
///
///     geometryeditorp150probe [--eff] [--lib=&lt;GeometryMaterials.xml&gt;] [--dump=&lt;файл&gt;]
///
/// `--eff` — посчитать эффективность RC-103 при расстоянии 0 и −5 мм
/// (200 000 историй, фиксированное зерно); `--lib` — библиотека веществ
/// пользователя (ТОЛЬКО ЧТЕНИЕ), чей состав печатается побитово вместе с
/// засевом; `--dump` — туда же печать состава «R» для сравнения ДО/ПОСЛЕ.
///
/// Код возврата: 0 — все три дефекта не воспроизводятся (правка на месте);
/// 1 — воспроизведён хотя бы один (так и должно быть ДО правки); 2 — нечем мерить.
/// </summary>
static class GeometryEditorP150Probe
{
    static int defects;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();

        bool eff = false;
        string lib = null, dump = null, corpus = null, store = null;
        foreach (string a in args)
        {
            if (a == "--eff") eff = true;
            else if (a.StartsWith("--lib=", StringComparison.Ordinal)) lib = a.Substring(6);
            else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dump = a.Substring(7);
            else if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
            else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
        }

        Formula(lib, dump);
        Negative(eff, corpus);
        Scene();

        // П172 (28.09.2026): остатки дубля 4 — `AMBER104`…`AMBER107` и
        // подсказка ISO (остаток ~~`AMBER102`~~).
        Hydrate();
        Density(corpus);
        LibraryEdit();
        IsoHint();
        StoreFile(store, dump);

        Console.WriteLine();
        Console.WriteLine(defects == 0 ? "ДЕФЕКТОВ НЕ ВОСПРОИЗВЕДЕНО" : "ВОСПРОИЗВЕДЕНО ДЕФЕКТОВ: " + defects);
        return defects == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // AMBER96
    // ------------------------------------------------------------------

    static void Formula(string lib, string dump)
    {
        Console.WriteLine("== AMBER96: разбор формулы ==");
        MethodInfo problem = typeof(GeometryMaterialLibrary).GetMethod("FormulaProblem",
            BindingFlags.Public | BindingFlags.Static);
        Console.WriteLine("   FormulaProblem в приложении: {0}", problem != null ? "есть" : "НЕТ");

        // Что формула ОБЯЗАНА дать (атомы), null — обязана дать отказ.
        var cases = new List<KeyValuePair<string, string>>
        {
            Case("H2O", "1:2 8:1"), Case("Bi4Ge3O12", "8:12 32:3 83:4"), Case("Lu2O3", "8:3 71:2"),
            Case("Al2O3", "8:3 13:2"), Case("CO2", "6:1 8:2"), Case("C2H4", "1:4 6:2"),
            Case("K2CO3", "6:1 8:3 19:2"), Case("SiO2", "8:2 14:1"), Case("NaI", "11:1 53:1"),
            Case("CsI", "53:1 55:1"), Case("Co", "27:1"), Case("Cd0.9Zn0.1Te1", "30:0.1 48:0.9 52:1"),
            // Принятая запись засева — обязана остаться прежней.
            Case("Bi4 Ge3 O12", "8:12 32:3 83:4"), Case("H2 O1", "1:2 8:1"), Case("Cd9 Zn1 Te10", "30:1 48:9 52:10"),
            Case("Na1 I1", "11:1 53:1"), Case("Ge1", "32:1"),
            // Обязаны отказать.
            Case("co2", null), Case("TI", null), Case("H2O)", null), Case("Ca(OH)2", null),
            Case("H2 Xx1", null), Case("2H", null),
            // ⚠ (П172, `AMBER104`) «H2.O» П150 ждал отказом («точка — только
            // между цифрами»). Точка перед заглавной — знак аддукта (IUPAC
            // IR-4.4.3.5), тот же, что у «C6H12O6.H2O» подписей ЛСРМ; отличить
            // одно от другого нечем, и H2·O = H2O — то самое, что набирали.
            Case("H2.O", "1:2 8:1"),
        };

        foreach (var c in cases)
        {
            Dictionary<int, double> atoms = GeometryMaterialLibrary.ParseFormula(c.Key);
            string got = Atoms(atoms);
            string words = problem != null ? (string)problem.Invoke(null, new object[] { c.Key }) : "(нет API)";
            bool ok = c.Value == null ? atoms.Count == 0 && problem != null && !string.IsNullOrEmpty(words)
                                      : got == c.Value && (problem == null || words == null);
            if (!ok) defects++;
            Console.WriteLine("   {0,-16} → {1,-22} ждём {2,-22} {3}{4}", "«" + c.Key + "»", got == "" ? "(пусто)" : got,
                              c.Value ?? "ОТКАЗ", ok ? "верно" : "ДЕФЕКТ",
                              words != null && words != "(нет API)" ? "   [" + words + "]" : "");
        }

        // μ/ρ воды и BGO: слитная против принятой записи.
        Console.WriteLine();
        foreach (var pair in new[] { new[] { "H2O", "H2 O1", "1" }, new[] { "Bi4Ge3O12", "Bi4 Ge3 O12", "7.13" },
                                     new[] { "Lu2O3", "Lu2 O3", "9.42" } })
        {
            double rho = double.Parse(pair[2], CultureInfo.InvariantCulture);
            GeometryMaterial a = Make(pair[0], rho), b = Make(pair[1], rho);
            Console.WriteLine("   μ «{0}» / «{1}»: 60 кэВ ×{2:F3}, 662 кэВ ×{3:F3}", pair[0], pair[1],
                              Ratio(a, b, 60.0), Ratio(a, b, 662.0));
        }

        // Побитово: засев и библиотека пользователя (только чтение).
        StringBuilder text = new StringBuilder();
        List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
        Composition("засев", seed, text);
        if (lib != null)
        {
            Composition("библиотека " + lib, ReadLibrary(lib), text);
        }

        if (dump != null)
        {
            File.WriteAllText(dump, text.ToString(), new UTF8Encoding(false));
            Console.WriteLine("   состав «R» — в {0}", dump);
        }
    }

    static KeyValuePair<string, string> Case(string f, string atoms)
    {
        return new KeyValuePair<string, string>(f, atoms);
    }

    static string Atoms(Dictionary<int, double> atoms)
    {
        List<int> z = new List<int>(atoms.Keys);
        z.Sort();
        List<string> parts = new List<string>();
        foreach (int k in z)
        {
            parts.Add(k.ToString(CultureInfo.InvariantCulture) + ":" + atoms[k].ToString("R", CultureInfo.InvariantCulture));
        }

        return string.Join(" ", parts);
    }

    static GeometryMaterial Make(string formula, double rho)
    {
        return GeometryMaterialLibrary.Make(new GeometryMaterialLibrary.Entry { Name = formula, Formula = formula, Density = rho }, rho);
    }

    static double Ratio(GeometryMaterial a, GeometryMaterial b, double e)
    {
        return a.LinearAttenuation(e) / b.LinearAttenuation(e);
    }

    static void Composition(string title, List<GeometryMaterialLibrary.Entry> list, StringBuilder text)
    {
        Func<string, GeometryMaterialLibrary.Entry> lookup = name =>
            list.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        int withFormula = 0, empty = 0;
        text.AppendLine("# " + title + ": " + list.Count.ToString(CultureInfo.InvariantCulture));
        foreach (GeometryMaterialLibrary.Entry e in list)
        {
            GeometryMaterial m = GeometryMaterialLibrary.Make(e, e.Density, lookup);
            if (!string.IsNullOrEmpty(e.Formula)) withFormula++;
            if (m.Fractions.Count == 0) empty++;
            List<int> z = new List<int>(m.Fractions.Keys);
            z.Sort();
            StringBuilder row = new StringBuilder();
            foreach (int k in z)
            {
                row.Append(' ').Append(k.ToString(CultureInfo.InvariantCulture)).Append(':')
                   .Append(m.Fractions[k].ToString("R", CultureInfo.InvariantCulture));
            }

            text.AppendLine(e.Name + " | " + (e.Formula ?? "") + " | " + m.Density.ToString("R", CultureInfo.InvariantCulture) + " |" + row);
        }

        Console.WriteLine("   {0}: веществ {1}, с формулой {2}, с пустым составом {3}", title, list.Count, withFormula, empty);
    }

    static List<GeometryMaterialLibrary.Entry> ReadLibrary(string path)
    {
        GeometryMaterialConfig config;
        using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            config = (GeometryMaterialConfig)new XmlSerializer(typeof(GeometryMaterialConfig)).Deserialize(s);
        }

        List<GeometryMaterialLibrary.Entry> list = new List<GeometryMaterialLibrary.Entry>();
        foreach (GeometryMaterialRecord r in config.Materials ?? new GeometryMaterialRecord[0])
        {
            GeometryMaterialLibrary.Entry e = new GeometryMaterialLibrary.Entry
            {
                Name = r.Name, Abbr = r.Abbr, Formula = r.Formula ?? "", Density = r.Density, Kind = r.Kind,
            };
            foreach (GeometryMaterialComponent c in r.Components ?? new GeometryMaterialComponent[0])
            {
                e.Components.Add(new GeometryMaterialComponent { Material = c.Material, Weight = c.Weight });
            }

            foreach (GeometryElementFraction f in r.Fractions ?? new GeometryElementFraction[0])
            {
                e.ElementFractions[f.Z] = f.Fraction;
            }

            list.Add(e);
        }

        return list;
    }

    // ------------------------------------------------------------------
    // AMBER98
    // ------------------------------------------------------------------

    static void Negative(bool eff, string corpus)
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER98: отрицательные длины ==");
        string path = corpus ?? Path.Combine("tools", "CORPUS", "corpus", "geometries", "RC103_point0.in");
        if (!File.Exists(path))
        {
            Console.WriteLine("   нет {0} — раздел пропущен", path);
            defects++;
            return;
        }

        GeometryModel rc = GeometryModel.Load(path);
        Console.WriteLine("   {0}: PointDistance {1} мм, торец: отражатель {2} + зазор {3} + корпус {4} мм",
                          Path.GetFileName(path), rc.PointDistance, rc.FrontReflectorThickness,
                          rc.FrontGapThickness, rc.FrontCladdingThickness);

        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            MethodInfo validate = typeof(GeometryEditorPanel).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic);
            string[][] tries =
            {
                new[] { "PointDistance", "-5" }, new[] { "FrontReflectorThickness", "-0.5" },
                new[] { "FrontGapThickness", "-1" }, new[] { "SideCladdingThickness", "-1" },
                new[] { "MountingThickness", "-1" },
            };
            foreach (string[] t in tries)
            {
                GeometryModel g = rc.Clone();
                typeof(GeometryModel).GetField(t[0]).SetValue(g, double.Parse(t[1], CultureInfo.InvariantCulture));
                string error = (string)validate.Invoke(panel, new object[] { g });
                List<GeometryScenes.Issue> issues = GeometryScenes.Inconsistencies(g);
                bool onField = issues.Exists(i => i.Field == t[0]);
                if (error == null || !onField) defects++;
                Console.WriteLine("   {0} = {1}: Validate {2}; поле подсвечено: {3}", t[0], t[1],
                                  error == null ? "ПРИНЯЛ (дефект)" : "отказ «" + error.Replace("\r\n", " / ") + "»",
                                  onField ? "да" : "нет");
            }

            // Контроль: годная геометрия молчит, у поля Iso — прежнее правило.
            string clean = (string)validate.Invoke(panel, new object[] { rc.Clone() });
            Console.WriteLine("   контроль: RC103_point0 как есть — Validate {0}", clean == null ? "молчит" : "ОТКАЗ «" + clean + "»");
            if (clean != null) defects++;

            // Неактивная панель: отрицательное расстояние маринелли у точки не мешает.
            GeometryModel other = rc.Clone();
            other.MarinelliToDetectorDistance = -3.0;
            other.BeakerToDetectorDistance = -3.0;
            string otherError = (string)validate.Invoke(panel, new object[] { other });
            Console.WriteLine("   контроль: у точки минус в полях цилиндра/маринелли — Validate {0}",
                              otherError == null ? "молчит" : "ОТКАЗ «" + otherError + "»");
            if (otherError != null) defects++;
        }

        if (eff)
        {
            foreach (double d in new[] { 0.0, -5.0 })
            {
                GeometryModel g = rc.Clone();
                g.PointDistance = d;
                EfficiencySimulator sim = new EfficiencySimulator(g) { Histories = 200000 };
                sim.ResetStream(150);
                double err60, err662;
                double e60 = sim.Efficiency(60.0, out err60);
                double e662 = sim.Efficiency(662.0, out err662);
                Console.WriteLine("   ε RC-103, PointDistance {0,4} мм: 60 кэВ {1:E4} (±{2:F2} %), 662 кэВ {3:E4} (±{4:F2} %)",
                                  d, e60, err60, e662, err662);
            }
        }
    }

    // ------------------------------------------------------------------
    // AMBER94
    // ------------------------------------------------------------------

    static void Scene()
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER94: полевая сцена за веществом, плотностью и верхней энергией ==");
        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            panel.SetModel(GeometryEditorPanel.Blank());
            ComboBox source = (ComboBox)Field(panel, "sourceTypeCombo");
            source.SelectedIndex = 4;                       // цилиндр + «на земле»
            Report("выбрали «на земле»", Built(panel), 3000.0);

            // (1) плотность пробы 1.5 → 1.0, уход фокуса с поля.
            var fields = (Dictionary<string, TextBox>)Field(panel, "fields");
            TextBox density = fields["Source.Density"];
            density.Text = "1";
            typeof(Control).GetMethod("OnLeave", BindingFlags.Instance | BindingFlags.NonPublic)
                           .Invoke(density, new object[] { EventArgs.Empty });
            Report("плотность 1.0 (уход с поля)", Built(panel), 3000.0);

            // (2) вещество пробы → вода.
            var materials = (Dictionary<string, ComboBox>)Field(panel, "materials");
            ComboBox src = materials["Source"];
            int water = -1;
            for (int i = 0; i < src.Items.Count; i++)
            {
                var e = src.Items[i] as GeometryMaterialLibrary.Entry;
                if (e != null && e.Name == "Water, liquid") water = i;
            }

            src.SelectedIndex = water;
            Report("вещество «Water, liquid»", Built(panel), 3000.0);

            // (3) верхняя энергия 3000 → 1460.
            MethodInfo two = typeof(GeometryEditorPanel).GetMethod("SetSceneEnergy", new[] { typeof(double), typeof(bool) });
            if (two != null) two.Invoke(panel, new object[] { 1460.0, true });
            else panel.SetSceneEnergy(1460.0);
            Report("верхняя энергия 1460", Built(panel), 1460.0);

            // (4) Контроль «не при загрузке»: сохранённая сцена с правленым
            //     диаметром открывается как есть, и программная подача энергии
            //     (восстановление полей расчёта) её не трогает.
            GeometryModel saved = Built(panel).Clone();
            saved.BeakerDiameter = 1234.5;
            panel.SetModel(saved);
            double afterLoad = Model(panel).BeakerDiameter;
            if (two != null) two.Invoke(panel, new object[] { 2614.0, false });
            else panel.SetSceneEnergy(2614.0);
            double afterRestore = Model(panel).BeakerDiameter;
            Console.WriteLine("   контроль загрузки: правленый диаметр 1234.5 → после SetModel {0:R}, после подачи энергии без пересчёта {1:R}",
                              afterLoad, afterRestore);
            if (afterLoad != 1234.5 || afterRestore != 1234.5) defects++;
        }
    }

    static void Report(string what, GeometryModel g, double kev)
    {
        GeometryModel want = g.Clone();
        GeometryScenes.Ground(want, kev);
        double mfp = GeometryScenes.MeanFreePathMm(g.Source, kev);
        // Поля хранят число текстом «G8» — сравнение относительное, 1e-6.
        bool same = Math.Abs(want.BeakerDiameter - g.BeakerDiameter) <= 1e-6 * want.BeakerDiameter
                    && Math.Abs(want.SourceHeight - g.SourceHeight) <= 1e-6 * want.SourceHeight;
        if (!same) defects++;
        Console.WriteLine("   {0,-30} проба «{1}» ρ {2:R}: глубина {3:F1} мм = {4:F2} λ({5:0} кэВ), Ø {6:F1} мм; по формуле {7:F1} / {8:F1} — {9}",
                          what, g.Source.Name, g.Source.Density, g.SourceHeight, g.SourceHeight / mfp, kev,
                          g.BeakerDiameter, want.SourceHeight, want.BeakerDiameter, same ? "сходится" : "ДЕФЕКТ: сцена прежняя");
    }

    static object Field(object o, string name)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return f == null ? null : f.GetValue(o);
    }

    /// <summary>То, что сейчас в ПОЛЯХ (`BuildModel`), — а не последняя применённая модель.</summary>
    static GeometryModel Built(object panel)
    {
        return (GeometryModel)typeof(GeometryEditorPanel).GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic)
                                                         .Invoke(panel, null);
    }

    static GeometryModel Model(object panel)
    {
        return (GeometryModel)Field(panel, "model");
    }

    // ------------------------------------------------------------------
    // П172 · AMBER104 (1): гидрат через точку
    // ------------------------------------------------------------------

    static void Hydrate()
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER104 (1): гидрат через точку ==");
        MethodInfo problem = typeof(GeometryMaterialLibrary).GetMethod("FormulaProblem",
            BindingFlags.Public | BindingFlags.Static);
        // exact — ровно этот состав; either — этот состав ИЛИ отказ словами
        // (запись двусмысленна); refuse — только отказ.
        string[][] cases =
        {
            new[] { "C6H12O6.H2O", "exact", "1:14 6:6 8:7" },
            new[] { "CaSO4*2H2O", "exact", "1:4 8:6 16:1 20:1" },
            new[] { "CaSO4·2H2O", "exact", "1:4 8:6 16:1 20:1" },
            new[] { "CaSO4 .2H2O", "exact", "1:4 8:6 16:1 20:1" },
            new[] { "NaCl.2H2O", "exact", "1:4 8:2 11:1 17:1" },
            new[] { "Na2B4O7*10H2O", "exact", "1:20 5:4 8:17 11:2" },
            new[] { "Cd0.9Zn0.1Te1", "exact", "30:0.1 48:0.9 52:1" },
            new[] { "Si1.5 O3", "exact", "8:3 14:1.5" },
            new[] { "CaSO4.2H2O", "either", "1:4 8:6 16:1 20:1" },
            new[] { "CuSO4.5H2O", "either", "1:10 8:9 16:1 29:1" },
            new[] { "Na2SO4.10H2O", "either", "1:20 8:14 11:2 16:1" },
            new[] { "H2O.", "refuse", null }, new[] { ".H2O", "refuse", null },
            // «CaSO4.2» — 4.2 атома кислорода, не гидрат: за дробью нет формулы.
            new[] { "H2O..H2O", "refuse", null }, new[] { "CaSO4.2", "exact", "8:4.2 16:1 20:1" },
            new[] { "H2O*", "refuse", null }, new[] { "*2H2O", "refuse", null },
            new[] { "CaSO4*0H2O", "refuse", null },
        };

        foreach (string[] c in cases)
        {
            Dictionary<int, double> atoms = GeometryMaterialLibrary.ParseFormula(c[0]);
            string got = Atoms(atoms);
            string words = problem != null ? (string)problem.Invoke(null, new object[] { c[0] }) : null;
            bool refused = atoms.Count == 0 && !string.IsNullOrEmpty(words);
            bool ok = c[1] == "exact" ? got == c[2] && words == null
                    : c[1] == "either" ? (got == c[2] && words == null) || refused
                    : refused;
            if (!ok) defects++;
            Console.WriteLine("   {0,-18} → {1,-26} ждём {2,-30} {3}{4}", "«" + c[0] + "»", got == "" ? "(пусто)" : got,
                              (c[1] == "either" ? "это или отказ: " : c[1] == "refuse" ? "ОТКАЗ" : "") + (c[2] ?? ""),
                              ok ? "верно" : "ДЕФЕКТ", words != null ? "   [" + words + "]" : "");
        }

        // μ разобранного против верного состава.
        foreach (string[] pair in new[] { new[] { "CaSO4.2H2O", "Ca1 S1 O6 H4", "2.32" },
                                          new[] { "CuSO4.5H2O", "Cu1 S1 O9 H10", "2.286" },
                                          new[] { "Na2SO4.10H2O", "Na2 S1 O14 H20", "1.464" } })
        {
            double rho = double.Parse(pair[2], CultureInfo.InvariantCulture);
            GeometryMaterial a = Make(pair[0], rho), b = Make(pair[1], rho);
            if (a.Fractions.Count == 0)
            {
                Console.WriteLine("   μ «{0}»: отказ — состава нет, сравнивать нечего", pair[0]);
                continue;
            }

            Console.WriteLine("   μ «{0}» / «{1}»: 30 кэВ ×{2:F3}, 60 кэВ ×{3:F3}", pair[0], pair[1],
                              Ratio(a, b, 30.0), Ratio(a, b, 60.0));
        }
    }

    // ------------------------------------------------------------------
    // П172 · AMBER105: плотность ≤ 0 в поле вещества
    // ------------------------------------------------------------------

    static void Density(string corpus)
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER105: плотность ≤ 0 в поле вещества ==");
        string path = corpus ?? Path.Combine("tools", "CORPUS", "corpus", "geometries", "RC103_point0.in");
        if (!File.Exists(path))
        {
            Console.WriteLine("   нет {0} — раздел пропущен", path);
            defects++;
            return;
        }

        GeometryModel rc = GeometryModel.Load(path);
        MethodInfo validate = typeof(GeometryEditorPanel).GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo mark = typeof(GeometryEditorPanel).GetMethod("MarkBadValues", BindingFlags.Instance | BindingFlags.NonPublic);
        string[][] tries =
        {
            new[] { "Crystal", "-1.5" }, new[] { "Crystal", "0" }, new[] { "Reflector", "0" },
            new[] { "Cladding", "-2.7" }, new[] { "Source", "0" },
        };
        foreach (string[] t in tries)
        {
            using (GeometryEditorPanel panel = new GeometryEditorPanel())
            {
                panel.SetModel(rc.Clone());
                var fields = (Dictionary<string, TextBox>)Field(panel, "fields");
                TextBox box = fields[t[0] + ".Density"];
                string was = box.Text;
                box.Text = t[1];
                GeometryModel built = Built(panel);
                GeometryMaterial m = (GeometryMaterial)typeof(GeometryModel).GetField(t[0]).GetValue(built);
                mark.Invoke(panel, null);
                bool red = box.BackColor != System.Drawing.SystemColors.Window;
                string error = (string)validate.Invoke(panel, new object[] { built });
                bool ok = error != null && red;
                if (!ok) defects++;
                Console.WriteLine("   {0}.Density «{1}» → «{2}»: в модели ρ {3:R} («{4}»); поле красное: {5}; Validate {6} — {7}",
                                  t[0], was, t[1], m.Density, m.Name, red ? "да" : "нет",
                                  error == null ? "ПРИНЯЛ" : "отказ «" + error.Replace("\r\n", " / ") + "»",
                                  ok ? "верно" : "ДЕФЕКТ");
            }
        }

        // Контроли: годная геометрия молчит; у изотропного поля строка
        // вещества пробы снята — ноль в ней не мешает; пустой зазор (вещество
        // не выбрано) с нулём плотности — не отказ.
        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            panel.SetModel(rc.Clone());
            string clean = (string)validate.Invoke(panel, new object[] { Built(panel) });
            Console.WriteLine("   контроль: RC103_point0 как есть — Validate {0}", clean == null ? "молчит" : "ОТКАЗ «" + clean + "»");
            if (clean != null) defects++;

            var materials = (Dictionary<string, ComboBox>)Field(panel, "materials");
            var fields = (Dictionary<string, TextBox>)Field(panel, "fields");
            Console.WriteLine("   зазор RC103_point0: вещество «{0}», ρ в поле «{1}»",
                              materials["Gap"].SelectedIndex >= 0 ? materials["Gap"].SelectedItem.ToString() : "(не выбрано)",
                              fields["Gap.Density"].Text);

            ComboBox source = (ComboBox)Field(panel, "sourceTypeCombo");
            source.SelectedIndex = 6;                       // изотропное поле
            fields["Source.Density"].Text = "0";
            string iso = (string)validate.Invoke(panel, new object[] { Built(panel) });
            Console.WriteLine("   контроль: ISO, в снятой строке пробы ρ 0 — Validate {0}", iso == null ? "молчит" : "ОТКАЗ «" + iso + "»");
            if (iso != null) defects++;
        }
    }

    // ------------------------------------------------------------------
    // П172 · AMBER106: правка вещества пробы через библиотеку и полевая сцена
    // ------------------------------------------------------------------

    static void LibraryEdit()
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER106: правка вещества пробы в библиотеке («…») и полевая сцена ==");
        GeometryMaterialStore.Reload();
        MethodInfo apply = typeof(GeometryEditorPanel).GetMethod("ApplyLibraryEdit", BindingFlags.Instance | BindingFlags.NonPublic);
        Console.WriteLine("   ApplyLibraryEdit в приложении: {0}", apply != null ? "есть" : "НЕТ — путь повторён пробой по коду EditMaterials");
        try
        {
            using (GeometryEditorPanel panel = new GeometryEditorPanel())
            {
                panel.SetModel(GeometryEditorPanel.Blank());
                ComboBox source = (ComboBox)Field(panel, "sourceTypeCombo");
                source.SelectedIndex = 4;                   // цилиндр «на земле»
                GeometryModel first = Built(panel);
                Report("до правки библиотеки", first, 3000.0);
                string name = first.Source.Name;

                var materials = (Dictionary<string, ComboBox>)Field(panel, "materials");
                MethodInfo materialOf = typeof(GeometryEditorPanel).GetMethod("MaterialOf", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo get = typeof(GeometryEditorPanel).GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo snapshot = typeof(GeometryEditorPanel).GetMethod("SnapshotMaterials", BindingFlags.Static | BindingFlags.NonPublic);
                var was = new Dictionary<string, GeometryMaterial>(StringComparer.Ordinal);
                foreach (string key in materials.Keys)
                {
                    was[key] = (GeometryMaterial)materialOf.Invoke(panel, new object[] { key, get.Invoke(panel, new object[] { key + ".Density" }) });
                }

                var before = (List<GeometryMaterialLibrary.Entry>)snapshot.Invoke(null, null);

                // «Правка в окне библиотеки»: состав вещества пробы — свинец
                // вместо грунта (крупная замена, пробег ×0.1 и меньше).
                GeometryMaterialLibrary.Entry entry = GeometryMaterialStore.Entries.Find(
                    x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                entry.Formula = "Pb1";
                entry.Components.Clear();
                entry.ElementFractions.Clear();
                var after = (List<GeometryMaterialLibrary.Entry>)snapshot.Invoke(null, null);

                if (apply != null)
                {
                    apply.Invoke(panel, new object[] { was, before, after });
                }
                else
                {
                    // Тело EditMaterials HEAD после `ShowDialog` — дословно.
                    FieldInfo loading = typeof(GeometryEditorPanel).GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic);
                    var kinds = (Dictionary<string, GeometryMaterialLibrary.MaterialKind>)Field(panel, "materialKinds");
                    MethodInfo fill = typeof(GeometryEditorPanel).GetMethod("FillMaterialCombo", BindingFlags.Static | BindingFlags.NonPublic);
                    MethodInfo select = typeof(GeometryEditorPanel).GetMethod("SelectMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                    bool wasLoading = (bool)loading.GetValue(panel);
                    loading.SetValue(panel, true);
                    try
                    {
                        foreach (KeyValuePair<string, ComboBox> pair in materials)
                        {
                            pair.Value.Items.Clear();
                            fill.Invoke(null, new object[] { pair.Value, kinds[pair.Key] });
                            GeometryMaterial material = was[pair.Key];
                            bool touched = material != null && GeometryMaterialLibrary.CompositionChanged(material.Name, before, after);
                            select.Invoke(panel, new object[] { pair.Key, material, !touched });
                        }
                    }
                    finally
                    {
                        loading.SetValue(panel, wasLoading);
                    }

                    typeof(GeometryEditorPanel).GetMethod("RefreshSketch", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                }

                GeometryModel second = Built(panel);
                Console.WriteLine("   состав пробы в полях после правки: {0}", GeometryMaterialLibrary.Describe(second.Source));
                Report("после правки «" + name + "» → Pb", second, 3000.0);
            }
        }
        finally
        {
            GeometryMaterialStore.Reload();
        }
    }

    // ------------------------------------------------------------------
    // П172 · остаток AMBER102: подсказка «для фона — сцена ISO» в редакторе
    // ------------------------------------------------------------------

    static void IsoHint()
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER102 (остаток): подсказка ISO у выбора вида источника ==");
        string[] names = { "точка", "цилиндр", "маринелли", "кювета", "на земле", "в лунке", "ISO" };
        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            panel.SetModel(GeometryEditorPanel.Blank());
            ComboBox source = (ComboBox)Field(panel, "sourceTypeCombo");
            Label scene = (Label)Field(panel, "sceneLabel");
            for (int i = 0; i < names.Length; i++)
            {
                source.SelectedIndex = i;
                string text = scene.Text ?? "";
                bool sample = i < 4;
                bool mentions = text.IndexOf("ISO", StringComparison.Ordinal) >= 0;
                // У вида пробы подсказка обязана назвать ISO; у полевых сцен —
                // прежняя строка сцены (пробег или эффективная площадь).
                bool ok = sample ? mentions : text.Length > 0;
                if (!ok) defects++;
                Console.WriteLine("   {0,-10} строка под списком: «{1}» — {2}", names[i], text, ok ? "верно" : "ДЕФЕКТ");
            }
        }
    }

    // ------------------------------------------------------------------
    // П172 · AMBER104 (2) регистр после обновления, AMBER107 засев и перенос
    // ------------------------------------------------------------------

    static readonly string OldFreon = "6:0.061309 9:0.290924 55:0.647767";
    static readonly string OldLaOs = "8:0.049097 16:0.098383 57:0.85252";

    static GeometryMaterial Packed(string name, double rho, string packed)
    {
        GeometryMaterial m = new GeometryMaterial { Name = name, Density = rho };
        foreach (KeyValuePair<int, double> p in GeometryMaterialSeed.Fractions(packed)) m.Fractions[p.Key] = p.Value;
        return m;
    }

    static string Fr(Dictionary<int, double> f)
    {
        return Atoms(f);
    }

    static void StoreFile(string amberLib, string dump)
    {
        Console.WriteLine();
        Console.WriteLine("== AMBER107: засев — фреон и оксисульфид ==");
        List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
        GeometryMaterialLibrary.Entry freon = seed.Find(x => x.Name == "Freon-13ii");
        GeometryMaterialLibrary.Entry laos = seed.Find(x => x.Name == "Lanthanum oxysulfide");
        // Эталон — NIST STAR, он же `matdb.star_material_composition` id 165 и 181.
        string nistFreon = "6:0.061309 9:0.290924 53:0.647767";
        string nistLaOs = "8:0.0936 16:0.093778 57:0.812622";
        foreach (var t in new[] { Tuple.Create(freon, nistFreon, OldFreon), Tuple.Create(laos, nistLaOs, OldLaOs) })
        {
            string got = Fr(t.Item1.ElementFractions);
            bool ok = got == Fr(GeometryMaterialSeed.Fractions(t.Item2));
            if (!ok) defects++;
            GeometryMaterial now = Packed(t.Item1.Name, t.Item1.Density, got);
            GeometryMaterial nist = Packed(t.Item1.Name, t.Item1.Density, t.Item2);
            Console.WriteLine("   «{0}» засев: {1}; NIST: {2} — {3}; μ засева/NIST: 34.5 кэВ ×{4:F3}, 60 кэВ ×{5:F3}, 100 кэВ ×{6:F3}",
                              t.Item1.Name, got, Fr(GeometryMaterialSeed.Fractions(t.Item2)), ok ? "верно" : "ДЕФЕКТ",
                              Ratio(now, nist, 34.5), Ratio(now, nist, 60.0), Ratio(now, nist, 100.0));
        }

        string path = GeometryMaterialStore.FilePath;
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        Console.WriteLine();
        Console.WriteLine("== AMBER104 (2) и перенос AMBER107: файл библиотеки через склад ==");
        Console.WriteLine("   файл склада: {0}", path);
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(appDir), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("   файл склада НЕ в каталоге пробы — писать туда нельзя, раздел пропущен");
            defects++;
            return;
        }

        string backup = File.Exists(path) ? File.ReadAllText(path) : null;
        FieldInfo legacyField = typeof(GeometryMaterialLibrary.Entry).GetField("LegacyFormula");
        MethodInfo legacyWords = typeof(GeometryMaterialLibrary).GetMethod("LegacyFormulaProblem", BindingFlags.Public | BindingFlags.Static);
        Console.WriteLine("   Entry.LegacyFormula: {0}; LegacyFormulaProblem: {1}", legacyField != null ? "есть" : "НЕТ",
                          legacyWords != null ? "есть" : "НЕТ");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // (а) Файл поколения 8 — сохранён ДО этой правки.
            StringBuilder x = new StringBuilder();
            x.AppendLine("<?xml version=\"1.0\"?>");
            x.AppendLine("<GeometryMaterials SeedVersion=\"8\"><Materials>");
            string[][] formulas =
            {
                // имя, формула, ждём: flag — отказ словами; иначе — состав атомов
                new[] { "p172 CS1 I1", "CS1 I1", "flag" }, new[] { "p172 NI1", "NI1", "flag" },
                new[] { "p172 PB1", "PB1", "flag" }, new[] { "p172 SI1 O2", "SI1 O2", "flag" },
                new[] { "p172 CO2", "CO2", "flag" },
                new[] { "p172 Cs1 I1", "Cs1 I1", "53:1 55:1" }, new[] { "p172 H2O", "H2O", "1:2 8:1" },
                new[] { "p172 Bi4 Ge3 O12", "Bi4 Ge3 O12", "8:12 32:3 83:4" }, new[] { "p172 Ge1", "Ge1", "32:1" },
            };
            foreach (string[] f in formulas)
            {
                x.AppendFormat("<Material Name=\"{0}\" Abbr=\"\" Formula=\"{1}\" Density=\"1\" Kind=\"Source\" />\r\n", f[0], f[1]);
            }

            x.AppendLine("<Material Name=\"Freon-13ii\" Abbr=\"\" Formula=\"\" Density=\"1.8\" Kind=\"Other\"><Fractions>"
                         + "<Element Z=\"6\" Fraction=\"0.061309\" /><Element Z=\"9\" Fraction=\"0.290924\" />"
                         + "<Element Z=\"55\" Fraction=\"0.647767\" /></Fractions></Material>");
            x.AppendLine("<Material Name=\"Lanthanum oxysulfide\" Abbr=\"\" Formula=\"\" Density=\"5.9\" Kind=\"Other\"><Fractions>"
                         + "<Element Z=\"8\" Fraction=\"0.049097\" /><Element Z=\"16\" Fraction=\"0.098383\" />"
                         + "<Element Z=\"57\" Fraction=\"0.85252000000000006\" /></Fractions></Material>");
            x.AppendLine("</Materials></GeometryMaterials>");
            File.WriteAllText(path, x.ToString(), new UTF8Encoding(false));
            GeometryMaterialStore.Reload();
            List<GeometryMaterialLibrary.Entry> list = GeometryMaterialStore.Entries;
            Console.WriteLine("   (а) файл поколения 8: веществ {0}, отказ загрузки: {1}", list.Count,
                              GeometryMaterialStore.LoadError ?? "нет");
            foreach (string[] f in formulas)
            {
                GeometryMaterialLibrary.Entry e = list.Find(y => y.Name == f[0]);
                GeometryMaterial m = GeometryMaterialLibrary.Make(e, e.Density);
                string got = Fr(m.Fractions.Count > 0 ? AtomsOf(e) : new Dictionary<int, double>());
                string words = legacyWords != null ? (string)legacyWords.Invoke(null, new object[] { e }) : null;
                bool ok = f[2] == "flag" ? m.Fractions.Count == 0 && !string.IsNullOrEmpty(words)
                                         : got == f[2] && words == null;
                if (!ok) defects++;
                Console.WriteLine("      «{0}» → {1,-22} ждём {2,-18} {3}{4}", f[1], got == "" ? "(состава нет)" : got,
                                  f[2] == "flag" ? "ОТКАЗ словами" : f[2], ok ? "верно" : "ДЕФЕКТ",
                                  words != null ? "   [" + words + "]" : "");
            }

            GeometryMaterialLibrary.Entry fe = list.Find(y => y.Name == "Freon-13ii");
            GeometryMaterialLibrary.Entry le = list.Find(y => y.Name == "Lanthanum oxysulfide");
            bool fOk = Fr(fe.ElementFractions) == Fr(GeometryMaterialSeed.Fractions(nistFreon));
            bool lOk = Fr(le.ElementFractions) == Fr(GeometryMaterialSeed.Fractions(OldLaOs)) && le.Density == 5.9;
            if (!fOk) defects++;
            if (!lOk) defects++;
            Console.WriteLine("      перенос «Freon-13ii» (побитово прежний засев): {0} — {1}", Fr(fe.ElementFractions), fOk ? "перенесён" : "ДЕФЕКТ: остался прежним");
            Console.WriteLine("      «Lanthanum oxysulfide» правлен рукой (ρ 5.9): {0}, ρ {1:R} — {2}", Fr(le.ElementFractions), le.Density,
                              lOk ? "не тронут" : "ДЕФЕКТ: тронут");

            // (б) Файл поколения 9 и выше — формула набрана при новом разборе.
            File.WriteAllText(path, "<?xml version=\"1.0\"?>\r\n<GeometryMaterials SeedVersion=\"9\"><Materials>"
                              + "<Material Name=\"p172 CO2\" Abbr=\"\" Formula=\"CO2\" Density=\"1\" Kind=\"Source\" />"
                              + "</Materials></GeometryMaterials>\r\n", new UTF8Encoding(false));
            GeometryMaterialStore.Reload();
            GeometryMaterialLibrary.Entry co2 = GeometryMaterialStore.Entries.Find(y => y.Name == "p172 CO2");
            string co2Words = legacyWords != null ? (string)legacyWords.Invoke(null, new object[] { co2 }) : null;
            bool co2Ok = Fr(AtomsOf(co2)) == "6:1 8:2" && GeometryMaterialLibrary.Make(co2, 1.0).Fractions.Count == 2 && co2Words == null;
            if (!co2Ok) defects++;
            Console.WriteLine("   (б) файл поколения 9: «CO2» → {0} — {1}", Fr(AtomsOf(co2)), co2Ok ? "верно (C O2, без отказа)" : "ДЕФЕКТ");

            // (в) Библиотека Amber ЧЕРЕЗ склад (копия, источник только читается).
            if (amberLib != null)
            {
                File.Copy(amberLib, path, true);
                GeometryMaterialStore.Reload();
                List<GeometryMaterialLibrary.Entry> amber = GeometryMaterialStore.Entries;
                StringBuilder text = new StringBuilder();
                Composition("склад " + amberLib, amber, text);
                int flagged = 0;
                foreach (GeometryMaterialLibrary.Entry e in amber)
                {
                    if (legacyWords != null && legacyWords.Invoke(null, new object[] { e }) != null) flagged++;
                }

                Console.WriteLine("   (в) библиотека Amber через склад: отказов по регистру {0}", flagged);
                if (dump != null)
                {
                    File.WriteAllText(dump + ".store", text.ToString(), new UTF8Encoding(false));
                    Console.WriteLine("   состав «R» склада — в {0}.store", dump);
                }
            }
        }
        finally
        {
            if (backup != null) File.WriteAllText(path, backup);
            else if (File.Exists(path)) File.Delete(path);
            GeometryMaterialStore.Reload();
        }
    }

    /// <summary>Атомы формулы записи (для записей без долей), как разбирает приложение.</summary>
    static Dictionary<int, double> AtomsOf(GeometryMaterialLibrary.Entry e)
    {
        return GeometryMaterialLibrary.ParseFormula(e.Formula);
    }
}
