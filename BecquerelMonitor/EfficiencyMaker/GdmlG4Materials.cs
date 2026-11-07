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
    /// Плотности и весовые доли — из <c>G4NistMaterialBuilder.cc</c> Geant4, сверены
    /// построчно (`AMBER303`, проба `GdmlReaderProbeA303 --g4=`). Соединения, заданные в
    /// Geant4 числом атомов (вода, NaI), записаны массовыми долями. Плотность элемента,
    /// который встречается только внутри соединения, на массу тома не влияет — она
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
            Add("G4_N", 0.0011652, "N", 1.0);
            Add("G4_O", 0.00133151, "O", 1.0);
            Add("G4_F", 0.00158029, "F", 1.0);
            Add("G4_Na", 0.971, "Na", 1.0);
            Add("G4_Mg", 1.74, "Mg", 1.0);
            Add("G4_Al", 2.699, "Al", 1.0);
            Add("G4_Si", 2.33, "Si", 1.0);
            Add("G4_S", 2.0, "S", 1.0);
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
            // (`AMBER301`, fixGdml2) Пустота Geant4 — мир большинства примеров: `AddMaterial("G4_Galactic",
            // universe_mean_density, 1, 21.8, 1, kStateGas)`, universe_mean_density = 1e-25 г/см³ (CLHEP
            // `PhysicalConstants.h:128`), Z = 1. Без неё банка из пустоты вокруг кристалла была «составом без плотности», и
            // одна мерка звала её корпусом, а другая — оболочкой (критик `critGdml2`, вход `f3_vacuum_container`).
            Add("G4_Galactic", 1e-25, "H", 1.0);
            Add("G4_AIR", 0.00120479, "C", 0.000124, "N", 0.755267, "O", 0.231781, "Ar", 0.012827);
            Add("G4_WATER", 1.0, "H", 0.111894, "O", 0.888106);
            Add("G4_SODIUM_IODIDE", 3.667, "Na", 0.153373, "I", 0.846627);
            Add("G4_CESIUM_IODIDE", 4.51, "Cs", 0.511549, "I", 0.488451);
            Add("G4_POLYETHYLENE", 0.94, "H", 0.143711, "C", 0.856289);
            Add("G4_POLYPROPYLENE", 0.9, "H", 0.143711, "C", 0.856289);
            Add("G4_SILICON_DIOXIDE", 2.32, "O", 0.532565, "Si", 0.467435);

            // (`AMBER317`) Отражатели, плёнки, стёкла и кристаллы, типовые для детекторов. Плотности и составы — из
            // G4NistMaterialBuilder.cc Geant4 11.4.2; доли по числу атомов пересчитаны стандартными атомными массами,
            // сверка — `GdmlReaderProbeA303 --g4=`.
            Add("G4_TEFLON", 2.2, "C", 0.240178, "F", 0.759822);
            Add("G4_ALUMINUM_OXIDE", 3.97, "Al", 0.529251, "O", 0.470749);
            Add("G4_TITANIUM_DIOXIDE", 4.26, "Ti", 0.599343, "O", 0.400657);
            Add("G4_BARIUM_SULFATE", 4.5, "Ba", 0.588402, "S", 0.137388, "O", 0.274209);
            Add("G4_MAGNESIUM_OXIDE", 3.58, "Mg", 0.603036, "O", 0.396964);
            Add("G4_KAPTON", 1.42, "C", 0.691127, "H", 0.026363, "N", 0.073271, "O", 0.209238);
            Add("G4_MYLAR", 1.4, "C", 0.625010, "H", 0.041961, "O", 0.333029);
            Add("G4_POLYSTYRENE", 1.06, "C", 0.922577, "H", 0.077423);
            Add("G4_BGO", 7.13, "Bi", 0.670973, "Ge", 0.174919, "O", 0.154108);
            Add("G4_LITHIUM_IODIDE", 3.494, "Li", 0.051858, "I", 0.948142);
            Add("G4_GLASS_PLATE", 2.4, "O", 0.4598, "Na", 0.096441, "Si", 0.336553, "Ca", 0.107205);
            Add("G4_Pyrex_Glass", 2.23, "B", 0.040064, "O", 0.539562, "Na", 0.028191, "Al", 0.011644, "Si", 0.37722, "K", 0.003321);
            Add("G4_RUBBER_NATURAL", 0.92, "H", 0.118371, "C", 0.881629);
            Add("G4_Be", 1.848, "Be", 1.0);
            Add("G4_Ti", 4.54, "Ti", 1.0);
            Add("G4_Ge", 5.323, "Ge", 1.0);
            Add("G4_Cd", 8.65, "Cd", 1.0);
            Add("G4_STAINLESS-STEEL", 8.00, "Fe", 0.7462, "Cr", 0.1690, "Ni", 0.0848);
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
                int z = ZOf(symbol);
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

        /// <summary>Z по символу элемента; 0 — такого символа нет.</summary>
        public static int ZOf(string symbol)
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
