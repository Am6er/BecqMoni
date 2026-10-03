// kernels.cuh — ядра узла матрицы: тело цикла взвешенной ветви (`Run`) и тело цикла
// аналоговой ветви (`AnalogContinuumRun`) для истории i. Полоса П221 (`AMBER160`), часть Д.
// Аргументы и слоты накопителей — tally.cuh. Включается ПОСЛЕ всех sim_*.cuh.
//
// Нить: `Sim s` в локальной памяти; начальные значения модулей — Init*() один раз;
// затем истории gid, gid + stride, … Каждая история начинает СО СВОЕГО состояния ГСЧ
// (mode 0 — `a.states[i]`, ступень 1; mode 1 — Philox со счётчиком `first + i`).
//
// ⚠ ЧТО В C# ПЕРЕЖИВАЕТ ИСТОРИЮ (поток ГСЧ узла один и идёт подряд через истории):
//   * ГСЧ — здесь заменён своим состоянием на историю;
//   * `source.Retune` — раз на узел ДО цикла, случайных чисел не тянет: здесь — раз на
//     нить до первой истории, то же значение;
//   * кэши μ областей на энергию, кэш луча, памятки (`relaxByZ`, `Scatterers`,
//     `LFractions`, `LogsNow`) — значение то же, что посчитанное заново; чисел не меняют;
//   * метки исхода (`lossAnnihilation` … `lightDeposit`) — сбрасываются самой историей
//     перед каждым `InCrystal` верхнего уровня (OneHistory :9202, ScatteredRun :4878,
//     AnalogTransport :9745); очередь `pendCount` и признак `fromOutsideAnnihilation` —
//     началом AnalogTransport (:9761, :9772); `historyDeposit`/`lastHistoryCos` —
//     началом OneHistory (:9175); буфер вылетов — перед каждым InCrystal аналоговой ветви
//     (:9780) и в CarriedElectronDeposit (:7611);
//   * счётчики `Count*`/`Weight*`, `resolution*`, `lightBinSplit` — копятся, на розыгрыш
//     не влияют: здесь — суммы нити, сброс в `a.scal` в конце.
// Единственное исключение из «сбрасывает сама история» — `lightDeposit` и метки у
// взвешенной истории, НЕ дошедшей до кристалла при `SingleScatter = false`: C# оставляет
// их от прошлой истории, но и не читает. На числа узла не влияет; в выходе истории
// (`HistoryOut.light`) такое поле несравнимо с CPU.
#pragma once
#include "sim.cuh"
#include "tally.cuh"

static_assert(Sim::ResponseChannelCount == RM_CHANNELS, "каналов отклика в Sim и в tally.cuh — разное число");

// = EfficiencySimulator.cs:10840 класс AngularMomentSums (суммы моментов Q_k узла).
// Суммы в double ВСЕГДА (накопители по историям); N — число историй нити.
struct AngularMomentSums
{
    double S0, S2, S4, S00, S22, S44, S02, S04;
    double T0, T2, T4, T00, T22, T44, T02, T04;
    long long N;

    RM_DEV void Clear()
    {
        S0 = S2 = S4 = S00 = S22 = S44 = S02 = S04 = 0.0;
        T0 = T2 = T4 = T00 = T22 = T44 = T02 = T04 = 0.0;
        N = 0;
    }

    // = EfficiencySimulator.cs:10871 AngularMomentSums.Add
    RM_DEV void Add(double score, double total, double cos)
    {
        double c2 = cos * cos;
        double p2 = 0.5 * (3.0 * c2 - 1.0);
        double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
        double s2 = score * p2, s4 = score * p4;
        S0 += score; S2 += s2; S4 += s4;
        S00 += score * score; S22 += s2 * s2; S44 += s4 * s4;
        S02 += score * s2; S04 += score * s4;
        double t2 = total * p2, t4 = total * p4;
        T0 += total; T2 += t2; T4 += t4;
        T00 += total * total; T22 += t2 * t2; T44 += t4 * t4;
        T02 += total * t2; T04 += total * t4;
        N++;
    }
};

// Состояние ГСЧ на начало истории i (см. NodeArgs, tally.cuh).
RM_DEV void SetHistoryRng(Sim& s, const NodeArgs& a, long long i)
{
    if (a.rngMode == 0)
    {
        s.rng.mode = 0;
        s.rng.s = a.states[i];
    }
    else
    {
        unsigned long long c = (unsigned long long)(a.first + i);
        s.rng.mode = 1;
        s.rng.key0 = a.key0;
        s.rng.key1 = a.key1;
        s.rng.c0 = (uint32_t)(c & 0xFFFFFFFFull);
        s.rng.c1 = (uint32_t)(c >> 32);
        s.rng.c2 = 0u;
        s.rng.left = 0;
    }
}

// Начальное состояние нити: модули в порядке README («Начальное состояние нити»).
// ⚠ Полоса Б назвала свою `GeomInit` (decl_geom.inc), а не `InitGeom`, как в README.
RM_DEV void InitThread(Sim& s)
{
    s.GeomInit();
    s.InitPhoton();
    s.InitElectron();
    s.InitBranch();
}

// Замерный сброс состояния, ПЕРЕЖИВАЮЩЕГО историю (`NodeArgs.resetMask`, хост — из
// `BQ_GPU_RESET`): бит 1 — кэши μ областей (как у нового симулятора), бит 2 — кэш луча.
// Нужен, чтобы найти, какое состояние делает историю зависимой от предыдущей
// (ступень 1: одна нить подряд сходится с CPU, раскрой — нет).
RM_DEV void ResetCarried(Sim& s, int mask)
{
    if (mask & 1)
    {
        for (int r = 0; r < RM_MAX_REG; r++)
        {
            s.regMuEnergy[r] = (real)-1.0;
            s.regHasTotal[r] = s.regHasNoCoherent[r] = s.regHasIncoherent[r] = s.regHasCoherent[r] = s.regHasPair[r] = false;
            s.regBracketed[r] = false;
        }
    }

    if (mask & 2)
    {
        s.rayCount = 0;
        s.rayValid = false;
    }

    if (mask & 4)
    {
        s.raySaveValid = false;
        s.raySaveCount = 0;
        s.raySaveDepth = 0;
    }
}

RM_DEV void AddSlot(double* scal, int slot, double v)
{
    if (v != 0.0)
    {
        atomicAdd(scal + slot, v);
    }
}

// = EfficiencySimulator.cs:8652-8664 — тело цикла взвешенной ветви `Run` для истории i:
//   pointWeight = source.NextWeighted; score = OneHistory(...); sum, sum2; angular.Add.
// Накопители: a.hist (гистограмма), a.chan (7 каналов), a.light (lightSum), a.scal
// (W_*). Свёртка, нормировка, перенос шкалы света и всё после цикла — C# на хосте.
__global__ void WeightedKernel(NodeArgs a)
{
    Sim s;
    InitThread(s);
    s.channelHistograms = a.chan;
    s.channelBins = a.bins;
    s.lightSum = a.light;
    s.lightSumLen = a.light != nullptr ? a.bins : 0;

    // = `this.source.Retune(this, energyKev)` (:8651) — раз до цикла.
    s.SourceRetune(a.energyKev);

    double sum = 0.0, sum2 = 0.0;
    AngularMomentSums angular;
    angular.Clear();

    long long gid = (long long)blockIdx.x * blockDim.x + threadIdx.x;
    long long stride = (long long)gridDim.x * blockDim.x;
    for (long long i = gid; i < a.n; i += stride)
    {
        SetHistoryRng(s, a, i);
        if (a.resetMask != 0) ResetCarried(s, a.resetMask);
        real x, y, z;
        RM_PHASE_BEGIN(0)
        real pointWeight = s.SourceNextWeighted(x, y, z);
        RM_PHASE_END(0)
        RM_PHASE_BEGIN(5)
        real score = s.OneHistory(a.energyKev, x, y, z, a.hist, a.bins, a.binKev, pointWeight);
        RM_PHASE_END(5)
        sum += (double)score;
        sum2 += (double)score * (double)score;
        angular.Add((double)score, (double)s.historyDeposit, (double)s.lastHistoryCos);

        if (a.perHistory != nullptr)
        {
            HistoryOut& h = a.perHistory[i];
            h.score = (double)score;
            h.weight = (double)pointWeight;
            h.depositA = (double)s.historyDeposit;
            h.light = (double)s.lightDeposit;
            h.cosv = (double)s.lastHistoryCos;
            h.channel = -1;
            h.bin = -1;
            h.flags = 0;
            h.rngAfter = s.rng.s;
        }
    }

    double* q = a.scal;
    AddSlot(q, W_SUM, sum);
    AddSlot(q, W_SUM2, sum2);
    AddSlot(q, W_S0, angular.S0);   AddSlot(q, W_S2, angular.S2);   AddSlot(q, W_S4, angular.S4);
    AddSlot(q, W_S00, angular.S00); AddSlot(q, W_S22, angular.S22); AddSlot(q, W_S44, angular.S44);
    AddSlot(q, W_S02, angular.S02); AddSlot(q, W_S04, angular.S04);
    AddSlot(q, W_T0, angular.T0);   AddSlot(q, W_T2, angular.T2);   AddSlot(q, W_T4, angular.T4);
    AddSlot(q, W_T00, angular.T00); AddSlot(q, W_T22, angular.T22); AddSlot(q, W_T44, angular.T44);
    AddSlot(q, W_T02, angular.T02); AddSlot(q, W_T04, angular.T04);
    AddSlot(q, W_RESOLUTION, s.resolutionWeighted);
    AddSlot(q, W_LIGHT_BIN_SPLIT, s.lightBinSplit);
    AddSlot(q, W_COUNT_LIGHT_BIN_SPLIT, (double)s.CountLightBinSplit);
    AddSlot(q, W_WEIGHT_LIGHT_BIN_SPLIT, s.WeightLightBinSplit);
    AddSlot(q, W_COUNT_PATH_LIMIT_CUT, (double)s.CountPathLimitCut);
    AddSlot(q, W_COUNT_CASCADE_OVERFLOW, (double)s.CountCascadeOverflow);
    AddSlot(q, W_COUNT_ESCAPE_DROPPED, (double)s.CountEscapeDropped);
}

// = EfficiencySimulator.cs:10181-10283 — тело цикла `AnalogContinuumRun` для истории i.
// Накопители — ЛОКАЛЬНЫЕ массивы C# этого прогона: `hist` → a.hist, `hist2` → a.hist2,
// `channels[c]` → a.chan + c·bins (null — каналов нет), `light` → a.light (null — нет);
// скаляры — a.scal (A_*). Перезапись бинов [0, пик) итоговой гистограммы (:10294-10309),
// шум после свёртки и добавка класса вне конуса в пик (:8715-8741) — C# на хосте.
__global__ void AnalogKernel(NodeArgs a)
{
    Sim s;
    InitThread(s);
    s.channelHistograms = nullptr;   // ветвь пишет в a.chan сама (локальные `channels` C#)
    s.channelBins = a.bins;
    s.lightSum = a.light;
    s.lightSumLen = a.light != nullptr ? a.bins : 0;

    // = `this.source.Retune(this, energyKev)` (:10180) — раз до цикла.
    s.SourceRetune(a.energyKev);

    double outsideWeight = 0.0, outsideWeight2 = 0.0, outsideLight = 0.0;
    long long scored = 0;
    int peak = a.bins - 1;
    real energyKev = a.energyKev;
    real binKev = a.binKev;

    long long gid = (long long)blockIdx.x * blockDim.x + threadIdx.x;
    long long stride = (long long)gridDim.x * blockDim.x;
    for (long long i = gid; i < a.n; i += stride)
    {
        SetHistoryRng(s, a, i);
        if (a.resetMask != 0) ResetCarried(s, a.resetMask);
        real x, y, z;
        real weight = s.SourceNextWeighted(x, y, z);
        bool inWeightedCone;
        real depositedOutside;
        bool comptonOutside;
        real deposited = s.AnalogHistory(energyKev, x, y, z, weight, false,
                                         inWeightedCone, depositedOutside, comptonOutside);

        HistoryOut* h = a.perHistory != nullptr ? a.perHistory + i : nullptr;
        if (h != nullptr)
        {
            h->score = (double)deposited;
            h->weight = (double)weight;
            h->depositA = (double)depositedOutside;
            h->light = (double)s.lightDeposit;
            h->cosv = 0.0;
            h->channel = -1;
            h->bin = -1;
            h->flags = (inWeightedCone ? 1 : 0) | (comptonOutside ? 2 : 0);
            h->rngAfter = s.rng.s;
        }

        if (!(deposited > (real)0.0))
        {
            continue;
        }

        // Бин — тем же правилом, что у взвешенной ветви (`AMBER50`).
        int bin = s.BinOf(peak, binKev, energyKev, deposited);
        if (h != nullptr)
        {
            h->bin = bin;
        }

        // (`AMBER145`) Второй счёт пика.
        s.TallyResolutionPeak(peak, binKev, energyKev, deposited, weight, true);
        if (bin == peak)
        {
            s.CountPeakBinDropped++;
            s.WeightPeakBinDropped += (double)weight;
            if (comptonOutside)
            {
                s.CountPeakBinDroppedScattered++;
            }

            // (`AMBER66`) Вне конуса взвешенной ветви пик считать некому — своя сумма.
            if (!inWeightedCone)
            {
                s.CountPeakOutOfCone++;
                // не перенесено: WeightPeakOutOfCone/WeightPeakOutOfCone2 — те же суммы,
                // что outsideWeight/outsideWeight2 (слоты A_OUTSIDE/A_OUTSIDE2).
                outsideWeight += (double)weight;
                outsideWeight2 += (double)weight * (double)weight;
                outsideLight += (double)(weight * s.lightDeposit);
            }

            continue;               // бин пика — за взвешенной оценкой
        }

        atomicAdd(a.hist + bin, (double)weight);
        atomicAdd(a.hist2 + bin, (double)weight * (double)weight);
        scored++;
        // не перенесено: CountAnalogScored (= scored, слот A_SCORED).
        if (a.light != nullptr)
        {
            atomicAdd(a.light + bin, (double)(weight * s.lightDeposit));
        }

        // Канал считается и без накопителя каналов — для выхода истории; ChannelOf
        // случайных чисел не тянет и состояния не меняет.
        if (a.chan != nullptr || h != nullptr)
        {
            Sim::ResponseChannel channel = s.ChannelOf(energyKev - deposited, deposited, depositedOutside);
            if (channel == Sim::ResponseChannel::Peak)
            {
                channel = Sim::ResponseChannel::Compton;
            }

            if (a.chan != nullptr)
            {
                atomicAdd(a.chan + (long long)(int)channel * a.bins + bin, (double)weight);
            }

            if (h != nullptr)
            {
                h->channel = (int)channel;
            }
        }
    }

    double* q = a.scal;
    AddSlot(q, A_OUTSIDE, outsideWeight);
    AddSlot(q, A_OUTSIDE2, outsideWeight2);
    AddSlot(q, A_OUTSIDE_LIGHT, outsideLight);
    AddSlot(q, A_RESOLUTION, s.resolutionAnalog);
    AddSlot(q, A_SCORED, (double)scored);
    AddSlot(q, A_COUNT_PEAK_BIN_DROPPED, (double)s.CountPeakBinDropped);
    AddSlot(q, A_WEIGHT_PEAK_BIN_DROPPED, s.WeightPeakBinDropped);
    AddSlot(q, A_COUNT_PEAK_BIN_DROPPED_SCATTERED, (double)s.CountPeakBinDroppedScattered);
    AddSlot(q, A_COUNT_PEAK_OUT_OF_CONE, (double)s.CountPeakOutOfCone);
    AddSlot(q, A_COUNT_PATH_LIMIT_CUT, (double)s.CountPathLimitCut);
    AddSlot(q, A_COUNT_CASCADE_OVERFLOW, (double)s.CountCascadeOverflow);
    AddSlot(q, A_COUNT_ESCAPE_DROPPED, (double)s.CountEscapeDropped);
    AddSlot(q, A_COUNT_PENDING_DROPPED, (double)s.CountPendingDropped);
}

// =====================================================================================
// (`AMBER161`, П227) Взвешенная ветвь СТАДИЯМИ — по уплотнённым спискам историй.
// =====================================================================================
//
// Одно ядро «нить — история» ведёт варп, пока не кончит самая длинная из его историй, а
// ветвления «дошёл / не дошёл до кристалла», «рассеянный дошёл / нет» раскалывают варп:
// перенос первичного в кристалле начинали 14.5 нитей из 32, рассеянного — 6.7. Здесь
// история разрезана на части (sim_branch.cuh), и каждая часть — своё ядро по списку
// историй, которым она нужна:
//   1 — источник, луч до кристалла, решение о рассеянии   (все истории порции);
//   2 — перенос первичного в кристалле и его счёт          (список P);
//   3 — путь рассеянного до кристалла                      (список S, после 2);
//   4 — перенос рассеянного в кристалле и его счёт         (список C, из 3);
//   5 — суммы узла (sum, sum2, моменты Q_k)                (все).
// Между стадиями история хранит своё состояние в глобальной памяти (`WBuf`):
//   * ГСЧ — номер блока Philox и остаток слов; буфер слов восстанавливается повтором
//     блока, поэтому поток розыгрышей истории ТОТ ЖЕ, что в одном ядре;
//   * кэш луча — начало и направление последнего сбора пересечений; стадия собирает
//     их заново (`CollectCrossings` детерминирован), и кэш тот же — он НЕ прозрачен
//     (`A315`): попадание в кэш и новый сбор дают разные числа в последнем разряде.
// Значит, стадии обязаны совпасть с одним ядром по каждой истории; суммы — до порядка
// атомарных сложений (сверка — повторитель, double-сборка).

struct WBuf
{
    int cap;                    // ёмкость порции
    unsigned int* c2;           // ГСЧ: номер следующего блока Philox
    int* left;                  // ГСЧ: слов осталось в буфере
    unsigned char* rayValid;    // кэш луча: был ли
    real* ray;                  // [6·cap]: x y z ux uy uz начала сбора
    real* src;                  // [3·cap]: точка источника
    real* f;                    // [9·cap]: ux uy uz weight px py pz tau tauScatter
    real* score;
    real* hd;                   // historyDeposit
    real* cosv;                 // lastHistoryCos
    real* sc;                   // [8·cap]: рассеянный: px py pz sx sy sz энергия вес
    int* listP;
    int* listS;
    int* listC;
    int* counts;                // [3]: длины P, S, C
};

RM_DEV void WarpAddSlot(double* scal, int slot, double v)
{
    for (int o = 16; o > 0; o >>= 1)
    {
        v += __shfl_down_sync(0xFFFFFFFFu, v, o);
    }

    if ((threadIdx.x & 31) == 0 && v != 0.0)
    {
        atomicAdd(scal + slot, v);
    }
}

// Добавить в список, одним атомиком на варп. Звать ВСЕМИ нитями варпа.
RM_DEV void WarpAppend(int* counter, int* list, bool want, int value)
{
    unsigned mask = __ballot_sync(0xFFFFFFFFu, want);
    if (mask == 0u)
    {
        return;
    }

    int lane = threadIdx.x & 31;
    int leader = __ffs(mask) - 1;
    int at = 0;
    if (lane == leader)
    {
        at = atomicAdd(counter, __popc(mask));
    }

    at = __shfl_sync(0xFFFFFFFFu, at, leader);
    if (want)
    {
        list[at + __popc(mask & ((1u << lane) - 1u))] = value;
    }
}

RM_DEV void StageInit(Sim& s, const NodeArgs& a)
{
    InitThread(s);
    s.channelHistograms = a.chan;
    s.channelBins = a.bins;
    s.lightSum = a.light;
    s.lightSumLen = a.light != nullptr ? a.bins : 0;
    s.SourceRetune(a.energyKev);
}

RM_DEV void StageSave(const Sim& s, const WBuf& w, int j)
{
    w.c2[j] = s.rng.c2;
    w.left[j] = s.rng.left;
    w.rayValid[j] = s.rayValid ? 1 : 0;
    if (s.rayValid)
    {
        w.ray[0 * w.cap + j] = s.rayX; w.ray[1 * w.cap + j] = s.rayY; w.ray[2 * w.cap + j] = s.rayZ;
        w.ray[3 * w.cap + j] = s.rayUx; w.ray[4 * w.cap + j] = s.rayUy; w.ray[5 * w.cap + j] = s.rayUz;
    }
}

RM_DEV void StageRestore(Sim& s, const NodeArgs& a, const WBuf& w, long long base, int j)
{
    SetHistoryRng(s, a, base + j);
    // Бит 16 `resetMask` — грубый положительный контроль: поток ГСЧ не восстановлен.
    if (a.resetMask & 16) return;
    s.rng.c2 = w.c2[j];
    s.rng.left = w.left[j];
    if (s.rng.left > 0)
    {
        Philox4x32_10(s.rng.buf, s.rng.c0, s.rng.c1, s.rng.c2 - 1u, 0u, s.rng.key0, s.rng.key1);
    }

    // Бит 8 esetMask — положительный контроль сверки стадий: без восстановления кэша
    // луча стадии ОБЯЗАНЫ разойтись с одним ядром (кэш не прозрачен, A315).
    if (w.rayValid[j] && !(a.resetMask & 8))
    {
        s.CollectCrossings(w.ray[0 * w.cap + j], w.ray[1 * w.cap + j], w.ray[2 * w.cap + j],
                           w.ray[3 * w.cap + j], w.ray[4 * w.cap + j], w.ray[5 * w.cap + j]);
    }
}

RM_DEV void StageLoadFront(const WBuf& w, int j, Sim::WeightedFront& f)
{
    f.ux = w.f[0 * w.cap + j]; f.uy = w.f[1 * w.cap + j]; f.uz = w.f[2 * w.cap + j];
    f.weight = w.f[3 * w.cap + j];
    f.px = w.f[4 * w.cap + j]; f.py = w.f[5 * w.cap + j]; f.pz = w.f[6 * w.cap + j];
    f.tau = w.f[7 * w.cap + j];
    f.tauScatter = w.f[8 * w.cap + j];
}

// Счётчики нити — в слоты одним атомиком на варп (неактивные нити — нулями).
RM_DEV void StageFlush(const Sim& s, double* q, bool active)
{
    WarpAddSlot(q, W_RESOLUTION, active ? s.resolutionWeighted : 0.0);
    WarpAddSlot(q, W_LIGHT_BIN_SPLIT, active ? s.lightBinSplit : 0.0);
    WarpAddSlot(q, W_COUNT_LIGHT_BIN_SPLIT, active ? (double)s.CountLightBinSplit : 0.0);
    WarpAddSlot(q, W_WEIGHT_LIGHT_BIN_SPLIT, active ? s.WeightLightBinSplit : 0.0);
    WarpAddSlot(q, W_COUNT_PATH_LIMIT_CUT, active ? (double)s.CountPathLimitCut : 0.0);
    WarpAddSlot(q, W_COUNT_CASCADE_OVERFLOW, active ? (double)s.CountCascadeOverflow : 0.0);
    WarpAddSlot(q, W_COUNT_ESCAPE_DROPPED, active ? (double)s.CountEscapeDropped : 0.0);
}

__global__ void WeightedStage1(NodeArgs a, WBuf w, long long base, int count)
{
    int j = blockIdx.x * blockDim.x + threadIdx.x;
    bool active = j < count;
    bool wantP = false, wantS = false;
    Sim s;
    if (active)
    {
        StageInit(s, a);
        SetHistoryRng(s, a, base + j);
        real x, y, z;
        real pointWeight = s.SourceNextWeighted(x, y, z);
        Sim::WeightedFront f;
        s.WeightedFrontRun(a.energyKev, x, y, z, pointWeight, f);
        w.src[0 * w.cap + j] = x; w.src[1 * w.cap + j] = y; w.src[2 * w.cap + j] = z;
        w.f[0 * w.cap + j] = f.ux; w.f[1 * w.cap + j] = f.uy; w.f[2 * w.cap + j] = f.uz;
        w.f[3 * w.cap + j] = f.weight;
        w.f[4 * w.cap + j] = f.px; w.f[5 * w.cap + j] = f.py; w.f[6 * w.cap + j] = f.pz;
        w.f[7 * w.cap + j] = f.tau;
        w.f[8 * w.cap + j] = f.tauScatter;
        w.score[j] = f.score;
        w.hd[j] = s.historyDeposit;
        w.cosv[j] = s.lastHistoryCos;
        StageSave(s, w, j);
        wantP = f.toCrystal;
        wantS = f.scatter;
    }

    WarpAppend(w.counts + 0, w.listP, wantP, j);
    WarpAppend(w.counts + 1, w.listS, wantS, j);
    StageFlush(s, a.scal, active);
}

__global__ void WeightedStage2(NodeArgs a, WBuf w, long long base)
{
    int k = blockIdx.x * blockDim.x + threadIdx.x;
    bool active = k < w.counts[0];
    Sim s;
    if (active)
    {
        int j = w.listP[k];
        StageInit(s, a);
        StageRestore(s, a, w, base, j);
        Sim::WeightedFront f;
        StageLoadFront(w, j, f);
        s.historyDeposit = w.hd[j];
        w.score[j] = s.WeightedPrimaryCrystal(a.hist, a.bins, a.binKev, a.energyKev, f);
        w.hd[j] = s.historyDeposit;
        StageSave(s, w, j);
    }

    StageFlush(s, a.scal, active);
}

__global__ void WeightedStage3(NodeArgs a, WBuf w, long long base)
{
    int k = blockIdx.x * blockDim.x + threadIdx.x;
    bool active = k < w.counts[1];
    bool wantC = false;
    int j = 0;
    Sim s;
    if (active)
    {
        j = w.listS[k];
        StageInit(s, a);
        StageRestore(s, a, w, base, j);
        Sim::WeightedFront f;
        StageLoadFront(w, j, f);
        s.ScatteredReset();
        real sw, scattered, px, py, pz, sx, sy, sz;
        wantC = s.ScatterToCrystal(w.src[0 * w.cap + j], w.src[1 * w.cap + j], w.src[2 * w.cap + j],
                                   f.ux, f.uy, f.uz, a.energyKev, f.tauScatter, sw, scattered,
                                   px, py, pz, sx, sy, sz);
        if (wantC)
        {
            w.sc[0 * w.cap + j] = px; w.sc[1 * w.cap + j] = py; w.sc[2 * w.cap + j] = pz;
            w.sc[3 * w.cap + j] = sx; w.sc[4 * w.cap + j] = sy; w.sc[5 * w.cap + j] = sz;
            w.sc[6 * w.cap + j] = scattered;
            w.sc[7 * w.cap + j] = sw;
        }

        StageSave(s, w, j);
    }

    WarpAppend(w.counts + 2, w.listC, wantC, j);
    StageFlush(s, a.scal, active);
}

__global__ void WeightedStage4(NodeArgs a, WBuf w, long long base)
{
    int k = blockIdx.x * blockDim.x + threadIdx.x;
    bool active = k < w.counts[2];
    Sim s;
    if (active)
    {
        int j = w.listC[k];
        StageInit(s, a);
        StageRestore(s, a, w, base, j);
        s.historyDeposit = w.hd[j];
        s.ScatteredReset();
        real scattered = w.sc[6 * w.cap + j];
        real sEscaped = s.InCrystal(w.sc[0 * w.cap + j], w.sc[1 * w.cap + j], w.sc[2 * w.cap + j],
                                    w.sc[3 * w.cap + j], w.sc[4 * w.cap + j], w.sc[5 * w.cap + j],
                                    scattered, 0);
        real add = s.ScatteredFinish(a.hist, a.bins, a.binKev, a.energyKev, w.f[3 * w.cap + j],
                                     w.sc[7 * w.cap + j], scattered, sEscaped);
        w.score[j] += add;
        w.hd[j] = s.historyDeposit;
    }

    StageFlush(s, a.scal, active);
}

__global__ void WeightedStage5(NodeArgs a, WBuf w, int count)
{
    int j = blockIdx.x * blockDim.x + threadIdx.x;
    double score = 0.0, total = 0.0, cosv = 0.0;
    bool active = j < count;
    if (active)
    {
        score = (double)w.score[j];
        total = (double)w.hd[j];
        cosv = (double)w.cosv[j];
    }

    AngularMomentSums m;
    m.Clear();
    if (active)
    {
        m.Add(score, total, cosv);
    }

    double* q = a.scal;
    WarpAddSlot(q, W_SUM, score);
    WarpAddSlot(q, W_SUM2, score * score);
    WarpAddSlot(q, W_S0, m.S0);   WarpAddSlot(q, W_S2, m.S2);   WarpAddSlot(q, W_S4, m.S4);
    WarpAddSlot(q, W_S00, m.S00); WarpAddSlot(q, W_S22, m.S22); WarpAddSlot(q, W_S44, m.S44);
    WarpAddSlot(q, W_S02, m.S02); WarpAddSlot(q, W_S04, m.S04);
    WarpAddSlot(q, W_T0, m.T0);   WarpAddSlot(q, W_T2, m.T2);   WarpAddSlot(q, W_T4, m.T4);
    WarpAddSlot(q, W_T00, m.T00); WarpAddSlot(q, W_T22, m.T22); WarpAddSlot(q, W_T44, m.T44);
    WarpAddSlot(q, W_T02, m.T02); WarpAddSlot(q, W_T04, m.T04);
}