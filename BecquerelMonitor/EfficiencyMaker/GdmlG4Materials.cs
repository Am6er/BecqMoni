using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Составы веществ, которые GDML-файлы Geant4 называют, но НЕ описывают:
    /// имена <c>G4_*</c> — это ссылки на таблицу NIST Geant4
    /// (<c>G4NistMaterialBuilder</c>), и в файле у них нет ни плотности, ни долей.
    /// Без этой таблицы сводка импорта не может сказать «химсостав», а
    /// подставлять первое похожее вещество из библиотеки нельзя — это была бы
    /// молчаливая подмена.
    ///
    /// Числа — массовые доли NIST (Geant4 NIST material builder); простые
    /// элементы взяты из <c>matdb.star_materials</c>, соединения — из той же
    /// таблицы NIST, откуда их берёт Geant4. Плотность элемента, который
    /// встречается только внутри соединения, на массу тома не влияет — она
    /// нужна лишь тогда, когда элемент стоит веществом тома напрямую.
    /// </summary>
    public static class GdmlG4Materials
    {
        public static readonly Dictionary<string, GdmlMaterial> Table =
            new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);

        static GdmlG4Materials()
        {
            // Простые элементы.
            Add("G4_H", 8.3748e-05, "H", 1.0);
            Add("G4_Li", 0.534, "Li", 1.0);
            Add("G4_C", 2.0, "C", 1.0);
            Add("G4_N", 0.00116528, "N", 1.0);
            Add("G4_O", 0.00133151, "O", 1.0);
            Add("G4_F", 0.00158029, "F", 1.0);
            Add("G4_Na", 0.971, "Na", 1.0);
            Add("G4_Mg", 1.74, "Mg", 1.0);
            Add("G4_Al", 2.6989, "Al", 1.0);
            Add("G4_Si", 2.33, "Si", 1.0);
            Add("G4_S", 2.07, "S", 1.0);
            Add("G4_Cl", 0.00299473, "Cl", 1.0);
            Add("G4_Ar", 0.00166201, "Ar", 1.0);
            Add("G4_K", 0.862, "K", 1.0);
            Add("G4_Ca", 1.55, "Ca", 1.0);
            Add("G4_Cr", 7.18, "Cr", 1.0);
            Add("G4_Mn", 7.44, "Mn", 1.0);
            Add("G4_Fe", 7.874, "Fe", 1.0);
            Add("G4_Ni", 8.902, "Ni", 1.0);
            Add("G4_Cu", 8.96, "Cu", 1.0);
            Add("G4_Zn", 7.133, "Zn", 1.0);
            Add("G4_Sn", 7.31, "Sn", 1.0);
            Add("G4_I", 4.93, "I", 1.0);
            Add("G4_Cs", 1.873, "Cs", 1.0);
            Add("G4_W", 19.3, "W", 1.0);
            Add("G4_Pb", 11.35, "Pb", 1.0);

            // Соединения и смеси, которые встречаются в наборе VibeEngineering.
            Add("G4_AIR", 0.00120479, "N", 0.755268, "O", 0.231781, "Ar", 0.012827);
            Add("G4_WATER", 1.0, "H", 0.111894, "O", 0.888106);
            Add("G4_SODIUM_IODIDE", 3.667, "Na", 0.153373, "I", 0.846627);
            Add("G4_CESIUM_IODIDE", 4.51, "Cs", 0.511549, "I", 0.488451);
            Add("G4_POLYETHYLENE", 0.94, "H", 0.143711, "C", 0.856289);
            Add("G4_POLYPROPYLENE", 0.9, "H", 0.143711, "C", 0.856289);
            Add("G4_SILICON_DIOXIDE", 2.32, "O", 0.532565, "Si", 0.467435);
            Add("G4_STAINLESS-STEEL", 8.02,
                "Fe", 0.746, "Cr", 0.17, "Ni", 0.12, "Mn", 0.01, "Si", 0.01);
        }

        static void Add(string name, double density, params object[] elements)
        {
            GdmlMaterial material = new GdmlMaterial
            {
                Name = name,
                Density = density,
                HasDensity = true,
                Origin = "G4",
            };

            for (int i = 0; i + 1 < elements.Length; i += 2)
            {
                string symbol = (string)elements[i];
                double fraction = Convert.ToDouble(elements[i + 1]);
                int z = ElementZ(symbol);
                if (z > 0)
                {
                    material.Fractions[z] = fraction;
                }
            }

            Table[name] = material;
        }

        static readonly string[] Symbols =
        {
            "",
            "H", "He", "Li", "Be", "B", "C", "N", "O", "F", "Ne",
            "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar", "K", "Ca",
            "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
            "Ga", "Ge", "As", "Se", "Br", "Kr", "Rb", "Sr", "Y", "Zr",
            "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn",
            "Sb", "Te", "I", "Xe", "Cs", "Ba", "La", "Ce", "Pr", "Nd",
            "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb",
            "Lu", "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg",
            "Tl", "Pb", "Bi", "Po", "At", "Rn", "Fr", "Ra", "Ac", "Th",
            "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm",
            "Md", "No", "Lr", "Rf", "Db", "Sg", "Bh", "Hs", "Mt", "Ds",
            "Rg", "Cn", "Nh", "Fl", "Mc", "Lv", "Ts", "Og",
        };

        /// <summary>Символ элемента по Z; неизвестный Z печатается числом.</summary>
        public static string SymbolOf(int z)
        {
            return z > 0 && z < Symbols.Length
                ? Symbols[z]
                : z.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static int ElementZ(string symbol)
        {
            for (int z = 1; z < Symbols.Length; z++)
            {
                if (string.Equals(Symbols[z], symbol, StringComparison.Ordinal))
                {
                    return z;
                }
            }

            return 0;
        }
    }
}
