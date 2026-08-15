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
    ///
    /// Смесь задаётся иначе — списком ВЕЩЕСТВ с массовыми весами
    /// (<see cref="Entry.Components"/>): так устроен формат `.in` и так
    /// называют пробу («песок с водой пополам»), а сводить смесь к одной
    /// формуле пришлось бы руками — то есть ровно тем счётом, ради ухода от
    /// которого формулы здесь и появились.
    ///
    /// Сам список с 15.08.2026 живёт в конфигурации пользователя
    /// (<see cref="GeometryMaterialStore"/>), а вшитый — только засев для того,
    /// у кого файла ещё нет (`E20`).
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

            /// <summary>
            /// Смесь: имена веществ этой же библиотеки и их массовые веса. Пуст
            /// у вещества, заданного формулой; непуст — формула не смотрится
            /// вовсе.
            /// </summary>
            public readonly List<GeometryMaterialComponent> Components =
                new List<GeometryMaterialComponent>();

            public bool IsMixture
            {
                get { return this.Components.Count > 0; }
            }

            public Entry Clone()
            {
                Entry copy = new Entry
                {
                    Name = this.Name,
                    Abbr = this.Abbr,
                    Formula = this.Formula,
                    Density = this.Density,
                    Kind = this.Kind,
                };

                foreach (GeometryMaterialComponent component in this.Components)
                {
                    copy.Components.Add(new GeometryMaterialComponent
                    {
                        Material = component.Material,
                        Weight = component.Weight,
                    });
                }

                return copy;
            }

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

        /// <summary>
        /// Действующая библиотека — та, что в конфигурации пользователя. Вшитый
        /// список отдаётся, только пока файла нет.
        /// </summary>
        static List<Entry> All
        {
            get { return GeometryMaterialStore.Entries; }
        }

        /// <summary>
        /// Вшитый список — ЗАСЕВ библиотеки. Правкой этого метода вещество
        /// пользователю больше не заводится (для этого есть редактор): сюда оно
        /// попадает только затем, чтобы стоять в списке у того, кто программу
        /// поставил впервые. Добавили строку — поднимите
        /// <see cref="GeometryMaterialStore.CurrentSeedVersion"/>, иначе к тем,
        /// у кого файл уже есть, новое вещество не доедет.
        /// </summary>
        public static List<Entry> Seed()
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

            // Оксид лютеция — проба трёх спектров Lu-176 (ASN16, AS80x80,
            // RC-103, одна банка 50 мл Ø40 × h15). Заведён 15.08.2026 как
            // ЗАТЫЧКА, потому что редактора веществ ещё не было; сегодня он тут
            // на правах засева — без него геометрию этих съёмок нельзя было
            // назвать иначе как воздухом, а цена такой подмены измерена —
            // кривая AS80x80 завышалась в 2.6 раза (`E19`, §13ж журнала
            // матрицы).
            //
            // ⚠ Плотность здесь — МОНОЛИТНАЯ (9.42), в отличие от остальных
            // проб этого списка, где стоит насыпная: у порошка оксида она в
            // разы меньше и НЕ ИЗВЕСТНА, пока банку не взвесили. Значение из
            // списка — только умолчание поля; настоящая всегда подаётся в
            // `Make(entry, density)` и считается от массы.
            add("Lu2O3", "Lutetium oxide", "Lu2 O3", 9.42, MaterialKind.Source);
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

        // Символы элементов больше не переписаны сюда списком: они есть в
        // `matdb.sqlite` (символы элементов), и второй список означал бы второй
        // источник правды. Сверено перед переносом: 92 символа в коде против
        // 119 в базе, расхождений ноль.
        public static int ZOf(string symbol)
        {
            return MaterialDatabase.ZOf(symbol);
        }

        public static string SymbolOf(int z)
        {
            return MaterialDatabase.SymbolOf(z);
        }

        /// <summary>
        /// Готовое вещество: массовые доли посчитаны из формулы (или сложены из
        /// смеси), плотность взята заданная — у одного и того же вещества она
        /// зависит от набивки, и правит её пользователь.
        /// </summary>
        public static GeometryMaterial Make(Entry entry, double density)
        {
            return Make(entry, density, ByName);
        }

        /// <summary>
        /// То же, но состав смеси ищется НЕ в сохранённой библиотеке, а там, где
        /// скажет вызывающий. Нужно редактору: он показывает состав вещества,
        /// которое ещё правят, и составляющие у него — из рабочего списка, а не
        /// из файла на диске.
        /// </summary>
        public static GeometryMaterial Make(Entry entry, double density, Func<string, Entry> lookup)
        {
            GeometryMaterial material = new GeometryMaterial
            {
                Name = entry.Name,
                Density = density > 0.0 ? density : entry.Density,
            };

            Dictionary<int, double> fractions = Compose(entry, lookup, new List<string>());
            foreach (KeyValuePair<int, double> pair in fractions)
            {
                material.Fractions[pair.Key] = pair.Value;
            }

            return material;
        }

        /// <summary>
        /// Массовые доли элементов: из формулы, а у смеси — сложением долей
        /// составляющих с их весами.
        ///
        /// <paramref name="chain"/> — имена веществ, которые уже разбираются
        /// СЕЙЧАС. Смесь, входящая сама в себя (хоть через три звена), иначе
        /// уводит расчёт в бесконечность; здесь такая составляющая просто
        /// пропускается, а редактор про кольцо говорит отдельно — молчаливо
        /// повисшая программа хуже отказа.
        /// </summary>
        static Dictionary<int, double> Compose(Entry entry, Func<string, Entry> lookup, List<string> chain)
        {
            Dictionary<int, double> result = new Dictionary<int, double>();
            if (entry == null)
            {
                return result;
            }

            if (!entry.IsMixture)
            {
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
                    return result;
                }

                foreach (KeyValuePair<int, double> pair in atoms)
                {
                    double mass;
                    if (AttenuationData.AtomicMass.TryGetValue(pair.Key, out mass) && mass > 0.0)
                    {
                        result[pair.Key] = pair.Value * mass / total;
                    }
                }

                return result;
            }

            chain.Add(entry.Name ?? "");
            try
            {
                // Веса относительные — нормируются здесь. «50 и 50» и «1 и 1»
                // обязаны дать одно и то же.
                double weights = 0.0;
                foreach (GeometryMaterialComponent component in entry.Components)
                {
                    if (component.Weight > 0.0 && !InChain(chain, component.Material))
                    {
                        weights += component.Weight;
                    }
                }

                if (!(weights > 0.0))
                {
                    return result;
                }

                foreach (GeometryMaterialComponent component in entry.Components)
                {
                    if (!(component.Weight > 0.0) || InChain(chain, component.Material))
                    {
                        continue;
                    }

                    Entry part = lookup != null ? lookup(component.Material) : null;
                    if (part == null)
                    {
                        continue;
                    }

                    double share = component.Weight / weights;
                    foreach (KeyValuePair<int, double> pair in Compose(part, lookup, chain))
                    {
                        double have;
                        result.TryGetValue(pair.Key, out have);
                        result[pair.Key] = have + share * pair.Value;
                    }
                }
            }
            finally
            {
                chain.RemoveAt(chain.Count - 1);
            }

            return result;
        }

        static bool InChain(List<string> chain, string name)
        {
            foreach (string item in chain)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Кольцо в составе: входит ли вещество само в себя через свои
        /// составляющие. Вызывает редактор перед сохранением — расчёт такую
        /// составляющую пропустит молча, и состав получится не тот, что виден.
        /// </summary>
        public static bool HasCycle(Entry entry, Func<string, Entry> lookup)
        {
            return HasCycle(entry, lookup, new List<string>());
        }

        static bool HasCycle(Entry entry, Func<string, Entry> lookup, List<string> chain)
        {
            if (entry == null || !entry.IsMixture)
            {
                return false;
            }

            if (InChain(chain, entry.Name))
            {
                return true;
            }

            chain.Add(entry.Name ?? "");
            try
            {
                foreach (GeometryMaterialComponent component in entry.Components)
                {
                    Entry part = lookup != null ? lookup(component.Material) : null;
                    if (part != null && HasCycle(part, lookup, chain))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                chain.RemoveAt(chain.Count - 1);
            }

            return false;
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
