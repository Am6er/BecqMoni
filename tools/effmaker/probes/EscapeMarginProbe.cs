using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Приёмка `A271` — отбор родителей образов вылета SE/DE НЕ ВЫКЛЮЧАЕТСЯ на
/// спектре без геометрии.
///
/// ЧТО ЭТО ЗА БЕДА. `FsaLibrary.EscapeImages` отсеивала родителей по доле
/// рождения пар в веществе кристалла, а при неизвестном веществе доля равна
/// `NaN` — и условие `!double.IsNaN(share) &amp;&amp; !(share >= порог)`
/// отключало отсев ЦЕЛИКОМ. Родителем становилась любая линия выше 1022 кэВ по
/// сырому выходу: наверх выходила `Bi-214` 1120 (98 кэВ над порогом пар), её
/// образ `SE-1120` садился на 609.0 — то есть ПОВЕРХ линии `Bi-214` 609.3 — и
/// забирал до 76 % отсчётов спектра (`AS80_Charoite`).
///
/// ЧТО ПРОВЕРЯЕТСЯ ЗДЕСЬ, тремя движениями:
///
///   1. **Замер порога.** Печатается доля рождения пар в полном ослаблении для
///      КАЖДОГО кристалла библиотеки и энергия, на которой она пересекает
///      <see cref="FsaLibrary.EscapeMinPairShare"/>. Наименьшая из пересечений
///      и есть то место, ниже которого родителя отвергает ВСЯКОЕ известное
///      вещество, — оттуда взят <see cref="FsaLibrary.EscapeParentMarginKev"/>.
///      Число печатается, а не сверяется с круглым: если вещества в библиотеке
///      поменяются, замер это покажет.
///   2. **Запас работает без вещества.** Состав с линиями у порога (1050, 1100,
///      1120 кэВ) и одной законной (2614.5) без вещества кристалла обязан дать
///      родителя ровно одного — 2614.
///   3. **Вещество восстанавливается по элементам.** У спектра без геометрии
///      массовых долей нет, но элементы кристалла названы («Cs;I» в
///      `materials.csv`, кристалл прибора в приложении). Состав элементов
///      однозначно называет вещество библиотеки, и
///      <see cref="FsaSampleLibrary.CrystalFractionsOf"/> обязана его найти;
///      отбор с восстановленными долями обязан совпасть с отбором по долям,
///      взятым прямо из вещества.
///
///     escapemarginprobe
///
/// Ожидание: «СОШЛОСЬ».
/// </summary>
static class EscapeMarginProbe
{
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        int bad = 0;
        bad += Crossings();
        bad += MarginWithoutMatter();
        bad += ResolveByElements();

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: отсев родителей вылета работает и без вещества кристалла"
            : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. Замер: где доля рождения пар пересекает порог у каждого кристалла
    // ------------------------------------------------------------------

    static int Crossings()
    {
        Console.WriteLine("=== 1. доля рождения пар в полном ослаблении, кристаллы библиотеки ===");
        Console.WriteLine("порог доли {0} ; порог пар {1:F0} кэВ ; запас {2:F0} кэВ (то есть от {3:F0} кэВ)",
                          FsaLibrary.EscapeMinPairShare.ToString("G", CultureInfo.InvariantCulture),
                          FsaLibrary.PairThresholdKev,
                          FsaLibrary.EscapeParentMarginKev,
                          FsaLibrary.PairThresholdKev + FsaLibrary.EscapeParentMarginKev);
        Console.WriteLine();
        Console.WriteLine("{0,-8} {1,-26} {2,10} {3,10} {4,10} {5,12}",
                          "вещество", "имя", "1100 кэВ", "1173 кэВ", "2614 кэВ", "пересечение");

        List<GeometryMaterialLibrary.Entry> crystals =
            GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Crystal);
        if (crystals.Count == 0)
        {
            Console.WriteLine("⛔ в библиотеке нет ни одного кристалла");
            return 1;
        }

        double lowest = double.MaxValue;
        string lowestName = "";
        foreach (GeometryMaterialLibrary.Entry entry in crystals)
        {
            GeometryMaterial material = GeometryMaterialLibrary.Make(entry, entry.Density);
            var fractions = new Dictionary<int, double>(material.Fractions);
            double cross = Crossing(fractions);
            Console.WriteLine("{0,-8} {1,-26} {2,9:F4}% {3,9:F4}% {4,9:F3}% {5,9:F1} кэВ",
                              entry.Abbr, entry.Name,
                              100.0 * Share(fractions, 1100.0),
                              100.0 * Share(fractions, 1173.2),
                              100.0 * Share(fractions, 2614.5),
                              cross);
            if (cross > 0.0 && cross < lowest)
            {
                lowest = cross;
                lowestName = string.IsNullOrEmpty(entry.Abbr) ? entry.Name : entry.Abbr;
            }
        }

        Console.WriteLine();
        Console.WriteLine("НАИМЕНЬШЕЕ пересечение: {0:F1} кэВ ({1}), то есть запас {2:F1} кэВ над порогом пар",
                          lowest, lowestName, lowest - FsaLibrary.PairThresholdKev);

        // Запас обязан быть НЕ ВЫШЕ наименьшего пересечения: иначе он отверг бы
        // родителя, которого известное вещество принимает, и правило по энергии
        // стало бы спорить с правилом по веществу.
        double margin = FsaLibrary.EscapeParentMarginKev;
        bool ok = margin > 0.0 && margin <= lowest - FsaLibrary.PairThresholdKev + 1e-9;
        Console.WriteLine("  запас {0:F1} кэВ {1} наименьшего пересечения — {2}",
                          margin,
                          margin <= lowest - FsaLibrary.PairThresholdKev + 1e-9 ? "не выше" : "ВЫШЕ",
                          ok ? "сошлось" : "РАСХОЖДЕНИЕ");
        return ok ? 0 : 1;
    }

    /// <summary>Энергия, на которой доля пар пересекает порог; половинным делением.</summary>
    static double Crossing(Dictionary<int, double> fractions)
    {
        double lo = FsaLibrary.PairThresholdKev, hi = 3000.0;
        if (Share(fractions, hi) < FsaLibrary.EscapeMinPairShare)
        {
            return -1.0;
        }

        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Share(fractions, mid) >= FsaLibrary.EscapeMinPairShare)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>Та же величина, что считает отбор; своим счётом, для наглядности.</summary>
    static double Share(Dictionary<int, double> fractions, double energyKev)
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

        return total > 0.0 ? pair / total : double.NaN;
    }

    // ------------------------------------------------------------------
    // 2. Без вещества запас всё равно отсекает линии у порога
    // ------------------------------------------------------------------

    static int MarginWithoutMatter()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. без вещества кристалла ===");

        // Ровно та беда, что измерена на корпусе: сильная линия в 98 кэВ над
        // порогом пар против слабой, но законной 2614.5.
        var composition = new List<FsaComponent>
        {
            Component("Near-threshold", 1050.0, 90.0, 1100.0, 80.0, 1120.3, 14.9),
            Component("Tl-208", 2614.5, 35.6),
        };

        List<string> parents = Parents(FsaLibrary.EscapeImages(composition, null));
        Console.WriteLine("  родители без вещества: {0}",
                          parents.Count == 0 ? "(пусто)" : string.Join(", ", parents.ToArray()));
        return Same("без вещества", new[] { "2614" }, parents);
    }

    // ------------------------------------------------------------------
    // 3. Вещество восстанавливается по названным элементам
    // ------------------------------------------------------------------

    static int ResolveByElements()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. вещество по элементам кристалла ===");

        int bad = 0;
        // Кристаллы непонятной части корпуса: `materials.csv` называет их
        // элементами, а не долями.
        bad += Resolved("Cs;I", new[] { 55, 53 }, "Cesium iodide");
        bad += Resolved("Na;I", new[] { 11, 53 }, "Sodium iodide");
        bad += Resolved("La;Br", new[] { 57, 35 }, "Lanthanum bromide");
        bad += Resolved("Ge", new[] { 32 }, "Germanium");

        // Отрицательный контроль: набор, которого нет ни у одного кристалла,
        // вещества не получает — и подмены догадкой не происходит.
        var spec = new FsaSampleSpec();
        spec.CrystalElements.Add(26);
        spec.CrystalElements.Add(82);
        Dictionary<int, double> none = FsaSampleLibrary.CrystalFractionsOf(spec);
        Console.WriteLine("  {0,-8} {1}", "Fe;Pb",
                          none.Count == 0 ? "вещества нет — сошлось" : "НАШЛОСЬ ВЕЩЕСТВО — РАСХОЖДЕНИЕ");
        bad += none.Count == 0 ? 0 : 1;

        // И главное: отбор с восстановленными долями совпадает с отбором по
        // долям самого вещества. Иначе восстановление меняло бы физику.
        var composition = new List<FsaComponent>
        {
            Component("Near-threshold", 1050.0, 90.0, 1100.0, 80.0, 1120.3, 14.9),
            Component("K-40", 1460.8, 10.66),
            Component("Tl-208", 2614.5, 35.6),
        };

        var csiSpec = new FsaSampleSpec();
        csiSpec.CrystalElements.Add(55);
        csiSpec.CrystalElements.Add(53);
        List<string> byElements = Parents(
            FsaLibrary.EscapeImages(composition, FsaSampleLibrary.CrystalFractionsOf(csiSpec)));

        GeometryMaterialLibrary.Entry csi = GeometryMaterialLibrary.ByName("Cesium iodide");
        var direct = new Dictionary<int, double>(
            GeometryMaterialLibrary.Make(csi, csi.Density).Fractions);
        List<string> byMatter = Parents(FsaLibrary.EscapeImages(composition, direct));

        Console.WriteLine("  по элементам: {0}", string.Join(", ", byElements.ToArray()));
        Console.WriteLine("  по веществу : {0}", string.Join(", ", byMatter.ToArray()));
        bad += Same("восстановление = вещество", byMatter, byElements);
        return bad;
    }

    static int Resolved(string label, int[] elements, string expected)
    {
        var spec = new FsaSampleSpec();
        foreach (int z in elements)
        {
            spec.CrystalElements.Add(z);
        }

        Dictionary<int, double> fractions = FsaSampleLibrary.CrystalFractionsOf(spec);
        GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(expected);
        var want = entry == null
            ? new Dictionary<int, double>()
            : new Dictionary<int, double>(GeometryMaterialLibrary.Make(entry, entry.Density).Fractions);

        bool ok = fractions.Count == want.Count && fractions.Count > 0;
        foreach (KeyValuePair<int, double> pair in want)
        {
            double have;
            ok &= fractions.TryGetValue(pair.Key, out have) && Math.Abs(have - pair.Value) < 1e-9;
        }

        Console.WriteLine("  {0,-8} -> {1,-26} {2}", label, expected, ok ? "сошлось" : "РАСХОЖДЕНИЕ");
        return ok ? 0 : 1;
    }

    // ------------------------------------------------------------------

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

    static int Same(string what, IList<string> expected, IList<string> actual)
    {
        bool ok = expected.Count == actual.Count;
        for (int i = 0; ok && i < expected.Count; i++)
        {
            ok = expected[i] == actual[i];
        }

        Console.WriteLine("  {0,-26} ожидалось [{1}] — {2}", what,
                          string.Join(" ", new List<string>(expected).ToArray()),
                          ok ? "сошлось" : "РАСХОЖДЕНИЕ");
        return ok ? 0 : 1;
    }
}
