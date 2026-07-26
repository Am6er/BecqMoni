using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;

namespace BecquerelMonitor.RoiWizard
{
    // Снимок ядерных данных IAEA Live Chart (ENSDF), встроенный в сборку ресурсом.
    // API IAEA не отдаёт CORS и требует сети, поэтому данные лежат снимком; обновление —
    // скриптом tools/export_catalog.py, он же проставляет Generated и пороги.
    [XmlRoot("NuclideCatalog")]
    public class NuclideCatalog
    {
        [XmlAttribute]
        public string Generated { get; set; }

        // Пороги, ниже которых линии в снимок не попали, %
        [XmlAttribute]
        public double GammaMinIntensity { get; set; }

        [XmlAttribute]
        public double XrayMinIntensity { get; set; }

        // Чем задана классификация семейств — окно показывает это под пояснением кода
        [XmlAttribute]
        public string FamilyStandard { get; set; }

        [XmlAttribute]
        public string FamilyStandardRu { get; set; }

        [XmlArray("Families"), XmlArrayItem("Family")]
        public List<CatalogFamily> Families { get; set; }

        [XmlArray("Nuclides"), XmlArrayItem("Nuclide")]
        public List<CatalogNuclide> Nuclides { get; set; }

        [XmlArray("Chains"), XmlArrayItem("Chain")]
        public List<CatalogChain> Chains { get; set; }

        [XmlArray("XrfElements"), XmlArrayItem("Element")]
        public List<XrfElement> XrfElements { get; set; }

        Dictionary<string, CatalogNuclide> byName;
        Dictionary<string, CatalogChain> byChainId;
        Dictionary<string, XrfElement> byElement;

        public NuclideCatalog()
        {
            this.Nuclides = new List<CatalogNuclide>();
            this.Chains = new List<CatalogChain>();
            this.XrfElements = new List<XrfElement>();
            this.Families = new List<CatalogFamily>();
        }

        const string ResourceName = "BecquerelMonitor.RoiWizard.nuclides.xml";

        static NuclideCatalog instance;
        static readonly object instanceLock = new object();

        // Каталог читается один раз на процесс: снимок неизменен, а разбор XML на 121 нуклид
        // стоит заметно дороже, чем удержание его в памяти. Блокировка нужна не ради UI-потока,
        // а на случай, если каталог когда-нибудь понадобится фоновой обработке спектра:
        // двойная загрузка дала бы два разных экземпляра и рассинхронизацию ссылок.
        public static NuclideCatalog GetInstance()
        {
            if (instance == null)
            {
                lock (instanceLock)
                {
                    if (instance == null)
                    {
                        instance = LoadEmbedded();
                    }
                }
            }
            return instance;
        }

        public static NuclideCatalog LoadEmbedded()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException(
                        "Resource " + ResourceName + " not found: add RoiWizard\\nuclides.xml " +
                        "to the project as an EmbeddedResource.");
                }
                return Load(stream);
            }
        }

        public static NuclideCatalog Load(Stream stream)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(NuclideCatalog));
            NuclideCatalog catalog = (NuclideCatalog)serializer.Deserialize(stream);
            catalog.BuildIndex();
            return catalog;
        }

        void BuildIndex()
        {
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
            if (symbol != null && this.byElement != null && this.byElement.TryGetValue(symbol, out result))
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
    }

    // Семейство нуклидов: код, человеческое название и пояснение на обоих языках.
    // Классификация NORM/MED/IND/SNM — по ANSI N42.34; FISS/NAA/WASTE вне стандарта.
    public class CatalogFamily
    {
        [XmlAttribute]
        public string Code { get; set; }

        [XmlAttribute]
        public string Title { get; set; }

        [XmlAttribute]
        public string TitleRu { get; set; }

        [XmlAttribute]
        public string Info { get; set; }

        [XmlAttribute]
        public string InfoRu { get; set; }
    }

    public class CatalogNuclide
    {
        [XmlAttribute]
        public string Name { get; set; }

        // Идентификатор ряда распада (u238, th232, u235) либо пусто
        [XmlAttribute]
        public string Chain { get; set; }

        // Коды семейств через пробел: NORM MED IND SNM FISS NAA WASTE POPULAR.
        // Классификация — ANSI N42.34 (идентификаторы RIID); FISS/NAA/WASTE вне стандарта.
        [XmlAttribute]
        public string Families { get; set; }

        [XmlAttribute]
        public double HalfLifeSeconds { get; set; }

        [XmlAttribute]
        public double HalfLifeYears { get; set; }

        // Готовая подпись периода полураспада («3,05 мин»), чтобы не форматировать заново
        [XmlAttribute]
        public string HalfLifeText { get; set; }

        [XmlArray("Gamma"), XmlArrayItem("Line")]
        public List<CatalogGammaLine> Gamma { get; set; }

        [XmlArray("Xray"), XmlArrayItem("Line")]
        public List<CatalogXrayLine> Xray { get; set; }

        public CatalogNuclide()
        {
            this.Gamma = new List<CatalogGammaLine>();
            this.Xray = new List<CatalogXrayLine>();
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
        [XmlAttribute("E")]
        public double Energy { get; set; }

        // На 100 распадов нуклида, %
        [XmlAttribute("I")]
        public double Intensity { get; set; }
    }

    public class CatalogXrayLine
    {
        [XmlAttribute("E")]
        public double Energy { get; set; }

        [XmlAttribute("I")]
        public double Intensity { get; set; }

        // Оболочка: KA1, KA2, KpB1, KB, L
        [XmlAttribute("Shell")]
        public string Shell { get; set; }
    }

    public class CatalogChain
    {
        [XmlAttribute]
        public string Id { get; set; }

        [XmlAttribute]
        public string Root { get; set; }

        [XmlAttribute]
        public string Title { get; set; }

        // Порядок членов ряда сверху вниз
        [XmlArray("Members"), XmlArrayItem("Member")]
        public List<string> Members { get; set; }

        public CatalogChain()
        {
            this.Members = new List<string>();
        }
    }

    // Характеристический рентген материалов защиты и детектора: маркеры, не выходы.
    // Интенсивности условные, Kα1 = 100.
    public class XrfElement
    {
        [XmlAttribute]
        public string Symbol { get; set; }

        [XmlAttribute]
        public int Z { get; set; }

        [XmlAttribute]
        public string Context { get; set; }

        // русское пояснение — форма показывает его при русской культуре интерфейса
        [XmlAttribute]
        public string ContextRu { get; set; }

        [XmlArray("Lines"), XmlArrayItem("Line")]
        public List<XrfLine> Lines { get; set; }

        public XrfElement()
        {
            this.Lines = new List<XrfLine>();
        }
    }

    public class XrfLine
    {
        [XmlAttribute("Label")]
        public string Label { get; set; }

        [XmlAttribute("E")]
        public double Energy { get; set; }

        [XmlAttribute("I")]
        public double Intensity { get; set; }
    }
}
