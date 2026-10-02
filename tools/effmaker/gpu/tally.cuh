// tally.cuh — аргументы ядер узла и накопители. Полоса П221 (`AMBER160`).
//
// Узел матрицы = ДВА цикла по n историй (EfficiencySimulator.Run, :8605):
//   взвешенная ветвь — `OneHistory` (:9144), тело цикла :8652–8664;
//   аналоговая ветвь — `AnalogContinuumRun` (:10150), тело цикла :10181–10283.
// Ядро делает ровно тело цикла для истории `i` и складывает её вклад атомиками
// в `double` (во float единичные вклады теряются после 2^24 историй — map_data §4 п.2).
// Всё, что после цикла (свёртка шума, перезапись бинов ниже пика, нормировка,
// перенос шкалы света), делает C#-код приложения на хосте — GPU отдаёт СЫРЫЕ суммы.
#pragma once
#include "common.cuh"

#define RM_CHANNELS 7   // = EfficiencySimulator.ResponseChannelCount

// Скаляры взвешенной ветви (накопители `Run` и поля симулятора, которые цикл копит).
enum WeightedSlot
{
    W_SUM = 0, W_SUM2,                        // sum, sum2 (:8661–8662)
    W_S0, W_S2, W_S4, W_S00, W_S22, W_S44, W_S02, W_S04,   // AngularMomentSums (:10871)
    W_T0, W_T2, W_T4, W_T00, W_T22, W_T44, W_T02, W_T04,
    W_RESOLUTION,                             // resolutionWeighted (:1472)
    W_LIGHT_BIN_SPLIT,                        // lightBinSplit (:10363)
    W_COUNT_LIGHT_BIN_SPLIT,                  // CountLightBinSplit (:10359)
    W_WEIGHT_LIGHT_BIN_SPLIT,                 // WeightLightBinSplit (:10360)
    W_COUNT_PATH_LIMIT_CUT,                   // CountPathLimitCut
    W_COUNT_CASCADE_OVERFLOW,                 // CountCascadeOverflow
    W_COUNT_ESCAPE_DROPPED,                   // CountEscapeDropped
    W_SLOTS
};

// Скаляры аналоговой ветви.
enum AnalogSlot
{
    A_OUTSIDE = 0, A_OUTSIDE2, A_OUTSIDE_LIGHT,   // outsideWeight, outsideWeight2, outsideLight (:10161–10163)
    A_RESOLUTION,                                 // resolutionAnalog (:1468)
    A_SCORED,                                     // scored (:10262)
    A_COUNT_PEAK_BIN_DROPPED,                     // CountPeakBinDropped (:10225)
    A_WEIGHT_PEAK_BIN_DROPPED,                    // WeightPeakBinDropped (:10230)
    A_COUNT_PEAK_BIN_DROPPED_SCATTERED,           // CountPeakBinDroppedScattered (:10233)
    A_COUNT_PEAK_OUT_OF_CONE,                     // CountPeakOutOfCone (:10247)
    A_COUNT_PATH_LIMIT_CUT,
    A_COUNT_CASCADE_OVERFLOW,
    A_COUNT_ESCAPE_DROPPED,
    A_COUNT_PENDING_DROPPED,
    A_SLOTS
};

// Выход ОДНОЙ истории — для ступени 1 приёмки (сверка с CPU по историям).
struct HistoryOut
{
    double score;          // взвешенная: возврат OneHistory; аналоговая: deposited (возврат AnalogHistory)
    double weight;         // аналоговая: вес после AnalogHistory (ref weight); взвешенная: pointWeight
    double depositA;       // взвешенная: historyDeposit; аналоговая: depositedOutside
    double light;          // lightDeposit после истории
    double cosv;           // взвешенная: lastHistoryCos; аналоговая: 0
    int channel;           // аналоговая: канал по ChannelOf (−1 если не зачтена); взвешенная: −1
    int bin;               // аналоговая: BinOf (−1 если deposited ≤ 0); взвешенная: −1
    int flags;             // аналоговая: бит0 inWeightedCone, бит1 comptonOutside
    unsigned long long rngAfter; // состояние xorshift64* после истории — сверка ЧИСЛА выдач
};

struct NodeArgs
{
    real energyKev;
    real binKev;
    int bins;               // PeakBin(E, bin) + 1 = длина гистограммы
    long long n;            // историй в цикле
    long long first;        // номер первой истории этого запуска (Philox-счётчик)

    // Генератор: mode 0 — xorshift64*, состояние истории i = states[i] (ступень 1);
    // mode 1 — Philox, ключ = key0/key1 (зерно узла), счётчик = first + i.
    int rngMode;
    const unsigned long long* states;
    unsigned int key0, key1;

    // Накопители (double, атомики). Длины: hist/light — bins; chan — 7·bins (канал c,
    // бин b → chan[c·bins + b]); scal — W_SLOTS / A_SLOTS.
    double* hist;
    double* hist2;          // только аналоговая
    double* chan;
    double* light;
    double* scal;

    HistoryOut* perHistory; // не null — писать выход каждой истории (ступень 1)
    int resetMask;          // замер: сброс переживающего историю состояния (kernels.cuh, ResetCarried)
};
