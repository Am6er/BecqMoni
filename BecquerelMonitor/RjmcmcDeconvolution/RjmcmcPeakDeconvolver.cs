using BecquerelMonitor.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal static class RjmcmcPeakDeconvolver
    {
        enum MoveKind
        {
            Birth,
            Death,
            UpdateExtra,
            UpdateAnchorAmplitude,
            UpdateBackground
        }

        sealed class MoveProbabilities
        {
            public double Birth;
            public double Death;
            public double UpdateExtra;
            public double UpdateAnchorAmplitude;
            public double UpdateBackground;
        }

        sealed class RjmcmcPeakComponent
        {
            public int Channel;
            public double Fwhm;
            public double Amplitude;

            public RjmcmcPeakComponent Clone()
            {
                return new RjmcmcPeakComponent
                {
                    Channel = Channel,
                    Fwhm = Fwhm,
                    Amplitude = Amplitude
                };
            }
        }

        sealed class RjmcmcState
        {
            public double BackgroundIntercept;
            public double BackgroundSlope;
            public List<RjmcmcPeakComponent> Anchors;
            public List<RjmcmcPeakComponent> Extras;
            public double LogLikelihood;
            public double LogPosterior;

            public RjmcmcState Clone()
            {
                RjmcmcState clone = new RjmcmcState
                {
                    BackgroundIntercept = BackgroundIntercept,
                    BackgroundSlope = BackgroundSlope,
                    LogLikelihood = LogLikelihood,
                    LogPosterior = LogPosterior,
                    Anchors = new List<RjmcmcPeakComponent>(),
                    Extras = new List<RjmcmcPeakComponent>()
                };

                foreach (RjmcmcPeakComponent anchor in Anchors)
                {
                    clone.Anchors.Add(anchor.Clone());
                }

                foreach (RjmcmcPeakComponent extra in Extras)
                {
                    clone.Extras.Add(extra.Clone());
                }

                return clone;
            }
        }

        sealed class BirthProposal
        {
            public int Channel;
            public double Probability;
        }

        public static RjmcmcResult Run(
            EnergySpectrum inferenceSpectrum,
            FWHMPeakDetector.PeakFinder finder,
            FWHMPeakDetectionMethodConfig peakConfig,
            FwhmCalibration fwhmCalibration)
        {
            RjmcmcResult result = new RjmcmcResult();
            RjmcmcConfig config = RjmcmcConfig.CreateForRoiSearch();
            if (!config.Enabled ||
                inferenceSpectrum == null ||
                peakConfig == null ||
                fwhmCalibration == null)
            {
                return result;
            }

            List<RjmcmcRoi> rois = BuildRois(inferenceSpectrum, finder, peakConfig, fwhmCalibration, config);
            int roiSeed = config.Seed;
            foreach (RjmcmcRoi roi in rois)
            {
                if (result.ProcessedRois.Count >= config.MaxRois)
                {
                    break;
                }

                if (roi == null || roi.Width < 5)
                {
                    continue;
                }

                result.ProcessedRois.Add(roi);
                List<RjmcmcPeakCandidate> extras = ProcessRoi(
                    inferenceSpectrum,
                    fwhmCalibration,
                    roi,
                    config,
                    roiSeed++);
                result.ExtraCandidates.AddRange(extras);
            }

            return result;
        }

        static List<RjmcmcRoi> BuildRois(
            EnergySpectrum spectrum,
            FWHMPeakDetector.PeakFinder finder,
            FWHMPeakDetectionMethodConfig peakConfig,
            FwhmCalibration fwhmCalibration,
            RjmcmcConfig config)
        {
            List<RjmcmcRoi> rawRois = new List<RjmcmcRoi>();
            if (finder?.centroids == null || finder.centroids.Length == 0)
            {
                return rawRois;
            }

            int minChannel = Convert.ToInt32(spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Min_Range, maxChannels: spectrum.NumberOfChannels));
            int maxChannel = Convert.ToInt32(spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Max_Range, maxChannels: spectrum.NumberOfChannels));
            minChannel = Math.Max(0, Math.Min(spectrum.NumberOfChannels - 1, minChannel));
            maxChannel = Math.Max(0, Math.Min(spectrum.NumberOfChannels - 1, maxChannel));
            if (maxChannel < minChannel)
            {
                int swap = minChannel;
                minChannel = maxChannel;
                maxChannel = swap;
            }

            foreach (double centroidValue in finder.centroids)
            {
                int centroid = Math.Max(minChannel, Math.Min(maxChannel, Convert.ToInt32(Math.Round(centroidValue))));
                double fwhm = fwhmCalibration.ChannelToFwhm(centroid);
                if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
                {
                    continue;
                }

                int radius = Math.Max(3, Convert.ToInt32(Math.Ceiling(config.RoiRadiusFwhm * fwhm)));
                RjmcmcRoi roi = new RjmcmcRoi
                {
                    StartChannel = Math.Max(minChannel, centroid - radius),
                    EndChannel = Math.Min(maxChannel, centroid + radius)
                };
                roi.AnchorChannels.Add(centroid);
                rawRois.Add(roi);
            }

            rawRois.Sort((a, b) => a.StartChannel.CompareTo(b.StartChannel));
            List<RjmcmcRoi> merged = new List<RjmcmcRoi>();
            foreach (RjmcmcRoi roi in rawRois)
            {
                if (merged.Count == 0)
                {
                    merged.Add(roi);
                    continue;
                }

                RjmcmcRoi current = merged[merged.Count - 1];
                if (roi.StartChannel <= current.EndChannel + 1)
                {
                    current.EndChannel = Math.Max(current.EndChannel, roi.EndChannel);
                    foreach (int anchorChannel in roi.AnchorChannels)
                    {
                        if (!current.AnchorChannels.Contains(anchorChannel))
                        {
                            current.AnchorChannels.Add(anchorChannel);
                        }
                    }
                }
                else
                {
                    merged.Add(roi);
                }
            }

            List<RjmcmcRoi> bounded = new List<RjmcmcRoi>();
            foreach (RjmcmcRoi roi in merged)
            {
                foreach (RjmcmcRoi boundedRoi in SplitOrBoundRoi(roi, minChannel, maxChannel, config))
                {
                    boundedRoi.AnchorChannels.Sort();
                    if (!bounded.Any(existing =>
                        existing.StartChannel == boundedRoi.StartChannel &&
                        existing.EndChannel == boundedRoi.EndChannel &&
                        existing.AnchorChannels.SequenceEqual(boundedRoi.AnchorChannels)))
                    {
                        bounded.Add(boundedRoi);
                    }
                }
            }

            return bounded;
        }

        static IEnumerable<RjmcmcRoi> SplitOrBoundRoi(
            RjmcmcRoi roi,
            int minChannel,
            int maxChannel,
            RjmcmcConfig config)
        {
            if (roi.Width <= config.MaxChannelsPerRoi && roi.AnchorChannels.Count <= config.MaxAnchorsPerRoi)
            {
                yield return roi;
                yield break;
            }

            foreach (int anchorChannel in roi.AnchorChannels)
            {
                int halfWidth = Math.Max(2, config.MaxChannelsPerRoi / 2);
                int start = Math.Max(minChannel, anchorChannel - halfWidth);
                int end = Math.Min(maxChannel, start + config.MaxChannelsPerRoi - 1);
                start = Math.Max(minChannel, end - config.MaxChannelsPerRoi + 1);

                RjmcmcRoi bounded = new RjmcmcRoi
                {
                    StartChannel = start,
                    EndChannel = end
                };

                foreach (int channel in roi.AnchorChannels)
                {
                    if (channel >= bounded.StartChannel && channel <= bounded.EndChannel)
                    {
                        bounded.AnchorChannels.Add(channel);
                    }
                }

                if (bounded.AnchorChannels.Count > 0 && bounded.AnchorChannels.Count <= config.MaxAnchorsPerRoi)
                {
                    yield return bounded;
                }
            }
        }

        static List<RjmcmcPeakCandidate> ProcessRoi(
            EnergySpectrum inferenceSpectrum,
            FwhmCalibration fwhmCalibration,
            RjmcmcRoi roi,
            RjmcmcConfig config,
            int seed)
        {
            int[] observed = ExtractObserved(inferenceSpectrum, roi);
            if (observed.Length < 5 || observed.Max() <= 0 || config.MaxExtraPeaksPerRoi <= 0)
            {
                return new List<RjmcmcPeakCandidate>();
            }

            int chainCount = Math.Max(1, config.ChainCount);
            Task<List<RjmcmcPeakCandidate>>[] chainTasks = new Task<List<RjmcmcPeakCandidate>>[chainCount];
            for (int chainIndex = 0; chainIndex < chainTasks.Length; chainIndex++)
            {
                int currentChainIndex = chainIndex;
                chainTasks[currentChainIndex] = Task.Factory.StartNew(
                    () => ProcessRoiChain(
                        observed,
                        fwhmCalibration,
                        roi,
                        config,
                        seed + currentChainIndex * 7919),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            Task.WaitAll(chainTasks);
            return MergeCandidates(chainTasks.Select(task => task.Result), config.MaxExtraPeaksPerRoi);
        }

        static List<RjmcmcPeakCandidate> ProcessRoiChain(
            int[] observed,
            FwhmCalibration fwhmCalibration,
            RjmcmcRoi roi,
            RjmcmcConfig config,
            int seed)
        {
            List<RjmcmcPeakCandidate> candidates = new List<RjmcmcPeakCandidate>();
            Random random = new Random(seed);
            double amplitudeScale = EstimateAmplitudeScale(observed);
            RjmcmcState current = CreateInitialState(roi, observed, fwhmCalibration);
            if (!EvaluateState(current, roi, observed, fwhmCalibration, amplitudeScale, config))
            {
                return candidates;
            }

            RjmcmcState best = current.Clone();
            int iterations = config.BurnIn + config.Samples;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                MoveProbabilities moveProbabilities = GetMoveProbabilities(current.Extras.Count, current.Anchors.Count, config.MaxExtraPeaksPerRoi);
                MoveKind move = DrawMove(moveProbabilities, random.NextDouble());
                RjmcmcState proposal = null;
                double logProposalRatio = 0.0;
                switch (move)
                {
                    case MoveKind.Birth:
                        proposal = ProposeBirth(current, roi, observed, fwhmCalibration, amplitudeScale, config, moveProbabilities, random, out logProposalRatio);
                        break;
                    case MoveKind.Death:
                        proposal = ProposeDeath(current, roi, observed, fwhmCalibration, amplitudeScale, config, moveProbabilities, random, out logProposalRatio);
                        break;
                    case MoveKind.UpdateExtra:
                        proposal = ProposeUpdateExtra(current, roi, fwhmCalibration, amplitudeScale, config, random);
                        break;
                    case MoveKind.UpdateAnchorAmplitude:
                        proposal = ProposeUpdateAnchorAmplitude(current, amplitudeScale, random);
                        break;
                    case MoveKind.UpdateBackground:
                        proposal = ProposeUpdateBackground(current, roi, observed, config, random);
                        break;
                }

                if (proposal == null || !EvaluateState(proposal, roi, observed, fwhmCalibration, amplitudeScale, config))
                {
                    continue;
                }

                double logAcceptance = proposal.LogPosterior - current.LogPosterior + logProposalRatio;
                if (Math.Log(Math.Max(Double.Epsilon, random.NextDouble())) <= logAcceptance)
                {
                    current = proposal;
                }

                if (iteration >= config.BurnIn && current.LogPosterior > best.LogPosterior)
                {
                    best = current.Clone();
                }
            }

            candidates.AddRange(CollectCandidates(best, roi, observed, fwhmCalibration, amplitudeScale, config));
            return candidates;
        }

        static List<RjmcmcPeakCandidate> MergeCandidates(
            IEnumerable<List<RjmcmcPeakCandidate>> chainCandidates,
            int maxCandidateCount)
        {
            List<RjmcmcPeakCandidate> merged = new List<RjmcmcPeakCandidate>();
            foreach (RjmcmcPeakCandidate candidate in chainCandidates
                .Where(list => list != null)
                .SelectMany(list => list)
                .OrderByDescending(candidate => candidate.DevianceImprovement))
            {
                bool duplicate = false;
                foreach (RjmcmcPeakCandidate existing in merged)
                {
                    double duplicateDistance = Math.Max(1.0, 0.20 * Math.Max(existing.Fwhm, candidate.Fwhm));
                    if (Math.Abs(existing.Channel - candidate.Channel) <= duplicateDistance)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    merged.Add(candidate);
                    if (merged.Count >= maxCandidateCount)
                    {
                        break;
                    }
                }
            }

            return merged.OrderBy(candidate => candidate.Channel).ToList();
        }

        static int[] ExtractObserved(EnergySpectrum spectrum, RjmcmcRoi roi)
        {
            int[] observed = new int[roi.Width];
            for (int i = 0; i < observed.Length; i++)
            {
                observed[i] = spectrum.Spectrum[roi.StartChannel + i];
            }
            return observed;
        }

        static RjmcmcState CreateInitialState(RjmcmcRoi roi, int[] observed, FwhmCalibration fwhmCalibration)
        {
            RjmcmcState state = new RjmcmcState
            {
                Anchors = new List<RjmcmcPeakComponent>(),
                Extras = new List<RjmcmcPeakComponent>()
            };

            EstimateBackground(observed, out double leftMean, out double rightMean);
            int width = Math.Max(1, observed.Length - 1);
            state.BackgroundIntercept = Math.Max(0.1, leftMean);
            state.BackgroundSlope = (rightMean - leftMean) / width;

            foreach (int anchorChannel in roi.AnchorChannels)
            {
                int localIndex = anchorChannel - roi.StartChannel;
                localIndex = Math.Max(0, Math.Min(observed.Length - 1, localIndex));
                double background = BackgroundAt(state, localIndex);
                double amplitude = Math.Max(1.0, observed[localIndex] - background);
                state.Anchors.Add(new RjmcmcPeakComponent
                {
                    Channel = anchorChannel,
                    Fwhm = Math.Max(1.0, fwhmCalibration.ChannelToFwhm(anchorChannel)),
                    Amplitude = amplitude
                });
            }

            return state;
        }

        static void EstimateBackground(int[] observed, out double leftMean, out double rightMean)
        {
            int edgeWidth = Math.Max(2, observed.Length / 8);
            leftMean = 0.0;
            rightMean = 0.0;
            for (int i = 0; i < edgeWidth; i++)
            {
                leftMean += observed[i];
                rightMean += observed[observed.Length - 1 - i];
            }
            leftMean /= edgeWidth;
            rightMean /= edgeWidth;
        }

        static bool EvaluateState(
            RjmcmcState state,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            double amplitudeScale,
            RjmcmcConfig config)
        {
            if (state == null)
            {
                return false;
            }

            double leftBackground = BackgroundAt(state, 0);
            double rightBackground = BackgroundAt(state, observed.Length - 1);
            if (leftBackground < 0.0 || rightBackground < 0.0)
            {
                return false;
            }

            double[] lambda = ArrayPool<double>.Shared.Rent(observed.Length);
            try
            {
                if (!TryBuildLambdaArray(state, roi, observed.Length, fwhmCalibration, lambda))
                {
                    return false;
                }

                double logLikelihood = 0.0;
                for (int i = 0; i < observed.Length; i++)
                {
                    double lambdaValue = lambda[i];
                    if (!PeakShapeModel.IsFinite(lambdaValue) || lambdaValue <= 0.0)
                    {
                        return false;
                    }

                    int observedValue = observed[i];
                    logLikelihood += observedValue > 0
                        ? observedValue * Math.Log(lambdaValue) - lambdaValue
                        : -lambdaValue;
                }

                double logPrior = 0.0;
                foreach (RjmcmcPeakComponent anchor in state.Anchors)
                {
                    if (anchor.Amplitude <= 0.0 || !PeakShapeModel.IsFinite(anchor.Amplitude))
                    {
                        return false;
                    }

                    logPrior += LogExponentialPdf(anchor.Amplitude, amplitudeScale);
                }

                foreach (RjmcmcPeakComponent extra in state.Extras)
                {
                    if (extra.Amplitude <= 0.0 || !PeakShapeModel.IsFinite(extra.Amplitude))
                    {
                        return false;
                    }

                    logPrior += LogExponentialPdf(extra.Amplitude, amplitudeScale);
                    logPrior -= config.ExtraPeakPenalty;
                }

                state.LogLikelihood = logLikelihood;
                state.LogPosterior = logLikelihood + logPrior;
                return PeakShapeModel.IsFinite(state.LogPosterior);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(lambda);
            }
        }

        static bool TryBuildLambdaArray(
            RjmcmcState state,
            RjmcmcRoi roi,
            int length,
            FwhmCalibration fwhmCalibration,
            double[] lambda)
        {
            for (int i = 0; i < length; i++)
            {
                double background = BackgroundAt(state, i);
                if (!PeakShapeModel.IsFinite(background))
                {
                    return false;
                }

                lambda[i] = Math.Max(1E-6, background);
            }

            foreach (RjmcmcPeakComponent anchor in state.Anchors)
            {
                if (!TryAddComponent(lambda, length, roi, anchor, fwhmCalibration))
                {
                    return false;
                }
            }

            foreach (RjmcmcPeakComponent extra in state.Extras)
            {
                if (!TryAddComponent(lambda, length, roi, extra, fwhmCalibration))
                {
                    return false;
                }
            }

            return true;
        }

        static bool TryAddComponent(
            double[] lambda,
            int length,
            RjmcmcRoi roi,
            RjmcmcPeakComponent component,
            FwhmCalibration fwhmCalibration)
        {
            if (component == null ||
                component.Amplitude <= 0.0 ||
                component.Fwhm <= 0.0 ||
                !PeakShapeModel.IsFinite(component.Amplitude) ||
                !PeakShapeModel.IsFinite(component.Fwhm))
            {
                return false;
            }

            double leftSupport = PeakShapeModel.GetLeftSupport(fwhmCalibration, component.Fwhm);
            double rightSupport = PeakShapeModel.GetRightSupport(fwhmCalibration, component.Fwhm);
            if (!PeakShapeModel.IsFinite(leftSupport) || !PeakShapeModel.IsFinite(rightSupport))
            {
                return false;
            }

            int startChannel = Math.Max(roi.StartChannel, component.Channel - Convert.ToInt32(Math.Ceiling(leftSupport)));
            int endChannel = Math.Min(roi.EndChannel, component.Channel + Convert.ToInt32(Math.Ceiling(rightSupport)));
            if (startChannel > endChannel)
            {
                return true;
            }

            for (int channel = startChannel; channel <= endChannel; channel++)
            {
                int localIndex = channel - roi.StartChannel;
                if (localIndex >= 0 && localIndex < length)
                {
                    lambda[localIndex] += PeakShapeModel.Evaluate(
                        channel,
                        component.Amplitude,
                        component.Channel,
                        component.Fwhm,
                        fwhmCalibration);
                }
            }

            return true;
        }

        static double BackgroundAt(RjmcmcState state, int localIndex)
        {
            return state.BackgroundIntercept + state.BackgroundSlope * localIndex;
        }

        static double EstimateAmplitudeScale(int[] observed)
        {
            double max = observed.Max();
            return Math.Max(5.0, max * 0.75);
        }

        static MoveProbabilities GetMoveProbabilities(int extraCount, int anchorCount, int maxExtraCount)
        {
            MoveProbabilities probabilities = new MoveProbabilities();
            if (extraCount < maxExtraCount)
            {
                probabilities.Birth = 0.30;
            }
            if (extraCount > 0)
            {
                probabilities.Death = 0.20;
                probabilities.UpdateExtra = 0.25;
            }

            if (anchorCount > 0)
            {
                probabilities.UpdateAnchorAmplitude = 0.15;
            }
            probabilities.UpdateBackground = 0.10;
            Normalize(probabilities);
            return probabilities;
        }

        static void Normalize(MoveProbabilities probabilities)
        {
            double total =
                probabilities.Birth +
                probabilities.Death +
                probabilities.UpdateExtra +
                probabilities.UpdateAnchorAmplitude +
                probabilities.UpdateBackground;
            if (total <= 0.0)
            {
                probabilities.UpdateAnchorAmplitude = 1.0;
                total = 1.0;
            }

            probabilities.Birth /= total;
            probabilities.Death /= total;
            probabilities.UpdateExtra /= total;
            probabilities.UpdateAnchorAmplitude /= total;
            probabilities.UpdateBackground /= total;
        }

        static MoveKind DrawMove(MoveProbabilities probabilities, double value)
        {
            double cumulative = probabilities.Birth;
            if (value < cumulative)
            {
                return MoveKind.Birth;
            }

            cumulative += probabilities.Death;
            if (value < cumulative)
            {
                return MoveKind.Death;
            }

            cumulative += probabilities.UpdateExtra;
            if (value < cumulative)
            {
                return MoveKind.UpdateExtra;
            }

            cumulative += probabilities.UpdateAnchorAmplitude;
            if (value < cumulative)
            {
                return MoveKind.UpdateAnchorAmplitude;
            }

            return MoveKind.UpdateBackground;
        }

        static RjmcmcState ProposeBirth(
            RjmcmcState current,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            double amplitudeScale,
            RjmcmcConfig config,
            MoveProbabilities forwardProbabilities,
            Random random,
            out double logProposalRatio)
        {
            logProposalRatio = 0.0;
            if (current.Extras.Count >= config.MaxExtraPeaksPerRoi)
            {
                return null;
            }

            BirthProposal birthProposal = DrawBirthChannel(current, roi, observed, fwhmCalibration, random);
            if (birthProposal == null || birthProposal.Probability <= 0.0)
            {
                return null;
            }

            double amplitude = SampleExponential(random, amplitudeScale);
            if (amplitude <= 0.0)
            {
                return null;
            }

            RjmcmcState proposal = current.Clone();
            proposal.Extras.Add(new RjmcmcPeakComponent
            {
                Channel = birthProposal.Channel,
                Fwhm = Math.Max(1.0, fwhmCalibration.ChannelToFwhm(birthProposal.Channel)),
                Amplitude = amplitude
            });

            MoveProbabilities reverseProbabilities = GetMoveProbabilities(proposal.Extras.Count, proposal.Anchors.Count, config.MaxExtraPeaksPerRoi);
            double deathSelectionProbability = 1.0 / proposal.Extras.Count;
            logProposalRatio =
                Math.Log(reverseProbabilities.Death) +
                Math.Log(deathSelectionProbability) -
                Math.Log(forwardProbabilities.Birth) -
                Math.Log(birthProposal.Probability) -
                LogExponentialPdf(amplitude, amplitudeScale);
            return proposal;
        }

        static RjmcmcState ProposeDeath(
            RjmcmcState current,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            double amplitudeScale,
            RjmcmcConfig config,
            MoveProbabilities forwardProbabilities,
            Random random,
            out double logProposalRatio)
        {
            logProposalRatio = 0.0;
            if (current.Extras.Count == 0)
            {
                return null;
            }

            int index = random.Next(current.Extras.Count);
            RjmcmcPeakComponent removed = current.Extras[index].Clone();
            RjmcmcState proposal = current.Clone();
            proposal.Extras.RemoveAt(index);

            MoveProbabilities reverseProbabilities = GetMoveProbabilities(proposal.Extras.Count, proposal.Anchors.Count, config.MaxExtraPeaksPerRoi);
            BirthProposal reverseBirth = GetBirthProbabilityForChannel(proposal, roi, observed, fwhmCalibration, removed.Channel);
            if (reverseBirth == null || reverseBirth.Probability <= 0.0)
            {
                return null;
            }

            double deathSelectionProbability = 1.0 / current.Extras.Count;
            logProposalRatio =
                Math.Log(reverseProbabilities.Birth) +
                Math.Log(reverseBirth.Probability) +
                LogExponentialPdf(removed.Amplitude, amplitudeScale) -
                Math.Log(forwardProbabilities.Death) -
                Math.Log(deathSelectionProbability);
            return proposal;
        }

        static RjmcmcState ProposeUpdateExtra(
            RjmcmcState current,
            RjmcmcRoi roi,
            FwhmCalibration fwhmCalibration,
            double amplitudeScale,
            RjmcmcConfig config,
            Random random)
        {
            if (current.Extras.Count == 0)
            {
                return null;
            }

            int index = random.Next(current.Extras.Count);
            RjmcmcState proposal = current.Clone();
            RjmcmcPeakComponent component = proposal.Extras[index];
            int channelStep = Convert.ToInt32(Math.Round(SampleNormal(random) * config.CenterUpdateSigmaFwhm * component.Fwhm));
            double amplitudeStep = SampleNormal(random) * amplitudeScale * 0.10;
            int newChannel = component.Channel + channelStep;
            double newAmplitude = component.Amplitude + amplitudeStep;
            if (newChannel < roi.StartChannel || newChannel > roi.EndChannel || newAmplitude <= 0.0)
            {
                return null;
            }

            component.Channel = newChannel;
            component.Fwhm = Math.Max(1.0, fwhmCalibration.ChannelToFwhm(newChannel));
            component.Amplitude = newAmplitude;
            return proposal;
        }

        static RjmcmcState ProposeUpdateAnchorAmplitude(RjmcmcState current, double amplitudeScale, Random random)
        {
            if (current.Anchors.Count == 0)
            {
                return null;
            }

            int index = random.Next(current.Anchors.Count);
            RjmcmcState proposal = current.Clone();
            RjmcmcPeakComponent component = proposal.Anchors[index];
            double amplitudeStep = SampleNormal(random) * amplitudeScale * 0.10;
            double newAmplitude = component.Amplitude + amplitudeStep;
            if (newAmplitude <= 0.0)
            {
                return null;
            }

            component.Amplitude = newAmplitude;
            return proposal;
        }

        static RjmcmcState ProposeUpdateBackground(
            RjmcmcState current,
            RjmcmcRoi roi,
            int[] observed,
            RjmcmcConfig config,
            Random random)
        {
            RjmcmcState proposal = current.Clone();
            double meanObserved = observed.Average();
            double width = Math.Max(1.0, roi.Width - 1);
            double backgroundStep = Math.Max(1.0, meanObserved * config.BackgroundUpdateFraction);
            proposal.BackgroundIntercept += SampleNormal(random) * backgroundStep;
            proposal.BackgroundSlope += SampleNormal(random) * backgroundStep / width;
            if (BackgroundAt(proposal, 0) < 0.0 || BackgroundAt(proposal, roi.Width - 1) < 0.0)
            {
                return null;
            }

            return proposal;
        }

        static BirthProposal DrawBirthChannel(
            RjmcmcState state,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            Random random)
        {
            double[] probabilities = BuildBirthProbabilities(state, roi, observed, fwhmCalibration);
            double sample = random.NextDouble();
            double cumulative = 0.0;
            for (int i = 0; i < probabilities.Length; i++)
            {
                cumulative += probabilities[i];
                if (sample <= cumulative)
                {
                    return new BirthProposal
                    {
                        Channel = roi.StartChannel + i,
                        Probability = probabilities[i]
                    };
                }
            }

            return new BirthProposal
            {
                Channel = roi.EndChannel,
                Probability = probabilities[probabilities.Length - 1]
            };
        }

        static BirthProposal GetBirthProbabilityForChannel(
            RjmcmcState state,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            int channel)
        {
            int index = channel - roi.StartChannel;
            if (index < 0 || index >= roi.Width)
            {
                return null;
            }

            double[] probabilities = BuildBirthProbabilities(state, roi, observed, fwhmCalibration);
            return new BirthProposal
            {
                Channel = channel,
                Probability = probabilities[index]
            };
        }

        static double[] BuildBirthProbabilities(
            RjmcmcState state,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration)
        {
            double[] weights = new double[roi.Width];
            double[] lambda = ArrayPool<double>.Shared.Rent(roi.Width);
            try
            {
                if (!TryBuildLambdaArray(state, roi, roi.Width, fwhmCalibration, lambda))
                {
                    return UniformBirthProbabilities(weights);
                }

                HashSet<int> occupiedChannels = new HashSet<int>();
                foreach (RjmcmcPeakComponent anchor in state.Anchors)
                {
                    occupiedChannels.Add(anchor.Channel);
                }
                foreach (RjmcmcPeakComponent extra in state.Extras)
                {
                    occupiedChannels.Add(extra.Channel);
                }

                double total = 0.0;
                for (int i = 0; i < roi.Width; i++)
                {
                    double residual = observed[i] - lambda[i];
                    double weight = 0.05 + Math.Max(0.0, residual);
                    int channel = roi.StartChannel + i;
                    if (occupiedChannels.Contains(channel))
                    {
                        weight = 0.0;
                    }

                    weights[i] = weight;
                    total += weight;
                }

                if (total <= 0.0)
                {
                    return UniformBirthProbabilities(weights);
                }

                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] /= total;
                }

                return weights;
            }
            finally
            {
                ArrayPool<double>.Shared.Return(lambda);
            }
        }

        static double[] UniformBirthProbabilities(double[] weights)
        {
            double uniform = 1.0 / weights.Length;
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = uniform;
            }
            return weights;
        }

        static List<RjmcmcPeakCandidate> CollectCandidates(
            RjmcmcState best,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            double amplitudeScale,
            RjmcmcConfig config)
        {
            List<RjmcmcPeakCandidate> candidates = new List<RjmcmcPeakCandidate>();
            if (best.Extras.Count == 0)
            {
                return candidates;
            }

            foreach (RjmcmcPeakComponent extra in best.Extras.OrderBy(component => component.Channel))
            {
                if (extra.Amplitude < Math.Max(config.MinimumCandidateAmplitude, amplitudeScale * 0.03))
                {
                    continue;
                }

                double improvement = ComputeDevianceImprovement(best, extra, roi, observed, fwhmCalibration, amplitudeScale, config);
                if (improvement < config.MinDevianceImprovement)
                {
                    continue;
                }

                int localIndex = extra.Channel - roi.StartChannel;
                localIndex = Math.Max(0, Math.Min(observed.Length - 1, localIndex));
                double background = Math.Max(1.0, BackgroundAt(best, localIndex));
                double snr = extra.Amplitude / Math.Sqrt(background + extra.Amplitude);
                candidates.Add(new RjmcmcPeakCandidate
                {
                    Channel = extra.Channel,
                    Fwhm = extra.Fwhm,
                    Amplitude = extra.Amplitude,
                    Snr = snr,
                    DevianceImprovement = improvement
                });
            }

            return candidates;
        }

        static double ComputeDevianceImprovement(
            RjmcmcState best,
            RjmcmcPeakComponent extra,
            RjmcmcRoi roi,
            int[] observed,
            FwhmCalibration fwhmCalibration,
            double amplitudeScale,
            RjmcmcConfig config)
        {
            RjmcmcState reduced = best.Clone();
            for (int i = 0; i < reduced.Extras.Count; i++)
            {
                if (reduced.Extras[i].Channel == extra.Channel &&
                    Math.Abs(reduced.Extras[i].Amplitude - extra.Amplitude) < 1E-6)
                {
                    reduced.Extras.RemoveAt(i);
                    break;
                }
            }

            if (!EvaluateState(reduced, roi, observed, fwhmCalibration, amplitudeScale, config))
            {
                return Double.PositiveInfinity;
            }

            return 2.0 * (best.LogLikelihood - reduced.LogLikelihood);
        }

        static double SampleNormal(Random random)
        {
            double u1 = Math.Max(Double.Epsilon, random.NextDouble());
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        static double SampleExponential(Random random, double scale)
        {
            if (scale <= 0.0)
            {
                return 0.0;
            }

            return -scale * Math.Log(Math.Max(Double.Epsilon, 1.0 - random.NextDouble()));
        }

        static double LogExponentialPdf(double value, double scale)
        {
            if (value <= 0.0 || scale <= 0.0)
            {
                return Double.NegativeInfinity;
            }

            return -Math.Log(scale) - value / scale;
        }
    }
}
