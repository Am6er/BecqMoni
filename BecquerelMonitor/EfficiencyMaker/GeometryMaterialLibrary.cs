using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Вещества для конструктора геометрий.
    ///
    /// Состав задаётся ФОРМУЛОЙ, а массовые доли считаются из атомных масс
    /// (<see cref="AttenuationData.AtomicMass"/>) при обращении. Вписывать доли
    /// руками нельзя нарочно: в файлах LSRM они записаны шестью знаками
    /// (0.488451 / 0.511549 у иодида цезия), и опечатка в такой строке не
    /// бросается в глаза, а кривую портит молча.
    ///
    /// Доли получаются МАССОВЫЕ — это то, чего ждёт и формат (FractionType =
    /// MASS), и наш расчёт ослабления по правилу Брэгга.
    /// </summary>
    public static class GeometryMaterialLibrary
    {
        /// <summary>Вещество библиотеки: имя как в файлах LSRM, формула, плотность.</summary>
        public sealed class Entry
        {
            /// <summary>
            /// Имя ровно как в файлах LSRM. В список оно и пишется — иначе их
            /// программа не узнает вещество.
            /// </summary>
            public string Name;

            /// <summary>
            /// Привычное сокращение: CsI, NaI, HPGe. В файл не попадает, но в
            /// списке стоит первым — по «Cesium iodide» кристалл ищут глазами
            /// дольше, чем по «CsI».
            /// </summary>
            public string Abbr;

            public string Formula;
            public double Density;
            public MaterialKind Kind;

            public override string ToString()
            {
                return string.IsNullOrEmpty(this.Abbr) ? this.Name : this.Abbr + " — " + this.Name;
            }
        }

        /// <summary>Куда вещество годится — чтобы список в UI не был на сорок строк.</summary>
        public enum MaterialKind
        {
            Crystal,
            Reflector,
            Cladding,
            BeakerWall,
            Source
        }

        static readonly string[] Symbols =
        {
            "n",
            "H",  "He", "Li", "Be", "B",  "C",  "N",  "O",  "F",  "Ne",
            "Na", "Mg", "Al", "Si", "P",  "S",  "Cl", "Ar", "K",  "Ca",
            "Sc", "Ti", "V",  "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
            "Ga", "Ge", "As", "Se", "Br", "Kr", "Rb", "Sr", "Y",  "Zr",
            "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn",
            "Sb", "Te", "I",  "Xe", "Cs", "Ba", "La", "Ce", "Pr", "Nd",
            "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb",
            "Lu", "Hf", "Ta", "W",  "Re", "Os", "Ir", "Pt", "Au", "Hg",
            "Tl", "Pb", "Bi", "Po", "At", "Rn", "Fr", "Ra", "Ac", "Th",
            "Pa", "U"
        };

        static readonly List<Entry> All = Build();

        static List<Entry> Build()
        {
            List<Entry> list = new List<Entry>();
            Action<string, string, string, double, MaterialKind> add =
                (abbr, name, formula, density, kind) => list.Add(new Entry
                {
                    Abbr = abbr, Name = name, Formula = formula, Density = density, Kind = kind
                });

            // Кристаллы. Имена — как в файлах LSRM, чтобы сохранённый файл
            // читался их же программой без переименований; сокращение только для
            // списка. HPGe — не отдельное вещество, а тот же германий: приставка
            // говорит о чистоте, а не о составе.
            add("CsI", "Cesium iodide", "Cs1 I1", 4.51, MaterialKind.Crystal);
            add("NaI", "Sodium iodide", "Na1 I1", 3.667, MaterialKind.Crystal);
            add("BGO", "Bismuth germanate", "Bi4 Ge3 O12", 7.13, MaterialKind.Crystal);
            add("LaBr3", "Lanthanum bromide", "La1 Br3", 5.08, MaterialKind.Crystal);
            add("CeBr3", "Cerium bromide", "Ce1 Br3", 5.1, MaterialKind.Crystal);
            add("SrI2", "Strontium iodide", "Sr1 I2", 4.55, MaterialKind.Crystal);
            add("CdTe", "Cadmium telluride", "Cd1 Te1", 5.85, MaterialKind.Crystal);
            add("CZT", "Cadmium zinc telluride", "Cd9 Zn1 Te10", 5.78, MaterialKind.Crystal);
            add("GSO", "Gadolinium oxyorthosilicate", "Gd2 Si1 O5", 6.71, MaterialKind.Crystal);
            add("HPGe", "Germanium", "Ge1", 5.323, MaterialKind.Crystal);

            // Отражатели
            add("PTFE", "Polytetrafluoroethylene", "C2 F4", 2.25, MaterialKind.Reflector);
            add("MgO", "Magnesium oxide", "Mg1 O1", 3.58, MaterialKind.Reflector);
            add("TiO2", "Titanium dioxide", "Ti1 O2", 4.23, MaterialKind.Reflector);
            add("Al2O3", "Aluminum oxide", "Al2 O3", 3.97, MaterialKind.Reflector);

            // Корпус и оправа
            add("Al", "Aluminum", "Al1", 2.7, MaterialKind.Cladding);
            add("Ti", "Titanium", "Ti1", 4.51, MaterialKind.Cladding);
            add("Fe", "Iron", "Fe1", 7.874, MaterialKind.Cladding);
            add("C", "Carbon", "C1", 1.7, MaterialKind.Cladding);

            // Стенки сосудов
            add("PET", "Polyethylene terephthalate", "C10 H8 O4", 1.38, MaterialKind.BeakerWall);
            add("PE", "Polyethylene", "C2 H4", 0.94, MaterialKind.BeakerWall);
            add("PP", "Polypropylene", "C3 H6", 0.905, MaterialKind.BeakerWall);
            add("PS", "Polystyrene", "C8 H8", 1.06, MaterialKind.BeakerWall);
            add("SiO2", "Glass", "Si1 O2", 2.32, MaterialKind.BeakerWall);

            // Пробы
            add("H2O", "Water, liquid", "H2 O1", 1.0, MaterialKind.Source);
            add("Air", "Air, dry", "N2 O1", 0.001205, MaterialKind.Source);
            add("SiO2", "Silicon dioxide", "Si1 O2", 1.6, MaterialKind.Source);
            add("CaCO3", "Calcium carbonate", "Ca1 C1 O3", 1.5, MaterialKind.Source);
            add("KCl", "Potassium chloride", "K1 Cl1", 1.0, MaterialKind.Source);
            return list;
        }

        /// <summary>Вещества, годные для этого места сцены.</summary>
        public static List<Entry> Of(MaterialKind kind)
        {
            List<Entry> list = new List<Entry>();
            foreach (Entry entry in All)
            {
                if (entry.Kind == kind)
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        public static Entry ByName(string name)
        {
            foreach (Entry entry in All)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

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

        public static string SymbolOf(int z)
        {
            return z > 0 && z < Symbols.Length ? Symbols[z] : z.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Готовое вещество: массовые доли посчитаны из формулы, плотность взята
        /// заданная (её пользователь правит руками — у одного и того же вещества
        /// она зависит от набивки).
        /// </summary>
        public static GeometryMaterial Make(Entry entry, double density)
        {
            GeometryMaterial material = new GeometryMaterial
            {
                Name = entry.Name,
                Density = density > 0.0 ? density : entry.Density,
            };

            Dictionary<int, double> atoms = ParseFormula(entry.Formula);
            double total = 0.0;
            foreach (KeyValuePair<int, double> pair in atoms)
            {
                double mass;
                if (AttenuationData.AtomicMass.TryGetValue(pair.Key, out mass) && mass > 0.0)
                {
                    total += pair.Value * mass;
                }
            }

            if (!(total > 0.0))
            {
                return material;
            }

            foreach (KeyValuePair<int, double> pair in atoms)
            {
                double mass;
                if (AttenuationData.AtomicMass.TryGetValue(pair.Key, out mass) && mass > 0.0)
                {
                    material.Fractions[pair.Key] = pair.Value * mass / total;
                }
            }

            return material;
        }

        /// <summary>«Bi4 Ge3 O12» -> {83:4, 32:3, 8:12}.</summary>
        public static Dictionary<int, double> ParseFormula(string formula)
        {
            Dictionary<int, double> atoms = new Dictionary<int, double>();
            if (string.IsNullOrEmpty(formula))
            {
                return atoms;
            }

            foreach (string part in formula.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int split = 0;
                while (split < part.Length && !char.IsDigit(part[split]))
                {
                    split++;
                }

                string symbol = part.Substring(0, split);
                double count;
                if (split >= part.Length
                    || !double.TryParse(part.Substring(split), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out count))
                {
                    count = 1.0;
                }

                int z = ZOf(symbol);
                if (z > 0 && count > 0.0)
                {
                    double have;
                    atoms.TryGetValue(z, out have);
                    atoms[z] = have + count;
                }
            }

            return atoms;
        }

        /// <summary>Состав одной строкой для показа рядом с выбором вещества.</summary>
        public static string Describe(GeometryMaterial material)
        {
            if (material == null || material.Fractions.Count == 0)
            {
                return "";
            }

            List<int> order = new List<int>(material.Fractions.Keys);
            order.Sort();
            StringBuilder text = new StringBuilder();
            foreach (int z in order)
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:F4}",
                                  SymbolOf(z), material.Fractions[z]);
            }

            return text.ToString();
        }
    }
}
