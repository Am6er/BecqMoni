using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.RoiWizard
{
    public enum IssueLevel
    {
        Warning,
        Error
    }

    // Что именно не так. Ядро не собирает фразу: текст зависит от языка интерфейса,
    // а язык — дело формы. Здесь только код замечания и подстановки к нему.
    public enum IssueKind
    {
        EqualEnergies,
        ZeroYield,
        AnchorIsXrf,
        AnchorIsSecondary,
        NoAnchor,
        AnchorIsXray,
        ZonesOverlap,
        MixedChains
    }

    public class SetIssue
    {
        public IssueLevel Level { get; set; }
        public IssueKind Kind { get; set; }
        public object[] Args { get; set; }

        public SetIssue()
        {
            this.Args = new object[0];
        }
    }

    // Проверки перед сохранением. Для ROI всё совещательное, для набора совпавшие энергии
    // и нулевая интенсивность — ошибки: две линии на одной позиции вырождают подгонку
    // амплитуд (два параметра на один пик), а Intencity = 0 выбрасывает линию из связки
    // по цепочке.
    public static class SetChecker
    {
        static bool HasGamma(List<SpectralLine> lines)
        {
            foreach (SpectralLine line in lines)
            {
                if (line.Type == LineType.Gamma)
                {
                    return true;
                }
            }
            return false;
        }

        public static List<SetIssue> Check(IEnumerable<SpectralLine> lines, bool forLibrary,
                                           ZoneCalculator zones)
        {
            return Check(lines, forLibrary, zones, null);
        }

        // resolution нужен, чтобы проверить якорь ровно так же, как его выберет
        // BuildNuclideSet; без модели разрешения проверка якоря пропускается
        public static List<SetIssue> Check(IEnumerable<SpectralLine> lines, bool forLibrary,
                                           ZoneCalculator zones, ResolutionModel resolution)
        {
            // приведение обязательно: без него null подходит обеим перегрузкам (CS0121)
            return Check(lines, forLibrary, zones, resolution, (IList<SpectralLine>)null);
        }

        // anchorOverride — якорь, выбранный руками. Проверять надо именно его: в набор
        // уйдёт он, а не тот, что предложил бы AnchorPicker.
        public static List<SetIssue> Check(IEnumerable<SpectralLine> lines, bool forLibrary,
                                           ZoneCalculator zones, ResolutionModel resolution,
                                           SpectralLine anchorOverride)
        {
            List<SpectralLine> anchors = null;
            if (anchorOverride != null)
            {
                anchors = new List<SpectralLine>();
                anchors.Add(anchorOverride);
            }
            return Check(lines, forLibrary, zones, resolution, anchors);
        }

        // Якорей может быть несколько: LibraryPeakFitter перебирает все записи с IsAnchor
        // и требует совпадения с найденным пиком хотя бы одной из них.
        public static List<SetIssue> Check(IEnumerable<SpectralLine> lines, bool forLibrary,
                                           ZoneCalculator zones, ResolutionModel resolution,
                                           IList<SpectralLine> anchorOverride)
        {
            List<SetIssue> issues = new List<SetIssue>();
            List<SpectralLine> sorted = new List<SpectralLine>();
            foreach (SpectralLine line in lines)
            {
                if (line.Selected)
                {
                    sorted.Add(line);
                }
            }
            sorted.Sort(delegate(SpectralLine a, SpectralLine b) { return a.Energy.CompareTo(b.Energy); });

            IssueLevel level = forLibrary ? IssueLevel.Error : IssueLevel.Warning;
            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].Energy - sorted[i - 1].Energy) < DegenerateGap(sorted[i], resolution))
                {
                    // Совпавшие энергии подгонку вырождают, но запрещать из-за них экспорт
                    // нельзя: пара «рентген распада + ХРИ того же элемента» — физически одна
                    // линия (K-серия свинца у Tl-208 и защиты), и снимать её или оставить —
                    // решение оператора. Поэтому предупреждение, а не ошибка.
                    issues.Add(new SetIssue
                    {
                        Level = IssueLevel.Warning,
                        Kind = IssueKind.EqualEnergies,
                        Args = new object[] {
                            Name(sorted[i - 1], forLibrary), Name(sorted[i], forLibrary),
                            sorted[i - 1].Energy, sorted[i].Energy }
                    });
                }
            }
            foreach (SpectralLine line in sorted)
            {
                // Аппаратные записи (ХРИ защиты и расчётные вторичные) выхода на распад
                // не имеют по определению, и в набор они уходят с Intencity = 0 намеренно
                // — см. SetExporter.BuildNuclideSet. Требовать от них ненулевой
                // интенсивности значило бы запрещать единственное, ради чего они нужны:
                // занять место, куда иначе сядет фантомная линия нуклида.
                if (line.Type == LineType.Xrf || line.Type == LineType.Secondary)
                {
                    continue;
                }
                if (!(line.Intensity > 0))
                {
                    issues.Add(new SetIssue
                    {
                        Level = level,
                        Kind = IssueKind.ZeroYield,
                        Args = new object[] { Name(line, forLibrary), line.Energy }
                    });
                }
            }
            if (forLibrary && (resolution != null || (anchorOverride != null && anchorOverride.Count > 0)))
            {
                List<SpectralLine> anchors = anchorOverride != null && anchorOverride.Count > 0
                    ? new List<SpectralLine>(anchorOverride)
                    : AnchorPicker.PickMany(sorted, resolution, AnchorPicker.DefaultCount);

                bool manual = anchorOverride != null && anchorOverride.Count > 0;
                bool rejected = false;
                if (manual)
                {
                    foreach (SpectralLine chosen in anchors)
                    {
                        if (AnchorPicker.IsAcceptable(chosen))
                        {
                            continue;
                        }
                        rejected = true;
                        issues.Add(new SetIssue
                        {
                            Level = IssueLevel.Error,
                            Kind = chosen.Type == LineType.Xrf
                                ? IssueKind.AnchorIsXrf
                                : IssueKind.AnchorIsSecondary,
                            Args = new object[] { chosen.LibraryName, chosen.Energy }
                        });
                    }
                }
                if (!rejected && anchors.Count == 0)
                {
                    issues.Add(new SetIssue
                    {
                        Level = IssueLevel.Error,
                        Kind = IssueKind.NoAnchor
                    });
                }
                else if (!rejected && !HasGamma(anchors))
                {
                    issues.Add(new SetIssue
                    {
                        Level = IssueLevel.Warning,
                        Kind = IssueKind.AnchorIsXray,
                        Args = new object[] { anchors[0].LibraryName, anchors[0].Energy }
                    });
                }
            }
            if (forLibrary)
            {
                // Один набор — одна цепочка. Якорный гейт LibraryPeakFitter общий на весь
                // набор: если в нём лежат и Th-232, и U-238, то одного найденного якоря
                // (скажем, 2614,5 кэВ тория) достаточно, чтобы фит посадил компоненты и
                // на урановую половину — на ториевом образце она даёт ложные
                // отождествления. Ровно это делал пресет «ЕРН-фон», добавлявший два ряда
                // одним набором.
                //
                // Линии без родителя в скобках (K-40, Cs-137, ХРИ, вторичные) в ряд не
                // входят и здесь не считаются: набор «Cs-137 + Co-60» законен.
                List<string> chains = new List<string>();
                foreach (SpectralLine line in sorted)
                {
                    string chain = ChainOf(line);
                    if (chain == null || chains.Contains(chain))
                    {
                        continue;
                    }
                    chains.Add(chain);
                }
                if (chains.Count > 1)
                {
                    issues.Add(new SetIssue
                    {
                        Level = IssueLevel.Warning,
                        Kind = IssueKind.MixedChains,
                        Args = new object[] { string.Join(", ", chains.ToArray()) }
                    });
                }
            }
            if (!forLibrary && zones != null && zones.Style != RoiStyle.Markers)
            {
                for (int i = 1; i < sorted.Count; i++)
                {
                    double lowerA, upperA, lowerB, upperB;
                    zones.LimitsFor(sorted[i - 1], out lowerA, out upperA);
                    zones.LimitsFor(sorted[i], out lowerB, out upperB);
                    if (lowerB < upperA)
                    {
                        issues.Add(new SetIssue
                        {
                            Level = IssueLevel.Warning,
                            Kind = IssueKind.ZonesOverlap,
                            Args = new object[] {
                                sorted[i - 1].Label, lowerA, upperA,
                                sorted[i].Label, lowerB, upperB }
                        });
                    }
                }
            }
            return issues;
        }

        // Ниже какого разноса две линии считаются стоящими «на одной позиции».
        //
        // Фиксированный килоэлектронвольт для этого не годится: разрешение растёт как
        // корень из энергии, и один и тот же зазор на 14 кэВ — это пятая доля FWHM
        // (позиции ещё различимы), а на 2614 кэВ — сотая (позиции неразличимы ничем).
        // Порог поэтому в долях FWHM: у сцинтиллятора R = 7.5 % это 0.7 кэВ на 13 кэВ —
        // примерно прежний килоэлектронвольт — и 9.8 кэВ на 2614, где прежний порог
        // молча пропускал вырожденные пары.
        //
        // 0.1·FWHM — заметно ниже и предела Sparrow (0.85), и допуска заявки линии в
        // LibraryPeakFitter (0.25): пары шире этого фит ещё разбирает BR-связкой,
        // а ближе — амплитуда делится между компонентами произвольно.
        public const double DegenerateFwhmFactor = 0.1;

        // Запасной порог, когда модель разрешения не задана (проверка ROI зовётся без
        // неё): прежняя константа, чтобы поведение не менялось молча.
        const double DegenerateGapFallbackKeV = 1.0;

        static double DegenerateGap(SpectralLine line, ResolutionModel resolution)
        {
            if (resolution == null)
            {
                return DegenerateGapFallbackKeV;
            }
            double fwhm = resolution.Fwhm(line.Energy);
            return fwhm > 0 ? DegenerateFwhmFactor * fwhm : DegenerateGapFallbackKeV;
        }

        // Цепочка линии — так, как её прочитает LibraryPeakFitter.ChainOf: текст в
        // ПОСЛЕДНИХ скобках имени записи. Скобки в имени не украшение, а признак ряда
        // (см. SpectralLine.LibraryName), поэтому смотреть надо именно на LibraryName, а
        // не на Nuclide: в режиме «линии семейства» линия Ra-228 идёт под именем Th-232.
        //
        // null означает «линия не входит ни в один ряд»: одиночный нуклид (K-40, Cs-137),
        // ХРИ материала или расчётный вторичный пик. Такие линии в проверке смешения не
        // участвуют — набор из нескольких независимых нуклидов законен.
        static string ChainOf(SpectralLine line)
        {
            string name = line.LibraryName;
            if (string.IsNullOrEmpty(name) || name[name.Length - 1] != ')')
            {
                return null;
            }
            int close = name.Length - 1;
            int open = name.LastIndexOf('(', close - 1);
            if (open <= 0)
            {
                return null;
            }
            return name.Substring(open + 1, close - open - 1);
        }

        static string Name(SpectralLine line, bool forLibrary)
        {
            return forLibrary ? line.LibraryName : line.Label;
        }
    }
}
