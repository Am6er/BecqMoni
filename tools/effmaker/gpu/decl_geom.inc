// decl_geom.inc — поля и объявления методов `struct Sim` модуля ГЕОМЕТРИИ: кэши μ
// областей на энергию, навигация (разбор луча, кэш луча), путь в кристалле, предел
// пути, розыгрыш точки вылета и направления. Входит В ТЕЛО `struct Sim` (sim.cuh);
// определения — sim_geom.cuh. Полоса П221 (`AMBER160`), часть Б.
//
// ⚠ float: константы обхода рассчитаны на double (map_geometry.md §5.4). Перенесены
// КАК ЕСТЬ (ступень 1 приёмки — double); каждое место, где они вырождаются во float,
// помечено `// ⚠ float:` в sim_geom.cuh.

// --- константы класса (EfficiencySimulator.cs:58, :65) ---
// = `const double Eps = 1e-9` (см).
// ⚠ float: 1e-9 см ниже ULP float уже при |x| ≈ 0.01 см — сдвиг на Eps во float
// вырождается в ноль; полуоткрытость интервалов держится только порядком сравнений.
static constexpr real Eps = (real)1e-9;
// = `const double Far = 1e300`.
// ⚠ float: 1e300 во float — бесконечность; взят FLT_MAX, сравнения `t1 < Far` те же.
#ifdef RM_REAL_FLOAT
static constexpr real Far = FLT_MAX;
#else
static constexpr real Far = 1e300;
#endif

// --- кэш ослабления областей на ОДНУ энергию (`Region`, EfficiencySimulator.cs:2054-2081) ---
// Индекс — номер области в `D.regions` (= `regionArray`). Снимок состава (`Snapshot`)
// неизменяем и лежит в `RegionG`; здесь — только то, что меняется с энергией.
real regMuEnergy[RM_MAX_REG];                 // muEnergy (−1 — пусто)
real regMuTotal[RM_MAX_REG], regMuNoCoherent[RM_MAX_REG], regMuIncoherent[RM_MAX_REG],
     regMuCoherent[RM_MAX_REG], regMuPair[RM_MAX_REG];
bool regHasTotal[RM_MAX_REG], regHasNoCoherent[RM_MAX_REG], regHasIncoherent[RM_MAX_REG],
     regHasCoherent[RM_MAX_REG], regHasPair[RM_MAX_REG];
// bracketLo/bracketHi областей подряд: элемент i области r — [D.regions[r].SnapBase + i].
int bracketLo[RM_MAX_SNAP_TOTAL], bracketHi[RM_MAX_SNAP_TOTAL];
real regLogEnergy[RM_MAX_REG];                // logEnergy
bool regBracketed[RM_MAX_REG];                // bracketed

// --- разбиение луча (`T43`, EfficiencySimulator.cs:4325-4346) ---
// C# держит `rayCross/raySeg` и запасную пару `raySpareCross/Seg` и МЕНЯЕТ ИХ МЕСТАМИ
// в SaveRay/RestoreRay. Здесь две пары буферов и номер текущей (`rayCur`): обмен
// ссылок = смена номера, содержимое не копируется, как и в C#.
real rayCrossBuf[2][RM_MAX_EVENTS];
int raySegBuf[2][RM_MAX_EVENTS + 1];          // область отрезка, −1 = null
int rayCur;                                   // какая пара — rayCross/raySeg
bool rayAllocated;                            // = `rayCross != null` C#: был ли хоть один разбор
int rayCount;
real rayX, rayY, rayZ, rayUx, rayUy, rayUz;
bool rayValid;
// = EfficiencySimulator.ForgetRay (`A315`, П221): кэш луча не переживает историю.
RM_DEV void ForgetRay() { rayValid = false; }
real eventT[RM_MAX_EVENTS];
int eventCode[RM_MAX_EVENTS];
real spanBuf[4];
int raySaveCount, raySaveDepth;
real raySaveX, raySaveY, raySaveZ, raySaveUx, raySaveUy, raySaveUz;
bool raySaveValid, raySaveSwapped;

// --- счётчики обхода (EfficiencySimulator.cs:3726, :4786) ---
// CountMu объявлен здесь (в C# — одной строкой с прочими), растят его обходы
// полосы фотона (`ToCrystal` :4814, `ScatteredContribution` :5020, `KillDepthToExit` :5111).
long long CountAt, CountStep, CountMu, CountWalk;
long long CountPathLimitCut;

// Начальные значения полей модуля — инициализаторы полей C# (`muEnergy = -1.0` и т.д.).
// Ядро зовёт ОДИН раз при создании `Sim` нити (README, «Начальное состояние нити»).
RM_DEVF void InitGeom();
// Прежнее имя того же — им зовёт kernels.cuh (`InitThread`); оставлено, чтобы не
// править чужой файл.
RM_DEV void GeomInit() { InitGeom(); }

// Область r ГОТОВОЙ структурой (доступ к D.regions).
RM_DEV const RegionG& RegG(int r) const { return D.regions[r]; }

// --- методы `Region` (EfficiencySimulator.cs:2083-2432) → `Reg<Имя>(r, …)` ---
RM_DEVF void RegEnsureBracket(int r, real energyKev);
RM_DEVF real RegTotal(int r, real energyKev);
RM_DEVF real RegNoCoherent(int r, real energyKev);
RM_DEVF real RegChannel(int r, real energyKev, PhotonProcess process);
RM_DEVF int RegPrepareElements(int r, real energyKev);
RM_DEVF int RegElementZ(int r, int i);
RM_DEVF bool RegElementKnown(int r, int i);
RM_DEVF real RegElementFraction(int r, int i);
RM_DEVF real RegElementCrossSection(int r, int i, real energyKev, PhotonProcess process, bool thresholdPair);
RM_DEVF real RegPairThreshold(int r, real energyKev);
RM_DEVF real RegMu(int r, real energyKev, bool withoutCoherent);
RM_DEVF real RegIncoherent(int r, real energyKev);
RM_DEVF real RegCoherent(int r, real energyKev);
RM_DEVF real RegPair(int r, real energyKev, bool thresholdPair);
RM_DEVF void RegRetune(int r, real energyKev);
RM_DEVF bool RegContains(int r, real x, real y, real z);
RM_DEVF bool RegContains(int r, real x, real y, real z, real rr);

// --- навигация (EfficiencySimulator.cs:4027-4786) ---
// Область — индекс в D.regions, −1 = null (вне сцены).
RM_DEVF int At(real x, real y, real z);
RM_DEVF int SegmentAt(real bound);
RM_DEVF real StepToBoundary(real x, real y, real z, real ux, real uy, real uz);
RM_DEVF int SpanFlat(int i, real x, real y, real z, real ux, real uy, real uz, real* into);
RM_DEVF void SaveRay();
RM_DEVF void RestoreRay();
RM_DEVF bool RayCacheHit(real x, real y, real z, real ux, real uy, real uz, real& along);
RM_DEVF bool OnCachedRay(real x, real y, real z, real& along);
RM_DEVF void CollectCrossings(real x, real y, real z, real ux, real uy, real uz);
RM_DEVF int Winner(uint64_t active);
static RM_DEVF void SlabExit(real p, real u, real lo, real hi, real& best);
static RM_DEVF void CylinderExit(real x, real y, real ux, real uy, real radius, real& best);
RM_DEVF real WeightedMu(int region, real energyKev);
RM_DEVF real PathLimit(real x, real y, real z);
RM_DEVF bool PathCut(real wouldTravel, real limit);
RM_DEVF real CrystalPath(real x, real y, real z, real ux, real uy, real uz);

// --- источник и направление (EfficiencySimulator.cs:1630, 2982-3258, 7009-7046, 10512) ---
// Методы `Sampler` → `Source<Имя>` с переключателем по `D.scene->sourceKind`.
RM_DEVF bool SceneBounds(real& centerZ, real& radius);
RM_DEVF void SourceNext(real& x, real& y, real& z);
RM_DEVF real SourceNextWeighted(real& x, real& y, real& z);
RM_DEVF void SourceRetune(real energyKev);
RM_DEVF real SourceDirectionWeight(real x, real y, real z, real ux, real uy, real uz);
RM_DEVF bool SourcePreferCone();
RM_DEVF void Rotate(real& ux, real& uy, real& uz, real cos);
RM_DEVF void Isotropic(real& ux, real& uy, real& uz);
RM_DEVF void InCone(real ax, real ay, real az, real cosMax, real& ux, real& uy, real& uz);
