using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.RoiWizard
{
    [Flags]
    public enum SecondaryKind
    {
        None = 0,
        Backscatter = 1,
        ComptonEdge = 2,
        SingleEscape = 4,
        DoubleEscape = 8,
        IodineEscape = 16,
        Annihilation = 32,
        CascadeSum = 64,
        PileUp = 128
    }

    // Расчёт вторичных особенностей спектра: того, что видно в спектре, но не является
    // фотопиком реальной линии. Маркеры нужны, чтобы такие особенности не приписывали
    // лишним нуклидам.
    //
    // Формулы — Knoll, Radiation Detection and Measurement, гл. 10: комптоновский край
    // разд. II.B, пик обратного рассеяния и пики вылета разд. III, вылет K-рентгена иода
    // рис. 10.10 (28 кэВ ниже фотопика), суммирование каскадов разд. III.E.
    //
    // Аналитическая формула даёт край ступеньки, а в спектре виден центроид особенности,
    // размытой разрешением: поиск идёт методом второй производной (алгоритм Марискотти,
    // Gilmore, Practical Gamma-ray Spectrometry, разд. 9.2.3), поэтому найденное положение
    // систематически смещено. Величины поправок получены из измерений комплекса Gamma-1C
    // (NaI(Tl) 63×63, защита Pb 50 мм с вкладышем Cd/Cu): 9 нуклидов,
    // 18 первичных линий, 31 надёжная запись из 41.
    public static class SecondaryPeaks
    {
        public const double ElectronMassKeV = 510.999;

        // |сдвиг| комптоновского края = k·FWHM(E_края). Медиана по 12 записям — 0.78,
        // кучно 0.69…0.84 на 340–1250 кэВ. Источник правила даёт 0.7 по двум нуклидам.
        public const double ComptonEdgeFwhmFactor = 0.8;

        // Пик обратного рассеяния смещён вверх: многократное рассеяние добавляется к
        // однократному 180°. Медиана 8 записей +9.9 кэВ, разброс +0.3…+18.4 — величина
        // задаётся геометрией источника и защиты, через FWHM не выражается.
        public const double BackscatterShiftKeV = 10.0;

        // Доли от площади родительского фотопика (медианы измеренных отношений).
        // Прежнее общее «10 %» расходилось с измерениями почти втрое.
        public const double BackscatterFraction = 0.08;
        public const double ComptonEdgeFraction = 0.06;
        public const double EscapeFraction = 0.06;
        public const double DefaultFraction = 0.10;

        public static double BackscatterEnergy(double energy)
        {
            return energy / (1.0 + 2.0 * energy / ElectronMassKeV);
        }

        public static double ComptonEdgeEnergy(double energy)
        {
            return energy - BackscatterEnergy(energy);
        }

        // Подпись маркера аннигиляции приходит снаружи: она попадает в набор и видна
        // оператору, а язык интерфейса ядру знать неоткуда.
        public const string DefaultAnnihilationLabel = "Annihilation 511";

        public static List<SpectralLine> Generate(IEnumerable<SpectralLine> lines,
                                                  ResolutionModel resolution,
                                                  SecondaryKind kinds,
                                                  double minParentIntensity)
        {
            return Generate(lines, resolution, kinds, minParentIntensity, DefaultAnnihilationLabel);
        }

        public static List<SpectralLine> Generate(IEnumerable<SpectralLine> lines,
                                                  ResolutionModel resolution,
                                                  SecondaryKind kinds,
                                                  double minParentIntensity,
                                                  string annihilationLabel)
        {
            List<SpectralLine> result = new List<SpectralLine>();
            List<SpectralLine> parents = new List<SpectralLine>();
            bool wantAnnihilation = (kinds & SecondaryKind.Annihilation) != 0;

            foreach (SpectralLine line in lines)
            {
                if (line.Type != LineType.Gamma || !line.Selected || line.Intensity < minParentIntensity)
                {
                    continue;
                }
                parents.Add(line);
                string origin = line.Nuclide + " " + Math.Round(line.Energy);

                double backscatter = BackscatterEnergy(line.Energy);
                double edge = line.Energy - backscatter;

                if ((kinds & SecondaryKind.Backscatter) != 0 && line.Energy >= 200)
                {
                    Add(result, line, "BS", backscatter + BackscatterShiftKeV, BackscatterFraction, origin);
                }
                if ((kinds & SecondaryKind.ComptonEdge) != 0 && line.Energy >= 200)
                {
                    Add(result, line, "CE", edge - ComptonEdgeFwhmFactor * resolution.Fwhm(edge),
                        ComptonEdgeFraction, origin);
                }
                // порог образования пар 1022 кэВ; берём с запасом, чтобы маркер не лез туда,
                // где пик вылета формально возможен, но неразличим
                if ((kinds & SecondaryKind.SingleEscape) != 0 && line.Energy > 1122)
                {
                    Add(result, line, "SE", line.Energy - ElectronMassKeV, EscapeFraction, origin);
                }
                if ((kinds & SecondaryKind.DoubleEscape) != 0 && line.Energy > 1222)
                {
                    Add(result, line, "DE", line.Energy - 2 * ElectronMassKeV, DefaultFraction, origin);
                }
                if ((kinds & SecondaryKind.IodineEscape) != 0 && line.Energy < 200)
                {
                    // поглощение в NaI идёт в основном на атомах иода; пик на 28 кэВ ниже
                    Add(result, line, "I-esc", line.Energy - 28.6, DefaultFraction, origin);
                }
                if ((kinds & SecondaryKind.PileUp) != 0)
                {
                    Add(result, line, "PU", 2 * line.Energy, DefaultFraction, origin);
                }
            }

            if ((kinds & SecondaryKind.CascadeSum) != 0)
            {
                AddCascadeSums(result, parents);
            }
            if (wantAnnihilation)
            {
                AddAnnihilation(result, lines, annihilationLabel);
            }
            return result;
        }

        // Истинные совпадения: оба кванта каскада одного распада поглощены за время,
        // меньшее отклика детектора. Суммирование не только создаёт суммарный пик, но и
        // изымает отсчёты из одиночных фотопиков — поэтому такие линии не годятся опорой
        // для библиотечного фита (Knoll, гл. 10, разд. III.E).
        static void AddCascadeSums(List<SpectralLine> result, List<SpectralLine> parents)
        {
            Dictionary<string, List<SpectralLine>> byNuclide = new Dictionary<string, List<SpectralLine>>();
            foreach (SpectralLine line in parents)
            {
                List<SpectralLine> list;
                if (!byNuclide.TryGetValue(line.Nuclide, out list))
                {
                    list = new List<SpectralLine>();
                    byNuclide[line.Nuclide] = list;
                }
                list.Add(line);
            }
            foreach (KeyValuePair<string, List<SpectralLine>> entry in byNuclide)
            {
                List<SpectralLine> list = entry.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        double energy = list[i].Energy + list[j].Energy;
                        result.Add(new SpectralLine
                        {
                            Key = "sec|sum|" + entry.Key + "|" + Math.Round(energy),
                            Nuclide = entry.Key,
                            Label = "SUM (" + entry.Key + " " + Math.Round(list[i].Energy) + "+" +
                                    Math.Round(list[j].Energy) + ")",
                            Energy = Math.Round(energy, 2),
                            Intensity = Math.Round(DefaultFraction * Math.Min(list[i].Intensity, list[j].Intensity), 3),
                            Type = LineType.Secondary,
                            HalfLifeYears = list[i].HalfLifeYears,
                            HalfLifeText = list[i].HalfLifeText
                        });
                    }
                }
            }
        }

        // 511 кэВ появляется и от образования пар в окружающих материалах, а не только
        // от позитронного источника — Knoll предупреждает не путать эти случаи
        static void AddAnnihilation(List<SpectralLine> result, IEnumerable<SpectralLine> lines,
                                    string label)
        {
            foreach (SpectralLine line in lines)
            {
                if (line.Selected && Math.Abs(line.Energy - 511.0) < 1.5)
                {
                    return;                      // настоящая линия рядом — маркер не нужен
                }
            }
            result.Add(new SpectralLine
            {
                Key = "sec|ann|511",
                Nuclide = "—",
                Label = string.IsNullOrEmpty(label) ? DefaultAnnihilationLabel : label,
                Energy = 511.0,
                Intensity = 5.0,
                Type = LineType.Secondary,
                HalfLifeYears = 1e9,
                HalfLifeText = "—"
            });
        }

        static void Add(List<SpectralLine> result, SpectralLine parent, string tag,
                        double energy, double fraction, string origin)
        {
            if (energy < 4)
            {
                return;
            }
            result.Add(new SpectralLine
            {
                Key = "sec|" + tag + "|" + parent.Nuclide + "|" + Math.Round(energy),
                Nuclide = parent.Nuclide,
                Label = tag + " (" + origin + ")",
                Energy = Math.Round(energy, 2),
                Intensity = Math.Round(parent.Intensity * fraction, 3),
                Type = LineType.Secondary,
                HalfLifeYears = parent.HalfLifeYears,
                HalfLifeText = parent.HalfLifeText
            });
        }

   }
}
