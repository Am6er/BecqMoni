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
    for (int k = 0; k < PendMax; k++)
    {
        pendFromOutsideAnnihilation[k] = false;
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
RM_DEVF real Sim::OneHistory(real energyKev, real x, real y, real z,
                             double* histogram, int histogramLength, real binKev, real pointWeight)
{
    ForgetRay();          // (`A315`, решение Amber 02.10.2026) кэш луча не переживает историю
    RM_PHASE_BEGIN(1)
    const SceneG& sc = *D.scene;
    {
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
        real score = (real)0.0;
        bool reached = ToCrystal(px, py, pz, ux, uy, uz, energyKev, tau);
        RM_PHASE_END(1)
        // ⛔ (`AMBER161`, П227) ОДИН вызов ScatteredRun на обе ветви. В C# (:9180, :9255) он
        // стоит дважды — для промаха до кристалла и после переноса попавшего луча, — и на
        // GPU варп исполнял его ДВАЖДЫ по очереди: сначала нити-промахи (57 % историй), потом
        // попавшие. Замер частей (RM_PHASES, 657 кэВ): ScatteredRun — 80 % тактов нитей.
        // Здесь ветви лишь выбирают свой τ, а сам вызов — общий, и варп входит в него
        // целиком. Порядок розыгрышей ИСТОРИИ прежний (промах: только рассеяние;
        // попадание: кристалл, затем рассеяние), сложение в `score` — в том же порядке.
        bool scatter = false;
        real tauScatter = (real)0.0;
        if (!reached && !C.ScoreEntranceOnly && C.SingleScatter)
        {
            scatter = true;
            tauScatter = KillDepthToExit(x, y, z, ux, uy, uz, energyKev);
        }

        if (reached)
        {
            if (C.ScoreEntranceOnly)
            {
                score = weight * M_Exp(-tau);
            }
            else
            {
                lossAnnihilation = (real)0.0;
                annihilationEscapes = 0;
                lossXray = (real)0.0;
                lossXrayK = (real)0.0;
                lossXrayL = (real)0.0;
                lightDeposit = (real)0.0;
                // не перенесено: ResetTrace() — трассировка каналов.
                RM_PHASE_BEGIN(2)
                real escaped = InCrystal(px, py, pz, ux, uy, uz, energyKev, 0);
                RM_PHASE_END(2)
                // ⚠ float: `energyKev − escaped` — недобор прямого попадания; правило пика
                // при допуске — InPeak (map_data.md §4 п. 1).
                if (InPeak(energyKev, energyKev - escaped))
                {
                    score = weight * M_Exp(-tau);
                }

                real share = weight * M_Exp(-tau);
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
            }

            if (!C.ScoreEntranceOnly)
            {
                scatter = true;
                tauScatter = tau;
            }
        }

        if (scatter)
        {
            RM_PHASE_BEGIN(3)
            score += ScatteredRun(histogram, histogramLength, binKev, x, y, z, ux, uy, uz,
                                  energyKev, tauScatter, weight);
            RM_PHASE_END(3)
        }

        return score;
    }
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
RM_DEVF real Sim::ScatteredRun(double* histogram, int histogramLength, real binKev,
                               real x, real y, real z, real ux, real uy, real uz,
                               real energyKev, real tauKill, real weight)
{
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.

    real sw, scattered, sEscaped;
    if (!ScatteredContribution(x, y, z, ux, uy, uz, energyKev, tauKill, sw, scattered, sEscaped))
    {
        return (real)0.0;
    }

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
RM_DEVF bool Sim::ScatteredContribution(real x, real y, real z, real ux, real uy, real uz,
                                        real energyKev, real tauKill,
                                        real& weight, real& scatteredEnergy, real& escapedEnergy)
{
    weight = (real)0.0;
    scatteredEnergy = (real)0.0;
    escapedEnergy = (real)0.0;
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
                RM_PHASE_BEGIN(7)
                escapedEnergy = InCrystal(px, py, pz, sx, sy, sz, scattered, 0);
                RM_PHASE_END(7)
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
    ForgetRay();          // (`A315`, решение Amber 02.10.2026) кэш луча не переживает историю
    const SceneG& sc = *D.scene;
    real limit = PathLimit(x, y, z);
    real ux, uy, uz;

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

    if (outsideOnly && inWeightedCone)
    {
        depositedOutside = (real)0.0;
        comptonOutside = false;
        return (real)0.0;
    }

    return AnalogTransport(x, y, z, ux, uy, uz, energyKev, limit,
                           depositedOutside, comptonOutside);
}

// = EfficiencySimulator.cs:9741 AnalogTransport
RM_DEVF real Sim::AnalogTransport(real x, real y, real z, real ux, real uy, real uz,
                                  real energyKev, real limit,
                                  real& depositedOutside, bool& comptonOutside)
{
    lossAnnihilation = (real)0.0;
    annihilationEscapes = 0;
    lossXray = (real)0.0;
    lossXrayK = (real)0.0;
    lossXrayL = (real)0.0;
    lightDeposit = (real)0.0;
    // не перенесено: ResetTrace() — трассировка каналов.

    real e = energyKev;
    // ⚠ float: `deposited` — СУММА кусков заноса; во float (E − E′) + E′ ≠ E на ulp(E),
    // правило пика при допуске — InPeak (map_data.md §4 п. 1).
    real deposited = (real)0.0;
    depositedOutside = (real)0.0;
    fromOutsideAnnihilation = false;
    real travelled = (real)0.0;
    comptonOutside = false;

    // Очередь квантов истории (`A52`/`A55`).
    pendCount = 0;
    while (true)
    {
        for (int guard = 0; guard < 400 && e > (real)1.0; guard++)
        {
            int here = At(x, y, z);
            if (here >= 0 && D.regions[here].IsCrystal)
            {
                escapeCount = 0;
                escapeLost = 0;
                escapeCollect = true;
                real escaped = InCrystal(x, y, z, ux, uy, uz, e, 0);
                escapeCollect = false;
                // ⚠ float: порог 1e-9 кэВ ниже ulp(e) — «вклад был» решает точное равенство
                // escaped == e у пролёта насквозь.
                if (e - escaped > (real)1e-9)
                {
                    deposited += e - escaped;
                    if (fromOutsideAnnihilation)
                    {
                        depositedOutside += e - escaped;
                    }

                    // Возврат из обвязки (`A55`): вылетевшее — в очередь истории.
                    for (int k = 0; k < escapeCount; k++)
                    {
                        PushPending(escX[k], escY[k], escZ[k],
                                    escUx[k], escUy[k], escUz[k],
                                    escE[k]);
                    }

                    CountEscapeDropped += escapeLost;
                    break;
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
                break;              // ушёл из сцены
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
                                if (fromOutsideAnnihilation)
                                {
                                    depositedOutside += carried;
                                }
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
                            if (fromOutsideAnnihilation)
                            {
                                depositedOutside += carried;
                            }
                        }

                        break;
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
                        if (fromOutsideAnnihilation)
                        {
                            depositedOutside += gain;
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
                        if (fromOutsideAnnihilation)
                        {
                            depositedOutside += carried;
                        }
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
        }

        // Следующий отложенный квант истории (`A52`/`A55`): свой путь и предел заново,
        // `deposited` истории общий.
        if (pendCount > 0)
        {
            int k = --pendCount;
            x = pendX[k];
            y = pendY[k];
            z = pendZ[k];
            ux = pendUx[k];
            uy = pendUy[k];
            uz = pendUz[k];
            e = pendE[k];
            fromOutsideAnnihilation = pendFromOutsideAnnihilation[k];
            travelled = (real)0.0;
            continue;
        }

        break;
    }

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
