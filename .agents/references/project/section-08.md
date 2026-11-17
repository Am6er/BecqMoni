<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Architecture

**Manager singletons.** Global state is held in `*Manager` classes accessed via
`GetInstance()`: `GlobalConfigManager`, `DeviceConfigManager`, `NuclideDefinitionManager`,
`DocumentManager`, `MeasurementResultManager`, `ROIConfigManager`. They load/save
serialized XML config. Startup order matters (see `Program.cs` / the harness `Main`).

**Device abstraction.** `DeviceType` is a static registry mapping a device Id to a triple of
`Type`s: `{ DeviceConfigFormType, DeviceControllerType, DeviceConfigType }`. Each supported
detector is implemented as a set of parallel classes following the same naming pattern —
`XxxDeviceConfig` / `XxxDeviceController` (extends abstract `DeviceController`) /
`XxxDeviceForm` / `XxxIn` (input/stream reader). Supported devices include AudioInput,
AtomSpectra(VCP), RadiaCode, Obsidian. To add a device, add the four classes and register the
`DeviceType`.

**Document / view layer.** `DocumentManager` owns open documents; `DocEnergySpectrum` is the
spectrum document. The UI is a docking layout of "DC" panel views under `DCControlPanel`
(`DCEnergyCalibrationView`, `DCFwhmCalibrationView`, `DCPeakDetectionView`, `DCResultView`,
`DCSampleInfoView`, `DCSpectrumListView`, `DCCountRateView`, ...). `EnergySpectrumView` is the
large custom-drawn spectrum chart. `MainForm` is the shell.

**Data model.** `EnergySpectrum`, `ResultData`, `MeasurementResult(Collection)`, `Peak`, ROI
types. Note the versioned duplicate classes (`ResultData_097b`, `EnergySpectrum_097b`,
`DeviceConfigInfo_097b`, etc.) — these exist solely for backward-compatible deserialization of
older config/result files; the unsuffixed class is current.

**Calibration.** Energy: `PolynomialEnergyCalibration`, `NonlinearEnergyCalibration` (base
`EnergyCalibration`). Resolution: `SqrtFwhmCalibration`, `SimpleSqrtFwhmCalibration` (base
`FwhmCalibration`). `Utils/CalibrationSolver` fits calibration curves.

**Peak detection pipeline** (the actively developed area — `PeakDetector.DetectPeak`):
1. Optional background subtraction + smoothing (`SpectrumAriphmetics` — SMA/WMA, SNIP
   continuum estimation).
2. `FWHMPeakDetector.PeakFinder` — matched-filter peak finder (SNR = z-score against a
   zero-sum kernel). This is the only detector.
3. `CollectPeaks` turns finder centroids into `Peak`s and labels them from the active
   nuclide set (`MatchNuclide`, `NuclideSet.HideUnknownPeaks`).
   Peak shape modeling lives in `Utils/PeakShapeModel`, `Utils/PseudoVoigtProfile`.

**Full-spectrum analysis** (`FullSpectrumAnalysis/`, ported from `tools/pie`): an alternative
to peak search — the spectrum, with the measured background subtracted, is decomposed into
nuclide/chain templates plus a non-negative spline continuum by weighted NNLS with Huber
reweighting (`FsaAnalyzer`). The composition is inferred from the peak-search labels and
expanded from `nucdb` into whole decay chains (`FsaCompositionInference`, default "From
NucBase"; the nuclide sets are the alternative) — the builtin nuclide table and the auto-added
room nuclides were removed 01.09.2026. The energy scale is anchored on the model's own
full-energy peaks (`AMBER17`, on by default); the drift grid is still in the code but defaults to
one node since 01.09.2026. Templates come from the response matrix of the device geometry or,
without it, from the efficiency curve (`FsaEfficiency`), which lives with its geometry in the
device config (`EfficiencyConfigData`), not in a ROI config. Runs in the background
(`FsaAnalysisSession`, one per document) and is drawn as a stacked chart by
`EnergySpectrumView.Fsa.cs` (`UpdateFsaOverlay` / `ShowFsaOverlay`); switched on by
`BackgroundMode.ShowFSA` ("Show FSA" in the background-mode dropdown of the spectrum toolbar).
A user-facing illustrated explainer of geometry → matrix → FSA → activity is
`docs/how-it-works.html` (`AMBER238`).
