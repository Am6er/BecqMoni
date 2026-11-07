using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    /// <summary>Итог <see cref="NuclideSetAdvisor"/>: какой сет предложить и почему.</summary>
    public sealed class NuclideSetSuggestion
    {
        public NuclideSet Set;

        /// <summary>Балл сета: доля пиков, которые объясняют его главные линии, × доля этих линий, нашедшая пик.</summary>
        public double Score;

        /// <summary>Балл второго места.</summary>
        public double RunnerUp;

        /// <summary>Доля пиков (по весу SNR^0.75), которые объясняют главные линии сета.</summary>
        public double PeakShare;

        /// <summary>Доля главных линий сета (по выходу), нашедшая пик.</summary>
        public double LineShare;

        /// <summary>Сколько пиков объясняют главные линии сета.</summary>
        public int ExplainedPeaks;

        /// <summary>SNR сильнейшего из этих пиков.</summary>
        public double StrongestSnr;

        /// <summary>Энергия этого пика, кэВ.</summary>
        public double StrongestKev;
    }

    /// <summary>
    /// ⛔ (`AMBER313`, 05.11.2026) КАКОЙ СЕТ ОБЪЯСНЯЕТ НАЙДЕННЫЕ ПИКИ. Правило Amber: «Нужно выбирать
    /// правильный сет. А не пользовать All Nuclides!»; вопросником, дословно: «Подсказка в поиске
    /// пиков, где ComboBox -- All Nuclides -- Появляется сразу по итогу решения - нужен этот сет».
    ///
    /// Чистая функция: без окна, без состояния, без имён нуклидов; сеты и линии приходят из
    /// библиотеки. Разбор и конфиг не трогает.
    ///
    /// Судят только ГЛАВНЫЕ линии сета: не рентген элемента (<see cref="NuclideDefinition.IsElementXray"/>),
    /// с проставленным выходом и не вошедшие ни в какой другой сет библиотеки. Линия, общая для
    /// нескольких сетов (калий как фон пробы), и рентген защиты ничего не различают: прежде рентген с
    /// большим выходом держал долю линий сета с одной гамма-линией (662 кэВ) не выше 0.59, и балл падал
    /// при единственном настоящем пике. Решение Amber 05.11.2026 вопросником: «Разобрать Cs-137
    /// (Рекомендую)».
    ///
    /// Балл = (доля пиков по весу SNR^0.75, объяснённых главными линиями) × (доля главных линий по
    /// выходу, нашедшая пик). Пик, который объясняет только рентген элемента из этого сета, ни за, ни
    /// против: он убирается из знаменателя. Рентген защиты есть при любом источнике, и считать его
    /// необъяснённым значило бы ронять балл сета за то, что он честно назвал. Общая линия (калий) остаётся в
    /// знаменателе. Пик внизу шкалы один доводом быть не может без большого SNR: у фона и у пробы без
    /// источника там часто стоит единственный горб.
    ///
    /// Форма балла, окно и пороги подобраны на разделённой выборке (журнал
    /// `handover/handover-2026-11-05-fixsets.md`), проверка — <c>SetHintProbeA313</c>. Молчание здесь
    /// дешевле неверного совета: сомнение значит «не предлагать».
    /// </summary>
    public static class NuclideSetAdvisor
    {
        /// <summary>Линия объясняет пик, если промах не больше этой доли FWHM пика.</summary>
        public const double LineWindowInFwhm = 0.5;

        /// <summary>Степень SNR в весе пика.</summary>
        public const double PeakWeightPower = 0.75;

        /// <summary>Балл, ниже которого сет не предлагается.</summary>
        public const double MinimumScore = 0.45;

        /// <summary>Отрыв балла от второго места, ниже которого сет не предлагается.</summary>
        public const double MinimumGap = 0.20;

        /// <summary>Балл, ниже которого показанный сет перестаёт держаться (<see cref="NuclideSetHintTracker"/>).</summary>
        public const double HoldScore = 0.35;

        /// <summary>Отрыв, ниже которого показанный сет перестаёт держаться.</summary>
        public const double HoldGap = 0.10;

        /// <summary>SNR, начиная с которого сет можно предлагать по одному объяснённому пику.</summary>
        public const double MinimumSinglePeakSnr = 100.0;

        /// <summary>То же для единственного пика ниже <see cref="LowEnergyKev"/>.</summary>
        public const double MinimumLowPeakSnr = 300.0;

        /// <summary>Граница низа шкалы, кэВ.</summary>
        public const double LowEnergyKev = 100.0;

        /// <summary>Сет для найденных пиков или null: пиков нет, сетов нет, различие неуверенное.</summary>
        public static NuclideSetSuggestion Suggest(IList<Peak> peaks, EnergyCalibration calibration,
                                                   IList<NuclideSet> sets, IList<NuclideDefinition> definitions,
                                                   NuclideSet held = null)
        {
            if (peaks == null || peaks.Count == 0 || calibration == null)
            {
                return null;
            }

            double[] energy = new double[peaks.Count];
            double[] snr = new double[peaks.Count];
            double[] fwhmKev = new double[peaks.Count];
            for (int i = 0; i < peaks.Count; i++)
            {
                energy[i] = peaks[i].Energy;
                snr[i] = peaks[i].SNR;
                fwhmKev[i] = peaks[i].FwhmKev(calibration);
            }

            return Suggest(energy, snr, fwhmKev, sets, definitions, held);
        }

        /// <summary>
        /// То же на голых числах: энергия, SNR и FWHM пика в кэВ (0 — ширина не измерена).
        /// <paramref name="held"/> — сет, уже показанный человеку: он остаётся при более мягких
        /// <see cref="HoldScore"/> и <see cref="HoldGap"/>, чтобы подсказка не мигала при наборе спектра.
        /// </summary>
        public static NuclideSetSuggestion Suggest(double[] energy, double[] snr, double[] fwhmKev,
                                                   IList<NuclideSet> sets, IList<NuclideDefinition> definitions,
                                                   NuclideSet held = null)
        {
            if (energy == null || energy.Length == 0 || sets == null || sets.Count == 0 || definitions == null)
            {
                return null;
            }

            int peaks = energy.Length;
            double[] weight = new double[peaks];
            double totalWeight = 0.0;
            for (int p = 0; p < peaks; p++)
            {
                weight[p] = snr[p] > 0.0 ? Math.Pow(snr[p], PeakWeightPower) : 0.0;
                totalWeight += weight[p];
            }

            if (!(totalWeight > 0.0))
            {
                return null;
            }

            NuclideSetSuggestion best = null;
            NuclideSetSuggestion heldScore = null;
            double runnerUp = 0.0;
            double othersOfHeld = 0.0;
            bool[] explained = new bool[peaks];
            foreach (NuclideSet set in sets)
            {
                if (set == null)
                {
                    continue;
                }

                NuclideSetSuggestion current = Judge(set, sets, definitions, energy, snr, fwhmKev, weight, totalWeight, explained);
                if (current == null)
                {
                    continue;
                }

                if (ReferenceEquals(set, held))
                {
                    heldScore = current;
                }
                else if (current.Score > othersOfHeld)
                {
                    othersOfHeld = current.Score;
                }

                if (best == null || current.Score > best.Score)
                {
                    if (best != null)
                    {
                        runnerUp = best.Score;
                    }

                    best = current;
                }
                else if (current.Score > runnerUp)
                {
                    runnerUp = current.Score;
                }
            }

            if (heldScore != null && heldScore.Score >= HoldScore && heldScore.Score - othersOfHeld >= HoldGap)
            {
                heldScore.RunnerUp = othersOfHeld;
                return heldScore;
            }

            if (best == null || best.Score < MinimumScore || best.Score - runnerUp < MinimumGap)
            {
                return null;
            }

            if (best.ExplainedPeaks < 2)
            {
                double needed = best.StrongestKev < LowEnergyKev ? MinimumLowPeakSnr : MinimumSinglePeakSnr;
                if (best.StrongestSnr < needed)
                {
                    return null;
                }
            }

            best.RunnerUp = runnerUp;
            return best;
        }

        /// <summary>Балл одного сета; null — судить нечем (у сета нет линий с выходом).</summary>
        static NuclideSetSuggestion Judge(NuclideSet set, IList<NuclideSet> sets, IList<NuclideDefinition> definitions,
                                          double[] energy, double[] snr, double[] fwhmKev, double[] weight, double totalWeight, bool[] explained)
        {
            // Главные линии; если всё общее с другими сетами — все не-рентгеновские, если и таких нет — все с выходом.
            for (int rank = 0; rank < 3; rank++)
            {
                Array.Clear(explained, 0, explained.Length);
                bool[] neutral = new bool[explained.Length];
                double allYield = 0.0;
                double foundYield = 0.0;
                foreach (NuclideDefinition line in definitions)
                {
                    if (line == null || !line.Visible || line.Energy <= 0.0
                        || line.Sets == null || !line.Sets.Contains(set.Id))
                    {
                        continue;
                    }

                    // Линия без выхода (образ аннигиляции в своём сете): пик объясняет, в долю линий не идёт.
                    bool noYield = !(line.Intencity > 0.0);
                    bool skipped = noYield
                        ? InOtherSet(line, set, sets)
                        : (rank == 0 && (line.IsElementXray || InOtherSet(line, set, sets))) || (rank == 1 && line.IsElementXray);
                    if (skipped)
                    {
                        if (line.IsElementXray)
                        {
                            for (int p = 0; p < energy.Length; p++)
                            {
                                if (fwhmKev[p] > 0.0 && Math.Abs(energy[p] - line.Energy) <= LineWindowInFwhm * fwhmKev[p])
                                {
                                    neutral[p] = true;
                                }
                            }
                        }

                        continue;
                    }

                    bool found = false;
                    for (int p = 0; p < energy.Length; p++)
                    {
                        if (fwhmKev[p] > 0.0 && Math.Abs(energy[p] - line.Energy) <= LineWindowInFwhm * fwhmKev[p])
                        {
                            explained[p] = true;
                            found = true;
                        }
                    }

                    if (!noYield)
                    {
                        allYield += line.Intencity;
                        if (found)
                        {
                            foundYield += line.Intencity;
                        }
                    }
                }

                if (!(allYield > 0.0))
                {
                    continue;
                }

                double explainedWeight = 0.0;
                double countedWeight = totalWeight;
                int explainedPeaks = 0;
                double strongest = 0.0;
                double strongestKev = 0.0;
                for (int p = 0; p < energy.Length; p++)
                {
                    if (explained[p])
                    {
                        explainedWeight += weight[p];
                        explainedPeaks++;
                        if (snr[p] > strongest || (snr[p] == strongest && energy[p] > strongestKev))
                        {
                            strongest = snr[p];
                            strongestKev = energy[p];
                        }
                    }
                    else if (neutral[p])
                    {
                        countedWeight -= weight[p];
                    }
                }

                double peakShare = countedWeight > 0.0 ? explainedWeight / countedWeight : 0.0;
                double lineShare = foundYield / allYield;
                return new NuclideSetSuggestion
                {
                    Set = set,
                    Score = peakShare * lineShare,
                    PeakShare = peakShare,
                    LineShare = lineShare,
                    ExplainedPeaks = explainedPeaks,
                    StrongestSnr = strongest,
                    StrongestKev = strongestKev,
                };
            }

            return null;
        }

        static bool InOtherSet(NuclideDefinition line, NuclideSet set, IList<NuclideSet> sets)
        {
            foreach (NuclideSet other in sets)
            {
                if (other != null && !ReferenceEquals(other, set) && line.Sets.Contains(other.Id))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// (`AMBER313`, Н4 критика `critSets`) Подсказка на живом наборе не должна дёргаться на такте.
    /// Показанный сет держится при мягких порогах (<see cref="NuclideSetAdvisor.HoldScore"/>); при записи
    /// новый сет показывается только после второго подряд одинакового вердикта, и голосует только НОВЫЙ
    /// список пиков, то есть конец поиска. Вызовы в начале такта и по итогу FSA видят прежний список:
    /// прежде его голос принимался при новом числе отсчётов, и подтвердить сет мог спектр, которого поиск
    /// ещё не видел (контроль критика `critWave2`: пустой спектр). На готовом спектре — сразу. По одному на
    /// документ, без окна.
    /// </summary>
    public sealed class NuclideSetHintTracker
    {
        public const int LiveConfirmations = 2;

        NuclideSet shown;
        NuclideSet pending;
        int confirmed;
        IList<Peak> lastPeaks;

        public NuclideSetSuggestion Step(IList<Peak> peaks, EnergyCalibration calibration,
                                         IList<NuclideSet> sets, IList<NuclideDefinition> definitions,
                                         bool recording)
        {
            bool sameList = ReferenceEquals(peaks, this.lastPeaks);
            this.lastPeaks = peaks;
            NuclideSetSuggestion suggestion = NuclideSetAdvisor.Suggest(peaks, calibration, sets, definitions, this.shown);
            if (suggestion != null && ReferenceEquals(suggestion.Set, this.shown))
            {
                return suggestion;
            }

            this.shown = null;
            if (suggestion == null)
            {
                if (!sameList)
                {
                    this.pending = null;
                    this.confirmed = 0;
                }

                return null;
            }

            if (recording)
            {
                if (sameList)
                {
                    return null;
                }

                if (!ReferenceEquals(suggestion.Set, this.pending))
                {
                    this.pending = suggestion.Set;
                    this.confirmed = 1;
                }
                else
                {
                    this.confirmed++;
                }

                if (this.confirmed < LiveConfirmations)
                {
                    return null;
                }
            }

            this.pending = null;
            this.confirmed = 0;
            this.shown = suggestion.Set;
            return suggestion;
        }

        public void Reset()
        {
            this.shown = null;
            this.pending = null;
            this.confirmed = 0;
        }
    }
}
