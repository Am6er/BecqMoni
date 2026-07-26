using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BecquerelMonitor.RoiWizard
{
    // Каталог ядерных данных конструктора. Источник — тот же nucdb.sqlite, из которого
    // работает NucBase: своего снимка у модуля нет и быть не должно, иначе две копии
    // одних и тех же данных разъезжаются после первой правки любой из них.
    //
    // Из штатных таблиц читаются γ- и X-линии с оболочками (decay_radiations) и периоды
    // полураспада (nuclides); ряды распада СЧИТАЮТСЯ обходом decay_chain — их состав и
    // ветвление нигде не хранятся, чтобы не заводить вторую копию.
    //
    // Единственное, чего в базе не было и что пришлось в неё добавить: классификация по
    // семействам (families / nuclide_families), линии ХРИ материалов защиты и детектора
    // (xrf_elements / xrf_lines — это не излучение распада, в decay_radiations им места
    // нет), выбор разбираемых рядов (chains) и пояснения к каталогу (catalog_meta).
    public class NuclideCatalog
    {
        public string Generated { get; private set; }

        // Пороги, ниже которых линия в каталог не берётся, %
        public double GammaMinIntensity { get; private set; }
        public double XrayMinIntensity { get; private set; }

        // Чем задана классификация семейств — окно показывает это под пояснением кода
        public string FamilyStandard { get; private set; }
        public string FamilyStandardRu { get; private set; }

        public List<CatalogFamily> Families { get; private set; }
        public List<CatalogNuclide> Nuclides { get; private set; }
        public List<CatalogChain> Chains { get; private set; }
        public List<XrfElement> XrfElements { get; private set; }

        Dictionary<string, CatalogNuclide> byName;
        Dictionary<string, CatalogNuclide> byNucid;
        Dictionary<string, CatalogChain> byChainId;
        Dictionary<string, XrfElement> byElement;

        public NuclideCatalog()
        {
            this.Families = new List<CatalogFamily>();
            this.Nuclides = new List<CatalogNuclide>();
            this.Chains = new List<CatalogChain>();
            this.XrfElements = new List<XrfElement>();
            this.GammaMinIntensity = 0.05;
            this.XrayMinIntensity = 0.5;
        }

        static NuclideCatalog instance;
        static readonly object instanceLock = new object();

        // Каталог читается один раз на процесс: база неизменна, а разбор нескольких
        // десятков тысяч строк стоит заметно дороже, чем удержание их в памяти.
        // Блокировка нужна на случай, если каталог понадобится фоновой обработке
        // спектра: двойная загрузка дала бы два экземпляра и рассинхронизацию ссылок.
        public static NuclideCatalog GetInstance()
        {
            if (instance == null)
            {
                lock (instanceLock)
                {
                    if (instance == null)
                    {
                        instance = Load();
                    }
                }
            }
            return instance;
        }

        // Номер редакции каталога. Растёт при каждом сбросе кэша; по нему открытые
        // окна понимают, что их списки устарели, — сам по себе сброс singleton'а им
        // не поможет, они уже разложили каталог по своим контролам.
        static int version;

        public static int Version
        {
            get { return version; }
        }

        // Сбросить кэш: база изменилась. Зовётся из редактора семейств в NucBase —
        // каталог читается один раз на процесс, и без сброса правка классификации
        // была бы видна только после перезапуска.
        public static void Invalidate()
        {
            lock (instanceLock)
            {
                instance = null;
                version++;
            }
        }

        // Путь к базе берётся от сборки, а не от Environment.CurrentDirectory: рабочий
        // каталог процесса меняет любой файловый диалог, и после «Открыть спектр» из
        // другой папки база перестала бы находиться.
        public static string DatabasePath
        {
            get { return NucBase.DecayChains.DatabasePath; }
        }

        public static NuclideCatalog Load()
        {
            return Load(DatabasePath);
        }

        public static NuclideCatalog Load(string databasePath)
        {
            if (!File.Exists(databasePath))
            {
                throw new FileNotFoundException(
                    "Nuclide database not found: " + databasePath, databasePath);
            }

            NuclideCatalog catalog = new NuclideCatalog();
            catalog.Generated = File.GetLastWriteTime(databasePath)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string connectionString = "Data Source=" + databasePath + ";Mode=ReadOnly;Cache=Shared;";
            using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                catalog.ReadMeta(connection);
                catalog.ReadFamilies(connection);
                catalog.ReadNuclides(connection);
                catalog.ReadLines(connection);
                catalog.ReadXrf(connection);
                catalog.ReadChains(connection);
            }
            catalog.BuildIndex();
            return catalog;
        }

        // ─── чтение ─────────────────────────────────────────────────────────

        static SqliteCommand Command(SqliteConnection connection, string sql)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command;
        }

        void ReadMeta(SqliteConnection connection)
        {
            using (SqliteCommand command = Command(connection,
                "select key, value, value_ru from catalog_meta"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string key = reader.GetString(0);
                    string value = Text(reader, 1);
                    switch (key)
                    {
                        case "family_standard":
                            this.FamilyStandard = value;
                            this.FamilyStandardRu = Text(reader, 2);
                            break;
                        case "gamma_min_intensity":
                            this.GammaMinIntensity = ParseDouble(value, 0.05);
                            break;
                        case "xray_min_intensity":
                            this.XrayMinIntensity = ParseDouble(value, 0.5);
                            break;
                    }
                }
            }
        }

        void ReadFamilies(SqliteConnection connection)
        {
            using (SqliteCommand command = Command(connection,
                "select code, title, title_ru, info, info_ru from families order by sort_order"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    this.Families.Add(new CatalogFamily
                    {
                        Code = reader.GetString(0),
                        Title = Text(reader, 1),
                        TitleRu = Text(reader, 2),
                        Info = Text(reader, 3),
                        InfoRu = Text(reader, 4)
                    });
                }
            }
        }

        // Нуклиды каталога — те, у кого есть хоть одна линия, плюс все отнесённые к
        // семейству. Их около двух тысяч против 121 в прежнем снимке: поиск «кто ещё
        // светит рядом» идёт теперь по всей базе, а не по вручную отобранной сотне.
        //
        // Классифицированные берутся даже без линий: F-18, Bi-210 и Po-212/215/218 —
        // чистые α/β-излучатели, гамма-линий в базе у них нет, но из списка семейства они
        // пропадать не должны. Форма показывает их отдельным серым стилем «без линий».
        void ReadNuclides(SqliteConnection connection)
        {
            using (SqliteCommand command = Command(connection,
                "select n.nucid, n.half_life_sec from nuclides n " +
                "where n.half_life_sec is not null " +
                "  and (exists (select 1 from decay_radiations r " +
                "               where r.parent_nucid = n.nucid and r.type_a in ('G','X') " +
                "                 and r.intensity_num is not null and r.energy_num is not null) " +
                "       or exists (select 1 from nuclide_families f where f.nucid = n.nucid))"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string nucid = reader.GetString(0);
                    double seconds = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                    this.Nuclides.Add(new CatalogNuclide
                    {
                        Nucid = nucid,
                        Name = PrettyName(nucid),
                        HalfLifeSeconds = seconds,
                        HalfLifeYears = seconds > 0 ? seconds / SecondsPerYear : 0.0,
                        HalfLifeText = FormatHalfLife(seconds)
                    });
                }
            }

            this.byNucid = new Dictionary<string, CatalogNuclide>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogNuclide nuclide in this.Nuclides)
            {
                this.byNucid[nuclide.Nucid] = nuclide;
            }

            using (SqliteCommand command = Command(connection,
                "select nucid, code from nuclide_families"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    CatalogNuclide nuclide;
                    if (this.byNucid.TryGetValue(reader.GetString(0), out nuclide))
                    {
                        nuclide.AddFamily(reader.GetString(1));
                    }
                }
            }
        }

        // Линии привязываются к МИНИМАЛЬНОМУ parent_l_seqno этого родителя, а не к нулю:
        // у Pa-234m линия 1001.03 кэВ лежит при parent_l_seqno = 2, и жёсткий ноль терял
        // бы её целиком. Строки с уровнем выше минимального дублируют переход с
        // ветвлением возбуждённого уровня.
        void ReadLines(SqliteConnection connection)
        {
            // Минимальный уровень берётся соединением с одним группировочным проходом,
            // а не коррелированным подзапросом на каждую строку: последний превращал
            // выборку в 66 тыс. полных сканов таблицы.
            using (SqliteCommand command = Command(connection,
                "select r.parent_nucid, r.type_a, r.type_c, r.energy_num, r.intensity_num " +
                "from decay_radiations r " +
                "join (select parent_nucid, min(parent_l_seqno) as level " +
                "      from decay_radiations group by parent_nucid) m " +
                "  on m.parent_nucid = r.parent_nucid and m.level = r.parent_l_seqno " +
                "where r.type_a in ('G','X') " +
                "  and r.energy_num is not null and r.intensity_num is not null " +
                "  and r.intensity_num > 0 and r.energy_num > 0 " +
                "order by r.parent_nucid, r.energy_num"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    CatalogNuclide nuclide;
                    if (!this.byNucid.TryGetValue(reader.GetString(0), out nuclide))
                    {
                        continue;
                    }
                    double energy = reader.GetDouble(3);
                    double intensity = reader.GetDouble(4);
                    if (reader.GetString(1) == "G")
                    {
                        if (intensity < this.GammaMinIntensity)
                        {
                            continue;
                        }
                        nuclide.Gamma.Add(new CatalogGammaLine
                        {
                            Energy = energy,
                            Intensity = intensity
                        });
                    }
                    else
                    {
                        if (intensity < this.XrayMinIntensity)
                        {
                            continue;
                        }
                        string shell = Text(reader, 2);
                        nuclide.Xray.Add(new CatalogXrayLine
                        {
                            Energy = energy,
                            Intensity = intensity,
                            Shell = shell == null ? "" : shell.Trim()
                        });
                    }
                }
            }
        }

        void ReadXrf(SqliteConnection connection)
        {
            Dictionary<string, XrfElement> elements =
                new Dictionary<string, XrfElement>(StringComparer.OrdinalIgnoreCase);
            using (SqliteCommand command = Command(connection,
                "select symbol, z, context, context_ru from xrf_elements order by symbol"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    XrfElement element = new XrfElement
                    {
                        Symbol = reader.GetString(0),
                        Z = reader.GetInt32(1),
                        Context = Text(reader, 2),
                        ContextRu = Text(reader, 3)
                    };
                    elements[element.Symbol] = element;
                    this.XrfElements.Add(element);
                }
            }

            using (SqliteCommand command = Command(connection,
                "select symbol, label, energy_kev, intensity_rel from xrf_lines " +
                "order by symbol, energy_kev"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    XrfElement element;
                    if (elements.TryGetValue(reader.GetString(0), out element))
                    {
                        element.Lines.Add(new XrfLine
                        {
                            Label = reader.GetString(1),
                            Energy = reader.GetDouble(2),
                            Intensity = reader.GetDouble(3)
                        });
                    }
                }
            }
        }

        // ─── ряды распада ───────────────────────────────────────────────────

        void ReadChains(SqliteConnection connection)
        {
            List<CatalogChain> chains = new List<CatalogChain>();
            using (SqliteCommand command = Command(connection,
                "select id, root_nucid, title, title_ru from chains order by sort_order"))
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    chains.Add(new CatalogChain
                    {
                        Id = reader.GetString(0),
                        RootNucid = reader.GetString(1),
                        Root = PrettyName(reader.GetString(1)),
                        Title = Text(reader, 2),
                        TitleRu = Text(reader, 3)
                    });
                }
            }

            foreach (CatalogChain chain in chains)
            {
                this.FillChain(connection, chain);
                this.Chains.Add(chain);
            }

            // Ряд нуклида — самый длинный из тех, что его содержат: Pb-214 числится за
            // U-238, а не за подрядом Ra-226, иначе «добавить весь ряд» от свинца
            // обрывалось бы на радии. Подряд при этом остаётся доступен сам по себе.
            foreach (CatalogChain chain in this.Chains)
            {
                foreach (string member in chain.Members)
                {
                    CatalogNuclide nuclide = this.FindByNucid(this.ToNucid(member));
                    if (nuclide == null)
                    {
                        continue;
                    }
                    CatalogChain current = this.FindChainById(nuclide.Chain);
                    if (current == null || chain.Members.Count > current.Members.Count)
                    {
                        nuclide.Chain = chain.Id;
                    }
                }
            }
        }

        // Обход ряда от корня с накоплением ветвления. Следуются только переходы с
        // минимальным l_seqno на пару «родитель → дочерний»: строки с большим уровнем
        // описывают распад возбуждённого уровня и дублируют переход с другим процентом
        // (у Bi-212 наряду с настоящими 35.94 / 64.06 % приезжают 67 / 33 % с уровня 5).
        //
        // Накопленная доля — это интенсивность НА РАСПАД РОДИТЕЛЯ РЯДА. Ровно её ждёт
        // BR-связка LibraryPeakFitter: без множителя 0.3594 линии Tl-208 делят амплитуду
        // в бленде с линиями Bi-212 в неверной пропорции.
        void FillChain(SqliteConnection connection, CatalogChain chain)
        {
            Dictionary<string, double> fraction =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // Сам обход живёт в NucBase.DecayChains: тот же расчёт нужен импорту
            // определений из NucBase, и две копии одного обхода разъехались бы.
            // Возвращается порядок обхода — сверху вниз по ряду, он же порядок членов.
            List<string> order = NucBase.DecayChains.Fill(connection, chain.RootNucid, fraction);

            foreach (string nucid in order)
            {
                string name = PrettyName(nucid);
                chain.Members.Add(name);
                chain.Branching[name] = fraction[nucid];
            }
        }

        const double SecondsPerYear = 31536000.0;

        // ─── индекс и выборки ───────────────────────────────────────────────

        void BuildIndex()
        {
            this.Nuclides.Sort(delegate(CatalogNuclide a, CatalogNuclide b)
            {
                return string.CompareOrdinal(a.Name, b.Name);
            });
            this.byName = new Dictionary<string, CatalogNuclide>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogNuclide nuclide in this.Nuclides)
            {
                this.byName[nuclide.Name] = nuclide;
            }
            this.byChainId = new Dictionary<string, CatalogChain>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogChain chain in this.Chains)
            {
                this.byChainId[chain.Id] = chain;
            }
            this.byElement = new Dictionary<string, XrfElement>(StringComparer.OrdinalIgnoreCase);
            foreach (XrfElement element in this.XrfElements)
            {
                this.byElement[element.Symbol] = element;
            }
        }

        CatalogNuclide FindByNucid(string nucid)
        {
            CatalogNuclide result;
            if (nucid != null && this.byNucid != null && this.byNucid.TryGetValue(nucid, out result))
            {
                return result;
            }
            return null;
        }

        CatalogChain FindChainById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            foreach (CatalogChain chain in this.Chains)
            {
                if (string.Equals(chain.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return chain;
                }
            }
            return null;
        }

        public CatalogNuclide Find(string name)
        {
            CatalogNuclide result;
            if (name != null && this.byName != null && this.byName.TryGetValue(name, out result))
            {
                return result;
            }
            return null;
        }

        public CatalogChain FindChain(string id)
        {
            CatalogChain result;
            if (id != null && this.byChainId != null && this.byChainId.TryGetValue(id, out result))
            {
                return result;
            }
            return null;
        }

        public XrfElement FindElement(string symbol)
        {
            XrfElement result;
            if (symbol != null && this.byElement != null &&
                this.byElement.TryGetValue(symbol, out result))
            {
                return result;
            }
            return null;
        }

        // Корень ряда: по нему нуклид получает пометку «(Th-232)» в имени, а BecqMoni —
        // признак цепочки (ChainOf в LibraryPeakFitter читает последние скобки имени).
        public string ChainRoot(CatalogNuclide nuclide)
        {
            if (nuclide == null || string.IsNullOrEmpty(nuclide.Chain))
            {
                return null;
            }
            CatalogChain chain = this.FindChain(nuclide.Chain);
            return chain == null ? null : chain.Root;
        }

        // Множитель пересчёта интенсивности нуклида на распад родителя его ряда.
        // Заменяет прежнюю зашитую таблицу шести ветвлений: числа берутся оттуда же,
        // откуда линии, и не могут разойтись с базой.
        public double ChainBranchingOf(CatalogNuclide nuclide)
        {
            if (nuclide == null)
            {
                return 1.0;
            }
            CatalogChain chain = this.FindChain(nuclide.Chain);
            return chain == null ? 1.0 : chain.BranchingOf(nuclide.Name);
        }

        public CatalogFamily FindFamily(string code)
        {
            foreach (CatalogFamily family in this.Families)
            {
                if (string.Equals(family.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return family;
                }
            }
            return null;
        }

        public IEnumerable<CatalogNuclide> ByFamily(string family)
        {
            foreach (CatalogNuclide nuclide in this.Nuclides)
            {
                if (nuclide.HasFamily(family))
                {
                    yield return nuclide;
                }
            }
        }

        // ─── имена и форматирование ─────────────────────────────────────────

        // «208TL» → «Tl-208», «234PAm1» → «Pa-234m», «99TCm1» → «Tc-99m».
        // Номер изомера отбрасывается: в подписи он не нужен, а второго изомера с
        // гамма-линиями в базе не встречается.
        //
        // РЕГИСТР ЗНАЧАЩИЙ. Символ элемента в nucid записан заглавными, изомер — строчной
        // «m» (в базе 1077 таких записей, ни одной с заглавной M на конце). Искать
        // маркер изомера без учёта регистра нельзя: у Am, Cm, Fm, Pm, Sm и Tm вторая
        // буква символа съедалась бы, и Am-241 — первый нуклид любого калибровочного
        // набора — превращался в «A-241m».
        public static string PrettyName(string nucid)
        {
            if (string.IsNullOrEmpty(nucid))
            {
                return nucid;
            }
            int digits = 0;
            while (digits < nucid.Length && char.IsDigit(nucid[digits]))
            {
                digits++;
            }
            if (digits == 0 || digits >= nucid.Length)
            {
                return nucid;
            }
            string mass = nucid.Substring(0, digits);
            string tail = nucid.Substring(digits);
            string symbol = tail;
            string meta = "";
            int marker = tail.IndexOf('m');          // именно строчная
            if (marker > 0)
            {
                symbol = tail.Substring(0, marker);
                meta = "m";
            }
            symbol = symbol.Length == 1
                ? symbol.ToUpperInvariant()
                : char.ToUpperInvariant(symbol[0]) + symbol.Substring(1).ToLowerInvariant();
            return symbol + "-" + mass + meta;
        }

        // «Tl-208» → «208TL». Обратное преобразование неоднозначно по суффиксу изомера
        // (в базе встречаются и «234PAm1», и «108AGm»), поэтому варианты перебираются.
        string ToNucid(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            int dash = name.IndexOf('-');
            if (dash <= 0)
            {
                return name;
            }
            string symbol = name.Substring(0, dash).ToUpperInvariant();
            string mass = name.Substring(dash + 1);
            bool meta = mass.EndsWith("m", StringComparison.OrdinalIgnoreCase);
            if (meta)
            {
                mass = mass.Substring(0, mass.Length - 1);
            }
            string root = mass + symbol;
            if (!meta || this.byNucid == null)
            {
                return root;
            }
            foreach (string suffix in MetastableSuffixes)
            {
                if (this.byNucid.ContainsKey(root + suffix))
                {
                    return root + suffix;
                }
            }
            return root + "m1";
        }

        static readonly string[] MetastableSuffixes = { "m1", "m", "m2", "m3" };

        // Машинная запись «<число> <код единицы>»: подпись на языке интерфейса собирает
        // форма (HalfLifeLabel). Сам каталог одноязычен — это данные, а не текст.
        public static string FormatHalfLife(double seconds)
        {
            if (!(seconds > 0))
            {
                return "";
            }
            double value;
            string unit;
            if (seconds < 1e-6) { value = seconds * 1e9; unit = "ns"; }
            else if (seconds < 1e-3) { value = seconds * 1e6; unit = "us"; }
            else if (seconds < 1.0) { value = seconds * 1e3; unit = "ms"; }
            else if (seconds < 60.0) { value = seconds; unit = "s"; }
            else if (seconds < 3600.0) { value = seconds / 60.0; unit = "m"; }
            else if (seconds < 86400.0) { value = seconds / 3600.0; unit = "h"; }
            else if (seconds < SecondsPerYear) { value = seconds / 86400.0; unit = "d"; }
            else { value = seconds / SecondsPerYear; unit = "y"; }

            string text = value >= 1e4
                ? value.ToString("0.##e+00", CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);
            return text + " " + unit;
        }

        static string Text(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? null : reader.GetString(index);
        }

        static double ParseDouble(string text, double fallback)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }
    }

    // Семейство нуклидов: код, человеческое название и пояснение на обоих языках.
    // Классификация NORM/MED/IND/SNM — по ANSI N42.34; POPULAR/FISS/NAA/WASTE вне стандарта.
    public class CatalogFamily
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string TitleRu { get; set; }
        public string Info { get; set; }
        public string InfoRu { get; set; }
    }

    public class CatalogNuclide
    {
        // Ключ в nucdb.sqlite («208TL»); по нему идёт обход рядов
        public string Nucid { get; set; }

        // Подпись («Tl-208»)
        public string Name { get; set; }

        // Идентификатор ряда распада (u238, ra226, th232, u235) либо пусто
        public string Chain { get; set; }

        // Коды семейств через пробел: POPULAR NORM MED IND SNM FISS NAA WASTE
        public string Families { get; set; }

        public double HalfLifeSeconds { get; set; }
        public double HalfLifeYears { get; set; }

        // Машинная запись периода полураспада («3.053 m»); переводит её форма
        public string HalfLifeText { get; set; }

        public List<CatalogGammaLine> Gamma { get; set; }
        public List<CatalogXrayLine> Xray { get; set; }

        public CatalogNuclide()
        {
            this.Gamma = new List<CatalogGammaLine>();
            this.Xray = new List<CatalogXrayLine>();
        }

        public void AddFamily(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }
            code = code.ToUpperInvariant();
            this.Families = string.IsNullOrEmpty(this.Families) ? code : this.Families + " " + code;
        }

        public bool HasFamily(string family)
        {
            if (string.IsNullOrEmpty(this.Families) || string.IsNullOrEmpty(family))
            {
                return false;
            }
            foreach (string code in this.Families.Split(' '))
            {
                if (string.Equals(code, family, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public int LineCount
        {
            get { return this.Gamma.Count + this.Xray.Count; }
        }
    }

    public class CatalogGammaLine
    {
        public double Energy { get; set; }

        // На 100 распадов нуклида, %
        public double Intensity { get; set; }
    }

    public class CatalogXrayLine
    {
        public double Energy { get; set; }
        public double Intensity { get; set; }

        // Оболочка: KA1, KA2, KpB1, KB, L
        public string Shell { get; set; }
    }

    public class CatalogChain
    {
        public string Id { get; set; }
        public string RootNucid { get; set; }
        public string Root { get; set; }
        public string Title { get; set; }
        public string TitleRu { get; set; }

        // Порядок членов ряда сверху вниз
        public List<string> Members { get; set; }

        // Доля распадов ряда, проходящая через нуклид (вековое равновесие). Считается
        // обходом decay_chain, а не задаётся таблицей: у Bi-212 это 0.3594 на ветку
        // Tl-208 и 0.6406 на Po-212, и брать эти числа надо оттуда же, откуда линии.
        public Dictionary<string, double> Branching { get; set; }

        public CatalogChain()
        {
            this.Members = new List<string>();
            this.Branching = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        public double BranchingOf(string nuclideName)
        {
            double value;
            return this.Branching.TryGetValue(nuclideName ?? "", out value) ? value : 1.0;
        }
    }

    // Характеристический рентген материалов защиты и детектора: маркеры, не выходы.
    // Интенсивности условные, Kα1 = 100.
    public class XrfElement
    {
        public string Symbol { get; set; }
        public int Z { get; set; }
        public string Context { get; set; }

        // русское пояснение — форма показывает его при русской культуре интерфейса
        public string ContextRu { get; set; }

        public List<XrfLine> Lines { get; set; }

        public XrfElement()
        {
            this.Lines = new List<XrfLine>();
        }
    }

    public class XrfLine
    {
        public string Label { get; set; }
        public double Energy { get; set; }
        public double Intensity { get; set; }
    }
}
