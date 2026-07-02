using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
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
            public List<RjmcmcPeakCandidate> Candidates;
            public double LogPosterior;
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

            List<RjmcmcRoi> rois = SelectProcessableRois(BuildRois(foregroundSpectrum, finder, peakConfig, fwhmCalibration, config), config);
            List<RjmcmcPeakCandidate> extraCandidates = ProcessRois(foregroundSpectrum, fixedBackgroundSpectrum, fwhmCalibration, rois, config);
            result.ExtraCandidates.AddRange(extraCandidates);
            return result;
        }

        /// <summary>
        /// Applies the ROI budget to the FWHM-anchored search regions before the expensive sampler runs.
        /// References: Deep research report, sections "Bayesian variable-dimensional models" and "Implementation priorities",
        /// recommending RJMCMC only for small ambiguous ROIs rather than the whole spectrum; Gulam Razul et al. (2003),
        /// NIM A 497, 492-510.
        /// </summary>
        static List<RjmcmcRoi> SelectProcessableRois(List<RjmcmcRoi> rois, RjmcmcConfig config)
        {
            List<RjmcmcRoi> selected = new List<RjmcmcRoi>();
            foreach (RjmcmcRoi roi in rois)
            {
                if (selected.Count >= config.MaxRois)
                {
                    break;
                }

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

        /// <summary>
        /// Splits overly wide or crowded ROIs so each sampler instance remains local and bounded.
        /// References: Deep research report, section "Implementation priorities"; Green (1995), Biometrika 82(4), 711-732,
        /// for dimension-changing model search where computation grows with local model dimension.
        /// </summary>
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

            int chainCount = Math.Max(1, config.ChainCount);
            RjmcmcRoiWorkspace[] workspaces = new RjmcmcRoiWorkspace[rois.Count];
            RjmcmcChainResult[][] chainResults = new RjmcmcChainResult[rois.Count][];
            for (int roiIndex = 0; roiIndex < rois.Count; roiIndex++)
            {
                workspaces[roiIndex] = CreateWorkspace(foregroundSpectrum, fixedBackgroundSpectrum, fwhmCalibration, rois[roiIndex], config);
                chainResults[roiIndex] = new RjmcmcChainResult[chainCount];
            }

            int totalJobs = rois.Count * chainCount;
            Parallel.For(
                0,
                totalJobs,
                new ParallelOptions { MaxDegreeOfParallelism = chainCount },
                jobIndex =>
                {
                    int roiIndex = jobIndex / chainCount;
                    int chainIndex = jobIndex % chainCount;
                    RjmcmcRoiWorkspace workspace = workspaces[roiIndex];
                    chainResults[roiIndex][chainIndex] = workspace.IsUsable
                        ? ProcessRoiChain(workspace, config, config.Seed + roiIndex + chainIndex * 7919)
                        : CreateEmptyChainResult();
                });

            for (int roiIndex = 0; roiIndex < rois.Count; roiIndex++)
            {
                candidates.AddRange(SelectBestChainCandidates(chainResults[roiIndex], config.MaxExtraPeaksPerRoi));
            }

            return candidates;
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
                MeanObserved = observed.Length > 0 ? sumResidual / observed.Length : 0.0,
                AmplitudeScale = amplitudeScale,
                LogAmplitudeScale = Math.Log(amplitudeScale),
                FixedCenterSigmaChannels = EstimateFixedCenterSigmaChannels(roi, fwhmCalibration, config),
                IsUsable = isUsable
            };
            return workspace;
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
            foreach (int anchorChannel in roi.AnchorChannels)
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
                double fwhm = Math.Max(1.0, fwhmCalibration.ChannelToFwhm(centerChannel));
                RjmcmcComponentProfile profile = new RjmcmcComponentProfile
                {
                    Fwhm = fwhm,
                    StartIndex = 0,
                    EndIndex = -1,
                    RelativeValues = new double[0],
                    IsValid = false
                };
                profiles[localCenter] = profile;

                if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
                {
                    continue;
                }

                double leftSupport = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
                double rightSupport = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
                if (!PeakShapeModel.IsFinite(leftSupport) || !PeakShapeModel.IsFinite(rightSupport))
                {
                    continue;
                }

                int startChannel = Math.Max(roi.StartChannel, centerChannel - Convert.ToInt32(Math.Ceiling(leftSupport)));
                int endChannel = Math.Min(roi.EndChannel, centerChannel + Convert.ToInt32(Math.Ceiling(rightSupport)));
                if (startChannel > endChannel)
                {
                    profile.IsValid = true;
                    continue;
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
            }

            return profiles;
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

                if (iteration >= config.BurnIn && current.LogPosterior > best.LogPosterior)
                {
                    best = current.Clone();
                }
            }

            return new RjmcmcChainResult
            {
                Candidates = CollectCandidates(best, workspace, lambda, config),
                LogPosterior = best.LogPosterior
            };
        }

        static RjmcmcChainResult CreateEmptyChainResult()
        {
            return new RjmcmcChainResult
            {
                Candidates = new List<RjmcmcPeakCandidate>(),
                LogPosterior = Double.NegativeInfinity
            };
        }

        static List<RjmcmcPeakCandidate> SelectBestChainCandidates(
            IEnumerable<RjmcmcChainResult> chainResults,
            int maxCandidateCount)
        {
            RjmcmcChainResult best = chainResults
                .Where(result => result != null)
                .OrderByDescending(result => result.LogPosterior)
                .FirstOrDefault();
            if (best == null || best.Candidates == null || best.Candidates.Count == 0)
            {
                return new List<RjmcmcPeakCandidate>();
            }

            return best.Candidates
                .OrderByDescending(candidate => candidate.DevianceImprovement)
                .Take(maxCandidateCount)
                .OrderBy(candidate => candidate.Channel)
                .ToList();
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
            int[] observed = workspace.Observed;
            RjmcmcState state = new RjmcmcState
            {
                Anchors = new List<RjmcmcPeakComponent>(roi.AnchorChannels.Count),
                Extras = new List<RjmcmcPeakComponent>()
            };

            EstimateBackground(workspace, out double leftMean, out double rightMean);
            int width = Math.Max(1, observed.Length - 1);
            state.BackgroundIntercept = Math.Max(0.1, leftMean);
            state.BackgroundSlope = (rightMean - leftMean) / width;

            foreach (int anchorChannel in roi.AnchorChannels)
            {
                int localIndex = anchorChannel - roi.StartChannel;
                localIndex = Math.Max(0, Math.Min(observed.Length - 1, localIndex));
                double background = FixedBackgroundAt(workspace.FixedBackground, localIndex) + BackgroundAt(state, localIndex);
                double amplitude = Math.Max(1.0, observed[localIndex] - background);
                state.Anchors.Add(new RjmcmcPeakComponent
                {
                    Channel = anchorChannel,
                    Fwhm = GetProfile(workspace, anchorChannel)?.Fwhm ?? 1.0,
                    Amplitude = amplitude
                });
            }

            return state;
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

            double leftBackground = BackgroundAt(state, 0);
            double rightBackground = BackgroundAt(state, workspace.Length - 1);
            if (leftBackground < 0.0 || rightBackground < 0.0)
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
            double[] fixedBackground = workspace.FixedBackground;
            if (fixedBackground == null)
            {
                for (int i = 0; i < length; i++)
                {
                    double background = intercept + slope * i;
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
                    double background = fixedBackground[i] + intercept + slope * i;
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
            if (localIndex < 0 || localIndex >= workspace.Profiles.Length)
            {
                return null;
            }

            return workspace.Profiles[localIndex];
        }

        static double BackgroundAt(RjmcmcState state, int localIndex)
        {
            return state.BackgroundIntercept + state.BackgroundSlope * localIndex;
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
            if (BackgroundAt(proposal, 0) < 0.0 || BackgroundAt(proposal, workspace.Roi.Width - 1) < 0.0)
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
            return Math.Max(1.0, 0.20 * Math.Max(1.0, fwhm));
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
        static List<RjmcmcPeakCandidate> CollectCandidates(
            RjmcmcState best,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
            RjmcmcConfig config)
        {
            List<RjmcmcPeakCandidate> candidates = new List<RjmcmcPeakCandidate>();
            if (best.Extras.Count == 0)
            {
                return candidates;
            }

            foreach (RjmcmcPeakComponent extra in best.Extras.OrderBy(component => component.Channel))
            {
                double improvement = ComputeDevianceImprovement(best, extra, workspace, lambda, config);
                if (improvement < config.MinDevianceImprovement)
                {
                    continue;
                }

                double snr = Math.Sqrt(Math.Max(0.0, improvement));
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

        /// <summary>
        /// Measures the local likelihood loss caused by removing one extra component from the best model.
        /// References: Deep research report, section "Validation and quality criteria", for Poisson deviance and
        /// likelihood-based residual checks; Gulam Razul et al. (2003), NIM A 497, 492-510.
        /// </summary>
        static double ComputeDevianceImprovement(
            RjmcmcState best,
            RjmcmcPeakComponent extra,
            RjmcmcRoiWorkspace workspace,
            double[] lambda,
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

            if (!EvaluateState(reduced, workspace, lambda, config))
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
