// physics.h — поколение физики переноса, которое несёт эта сборка ядра (`AMBER219`, П245).
//
// Число — копия `ResponseMatrix.PhysicsVersion` (BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs)
// на момент, когда перенос `sim_*.cuh` сверен с C#-оригиналом. Приложение читает его из
// `rm_build_info()` и при расхождении со своим `PhysicsVersion` гасит галку «Use Nvidia GPU»
// с причиной «библиотека собрана для другой физики»; сторож `tools/check_gpu_dll.py` сверяет
// то же на дереве. Поднимая `PhysicsVersion` в C#, перенеси правку в ядро и подними число здесь.
//
// 27 (`M14`, П343 19.10.2026) — табличное I ESTAR по ближайшей плотности (`EstarCalculator`).
// Ядро таблиц ESTAR не считает: пробег и выход слоёв ему выгружает ЦП (`GpuPackTables.
// WriteElectronMats` ← `ElectronData.Material`), так что правка доезжает упаковкой, а здесь —
// только поколение.
//
// 28 (`AMBER274`, полоса `amber274p28`, 30.10.2026) — страгглинг потерь электрона по модели
// Урбана у всех электронов переноса (`ResidualAfter`/`UrbanLoss`, sim_electron.cuh — перенос
// ElectronTransport.cs; ключ `ElectronStraggling` в cfg.h; I, доля радиационной и постоянные
// Урбана вещества — в `ElectronMaterialG`), и раздел электрона пробы на GPU (`rm_esrc`,
// `ElectronSourceKernel`).
//
// 29 (`AMBER274`, полоса `phys29`, 01.11.2026; решение Amber 30.10.2026 «Слои + кристалл сразу») —
// перенос электрона, сходящийся по шагу (ключ `ElectronMixedTransport` в cfg.h, рычаг замера
// `ElectronMixedParts`): в слоях сечения смешанной схемы при энергии середины шага, в кристалле —
// та же смешанная схема вместо Хайленда на шаг (`TransportElectron`, sim_electron.cuh — перенос
// ElectronTransport.cs); потолок шагов под ключом — `TransportMaxStepsMixed` (decl_electron.inc).
#pragma once
//
// 30 (`AMBER274`, полоса `phys29c`, 02.11.2026) — правки трёх критиков физики 29 (ключ `ElectronElasticRefine`,
// рычаг `ElectronRefineParts`) и ядерная часть упругого рассеяния по DPWA (ключ `ElectronScreeningDpwa`, узлы —
// `ScatterElementG.DpwaEl/DpwaPos`): части Z² и Z порознь, предел угла e–e, Мотт у всех Z, позитрон, пересчёт
// затёртых долей, сечения шага на концах полного шага с мажорантой (`RefinedElasticStep`, `RefinedHardCollision`,
// `RefinedAngle`, `CrystalHardCollision` — sim_electron.cuh, перенос ElectronTransport.cs); признак позитрона
// в переносе (`TransportElectron`, `TransportInLayers`, `ElectronLoss`, `CarriedElectronEnters`).
#define RM_PHYSICS_VERSION 30
