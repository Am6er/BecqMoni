using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.RoiWizard
{
    // Якорная линия: ровно одна запись набора получает IsAnchor = true. Найдя её в спектре,
    // BecqMoni сажает остальные линии набора на табличные позиции и подгоняет амплитуды
    // (библиотечный фит). Без якоря механизм не запускается вовсе.
    public static class AnchorPicker
    {
        // Хороший якорь — сильная И одинокая линия: сосед внутри FWHM смещает центроид
        // найденного пика, и совпадение с табличной энергией перестаёт быть надёжным.
        // Правило даёт 2614.5 для ряда Th-232 и 609.3 для Ra-226.
        // Сколько якорных линий помечать по умолчанию. LibraryPeakFitter перебирает все
        // записи с IsAnchor, берёт сдвиг калибровки с сильнейшей по SNR и требует, чтобы
        // с найденным пиком совпала хотя бы одна (допуск 0.5·FWHM). Единственный якорь —
        // единственная точка отказа: не нашёлся 2614.5 — набор молчит целиком.
        public const int DefaultCount = 3;

        // Несколько якорей по тому же правилу: сильные и одинокие γ-линии, по убыванию
        // интенсивности. Одинокие идут первыми — у них центроид найденного пика не смещён
        // соседом; если одиноких не хватает, добираются просто сильные.
        public static List<SpectralLine> PickMany(IList<SpectralLine> lines,
                                                  ResolutionModel resolution, int count)
        {
            List<SpectralLine> picked = new List<SpectralLine>();
            if (lines == null || lines.Count == 0 || count <= 0)
            {
                return picked;
            }

            double max = MaxGammaIntensity(lines);
            List<SpectralLine> lonely = new List<SpectralLine>();
            List<SpectralLine> rest = new List<SpectralLine>();
            foreach (SpectralLine line in lines)
            {
                if (line.Type != LineType.Gamma || line.Intensity < 0.2 * max)
                {
                    continue;
                }
                if (IsLonely(line, lines, resolution, max))
                {
                    lonely.Add(line);
                }
                else
                {
                    rest.Add(line);
                }
            }
            lonely.Sort(ByIntensityDesc);
            rest.Sort(ByIntensityDesc);

            foreach (SpectralLine line in lonely)
            {
                if (picked.Count >= count)
                {
                    break;
                }
                picked.Add(line);
            }
            foreach (SpectralLine line in rest)
            {
                if (picked.Count >= count)
                {
                    break;
                }
                picked.Add(line);
            }
            if (picked.Count == 0)
            {
                // γ-линий в наборе нет вовсе — та же оговорка, что и у Pick
                SpectralLine fallback = Strongest(lines, LineType.Xray);
                if (fallback != null)
                {
                    picked.Add(fallback);
                }
            }
            return picked;
        }

        static int ByIntensityDesc(SpectralLine a, SpectralLine b)
        {
            return b.Intensity.CompareTo(a.Intensity);
        }

        static double MaxGammaIntensity(IList<SpectralLine> lines)
        {
            double max = 0.0;
            foreach (SpectralLine line in lines)
            {
                if (line.Type == LineType.Gamma && line.Intensity > max)
                {
                    max = line.Intensity;
                }
            }
            return max;
        }

        public static SpectralLine Pick(IList<SpectralLine> lines, ResolutionModel resolution)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }
            // Порог 0.2·max считается по ОДНИМ γ-линиям. Интенсивности ХРИ условные
            // (Kα1 = 100), и если брать максимум по всем линиям, то у слабо-γ нуклида
            // рядом с ХРИ свинца все настоящие γ уходят ниже порога.
            double max = 0.0;
            foreach (SpectralLine line in lines)
            {
                if (line.Type == LineType.Gamma && line.Intensity > max)
                {
                    max = line.Intensity;
                }
            }

            SpectralLine best = null;
            SpectralLine bestLonely = null;
            foreach (SpectralLine line in lines)
            {
                if (line.Type != LineType.Gamma || line.Intensity < 0.2 * max)
                {
                    continue;
                }
                if (best == null || line.Intensity > best.Intensity)
                {
                    best = line;
                }
                if (IsLonely(line, lines, resolution, max) &&
                    (bestLonely == null || line.Intensity > bestLonely.Intensity))
                {
                    bestLonely = line;
                }
            }
            SpectralLine pick = bestLonely ?? best;
            if (pick != null)
            {
                return pick;
            }
            // Фолбэк только на настоящие линии распада: у ХРИ интенсивность условная,
            // у вторичных положение — эмпирическая поправка. Якорь на таком маркере
            // означал бы, что LibraryPeakFitter сажает весь набор по нефизической опоре.
            return Strongest(lines, LineType.Xray);
        }

        static SpectralLine Strongest(IList<SpectralLine> lines, LineType type)
        {
            SpectralLine pick = null;
            foreach (SpectralLine line in lines)
            {
                if (line.Type == type && (pick == null || line.Intensity > pick.Intensity))
                {
                    pick = line;
                }
            }
            return pick;
        }

        // Годится ли линия в якоря: набор без якоря библиотечный фит не запускает вовсе,
        // а якорь на ХРИ или вторичном маркере хуже отсутствия — фит «найдёт» опору там,
        // где её физически нет.
        public static bool IsAcceptable(SpectralLine line)
        {
            return line != null && (line.Type == LineType.Gamma || line.Type == LineType.Xray);
        }

        static bool IsLonely(SpectralLine line, IList<SpectralLine> lines,
                             ResolutionModel resolution, double max)
        {
            double window = resolution.Fwhm(line.Energy);
            foreach (SpectralLine other in lines)
            {
                if (!ReferenceEquals(other, line) && other.Intensity >= 0.05 * max &&
                    Math.Abs(other.Energy - line.Energy) < window)
                {
                    return false;
                }
            }
            return true;
        }
    }

}
