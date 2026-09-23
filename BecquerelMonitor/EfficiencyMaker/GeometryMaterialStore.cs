using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Одна составляющая смеси: ИМЯ вещества библиотеки и его массовый вес.
    ///
    /// Именем, а не копией состава, — нарочно. Так устроен и формат `.in`
    /// (`Nmaterials`, `Name[i]`, `MatRelWeight[i]`), и так правка вещества
    /// доходит до всех смесей, куда оно входит: скопированный состав пришлось
    /// бы править во всех местах и молча расходился бы с исходником.
    /// </summary>
    public sealed class GeometryMaterialComponent
    {
        [XmlAttribute]
        public string Material = "";

        /// <summary>
        /// Вес ОТНОСИТЕЛЬНЫЙ: доли нормируются на сумму при расчёте. Требовать
        /// суммы ровно единицы значило бы ловить пользователя на округлении
        /// («0.33 + 0.33 + 0.33 = 0.99, введите заново»), а смысла в этом нет.
        /// </summary>
        [XmlAttribute]
        public double Weight;
    }

    /// <summary>Вещество библиотеки в том виде, в каком оно ложится в XML.</summary>
    public sealed class GeometryMaterialRecord
    {
        [XmlAttribute]
        public string Name = "";

        [XmlAttribute]
        public string Abbr = "";

        [XmlAttribute]
        public string Formula = "";

        [XmlAttribute]
        public double Density;

        [XmlAttribute]
        public GeometryMaterialLibrary.MaterialKind Kind;

        [XmlArray("Components")]
        [XmlArrayItem("Component")]
        public GeometryMaterialComponent[] Components;

        /// <summary>
        /// Массовые доли элементов, заданные прямо (ввоз таблицы ЛСРМ). Пусто у
        /// вещества, описанного формулой или смесью.
        /// </summary>
        [XmlArray("Fractions")]
        [XmlArrayItem("Element")]
        public GeometryElementFraction[] Fractions;
    }

    /// <summary>Файл библиотеки веществ целиком.</summary>
    [XmlRoot("GeometryMaterials")]
    public sealed class GeometryMaterialConfig
    {
        /// <summary>
        /// Поколение вшитого списка, с которым файл в последний раз сводили.
        /// Когда в коде появляется новое вещество, а у пользователя файл уже
        /// есть, поколение растёт — и новое вещество доезжает до него, не
        /// затирая его собственных. Без этого счётчика вшитый список после
        /// первой же правки пользователя замерзал бы навсегда.
        /// </summary>
        [XmlAttribute]
        public int SeedVersion;

        [XmlArray("Materials")]
        [XmlArrayItem("Material")]
        public GeometryMaterialRecord[] Materials;

        /// <summary>
        /// Вшитые вещества, которые пользователь УДАЛИЛ. Без этого списка
        /// сведение с новым поколением возвращало бы их обратно на каждом
        /// обновлении программы, и удаление выглядело бы неработающим.
        /// </summary>
        [XmlArray("Removed")]
        [XmlArrayItem("Name")]
        public string[] Removed;
    }

    /// <summary>
    /// Хранилище библиотеки веществ: файл в конфигурации пользователя рядом с
    /// остальными (`config\GeometryMaterials.xml`).
    ///
    /// Зачем оно есть (`E20`). До 15.08.2026 список веществ был зашит в
    /// `GeometryMaterialLibrary.Build()`, и своё вещество нельзя было завести
    /// иначе как правкой исходника: оксид лютеция так и появился — затычкой в
    /// коде. Цена подмены измерена — проба, оставшаяся воздухом, завысила
    /// кривую AS80x80 в 2.6 раза (`E19`, §13ж журнала матрицы), и заметили это
    /// только через расхождение сумм-пика.
    ///
    /// Вшитый список никуда не делся: он — ЗАСЕВ, то есть то, что видит
    /// пользователь, у которого файла ещё нет. Как только файл появился, правда
    /// в нём, а засев доезжает по поколениям (см. <see cref="GeometryMaterialConfig.SeedVersion"/>).
    /// </summary>
    public static class GeometryMaterialStore
    {
        /// <summary>
        /// Поколение вшитого списка. Растёт, когда в <c>Seed()</c> добавили
        /// вещество и его надо довезти до тех, у кого файл уже есть.
        /// </summary>
        /// 2 (16.08.2026) — ввезена таблица веществ ЛСРМ, 268 строк.
        /// 3 (16.08.2026) — двуокись тория (ThO2) по указанию Amber: без неё
        ///     состав «Электродов WT-20» нечем записать так, чтобы плотность
        ///     из него считалась (`E26`).
        /// 4 (16.08.2026) — четыре набивки поверочных эталонов ЛСРМ
        ///     (ОИСН-06/-10/-16, РИСН-379): состав из заголовков спектров
        ///     поверки, `B12`.
        /// 5 (16.08.2026) — грунт (`Soil`) по решению Amber: без него двум
        ///     готовым сценам съёмки в поле (`E27`) нечем считать свободный
        ///     пробег, а по воздуху сцена выходит в сорок метров.
        /// 6 (06.09.2026) — набивки ВТОРОЙ ПОВЕРКИ, `ОИСН-06 (2024)` и
        ///     `ОИСН-16 (2024)`, по решению Amber (`A181`, `B13`): поставка
        ///     переобъявила состав, не сменив имени, и 23 из 44 корпусных
        ///     геометрий несут набивку, которой в засеве не было вовсе.
        /// 7 (10.09.2026) — `Ториевое стекло` по прямому слову Amber
        ///     («Заведи отдельно Ториевое стекло с этой плотностью», `AMBER3`):
        ///     ториевый диск Ø40 × 5 мм стоял в геометрии как `Glass, plate`
        ///     2.4 г/см³ при взвешенных 4.345, и матрица отклика этой сцены
        ///     делала модель хуже, чем расчёт без матрицы вовсе.
        /// 8 (22.09.2026) — ВОЗДУХ И СТЕКЛО по NIST (`AMBER53`): «Air, dry»
        ///     получил состав NIST вместо формулы `N2 O1` (не было аргона), а
        ///     стеклом стенки сосуда стала «Glass, plate» вместо кварца
        ///     «Glass». Новых ИМЁН поколение не приносит, и потому одного
        ///     сведения по именам мало — нужен ПЕРЕНОС
        ///     (<see cref="MigrateAirGlass"/>, `AMBER67`).
        public const int CurrentSeedVersion = 8;

        /// <summary>
        /// (`AMBER67`) Поколение, НАЧИНАЯ С КОТОРОГО воздух и стекло стоят по
        /// NIST. Файл более старого поколения проходит перенос
        /// <see cref="MigrateAirGlass"/>; отдельной постоянной — чтобы
        /// следующее поколение засева (новое вещество) не заставляло перенос
        /// повторяться и чтобы условие читалось словом, а не числом.
        /// </summary>
        const int AirGlassSeedVersion = 8;

        static List<GeometryMaterialLibrary.Entry> entries;
        static List<string> removed = new List<string>();

        /// <summary>
        /// Чем кончилась загрузка файла. Пусто — всё в порядке (в том числе
        /// когда файла нет вовсе: это обычное первое открытие). Непусто —
        /// библиотека сейчас ВШИТАЯ, а не пользовательская, и сохранение
        /// заменит непрочитанный файл; поэтому редактор говорит об этом вслух.
        /// </summary>
        public static string LoadError { get; private set; }

        public static string FilePath
        {
            get { return Package.GetInstance().GeometryMaterials; }
        }

        public static List<GeometryMaterialLibrary.Entry> Entries
        {
            get
            {
                EnsureLoaded();
                return entries;
            }
        }

        /// <summary>Перечитать с диска — после правки файла снаружи.</summary>
        public static void Reload()
        {
            entries = null;
            LoadError = null;
        }

        static void EnsureLoaded()
        {
            if (entries != null)
            {
                return;
            }

            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            removed = new List<string>();

            GeometryMaterialConfig config = null;
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(GeometryMaterialConfig));
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        config = (GeometryMaterialConfig)serializer.Deserialize(stream);
                    }
                }
            }
            catch (Exception e)
            {
                // Библиотека остаётся вшитой. Молча подставить её нельзя:
                // пользователь увидел бы СВОЙ список без своих веществ и решил,
                // что они пропали, — поэтому причина сохраняется и показывается.
                LoadError = e.Message;
                config = null;
            }

            if (config == null)
            {
                entries = seed;
                return;
            }

            List<GeometryMaterialLibrary.Entry> list = new List<GeometryMaterialLibrary.Entry>();
            if (config.Materials != null)
            {
                foreach (GeometryMaterialRecord record in config.Materials)
                {
                    if (record != null && !string.IsNullOrEmpty(record.Name))
                    {
                        list.Add(FromRecord(record));
                    }
                }
            }

            if (config.Removed != null)
            {
                foreach (string name in config.Removed)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        removed.Add(name);
                    }
                }
            }

            // Довезти новое из вшитого списка: то, чего в файле нет по имени и
            // что пользователь не удалял.
            if (config.SeedVersion < CurrentSeedVersion)
            {
                foreach (GeometryMaterialLibrary.Entry entry in seed)
                {
                    if (Find(list, entry.Name) == null && !Contains(removed, entry.Name))
                    {
                        list.Add(entry);
                    }
                }
            }

            // (`AMBER67`) ...а затем перенести то, у чего ИМЯ ОСТАЛОСЬ ПРЕЖНИМ,
            // а содержимое в засеве переписано. Сведение выше такого не видит
            // по построению: оно смотрит только на отсутствующие имена.
            if (config.SeedVersion < AirGlassSeedVersion)
            {
                MigrateAirGlass(list, seed);
            }

            entries = list;
        }

        /// <summary>
        /// (`AMBER67`, решение Amber 22.09.2026 вопросником, дословно: «Да,
        /// перенос кодом: только записи, ПОБИТОВО равные прежнему засеву»)
        /// ПЕРЕНОС библиотеки пользователя на засев поколения 8 — воздух и
        /// стекло по NIST (`AMBER53`).
        ///
        /// ⛔ Трогается ТОЛЬКО запись, побитово равная тому, чем её положил
        /// ПРЕЖНИЙ засев: имя, сокращение, формула, плотность, вид, состав,
        /// смесь — всё до последнего поля. Правленная рукой остаётся как есть,
        /// даже если правка — одна цифра плотности: своё вещество человека
        /// программа переписывать не вправе, а отличить «своё» от «нашего»
        /// больше нечем — у записи нет пометки происхождения.
        ///
        /// Три движения, и все три — то же, что увидел бы новый пользователь:
        ///
        /// 1. «Air, dry» формулой `N2 O1` → составом NIST (N/O/Ar/C). Формула
        ///    не была воздухом: без аргона μ/ρ на 30 кэВ 0.3325 против 0.3538
        ///    см²/г (−6.0 %); на самом толстом зазоре поставки 21.7 мм
        ///    пропускание расходится на 5.6e-5 (П123).
        /// 2. «Glass» (кварц `Si1 O2` 2.32) СНИМАЕТСЯ: в засеве поколения 8
        ///    его нет вовсе — стеклом стенки сосуда стала «Glass, plate».
        ///    Геометрии этим не рвутся: вещество в них лежит СОСТАВОМ, а не
        ///    ссылкой на библиотеку (<c>GeometryMaterial</c>), и список — лишь
        ///    источник выбора в редакторе.
        /// 3. «Glass, plate» строкой таблицы ЛСРМ (вид `Other`) → тем же
        ///    составом, но видом `BeakerWall` и сокращением «Glass»: иначе
        ///    плитного стекла в списке стенок сосуда у человека не будет
        ///    вовсе — имя занято, и сведение по именам его не добавит.
        ///
        /// ⚠ Запись НЕ СОХРАНЯЕТСЯ: перенос живёт в памяти ровно так же, как
        /// сведение по именам выше, а файл переписывает только редактор
        /// (<see cref="Save"/>) — по движению человека, а не сам собой.
        /// </summary>
        static void MigrateAirGlass(List<GeometryMaterialLibrary.Entry> list,
                                    List<GeometryMaterialLibrary.Entry> seed)
        {
            // Воздух: прежняя запись — формулой, без долей.
            Migrate(list, PreviousAir(), Find(seed, "Air, dry"));

            // Кварц под именем «Glass»: преемника в засеве нет — снять.
            Migrate(list, PreviousGlass(), null);

            // Плитное стекло: прежняя запись — строка таблицы, вид `Other`.
            GeometryMaterialLibrary.Entry plate = Find(seed, "Glass, plate");
            Migrate(list, PreviousPlate(plate), plate);
        }

        /// <summary>
        /// Заменить запись <paramref name="before"/> на <paramref name="after"/>
        /// (null — снять), и только если она в списке есть и побитово равна
        /// прежнему засеву. Имя ищется тем же сравнением, каким библиотека
        /// ищет вещество везде (<see cref="Find"/>).
        /// </summary>
        static void Migrate(List<GeometryMaterialLibrary.Entry> list,
                            GeometryMaterialLibrary.Entry before,
                            GeometryMaterialLibrary.Entry after)
        {
            if (before == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Name, before.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!SameEntry(list[i], before))
                {
                    // Правлена рукой — не наша.
                    return;
                }

                if (after == null)
                {
                    list.RemoveAt(i);
                }
                else
                {
                    list[i] = after.Clone();
                }

                return;
            }
        }

        /// <summary>
        /// «Air, dry» ПРЕЖНЕГО засева (до 22.09.2026): формула `N2 O1`,
        /// 0.001205 г/см³, вид пробы. Стоит здесь строкой, а не берётся из
        /// <see cref="GeometryMaterialLibrary"/>: в засеве её больше нет, а
        /// перенос обязан узнавать ровно ту запись, которую положил прежний
        /// код, — иначе он трогал бы чужое.
        /// </summary>
        static GeometryMaterialLibrary.Entry PreviousAir()
        {
            return new GeometryMaterialLibrary.Entry
            {
                Name = "Air, dry",
                Abbr = "Air",
                Formula = "N2 O1",
                Density = 0.001205,
                Kind = GeometryMaterialLibrary.MaterialKind.Source,
            };
        }

        /// <summary>
        /// «Glass» ПРЕЖНЕГО засева: кварц `Si1 O2` 2.32 г/см³ стенкой сосуда.
        /// Это NIST «Silicon dioxide», а не стекло (`AMBER53`).
        /// </summary>
        static GeometryMaterialLibrary.Entry PreviousGlass()
        {
            return new GeometryMaterialLibrary.Entry
            {
                Name = "Glass",
                Abbr = "SiO2",
                Formula = "Si1 O2",
                Density = 2.32,
                Kind = GeometryMaterialLibrary.MaterialKind.BeakerWall,
            };
        }

        /// <summary>
        /// «Glass, plate» ПРЕЖНЕГО засева — строка таблицы ЛСРМ: тот же состав
        /// и плотность, что у нынешней записи засева, но без сокращения и видом
        /// `Other`. Выводится ИЗ НЕЁ ЖЕ, а не набирается числами: состав у
        /// строки таблицы один и тот же до и после, и переписывать его сюда
        /// значило бы завести вторую копию четырёх долей, которая молча
        /// разойдётся с таблицей.
        /// </summary>
        static GeometryMaterialLibrary.Entry PreviousPlate(GeometryMaterialLibrary.Entry plate)
        {
            if (plate == null)
            {
                return null;
            }

            GeometryMaterialLibrary.Entry before = plate.Clone();
            before.Abbr = "";
            before.Kind = GeometryMaterialLibrary.MaterialKind.Other;
            return before;
        }

        /// <summary>
        /// (`AMBER67`, то же решение Amber 22.09.2026) ПЕРЕНОС ВЕЩЕСТВА СЛОТОВ
        /// готовой геометрии на состав действующей библиотеки.
        ///
        /// Зачем отдельно от библиотеки. Вещество в геометрии лежит СНИМКОМ —
        /// имя, плотность и массовые доли (<c>GeometryMaterial</c>), а не
        /// ссылкой на библиотеку: сцена обязана считаться и тогда, когда
        /// вещества в списке уже нет. Поэтому правка засева до слотов не
        /// доезжает сама: у Amber 18 слотов трёх приборов (12 зазоров и 6 проб)
        /// несут воздух долями N 0.636483 / O 0.363517 — тем, что давала
        /// формула `N2 O1`, и после переноса библиотеки они остались бы с ним.
        ///
        /// ⛔ Трогается ТОЛЬКО снимок, побитово равный тому, что давал ПРЕЖНИЙ
        /// засев: имя «Air, dry», плотность 0.001205 и ровно два элемента с
        /// долями, посчитанными из формулы `N2 O1`. Воздух, заданный человеком
        /// иначе (другая плотность, свои доли), не трогается — как и в
        /// библиотеке.
        ///
        /// ⚠ Файлы `.in` этим путём НЕ идут: их читает
        /// <c>GeometryModel.Load</c>, а не этот перенос, и 61 сцена корпуса и
        /// моделей остаётся побитово прежней — вместе со своими клеймами и
        /// матрицами склада.
        ///
        /// ⚠ Атомные массы (`matdb`) поднимаются ТОЛЬКО когда слот прошёл
        /// дешёвую проверку «имя, плотность, два элемента»: эталон прежнего
        /// состава считается из формулы, и считать его на каждой геометрии
        /// незачем — у того, кто уже на новом засеве, до базы дело не доходит.
        /// </summary>
        public static void MigrateGeometry(GeometryModel model)
        {
            if (model == null)
            {
                return;
            }

            MigrateSlot(model.Crystal);
            MigrateSlot(model.Reflector);
            MigrateSlot(model.Gap);
            MigrateSlot(model.Cladding);
            MigrateSlot(model.BeakerWall);
            MigrateSlot(model.Source);
        }

        /// <summary>Один слот геометрии — см. <see cref="MigrateGeometry"/>.</summary>
        static void MigrateSlot(GeometryMaterial material)
        {
            if (material == null
                || !string.Equals(material.Name, "Air, dry", StringComparison.OrdinalIgnoreCase)
                || material.Density != 0.001205
                || material.Fractions.Count != 2
                || !material.Fractions.ContainsKey(7)
                || !material.Fractions.ContainsKey(8))
            {
                return;
            }

            Dictionary<int, double> before = PreviousAirFractions();
            if (before == null || before.Count != material.Fractions.Count)
            {
                return;
            }

            foreach (KeyValuePair<int, double> pair in before)
            {
                double got;
                if (!material.Fractions.TryGetValue(pair.Key, out got) || got != pair.Value)
                {
                    return;
                }
            }

            GeometryMaterialLibrary.Entry air = Find(Entries, "Air, dry");
            if (air == null)
            {
                return;
            }

            GeometryMaterial now = GeometryMaterialLibrary.Make(air, material.Density);
            if (now.Fractions.Count == 0)
            {
                return;
            }

            material.Fractions.Clear();
            foreach (KeyValuePair<int, double> pair in now.Fractions)
            {
                material.Fractions[pair.Key] = pair.Value;
            }
        }

        static Dictionary<int, double> previousAirFractions;

        /// <summary>
        /// Состав воздуха ПРЕЖНЕГО засева — тот, что давала формула `N2 O1`
        /// через атомные массы. Считается один раз: он же сравнивается с
        /// каждым слотом.
        /// </summary>
        static Dictionary<int, double> PreviousAirFractions()
        {
            if (previousAirFractions == null)
            {
                GeometryMaterial air = GeometryMaterialLibrary.Make(PreviousAir(), 0.001205, name => null);
                previousAirFractions = new Dictionary<int, double>(air.Fractions);
            }

            return previousAirFractions.Count > 0 ? previousAirFractions : null;
        }

        /// <summary>
        /// Побитовое равенство двух записей библиотеки: все поля, состав и
        /// смесь. Числа сравниваются ТОЧНО — округления здесь нет и быть не
        /// может: обе стороны либо пришли из одного и того же кода засева, либо
        /// проехали через файл, который пишет и читает их без потери
        /// (<c>XmlSerializer</c>, круговой формат double).
        /// </summary>
        static bool SameEntry(GeometryMaterialLibrary.Entry a, GeometryMaterialLibrary.Entry b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (!string.Equals(a.Name ?? "", b.Name ?? "", StringComparison.Ordinal)
                || !string.Equals(a.Abbr ?? "", b.Abbr ?? "", StringComparison.Ordinal)
                || !string.Equals(a.Formula ?? "", b.Formula ?? "", StringComparison.Ordinal)
                || a.Density != b.Density
                || a.Kind != b.Kind
                || a.ElementFractions.Count != b.ElementFractions.Count
                || a.Components.Count != b.Components.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> pair in a.ElementFractions)
            {
                double other;
                if (!b.ElementFractions.TryGetValue(pair.Key, out other) || other != pair.Value)
                {
                    return false;
                }
            }

            for (int i = 0; i < a.Components.Count; i++)
            {
                if (!string.Equals(a.Components[i].Material ?? "", b.Components[i].Material ?? "",
                                   StringComparison.Ordinal)
                    || a.Components[i].Weight != b.Components[i].Weight)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Заменить библиотеку целиком и записать её. Зовёт ТОЛЬКО редактор:
        /// конфигурация пользователя правится человеком, а не расчётом.
        /// </summary>
        public static void Save(IEnumerable<GeometryMaterialLibrary.Entry> list)
        {
            List<GeometryMaterialLibrary.Entry> next = new List<GeometryMaterialLibrary.Entry>();
            foreach (GeometryMaterialLibrary.Entry entry in list)
            {
                next.Add(entry.Clone());
            }

            // Что из вшитого списка человек удалил — запомнить поимённо, иначе
            // следующее сведение вернёт удалённое обратно.
            List<string> gone = new List<string>();
            foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialLibrary.Seed())
            {
                if (Find(next, entry.Name) == null)
                {
                    gone.Add(entry.Name);
                }
            }

            GeometryMaterialConfig config = new GeometryMaterialConfig
            {
                SeedVersion = CurrentSeedVersion,
                Materials = ToRecords(next),
                Removed = gone.ToArray(),
            };

            string path = FilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(GeometryMaterialConfig));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(stream, config);
            }

            entries = next;
            removed = gone;
            LoadError = null;
        }

        static GeometryMaterialRecord[] ToRecords(List<GeometryMaterialLibrary.Entry> list)
        {
            List<GeometryMaterialRecord> records = new List<GeometryMaterialRecord>();
            foreach (GeometryMaterialLibrary.Entry entry in list)
            {
                GeometryMaterialRecord record = new GeometryMaterialRecord
                {
                    Name = entry.Name ?? "",
                    Abbr = entry.Abbr ?? "",
                    Formula = entry.Formula ?? "",
                    Density = entry.Density,
                    Kind = entry.Kind,
                };

                if (entry.Components != null && entry.Components.Count > 0)
                {
                    record.Components = entry.Components.ToArray();
                }

                if (entry.ElementFractions.Count > 0)
                {
                    List<GeometryElementFraction> fractions = new List<GeometryElementFraction>();
                    foreach (KeyValuePair<int, double> pair in entry.ElementFractions)
                    {
                        fractions.Add(new GeometryElementFraction { Z = pair.Key, Fraction = pair.Value });
                    }

                    fractions.Sort((a, b) => a.Z.CompareTo(b.Z));
                    record.Fractions = fractions.ToArray();
                }

                records.Add(record);
            }

            return records.ToArray();
        }

        static GeometryMaterialLibrary.Entry FromRecord(GeometryMaterialRecord record)
        {
            GeometryMaterialLibrary.Entry entry = new GeometryMaterialLibrary.Entry
            {
                Name = record.Name ?? "",
                Abbr = record.Abbr ?? "",
                Formula = record.Formula ?? "",
                Density = record.Density,
                Kind = record.Kind,
            };

            if (record.Components != null)
            {
                foreach (GeometryMaterialComponent component in record.Components)
                {
                    if (component != null && !string.IsNullOrEmpty(component.Material))
                    {
                        entry.Components.Add(new GeometryMaterialComponent
                        {
                            Material = component.Material,
                            Weight = component.Weight,
                        });
                    }
                }
            }

            if (record.Fractions != null)
            {
                foreach (GeometryElementFraction fraction in record.Fractions)
                {
                    if (fraction != null && fraction.Z > 0 && fraction.Fraction > 0.0)
                    {
                        entry.ElementFractions[fraction.Z] = fraction.Fraction;
                    }
                }
            }

            return entry;
        }

        static GeometryMaterialLibrary.Entry Find(List<GeometryMaterialLibrary.Entry> list, string name)
        {
            foreach (GeometryMaterialLibrary.Entry entry in list)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        static bool Contains(List<string> list, string name)
        {
            foreach (string item in list)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
