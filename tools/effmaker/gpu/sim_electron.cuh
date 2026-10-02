// sim_electron.cuh — определения методов `Sim` модуля «электрон» (объявления —
// decl_electron.inc). Полоса П221 (`AMBER160`), полоса Г.
//
// ПОСТРОЧНЫЙ перенос ElectronTransport.cs (partial `EfficiencySimulator`) и кусков
// EfficiencySimulator.cs вокруг заноса электрона: те же имена, тот же порядок розыгрышей
// `Uniform()` и арифметики (ступень 1 приёмки — сверка с CPU в double по историям).
// Константы приводятся к `real` явно (`(real)0.5`): в double это то же число, во float
// выражение не уходит в double молча.
//
// Чужие функции (зовутся по именам C#):
//   полоса А (tables_dev.cuh): RangeOf, EnergyOfRange (ElectronMaterialG);
//     Photons, Anchor, SampleKev, StepPhotons, SampleStepKev (ThickTargetBremG);
//   полоса Б (sim_geom.cuh): At, StepToBoundary, CrystalPath, SaveRay, RestoreRay,
//     Rotate, Isotropic;
//   полоса В: InCrystal, ElectronLoss, AddLight, NoteEscape, Poisson; поля escapeCollect,
//     escapeCount, escapeLost, escX…escE, CountEscapeDropped;
//   полоса Д: PushPending, PushTotalPending.
#pragma once
#include "sim.cuh"

// ============================================================================
// Начальное состояние нити
// ============================================================================

// = ElectronTransport.cs / EfficiencySimulator.cs — значения полей по конструктору C#
RM_DEV void Sim::InitElectron()
{
    carriedInCrystal = false;
    layerBremPush = pushNone;
    layerBremEnabled = false;
    layerBremZ = (real)0;
    for (int i = 0; i < RM_LAYER_SCATTER_MAX; i++)
    {
        layerHardShare[i] = (real)0;
    }

    CountBremPhotons = 0;
    SumBremKev = 0.0;
    CountLayerEscapes = 0; CountLayerReturns = 0; CountLayerCarries = 0;
    SumLayerReturnKev = 0.0;
    CountLayerSteps = 0; CountLayerHardCollisions = 0;
    CountLayerEscapesCarried = 0; CountLayerReturnsCarried = 0;
    CountLayerBorn = 0;
    CountLayerReturnsSameFace = 0; CountLayerReturnsOtherFace = 0; CountLayerReturnsKilled = 0;
    CountLayerBremPhotons = 0;
    SumLayerBremKev = 0.0;
    SumLayerPathG = 0.0;
}

// ============================================================================
// Доступ к данным (поля симулятора C# и словари по веществу → `D`)
// ============================================================================

// = EfficiencySimulator.cs:1568 crystal
RM_DEV const RegionG& Sim::CrystalRegion()
{
    return D.regions[D.scene->crystal];
}

// = EfficiencySimulator.cs:1647 electron (ElectronData.Match(geometry.Crystal) …, :1846)
// Зовётся только там, где C# уже проверил `electron != null` (ElectronLoss, :5643).
RM_DEV const ElectronMaterialG& Sim::ElectronMat()
{
    return D.electronMats[D.scene->electron];
}

// = EfficiencySimulator.cs:1657 bremTable (ThickTargetBrem.For(geometry.Crystal, electron, 5), :1861)
// Зовётся только при `alongPath` (ElectronLoss ставит его лишь при bremTable != null).
RM_DEV const ThickTargetBremG& Sim::BremTab()
{
    return D.brems[D.scene->bremTable];
}

// = ElectronTransport.cs:817 this.geometry.Crystal.Density (SceneG.crystalMaterial полосы Б)
RM_DEV real Sim::CrystalDensity()
{
    return D.materials[D.scene->crystalMaterial].Density;
}

// = ElectronTransport.cs:156 CrystalRadiationLength
// Кэш C# (`crystalRadiationLength`, счёт по Цаю из `geometry.Crystal.Fractions`) снят
// упаковщиком вызовом самого C#-метода: SceneG.crystalRadiationLength (полоса Б).
RM_DEV real Sim::CrystalRadiationLength()
{
    return D.scene->crystalRadiationLength;
}

// = ElectronTransport.cs:193 LayerRadiationLength (кэш layerRadiationCache → поле вещества)
RM_DEV real Sim::LayerRadiationLength(int material)
{
    return D.materials[material].LayerRadiationLength;
}

// = ElectronTransport.cs:300 LayerScatterElements (кэш layerScatterCache → начало и длина
// отрезка `D.scatterElements`; порядок элементов — порядок `material.Fractions`, как в C#)
RM_DEV int Sim::LayerScatterElements(int material, int& count)
{
    const MaterialG& m = D.materials[material];
    count = m.LayerScatterElementsLen;
    return m.LayerScatterElements;
}

// = ElectronTransport.cs:718 LayerBremZ (кэш layerBremZCache → поле вещества)
RM_DEV real Sim::LayerBremZ(int material)
{
    return D.materials[material].LayerBremZ;
}

// = EfficiencySimulator.cs:7691 CarryMedium (кэш carryCache → индекс в D.electronMats;
// выбор Match / ForComposition / вода под `ElectronAnyMaterial` сделал C# при упаковке)
RM_DEV int Sim::CarryMedium(int material)
{
    return D.materials[material].CarryMedium;
}

// = EfficiencySimulator.cs:7716 LayerBrem (кэш layerBremCache → индекс в D.brems, −1 = null)
RM_DEV int Sim::LayerBrem(int material)
{
    return D.materials[material].LayerBrem;
}

// = EfficiencySimulator.cs:7683 WaterTable (только абляция `ElectronWalkToCrystal`)
RM_DEV int Sim::WaterTable()
{
    return D.scene->waterTable;
}

// Вызов делегата C# `push(x, y, z, ux, uy, uz, e)` по номеру очереди (decl: PushQueue).
RM_DEV void Sim::PushByQueue(int queue, real x, real y, real z, real ux, real uy, real uz, real energyKev)
{
    if (queue == pushAnalog)
    {
        PushPending(x, y, z, ux, uy, uz, energyKev);
    }
    else if (queue == pushTotal)
    {
        PushTotalPending(x, y, z, ux, uy, uz, energyKev);
    }
}

// ============================================================================
// ElectronTransport.cs — смешанная схема упругого рассеяния в слоях (`M13`)
// ============================================================================

// = ElectronTransport.cs:337 LayerHardCutoffMu
RM_DEV real Sim::LayerHardCutoffMu()
{
    real deg = (real)C.LayerHardCutoffDeg;
    if (!(deg > (real)0) || deg >= (real)180)
    {
        deg = (real)20;
    }

    return (real)1 - M_Cos(deg * M_PI_R / (real)180);
}

// = ElectronTransport.cs:353 ScreeningTwoA
RM_DEV real Sim::ScreeningTwoA(int z, real z13, real beta2, real betaGamma)
{
    real chi = FineStructure * z13 / ((real)1.77 * betaGamma);   // ħ/(2 p a)
    real az = FineStructure * z;
    return (real)2 * chi * chi * ((real)1.13 + (real)3.76 * az * az / beta2);
}

// = ElectronTransport.cs:371 LayerElasticStep
RM_DEV void Sim::LayerElasticStep(const ScatterElementG* elements, int elementsLen, real tKev, real muCut,
                                  real& hardPerCm, real& softTransportPerCm, real* hardShare)
{
    real gamma = (real)1 + tKev / ElectronMassKev;
    // ⚠ float: β² = 1 − 1/γ² (map_data §4 п.10) — у 1 кэВ β² ≈ 0.004, отн. ошибка ~3e-5;
    // устойчиво τ(τ+2)/(τ+1)². Здесь — как в C# (ступень 1).
    real beta2 = (real)1 - (real)1 / (gamma * gamma);
    real beta = M_Sqrt(beta2);
    real p2 = tKev * (tKev + (real)2 * ElectronMassKev);                  // (кэВ/c)²
    real common = (real)2 * M_PI_R * ClassicalRadiusCm * ClassicalRadiusCm
                  * ElectronMassKev * ElectronMassKev / (beta2 * p2);       // см²
    real hard = (real)0, trSoft = (real)0;
    for (int i = 0; i < elementsLen; i++)
    {
        const ScatterElementG& e = elements[i];
        real a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
        real pref = e.AtomsPerCm3 * common * e.ZZ1;                         // 1/см
        real majorant = e.Mott ? (real)1 + M_PI_R * FineStructure * e.Z * beta / (real)4 : (real)1;
        real h = pref * ((real)1 / (muCut + a2) - (real)1 / ((real)2 + a2)) * majorant;
        hardShare[i] = h;
        hard += h;

        // Транспортное сечение НИЖЕ отсечки, 1/см:
        // ∫₀^X x dx/(x + 2A)² = ln((X + 2A)/2A) + 2A/(X + 2A) − 1.
        // ⚠ float: ln(...) + 2A/(X+2A) − 1 при X ≫ 2A гасит единицу — годно; при X ~ 2A
        // (очень мягкий электрон) вычитание почти равных.
        trSoft += pref * (M_Log((muCut + a2) / a2) + a2 / (muCut + a2) - (real)1);
    }

    hardPerCm = hard;
    softTransportPerCm = trSoft > (real)0 ? trSoft : (real)0;
}

// = ElectronTransport.cs:407 LayerHardCollision
RM_DEV bool Sim::LayerHardCollision(const ScatterElementG* elements, int elementsLen, const real* hardShare,
                                    real hardPerCm, real tKev, real muCut, real& ux, real& uy, real& uz)
{
    real pick = Uniform() * hardPerCm;
    int i = 0;
    for (; i < elementsLen - 1; i++)
    {
        pick -= hardShare[i];
        if (pick <= (real)0)
        {
            break;
        }
    }

    const ScatterElementG& e = elements[i];
    real gamma = (real)1 + tKev / ElectronMassKev;
    real beta2 = (real)1 - (real)1 / (gamma * gamma);    // ⚠ float: map_data §4 п.10
    real beta = M_Sqrt(beta2);
    real a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
    real lo = (real)1 / (muCut + a2), hi = (real)1 / ((real)2 + a2);
    real mu = (real)1 / (lo - Uniform() * (lo - hi)) - a2;
    if (mu < muCut)
    {
        mu = muCut;
    }
    else if (mu > (real)2)
    {
        mu = (real)2;
    }

    if (e.Mott)
    {
        // Мак-Кинли—Фешбах для электрона: R = 1 − β² s² + παZβ s(1 − s),
        // s = sin(θ/2) = √(μ/2); R ≤ 1 + παZβ/4.
        real s = M_Sqrt((real)0.5 * mu);
        real paz = M_PI_R * FineStructure * e.Z * beta;
        real r = (real)1 - beta2 * s * s + paz * s * ((real)1 - s);
        if (Uniform() * ((real)1 + (real)0.25 * paz) > r)
        {
            return false;
        }
    }

    // ⚠ float: поворот на cos = 1 − μ (map_data §4 п.9) — здесь μ ≥ μ_c (20° → 0.06),
    // потери малых углов нет; оставлено как в C#.
    Rotate(ux, uy, uz, (real)1 - mu);
    return true;
}

// ============================================================================
// ElectronTransport.cs — ранний выход, углы
// ============================================================================

// = ElectronTransport.cs:482 CrystalNearestFace
RM_DEV real Sim::CrystalNearestFace(real x, real y, real z)
{
    const RegionG& c = CrystalRegion();
    real d = M_Min(z - c.ZMin, c.ZMax - z);
    if (c.IsBox)
    {
        d = M_Min(d, M_Min(c.AX - M_Abs(x), c.AY - M_Abs(y)));
    }
    else
    {
        d = M_Min(d, c.ROut - M_Sqrt(x * x + y * y));
    }

    return d > (real)0 ? d : (real)0;
}

// = ElectronTransport.cs:514 RegionNearestFace
// `Region r` → номер области; `regionArray` → D.regions (тот же порядок — приоритет `At`).
RM_DEV real Sim::RegionNearestFace(int r, real x, real y, real z)
{
    const RegionG& rr = D.regions[r];
    real d = M_Min(z - rr.ZMin, rr.ZMax - z);
    real rad = M_Sqrt(x * x + y * y);
    if (rr.IsBox)
    {
        d = M_Min(d, M_Min(rr.AX - M_Abs(x), rr.AY - M_Abs(y)));
    }
    else
    {
        d = M_Min(d, rr.ROut - rad);
        if (rr.RIn > (real)0)
        {
            d = M_Min(d, rad - rr.RIn);
        }
    }

    // Дыры: области, стоящие в списке раньше этой.
    for (int i = 0; i < D.nRegions && d > (real)0; i++)
    {
        if (i == r)
        {
            break;
        }

        const RegionG& h = D.regions[i];
        real dz = M_Max((real)0, M_Max(h.ZMin - z, z - h.ZMax));
        real dxy;
        if (h.IsBox)
        {
            real dx = M_Max((real)0, M_Abs(x) - h.AX);
            real dy = M_Max((real)0, M_Abs(y) - h.AY);
            dxy = M_Sqrt(dx * dx + dy * dy);
        }
        else
        {
            dxy = M_Max((real)0, M_Max(rad - h.ROut, h.RIn - rad));
        }

        d = M_Min(d, M_Sqrt(dxy * dxy + dz * dz));
    }

    return d > (real)0 ? d : (real)0;
}

// = ElectronTransport.cs:567 HighlandTheta0
RM_DEV real Sim::HighlandTheta0(real tKev, real xOverX0)
{
    real gamma = (real)1 + tKev / ElectronMassKev;
    real beta2 = (real)1 - (real)1 / (gamma * gamma);    // ⚠ float: map_data §4 п.10
    real p = M_Sqrt(tKev * (tKev + (real)2 * ElectronMassKev));   // кэВ/c
    real betaP = M_Sqrt(beta2) * p;                               // кэВ
    real bracket = (real)1 + (real)0.038 * M_Log(xOverX0 / beta2);
    if (bracket < (real)0.25)
    {
        bracket = (real)0.25;
    }

    return (real)13600 / betaP * M_Sqrt(xOverX0) * bracket;
}

// = ElectronTransport.cs:589 SampleHingeMu
RM_DEV real Sim::SampleHingeMu(real theta0)
{
    real s = theta0 * theta0;
    if (s > (real)50)
    {
        return (real)2 * Uniform();
    }

    real r = Uniform();
    // ⚠ float: −s·log(1 − r·(1 − exp(−2/s))) (map_data §4 п.11): при малом s (θ₀ ≲ 3e-4)
    // exp(−2/s) = 0 — годно; при r·(…) ≪ 1 log(1 − ·) теряет разряды → −s·log1p(−r·(−expm1(−2/s))).
    real mu = -s * M_Log((real)1 - r * ((real)1 - M_Exp((real)-2 / s)));
    return mu > (real)2 ? (real)2 : (mu < (real)0 ? (real)0 : mu);
}

// = ElectronTransport.cs:607 SauterCosine
RM_DEV real Sim::SauterCosine(real tKev)
{
    real tau = tKev / ElectronMassKev;
    if (tau > (real)1000)
    {
        return (real)1;
    }

    real gamma = tau + (real)1;
    real beta = M_Sqrt(tau * (tau + (real)2)) / gamma;
    // ⚠ float: a = (1 − β)/β — вычитание почти равных у больших τ; на шкале ≤ 3 МэВ
    // 1 − β ≥ 0.01, отн. ошибка ~1e-5 — годно.
    real a = ((real)1 - beta) / beta;
    real ap2 = a + (real)2;
    real b = (real)0.5 * beta * gamma * (gamma - (real)1) * (gamma - (real)2);
    real grej = (real)2 * ((real)1 + a * b) / a;
    real z, g;
    int guard = 0;
    do
    {
        real q = Uniform();
        z = (real)2 * a * ((real)2 * q + ap2 * M_Sqrt(q)) / (ap2 * ap2 - (real)4 * q);
        g = ((real)2 - z) * ((real)1 / (a + z) + b);
    }
    while (g < Uniform() * grej && ++guard < 1000);

    return (real)1 - z;
}

// = ElectronTransport.cs:639 TsaiCosine
RM_DEV real Sim::TsaiCosine(real tKev)
{
    const real A1 = (real)1.6, A2 = A1 / (real)3, Border = (real)0.25;
    real uMax = (real)2 * ((real)1 + tKev / ElectronMassKev);
    real u;
    int guard = 0;
    do
    {
        // C# вычисляет множители слева направо; порядок двух выдач в C++ не задан,
        // но произведение перестановочно побитово — число то же.
        real u1 = Uniform();
        real u2 = Uniform();
        real uu = -M_Log(u1 * u2);
        u = Border > Uniform() ? uu * A1 : uu * A2;
    }
    while (u > uMax && ++guard < 1000);

    return (real)1 - (real)2 * u * u / (uMax * uMax);
}

// = ElectronTransport.cs:672 Brem2BSCosine
RM_DEV real Sim::Brem2BSCosine(real tKev, real kKev, real z)
{
    real energy = tKev + ElectronMassKev;                  // полная энергия до
    real final_ = energy - kKev;
    if (!(final_ > ElectronMassKev) || !(z >= (real)1))
    {
        return TsaiCosine(tKev);                           // квант унёс всё — предела у формулы нет
    }

    real ratio = final_ / energy;
    real ratio1 = ((real)1 + ratio) * ((real)1 + ratio);
    real ratio2 = (real)1 + ratio * ratio;
    real gamma = energy / ElectronMassKev;
    real beta = M_Sqrt((gamma - (real)1) * (gamma + (real)1)) / gamma;
    real fz = (real)0.00008116224 * M_Pow(z, (real)1 / (real)3) * M_Pow(z + (real)1, (real)1 / (real)3);
    real yMax = (real)2 * beta * ((real)1 + beta) * gamma * gamma;
    real gMax = M_Max(Brem2BSReject((real)0, ratio, ratio1, ratio2, fz),
                      Brem2BSReject(yMax, ratio, ratio1, ratio2, fz));
    real y;
    int guard = 0;
    do
    {
        real q = Uniform();
        y = q * yMax / ((real)1 + yMax * ((real)1 - q));
    }
    while ((Uniform() * gMax > Brem2BSReject(y, ratio, ratio1, ratio2, fz) || y > yMax) && ++guard < 1000);

    return (real)1 - (real)2 * y / yMax;
}

// = ElectronTransport.cs:703 Brem2BSReject
RM_DEV real Sim::Brem2BSReject(real y, real ratio, real ratio1, real ratio2, real fz)
{
    real y2 = ((real)1 + y) * ((real)1 + y);
    real x = (real)4 * y * ratio / y2;
    return (real)4 * x - ratio1 - (ratio2 - x) * M_Log(fz / y2);
}

// = ElectronTransport.cs:753 ElectronBirthDirection
RM_DEV void Sim::ElectronBirthDirection(ElectronBirth birth, real te, real rx, real ry, real rz,
                                        real& ux, real& uy, real& uz)
{
    switch (birth)
    {
        case ElectronBirth::Photo:
            ux = rx; uy = ry; uz = rz;
            Rotate(ux, uy, uz, SauterCosine(te));
            return;
        case ElectronBirth::Pair:
            ux = rx; uy = ry; uz = rz;
            Rotate(ux, uy, uz, TsaiCosine(te));
            return;
        case ElectronBirth::Given:
        {
            real norm = M_Sqrt(rx * rx + ry * ry + rz * rz);
            if (norm > (real)1e-12)
            {
                ux = rx / norm; uy = ry / norm; uz = rz / norm;
                return;
            }

            break;
        }
        default:
            break;
    }

    Isotropic(ux, uy, uz);
}

// = ElectronTransport.cs:786 ComptonElectronDirection
RM_DEV void Sim::ComptonElectronDirection(real e, real ux0, real uy0, real uz0,
                                          real scattered, real ux1, real uy1, real uz1,
                                          real& dx, real& dy, real& dz)
{
    // ⚠ float: импульс электрона = разность почти равных у малых передач (квант почти не
    // отклонился): направление шумит; такой электрон до грани всё равно не долетит.
    dx = e * ux0 - scattered * ux1;
    dy = e * uy0 - scattered * uy1;
    dz = e * uz0 - scattered * uz1;
}

// ============================================================================
// ElectronTransport.cs — перенос по кристаллу
// ============================================================================

// = ElectronTransport.cs:813 TransportElectron
RM_DEVF real Sim::TransportElectron(real x, real y, real z, real ux, real uy, real uz,
                                    real te, bool alongPath, int depth, real& radiated, real& lost)
{
    real density = CrystalDensity();
    real x0 = CrystalRadiationLength();
    real residual = RangeOf(ElectronMat(), te);     // г/см²
    if (!(residual > (real)0) || !(density > (real)0))
    {
        return (real)0;
    }

    // Ранний выход: пробег короче расстояния до ближайшей грани —
    // погибнет внутри при любой траектории.
    if (!C.ElectronTransportNoEarlyExit
        && residual / density <= CrystalNearestFace(x, y, z))
    {
        if (alongPath)
        {
            RestBremsstrahlung(x, y, z, ux, uy, uz, te, te, depth, radiated, lost);
        }

        return (real)0;
    }

    real fraction = (real)C.ElectronStepFraction;
    if (!(fraction > (real)0) || fraction > (real)1)
    {
        fraction = (real)1;
    }

    // (`M3`) Подтяжка уровня к ESTAR — по НАЧАЛЬНОЙ энергии, одна на весь путь.
    real anchor = alongPath ? Anchor(BremTab(), te) : (real)1;

    // (`AMBER44`) Унесено через грани и осело СНАРУЖИ.
    real escaped = (real)0;

    real t = te;
    for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)
    {
        real stepG = residual * fraction;                          // г/см²
        real stepCm = stepG / density;

        // Прямой ход до шарнира.
        real first = stepCm * Uniform();
        real toEdge = CrystalPath(x, y, z, ux, uy, uz);
        if (first >= toEdge)
        {
            if (!EscapeOrReturn(x, y, z, ux, uy, uz, toEdge,
                                residual - toEdge * density, te - radiated - escaped,
                                depth, residual, t, escaped))
            {
                return escaped;
            }

            continue;               // вернулся — новый шаг от точки входа
        }

        x += ux * first;
        y += uy * first;
        z += uz * first;

        // Поворот на угол всего шага, ширина — по энергии в его середине.
        real tMid = EnergyOfRange(ElectronMat(), residual - (real)0.5 * stepG);
        if (tMid < TransportCutKev)
        {
            tMid = TransportCutKev;
        }

        if (alongPath)
        {
            // (`M3`, П44) Тормозное ШАГА — в точке шарнира, при энергии середины шага,
            // по направлению электрона ДО поворота.
            StepBremsstrahlung(x, y, z, ux, uy, uz, tMid, stepG, anchor, te, depth,
                               radiated, lost);
        }

        real theta0 = HighlandTheta0(tMid, stepG / x0);
        // ⚠ float: поворот на cos = 1 − μ (map_data §4 п.9): при μ < 6e-8 cos ≡ 1 —
        // малые углы шарнира теряются (лучше передавать μ, а не cos).
        Rotate(ux, uy, uz, (real)1 - SampleHingeMu(theta0));

        // Прямой ход до конца шага.
        real second = stepCm - first;
        toEdge = CrystalPath(x, y, z, ux, uy, uz);
        if (second >= toEdge)
        {
            if (!EscapeOrReturn(x, y, z, ux, uy, uz, toEdge,
                                residual - (first + toEdge) * density, te - radiated - escaped,
                                depth, residual, t, escaped))
            {
                return escaped;
            }

            continue;
        }

        x += ux * second;
        y += uy * second;
        z += uz * second;
        residual -= stepG;
        t = EnergyOfRange(ElectronMat(), residual);

        if (!C.ElectronTransportNoEarlyExit
            && residual / density <= CrystalNearestFace(x, y, z))
        {
            if (alongPath)
            {
                RestBremsstrahlung(x, y, z, ux, uy, uz, t, te, depth, radiated, lost);
            }

            return escaped;
        }
    }

    return escaped;
}

// = ElectronTransport.cs:948 EscapeOrReturn
RM_DEVF bool Sim::EscapeOrReturn(real& x, real& y, real& z, real& ux, real& uy, real& uz,
                                 real toEdge, real residualAtFace, real cap, int depth,
                                 real& residual, real& t, real& escaped)
{
    real tExit = EscapeEnergy(residualAtFace, cap);
    if (!C.ElectronLayerTransport || !(tExit > TransportCutKev))
    {
        escaped += tExit;
        return false;
    }

    // (`M13`, П106) Население: занесённый электрон — только сам перенос из
    // `CarriedElectronDeposit` (глубина 0 под меткой); всё прочее — своё.
    bool carried = carriedInCrystal && depth == 0;
    if (carried ? !C.LayerReturnCarried : !C.LayerReturnOwn)
    {
        escaped += tExit;
        return false;
    }

    // ⚠ float: сдвиг 1e-7 см меньше ulp координаты ≳ 1 см (ulp(4 см) = 4.8e-7) —
    // точка НЕ сдвигается с грани, и `At` может отдать её кристаллу (возврат на месте).
    real advance = toEdge + RM_NUDGE;
    x += ux * advance;
    y += uy * advance;
    z += uz * advance;
    // (П106) Грань выхода своего электрона — для счётчиков и рычага `LayerReturnKill`.
    int exitFace = carried ? 0 : CrystalFaceId(x, y, z);

    // ⛔ КЭШ ЛУЧА — СНИМОК И ВОЗВРАТ (П94 §7.1): кэш кванта прячется на время
    // переноса электрона и возвращается тем же.
    SaveRay();
    CountLayerEscapes++;
    if (carried)
    {
        CountLayerEscapesCarried++;
    }

    bool bremAllowed = carried ? C.LayerBornBremsstrahlung : C.LayerExitBremsstrahlung;
    real tOut = tExit - (bremAllowed && !C.ElectronLayerBremAlongPath ? LayerBremsstrahlung(x, y, z, tExit) : (real)0);
    real tBack = tOut;
    layerBremPush = pushNone;
    layerBremEnabled = bremAllowed;
    bool back = TransportInLayers(x, y, z, ux, uy, uz, tBack, depth);
    RestoreRay();
    if (!back)
    {
        escaped += tExit;
        return false;
    }

    // (П106) Возврат своего электрона: грань входа против грани выхода и рычаг `LayerReturnKill`.
    if (!carried)
    {
        int entryFace = CrystalFaceId(x, y, z);
        bool same = entryFace == exitFace;
        if (same)
        {
            CountLayerReturnsSameFace++;
        }
        else
        {
            CountLayerReturnsOtherFace++;
        }

        if (C.LayerReturnKill == 1 || (C.LayerReturnKill == 2 && same) || (C.LayerReturnKill == 3 && !same))
        {
            CountLayerReturnsKilled++;
            escaped += tExit;
            return false;
        }
    }
    else
    {
        CountLayerReturnsCarried++;
    }

    // Вернулся: осело снаружи `tExit − tBack`, дальше — перенос по кристаллу с остатком.
    CountLayerReturns++;
    SumLayerReturnKev += tBack;
    escaped += tExit - tBack;
    t = tBack;
    residual = RangeOf(ElectronMat(), t);
    return residual > (real)0;
}

// = ElectronTransport.cs:1064 CrystalFaceId
RM_DEV int Sim::CrystalFaceId(real x, real y, real z)
{
    const RegionG& c = CrystalRegion();
    int best = 1;
    real d = M_Abs(z - c.ZMin);
    real dz1 = M_Abs(c.ZMax - z);
    if (dz1 < d) { d = dz1; best = 2; }
    if (c.IsBox)
    {
        real dxm = M_Abs(x + c.AX), dxp = M_Abs(c.AX - x);
        real dym = M_Abs(y + c.AY), dyp = M_Abs(c.AY - y);
        if (dxm < d) { d = dxm; best = 3; }
        if (dxp < d) { d = dxp; best = 4; }
        if (dym < d) { d = dym; best = 5; }
        if (dyp < d) { d = dyp; best = 6; }
    }
    else
    {
        real rad = M_Sqrt(x * x + y * y);
        real dro = M_Abs(c.ROut - rad);
        if (dro < d) { d = dro; best = 3; }
        if (c.RIn > (real)0)
        {
            real dri = M_Abs(rad - c.RIn);
            if (dri < d) { d = dri; best = 4; }
        }
    }

    return best;
}

// = ElectronTransport.cs:1110 LayerBremsstrahlung
RM_DEVF real Sim::LayerBremsstrahlung(real x, real y, real z, real te)
{
    const real MinKev = (real)5;
    if (!C.ElectronAnyMaterial || !C.Bremsstrahlung || !(te > MinKev))
    {
        return (real)0;
    }

    int here = At(x, y, z);
    if (here < 0 || D.regions[here].IsCrystal || D.regions[here].Material < 0)
    {
        return (real)0;
    }

    int tableIndex = LayerBrem(D.regions[here].Material);
    if (tableIndex < 0)
    {
        return (real)0;
    }

    const ThickTargetBremG& table = D.brems[tableIndex];
    real radiated = (real)0;
    int n = Poisson(Photons(table, te));
    for (int i = 0; i < n; i++)
    {
        real k = SampleKev(table, te, Uniform());
        real ax, ay, az;
        Isotropic(ax, ay, az);
        real kUse = M_Min(k, te - radiated);
        if (!(kUse > (real)0))
        {
            continue;
        }

        radiated += kUse;
        CountBremPhotons++;
        SumBremKev += kUse;
        if (escapeCollect)
        {
            NoteEscape(x, y, z, ax, ay, az, kUse);
        }
    }

    return radiated;
}

// ============================================================================
// ElectronTransport.cs — перенос в слоях обвязки
// ============================================================================

// = ElectronTransport.cs:1201 TransportInLayers
RM_DEVF bool Sim::TransportInLayers(real& x, real& y, real& z, real& ux, real& uy, real& uz,
                                    real& t, int depth)
{
    // ⚠ Зовущий обязан спрятать кэш луча кванта (`SaveRay`) и вернуть его после
    // (`RestoreRay`) — П94 §7.1.
    (void)depth;
    real fraction = (real)C.ElectronStepFraction;
    if (!(fraction > (real)0) || fraction > (real)1)
    {
        fraction = (real)1;
    }

    // (`M13`) Смешанная схема: отсечка одна на весь перенос.
    bool mixed = C.ElectronLayerMixedScattering;
    real muCut = mixed ? LayerHardCutoffMu() : (real)0;

    // (`M13`, П106) Тормозное ПО ХОДУ переноса.
    bool lbrem = C.ElectronLayerBremAlongPath && layerBremEnabled
                 && C.ElectronAnyMaterial && C.Bremsstrahlung;
    int bremTable = -1;              // ThickTargetBrem bremTable = null
    int bremMaterial = -1;           // GeometryMaterial bremMaterial = null
    real bremAnchor = (real)1, radiated = (real)0, tEntry = t;

    for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)
    {
        int here = At(x, y, z);
        if (here >= 0 && D.regions[here].IsCrystal)
        {
            return true;
        }

        real toNext = StepToBoundary(x, y, z, ux, uy, uz);
        if (toNext >= CS_DOUBLE_MAX)
        {
            return false;           // ушёл из сцены
        }

        int hereMat = here >= 0 ? D.regions[here].Material : -1;
        real density = hereMat >= 0 ? D.materials[hereMat].Density : (real)0;
        if (!(density > (real)0))
        {
            // Пустота — по прямой до следующей границы, без потерь.
            // ⚠ float: сдвиг 1e-7 см тонет в ulp координаты (см. EscapeOrReturn).
            real through = toNext + RM_NUDGE;
            x += ux * through;
            y += uy * through;
            z += uz * through;
            continue;
        }

        const ElectronMaterialG& medium = D.electronMats[CarryMedium(hereMat)];
        real x0 = LayerRadiationLength(hereMat);
        real residual = RangeOf(medium, t);          // г/см²
        if (!(residual > (real)0))
        {
            return false;
        }

        // (П106) Смена вещества — своя таблица тормозного и якорь по энергии входа в вещество.
        if (lbrem && hereMat != bremMaterial)
        {
            bremMaterial = hereMat;
            bremTable = LayerBrem(hereMat);
            bremAnchor = bremTable >= 0 ? Anchor(D.brems[bremTable], t) : (real)1;
            // (П111) Эффективный Z вещества — для углового розыгрыша 2BS.
            layerBremZ = C.ElectronLayerBremAngular2BS ? LayerBremZ(hereMat) : (real)0;
        }

        // Ранний выход, как в кристалле.
        if (residual / density <= RegionNearestFace(here, x, y, z))
        {
            // (П106) Погибнет здесь — остаток тормозного толстой мишенью в точке гибели.
            if (lbrem && bremTable >= 0)
            {
                LayerRestBremsstrahlung(x, y, z, ux, uy, uz, t, tEntry, D.brems[bremTable], radiated);
            }

            SumLayerPathG += residual;                  // (П111) остаток пробега — весь в этом веществе
            return false;
        }

        real stepG = residual * fraction;
        real stepCm = stepG / density;

        // (`M13`) Смешанная схема: расстояние до жёсткого столкновения.
        const ScatterElementG* elements = nullptr;
        int elementsLen = 0;
        real hardPerCm = (real)0, softTransportPerCm = (real)0;
        bool hardHit = false;
        if (mixed)
        {
            CountLayerSteps++;
            int first0 = LayerScatterElements(hereMat, elementsLen);
            elements = D.scatterElements + first0;
            if (elementsLen > 0)
            {
                LayerElasticStep(elements, elementsLen, t, muCut, hardPerCm, softTransportPerCm, layerHardShare);
                if (hardPerCm > (real)0)
                {
                    real toHard = -M_Log(Uniform()) / hardPerCm;
                    if (toHard < stepCm)
                    {
                        stepCm = toHard;
                        stepG = stepCm * density;
                        hardHit = true;
                    }
                }
            }
        }

        // Прямой ход до шарнира; граница области раньше — переход.
        real first = stepCm * Uniform();
        if (first >= toNext)
        {
            // (П106) Тормозное ПРОЙДЕННОГО отрезка до границы — в точке перехода.
            if (lbrem && bremTable >= 0 && toNext > (real)0)
            {
                LayerStepBremsstrahlung(x + ux * toNext, y + uy * toNext, z + uz * toNext, ux, uy, uz,
                                        LayerMidEnergy(medium, residual, (real)0.5 * toNext * density),
                                        toNext * density, bremAnchor, tEntry, D.brems[bremTable], radiated);
            }

            real through = toNext + RM_NUDGE;     // ⚠ float: см. выше
            x += ux * through;
            y += uy * through;
            z += uz * through;
            t = EnergyOfRange(medium, residual - toNext * density);
            SumLayerPathG += toNext * density;      // (П111) счётчик пути в веществе
            continue;
        }

        x += ux * first;
        y += uy * first;
        z += uz * first;
        SumLayerPathG += first * density;               // (П111) счётчик пути в веществе

        real tMid = EnergyOfRange(medium, residual - (real)0.5 * stepG);
        if (tMid < TransportCutKev)
        {
            tMid = TransportCutKev;
        }

        // (`M13`, П106) Тормозное ПЕРВОГО ОТРЕЗКА шага — в точке шарнира.
        if (lbrem && bremTable >= 0)
        {
            LayerStepBremsstrahlung(x, y, z, ux, uy, uz,
                                    LayerMidEnergy(medium, residual, (real)0.5 * first * density),
                                    first * density, bremAnchor, tEntry, D.brems[bremTable], radiated);
        }

        // (`M13`) Под ключом ширина мягкого шарнира — 1 − exp(−s·Σnσ_tr,soft); без ключа —
        // Хайленд на весь шаг. Слой без опознанных элементов — Хайленд.
        // ⚠ float: 1 − exp(−x) при малом x (короткий шаг, лёгкое вещество) — вычитание
        // почти равных; устойчиво −expm1(−x).
        real theta0 = mixed && elements != nullptr && elementsLen > 0
            ? M_Sqrt(M_Max((real)0, (real)1 - M_Exp(-stepCm * softTransportPerCm)))
            : HighlandTheta0(tMid, stepG / x0);
        // ⚠ float: поворот на cos = 1 − μ (map_data §4 п.9).
        Rotate(ux, uy, uz, (real)1 - SampleHingeMu(theta0));

        // Прямой ход до конца шага — новым лучом.
        real second = stepCm - first;
        toNext = StepToBoundary(x, y, z, ux, uy, uz);
        if (toNext >= CS_DOUBLE_MAX)
        {
            return false;
        }

        if (second >= toNext)
        {
            // (П106) Тормозное второго отрезка, обрезанного границей, — в точке перехода.
            if (lbrem && bremTable >= 0 && toNext > (real)0)
            {
                LayerStepBremsstrahlung(x + ux * toNext, y + uy * toNext, z + uz * toNext, ux, uy, uz,
                                        LayerMidEnergy(medium, residual - first * density, (real)0.5 * toNext * density),
                                        toNext * density, bremAnchor, tEntry, D.brems[bremTable], radiated);
            }

            real through = toNext + RM_NUDGE;     // ⚠ float: см. выше
            x += ux * through;
            y += uy * through;
            z += uz * through;
            t = EnergyOfRange(medium, residual - (first + toNext) * density);
            SumLayerPathG += toNext * density;      // (П111) счётчик пути в веществе
            continue;
        }

        x += ux * second;
        y += uy * second;
        z += uz * second;
        SumLayerPathG += second * density;              // (П111) счётчик пути в веществе

        // (П106) Тормозное второго отрезка — в его конце, по направлению после шарнира.
        if (lbrem && bremTable >= 0 && second > (real)0)
        {
            LayerStepBremsstrahlung(x, y, z, ux, uy, uz,
                                    LayerMidEnergy(medium, residual - first * density, (real)0.5 * second * density),
                                    second * density, bremAnchor, tEntry, D.brems[bremTable], radiated);
        }

        residual -= stepG;
        t = EnergyOfRange(medium, residual);

        // (`M13`) Жёсткое столкновение в конце оборванного шага — при энергии его конца.
        if (hardHit && t > TransportCutKev
            && LayerHardCollision(elements, elementsLen, layerHardShare, hardPerCm, t, muCut,
                                  ux, uy, uz))
        {
            CountLayerHardCollisions++;
        }
    }

    return false;
}

// = ElectronTransport.cs:1440 LayerMidEnergy
RM_DEV real Sim::LayerMidEnergy(const ElectronMaterialG& medium, real residualG, real halfG)
{
    real t = EnergyOfRange(medium, M_Max((real)0, residualG - halfG));
    return t < TransportCutKev ? TransportCutKev : t;
}

// = ElectronTransport.cs:1456 LayerStepBremsstrahlung
RM_DEVF void Sim::LayerStepBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                          real tKev, real stepG, real anchor, real cap,
                                          const ThickTargetBremG& table, real& radiated)
{
    int n = Poisson(StepPhotons(table, tKev, stepG, anchor));
    for (int i = 0; i < n; i++)
    {
        real k = SampleStepKev(table, tKev, Uniform());
        LayerEmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, cap, radiated);
    }
}

// = ElectronTransport.cs:1474 LayerRestBremsstrahlung
RM_DEVF void Sim::LayerRestBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                          real tKev, real cap, const ThickTargetBremG& table, real& radiated)
{
    if (!(tKev > table.MinKev))
    {
        return;
    }

    int n = Poisson(Photons(table, tKev));
    for (int i = 0; i < n; i++)
    {
        real k = SampleKev(table, tKev, Uniform());
        LayerEmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, cap, radiated);
    }
}

// = ElectronTransport.cs:1498 LayerEmitBremsstrahlung
RM_DEVF void Sim::LayerEmitBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                          real k, real tKev, real cap, real& radiated)
{
    real ax = ux, ay = uy, az = uz;
    // (П111) Косинус разыгрывается ДО зажима, как и было у Цая.
    Rotate(ax, ay, az, C.ElectronLayerBremAngular2BS
                           ? Brem2BSCosine(tKev, k, layerBremZ)
                           : TsaiCosine(tKev));
    real kUse = M_Min(k, cap - radiated);
    if (!(kUse > (real)0))
    {
        return;
    }

    radiated += kUse;
    CountLayerBremPhotons++;
    SumLayerBremKev += kUse;
    if (layerBremPush != pushNone)
    {
        PushByQueue(layerBremPush, x, y, z, ax, ay, az, kUse);
    }
    else if (escapeCollect)
    {
        NoteEscape(x, y, z, ax, ay, az, kUse);
    }
}

// = ElectronTransport.cs:1536 StepBremsstrahlung
RM_DEVF void Sim::StepBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                     real tKev, real stepG, real anchor, real te, int depth,
                                     real& radiated, real& lost)
{
    int n = Poisson(StepPhotons(BremTab(), tKev, stepG, anchor));
    for (int i = 0; i < n; i++)
    {
        real k = SampleStepKev(BremTab(), tKev, Uniform());
        EmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, te, depth, radiated, lost);
    }
}

// = ElectronTransport.cs:1553 RestBremsstrahlung
RM_DEVF void Sim::RestBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                     real tKev, real te, int depth, real& radiated, real& lost)
{
    if (!(tKev > BremTab().MinKev))
    {
        return;
    }

    int n = Poisson(Photons(BremTab(), tKev));
    for (int i = 0; i < n; i++)
    {
        real k = SampleKev(BremTab(), tKev, Uniform());
        EmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, te, depth, radiated, lost);
    }
}

// = ElectronTransport.cs:1571 EmitBremsstrahlung
RM_DEVF void Sim::EmitBremsstrahlung(real x, real y, real z, real ux, real uy, real uz,
                                     real k, real tKev, real te, int depth, real& radiated, real& lost)
{
    real ax, ay, az;
    if (C.BremAlongPath >= 2)
    {
        ax = ux; ay = uy; az = uz;
        Rotate(ax, ay, az, TsaiCosine(tKev));
    }
    else
    {
        Isotropic(ax, ay, az);
    }

    real kUse = M_Min(k, te - radiated);
    if (!(kUse > (real)0))
    {
        return;
    }

    radiated += kUse;
    CountBremPhotons++;
    SumBremKev += kUse;
    lost += InCrystal(x, y, z, ax, ay, az, kUse, depth + 1);   // рекурсия (полоса В)
}

// = ElectronTransport.cs:1599 EscapeEnergy
RM_DEV real Sim::EscapeEnergy(real residualG, real cap)
{
    if (!(residualG > (real)0))
    {
        return (real)0;
    }

    real t = EnergyOfRange(ElectronMat(), residualG);
    return t < cap ? (t > (real)0 ? t : (real)0) : (cap > (real)0 ? cap : (real)0);
}

// ============================================================================
// EfficiencySimulator.cs — занос электрона, рождённого вне кристалла
// ============================================================================

// = EfficiencySimulator.cs:7472 ElectronReachesCrystal (ветка БЕЗ ключа `ElectronLayerTransport`)
// абляция: при умолчании (`eltr=1`) обходы зовут CarriedElectronReaches.
RM_DEVF bool Sim::ElectronReachesCrystal(real x, real y, real z, real ux, real uy, real uz, real energyKev)
{
    real used;
    return ElectronWalkToCrystal(x, y, z, ux, uy, uz, energyKev, used);
}

// = EfficiencySimulator.cs:7489 ElectronCarryDeposit
// абляция: ветка БЕЗ ключа `ElectronLayerTransport` (при умолчании — CarriedElectronDeposit).
RM_DEVF bool Sim::ElectronCarryDeposit(real x, real y, real z, real ux, real uy, real uz, real energyKev,
                                       real& depositKev)
{
    depositKev = (real)0;
    real used;
    if (!ElectronWalkToCrystal(x, y, z, ux, uy, uz, energyKev, used))
    {
        return false;
    }

    real left = M_Max((real)0, (real)1 - used);
    depositKev = D.scene->electron >= 0
        ? EnergyOfRange(ElectronMat(), left * RangeOf(ElectronMat(), energyKev))
        : energyKev * left;
    if (!(depositKev > (real)0))
    {
        return false;
    }

    AddLight(depositKev, depositKev);
    return true;
}

// = EfficiencySimulator.cs:7532 CarriedElectronEnters
RM_DEVF bool Sim::CarriedElectronEnters(real x, real y, real z, ElectronBirth birth, real te,
                                        real rx, real ry, real rz, int material, int push,
                                        real& xIn, real& yIn, real& zIn,
                                        real& uxIn, real& uyIn, real& uzIn, real& tIn)
{
    xIn = x; yIn = y; zIn = z;
    tIn = te - OutsideBremsstrahlung(x, y, z, te, material, push);
    // Рычаг абляции `--detour=0` (П92) значит «заноса нет» в обоих режимах.
    if (!(tIn >= (real)20) || !(C.ElectronCarryDetour > 0.0))
    {
        uxIn = rx; uyIn = ry; uzIn = rz;
        return false;               // пробег меньше ~10 мкм — не долетит
    }

    ElectronBirthDirection(birth, tIn, rx, ry, rz, uxIn, uyIn, uzIn);
    // Кэш луча кванта — спрятать на время переноса электрона и вернуть (П94 §7.1).
    SaveRay();
    // (П106) Тормозное по ходу переноса — в очередь обхода.
    layerBremPush = push;
    layerBremEnabled = C.LayerBornBremsstrahlung;
    CountLayerBorn++;
    bool entered = TransportInLayers(xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn, 0);
    layerBremPush = pushNone;
    RestoreRay();
    if (!entered)
    {
        return false;
    }

    CountLayerCarries++;
    return true;
}

// = EfficiencySimulator.cs:7577 CarriedElectronReaches
RM_DEVF bool Sim::CarriedElectronReaches(real x, real y, real z, ElectronBirth birth, real te,
                                         real rx, real ry, real rz, int material, int push)
{
    real xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn;
    return CarriedElectronEnters(x, y, z, birth, te, rx, ry, rz, material, push,
                                 xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn)
           && tIn > TransportCutKev;
}

// = EfficiencySimulator.cs:7599 CarriedElectronDeposit
RM_DEVF real Sim::CarriedElectronDeposit(real x, real y, real z, ElectronBirth birth, real te,
                                         real rx, real ry, real rz, int material, int push)
{
    real xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn;
    if (!CarriedElectronEnters(x, y, z, birth, te, rx, ry, rz, material, push,
                               xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn)
        || !(tIn > TransportCutKev))
    {
        return (real)0;
    }

    escapeCount = 0;
    escapeLost = 0;
    escapeCollect = true;
    // (П106) Метка населения для `EscapeOrReturn`: перенос занесённого.
    carriedInCrystal = true;
    real lost = ElectronLoss(xIn, yIn, zIn, tIn, 0, ElectronBirth::Given, uxIn, uyIn, uzIn);
    carriedInCrystal = false;
    escapeCollect = false;
    for (int k = 0; k < escapeCount; k++)
    {
        PushByQueue(push, escX[k], escY[k], escZ[k],
                    escUx[k], escUy[k], escUz[k], escE[k]);
    }

    CountEscapeDropped += escapeLost;
    real deposit = tIn - lost;
    return deposit > (real)0 ? deposit : (real)0;
}

// = EfficiencySimulator.cs:7630 ElectronWalkToCrystal
// абляция: обход по прямой без ключа `ElectronLayerTransport`.
RM_DEVF bool Sim::ElectronWalkToCrystal(real x, real y, real z, real ux, real uy, real uz, real energyKev,
                                        real& usedFraction)
{
    usedFraction = (real)0;
    if (energyKev < (real)20)
    {
        return false;               // пробег меньше ~10 мкм — не долетит
    }

    real used = (real)0;            // израсходованная доля пробега
    for (int guard = 0; guard < 60; guard++)
    {
        int here = At(x, y, z);
        if (here >= 0 && D.regions[here].IsCrystal)
        {
            usedFraction = used;
            return true;
        }

        real step = StepToBoundary(x, y, z, ux, uy, uz);
        if (step >= CS_DOUBLE_MAX)
        {
            return false;           // ушёл из сцены
        }

        // C#: `here.Material.Density` без проверки на null (у области вещество есть всегда).
        real density = here >= 0 ? D.materials[D.regions[here].Material].Density : AirDensity;
        const ElectronMaterialG& medium = D.electronMats[here >= 0
            ? CarryMedium(D.regions[here].Material) : WaterTable()];
        real range = RangeOf(medium, energyKev)
                     * (real)C.ElectronCarryDetour;
        used += step * density / M_Max(range, (real)1e-12);
        if (used >= (real)1)
        {
            return false;           // пробег кончился в слое
        }

        real advance = step + RM_NUDGE;   // ⚠ float: см. EscapeOrReturn
        x += ux * advance;
        y += uy * advance;
        z += uz * advance;
    }

    return false;
}

// = EfficiencySimulator.cs:7743 OutsideBremsstrahlung
RM_DEVF real Sim::OutsideBremsstrahlung(real x, real y, real z, real te, int material, int push)
{
    const real MinKev = (real)5;
    // (П106) `LayerBornBremsstrahlung` — рычаг замера, умолчанием ВКЛ.
    if (!C.ElectronAnyMaterial || !C.Bremsstrahlung || !C.LayerBornBremsstrahlung
        || !(te > MinKev) || material < 0)
    {
        return (real)0;
    }

    // (`M13`, П106) Под ключом `ElectronLayerBremAlongPath` электрон, которого перенос в
    // слоях ПОВЕДЁТ, излучает по ходу переноса — толстая мишень здесь не разыгрывается.
    if (C.ElectronLayerBremAlongPath && C.ElectronLayerTransport
        && C.ElectronCarryDetour > 0.0 && te >= (real)20)
    {
        return (real)0;
    }

    int tableIndex = LayerBrem(material);
    if (tableIndex < 0)
    {
        return (real)0;
    }

    const ThickTargetBremG& table = D.brems[tableIndex];
    real radiated = (real)0;
    int n = Poisson(Photons(table, te));
    for (int i = 0; i < n; i++)
    {
        real k = SampleKev(table, te, Uniform());
        real ax, ay, az;
        Isotropic(ax, ay, az);
        real kUse = M_Min(k, te - radiated);
        if (!(kUse > (real)0))
        {
            continue;
        }

        radiated += kUse;
        PushByQueue(push, x, y, z, ax, ay, az, kUse);
    }

    return radiated;
}
