using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BecquerelMonitor
{
    /// <summary>
    /// Библиотечный фит по нуклидному сету: если FWHM-finder нашёл якорную
    /// линию сета (NuclideDefinition.IsAnchor), компоненты сажаются на линии
    /// сета в фиксированных (табличных) позициях, амплитуды фитятся
    /// Пуассон-правдоподобием тем же модельным стеком, что и деконволюция
    /// (профили PeakShapeModel, SASNIP-континуум + фон прибора). Значимые
    /// компоненты (Fisher z >= порога) добавляются как пики origin Library.
    ///
    /// Суб-Sparrow бленды линий одной цепочки решаются BR-связкой: линии
    /// ближе 0.85·FWHM объединяются в группу с одной свободной амплитудой и
    /// весами ∝ NuclideDefinition.Intencity (вековое равновесие ряда, кривая
    /// эффективности на близких энергиях сокращается). Пик-центроид бленда
    /// заменяется на линии группы, если это улучшает AIC (D + 2k).
    /// Дополнительно фитятся escape-компоненты SE/DE от сильных пиков.
    ///
    /// Портировано и развито из oracle-режима tools/RjmcmcHarness (итерации
    /// 6 и 8 в tools/RjmcmcTuning/README.md). Формализм: Nagata et al.,
    /// arXiv:1812.05501 (Пуассон, значимость через информацию Фишера);
    /// Okubo et al., arXiv:2605.17518 (внешние ограничения разрывают
    /// спектральное вырождение); Ukita, JPSJ 91 (2022) 064002 (фиксированные
    /// позиции разрушают вырождение вложенных моделей).
    /// </summary>
    public class LibraryPeakFitter
    {
        // Порог значимости Fisher z фитованной амплитуды (критерий
        // RECOVERABLE oracle-режима). Линии слабее порога считаются
        // отсутствующими и пиков не порождают.
        public const double SignificanceZ = 4.0;

        // Допуск совпадения якорной линии с найденным пиком, доли FWHM пика.
        const double AnchorMatchToleranceFwhm = 0.5;

        // Пик «принадлежит» линии (линия уже обнаружена), если центр пика в
        // этой доле FWHM от линии. Меньше исторических 0.5: посторонний пик в
        // 0.3-0.5 FWHM не должен блокировать посадку компонента линии.
        const double ClaimToleranceFwhm = 0.25;

        // Пик считается центроидом бленда группы (и подлежит замене на её
        // линии), если он в этой доле FWHM от какой-либо линии группы.
        const double BlendCoverToleranceFwhm = 0.5;

        // Порог кластеризации линий в bound-группу: предел Sparrow
        // (delta < 2·sigma = 0.85·FWHM — неразрешимо слепым поиском).
        const double SparrowFwhm = 0.85;

        // Минимальная энергия источника для посадки escape-компонент
        // (SE = E-511, DE = E-1022); ниже SE слаб и тонет в континууме.
        const double EscapeSourceMinEnergy = 1200.0;

        // Максимум итераций координатного спуска (как в oracle-режиме).
        const int FitIterations = 300;

        public sealed class LibraryCandidate
        {
            public NuclideDefinition Nuclide; // null у escape-компонент
            public string Label;              // подпись для диагностики (SE/DE)
            public int Channel;
            public double Fwhm;
            public double Amplitude;
            public double Z;
        }

        public sealed class LibraryFitResult
        {
            public List<LibraryCandidate> AddedPeaks = new List<LibraryCandidate>();
            // Пики-центроиды блендов, заменённые линиями bound-группы.
            public List<Peak> ReplacedPeaks = new List<Peak>();
            // Пики, совпавшие с якорными линиями сета (включившие фит).
            public List<Peak> AnchorPeaks = new List<Peak>();
        }

        sealed class FitComponent
        {
            public NuclideDefinition Nuclide;
            public string Label;
            public int Channel;
            public double Fwhm;
            public int Start;
            public double[] Profile;
            public double Amplitude;
            // Члены bound-группы (для развёртки амплитуды по линиям).
            public List<LineSite> GroupMembers;
            public double[] GroupWeights;
        }

        sealed class LineSite
        {
            public NuclideDefinition Nuclide;
            public int Channel;
            public double Fwhm;
            public double Intensity;
            public string Chain;
        }

        public static LibraryFitResult Fit(
            EnergySpectrum spectrum,
            EnergySpectrum backgroundSpectrum,
            int[] snipContinuum,
            FwhmCalibration fwhmCalibration,
            List<Peak> existingPeaks,
            NuclideSet nuclideSet,
            FWHMPeakDetectionMethodConfig peakConfig)
        {
            LibraryFitResult result = new LibraryFitResult();
            if (spectrum?.Spectrum == null || fwhmCalibration == null || nuclideSet == null || peakConfig == null)
            {
                return result;
            }

            List<NuclideDefinition> setLines = NuclideDefinitionManager.GetInstance().NuclideDefinitions
                .Where(n => n != null && n.Visible && n.Energy > 0.0 && n.Sets != null && n.Sets.Contains(nuclideSet.Id))
                .OrderBy(n => n.Energy)
                .ToList();
            if (setLines.Count == 0 || !setLines.Any(n => n.IsAnchor))
            {
                return result;
            }

            int channels = spectrum.NumberOfChannels;
            int chMin = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Min_Range, maxChannels: channels));
            int chMax = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Max_Range, maxChannels: channels));
            if (chMax < chMin)
            {
                int swap = chMin;
                chMin = chMax;
                chMax = swap;
            }

            // Гейт: хотя бы одна якорная линия должна совпасть с найденным
            // пиком. Сдвиг калибровки берём с сильнейшего (по SNR) якоря:
            // matched-filter центроид точнее табличной позиции при дрейфе.
            int calibrationShift = 0;
            double bestAnchorSnr = Double.NegativeInfinity;
            bool anchorMatched = false;
            foreach (NuclideDefinition anchorLine in setLines.Where(n => n.IsAnchor))
            {
                int anchorChannel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(anchorLine.Energy, maxChannels: channels));
                foreach (Peak peak in existingPeaks)
                {
                    double tolerance = AnchorMatchToleranceFwhm * Math.Max(1.0, peak.FWHM);
                    if (Math.Abs(peak.Channel - anchorChannel) <= tolerance)
                    {
                        anchorMatched = true;
                        if (!result.AnchorPeaks.Contains(peak))
                        {
                            result.AnchorPeaks.Add(peak);
                        }
                        if (peak.SNR > bestAnchorSnr)
                        {
                            bestAnchorSnr = peak.SNR;
                            calibrationShift = peak.Channel - anchorChannel;
                        }
                    }
                }
            }

            if (!anchorMatched)
            {
                return result;
            }

            int[] observed = spectrum.Spectrum;
            double[] fixedBackground = BuildFixedBackground(spectrum, backgroundSpectrum, snipContinuum);

            // --- Сайты линий сета ---
            List<LineSite> sites = new List<LineSite>();
            foreach (NuclideDefinition line in setLines)
            {
                int channel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(line.Energy, maxChannels: channels)) + calibrationShift;
                if (channel <= chMin || channel >= chMax)
                {
                    continue;
                }

                double fwhm = fwhmCalibration.ChannelToFwhm(channel);
                if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
                {
                    continue;
                }

                sites.Add(new LineSite
                {
                    Nuclide = line,
                    Channel = channel,
                    Fwhm = fwhm,
                    Intensity = line.Intencity,
                    Chain = ChainOf(line)
                });
            }

            if (sites.Count == 0)
            {
                return result;
            }

            // «Заявленные» линии: линия уже обнаружена, если существующий пик
            // стоит в ClaimToleranceFwhm от неё. Такие пики защищены от
            // замены другими группами.
            Dictionary<Peak, LineSite> claims = BuildClaims(existingPeaks, sites);

            // --- Кластеризация в bound-группы (Sparrow + одна цепочка + BR) ---
            List<List<LineSite>> clusters = ClusterSites(sites);
            List<LineSite> singles = new List<LineSite>();
            List<List<LineSite>> groups = new List<List<LineSite>>();
            foreach (List<LineSite> cluster in clusters)
            {
                foreach (IGrouping<string, LineSite> chainGroup in cluster.GroupBy(s => s.Chain))
                {
                    List<LineSite> members = chainGroup.ToList();
                    if (members.Count >= 2 && members.All(m => m.Intensity > 0.0))
                    {
                        groups.Add(members);
                    }
                    else
                    {
                        singles.AddRange(members);
                    }
                }
            }

            // --- Свободные компоненты: существующие пики ---
            Dictionary<Peak, FitComponent> peakComponents = new Dictionary<Peak, FitComponent>();
            foreach (Peak peak in existingPeaks.OrderBy(p => p.Channel))
            {
                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, peak.Channel, null, null);
                if (component != null)
                {
                    peakComponents[peak] = component;
                }
            }

            // --- Одиночные линии: пропустить заявленные, посадить свободные ---
            List<FitComponent> singleComponents = new List<FitComponent>();
            foreach (LineSite site in singles)
            {
                if (claims.Values.Contains(site))
                {
                    continue;
                }

                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, site.Channel, site.Nuclide, null);
                if (component != null)
                {
                    singleComponents.Add(component);
                }
            }

            // --- Escape-сайты SE/DE от сильных найденных пиков ---
            // Источник обязан быть заявлен линией сета: escape от ложного
            // пика — это ложный escape (например, SE от мусорного 2333).
            foreach (Peak sourcePeak in existingPeaks.Where(p => p.Energy >= EscapeSourceMinEnergy && claims.ContainsKey(p)))
            {
                foreach (double escapeOffset in new[] { 511.0, 1022.0 })
                {
                    double escapeEnergy = sourcePeak.Energy - escapeOffset;
                    int escapeChannel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(escapeEnergy, maxChannels: channels));
                    if (escapeChannel <= chMin || escapeChannel >= chMax)
                    {
                        continue;
                    }

                    double escapeFwhm = fwhmCalibration.ChannelToFwhm(escapeChannel);
                    if (!PeakShapeModel.IsFinite(escapeFwhm) || escapeFwhm <= 0.0)
                    {
                        continue;
                    }

                    double claimTolerance = ClaimToleranceFwhm * escapeFwhm;
                    bool occupied =
                        existingPeaks.Any(p => Math.Abs(p.Channel - escapeChannel) <= claimTolerance) ||
                        sites.Any(s => Math.Abs(s.Channel - escapeChannel) <= claimTolerance) ||
                        singleComponents.Any(c => c.Label != null && Math.Abs(c.Channel - escapeChannel) <= claimTolerance);
                    if (occupied)
                    {
                        continue;
                    }

                    string label = String.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} {1:F0}",
                        escapeOffset > 600.0 ? "DE" : "SE",
                        sourcePeak.Energy);
                    FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, escapeChannel, null, label);
                    if (component != null)
                    {
                        singleComponents.Add(component);
                    }
                }
            }

            // --- Модель A: пики + одиночные компоненты, без групп ---
            List<FitComponent> model = new List<FitComponent>(peakComponents.Values);
            model.AddRange(singleComponents);
            double devianceCurrent = FitModel(observed, fixedBackground, model, chMin, chMax);

            // --- Последовательное принятие bound-групп по AIC ---
            List<Peak> replacedPeaks = new List<Peak>();
            foreach (List<LineSite> group in groups.OrderBy(g => g[0].Channel))
            {
                FitComponent groupComponent = BuildGroupComponent(spectrum, fwhmCalibration, group);
                if (groupComponent == null)
                {
                    continue;
                }

                // Пики-центроиды бленда: близко к линиям группы и не заявлены
                // линией вне группы.
                List<Peak> covering = new List<Peak>();
                foreach (KeyValuePair<Peak, FitComponent> entry in peakComponents)
                {
                    if (!model.Contains(entry.Value))
                    {
                        continue; // уже заменён предыдущей группой
                    }

                    Peak peak = entry.Key;
                    bool nearGroup = group.Any(s => Math.Abs(peak.Channel - s.Channel) <= BlendCoverToleranceFwhm * Math.Max(1.0, Math.Max(peak.FWHM, s.Fwhm)));
                    if (!nearGroup)
                    {
                        continue;
                    }

                    if (claims.TryGetValue(peak, out LineSite claimedBy) && !group.Contains(claimedBy))
                    {
                        continue; // пик принадлежит линии вне группы — защищён
                    }

                    covering.Add(peak);
                }

                List<FitComponent> trial = model
                    .Where(c => !covering.Any(p => ReferenceEquals(peakComponents[p], c)))
                    .ToList();
                trial.Add(groupComponent);

                double devianceTrial = FitModel(observed, fixedBackground, trial, chMin, chMax);

                // AIC: D + 2k; в trial добавлен 1 параметр, убрано covering.Count.
                double aicCurrent = devianceCurrent + 2.0 * model.Count;
                double aicTrial = devianceTrial + 2.0 * trial.Count;
                if (aicTrial >= aicCurrent)
                {
                    continue;
                }

                double[] lambdaTrial = BuildLambda(fixedBackground, trial, channels);
                double bestMemberZ = BestMemberZ(spectrum, fwhmCalibration, groupComponent, lambdaTrial);
                if (bestMemberZ < SignificanceZ)
                {
                    continue;
                }

                model = trial;
                devianceCurrent = devianceTrial;
                replacedPeaks.AddRange(covering);
            }

            // --- Финальный отбор ---
            double[] lambda = BuildLambda(fixedBackground, model, channels);
            foreach (FitComponent component in model)
            {
                if (component.GroupMembers != null)
                {
                    // Развёртка bound-группы по линиям: амплитуда доли w_i,
                    // z по собственному профилю линии.
                    for (int i = 0; i < component.GroupMembers.Count; i++)
                    {
                        LineSite member = component.GroupMembers[i];
                        double memberAmplitude = component.Amplitude * component.GroupWeights[i];
                        FitComponent memberComponent = BuildFitComponent(spectrum, fwhmCalibration, member.Channel, member.Nuclide, null);
                        if (memberComponent == null)
                        {
                            continue;
                        }

                        memberComponent.Amplitude = memberAmplitude;
                        double z = FisherZ(memberComponent, lambda);
                        if (z < SignificanceZ)
                        {
                            continue;
                        }

                        result.AddedPeaks.Add(new LibraryCandidate
                        {
                            Nuclide = member.Nuclide,
                            Channel = member.Channel,
                            Fwhm = member.Fwhm,
                            Amplitude = memberAmplitude,
                            Z = z
                        });
                    }
                }
                else if (component.Nuclide != null)
                {
                    // Одиночная линия сета. Escape-компоненты (Label != null)
                    // НЕ выводятся как пики: они остаются только в модели
                    // фита, чтобы амплитуда SE/DE не перетекала в соседние
                    // линии. Библиотечную пометку получают ТОЛЬКО линии сета.
                    double z = FisherZ(component, lambda);
                    if (z < SignificanceZ)
                    {
                        continue;
                    }

                    result.AddedPeaks.Add(new LibraryCandidate
                    {
                        Nuclide = component.Nuclide,
                        Channel = component.Channel,
                        Fwhm = component.Fwhm,
                        Amplitude = component.Amplitude,
                        Z = z
                    });
                }
            }

            // --- Дедуп дрейфа: незаявленный существующий пик в 0.5·FWHM от
            // принятой библиотечной компоненты — это либо та же линия со
            // сдвинутым центроидом (дрейф калибровки, K-40 1482→1461), либо
            // артефакт на её склоне. Табличная позиция точнее — пик уходит.
            // Заявленные пики (принадлежащие другой линии сета) защищены.
            foreach (LibraryCandidate candidate in result.AddedPeaks)
            {
                foreach (Peak peak in existingPeaks)
                {
                    if (replacedPeaks.Contains(peak) || claims.ContainsKey(peak))
                    {
                        continue;
                    }

                    double tolerance = BlendCoverToleranceFwhm * Math.Max(1.0, Math.Max(peak.FWHM, candidate.Fwhm));
                    if (Math.Abs(peak.Channel - candidate.Channel) <= tolerance)
                    {
                        replacedPeaks.Add(peak);
                    }
                }
            }

            result.ReplacedPeaks = replacedPeaks;
            return result;
        }

        // Идентификатор цепочки для векового равновесия: текст в последних
        // скобках имени («Bi-214 (Ra-226)» → «Ra-226»), иначе имя целиком.
        // BR-связка допустима только внутри одной цепочки.
        static string ChainOf(NuclideDefinition nuclide)
        {
            string name = nuclide.Name ?? "";
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                return name.Substring(open + 1, close - open - 1).Trim();
            }
            return name.Trim();
        }

        static Dictionary<Peak, LineSite> BuildClaims(List<Peak> peaks, List<LineSite> sites)
        {
            Dictionary<Peak, LineSite> claims = new Dictionary<Peak, LineSite>();
            foreach (Peak peak in peaks)
            {
                LineSite best = null;
                double bestDistance = Double.MaxValue;
                foreach (LineSite site in sites)
                {
                    double distance = Math.Abs(peak.Channel - site.Channel);
                    double tolerance = ClaimToleranceFwhm * Math.Max(1.0, Math.Max(peak.FWHM, site.Fwhm));
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        best = site;
                        bestDistance = distance;
                    }
                }

                if (best != null)
                {
                    claims[peak] = best;
                }
            }

            return claims;
        }

        static List<List<LineSite>> ClusterSites(List<LineSite> sites)
        {
            List<List<LineSite>> clusters = new List<List<LineSite>>();
            List<LineSite> current = null;
            LineSite previous = null;
            foreach (LineSite site in sites.OrderBy(s => s.Channel))
            {
                if (previous != null &&
                    Math.Abs(site.Channel - previous.Channel) <= SparrowFwhm * Math.Max(site.Fwhm, previous.Fwhm))
                {
                    current.Add(site);
                }
                else
                {
                    current = new List<LineSite> { site };
                    clusters.Add(current);
                }

                previous = site;
            }

            return clusters;
        }

        static int ClampChannel(int channels, double value)
        {
            return Math.Max(0, Math.Min(channels - 1, Convert.ToInt32(Math.Round(value))));
        }

        // Фиксированный фон = огибающая max(SASNIP-континуум, масштабированный
        // по времени фон прибора) — тот же рецепт, что в RJMCMC-деконволюции
        // (ExtractFixedBackground) и oracle-режиме харнесса.
        static double[] BuildFixedBackground(EnergySpectrum foreground, EnergySpectrum background, int[] snip)
        {
            int channels = foreground.NumberOfChannels;
            double[] fixedBackground = new double[channels];
            double scale = background != null && background.MeasurementTime > 0.0 && foreground.MeasurementTime > 0.0
                ? foreground.MeasurementTime / background.MeasurementTime
                : 0.0;
            bool sameCalibration = background != null &&
                foreground.EnergyCalibration != null &&
                foreground.EnergyCalibration.Equals(background.EnergyCalibration);
            for (int i = 0; i < channels; i++)
            {
                double value = snip != null && i < snip.Length ? Math.Max(0.0, snip[i]) : 0.0;
                if (scale > 0.0)
                {
                    int backgroundChannel = i;
                    if (!sameCalibration)
                    {
                        double energy = foreground.EnergyCalibration.ChannelToEnergy(i);
                        backgroundChannel = Convert.ToInt32(background.EnergyCalibration.EnergyToChannel(energy, maxChannels: background.NumberOfChannels));
                    }

                    if (backgroundChannel >= 0 && backgroundChannel < background.NumberOfChannels)
                    {
                        value = Math.Max(value, scale * background.Spectrum[backgroundChannel]);
                    }
                }

                fixedBackground[i] = value;
            }

            return fixedBackground;
        }

        static FitComponent BuildFitComponent(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration, int channel, NuclideDefinition nuclide, string label)
        {
            double fwhm = fwhmCalibration.ChannelToFwhm(channel);
            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                return null;
            }

            double left = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
            double right = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
            if (!PeakShapeModel.IsFinite(left) || !PeakShapeModel.IsFinite(right))
            {
                return null;
            }

            int start = Math.Max(0, channel - Convert.ToInt32(Math.Ceiling(left)));
            int end = Math.Min(spectrum.NumberOfChannels - 1, channel + Convert.ToInt32(Math.Ceiling(right)));
            if (start > end)
            {
                return null;
            }

            double[] profile = new double[end - start + 1];
            for (int ch = start; ch <= end; ch++)
            {
                profile[ch - start] = PeakShapeModel.RelativeValue(ch - channel, fwhm, fwhmCalibration);
            }

            return new FitComponent
            {
                Nuclide = nuclide,
                Label = label,
                Channel = channel,
                Fwhm = fwhm,
                Start = start,
                Profile = profile,
                Amplitude = 0.0
            };
        }

        // Композитный компонент bound-группы: profile = Σ w_i·profile_i,
        // w_i = I_i/ΣI (амплитуда группы = суммарная площадь линий).
        static FitComponent BuildGroupComponent(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration, List<LineSite> members)
        {
            List<FitComponent> memberComponents = new List<FitComponent>();
            foreach (LineSite member in members)
            {
                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, member.Channel, member.Nuclide, null);
                if (component == null)
                {
                    return null;
                }

                memberComponents.Add(component);
            }

            double intensitySum = members.Sum(m => m.Intensity);
            if (intensitySum <= 0.0)
            {
                return null;
            }

            double[] weights = members.Select(m => m.Intensity / intensitySum).ToArray();
            int start = memberComponents.Min(c => c.Start);
            int end = memberComponents.Max(c => c.Start + c.Profile.Length - 1);
            double[] profile = new double[end - start + 1];
            for (int i = 0; i < memberComponents.Count; i++)
            {
                FitComponent component = memberComponents[i];
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    profile[component.Start + j - start] += weights[i] * component.Profile[j];
                }
            }

            // Центр группы — линия с максимальным весом (для диагностики).
            int strongestIndex = Array.IndexOf(weights, weights.Max());
            return new FitComponent
            {
                Nuclide = members[strongestIndex].Nuclide,
                Channel = members[strongestIndex].Channel,
                Fwhm = members[strongestIndex].Fwhm,
                Start = start,
                Profile = profile,
                Amplitude = 0.0,
                GroupMembers = members,
                GroupWeights = weights
            };
        }

        // Максимальный z по членам группы (для критерия принятия).
        static double BestMemberZ(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration, FitComponent groupComponent, double[] lambda)
        {
            double best = 0.0;
            for (int i = 0; i < groupComponent.GroupMembers.Count; i++)
            {
                LineSite member = groupComponent.GroupMembers[i];
                FitComponent memberComponent = BuildFitComponent(spectrum, fwhmCalibration, member.Channel, member.Nuclide, null);
                if (memberComponent == null)
                {
                    continue;
                }

                memberComponent.Amplitude = groupComponent.Amplitude * groupComponent.GroupWeights[i];
                double z = FisherZ(memberComponent, lambda);
                if (z > best)
                {
                    best = z;
                }
            }

            return best;
        }

        static double[] BuildLambda(double[] fixedBackground, List<FitComponent> components, int channels)
        {
            double[] lambda = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                lambda[i] = Math.Max(1E-6, fixedBackground[i]);
            }

            foreach (FitComponent component in components)
            {
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    lambda[component.Start + j] += component.Amplitude * component.Profile[j];
                }
            }

            return lambda;
        }

        static double PoissonDeviance(int[] observed, double[] lambda, int chMin, int chMax)
        {
            double deviance = 0.0;
            for (int i = chMin; i <= chMax; i++)
            {
                double mu = Math.Max(1E-9, lambda[i]);
                int k = observed[i];
                deviance += k > 0
                    ? 2.0 * (k * Math.Log(k / mu) - (k - mu))
                    : 2.0 * mu;
            }

            return deviance;
        }

        static double LocalLogLikelihoodDelta(int[] observed, double[] lambda, FitComponent component, double amplitudeDelta, int chMin, int chMax)
        {
            double delta = 0.0;
            for (int j = 0; j < component.Profile.Length; j++)
            {
                int ch = component.Start + j;
                if (ch < chMin || ch > chMax)
                {
                    continue;
                }

                double p = component.Profile[j];
                if (p <= 0.0)
                {
                    continue;
                }

                double mu = lambda[ch];
                double muNew = mu + amplitudeDelta * p;
                if (muNew <= 1E-9)
                {
                    return Double.NegativeInfinity;
                }

                delta += observed[ch] > 0
                    ? observed[ch] * Math.Log(muNew / mu) - (muNew - mu)
                    : -(muNew - mu);
            }

            return delta;
        }

        static void ApplyAmplitudeDelta(double[] lambda, FitComponent component, double amplitudeDelta)
        {
            for (int j = 0; j < component.Profile.Length; j++)
            {
                lambda[component.Start + j] += amplitudeDelta * component.Profile[j];
            }

            component.Amplitude += amplitudeDelta;
        }

        // Полный фит модели «с нуля» (амплитуды сбрасываются) — используется
        // и для базовой модели, и для AIC-проб bound-групп.
        static double FitModel(int[] observed, double[] fixedBackground, List<FitComponent> components, int chMin, int chMax)
        {
            foreach (FitComponent component in components)
            {
                component.Amplitude = 0.0;
            }

            return FitAmplitudes(observed, fixedBackground, components, chMin, chMax, FitIterations);
        }

        // Координатный Пуассон-спуск с matched-инициализацией (oracle-режим).
        static double FitAmplitudes(int[] observed, double[] fixedBackground, List<FitComponent> components, int chMin, int chMax, int iterations)
        {
            double[] lambda = BuildLambda(fixedBackground, components, observed.Length);

            foreach (FitComponent component in components)
            {
                double numerator = 0.0;
                double denominator = 0.0;
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    int ch = component.Start + j;
                    double p = component.Profile[j];
                    numerator += (observed[ch] - lambda[ch]) * p;
                    denominator += p * p;
                }

                double initial = denominator > 0.0 ? Math.Max(0.0, numerator / denominator) : 0.0;
                if (initial > 0.0)
                {
                    ApplyAmplitudeDelta(lambda, component, initial);
                }
            }

            double stepScale = 1.0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool improved = false;
                foreach (FitComponent component in components)
                {
                    double step = Math.Max(0.5, component.Amplitude * 0.10) * stepScale;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        double delta = direction * step;
                        if (component.Amplitude + delta < 0.0)
                        {
                            delta = -component.Amplitude;
                            if (delta == 0.0)
                            {
                                continue;
                            }
                        }

                        double gain = LocalLogLikelihoodDelta(observed, lambda, component, delta, chMin, chMax);
                        if (gain > 1E-9)
                        {
                            ApplyAmplitudeDelta(lambda, component, delta);
                            improved = true;
                            break;
                        }
                    }
                }

                if (improved)
                {
                    stepScale = Math.Min(1.0, stepScale * 1.25);
                }
                else
                {
                    stepScale *= 0.5;
                    if (stepScale < 1E-5)
                    {
                        break;
                    }
                }
            }

            return PoissonDeviance(observed, lambda, chMin, chMax);
        }

        // z = A·sqrt(I), I = Σ p²/max(1, λ) — информация Фишера амплитуды при
        // Пуассон-шуме; z сопоставим по смыслу с SNR finder'а.
        static double FisherZ(FitComponent component, double[] lambda)
        {
            if (component.Amplitude <= 0.0)
            {
                return 0.0;
            }

            double information = 0.0;
            for (int j = 0; j < component.Profile.Length; j++)
            {
                double p = component.Profile[j];
                information += p * p / Math.Max(1.0, lambda[component.Start + j]);
            }

            return component.Amplitude * Math.Sqrt(information);
        }
    }
}
