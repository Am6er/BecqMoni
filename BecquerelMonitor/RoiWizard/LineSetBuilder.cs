using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.RoiWizard
{
    // Как нуклид попадает в набор. Скобки в имени — не украшение: BecqMoni читает по ним
    // цепочку (ChainOf в LibraryPeakFitter) и связывает амплитуды линий ряда.
    public enum AddMode
    {
        // только собственные линии нуклида
        Single,
        // линии дочерних идут под именем родителя, одной записью
        FamilyLines,
        // дочерние — отдельными нуклидами, родитель в скобках
        Chain
    }

    public class SourceSelection
    {
        // имя нуклида -> подпись, под которой его линии попадут в набор
        public Dictionary<string, string> Nuclides { get; private set; }

        // символы элементов, чей характеристический рентген добавлен как маркеры
        public HashSet<string> XrfElements { get; private set; }

        public SourceSelection()
        {
            this.Nuclides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.XrfElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Add(NuclideCatalog catalog, string name, AddMode mode)
        {
            CatalogNuclide nuclide = catalog.Find(name);
            if (nuclide == null)
            {
                return;
            }
            switch (mode)
            {
                case AddMode.Single:
                    this.Nuclides[name] = name;
                    break;

                case AddMode.FamilyLines:
                    // все члены ряда под именем выбранного нуклида: в наборе он один,
                    // а линии дочерних приписаны ему
                    this.Nuclides[name] = name;
                    foreach (string member in MembersBelow(catalog, nuclide))
                    {
                        this.Nuclides[member] = name;
                    }
                    break;

                case AddMode.Chain:
                    // корень цепочки идёт без скобок — иначе он оказался бы сам себе родителем
                    this.Nuclides[name] = name;
                    foreach (string member in MembersBelow(catalog, nuclide))
                    {
                        this.Nuclides[member] = member + " (" + name + ")";
                    }
                    break;
            }
        }

        // Добавление в составе группы: у члена ЕРН-ряда подпись обязана нести КОРЕНЬ ряда,
        // а не имя самого нуклида. Иначе набор, собранный кнопкой «добавить все» по ряду
        // или по семейству, распадается на два десятка одиночных «цепочек», и связка
        // амплитуд в LibraryPeakFitter не собирается ни по одной.
        //
        // Отдельный метод, а не режим Single, потому что одиночно добавленный нуклид —
        // это другой случай: у него родителя в наборе нет, подпись остаётся своей,
        // и равновесный пересчёт к нему не применяется.
        public void AddGroupMember(NuclideCatalog catalog, string name)
        {
            CatalogNuclide nuclide = catalog.Find(name);
            if (nuclide == null)
            {
                return;
            }
            string root = catalog.ChainRoot(nuclide);
            this.Nuclides[name] = string.IsNullOrEmpty(root) || string.Equals(root, name, StringComparison.Ordinal)
                ? name
                : name + " (" + root + ")";
        }

        public void Remove(string name)
        {
            this.Nuclides.Remove(name);
        }

        public void Clear()
        {
            this.Nuclides.Clear();
            this.XrfElements.Clear();
        }

        static IEnumerable<string> MembersBelow(NuclideCatalog catalog, CatalogNuclide parent)
        {
            CatalogChain chain = catalog.FindChain(parent.Chain);
            if (chain == null)
            {
                yield break;
            }
            int start = chain.Members.IndexOf(parent.Name);
            if (start < 0)
            {
                yield break;
            }
            for (int i = start + 1; i < chain.Members.Count; i++)
            {
                yield return chain.Members[i];
            }
        }
    }

    // Фильтры отбирают линии в набор; видимостью в таблице они не управляют.
    // Разделение выстрадано: иначе пользователь снимает галку типа, а из набора
    // молча пропадают линии.
    public class LineFilter
    {
        public bool IntensityOn { get; set; }
        public double MinIntensity { get; set; }
        // относительная — в процентах от сильнейшей линии этого же нуклида
        public bool RelativeIntensity { get; set; }

        public bool EnergyOn { get; set; }
        public double MinEnergy { get; set; }
        public double MaxEnergy { get; set; }

        public bool HalfLifeOn { get; set; }
        public double MinHalfLifeYears { get; set; }
        public double MaxHalfLifeYears { get; set; }

        public LineFilter()
        {
            this.MinIntensity = 3.0;
            this.RelativeIntensity = true;
            this.MinEnergy = 10.0;
            this.MaxEnergy = 3000.0;
            this.MaxHalfLifeYears = double.PositiveInfinity;
        }

        public bool Passes(SpectralLine line, double strongestOfNuclide)
        {
            if (this.IntensityOn)
            {
                double value = this.RelativeIntensity && strongestOfNuclide > 0
                    ? 100.0 * line.Intensity / strongestOfNuclide
                    : line.Intensity;
                if (value < this.MinIntensity)
                {
                    return false;
                }
            }
            if (this.EnergyOn && (line.Energy < this.MinEnergy || line.Energy > this.MaxEnergy))
            {
                return false;
            }
            if (this.HalfLifeOn && (line.HalfLifeYears < this.MinHalfLifeYears ||
                                    line.HalfLifeYears > this.MaxHalfLifeYears))
            {
                return false;
            }
            return true;
        }
    }

    public class LineSetBuilder
    {
        readonly NuclideCatalog catalog;

        public LineSetBuilder(NuclideCatalog catalog)
        {
            this.catalog = catalog;
        }

        // Пересчёт интенсивностей на один распад родителя ряда. Держать включённым для
        // рядов: веса связанных линий BecqMoni берёт из Intencity, и без пересчёта
        // относительные высоты линий разных членов ряда несопоставимы.
        public bool ScaleToSeriesParent { get; set; }

        public LineSetBuilder Reset()
        {
            this.ScaleToSeriesParent = true;
            return this;
        }

        public List<SpectralLine> Build(SourceSelection selection, LineFilter filter)
        {
            List<SpectralLine> lines = new List<SpectralLine>();
            foreach (KeyValuePair<string, string> entry in selection.Nuclides)
            {
                CatalogNuclide nuclide = this.catalog.Find(entry.Key);
                if (nuclide == null)
                {
                    continue;
                }
                string label = entry.Value;
                // Пересчёт на распад родителя применим только к нуклиду, взятому в составе
                // ряда: у одиночно добавленного Tl-208 родителя в наборе нет, и множитель
                // 0.3594 превратил бы 99.75 % его линии 2614 кэВ в 35.85 % без всяких на то
                // оснований. Признак «взят в составе ряда» — подпись отличается от имени:
                // при Chain это «Tl-208 (Th-232)», при FamilyLines — имя родителя.
                bool partOfSeries = !string.Equals(label, nuclide.Name, StringComparison.Ordinal);
                double equilibrium = this.ScaleToSeriesParent && partOfSeries
                    ? EquilibriumFactors.For(nuclide.Name)
                    : 1.0;

                foreach (CatalogGammaLine gamma in nuclide.Gamma)
                {
                    lines.Add(new SpectralLine
                    {
                        Key = "g|" + nuclide.Name + "|" + Fmt(gamma.Energy),
                        Nuclide = nuclide.Name,
                        Label = label,
                        OwnerLabel = label,
                        Energy = gamma.Energy,
                        Intensity = Math.Round(gamma.Intensity * equilibrium, 4),
                        RawIntensity = gamma.Intensity,
                        Type = LineType.Gamma,
                        HalfLifeYears = nuclide.HalfLifeYears > 0 ? nuclide.HalfLifeYears : 1e9,
                        HalfLifeText = nuclide.HalfLifeText
                    });
                }
                foreach (CatalogXrayLine xray in nuclide.Xray)
                {
                    lines.Add(new SpectralLine
                    {
                        Key = "x|" + nuclide.Name + "|" + Fmt(xray.Energy) + "|" + xray.Shell,
                        Nuclide = nuclide.Name,
                        Label = WithSuffix(label, "X " + xray.Shell),
                        OwnerLabel = label,
                        Energy = xray.Energy,
                        Intensity = Math.Round(xray.Intensity * equilibrium, 4),
                        RawIntensity = xray.Intensity,
                        Type = LineType.Xray,
                        HalfLifeYears = nuclide.HalfLifeYears > 0 ? nuclide.HalfLifeYears : 1e9,
                        HalfLifeText = nuclide.HalfLifeText
                    });
                }
            }

            foreach (string symbol in selection.XrfElements)
            {
                XrfElement element = this.catalog.FindElement(symbol);
                if (element == null)
                {
                    continue;
                }
                foreach (XrfLine line in element.Lines)
                {
                    lines.Add(new SpectralLine
                    {
                        Key = "xrf|" + symbol + "|" + Fmt(line.Energy),
                        Nuclide = "XRF " + symbol,
                        Label = "XRF " + symbol + " " + line.Label,
                        Energy = line.Energy,
                        // интенсивности ХРИ условные (Ka1 = 100) — это маркеры, не выходы,
                        // поэтому равновесие ряда к ним не применяется
                        Intensity = line.Intensity,
                        RawIntensity = line.Intensity,
                        Type = LineType.Xrf,
                        HalfLifeYears = 1e9,
                        HalfLifeText = "—"
                    });
                }
            }

            ApplySelection(lines, filter);
            return lines;
        }

        // Профиль «полный набор» для библиотечного фита: все γ- и X-линии выбранных
        // источников, независимо от галок, фильтров и слияния.
        //
        // Прореживание набора вредит: по прогонам разработчика BecqMoni (RjmcmcTuning,
        // итерация 9) полная цепочка ряда даёт recall 26/29 для Th-232 и 27/27 для Ra-226,
        // а «набор пользователя из 16 сильных линий» не добавляет к слепому поиску ничего.
        // Слияние тоже не применяется: пары от 0.25 до 0.85 FWHM разбирает сам библиотечный
        // фит по якорю, и склеивать их — значит отнимать у него линии.
        //
        // Вторичных пиков здесь нет и быть не должно: escape- и сумм-компоненты
        // LibraryPeakFitter строит сам, а имя вида «SE (Bi-214)» создало бы ложную цепочку.
        public List<SpectralLine> BuildFullSet(SourceSelection selection)
        {
            List<SpectralLine> lines = this.Build(selection, null);
            foreach (SpectralLine line in lines)
            {
                line.Selected = true;
            }
            return lines;
        }

        // Фильтр решает, что выбрано, а не что видно
        static void ApplySelection(List<SpectralLine> lines, LineFilter filter)
        {
            if (filter == null)
            {
                return;
            }
            Dictionary<string, double> strongest = new Dictionary<string, double>();
            foreach (SpectralLine line in lines)
            {
                double current;
                if (!strongest.TryGetValue(line.Nuclide, out current) || line.Intensity > current)
                {
                    strongest[line.Nuclide] = line.Intensity;
                }
            }
            foreach (SpectralLine line in lines)
            {
                double max;
                strongest.TryGetValue(line.Nuclide, out max);
                line.Selected = filter.Passes(line, max);
            }
        }

        // Оставить только N самых сильных линий каждого нуклида
        public static void SelectTopPerNuclide(List<SpectralLine> lines, int count)
        {
            Dictionary<string, List<SpectralLine>> byNuclide = new Dictionary<string, List<SpectralLine>>();
            foreach (SpectralLine line in lines)
            {
                List<SpectralLine> list;
                if (!byNuclide.TryGetValue(line.Nuclide, out list))
                {
                    list = new List<SpectralLine>();
                    byNuclide[line.Nuclide] = list;
                }
                list.Add(line);
            }
            foreach (List<SpectralLine> list in byNuclide.Values)
            {
                list.Sort(delegate(SpectralLine a, SpectralLine b) { return b.Intensity.CompareTo(a.Intensity); });
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Selected = i < count;
                }
            }
        }

        // ChainOf в BecqMoni читает текст в ПОСЛЕДНИХ скобках имени как имя родительской
        // цепочки. Суффикс рентгеновской линии («X KA1») шёл после скобок и разрывал признак
        // цепочки — «Tl-208 (Th-232) X KA1» вместо «Tl-208 X KA1 (Th-232)», поэтому суффикс
        // вставляется перед скобками родителя.
        internal static string WithSuffix(string label, string suffix)
        {
            if (string.IsNullOrEmpty(label))
            {
                return suffix;
            }
            int close = label.LastIndexOf(')');
            int open = close > 0 ? label.LastIndexOf('(', close - 1) : -1;
            if (close == label.Length - 1 && open > 0)
            {
                return label.Substring(0, open).TrimEnd() + " " + suffix +
                       " (" + label.Substring(open + 1, close - open - 1) + ")";
            }
            return label + " " + suffix;
        }

        static string Fmt(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
