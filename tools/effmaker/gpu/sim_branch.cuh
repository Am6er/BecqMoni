// sim_branch.cuh — определения методов `Sim` модуля ВЕТВЕЙ ИСТОРИИ (объявления —
// decl_branch.inc): взвешенная ветвь с вынужденным однократным рассеянием, аналоговая
// ветвь с очередью квантов, счёт в бины, каналы и свет. Полоса П221 (`AMBER160`), часть Д.
//
// ПОСТРОЧНЫЙ перенос `BecquerelMonitor/EfficiencyMaker/EfficiencySimulator.cs`: те же
// имена, тот же порядок обращений к `Uniform()`. У каждой функции — `// = <строка>`.
//
// Чужие функции (зовутся по именам C#):
//   полоса Б (sim_geom.cuh): At, StepToBoundary, PathLimit, PathCut, CrystalPath,
//     WeightedMu, SceneBounds, RegIncoherent, RegCoherent, RegPair, Rotate, Isotropic,
//     InCone, SourceDirectionWeight, SourcePreferCone, поле CountMu;
//   полоса В (фотон): InCrystal, ComptonScatter(область), RayleighCosine(область),
//     SampleFluorescence(область), AnalogMu, метки lossAnnihilation/annihilationEscapes/
//     lossXray/lossXrayK/lossXrayL/lightDeposit, буфер вылетов esc*/escapeCount/
//     escapeLost/escapeCollect, CountCascadeOverflow;
//   полоса Г (электрон): CarriedElectronDeposit, ElectronCarryDeposit,
//     OutsideBremsstrahlung, ComptonElectronDirection, enum class ElectronBirth,
//     enum PushQueue (pushAnalog/pushTotal), ElectronMassKev.
//
// Не перенесено (не меняет чисел): `ResetTrace` и `Trace` (трассировка каналов
// `TraceChannels`, отладка; поля trace* читает только `Trace`).
#pragma once
#include "sim.cuh"
#include "tally.cuh"

// =====================================================================================
// Начальное состояние нити
// =====================================================================================

// = инициализаторы полей C# модуля ветвей (EfficiencySimulator.cs:1552-1553, 3747-3815,
// 3853-3981, 1449, 8160-8166): очереди пусты, признак происхождения снят, накопители —
// ноль, указателей на копилки нет (их ставит ядро).
RM_DEVF void Sim::InitBranch()
{
    pendCount = 0;
    totPendCount = 0;
    fromOutsideAnnihilation = false;
    backState = 0;
    for (int k = 0; k < PendMax; k++)
    {
        pendFromOutsideAnnihilation[k] = false;
        pendBackState[k] = 0;
    }

    CountPendingDropped = 0;
    CountEscapeDropped = 0;
    lastHistoryCos = (real)0.0;
    historyDeposit = (real)0.0;
    channelHistograms = nullptr;
    channelBins = 0;
    lightSum = nullptr;
    lightSumLen = 0;
    resolutionWeighted = 0.0;
    resolutionAnalog = 0.0;
    lightBinSplit = 0.0;
    CountLightBinSplit = 0;
    WeightLightBinSplit = 0.0;
    CountPeakBinDropped = 0;
    WeightPeakBinDropped = 0.0;
    CountPeakBinDroppedScattered = 0;
    CountPeakOutOfCone = 0;
}

// =====================================================================================
// Очереди квантов
// =====================================================================================

// = EfficiencySimulator.cs:3783 PushPending
RM_DEVF void Sim::PushPending(real x, real y, real z, real ux, real uy, real uz, real energyKev)
{
    if (pendCount >= PendMax)
    {
        CountPendingDropped++;
        // не перенесено: Interlocked.Increment(TotalPendingDropped) — сумму по нитям даёт
        // слот A_COUNT_PENDING_DROPPED.
        return;
    }

    int k = pendCount++;
    pendX[k] = x;
    pendY[k] = y;
    pendZ[k] = z;
    pendUx[k] = ux;
    pendUy[k] = uy;
    pendUz[k] = uz;
    pendE[k] = energyKev;
    pendFromOutsideAnnihilation[k] = fromOutsideAnnihilation;
    pendBackState[k] = backState;
}

// = EfficiencySimulator.cs:3817 PushTotalPending
RM_DEVF void Sim::PushTotalPending(real x, real y, real z, real ux, real uy, real uz, real energyKev)
{
    if (totPendCount >= PendMax)
    {
        CountPendingDropped++;
        return;
    }

    int k = totPendCount++;
    totPendX[k] = x;
    totPendY[k] = y;
    totPendZ[k] = z;
    totPendUx[k] = ux;
    totPendUy[k] = uy;
    totPendUz[k] = uz;
    totPendE[k] = energyKev;
}

// =====================================================================================
// Взвешенная ветвь
// =====================================================================================

// = EfficiencySimulator.cs:9144 OneHistory (с весом точки; перегрузка :9132 без веса —
// вес 1.0, на GPU не нужна). `histogram` — накопитель a.hist (null — без гистограммы),
// `histogramLength` — `histogram.Length`.
//
// (`AMBER161`, П227) История разрезана на ЧАСТИ, которые ядра стадий (kernels.cuh,
// `WeightedStage*`) исполняют по уплотнённым спискам историй: начало (источник, луч до
// кристалла) → перенос первичного в кристалле → путь рассеянного до кристалла → его
// перенос в кристалле. Здесь части зовутся подряд — порядок розыгрышей тот же, что был
// в одном теле.
RM_DEVF real Sim::OneHistory(real energyKev, real x, real y, real z,
                             double* histogram, int histogramLength, real binKev, real pointWeight)
{
    WeightedFront f;
    WeightedFrontRun(energyKev, x, y, z, pointWeight, f);
    real score = f.score;
    if (f.toCrystal)
    {
        RM_PHASE_BEGIN(2)
        score = WeightedPrimaryCrystal(histogram, histogramLength, binKev, energyKev, f);
        RM_PHASE_END(2)
    }

    if (f.scatter)
    {
        RM_PHASE_BEGIN(3)
        score += ScatteredRun(histogram, histogramLength, binKev, x, y, z, f.ux, f.uy, f.uz,
                              energyKev, f.tauScatter, f.weight);
        RM_PHASE_END(3)
    }

    return score;
}

// Начало взвешенной истории (OneHistory :9175-9196): направление в конусе сцены, вес,
// луч до кристалла, решение о вынужденном рассеянии.
// ⛔ (`AMBER161`, П227) ОДИН вызов ScatteredRun на обе ветви. В C# (:9180, :9255) он
// стоит дважды — для промаха до кристалла и после переноса попавшего луча, — и на
// GPU варп исполнял его ДВАЖДЫ по очереди: сначала нити-промахи (57 % историй), потом
// попавшие. Замер частей (RM_PHASES, 657 кэВ): ScatteredRun — 80 % тактов нитей.
// Здесь начало лишь выбирает τ рассеяния, а вызов — общий. Порядок розыгрышей ИСТОРИИ
// прежний (промах: только рассеяние; попадание: кристалл, затем рассеяние).
RM_DEVF void Sim::WeightedFrontRun(real energyKev, real x, real y, real z, real pointWeight,
                                   WeightedFront& f)
{
    ForgetRay();          // (`A315`, решение Amber 02.10.2026) кэш луча не переживает историю
    RM_PHASE_BEGIN(1)
    const SceneG& sc = *D.scene;
    real dz = sc.sphereZ - z;
    real dist = M_Sqrt(x * x + y * y + dz * dz);
    real weight = pointWeight;
    real ux, uy, uz;
    if (dist > sc.sphereR)
    {
        real cosMax = M_Sqrt(M_Max((real)0.0, (real)1.0 - sc.sphereR * sc.sphereR / (dist * dist)));
        weight *= (real)0.5 * ((real)1.0 - cosMax);
        InCone(-x / dist, -y / dist, dz / dist, cosMax, ux, uy, uz);
    }
    else
    {
        Isotropic(ux, uy, uz);
    }

    weight *= SourceDirectionWeight(x, y, z, ux, uy, uz);

    lastHistoryCos = dist > (real)0.0 ? (ux * (-x) + uy * (-y) + uz * dz) / dist : uz;
    historyDeposit = (real)0.0;

    real px = x, py = y, pz = z, tau;
    bool reached = ToCrystal(px, py, pz, ux, uy, uz, energyKev, tau);
    RM_PHASE_END(1)

    f.ux = ux; f.uy = uy; f.uz = uz;
    f.weight = weight;
    f.px = px; f.py = py; f.pz = pz;
    f.tau = tau;
    f.score = (real)0.0;
    f.toCrystal = false;
    f.scatter = false;
    f.tauScatter = (real)0.0;
    if (!reached && !C.ScoreEntranceOnly && C.SingleScatter)
    {
        f.scatter = true;
        f.tauScatter = KillDepthToExit(x, y, z, ux, uy, uz, energyKev);
    }

    if (reached)
    {
        if (C.ScoreEntranceOnly)
        {
            f.score = weight * M_Exp(-tau);
        }
        else
        {
            f.toCrystal = true;
            f.scatter = true;
            f.tauScatter = tau;
        }
    }
}

// Перенос первичного кванта, дошедшего до кристалла, и его счёт (OneHistory :9200-9250).
// Возвращает счёт истории после этой части (вклад пика или ноль).
RM_DEVF real Sim::WeightedPrimaryCrystal(double* histogram, int histogramLength, real binKev,
                                         real energyKev, const WeightedFront& f)
{
    real score = (real)0.0;
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.
    real escaped = InCrystal(f.px, f.py, f.pz, f.ux, f.uy, f.uz, energyKev, 0);
    // ⚠ float: `energyKev − escaped` — недобор прямого попадания; правило пика
    // при допуске — InPeak (map_data.md §4 п. 1).
    if (InPeak(energyKev, energyKev - escaped))
    {
        score = f.weight * M_Exp(-f.tau);
    }

    real share = f.weight * M_Exp(-f.tau);
    if (energyKev - escaped > (real)0.0 && share > (real)0.0)
    {
        historyDeposit += share;
    }

    if (histogram != nullptr)
    {
        Deposit(histogram, histogramLength, binKev, energyKev, energyKev - escaped, share);
        TallyResolutionPeak(histogramLength - 1, binKev, energyKev,
                            energyKev - escaped, share, false);
        ScoreLight(binKev, energyKev, energyKev - escaped, share);
        if (channelHistograms != nullptr)
        {
            Deposit(channelHistograms + (int)ChannelOf(escaped) * channelBins, channelBins,
                    binKev, energyKev, energyKev - escaped, share);
        }
    }

    return score;
}

// = EfficiencySimulator.cs:4792 ToCrystal
RM_DEVF bool Sim::ToCrystal(real& x, real& y, real& z, real ux, real uy, real uz, real energyKev, real& tau)
{
    tau = (real)0.0;
    real travelled = (real)0.0;
    real limit = PathLimit(x, y, z);
    for (int guard = 0; guard < 200; guard++)
    {
        int here = At(x, y, z);
        if (here >= 0 && D.regions[here].IsCrystal)
        {
            return true;
        }

        real step = StepToBoundary(x, y, z, ux, uy, uz);
        if (step >= CS_DOUBLE_MAX || PathCut(travelled + step, limit))
        {
            return false;
        }

        if (here >= 0)
        {
            CountMu++;
            tau += WeightedMu(here, energyKev) * step;
            if (tau > (real)60.0)
            {
                return false;      // exp(-60) — заведомо ноль
            }
        }

        // ⚠ float: подталкивание 1e-7 см ниже ULP float уже на ~1 см (map_geometry.md
        // §5.4) — точка может не сдвинуться с границы, и цикл холостит до guard.
        real advance = step + RM_NUDGE;
        x += ux * advance;
        y += uy * advance;
        z += uz * advance;
        travelled += advance;
    }

    return false;
}

// = EfficiencySimulator.cs:4874 ScatteredRun
// (`AMBER161`) Разрезан на путь рассеянного до кристалла (`ScatterToCrystal`), перенос
// в кристалле и счёт (`ScatteredFinish`); здесь — подряд, как в C#.
RM_DEVF real Sim::ScatteredRun(double* histogram, int histogramLength, real binKev,
                               real x, real y, real z, real ux, real uy, real uz,
                               real energyKev, real tauKill, real weight)
{
    ScatteredReset();
    real sw, scattered, sEscaped;
    if (!ScatteredContribution(x, y, z, ux, uy, uz, energyKev, tauKill, sw, scattered, sEscaped))
    {
        return (real)0.0;
    }

    return ScatteredFinish(histogram, histogramLength, binKev, energyKev, weight, sw, scattered, sEscaped);
}

// Метки исхода перед переносом рассеянного (ScatteredRun :4878-4884).
RM_DEV void Sim::ScatteredReset()
{
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.
}

// Счёт рассеянного после его переноса в кристалле (ScatteredRun :4890-4940).
RM_DEVF real Sim::ScatteredFinish(double* histogram, int histogramLength, real binKev,
                                  real energyKev, real weight, real sw, real scattered, real sEscaped)
{
    // ⚠ float: недобор рассеянной истории — разность двух сумм (map_data.md §4 п. 1).
    real deposited = scattered - sEscaped;
    real share = weight * sw;
    if (deposited > (real)0.0 && share > (real)0.0)
    {
        historyDeposit += share;
    }

    if (histogram != nullptr)
    {
        Deposit(histogram, histogramLength, binKev, energyKev, deposited, share);
        TallyResolutionPeak(histogramLength - 1, binKev, energyKev, deposited, share, false);
        ScoreLight(binKev, energyKev, deposited, share);
        if (channelHistograms != nullptr)
        {
            ResponseChannel channel = ChannelOf(sEscaped);
            if (channel == ResponseChannel::Peak
                && !(C.PeakChannelByTolerance && InPeak(energyKev, deposited)))
            {
                channel = ResponseChannel::Compton;
            }

            Deposit(channelHistograms + (int)channel * channelBins, channelBins,
                    binKev, energyKev, deposited, share);
        }
    }

    return InPeak(energyKev, deposited) ? share : (real)0.0;
}

// = EfficiencySimulator.cs:4951 ScatteredContribution
// (`AMBER161`) = путь рассеянного до кристалла (`ScatterToCrystal`) + его перенос в
// кристалле; ядра стадий зовут части раздельно, по уплотнённым спискам.
RM_DEVF bool Sim::ScatteredContribution(real x, real y, real z, real ux, real uy, real uz,
                                        real energyKev, real tauKill,
                                        real& weight, real& scatteredEnergy, real& escapedEnergy)
{
    real px, py, pz, sx, sy, sz;
    escapedEnergy = (real)0.0;
    if (!ScatterToCrystal(x, y, z, ux, uy, uz, energyKev, tauKill, weight, scatteredEnergy,
                          px, py, pz, sx, sy, sz))
    {
        return false;
    }

    RM_PHASE_BEGIN(7)
    escapedEnergy = InCrystal(px, py, pz, sx, sy, sz, scatteredEnergy, 0);
    RM_PHASE_END(7)
    return true;
}

// Путь вынужденно рассеянного кванта до кристалла (ScatteredContribution :4955-5050):
// точка рассеяния на первичном луче, канал и угол, луч до кристалла. true — дошёл; тогда
// (px, py, pz) — точка входа, (sx, sy, sz) — направление, `weight` — вес вклада.
RM_DEVF bool Sim::ScatterToCrystal(real x, real y, real z, real ux, real uy, real uz,
                                   real energyKev, real tauKill,
                                   real& weight, real& scatteredEnergy,
                                   real& outX, real& outY, real& outZ,
                                   real& outUx, real& outUy, real& outUz)
{
    weight = (real)0.0;
    scatteredEnergy = (real)0.0;
    if (!C.SingleScatter || !(tauKill > (real)1e-6))
    {
        return false;
    }

#ifdef RM_REAL_FLOAT
    // ⚠ float: `1 − exp(−τ)` при τ от 1e-6 теряет все разряды (map_data.md §4 п. 8) —
    // во float через expm1; в double — как C#, ради побитовой сверки ступени 1.
    real interacted = -expm1(-tauKill);
#else
    real interacted = (real)1.0 - M_Exp(-tauKill);
#endif

    // Рулетка по весу поправки (`T43`): при пороге 0 (умолчание) число НЕ тянется.
    real survival = (real)1.0;
    if (C.ScatterRouletteWeight > 0.0 && interacted < (real)C.ScatterRouletteWeight)
    {
        survival = interacted / (real)C.ScatterRouletteWeight;
        if (Uniform() >= survival)
        {
            return false;              // история выбыла, вклад ноль
        }
    }

    // точка первого взаимодействия: tau_целевое из усечённой экспоненты
#ifdef RM_REAL_FLOAT
    // ⚠ float: `−log(1 − u·interacted)` при малом interacted — через log1p (map_data.md §4 п. 8).
    real tauTarget = -log1p(-Uniform() * interacted);
#else
    real tauTarget = -M_Log((real)1.0 - Uniform() * interacted);
#endif

    real px = x, py = y, pz = z;
    real accumulated = (real)0.0;
    int here = -1;
    real travelled = (real)0.0;
    real limit = PathLimit(px, py, pz);
    for (int guard = 0; guard < 200; guard++)
    {
        here = At(px, py, pz);
        if (here >= 0 && D.regions[here].IsCrystal)
        {
            return false;             // до кристалла не рассеялся
        }

        real step = StepToBoundary(px, py, pz, ux, uy, uz);
        if (step >= CS_DOUBLE_MAX || PathCut(travelled + step, limit))
        {
            return false;
        }

        if (here >= 0)
        {
            CountMu++;
            real mu = WeightedMu(here, energyKev);
            if (mu > (real)0.0 && accumulated + mu * step >= tauTarget)
            {
                real advance = (tauTarget - accumulated) / mu;
                px += ux * advance;
                py += uy * advance;
                pz += uz * advance;
                real incoherent = RegIncoherent(here, energyKev);
                real coherent = C.RayleighToCrystal ? RegCoherent(here, energyKev) : (real)0.0;
                real alive = incoherent + coherent;
                real share = alive / mu;
                if (!(share > (real)0.0))
                {
                    return false;     // взаимодействие было, но убивающее
                }

                real scattered;
                real sx = ux, sy = uy, sz = uz;
                // ⚠ Порядок проверок держит поток розыгрышей при выключенном ключе:
                // coherent там ноль, и число на выбор канала НЕ тянется.
                if (coherent > (real)0.0 && Uniform() * alive < coherent)
                {
                    scattered = energyKev;
                    Rotate(sx, sy, sz, RayleighCosine(here, energyKev));
                }
                else
                {
                    real cos;
                    scattered = ComptonScatter(here, energyKev, cos);
                    Rotate(sx, sy, sz, cos);
                }

                real tau2;
                RM_PHASE_BEGIN(6)
                bool toCrystal = ToCrystal(px, py, pz, sx, sy, sz, scattered, tau2);
                RM_PHASE_END(6)
                if (!toCrystal)
                {
                    return false;
                }

                weight = interacted * share * M_Exp(-tau2) / survival;
                scatteredEnergy = scattered;
                outX = px; outY = py; outZ = pz;
                outUx = sx; outUy = sy; outUz = sz;
                return true;
            }

            accumulated += mu * step;
        }

        // ⚠ float: подталкивание 1e-7 см (map_geometry.md §5.4).
        real next = step + RM_NUDGE;
        px += ux * next;
        py += uy * next;
        pz += uz * next;
        travelled += next;
    }

    return false;
}

// = EfficiencySimulator.cs:5094 KillDepthToExit
RM_DEVF real Sim::KillDepthToExit(real x, real y, real z, real ux, real uy, real uz, real energyKev)
{
    real tau = (real)0.0;
    real travelled = (real)0.0;
    real limit = PathLimit(x, y, z);
    for (int guard = 0; guard < 200; guard++)
    {
        int here = At(x, y, z);
        real step = StepToBoundary(x, y, z, ux, uy, uz);
        if (step >= CS_DOUBLE_MAX || PathCut(travelled + step, limit))
        {
            return tau;
        }

        if (here >= 0)
        {
            CountMu++;
            tau += WeightedMu(here, energyKev) * step;
            if (tau > (real)60.0)
            {
                return (real)60.0;
            }
        }

        // ⚠ float: подталкивание 1e-7 см (map_geometry.md §5.4).
        real advance = step + RM_NUDGE;
        x += ux * advance;
        y += uy * advance;
        z += uz * advance;
        travelled += advance;
    }

    return tau;
}

// =====================================================================================
// Аналоговая ветвь
// =====================================================================================

// = EfficiencySimulator.cs:9665 AnalogHistory
RM_DEVF real Sim::AnalogHistory(real energyKev, real x, real y, real z, real& weight,
                                bool outsideOnly, bool& inWeightedCone,
                                real& depositedOutside, bool& comptonOutside)
{
    real ux, uy, uz, limit;
    AnalogFront(x, y, z, weight, inWeightedCone, ux, uy, uz, limit);
    if (outsideOnly && inWeightedCone)
    {
        depositedOutside = (real)0.0;
        comptonOutside = false;
        return (real)0.0;
    }

    return AnalogTransport(x, y, z, ux, uy, uz, energyKev, limit,
                           depositedOutside, comptonOutside);
}

// (`AMBER161`, П227) Начало аналоговой истории (AnalogHistory :9669-9715): направление,
// вес, предел пути, признак конуса взвешенной ветви.
RM_DEVF void Sim::AnalogFront(real x, real y, real z, real& weight, bool& inWeightedCone,
                              real& ux, real& uy, real& uz, real& limit)
{
    ForgetRay();          // (`A315`, решение Amber 02.10.2026) кэш луча не переживает историю
    const SceneG& sc = *D.scene;
    limit = PathLimit(x, y, z);

    real coneZ, coneR;
    real coneDist = (real)0.0;
    if ((C.AnalogConeSampling || SourcePreferCone())
        && SceneBounds(coneZ, coneR)
        && (coneDist = M_Sqrt(x * x + y * y + (coneZ - z) * (coneZ - z))) > coneR)
    {
        real cosMax = M_Sqrt(M_Max((real)0.0, (real)1.0 - coneR * coneR / (coneDist * coneDist)));
        weight *= (real)0.5 * ((real)1.0 - cosMax);
        InCone(-x / coneDist, -y / coneDist, (coneZ - z) / coneDist, cosMax, ux, uy, uz);
    }
    else
    {
        Isotropic(ux, uy, uz);
    }

    weight *= SourceDirectionWeight(x, y, z, ux, uy, uz);

    // Лежит ли первичное направление в конусе взвешенной ветви (`AMBER66`):
    // арифметика без розыгрышей.
    inWeightedCone = true;
    real coneDz = sc.sphereZ - z;
    real coneR2 = M_Sqrt(x * x + y * y + coneDz * coneDz);
    if (coneR2 > sc.sphereR)
    {
        real cosMaxDet = M_Sqrt(M_Max(
            (real)0.0, (real)1.0 - sc.sphereR * sc.sphereR / (coneR2 * coneR2)));
        real cosToAxis = (ux * (-x) + uy * (-y) + uz * coneDz) / coneR2;
        inWeightedCone = cosToAxis >= cosMaxDet;
    }
}

// = EfficiencySimulator.cs:9741 AnalogTransport
// (`AMBER161`, П227) = начало (`AnalogWalkBegin`) и шаги (`AnalogStep`) до конца истории.
// Шаг — одна итерация цикла `for (guard …)` C# или выемка отложенного кванта; ядро с
// подкачкой (`AnalogKernel`) зовёт шаги по одному и берёт новую историю, как только
// старая кончилась. Порядок розыгрышей истории — тот же.
RM_DEVF real Sim::AnalogTransport(real x, real y, real z, real ux, real uy, real uz,
                                  real energyKev, real limit,
                                  real& depositedOutside, bool& comptonOutside)
{
    AnalogWalk w;
    AnalogWalkBegin(x, y, z, ux, uy, uz, energyKev, limit, w);
    while (!AnalogStep(w))
    {
    }

    depositedOutside = w.depositedOutside;
    comptonOutside = w.comptonOutside;
    historyBackscatter = w.depositedBackscatter;
    return w.deposited;
}

// = начало AnalogTransport (EfficiencySimulator.cs:9745-9772).
RM_DEVF void Sim::AnalogWalkBegin(real x, real y, real z, real ux, real uy, real uz,
                                  real energyKev, real limit, AnalogWalk& w)
{
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.

    w.x = x; w.y = y; w.z = z;
    w.ux = ux; w.uy = uy; w.uz = uz;
    w.e = energyKev;
    // ⚠ float: `deposited` — СУММА кусков заноса; во float (E − E′) + E′ ≠ E на ulp(E),
    // правило пика при допуске — InPeak (map_data.md §4 п. 1).
    w.deposited = (real)0.0;
    w.depositedOutside = (real)0.0;
    w.depositedBackscatter = (real)0.0;
    fromOutsideAnnihilation = false;
    backState = 0;
    w.travelled = (real)0.0;
    w.comptonOutside = false;
    w.limit = limit;
    w.guard = 0;

    // Очередь квантов истории (`A52`/`A55`).
    pendCount = 0;
}

// Один шаг AnalogTransport. true — история кончилась (очередь пуста).
// `continue` тела — следующий шаг (guard + 1); `break` тела — выход из цикла шагов
// (guard = 400), дальше — выемка отложенного кванта, как у C# после цикла `for`.
RM_DEVF bool Sim::AnalogStep(AnalogWalk& w)
{
    real x = w.x, y = w.y, z = w.z;
    real ux = w.ux, uy = w.uy, uz = w.uz;
    real e = w.e, deposited = w.deposited, travelled = w.travelled;
    const real limit = w.limit;
    real& depositedOutside = w.depositedOutside;
    bool& comptonOutside = w.comptonOutside;
    bool finished = false;
    if (w.guard < 400 && e > (real)1.0)
    {
        bool exitFor = false;
        do
        {
                int here = At(x, y, z);
                if (here >= 0 && D.regions[here].IsCrystal)
                {
                    escapeCount = 0;
                    escapeLost = 0;
                    escapeCollect = true;
                    RM_PHASE_BEGIN(2)
                    real escaped = InCrystal(x, y, z, ux, uy, uz, e, 0);
                    RM_PHASE_END(2)
                    escapeCollect = false;
                    // ⚠ float: порог 1e-9 кэВ ниже ulp(e) — «вклад был» решает точное равенство
                    // escaped == e у пролёта насквозь.
                    if (e - escaped > (real)1e-9)
                    {
                        deposited += e - escaped;
                        if (backState == 1)
                        {
                            w.depositedBackscatter += e - escaped;
                        }

                        if (fromOutsideAnnihilation)
                        {
                            depositedOutside += e - escaped;
                        }

                        // (`AMBER311`) Вышедший из кристалла без метки её больше не получает.
                        if (backState != 1)
                        {
                            backState = 2;
                        }

                        // Возврат из обвязки (`A55`): вылетевшее — в очередь истории.
                        for (int k = 0; k < escapeCount; k++)
                        {
                            PushPending(escX[k], escY[k], escZ[k],
                                        escUx[k], escUy[k], escUz[k],
                                        escE[k]);
                        }

                        CountEscapeDropped += escapeLost;
                        { exitFor = true; break; }
                    }

                    // пролетел насквозь без вклада — с дальней грани дальше
                    // ⚠ float: подталкивание 1e-7 см (map_geometry.md §5.4).
                    real through = CrystalPath(x, y, z, ux, uy, uz) + RM_NUDGE;
                    x += ux * through;
                    y += uy * through;
                    z += uz * through;
                    travelled += through;
                    continue;
                }

                real step = StepToBoundary(x, y, z, ux, uy, uz);
                if (step >= CS_DOUBLE_MAX || PathCut(travelled + step, limit))
                {
                    { exitFor = true; break; }              // ушёл из сцены
                }

                real muKill = here < 0 ? (real)0.0 : AnalogMu(here, e);
                if (muKill > (real)0.0)
                {
                    real free = -M_Log((real)1.0 - Uniform()) / muKill;
                    if (free < step)
                    {
                        x += ux * free;
                        y += uy * free;
                        z += uz * free;
                        travelled += free;
                        real incoherent = RegIncoherent(here, e);
                        real coherent = C.RayleighScatter ? RegCoherent(here, e) : (real)0.0;
                        real carried;
                        real gain;
                        real channel = Uniform() * muKill;
                        if (channel < coherent)
                        {
                            // Когерентное: только поворот, энергия та же.
                            Rotate(ux, uy, uz, RayleighCosine(here, e));
                            continue;
                        }

                        if (channel >= coherent + incoherent)
                        {
                            // Фотопоглощение ИЛИ рождение пары вне кристалла (`F27`, `A52`).
                            int material = D.regions[here].Material;
                            real pairMu = RegPair(here, e, C.XcomPairThreshold);
                            real restMu = muKill - coherent - incoherent;
                            if (pairMu > restMu)
                            {
                                pairMu = restMu;    // сумма каналов не больше полного
                            }

                            if (pairMu > (real)0.0 && channel >= muKill - pairMu
                                && e > (real)2.0 * (real)ElectronMassKev)
                            {
                                if (C.ElectronLayerTransport)
                                {
                                    gain = CarriedElectronDeposit(
                                        x, y, z, ElectronBirth::Pair, e - (real)2.0 * (real)ElectronMassKev,
                                        ux, uy, uz, material, pushAnalog);
                                    deposited += gain;
                                    if (backState == 1)
                                    {
                                        w.depositedBackscatter += gain;
                                    }

                                    if (fromOutsideAnnihilation)
                                    {
                                        depositedOutside += gain;
                                    }
                                }
                                else if (ElectronCarryDeposit(x, y, z, ux, uy, uz,
                                                              e - (real)2.0 * (real)ElectronMassKev
                                                              - OutsideBremsstrahlung(
                                                                  x, y, z, e - (real)2.0 * (real)ElectronMassKev,
                                                                  material, pushAnalog),
                                                              carried))
                                {
                                    deposited += carried;
                                    if (backState == 1)
                                    {
                                        w.depositedBackscatter += carried;
                                    }

                                    if (fromOutsideAnnihilation)
                                    {
                                        depositedOutside += carried;
                                    }
                                }

                                real ax, ay, az;
                                Isotropic(ax, ay, az);
                                // Признак происхождения — ДО PushPending (`AMBER52`).
                                fromOutsideAnnihilation = true;
                                PushPending(x, y, z, -ax, -ay, -az, (real)ElectronMassKev);

                                ux = ax;
                                uy = ay;
                                uz = az;
                                e = (real)ElectronMassKev;
                                continue;
                            }

                            real xrayOut = C.SampleFluorescenceOutside
                                ? SampleFluorescence(here, e) : (real)0.0;
                            if (xrayOut > (real)0.0)
                            {
                                real gx = ux, gy = uy, gz = uz;
                                Isotropic(ux, uy, uz);
                                if (C.ElectronLayerTransport)
                                {
                                    gain = CarriedElectronDeposit(
                                        x, y, z, ElectronBirth::Photo, e - xrayOut,
                                        gx, gy, gz, material, pushAnalog);
                                    deposited += gain;
                                    if (backState == 1)
                                    {
                                        w.depositedBackscatter += gain;
                                    }

                                    if (fromOutsideAnnihilation)
                                    {
                                        depositedOutside += gain;
                                    }
                                }
                                else if (ElectronCarryDeposit(x, y, z, ux, uy, uz,
                                                              e - xrayOut
                                                              - OutsideBremsstrahlung(
                                                                  x, y, z, e - xrayOut,
                                                                  material, pushAnalog),
                                                              carried))
                                {
                                    deposited += carried;
                                    if (backState == 1)
                                    {
                                        w.depositedBackscatter += carried;
                                    }

                                    if (fromOutsideAnnihilation)
                                    {
                                        depositedOutside += carried;
                                    }
                                }

                                // (`AMBER311`) Рентген защиты — новый квант, метка родителя на него не переходит.
                                if (backState == 1)
                                {
                                    backState = 0;
                                }

                                e = xrayOut;
                                continue;           // квант летит дальше
                            }

                            if (C.ElectronLayerTransport)
                            {
                                gain = CarriedElectronDeposit(
                                    x, y, z, ElectronBirth::Photo, e,
                                    ux, uy, uz, material, pushAnalog);
                                deposited += gain;
                                if (backState == 1)
                                {
                                    w.depositedBackscatter += gain;
                                }

                                if (fromOutsideAnnihilation)
                                {
                                    depositedOutside += gain;
                                }
                            }
                            else if (ElectronCarryDeposit(x, y, z, ux, uy, uz,
                                                          e - OutsideBremsstrahlung(
                                                              x, y, z, e, material, pushAnalog),
                                                          carried))
                            {
                                deposited += carried;
                                if (backState == 1)
                                {
                                    w.depositedBackscatter += carried;
                                }

                                if (fromOutsideAnnihilation)
                                {
                                    depositedOutside += carried;
                                }
                            }

                            { exitFor = true; break; }
                        }

                        real cos;
                        real after = ComptonScatter(here, e, cos);
                        comptonOutside = true;              // замер `S55`

                        if (C.ElectronLayerTransport)
                        {
                            real ux0 = ux, uy0 = uy, uz0 = uz;
                            Rotate(ux, uy, uz, cos);
                            real dx, dy, dz;
                            ComptonElectronDirection(e, ux0, uy0, uz0, after, ux, uy, uz,
                                                     dx, dy, dz);
                            gain = CarriedElectronDeposit(
                                x, y, z, ElectronBirth::Given, e - after,
                                dx, dy, dz, D.regions[here].Material, pushAnalog);
                            deposited += gain;
                            if (backState == 1)
                            {
                                w.depositedBackscatter += gain;
                            }

                            if (fromOutsideAnnihilation)
                            {
                                depositedOutside += gain;
                            }

                            if (backState == 0 && cos < BackscatterMaxCos)
                            {
                                backState = 1;     // (`AMBER311`) ПОСЛЕ заноса электрона отдачи
                            }

                            e = after;
                            continue;
                        }

                        // Занос комптон-электрона — ДО поворота фотона; фотон летит дальше.
                        if (ElectronCarryDeposit(x, y, z, ux, uy, uz,
                                                 e - after - OutsideBremsstrahlung(
                                                     x, y, z, e - after, D.regions[here].Material, pushAnalog),
                                                 carried))
                        {
                            deposited += carried;
                            if (backState == 1)
                            {
                                w.depositedBackscatter += carried;
                            }

                            if (fromOutsideAnnihilation)
                            {
                                depositedOutside += carried;
                            }
                        }

                        if (backState == 0 && cos < BackscatterMaxCos)
                        {
                            backState = 1;     // (`AMBER311`) ПОСЛЕ заноса электрона отдачи
                        }

                        e = after;
                        Rotate(ux, uy, uz, cos);
                        continue;
                    }
                }

                // ⚠ float: подталкивание 1e-7 см (map_geometry.md §5.4).
                real next = step + RM_NUDGE;
                x += ux * next;
                y += uy * next;
                z += uz * next;
                travelled += next;
        } while (false);

        w.guard = exitFor ? 400 : w.guard + 1;
    }
    else if (pendCount > 0)
    {
        // Следующий отложенный квант истории (`A52`/`A55`): свой путь и предел заново,
        // `deposited` истории общий.
        int k = --pendCount;
        x = pendX[k];
        y = pendY[k];
        z = pendZ[k];
        ux = pendUx[k];
        uy = pendUy[k];
        uz = pendUz[k];
        e = pendE[k];
        fromOutsideAnnihilation = pendFromOutsideAnnihilation[k];
        backState = pendBackState[k];
        travelled = (real)0.0;
        w.guard = 0;
    }
    else
    {
        finished = true;
    }

    w.x = x; w.y = y; w.z = z;
    w.ux = ux; w.uy = uy; w.uz = uz;
    w.e = e;
    w.deposited = deposited;
    w.travelled = travelled;
    return finished;
}

// =====================================================================================
// (`AMBER274`, Э1 + физика 28) Раздел электрона пробы — первичный электрон/позитрон из
// точки пробы (EfficiencySimulator.cs: ElectronSourceResponse / ElectronSourceHistory /
// ElectronHistoryFrom / CapsuleExit / PositronSourceDeposit / FlushEscapesToQueue).
// Решение Amber 29.10.2026 вопросником, дословно: «Портировать на GPU». Очередь квантов
// после электрона ведёт тот же обход, что у кванта узла (`AnalogStep`), — второго
// проводника нет (`S37`).
// =====================================================================================

// = EfficiencySimulator.cs ElectronHistoryFrom (обход очереди — `AnalogStep`, как цикл
// `AnalogTransportFrom` с уже набранным заносом).
RM_DEVF real Sim::ElectronHistoryFrom(real te, bool positron, real x, real y, real z,
                                      real ux, real uy, real uz, EsrcStats& st)
{
    ForgetRay();
    real limit = PathLimit(x, y, z);
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.
    fromOutsideAnnihilation = false;
    backState = 0;
    pendCount = 0;

    real t = CapsuleExit(te, uz);
    if (!(t > (real)0.0))
    {
        st.stoppedInCapsule++;
    }

    int here = At(x, y, z);
    int material = here >= 0 ? D.regions[here].Material : -1;
    // (`AMBER274`, Р7) «Вошло» — только первичная частица: разность счётчика до обхода очереди.
    long long carriesBefore = CountLayerCarries;
    real deposited = positron
        ? PositronSourceDeposit(x, y, z, t, ux, uy, uz, material, st)
        : t > (real)0.0
            ? CarriedElectronDeposit(x, y, z, ElectronBirth::Given, t, ux, uy, uz, material, pushAnalog)
            : (real)0.0;
    if (CountLayerCarries > carriesBefore)
    {
        st.entered++;
    }

    if (pendCount > 0)
    {
        st.queuedPhotons += pendCount;
        int k = --pendCount;
        fromOutsideAnnihilation = pendFromOutsideAnnihilation[k];
        backState = pendBackState[k];
        AnalogWalk w;
        w.x = pendX[k]; w.y = pendY[k]; w.z = pendZ[k];
        w.ux = pendUx[k]; w.uy = pendUy[k]; w.uz = pendUz[k];
        w.e = pendE[k];
        w.deposited = deposited;
        w.depositedOutside = (real)0.0;
        w.depositedBackscatter = (real)0.0;
        w.travelled = (real)0.0;
        w.limit = limit;
        w.guard = 0;
        w.comptonOutside = false;
        while (!AnalogStep(w))
        {
        }

        deposited = w.deposited;
    }

    return deposited;
}

// = EfficiencySimulator.cs CapsuleExit. Капсула и воздух — поля сцены (`SceneG.capsule*`,
// упаковщик `GpuPackScene.cs`); геометрия — уже в сантиметрах (`Д1`).
RM_DEVF real Sim::CapsuleExit(real te, real uz)
{
    const SceneG& sc = *D.scene;
    if (!sc.capsulePoint || !(te > (real)0.0))
    {
        return te;
    }

    real cos = M_Abs(uz);
    real sin = M_Sqrt(M_Max((real)0.0, (real)1.0 - cos * cos));
    real t = te;
    real half = sc.capsuleRingHalf, rIn = sc.capsuleRingRIn, rOut = sc.capsuleRingROut;
    if (half > (real)0.0 && rIn > (real)0.0 && rOut > rIn && cos * rIn < half * sin && sc.capsuleRingMedium >= 0)
    {
        real wallCm = (rOut - rIn) / M_Max(sin, (real)1e-6);
        const ElectronMaterialG& al = D.electronMats[sc.capsuleRingMedium];
        real left = ResidualAfter(al, t, RangeOf(al, t), wallCm * sc.capsuleRingDensity);
        t = left > (real)0.0 ? EnergyOfRange(al, left) : (real)0.0;
    }
    else if (sc.capsuleThickness > (real)0.0 && sc.capsuleFilmMedium >= 0 && sc.capsuleFilmDensity > (real)0.0)
    {
        real pathCm = sc.capsuleThickness / M_Max(cos, (real)0.05);
        const ElectronMaterialG& medium = D.electronMats[sc.capsuleFilmMedium];
        real residual = ResidualAfter(medium, t, RangeOf(medium, t), pathCm * sc.capsuleFilmDensity);
        t = residual > (real)0.0 ? EnergyOfRange(medium, residual) : (real)0.0;
    }

    if (t > (real)0.0 && uz > (real)0.0 && sc.pointAirCm > (real)0.0 && sc.capsuleAir)
    {
        real airCm = sc.pointAirCm / M_Max(uz, (real)0.05);
        const ElectronMaterialG& water = D.electronMats[sc.waterTable];
        real residual = ResidualAfter(water, t, RangeOf(water, t), airCm * AirDensity);
        t = residual > (real)0.0 ? EnergyOfRange(water, residual) : (real)0.0;
    }

    return t;
}

// = EfficiencySimulator.cs PositronSourceDeposit
RM_DEVF real Sim::PositronSourceDeposit(real x, real y, real z, real t, real ux, real uy, real uz,
                                        int material, EsrcStats& st)
{
    real px = x, py = y, pz = z;
    real deposited = (real)0.0;
    bool inCrystal = false;
    if (t > (real)0.0)
    {
        real xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn;
        bool entered = CarriedElectronEnters(x, y, z, ElectronBirth::Given, t, ux, uy, uz, material,
                                             pushAnalog, xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn, true);
        px = xIn;
        py = yIn;
        pz = zIn;
        if (entered)
        {
            inCrystal = true;
            // (`AMBER274`, Р11) Вошёл не выше порога переноса — оседает на месте входа.
            if (!(tIn > TransportCutKev))
            {
                if (tIn > (real)0.0)
                {
                    AddLight(tIn, tIn);
                    deposited = tIn;
                }
            }
            else
            {
                escapeCount = 0;
                escapeLost = 0;
                escapeCollect = true;
                carriedInCrystal = true;
                real lost = ElectronLoss(xIn, yIn, zIn, tIn, 0, ElectronBirth::Given, uxIn, uyIn, uzIn, true);
                carriedInCrystal = false;
                escapeCollect = false;
                FlushEscapesToQueue();
                deposited = M_Max((real)0.0, tIn - lost);
                if (C.PositronOffset)
                {
                    PositronStop(tIn, px, py, pz);
                }
            }
        }
        else if (layersLeftScene)
        {
            return (real)0.0;       // ушёл из сцены (исход переноса в слоях) — аннигилирует вне её
        }
    }

    st.annihilations++;
    real ax, ay, az;
    Isotropic(ax, ay, az);
    if (!inCrystal)
    {
        PushPending(px, py, pz, ax, ay, az, (real)ElectronMassKev);
        PushPending(px, py, pz, -ax, -ay, -az, (real)ElectronMassKev);
        return deposited;
    }

    for (int q = 0; q < 2; q++)
    {
        real sign = q == 0 ? (real)1.0 : (real)-1.0;
        escapeCount = 0;
        escapeLost = 0;
        escapeCollect = true;
        real escaped = InCrystal(px, py, pz, sign * ax, sign * ay, sign * az, (real)ElectronMassKev, 0);
        escapeCollect = false;
        FlushEscapesToQueue();
        if ((real)ElectronMassKev - escaped > (real)0.0)
        {
            deposited += (real)ElectronMassKev - escaped;
        }
    }

    return deposited;
}

// = EfficiencySimulator.cs FlushEscapesToQueue
RM_DEVF void Sim::FlushEscapesToQueue()
{
    for (int k = 0; k < escapeCount; k++)
    {
        PushPending(escX[k], escY[k], escZ[k], escUx[k], escUy[k], escUz[k], escE[k]);
    }

    CountEscapeDropped += escapeLost;
}

// =====================================================================================
// (`AMBER274` Ф8, полоса `f8cyl` 02.11.2026) ШОВ СОБЫТИЯ ЦЕЛИКОМ — строка ветви: бета ветви и
// кванты каскада её уровня одной историей (= EfficiencySimulator.cs BranchEventFrom). Решение
// Amber 30.10.2026 вопросником, дословно: «В Э1 событием целиком (Рекомендую)». Порядок
// розыгрышей — ровно C#; обход очереди — тот же `AnalogStep`, что у узла.
// =====================================================================================

// = EfficiencySimulator.cs WalkPendingFrom: снять верхний квант очереди и вести обход с уже
// набранным заносом (тот же блок, что в конце ElectronHistoryFrom).
RM_DEVF real Sim::WalkPendingFrom(real limit, real deposited)
{
    if (pendCount <= 0)
    {
        return deposited;
    }

    int k = --pendCount;
    fromOutsideAnnihilation = pendFromOutsideAnnihilation[k];
    backState = pendBackState[k];
    AnalogWalk w;
    w.x = pendX[k]; w.y = pendY[k]; w.z = pendZ[k];
    w.ux = pendUx[k]; w.uy = pendUy[k]; w.uz = pendUz[k];
    w.e = pendE[k];
    w.deposited = deposited;
    w.depositedOutside = (real)0.0;
    w.depositedBackscatter = (real)0.0;
    w.travelled = (real)0.0;
    w.limit = limit;
    w.guard = 0;
    w.comptonOutside = false;
    while (!AnalogStep(w))
    {
    }

    return w.deposited;
}

// = EfficiencySimulator.cs SampleBranchBeta: узел — двоичным поиском, внутри — равномерно.
RM_DEV real SampleBranchBeta(const Sim::BranchTabG& tab, real u)
{
    int lo = 0, hi = tab.betaN - 1;
    while (hi - lo > 1)
    {
        int mid = (lo + hi) >> 1;
        if (tab.betaCdf[mid] <= u)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    real span = tab.betaCdf[hi] - tab.betaCdf[lo];
    real frac = span > (real)0.0 ? (u - tab.betaCdf[lo]) / span : (real)0.5;
    if (frac < (real)0.0)
    {
        frac = (real)0.0;
    }
    else if (frac > (real)1.0)
    {
        frac = (real)1.0;
    }

    return tab.betaT[lo] + frac * (tab.betaT[hi] - tab.betaT[lo]);
}

// = EfficiencySimulator.cs BranchEventFrom.
RM_DEVF real Sim::BranchEventFrom(const BranchTabG& tab, real x, real y, real z, real ux, real uy, real uz,
                                  BranchStats& st, real& gammaDeposit, real& gammaLight)
{
    ForgetRay();
    real limit = PathLimit(x, y, z);
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.
    fromOutsideAnnihilation = false;
    backState = 0;
    pendCount = 0;

    real te = SampleBranchBeta(tab, Uniform());
    real t = CapsuleExit(te, uz);
    if (!(t > (real)0.0))
    {
        st.stoppedInCapsule++;
    }

    int here = At(x, y, z);
    int material = here >= 0 ? D.regions[here].Material : -1;
    long long carriesBefore = CountLayerCarries;
    real deposited = t > (real)0.0
        ? CarriedElectronDeposit(x, y, z, ElectronBirth::Given, t, ux, uy, uz, material, pushAnalog)
        : (real)0.0;
    if (CountLayerCarries > carriesBefore)
    {
        st.entered++;
    }

    deposited = WalkPendingFrom(limit, deposited);
    real betaDeposit = deposited, betaLight = lightDeposit;

    // Каскад — из точки распада, после обхода беты (C#: тот же порядок).
    fromOutsideAnnihilation = false;
    backState = 0;
    ForgetRay();
    int level = tab.startLevel;
    int steps = 0;
    for (; steps < 64; steps++)
    {
        int count = tab.levelCount[level];
        if (count <= 0)
        {
            break;
        }

        int first = tab.levelFirst[level];
        real u = Uniform();
        int tr = first;
        while (tr < first + count - 1 && u >= tab.trCdf[tr])
        {
            tr++;
        }

        real v = Uniform();
        int kind = 0;
        while (kind < 4 && v >= tab.emitCdf[tr * 5 + kind])
        {
            kind++;
        }

        real e = tab.emitKev[tr * 5 + kind];
        level = tab.trTo[tr];
        if (!(e > (real)0.0))
        {
            continue;
        }

        real ax, ay, az;
        Isotropic(ax, ay, az);
        st.cascadeQuanta++;
        if (kind == 0)
        {
            PushPending(x, y, z, ax, ay, az, e);
            continue;
        }

        st.cascadeElectrons++;
        real tc = CapsuleExit(e, az);
        if (tc > (real)0.0)
        {
            ForgetRay();
            deposited += CarriedElectronDeposit(x, y, z, ElectronBirth::Given, tc, ax, ay, az, material, pushAnalog);
        }
    }

    if (steps >= 64)
    {
        st.cascadeCut++;
    }

    deposited = WalkPendingFrom(limit, deposited);
    gammaDeposit = deposited - betaDeposit;
    gammaLight = lightDeposit - betaLight;
    return deposited;
}

// =====================================================================================
// Счёт: бины, каналы, свет
// =====================================================================================

// = EfficiencySimulator.cs:8395 PeakBin
RM_DEV int Sim::PeakBin(real energyKev, real binKev)
{
    return (int)(energyKev / binKev + (real)0.5);
}

// = EfficiencySimulator.cs:8434 InPeak
RM_DEV bool Sim::InPeak(real energyKev, real deposited)
{
#ifdef RM_REAL_FLOAT
    // ⚠ float: допуск 1e-9 кэВ в 10⁵ раз меньше ulp(E) при 2614 кэВ, а `deposited`
    // аналоговой ветви — сумма кусков (map_data.md §4 п. 1): полное поглощение уехало бы
    // в бин `peak − 1`. Во float допуск — не меньше 8 ulp(E); в double — как C#.
    real slack = (real)8.0 * FLT_EPSILON * M_Abs(energyKev);
    return energyKev - deposited <= (real)C.PeakHalfWidthKev + M_Max((real)1e-9, slack);
#else
    return energyKev - deposited <= C.PeakHalfWidthKev + 1e-9;
#endif
}

// = EfficiencySimulator.cs:8450 BinOf
RM_DEV int Sim::BinOf(int peak, real binKev, real energyKev, real deposited)
{
    int bin = (int)(deposited / binKev + (real)0.5);
    if (bin < 0)
    {
        bin = 0;
    }

    if (bin >= peak)
    {
        bin = InPeak(energyKev, deposited)
            ? peak
            : M_Max(0, peak - 1);
    }

    return bin;
}

// = EfficiencySimulator.cs:8468 Deposit. `histogram[...] += weight` → атомик в double
// (map_data.md §4 п. 2: float-сумма теряет единичные вклады после 2^24).
RM_DEV void Sim::Deposit(double* histogram, int histogramLength, real binKev, real energyKev,
                         real deposited, real weight)
{
    if (!(deposited > (real)0.0) || !(weight > (real)0.0))
    {
        return;
    }

    atomicAdd(histogram + BinOf(histogramLength - 1, binKev, energyKev, deposited), (double)weight);
}

// = EfficiencySimulator.cs:10337 ScoreLight
RM_DEV void Sim::ScoreLight(real binKev, real energyKev, real deposited, real weight)
{
    if (lightSum == nullptr || !(deposited > (real)0.0) || !(weight > (real)0.0))
    {
        return;
    }

    int peak = lightSumLen - 1;
    int bin = (int)(deposited / binKev + (real)0.5);
    if (bin < 0)
    {
        bin = 0;
    }

    if (bin > peak)
    {
        bin = peak;
    }

    int unified = BinOf(peak, binKev, energyKev, deposited);
    if (bin != unified)
    {
        CountLightBinSplit++;
        WeightLightBinSplit += (double)weight;
        if (!C.LightBinUnified)
        {
            lightBinSplit += (double)(weight * lightDeposit);
        }
    }

    atomicAdd(lightSum + (C.LightBinUnified ? unified : bin), (double)(weight * lightDeposit));
}

// = EfficiencySimulator.cs:1456 TallyResolutionPeak
RM_DEV void Sim::TallyResolutionPeak(int peak, real binKev, real energyKev,
                                     real deposited, real weight, bool analog)
{
    // ⚠ float: допуск `+ 1e-9` ниже ulp(E) — граница окна во float решается округлением.
    if (!(C.ResolutionPeakHalfWidthKev > 0.0) || !(deposited > (real)0.0) || !(weight > (real)0.0)
        || energyKev - deposited > C.ResolutionPeakHalfWidthKev + 1e-9
        || BinOf(peak, binKev, energyKev, deposited) == peak)
    {
        return;
    }

    if (analog)
    {
        resolutionAnalog += (double)weight;
    }
    else
    {
        resolutionWeighted += (double)weight;
    }
}

// = EfficiencySimulator.cs:8243 ChannelOf(escaped). Трассировка (`TraceChannels`) не перенесена.
RM_DEV Sim::ResponseChannel Sim::ChannelOf(real escaped)
{
    ResponseChannel channel = PickChannel(escaped);
    return channel;
}

// = EfficiencySimulator.cs:8273 ChannelOf(escaped, deposited, depositedOutside)
RM_DEV Sim::ResponseChannel Sim::ChannelOf(real escaped, real deposited, real depositedOutside)
{
    ResponseChannel channel = depositedOutside > (real)0.0 && depositedOutside >= deposited - depositedOutside
        ? ResponseChannel::AnnihilationOutside
        : PickChannel(escaped);
    return channel;
}

// = EfficiencySimulator.cs:8286 PickChannel
RM_DEV Sim::ResponseChannel Sim::PickChannel(real escaped)
{
    if (!(escaped > C.PeakHalfWidthKev))
    {
        return ResponseChannel::Peak;
    }

    real rest = escaped - lossAnnihilation - lossXray;
    if (lossAnnihilation >= lossXray && lossAnnihilation >= rest)
    {
        return annihilationEscapes >= 2
            ? ResponseChannel::EscapeAnnihilationDouble
            : ResponseChannel::EscapeAnnihilation;
    }

    return lossXray >= rest ? XrayChannel() : ResponseChannel::Compton;
}

// = EfficiencySimulator.cs:8321 XrayChannel
RM_DEV Sim::ResponseChannel Sim::XrayChannel()
{
    return C.SplitXrayShells && lossXrayL > lossXrayK
        ? ResponseChannel::EscapeXrayL
        : ResponseChannel::EscapeXrayK;
}
