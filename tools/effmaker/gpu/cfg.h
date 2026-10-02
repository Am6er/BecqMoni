// cfg.h — настройки симулятора для GPU. СГЕНЕРИРОВАНО отражением из открытых полей
// `EfficiencySimulator` (всё простое, кроме счётчиков-выходов Count*/Sum*/Last*/Weight*).
// Пересоздать: tools/effmaker/gpu/gen_cfg.ps1. Упаковщик GpuPack.cs ставит КАЖДОЕ поле по имени;
// поле C#, которого здесь нет, и поле здесь, которого нет в C#, — отказ (rm_cfg_set / rm_cfg_check).
#pragma once
#include <stdint.h>
struct Cfg
{
    int Histories;
    int Seed;
    double CoherentFractionOfTotal;
    bool MountingInFront;
    bool ScoreEntranceOnly;
    bool IsoFieldNoCosineWeight;
    bool ElectronEscape;
    bool Bremsstrahlung;
    bool BremFromData;
    double ElectronEscapeSlope;
    double ElectronEscapeT0Kev;
    double ElectronEscapeSoftAmp;
    double ElectronEscapeSoftKev;
    double ElectronEscapeCurve;
    bool ElectronTransport;
    double ElectronStepFraction;
    bool ElectronTransportNoEarlyExit;
    bool ElectronAnyMaterial;
    int BremAlongPath;
    bool ElectronLayerTransport;
    bool ElectronLayerMixedScattering;
    bool ElectronLayerBremAlongPath;
    bool ElectronLayerBremAngular2BS;
    double LayerHardCutoffDeg;
    bool LayerReturnOwn;
    bool LayerReturnCarried;
    bool LayerExitBremsstrahlung;
    int LayerReturnKill;
    bool LayerBornBremsstrahlung;
    double PeakHalfWidthKev;
    bool XrayEscape;
    bool LXrayEscape;
    bool SplitXrayShells;
    bool KLCascade;
    bool CoherentPassesThrough;
    bool KFractionByEnergy;
    bool MeasuredFluorescenceYield;
    bool LightNonproportionality;
    bool LightSubKevCurve;
    bool LightCascadeSplit;
    double LightEtaEh;
    double LightTrackEndKev;
    double LightTrackEndPower;
    bool LightBinUnified;
    bool PeakChannelByTolerance;
    int LYieldSupply;
    bool SingleScatter;
    double ScatterRouletteWeight;
    bool SampleFluorescenceOutside;
    bool AnalogContinuum;
    bool BoundCompton;
    bool DopplerBroadening;
    bool RayleighScatter;
    double CrystalCoherentScale;
    double OutsidePhotoScale;
    bool RayleighToCrystal;
    bool XcomPairThreshold;
    bool AnalogConeSampling;
    bool OutOfConePeakEverywhere;
    bool OutOfConeImportance;
    bool ImportanceSampling;
    bool ImportanceSamplingNoWeight;
    bool PositronTransport;
    bool PositronOffset;
    double ResolutionPeakHalfWidthKev;
    bool CurvePeakResolutionWindow;
    double CurveTightPeakHalfWidthKev;
    double ElectronCarryDetour;
    bool TotalFullSphere;
};

#define RM_CFG_FIELDS(X) \
    X(I, Histories) \
    X(I, Seed) \
    X(D, CoherentFractionOfTotal) \
    X(B, MountingInFront) \
    X(B, ScoreEntranceOnly) \
    X(B, IsoFieldNoCosineWeight) \
    X(B, ElectronEscape) \
    X(B, Bremsstrahlung) \
    X(B, BremFromData) \
    X(D, ElectronEscapeSlope) \
    X(D, ElectronEscapeT0Kev) \
    X(D, ElectronEscapeSoftAmp) \
    X(D, ElectronEscapeSoftKev) \
    X(D, ElectronEscapeCurve) \
    X(B, ElectronTransport) \
    X(D, ElectronStepFraction) \
    X(B, ElectronTransportNoEarlyExit) \
    X(B, ElectronAnyMaterial) \
    X(I, BremAlongPath) \
    X(B, ElectronLayerTransport) \
    X(B, ElectronLayerMixedScattering) \
    X(B, ElectronLayerBremAlongPath) \
    X(B, ElectronLayerBremAngular2BS) \
    X(D, LayerHardCutoffDeg) \
    X(B, LayerReturnOwn) \
    X(B, LayerReturnCarried) \
    X(B, LayerExitBremsstrahlung) \
    X(I, LayerReturnKill) \
    X(B, LayerBornBremsstrahlung) \
    X(D, PeakHalfWidthKev) \
    X(B, XrayEscape) \
    X(B, LXrayEscape) \
    X(B, SplitXrayShells) \
    X(B, KLCascade) \
    X(B, CoherentPassesThrough) \
    X(B, KFractionByEnergy) \
    X(B, MeasuredFluorescenceYield) \
    X(B, LightNonproportionality) \
    X(B, LightSubKevCurve) \
    X(B, LightCascadeSplit) \
    X(D, LightEtaEh) \
    X(D, LightTrackEndKev) \
    X(D, LightTrackEndPower) \
    X(B, LightBinUnified) \
    X(B, PeakChannelByTolerance) \
    X(I, LYieldSupply) \
    X(B, SingleScatter) \
    X(D, ScatterRouletteWeight) \
    X(B, SampleFluorescenceOutside) \
    X(B, AnalogContinuum) \
    X(B, BoundCompton) \
    X(B, DopplerBroadening) \
    X(B, RayleighScatter) \
    X(D, CrystalCoherentScale) \
    X(D, OutsidePhotoScale) \
    X(B, RayleighToCrystal) \
    X(B, XcomPairThreshold) \
    X(B, AnalogConeSampling) \
    X(B, OutOfConePeakEverywhere) \
    X(B, OutOfConeImportance) \
    X(B, ImportanceSampling) \
    X(B, ImportanceSamplingNoWeight) \
    X(B, PositronTransport) \
    X(B, PositronOffset) \
    X(D, ResolutionPeakHalfWidthKev) \
    X(B, CurvePeakResolutionWindow) \
    X(D, CurveTightPeakHalfWidthKev) \
    X(D, ElectronCarryDetour) \
    X(B, TotalFullSphere)
