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
        ZonesOverlap
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
                if (Math.Abs(sorted[i].Energy - sorted[i - 1].Energy) < 1.0)
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

        static string Name(SpectralLine line, bool forLibrary)
        {
            return forLibrary ? line.LibraryName : line.Label;
        }
    }
}
