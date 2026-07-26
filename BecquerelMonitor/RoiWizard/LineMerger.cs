using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.RoiWizard
{
    // Слияние линий, которые детектор всё равно не разделит.
    // Аналог того, что делал NuclideMaster в IDWin, но с порогами из LibraryPeakFitter.
    public class LineMerger
    {
        readonly ResolutionModel resolution;
        readonly double factor;

        public LineMerger(ResolutionModel resolution, double factorOfFwhm)
        {
            this.resolution = resolution;
            this.factor = factorOfFwhm;
        }

        public static LineMerger For(ResolutionModel resolution, MergeCriterion criterion)
        {
            return new LineMerger(resolution, MergeCriterionInfo.DefaultFactor(criterion));
        }

        public int MergedGroups { get; private set; }

        public int AbsorbedLines { get; private set; }

        // Возвращает набор, в котором слитые группы заменены одной линией.
        // Порог применяется ко ВСЕЙ группе (complete linkage), а не к соседней паре:
        // при попарном сравнении цепочка близких линий склеивалась в группу шире порога —
        // например у Ac-228 944…989 кэВ при пороге 12 кэВ.
        public List<SpectralLine> Merge(IEnumerable<SpectralLine> lines)
        {
            this.MergedGroups = 0;
            this.AbsorbedLines = 0;

            Dictionary<string, List<SpectralLine>> byNuclide = new Dictionary<string, List<SpectralLine>>();
            List<SpectralLine> untouched = new List<SpectralLine>();
            foreach (SpectralLine line in lines)
            {
                // вторичные пики не сливаем: они и так расчётные маркеры
                if (line.Type == LineType.Secondary)
                {
                    untouched.Add(line);
                    continue;
                }
                List<SpectralLine> list;
                if (!byNuclide.TryGetValue(line.Nuclide, out list))
                {
                    list = new List<SpectralLine>();
                    byNuclide[line.Nuclide] = list;
                }
                list.Add(line);
            }

            List<SpectralLine> result = new List<SpectralLine>(untouched);
            foreach (KeyValuePair<string, List<SpectralLine>> entry in byNuclide)
            {
                List<SpectralLine> group = entry.Value;
                group.Sort(delegate(SpectralLine a, SpectralLine b) { return a.Energy.CompareTo(b.Energy); });

                List<SpectralLine> cluster = new List<SpectralLine>();
                foreach (SpectralLine line in group)
                {
                    if (cluster.Count == 0)
                    {
                        cluster.Add(line);
                        continue;
                    }
                    // расстояние меряется от ПЕРВОЙ линии группы, поэтому вся группа
                    // укладывается в порог, а не только соседние пары
                    if (line.Energy - cluster[0].Energy <= this.factor * this.resolution.Fwhm(line.Energy))
                    {
                        cluster.Add(line);
                    }
                    else
                    {
                        Flush(cluster, result);
                        cluster = new List<SpectralLine> { line };
                    }
                }
                Flush(cluster, result);
            }
            result.Sort(delegate(SpectralLine a, SpectralLine b) { return a.Energy.CompareTo(b.Energy); });
            return result;
        }

        void Flush(List<SpectralLine> cluster, List<SpectralLine> output)
        {
            if (cluster.Count == 0)
            {
                return;
            }
            if (cluster.Count == 1)
            {
                output.Add(cluster[0]);
                return;
            }

            double sum = 0.0;
            double weighted = 0.0;
            bool anySelected = false;
            SpectralLine strongest = cluster[0];
            foreach (SpectralLine line in cluster)
            {
                sum += line.Intensity;
                weighted += line.Energy * line.Intensity;
                anySelected = anySelected || line.Selected;
                if (line.Intensity > strongest.Intensity)
                {
                    strongest = line;
                }
            }
            // центроид взвешен по интенсивности — именно туда сядет найденный пик
            double centroid = sum > 0 ? weighted / sum : cluster[0].Energy;
            string interval = Fmt(cluster[0].Energy) + "–" + Fmt(cluster[cluster.Count - 1].Energy);

            output.Add(new SpectralLine
            {
                Key = "m|" + strongest.Nuclide + "|" + centroid.ToString("0.0", CultureInfo.InvariantCulture),
                Nuclide = strongest.Nuclide,
                Label = strongest.Label + " (" + interval + ")",
                Interval = interval,
                Merged = true,
                Energy = Math.Round(centroid, 2),
                Intensity = Math.Round(sum, 3),
                RawIntensity = sum,
                Type = strongest.Type,
                HalfLifeYears = strongest.HalfLifeYears,
                HalfLifeText = strongest.HalfLifeText,
                Selected = anySelected
            });
            this.MergedGroups++;
            this.AbsorbedLines += cluster.Count;
        }

        // Порог в кэВ на характерных энергиях — то, что стоит показать пользователю:
        // «сливаются линии ближе N кэВ на 100, M на 662».
        public double ThresholdAt(double energy)
        {
            return this.factor * this.resolution.Fwhm(energy);
        }

        static string Fmt(double energy)
        {
            return energy < 100
                ? energy.ToString("0.0", CultureInfo.InvariantCulture)
                : Math.Round(energy).ToString(CultureInfo.InvariantCulture);
        }
    }
}
