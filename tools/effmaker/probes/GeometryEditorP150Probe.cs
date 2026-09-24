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
        string lib = null, dump = null, corpus = null;
        foreach (string a in args)
        {
            if (a == "--eff") eff = true;
            else if (a.StartsWith("--lib=", StringComparison.Ordinal)) lib = a.Substring(6);
            else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dump = a.Substring(7);
            else if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
        }

        Formula(lib, dump);
        Negative(eff, corpus);
        Scene();

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
            Case("H2 Xx1", null), Case("2H", null), Case("H2.O", null),
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
}
