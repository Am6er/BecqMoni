using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Приёмка `S122` — отбора родителей для образов вылета SE/DE в библиотеке FSA.
///
/// ЧТО ПРОВЕРЯЕТСЯ. Прежнее правило брало «три самые интенсивные линии выше
/// 1022 кэВ с выходом не ниже 1 %». В нём две ошибки, и обе видны на одном
/// составе:
///
///   1. У порога образ появлялся при практически нулевом выходе: над 1022 кэВ
///      пары рождаться МОГУТ, но их доля в ослаблении — тысячные процента.
///   2. Порядок ставился по выходу линии, а вылет зависит ещё и от энергии:
///      1461 кэВ K-40 с выходом 10.7 % даёт вылета больше, чем 1173 кэВ Co-60
///      со 100 %.
///
/// Состав ниже собран так, чтобы обе ошибки были обязаны проявиться, и
/// проверяется он ДВАЖДЫ: с веществом кристалла (CsI) и без него. Без вещества
/// правило обязано остаться прежним — спектр без геометрии не должен менять
/// поведение.
///
///     escapeparentsprobe
///
/// Ожидание: «СОШЛОСЬ» — с CsI образов не получают 1050 и 1100 кэВ, а тройка
/// родителей идёт 2614, 1332, 1461; без CsI тройка прежняя — 1332, 1173, 2614.
/// </summary>
static class EscapeParentsProbe
{
    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        Dictionary<int, double> csi = CrystalFractions("Cesium iodide", 4.51);
        if (csi == null || csi.Count == 0)
        {
            Console.Error.WriteLine("иодида цезия нет в библиотеке веществ");
            return 2;
        }

        Console.WriteLine("доля рождения пар в полном ослаблении CsI:");
        foreach (double e in new[] { 1050.0, 1100.0, 1173.2, 1332.5, 1460.8, 2614.5 })
        {
            Console.WriteLine("  {0,8:F1} кэВ   {1,9:F4} %", e, 100.0 * PairShare(csi, e));
        }

        // Состав: две линии сразу над порогом (выход большой, вылета нет),
        // Co-60, K-40 и Tl-208.
        var composition = new List<FsaComponent>
        {
            Component("Near-threshold", 1050.0, 90.0, 1100.0, 80.0),
            Component("Co-60", 1173.2, 99.85, 1332.5, 99.98),
            Component("K-40", 1460.8, 10.66),
            Component("Tl-208", 2614.5, 99.75),
        };

        int bad = 0;
        Console.WriteLine();
        List<string> withCrystal = Parents(FsaLibrary.EscapeAndAnnihilation(composition, csi));
        Console.WriteLine("с веществом кристалла: {0}", string.Join(", ", withCrystal.ToArray()));
        bad += Same("с CsI", new[] { "2614", "1332", "1461" }, withCrystal);

        List<string> without = Parents(FsaLibrary.EscapeAndAnnihilation(composition));
        Console.WriteLine("без вещества:          {0}", string.Join(", ", without.ToArray()));
        bad += Same("без вещества", new[] { "1332", "1173", "2614" }, without);

        // Аннигиляционный образ живёт по своему правилу и физическим отсевом
        // выше не убирается: квант 511 родится в защите, а не в кристалле.
        bad += Has("Ann-511 с CsI", FsaLibrary.EscapeAndAnnihilation(composition, csi));
        bad += Has("Ann-511 без вещества", FsaLibrary.EscapeAndAnnihilation(composition));

        // Состав из одних только «у порога»: образов вылета быть не должно
        // вовсе, а аннигиляционный — остаться.
        var onlyNear = new List<FsaComponent> { Component("Near-threshold", 1050.0, 90.0, 1100.0, 80.0) };
        List<string> nearParents = Parents(FsaLibrary.EscapeAndAnnihilation(onlyNear, csi));
        Console.WriteLine("только линии у порога: {0}",
                          nearParents.Count == 0 ? "(пусто)" : string.Join(", ", nearParents.ToArray()));
        bad += Same("только у порога", new string[0], nearParents);
        bad += Has("Ann-511 у порога", FsaLibrary.EscapeAndAnnihilation(onlyNear, csi));

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "СОШЛОСЬ: отбор родителей идёт по ожидаемой площади вылета"
                                   : "НЕ СОШЛОСЬ: расхождений " + bad);
        return bad == 0 ? 0 : 1;
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

    /// <summary>Метки родителей в том порядке, в каком образы построены.</summary>
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

        Console.WriteLine("  {0,-16} ожидалось [{1}] — {2}", what,
                          string.Join(" ", new List<string>(expected).ToArray()),
                          ok ? "сошлось" : "РАСХОЖДЕНИЕ");
        return ok ? 0 : 1;
    }

    static int Has(string what, List<FsaComponent> extra)
    {
        bool found = false;
        foreach (FsaComponent component in extra)
        {
            found |= string.Equals(component.Name, "Ann-511", StringComparison.OrdinalIgnoreCase);
        }

        Console.WriteLine("  {0,-22} {1}", what, found ? "есть" : "НЕТ — расхождение");
        return found ? 0 : 1;
    }

    static Dictionary<int, double> CrystalFractions(string name, double density)
    {
        GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
        if (entry == null)
        {
            return null;
        }

        GeometryMaterial material = GeometryMaterialLibrary.Make(entry, density);
        return material == null ? null : new Dictionary<int, double>(material.Fractions);
    }

    /// <summary>Та же величина, что считает отбор; печатается для наглядности.</summary>
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

        return total > 0.0 ? pair / total : double.NaN;
    }
}
