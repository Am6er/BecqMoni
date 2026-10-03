// decl_branch.inc — поля и объявления методов `struct Sim` модуля ВЕТВЕЙ ИСТОРИИ:
// взвешенная ветвь (`OneHistory`, вынужденное однократное рассеяние), аналоговая ветвь
// (`AnalogHistory`/`AnalogTransport` с очередью квантов), счёт (бины, каналы, свет,
// второй счёт пика). Входит В ТЕЛО `struct Sim` (sim.cuh); определения —
// sim_branch.cuh, ядра — kernels.cuh. Полоса П221 (`AMBER160`), часть Д.
//
// Накопители узла (гистограммы C#) — НЕ массивы нити, а указатели на накопители
// `NodeArgs` (tally.cuh), куда пишут атомики в `double`:
//   «histogram» взвешенной ветви — аргумент `double* histogram` (= a.hist) + `bins`;
//   «channelHistograms[c]»        — `channelHistograms + c * channelBins` (= a.chan);
//   «lightSum»                    — `lightSum` (= a.light), длина `lightSumLen`.
// Скалярные поля, которые C# копит через прогон (`resolutionWeighted`, `lightBinSplit`,
// `CountLightBinSplit` …), — поля нити; ядро сбрасывает их в слоты `a.scal` одним
// атомиком в конце своей работы (сумма та же, порядок сложения — другой).

// --- каналы отклика (= EfficiencySimulator.cs:7823 `enum ResponseChannel`) ---
// `enum class` — как `ResponseChannel.Peak` C#: имена не сталкиваются с методами `Sim`.
enum class ResponseChannel : int
{
    Peak = 0,
    Compton = 1,
    EscapeAnnihilation = 2,
    EscapeXrayK = 3,
    EscapeAnnihilationDouble = 4,
    EscapeXrayL = 5,
    AnnihilationOutside = 6
};

// = `public const int ResponseChannelCount = 7` (EfficiencySimulator.cs:7972). tally.cuh
// входит после `struct Sim`; равенство RM_CHANNELS проверяет static_assert в kernels.cuh.
static constexpr int ResponseChannelCount = 7;

// --- очередь квантов истории (EfficiencySimulator.cs:3747-3807) ---
// = `const int PendMax = 12`.
static constexpr int PendMax = 12;
real pendX[PendMax], pendY[PendMax], pendZ[PendMax];
real pendUx[PendMax], pendUy[PendMax], pendUz[PendMax];
real pendE[PendMax];
int pendCount;
bool pendFromOutsideAnnihilation[PendMax];
bool fromOutsideAnnihilation;

// Очередь обхода ПОЛНОЙ эффективности (`N4`, :3810-3815) — своя.
real totPendX[PendMax], totPendY[PendMax], totPendZ[PendMax];
real totPendUx[PendMax], totPendUy[PendMax], totPendUz[PendMax];
real totPendE[PendMax];
int totPendCount;

// = `public long CountPendingDropped, CountEscapeDropped` (:3778); растят их и полоса В
// (вылеты), и полоса Г (занос). Статические близнецы `TotalPendingDropped/
// TotalEscapeDropped` не переносятся: их роль — сумма по всем симуляторам процесса, её
// даёт слот `*_COUNT_*_DROPPED` в `a.scal`.
long long CountPendingDropped, CountEscapeDropped;

// Делегаты очередей `pushAnalog`/`pushTotal` (:3840-3841) — перечисление `PushQueue`
// полосы Г (decl_electron.inc), вызов делегата — её `PushByQueue(queue, …)`.

// --- угол и занос истории для моментов Q_k (`AMBER46`, :1552-1553) ---
real lastHistoryCos;
real historyDeposit;

// --- накопители узла (указатели на NodeArgs; ставит ядро) ---
double* channelHistograms;   // null — каналов нет (= `channelHistograms == null`)
int channelBins;             // длина гистограммы канала (= bins)
double* lightSum;            // null — шкала пропорциональна (= `lightSum == null`)
int lightSumLen;             // = `lightSum.Length`

// --- поля, которые цикл копит через прогон (сбрасываются в a.scal ядром) ---
double resolutionWeighted, resolutionAnalog;   // :1449
double lightBinSplit;                          // :8166
long long CountLightBinSplit;                  // :3978
double WeightLightBinSplit;                    // :3981
long long CountPeakBinDropped;                 // :3853
double WeightPeakBinDropped;                   // :3861
long long CountPeakBinDroppedScattered;        // :3953
long long CountPeakOutOfCone;                  // :3886
// не перенесено: CountAnalogScored (= слот A_SCORED), WeightPeakOutOfCone/2 (= A_OUTSIDE/2 —
// поле C# копит через прогоны экземпляра, на узле это та же сумма).

// Начальные значения полей модуля (инициализаторы C#): очереди пусты, накопители — ноль.
RM_DEVF void InitBranch();

// --- очереди ---
RM_DEVF void PushPending(real x, real y, real z, real ux, real uy, real uz, real energyKev);
RM_DEVF void PushTotalPending(real x, real y, real z, real ux, real uy, real uz, real energyKev);

// --- взвешенная ветвь ---
RM_DEVF real OneHistory(real energyKev, real x, real y, real z,
                        double* histogram, int histogramLength, real binKev, real pointWeight);

// (`AMBER161`, П227) Части OneHistory для ядер стадий (kernels.cuh): итог начала истории.
struct WeightedFront
{
    real ux, uy, uz;        // направление первичного
    real weight;            // вес истории (точка × конус × направление)
    real px, py, pz;        // точка входа в кристалл (если дошёл)
    real tau;               // оптическая толщина до кристалла
    real score;             // счёт при ScoreEntranceOnly, иначе ноль
    real tauScatter;        // τ вынужденного рассеяния
    bool toCrystal;         // нужен перенос первичного в кристалле
    bool scatter;           // нужен ScatteredRun
};
RM_DEVF void WeightedFrontRun(real energyKev, real x, real y, real z, real pointWeight, WeightedFront& f);
RM_DEVF real WeightedPrimaryCrystal(double* histogram, int histogramLength, real binKev,
                                    real energyKev, const WeightedFront& f);
RM_DEV void ScatteredReset();
RM_DEVF real ScatteredFinish(double* histogram, int histogramLength, real binKev,
                             real energyKev, real weight, real sw, real scattered, real sEscaped);
RM_DEVF bool ScatterToCrystal(real x, real y, real z, real ux, real uy, real uz,
                              real energyKev, real tauKill, real& weight, real& scatteredEnergy,
                              real& outX, real& outY, real& outZ,
                              real& outUx, real& outUy, real& outUz);
RM_DEVF bool ToCrystal(real& x, real& y, real& z, real ux, real uy, real uz, real energyKev, real& tau);
RM_DEVF real ScatteredRun(double* histogram, int histogramLength, real binKev,
                          real x, real y, real z, real ux, real uy, real uz,
                          real energyKev, real tauKill, real weight);
RM_DEVF bool ScatteredContribution(real x, real y, real z, real ux, real uy, real uz,
                                   real energyKev, real tauKill,
                                   real& weight, real& scatteredEnergy, real& escapedEnergy);
RM_DEVF real KillDepthToExit(real x, real y, real z, real ux, real uy, real uz, real energyKev);

// --- аналоговая ветвь ---
RM_DEVF real AnalogHistory(real energyKev, real x, real y, real z, real& weight,
                           bool outsideOnly, bool& inWeightedCone,
                           real& depositedOutside, bool& comptonOutside);
RM_DEVF real AnalogTransport(real x, real y, real z, real ux, real uy, real uz,
                             real energyKev, real limit,
                             real& depositedOutside, bool& comptonOutside);

// (`AMBER161`, П227) Состояние AnalogTransport между шагами (локальные C# :9750-9772).
struct AnalogWalk
{
    real x, y, z, ux, uy, uz;
    real e;
    real deposited, depositedOutside;
    real travelled, limit;
    int guard;               // счётчик цикла `for (guard …)`; 400 — выход из цикла
    bool comptonOutside;
};
RM_DEVF void AnalogFront(real x, real y, real z, real& weight, bool& inWeightedCone,
                         real& ux, real& uy, real& uz, real& limit);
RM_DEVF void AnalogWalkBegin(real x, real y, real z, real ux, real uy, real uz,
                             real energyKev, real limit, AnalogWalk& w);
RM_DEVF bool AnalogStep(AnalogWalk& w);
// Войдёт ли следующий шаг в кристалл (`InCrystal`). Розыгрышей и состояния не трогает
// (`At` растит только счётчик CountAt) — ядро с подкачкой копит такие шаги варпа в пачку.
RM_DEV bool AnalogStepEntersCrystal(const AnalogWalk& w)
{
    if (!(w.guard < 400 && w.e > (real)1.0))
    {
        return false;
    }

    int here = At(w.x, w.y, w.z);
    return here >= 0 && D.regions[here].IsCrystal;
}

// --- счёт: бины, каналы, свет ---
static RM_DEV int PeakBin(real energyKev, real binKev);
RM_DEV bool InPeak(real energyKev, real deposited);
RM_DEV int BinOf(int peak, real binKev, real energyKev, real deposited);
RM_DEV void Deposit(double* histogram, int histogramLength, real binKev, real energyKev,
                    real deposited, real weight);
RM_DEV void ScoreLight(real binKev, real energyKev, real deposited, real weight);
RM_DEV void TallyResolutionPeak(int peak, real binKev, real energyKev,
                                real deposited, real weight, bool analog);
RM_DEV ResponseChannel ChannelOf(real escaped);
RM_DEV ResponseChannel ChannelOf(real escaped, real deposited, real depositedOutside);
RM_DEV ResponseChannel PickChannel(real escaped);
RM_DEV ResponseChannel XrayChannel();
