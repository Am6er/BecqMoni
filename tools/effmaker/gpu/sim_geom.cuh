// sim_geom.cuh — определения методов `Sim` модуля ГЕОМЕТРИИ (объявления — decl_geom.inc).
// Полоса П221 (`AMBER160`), часть Б. ПОСТРОЧНЫЙ перенос C#-оригинала, имена те же;
// у каждой функции — строка `// = <Файл>.cs:<строка> <Имя>`.
//
// Сечения по элементам берутся функциями полосы А (`tables_dev.cuh`) с именами
// C#-оригинала: `Bracket`, `MassTotal`, `MassTotalWithoutCoherent`, `MassCrossSection`.
//
// ⚠ float: все места, где double-допуски обхода (1e-9, 1e-7, 1e-12 см) вырождаются
// во float, помечены `// ⚠ float:` — сейчас перенесены как есть (map_geometry.md §5.4).
#pragma once
#include "sim.cuh"

// Номер младшего установленного бита по последовательности де Брёйна
// (EfficiencySimulator.cs:4594-4606). Таблица считается из самой константы, как
// `BuildLowBitIndex` C#, — на этапе компиляции.
#define RM_DE_BRUIJN64 0x07EDD5E59A4E28C2ULL
struct RmLowBitTable
{
    int t[64];
    constexpr RmLowBitTable() : t()
    {
        for (int i = 0; i < 64; i++)
        {
            t[(int)(((1ULL << i) * RM_DE_BRUIJN64) >> 58)] = i;
        }
    }
};
static __constant__ RmLowBitTable RmLowBitIndex = RmLowBitTable();

// = (инициализаторы полей C#: `Region.muEnergy = -1.0`, EfficiencySimulator.cs:2054;
// кэш луча пуст; счётчики — ноль)
__device__ void Sim::InitGeom()
{
    for (int r = 0; r < RM_MAX_REG; r++)
    {
        regMuEnergy[r] = (real)-1.0;
        regMuTotal[r] = regMuNoCoherent[r] = regMuIncoherent[r] = regMuCoherent[r] = regMuPair[r] = (real)0;
        regHasTotal[r] = regHasNoCoherent[r] = regHasIncoherent[r] = regHasCoherent[r] = regHasPair[r] = false;
        regLogEnergy[r] = (real)0;
        regBracketed[r] = false;
    }

    rayCur = 0;
    rayAllocated = false;
    rayCount = 0;
    rayX = rayY = rayZ = rayUx = rayUy = rayUz = (real)0;
    rayValid = false;
    raySaveCount = 0;
    raySaveDepth = 0;
    raySaveX = raySaveY = raySaveZ = raySaveUx = raySaveUy = raySaveUz = (real)0;
    raySaveValid = false;
    raySaveSwapped = false;
    CountAt = CountStep = CountMu = CountWalk = 0;
    CountPathLimitCut = 0;
}

// ===========================================================================
// Region (EfficiencySimulator.cs:2023-2600)
// ===========================================================================

// = EfficiencySimulator.cs:2114 Region.EnsureBracket
__device__ void Sim::RegEnsureBracket(int r, real energyKev)
{
    if (regBracketed[r])
    {
        return;
    }

    // this.Snapshot() — снимок неизменяем и уже лежит в RegionG.
    const RegionG& g = D.regions[r];
    regLogEnergy[r] = M_Log(energyKev);
    const int* els = II(g.SnapElement);
    for (int i = 0; i < g.SnapLen; i++)
    {
        int lo = -1, hi = -1;
        if (els[i] < 0 || !Bracket(RR(D.elements[els[i]].EnergyKev), D.elements[els[i]].EnergyKevLen,
                                    energyKev, lo, hi))
        {
            lo = -1;
        }

        bracketLo[g.SnapBase + i] = lo;
        bracketHi[g.SnapBase + i] = hi;
    }

    regBracketed[r] = true;
}

// = EfficiencySimulator.cs:2140 Region.Total
__device__ real Sim::RegTotal(int r, real energyKev)
{
    if (!(energyKev > (real)0))
    {
        return (real)0;
    }

    RegEnsureBracket(r, energyKev);
    const RegionG& g = D.regions[r];
    const int* els = II(g.SnapElement);
    const real* fr = RR(g.SnapFraction);
    real mass = (real)0;
    for (int i = 0; i < g.SnapLen; i++)
    {
        real value = (real)0;
        int lo = bracketLo[g.SnapBase + i];
        if (lo >= 0)
        {
            // (`AMBER74`, П132) СУММОЙ КАНАЛОВ, как у кристалла.
            value = MassTotal(D.elements[els[i]], lo, bracketHi[g.SnapBase + i],
                              energyKev, regLogEnergy[r], g.ThresholdPair);
        }

        mass += fr[i] * value;
    }

    return mass * g.SnapDensity;
}

// = EfficiencySimulator.cs:2168 Region.NoCoherent
__device__ real Sim::RegNoCoherent(int r, real energyKev)
{
    if (!(energyKev > (real)0))
    {
        return (real)0;
    }

    RegEnsureBracket(r, energyKev);
    const RegionG& g = D.regions[r];
    const int* els = II(g.SnapElement);
    const real* fr = RR(g.SnapFraction);
    real mass = (real)0;
    for (int i = 0; i < g.SnapLen; i++)
    {
        real value = (real)0;
        int lo = bracketLo[g.SnapBase + i];
        if (lo >= 0)
        {
            // (`AMBER74`, П132) СУММОЙ ТРЁХ КАНАЛОВ.
            value = MassTotalWithoutCoherent(D.elements[els[i]], lo, bracketHi[g.SnapBase + i],
                                             energyKev, regLogEnergy[r], g.ThresholdPair);
        }

        mass += fr[i] * M_Max((real)0, value);
    }

    return mass * g.SnapDensity;
}

// = EfficiencySimulator.cs:2197 Region.Channel
__device__ real Sim::RegChannel(int r, real energyKev, PhotonProcess process)
{
    if (!(energyKev > (real)0))
    {
        return (real)0;
    }

    RegEnsureBracket(r, energyKev);
    const RegionG& g = D.regions[r];
    const int* els = II(g.SnapElement);
    const real* fr = RR(g.SnapFraction);
    real mass = (real)0;
    for (int i = 0; i < g.SnapLen; i++)
    {
        int lo = bracketLo[g.SnapBase + i];
        if (lo >= 0)
        {
            mass += fr[i] * MassCrossSection(D.elements[els[i]], lo, bracketHi[g.SnapBase + i],
                                             energyKev, regLogEnergy[r], process);
        }
    }

    return mass * g.SnapDensity;
}

// = EfficiencySimulator.cs:2226 Region.PrepareElements
__device__ int Sim::RegPrepareElements(int r, real energyKev)
{
    RegRetune(r, energyKev);
    if (!(energyKev > (real)0))
    {
        // this.Snapshot() — снимок уже есть.
        return -1;              // сечений нет: как у `MassCrossSection` при E ≤ 0
    }

    RegEnsureBracket(r, energyKev);
    return D.regions[r].SnapLen;
}

// = EfficiencySimulator.cs:2240 Region.ElementZ
__device__ int Sim::RegElementZ(int r, int i)
{
    return II(D.regions[r].SnapZ)[i];
}

// = EfficiencySimulator.cs:2246 Region.ElementKnown
__device__ bool Sim::RegElementKnown(int r, int i)
{
    return II(D.regions[r].SnapElement)[i] >= 0;
}

// = EfficiencySimulator.cs:2251 Region.ElementFraction
__device__ real Sim::RegElementFraction(int r, int i)
{
    return RR(D.regions[r].SnapFraction)[i];
}

// = EfficiencySimulator.cs:2261 Region.ElementCrossSection
__device__ real Sim::RegElementCrossSection(int r, int i, real energyKev, PhotonProcess process, bool thresholdPair)
{
    const RegionG& g = D.regions[r];
    int lo = bracketLo[g.SnapBase + i];
    if (lo < 0)
    {
        return (real)0;
    }

    return MassCrossSection(D.elements[II(g.SnapElement)[i]], lo, bracketHi[g.SnapBase + i],
                            energyKev, regLogEnergy[r], process, thresholdPair);
}

// = EfficiencySimulator.cs:2274 Region.PairThreshold
__device__ real Sim::RegPairThreshold(int r, real energyKev)
{
    if (!(energyKev > (real)0))
    {
        return (real)0;
    }

    RegEnsureBracket(r, energyKev);
    const RegionG& g = D.regions[r];
    const int* els = II(g.SnapElement);
    const real* fr = RR(g.SnapFraction);
    real mass = (real)0;
    for (int i = 0; i < g.SnapLen; i++)
    {
        int lo = bracketLo[g.SnapBase + i];
        if (lo >= 0)
        {
            mass += fr[i] * MassCrossSection(D.elements[els[i]], lo, bracketHi[g.SnapBase + i],
                                             energyKev, regLogEnergy[r],
                                             PhotonProcess::PairProduction, true);
        }
    }

    return mass * g.SnapDensity;
}

// = EfficiencySimulator.cs:2314 Region.Mu
__device__ real Sim::RegMu(int r, real energyKev, bool withoutCoherent)
{
    RegRetune(r, energyKev);
    const RegionG& g = D.regions[r];
    if (withoutCoherent)
    {
        if (!regHasNoCoherent[r])
        {
            regMuNoCoherent[r] = RegNoCoherent(r, energyKev);
            if (g.PhotoScale != (real)1.0)
            {
                // (`S197`, П178) рычаг замера: фотоэффект ×PhotoScale.
                regMuNoCoherent[r] = M_Max((real)0, regMuNoCoherent[r]
                    - ((real)1.0 - g.PhotoScale) * RegChannel(r, energyKev, PhotonProcess::Photoelectric));
            }

            regHasNoCoherent[r] = true;
        }

        return regMuNoCoherent[r];
    }

    if (!regHasTotal[r])
    {
        regMuTotal[r] = RegTotal(r, energyKev);
        if (g.PhotoScale != (real)1.0)
        {
            // (`S197`, П178) рычаг замера: фотоэффект ×PhotoScale.
            regMuTotal[r] = M_Max((real)0, regMuTotal[r]
                - ((real)1.0 - g.PhotoScale) * RegChannel(r, energyKev, PhotonProcess::Photoelectric));
        }

        regHasTotal[r] = true;
    }

    return regMuTotal[r];
}

// = EfficiencySimulator.cs:2352 Region.Incoherent
__device__ real Sim::RegIncoherent(int r, real energyKev)
{
    RegRetune(r, energyKev);
    if (!regHasIncoherent[r])
    {
        regMuIncoherent[r] = RegChannel(r, energyKev, PhotonProcess::Incoherent);
        regHasIncoherent[r] = true;
    }

    return regMuIncoherent[r];
}

// = EfficiencySimulator.cs:2365 Region.Coherent
__device__ real Sim::RegCoherent(int r, real energyKev)
{
    RegRetune(r, energyKev);
    if (!regHasCoherent[r])
    {
        regMuCoherent[r] = RegChannel(r, energyKev, PhotonProcess::Coherent);
        regHasCoherent[r] = true;
    }

    return regMuCoherent[r];
}

// = EfficiencySimulator.cs:2378 Region.Pair
__device__ real Sim::RegPair(int r, real energyKev, bool thresholdPair)
{
    RegRetune(r, energyKev);
    if (!regHasPair[r])
    {
        regMuPair[r] = thresholdPair
            ? RegPairThreshold(r, energyKev)
            : RegChannel(r, energyKev, PhotonProcess::PairProduction);
        regHasPair[r] = true;
    }

    return regMuPair[r];
}

// = EfficiencySimulator.cs:2392 Region.Retune
__device__ void Sim::RegRetune(int r, real energyKev)
{
    if (energyKev == regMuEnergy[r])
    {
        return;
    }

    regMuEnergy[r] = energyKev;
    regHasTotal[r] = false;
    regHasNoCoherent[r] = false;
    regHasIncoherent[r] = false;
    regHasCoherent[r] = false;
    regHasPair[r] = false;
    regBracketed[r] = false;
}

// = EfficiencySimulator.cs:2408 Region.Contains
__device__ bool Sim::RegContains(int r, real x, real y, real z)
{
    return RegContains(r, x, y, z, M_Sqrt(x * x + y * y));
}

// = EfficiencySimulator.cs:2419 Region.Contains (радиус передан)
__device__ bool Sim::RegContains(int r, real x, real y, real z, real rr)
{
    const RegionG& g = D.regions[r];
    // ⚠ float: `ZMin - Eps` и т.п. во float равны ZMin — сдвиг пропадает.
    if (z < g.ZMin - Eps || z >= g.ZMax - Eps)
    {
        return false;
    }

    if (g.IsBox)
    {
        return M_Abs(x) < g.AX - Eps && M_Abs(y) < g.AY - Eps;
    }

    return rr >= g.RIn - Eps && rr < g.ROut - Eps;
}

// не перенесено: Region.SpanAlong (EfficiencySimulator.cs:2460) с `Slab`/`Disk` —
// эталон, не рабочий путь (не вызывается нигде); рабочий — `SpanFlat`.

// ===========================================================================
// Навигация (EfficiencySimulator.cs:3992-4786)
// ===========================================================================

// = EfficiencySimulator.cs:4027 At
__device__ int Sim::At(real x, real y, real z)
{
    CountAt++;
    real along;
    // ⚠ float: порог 1e-7 см — порядка ULP float на координатах ~1 см.
    if (OnCachedRay(x, y, z, along) && along > (real)1e-7)
    {
        return raySegBuf[rayCur][SegmentAt(along)];
    }

    real r = M_Sqrt(x * x + y * y);
    int count = D.nRegions;
    for (int i = 0; i < count; i++)
    {
        if (RegContains(i, x, y, z, r))
        {
            return i;
        }
    }

    return -1;
}

// = EfficiencySimulator.cs:4057 SegmentAt
__device__ int Sim::SegmentAt(real bound)
{
    const real* rayCross = rayCrossBuf[rayCur];
    int lo = 0, hi = rayCount;
    while (lo < hi)
    {
        int mid = (lo + hi) >> 1;
        if (rayCross[mid] > bound)
        {
            hi = mid;
        }
        else
        {
            lo = mid + 1;
        }
    }

    return lo;
}

// = EfficiencySimulator.cs:4105 StepToBoundary
__device__ real Sim::StepToBoundary(real x, real y, real z, real ux, real uy, real uz)
{
    CountStep++;

    real along;
    // не перенесено: `MeasureCollectCost` (EfficiencySimulator.cs:3990) — замерный
    // статический ключ, в счёте всегда false.
    if (!RayCacheHit(x, y, z, ux, uy, uz, along))
    {
        CollectCrossings(x, y, z, ux, uy, uz);
        along = (real)0;
    }

    // ⛔ Курсором «только вперёд» нельзя (по одному лучу обход идёт дважды) —
    // поиск двоичный, как в C#.
    // ⚠ float: `along + 1e-7` при along ≳ 1 см во float не сдвигается.
    int lo = SegmentAt(along + (real)1e-7);
    return lo < rayCount ? rayCrossBuf[rayCur][lo] - along : CS_DOUBLE_MAX;
}

// = EfficiencySimulator.cs:4136 SpanFlat
__device__ int Sim::SpanFlat(int i, real x, real y, real z, real ux, real uy, real uz, real* into)
{
    const RegionG& g = D.regions[i];
    // Slab по оси z
    real lo, hi;
    real zMinE = g.regZMinE, zMaxE = g.regZMaxE;
    if (uz < Eps && uz > -Eps)
    {
        if (!(z >= zMinE && z < zMaxE))
        {
            return 0;
        }

        lo = -Far;
        hi = Far;
    }
    else
    {
        real a0 = (zMinE - z) / uz, b0 = (zMaxE - z) / uz;
        if (a0 <= b0) { lo = a0; hi = b0; }
        else { lo = b0; hi = a0; }
    }

    real a, b;
    if (g.regBox)
    {
        real axE = g.regAXE;
        if (ux < Eps && ux > -Eps)
        {
            if (!(x >= -axE && x < axE))
            {
                return 0;
            }

            a = -Far;
            b = Far;
        }
        else
        {
            real a1 = (-axE - x) / ux, b1 = (axE - x) / ux;
            if (a1 <= b1) { a = a1; b = b1; }
            else { a = b1; b = a1; }
        }

        if (a > lo) lo = a;
        if (b < hi) hi = b;

        real ayE = g.regAYE;
        if (uy < Eps && uy > -Eps)
        {
            if (!(y >= -ayE && y < ayE))
            {
                return 0;
            }

            a = -Far;
            b = Far;
        }
        else
        {
            real a2 = (-ayE - y) / uy, b2 = (ayE - y) / uy;
            if (a2 <= b2) { a = a2; b = b2; }
            else { a = b2; b = a2; }
        }

        if (a > lo) lo = a;
        if (b < hi) hi = b;
        if (!(hi > lo))
        {
            return 0;
        }

        into[0] = lo;
        into[1] = hi;
        return 1;
    }

    // Disk по внешнему радиусу
    real rOutE = g.regROutE;
    if (!(rOutE > (real)0))
    {
        return 0;
    }

    real aq = ux * ux + uy * uy;
    real cq = x * x + y * y - rOutE * rOutE;
    if (aq < Eps)
    {
        if (!(cq < (real)0))
        {
            return 0;
        }

        a = -Far;
        b = Far;
    }
    else
    {
        real bq = (real)2.0 * (x * ux + y * uy);
        real disc = bq * bq - (real)4.0 * aq * cq;
        if (disc <= (real)0)
        {
            return 0;                   // мимо или по касательной
        }

        real sq = M_Sqrt(disc), inv = (real)0.5 / aq;
        a = (-bq - sq) * inv;
        b = (-bq + sq) * inv;
    }

    if (a > lo) lo = a;
    if (b < hi) hi = b;
    if (!(hi > lo))
    {
        return 0;
    }

    // Disk по внутреннему радиусу — дырка кольца
    real rInE = g.regRInE;
    if (rInE > (real)0)
    {
        real h0, h1;
        bool hit;
        real ch = x * x + y * y - rInE * rInE;
        if (aq < Eps)
        {
            h0 = -Far;
            h1 = Far;
            hit = ch < (real)0;
        }
        else
        {
            real bh = (real)2.0 * (x * ux + y * uy);
            real dh = bh * bh - (real)4.0 * aq * ch;
            if (dh <= (real)0)
            {
                h0 = (real)0;
                h1 = (real)0;
                hit = false;
            }
            else
            {
                real sh = M_Sqrt(dh), ih = (real)0.5 / aq;
                h0 = (-bh - sh) * ih;
                h1 = (-bh + sh) * ih;
                hit = true;
            }
        }

        if (hit && h1 > lo && h0 < hi)
        {
            if (h0 <= lo && h1 >= hi)
            {
                return 0;               // отрезок целиком в дырке
            }

            if (h0 <= lo)
            {
                into[0] = h1;
                into[1] = hi;
                return 1;
            }

            if (h1 >= hi)
            {
                into[0] = lo;
                into[1] = h0;
                return 1;
            }

            into[0] = lo;
            into[1] = h0;
            into[2] = h1;
            into[3] = hi;
            return 2;
        }
    }

    into[0] = lo;
    into[1] = hi;
    return 1;
}

// = EfficiencySimulator.cs:4349 SaveRay
// C#: текущая пара массивов прячется, сборщику подставляется запасная (обмен
// ссылок). Здесь — смена номера пары `rayCur`; содержимое не копируется.
__device__ void Sim::SaveRay()
{
    if (raySaveDepth++ > 0)
    {
        return;                 // вложенный перенос — снимок уже есть
    }

    raySaveValid = rayValid;
    raySaveSwapped = false;
    if (rayAllocated)           // C#: `this.rayCross != null`
    {
        rayCur ^= 1;            // rayCross/raySeg ↔ raySpareCross/raySpareSeg
        raySaveCount = rayCount;
        raySaveX = rayX; raySaveY = rayY; raySaveZ = rayZ;
        raySaveUx = rayUx; raySaveUy = rayUy; raySaveUz = rayUz;
        raySaveSwapped = true;
    }

    rayValid = false;
}

// = EfficiencySimulator.cs:4380 RestoreRay
__device__ void Sim::RestoreRay()
{
    if (--raySaveDepth > 0)
    {
        return;
    }

    if (raySaveSwapped)
    {
        rayCur ^= 1;            // вернуть спрятанную пару, текущая снова запасная
        rayCount = raySaveCount;
        rayX = raySaveX; rayY = raySaveY; rayZ = raySaveZ;
        rayUx = raySaveUx; rayUy = raySaveUy; rayUz = raySaveUz;
        raySaveSwapped = false;
    }

    rayValid = raySaveValid;
}

// = EfficiencySimulator.cs:4407 RayCacheHit
__device__ bool Sim::RayCacheHit(real x, real y, real z, real ux, real uy, real uz, real& along)
{
    along = (real)0;
    if (!rayValid || ux != rayUx || uy != rayUy || uz != rayUz)
    {
        return false;
    }

    return OnCachedRay(x, y, z, along);
}

// = EfficiencySimulator.cs:4424 OnCachedRay
__device__ bool Sim::OnCachedRay(real x, real y, real z, real& along)
{
    along = (real)0;
    if (!rayValid)
    {
        return false;
    }

    real dx = x - rayX, dy = y - rayY, dz = z - rayZ;
    along = dx * rayUx + dy * rayUy + dz * rayUz;
    // ⚠ float: −1e-9 см ниже ULP float при along ~ 0.01 см и больше.
    if (along < (real)-1e-9)
    {
        return false;      // назад по лучу обход не ходит
    }

    real ox = dx - along * rayUx, oy = dy - along * rayUy,
         oz = dz - along * rayUz;
    // ⚠ float: отклонение² < 1e-12 см² (10 нм) — ниже ошибки округления float
    // на координатах ~10 см (ULP ≈ 1e-6 см, квадрат ≈ 1e-12): точка на луче может
    // не признаться лежащей на нём.
    return ox * ox + oy * oy + oz * oz < (real)1e-12;
}

// = EfficiencySimulator.cs:4472 CollectCrossings
__device__ void Sim::CollectCrossings(real x, real y, real z, real ux, real uy, real uz)
{
    CountWalk++;
    int count = D.nRegions;
    // C# растит массивы под `4 * count + 4`; здесь они фиксированы (RM_MAX_EVENTS),
    // число областей проверено читателем упаковки.
    rayAllocated = true;
    real* rayCross = rayCrossBuf[rayCur];
    int* raySeg = raySegBuf[rayCur];

    int m = 0;
    uint64_t active = 0ULL;
    for (int i = 0; i < count; i++)
    {
        int parts = SpanFlat(i, x, y, z, ux, uy, uz, spanBuf);
        for (int p = 0; p < parts; p++)
        {
            real t0 = spanBuf[2 * p], t1 = spanBuf[2 * p + 1];
            // ⚠ float: «позади точки сбора» по порогу 1e-7 см — порядок ULP float.
            if (t1 <= (real)1e-7)
            {
                continue;                       // отрезок позади точки сбора
            }

            // ⚠ float: тот же порог 1e-7 см.
            if (t0 <= (real)1e-7)
            {
                active |= 1ULL << i;            // область накрывает саму точку
            }
            else
            {
                eventT[m] = t0;
                eventCode[m] = i + 1;
                m++;
            }

            if (t1 < Far)
            {
                eventT[m] = t1;
                eventCode[m] = -(i + 1);
                m++;
            }
        }
    }

    // Сортировка вставками (`T43`).
    for (int i = 1; i < m; i++)
    {
        real t = eventT[i];
        int code = eventCode[i];
        int j = i - 1;
        while (j >= 0 && eventT[j] > t)
        {
            eventT[j + 1] = eventT[j];
            eventCode[j + 1] = eventCode[j];
            j--;
        }

        eventT[j + 1] = t;
        eventCode[j + 1] = code;
    }

    int n = 0;
    int current = Winner(active);
    for (int k = 0; k < m; k++)
    {
        int code = eventCode[k];
        if (code > 0)
        {
            active |= 1ULL << (code - 1);
        }
        else
        {
            active &= ~(1ULL << (-code - 1));
        }

        // События в одной точке применяются ВСЕ, и только потом решается,
        // сменилась ли область.
        if (k + 1 < m && eventT[k + 1] == eventT[k])
        {
            continue;
        }

        int next = Winner(active);
        if (next != current)            // C#: !ReferenceEquals(next, current)
        {
            rayCross[n] = eventT[k];
            raySeg[n] = current;
            n++;
            current = next;
        }
    }

    raySeg[n] = current;
    rayCount = n;
    rayX = x;
    rayY = y;
    rayZ = z;
    rayUx = ux;
    rayUy = uy;
    rayUz = uz;
    rayValid = true;
}

// = EfficiencySimulator.cs:4617 Winner (де Брёйн, таблица :4594-4606)
__device__ int Sim::Winner(uint64_t active)
{
    if (active == 0ULL)
    {
        return -1;
    }

    uint64_t low = active & (0ULL - active);
    return RmLowBitIndex.t[(int)((low * RM_DE_BRUIJN64) >> 58)];
}

// = EfficiencySimulator.cs:4645 SlabExit
__device__ void Sim::SlabExit(real p, real u, real lo, real hi, real& best)
{
    real t;
    if (u > Eps)
    {
        t = (hi - p) / u;
    }
    else if (u < -Eps)
    {
        t = (lo - p) / u;
    }
    else
    {
        return;             // луч параллелен слою — выхода по этой оси нет
    }

    if (t < (real)0)
    {
        t = (real)0;        // точка уже за гранью: пути внутри нет
    }

    if (t < best)
    {
        best = t;
    }
}

// = EfficiencySimulator.cs:4679 CylinderExit
__device__ void Sim::CylinderExit(real x, real y, real ux, real uy, real radius, real& best)
{
    if (!(radius > (real)0))
    {
        return;
    }

    real a = ux * ux + uy * uy;
    if (a < Eps)
    {
        return;             // луч вдоль оси — боковой поверхности не встретит
    }

    real b = (real)2.0 * (x * ux + y * uy);
    real c = x * x + y * y - radius * radius;
    real disc = b * b - (real)4.0 * a * c;
    if (disc < (real)0)
    {
        return;             // мимо цилиндра
    }

    real t = (-b + M_Sqrt(disc)) / ((real)2.0 * a);
    if (t < (real)0)
    {
        t = (real)0;
    }

    if (t < best)
    {
        best = t;
    }
}

// = EfficiencySimulator.cs:4722 WeightedMu
__device__ real Sim::WeightedMu(int region, real energyKev)
{
    return RegMu(region, energyKev, C.CoherentPassesThrough && !C.RayleighToCrystal);
}

// = EfficiencySimulator.cs:4749 PathLimit
__device__ real Sim::PathLimit(real x, real y, real z)
{
    const SceneG& s = *D.scene;
    real limit = (real)40.0 * s.sphereR + (real)200.0;
    if (s.pathSceneR > (real)0)
    {
        real dz = z - s.pathSceneZ;
        real d2 = x * x + y * y + dz * dz;
        if (d2 > s.pathSceneR * s.pathSceneR)
        {
            limit += M_Sqrt(d2) - s.pathSceneR;
        }
    }

    return limit;
}

// = EfficiencySimulator.cs:4770 PathCut
__device__ bool Sim::PathCut(real wouldTravel, real limit)
{
    if (wouldTravel > limit)
    {
        CountPathLimitCut++;
        return true;
    }

    return false;
}

// = EfficiencySimulator.cs:5130 CrystalPath
__device__ real Sim::CrystalPath(real x, real y, real z, real ux, real uy, real uz)
{
    const RegionG& c = D.regions[D.scene->crystal];
    real best = CS_DOUBLE_MAX;
    SlabExit(z, uz, c.ZMin, c.ZMax, best);
    if (c.IsBox)
    {
        SlabExit(x, ux, -c.AX, c.AX, best);
        SlabExit(y, uy, -c.AY, c.AY, best);
    }
    else
    {
        CylinderExit(x, y, ux, uy, c.ROut, best);
    }

    return best >= CS_DOUBLE_MAX ? (real)0 : best;
}

// ===========================================================================
// Источник и направление
// ===========================================================================

// = EfficiencySimulator.cs:1630 SceneBounds
__device__ bool Sim::SceneBounds(real& centerZ, real& radius)
{
    const SceneG& s = *D.scene;
    centerZ = (real)0;
    radius = (real)0;
    if (!(s.sceneRMax > (real)0) || s.sceneZMin > s.sceneZMax)
    {
        return false;
    }

    centerZ = (real)0.5 * (s.sceneZMin + s.sceneZMax);
    real half = (real)0.5 * (s.sceneZMax - s.sceneZMin);
    radius = M_Sqrt(s.sceneRMax * s.sceneRMax + half * half) + (real)1e-3;
    return true;
}

// = EfficiencySimulator.cs:3138 PointSampler.Next, :3169 BoxSampler.Next,
//   :3195 CylinderSampler.Next, :3230 MarinelliSampler.Next
// Случайные числа берутся по одному на оператор — порядок тот же, что у C#
// (в C++ порядок вычисления двух вызовов в одном выражении не определён).
__device__ void Sim::SourceNext(real& x, real& y, real& z)
{
    const SceneG& s = *D.scene;
    switch (s.sourceKind)
    {
        case RM_SOURCE_POINT:
        {
            x = (real)0;
            y = (real)0;
            z = s.PointZ;
            return;
        }

        case RM_SOURCE_BOX:
        {
            x = s.BoxAX * ((real)2.0 * Uniform() - (real)1.0);
            y = s.BoxAY * ((real)2.0 * Uniform() - (real)1.0);
            z = s.BoxZ0 + (s.BoxZ1 - s.BoxZ0) * Uniform();
            return;
        }

        case RM_SOURCE_CYLINDER:
        {
            // равномерно по объёму: радиус по корню, иначе центр перевешен
            real rr = s.CylR * M_Sqrt(Uniform());
            real phi = (real)2.0 * M_PI_R * Uniform();
            x = rr * M_Cos(phi);
            y = rr * M_Sin(phi);
            z = s.CylZ0 + (s.CylZ1 - s.CylZ0) * Uniform();
            return;
        }

        default:    // RM_SOURCE_MARINELLI
        {
            real rr, zz;
            if (Uniform() < s.MarCapFraction)
            {
                rr = s.MarRIn * M_Sqrt(Uniform());
                zz = s.MarZ0 + (s.MarZCap - s.MarZ0) * Uniform();
            }
            else
            {
                real a = s.MarRIn * s.MarRIn;
                real b = s.MarROut * s.MarROut;
                rr = M_Sqrt(a + (b - a) * Uniform());
                zz = s.MarZ0 + (s.MarZ1 - s.MarZ0) * Uniform();
            }

            real phi = (real)2.0 * M_PI_R * Uniform();
            x = rr * M_Cos(phi);
            y = rr * M_Sin(phi);
            z = zz;
            return;
        }
    }
}

// = EfficiencySimulator.cs:2996 Sampler.NextWeighted (у всех перенесённых — вес 1)
__device__ real Sim::SourceNextWeighted(real& x, real& y, real& z)
{
    SourceNext(x, y, z);
    return (real)1.0;
}

// = EfficiencySimulator.cs:3007 Sampler.Retune (у всех перенесённых — пусто)
__device__ void Sim::SourceRetune(real energyKev)
{
}

// = EfficiencySimulator.cs:3024 Sampler.DirectionWeight (у всех перенесённых — 1)
__device__ real Sim::SourceDirectionWeight(real x, real y, real z, real ux, real uy, real uz)
{
    return (real)1.0;
}

// = EfficiencySimulator.cs:3038 Sampler.PreferCone (у всех перенесённых — false)
__device__ bool Sim::SourcePreferCone()
{
    return false;
}

// не перенесено: IsoFieldSampler (EfficiencySimulator.cs:3070), ImportanceSampler
// (:3301) — упаковка на них отказывает (`GpuPackScene.cs`); SourceOutsideScene
// (:1597) — считается на ЦП до упаковки (ключ `--cone=far` пробы).

// = EfficiencySimulator.cs:7009 Rotate
__device__ void Sim::Rotate(real& ux, real& uy, real& uz, real cos)
{
    if (cos > (real)1.0) cos = (real)1.0;
    if (cos < (real)-1.0) cos = (real)-1.0;
    real sin = M_Sqrt(M_Max((real)0, (real)1.0 - cos * cos));
    real phi = (real)2.0 * M_PI_R * Uniform();
    real cp = M_Cos(phi), sp = M_Sin(phi);

    real perp = M_Sqrt(ux * ux + uy * uy);
    real nx, ny, nz;
    if (perp < (real)1e-8)
    {
        nx = sin * cp;
        ny = sin * sp;
        nz = cos * (uz >= (real)0 ? (real)1.0 : (real)-1.0);
    }
    else
    {
        nx = ux * cos + sin * (ux * uz * cp - uy * sp) / perp;
        ny = uy * cos + sin * (uy * uz * cp + ux * sp) / perp;
        nz = uz * cos - sin * perp * cp;
    }

    real norm = M_Sqrt(nx * nx + ny * ny + nz * nz);
    ux = nx / norm;
    uy = ny / norm;
    uz = nz / norm;
}

// = EfficiencySimulator.cs:7038 Isotropic
__device__ void Sim::Isotropic(real& ux, real& uy, real& uz)
{
    real cos = (real)2.0 * Uniform() - (real)1.0;
    real sin = M_Sqrt(M_Max((real)0, (real)1.0 - cos * cos));
    real phi = (real)2.0 * M_PI_R * Uniform();
    ux = sin * M_Cos(phi);
    uy = sin * M_Sin(phi);
    uz = cos;
}

// = EfficiencySimulator.cs:10512 InCone
__device__ void Sim::InCone(real ax, real ay, real az, real cosMax, real& ux, real& uy, real& uz)
{
    real cos = cosMax + ((real)1.0 - cosMax) * Uniform();
    ux = ax;
    uy = ay;
    uz = az;
    Rotate(ux, uy, uz, cos);
}
