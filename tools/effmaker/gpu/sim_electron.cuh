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
        layerHardShareEnd[i] = (real)0;
    }

    layerShareWrites = 0;
    CountMajorantEmpty = 0; CountShareRecomputed = 0; CountLayerStepLimited = 0;

    CountBremPhotons = 0;
    SumBremKev = 0.0;
    CountLayerEscapes = 0; CountLayerReturns = 0; CountLayerCarries = 0;
    CountLayerLeftScene = 0;
    layersLeftScene = false;
    SumLayerReturnKev = 0.0;
    CountLayerSteps = 0; CountLayerHardCollisions = 0;
    CountCrystalSteps = 0; CountCrystalHardCollisions = 0; CountTransportStepCap = 0;
    CountMottRejected = 0; CountMottMajorantExceeded = 0; CountMottTriesCap = 0;
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

// = ElectronTransport.cs MottTerms (`AMBER274`, физика 29, `phys29b`): c_j(β) = Σ_k b_jk (β − β̄)^k,
// схема Горнера — тот же порядок действий, что в C#.
RM_DEV void Sim::MottTerms(const real* b, real beta, real& c0, real& c1, real& c2, real& c3, real& c4)
{
    real d = beta - (real)MottBetaShift;
    c0 = b[0] + d * (b[1] + d * (b[2] + d * (b[3] + d * (b[4] + d * b[5]))));
    c1 = b[6] + d * (b[7] + d * (b[8] + d * (b[9] + d * (b[10] + d * b[11]))));
    c2 = b[12] + d * (b[13] + d * (b[14] + d * (b[15] + d * (b[16] + d * b[17]))));
    c3 = b[18] + d * (b[19] + d * (b[20] + d * (b[21] + d * (b[22] + d * b[23]))));
    c4 = b[24] + d * (b[25] + d * (b[26] + d * (b[27] + d * (b[28] + d * b[29]))));
}

// = ElectronTransport.cs MottMoments: жёсткое Σ_j c_j ∫_{μc}^{2} μ^{j/2} dμ/(μ+a)² и мягкое
// транспортное Σ_j c_j ∫₀^{μc} μ·μ^{j/2} dμ/(μ+a)², замкнутые формы.
// ⚠ float: при a ≫ μc (электрон ниже ~5 кэВ) мягкие формы вычитают близкие числа.
RM_DEV void Sim::MottMoments(real c0, real c1, real c2, real c3, real c4, real a, real muCut, real xCut,
                             real& hard, real& softTransport)
{
#ifdef RM_REAL_FLOAT
    // ⛔ (`AMBER274`, физика 30, Н4 критика кода) float: при a ≳ μ_c (электрон ниже ~30 кэВ) мягкие формы
    // вычитают близкие числа — у 1–3 кэВ от сечения оставался мусор (Z 71…83: ×(−600)…×1000). Там —
    // те же формы в double (сборка `rmgpu.dll` — всегда double, ступень 1 не задета).
    if (a > (real)0.05)
    {
        double hd, sd;
        MottMomentsD(c0, c1, c2, c3, c4, a, muCut, xCut, hd, sd);
        hard = (real)hd;
        softTransport = (real)sd;
        return;
    }
#endif
    const real Root2 = (real)1.4142135623730951;
    real s = M_Sqrt(a);
    real inXa = (real)1 / (muCut + a), in2a = (real)1 / ((real)2 + a);

    real h0 = inXa - in2a;
    real lh = M_Log(((real)2 + a) * inXa);
    real h2 = lh - a * h0;
    real h4 = ((real)2 - muCut) - (real)2 * a * lh + a * a * h0;
    real dAtan = M_Atan((Root2 - xCut) * s / (a + Root2 * xCut));
    real h1 = dAtan / s - Root2 * in2a + xCut * inXa;
    real h3 = (real)2 * (Root2 - xCut) - (real)3 * s * dAtan + a * (Root2 * in2a - xCut * inXa);
    hard = c0 * h0 + c1 * h1 + c2 * h2 + c3 * h3 + c4 * h4;

    real ls = M_Log((muCut + a) / a);
    real e = M_Atan(xCut / s);
    real s0 = ls + a * inXa - (real)1;
    real s1 = (real)2 * xCut - (real)3 * s * e + a * xCut * inXa;
    real s2 = muCut - (real)2 * a * ls + a * muCut * inXa;
    real s3 = ((real)2 / (real)3) * xCut * muCut - (real)4 * a * xCut + (real)5 * a * s * e - a * a * xCut * inXa;
    real s4 = (real)0.5 * muCut * muCut - (real)2 * a * muCut + (real)3 * a * a * ls - a * a * muCut * inXa;
    softTransport = c0 * s0 + c1 * s1 + c2 * s2 + c3 * s3 + c4 * s4;
}

// (`AMBER274`, физика 30, Н4) Те же формы, что MottMoments, в double — для float-сборки при a ≳ μ_c.
RM_DEV void Sim::MottMomentsD(double c0, double c1, double c2, double c3, double c4, double a, double muCut,
                              double xCut, double& hard, double& softTransport)
{
    const double Root2 = 1.4142135623730951;
    double s = sqrt(a);
    double inXa = 1.0 / (muCut + a), in2a = 1.0 / (2.0 + a);
    double h0 = inXa - in2a;
    double lh = log((2.0 + a) * inXa);
    double h2 = lh - a * h0;
    double h4 = (2.0 - muCut) - 2.0 * a * lh + a * a * h0;
    double dAtan = atan((Root2 - xCut) * s / (a + Root2 * xCut));
    double h1 = dAtan / s - Root2 * in2a + xCut * inXa;
    double h3 = 2.0 * (Root2 - xCut) - 3.0 * s * dAtan + a * (Root2 * in2a - xCut * inXa);
    hard = c0 * h0 + c1 * h1 + c2 * h2 + c3 * h3 + c4 * h4;
    double ls = log((muCut + a) / a);
    double e = atan(xCut / s);
    double s0 = ls + a * inXa - 1.0;
    double s1 = 2.0 * xCut - 3.0 * s * e + a * xCut * inXa;
    double s2 = muCut - 2.0 * a * ls + a * muCut * inXa;
    double s3 = (2.0 / 3.0) * xCut * muCut - 4.0 * a * xCut + 5.0 * a * s * e - a * a * xCut * inXa;
    double s4 = 0.5 * muCut * muCut - 2.0 * a * muCut + 3.0 * a * a * ls - a * a * muCut * inXa;
    softTransport = c0 * s0 + c1 * s1 + c2 * s2 + c3 * s3 + c4 * s4;
}

// = ElectronTransport.cs MottMajorant: наибольшее из 33 узлов на [x_c, √2] + h²/8·max|p″|.
RM_DEV real Sim::MottMajorant(real c0, real c1, real c2, real c3, real c4, real xCut)
{
    const real Root2 = (real)1.4142135623730951;
    real h = (Root2 - xCut) / (real)32;
    real best = (real)-1e30;
    for (int i = 0; i <= 32; i++)
    {
        real x = xCut + i * h;
        real p = c0 + x * (c1 + x * (c2 + x * (c3 + x * c4)));
        if (p > best)
        {
            best = p;
        }
    }

    real curvature = (real)2 * M_Abs(c2) + (real)6 * M_Abs(c3) * Root2 + (real)24 * M_Abs(c4);
    return best + (real)0.125 * h * h * curvature;
}

// = ElectronTransport.cs:371 LayerElasticStep
RM_DEV void Sim::LayerElasticStep(const ScatterElementG* elements, int elementsLen, real tKev, real muCut,
                                  real& hardPerCm, real& softTransportPerCm, real* hardShare)
{
    layerShareWrites++;     // (`AMBER274`, физика 30, Ф6) = ElectronTransport.cs
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
        if (e.MottFit)
        {
            // (`AMBER274`, физика 29, `phys29b`) Z > 30: точные сечения с R_eff (MottMoments).
            real c0, c1, c2, c3, c4, hFit, sFit;
            MottTerms(e.MottCoef, beta, c0, c1, c2, c3, c4);
            MottMoments(c0, c1, c2, c3, c4, a2, muCut, M_Sqrt(muCut), hFit, sFit);
            real hm = pref * hFit;
            hardShare[i] = hm;
            hard += hm;
            trSoft += pref * sFit;
            continue;
        }

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
    if (e.MottFit)
    {
        // (`AMBER274`, физика 29, `phys29b`) Z > 30: сечение уже точное (LayerElasticStep),
        // столкновение состоялось — угол отбором до принятия, R_eff/M.
        real c0, c1, c2, c3, c4;
        MottTerms(e.MottCoef, beta, c0, c1, c2, c3, c4);
        real xCut = M_Sqrt(muCut);
        real majorantFit = MottMajorant(c0, c1, c2, c3, c4, xCut);
        real muFit = muCut;
        for (int tries = 1; ; tries++)
        {
            muFit = (real)1 / (lo - Uniform() * (lo - hi)) - a2;
            if (muFit < muCut)
            {
                muFit = muCut;
            }
            else if (muFit > (real)2)
            {
                muFit = (real)2;
            }

            real x = M_Sqrt(muFit);
            real r = c0 + x * (c1 + x * (c2 + x * (c3 + x * c4)));
            if (r > majorantFit)
            {
                CountMottMajorantExceeded++;
            }

            if (Uniform() * majorantFit <= r)
            {
                break;
            }

            CountMottRejected++;
            if (tries >= MottMaxTries)
            {
                CountMottTriesCap++;
                break;
            }
        }

        Rotate(ux, uy, uz, (real)1 - muFit);
        return true;
    }

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
// (`AMBER274`, физика 30, полоса `phys29c`) Части Z² и Z порознь — ключи `ElectronElasticRefine`
// и `ElectronScreeningDpwa` (ElectronTransport.cs, раздел «РАЗДЕЛЬНЫЕ ЧАСТИ Z² И Z»). Тот же порядок
// действий и розыгрышей `Uniform()`, что в C# (ступень 1).
// ============================================================================

// = ElectronTransport.cs Refine
RM_DEV bool Sim::Refine(int bit)
{
    return C.ElectronElasticRefine && (C.ElectronRefineParts & bit) != 0;
}

// = ElectronTransport.cs EeLimitMu (Ф2)
RM_DEV real Sim::EeLimitMu(real tKev, bool positron)
{
    if (!Refine(RefineEeLimit))
    {
        return (real)2;
    }

    return positron
        ? (real)1
        : (real)1 - M_Sqrt((tKev + (real)2 * ElectronMassKev) / (tKev + (real)4 * ElectronMassKev));
}

// = ElectronTransport.cs SoftScreened (Н4): ряд при a ≫ μ_c, иначе замкнутая форма.
RM_DEV real Sim::SoftScreened(real a, real muCut)
{
    real r = muCut / a;
    if (r < (real)0.1)
    {
        real term = r * r, sum = (real)0, sign = (real)1;
        for (int n = 2; n <= 13; n++)
        {
            sum += sign * (real)(n - 1) * term / (real)n;
            term *= r;
            sign = -sign;
        }

        return sum;
    }

    return M_Log((muCut + a) / a) + a / (muCut + a) - (real)1;
}

// = ElectronTransport.cs DpwaElement.Locate: узел и доля по ln T (узлы — по 10 чисел, ln T первым).
RM_DEV int Sim::DpwaLocate(const real* d, int n, real invStep, real lt, real& u)
{
    if (!(lt > d[0]))
    {
        u = (real)0;
        return 0;
    }

    if (lt >= d[(n - 1) * 10])
    {
        u = (real)1;
        return n - 2;
    }

    int k = (int)((lt - d[0]) * invStep);
    if (k > n - 2)
    {
        k = n - 2;
    }

    while (k > 0 && lt < d[k * 10])
    {
        k--;
    }

    while (k < n - 2 && lt >= d[(k + 1) * 10])
    {
        k++;
    }

    u = (lt - d[k * 10]) / (d[(k + 1) * 10] - d[k * 10]);
    return k;
}

// = ElectronTransport.cs RefinedElasticStep
RM_DEV void Sim::RefinedElasticStep(const ScatterElementG* elements, int elementsLen, real tKev, real muCut,
                                    bool positron, real& hardPerCm, real& softTransportPerCm, real* hardShare)
{
    layerShareWrites++;
    bool posMcf = positron && Refine(RefinePositron);
    real gamma = (real)1 + tKev / ElectronMassKev;
    real beta2 = (real)1 - (real)1 / (gamma * gamma);    // ⚠ float: map_data §4 п.10
    real beta = M_Sqrt(beta2);
    real p2 = tKev * (tKev + (real)2 * ElectronMassKev);
    real common = (real)2 * M_PI_R * ClassicalRadiusCm * ClassicalRadiusCm
                  * ElectronMassKev * ElectronMassKev / (beta2 * p2);
    real eeTop = M_Min((real)2, EeLimitMu(tKev, positron));
    real lt = (real)0;
    bool ltDone = false;
    real hard = (real)0, trSoft = (real)0;
    for (int i = 0; i < elementsLen; i++)
    {
        const ScatterElementG& e = elements[i];
        real pref = e.AtomsPerCm3 * common * e.ZZ1;
        real h, s;
        if (e.Part == 2)
        {
            real a2e = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
            h = eeTop > muCut ? pref * ((real)1 / (muCut + a2e) - (real)1 / (eeTop + a2e)) : (real)0;
            s = pref * SoftScreened(a2e, muCut);
        }
        else
        {
            int dOff = positron ? e.DpwaPos : e.DpwaEl;
            int dLen = positron ? e.DpwaPosLen : e.DpwaElLen;
            if (dLen > 0)
            {
                if (!ltDone)
                {
                    lt = M_Log(tKev);
                    ltDone = true;
                }

                const real* d = RR(dOff);
                real u;
                int k = DpwaLocate(d, dLen / 10, positron ? e.DpwaPosInv : e.DpwaElInv, lt, u);
                const real* q0 = d + k * 10;
                const real* q1 = q0 + 10;
                h = pref * (q0[7] + u * (q1[7] - q0[7]));
                s = pref * (q0[8] + u * (q1[8] - q0[8]));
            }
            else if (e.MottFit && !posMcf)
            {
                real a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                real c0, c1, c2, c3, c4, hFit, sFit;
                MottTerms(e.MottCoef, beta, c0, c1, c2, c3, c4);
                MottMoments(c0, c1, c2, c3, c4, a2, muCut, M_Sqrt(muCut), hFit, sFit);
                h = pref * hFit;
                s = pref * sFit;
            }
            else
            {
                real a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                real majorant = !posMcf && e.Mott
                    ? (real)1 + M_PI_R * FineStructure * e.Z * beta / (real)4 : (real)1;
                h = pref * ((real)1 / (muCut + a2) - (real)1 / ((real)2 + a2)) * majorant;
                s = pref * SoftScreened(a2, muCut);
            }
        }

        hardShare[i] = h;
        hard += h;
        trSoft += s;
    }

    hardPerCm = hard;
    softTransportPerCm = trSoft > (real)0 ? trSoft : (real)0;
}

// = ElectronTransport.cs RefinedHardCollision
RM_DEV bool Sim::RefinedHardCollision(const ScatterElementG* elements, int elementsLen, const real* shareA,
                                      const real* shareB, real w, real majorant, bool allowEmpty, real tKev,
                                      real muCut, bool positron, real& ux, real& uy, real& uz)
{
    real pick = Uniform() * majorant;
    int i = 0;
    for (; i < elementsLen; i++)
    {
        pick -= shareA[i] + (shareB[i] - shareA[i]) * w;
        if (pick <= (real)0)
        {
            break;
        }
    }

    if (i >= elementsLen)
    {
        if (allowEmpty)
        {
            CountMajorantEmpty++;
            return false;
        }

        i = elementsLen - 1;
    }

    return RefinedAngle(elements[i], tKev, muCut, positron, ux, uy, uz);
}

// = ElectronTransport.cs RefinedAngle
RM_DEV bool Sim::RefinedAngle(const ScatterElementG& e, real tKev, real muCut, bool positron,
                              real& ux, real& uy, real& uz)
{
    real gamma = (real)1 + tKev / ElectronMassKev;
    real beta2 = (real)1 - (real)1 / (gamma * gamma);
    real beta = M_Sqrt(beta2);
    if (e.Part == 2)
    {
        real a2e = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
        real top = M_Min((real)2, EeLimitMu(tKev, positron));
        real loE = (real)1 / (muCut + a2e), hiE = (real)1 / (top + a2e);
        real muE = (real)1 / (loE - Uniform() * (loE - hiE)) - a2e;
        if (muE < muCut)
        {
            muE = muCut;
        }
        else if (muE > top)
        {
            muE = top;
        }

        Rotate(ux, uy, uz, (real)1 - muE);
        return true;
    }

    bool posMcf = positron && Refine(RefinePositron);
    int dOff = positron ? e.DpwaPos : e.DpwaEl;
    int dLen = positron ? e.DpwaPosLen : e.DpwaElLen;
    real a2, c0, c1, c2, c3, c4, majorantFit;
    if (dLen > 0)
    {
        const real* d = RR(dOff);
        real u;
        int k = DpwaLocate(d, dLen / 10, positron ? e.DpwaPosInv : e.DpwaElInv, M_Log(tKev), u);
        const real* q0 = d + k * 10;
        const real* q1 = q0 + 10;
        a2 = M_Exp(q0[1] + u * (q1[1] - q0[1]));
        c0 = q0[2] + u * (q1[2] - q0[2]);
        c1 = q0[3] + u * (q1[3] - q0[3]);
        c2 = q0[4] + u * (q1[4] - q0[4]);
        c3 = q0[5] + u * (q1[5] - q0[5]);
        c4 = q0[6] + u * (q1[6] - q0[6]);
        majorantFit = q0[9] + u * (q1[9] - q0[9]);
    }
    else if (e.MottFit && !posMcf)
    {
        a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
        MottTerms(e.MottCoef, beta, c0, c1, c2, c3, c4);
        majorantFit = MottMajorant(c0, c1, c2, c3, c4, M_Sqrt(muCut));
    }
    else
    {
        a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
        real loR = (real)1 / (muCut + a2), hiR = (real)1 / ((real)2 + a2);
        real mu = (real)1 / (loR - Uniform() * (loR - hiR)) - a2;
        if (mu < muCut)
        {
            mu = muCut;
        }
        else if (mu > (real)2)
        {
            mu = (real)2;
        }

        if (posMcf || e.Mott)
        {
            real s = M_Sqrt((real)0.5 * mu);
            real paz = M_PI_R * FineStructure * e.Z * beta;
            real r = posMcf
                ? (real)1 - beta2 * s * s - paz * s * ((real)1 - s)
                : (real)1 - beta2 * s * s + paz * s * ((real)1 - s);
            if (Uniform() * (posMcf ? (real)1 : (real)1 + (real)0.25 * paz) > r)
            {
                return false;
            }
        }

        Rotate(ux, uy, uz, (real)1 - mu);
        return true;
    }

    // DPWA и подгонка Мотта: отбор до принятия.
    real lo = (real)1 / (muCut + a2), hi = (real)1 / ((real)2 + a2);
    real muFit = muCut;
    for (int tries = 1; ; tries++)
    {
        muFit = (real)1 / (lo - Uniform() * (lo - hi)) - a2;
        if (muFit < muCut)
        {
            muFit = muCut;
        }
        else if (muFit > (real)2)
        {
            muFit = (real)2;
        }

        real x = M_Sqrt(muFit);
        real p = c0 + x * (c1 + x * (c2 + x * (c3 + x * c4)));
        if (p > majorantFit)
        {
            CountMottMajorantExceeded++;
        }

        if (Uniform() * majorantFit <= p)
        {
            break;
        }

        CountMottRejected++;
        if (tries >= MottMaxTries)
        {
            CountMottTriesCap++;
            break;
        }
    }

    Rotate(ux, uy, uz, (real)1 - muFit);
    return true;
}

// = ElectronTransport.cs CrystalHardCollision (Ф6: пересчёт затёртых долей без ГСЧ; Ф7: доли концов шага).
RM_DEV bool Sim::CrystalHardCollision(const ScatterElementG* elements, int elementsLen, bool split,
                                      bool stepMajorant, bool shareFix, long long shareMark, real tXs, real tA,
                                      real tB, real w, real hardPerCm, real t, real muCut, bool positron,
                                      real& ux, real& uy, real& uz)
{
    if (shareFix && layerShareWrites != shareMark)
    {
        real h, s;
        CountShareRecomputed++;
        if (stepMajorant)
        {
            RefinedElasticStep(elements, elementsLen, tA, muCut, positron, h, s, layerHardShare);
            RefinedElasticStep(elements, elementsLen, tB, muCut, positron, h, s, layerHardShareEnd);
        }
        else if (split)
        {
            RefinedElasticStep(elements, elementsLen, tXs, muCut, positron, h, s, layerHardShare);
        }
        else
        {
            LayerElasticStep(elements, elementsLen, tXs, muCut, h, s, layerHardShare);
        }
    }

    if (stepMajorant)
    {
        return RefinedHardCollision(elements, elementsLen, layerHardShare, layerHardShareEnd, w, hardPerCm,
                                    true, t, muCut, positron, ux, uy, uz);
    }

    if (split)
    {
        return RefinedHardCollision(elements, elementsLen, layerHardShare, layerHardShare, (real)0, hardPerCm,
                                    false, t, muCut, positron, ux, uy, uz);
    }

    return LayerHardCollision(elements, elementsLen, layerHardShare, hardPerCm, t, muCut, ux, uy, uz);
}

// ============================================================================
// ElectronTransport.cs — перенос по кристаллу
// ============================================================================

// = ElectronTransport.cs:813 TransportElectron
RM_DEVF real Sim::TransportElectron(real x, real y, real z, real ux, real uy, real uz,
                                    real te, bool alongPath, int depth, real& radiated, real& lost,
                                    bool positron)
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

    // (`AMBER274`, физика 29) Под ключом `ElectronMixedTransport` в кристалле — своя доля шага
    // (`ElectronCrystalStepFraction`); без ключа — общая, как было.
    // (физика 30, Н10) Без бита 2 рычага — кристалл физики 28 целиком (доля и потолок шагов).
    bool mixedCrystal = C.ElectronMixedTransport && (C.ElectronMixedParts & 2) != 0;
    real fraction = (real)(mixedCrystal ? C.ElectronCrystalStepFraction : C.ElectronStepFraction);
    if (!(fraction > (real)0) || fraction > (real)1)
    {
        fraction = (real)1;
    }

    // (`M3`) Подтяжка уровня к ESTAR — по НАЧАЛЬНОЙ энергии, одна на весь путь.
    real anchor = alongPath ? Anchor(BremTab(), te) : (real)1;

    // (`AMBER44`) Унесено через грани и осело СНАРУЖИ.
    real escaped = (real)0;

    // (`AMBER274`, физика 29) Смешанная схема в кристалле — под ключом `ElectronMixedTransport`
    // (бит 2 рычага `ElectronMixedParts`): элементы вещества кристалла (`SceneG.crystalMaterial`
    // = geometry.Crystal) и отсечка — те же, что у слоёв. Без опознанных элементов — Хайленд.
    bool mixed = mixedCrystal;
    const ScatterElementG* elements = nullptr;
    int elementsLen = 0;
    real muCut = (real)0;
    if (mixed)
    {
        int first0 = LayerScatterElements(D.scene->crystalMaterial, elementsLen);
        elements = D.scatterElements + first0;
        muCut = LayerHardCutoffMu();
        mixed = elementsLen > 0;
    }

    // (`AMBER274`, физика 30) Части Z² и Z порознь, Ф7, Ф6 — как в C#.
    bool split = mixed && (C.ElectronElasticRefine || C.ElectronScreeningDpwa);
    bool stepMajorant = split && Refine(RefineStepMajorant);
    bool shareFix = Refine(RefineShareRecompute);

    int maxSteps = mixedCrystal ? TransportMaxStepsMixed : TransportMaxSteps;
    real t = te;
    int step = 0;
    for (; step < maxSteps && t > TransportCutKev; step++)
    {
        real stepG = residual * fraction;                          // г/см²
        real stepCm = stepG / density;

        // (`AMBER274`, физика 29) Путь до жёсткого столкновения — по сечению при энергии
        // середины шага; выпало раньше конца шага — шаг обрывается на нём.
        real hardPerCm = (real)0, softTransportPerCm = (real)0;
        bool hardHit = false;
        real tXs = (real)0, tA = (real)0, tB = (real)0, wStep = (real)0;
        long long shareMark = 0;
        if (mixed && stepMajorant)
        {
            // (`AMBER274`, физика 30, Ф7) Сечения на концах полного шага, мажоранта.
            CountCrystalSteps++;
            tA = t;
            tB = EnergyOfRange(ElectronMat(), residual - stepG);
            if (tB < TransportCutKev)
            {
                tB = TransportCutKev;
            }

            real hA, hB, sA, sB;
            RefinedElasticStep(elements, elementsLen, tA, muCut, positron, hA, sA, layerHardShare);
            RefinedElasticStep(elements, elementsLen, tB, muCut, positron, hB, sB, layerHardShareEnd);
            shareMark = layerShareWrites;
            hardPerCm = M_Max(hA, hB);
            real fullCm = stepCm;
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

            wStep = stepCm < fullCm ? stepCm / fullCm : (real)1;
            softTransportPerCm = sA + (sB - sA) * (real)0.5 * wStep;
            if (!(softTransportPerCm > (real)0))
            {
                softTransportPerCm = (real)0;
            }
        }
        else if (mixed)
        {
            CountCrystalSteps++;
            tXs = EnergyOfRange(ElectronMat(), residual - (real)0.5 * stepG);
            if (tXs < TransportCutKev)
            {
                tXs = TransportCutKev;
            }

            if (split)
            {
                RefinedElasticStep(elements, elementsLen, tXs, muCut, positron,
                                   hardPerCm, softTransportPerCm, layerHardShare);
            }
            else
            {
                LayerElasticStep(elements, elementsLen, tXs, muCut,
                                 hardPerCm, softTransportPerCm, layerHardShare);
            }

            shareMark = layerShareWrites;
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

        // Прямой ход до шарнира.
        real first = stepCm * Uniform();
        real toEdge = CrystalPath(x, y, z, ux, uy, uz);
        if (first >= toEdge)
        {
            // (`AMBER274`, физика 28) Энергия на грани — с разыгранной потерей пути до неё.
            if (!EscapeOrReturn(x, y, z, ux, uy, uz, toEdge,
                                ResidualAfter(ElectronMat(), t, residual, toEdge * density),
                                te - radiated - escaped,
                                depth, residual, t, escaped, positron))
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

        // (`AMBER274`, физика 29) Под ключом ширина мягкого шарнира — 1 − exp(−s·Σnσ_tr,soft)
        // (аддитивно по пути); без ключа — Хайленд на весь шаг.
        real theta0 = mixed
            ? M_Sqrt(M_Max((real)0, (real)1 - M_Exp(-stepCm * softTransportPerCm)))
            : HighlandTheta0(tMid, stepG / x0);
        // ⚠ float: поворот на cos = 1 − μ (map_data §4 п.9): при μ < 6e-8 cos ≡ 1 —
        // малые углы шарнира теряются (лучше передавать μ, а не cos).
        Rotate(ux, uy, uz, (real)1 - SampleHingeMu(theta0));

        // Прямой ход до конца шага.
        real second = stepCm - first;
        toEdge = CrystalPath(x, y, z, ux, uy, uz);
        if (second >= toEdge)
        {
            if (!EscapeOrReturn(x, y, z, ux, uy, uz, toEdge,
                                ResidualAfter(ElectronMat(), t, residual, (first + toEdge) * density),
                                te - radiated - escaped,
                                depth, residual, t, escaped, positron))
            {
                return escaped;
            }

            continue;
        }

        x += ux * second;
        y += uy * second;
        z += uz * second;
        residual = ResidualAfter(ElectronMat(), t, residual, stepG);
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

        // (`AMBER274`, физика 29) Жёсткое столкновение в конце оборванного шага — при энергии
        // его конца; после раннего выхода (погибающему поворот не нужен).
        if (hardHit && t > TransportCutKev
            && CrystalHardCollision(elements, elementsLen, split, stepMajorant, shareFix, shareMark,
                                    tXs, tA, tB, wStep, hardPerCm, t, muCut, positron, ux, uy, uz))
        {
            CountCrystalHardCollisions++;
        }
    }

    if (step >= maxSteps && t > TransportCutKev)
    {
        CountTransportStepCap++;
    }

    return escaped;
}

// = ElectronTransport.cs:948 EscapeOrReturn
RM_DEVF bool Sim::EscapeOrReturn(real& x, real& y, real& z, real& ux, real& uy, real& uz,
                                 real toEdge, real residualAtFace, real cap, int depth,
                                 real& residual, real& t, real& escaped, bool positron)
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
    bool back = TransportInLayers(x, y, z, ux, uy, uz, tBack, depth, positron);
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
                                    real& t, int depth, bool positron)
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

    // (`AMBER274`, физика 29) Под ключом `ElectronMixedTransport` (бит 1 `ElectronMixedParts`)
    // сечения смешанной схемы — при энергии СЕРЕДИНЫ шага; потолок — `TransportMaxStepsMixed`.
    bool midXs = C.ElectronMixedTransport && (C.ElectronMixedParts & 1) != 0;
    int maxSteps = C.ElectronMixedTransport ? TransportMaxStepsMixed : TransportMaxSteps;
    // (`AMBER274`, физика 30) Части Z² и Z порознь, Ф7, Ф6 — как в кристалле.
    bool split = mixed && (C.ElectronElasticRefine || C.ElectronScreeningDpwa);
    bool stepMajorant = split && Refine(RefineStepMajorant);
    bool shareFix = Refine(RefineShareRecompute);
    int step = 0;
    for (; step < maxSteps && t > TransportCutKev; step++)
    {
        int here = At(x, y, z);
        if (here >= 0 && D.regions[here].IsCrystal)
        {
            return true;
        }

        real toNext = StepToBoundary(x, y, z, ux, uy, uz);
        if (toNext >= CS_DOUBLE_MAX)
        {
            CountLayerLeftScene++;
            layersLeftScene = true;
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

        // (`AMBER274`, физика 30, бит 32) Шаг в слое — не длиннее доли толщины тела области
        // (= ElectronTransport.cs RegionThicknessCm; DSMAX PENELOPE).
        if (Refine(RefineLayerStepLimit))
        {
            const RegionG& rg = D.regions[here];
            real thick = rg.ZMax - rg.ZMin;
            real side = rg.IsBox ? (real)2 * M_Min(rg.AX, rg.AY)
                                 : (rg.RIn > (real)0 ? rg.ROut - rg.RIn : (real)2 * rg.ROut);
            real capCm = (real)C.ElectronLayerStepMaxFraction * M_Min(thick, side);
            if (capCm > (real)0 && stepCm > capCm)
            {
                stepCm = capCm;
                stepG = stepCm * density;
                CountLayerStepLimited++;
            }
        }

        // (`M13`) Смешанная схема: расстояние до жёсткого столкновения.
        const ScatterElementG* elements = nullptr;
        int elementsLen = 0;
        real hardPerCm = (real)0, softTransportPerCm = (real)0;
        bool hardHit = false;
        real tXs = t, tA = (real)0, tB = (real)0, wStep = (real)0;
        long long shareMark = 0;
        if (mixed && stepMajorant)
        {
            // (`AMBER274`, физика 30, Ф7) Как в кристалле.
            CountLayerSteps++;
            int first0 = LayerScatterElements(hereMat, elementsLen);
            elements = D.scatterElements + first0;
            if (elementsLen > 0)
            {
                tA = t;
                tB = EnergyOfRange(medium, residual - stepG);
                if (tB < TransportCutKev)
                {
                    tB = TransportCutKev;
                }

                real hA, hB, sA, sB;
                RefinedElasticStep(elements, elementsLen, tA, muCut, positron, hA, sA, layerHardShare);
                RefinedElasticStep(elements, elementsLen, tB, muCut, positron, hB, sB, layerHardShareEnd);
                shareMark = layerShareWrites;
                hardPerCm = M_Max(hA, hB);
                real fullCm = stepCm;
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

                wStep = stepCm < fullCm ? stepCm / fullCm : (real)1;
                softTransportPerCm = sA + (sB - sA) * (real)0.5 * wStep;
                if (!(softTransportPerCm > (real)0))
                {
                    softTransportPerCm = (real)0;
                }
            }
        }
        else if (mixed)
        {
            CountLayerSteps++;
            int first0 = LayerScatterElements(hereMat, elementsLen);
            elements = D.scatterElements + first0;
            if (elementsLen > 0)
            {
                // (`AMBER274`, физика 29) Энергия сечений — середина шага (под ключом).
                if (midXs)
                {
                    tXs = EnergyOfRange(medium, residual - (real)0.5 * stepG);
                    if (tXs < TransportCutKev)
                    {
                        tXs = TransportCutKev;
                    }
                }

                if (split)
                {
                    RefinedElasticStep(elements, elementsLen, tXs, muCut, positron, hardPerCm, softTransportPerCm, layerHardShare);
                }
                else
                {
                    LayerElasticStep(elements, elementsLen, tXs, muCut, hardPerCm, softTransportPerCm, layerHardShare);
                }

                shareMark = layerShareWrites;
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
            t = EnergyOfRange(medium, ResidualAfter(medium, t, residual, toNext * density));
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
            CountLayerLeftScene++;
            layersLeftScene = true;
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
            t = EnergyOfRange(medium, ResidualAfter(medium, t, residual, (first + toNext) * density));
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

        residual = ResidualAfter(medium, t, residual, stepG);
        t = EnergyOfRange(medium, residual);

        // (`M13`) Жёсткое столкновение в конце оборванного шага — при энергии его конца.
        if (hardHit && t > TransportCutKev
            && CrystalHardCollision(elements, elementsLen, split, stepMajorant, shareFix, shareMark,
                                    tXs, tA, tB, wStep, hardPerCm, t, muCut, positron, ux, uy, uz))
        {
            CountLayerHardCollisions++;
        }
    }

    if (step >= maxSteps && t > TransportCutKev)
    {
        CountTransportStepCap++;
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

// ============================================================================
// (`AMBER274`, физика 28) Страгглинг потерь — модель Урбана (ElectronTransport.cs)
// ============================================================================

// = ElectronTransport.cs ResidualAfter. Без ключа — `residual − pathG` (побитово физика 27).
RM_DEVF real Sim::ResidualAfter(const ElectronMaterialG& medium, real t, real residual, real pathG)
{
    if (!C.ElectronStraggling || !(medium.PotentialEv > (real)0))
    {
        return residual - pathG;
    }

    real after = residual - pathG;
    if (!(after > (real)0))
    {
        return after;               // путь не короче пробега — остановился
    }

    real meanLoss = t - EnergyOfRange(medium, after);
    if (!(meanLoss >= StragglingMinLossKev))
    {
        return after;
    }

    // не перенесено: CountStragglingSteps
    real radiative = meanLoss * RadShareOf(medium, t);
    real loss = radiative + (real)UrbanLoss(medium, t, meanLoss - radiative);
    real tEnd = t - loss;
    if (!(tEnd > StragglingLowestKev))
    {
        // не перенесено: CountStragglingStops
        return (real)0;
    }

    return RangeOf(medium, tEnd);
}

// = ElectronTransport.cs UrbanLoss (`G4UniversalFluctuation::SampleFluctuations` +
// `G4UrbanFluctuation::SampleGlandz`). ⚠ float: арифметика модели — в double: обращение
// Пуассона, суммы столкновений `w3/(1 − w·u)` и `alfa − alfa1²` во float теряли бы хвост.
RM_DEVF double Sim::UrbanLoss(const ElectronMaterialG& m, real tR, real meanLossR)
{
    const double ElectronMassD = 510.99895;
    double ipot = m.Urban[0], ipotLog = m.Urban[1], f1 = m.Urban[2], f2 = m.Urban[3];
    double e1Fluct = m.Urban[4], e2Fluct = m.Urban[5], e1Log = m.Urban[6], e2Log = m.Urban[7];
    double t = tR;
    double meanLoss = meanLossR;
    double tcut = 0.5 * t;
    double e0 = StragglingE0Kev;
    if (tcut <= e0)
    {
        return meanLoss;
    }

    double gam = t / ElectronMassD + 1.0;
    double gam2 = gam * gam;
    double beta2 = 1.0 - 1.0 / gam2;
    double scaling = fmin(1.0 + 0.5 / tcut, 1.5);
    meanLoss /= scaling;
    double w2 = tcut > ipot ? log(2.0 * ElectronMassD * beta2 * gam2) - beta2 : 0.0;

    double a1 = 0.0, a2 = 0.0;
    double e1 = e1Fluct, e2 = e2Fluct;
    if (tcut > ipot && w2 > ipotLog)
    {
        if (w2 > e2Log)
        {
            double c = meanLoss * (1.0 - UrbanRate) / (w2 - ipotLog);
            a1 = c * f1 * (w2 - e1Log) / e1Fluct;
            a2 = c * f2 * (w2 - e2Log) / e2Fluct;
        }
        else
        {
            a1 = meanLoss * (1.0 - UrbanRate) / e1;
        }

        if (a1 < UrbanA0)
        {
            double fwnow = 0.5 + (UrbanFw - 0.5) * sqrt(a1 / UrbanA0);
            a1 /= fwnow;
            e1 *= fwnow;
        }
        else
        {
            a1 /= UrbanFw;
            e1 *= UrbanFw;
        }
    }

    double w1 = tcut / e0;
    double a3 = UrbanRate * meanLoss * (tcut - e0) / (e0 * tcut * log(w1));
    if (a1 + a2 <= 0.0)
    {
        a3 /= UrbanRate;
    }

    double loss = 0.0, emean = 0.0, sig2e = 0.0;
    if (a1 > 0.0)
    {
        UrbanExcitation(a1, e1, emean, loss, sig2e);
    }

    if (a2 > 0.0)
    {
        UrbanExcitation(a2, e2, emean, loss, sig2e);
    }

    if (sig2e > 0.0)
    {
        UrbanGauss(emean, sig2e, loss);
    }

    if (a3 > 0.0)
    {
        emean = 0.0;
        sig2e = 0.0;
        double p3 = a3, alfa = 1.0;
        if (a3 > UrbanNmaxCont)
        {
            alfa = w1 * (UrbanNmaxCont + a3) / (w1 * UrbanNmaxCont + a3);
            double alfa1 = alfa * log(alfa) / (alfa - 1.0);
            double namean = a3 * w1 * (alfa - 1.0) / ((w1 - 1.0) * alfa);
            emean += namean * e0 * alfa1;
            sig2e += e0 * e0 * namean * (alfa - alfa1 * alfa1);
            p3 -= namean;
        }

        double w3 = alfa * e0;
        if (tcut > w3)
        {
            double w = (tcut - w3) / tcut;
            int nnb = PoissonG4(p3);
            for (int k = 0; k < nnb; k++)
            {
                loss += w3 / (1.0 - w * (double)Uniform());
            }
        }

        if (sig2e > 0.0)
        {
            UrbanGauss(emean, sig2e, loss);
        }
    }

    return loss * scaling;
}

// = ElectronTransport.cs UrbanExcitation (`G4UniversalFluctuation::AddExcitation`)
RM_DEVF void Sim::UrbanExcitation(double ax, double ex, double& eav, double& eloss, double& esig2)
{
    if (ax > UrbanNmaxCont)
    {
        eav += ax * ex;
        esig2 += ax * ex * ex;
    }
    else
    {
        int p = PoissonG4(ax);
        if (p > 0)
        {
            eloss += ((p + 1) - 2.0 * (double)Uniform()) * ex;
        }
    }
}

// = ElectronTransport.cs UrbanGauss (`G4UniversalFluctuation::SampleGauss`)
RM_DEVF void Sim::UrbanGauss(double eav, double esig2, double& eloss)
{
    double x = eav;
    double sig = sqrt(esig2);
    if (eav < 0.25 * sig)
    {
        x += (2.0 * (double)Uniform() - 1.0) * eav;
    }
    else
    {
        int guard = 0;
        do
        {
            x = eav + sig * Gaussian();
        }
        while ((x < 0.0 || x > 2.0 * eav) && ++guard < 1000);
        if (guard >= 1000)
        {
            x = eav;
        }
    }

    eloss += x;
}

// = ElectronTransport.cs Gaussian — Бокс—Мюллер, два равномерных на вызов.
RM_DEVF double Sim::Gaussian()
{
    double r = sqrt(-2.0 * log((double)Uniform()));
    return r * cos(6.283185307179586 * (double)Uniform());
}

// = ElectronTransport.cs PoissonG4 (как `G4Poisson`; сумма — в double, потолок 100).
RM_DEVF int Sim::PoissonG4(double mean)
{
    if (!(mean > 0.0))
    {
        return 0;
    }

    if (mean <= 16.0)
    {
        double position = (double)Uniform();
        double value = exp(-mean);
        double sum = value;
        int number = 0;
        while (sum <= position && number < 100)
        {
            number++;
            value *= mean / number;
            sum += value;
        }

        return number;
    }

    double g = mean + Gaussian() * sqrt(mean) + 0.5;
    return g <= 0.0 ? 0 : (int)fmin(g, 2.0e9);
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
                                        real& uxIn, real& uyIn, real& uzIn, real& tIn, bool positron)
{
    xIn = x; yIn = y; zIn = z;
    layersLeftScene = false;
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
    bool entered = TransportInLayers(xIn, yIn, zIn, uxIn, uyIn, uzIn, tIn, 0, positron);
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
