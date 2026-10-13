// tables_dev.cuh — `__device__` перенос методов классов ДАННЫХ горячего пути, построчно с
// C#-оригинала, с тем же именем и первым аргументом-структурой (`data_tables.cuh`).
// Полоса П221 (`AMBER160`), полоса А. Соглашения — README.md этого каталога.
//
// Состав:
//   PartialCrossSections — Channel, Open, PairChannel, MassCrossSection (все перегрузки),
//                          MassTotal, MassTotalWithoutCoherent, HasElement;
//   MaterialDatabase     — PairThresholdShape, Bracket, Interpolate (три перегрузки);
//   Fluorescence         — Omega, OmegaLAt, CkAt, LYield, HasL;
//   PhotoShellModel      — KFraction, LFractions, LFraction, ShellCrossSection, ShellCount,
//                          EvalFit, InterpShell, InterpTable;
//   LightYieldCurve      — Of;
//   Relaxation           — TransitionsOf, BindingKev, ShellByBinding, AbsorbingShell (2),
//                          VacancyAfterPhoton, HasTransitions, Step;
//   ScatteringData.Atom  — ScatteringFunction, FormFactor, FormFactorTop, ShellCount,
//                          ShellBindingKev, SampleMomentumTransferSq, SelectShell,
//                          SampleMomentumAu, CoherentNorm, IncoherentNorm (+ Lookup и обе
//                          точные квадратуры для энергий вне сетки нормировок);
//                          ScatteringData::Segment, PartialIntegral, LogLog;
//   ElectronData         — RangeOf, YieldOf, EnergyOfRange; ElectronData::LogLog (обе);
//   ThickTargetBrem      — Photons, Radiated, Anchor, SampleKev, StepPhotons,
//                          StepRadiatedPerGram, SampleStepKev, SampleFrom, Bracket, Interpolate.
//
// Закрытые статические помощники C# — в пространствах имён по имени класса
// (`ScatteringData::LogLog`, `ElectronData::LogLog`), чтобы одноимённые помощники разных
// классов не путались перегрузкой; открытые статические и экземплярные — свободные функции.
// Константы классов — там же: `ScatteringData::InverseCmPerKev`, `MaterialDatabase::…`.
//
// ⚠ Внутри методов `struct Sim` одноимённый член Sim (если он есть) СКРЫВАЕТ свободную
// функцию отсюда — тогда звать с `::` (`::Step(relax, …)`).
//
// ⚠ Литералы: в режиме float КАЖДЫЙ литерал с плавающей точкой обёрнут в `(real)` —
// иначе выражение молча считалось бы в double (медленный FP64 на sm_86) и разошлось бы
// с «чистым float». В режиме double `(real)x` — тот же x, арифметика C# сохранена.
#pragma once
#include "common.cuh"
#include "data.cuh"

// `D` (`__constant__ DevData`) объявлен в sim.cuh и определён в api.cu ДО включения этого
// файла. Повторное `extern` после определения ломает хостовую половину nvcc (C2086).

// Строка зубчатого массива из арены: `a.Name[i]` → TblRow(a.Name, a.NameOff, i),
// `a.Name[i].Length` → TblRowLen(a.NameOff, i).
RM_DEV const real* TblRow(int flat, int offOff, int i) { return RR(flat) + II(offOff)[i]; }
RM_DEV int TblRowLen(int offOff, int i) { return II(offOff)[i + 1] - II(offOff)[i]; }

// ===========================================================================
// MaterialDatabase — статика
// ===========================================================================

namespace MaterialDatabase
{
    // = MaterialDatabase.cs:107 MaterialDatabase.PairNuclearThresholdKev
    constexpr double PairNuclearThresholdKev = 1022.0;
    // = MaterialDatabase.cs:110 MaterialDatabase.PairElectronThresholdKev
    constexpr double PairElectronThresholdKev = 2044.0;
}

// = MaterialDatabase.cs:116 MaterialDatabase.PairThresholdShape
RM_DEV real PairThresholdShape(real energyKev, real thresholdKev)
{
    if (!(energyKev > thresholdKev))
    {
        return 0;
    }

    // ⚠ float: у самого порога 1 − E₀/E — разность почти равных; (E − E₀)/E было бы
    // устойчивее, но арифметика оставлена как в C# (ступень 1 сверяет в double).
    real t = (real)1.0 - thresholdKev / energyKev;
    return t * t * t;
}

// (`AMBER161`, П227) Первый i в [0, n) с `pick < base + cum[i]`; n — если такого нет.
// `monotone` — читатель упаковки проверил, что cum не убывает: тогда предикат монотонен
// (base + cum[i] во float от cum не убывает), и двоичный поиск даёт ТОТ ЖЕ индекс, что
// линейный проход C#; иначе — линейный проход. У I/Cs/Pb оже-переходов сотни (у Pb до
// 1804): линейный проход занимал 17 % инструкций стадии переноса в кристалле.
RM_DEV int FirstAbove(const real* cum, int n, real base, real pick, bool monotone)
{
    if (monotone)
    {
        int lo = 0, hi = n;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (pick < base + cum[mid]) hi = mid; else lo = mid + 1;
        }

        return lo;
    }

    for (int i = 0; i < n; i++)
    {
        if (pick < base + cum[i])
        {
            return i;
        }
    }

    return n;
}

// = MaterialDatabase.cs:2856 MaterialDatabase.Bracket
RM_DEVF bool Bracket(const real* grid, int n, real x, int& lo, int& hi)
{
    if (n == 0)
    {
        lo = hi = -1;
        return false;
    }

    if (x <= grid[0])
    {
        lo = hi = 0;
        return true;
    }

    if (x >= grid[n - 1])
    {
        lo = hi = n - 1;
        return true;
    }

    lo = 0;
    hi = n - 1;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) / 2;
        if (grid[mid] <= x)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    return true;
}

// = MaterialDatabase.cs:2920 MaterialDatabase.Interpolate (пара узлов готова)
// `logGrid`/`logValues` — nullptr: логарифм на месте, как `null` в C#.
RM_DEVF real Interpolate(const real* grid, const real* logGrid,
                         const real* values, const real* logValues,
                         int lo, int hi, real x, real logX)
{
    if (lo == hi)
    {
        return values[lo];
    }

    real x0 = grid[lo], x1 = grid[hi];
    real y0 = values[lo], y1 = values[hi];
    if (!(x1 > x0))
    {
        // край поглощения: две точки на одной энергии, берётся верхняя
        return y1;
    }

    if (!(y0 > 0) || !(y1 > 0))
    {
        real f = (x - x0) / (x1 - x0);
        return y0 + f * (y1 - y0);
    }

    real lx0 = logGrid != nullptr ? logGrid[lo] : M_Log(x0);
    real lx1 = logGrid != nullptr ? logGrid[hi] : M_Log(x1);
    real ly0 = logValues != nullptr ? logValues[lo] : M_Log(y0);
    real ly1 = logValues != nullptr ? logValues[hi] : M_Log(y1);
    real t = (logX - lx0) / (lx1 - lx0);
    return M_Exp(ly0 + t * (ly1 - ly0));
}

// = MaterialDatabase.cs:2895 MaterialDatabase.Interpolate (с логарифмами)
RM_DEVF real Interpolate(const real* grid, int n, const real* logGrid,
                         const real* values, const real* logValues, real x)
{
    int lo, hi;
    if (!Bracket(grid, n, x, lo, hi))
    {
        return 0;
    }

    if (lo == hi)
    {
        return values[lo];
    }

    return Interpolate(grid, logGrid, values, logValues, lo, hi, x, M_Log(x));
}

// = MaterialDatabase.cs:2830 MaterialDatabase.Interpolate (без логарифмов)
RM_DEVF real Interpolate(const real* grid, int n, const real* values, real x)
{
    return Interpolate(grid, n, nullptr, values, nullptr, x);
}

// ===========================================================================
// PartialCrossSections (статический класс → свободные функции)
// ===========================================================================

// = PartialCrossSections.cs:343 PartialCrossSections.Channel
RM_DEV real Channel(const ElementG& element, int i, PhotonProcess process)
{
    switch (process)
    {
        case PhotonProcess::Coherent: return TblRow(element.Channels, element.ChannelsOff, 0)[i];
        case PhotonProcess::Incoherent: return TblRow(element.Channels, element.ChannelsOff, 1)[i];
        case PhotonProcess::Photoelectric: return TblRow(element.Channels, element.ChannelsOff, 2)[i];
        default: return TblRow(element.Channels, element.ChannelsOff, 3)[i]
                      + TblRow(element.Channels, element.ChannelsOff, 4)[i];
    }
}

// = PartialCrossSections.cs:337 PartialCrossSections.Open
// `logShape == null` C# — здесь nullptr (Len 0 в упаковке).
RM_DEV bool Open(const real* logShape, int logShapeLen, int i)
{
    return logShape != nullptr && i >= 0 && i < logShapeLen
        && !M_IsNaN(logShape[i]) && !M_IsInfinity(logShape[i]);
}

// = PartialCrossSections.cs:37 PartialCrossSections.HasElement
// ⚠ Отвечает по УПАКОВАННЫМ элементам (Z веществ сцены), а не по всей поставке.
RM_DEV bool HasElement(int z)
{
    return z >= 0 && z < 128 && D.elementsByZ[z] >= 0;
}

// = PartialCrossSections.cs:81 PartialCrossSections.MassCrossSection (элемент, пара узлов)
RM_DEVF real MassCrossSection(const ElementG& element,
                              int lo, int hi, real energyKev,
                              real logEnergyKev, PhotonProcess process)
{
    real a = Channel(element, lo, process);
    if (lo == hi)
    {
        return a;
    }

    const real* grid = RR(element.EnergyKev);
    real x0 = grid[lo], x1 = grid[hi];
    real b = Channel(element, hi, process);
    if (!(x1 > x0))
    {
        // край поглощения: две точки на одной энергии, берётся верхняя
        // ⚠ float: края XCOM — пары узлов в 0.1 эВ (иод 33.1693/33.1694 кэВ); во float
        // они различимы (ulp 3.8e-6 кэВ), но разность логарифмов узлов (~3e-6) несёт
        // ~10 % ошибки — доля внутри полосы 1e-4 кэВ шумит. Вне полосы безвредно.
        return b;
    }

    const real* logGrid = RR(element.LogEnergyKev);
    real f = (logEnergyKev - logGrid[lo]) / (logGrid[hi] - logGrid[lo]);
    if (!(a > 0) || !(b > 0))
    {
        // канал открывается не с нуля шкалы: рождение пар ниже 1.022 МэВ
        // ⚠ float: у самого узла порога (E − 1022 < ulp log E) доля f обнуляется и
        // сечение ~1e-12 см²/г уходит в 0 — относительно 100 %, абсолютно ничтожно.
        return a + f * (b - a);
    }

    const real* logChannel = TblRow(element.LogChannels, element.LogChannelsOff, (int)process);
    return M_Exp(logChannel[lo] + f * (logChannel[hi] - logChannel[lo]));
}

// = PartialCrossSections.cs:49 PartialCrossSections.MassCrossSection (по Z)
// Словарь `MaterialDatabase.TryGet` → `D.elementsByZ`.
RM_DEVF real MassCrossSection(int z, real energyKev, PhotonProcess process)
{
    int index = z >= 0 && z < 128 ? D.elementsByZ[z] : -1;
    if (!(energyKev > 0) || index < 0)
    {
        return 0;
    }

    const ElementG& element = D.elements[index];
    int lo, hi;
    if (!Bracket(RR(element.EnergyKev), element.EnergyKevLen, energyKev, lo, hi))
    {
        return 0;
    }

    return MassCrossSection(element, lo, hi, energyKev, M_Log(energyKev), process);
}

// = PartialCrossSections.cs:296 PartialCrossSections.PairChannel
RM_DEVF real PairChannel(const ElementG& element, int lo, int hi,
                         real energyKev, real logEnergyKev,
                         const real* logShape, int logShapeLen, real thresholdKev)
{
    real shape = PairThresholdShape(energyKev, thresholdKev);
    if (!(shape > 0) || logShape == nullptr)
    {
        return 0;                   // ниже порога канала нет вовсе
    }

    const real* logGrid = RR(element.LogEnergyKev);
    int p, q;
    if (lo != hi && Open(logShape, logShapeLen, lo) && Open(logShape, logShapeLen, hi)
        && logGrid[hi] > logGrid[lo])
    {
        p = lo;
        q = hi;                     // обычный участок: оба узла открыты
    }
    else if (Open(logShape, logShapeLen, hi))
    {
        // пороговый участок: наклон у двух ближайших открытых узлов СВЕРХУ
        p = hi;
        q = hi + 1;
        if (!Open(logShape, logShapeLen, q) || !(logGrid[q] > logGrid[p]))
        {
            return M_Exp(logShape[p]) * shape;
        }
    }
    else
    {
        return 0;                   // канал на этом участке ещё закрыт
    }

    real f = (logEnergyKev - logGrid[p]) / (logGrid[q] - logGrid[p]);
    return M_Exp(logShape[p] + f * (logShape[q] - logShape[p])) * shape;
}

// = PartialCrossSections.cs:273 PartialCrossSections.MassCrossSection (с ключом порога пар)
RM_DEVF real MassCrossSection(const ElementG& element,
                              int lo, int hi, real energyKev,
                              real logEnergyKev, PhotonProcess process,
                              bool thresholdPair)
{
    if (!thresholdPair || process != PhotonProcess::PairProduction)
    {
        return MassCrossSection(element, lo, hi, energyKev, logEnergyKev, process);
    }

    return PairChannel(element, lo, hi, energyKev, logEnergyKev,
                       element.LogPairNuclearShapeLen > 0 ? RR(element.LogPairNuclearShape) : nullptr,
                       element.LogPairNuclearShapeLen,
                       (real)MaterialDatabase::PairNuclearThresholdKev)
         + PairChannel(element, lo, hi, energyKev, logEnergyKev,
                       element.LogPairElectronShapeLen > 0 ? RR(element.LogPairElectronShape) : nullptr,
                       element.LogPairElectronShapeLen,
                       (real)MaterialDatabase::PairElectronThresholdKev);
}

// = PartialCrossSections.cs:231 PartialCrossSections.MassTotalWithoutCoherent
RM_DEVF real MassTotalWithoutCoherent(const ElementG& element,
                                      int lo, int hi, real energyKev,
                                      real logEnergyKev)
{
    return MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::Incoherent)
         + MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::Photoelectric)
         + MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::PairProduction);
}

// = PartialCrossSections.cs:207 PartialCrossSections.MassTotalWithoutCoherent (с ключом)
RM_DEVF real MassTotalWithoutCoherent(const ElementG& element,
                                      int lo, int hi, real energyKev,
                                      real logEnergyKev, bool thresholdPair)
{
    if (!thresholdPair)
    {
        return MassTotalWithoutCoherent(element, lo, hi, energyKev, logEnergyKev);
    }

    return MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::Incoherent)
         + MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::Photoelectric)
         + MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::PairProduction, true);
}

// = PartialCrossSections.cs:155 PartialCrossSections.MassTotal
RM_DEVF real MassTotal(const ElementG& element,
                       int lo, int hi, real energyKev,
                       real logEnergyKev)
{
    return MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::Coherent)
         + MassTotalWithoutCoherent(element, lo, hi, energyKev, logEnergyKev);
}

// = PartialCrossSections.cs:189 PartialCrossSections.MassTotal (с ключом)
RM_DEVF real MassTotal(const ElementG& element,
                       int lo, int hi, real energyKev,
                       real logEnergyKev, bool thresholdPair)
{
    if (!thresholdPair)
    {
        return MassTotal(element, lo, hi, energyKev, logEnergyKev);
    }

    return MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                            PhotonProcess::Coherent)
         + MassTotalWithoutCoherent(element, lo, hi, energyKev, logEnergyKev, true);
}

// ===========================================================================
// MaterialDatabase.Fluorescence
// ===========================================================================

// = MaterialDatabase.cs:197 Fluorescence.Omega
RM_DEV real Omega(const FluorescenceG& f, bool measured)
{
    return measured && f.OmegaKMeasured > 0
        ? f.OmegaKMeasured
        : f.OmegaK;
}

// = MaterialDatabase.cs:329 Fluorescence.HasL (свойство; значение положил писатель)
RM_DEV bool HasL(const FluorescenceG& f)
{
    return f.hasL != 0;
}

// = MaterialDatabase.cs:277 Fluorescence.OmegaLAt
RM_DEVF real OmegaLAt(const FluorescenceG& f, int li, int level)
{
    if (level > 0 && f.OmegaLSupplyLen > 0 && li < f.OmegaLSupplyLen
        && RR(f.OmegaLSupply)[li] > 0)
    {
        return RR(f.OmegaLSupply)[li];
    }

    return f.OmegaLLen > 0 && li < f.OmegaLLen ? RR(f.OmegaL)[li] : (real)0.0;
}

// = MaterialDatabase.cs:293 Fluorescence.CkAt
RM_DEVF real CkAt(const FluorescenceG& f, int j, int level)
{
    int ck = level == 2 ? f.CkSupply : level == 1 ? f.CkEadl : 0;
    int ckLen = level == 2 ? f.CkSupplyLen : level == 1 ? f.CkEadlLen : 0;
    return ckLen > 0 && j < ckLen ? RR(ck)[j] : (real)0.0;
}

// = MaterialDatabase.cs:305 Fluorescence.LYield
RM_DEVF real LYield(const FluorescenceG& f, int li, int level)
{
    real w3 = OmegaLAt(f, 2, level);
    if (li == 2)
    {
        return w3;
    }

    real w2 = OmegaLAt(f, 1, level) + CkAt(f, 2, level) * w3;
    if (li == 1)
    {
        return w2;
    }

    return OmegaLAt(f, 0, level) + CkAt(f, 0, level) * w2 + CkAt(f, 1, level) * w3;
}

// ===========================================================================
// MaterialDatabase.PhotoShellModel
// ===========================================================================

// = MaterialDatabase.cs:624 PhotoShellModel.EvalFit
RM_DEVF real EvalFit(const real* a, int aLen, real energyKev)
{
    // ⚠ float: x = 1000/E до ~30 у края иода → x⁶ ~ 7e8 при коэффициентах разных
    // знаков; сумма — разность больших чисел, во float теряется 3…5 знаков, и отношение
    // k/total в KFraction шумит. Лечение — предтабуляция долей на хосте в double.
    real x = (real)1000.0 / energyKev;      // 1/E, МэВ⁻¹
    real sum = 0, p = x;
    for (int i = 0; i < aLen; i++)
    {
        sum += a[i] * p;
        p *= x;
    }

    return sum;
}

// = MaterialDatabase.cs:701 PhotoShellModel.InterpTable (запасной путь без логарифмов)
RM_DEVF real InterpTable(const real* grid, int n, const real* values, real x)
{
    if (n == 0 || x < grid[0])
    {
        return 0;
    }

    if (x >= grid[n - 1])
    {
        return values[n - 1];
    }

    int lo = 0, hi = n - 1;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) / 2;
        if (grid[mid] <= x)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    if (!(grid[hi] > grid[lo]))
    {
        return values[hi];
    }

    real f = (M_Log(x) - M_Log(grid[lo]))
             / (M_Log(grid[hi]) - M_Log(grid[lo]));
    if (!(values[lo] > 0) || !(values[hi] > 0))
    {
        return values[lo] + f * (values[hi] - values[lo]);
    }

    return M_Exp(M_Log(values[lo]) + f * (M_Log(values[hi]) - M_Log(values[lo])));
}

// = MaterialDatabase.cs:645 PhotoShellModel.InterpShell
RM_DEVF real InterpShell(const PhotoShellModelG& m, int s, real x, real logX)
{
    const real* grid = TblRow(m.tableE, m.tableEOff, s);
    const real* values = TblRow(m.tableCs, m.tableCsOff, s);
    int n = TblRowLen(m.tableEOff, s);
    // `logE == null || logCs == null || logE[s] == null || logCs[s] == null` — строка
    // null упакована длиной 0; у непустой сетки строка логарифмов непуста.
    if (m.logTableELen == 0 || m.logTableCsLen == 0
        || TblRowLen(m.logTableEOff, s) == 0 || TblRowLen(m.logTableCsOff, s) == 0)
    {
        return InterpTable(grid, n, values, x);
    }

    if (n == 0 || x < grid[0])
    {
        return 0;
    }

    if (x >= grid[n - 1])
    {
        return values[n - 1];
    }

    int lo = 0, hi = n - 1;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) / 2;
        if (grid[mid] <= x)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    if (!(grid[hi] > grid[lo]))
    {
        return values[hi];
    }

    const real* logGrid = TblRow(m.logTableE, m.logTableEOff, s);
    const real* logValues = TblRow(m.logTableCs, m.logTableCsOff, s);
    real f = (logX - logGrid[lo]) / (logGrid[hi] - logGrid[lo]);
    if (!(values[lo] > 0) || !(values[hi] > 0))
    {
        return values[lo] + f * (values[hi] - values[lo]);
    }

    return M_Exp(logValues[lo] + f * (logValues[hi] - logValues[lo]));
}

// = MaterialDatabase.cs:479 PhotoShellModel.ShellCount (свойство)
RM_DEV int ShellCount(const PhotoShellModelG& m)
{
    return m.tableELen;
}

// = MaterialDatabase.cs:467 PhotoShellModel.ShellCrossSection
RM_DEVF real ShellCrossSection(const PhotoShellModelG& m, int seq, real energyKev)
{
    if (m.tableELen == 0 || seq < 0 || seq >= m.tableELen
        || TblRowLen(m.tableEOff, seq) == 0)
    {
        return (real)NAN;
    }

    return InterpTable(TblRow(m.tableE, m.tableEOff, seq), TblRowLen(m.tableEOff, seq),
                       TblRow(m.tableCs, m.tableCsOff, seq), energyKev);
}

// = MaterialDatabase.cs:411 PhotoShellModel.KFraction
RM_DEVF real KFraction(const PhotoShellModelG& m, real energyKev)
{
    if (!(energyKev > m.kEdgeKev))
    {
        return 0;
    }

    if (energyKev >= m.lowFromKev)
    {
        bool high = energyKev >= m.highFromKev;
        real k = high ? EvalFit(RR(m.highK), m.highKLen, energyKev)
                      : EvalFit(RR(m.lowK), m.lowKLen, energyKev);
        real total = high ? EvalFit(RR(m.highTotal), m.highTotalLen, energyKev)
                          : EvalFit(RR(m.lowTotal), m.lowTotalLen, energyKev);
        if (!(total > 0) || !(k > 0))
        {
            return 0;
        }

        return k >= total ? (real)1.0 : k / total;
    }

    // зазор между K-краем и началом фитов: табличные векторы по оболочкам
    real num = 0, den = 0;
    real logEnergyKev = M_Log(energyKev);
    for (int s = 0; s < m.tableELen; s++)
    {
        real v = InterpShell(m, s, energyKev, logEnergyKev);
        den += v;
        if (s == 0)
        {
            num = v;
        }
    }

    return den > 0 ? M_Min((real)1.0, num / den) : (real)0.0;
}

// = MaterialDatabase.cs:505 PhotoShellModel.LFractions
// C# возвращает массив из трёх или null; здесь — `false` вместо null, доли в `result`.
// Памятка `lastFracs`/`MemoSlot`/`Memo` (MaterialDatabase.cs:525-531, 571, 602-621) НЕ
// перенесена: значение зависит только от энергии и считается заново (README).
RM_DEVF bool LFractions(const PhotoShellModelG& m, real energyKev, real result[3])
{
    if (m.tableELen == 0 || m.tableELen < 4)
    {
        return false;
    }

    real rest = (real)1.0 - KFraction(m, energyKev);
    result[0] = 0;
    result[1] = 0;
    result[2] = 0;
    if (rest > 0)
    {
        real den = 0;
        real logEnergyKev = M_Log(energyKev);
        for (int s = 1; s < m.tableELen; s++)
        {
            real v = InterpShell(m, s, energyKev, logEnergyKev);
            if (v > 0)
            {
                den += v;
                if (s <= 3)
                {
                    result[s - 1] = v;
                }
            }
        }

        if (den > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                result[i] = rest * result[i] / den;
            }
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                result[i] = 0;
            }
        }
    }

    return true;
}

// = MaterialDatabase.cs:484 PhotoShellModel.LFraction
RM_DEVF real LFraction(const PhotoShellModelG& m, real energyKev, int li)
{
    real f[3];
    bool has = LFractions(m, energyKev, f);
    return has && li >= 0 && li < 3 ? f[li] : (real)0.0;
}

// ===========================================================================
// MaterialDatabase.LightYieldCurve
// ===========================================================================

// = MaterialDatabase.cs:810 LightYieldCurve.Of
// Памятка `LogNodes` (MaterialDatabase.cs:792) — готовый массив `logNodes`.
RM_DEVF real Of(const LightYieldCurveG& c, real electronKev)
{
    const real* e = RR(c.energyKev);
    const real* yieldRel = RR(c.yieldRel);
    int n = c.energyKevLen;
    if (!(electronKev > e[0]))
    {
        return yieldRel[0];
    }

    if (electronKev >= e[n - 1])
    {
        return yieldRel[n - 1];
    }

    int lo = 0, hi = n - 1;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) / 2;
        if (e[mid] <= electronKev)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    const real* le = RR(c.logNodes);
    real f = (M_Log(electronKev) - le[lo])
             / (le[hi] - le[lo]);
    return yieldRel[lo] + f * (yieldRel[hi] - yieldRel[lo]);
}

// ===========================================================================
// MaterialDatabase.Relaxation
// ===========================================================================

// = MaterialDatabase.cs:913 Relaxation.TransitionsOf
// Запасной путь по словарю `transitions` не перенесён: массив по обозначению EADL
// строится при загрузке всегда (`IndexByShell`, MaterialDatabase.cs:1853).
RM_DEV const TransitionsG* TransitionsOf(const RelaxationG& r, int shell)
{
    if ((unsigned)shell < (unsigned)r.transitionsByShellLen)
    {
        int t = II(r.transitionsByShell)[shell];
        return t < 0 ? nullptr : &D.transitions[t];
    }

    return nullptr;
}

// = MaterialDatabase.cs:938 Relaxation.BindingKev
RM_DEV real BindingKev(const RelaxationG& r, int shell)
{
    return (unsigned)shell < (unsigned)r.bindingByShellLen ? RR(r.bindingByShell)[shell] : (real)0.0;
}

// = MaterialDatabase.cs:955 Relaxation.ShellByBinding
RM_DEVF int ShellByBinding(const RelaxationG& r, real bindingKev)
{
    for (int i = 0; i < r.shellsByBindingLen; i++)
    {
        real b = RR(r.bindingByOrder)[i];
        if (M_Abs(b - bindingKev) <= (real)0.02 * M_Max(b, bindingKev))
        {
            return II(r.shellsByBinding)[i];
        }
    }

    return 0;
}

// = MaterialDatabase.cs:986 Relaxation.AbsorbingShell (с нижней границей обозначения)
RM_DEVF int AbsorbingShell(const RelaxationG& r, real photonKev, int minShellId)
{
    for (int i = 0; i < r.shellsByBindingLen; i++)
    {
        if (II(r.shellsByBinding)[i] > minShellId && RR(r.bindingByOrder)[i] < photonKev)
        {
            return II(r.shellsByBinding)[i];
        }
    }

    return 0;
}

// = MaterialDatabase.cs:976 Relaxation.AbsorbingShell
RM_DEV int AbsorbingShell(const RelaxationG& r, real photonKev)
{
    return AbsorbingShell(r, photonKev, 0);
}

// = MaterialDatabase.cs:1009 Relaxation.VacancyAfterPhoton
RM_DEVF int VacancyAfterPhoton(const RelaxationG& r, int shell, real lineKev, real u)
{
    const TransitionsG* t = TransitionsOf(r, shell);
    if (t == nullptr || t->radKevLen == 0)
    {
        return 0;
    }

    const real* radKev = RR(t->radKev);
    const int* radFrom = II(t->radFrom);
    if (lineKev > 0)
    {
        int best = 0;
        real gap = CS_DOUBLE_MAX;
        for (int i = 0; i < t->radKevLen; i++)
        {
            real d = M_Abs(radKev[i] - lineKev);
            if (d < gap)
            {
                gap = d;
                best = i;
            }
        }

        return radFrom[best];
    }

    // ⚠ float: накопленные вероятности EADL сравниваются с u·Σ; у тяжёлых (Pb — до
    // 1804 оже-переходов) соседние накопления различаются меньше ulp float ~6e-8.
    const real* radCum = RR(t->radCum);
    real pick = u * t->radSum;
    int at = FirstAbove(radCum, t->radCumLen, (real)0.0, pick, t->cumMonotone != 0);
    if (at < t->radCumLen)
    {
        return radFrom[at];
    }

    return radFrom[t->radFromLen - 1];
}

// = MaterialDatabase.cs:1047 Relaxation.HasTransitions
RM_DEV bool HasTransitions(const RelaxationG& r, int shell)
{
    const TransitionsG* t = TransitionsOf(r, shell);
    return t != nullptr && (t->radSum + t->augSum) > 0;
}

// = MaterialDatabase.cs:1064 Relaxation.Step
RM_DEVF bool Step(const RelaxationG& r, int shell, real u, bool nonRadiativeOnly,
                  bool& radiative, real& kev, int& from, int& ejected)
{
    radiative = false;
    kev = 0;
    from = 0;
    ejected = 0;
    const TransitionsG* t = TransitionsOf(r, shell);
    if (t == nullptr)
    {
        return false;
    }

    real total = nonRadiativeOnly ? t->augSum : t->radSum + t->augSum;
    if (!(total > 0))
    {
        return false;
    }

    // ⚠ float: «Σ EADL < 1 на 1e-6 — дырка садится на месте» (MaterialDatabase.cs:1060)
    // во float тонет в округлении u·total; накопления radCum/augCum — см. VacancyAfterPhoton.
    real pick = u * total;
    if (!nonRadiativeOnly && t->radCumLen > 0 && pick < t->radSum)
    {
        const real* radCum = RR(t->radCum);
        int i = FirstAbove(radCum, t->radCumLen, (real)0.0, pick, t->cumMonotone != 0);
        if (i < t->radCumLen)
        {
            radiative = true;
            kev = RR(t->radKev)[i];
            from = II(t->radFrom)[i];
            return true;
        }
    }

    if (t->augCumLen == 0)
    {
        return false;
    }

    const real* augCum = RR(t->augCum);
    real base0 = nonRadiativeOnly ? (real)0.0 : t->radSum;
    {
        int i = FirstAbove(augCum, t->augCumLen, base0, pick, t->cumMonotone != 0);
        if (i < t->augCumLen)
        {
            kev = RR(t->augKev)[i];
            from = II(t->augFrom)[i];
            ejected = II(t->augEjected)[i];
            return true;
        }
    }

    // хвост округления: последний оже-переход
    int last = t->augCumLen - 1;
    kev = RR(t->augKev)[last];
    from = II(t->augFrom)[last];
    ejected = II(t->augEjected)[last];
    return true;
}

// ===========================================================================
// ScatteringData и ScatteringData.Atom
// ===========================================================================

namespace ScatteringData
{
    // = ScatteringData.cs:45 ScatteringData.InverseCmPerKev
    constexpr double InverseCmPerKev = 8.065543937e6;
    // = ScatteringData.cs:48 ScatteringData.FineStructure
    constexpr double FineStructure = 7.2973525693e-3;
    // = ScatteringData.cs:120 Atom.NormNodes, NormLoKev, NormHiKev
    constexpr int NormNodes = 600;
    constexpr double NormLoKev = 1.0, NormHiKev = 30000.0;

    // = ScatteringData.cs:485 ScatteringData.Segment
    RM_DEVF int Segment(const real* grid, int n, real x)
    {
        if (!(x > grid[0]))
        {
            return -1;
        }

        if (x >= grid[n - 1])
        {
            return n - 2;
        }

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (grid[mid] <= x)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    // = ScatteringData.cs:516 ScatteringData.PartialIntegral
    RM_DEVF real PartialIntegral(const real* t, const real* f2, int i, real x)
    {
        real dt = t[i + 1] - t[i];
        if (!(dt > 0))
        {
            return 0;
        }

        real delta = x - t[i];
        if (delta <= 0)
        {
            return 0;
        }

        if (delta > dt)
        {
            delta = dt;
        }

        real slope = (f2[i + 1] - f2[i]) / dt;
        return f2[i] * delta + (real)0.5 * slope * delta * delta;
    }

    // = ScatteringData.cs:544 ScatteringData.LogLog
    RM_DEVF real LogLog(const real* grid, int n, const real* values, real x)
    {
        int i = Segment(grid, n, x);
        if (i < 0)
        {
            return values[0];
        }

        real x0 = grid[i], x1 = grid[i + 1];
        real y0 = values[i], y1 = values[i + 1];
        if (x >= grid[n - 1])
        {
            return values[n - 1];
        }

        if (!(x0 > 0) || !(y0 > 0) || !(y1 > 0))
        {
            real fl = (x - x0) / (x1 - x0);
            return y0 + fl * (y1 - y0);
        }

        real f = (M_Log(x) - M_Log(x0)) / (M_Log(x1) - M_Log(x0));
        return M_Exp(M_Log(y0) + f * (M_Log(y1) - M_Log(y0)));
    }
}

// = ScatteringData.cs:305 Atom.ScatteringFunction
RM_DEVF real ScatteringFunction(const AtomG& a, real xPerCm)
{
    return ScatteringData::LogLog(RR(a.sfX), a.sfXLen, RR(a.sfV), xPerCm);
}

// = ScatteringData.cs:311 Atom.FormFactor
RM_DEVF real FormFactor(const AtomG& a, real xPerCm)
{
    const real* ffT = RR(a.ffT);
    const real* ffF2 = RR(a.ffF2);
    real t = xPerCm * xPerCm;
    int i = ScatteringData::Segment(ffT, a.ffTLen, t);
    if (i < 0)
    {
        return M_Sqrt(ffF2[0]);
    }

    real f2 = ffF2[i]
              + (ffF2[i + 1] - ffF2[i])
                * (t - ffT[i]) / (ffT[i + 1] - ffT[i]);
    return M_Sqrt(M_Max((real)0.0, f2));
}

// = ScatteringData.cs:115 Atom.FormFactorTop (свойство)
RM_DEV real FormFactorTop(const AtomG& a)
{
    return RR(a.ffT)[a.ffTLen - 1];
}

// = ScatteringData.cs:98 Atom.ShellCount (свойство)
RM_DEV int ShellCount(const AtomG& a)
{
    return a.shellCumLen;
}

// = ScatteringData.cs:440 Atom.ShellBindingKev
RM_DEV real ShellBindingKev(const AtomG& a, int shell)
{
    return RR(a.shellBindKev)[shell];
}

// = ScatteringData.cs:336 Atom.SampleMomentumTransferSq
RM_DEVF real SampleMomentumTransferSq(const AtomG& a, real u, real tMax)
{
    // ⚠ float: диапазон t = x² до 1e34 (ScatteringData.cs:649) во float ПОМЕЩАЕТСЯ (max
    // 3.4e38); ∫F²dt по замеру 02.10.2026 (сцена G1S_denta120, Z ≤ 53) — не выше 1.8e19,
    // а не «1e37…1e38» карты данных: переполнения нет, бесконечностей в арене 0. Опасна
    // точность: розыгрыш живёт при t ≤ (8.07e6·E)² ≈ 6e20, а таблица тянется до 1e34.
    const real* t = RR(a.ffT);
    const real* f2 = RR(a.ffF2);
    const real* cum = RR(a.ffCum);
    int n = a.ffTLen;
    if (!(tMax > 0))
    {
        return 0;
    }

    if (tMax >= t[n - 1])
    {
        tMax = t[n - 1];
    }

    int last = ScatteringData::Segment(t, n, tMax);
    if (last < 0)
    {
        return u * tMax;
    }

    real head = ScatteringData::PartialIntegral(t, f2, last, tMax);
    real total = cum[last] + head;
    if (!(total > 0))
    {
        return u * tMax;
    }

    real target = u * total;
    int i = 0, hi = last;
    while (hi - i > 0)
    {
        int mid = (i + hi + 1) / 2;
        if (cum[mid] <= target)
        {
            i = mid;
        }
        else
        {
            hi = mid - 1;
        }
    }

    // ⚠ float: rest = target − cum[i] — разность почти равных на длинных накоплениях.
    real rest = target - cum[i];
    real t0 = t[i];
    real t1 = t[i + 1];
    real fa = f2[i];
    real fb = f2[i + 1];
    real dt = t1 - t0;
    if (!(dt > 0))
    {
        return t0;
    }

    real limit = i == last ? tMax - t0 : dt;

    // ∫ от t0 до t: a·Δ + (b−a)/dt·Δ²/2 = rest
    real slope = (fb - fa) / dt;
    real delta;
#ifdef RM_REAL_FLOAT
    // ⚠ float: литерал 1e-300 во float — НОЛЬ, и проверка «наклон ничтожен» не
    // сработала бы никогда: при равных F² в узлах (наклон ровно 0) ветка ниже дала бы
    // 0/0 = NaN. Смысл C# («ноль или денормал») держит наименьшее нормальное float.
    const real tinySlope = FLT_MIN;
#else
    const real tinySlope = 1e-300;
#endif
    if (M_Abs(slope) < tinySlope)
    {
        delta = fa > 0 ? rest / fa : (real)0.0;
    }
    else
    {
        // ⚠ float: (√disc − a)/slope — вычитание почти равных при малом slope·rest;
        // устойчиво 2·rest/(a + √disc). Оставлено как в C#.
        real disc = fa * fa + (real)2.0 * slope * rest;
        delta = disc > 0 ? (M_Sqrt(disc) - fa) / slope : (real)0.0;
    }

    if (delta < 0) delta = 0;
    if (delta > limit) delta = limit;
    return t0 + delta;
}

// = ScatteringData.cs:425 Atom.SelectShell
RM_DEVF int SelectShell(const AtomG& a, real u)
{
    const real* c = RR(a.shellCum);
    if (a.shellCumMonotone)
    {
        // (`AMBER161`) Первый i с u ≤ c[i] — двоичным поиском (c не убывает).
        int lo = 0, hi = a.shellCumLen;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (u <= c[mid]) hi = mid; else lo = mid + 1;
        }

        return lo < a.shellCumLen ? lo : a.shellCumLen - 1;
    }

    for (int i = 0; i < a.shellCumLen; i++)
    {
        if (u <= c[i])
        {
            return i;
        }
    }

    return a.shellCumLen - 1;
}

// = ScatteringData.cs:450 Atom.SampleMomentumAu
RM_DEVF real SampleMomentumAu(const AtomG& a, int shell, real u)
{
    const real* cum = TblRow(a.profCum, a.profCumOff, shell);
    const real* p = RR(a.momentumGrid);
    int n = TblRowLen(a.profCumOff, shell);
    if (u >= cum[n - 1])
    {
        return p[n - 1];
    }

    int lo = 0, hi = n - 1;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) / 2;
        if (cum[mid] <= u)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    real c0 = cum[lo], c1 = cum[hi];
    real f = c1 > c0 ? (u - c0) / (c1 - c0) : (real)0.0;
    return p[lo] + f * (p[hi] - p[lo]);
}

// = ScatteringData.cs:216 Atom.CoherentNormExact
// Список отрезков `cuts` (List<double>) не строится: те же точки обходятся потоком —
// 0, узлы ffT в (0, top), top — в том же порядке, что их добавлял C#.
RM_DEVF real CoherentNormExact(const AtomG& a, real energyKev)
{
    const real G3X[3] = { (real)-0.7745966692414834, (real)0.0, (real)0.7745966692414834 };
    const real G3W[3] = { (real)0.5555555555555556, (real)0.8888888888888888, (real)0.5555555555555556 };
    real xMax = (real)ScatteringData::InverseCmPerKev * energyKev;
    real tMax = xMax * xMax;
    if (!(tMax > 0))
    {
        return 0;
    }

    real top = M_Min(tMax, FormFactorTop(a));
    const real* ffT = RR(a.ffT);
    real sum = 0;
    real prev = 0;                        // cuts[0] = 0
    for (int k = 0; k <= a.ffTLen; k++)
    {
        real cut;
        if (k < a.ffTLen)
        {
            real tk = ffT[k];
            if (!(tk > 0 && tk < top))
            {
                continue;
            }

            cut = tk;
        }
        else
        {
            cut = top;                    // cuts.Add(top)
        }

        real lo = prev, hi = cut;
        prev = cut;
        if (!(hi > lo))
        {
            continue;
        }

        real h = (real)0.5 * (hi - lo), m = (real)0.5 * (lo + hi);
        for (int j = 0; j < 3; j++)
        {
            real t = m + h * G3X[j];
            real f = FormFactor(a, M_Sqrt(t));
            real c = (real)1.0 - (real)2.0 * t / tMax;
            sum += G3W[j] * h * f * f * (real)0.5 * ((real)1.0 + c * c);
        }
    }

    return sum * (real)2.0 / tMax;
}

// = ScatteringData.cs:263 Atom.IncoherentNormExact
// Отрезки — потоком, как в CoherentNormExact: 1, 1 − 2(x/k)² по узлам sfX в (0, k), −1.
RM_DEVF real IncoherentNormExact(const AtomG& a, real energyKev)
{
    const real G8X[8] =
    {
        (real)-0.9602898564975363, (real)-0.7966664774136267, (real)-0.5255324099163290, (real)-0.1834346424956498,
        (real)0.1834346424956498, (real)0.5255324099163290, (real)0.7966664774136267, (real)0.9602898564975363,
    };
    const real G8W[8] =
    {
        (real)0.1012285362903763, (real)0.2223810344533745, (real)0.3137066458778873, (real)0.3626837833783620,
        (real)0.3626837833783620, (real)0.3137066458778873, (real)0.2223810344533745, (real)0.1012285362903763,
    };
    real k = (real)ScatteringData::InverseCmPerKev * energyKev;
    real an = energyKev / (real)510.99895;
    const real* sfX = RR(a.sfX);
    real sum = 0;
    real prev = (real)1.0;                // cuts[0] = 1
    for (int q = 0; q <= a.sfXLen; q++)
    {
        real cut;
        if (q < a.sfXLen)
        {
            real x = sfX[q];
            if (!(x > 0 && x < k))
            {
                continue;
            }

            cut = (real)1.0 - (real)2.0 * (x / k) * (x / k);
        }
        else
        {
            cut = (real)-1.0;             // cuts.Add(-1.0)
        }

        real hi = prev, lo = cut;
        prev = cut;
        if (!(hi > lo))
        {
            continue;
        }

        real h = (real)0.5 * (hi - lo), m = (real)0.5 * (hi + lo);
        for (int j = 0; j < 8; j++)
        {
            real c = m + h * G8X[j];
            real r = (real)1.0 / ((real)1.0 + an * ((real)1.0 - c));
            real kn = r * r * (r + (real)1.0 / r - ((real)1.0 - c * c));
            real x = k * M_Sqrt(M_Max((real)0.0, (real)0.5 * ((real)1.0 - c)));
            sum += G8W[j] * h * kn * ScatteringFunction(a, x) / a.Z;
        }
    }

    return sum;
}

// = ScatteringData.cs:179 Atom.Lookup
// Делегат `Func<double,double> exact` C# → признак `coherent` (какую квадратуру звать за
// краями сетки 1 кэВ…30 МэВ).
RM_DEVF real Lookup(const AtomG& a, const real* logs, real energyKev, bool coherent)
{
    if (!(energyKev > (real)ScatteringData::NormLoKev) || !(energyKev < (real)ScatteringData::NormHiKev))
    {
        return coherent ? CoherentNormExact(a, energyKev) : IncoherentNormExact(a, energyKev);
    }

    real f = (M_Log(energyKev) - M_Log((real)ScatteringData::NormLoKev))
             / (M_Log((real)ScatteringData::NormHiKev) - M_Log((real)ScatteringData::NormLoKev))
             * (ScatteringData::NormNodes - 1);
    int i = (int)f;
    if (i >= ScatteringData::NormNodes - 1)
    {
        i = ScatteringData::NormNodes - 2;
    }

    real w = f - i;
    return M_Exp(logs[i] + w * (logs[i + 1] - logs[i]));
}

// = ScatteringData.cs:129 Atom.CoherentNorm
// `EnsureNorms` (ленивая сборка под замком) вынуждена при упаковке; здесь — только чтение.
RM_DEVF real CoherentNorm(const AtomG& a, real energyKev)
{
    return Lookup(a, RR(a.cohNormLog), energyKev, true);
}

// = ScatteringData.cs:139 Atom.IncoherentNorm
RM_DEVF real IncoherentNorm(const AtomG& a, real energyKev)
{
    return Lookup(a, RR(a.incNormLog), energyKev, false);
}

// ===========================================================================
// ElectronData и ElectronData.Material
// ===========================================================================

namespace ElectronData
{
    // = ElectronData.cs:1006 ElectronData.LogLog (без логарифмов, эталон)
    RM_DEVF real LogLog(const real* x, const real* y, int n, real v)
    {
        if (!(v > 0))
        {
            return y[0];
        }

        int i;
        if (v <= x[0])
        {
            i = 0;
        }
        else if (v >= x[n - 1])
        {
            i = n - 2;
        }
        else
        {
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (x[mid] <= v)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            i = lo;
        }

        real lx0 = M_Log(x[i]), lx1 = M_Log(x[i + 1]);
        real ly0 = M_Log(y[i]), ly1 = M_Log(y[i + 1]);
        real t = (M_Log(v) - lx0) / (lx1 - lx0);
        return M_Exp(ly0 + t * (ly1 - ly0));
    }

    // = ElectronData.cs:955 ElectronData.LogLog (с готовыми логарифмами)
    // ⚠ За краями — ЭКСТРАПОЛЯЦИЯ крайним наклоном (i = 0 или n − 2), не зажим.
    RM_DEVF real LogLog(const real* x, const real* logX, const real* y, const real* logY, int n, real v)
    {
        if (logX == nullptr || logY == nullptr)
        {
            return LogLog(x, y, n, v);
        }

        if (!(v > 0))
        {
            return y[0];
        }

        int i;
        if (v <= x[0])
        {
            i = 0;
        }
        else if (v >= x[n - 1])
        {
            i = n - 2;
        }
        else
        {
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (x[mid] <= v)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            i = lo;
        }

        real lx0 = logX[i], lx1 = logX[i + 1];
        real ly0 = logY[i], ly1 = logY[i + 1];
        real t = (M_Log(v) - lx0) / (lx1 - lx0);
        return M_Exp(ly0 + t * (ly1 - ly0));
    }
}

// Памятка `Material.LogsNow()` (ElectronData.cs:136) — готовые `logs*`; null-лог → nullptr.
RM_DEV const real* ElectronLogOrNull(int off, int len) { return len > 0 ? RR(off) : nullptr; }

// = ElectronData.cs:910 ElectronData.RangeOf
RM_DEVF real RangeOf(const ElectronMaterialG& m, real energyKev)
{
    return ElectronData::LogLog(RR(m.Energy), ElectronLogOrNull(m.logsEnergy, m.logsEnergyLen),
                                RR(m.Range), ElectronLogOrNull(m.logsRange, m.logsRangeLen),
                                m.EnergyLen, energyKev * (real)1e-3);
}

// = ElectronData.cs:917 ElectronData.YieldOf
RM_DEVF real YieldOf(const ElectronMaterialG& m, real energyKev)
{
    return ElectronData::LogLog(RR(m.Energy), ElectronLogOrNull(m.logsEnergy, m.logsEnergyLen),
                                RR(m.Yield), ElectronLogOrNull(m.logsYield, m.logsYieldLen),
                                m.EnergyLen, energyKev * (real)1e-3);
}

// = ElectronData.cs:928 ElectronData.EnergyOfRange
RM_DEVF real EnergyOfRange(const ElectronMaterialG& m, real range)
{
    if (!(range > 0))
    {
        return 0;
    }

    return ElectronData::LogLog(RR(m.Range), ElectronLogOrNull(m.logsRange, m.logsRangeLen),
                                RR(m.Energy), ElectronLogOrNull(m.logsEnergy, m.logsEnergyLen),
                                m.RangeLen, range) * (real)1e3;
}

// ===========================================================================
// ThickTargetBrem
// ===========================================================================

// = BremsstrahlungData.cs:893 ThickTargetBrem.Bracket
RM_DEVF void Bracket(const ThickTargetBremG& b, real teKev, int& lo, real& f)
{
    const real* g = RR(b.node);
    int hi = b.nodeLen - 1;
    lo = 0;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) / 2;
        if (g[mid] <= teKev)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    const real* ln = RR(b.logNode);
    f = (M_Log(teKev) - ln[lo]) / (ln[lo + 1] - ln[lo]);
}

// = BremsstrahlungData.cs:915 ThickTargetBrem.Interpolate
RM_DEVF real Interpolate(const ThickTargetBremG& b, const real* values, real teKev)
{
    const real* g = RR(b.node);
    int n = b.nodeLen;
    if (teKev <= g[0])
    {
        return 0;
    }

    if (teKev >= g[n - 1])
    {
        return values[n - 1];
    }

    int lo;
    real f;
    Bracket(b, teKev, lo, f);
    return values[lo] + f * (values[lo + 1] - values[lo]);
}

// = BremsstrahlungData.cs:423 ThickTargetBrem.SampleFrom
// `double[][] table` → плоский `tableFlat` + начала строк `tableOff`.
RM_DEVF real SampleFrom(const ThickTargetBremG& b, int tableFlat, int tableOff,
                        const real* counts, real teKev, real u)
{
    const real* g = RR(b.node);
    int n = b.nodeLen;
    if (!(teKev > g[0]))
    {
        return g[0];
    }

    int lo, hi;
    real f;
    if (teKev >= g[n - 1])
    {
        lo = hi = n - 1;
        f = 0;
    }
    else
    {
        Bracket(b, teKev, lo, f);
        hi = lo + 1;
    }

    real a = ((real)1.0 - f) * counts[lo], bb = hi != lo ? f * counts[hi] : (real)0.0;
    real sum = a + bb;
    if (!(sum > 0))
    {
        return g[0];
    }

    a /= sum;
    bb /= sum;
    const real* cl = TblRow(tableFlat, tableOff, lo);
    const real* ch = TblRow(tableFlat, tableOff, hi);
    // верх носителя: node_hi, если верхний узел в смеси, иначе node_lo
    int top = bb > 0 ? hi : lo;
    if (top <= 0)
    {
        return g[0];
    }

    // C(i) = a·cl[i] + b·ch[i] убывает от 1 (на MinKev) до 0 (на node[top]) — ищем, где u
    int i0 = 0, i1 = top;
    while (i1 - i0 > 1)
    {
        int mid = (i0 + i1) / 2;
        if (a * cl[mid] + bb * ch[mid] >= u)
        {
            i0 = mid;
        }
        else
        {
            i1 = mid;
        }
    }

    real c0 = a * cl[i0] + bb * ch[i0], c1 = a * cl[i1] + bb * ch[i1];
    real t = c0 > c1 ? (c0 - u) / (c0 - c1) : (real)0.0;
    t = t < 0 ? (real)0.0 : (t > 1 ? (real)1.0 : t);
    const real* ln = RR(b.logNode);
    real e0 = ln[i0];
    // верхний бин смеси сжат к T: квант не энергичнее электрона
    real e1 = i1 == hi && hi != lo ? M_Log(teKev) : ln[i1];
    return M_Exp(e0 + t * (e1 - e0));
}

// = BremsstrahlungData.cs:328 ThickTargetBrem.Photons
RM_DEVF real Photons(const ThickTargetBremG& b, real teKev)
{
    return Interpolate(b, RR(b.photons), teKev);
}

// = BremsstrahlungData.cs:334 ThickTargetBrem.Radiated
RM_DEVF real Radiated(const ThickTargetBremG& b, real teKev)
{
    return Interpolate(b, RR(b.radiatedKev), teKev);
}

// = BremsstrahlungData.cs:345 ThickTargetBrem.Anchor
RM_DEVF real Anchor(const ThickTargetBremG& b, real teKev)
{
    real v = Interpolate(b, RR(b.anchorFactor), teKev);
    return v > 0 ? v : (real)1.0;
}

// = BremsstrahlungData.cs:357 ThickTargetBrem.SampleKev
RM_DEVF real SampleKev(const ThickTargetBremG& b, real teKev, real u)
{
    return SampleFrom(b, b.cumulative, b.cumulativeOff, RR(b.photons), teKev, u);
}

// = BremsstrahlungData.cs:374 ThickTargetBrem.StepPhotons
RM_DEVF real StepPhotons(const ThickTargetBremG& b, real teKev, real stepGCm2, real anchor)
{
    real perGram = Interpolate(b, RR(b.thinPhotons), teKev);
    return perGram > 0 && stepGCm2 > 0 ? perGram * stepGCm2 * anchor : (real)0.0;
}

// = BremsstrahlungData.cs:381 ThickTargetBrem.StepRadiatedPerGram
RM_DEVF real StepRadiatedPerGram(const ThickTargetBremG& b, real teKev)
{
    return Interpolate(b, RR(b.thinRadiated), teKev);
}

// = BremsstrahlungData.cs:392 ThickTargetBrem.SampleStepKev
RM_DEVF real SampleStepKev(const ThickTargetBremG& b, real teKev, real u)
{
    return SampleFrom(b, b.thinAbove, b.thinAboveOff, RR(b.thinPhotons), teKev, u);
}
