using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

// (`AMBER323`, полоса `a323` 13.11.2026) МАГНИТНЫЙ ЭКРАН: приёмка слоя и шаблона LaBr3.
//
// Решение Amber 11.11.2026 вопросником, дословно: «Новый боковой слой в модели (Рекомендую)»
// (поле «магнитный экран»: толщина, материал, длина вдоль кристалла; умолчание 0 — корпус и
// склад не сдвигаются; пермаллой Ni 80 % / Fe 20 % — материалом конфигурации).
//
// Что проверяется (код 1 — хоть один провал):
//   (1) шаблон «AtomSpectra Pro LaBr3(Ce)»: размеры и вещества по слову Amber;
//   (2) `.in` туда и обратно: ключи экрана пишутся только у заданного экрана, читаются назад,
//       текст устойчив; без экрана — ни строки; XML туда и обратно, без экрана — ни элемента;
//   (3) клеймо: экран Py ≠ без экрана ≠ экран-воздух (та же раскладка, ослабления нет);
//   (4) сцена: область пермаллоя на своём месте, областей ≤ 16 (RM_MAX_REG GPU);
//   (5) шаблоны: перенос экрана своим шаблоном, сброс экрана чужим вшитым; боковая постановка
//       с экраном — отказ словами;
//   (6) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ФИЗИКИ: изотропное поле (ISO), вход в кристалл без рассеяния
//       (`ScoreEntranceOnly`) — экран Py и экран-воздух против НЕЗАВИСИМОЙ квадратуры лучей
//       (1/4π)∫dA∫cosθ·exp(−Σμᵢlᵢ)dΩ (пути — пересечением с цилиндрами и плоскостями слоёв,
//       раскладка выписана в пробе заново), μ — из `matdb` (`AttenuationData`); рядом —
//       пропускание экрана по нормали exp(−μρt) и плоская оценка 2·E3(Σμt). Потом — пик ISO и точка на оси (торец): точке
//       экран почти не мешает (первичные лучи входят через торец, экран — сбоку).
//   --write-scenes=<каталог> — записать сцены шаблона для `CorpusMatrixProbe` (сверка ЦП ↔ GPU):
//       `LaBr3_point0`, `LaBr3_point50`, `LaBr3_marinelli58` (колодец Ø58 — корпус Ø55 входит) и `LaBr3_beaker90`.
//
//   magneticshieldprobea323 [--n=1000000] [--npeak=300000] [--write-scenes=<каталог>] [--sabotage]
//   --sabotage — порча: экран считается вакуумом и в «Py»-плече; (6) ОБЯЗАН отказать.
static class MagneticShieldProbeA323
{
    const string PresetName = "AtomSpectra Pro LaBr3(Ce)";
    static int failed;

    static void Check(string what, bool ok, string detail = null)
    {
        Console.WriteLine("   {0,-66} {1}{2}", what, ok ? "ок" : "ПРОВАЛ", detail != null ? "  " + detail : "");
        if (!ok) failed++;
    }

    static string F(double v, string fmt = "F4")
    {
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        int n = 1000000, npeak = 300000;
        string scenes = null;
        bool sabotage = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--npeak=", StringComparison.Ordinal)) npeak = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--write-scenes=", StringComparison.Ordinal)) scenes = a.Substring(15);
            else if (a == "--sabotage") sabotage = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        GlobalConfigManager.GetInstance();
        GeometryModel g = LaBr3();
        if (g == null)
        {
            Console.Error.WriteLine("нет шаблона «" + PresetName + "»");
            return 2;
        }

        if (scenes != null)
        {
            return WriteScenes(scenes);
        }

        // (1) шаблон
        Console.WriteLine("(1) шаблон «{0}»:", PresetName);
        Console.WriteLine("   " + g.Describe());
        Check("кристалл LaBr3 Ø25 × 25 мм, цилиндр",
              g.Shape == CrystalShape.Cylinder && g.CrystalDiameter == 25.0 && g.CrystalHeight == 25.0
              && g.Crystal.Name == "Lanthanum bromide", g.Crystal.Name + " " + F(g.Crystal.Density, "F2"));
        double front = g.FrontReflectorThickness + g.FrontGapThickness + g.FrontCladdingThickness;
        Check("от кристалла до наружной плоскости крышки 8 мм", Math.Abs(front - 8.0) < 1e-12, F(front, "F3") + " мм");
        double outer = 0.5 * g.CrystalDiameter + g.SideReflectorThickness + g.SideGapThickness + g.MagneticShieldThickness
                       + g.SideCladdingThickness;
        Check("корпус на радиусе прибора: Ø55 (боковой зазор цилиндра, решение Amber 13.11.2026)",
              Math.Abs(outer - 27.5) < 1e-9 && g.SideGapThickness == 9.75, "r " + F(outer, "F2") + " мм, зазор сбоку "
              + F(g.SideGapThickness, "F2") + " мм");
        Check("алюминий 3.0 мм у торца и бока (оболочка 1.5 + корпус 1.5)",
              g.Cladding.Name == "Aluminum" && g.FrontCladdingThickness == 3.0 && g.SideCladdingThickness == 3.0);
        Check("экран: пермаллой 0.25 мм на всю высоту кристалла (25 мм)",
              g.HasMagneticShield && g.MagneticShieldThickness == 0.25 && g.MagneticShieldLength == 25.0
              && g.MagneticShield.Name == "Permalloy");
        double ni, fe;
        g.MagneticShield.Fractions.TryGetValue(28, out ni);
        g.MagneticShield.Fractions.TryGetValue(26, out fe);
        Check("пермаллой: Ni 0.80 / Fe 0.20 по массе, 8.7 г/см³",
              Math.Abs(ni - 0.8) < 1e-9 && Math.Abs(fe - 0.2) < 1e-9 && g.MagneticShield.Density == 8.7,
              "Ni " + F(ni, "F3") + ", Fe " + F(fe, "F3") + ", ρ " + F(g.MagneticShield.Density, "F2"));
        Check("в веществах слоёв нет неизвестных элементов", g.UnknownElementProblem() == null, g.UnknownElementProblem());
        Console.WriteLine();

        // (2) `.in` и XML
        Console.WriteLine("(2) файл `.in` и XML:");
        string text = GeometryWriter.Render(g);
        Check("`.in` несёт DS_MagneticShieldThickness/Length и блок вещества",
              text.Contains("DS_MagneticShieldThickness = 0.025 cm") && text.Contains("DS_MagneticShieldLength = 2.5 cm")
              && text.Contains("M_DS_MagneticShield.MName = Permalloy"));
        string tmp = Path.Combine(Path.GetTempPath(), "a323_shield_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".in");
        GeometryWriter.Save(g, tmp);
        GeometryModel back = GeometryModel.Load(tmp);
        File.Delete(tmp);
        Check("прочитан обратно: толщина, длина, вещество",
              Math.Abs(back.MagneticShieldThickness - 0.25) < 1e-9 && Math.Abs(back.MagneticShieldLength - 25.0) < 1e-9
              && back.MagneticShield.Name == "Permalloy" && Math.Abs(back.MagneticShield.Density - 8.7) < 1e-9,
              F(back.MagneticShieldThickness) + " / " + F(back.MagneticShieldLength) + " / " + back.MagneticShield.Name);
        Check("текст прочитанного = текст исходного (клеймо не плывёт)", GeometryWriter.Render(back) == text);
        GeometryModel none = g.Clone();
        none.MagneticShieldThickness = 0.0;
        none.MagneticShieldLength = 0.0;
        none.MagneticShield = new GeometryMaterial();
        Check("без экрана в `.in` ни строки экрана", !GeometryWriter.Render(none).Contains("MagneticShield"));
        string xmlNone = Xml(none), xmlShield = Xml(g);
        Check("без экрана в XML ни элемента экрана", !xmlNone.Contains("MagneticShield"));
        GeometryModel xback = FromXml(xmlShield);
        Check("XML с экраном туда и обратно",
              xback.MagneticShieldThickness == 0.25 && xback.MagneticShieldLength == 25.0
              && xback.MagneticShield.Name == "Permalloy" && Xml(xback) == xmlShield);
        GeometryModel halfSet = none.Clone();
        halfSet.MagneticShieldThickness = 0.25;
        Check("толщина без длины: в сцену не входит, текст как без экрана",
              !halfSet.HasMagneticShield && GeometryWriter.Render(halfSet) == GeometryWriter.Render(none));
        Console.WriteLine();

        // (3) клеймо
        Console.WriteLine("(3) клеймо матрицы:");
        // «Пустой» экран — воздух: та же раскладка и те же радиусы, ослабления нет (0.25 мм
        // воздуха — 3·10⁻⁵ г/см²). Вакуумом («вещество не задано») нельзя: такой слой счёт
        // отказывает словами (`GeometryModel.MissingMaterialProblem`, `AMBER307`).
        GeometryModel vac = g.Clone();
        vac.MagneticShield = Lib("Air, dry");
        var options = new ResponseMatrixOptions();
        string sPy = ResponseMatrix.ComputeStamp(g, options), sNone = ResponseMatrix.ComputeStamp(none, options),
               sVac = ResponseMatrix.ComputeStamp(vac, options);
        Check("экран Py, без экрана и экран-воздух — три разных клейма", sPy != sNone && sPy != sVac && sVac != sNone);
        Check("клеймо без экрана = клеймо «толщина без длины»", sNone == ResponseMatrix.ComputeStamp(halfSet, options));
        // (`crit323` Ф1) Нулевые зазоры, экран 10 мм от тыла: остаток рубашки до торца — вещество
        // зазора, и замена воздуха свинцом обязана сменить и сцену, и клеймо. Без экрана при нулевых
        // зазорах вещество зазора в сцену не входит — клеймо прежним правилом одно.
        GeometryModel shortAir = g.Clone();
        shortAir.FrontGapThickness = 0.0;
        shortAir.SideGapThickness = 0.0;
        shortAir.MagneticShieldLength = 10.0;
        shortAir.Gap = Lib("Air, dry");
        GeometryModel shortPb = shortAir.Clone();
        shortPb.Gap = Lib("Lead");
        string scAir = new EfficiencySimulator(shortAir).DescribeScene(), scPb = new EfficiencySimulator(shortPb).DescribeScene();
        Check("экран с остатком: зазор воздух → свинец меняет сцену И клеймо",
              shortAir.MagneticShieldLeavesGap && scAir != scPb
              && ResponseMatrix.ComputeStamp(shortAir, options) != ResponseMatrix.ComputeStamp(shortPb, options),
              ResponseMatrix.ComputeStamp(shortAir, options).Substring(0, 14) + " / "
              + ResponseMatrix.ComputeStamp(shortPb, options).Substring(0, 14));
        GeometryModel fullAir = shortAir.Clone();
        fullAir.MagneticShieldLength = 25.0;
        fullAir.FrontReflectorThickness = 0.0;
        GeometryModel fullPb = fullAir.Clone();
        fullPb.Gap = Lib("Lead");
        Check("экран без остатка (вся высота, торец без отражателя): сцена и клеймо от зазора не зависят",
              !fullAir.MagneticShieldLeavesGap
              && new EfficiencySimulator(fullAir).DescribeScene() == new EfficiencySimulator(fullPb).DescribeScene()
              && ResponseMatrix.ComputeStamp(fullAir, options) == ResponseMatrix.ComputeStamp(fullPb, options));
        GeometryModel noneAir = none.Clone();
        noneAir.FrontGapThickness = 0.0;
        noneAir.SideGapThickness = 0.0;
        noneAir.Gap = Lib("Air, dry");
        GeometryModel nonePb = noneAir.Clone();
        nonePb.Gap = Lib("Lead");
        Check("без экрана и без зазоров: клеймо от вещества зазора не зависит (прежнее правило)",
              ResponseMatrix.ComputeStamp(noneAir, options) == ResponseMatrix.ComputeStamp(nonePb, options));
        Console.WriteLine();

        // (4) сцена
        Console.WriteLine("(4) сцена переноса:");
        var sim = new EfficiencySimulator(g);
        string scene = sim.DescribeScene();
        Console.WriteLine("   " + scene.Replace("\r", "").Replace("\n", "\n   "));
        GeometryModel cm = g.InCentimeters();
        double rGapCm = 0.5 * cm.CrystalDiameter + cm.SideReflectorThickness + cm.SideGapThickness;
        double rShCm = rGapCm + cm.MagneticShieldThickness, rDetCm = rShCm + cm.SideCladdingThickness;
        string shieldRegion = string.Format(CultureInfo.InvariantCulture, "Permalloy r[{0:F2}..{1:F2}] z[0.00..2.50]", rGapCm, rShCm);
        Check("в сцене есть область пермаллоя за боковым зазором", scene.Contains(shieldRegion), shieldRegion);
        GeometryModel emptyShield = g.Clone();
        emptyShield.MagneticShield = new GeometryMaterial();
        Check("экран без вещества — отказ счёта словами (`MissingMaterialProblem`)",
              !string.IsNullOrEmpty(emptyShield.MissingMaterialProblem()), emptyShield.MissingMaterialProblem());
        string sceneNone = new EfficiencySimulator(none).DescribeScene();
        Check("без экрана: областей на две меньше, корпус от r 1.45", CountRegions(sceneNone) == CountRegions(scene) - 2
              && sceneNone.Contains(string.Format(CultureInfo.InvariantCulture, "Aluminum r[{0:F2}..{1:F2}]", rGapCm,
                                                  rGapCm + cm.SideCladdingThickness)),
              CountRegions(sceneNone).ToString(CultureInfo.InvariantCulture));
        // (`AMBER323`, решение Amber 13.11.2026 «Разрешить, умолчание 0») Боковой зазор цилиндра — в сцене и клейме.
        GeometryModel sideGap0 = none.Clone();
        sideGap0.SideGapThickness = 0.0;
        GeometryModel sideGap1 = none.Clone();
        sideGap1.SideGapThickness = 1.0;
        Check("цилиндр: боковой зазор 1 мм против 0 — другая сцена и другое клеймо",
              new EfficiencySimulator(sideGap0).DescribeScene() != new EfficiencySimulator(sideGap1).DescribeScene()
              && ResponseMatrix.ComputeStamp(sideGap0, options) != ResponseMatrix.ComputeStamp(sideGap1, options));
        GeometryModel box = g.Clone();
        box.Shape = CrystalShape.Box;
        box.CrystalBoxX = 20.0; box.CrystalBoxY = 20.0; box.CrystalBoxZ = 30.0;
        box.DropDeadCrystalSize();
        string sceneBox = new EfficiencySimulator(box).DescribeScene();
        Console.WriteLine("   брусок: " + sceneBox);
        Check("брусок 20×20×30 торцом к пробе: экран 25 мм от тыла рамкой, ниже — зазор",
              sceneBox.Contains(string.Format(CultureInfo.InvariantCulture, "Permalloy {0:F2}x{0:F2} z[0.50..3.00]", 2.0 * (1.0 + rShCm - 1.25)))
              && sceneBox.Contains(string.Format(CultureInfo.InvariantCulture, "Air, dry {0:F2}x{0:F2} z[-0.50..0.50]", 2.0 * (1.0 + rShCm - 1.25)))
              && sceneBox.Contains(string.Format(CultureInfo.InvariantCulture, "Aluminum {0:F2}x{0:F2} z[-0.50..3.00]", 2.0 * (1.0 + rDetCm - 1.25))));
        int regions = CountRegions(scene);
        Check("областей ≤ 16 (RM_MAX_REG ядра GPU)", regions > 0 && regions <= 16, regions.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine();

        // (5) шаблоны и отказ
        Console.WriteLine("(5) шаблоны и боковая постановка:");
        GeometryTemplate mine = GeometryTemplate.FromModel("mine", g);
        GeometryModel target = GeometryEditorPanel.Blank();
        mine.Apply(target);
        Check("свой шаблон переносит экран", target.HasMagneticShield && target.MagneticShield.Name == "Permalloy"
              && mine.Fingerprint().Contains(";msh=0.25"));
        GeometryTemplate plain = GeometryTemplate.FromModel("plain", none);
        Check("отпечаток шаблона без экрана — без полей экрана", !plain.Fingerprint().Contains("msh"));
        GeometryModel switched = g.Clone();
        GeometryPresets.Items.Find(p => p.Name == "Atom Spectra Pro 80x80").Apply(switched);
        Check("вшитый шаблон 80x80 после LaBr3 снимает экран", !switched.HasMagneticShield
              && switched.MagneticShieldThickness == 0.0 && switched.MagneticShield.IsEmpty);
        GeometryModel side = g.Clone();
        side.Shape = CrystalShape.Box;
        side.CrystalBoxX = 20.0; side.CrystalBoxY = 20.0; side.CrystalBoxZ = 30.0;
        side.DropDeadCrystalSize();
        side.Facing = GeometryDetectorFacing.Side;
        Check("брусок, проба сбоку, с экраном — отказ словами", side.FacingError.Length > 0, side.FacingError);
        side.MagneticShieldThickness = 0.0;
        Check("тот же без экрана — годен", side.FacingError.Length == 0);
        Console.WriteLine();

        // (6) физика
        Console.WriteLine("(6) положительный контроль: экран срезает мягкие кванты сбоку");
        Physics(g, vac, none, n, npeak, sabotage);

        Console.WriteLine();
        if (sabotage) Console.WriteLine("ПОРЧА: проба ОБЯЗАНА отказать — провалов {0}", failed);
        Console.WriteLine(failed == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : "ПРОВАЛОВ: " + failed);
        return failed == 0 ? 0 : 1;
    }

    static GeometryModel LaBr3()
    {
        GeometryPresets.Preset preset = GeometryPresets.Items.Find(p => p.Name == PresetName);
        if (preset == null) return null;
        GeometryModel g = GeometryEditorPanel.Blank();
        preset.Apply(g);
        g.Name = "LaBr3";
        g.SourceType = GeometrySourceType.Point;
        g.PointDistance = 0.0;
        return g;
    }

    static int WriteScenes(string dir)
    {
        Directory.CreateDirectory(dir);
        GeometryModel p0 = LaBr3();
        p0.Name = "LaBr3_point0";
        GeometryModel p50 = LaBr3();
        p50.Name = "LaBr3_point50";
        p50.PointDistance = 50.0;
        GeometryModel mar = LaBr3();
        mar.Name = "LaBr3_marinelli58";
        mar.SourceType = GeometrySourceType.Marinelli;
        mar.MarinelliHoleDiameter = 58.0;
        mar.MarinelliHoleSideThickness = 1.5;
        mar.MarinelliHoleEndWallThickness = 1.5;
        mar.MarinelliHoleHeight = 40.0;
        mar.MarinelliBeakerDiameter = 130.0;
        mar.MarinelliBeakerHeight = 90.0;
        mar.MarinelliSideThickness = 1.5;
        mar.MarinelliEndWallThickness = 1.5;
        mar.MarinelliSourceHeight = 80.0;
        mar.MarinelliToDetectorDistance = 0.0;
        mar.Source = Lib("Water, liquid");
        mar.BeakerWall = Lib("Polypropylene");
        GeometryModel cup = LaBr3();
        cup.Name = "LaBr3_beaker90";
        cup.SourceType = GeometrySourceType.Cylinder;
        cup.BeakerDiameter = 90.0;
        cup.BeakerHeight = 22.0;
        cup.SourceHeight = 20.0;
        cup.BeakerSideWallThickness = 1.0;
        cup.BeakerEndWallThickness = 1.0;
        cup.BeakerToDetectorDistance = 0.0;
        cup.Source = Lib("Water, liquid");
        cup.BeakerWall = Lib("Polypropylene");
        foreach (GeometryModel m in new[] { p0, p50, mar, cup })
        {
            string issues = string.Join("; ", GeometryScenes.Inconsistencies(m).ConvertAll(i => i.Field + ":" + i.Resource));
            string path = Path.Combine(dir, m.Name + ".in");
            GeometryWriter.Save(m, path);
            Console.WriteLine("{0}: {1}{2}", m.Name, path, issues.Length > 0 ? "  связки: " + issues : "");
        }

        return 0;
    }

    static GeometryMaterial Lib(string name)
    {
        GeometryMaterialLibrary.Entry e = GeometryMaterialLibrary.ByName(name);
        return GeometryMaterialLibrary.Make(e, e.Density);
    }

    static int CountRegions(string scene)
    {
        // `DescribeScene`: «cm: область; область; …».
        string body = scene.StartsWith("cm: ", StringComparison.Ordinal) ? scene.Substring(4) : scene;
        return body.Length == 0 ? 0 : body.Split(new[] { "; " }, StringSplitOptions.None).Length;
    }

    static string Xml(GeometryModel g)
    {
        using (var sw = new StringWriter(CultureInfo.InvariantCulture))
        {
            new XmlSerializer(typeof(GeometryModel)).Serialize(sw, g);
            return sw.ToString();
        }
    }

    static GeometryModel FromXml(string xml)
    {
        using (var sr = new StringReader(xml))
        {
            return (GeometryModel)new XmlSerializer(typeof(GeometryModel)).Deserialize(sr);
        }
    }

    // ---------------- физика ----------------

    /// <summary>μ/ρ смеси, см²/г, по долям вещества — из `matdb` тем же путём, что перенос.</summary>
    static double MuRho(GeometryMaterial m, double e)
    {
        double sum = 0.0;
        foreach (KeyValuePair<int, double> kv in m.Fractions) sum += kv.Value * AttenuationData.MassAttenuation(kv.Key, e);
        return sum;
    }

    /// <summary>2·E3(τ) = 2∫₀¹ μ e^(−τ/μ) dμ — пропускание плоского слоя при косинусном падении.</summary>
    static double TwoE3(double tau)
    {
        if (tau <= 0.0) return 1.0;
        const int steps = 20000;
        double h = 1.0 / steps, s = 0.0;
        for (int i = 0; i < steps; i++)
        {
            double mu = (i + 0.5) * h;
            s += mu * Math.Exp(-tau / mu);
        }

        return 2.0 * s * h;
    }

    static void Physics(GeometryModel py, GeometryModel vac, GeometryModel none, int n, int npeak, bool sabotage)
    {
        double[] energies = { 20.0, 25.0, 30.0, 35.0, 40.0, 50.0, 60.0, 80.0, 100.0, 150.0, 661.657 };
        GeometryMaterial pm = py.MagneticShield;
        double tCm = py.MagneticShieldThickness / GeometryModel.MmPerCm;
        Console.WriteLine("   пермаллой {0} г/см³, {1} мм = {2} г/см²; μ/ρ из `matdb` (AttenuationData, сумма каналов):",
                          F(pm.Density, "F2"), F(py.MagneticShieldThickness, "F2"), F(pm.Density * tCm, "F4"));
        Console.WriteLine("   {0,9} {1,10} {2,9} {3,12} {4,12}", "E, кэВ", "μ/ρ, см²/г", "μρt", "exp(−μρt)", "2·E3(μρt)");
        foreach (double e in energies)
        {
            double mt = MuRho(pm, e) * pm.Density * tCm;
            Console.WriteLine("   {0,9} {1,10} {2,9} {3,12} {4,12}", F(e, "F1"), F(MuRho(pm, e), "F3"), F(mt, "F4"),
                              Math.Exp(-mt).ToString("E3", CultureInfo.InvariantCulture), TwoE3(mt).ToString("E3", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();

        // Аналитика входа в кристалл в изотропном поле: грани кристалла как плоские слои.
        double rc = 0.5 * py.CrystalDiameter / GeometryModel.MmPerCm, hc = py.CrystalHeight / GeometryModel.MmPerCm;
        double aFace = Math.PI * rc * rc, aSide = 2.0 * Math.PI * rc * hc;
        Func<GeometryMaterial, double, double, double> layer = (m, mm, e) => MuRho(m, e) * m.Density * mm / GeometryModel.MmPerCm;
        GeometryModel iso = py.Clone();
        iso.Scene = GeometrySceneKind.Iso;
        GeometryScenes.Iso(iso);
        GeometryModel isoVac = vac.Clone();
        isoVac.Scene = GeometrySceneKind.Iso;
        isoVac.FieldRadius = iso.FieldRadius;
        GeometryModel isoNone = none.Clone();
        isoNone.Scene = GeometrySceneKind.Iso;
        isoNone.FieldRadius = iso.FieldRadius;
        if (sabotage) iso = isoVac.Clone();

        Console.WriteLine("   ISO (R = {0} мм), ВХОД в кристалл без рассеяния, см² на единичный флюенс, {1} историй.",
                          F(iso.FieldRadius, "F0"), n);
        Console.WriteLine("   «лучи» — независимая квадратура: (1/4π)∫dA∫cosθ·exp(−Σμᵢlᵢ)dΩ по поверхности кристалла,");
        Console.WriteLine("   пути lᵢ — пересечением луча с цилиндрами и плоскостями слоёв (раскладка выписана в пробе");
        Console.WriteLine("   заново, не из симулятора), μᵢ = ρ·Σw·μ/ρ из `matdb`. «плоск.» — слои как плоские пластины,");
        Console.WriteLine("   2·E3(Σμt) на грань (без краёв), справочно.");
        double s4 = 0.25 * (2.0 * aFace + aSide);
        double r0 = Rays(py, 0.0, true);
        Check("квадратура без ослабления = S/4 (Коши)", Math.Abs(r0 / s4 - 1.0) < 2e-3, F(r0) + " против " + F(s4));
        Console.WriteLine("   {0,7} {1,15} {2,8} {3,6} {4,15} {5,8} {6,6} {7,9} {8,9} {9,9}", "E", "A(Py) MC", "лучи", "σ",
                          "A(возд.) MC", "лучи", "σ", "Py/возд.", "лучи", "плоск.");
        double worstPull = 0.0;
        foreach (double e in energies)
        {
            if (e > 200.0) continue;
            double ePy, eVac;
            double aPy = Entrance(iso, e, n, out ePy), aVac = Entrance(isoVac, e, n, out eVac);
            double rPy = Rays(py, e, true), rVac = Rays(py, e, false);
            double pPy = (aPy - rPy) / (aPy * ePy / 100.0), pVac = (aVac - rVac) / (aVac * eVac / 100.0);
            double tf = layer(py.Reflector, py.FrontReflectorThickness, e) + layer(py.Gap, py.FrontGapThickness, e)
                        + layer(py.Cladding, py.FrontCladdingThickness, e);
            double tb = layer(py.Cladding, py.MountingThickness, e);
            double ts = layer(py.Reflector, py.SideReflectorThickness, e) + layer(py.Cladding, py.SideCladdingThickness, e);
            double tp = layer(pm, py.MagneticShieldThickness, e);
            double slab = (aFace * TwoE3(tf) + aFace * TwoE3(tb) + aSide * TwoE3(ts + tp))
                          / (aFace * TwoE3(tf) + aFace * TwoE3(tb) + aSide * TwoE3(ts));
            Console.WriteLine("   {0,7} {1,15} {2,8} {3,6} {4,15} {5,8} {6,6} {7,9} {8,9} {9,9}", F(e, "F1"),
                              F(aPy) + "±" + F(ePy, "F2") + "%", F(rPy), F(pPy, "F1"),
                              F(aVac) + "±" + F(eVac, "F2") + "%", F(rVac), F(pVac, "F1"),
                              F(aPy / aVac), F(rPy / rVac), F(slab));
            if (e >= 25.0) worstPull = Math.Max(worstPull, Math.Max(Math.Abs(pPy), Math.Abs(pVac)));
            if (e == 30.0)
            {
                Check("ISO 30 кэВ: экран срезает вход заметно (Py/воздух < 0.6)", aPy / aVac < 0.6, F(aPy / aVac));
            }
        }

        Check("ISO 25–150 кэВ: вход MC = квадратуре лучей в пределах 4σ (Py и воздух)", worstPull <= 4.0,
              "худшее " + F(worstPull, "F1") + " σ");
        Console.WriteLine();

        Console.WriteLine("   ISO, ПИК полного поглощения, см², {0} историй:", npeak);
        foreach (double e in new[] { 30.0, 60.0, 100.0, 661.657 })
        {
            double ePy, eVac;
            double aPy = Peak(iso, e, npeak, out ePy), aVac = Peak(isoVac, e, npeak, out eVac);
            Console.WriteLine("   {0,7}  Py {1} ± {2} %   воздух {3} ± {4} %   отношение {5}", F(e, "F1"), F(aPy), F(ePy, "F2"),
                              F(aVac), F(eVac, "F2"), F(aPy / aVac));
        }

        Console.WriteLine();
        Console.WriteLine("   Точка на оси у торца (0 и 50 мм), эффективность пика, {0} историй:", npeak);
        foreach (double d in new[] { 0.0, 50.0 })
        {
            GeometryModel a = py.Clone(), b = vac.Clone();
            a.PointDistance = d; b.PointDistance = d;
            if (sabotage) a = b.Clone();
            foreach (double e in new[] { 30.0, 60.0, 661.657 })
            {
                double ea, eb;
                double pa = Peak(a, e, npeak, out ea), pb = Peak(b, e, npeak, out eb);
                double r = pa / pb, s = r * Math.Sqrt(Math.Pow(ea / 100.0, 2) + Math.Pow(eb / 100.0, 2));
                Console.WriteLine("   d = {0,4} мм, {1,7} кэВ: Py {2} ± {3} %, воздух {4} ± {5} %, отношение {6} ± {7}",
                                  F(d, "F0"), F(e, "F1"), F(pa, "E4"), F(ea, "F2"), F(pb, "E4"), F(eb, "F2"), F(r), F(s));
                if (e == 30.0 && d == 0.0)
                {
                    Check("точка у торца, 30 кэВ: экран почти не мешает (отношение > 0.97)", r > 0.97, F(r));
                }
            }
        }
    }

    /// <summary>
    /// Вход в кристалл без рассеяния в изотропном поле единичного флюенса, см², квадратурой:
    /// (1/4π)∫dA∫cosθ·exp(−τ)dΩ. Слои выписаны здесь заново по полям модели (цилиндр,
    /// торец к пробе): отражатель, зазор, экран и его продолжение веществом зазора до торца,
    /// корпус, оправа за кристаллом; снаружи — пустота. <paramref name="e"/> = 0 — без ослабления.
    /// </summary>
    static double Rays(GeometryModel g, double e, bool shieldIsPy)
    {
        double c = 1.0 / GeometryModel.MmPerCm;
        double rc = 0.5 * g.CrystalDiameter * c, hc = g.CrystalHeight * c;
        double tfr = g.FrontReflectorThickness * c, tsr = g.SideReflectorThickness * c;
        double tfg = g.FrontGapThickness * c, tsg = g.SideGapThickness * c;
        double tfc = g.FrontCladdingThickness * c, tsc = g.SideCladdingThickness * c, tm = g.MountingThickness * c;
        double tsh = g.MagneticShieldThickness * c, lsh = g.MagneticShieldLength * c;
        double rGap = rc + tsr + tsg, rSh = rGap + tsh, rDet = rSh + tsc;
        double zs = Math.Max(-(tfr + tfg), hc - lsh);
        Func<GeometryMaterial, double> mu = m => e > 0.0 ? MuRho(m, e) * m.Density : 0.0;
        double muR = mu(g.Reflector), muG = mu(g.Gap), muC = mu(g.Cladding);
        double muS = shieldIsPy ? mu(g.MagneticShield) : mu(Lib("Air, dry"));
        // (rIn, rOut, z0, z1, μ) — первая подходящая побеждает, как в сцене.
        var regs = new List<double[]>
        {
            new[] { 0.0, rc, -tfr, 0.0, muR }, new[] { rc, rc + tsr, -tfr, hc, muR },
            new[] { 0.0, rc + tsr, -(tfr + tfg), -tfr, muG }, new[] { rc + tsr, rGap, -(tfr + tfg), hc, muG },
            new[] { rGap, rSh, zs, hc, muS }, new[] { rGap, rSh, -(tfr + tfg), zs, muG },
            new[] { 0.0, rDet, -(tfr + tfg + tfc), -(tfr + tfg), muC }, new[] { rSh, rDet, -(tfr + tfg), hc, muC },
            new[] { 0.0, rDet, hc, hc + tm, muC },
        };
        double[] radii = { rc, rc + tsr, rGap, rSh, rDet };
        double[] planes = { -(tfr + tfg + tfc), -(tfr + tfg), -tfr, 0.0, zs, hc, hc + tm };
        double zMin = planes[0], zMax = hc + tm;

        const int nMu = 64, nPhi = 96, nR = 48, nZ = 120;
        double dmu = 1.0 / nMu, dphi = 2.0 * Math.PI / nPhi, eps = 1e-9;
        double total = 0.0;
        var ts = new List<double>(32);
        Func<double, double, double, double, double, double, double> tau = (px, py, pz, dx, dy, dz) =>
        {
            ts.Clear();
            ts.Add(0.0);
            double a = dx * dx + dy * dy, b = px * dx + py * dy, cc = px * px + py * py;
            if (a > 0.0)
            {
                foreach (double R in radii)
                {
                    double disc = b * b - a * (cc - R * R);
                    if (disc < 0.0) continue;
                    double sq = Math.Sqrt(disc);
                    double t1 = (-b - sq) / a, t2 = (-b + sq) / a;
                    if (t1 > 0.0) ts.Add(t1);
                    if (t2 > 0.0) ts.Add(t2);
                }
            }

            if (dz != 0.0)
            {
                foreach (double Z in planes)
                {
                    double t = (Z - pz) / dz;
                    if (t > 0.0) ts.Add(t);
                }
            }

            ts.Sort();
            double sum = 0.0;
            for (int i = 0; i + 1 < ts.Count; i++)
            {
                double tm0 = 0.5 * (ts[i] + ts[i + 1]);
                double x = px + tm0 * dx, y = py + tm0 * dy, z = pz + tm0 * dz;
                double r = Math.Sqrt(x * x + y * y);
                if (z < zMin || z > zMax || r > rDet) break;
                foreach (double[] q in regs)
                {
                    if (r >= q[0] && r < q[1] && z >= q[2] && z < q[3])
                    {
                        sum += q[4] * (ts[i + 1] - ts[i]);
                        break;
                    }
                }
            }

            return sum;
        };

        // торец (z = 0, луч назад — к −z) и тыл (z = hc, луч назад — к +z)
        for (int face = 0; face < 2; face++)
        {
            double z0 = face == 0 ? -eps : hc + eps, sz = face == 0 ? -1.0 : 1.0;
            for (int ir = 0; ir < nR; ir++)
            {
                double r = (ir + 0.5) * rc / nR, dA = 2.0 * Math.PI * r * rc / nR;
                double inner = 0.0;
                for (int im = 0; im < nMu; im++)
                {
                    double m = (im + 0.5) * dmu, s = Math.Sqrt(1.0 - m * m);
                    for (int ip = 0; ip < nPhi; ip++)
                    {
                        double f = (ip + 0.5) * dphi;
                        inner += m * Math.Exp(-tau(r, 0.0, z0, s * Math.Cos(f), s * Math.Sin(f), sz * m));
                    }
                }

                total += dA * inner * dmu * dphi;
            }
        }

        // бок (r = rc, луч назад — наружу)
        for (int iz = 0; iz < nZ; iz++)
        {
            double z = (iz + 0.5) * hc / nZ, dA = 2.0 * Math.PI * rc * hc / nZ;
            double inner = 0.0;
            for (int im = 0; im < nMu; im++)
            {
                double m = (im + 0.5) * dmu, s = Math.Sqrt(1.0 - m * m);
                for (int ip = 0; ip < nPhi; ip++)
                {
                    double f = (ip + 0.5) * dphi;
                    inner += m * Math.Exp(-tau(rc + eps, 0.0, z, m, s * Math.Cos(f), s * Math.Sin(f)));
                }
            }

            total += dA * inner * dmu * dphi;
        }

        return total / (4.0 * Math.PI);
    }

    static double Entrance(GeometryModel g, double e, int n, out double err)
    {
        var sim = new EfficiencySimulator(g) { Histories = n, ScoreEntranceOnly = true };
        sim.ResetStream(20261113UL);
        return sim.Efficiency(e, out err);
    }

    static double Peak(GeometryModel g, double e, int n, out double err)
    {
        var sim = new EfficiencySimulator(g) { Histories = n };
        sim.ResetStream(20261113UL);
        return sim.Efficiency(e, out err);
    }
}
