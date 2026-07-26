using System;
using System.Collections.Generic;

namespace BecquerelMonitor.RoiWizard
{
    public enum LineType
    {
        // γ-линия распада
        Gamma,
        // рентген, сопровождающий распад (K/L-серия дочернего атома)
        Xray,
        // характеристический рентген материала защиты или детектора
        Xrf,
        // расчётный вторичный пик: обратное рассеяние, вылет, суммирование
        Secondary
    }

    // Строка рабочего набора. Ключ нужен, чтобы выбор пользователя переживал
    // пересборку набора (смена фильтров, равновесия ряда, слияния).
    public class SpectralLine
    {
        public string Key { get; set; }

        // Имя нуклида в каталоге либо «XRF Pb» для флуоресценции
        public string Nuclide { get; set; }

        // Подпись: «Ac-228 (Th-232)», «Tl-208 (Th-232) X KA1», «XRF Pb Ka1 74.97».
        // Родитель в скобках — признак цепочки для BecqMoni, не украшение.
        public string Label { get; set; }

        // Подпись нуклида без суффикса линии — то, чем линия числится в наборе.
        // В режиме «линии семейства» линия Ra-228 идёт под именем Th-232, и цепочку
        // надо брать отсюда, а не из Nuclide: иначе набор рассыпется по членам ряда.
        public string OwnerLabel { get; set; }

        public double Energy { get; set; }

        // Интенсивность, % (с учётом равновесия ряда, если оно включено)
        public double Intensity { get; set; }

        // Табличная интенсивность до пересчёта на распад родителя
        public double RawIntensity { get; set; }

        public LineType Type { get; set; }

        public double HalfLifeYears { get; set; }

        public string HalfLifeText { get; set; }

        public bool Selected { get; set; }

        // Линия — результат слияния группы; Interval хранит края группы
        public bool Merged { get; set; }

        public string Interval { get; set; }

        public SpectralLine()
        {
            this.HalfLifeYears = 1e9;
            this.Selected = true;
        }

        // Имя записи для библиотеки нуклидов.
        // ChainOf (LibraryPeakFitter) читает текст в ПОСЛЕДНИХ скобках имени как имя
        // родительской цепочки. У слитой линии подпись кончалась бы интервалом энергий —
        // «Ac-228 (Th-232) (964.8–969.0)» — и цепочка читалась бы как «964.8–969.0»,
        // то есть связка амплитуд по ряду не собралась бы. Поэтому интервал выносится
        // из скобок, а скобки остаются родителю.
        public string LibraryName
        {
            get
            {
                string name = this.Label;
                if (this.Merged && !string.IsNullOrEmpty(this.Interval))
                {
                    string tail = " (" + this.Interval + ")";
                    string baseName = name.EndsWith(tail, StringComparison.Ordinal)
                        ? name.Substring(0, name.Length - tail.Length)
                        : name;
                    int close = baseName.LastIndexOf(')');
                    int open = close > 0 ? baseName.LastIndexOf('(', close - 1) : -1;
                    if (close == baseName.Length - 1 && open > 0)
                    {
                        string head = baseName.Substring(0, open).TrimEnd();
                        string parent = baseName.Substring(open + 1, close - open - 1);
                        name = head + " " + this.Interval + " (" + parent + ")";
                    }
                    else
                    {
                        name = baseName + " " + this.Interval;
                    }
                }
                // ChainOf берёт цепочку из последних скобок, а без них — имя целиком.
                // Поэтому «U-238 X L» становится собственной цепочкой и не связывается
                // с «U-238»: линии с суффиксом у корня ряда и у одиночного нуклида
                // выпадали из связки. Дописываем цепочку явно; у ХРИ материалов и
                // вторичных маркеров связывать нечего, их не трогаем.
                string owner = string.IsNullOrEmpty(this.OwnerLabel) ? this.Nuclide : this.OwnerLabel;
                if (this.Type != LineType.Xrf && this.Type != LineType.Secondary &&
                    !EndsWithBrackets(name) && !string.Equals(name, owner, StringComparison.Ordinal))
                {
                    name += " (" + owner + ")";
                }
                return name;
            }
        }

        static bool EndsWithBrackets(string name)
        {
            if (string.IsNullOrEmpty(name) || name[name.Length - 1] != ')')
            {
                return false;
            }
            return name.LastIndexOf('(') >= 0;
        }

        public SpectralLine Clone()
        {
            return (SpectralLine)this.MemberwiseClone();
        }
    }

    // Модель уширения пика для сцинтилляторов: FWHM(E) = R·√(662·E)/100.
    // Для HPGe (R ~0.15 %) она занижает ширину на низких энергиях — там нужен
    // закон √(a+bE), здесь не реализован.
    public class ResolutionModel
    {
        readonly double resolutionAt662;

        public ResolutionModel(double resolutionAt662Percent)
        {
            this.resolutionAt662 = resolutionAt662Percent > 0 ? resolutionAt662Percent : 7.5;
        }

        public double ResolutionAt662
        {
            get { return this.resolutionAt662; }
        }

        public double Fwhm(double energy)
        {
            if (energy <= 0)
            {
                return 0;
            }
            return this.resolutionAt662 / 100.0 * Math.Sqrt(662.0 * energy);
        }
    }

    // Критерий, ближе которого линии считаются нераздельными.
    // Числа взяты из BecquerelMonitor/LibraryPeakFitter.cs, не подобраны на глаз.
    public enum MergeCriterion
    {
        // SparrowFwhm = 0.85 (δ = 2σ): физический предел разрешимости двух пиков.
        // Ближе этого дублет виден как один пик — для ROI-маркеров сливаем.
        Sparrow,
        // ClaimToleranceFwhm = 0.25: для наборов, уходящих в библиотеку. Пары от 0.25
        // до 0.85 FWHM разбирает библиотечный фит по якорной линии, более далёкие —
        // деконволюция, поэтому сливать их нельзя: набор обедняется.
        AnchoredSet,
        // Прежнее поведение NuclideMaster: 2–3×FWHM, грубое окно идентификации.
        Manual
    }

    public static class MergeCriterionInfo
    {
        public const double SparrowFwhm = 0.85;
        public const double ClaimToleranceFwhm = 0.25;

        public static double DefaultFactor(MergeCriterion criterion)
        {
            switch (criterion)
            {
                case MergeCriterion.Sparrow:
                    return SparrowFwhm;
                case MergeCriterion.AnchoredSet:
                    return ClaimToleranceFwhm;
                default:
                    return 3.0;
            }
        }

        // Для якорного режима осмысленный разброс — 0.2…0.3 (оценка разработчика
        // BecqMoni; ClaimToleranceFwhm = 0.25 попадает в середину).
        public static bool IsFactorSane(MergeCriterion criterion, double factor)
        {
            switch (criterion)
            {
                case MergeCriterion.Sparrow:
                    return Math.Abs(factor - SparrowFwhm) < 1e-9;
                case MergeCriterion.AnchoredSet:
                    return factor >= 0.2 && factor <= 0.3;
                default:
                    return factor > 0;
            }
        }
    }

    // Вековое равновесие ряда: доля распадов ряда, проходящая через нуклид.
    // Bi-212: β 64.06 % (→Po-212) / α 35.94 % (→Tl-208);
    // Ac-227: β 98.62 % (→Th-227) / α 1.38 % (→Fr-223);
    // Bi-211: α 99.724 % (→Tl-207) / β 0.276 % (→Po-211).
    public static class EquilibriumFactors
    {
        static readonly Dictionary<string, double> factors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Tl-208", 0.3594 },
            { "Po-212", 0.6406 },
            { "Fr-223", 0.0138 },
            { "Th-227", 0.9862 },
            { "Tl-207", 0.99724 },
            { "Po-211", 0.00276 }
        };

        // Без пересчёта I — «на 100 распадов самого излучающего нуклида», как в базе IAEA
        // и в файлах Intensities BecqMoni: у Tl-208 линия 2614 кэВ имеет I = 99.75 %.
        // В равновесном ряду Th-232 через Tl-208 идёт лишь 35.94 % распадов, поэтому
        // на один распад Th-232 эта линия даёт 99.75 × 0.3594 ≈ 35.85 %.
        public static double For(string nuclide)
        {
            double factor;
            return factors.TryGetValue(nuclide ?? "", out factor) ? factor : 1.0;
        }
    }
}
