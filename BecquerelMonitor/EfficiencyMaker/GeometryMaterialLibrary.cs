using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BecquerelMonitor.Properties;

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

            /// <summary>
            /// Массовые доли элементов, заданные ПРЯМО: Z -> доля. Третий способ
            /// описать вещество, и он старше двух других — смотрится первым.
            ///
            /// Заведён под таблицу ЛСРМ (`materials.dat`, 287 веществ): у 39 из
            /// них формулы нет вовсе — ткани ICRU, сплавы, воздух описаны только
            /// долями, и вывести их не из чего. А у остальных формулу брать
            /// НЕЛЬЗЯ ещё и потому, что записана она там для человека, а не для
            /// разбора: полимеры стоят как `(C2F4)n`, а у воды — прямая опечатка
            /// `H20`, которая нашим разбором формулы прочлась бы как двадцать
            /// водородов. Доли в том же файле — величины NIST и сходятся к
            /// единице у всех 287 строк.
            ///
            /// Правило «руками доли не вписывают» этим не нарушено: сюда они
            /// попадают ИМПОРТОМ из файла поставщика, а не с клавиатуры, и
            /// редактор их не показывает полем для правки.
            /// </summary>
            public readonly Dictionary<int, double> ElementFractions = new Dictionary<int, double>();

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

                foreach (KeyValuePair<int, double> pair in this.ElementFractions)
                {
                    copy.ElementFractions[pair.Key] = pair.Value;
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
            Source,

            /// <summary>
            /// Место не назначено. Сюда попадают 258 веществ таблицы ЛСРМ
            /// (`materials.dat`): у них в файле никакого «куда годится» нет, а
            /// разложить их по видам можно было бы только УГАДЫВАНИЕМ — свинец
            /// это оправа или проба, стекло это сосуд или образец, зависит от
            /// съёмки, а не от вещества.
            ///
            /// В списках редактора геометрии они идут ПОСЛЕ веществ своего вида,
            /// за разделителем: короткий выверенный список остаётся сверху, а
            /// спрятано при этом ничего. Назначить вид можно в редакторе
            /// веществ — это одно движение, и оно запоминается.
            /// </summary>
            Other
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

            // Двуокись тория — по указанию Amber 16.08.2026, в ОБЩИЙ засев.
            // Понадобилась на «Электродах WT-20»: их 2 % тория связаны в ThO2,
            // а в библиотеке торий и кислород лежали порознь, и кислород —
            // ГАЗООБРАЗНЫЙ. От этого плотность из состава выходила 0.535 г/см³
            // вместо 18.92 — 0.24 % массы занимали 97 % объёма (`E26`).
            // С этой строкой WT-20 записывается вольфрамом 0.98 плюс ThO2 0.02
            // и даёт 18.937 против введённых Amber 18.92.
            //
            // Плотность 9.86 — монолитная (кристаллическая 10.0; у спечённой
            // керамики 9.7–9.9), вид `Source`: в наших съёмках ThO2 встречается
            // добавкой в пробе, а не обвязкой.
            add("ThO2", "Thorium dioxide", "Th1 O2", 9.86, MaterialKind.Source);

            // Таблица веществ ЛСРМ (`materials.dat` их же GeometryMaster, 2008;
            // ввоз 16.08.2026 — `tools/effmaker/import_lsrm_materials.py`).
            // Двадцать девять строк выше — НАШИ, выверенные руками, и таблица их
            // не трогает: у трёх плотность отличается НАРОЧНО (`SiO2` 1.6 и
            // `CaCO3` 1.5 — насыпные, `M5`, против монолитных 2.32 и 2.8).
            //
            // Состав ввезённых задан массовыми долями, а не формулой, и вид у
            // них `Other`: «куда годится» в файле ЛСРМ нет, а разложить 287
            // веществ по пяти видам можно было бы только угадыванием.
            HashSet<string> already = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in list)
            {
                already.Add(entry.Name);
            }

            foreach (string[] row in GeometryMaterialSeed.Rows)
            {
                if (already.Contains(row[0]))
                {
                    continue;
                }

                double density;
                if (!double.TryParse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture, out density)
                    || !(density > 0.0))
                {
                    continue;
                }

                Entry imported = new Entry
                {
                    Abbr = "",
                    Name = row[0],
                    Formula = "",
                    Density = density,
                    Kind = MaterialKind.Other,
                };

                // Формула из файла кладётся ПОДПИСЬЮ, а не источником состава:
                // разбирать её нельзя (`(C2F4)n`, опечатка `H20` у воды).
                foreach (KeyValuePair<int, double> pair in GeometryMaterialSeed.Fractions(row[3]))
                {
                    imported.ElementFractions[pair.Key] = pair.Value;
                }

                if (imported.ElementFractions.Count > 0)
                {
                    list.Add(imported);
                }
            }

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
        /// <summary>
        /// Плотность СМЕСИ из плотностей её составляющих: объёмы складываются,
        /// то есть 1/ρ = Σ w_i/ρ_i при массовых долях w_i. Ложь — вывести
        /// нельзя, и тогда <paramref name="problem"/> говорит почему.
        ///
        /// Из ФОРМУЛЫ плотность не выводится вовсе, и делать вид, что выводится,
        /// нельзя: формула задаёт состав, а плотность — упаковку, для которой
        /// нужна кристаллическая структура или справочное значение. Поэтому
        /// метод работает только со смесью, у которой плотность каждой части
        /// уже известна.
        ///
        /// ⚠ Отдельно ловится смесь ГАЗА С ТВЁРДЫМ. Проверено на «Электроды
        /// WT-20» из поставки: вольфрам 0.98 + торий 0.01758 + кислород
        /// газообразный 0.00242 даёт **0.535 г/см³** вместо 18.92, потому что
        /// 0.24 % кислорода при ρ = 0.001332 занимают 97 % объёма. Кислород там
        /// связан в ThO₂ и газом не является: правильная запись — вольфрам 0.98
        /// плюс двуокись тория 0.02, и она даёт **18.94** против введённых
        /// Amber 18.92. То есть правило верное, а состав записан не тем.
        /// Молча выдать 0.535 было бы хуже отказа.
        /// </summary>
        public static bool TryDensityFromComponents(Entry entry, Func<string, Entry> lookup,
                                                    out double density, out string problem)
        {
            density = 0.0;
            problem = null;
            if (entry == null || entry.Components.Count == 0)
            {
                problem = Resources.GeometryMaterialsDensityNeedsMixture;
                return false;
            }

            double weightSum = 0.0;
            foreach (GeometryMaterialComponent component in entry.Components)
            {
                weightSum += Math.Max(0.0, component.Weight);
            }

            if (!(weightSum > 0.0))
            {
                problem = Resources.GeometryMaterialsDensityNeedsMixture;
                return false;
            }

            double inverse = 0.0;
            double lightest = double.MaxValue;
            double heaviest = 0.0;
            string gassy = null;
            foreach (GeometryMaterialComponent component in entry.Components)
            {
                double weight = Math.Max(0.0, component.Weight) / weightSum;
                if (weight <= 0.0)
                {
                    continue;
                }

                Entry part = lookup == null ? null : lookup(component.Material);
                if (part == null || !(part.Density > 0.0))
                {
                    problem = string.Format(CultureInfo.CurrentCulture,
                                            Resources.GeometryMaterialsDensityNoPart,
                                            component.Material);
                    return false;
                }

                inverse += weight / part.Density;
                if (part.Density < lightest)
                {
                    lightest = part.Density;
                    gassy = part.Name;
                }

                heaviest = Math.Max(heaviest, part.Density);
            }

            if (!(inverse > 0.0))
            {
                problem = Resources.GeometryMaterialsDensityNeedsMixture;
                return false;
            }

            // Газ вперемешку с конденсированным веществом — не смесь, а ошибка
            // записи состава. Смесь ОДНИХ газов (воздух) при этом считается
            // как считалась: условие требует обеих крайностей сразу.
            if (lightest < 0.05 && heaviest > 1.0)
            {
                problem = string.Format(CultureInfo.CurrentCulture,
                                        Resources.GeometryMaterialsDensityGasInSolid,
                                        gassy, lightest, 1.0 / inverse);
                return false;
            }

            density = 1.0 / inverse;
            return true;
        }

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

            // Прямые доли старше всего остального: если они есть, вещество ими и
            // описано, а формула рядом — только для глаз.
            if (entry.ElementFractions.Count > 0)
            {
                double weight = 0.0;
                foreach (KeyValuePair<int, double> pair in entry.ElementFractions)
                {
                    if (pair.Key > 0 && pair.Value > 0.0)
                    {
                        weight += pair.Value;
                    }
                }

                if (!(weight > 0.0))
                {
                    return result;
                }

                // Нормировка на всякий случай: у ЛСРМ суммы сходятся к единице
                // все 287, но библиотека правится человеком, и полагаться на
                // это нельзя.
                foreach (KeyValuePair<int, double> pair in entry.ElementFractions)
                {
                    if (pair.Key > 0 && pair.Value > 0.0)
                    {
                        result[pair.Key] = pair.Value / weight;
                    }
                }

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
