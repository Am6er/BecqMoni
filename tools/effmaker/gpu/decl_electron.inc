// decl_electron.inc — поля и объявления методов `Sim` модуля «электрон»: перенос электрона
// по кристаллу и в слоях обвязки, тормозное по ходу, занос электрона, рождённого вне
// кристалла. Входит В ТЕЛО `struct Sim` (sim.cuh). Определения — sim_electron.cuh.
// Полоса П221 (`AMBER160`), полоса Г. Оригиналы: ElectronTransport.cs (partial того же
// `EfficiencySimulator`) и куски EfficiencySimulator.cs вокруг заноса (:7472–7789).

// --- типы -------------------------------------------------------------------
// = ElectronTransport.cs:119 ElectronBirth
// ⚠ Тип нужен и объявлению `ElectronLoss` (полоса В), а её decl-файл входит в `Sim`
// РАНЬШЕ этого. Поэтому под защитой: если распорядитель вынесет перечисление в
// sim.cuh (до `struct Sim`) с тем же `#define`, здесь оно не повторится.
// Перечисление ОБЛАСТНОЕ: `Isotropic` иначе столкнулся бы с методом `Isotropic(...)`.
#ifndef RM_ELECTRON_BIRTH_DEFINED
#define RM_ELECTRON_BIRTH_DEFINED
enum class ElectronBirth : int
{
    Isotropic = 0,   // оже-электроны каскада, неизвестный процесс
    Photo = 1,       // фотоэлектрон: Заутер—Гаврила относительно кванта
    Given = 2,       // направление задано вызывающим (комптон — из кинематики)
    Pair = 3,        // лептон пары: модифицированный Цай относительно кванта
};
#endif

// Делегаты C# `Action<double ×7>` (`pushAnalog` = PushPending, `pushTotal` =
// PushTotalPending, `layerBremPush` — один из них или null) → номер очереди.
// Имена значений — ИМЕНА ПОЛЕЙ-делегатов C#: вызов `CarriedElectronDeposit(…, this.pushAnalog)`
// переносится как `CarriedElectronDeposit(…, pushAnalog)`.
#ifndef RM_PUSH_QUEUE_DEFINED
#define RM_PUSH_QUEUE_DEFINED
enum PushQueue : int
{
    pushNone = 0,     // null
    pushAnalog = 1,   // = EfficiencySimulator.cs:1842 this.pushAnalog = this.PushPending
    pushTotal = 2,    // = EfficiencySimulator.cs:1843 this.pushTotal = this.PushTotalPending
};
#endif

// --- константы --------------------------------------------------------------
// = EfficiencySimulator.cs:55–57 и ScatteringData.cs:48 — общие с другими модулями:
// под защитой, чтобы второй модуль, объявивший их тем же `#define`, не столкнулся.
#ifndef RM_SIM_PHYS_CONST_DEFINED
#define RM_SIM_PHYS_CONST_DEFINED
static constexpr real ElectronMassKev = (real)510.99895;
static constexpr real ClassicalRadiusCm = (real)2.8179403262e-13;
static constexpr real Avogadro = (real)6.02214076e23;
#endif
#ifndef RM_FINE_STRUCTURE_DEFINED
#define RM_FINE_STRUCTURE_DEFINED
static constexpr real FineStructure = (real)7.2973525693e-3;   // = ScatteringData.FineStructure
#endif

// = ElectronTransport.cs:135 TransportCutKev — ниже кладётся на месте, кэВ.
static constexpr real TransportCutKev = (real)1.0;
// = ElectronTransport.cs:143 TransportMaxSteps
static constexpr int TransportMaxSteps = 400;
// = ElectronTransport.cs:271 MottMaxZ
static constexpr int MottMaxZ = 30;
// = EfficiencySimulator.cs:7676 AirDensity (только абляция `ElectronWalkToCrystal`)
static constexpr real AirDensity = (real)1.205e-3;

// C# растит `layerHardShare` под число элементов вещества слоя; здесь — потолок
// (проба грунта бывает и в 15 элементов — взято с запасом).
// ⛔ Упаковщик сцены ОБЯЗАН отказать веществу с `LayerScatterElementsLen` больше него
// (host_scene.h полосы Б на 02.10.2026 этого НЕ проверяет — иначе выход за массив нити).
#define RM_LAYER_SCATTER_MAX 32

// --- состояние истории (поля C#) -------------------------------------------
// = EfficiencySimulator.cs:535 carriedInCrystal — перенос по кристаллу идёт у ЗАНЕСЁННОГО электрона.
bool carriedInCrystal;
// = EfficiencySimulator.cs:546 layerBremPush (делегат) → номер очереди, pushNone = null.
int layerBremPush;
// = EfficiencySimulator.cs:553 layerBremEnabled
bool layerBremEnabled;
// = ElectronTransport.cs:746 layerBremZ — Z текущего вещества у переноса в слоях.
real layerBremZ;
// = ElectronTransport.cs:297 layerHardShare — рабочий массив долей жёстких сечений.
real layerHardShare[RM_LAYER_SCATTER_MAX];

// Счётчики-диагностика (матрица их не читает; на ход и на ГСЧ не влияют).
// = EfficiencySimulator.cs:457–458 (общие с ElectronLoss полосы В — объявлены здесь)
long long CountBremPhotons;
double SumBremKev;
// = EfficiencySimulator.cs:467–468
long long CountLayerEscapes, CountLayerReturns, CountLayerCarries;
double SumLayerReturnKev;
// = EfficiencySimulator.cs:476
long long CountLayerSteps, CountLayerHardCollisions;
// = EfficiencySimulator.cs:525–528
long long CountLayerEscapesCarried, CountLayerReturnsCarried;
long long CountLayerBorn;
long long CountLayerReturnsSameFace, CountLayerReturnsOtherFace, CountLayerReturnsKilled;
// = EfficiencySimulator.cs:559–560, 568
long long CountLayerBremPhotons;
double SumLayerBremKev;
double SumLayerPathG;

// не перенесено (СБОРКА, не горячий путь — числа кладёт упаковщик, вызывая сам C#):
// TsaiRadiationLength (ElectronTransport.cs:455), тела CrystalRadiationLength /
// LayerRadiationLength / LayerScatterElements / LayerBremZ (счёт по составу) и кэши
// crystalRadiationLength, layerRadiationCache, layerScatterCache, layerBremZCache,
// carryCache, layerBremCache, waterTable → поля `MaterialG` / `SceneG` полосы Б;
// здесь от этих методов остались только доступы с теми же именами.

// --- методы -----------------------------------------------------------------
RM_DEV void InitElectron();

// Доступ к данным, которые в C# — поля симулятора / кэши по веществу.
RM_DEV const RegionG& CrystalRegion();
RM_DEV const ElectronMaterialG& ElectronMat();     // this.electron (кристалл)
RM_DEV const ThickTargetBremG& BremTab();          // this.bremTable (кристалл)
RM_DEV real CrystalDensity();                      // this.geometry.Crystal.Density
RM_DEV real CrystalRadiationLength();
RM_DEV real LayerRadiationLength(int material);
RM_DEV int LayerScatterElements(int material, int& count);
RM_DEV real LayerBremZ(int material);
RM_DEV int CarryMedium(int material);
RM_DEV int LayerBrem(int material);
RM_DEV int WaterTable();
RM_DEV void PushByQueue(int queue, real x, real y, real z, real ux, real uy, real uz, real energyKev);

// ElectronTransport.cs
RM_DEV real LayerHardCutoffMu();
static RM_DEV real ScreeningTwoA(int z, real z13, real beta2, real betaGamma);
RM_DEV void LayerElasticStep(const ScatterElementG* elements, int elementsLen, real tKev, real muCut,
                             real& hardPerCm, real& softTransportPerCm, real* hardShare);
RM_DEV bool LayerHardCollision(const ScatterElementG* elements, int elementsLen, const real* hardShare,
                               real hardPerCm, real tKev, real muCut, real& ux, real& uy, real& uz);
RM_DEV real CrystalNearestFace(real x, real y, real z);
RM_DEV real RegionNearestFace(int r, real x, real y, real z);
static RM_DEV real HighlandTheta0(real tKev, real xOverX0);
RM_DEV real SampleHingeMu(real theta0);
RM_DEV real SauterCosine(real tKev);
RM_DEV real TsaiCosine(real tKev);
RM_DEV real Brem2BSCosine(real tKev, real kKev, real z);
static RM_DEV real Brem2BSReject(real y, real ratio, real ratio1, real ratio2, real fz);
RM_DEV void ElectronBirthDirection(ElectronBirth birth, real te, real rx, real ry, real rz,
                                   real& ux, real& uy, real& uz);
static RM_DEV void ComptonElectronDirection(real e, real ux0, real uy0, real uz0,
                                            real scattered, real ux1, real uy1, real uz1,
                                            real& dx, real& dy, real& dz);
RM_DEVF real TransportElectron(real x, real y, real z, real ux, real uy, real uz,
                               real te, bool alongPath, int depth, real& radiated, real& lost);
RM_DEVF bool EscapeOrReturn(real& x, real& y, real& z, real& ux, real& uy, real& uz,
                            real toEdge, real residualAtFace, real cap, int depth,
                            real& residual, real& t, real& escaped);
RM_DEV int CrystalFaceId(real x, real y, real z);
RM_DEVF real LayerBremsstrahlung(real x, real y, real z, real te);
RM_DEVF bool TransportInLayers(real& x, real& y, real& z, real& ux, real& uy, real& uz,
                               real& t, int depth);
static RM_DEV real LayerMidEnergy(const ElectronMaterialG& medium, real residualG, real halfG);
RM_DEVF void LayerStepBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                     real tKev, real stepG, real anchor, real cap,
                                     const ThickTargetBremG& table, real& radiated);
RM_DEVF void LayerRestBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                     real tKev, real cap, const ThickTargetBremG& table, real& radiated);
RM_DEVF void LayerEmitBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                     real k, real tKev, real cap, real& radiated);
RM_DEVF void StepBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                real tKev, real stepG, real anchor, real te, int depth,
                                real& radiated, real& lost);
RM_DEVF void RestBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                real tKev, real te, int depth, real& radiated, real& lost);
RM_DEVF void EmitBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                real k, real tKev, real te, int depth, real& radiated, real& lost);
RM_DEV real EscapeEnergy(real residualG, real cap);

// EfficiencySimulator.cs — занос электрона, рождённого вне кристалла
RM_DEVF bool ElectronReachesCrystal(real x, real y, real z, real ux, real uy, real uz, real energyKev);
RM_DEVF bool ElectronCarryDeposit(real x, real y, real z, real ux, real uy, real uz, real energyKev,
                                  real& depositKev);
RM_DEVF bool CarriedElectronEnters(real x, real y, real z, ElectronBirth birth, real te,
                                   real rx, real ry, real rz, int material, int push,
                                   real& xIn, real& yIn, real& zIn,
                                   real& uxIn, real& uyIn, real& uzIn, real& tIn);
RM_DEVF bool CarriedElectronReaches(real x, real y, real z, ElectronBirth birth, real te,
                                    real rx, real ry, real rz, int material, int push);
RM_DEVF real CarriedElectronDeposit(real x, real y, real z, ElectronBirth birth, real te,
                                    real rx, real ry, real rz, int material, int push);
RM_DEVF bool ElectronWalkToCrystal(real x, real y, real z, real ux, real uy, real uz, real energyKev,
                                   real& usedFraction);
RM_DEVF real OutsideBremsstrahlung(real x, real y, real z, real te, int material, int push);
