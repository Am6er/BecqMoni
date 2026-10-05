// decl_photon.inc — поля и объявления методов `struct Sim` модуля ФОТОНА В КРИСТАЛЛЕ:
// `InCrystal` и всё, что он зовёт (каналы кристалла, флуоресценция K/L и каскад,
// разрядка EADL электронами, комптон с доплером, рэлей, пары и позитрон, потеря
// энергии электроном, свет), буфер вылетов и метки исхода истории. Входит В ТЕЛО
// `struct Sim` (sim.cuh); определения — sim_photon.cuh. Полоса П221 (`AMBER160`), часть В.

// --- общие типы и константы, нужные ОБЪЯВЛЕНИЯМ ниже -------------------------
// Этот файл входит в `Sim` РАНЬШЕ decl_electron.inc (полоса Г), а `ElectronLoss`
// принимает `ElectronBirth`. Поэтому перечисление и общие константы объявляются здесь
// ПОД ТЕМИ ЖЕ СТРАЖАМИ и ТЕМИ ЖЕ ОПРЕДЕЛЕНИЯМИ, что в decl_electron.inc: кто первый —
// тот и объявил, второй пропускает. Менять — в обоих файлах вместе (лучше — вынести в sim.cuh).
#ifndef RM_ELECTRON_BIRTH_DEFINED
#define RM_ELECTRON_BIRTH_DEFINED
// = ElectronTransport.cs:119 ElectronBirth
enum class ElectronBirth : int
{
    Isotropic = 0,   // оже-электроны каскада, неизвестный процесс
    Photo = 1,       // фотоэлектрон: Заутер—Гаврила относительно кванта
    Given = 2,       // направление задано вызывающим (комптон — из кинематики)
    Pair = 3,        // лептон пары: модифицированный Цай относительно кванта
};
#endif

#ifndef RM_SIM_PHYS_CONST_DEFINED
#define RM_SIM_PHYS_CONST_DEFINED
// = EfficiencySimulator.cs:55–57
static constexpr real ElectronMassKev = (real)510.99895;
static constexpr real ClassicalRadiusCm = (real)2.8179403262e-13;
static constexpr real Avogadro = (real)6.02214076e23;
#endif

// --- константы модуля (EfficiencySimulator.cs:5167, :8216, :8085) ---
static constexpr int EscapeMax = 8;                          // = EscapeMax (:5167)
static constexpr real AnnihilationEscapeShare = (real)0.5;   // = AnnihilationEscapeShare (:8216)
static constexpr int CascadeStackSize = 128;                 // = cascadeStack.Length (:8085)
// Предел элементов в таблице флуоресцентов и рассеивателей вещества — под рабочие
// массивы нити (`Fluorescers.Weight`, `Scatterers.Product*` C#). Составы сцен корпуса —
// ≤ 17 элементов (map_data.md §0); больше — `__trap()` (отказ, а не порча).
static constexpr int PhMaxElements = 32;

// --- буфер вылетов из кристалла (`A55`, EfficiencySimulator.cs:5174-5178) ---
bool escapeCollect;
int escapeCount, escapeLost;
real escX[EscapeMax], escY[EscapeMax], escZ[EscapeMax];
real escUx[EscapeMax], escUy[EscapeMax], escUz[EscapeMax];
real escE[EscapeMax];

// --- метки исхода текущей истории (EfficiencySimulator.cs:8034-8080, :8150, :8155, :3941) ---
// Обнуляет их перед историей полоса Д (как C# :4882, :9205, :9748).
real lossAnnihilation;
real lossXray;
real lossXrayK;
real lossXrayL;
bool lastXrayIsL;
int lastAbsorbZ;
int lastAbsorbShell;
int lastKLine;
bool lastCascadeRolled;
int lastVacancyLine;
bool lastVacancyRolled;
real pendingCascadeKev;
int annihilationEscapes;
// Свет истории (:8155). ⚠ float: несколько слагаемых на историю — годен (map_data §4 п.17);
// по историям копит полоса Д в double.
real lightDeposit;

// Стек вакансий каскада (:8085). ОДИН на нить, как в C# один на симулятор; вложенный
// `RelaxationElectrons` (через тормозное оже-электрона → `InCrystal` → фотопоглощение)
// работает ВЫШЕ живых записей внешнего — дно `cascadeUsed` (= C# `cascadeUsed`, `A314`, П232).
int cascadeStack[CascadeStackSize];
int cascadeUsed;

// --- счётчики-диагностика (EfficiencySimulator.cs:3890-3893, :3932, :8124) ---
// CountBremPhotons/SumBremKev (:457-458) объявлены в decl_electron.inc (полоса Г),
// CountEscapeDropped (:3778) — в decl_branch.inc (полоса Д); здесь их нет.
long long CountCrystalCompton, CountCrystalVacancy, CountVacancyXray;
long long CountKXray, CountLXray;
long long CountKLCascade, CountKLVacancy;
long long CountCascadeOverflow;
// не перенесено: traceZ, traceShell, traceXrayKev, traceXrayGone (:8131-8134) — только
// для трассировки `TraceChannels`, на розыгрыш не влияют;
// relaxByZ/relaxKnown (:8095-8096) — памятка, на устройстве прямо `D.relaxByZ[z]`.

// Начальное состояние полей модуля — как их оставляет конструктор C# (нули, false).
RM_DEV void InitPhoton();

// --- вылеты ---
RM_DEV void NoteEscape(real x, real y, real z, real ux, real uy, real uz, real energyKev);
RM_DEV void NoteXrayShell(real kev, bool isL);

// --- фотон в кристалле ---
RM_DEVF real InCrystal(real x, real y, real z, real ux, real uy, real uz, real energyKev, int depth);
RM_DEVF void CrystalChannels(real energyKev, real& photo, real& compton, real& pair);
static RM_DEVF real KleinNishinaTotal(real energyKev);
static RM_DEV real KleinNishinaCos(real a, real cos);
static RM_DEVF real KleinNishinaIntegral(real a);
RM_DEVF void PositronStop(real te, real& x, real& y, real& z);

// --- электрон в кристалле, разрядка атома ---
RM_DEVF real ElectronLoss(real x, real y, real z, real te, int depth);
RM_DEVF real ElectronLoss(real x, real y, real z, real te, int depth,
                          ElectronBirth birth, real rx, real ry, real rz);
RM_DEVF real PhotoElectronsSplit(real x, real y, real z, real e, int atomZ, int shell,
                                 real xray, int kLine, real casc, bool cascadeRolled, int depth,
                                 real ux, real uy, real uz);
RM_DEVF real VacancyElectronsSplit(real x, real y, real z, real total, int atomZ,
                                   real bindingKev, real xray, int kLine, bool rolled, int depth,
                                   real edx, real edy, real edz);
// `relax` — индекс в D.relax (C# `MaterialDatabase.Relaxation`).
RM_DEVF real RelaxationElectrons(real x, real y, real z, int relax,
                                 int shell, real budgetKev, real xray, int kLine, real casc,
                                 bool cascadeRolled, int depth, bool selfRolled = true);
// Индекс в D.relax, −1 = null.
RM_DEV int RelaxationOf(int z);
RM_DEV void AddLight(real deposited, real te);
RM_DEVF int Poisson(real mean);

// --- флуоресценция ---
// `region` — индекс в D.regions (C# `Region`); `f` — индекс в D.fluor; `shell` — в D.photoShell (−1 = null).
RM_DEVF real SampleFluorescence(int region, real energyKev);
RM_DEVF int PickShellWithoutXray(int f, const real* lFrac, real energyKev, real kFraction, real omega);
RM_DEVF void CascadeAfterK(int f, int shell, int kLine, real energyKev);
RM_DEVF int LSubshellAfterCk(int f, int li);
static RM_DEV real PickLine(real pick, const real* kev, int kevLen, const real* weight, int weightLen);
static RM_DEV real PickLineAt(real pick, const real* kev, int kevLen, const real* weight, int weightLen,
                              int& line);
// `material` — индекс в D.materials (C# `GeometryMaterial`, −1 = null).
RM_DEVF real VacancyXray(int material, int z, real bindingKev);

// --- рассеяние ---
RM_DEV real AnalogMu(int region, real energyKev);
// Возврат — индекс в D.atoms (C# `ScatteringData.Atom`, −1 = null); `region` −1 = null.
RM_DEVF int PickAtom(int material, real energyKev, PhotonProcess process, int region);
RM_DEVF real ComptonScatter(int region, real energyKev, real& cos);
RM_DEVF real ComptonScatter(int region, real energyKev, real& cos, real& vacancyKev, int& vacancyZ);
RM_DEVF real ComptonScatter(int material, real energyKev, real& cos, real& vacancyKev, int& vacancyZ,
                            int region);
RM_DEVF real DopplerEnergy(int atom, real energyKev, real cos, real free, real& vacancyKev);
static RM_DEV real Consistent(real eps, real var2, real q);
RM_DEVF real RayleighCosine(int region, real energyKev);
RM_DEVF real RayleighCosine(int material, real energyKev, int region);
RM_DEVF real ComptonCosine(real energyKev, int atom);
RM_DEVF real ComptonCosine(real energyKev);
