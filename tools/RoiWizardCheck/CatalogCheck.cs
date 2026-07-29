using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.RoiWizard;

// Проверка каталога, переведённого на nucdb.sqlite: читается ли он вообще,
// сходятся ли ветвления рядов с эталоном LibraryFitLab и на месте ли семейства.
static class CatalogCheck
{
    static int failures;

    static void Check(string what, bool ok, string detail)
    {
        Console.WriteLine("{0} {1}{2}", ok ? "  ok  " : "  FAIL", what,
                          detail == null ? "" : "   " + detail);
        if (!ok) failures++;
    }

    static void Near(string what, double actual, double expected, double tolerance)
    {
        Check(what, Math.Abs(actual - expected) <= tolerance,
              string.Format(CultureInfo.InvariantCulture, "{0:G6} (ждали {1:G6})", actual, expected));
    }

    static int Main()
    {
        Stopwatch clock = Stopwatch.StartNew();
        NuclideCatalog catalog = NuclideCatalog.GetInstance();
        clock.Stop();
        Console.WriteLine("загрузка: {0} мс, нуклидов {1}, рядов {2}, семейств {3}, ХРИ {4}",
                          clock.ElapsedMilliseconds, catalog.Nuclides.Count, catalog.Chains.Count,
                          catalog.Families.Count, catalog.XrfElements.Count);
        Console.WriteLine();

        // --- имена и линии ---
        CatalogNuclide tl208 = catalog.Find("Tl-208");
        Check("Tl-208 найден", tl208 != null, null);
        if (tl208 == null) return 1;
        CatalogGammaLine strongest = null;
        foreach (CatalogGammaLine g in tl208.Gamma)
            if (strongest == null || g.Intensity > strongest.Intensity) strongest = g;
        Near("Tl-208 сильнейшая линия, кэВ", strongest.Energy, 2614.511, 0.01);
        Near("Tl-208 её интенсивность, %", strongest.Intensity, 99.754, 0.01);
        Check("Tl-208 есть K-рентген", tl208.Xray.Count > 0, "линий X: " + tl208.Xray.Count);

        CatalogNuclide bi214 = catalog.Find("Bi-214");
        Near("Bi-214 609 кэВ", FindLine(bi214, 609.3), 45.44, 0.05);
        CatalogNuclide k40 = catalog.Find("K-40");
        Near("K-40 1460.8 кэВ", FindLine(k40, 1460.8), 10.66, 0.05);
        Near("K-40 T½, лет", k40.HalfLifeYears, 1.248e9, 1e7);
        Check("K-40 подпись T½", k40.HalfLifeText, "1.25e+09 y");

        // Регистр в nucid значащий: символы Am, Cm, Fm, Pm, Sm, Tm кончаются на m,
        // и поиск маркера изомера без учёта регистра съедал вторую букву — Am-241
        // превращался в «A-241m».
        Check("PrettyName 241AM", NuclideCatalog.PrettyName("241AM"), "Am-241");
        Check("PrettyName 244CM", NuclideCatalog.PrettyName("244CM"), "Cm-244");
        Check("PrettyName 147PM", NuclideCatalog.PrettyName("147PM"), "Pm-147");
        Check("PrettyName 152SM", NuclideCatalog.PrettyName("152SM"), "Sm-152");
        Check("PrettyName 234PAm1", NuclideCatalog.PrettyName("234PAm1"), "Pa-234m1");
        Check("PrettyName 108AGm", NuclideCatalog.PrettyName("108AGm"), "Ag-108m");
        Check("PrettyName 99TCm1", NuclideCatalog.PrettyName("99TCm1"), "Tc-99m1");
        // номер состояния значащий: 163 группы схлопывались в одно имя, у Y-98 их шесть
        Check("PrettyName 152EUm1", NuclideCatalog.PrettyName("152EUm1"), "Eu-152m1");
        Check("PrettyName 152EUm2", NuclideCatalog.PrettyName("152EUm2"), "Eu-152m2");
        Check("PrettyName 208TL", NuclideCatalog.PrettyName("208TL"), "Tl-208");
        Check("Am-241 есть в каталоге", catalog.Find("Am-241") != null, null);
        Check("Cm-244 есть в каталоге", catalog.Find("Cm-244") != null, null);

        CatalogNuclide pa234m = catalog.Find("Pa-234m1");
        Check("Pa-234m найден (изомер)", pa234m != null, pa234m == null ? null : pa234m.Nucid);
        if (pa234m != null)
            Near("Pa-234m 1001.03 кэВ (min l_seqno)", FindLine(pa234m, 1001.03), 0.842, 0.05);

        Console.WriteLine();

        // --- ряды и ветвление: сверка с tools/LibraryFitLab ---
        CatalogChain th232 = catalog.FindChain("th232");
        Check("ряд th232 есть", th232 != null, null);
        if (th232 != null)
        {
            Check("th232 корень", th232.Root, "Th-232");
            Near("th232: ветвление Tl-208", th232.BranchingOf("Tl-208"), 0.3594, 0.002);
            Near("th232: ветвление Po-212", th232.BranchingOf("Po-212"), 0.6406, 0.002);
            Near("th232: ветвление Ac-228", th232.BranchingOf("Ac-228"), 1.0, 0.002);
            Check("th232: состав", th232.Members.Count >= 10,
                  string.Join(", ", th232.Members.ToArray()));
            // порядок обхода, а не по доле: на нём держится «добавить членов ниже»
            Check("th232: порядок ряда",
                  string.Join(",", th232.Members.GetRange(0, 5).ToArray()),
                  "Th-232,Ra-228,Ac-228,Th-228,Ra-224");
            Check("th232: Tl-208 идёт после Pb-212",
                  th232.Members.IndexOf("Tl-208") > th232.Members.IndexOf("Pb-212"), null);
        }

        // Тот же пересчёт, что теперь делает импорт из NucBase: 583.19 кэВ Tl-208
        // при импорте ряда Th-232 обязан лечь в Intencity как 30.5 %, а не 85 %.
        Dictionary<string, double> branching =
            BecquerelMonitor.NucBase.DecayChains.BranchingFrom("232TH");
        Check("NucBase: ряд Th-232 посчитан", branching.Count >= 10, branching.Count + " членов");
        Near("NucBase: множитель 208TL",
             BecquerelMonitor.NucBase.DecayChains.FactorOf(branching, "208TL"), 0.3594, 0.002);
        Near("NucBase: 583.19 на распад Th-232, %",
             85.0 * BecquerelMonitor.NucBase.DecayChains.FactorOf(branching, "208TL"), 30.549, 0.05);
        Near("NucBase: множитель 212BI (сам в ряду целиком)",
             BecquerelMonitor.NucBase.DecayChains.FactorOf(branching, "212BI"), 1.0, 0.002);
        Near("NucBase: неизвестный нуклид не масштабируется",
             BecquerelMonitor.NucBase.DecayChains.FactorOf(branching, "137CS"), 1.0, 1e-9);

        CatalogChain u235 = catalog.FindChain("u235");
        if (u235 != null)
        {
            Near("u235: ветвление Th-227", u235.BranchingOf("Th-227"), 0.9862, 0.002);
            Near("u235: ветвление Fr-223", u235.BranchingOf("Fr-223"), 0.0138, 0.002);
        }

        CatalogChain u238 = catalog.FindChain("u238");
        if (u238 != null)
        {
            // 0.9984, а не 1: недостающие 0.16 % — переход 234mPa -> 234Pa -> 234U,
            // у которого в decay_chain нет процента. Ожидание зафиксировано явно,
            // чтобы допуск не прятал расхождение.
            Near("u238: ветвление Ra-226", u238.BranchingOf("Ra-226"), 0.9984, 0.0005);
            Check("u238 содержит Bi-214", u238.Members.Contains("Bi-214"), null);
        }

        CatalogChain ra226 = catalog.FindChain("ra226");
        if (ra226 != null)
            Check("ra226 содержит Bi-214", ra226.Members.Contains("Bi-214"), null);

        // Tl-208 на распад родителя: 99.754 % x 0.3594 = 35.85 % (эталон recommended_sets.csv)
        if (th232 != null)
            Near("Tl-208 2614 на распад Th-232, %",
                 strongest.Intensity * th232.BranchingOf("Tl-208"), 35.8516, 0.05);

        // Ряд нуклида — самый длинный из содержащих его
        CatalogNuclide pb214 = catalog.Find("Pb-214");
        Check("Pb-214 числится за u238", pb214 == null ? "?" : pb214.Chain, "u238");
        Check("ChainRoot(Pb-214)", catalog.ChainRoot(pb214), "U-238");
        Near("ChainBranchingOf(Tl-208)", catalog.ChainBranchingOf(tl208), 0.3594, 0.002);

        Console.WriteLine();

        // --- семейства ---
        int classified = 0;
        foreach (CatalogNuclide n in catalog.Nuclides)
            if (!string.IsNullOrEmpty(n.Families)) classified++;
        Check("нуклидов с семейством", classified == 121, classified.ToString());
        Check("семейств", catalog.Families.Count == 8, catalog.Families.Count.ToString());
        Check("NORM непустое", Count(catalog.ByFamily("NORM")) == 39,
              Count(catalog.ByFamily("NORM")).ToString());
        Check("POPULAR непустое", Count(catalog.ByFamily("POPULAR")) == 12,
              Count(catalog.ByFamily("POPULAR")).ToString());
        CatalogFamily med = catalog.FindFamily("MED");
        Check("MED с русской подписью", med != null && !string.IsNullOrEmpty(med.TitleRu),
              med == null ? null : med.TitleRu);
        Check("пояснение к стандарту", !string.IsNullOrEmpty(catalog.FamilyStandardRu), null);

        // --- ХРИ ---
        XrfElement pb = catalog.FindElement("Pb");
        Check("ХРИ Pb", pb != null && pb.Lines.Count == 8,
              pb == null ? null : pb.Lines.Count + " линий, Z=" + pb.Z);
        Check("ХРИ Pb контекст ru", pb != null && !string.IsNullOrEmpty(pb.ContextRu), null);

        // Порядок членов MergeCriterion — контракт с comboCriterion: форма приводит
        // SelectedIndex прямо к перечислению, и сдвиг на единицу молча подменил бы
        // критерий. Проверяем и порядок, и множители.
        Check("критериев слияния", Enum.GetValues(typeof(MergeCriterion)).Length == 4,
              Enum.GetValues(typeof(MergeCriterion)).Length.ToString());
        Check("индекс 0 = Sparrow", ((MergeCriterion)0).ToString(), "Sparrow");
        Check("индекс 1 = Measured", ((MergeCriterion)1).ToString(), "Measured");
        Check("индекс 2 = AnchoredSet", ((MergeCriterion)2).ToString(), "AnchoredSet");
        Check("индекс 3 = Manual", ((MergeCriterion)3).ToString(), "Manual");
        Near("Sparrow = 0.85", MergeCriterionInfo.DefaultFactor(MergeCriterion.Sparrow), 0.85, 1e-9);
        Near("Measured = 0.7", MergeCriterionInfo.DefaultFactor(MergeCriterion.Measured), 0.70, 1e-9);
        Near("AnchoredSet = 0.25", MergeCriterionInfo.DefaultFactor(MergeCriterion.AnchoredSet), 0.25, 1e-9);
        Check("плато Measured принимает 0.5", MergeCriterionInfo.IsFactorSane(MergeCriterion.Measured, 0.5), null);
        Check("плато Measured отвергает 0.3", !MergeCriterionInfo.IsFactorSane(MergeCriterion.Measured, 0.3), null);

        // Ветвление ниже точки слияния ветвей: к Pb-210 в ряду U-238 приходят слабая
        // прямая ветка Bi-214 (0.003 %) и основная через Po-214. Раскрытие узла один раз
        // «по накопленному на этот момент» занижало потомков в 33 000 раз.
        if (u238 != null)
        {
            Near("u238: Pb-210 после слияния", u238.BranchingOf("Pb-210"), 0.9984, 0.01);
            Near("u238: Bi-210 (потомок за слиянием)", u238.BranchingOf("Bi-210"), 0.9984, 0.01);
            Near("u238: Po-210 (потомок за слиянием)", u238.BranchingOf("Po-210"), 0.9984, 0.01);
        }

        // Дубли строк в nuclides: 144TBm лежит трижды, 161PM и 35NA дважды
        int tb = 0, pm = 0;
        foreach (CatalogNuclide n in catalog.Nuclides)
        {
            if (n.Name == "Tb-144m") tb++;
            if (n.Name == "Pm-161") pm++;
        }
        Check("Tb-144m в списке один раз", tb <= 1, tb.ToString());
        Check("Pm-161 в списке один раз", pm <= 1, pm.ToString());

        Console.WriteLine();
        CompareWithReference(catalog);

        Console.WriteLine();
        CheckRoiPrimitives(catalog);

        Console.WriteLine();
        CheckMixedChains(catalog);

        Console.WriteLine();
        CheckLibraryMerge(catalog);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ВСЁ ЗЕЛЁНОЕ" : failures + " ПРОВАЛОВ");
        return failures == 0 ? 0 : 1;
    }

    // D1: у каждой области ROI-конфигурации есть примитив подсчёта. Без него
    // MeasurementResultManager.CalculateROI обходит пустой список и возвращает count = 0 —
    // конфигурация мастера рисовала границы и показывала нули по всем областям.
    static void CheckRoiPrimitives(NuclideCatalog catalog)
    {
        // таблицы примитивов в приложении наполняет старт; харнесс поднимает их сам
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

        ResolutionModel resolution = new ResolutionModel(6.664);
        LineSetBuilder builder = new LineSetBuilder(catalog).Reset();
        SourceSelection selection = new SourceSelection();
        selection.Add(catalog, "Th-232", AddMode.Chain);
        List<SpectralLine> lines = builder.BuildRecommendedSet(selection, resolution, null);

        ZoneCalculator zones = new ZoneCalculator(resolution);
        zones.Style = RoiStyle.Zones;
        ROIConfigData config = new SetExporter(resolution, zones)
            .BuildRoiConfig(lines, "harness zones", null);

        Check("области построены", config.ROIDefinitions.Count > 0,
              config.ROIDefinitions.Count + " шт.");
        int empty = 0, wrongType = 0, wrongLimits = 0;
        foreach (ROIDefinitionData roi in config.ROIDefinitions)
        {
            if (roi.ROIPrimitives.Count == 0) { empty++; continue; }
            ROISimpleDifferenceData prim = roi.ROIPrimitives[0] as ROISimpleDifferenceData;
            if (prim == null) { wrongType++; continue; }
            // границы примитива обязаны совпасть с границами области: считается площадь
            // именно нарисованной зоны, а не какого-то своего окна
            if (Math.Abs(prim.LowerLimit - roi.LowerLimit) > 1e-9 ||
                Math.Abs(prim.UpperLimit - roi.UpperLimit) > 1e-9) wrongLimits++;
        }
        Check("у всех областей есть примитив", empty == 0, "пустых: " + empty);
        Check("примитив — BG difference", wrongType == 0, "иного типа: " + wrongType);
        Check("границы примитива равны границам области", wrongLimits == 0,
              "расхождений: " + wrongLimits);
        if (config.ROIDefinitions.Count > 0)
        {
            ROIPrimitiveData first = config.ROIDefinitions[0].ROIPrimitives.Count > 0
                ? config.ROIDefinitions[0].ROIPrimitives[0] : null;
            Check("PrimitiveType в файле", first == null ? null : first.PrimitiveType,
                  "BG difference");
            Check("OperationType в файле", first == null ? null : first.OperationType,
                  "Addition");
        }

        // Режим МАРКЕРОВ: границы области равны −10 («зоны нет»), примитив с таким
        // интервалом дал бы ноль отсчётов и выглядел настроенным. Найдено живым прогоном
        // оператора: первая версия правки создавала такие пустышки.
        ZoneCalculator markers = new ZoneCalculator(resolution);
        markers.Style = RoiStyle.Markers;
        ROIConfigData marked = new SetExporter(resolution, markers)
            .BuildRoiConfig(lines, "harness markers", null);
        int markerPrims = 0, negative = 0;
        foreach (ROIDefinitionData roi in marked.ROIDefinitions)
        {
            markerPrims += roi.ROIPrimitives.Count;
            if (roi.LowerLimit >= 0.0) negative++;
        }
        Check("маркеры: области построены", marked.ROIDefinitions.Count > 0,
              marked.ROIDefinitions.Count + " шт.");
        Check("маркеры: границы −10 (зоны нет)", negative == 0, "с обычными границами: " + negative);
        Check("маркеры: примитивов НЕТ (пустышку не пишем)", markerPrims == 0,
              "примитивов: " + markerPrims);
    }

    // D9: набор из двух рядов распада ловится предупреждением. Якорный гейт
    // LibraryPeakFitter общий на весь набор, поэтому один найденный якорь включает и
    // вторую половину — на ториевом образце урановая даёт ложные отождествления.
    static void CheckMixedChains(NuclideCatalog catalog)
    {
        ResolutionModel resolution = new ResolutionModel(6.664);

        SourceSelection mixed = new SourceSelection();
        mixed.Add(catalog, "Th-232", AddMode.Chain);
        mixed.Add(catalog, "U-238", AddMode.Chain);
        Check("смешанные ряды дают предупреждение",
              HasMixedChains(catalog, mixed, resolution), null);

        SourceSelection single = new SourceSelection();
        single.Add(catalog, "Th-232", AddMode.Chain);
        Check("один ряд предупреждения не даёт",
              !HasMixedChains(catalog, single, resolution), null);

        // Набор независимых нуклидов законен: у них нет родителя в скобках, в ряд они не
        // входят, и запрещать «Cs-137 + Co-60» было бы ложной тревогой.
        SourceSelection separate = new SourceSelection();
        separate.Add(catalog, "Cs-137", AddMode.Single);
        separate.Add(catalog, "Co-60", AddMode.Single);
        Check("Cs-137 + Co-60 — не смешение рядов",
              !HasMixedChains(catalog, separate, resolution), null);
    }

    // D4: повторный прогон мастера не плодит записи библиотеки. Прежний AddRange добавлял
    // копию каждой линии на каждый прогон — четыре прогона давали 230 записей вместо 118 и
    // до четырёх копий «Tl-208 (Th-232) 2614.511». Это единственный дефект мастера,
    // оставлявший необратимый след в пользовательском файле.
    static void CheckLibraryMerge(NuclideCatalog catalog)
    {
        ResolutionModel resolution = new ResolutionModel(6.664);
        LineSetBuilder builder = new LineSetBuilder(catalog).Reset();
        SourceSelection selection = new SourceSelection();
        selection.Add(catalog, "Th-232", AddMode.Chain);
        List<SpectralLine> lines = builder.BuildRecommendedSet(selection, resolution, null);
        SetExporter exporter = new SetExporter(resolution);

        List<NuclideDefinition> library = new List<NuclideDefinition>();

        List<NuclideDefinition> first;
        NuclideSet setA = exporter.BuildNuclideSet(lines, "harness A", null, null,
                                                   AnchorPicker.DefaultCount, out first);
        SetExporter.MergeIntoLibrary(library, setA.Id, first);
        int afterFirst = library.Count;
        Check("первый прогон наполняет библиотеку", afterFirst > 0, afterFirst + " записей");

        // тот же состав вторым набором: линии те же, набор другой
        List<NuclideDefinition> second;
        NuclideSet setB = exporter.BuildNuclideSet(lines, "harness B", null, null,
                                                   AnchorPicker.DefaultCount, out second);
        SetExporter.MergeIntoLibrary(library, setB.Id, second);
        Check("второй прогон записей не добавил", library.Count == afterFirst,
              library.Count + " (было " + afterFirst + ")");

        int inBoth = 0;
        foreach (NuclideDefinition d in library)
            if (d.Sets.Contains(setA.Id) && d.Sets.Contains(setB.Id)) inBoth++;
        Check("записи принадлежат обоим наборам", inBoth == afterFirst,
              inBoth + " из " + afterFirst);

        // дублей по (имя, энергия) не появилось
        int duplicates = 0;
        for (int i = 0; i < library.Count; i++)
            for (int j = i + 1; j < library.Count; j++)
                if (library[i].Name == library[j].Name &&
                    Math.Abs(library[i].Energy - library[j].Energy) < SetExporter.SameEnergyKeV)
                    duplicates++;
        Check("дублей (имя, энергия) нет", duplicates == 0, "пар: " + duplicates);

        // якорь не теряется: во втором наборе он тот же, флаг должен остаться поднятым
        int anchors = 0;
        foreach (NuclideDefinition d in library) if (d.IsAnchor) anchors++;
        Check("якоря сохранены", anchors > 0, anchors + " шт.");
    }

    static bool HasMixedChains(NuclideCatalog catalog, SourceSelection selection,
                               ResolutionModel resolution)
    {
        List<SpectralLine> lines = new LineSetBuilder(catalog).Reset()
            .BuildRecommendedSet(selection, resolution, null);
        foreach (SetIssue issue in SetChecker.Check(lines, true, null, resolution))
            if (issue.Kind == IssueKind.MixedChains) return true;
        return false;
    }

    // Приёмочная проверка фильтра состава: воспроизводит ли BuildRecommendedSet сет из
    // tools/LibraryFitLab/data/recommended_sets.csv для ASN16 (k = 0.7, I_min = 1 %).
    //
    // Модель разрешения лаборатории FWHM = sqrt(2.940*E) кэВ; ResolutionModel задан как
    // R/100*sqrt(662*E), значит R = 100*sqrt(2.940/662) = 6.664 % (README даёт 6.7 % на 662).
    static void CompareWithReference(NuclideCatalog catalog)
    {
        double[] reference = {
            84.4, 99.5, 129.1, 209.3, 238.6, 270.3, 300.1, 338.3, 409.5, 463.0,
            510.8, 583.2, 674.8, 727.3, 795.0, 860.6, 911.2, 969.0, 1588.2, 2614.5 };

        SourceSelection selection = new SourceSelection();
        selection.Add(catalog, "Th-232", AddMode.Chain);

        LineSetBuilder builder = new LineSetBuilder(catalog).Reset();
        ResolutionModel resolution = new ResolutionModel(6.664);
        List<SpectralLine> lines = builder.BuildRecommendedSet(selection, resolution, null);

        // сравниваем по γ: эталон строился только по гаммам в диапазоне 10..3200 кэВ
        List<SpectralLine> got = new List<SpectralLine>();
        foreach (SpectralLine line in lines)
            if (line.Selected && line.Type == LineType.Gamma &&
                line.Energy >= 10.0 && line.Energy <= 3200.0) got.Add(line);

        List<double> missing = new List<double>();
        foreach (double e in reference)
        {
            bool found = false;
            foreach (SpectralLine line in got) if (Math.Abs(line.Energy - e) < 1.0) found = true;
            if (!found) missing.Add(e);
        }
        List<double> extra = new List<double>();
        foreach (SpectralLine line in got)
        {
            bool known = false;
            foreach (double e in reference) if (Math.Abs(line.Energy - e) < 1.0) known = true;
            if (!known) extra.Add(Math.Round(line.Energy, 1));
        }

        Console.WriteLine("эталон ASN16 Th-232: {0} линий; получено {1} γ",
                          reference.Length, got.Count);
        Check("совпало с эталоном", missing.Count == 0,
              missing.Count == 0 ? null : "нет: " + Join(missing));
        if (extra.Count > 0) Console.WriteLine("       сверх эталона: " + Join(extra));
        Check("объём сета в разумных пределах", got.Count >= 18 && got.Count <= 26,
              got.Count.ToString());

        // якорь обязан проходить мимо фильтров
        SourceSelection u238 = new SourceSelection();
        u238.Add(catalog, "U-238", AddMode.Chain);
        List<SpectralLine> all = builder.Build(u238, null);
        SpectralLine pa234m = null;
        foreach (SpectralLine line in all)
            if (line.Nuclide == "Pa-234m1" && Math.Abs(line.Energy - 1001.03) < 1.0) pa234m = line;
        Check("якорь U-238 1001.03 найден", pa234m != null,
              pa234m == null ? null : pa234m.Intensity.ToString("0.###", CultureInfo.InvariantCulture));
        if (pa234m != null)
        {
            Check("он слабее порога 1 %", pa234m.Intensity < 1.0, null);
            List<SpectralLine> forced = builder.BuildRecommendedSet(
                u238, resolution, new List<SpectralLine> { pa234m });
            bool kept = false;
            foreach (SpectralLine line in forced)
                if (line.Selected && Math.Abs(line.Energy - 1001.03) < 1.0) kept = true;
            Check("и всё равно попал в сет (mustKeep)", kept, null);
        }
    }

    static string Join(List<double> values)
    {
        string[] parts = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
            parts[i] = values[i].ToString("0.#", CultureInfo.InvariantCulture);
        return string.Join(", ", parts);
    }

    static void Check(string what, string actual, string expected)
    {
        Check(what, string.Equals(actual, expected, StringComparison.Ordinal),
              "«" + actual + "» (ждали «" + expected + "»)");
    }

    static int Count(IEnumerable<CatalogNuclide> items)
    {
        int n = 0;
        foreach (CatalogNuclide x in items) n++;
        return n;
    }

    static double FindLine(CatalogNuclide nuclide, double energy)
    {
        if (nuclide == null) return -1;
        foreach (CatalogGammaLine g in nuclide.Gamma)
            if (Math.Abs(g.Energy - energy) < 0.6) return g.Intensity;
        return -1;
    }
}
