// Плотность смеси из плотностей её частей (вопрос Amber 16.08.2026 про WT-20).
//
//     densityfrompartsprobe [<GeometryMaterials.xml>]
//
// Правило: объёмы складываются, 1/ρ = Σ w_i/ρ_i. Проба берёт библиотеку из
// файла ПОЛЬЗОВАТЕЛЯ (по умолчанию — вшитый засев) и считает четыре случая:
//
//   * «Электроды WT-20» КАК ЗАПИСАН в поставке — обязан быть ОТКАЗ: кислород
//     стоит газообразным, 0.24 % массы заняли бы 97 % объёма;
//   * тот же WT-20, записанный правильно (вольфрам + двуокись тория) — обязан
//     дать 18.94 против введённых Amber 18.92;
//   * смесь одних газов — считается, отказа быть НЕ должно (иначе правило
//     запрещало бы воздух);
//   * вещество формулой — отказ со словами «из формулы плотность не выводится».
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BecquerelMonitor.EfficiencyMaker;

static class DensityFromPartsProbe
{
    static int failures;
    static List<GeometryMaterialLibrary.Entry> library;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        library = new List<GeometryMaterialLibrary.Entry>(GeometryMaterialLibrary.Seed());
        if (args.Length > 0 && File.Exists(args[0]))
        {
            Console.WriteLine("библиотека: {0}", args[0]);
        }

        // Двуокись тория с 16.08.2026 в засеве (указание Amber). Проверяем это
        // же: если строка из засева пропадёт, «правильная запись» перестанет
        // считаться, и проба обязана это заметить, а не подставить своё.
        if (Lookup("Thorium dioxide") == null)
        {
            Console.WriteLine("РАСХОЖДЕНИЕ: в засеве нет «Thorium dioxide»");
            failures++;
        }

        Case("WT-20 как в поставке", Mix(
                 Part("Tungsten", 0.98), Part("Thorium", 0.01758), Part("Oxygen, gaseous", 0.00242)),
             false, 0.0);

        Case("WT-20 правильной записью", Mix(
                 Part("Tungsten", 0.98), Part("Thorium dioxide", 0.02)),
             true, 18.94);

        Case("смесь одних газов", Mix(
                 Part("Oxygen, gaseous", 0.23), Part("Nitrogen, gaseous", 0.77)),
             true, 0.0);

        Case("вещество формулой", new GeometryMaterialLibrary.Entry
        {
            Name = "проба", Formula = "Cs1 I1", Density = 4.51,
        }, false, 0.0);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + failures);
        return failures == 0 ? 0 : 1;
    }

    static GeometryMaterialComponent Part(string name, double weight)
    {
        return new GeometryMaterialComponent { Material = name, Weight = weight };
    }

    static GeometryMaterialLibrary.Entry Mix(params GeometryMaterialComponent[] parts)
    {
        GeometryMaterialLibrary.Entry entry = new GeometryMaterialLibrary.Entry { Name = "проба" };
        foreach (GeometryMaterialComponent part in parts)
        {
            entry.Components.Add(part);
        }

        return entry;
    }

    static void Case(string title, GeometryMaterialLibrary.Entry entry, bool expected, double want)
    {
        double density;
        string problem;
        bool ok = GeometryMaterialLibrary.TryDensityFromComponents(entry, Lookup, out density, out problem);
        Console.WriteLine();
        Console.WriteLine("== {0} ==", title);
        if (ok)
        {
            Console.WriteLine("   ρ = {0:0.####} г/см³", density);
            foreach (GeometryMaterialComponent part in entry.Components)
            {
                GeometryMaterialLibrary.Entry p = Lookup(part.Material);
                Console.WriteLine("     {0,-22} масса {1,8:0.#####}  ρ {2,8:0.####}  объём {3,7:0.0} %",
                                  part.Material, part.Weight, p == null ? 0.0 : p.Density,
                                  p == null || p.Density <= 0.0 ? 0.0
                                      : 100.0 * (part.Weight / p.Density) * density);
            }
        }
        else
        {
            Console.WriteLine("   ОТКАЗ: {0}", problem);
        }

        if (ok != expected)
        {
            Console.WriteLine("   РАСХОЖДЕНИЕ: ждали {0}", expected ? "счёт" : "отказ");
            failures++;
        }
        else if (ok && want > 0.0 && Math.Abs(density - want) > 0.05)
        {
            Console.WriteLine("   РАСХОЖДЕНИЕ: ждали {0:0.##}", want);
            failures++;
        }
    }

    static GeometryMaterialLibrary.Entry Lookup(string name)
    {
        foreach (GeometryMaterialLibrary.Entry entry in library)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }
}
