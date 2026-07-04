using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal static class RjmcmcPeakDeconvolver
    {
        internal sealed class RjmcmcRuntimeStatsSnapshot
        {
            public bool HasData { get; set; }
            public double LastRunElapsedMilliseconds { get; set; }
            public double AverageElapsedMilliseconds { get; set; }
            public int SampleCount { get; set; }
            public int Capacity { get; set; }
        }

        enum MoveKind
        {
            Birth,
            Death,
            UpdateExtra,
            UpdateAnchorAmplitude,
            UpdateBackground
        }

        struct MoveProbabilities
        {
            public double Birth;
            public double Death;
            public double UpdateExtra;
            public double UpdateAnchorAmplitude;
            public double UpdateBackground;
        }

        sealed class RjmcmcComponentProfile
        {
            public double Fwhm;
            public int StartIndex;
            public int EndIndex;
            public double[] RelativeValues;
            public bool IsValid;
        }

        sealed class RjmcmcRoiWorkspace
        {
            public RjmcmcRoi Roi;
            public int[] Observed;
            public double[] FixedBackground;
            public bool[] AnchorForbiddenChannels;
            public int AvailableNonAnchorChannelCount;
            public RjmcmcComponentProfile[] Profiles;
            public Dictionary<int, RjmcmcComponentProfile> HaloProfiles;
            public double MeanObserved;
            public double AmplitudeScale;
            public double LogAmplitudeScale;
            public int FixedCenterSigmaChannels;
            public bool IsUsable;

            public int Length
            {
                get
                {
                    return Observed.Length;
                }
            }
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
            public double BackgroundQuadratic;
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
                    BackgroundQuadratic = BackgroundQuadratic,
                    LogLikelihood = LogLikelihood,
                    LogPosterior = LogPosterior,
                    Anchors = new List<RjmcmcPeakComponent>(Anchors.Count),
                    Extras = new List<RjmcmcPeakComponent>(Extras.Count)
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

        sealed class RjmcmcChainResult
        {
            public List<RjmcmcPeakCandidate> RawCandidates;
            public double LogPosterior;
        }

        sealed class ProfileDevianceResult
        {
            public double Improvement;
            public double ResidualSnr;
            public double ResidualCorrelation;
        }

        const int RuntimeHistoryCapacity = 10;
        static readonly object runtimeStatsSync = new object();
        static readonly Queue<double> recentRunElapsedMilliseconds = new Queue<double>(RuntimeHistoryCapacity);
        static double recentRunElapsedMillisecondsSum;
        static double lastRunElapsedMilliseconds;

        sealed class RjmcmcOccupancyTracker
        {
            readonly int[] centerCounts;
            readonly List<int>[] centerSampleIds;

            public int SampleCount { get; private set; }

            public RjmcmcOccupancyTracker(int length)
            {
                centerCounts = new int[Math.Max(0, length)];
                centerSampleIds = new List<int>[centerCounts.Length];
            }

            public void Record(RjmcmcState state, RjmcmcRoiWorkspace workspace)
            {
                SampleCount++;
                if (state?.Extras == null || workspace == null)
                {
                    return;
                }

                foreach (RjmcmcPeakComponent extra in state.Extras)
                {
                    int localIndex = extra.Channel - workspace.Roi.StartChannel;
                    if (localIndex >= 0 && localIndex < centerCounts.Length)
                    {
                        centerCounts[localIndex]++;
                        if (centerSampleIds[localIndex] == null)
                        {
                            centerSampleIds[localIndex] = new List<int>();
                        }

                        centerSampleIds[localIndex].Add(SampleCount);
                    }
                }
            }

            public double FractionNear(int channel, RjmcmcRoiWorkspace workspace, int tolerance)
            {
                if (SampleCount <= 0 || workspace == null)
                {
                    return 0.0;
                }

                int count = CountDistinctSamplesNear(channel, workspace, tolerance);
                return Math.Min(1.0, count / (double)SampleCount);
            }

            public double CenterStdDevNear(int channel, RjmcmcRoiWorkspace workspace, int tolerance)
            {
                if (SampleCount <= 0 || workspace == null)
                {
                    return Double.PositiveInfinity;
                }

                int centerLocal = channel - workspace.Roi.StartChannel;
                int start = Math.Max(0, centerLocal - tolerance);
                int end = Math.Min(centerCounts.Length - 1, centerLocal + tolerance);
                Dictionary<int, int> nearestLocalIndexBySample = new Dictionary<int, int>();
                for (int localIndex = start; localIndex <= end; localIndex++)
                {
                    List<int> sampleIds = centerSampleIds[localIndex];
                    if (sampleIds == null || sampleIds.Count == 0)
                    {
                        continue;
                    }

                    foreach (int sampleId in sampleIds)
                    {
                        if (!nearestLocalIndexBySample.TryGetValue(sampleId, out int existingLocalIndex) ||
                            Math.Abs(localIndex - centerLocal) < Math.Abs(existingLocalIndex - centerLocal))
                        {
                            nearestLocalIndexBySample[sampleId] = localIndex;
                        }
                    }
                }

                int count = nearestLocalIndexBySample.Count;
                double sum = 0.0;
                double sumSquares = 0.0;
                foreach (int localIndex in nearestLocalIndexBySample.Values)
                {
                    double absoluteChannel = workspace.Roi.StartChannel + localIndex;
                    sum += absoluteChannel;
                    sumSquares += absoluteChannel * absoluteChannel;
                }

                if (count <= 0)
                {
                    return Double.PositiveInfinity;
                }

                double mean = sum / count;
                double variance = Math.Max(0.0, sumSquares / count - mean * mean);
                return Math.Sqrt(variance);
            }

            int CountDistinctSamplesNear(int channel, RjmcmcRoiWorkspace workspace, int tolerance)
            {
                int centerLocal = channel - workspace.Roi.StartChannel;
                int start = Math.Max(0, centerLocal - tolerance);
                int end = Math.Min(centerCounts.Length - 1, centerLocal + tolerance);
                HashSet<int> sampleIds = new HashSet<int>();
                for (int localIndex = start; localIndex <= end; localIndex++)
                {
                    List<int> localSampleIds = centerSampleIds[localIndex];
                    if (localSampleIds == null)
                    {
                        continue;
                    }

                    foreach (int sampleId in localSampleIds)
                    {
                        sampleIds.Add(sampleId);
                    }
                }

                return sampleIds.Count;
            }
        }

        /// <summary>
        /// Runs local trans-dimensional peak deconvolution after deterministic FWHM peak search.
        /// References: Green (1995), "Reversible jump Markov chain Monte Carlo computation and Bayesian model determination",
        /// https://doi.org/10.1093/biomet/82.4.711; Gulam Razul, Fitzgerald and Andrieu (2003),
        /// "Bayesian model selection and parameter estimation of nuclear emission spectra using RJMCMC",
        /// https://doi.org/10.1016/S0168-9002(02)01807-7; Deep research report, sections "Bayesian variable-dimensional
        /// models: RJMCMC and exchange Monte Carlo" and "Implementation priorities".
        /// </summary>
        public static RjmcmcResult Run(
            EnergySpectrum foregroundSpectrum,
            EnergySpectrum fixedBackgroundSpectrum,
            FWHMPeakDetector.PeakFinder finder,
            FWHMPeakDetectionMethodConfig peakConfig,
            FwhmCalibration fwhmCalibration)
        {
            RjmcmcResult result = new RjmcmcResult();
            RjmcmcConfig config = RjmcmcConfig.CreateForRoiSearch(peakConfig);
            if (!config.Enabled ||
                foregroundSpectrum == null ||
                peakConfig == null ||
                fwhmCalibration == null)
            {
                return result;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                List<RjmcmcRoi> rois = SelectProcessableRois(BuildRois(foregroundSpectrum, finder, peakConfig, fwhmCalibration, config), config);
                List<RjmcmcPeakCandidate> extraCandidates = ProcessRois(foregroundSpectrum, fixedBackgroundSpectrum, fwhmCalibration, rois, config);
                result.ExtraCandidates.AddRange(extraCandidates);
                return result;
            }
            finally
            {
                stopwatch.Stop();
                RjmcmcRuntimeStatsSnapshot runtimeStats = RecordRuntime(stopwatch.Elapsed.TotalMilliseconds);
                result.LastRunElapsedMilliseconds = runtimeStats.LastRunElapsedMilliseconds;
                result.AverageElapsedMillisecondsLast10Runs = runtimeStats.AverageElapsedMilliseconds;
                result.AverageElapsedMillisecondsSampleCount = runtimeStats.SampleCount;
            }
        }

        internal static RjmcmcRuntimeStatsSnapshot GetRuntimeStatsSnapshot()
        {
            lock (runtimeStatsSync)
            {
                return CreateRuntimeStatsSnapshotUnsafe();
            }
        }

        static RjmcmcRuntimeStatsSnapshot RecordRuntime(double elapsedMilliseconds)
        {
            lock (runtimeStatsSync)
            {
                lastRunElapsedMilliseconds = elapsedMilliseconds;
                recentRunElapsedMilliseconds.Enqueue(elapsedMilliseconds);
                recentRunElapsedMillisecondsSum += elapsedMilliseconds;

                while (recentRunElapsedMilliseconds.Count > RuntimeHistoryCapacity)
                {
                    recentRunElapsedMillisecondsSum -= recentRunElapsedMilliseconds.Dequeue();
                }

                return CreateRuntimeStatsSnapshotUnsafe();
            }
        }

        static RjmcmcRuntimeStatsSnapshot CreateRuntimeStatsSnapshotUnsafe()
        {
            int sampleCount = recentRunElapsedMilliseconds.Count;
            bool hasData = sampleCount > 0;
            return new RjmcmcRuntimeStatsSnapshot
            {
                HasData = hasData,
                LastRunElapsedMilliseconds = hasData ? lastRunElapsedMilliseconds : 0.0,
                AverageElapsedMilliseconds = hasData ? recentRunElapsedMillisecondsSum / sampleCount : 0.0,
                SampleCount = sampleCount,
                Capacity = RuntimeHistoryCapacity
            };
        }

        /// <summary>
        /// Applies cheap structural checks to the FWHM-anchored search regions before workspace-level residual gating.
        /// References: Deep research report, sections "Bayesian variable-dimensional models" and "Implementation priorities",
        /// recommending RJMCMC only for small ambiguous ROIs rather than the whole spectrum; Gulam Razul et al. (2003),
        /// NIM A 497, 492-510.
        /// </summary>
        static List<RjmcmcRoi> SelectProcessableRois(List<RjmcmcRoi> rois, RjmcmcConfig config)
        {
            List<RjmcmcRoi> selected = new List<RjmcmcRoi>();
            foreach (RjmcmcRoi roi in rois)
            {
                if (roi == null || roi.Width < 5)
                {
                    continue;
                }

                selected.Add(roi);
            }

            return selected;
        }

        /// <summary>
        /// Builds FWHM-scaled ROIs around deterministic peaks, treating them as anchor components.
        /// References: Deep research report, sections "Role of FWHM and physical formulation" and "Implementation priorities";
        /// Gulam Razul et al. (2003), NIM A 497, 492-510, for nuclear spectra with unknown peak/background model order.
        /// </summary>
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
                roi.ReferenceAnchorChannels.Add(centroid);
                rawRois.Add(roi);
            }

            List<int> allAnchorChannels = rawRois
                .SelectMany(roi => roi.AnchorChannels)
                .Distinct()
                .OrderBy(channel => channel)
                .ToList();

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

                    foreach (int anchorChannel in roi.ReferenceAnchorChannels)
                    {
                        if (!current.ReferenceAnchorChannels.Contains(anchorChannel))
                        {
                            current.ReferenceAnchorChannels.Add(anchorChannel);
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
                foreach (RjmcmcRoi boundedRoi in SplitOrBoundRoi(roi, allAnchorChannels, minChannel, maxChannel, fwhmCalibration, config))
                {
                    boundedRoi.AnchorChannels.Sort();
                    boundedRoi.HaloAnchorChannels.Sort();
                    boundedRoi.ReferenceAnchorChannels.Sort();
                    if (!bounded.Any(existing =>
                        existing.StartChannel == boundedRoi.StartChannel &&
                        existing.EndChannel == boundedRoi.EndChannel &&
                        existing.AnchorChannels.SequenceEqual(boundedRoi.AnchorChannels) &&
                        existing.HaloAnchorChannels.SequenceEqual(boundedRoi.HaloAnchorChannels) &&
                        existing.ReferenceAnchorChannels.SequenceEqual(boundedRoi.ReferenceAnchorChannels)))
                    {
                        bounded.Add(boundedRoi);
                    }
                }
            }

            return bounded;
        }

        /// <summary>
        /// Splits overly wide or crowded ROIs so each sampler instance remains local and bounded.
        /// References: Deep research report, section "Implementation priorities"; Green (1995), Biometrika 82(4), 711-732,
        /// for dimension-changing model search where computation grows with local model dimension.
        /// </summary>
        static IEnumerable<RjmcmcRoi> SplitOrBoundRoi(
            RjmcmcRoi roi,
            IList<int> allAnchorChannels,
            int minChannel,
            int maxChannel,
            FwhmCalibration fwhmCalibration,
            RjmcmcConfig config)
        {
            List<int> anchorChannels = roi?.AnchorChannels?
                .Distinct()
                .OrderBy(channel => channel)
                .ToList();
            if (anchorChannels == null || anchorChannels.Count == 0)
            {
                yield break;
            }

            foreach (RjmcmcRoi bounded in SplitAnchorGroup(
                anchorChannels,
                allAnchorChannels,
                minChannel,
                maxChannel,
                fwhmCalibration,
                config))
            {
                yield return bounded;
            }
        }

        static IEnumerable<RjmcmcRoi> SplitAnchorGroup(
            List<int> anchorChannels,
            IList<int> allAnchorChannels,
            int minChannel,
            int maxChannel,
            FwhmCalibration fwhmCalibration,
            RjmcmcConfig config)
        {
            if (anchorChannels == null || anchorChannels.Count == 0)
            {
                yield break;
            }

            RjmcmcRoi bounded = CreateBoundedRoi(anchorChannels, minChannel, maxChannel, fwhmCalibration, config);
            int maxWidth = ResolveEffectiveMaxChannelsPerRoi(anchorChannels, minChannel, maxChannel, fwhmCalibration, config);
            bool widthFits = bounded.Width <= maxWidth;
            bool anchorCountFits = anchorChannels.Count <= config.MaxAnchorsPerRoi;
            if (widthFits && anchorCountFits)
            {
                PopulateLocalAnchors(bounded, allAnchorChannels);
                CopyReferenceAnchors(
                    bounded.ReferenceAnchorChannels,
                    allAnchorChannels,
                    minChannel,
                    maxChannel);
                PopulateHaloAnchors(bounded, fwhmCalibration);
                if (bounded.AnchorChannels.Count > 0 && bounded.AnchorChannels.Count <= config.MaxAnchorsPerRoi)
                {
                    yield return bounded;
                }

                yield break;
            }

            int splitIndex = FindAnchorGroupSplitIndex(anchorChannels, fwhmCalibration);
            if (splitIndex <= 0 || splitIndex >= anchorChannels.Count)
            {
                PopulateLocalAnchors(bounded, allAnchorChannels);
                CopyReferenceAnchors(
                    bounded.ReferenceAnchorChannels,
                    allAnchorChannels,
                    minChannel,
                    maxChannel);
                PopulateHaloAnchors(bounded, fwhmCalibration);
                if (bounded.AnchorChannels.Count > 0 && bounded.AnchorChannels.Count <= config.MaxAnchorsPerRoi)
                {
                    yield return bounded;
                }

                yield break;
            }

            List<int> left = anchorChannels.GetRange(0, splitIndex);
            List<int> right = anchorChannels.GetRange(splitIndex, anchorChannels.Count - splitIndex);

            foreach (RjmcmcRoi child in SplitAnchorGroup(left, allAnchorChannels, minChannel, maxChannel, fwhmCalibration, config))
            {
                yield return child;
            }

            foreach (RjmcmcRoi child in SplitAnchorGroup(right, allAnchorChannels, minChannel, maxChannel, fwhmCalibration, config))
            {
                yield return child;
            }
        }

        static RjmcmcRoi CreateBoundedRoi(
            IList<int> anchorChannels,
            int minChannel,
            int maxChannel,
            FwhmCalibration fwhmCalibration,
            RjmcmcConfig config)
        {
            int firstAnchor = anchorChannels[0];
            int lastAnchor = anchorChannels[anchorChannels.Count - 1];
            int leftRadius = GetAnchorRoiRadius(firstAnchor, fwhmCalibration, config);
            int rightRadius = GetAnchorRoiRadius(lastAnchor, fwhmCalibration, config);
            return new RjmcmcRoi
            {
                StartChannel = Math.Max(minChannel, firstAnchor - leftRadius),
                EndChannel = Math.Min(maxChannel, lastAnchor + rightRadius)
            };
        }

        static int ResolveEffectiveMaxChannelsPerRoi(
            IList<int> anchorChannels,
            int minChannel,
            int maxChannel,
            FwhmCalibration fwhmCalibration,
            RjmcmcConfig config)
        {
            int availableChannels = Math.Max(1, maxChannel - minChannel + 1);
            int spectrumGuard = Math.Min(
                config.MaxChannelsPerRoi,
                Math.Max(128, availableChannels / 4));

            if (anchorChannels == null || anchorChannels.Count == 0)
            {
                return Math.Max(5, spectrumGuard);
            }

            double representativeFwhm = anchorChannels
                .Select(channel => SanitizeFwhm(fwhmCalibration.ChannelToFwhm(channel)))
                .OrderBy(fwhm => fwhm)
                .ElementAt(anchorChannels.Count / 2);
            double widthFwhm = Math.Max(
                2.0 * config.RoiRadiusFwhm + 1.0,
                3.0 * config.RoiRadiusFwhm);
            int fwhmBasedLimit = Math.Max(5, Convert.ToInt32(Math.Ceiling(widthFwhm * representativeFwhm)));
            return Math.Max(5, Math.Min(spectrumGuard, fwhmBasedLimit));
        }

        static int FindAnchorGroupSplitIndex(IList<int> anchorChannels, FwhmCalibration fwhmCalibration)
        {
            if (anchorChannels == null || anchorChannels.Count < 2)
            {
                return 0;
            }

            int bestIndex = 0;
            double bestNormalizedGap = Double.NegativeInfinity;
            for (int i = 0; i < anchorChannels.Count - 1; i++)
            {
                int leftChannel = anchorChannels[i];
                int rightChannel = anchorChannels[i + 1];
                double leftFwhm = SanitizeFwhm(fwhmCalibration.ChannelToFwhm(leftChannel));
                double rightFwhm = SanitizeFwhm(fwhmCalibration.ChannelToFwhm(rightChannel));
                double normalizedGap = (rightChannel - leftChannel) / Math.Max(1.0, 0.5 * (leftFwhm + rightFwhm));
                if (normalizedGap > bestNormalizedGap)
                {
                    bestNormalizedGap = normalizedGap;
                    bestIndex = i + 1;
                }
            }

            return bestIndex;
        }

        static int GetAnchorRoiRadius(int anchorChannel, FwhmCalibration fwhmCalibration, RjmcmcConfig config)
        {
            double fwhm = SanitizeFwhm(fwhmCalibration.ChannelToFwhm(anchorChannel));
            return Math.Max(3, Convert.ToInt32(Math.Ceiling(config.RoiRadiusFwhm * fwhm)));
        }

        static double SanitizeFwhm(double fwhm)
        {
            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                return 1.0;
            }

            return fwhm;
        }

        static void PopulateLocalAnchors(RjmcmcRoi roi, IEnumerable<int> allAnchorChannels)
        {
            if (roi == null || allAnchorChannels == null)
            {
                return;
            }

            roi.AnchorChannels.Clear();
            foreach (int anchorChannel in allAnchorChannels)
            {
                if (anchorChannel >= roi.StartChannel &&
                    anchorChannel <= roi.EndChannel &&
                    !roi.AnchorChannels.Contains(anchorChannel))
                {
                    roi.AnchorChannels.Add(anchorChannel);
                }
            }
        }

        static void PopulateHaloAnchors(RjmcmcRoi roi, FwhmCalibration fwhmCalibration)
        {
            if (roi == null)
            {
                return;
            }

            roi.HaloAnchorChannels.Clear();
            List<int> referenceAnchorChannels = (roi.ReferenceAnchorChannels.Count > 0
                ? roi.ReferenceAnchorChannels
                : roi.AnchorChannels)
                .Distinct()
                .OrderBy(channel => channel)
                .ToList();

            int? leftNeighbor = referenceAnchorChannels
                .Where(channel => channel < roi.StartChannel)
                .Cast<int?>()
                .LastOrDefault();
            int? rightNeighbor = referenceAnchorChannels
                .Where(channel => channel > roi.EndChannel)
                .Cast<int?>()
                .FirstOrDefault();

            if (leftNeighbor.HasValue && DoesAnchorProfileIntersectRoi(leftNeighbor.Value, roi, fwhmCalibration))
            {
                roi.HaloAnchorChannels.Add(leftNeighbor.Value);
            }

            if (rightNeighbor.HasValue &&
                !roi.HaloAnchorChannels.Contains(rightNeighbor.Value) &&
                DoesAnchorProfileIntersectRoi(rightNeighbor.Value, roi, fwhmCalibration))
            {
                roi.HaloAnchorChannels.Add(rightNeighbor.Value);
            }
        }

        static bool DoesAnchorProfileIntersectRoi(int anchorChannel, RjmcmcRoi roi, FwhmCalibration fwhmCalibration)
        {
            if (roi == null)
            {
                return false;
            }

            double fwhm = fwhmCalibration.ChannelToFwhm(anchorChannel);
            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                return false;
            }

            double leftSupport = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
            double rightSupport = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
            if (!PeakShapeModel.IsFinite(leftSupport) || !PeakShapeModel.IsFinite(rightSupport))
            {
                return false;
            }

            double supportStart = anchorChannel - leftSupport;
            double supportEnd = anchorChannel + rightSupport;
            return supportEnd >= roi.StartChannel && supportStart <= roi.EndChannel;
        }

        static void CopyReferenceAnchors(List<int> target, IEnumerable<int> source, int minChannel, int maxChannel)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.Clear();
            foreach (int anchorChannel in source)
            {
                if (anchorChannel < minChannel || anchorChannel > maxChannel || target.Contains(anchorChannel))
                {
                    continue;
                }

                target.Add(anchorChannel);
            }
        }

        /// <summary>
        /// Evaluates ROI-chain jobs and keeps one internally consistent best chain per ROI.
        /// References: Green (1995), https://doi.org/10.1093/biomet/82.4.711; Tiboaca et al. (2014),
        /// "Bayesian parameter estimation and model selection of a nonlinear dynamical system using reversible jump MCMC",
        /// ISMA 2014, paper 0239, for RJMCMC as simultaneous parameter estimation and model selection.
        /// </summary>
        static List<RjmcmcPeakCandidate> ProcessRois(
            EnergySpectrum foregroundSpectrum,
            EnergySpectrum fixedBackgroundSpectrum,
            FwhmCalibration fwhmCalibration,
            List<RjmcmcRoi> rois,
            RjmcmcConfig config)
        {
            List<RjmcmcPeakCandidate> candidates = new List<RjmcmcPeakCandidate>();
            if (rois == null || rois.Count == 0)
            {
                return candidates;
            }

            List<RjmcmcRoiWorkspace> usableWorkspaces = new List<RjmcmcRoiWorkspace>();
            foreach (RjmcmcRoi roi in rois)
            {
                if (usableWorkspaces.Count >= config.MaxRois)
                {
                    break;
                }

                RjmcmcRoiWorkspace workspace = CreateWorkspace(foregroundSpectrum, fixedBackgroundSpectrum, fwhmCalibration, roi, config);
                if (workspace.IsUsable)
                {
                    usableWorkspaces.Add(workspace);
                }
            }

            if (usableWorkspaces.Count == 0)
            {
                return candidates;
            }

            int chainCount = Math.Max(1, config.ChainCount);
            RjmcmcRoiWorkspace[] workspaces = usableWorkspaces.ToArray();
            RjmcmcChainResult[][] chainResults = new RjmcmcChainResult[workspaces.Length][];
            for (int roiIndex = 0; roiIndex < workspaces.Length; roiIndex++)
            {
                chainResults[roiIndex] = new RjmcmcChainResult[chainCount];
            }

            int totalJobs = workspaces.Length * chainCount;
            int maxParallelism = Math.Min(config.MaxDegreeOfParallelism, totalJobs);
            Parallel.For(
                0,
                totalJobs,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism
                },
                jobIndex =>
                {
                    int roiIndex = jobIndex / chainCount;
                    int chainIndex = jobIndex % chainCount;
                    RjmcmcRoiWorkspace workspace = workspaces[roiIndex];
                    chainResults[roiIndex][chainIndex] = workspace.IsUsable
                        ? ProcessRoiChain(workspace, config, config.Seed + roiIndex + chainIndex * 7919)
                        : CreateEmptyChainResult();
                });

            for (int roiIndex = 0; roiIndex < workspaces.Length; roiIndex++)
            {
                candidates.AddRange(SelectBestChainCandidates(chainResults[roiIndex], config));
            }

            return DedupeCandidatesAcrossRois(candidates, config);
        }

        /// <summary>
        /// Precomputes ROI observations, amplitude scale and peak-shape profiles used by all chains.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for modelling nuclear peaks with known functional
        /// families and detector response; Deep research report, section "Role of FWHM and physical formulation".
        /// </summary>
        static RjmcmcRoiWorkspace CreateWorkspace(
            EnergySpectrum foregroundSpectrum,
            EnergySpectrum fixedBackgroundSpectrum,
            FwhmCalibration fwhmCalibration,
            RjmcmcRoi roi,
            RjmcmcConfig config)
        {
            int[] observed = ExtractObserved(foregroundSpectrum, roi);
            double[] fixedBackground = ExtractFixedBackground(foregroundSpectrum, fixedBackgroundSpectrum, roi);
            int maxObserved = 0;
            double maxResidual = 0.0;
            double sumResidual = 0.0;
            for (int i = 0; i < observed.Length; i++)
            {
                int value = observed[i];
                if (value > maxObserved)
                {
                    maxObserved = value;
                }

                double residual = Math.Max(0.0, value - FixedBackgroundAt(fixedBackground, i));
                if (residual > maxResidual)
                {
                    maxResidual = residual;
                }

                sumResidual += residual;
            }

            double amplitudeScale = EstimateAmplitudeScale(maxResidual);
            bool isUsable = observed.Length >= 5 && maxObserved > 0 && config.MaxExtraPeaksPerRoi > 0;
            bool[] anchorForbiddenChannels = BuildAnchorForbiddenChannels(roi, fwhmCalibration);
            RjmcmcRoiWorkspace workspace = new RjmcmcRoiWorkspace
            {
                Roi = roi,
                Observed = observed,
                FixedBackground = fixedBackground,
                AnchorForbiddenChannels = anchorForbiddenChannels,
                AvailableNonAnchorChannelCount = CountAvailableNonAnchorChannels(anchorForbiddenChannels),
                Profiles = isUsable ? BuildComponentProfiles(roi, fwhmCalibration) : new RjmcmcComponentProfile[0],
                HaloProfiles = isUsable ? BuildHaloComponentProfiles(roi, fwhmCalibration) : new Dictionary<int, RjmcmcComponentProfile>(),
                MeanObserved = observed.Length > 0 ? sumResidual / observed.Length : 0.0,
                AmplitudeScale = amplitudeScale,
                LogAmplitudeScale = Math.Log(amplitudeScale),
                FixedCenterSigmaChannels = EstimateFixedCenterSigmaChannels(roi, fwhmCalibration, config),
                IsUsable = isUsable
            };
            workspace.IsUsable = isUsable && HasAnchorOnlyResidualCandidate(workspace, config);
            return workspace;
        }

        static bool HasAnchorOnlyResidualCandidate(RjmcmcRoiWorkspace workspace, RjmcmcConfig config)
        {
            if (workspace == null ||
                workspace.AvailableNonAnchorChannelCount <= 0 ||
                workspace.Profiles == null ||
                workspace.Profiles.Length == 0)
            {
                return false;
            }

            RjmcmcState anchorOnly = CreateInitialState(workspace);
            if (!OptimizeProfileLikelihoodState(anchorOnly, workspace, config))
            {
                return false;
            }

            double[] lambda = new double[workspace.Length];
            if (!TryBuildLambdaArray(anchorOnly, workspace, lambda))
            {
                return false;
            }

            double minimumResidualSnr = Math.Max(1.0, config.TargetSnr * config.PreselectionSnrFraction);
            for (int localIndex = 0; localIndex < workspace.Length; localIndex++)
            {
                if (workspace.AnchorForbiddenChannels[localIndex] ||
                    !IsLocalPositiveResidualMaximum(workspace, lambda, localIndex))
                {
                    continue;
                }

                int channel = workspace.Roi.StartChannel + localIndex;
                RjmcmcComponentProfile profile = GetProfile(workspace, channel);
                if (TryCalculateResidualProfileScore(workspace, lambda, profile, out double residualSnr, out double residualCorrelation) &&
                    residualSnr >= minimumResidualSnr &&
                    residualCorrelation >= config.MinimumResidualProfileCorrelation)
                {
                    return true;
                }
            }

            return false;
        }

        static int EstimateFixedCenterSigmaChannels(
            RjmcmcRoi roi,
            FwhmCalibration fwhmCalibration,
            RjmcmcConfig config)
        {
            int centerChannel = roi.StartChannel + (roi.Width - 1) / 2;
            double fwhm = fwhmCalibration.ChannelToFwhm(centerChannel);
            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                fwhm = 1.0;
            }

            return Math.Max(1, Convert.ToInt32(Math.Round(config.CenterUpdateSigmaFwhm * fwhm)));
        }

        static bool[] BuildAnchorForbiddenChannels(RjmcmcRoi roi, FwhmCalibration fwhmCalibration)
        {
            bool[] forbidden = new bool[roi.Width];
            foreach (int anchorChannel in EnumerateModelAnchorChannels(roi))
            {
                double fwhm = fwhmCalibration.ChannelToFwhm(anchorChannel);
                if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
                {
                    fwhm = 1.0;
                }

                int radius = Math.Max(1, Convert.ToInt32(Math.Floor(AnchorExclusionDistance(fwhm))));
                int start = Math.Max(roi.StartChannel, anchorChannel - radius);
                int end = Math.Min(roi.EndChannel, anchorChannel + radius);
                for (int channel = start; channel <= end; channel++)
                {
                    forbidden[channel - roi.StartChannel] = true;
                }
            }

            return forbidden;
        }

        static int CountAvailableNonAnchorChannels(bool[] anchorForbiddenChannels)
        {
            int count = 0;
            for (int i = 0; i < anchorForbiddenChannels.Length; i++)
            {
                if (!anchorForbiddenChannels[i])
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Caches unit-amplitude peak profiles for every candidate center in the ROI.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for Gaussian/Lorentz-type peak models and
        /// deconvolution with detector response; Deep research report, section "Role of FWHM and physical formulation".
        /// </summary>
        static RjmcmcComponentProfile[] BuildComponentProfiles(RjmcmcRoi roi, FwhmCalibration fwhmCalibration)
        {
            RjmcmcComponentProfile[] profiles = new RjmcmcComponentProfile[roi.Width];
            for (int localCenter = 0; localCenter < profiles.Length; localCenter++)
            {
                int centerChannel = roi.StartChannel + localCenter;
                profiles[localCenter] = BuildComponentProfile(roi, fwhmCalibration, centerChannel);
            }

            return profiles;
        }

        static Dictionary<int, RjmcmcComponentProfile> BuildHaloComponentProfiles(RjmcmcRoi roi, FwhmCalibration fwhmCalibration)
        {
            Dictionary<int, RjmcmcComponentProfile> profiles = new Dictionary<int, RjmcmcComponentProfile>();
            foreach (int anchorChannel in roi.HaloAnchorChannels)
            {
                profiles[anchorChannel] = BuildComponentProfile(roi, fwhmCalibration, anchorChannel);
            }

            return profiles;
        }

        static RjmcmcComponentProfile BuildComponentProfile(
            RjmcmcRoi roi,
            FwhmCalibration fwhmCalibration,
            int centerChannel)
        {
            double fwhm = Math.Max(1.0, fwhmCalibration.ChannelToFwhm(centerChannel));
            RjmcmcComponentProfile profile = new RjmcmcComponentProfile
            {
                Fwhm = fwhm,
                StartIndex = 0,
                EndIndex = -1,
                RelativeValues = new double[0],
                IsValid = false
            };

            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                return profile;
            }

            double leftSupport = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
            double rightSupport = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
            if (!PeakShapeModel.IsFinite(leftSupport) || !PeakShapeModel.IsFinite(rightSupport))
            {
                return profile;
            }

            int startChannel = Math.Max(roi.StartChannel, centerChannel - Convert.ToInt32(Math.Ceiling(leftSupport)));
            int endChannel = Math.Min(roi.EndChannel, centerChannel + Convert.ToInt32(Math.Ceiling(rightSupport)));
            if (startChannel > endChannel)
            {
                profile.IsValid = true;
                return profile;
            }

            profile.StartIndex = startChannel - roi.StartChannel;
            profile.EndIndex = endChannel - roi.StartChannel;
            profile.RelativeValues = new double[profile.EndIndex - profile.StartIndex + 1];
            for (int localIndex = profile.StartIndex; localIndex <= profile.EndIndex; localIndex++)
            {
                int channel = roi.StartChannel + localIndex;
                profile.RelativeValues[localIndex - profile.StartIndex] =
                    PeakShapeModel.RelativeValue(channel - centerChannel, fwhm, fwhmCalibration);
            }

            profile.IsValid = true;
            return profile;
        }

        /// <summary>
        /// Runs one Metropolis-Hastings reversible-jump chain for a fixed ROI and returns its best extra components.
        /// References: Green (1995), Biometrika 82(4), 711-732; Tiboaca et al. (2014), ISMA 2014, paper 0239,
        /// for RJMCMC moves between parameter spaces of different dimensions.
        /// </summary>
        static RjmcmcChainResult ProcessRoiChain(
            RjmcmcRoiWorkspace workspace,
            RjmcmcConfig config,
            int seed)
        {
            Random random = new Random(seed);
            double[] lambda = new double[workspace.Length];
            RjmcmcState current = CreateInitialState(workspace);
            if (!EvaluateState(current, workspace, lambda, config))
            {
                return CreateEmptyChainResult();
            }

            RjmcmcState best = current.Clone();
            RjmcmcOccupancyTracker occupancy = new RjmcmcOccupancyTracker(workspace.Length);
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
                        proposal = ProposeBirth(current, workspace, lambda, config, moveProbabilities, random, out logProposalRatio);
                        break;
                    case MoveKind.Death:
                        proposal = ProposeDeath(current, workspace, lambda, config, moveProbabilities, random, out logProposalRatio);
                        break;
                    case MoveKind.UpdateExtra:
                        proposal = ProposeUpdateExtra(current, workspace, config, random);
                        break;
                    case MoveKind.UpdateAnchorAmplitude:
                        proposal = ProposeUpdateAnchorAmplitude(current, workspace.AmplitudeScale, random);
                        break;
                    case MoveKind.UpdateBackground:
                        proposal = ProposeUpdateBackground(current, workspace, config, random);
                        break;
                }

                if (proposal == null || !EvaluateState(proposal, workspace, lambda, config))
                {
                    continue;
                }

                double logAcceptance = proposal.LogPosterior - current.LogPosterior + logProposalRatio;
                if (Math.Log(Math.Max(Double.Epsilon, random.NextDouble())) <= logAcceptance)
                {
                    current = proposal;
                }

                if (iteration >= config.BurnIn)
                {
                    occupancy.Record(current, workspace);
                    if (current.LogPosterior > best.LogPosterior)
                    {
                        best = current.Clone();
                    }
                }
            }

            return new RjmcmcChainResult
            {
                RawCandidates = CollectRawCandidates(best, workspace, lambda, occupancy, config),
                LogPosterior = best.LogPosterior
            };
        }

        static RjmcmcChainResult CreateEmptyChainResult()
        {
            return new RjmcmcChainResult
            {
                RawCandidates = new List<RjmcmcPeakCandidate>(),
                LogPosterior = Double.NegativeInfinity
            };
        }

        static List<RjmcmcPeakCandidate> SelectBestChainCandidates(
            IEnumerable<RjmcmcChainResult> chainResults,
            RjmcmcConfig config)
        {
            List<RjmcmcChainResult> results = chainResults
                .Where(result => result != null)
                .ToList();
            RjmcmcChainResult best = results
                .OrderByDescending(result => result.LogPosterior)
                .FirstOrDefault();
            if (best == null || best.RawCandidates == null || best.RawCandidates.Count == 0)
            {
                return new List<RjmcmcPeakCandidate>();
            }

            List<RjmcmcPeakCandidate> accepted = best.RawCandidates
                .Where(candidate => candidate.DevianceImprovement >= config.MinDevianceImprovement)
                .OrderByDescending(candidate => candidate.DevianceImprovement)
                .Where(candidate => HasRequiredChainSupport(candidate, best, results, config))
                .Take(config.MaxExtraPeaksPerRoi)
                .OrderBy(candidate => candidate.Channel)
                .ToList();
            foreach (RjmcmcPeakCandidate candidate in accepted)
            {
                TraceCandidateDiagnostics(candidate);
            }

            return accepted;
        }

        static void TraceCandidateDiagnostics(RjmcmcPeakCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            Trace.WriteLine(String.Format(
                CultureInfo.InvariantCulture,
                "RJMCMC accepted extra: ch={0:F2}; score={1:F2}; deltaD={2:F2}; occupancy={3:F3}; centerStdDev={4:F2}; residualSnr={5:F2}; residualCorr={6:F3}; anchorDistFwhm={7:F3}; supportChains={8}",
                candidate.Channel,
                candidate.Snr,
                candidate.DevianceImprovement,
                candidate.PosteriorOccupancy,
                candidate.CenterStdDev,
                candidate.ResidualSnr,
                candidate.ResidualCorrelation,
                candidate.AnchorDistanceFwhm,
                candidate.SupportingChainCount));
        }

        static bool HasRequiredChainSupport(
            RjmcmcPeakCandidate candidate,
            RjmcmcChainResult best,
            List<RjmcmcChainResult> chainResults,
            RjmcmcConfig config)
        {
            if (candidate == null || chainResults == null || chainResults.Count <= 1)
            {
                if (candidate != null)
                {
                    candidate.SupportingChainCount = 1;
                }

                return true;
            }

            int requiredSupport = RequiredSupportingChainCount(candidate, chainResults.Count, config);
            int supportCount = CountSupportingChains(candidate, best, chainResults, config);
            candidate.SupportingChainCount = supportCount;
            return supportCount >= requiredSupport;
        }

        static int CountSupportingChains(
            RjmcmcPeakCandidate candidate,
            RjmcmcChainResult best,
            List<RjmcmcChainResult> chainResults,
            RjmcmcConfig config)
        {
            int supportCount = 1;
            int tolerance = CandidateSupportTolerance(candidate, config);
            bool candidateIsClose = IsCloseToAnchor(candidate.AnchorDistanceFwhm, config);
            foreach (RjmcmcChainResult chainResult in chainResults)
            {
                if (Object.ReferenceEquals(chainResult, best) ||
                    chainResult?.RawCandidates == null ||
                    chainResult.RawCandidates.Count == 0)
                {
                    continue;
                }

                bool supported = chainResult.RawCandidates.Any(other =>
                    other.DevianceImprovement >= config.SupportingDevianceImprovement &&
                    Math.Abs(other.Channel - candidate.Channel) <= tolerance &&
                    (!candidateIsClose ||
                        (other.PosteriorOccupancy >= config.CloseAnchorMinimumPosteriorOccupancy &&
                         other.ResidualCorrelation >= config.CloseAnchorMinimumResidualProfileCorrelation)));
                if (supported)
                {
                    supportCount++;
                }
            }

            return supportCount;
        }

        static int RequiredSupportingChainCount(
            RjmcmcPeakCandidate candidate,
            int chainCount,
            RjmcmcConfig config)
        {
            int requiredSupport = Math.Min(chainCount, Math.Max(1, config.MinimumSupportingChains));
            if (IsCloseToAnchor(candidate?.AnchorDistanceFwhm ?? Double.PositiveInfinity, config))
            {
                requiredSupport = Math.Max(requiredSupport, Math.Max(1, config.CloseAnchorMinimumSupportingChains));
                requiredSupport = Math.Min(chainCount, requiredSupport);
            }

            return requiredSupport;
        }

        static int CandidateSupportTolerance(RjmcmcPeakCandidate candidate, RjmcmcConfig config)
        {
            double fwhm = candidate != null && PeakShapeModel.IsFinite(candidate.Fwhm) && candidate.Fwhm > 0.0
                ? candidate.Fwhm
                : 1.0;
            return CenterToleranceForFwhm(fwhm, config);
        }

        static List<RjmcmcPeakCandidate> DedupeCandidatesAcrossRois(
            IEnumerable<RjmcmcPeakCandidate> candidates,
            RjmcmcConfig config)
        {
            if (candidates == null)
            {
                return new List<RjmcmcPeakCandidate>();
            }

            List<RjmcmcPeakCandidate> ordered = candidates
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.DevianceImprovement)
                .ThenByDescending(candidate => candidate.SupportingChainCount)
                .ThenByDescending(candidate => candidate.PosteriorOccupancy)
                .ThenByDescending(candidate => candidate.ResidualCorrelation)
                .ToList();

            List<RjmcmcPeakCandidate> deduped = new List<RjmcmcPeakCandidate>();
            foreach (RjmcmcPeakCandidate candidate in ordered)
            {
                if (deduped.Any(existing => AreEquivalentCandidates(existing, candidate, config)))
                {
                    continue;
                }

                deduped.Add(candidate);
            }

            deduped.Sort((left, right) => left.Channel.CompareTo(right.Channel));
            return deduped;
        }

        static bool AreEquivalentCandidates(
            RjmcmcPeakCandidate left,
            RjmcmcPeakCandidate right,
            RjmcmcConfig config)
        {
            if (left == null || right == null)
            {
                return false;
            }

            int tolerance = Math.Max(CandidateSupportTolerance(left, config), CandidateSupportTolerance(right, config));
            return Math.Abs(left.Channel - right.Channel) <= tolerance;
        }

        static int CenterToleranceForFwhm(double fwhm, RjmcmcConfig config)
        {
            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                fwhm = 1.0;
            }

            int tolerance = Convert.ToInt32(Math.Ceiling(config.SupportCenterToleranceFwhm * fwhm));
            tolerance = Math.Max(2, tolerance);
            return Math.Min(config.SupportCenterToleranceMaxChannels, tolerance);
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

        static double[] ExtractFixedBackground(
            EnergySpectrum foregroundSpectrum,
            EnergySpectrum backgroundSpectrum,
            RjmcmcRoi roi)
        {
            if (foregroundSpectrum == null ||
                backgroundSpectrum == null ||
                foregroundSpectrum.Spectrum == null ||
                backgroundSpectrum.Spectrum == null ||
                foregroundSpectrum.MeasurementTime <= 0.0 ||
                backgroundSpectrum.MeasurementTime <= 0.0)
            {
                return null;
            }

            double scale = foregroundSpectrum.MeasurementTime / backgroundSpectrum.MeasurementTime;
            if (!PeakShapeModel.IsFinite(scale) || scale <= 0.0)
            {
                return null;
            }

            double[] fixedBackground = new double[roi.Width];
            bool hasBackground = false;
            bool sameCalibration = foregroundSpectrum.EnergyCalibration != null &&
                foregroundSpectrum.EnergyCalibration.Equals(backgroundSpectrum.EnergyCalibration);
            for (int i = 0; i < fixedBackground.Length; i++)
            {
                int foregroundChannel = roi.StartChannel + i;
                int backgroundChannel = foregroundChannel;
                if (!sameCalibration)
                {
                    if (foregroundSpectrum.EnergyCalibration == null || backgroundSpectrum.EnergyCalibration == null)
                    {
                        return null;
                    }

                    double energy = foregroundSpectrum.EnergyCalibration.ChannelToEnergy(foregroundChannel);
                    backgroundChannel = Convert.ToInt32(backgroundSpectrum.EnergyCalibration.EnergyToChannel(
                        energy,
                        maxChannels: backgroundSpectrum.NumberOfChannels));
                }

                if (backgroundChannel < 0 || backgroundChannel >= backgroundSpectrum.NumberOfChannels)
                {
                    continue;
                }

                double value = scale * backgroundSpectrum.Spectrum[backgroundChannel];
                if (PeakShapeModel.IsFinite(value) && value > 0.0)
                {
                    fixedBackground[i] = value;
                    hasBackground = true;
                }
            }

            return hasBackground ? fixedBackground : null;
        }

        /// <summary>
        /// Creates the starting model with fixed FWHM anchors from the deterministic peak finder and a linear background.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for joint peak/background modelling; Deep research
        /// report, sections "Role of FWHM and physical formulation" and "Implementation priorities".
        /// </summary>
        static RjmcmcState CreateInitialState(RjmcmcRoiWorkspace workspace)
        {
            RjmcmcRoi roi = workspace.Roi;
            RjmcmcState state = new RjmcmcState
            {
                Anchors = new List<RjmcmcPeakComponent>(CountModelAnchors(roi)),
                Extras = new List<RjmcmcPeakComponent>()
            };

            EstimateBackground(workspace, out double leftMean, out double rightMean);
            int width = Math.Max(1, workspace.Observed.Length - 1);
            state.BackgroundIntercept = Math.Max(0.1, leftMean);
            state.BackgroundSlope = (rightMean - leftMean) / width;
            state.BackgroundQuadratic = 0.0;

            foreach (int anchorChannel in EnumerateModelAnchorChannels(roi))
            {
                state.Anchors.Add(new RjmcmcPeakComponent
                {
                    Channel = anchorChannel,
                    Fwhm = GetProfile(workspace, anchorChannel)?.Fwhm ?? 1.0,
                    Amplitude = EstimateInitialAnchorAmplitude(workspace, state, anchorChannel)
                });
            }

            return state;
        }

        static double EstimateInitialAnchorAmplitude(RjmcmcRoiWorkspace workspace, RjmcmcState state, int anchorChannel)
        {
            if (anchorChannel >= workspace.Roi.StartChannel && anchorChannel <= workspace.Roi.EndChannel)
            {
                int localIndex = anchorChannel - workspace.Roi.StartChannel;
                localIndex = Math.Max(0, Math.Min(workspace.Observed.Length - 1, localIndex));
                double backgroundAtCenter = FixedBackgroundAt(workspace.FixedBackground, localIndex) + BackgroundAt(state, localIndex);
                return Math.Max(1.0, workspace.Observed[localIndex] - backgroundAtCenter);
            }

            RjmcmcComponentProfile profile = GetProfile(workspace, anchorChannel);
            if (profile == null || !profile.IsValid || profile.StartIndex > profile.EndIndex)
            {
                return 1.0;
            }

            double numerator = 0.0;
            double denominator = 0.0;
            for (int localIndex = profile.StartIndex; localIndex <= profile.EndIndex; localIndex++)
            {
                double relativeValue = profile.RelativeValues[localIndex - profile.StartIndex];
                if (!PeakShapeModel.IsFinite(relativeValue) || relativeValue <= 0.0)
                {
                    continue;
                }

                double background = FixedBackgroundAt(workspace.FixedBackground, localIndex) + BackgroundAt(state, localIndex);
                double residual = Math.Max(0.0, workspace.Observed[localIndex] - background);
                numerator += residual * relativeValue;
                denominator += relativeValue * relativeValue;
            }

            if (!PeakShapeModel.IsFinite(numerator) || !PeakShapeModel.IsFinite(denominator) || denominator <= 0.0)
            {
                return 1.0;
            }

            return Math.Max(1.0, numerator / denominator);
        }

        static void EstimateBackground(RjmcmcRoiWorkspace workspace, out double leftMean, out double rightMean)
        {
            int[] observed = workspace.Observed;
            int edgeWidth = Math.Max(2, observed.Length / 8);
            leftMean = 0.0;
            rightMean = 0.0;
            for (int i = 0; i < edgeWidth; i++)
            {
                leftMean += Math.Max(0.0, observed[i] - FixedBackgroundAt(workspace.FixedBackground, i));
                int rightIndex = observed.Length - 1 - i;
                rightMean += Math.Max(0.0, observed[rightIndex] - FixedBackgroundAt(workspace.FixedBackground, rightIndex));
            }
            leftMean /= edgeWidth;
            rightMean /= edgeWidth;
        }

        /// <summary>
        /// Evaluates the Poisson log likelihood plus exponential peak-amplitude priors for the current ROI model.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for Bayesian posterior inference in nuclear spectra;
        /// Deep research report, sections "Role of FWHM and physical formulation" and "Validation and quality criteria",
        /// recommending likelihood/deviance metrics for count spectra.
        /// </summary>
        static bool EvaluateState(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcConfig config)
        {
            if (state == null)
            {
                return false;
            }

            if (!AreExtraCentersAllowed(state, workspace))
            {
                return false;
            }

            if (!IsBackgroundNonNegative(state, workspace.Length))
            {
                return false;
            }

            if (!TryBuildLambdaArray(state, workspace, lambda))
            {
                return false;
            }

            double logLikelihood = 0.0;
            int[] observed = workspace.Observed;
            for (int i = 0; i < workspace.Length; i++)
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

                logPrior += LogExponentialPdf(anchor.Amplitude, workspace.AmplitudeScale, workspace.LogAmplitudeScale);
            }

            foreach (RjmcmcPeakComponent extra in state.Extras)
            {
                if (extra.Amplitude <= 0.0 || !PeakShapeModel.IsFinite(extra.Amplitude))
                {
                    return false;
                }

                logPrior += LogExponentialPdf(extra.Amplitude, workspace.AmplitudeScale, workspace.LogAmplitudeScale);
                logPrior -= config.ExtraPeakPenalty;
            }

            state.LogLikelihood = logLikelihood;
            state.LogPosterior = logLikelihood + logPrior;
            return PeakShapeModel.IsFinite(state.LogPosterior);
        }

        /// <summary>
        /// Builds the expected count rate lambda for background, anchor peaks and RJMCMC extra peaks.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for additive peak/background spectrum models;
        /// Deep research report, section "Role of FWHM and physical formulation".
        /// </summary>
        static bool TryBuildLambdaArray(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            double[] lambda)
        {
            int length = workspace.Length;
            double intercept = state.BackgroundIntercept;
            double slope = state.BackgroundSlope;
            double quadratic = state.BackgroundQuadratic;
            double[] fixedBackground = workspace.FixedBackground;
            if (fixedBackground == null)
            {
                for (int i = 0; i < length; i++)
                {
                    double background = intercept + slope * i + quadratic * i * i;
                    if (!PeakShapeModel.IsFinite(background))
                    {
                        return false;
                    }

                    lambda[i] = Math.Max(1E-6, background);
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    double background = fixedBackground[i] + intercept + slope * i + quadratic * i * i;
                    if (!PeakShapeModel.IsFinite(background))
                    {
                        return false;
                    }

                    lambda[i] = Math.Max(1E-6, background);
                }
            }

            foreach (RjmcmcPeakComponent anchor in state.Anchors)
            {
                if (!TryAddComponent(lambda, workspace, anchor))
                {
                    return false;
                }
            }

            foreach (RjmcmcPeakComponent extra in state.Extras)
            {
                if (!TryAddComponent(lambda, workspace, extra))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds one cached peak-shape component into the expected count array.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510; Deep research report, section
        /// "Role of FWHM and physical formulation", for using FWHM-calibrated peak shapes.
        /// </summary>
        static bool TryAddComponent(
            double[] lambda,
            RjmcmcRoiWorkspace workspace,
            RjmcmcPeakComponent component)
        {
            if (component == null ||
                component.Amplitude <= 0.0 ||
                component.Fwhm <= 0.0 ||
                !PeakShapeModel.IsFinite(component.Amplitude) ||
                !PeakShapeModel.IsFinite(component.Fwhm))
            {
                return false;
            }

            RjmcmcComponentProfile profile = GetProfile(workspace, component.Channel);
            if (profile == null || !profile.IsValid)
            {
                return false;
            }

            if (profile.StartIndex > profile.EndIndex)
            {
                return true;
            }

            for (int localIndex = profile.StartIndex; localIndex <= profile.EndIndex; localIndex++)
            {
                lambda[localIndex] += component.Amplitude * profile.RelativeValues[localIndex - profile.StartIndex];
            }

            return true;
        }

        static RjmcmcComponentProfile GetProfile(RjmcmcRoiWorkspace workspace, int channel)
        {
            int localIndex = channel - workspace.Roi.StartChannel;
            if (localIndex >= 0 && localIndex < workspace.Profiles.Length)
            {
                return workspace.Profiles[localIndex];
            }

            if (workspace.HaloProfiles != null && workspace.HaloProfiles.TryGetValue(channel, out RjmcmcComponentProfile profile))
            {
                return profile;
            }

            return null;
        }

        static double BackgroundAt(RjmcmcState state, int localIndex)
        {
            return state.BackgroundIntercept +
                state.BackgroundSlope * localIndex +
                state.BackgroundQuadratic * localIndex * localIndex;
        }

        static bool IsBackgroundNonNegative(RjmcmcState state, int length)
        {
            for (int i = 0; i < length; i++)
            {
                double background = BackgroundAt(state, i);
                if (!PeakShapeModel.IsFinite(background) || background < 0.0)
                {
                    return false;
                }
            }

            return true;
        }

        static double EstimateAmplitudeScale(double maxObserved)
        {
            return Math.Max(5.0, maxObserved * 0.75);
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
            Normalize(ref probabilities);
            return probabilities;
        }

        static void Normalize(ref MoveProbabilities probabilities)
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

        /// <summary>
        /// Proposes a dimension-increasing RJMCMC move by adding one extra peak at a residual-weighted channel.
        /// References: Green (1995), https://doi.org/10.1093/biomet/82.4.711, for reversible-jump birth/death moves;
        /// Gulam Razul et al. (2003), NIM A 497, 492-510, for unknown peak count in nuclear spectra.
        /// </summary>
        static RjmcmcState ProposeBirth(
            RjmcmcState current,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
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

            BirthProposal birthProposal = DrawBirthChannel(current, workspace, lambda, random);
            if (birthProposal == null || birthProposal.Probability <= 0.0)
            {
                return null;
            }

            double amplitude = SampleExponential(random, workspace.AmplitudeScale);
            if (amplitude <= 0.0)
            {
                return null;
            }

            RjmcmcComponentProfile profile = GetProfile(workspace, birthProposal.Channel);

            RjmcmcState proposal = current.Clone();
            proposal.Extras.Add(new RjmcmcPeakComponent
            {
                Channel = birthProposal.Channel,
                Fwhm = profile?.Fwhm ?? 1.0,
                Amplitude = amplitude
            });

            MoveProbabilities reverseProbabilities = GetMoveProbabilities(proposal.Extras.Count, proposal.Anchors.Count, config.MaxExtraPeaksPerRoi);
            double deathSelectionProbability = 1.0 / proposal.Extras.Count;
            logProposalRatio =
                Math.Log(reverseProbabilities.Death) +
                Math.Log(deathSelectionProbability) -
                Math.Log(forwardProbabilities.Birth) -
                Math.Log(birthProposal.Probability) -
                LogExponentialPdf(amplitude, workspace.AmplitudeScale, workspace.LogAmplitudeScale);
            return proposal;
        }

        /// <summary>
        /// Proposes a dimension-decreasing RJMCMC move by removing one extra peak.
        /// References: Green (1995), Biometrika 82(4), 711-732; Tiboaca et al. (2014), ISMA 2014, paper 0239,
        /// for jumps between parameter spaces with different dimensions.
        /// </summary>
        static RjmcmcState ProposeDeath(
            RjmcmcState current,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
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
            BirthProposal reverseBirth = GetBirthProbabilityForChannel(proposal, workspace, lambda, removed.Channel);
            if (reverseBirth == null || reverseBirth.Probability <= 0.0)
            {
                return null;
            }

            double deathSelectionProbability = 1.0 / current.Extras.Count;
            logProposalRatio =
                Math.Log(reverseProbabilities.Birth) +
                Math.Log(reverseBirth.Probability) +
                LogExponentialPdf(removed.Amplitude, workspace.AmplitudeScale, workspace.LogAmplitudeScale) -
                Math.Log(forwardProbabilities.Death) -
                Math.Log(deathSelectionProbability);
            return proposal;
        }

        /// <summary>
        /// Proposes an in-model perturbation of an extra peak center and amplitude.
        /// References: Green (1995), Biometrika 82(4), 711-732, for Metropolis-Hastings updates inside trans-dimensional
        /// models; Deep research report, section "Bayesian variable-dimensional models".
        /// </summary>
        static RjmcmcState ProposeUpdateExtra(
            RjmcmcState current,
            RjmcmcRoiWorkspace workspace,
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
            int channelStep = Convert.ToInt32(Math.Round(SampleNormal(random) * workspace.FixedCenterSigmaChannels));
            double amplitudeStep = SampleNormal(random) * workspace.AmplitudeScale * 0.10;
            int newChannel = component.Channel + channelStep;
            double newAmplitude = component.Amplitude + amplitudeStep;
            if (newChannel < workspace.Roi.StartChannel ||
                newChannel > workspace.Roi.EndChannel ||
                newAmplitude <= 0.0 ||
                !IsExtraCenterAllowed(proposal, workspace, newChannel, component))
            {
                return null;
            }

            RjmcmcComponentProfile profile = GetProfile(workspace, newChannel);
            component.Channel = newChannel;
            component.Fwhm = profile?.Fwhm ?? 1.0;
            component.Amplitude = newAmplitude;
            return proposal;
        }

        /// <summary>
        /// Proposes an in-model amplitude perturbation for a deterministic anchor peak.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for joint parameter estimation of peak components;
        /// Green (1995), Biometrika 82(4), 711-732, for Metropolis-Hastings updates in RJMCMC.
        /// </summary>
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

        /// <summary>
        /// Proposes an in-model perturbation of the local linear background.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for modelling peak parameters and background jointly;
        /// Deep research report, section "Role of FWHM and physical formulation".
        /// </summary>
        static RjmcmcState ProposeUpdateBackground(
            RjmcmcState current,
            RjmcmcRoiWorkspace workspace,
            RjmcmcConfig config,
            Random random)
        {
            RjmcmcState proposal = current.Clone();
            double width = Math.Max(1.0, workspace.Roi.Width - 1);
            double backgroundStep = Math.Max(1.0, workspace.MeanObserved * config.BackgroundUpdateFraction);
            proposal.BackgroundIntercept += SampleNormal(random) * backgroundStep;
            proposal.BackgroundSlope += SampleNormal(random) * backgroundStep / width;
            proposal.BackgroundQuadratic += SampleNormal(random) * backgroundStep / (width * width);
            if (!IsBackgroundNonNegative(proposal, workspace.Roi.Width))
            {
                return null;
            }

            return proposal;
        }

        /// <summary>
        /// Draws a birth channel from the positive residual distribution while excluding occupied centers.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510, for stochastic Bayesian exploration of peak models;
        /// Deep research report, section "Implementation priorities", for candidate-guided difficult-ROI inference.
        /// </summary>
        static BirthProposal DrawBirthChannel(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            Random random)
        {
            if (!TryBuildLambdaArray(state, workspace, lambda))
            {
                return DrawUniformBirthChannel(state, workspace, random);
            }

            double totalWeight = CalculateBirthWeightTotal(state, workspace, lambda);
            if (totalWeight <= 0.0 || !PeakShapeModel.IsFinite(totalWeight))
            {
                return DrawUniformBirthChannel(state, workspace, random);
            }

            double sample = random.NextDouble();
            double cumulative = 0.0;
            for (int i = 0; i < workspace.Length; i++)
            {
                double probability = BirthWeightAt(state, workspace, lambda, i) / totalWeight;
                cumulative += probability;
                if (sample <= cumulative)
                {
                    return new BirthProposal
                    {
                        Channel = workspace.Roi.StartChannel + i,
                        Probability = probability
                    };
                }
            }

            double lastProbability = BirthWeightAt(state, workspace, lambda, workspace.Length - 1) / totalWeight;
            return new BirthProposal
            {
                Channel = workspace.Roi.EndChannel,
                Probability = lastProbability
            };
        }

        /// <summary>
        /// Computes the reverse birth probability needed by the death proposal ratio.
        /// References: Green (1995), https://doi.org/10.1093/biomet/82.4.711, for reversible proposal densities;
        /// Tiboaca et al. (2014), ISMA 2014, paper 0239, for dimension-matching RJMCMC transitions.
        /// </summary>
        static BirthProposal GetBirthProbabilityForChannel(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            int channel)
        {
            int index = channel - workspace.Roi.StartChannel;
            if (index < 0 || index >= workspace.Length)
            {
                return null;
            }

            if (!TryBuildLambdaArray(state, workspace, lambda))
            {
                return new BirthProposal
                {
                    Channel = channel,
                    Probability = UniformBirthProbabilityForChannel(state, workspace, channel)
                };
            }

            double totalWeight = CalculateBirthWeightTotal(state, workspace, lambda);
            double probability = totalWeight <= 0.0 || !PeakShapeModel.IsFinite(totalWeight)
                ? UniformBirthProbabilityForChannel(state, workspace, channel)
                : BirthWeightAt(state, workspace, lambda, index) / totalWeight;
            return new BirthProposal
            {
                Channel = channel,
                Probability = probability
            };
        }

        static double CalculateBirthWeightTotal(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            double[] lambda)
        {
            double total = 0.0;
            for (int i = 0; i < workspace.Length; i++)
            {
                total += BirthWeightAt(state, workspace, lambda, i);
            }

            return total;
        }

        static double BirthWeightAt(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            int localIndex)
        {
            if (workspace.AnchorForbiddenChannels[localIndex])
            {
                return 0.0;
            }

            int channel = workspace.Roi.StartChannel + localIndex;
            if (IsExtraChannelOccupied(state, channel, null))
            {
                return 0.0;
            }

            double residual = workspace.Observed[localIndex] - lambda[localIndex];
            return 0.05 + Math.Max(0.0, residual);
        }

        static bool AreExtraCentersAllowed(RjmcmcState state, RjmcmcRoiWorkspace workspace)
        {
            foreach (RjmcmcPeakComponent extra in state.Extras)
            {
                if (!IsExtraCenterAllowed(state, workspace, extra.Channel, extra))
                {
                    return false;
                }
            }

            return true;
        }

        static bool IsExtraCenterAllowed(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            int channel,
            RjmcmcPeakComponent ignoredExtra)
        {
            int localIndex = channel - workspace.Roi.StartChannel;
            if (localIndex < 0 ||
                localIndex >= workspace.AnchorForbiddenChannels.Length ||
                workspace.AnchorForbiddenChannels[localIndex])
            {
                return false;
            }

            return !IsExtraChannelOccupied(state, channel, ignoredExtra);
        }

        static bool IsExtraChannelOccupied(RjmcmcState state, int channel, RjmcmcPeakComponent ignoredExtra)
        {
            foreach (RjmcmcPeakComponent extra in state.Extras)
            {
                if (Object.ReferenceEquals(extra, ignoredExtra))
                {
                    continue;
                }

                if (extra.Channel == channel)
                {
                    return true;
                }
            }

            return false;
        }

        static double AnchorExclusionDistance(double fwhm)
        {
            return Math.Max(1.0, 0.04 * Math.Max(1.0, fwhm));
        }

        static double NearestAnchorDistanceFwhm(RjmcmcPeakComponent extra, RjmcmcRoiWorkspace workspace)
        {
            if (extra == null ||
                workspace?.Roi == null)
            {
                return Double.PositiveInfinity;
            }

            List<int> referenceAnchors = workspace.Roi.ReferenceAnchorChannels != null && workspace.Roi.ReferenceAnchorChannels.Count > 0
                ? workspace.Roi.ReferenceAnchorChannels
                : workspace.Roi.AnchorChannels;
            if (referenceAnchors == null || referenceAnchors.Count == 0)
            {
                return Double.PositiveInfinity;
            }

            double nearest = Double.PositiveInfinity;
            foreach (int anchorChannel in referenceAnchors)
            {
                RjmcmcComponentProfile anchorProfile = GetProfile(workspace, anchorChannel);
                double anchorFwhm = anchorProfile != null && PeakShapeModel.IsFinite(anchorProfile.Fwhm) && anchorProfile.Fwhm > 0.0
                    ? anchorProfile.Fwhm
                    : Math.Max(1.0, extra.Fwhm);
                double extraFwhm = PeakShapeModel.IsFinite(extra.Fwhm) && extra.Fwhm > 0.0
                    ? extra.Fwhm
                    : anchorFwhm;
                double scaleFwhm = Math.Max(anchorFwhm, extraFwhm);
                double distanceFwhm = Math.Abs(extra.Channel - anchorChannel) / scaleFwhm;
                if (PeakShapeModel.IsFinite(distanceFwhm) && distanceFwhm < nearest)
                {
                    nearest = distanceFwhm;
                }
            }

            return nearest;
        }

        static bool IsCloseToAnchor(double anchorDistanceFwhm, RjmcmcConfig config)
        {
            return PeakShapeModel.IsFinite(anchorDistanceFwhm) &&
                anchorDistanceFwhm < config.CloseAnchorDistanceFwhm;
        }

        static BirthProposal DrawUniformBirthChannel(RjmcmcState state, RjmcmcRoiWorkspace workspace, Random random)
        {
            int availableCount = CountAvailableBirthChannels(state, workspace);
            if (availableCount <= 0)
            {
                return null;
            }

            int selected = random.Next(availableCount);
            for (int i = 0; i < workspace.Length; i++)
            {
                if (workspace.AnchorForbiddenChannels[i])
                {
                    continue;
                }

                int channel = workspace.Roi.StartChannel + i;
                if (IsExtraChannelOccupied(state, channel, null))
                {
                    continue;
                }

                if (selected == 0)
                {
                    return new BirthProposal
                    {
                        Channel = channel,
                        Probability = 1.0 / availableCount
                    };
                }

                selected--;
            }

            return null;
        }

        static double UniformBirthProbabilityForChannel(RjmcmcState state, RjmcmcRoiWorkspace workspace, int channel)
        {
            if (!IsExtraCenterAllowed(state, workspace, channel, null))
            {
                return 0.0;
            }

            int availableCount = CountAvailableBirthChannels(state, workspace);
            return availableCount > 0 ? 1.0 / availableCount : 0.0;
        }

        static int CountAvailableBirthChannels(RjmcmcState state, RjmcmcRoiWorkspace workspace)
        {
            return Math.Max(0, workspace.AvailableNonAnchorChannelCount - state.Extras.Count);
        }

        static double FixedBackgroundAt(double[] fixedBackground, int localIndex)
        {
            return fixedBackground != null ? fixedBackground[localIndex] : 0.0;
        }

        /// <summary>
        /// Converts the best sampled extra components into reportable candidates after amplitude and deviance filters.
        /// References: Deep research report, section "Validation and quality criteria", for hit/miss/false-hit and
        /// likelihood/deviance-based validation; Gulam Razul et al. (2003), NIM A 497, 492-510, for model selection
        /// and parameter estimation of nuclear emission spectra.
        /// </summary>
        static List<RjmcmcPeakCandidate> CollectRawCandidates(
            RjmcmcState best,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcOccupancyTracker occupancy,
            RjmcmcConfig config)
        {
            List<RjmcmcPeakCandidate> candidates = new List<RjmcmcPeakCandidate>();
            if (best?.Extras == null || best.Extras.Count == 0)
            {
                return candidates;
            }

            foreach (RjmcmcPeakComponent extra in best.Extras.OrderBy(component => component.Channel))
            {
                int tolerance = CenterToleranceForFwhm(extra.Fwhm, config);
                double anchorDistanceFwhm = NearestAnchorDistanceFwhm(extra, workspace);
                double posteriorOccupancy = occupancy?.FractionNear(extra.Channel, workspace, tolerance) ?? 0.0;
                if (posteriorOccupancy < config.MinimumPosteriorOccupancy)
                {
                    continue;
                }

                double centerStdDev = occupancy?.CenterStdDevNear(extra.Channel, workspace, tolerance) ?? Double.PositiveInfinity;
                double maxCenterStdDev = Math.Max(1.0, config.MaximumPosteriorCenterStdDevFwhm * Math.Max(1.0, extra.Fwhm));
                if (!PeakShapeModel.IsFinite(centerStdDev) || centerStdDev > maxCenterStdDev)
                {
                    continue;
                }

                ProfileDevianceResult profile = ComputeProfileDevianceImprovement(best, extra, workspace, lambda, config);
                if (profile == null ||
                    !PeakShapeModel.IsFinite(profile.Improvement) ||
                    profile.Improvement <= 0.0)
                {
                    continue;
                }

                if (IsCloseToAnchor(anchorDistanceFwhm, config) &&
                    posteriorOccupancy < config.CloseAnchorMinimumPosteriorOccupancy)
                {
                    continue;
                }

                double snr = Math.Sqrt(profile.Improvement);
                if (IsCloseToAnchor(anchorDistanceFwhm, config) &&
                    profile.ResidualCorrelation < config.CloseAnchorMinimumResidualProfileCorrelation)
                {
                    continue;
                }

                candidates.Add(new RjmcmcPeakCandidate
                {
                    Channel = extra.Channel,
                    Fwhm = extra.Fwhm,
                    Amplitude = extra.Amplitude,
                    Snr = snr,
                    DevianceImprovement = profile.Improvement,
                    PosteriorOccupancy = posteriorOccupancy,
                    CenterStdDev = centerStdDev,
                    ResidualSnr = profile.ResidualSnr,
                    ResidualCorrelation = profile.ResidualCorrelation,
                    AnchorDistanceFwhm = anchorDistanceFwhm,
                    RoiStartChannel = workspace.Roi.StartChannel,
                    RoiEndChannel = workspace.Roi.EndChannel,
                    LocalAnchorChannels = workspace.Roi.AnchorChannels.ToArray(),
                    HaloAnchorChannels = workspace.Roi.HaloAnchorChannels.ToArray(),
                    ReferenceAnchorChannels = workspace.Roi.ReferenceAnchorChannels.ToArray()
                });
            }

            return candidates;
        }

        static IEnumerable<int> EnumerateModelAnchorChannels(RjmcmcRoi roi)
        {
            if (roi == null)
            {
                yield break;
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (int anchorChannel in roi.AnchorChannels)
            {
                if (seen.Add(anchorChannel))
                {
                    yield return anchorChannel;
                }
            }

            foreach (int anchorChannel in roi.HaloAnchorChannels)
            {
                if (seen.Add(anchorChannel))
                {
                    yield return anchorChannel;
                }
            }
        }

        static int CountModelAnchors(RjmcmcRoi roi)
        {
            return EnumerateModelAnchorChannels(roi).Count();
        }

        /// <summary>
        /// Measures the local likelihood loss caused by removing one extra component from the best model.
        /// References: Deep research report, section "Validation and quality criteria", for Poisson deviance and
        /// likelihood-based residual checks; Gulam Razul et al. (2003), NIM A 497, 492-510.
        /// </summary>
        static ProfileDevianceResult ComputeProfileDevianceImprovement(
            RjmcmcState best,
            RjmcmcPeakComponent extra,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcConfig config)
        {
            RjmcmcState full = best.Clone();
            if (!OptimizeProfileLikelihoodState(full, workspace, config))
            {
                return null;
            }

            RjmcmcState reduced = full.Clone();
            if (!RemoveMatchingExtra(reduced, extra))
            {
                return null;
            }

            if (!OptimizeProfileLikelihoodState(reduced, workspace, config))
            {
                return null;
            }

            if (!PassesResidualShapeGate(extra, reduced, workspace, lambda, config, out double residualSnr, out double residualCorrelation))
            {
                return null;
            }

            double improvement = 2.0 * (full.LogLikelihood - reduced.LogLikelihood);
            if (!PeakShapeModel.IsFinite(improvement) || improvement <= 0.0)
            {
                return null;
            }

            return new ProfileDevianceResult
            {
                Improvement = improvement,
                ResidualSnr = residualSnr,
                ResidualCorrelation = residualCorrelation
            };
        }

        static bool RemoveMatchingExtra(RjmcmcState state, RjmcmcPeakComponent extra)
        {
            if (state?.Extras == null || extra == null || state.Extras.Count == 0)
            {
                return false;
            }

            int bestIndex = -1;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; i < state.Extras.Count; i++)
            {
                RjmcmcPeakComponent candidate = state.Extras[i];
                double distance = Math.Abs(candidate.Channel - extra.Channel);
                if (candidate.Channel == extra.Channel &&
                    Math.Abs(candidate.Amplitude - extra.Amplitude) < 1E-6)
                {
                    bestIndex = i;
                    break;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            state.Extras.RemoveAt(bestIndex);
            return true;
        }

        static bool OptimizeProfileLikelihoodState(
            RjmcmcState state,
            RjmcmcRoiWorkspace workspace,
            RjmcmcConfig config)
        {
            if (state == null || workspace == null)
            {
                return false;
            }

            double[] localLambda = new double[workspace.Length];
            if (!EvaluateState(state, workspace, localLambda, config))
            {
                return false;
            }

            int iterations = Math.Max(0, config.ProfileOptimizationIterations);
            double stepScale = 1.0;
            double width = Math.Max(1.0, workspace.Roi.Width - 1);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool improved = false;
                foreach (RjmcmcPeakComponent anchor in state.Anchors)
                {
                    double step = Math.Max(1.0, anchor.Amplitude * 0.10 * stepScale);
                    improved |= TryImproveComponentAmplitude(state, anchor, workspace, localLambda, config, step);
                }

                foreach (RjmcmcPeakComponent extra in state.Extras)
                {
                    double step = Math.Max(1.0, extra.Amplitude * 0.10 * stepScale);
                    improved |= TryImproveComponentAmplitude(state, extra, workspace, localLambda, config, step);
                }

                double backgroundStep = Math.Max(1.0, workspace.MeanObserved * config.BackgroundUpdateFraction * stepScale);
                improved |= TryImproveBackgroundParameter(state, 0, workspace, localLambda, config, backgroundStep);
                improved |= TryImproveBackgroundParameter(state, 1, workspace, localLambda, config, backgroundStep / width);
                improved |= TryImproveBackgroundParameter(state, 2, workspace, localLambda, config, backgroundStep / (width * width));

                if (improved)
                {
                    stepScale = Math.Min(1.0, stepScale * 1.20);
                }
                else
                {
                    stepScale *= 0.50;
                    if (stepScale < 1E-3)
                    {
                        break;
                    }
                }
            }

            return EvaluateState(state, workspace, localLambda, config);
        }

        static bool TryImproveComponentAmplitude(
            RjmcmcState state,
            RjmcmcPeakComponent component,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcConfig config,
            double step)
        {
            double originalValue = component.Amplitude;
            double bestValue = originalValue;
            double bestLogLikelihood = state.LogLikelihood;
            bool improved = false;

            for (int direction = -1; direction <= 1; direction += 2)
            {
                double trialValue = originalValue + direction * step;
                if (trialValue <= 1E-6 || !PeakShapeModel.IsFinite(trialValue))
                {
                    continue;
                }

                component.Amplitude = trialValue;
                if (EvaluateState(state, workspace, lambda, config) &&
                    state.LogLikelihood > bestLogLikelihood + 1E-9)
                {
                    bestLogLikelihood = state.LogLikelihood;
                    bestValue = trialValue;
                    improved = true;
                }
            }

            component.Amplitude = bestValue;
            EvaluateState(state, workspace, lambda, config);
            return improved;
        }

        static bool TryImproveBackgroundParameter(
            RjmcmcState state,
            int parameterIndex,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcConfig config,
            double step)
        {
            double originalValue = GetBackgroundParameter(state, parameterIndex);
            double bestValue = originalValue;
            double bestLogLikelihood = state.LogLikelihood;
            bool improved = false;

            for (int direction = -1; direction <= 1; direction += 2)
            {
                double trialValue = originalValue + direction * step;
                if (!PeakShapeModel.IsFinite(trialValue))
                {
                    continue;
                }

                SetBackgroundParameter(state, parameterIndex, trialValue);
                if (EvaluateState(state, workspace, lambda, config) &&
                    state.LogLikelihood > bestLogLikelihood + 1E-9)
                {
                    bestLogLikelihood = state.LogLikelihood;
                    bestValue = trialValue;
                    improved = true;
                }
            }

            SetBackgroundParameter(state, parameterIndex, bestValue);
            EvaluateState(state, workspace, lambda, config);
            return improved;
        }

        static double GetBackgroundParameter(RjmcmcState state, int parameterIndex)
        {
            switch (parameterIndex)
            {
                case 0:
                    return state.BackgroundIntercept;
                case 1:
                    return state.BackgroundSlope;
                default:
                    return state.BackgroundQuadratic;
            }
        }

        static void SetBackgroundParameter(RjmcmcState state, int parameterIndex, double value)
        {
            switch (parameterIndex)
            {
                case 0:
                    state.BackgroundIntercept = value;
                    break;
                case 1:
                    state.BackgroundSlope = value;
                    break;
                default:
                    state.BackgroundQuadratic = value;
                    break;
            }
        }

        static bool PassesResidualShapeGate(
            RjmcmcPeakComponent extra,
            RjmcmcState reduced,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcConfig config,
            out double residualSnr,
            out double residualCorrelation)
        {
            residualSnr = 0.0;
            residualCorrelation = 0.0;
            if (extra == null || reduced == null || workspace == null)
            {
                return false;
            }

            if (!EvaluateState(reduced, workspace, lambda, config))
            {
                return false;
            }

            int centerLocal = extra.Channel - workspace.Roi.StartChannel;
            if (centerLocal < 0 || centerLocal >= workspace.Length)
            {
                return false;
            }

            int tolerance = CenterToleranceForFwhm(extra.Fwhm, config);
            int residualMaxIndex = FindPositiveResidualMaximumNear(workspace, lambda, centerLocal, tolerance);
            if (residualMaxIndex < 0)
            {
                return false;
            }

            RjmcmcComponentProfile profile = GetProfile(workspace, extra.Channel);
            if (!TryCalculateResidualProfileScore(workspace, lambda, profile, out residualSnr, out residualCorrelation))
            {
                return false;
            }

            double minimumResidualSnr = Math.Max(1.0, config.TargetSnr * config.ResidualMatchedSnrFraction);
            return residualSnr >= minimumResidualSnr &&
                residualCorrelation >= config.MinimumResidualProfileCorrelation;
        }

        static int FindPositiveResidualMaximumNear(
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            int centerLocal,
            int tolerance)
        {
            int start = Math.Max(0, centerLocal - tolerance);
            int end = Math.Min(workspace.Length - 1, centerLocal + tolerance);
            int bestIndex = -1;
            double bestResidual = 0.0;
            for (int localIndex = start; localIndex <= end; localIndex++)
            {
                double residual = workspace.Observed[localIndex] - lambda[localIndex];
                if (residual <= bestResidual)
                {
                    continue;
                }

                if (!IsLocalPositiveResidualMaximum(workspace, lambda, localIndex))
                {
                    continue;
                }

                bestResidual = residual;
                bestIndex = localIndex;
            }

            return bestIndex;
        }

        static bool IsLocalPositiveResidualMaximum(
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            int localIndex)
        {
            double residual = workspace.Observed[localIndex] - lambda[localIndex];
            if (!PeakShapeModel.IsFinite(residual) || residual <= 0.0)
            {
                return false;
            }

            double leftResidual = localIndex > 0
                ? workspace.Observed[localIndex - 1] - lambda[localIndex - 1]
                : Double.NegativeInfinity;
            double rightResidual = localIndex + 1 < workspace.Length
                ? workspace.Observed[localIndex + 1] - lambda[localIndex + 1]
                : Double.NegativeInfinity;
            return residual >= leftResidual && residual >= rightResidual;
        }

        static bool TryCalculateResidualProfileScore(
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcComponentProfile profile,
            out double residualSnr,
            out double residualCorrelation)
        {
            residualSnr = 0.0;
            residualCorrelation = 0.0;
            if (workspace == null ||
                lambda == null ||
                profile == null ||
                !profile.IsValid ||
                profile.StartIndex > profile.EndIndex)
            {
                return false;
            }

            double numerator = 0.0;
            double residualEnergy = 0.0;
            double profileEnergy = 0.0;
            double noiseProfileEnergy = 0.0;
            for (int localIndex = profile.StartIndex; localIndex <= profile.EndIndex; localIndex++)
            {
                double profileValue = profile.RelativeValues[localIndex - profile.StartIndex];
                double residual = workspace.Observed[localIndex] - lambda[localIndex];
                numerator += residual * profileValue;
                residualEnergy += residual * residual;
                profileEnergy += profileValue * profileValue;
                noiseProfileEnergy += Math.Max(1.0, lambda[localIndex]) * profileValue * profileValue;
            }

            if (numerator <= 0.0 ||
                residualEnergy <= 0.0 ||
                profileEnergy <= 0.0 ||
                noiseProfileEnergy <= 0.0)
            {
                return false;
            }

            residualSnr = numerator / Math.Sqrt(noiseProfileEnergy);
            residualCorrelation = numerator / Math.Sqrt(residualEnergy * profileEnergy);
            return PeakShapeModel.IsFinite(residualSnr) &&
                PeakShapeModel.IsFinite(residualCorrelation);
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

        static double LogExponentialPdf(double value, double scale, double logScale)
        {
            if (value <= 0.0 || scale <= 0.0)
            {
                return Double.NegativeInfinity;
            }

            return -logScale - value / scale;
        }
    }
}
