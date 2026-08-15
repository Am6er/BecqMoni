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
        public const int CurrentSeedVersion = 4;

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

            entries = list;
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
