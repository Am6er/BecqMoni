// sim_photon.cuh — определения методов `Sim` модуля ФОТОНА В КРИСТАЛЛЕ (объявления —
// decl_photon.inc). ПОСТРОЧНЫЙ перенос с `BecquerelMonitor/EfficiencyMaker/EfficiencySimulator.cs`,
// имена C#; у каждой функции — `// = EfficiencySimulator.cs:<строка> <Имя>`.
// Полоса П221 (`AMBER160`), часть В. Соглашения — README.md этого каталога.
//
// Порядок розыгрышей `Uniform()` и арифметики — ровно C# (ступень 1 приёмки: сверка
// истории с CPU в double). Опасные во float места — `// ⚠ float:` (map_data.md §4).
// Литералы с плавающей точкой обёрнуты в `(real)`: в режиме float выражение иначе молча
// считалось бы в double; в режиме double `(real)x` — тот же x.
//
// Чужие функции, которые здесь зовутся:
//   полоса А (tables_dev.cuh, свободные, зовутся через `::`): MassCrossSection(int z, …),
//     Omega, HasL, OmegaLAt, CkAt, LYield, KFraction, LFractions, Of, BindingKev,
//     ShellByBinding, AbsorbingShell (2), VacancyAfterPhoton, Step, ScatteringFunction,
//     ShellCount(AtomG), ShellBindingKev, SampleMomentumTransferSq, SelectShell,
//     SampleMomentumAu, RangeOf, YieldOf, EnergyOfRange, Photons, SampleKev, TblRow, TblRowLen;
//     константы ScatteringData::InverseCmPerKev, ScatteringData::FineStructure;
//   полоса Б (sim_geom.cuh, члены Sim): CrystalPath, Rotate, Isotropic, RegCoherent, RegMu,
//     RegPrepareElements, RegElementZ, RegElementKnown, RegElementFraction, RegElementCrossSection;
//   полоса Г (sim_electron.cuh, члены Sim): ElectronBirthDirection, ComptonElectronDirection,
//     TransportElectron.
#pragma once
#include "sim.cuh"
#include "tables_dev.cuh"

// --- начальное состояние -------------------------------------------------------------

// Поля модуля — как их оставляет конструктор C# (инициализаторов у них нет: нули, false).
RM_DEV void Sim::InitPhoton()
{
    escapeCollect = false;
    escapeCount = 0;
    escapeLost = 0;
    for (int k = 0; k < EscapeMax; k++)
    {
        escX[k] = 0; escY[k] = 0; escZ[k] = 0;
        escUx[k] = 0; escUy[k] = 0; escUz[k] = 0;
        escE[k] = 0;
    }

    lossAnnihilation = 0;
    lossXray = 0;
    lossXrayK = 0;
    lossXrayL = 0;
    lastXrayIsL = false;
    lastAbsorbZ = 0;
    lastAbsorbShell = 0;
    lastKLine = 0;
    lastCascadeRolled = false;
    lastVacancyLine = 0;
    lastVacancyRolled = false;
    pendingCascadeKev = 0;
    annihilationEscapes = 0;
    lightDeposit = 0;
    // cascadeStack не обнуляется: C# читает только записанное (`top`).

    CountCrystalCompton = 0;
    CountCrystalVacancy = 0;
    CountVacancyXray = 0;
    CountKXray = 0;
    CountLXray = 0;
    CountKLCascade = 0;
    CountKLVacancy = 0;
    CountCascadeOverflow = 0;
}

// --- вылеты ---------------------------------------------------------------------------

// = EfficiencySimulator.cs:5185 NoteEscape
RM_DEV void Sim::NoteEscape(real x, real y, real z, real ux, real uy, real uz, real energyKev)
{
    if (escapeCount >= EscapeMax)
    {
        escapeLost++;
        // не перенесено: Interlocked.Increment(ref TotalEscapeDropped) — статическая сумма
        // процесса; на узле её даёт CountEscapeDropped += escapeLost полосы Д (:7625, :9809).
        return;
    }

    int k = escapeCount++;
    // ⚠ float: сдвиг за грань на 1e-7 см меньше ULP float уже при |x| ≳ 1 см (ULP(1.0f) =
    // 1.2e-7) — точка остаётся НА грани, и `At` отдаёт её кристаллу (обход войдёт повторно).
    escX[k] = x + ux * RM_NUDGE;
    escY[k] = y + uy * RM_NUDGE;
    escZ[k] = z + uz * RM_NUDGE;
    escUx[k] = ux;
    escUy[k] = uy;
    escUz[k] = uz;
    escE[k] = energyKev;
}

// = EfficiencySimulator.cs:8337 NoteXrayShell
RM_DEV void Sim::NoteXrayShell(real kev, bool isL)
{
    if (isL)
    {
        lossXrayL += kev;
    }
    else
    {
        lossXrayK += kev;
    }
}

// --- фотон в кристалле -------------------------------------------------------------

// = EfficiencySimulator.cs:5205 InCrystal
RM_DEVF real Sim::InCrystal(real x, real y, real z, real ux, real uy, real uz,
                            real energyKev, int depth)
{
    if (depth > 12)
    {
        return energyKev;
    }

    // lost копит энергию, ушедшую из кристалла на предыдущих шагах.
    // ⚠ float: возврат `lost + e` — недобор истории; правило пика InPeak (полоса Д) при
    // допуске 0 держится тем, что у поглощённой истории все слагаемые — точные нули.
    real lost = (real)0.0;
    real e = energyKev;
    const int crystalRegion = D.scene->crystal;   // = this.crystal
    for (int step = 0; step < 200; step++)
    {
        real photo, compton, pair;
        CrystalChannels(e, photo, compton, pair);
        // Когерентное — отдельным каналом: энергии не оставляет, но поворачивает квант.
        real coherent = C.RayleighScatter
            ? RegCoherent(crystalRegion, e) * (real)C.CrystalCoherentScale : (real)0.0;
        real total = photo + compton + pair + coherent;
        if (!(total > (real)0.0))
        {
            // Сечения нет вовсе — квант проходит кристалл насквозь (`A55`).
            if (escapeCollect)
            {
                real out0 = CrystalPath(x, y, z, ux, uy, uz);
                NoteEscape(x + ux * out0, y + uy * out0, z + uz * out0,
                           ux, uy, uz, e);
            }

            return lost + e;
        }

        real path = CrystalPath(x, y, z, ux, uy, uz);
        // ⚠ float: `1 − U` у U → 1 даёт 0 и бесконечный пробег; `Uniform()` во float зажат
        // сверху последним float меньше 1 (sim.cuh), хвост глубже 16.6 пробегов срезан.
        real free = -M_Log((real)1.0 - Uniform()) / total;
        if (free >= path)
        {
            // ⛔ ВЫЛЕТ (`A55`).
            if (escapeCollect)
            {
                NoteEscape(x + ux * path, y + uy * path, z + uz * path,
                           ux, uy, uz, e);
            }

            return lost + e;                // вылетел
        }

        x += ux * free;
        y += uy * free;
        z += uz * free;

        real pick = Uniform() * total;
        if (pick < photo)
        {
            real xray = SampleFluorescence(crystalRegion, e);
            // (`A101`) Второй квант каскада K→L — забирается СРАЗУ, до рекурсии.
            real casc = pendingCascadeKev;
            pendingCascadeKev = (real)0.0;
            // (`AMBER16` п. 1) Серия ЭТОГО кванта — до рекурсии.
            bool xrayIsL = lastXrayIsL;
            // (`F11` (а), П17) Место поглощения и решения переноса — до рекурсии.
            int absorbZ = lastAbsorbZ;
            int absorbShell = lastAbsorbShell;
            int kLine = lastKLine;
            bool cascadeRolled = lastCascadeRolled;
            if (xray > (real)0.0)
            {
                real kx, ky, kz;
                Isotropic(kx, ky, kz);
                // Порядок вызовов тот же: сначала электрон, потом рентгеновский квант.
                real electron = C.LightCascadeSplit && absorbZ > 0 && absorbShell > 0
                    ? PhotoElectronsSplit(x, y, z, e, absorbZ, absorbShell,
                                          xray, kLine, casc, cascadeRolled, depth,
                                          ux, uy, uz)
                    : ElectronLoss(x, y, z, e - xray - casc, depth,
                                   ElectronBirth::Photo, ux, uy, uz);
                // Метка канала — только НЕПОМЕЧЕННЫЙ остаток вылета (§4 map_history).
                real markedBefore = lossAnnihilation + lossXray;
                real gone = InCrystal(x, y, z, kx, ky, kz, xray, depth + 1);
                real markedInside = lossAnnihilation + lossXray - markedBefore;
                real ownXray = M_Max((real)0.0, gone - markedInside);
                lossXray += ownXray;
                NoteXrayShell(ownXray, xrayIsL);
                // не перенесено: this.traceXrayGone = gone (трассировка).

                // (`A101`) Каскадный L-квант — тем же путём, что вакансионный рентген.
                real goneC = (real)0.0;
                if (casc > (real)0.0)
                {
                    real cx, cy, cz;
                    Isotropic(cx, cy, cz);
                    real markedBeforeC = lossAnnihilation + lossXray;
                    goneC = InCrystal(x, y, z, cx, cy, cz, casc, depth + 1);
                    real markedInsideC = lossAnnihilation + lossXray - markedBeforeC;
                    real ownCasc = M_Max((real)0.0, goneC - markedInsideC);
                    lossXray += ownCasc;
                    // (`AMBER16` п. 1) Каскадный квант — ВСЕГДА L.
                    NoteXrayShell(ownCasc, true);
                }

                return lost + electron + gone + goneC;
            }

            // фотоэлектрон уносит почти всю энергию кванта
            if (C.LightCascadeSplit && absorbZ > 0 && absorbShell > 0)
            {
                return lost + PhotoElectronsSplit(x, y, z, e, absorbZ, absorbShell,
                                                  (real)0.0, -1, (real)0.0, false, depth,
                                                  ux, uy, uz);
            }

            return lost + ElectronLoss(x, y, z, e, depth, ElectronBirth::Photo, ux, uy, uz);
        }

        if (pick < photo + compton)
        {
            real cos;
            real vacancy;
            int vacancyZ;
            real scattered = ComptonScatter(crystalRegion, e, cos, vacancy, vacancyZ);
            // (`A72`, П27) Направление ДО поворота — для импульса электрона отдачи.
            real ux0 = ux, uy0 = uy, uz0 = uz;
            Rotate(ux, uy, uz, cos);
            real edx, edy, edz;
            ComptonElectronDirection(e, ux0, uy0, uz0, scattered, ux, uy, uz,
                                     edx, edy, edz);

            // ⛔ ВАКАНСИЯ ОБОЛОЧКИ (`A61`): энергию связи забирает характеристический квант.
            // ⚠ float: `e − scattered` при малом угле — разность близких (ранг М).
            real toElectron = e - scattered;
            CountCrystalCompton++;
            if (vacancy > (real)0.0)
            {
                CountCrystalVacancy++;
            }

            real vacancyXray = toElectron > vacancy
                ? VacancyXray(D.scene->crystalMaterial, vacancyZ, vacancy) : (real)0.0;
            // (`F11` (а), П17) Признаки розыгрыша — до рекурсии.
            int vacancyLine = lastVacancyLine;
            bool vacancyRolled = lastVacancyRolled;
            if (vacancyXray > (real)0.0)
            {
                CountVacancyXray++;
            }
            if (vacancyXray > (real)0.0 && vacancyXray <= vacancy)
            {
                toElectron -= vacancyXray;
                real vx, vy, vz2;
                Isotropic(vx, vy, vz2);
                real markedBefore = lossAnnihilation + lossXray;
                real goneV = InCrystal(x, y, z, vx, vy, vz2, vacancyXray, depth + 1);
                real markedInside = lossAnnihilation + lossXray - markedBefore;
                real ownVacancy = M_Max((real)0.0, goneV - markedInside);
                lossXray += ownVacancy;
                // (`AMBER16` п. 1) Вакансионный квант — ВСЕГДА K.
                NoteXrayShell(ownVacancy, false);
                lost += goneV;
            }

            // (`F11` (а), П17) Раскладка у вакансии комптона.
            if (C.LightCascadeSplit && vacancy > (real)0.0 && vacancyZ > 0
                && toElectron + vacancyXray > vacancy)
            {
                lost += VacancyElectronsSplit(x, y, z, toElectron + vacancyXray, vacancyZ,
                                              vacancy, vacancyXray, vacancyLine,
                                              vacancyRolled, depth, edx, edy, edz);
            }
            else
            {
                lost += ElectronLoss(x, y, z, toElectron, depth,
                                     ElectronBirth::Given, edx, edy, edz);
            }

            e = scattered;
            if (e < (real)1.0)
            {
                // остаток осел на месте — по свету это электрон той же субкэвной энергии
                AddLight(e, e);
                return lost;
            }

            continue;
        }

        if (pick < photo + compton + coherent)
        {
            // Когерентное рассеяние: энергия та же, направление другое.
            Rotate(ux, uy, uz, RayleighCosine(crystalRegion, e));
            continue;
        }

        // рождение пары: кванты аннигиляции летят СТРОГО навстречу.
        // (C#: `e - 2.0 * ElectronMassKev`, константа свёрнута в double; во float 2·m точно.)
        real kinetic = e - (real)2.0 * ElectronMassKev;
        real px = x, py = y, pz = z;
        real escaped;
        if (C.PositronTransport)
        {
            // Электрон и позитрон — ПОРОЗНЬ (`S120`).
            real share = Uniform();
            // C#: `lost + A + B` слева направо, A — раньше B (оба тянут ГСЧ).
            real lossA = ElectronLoss(x, y, z, kinetic * share, depth,
                                      ElectronBirth::Pair, ux, uy, uz);
            real lossB = ElectronLoss(x, y, z, kinetic * ((real)1.0 - share), depth,
                                      ElectronBirth::Pair, ux, uy, uz);
            escaped = lost + lossA + lossB;
            if (C.PositronOffset)
            {
                PositronStop(kinetic * ((real)1.0 - share), px, py, pz);
            }
        }
        else
        {
            escaped = lost + ElectronLoss(x, y, z, kinetic, depth,
                                          ElectronBirth::Pair, ux, uy, uz);
        }

        real ax, ay, az;
        Isotropic(ax, ay, az);
        real pairMarkedBefore = lossAnnihilation + lossXray;
        real first = InCrystal(px, py, pz, ax, ay, az, ElectronMassKev, depth + 1);
        // ⛔ (`AMBER15`) Замер меток ЕЩЁ И ПОСЕРЕДИНЕ — между квантами.
        real firstMarked = lossAnnihilation + lossXray - pairMarkedBefore;
        real second = InCrystal(px, py, pz, -ax, -ay, -az, ElectronMassKev, depth + 1);
        real pairMarkedInside = lossAnnihilation + lossXray - pairMarkedBefore;
        // ⚠ Само слагаемое метки — одной суммой, как в C#.
        lossAnnihilation += M_Max((real)0.0, first + second - pairMarkedInside);
        real firstOwn = M_Max((real)0.0, first - firstMarked);
        real secondOwn = M_Max((real)0.0, second - (pairMarkedInside - firstMarked));
        real leftCrystal = AnnihilationEscapeShare * ElectronMassKev;
        if (firstOwn > leftCrystal)
        {
            annihilationEscapes++;
        }

        if (secondOwn > leftCrystal)
        {
            annihilationEscapes++;
        }

        return escaped + first + second;
    }

    return lost + e;
}

// Запасной путь `PartialCrossSections.MassCrossSection(int z, …)` ниже (в `CrystalChannels`,
// `SampleFluorescence`, `PickAtom`) на устройстве ДОСТИЖИМ ТОЛЬКО при E ≤ 0: снимок области
// совпадает с составом вещества по построению (data_scene.cuh, `RegionG`), подпоследовательности
// флуоресцентов и рассеивателей сходятся. Перенесён построчно — цена нулевая.

// = EfficiencySimulator.cs:3600 CrystalChannels
RM_DEVF void Sim::CrystalChannels(real energyKev, real& photo, real& compton, real& pair)
{
    const int m = D.scene->crystalMaterial;          // = this.geometry.Crystal
    const MaterialG& mg = D.materials[m];
    if (D.scene->crystalHasPartials)
    {
        photo = (real)0.0;
        compton = (real)0.0;
        pair = (real)0.0;

        // ⚡ (`A43`, П45) Пара узлов и логарифм — из снимка области кристалла.
        const int c = D.scene->crystal;               // = this.crystal
        int count = c >= 0 && D.regions[c].Material == m && mg.ZLen > 0
            ? RegPrepareElements(c, energyKev) : -2;
        if (count >= 0 && count == mg.ZLen)
        {
            for (int i = 0; i < count; i++)
            {
                if (!RegElementKnown(c, i))
                {
                    continue;       // элемента нет в поставке — как `TryGet` false
                }

                real fraction = RegElementFraction(c, i);
                real sigmaPhoto = RegElementCrossSection(c, i, energyKev, PhotonProcess::Photoelectric, false);
                real sigmaCompton = RegElementCrossSection(c, i, energyKev, PhotonProcess::Incoherent, false);
                real sigmaPair = RegElementCrossSection(c, i, energyKev, PhotonProcess::PairProduction,
                                                        C.XcomPairThreshold);
                photo += fraction * sigmaPhoto;
                compton += fraction * sigmaCompton;
                pair += fraction * sigmaPair;
            }

            photo *= mg.Density;
            compton *= mg.Density;
            pair *= mg.Density;
            return;
        }

        // Запасной проход по словарю состава (:3646-3670). На устройстве сюда приходит
        // только E ≤ 0 (`PrepareElements` = −1), и тогда каждый элемент пропускается
        // (`!(energyKev > 0.0)` → continue): каналы — нули, ровно как в C#.
        // Иной приход — рассогласование упаковки: отказ.
        if (energyKev > (real)0.0)
        {
            __trap();
        }

        photo *= mg.Density;
        compton *= mg.Density;
        pair *= mg.Density;
        return;
    }

    // Ветка «без парциальных» (:3673-3693): кристалл, у которого нет XCOM-сечений хоть
    // одного элемента. Ей нужны `GeometryMaterial.LinearAttenuation` (таблица
    // `AttenuationData`, GeometryModel.cs:177) и `ElectronDensity` (GeometryModel.cs:357,
    // `AttenuationData.AtomicMass`) — этих таблиц на устройстве НЕТ. Сцены корпуса её не
    // зовут (`crystalHasPartials` истинен); упаковщик обязан отказать такой сцене, а здесь —
    // громкий отказ вместо тихой подмены. Тело перенесено под ключом на будущее.
#ifdef RM_CRYSTAL_NO_PARTIALS
    real total = LinearAttenuation(mg, energyKev);
    compton = KleinNishinaTotal(energyKev) * ElectronDensity(mg);
    if (compton > total)
    {
        compton = total;
    }

    real rest = M_Max((real)0.0, total - compton - (real)C.CoherentFractionOfTotal * total);
    real ramp = (real)0.0;
    if (energyKev > (real)2.0 * ElectronMassKev)
    {
        ramp = M_Min((real)1.0, (energyKev - (real)2.0 * ElectronMassKev)
                                / ((real)1500.0 - (real)2.0 * ElectronMassKev));
    }

    pair = ramp * rest;
    photo = rest - pair;
#else
    photo = (real)0.0;
    compton = (real)0.0;
    pair = (real)0.0;
    __trap();
#endif
}

// = EfficiencySimulator.cs:3561 KleinNishinaTotal
// ⚠ float: при малых a = E/mc² (1…25 кэВ) слагаемые ~1/a² гасятся до 8/3 — мусор во float
// (map_data §4 п.4); зовётся только веткой «без парциальных» (выше, под ключом).
RM_DEVF real Sim::KleinNishinaTotal(real energyKev)
{
    real a = energyKev / ElectronMassKev;
    if (!(a > (real)0.0))
    {
        return (real)0.0;
    }

    real t1 = ((real)1.0 + a) / (a * a) * ((real)2.0 * ((real)1.0 + a) / ((real)1.0 + (real)2.0 * a)
                                           - M_Log((real)1.0 + (real)2.0 * a) / a);
    real t2 = M_Log((real)1.0 + (real)2.0 * a) / ((real)2.0 * a);
    real t3 = ((real)1.0 + (real)3.0 * a) / (((real)1.0 + (real)2.0 * a) * ((real)1.0 + (real)2.0 * a));
    // C#: `2.0 * Math.PI * r * r * (…)` слева направо.
    return (real)2.0 * M_PI_R * ClassicalRadiusCm * ClassicalRadiusCm * (t1 + t2 - t3);
}

// = EfficiencySimulator.cs:9626 KleinNishinaCos
RM_DEV real Sim::KleinNishinaCos(real a, real cos)
{
    real r = (real)1.0 / ((real)1.0 + a * ((real)1.0 - cos));
    return r * r * (r + (real)1.0 / r - ((real)1.0 - cos * cos));
}

// = EfficiencySimulator.cs:9633 KleinNishinaIntegral
// ⚠ float: ряд включается лишь при a < 1e-4; при a ~ 0.002…0.05 (1…25 кэВ) слагаемые ~1/a²
// гасятся до 8/3 — во float мусор (map_data §4 п.4). Лечение — ряд до a ~ 0.1 или таблица.
RM_DEVF real Sim::KleinNishinaIntegral(real a)
{
    if (a < (real)1e-4)
    {
        return (real)8.0 / (real)3.0 * ((real)1.0 - (real)2.0 * a);   // томсоновский предел
    }

    real b = (real)1.0 + (real)2.0 * a;
    real l = M_Log(b);
    return (real)2.0 * (((real)1.0 + a) / (a * a) * ((real)2.0 * ((real)1.0 + a) / b - l / a)
                        + l / ((real)2.0 * a)
                        - ((real)1.0 + (real)3.0 * a) / (b * b));
}

// = EfficiencySimulator.cs:5554 PositronStop
RM_DEVF void Sim::PositronStop(real te, real& x, real& y, real& z)
{
    if (D.scene->electron < 0 || !(te > (real)1.0))
    {
        return;                     // осел там же, где родился
    }

    real range = ::RangeOf(D.electronMats[D.scene->electron], te)
                 / D.materials[D.scene->crystalMaterial].Density;   // см
    real reach = range * M_Min((real)1.0, (real)C.ElectronEscapeSlope
        * M_Max((real)0.0, te - (real)C.ElectronEscapeT0Kev) / (real)1000.0);
    real ux, uy, uz;
    Isotropic(ux, uy, uz);
    if (!(reach > (real)0.0))
    {
        return;                     // розыгрыш направления сделан всегда
    }

    real toEdge = CrystalPath(x, y, z, ux, uy, uz);
    // ⚠ float: отступ 1e-7 см от грани меньше ULP float уже при |x| ≳ 1 см.
    real move = M_Min(reach, M_Max((real)0.0, toEdge - RM_NUDGE));
    x += ux * move;
    y += uy * move;
    z += uz * move;
}

// --- электрон в кристалле -------------------------------------------------------------

// = EfficiencySimulator.cs:5635 ElectronLoss (без направления — изотропное рождение)
RM_DEVF real Sim::ElectronLoss(real x, real y, real z, real te, int depth)
{
    return ElectronLoss(x, y, z, te, depth, ElectronBirth::Isotropic, (real)0.0, (real)0.0, (real)0.0);
}

// = EfficiencySimulator.cs:5640 ElectronLoss
RM_DEVF real Sim::ElectronLoss(real x, real y, real z, real te, int depth,
                               ElectronBirth birth, real rx, real ry, real rz)
{
    if (D.scene->electron < 0 || !(te > (real)1.0) || depth > 12)
    {
        // электрон осел целиком там, где родился
        AddLight(te, te);
        return (real)0.0;
    }

    real lost = (real)0.0;

    // Свет электрона — по СОБСТВЕННОМУ треку: излучённое тормозным покидает трек всё.
    real radiated = (real)0.0;

    // (`M3`, П44) Тормозное ВДОЛЬ ПУТИ: кванты рождаются на шагах переноса.
    bool alongPath = C.BremAlongPath != 0 && C.Bremsstrahlung && C.BremFromData
                     && D.scene->bremTable >= 0 && C.ElectronEscape && C.ElectronTransport;

    // мёртво при умолчаниях матрицы (alongPath истинен): тормозное в точке рождения.
    if (C.Bremsstrahlung && !alongPath)
    {
        const real MinKev = (real)5.0;      // ниже кванту не выйти ниоткуда
        if (te > MinKev)
        {
            const int table = C.BremFromData ? D.scene->bremTable : -1;
            real mean = table >= 0
                ? ::Photons(D.brems[table], te)
                : ::YieldOf(D.electronMats[D.scene->electron], te) * te / (te - MinKev)
                  * M_Log(te / MinKev);
            int n = Poisson(mean);
            for (int i = 0; i < n; i++)
            {
                real k = table >= 0
                    ? ::SampleKev(D.brems[table], te, Uniform())
                    : MinKev * M_Pow(te / MinKev, Uniform());
                real ax, ay, az;
                Isotropic(ax, ay, az);

                // Сумма квантов не может превысить энергию электрона; розыгрыши делаются ВСЕГДА.
                real kUse = M_Min(k, te - radiated);
                if (!(kUse > (real)0.0))
                {
                    continue;
                }

                radiated += kUse;
                CountBremPhotons++;
                SumBremKev += kUse;
                lost += InCrystal(x, y, z, ax, ay, az, kUse, depth + 1);
            }
        }
    }

    real escapedSelf = (real)0.0;
    if (C.ElectronEscape && C.ElectronTransport)
    {
        // ⛔ (`A72`, П27) ПЕРЕНОС: направление рождения по процессу, шаги по пробегу.
        real ux, uy, uz;
        ElectronBirthDirection(birth, te, rx, ry, rz, ux, uy, uz);
        escapedSelf = TransportElectron(x, y, z, ux, uy, uz, te, alongPath, depth,
                                        radiated, lost);
        lost += escapedSelf;
    }
    else if (C.ElectronEscape)
    {
        // мёртво при умолчаниях матрицы (ElectronTransport ВКЛ): эффективная глубина вылета.
        real density = D.materials[D.scene->crystalMaterial].Density;
        const ElectronMaterialG& el = D.electronMats[D.scene->electron];
        real range = ::RangeOf(el, te) / density;   // см
        real share = (real)C.ElectronEscapeSlope
            * M_Max((real)0.0, te - (real)C.ElectronEscapeT0Kev) / (real)1000.0;
        if (C.ElectronEscapeSoftAmp > 0.0 && C.ElectronEscapeSoftKev > 0.0)
        {
            share += (real)C.ElectronEscapeSoftAmp
                     * M_Exp(-te / (real)C.ElectronEscapeSoftKev);
        }

        real reach = range * M_Min((real)1.0, share);
        real ax, ay, az;
        Isotropic(ax, ay, az);
        real toEdge = CrystalPath(x, y, z, ax, ay, az);
        if (toEdge < reach)
        {
            real frac = toEdge / reach;
            real used = range * (C.ElectronEscapeCurve == 1.0
                ? frac : M_Pow(frac, (real)C.ElectronEscapeCurve));
            // ⚠ float: `range − used` — разность близких у самой границы (ранг М).
            escapedSelf = M_Min(
                ::EnergyOfRange(el, (range - used) * density),
                te - radiated);
            lost += escapedSelf;
        }
    }

    AddLight(te - radiated - escapedSelf, te);
    return lost;
}

// = EfficiencySimulator.cs:5778 PhotoElectronsSplit
RM_DEVF real Sim::PhotoElectronsSplit(real x, real y, real z, real e, int atomZ, int shell,
                                      real xray, int kLine, real casc, bool cascadeRolled, int depth,
                                      real ux, real uy, real uz)
{
    int relax = RelaxationOf(atomZ);
    real binding = relax < 0 ? (real)0.0 : ::BindingKev(D.relax[relax], shell);
    if (!(binding > (real)0.0) || e <= binding)
    {
        return ElectronLoss(x, y, z, e - xray - casc, depth, ElectronBirth::Photo, ux, uy, uz);
    }

    real lost = ElectronLoss(x, y, z, e - binding, depth, ElectronBirth::Photo, ux, uy, uz);
    lost += RelaxationElectrons(x, y, z, relax, shell, binding - xray - casc,
                                xray, kLine, casc, cascadeRolled, depth);
    return lost;
}

// = EfficiencySimulator.cs:5807 VacancyElectronsSplit
RM_DEVF real Sim::VacancyElectronsSplit(real x, real y, real z, real total, int atomZ,
                                        real bindingKev, real xray, int kLine, bool rolled, int depth,
                                        real edx, real edy, real edz)
{
    int relax = RelaxationOf(atomZ);
    int shell = relax < 0 ? 0 : ::ShellByBinding(D.relax[relax], bindingKev);
    real binding = shell > 0 ? ::BindingKev(D.relax[relax], shell) : (real)0.0;
    if (!(binding > (real)0.0) || total <= binding)
    {
        return ElectronLoss(x, y, z, total - xray, depth, ElectronBirth::Given, edx, edy, edz);
    }

    real lost = ElectronLoss(x, y, z, total - binding, depth, ElectronBirth::Given, edx, edy, edz);
    lost += RelaxationElectrons(x, y, z, relax, shell, binding - xray,
                                xray, kLine, (real)0.0, false, depth, rolled);
    return lost;
}

// = EfficiencySimulator.cs:5854 RelaxationElectrons
// ⚠ Стек `cascadeStack` — ОДИН на нить, как в C# один на симулятор (`int[] stack =
// this.cascadeStack`): вложенный вызов (через `ElectronLoss` → тормозное → `InCrystal` →
// фотопоглощение) пишет в него с нуля поверх незавершённого внешнего. Перенесено КАК ЕСТЬ.
RM_DEVF real Sim::RelaxationElectrons(real x, real y, real z, int relaxIndex,
                                      int shell, real budgetKev, real xray, int kLine, real casc,
                                      bool cascadeRolled, int depth, bool selfRolled)
{
    if (!(budgetKev > (real)0.0))
    {
        return (real)0.0;
    }

    const RelaxationG& relax = D.relax[relaxIndex];
    int* stack = cascadeStack;
    int top = 0;
    bool firstNonRadiative = false;
    int seed = shell;
    if (xray > (real)0.0)
    {
        // квант уже разыгран переносом: дырка переехала
        int next = shell == 1 && (kLine == 0 || kLine == 1)
            ? (kLine == 0 ? 6 : 5)
            : ::VacancyAfterPhoton(relax, shell, xray, Uniform());
        if (next <= 0)
        {
            AddLight(budgetKev, budgetKev);
            return (real)0.0;
        }

        seed = next;
        if (casc > (real)0.0)
        {
            int after = ::VacancyAfterPhoton(relax, next, casc, Uniform());
            if (after <= 0)
            {
                AddLight(budgetKev, budgetKev);
                return (real)0.0;
            }

            seed = after;
        }
        else
        {
            firstNonRadiative = cascadeRolled;
        }
    }
    else
    {
        firstNonRadiative = selfRolled;
    }

    stack[top++] = seed;
    real lost = (real)0.0;
    real blob = (real)0.0;
    real scored = (real)0.0;
    bool first = true;
    while (top > 0)
    {
        int v = stack[--top];
        bool radiative;
        real kev;
        int from, ejected;
        bool nonRad = first && firstNonRadiative;
        first = false;
        if (!::Step(relax, v, Uniform(), nonRad, radiative, kev, from, ejected))
        {
            // переходов нет: дырка садится на месте своей энергией связи
            blob += ::BindingKev(relax, v);
            scored += ::BindingKev(relax, v);
            continue;
        }

        if (radiative)
        {
            // квант каскада, которого перенос не разыгрывал, — на месте
            int target = ::AbsorbingShell(relax, kev);
            real pe = target > 0 ? kev - ::BindingKev(relax, target) : kev;
            scored += pe;
            if (pe >= (real)1.0)
            {
                lost += ElectronLoss(x, y, z, pe, depth);
            }
            else if (pe > (real)0.0)
            {
                blob += pe;
            }

            if (top + 2 > CascadeStackSize)
            {
                CountCascadeOverflow++;
                break;
            }

            stack[top++] = from;
            if (target > 0)
            {
                stack[top++] = target;
            }

            continue;
        }

        scored += kev;
        if (kev >= (real)1.0)
        {
            lost += ElectronLoss(x, y, z, kev, depth);
        }
        else if (kev > (real)0.0)
        {
            blob += kev;
        }

        if (top + 2 > CascadeStackSize)
        {
            CountCascadeOverflow++;
            break;
        }

        stack[top++] = from;
        stack[top++] = ejected;
    }

    // Баланс: всё, чего каскад не разложил, — в сгусток.
    // ⚠ float: `budgetKev − scored` — разность близких; порог 0.5 кэВ во float годен.
    blob += budgetKev - scored;
    if (M_Abs(budgetKev - scored) > (real)0.5)
    {
        CountCascadeOverflow++;
    }

    if (blob > (real)0.0)
    {
        AddLight(blob, blob);
    }

    return lost;
}

// = EfficiencySimulator.cs:8098 RelaxationOf (памятка по Z → прямо D.relaxByZ, длина 128).
// C# при z вне [0, 120) идёт мимо памятки в `MaterialDatabase.RelaxationOf(z)` — тот же объект.
RM_DEV int Sim::RelaxationOf(int z)
{
    if (z < 0 || z >= 128)
    {
        return -1;
    }

    return D.relaxByZ[z];
}

// = EfficiencySimulator.cs:8194 AddLight
RM_DEV void Sim::AddLight(real deposited, real te)
{
    if (D.scene->lightYield >= 0 && deposited > (real)0.0)
    {
        lightDeposit += deposited * ::Of(D.lightYields[D.scene->lightYield], te);
    }
}

// = EfficiencySimulator.cs:6501 Poisson
// (Зовут и ElectronTransport.cs:1131, 1460, 1482, 1540, 1562 и EfficiencySimulator.cs:7772 — полоса Г.)
RM_DEVF int Sim::Poisson(real mean)
{
    if (!(mean > (real)0.0))
    {
        return 0;
    }

    if (mean > (real)20.0)
    {
        mean = (real)20.0;
    }

    real limit = M_Exp(-mean), p = (real)1.0;
    int k = 0;
    while (k < 64)
    {
        p *= Uniform();
        if (p <= limit)
        {
            break;
        }

        k++;
    }

    return k;
}

// --- флуоресценция -----------------------------------------------------------------

// = EfficiencySimulator.cs:6001 SampleFluorescence
RM_DEVF real Sim::SampleFluorescence(int region, real energyKev)
{
    // ⚡ (`A43`, П45) Вещество — у области.
    const int material = D.regions[region].Material;
    const MaterialG& mg = D.materials[material];
    // (`A101`, `AMBER16`, `F11`) Признаки принадлежат ТОЛЬКО этому вызову.
    pendingCascadeKev = (real)0.0;
    lastXrayIsL = false;
    lastAbsorbZ = 0;
    lastAbsorbShell = 0;
    lastKLine = -1;
    lastCascadeRolled = false;
    // = FluorescersOf(material) (:1939) → D.fluorescers[MaterialG.fluorescers].
    if (!C.XrayEscape || mg.fluorescers < 0 || D.fluorescers[mg.fluorescers].ZLen == 0)
    {
        return (real)0.0;
    }

    const FluorescersG& f0 = D.fluorescers[mg.fluorescers];
    const int nF = f0.ZLen;
    if (nF > PhMaxElements)
    {
        __trap();                   // рабочий массив нити меньше таблицы — отказ
    }

    // Веса элементов-флуоресцентов и знаменатель — ОДНИМ проходом по составу.
    real sum = (real)0.0;
    real all = (real)0.0;
    real weight[PhMaxElements];     // = f0.Weight (буфер C#), длина nF
    int next = 0;
    int count = RegPrepareElements(region, energyKev);
    bool bySnapshot = count == mg.ZLen;     // = material.Fractions.Count
    int index = 0;
    for (int fi_ = 0; fi_ < mg.ZLen; fi_++)  // foreach (pair in material.Fractions)
    {
        const int key = II(mg.Z)[fi_];
        const real value = RR(mg.Fraction)[fi_];
        real photo = bySnapshot && RegElementZ(region, index) == key
            ? RegElementCrossSection(region, index, energyKev, PhotonProcess::Photoelectric, false)
            : ::MassCrossSection(key, energyKev, PhotonProcess::Photoelectric);
        index++;
        all += value * photo;
        if (next < nF && II(f0.Z)[next] == key)
        {
            // ⛔ (`A60`) Элемент годится и тогда, когда K закрыта, а L уже открыта.
            const FluorescenceG& fi = D.fluor[II(f0.Data)[next]];
            bool kOpen = energyKev > fi.KEdgeKev;
            bool lOpen = C.LXrayEscape && ::HasL(fi)
                && energyKev > RR(fi.LEdgeKev)[fi.LEdgeKevLen - 1];
            if (kOpen || lOpen)
            {
                weight[next] = RR(f0.Fraction)[next] * photo;
                sum += weight[next];
            }
            else
            {
                weight[next] = (real)0.0;     // ни одной доступной оболочки
            }

            next++;
        }
    }

    if (next != nF)
    {
        // подпоследовательность не сошлась — прежний путь, два прохода
        sum = (real)0.0;
        for (int i = 0; i < nF; i++)
        {
            const FluorescenceG& fi = D.fluor[II(f0.Data)[i]];
            bool kOpen = energyKev > fi.KEdgeKev;
            bool lOpen = C.LXrayEscape && ::HasL(fi)
                && energyKev > RR(fi.LEdgeKev)[fi.LEdgeKevLen - 1];
            if (!kOpen && !lOpen)
            {
                weight[i] = (real)0.0;
                continue;
            }

            weight[i] = RR(f0.Fraction)[i] * ::MassCrossSection(
                II(f0.Z)[i], energyKev, PhotonProcess::Photoelectric);
            sum += weight[i];
        }

        all = (real)0.0;
        for (int fi_ = 0; fi_ < mg.ZLen; fi_++)
        {
            all += RR(mg.Fraction)[fi_] * ::MassCrossSection(
                II(mg.Z)[fi_], energyKev, PhotonProcess::Photoelectric);
        }
    }

    if (!(sum > (real)0.0))
    {
        return (real)0.0;
    }

    if (!(all > (real)0.0) || Uniform() * all >= sum)
    {
        return (real)0.0;
    }

    real pick = Uniform() * sum;
    int k = 0;
    while (k < nF - 1 && pick >= weight[k])
    {
        pick -= weight[k];
        k++;
    }

    const int fIndex = II(f0.Data)[k];
    const FluorescenceG& f = D.fluor[fIndex];
    lastAbsorbZ = II(f0.Z)[k];
    const int shellIndex = II(f0.Shells)[k];   // −1 = null

    // Доля K-оболочки: по энергии из EPICS2017, если данные есть; иначе — константа.
    // ⚠ float: фиты EPICS (EvalFit, x⁶ ~ 7e8 при коэффициентах разных знаков) — полоса А,
    // map_data §4 п.5.
    real kFraction = shellIndex >= 0
        ? ::KFraction(D.photoShell[shellIndex], energyKev)
        : f.KFraction;

    real omega = ::Omega(f, C.MeasuredFluorescenceYield);
    real u = Uniform();
    if (u < kFraction * omega)
    {
        CountKXray++;
        int line;
        real kev = PickLineAt(Uniform(), RR(f.LineKev), f.LineKevLen, RR(f.LineWeight), f.LineWeightLen, line);
        if (C.KLCascade)
        {
            CascadeAfterK(fIndex, shellIndex, line, energyKev);
        }

        lastAbsorbShell = 1;
        lastKLine = line;
        // не перенесено: traceZ/traceShell/traceXrayKev (трассировка).
        return kev;
    }

    // ⛔ (`A60`) L-СЕРИЯ. Случайное число ТО ЖЕ САМОЕ, что решало судьбу K.
    if (!C.LXrayEscape || !::HasL(f) || shellIndex < 0)
    {
        // оже-электрон либо нет данных L
        lastAbsorbShell = PickShellWithoutXray(fIndex, nullptr, energyKev, kFraction, omega);
        return (real)0.0;
    }

    // Все три доли — ОДНИМ вызовом (`A60`); null C# → false.
    real lFrac[3];
    if (!::LFractions(D.photoShell[shellIndex], energyKev, lFrac))
    {
        lastAbsorbShell = PickShellWithoutXray(fIndex, nullptr, energyKev, kFraction, omega);
        return (real)0.0;
    }

    real edge = kFraction * omega;
    for (int li = 0; li < f.OmegaLLen && li < 3; li++)   // 3 = lFrac.Length
    {
        // (`M9`) Выход дырки — ПОЛНЫЙ, с переходами Костера—Кронига.
        real yield = ::LYield(f, li, C.LYieldSupply);
        if (!(yield > (real)0.0) || ::TblRowLen(f.LineKevLOff, li) == 0   // LineKevL[li] == null
            || energyKev <= RR(f.LEdgeKev)[li])
        {
            continue;               // подоболочка закрыта на этой энергии
        }

        edge += lFrac[li] * yield;
        if (u < edge)
        {
            CountLXray++;
            // (`M9`) Линия — той подоболочки, КУДА дырка доехала.
            int emit = LSubshellAfterCk(fIndex, li);
            real lkev = PickLine(Uniform(),
                                 ::TblRow(f.LineKevL, f.LineKevLOff, emit), ::TblRowLen(f.LineKevLOff, emit),
                                 ::TblRow(f.LineWeightL, f.LineWeightLOff, emit), ::TblRowLen(f.LineWeightLOff, emit));
            // (`AMBER16` п. 1) ЕДИНСТВЕННОЕ место, где признак серии становится истиной.
            lastXrayIsL = true;
            lastAbsorbShell = emit == 0 ? 3 : (emit == 1 ? 5 : 6);   // EADL: L1=3, L2=5, L3=6
            return lkev;
        }
    }

    // оже-электрон
    lastAbsorbShell = PickShellWithoutXray(fIndex, lFrac, energyKev, kFraction, omega);
    return (real)0.0;
}

// = EfficiencySimulator.cs:6250 PickShellWithoutXray
// `lFrac` — три доли или nullptr (C# null).
RM_DEVF int Sim::PickShellWithoutXray(int fIndex, const real* lFrac,
                                      real energyKev, real kFraction, real omega)
{
    if (!C.LightCascadeSplit)
    {
        return 0;
    }

    const FluorescenceG& f = D.fluor[fIndex];
    real wK = energyKev > f.KEdgeKev ? kFraction * ((real)1.0 - omega) : (real)0.0;
    real wL1 = (real)0.0, wL2 = (real)0.0, wL3 = (real)0.0;
    real lSum = (real)0.0;
    if (lFrac != nullptr && ::HasL(f))
    {
        for (int li = 0; li < 3 && li < 3 && li < f.OmegaLLen; li++)   // 3 = lFrac.Length
        {
            if (li < f.LEdgeKevLen && energyKev <= RR(f.LEdgeKev)[li])
            {
                continue;
            }

            // (`M9`) «Не квант» — против ПОЛНОГО выхода дырки.
            real w = lFrac[li] * ((real)1.0 - ::LYield(f, li, C.LYieldSupply));
            if (li == 0) wL1 = w; else if (li == 1) wL2 = w; else wL3 = w;
            lSum += lFrac[li];
        }
    }

    real wRest = M_Max((real)0.0, (real)1.0 - kFraction - lSum);
    real total = wK + wL1 + wL2 + wL3 + wRest;
    if (!(total > (real)0.0))
    {
        return 0;
    }

    real pick = Uniform() * total;
    if (pick < wK) return 1;
    pick -= wK;
    if (pick < wL1) return 3;
    pick -= wL1;
    if (pick < wL2) return 5;
    pick -= wL2;
    if (pick < wL3) return 6;
    // M и глубже: самая глубокая подоболочка за L3 с краем ниже энергии
    int relax = RelaxationOf(lastAbsorbZ);
    return relax < 0 ? 0 : ::AbsorbingShell(D.relax[relax], energyKev, 7);
}

// = EfficiencySimulator.cs:6312 CascadeAfterK
RM_DEVF void Sim::CascadeAfterK(int fIndex, int shell, int kLine, real energyKev)
{
    const FluorescenceG& f = D.fluor[fIndex];
    if (!C.LXrayEscape || !::HasL(f) || shell < 0)
    {
        return;                     // L-канала нет — каскаду некуда идти
    }

    // Kα1 -> L3 (индекс 2), Kα2 -> L2 (индекс 1), Kβ -> мимо L.
    int li = kLine == 0 ? 2 : (kLine == 1 ? 1 : -1);
    real yield = li < 0 ? (real)0.0 : ::LYield(f, li, C.LYieldSupply);
    if (li < 0 || li >= f.OmegaLLen || ::TblRowLen(f.LineKevLOff, li) == 0   // LineKevL[li] == null
        || !(yield > (real)0.0))
    {
        return;
    }

    // ⚠ Подоболочка обязана быть ОТКРЫТА на этой энергии.
    if (li < f.LEdgeKevLen && energyKev <= RR(f.LEdgeKev)[li])
    {
        return;
    }

    CountKLVacancy++;
    lastCascadeRolled = true;
    if (Uniform() >= yield)
    {
        return;                     // ответил оже-электрон
    }

    CountKLCascade++;
    int emit = LSubshellAfterCk(fIndex, li);
    pendingCascadeKev =
        PickLine(Uniform(),
                 ::TblRow(f.LineKevL, f.LineKevLOff, emit), ::TblRowLen(f.LineKevLOff, emit),
                 ::TblRow(f.LineWeightL, f.LineWeightLOff, emit), ::TblRowLen(f.LineWeightLOff, emit));
}

// = EfficiencySimulator.cs:6371 LSubshellAfterCk
RM_DEVF int Sim::LSubshellAfterCk(int fIndex, int li)
{
    int level = C.LYieldSupply;
    if (level == 0 || li >= 2)
    {
        return li;
    }

    const FluorescenceG& f = D.fluor[fIndex];
    real own = ::OmegaLAt(f, li, level);
    real total = ::LYield(f, li, level);
    if (!(total > own) || Uniform() * total < own)
    {
        return li;                  // ответила своей линией
    }

    if (li == 1)
    {
        return ::TblRowLen(f.LineKevLOff, 2) != 0 ? 2 : li;      // L2 → L3
    }

    // L1: на L2 (и, быть может, дальше) или сразу на L3
    real toL2 = ::CkAt(f, 0, level) * ::LYield(f, 1, level);
    real toL3 = ::CkAt(f, 1, level) * ::OmegaLAt(f, 2, level);
    if (!(toL2 + toL3 > (real)0.0))
    {
        return li;
    }

    if (Uniform() * (toL2 + toL3) < toL2)
    {
        real own2 = ::OmegaLAt(f, 1, level);
        real total2 = ::LYield(f, 1, level);
        int at = !(total2 > own2) || Uniform() * total2 < own2 ? 1 : 2;
        return ::TblRowLen(f.LineKevLOff, at) != 0 ? at
             : (::TblRowLen(f.LineKevLOff, 1) != 0 ? 1 : li);
    }

    return ::TblRowLen(f.LineKevLOff, 2) != 0 ? 2 : li;
}

// = EfficiencySimulator.cs:6411 PickLine
RM_DEV real Sim::PickLine(real pick, const real* kev, int kevLen, const real* weight, int weightLen)
{
    int line;
    return PickLineAt(pick, kev, kevLen, weight, weightLen, line);
}

// = EfficiencySimulator.cs:6424 PickLineAt
// ⚠ float: накопление весов 0…1 годно (map_data §4 п.16).
RM_DEV real Sim::PickLineAt(real pick, const real* kev, int kevLen, const real* weight, int weightLen,
                            int& line)
{
    real acc = (real)0.0;
    for (int i = 0; i < weightLen; i++)
    {
        acc += weight[i];
        if (pick < acc)
        {
            line = i;
            return kev[i];
        }
    }

    line = kevLen - 1;
    return kev[kevLen - 1];
}

// = EfficiencySimulator.cs:6451 VacancyXray
RM_DEVF real Sim::VacancyXray(int material, int z, real bindingKev)
{
    // (`F11` (а), П17) Признаки принадлежат ТОЛЬКО этому вызову.
    lastVacancyLine = -1;
    lastVacancyRolled = false;
    if (!C.XrayEscape || z <= 0 || !(bindingKev > (real)0.0) || material < 0)
    {
        return (real)0.0;
    }

    const int fl = D.materials[material].fluorescers;   // = FluorescersOf(material)
    if (fl < 0)
    {
        return (real)0.0;           // таблица пуста — цикл C# не входит ни разу
    }

    const FluorescersG& f0 = D.fluorescers[fl];
    for (int i = 0; i < f0.ZLen; i++)
    {
        if (II(f0.Z)[i] != z)
        {
            continue;
        }

        const FluorescenceG& f = D.fluor[II(f0.Data)[i]];
        if (!(f.KEdgeKev > (real)0.0) || bindingKev < (real)0.9 * f.KEdgeKev)
        {
            return (real)0.0;       // вакансия не на K
        }

        lastVacancyRolled = true;
        if (Uniform() >= ::Omega(f, C.MeasuredFluorescenceYield))
        {
            return (real)0.0;       // оже-электрон: энергия осела на месте
        }

        real line = Uniform();
        real acc = (real)0.0;
        for (int k = 0; k < f.LineWeightLen; k++)
        {
            acc += RR(f.LineWeight)[k];
            if (line < acc)
            {
                lastVacancyLine = k;
                return RR(f.LineKev)[k];
            }
        }

        lastVacancyLine = f.LineKevLen - 1;
        return RR(f.LineKev)[f.LineKevLen - 1];
    }

    return (real)0.0;
}

// --- рассеяние -------------------------------------------------------------------------

// = EfficiencySimulator.cs:6548 AnalogMu
RM_DEV real Sim::AnalogMu(int region, real energyKev)
{
    return RegMu(region, energyKev, !C.RayleighScatter);
}

// = EfficiencySimulator.cs:6615 PickAtom
// Памятки `Scatterers.Product*/Energy*/Total*` (:6636-6645, :6691-6700) НЕ перенесены
// (README): вклады считаются заново тем же порядком слагаемых — числа те же до бита.
// `ScatterersOf(material)` (:6554) → D.scatterers[MaterialG.scatterers].
RM_DEVF int Sim::PickAtom(int material, real energyKev, PhotonProcess process, int region)
{
    const ScatterersG& s = D.scatterers[D.materials[material].scatterers];
    int n = s.AtomLen;
    if (n == 0)
    {
        return -1;
    }

    if (n == 1)
    {
        return II(s.Atom)[0];
    }

    if (n > PhMaxElements)
    {
        __trap();                   // рабочий массив нити меньше таблицы — отказ
    }

    real product[PhMaxElements];
    real total = (real)0.0;
    bool done = false;
    if (region >= 0 && D.regions[region].Material == material)   // ReferenceEquals(region.Material, material)
    {
        int count = RegPrepareElements(region, energyKev);
        int at = 0;
        int matched = 0;
        for (int i = 0; i < n && count >= 0; i++)
        {
            while (at < count && RegElementZ(region, at) != II(s.Z)[i])
            {
                at++;
            }

            if (at >= count)
            {
                break;
            }

            product[i] = RR(s.MassFraction)[i]
                         * RegElementCrossSection(region, at, energyKev, process, false);
            total += product[i];
            at++;
            matched++;
        }

        done = matched == n;
    }

    if (!done)
    {
        total = (real)0.0;
        for (int i = 0; i < n; i++)
        {
            product[i] = RR(s.MassFraction)[i]
                         * ::MassCrossSection(II(s.Z)[i], energyKev, process);
            total += product[i];
        }
    }

    if (!(total > (real)0.0))
    {
        return II(s.Atom)[0];
    }

    real pick = Uniform() * total;
    real running = (real)0.0;
    for (int i = 0; i < n; i++)
    {
        running += product[i];
        if (pick <= running)
        {
            return II(s.Atom)[i];
        }
    }

    return II(s.Atom)[n - 1];
}

// = EfficiencySimulator.cs:6761 ComptonScatter (область, без вакансии)
RM_DEVF real Sim::ComptonScatter(int region, real energyKev, real& cos)
{
    real vacancyKev;
    int vacancyZ;
    return ComptonScatter(D.regions[region].Material, energyKev, cos, vacancyKev, vacancyZ, region);
}

// = EfficiencySimulator.cs:6768 ComptonScatter (область, с вакансией)
RM_DEVF real Sim::ComptonScatter(int region, real energyKev, real& cos, real& vacancyKev, int& vacancyZ)
{
    return ComptonScatter(D.regions[region].Material, energyKev, cos, vacancyKev, vacancyZ, region);
}

// = EfficiencySimulator.cs:6774 ComptonScatter
RM_DEVF real Sim::ComptonScatter(int material, real energyKev, real& cos, real& vacancyKev, int& vacancyZ,
                                 int region)
{
    vacancyKev = (real)0.0;
    vacancyZ = 0;
    int atom = -1;
    if ((C.BoundCompton || C.DopplerBroadening) && material >= 0)
    {
        atom = PickAtom(material, energyKev, PhotonProcess::Incoherent, region);
    }

    cos = ComptonCosine(energyKev, C.BoundCompton ? atom : -1);
    real free = energyKev / ((real)1.0 + energyKev / ElectronMassKev * ((real)1.0 - cos));
    if (!C.DopplerBroadening || atom < 0 || ::ShellCount(D.atoms[atom]) == 0)
    {
        return free;
    }

    vacancyZ = D.atoms[atom].Z;
    return DopplerEnergy(atom, energyKev, cos, free, vacancyKev);
}

// = EfficiencySimulator.cs:6811 DopplerEnergy
RM_DEVF real Sim::DopplerEnergy(int atomIndex, real energyKev, real cos, real free, real& vacancyKev)
{
    const AtomG& atom = D.atoms[atomIndex];
    vacancyKev = (real)0.0;
    real a = energyKev / ElectronMassKev;
    real var2 = (real)1.0 + a * ((real)1.0 - cos);
    for (int guard = 0; guard < 64; guard++)
    {
        int shell = ::SelectShell(atom, Uniform());
        real binding = ::ShellBindingKev(atom, shell);
        if (binding >= energyKev)
        {
            continue;                  // оболочка кванту не по зубам
        }

        real q = (real)ScatteringData::FineStructure
                 * ::SampleMomentumAu(atom, shell, Uniform());
        if (Uniform() < (real)0.5)
        {
            q = -q;
        }

        real q2 = q * q;
        real var3 = var2 * var2 - q2;
        real var4 = var2 - q2 * cos;
#ifdef RM_REAL_FLOAT
        // ⚠ float: `var4² − var3 + q²·var3` — вычитание почти равных (var4² ≈ var3 ≈ var2²,
        // разность O(q²) ~ 1e-6…2e-3): во float знак и корень случайны (map_data §4 п.3).
        // Тождество БЕЗ вычитания (раскрытие тех же скобок):
        //   disc = q²·[(var2 − cos)² + (1 − cos²)·(1 − q²)] — все слагаемые ≥ 0 при |q| < 1.
        // Только во float: ступень 1 (double) считает формулой C# до бита.
        real dv = var2 - cos;
        real disc = q2 * (dv * dv + ((real)1.0 - cos * cos) * ((real)1.0 - q2));
#else
        real disc = var4 * var4 - var3 + q2 * var3;
#endif
        if (!(var3 > (real)0.0) || !(disc >= (real)0.0))
        {
            continue;
        }

        real root = M_Sqrt(disc);
        real eps = Consistent((var4 - root) / var3, var2, q);
        if (!(eps > (real)0.0))
        {
            eps = Consistent((var4 + root) / var3, var2, q);
        }

        if (!(eps > (real)0.0) || eps > (real)1.0)
        {
            continue;
        }

        real scattered = eps * energyKev;
        if (scattered > energyKev - binding)
        {
            continue;
        }

        vacancyKev = binding;      // `A61`: с этой оболочки сбит электрон
        return scattered;
    }

    return free;
}

// = EfficiencySimulator.cs:6891 Consistent
// ⚠ float: порог `|1 − eps·var2| < 1e-12` во float смысла не имеет (ULP(1.0f) = 1.2e-7):
// «оба корня совпали» не распознаётся, выбор корня — по знаку остатка (map_data §4 п.3).
RM_DEV real Sim::Consistent(real eps, real var2, real q)
{
    if (!(eps > (real)0.0))
    {
        return (real)-1.0;
    }

    real residual = (real)1.0 - eps * var2;
    if (M_Abs(residual) < (real)1e-12)
    {
        return eps;                    // q ≈ 0, оба корня совпали
    }

    return (residual > (real)0.0) == (q > (real)0.0) ? eps : (real)-1.0;
}

// = EfficiencySimulator.cs:6920 RayleighCosine (область)
RM_DEVF real Sim::RayleighCosine(int region, real energyKev)
{
    return RayleighCosine(D.regions[region].Material, energyKev, region);
}

// = EfficiencySimulator.cs:6925 RayleighCosine
RM_DEVF real Sim::RayleighCosine(int material, real energyKev, int region)
{
    int atom = PickAtom(material, energyKev, PhotonProcess::Coherent, region);
    real xMax = (real)ScatteringData::InverseCmPerKev * energyKev;
    // ⚠ float: tMax до (8.07e6·3000)² ≈ 6e20 — во float представимо; ffCum до ~1e37 у края
    // float — обрезка таблиц при упаковке (map_data §4 п.6), розыгрыш — полоса А.
    real tMax = xMax * xMax;
    if (atom < 0 || !(tMax > (real)0.0))
    {
        return (real)2.0 * Uniform() - (real)1.0;
    }

    for (int guard = 0; guard < 1000; guard++)
    {
        real t = ::SampleMomentumTransferSq(D.atoms[atom], Uniform(), tMax);
        real cos = (real)1.0 - (real)2.0 * t / tMax;
        if (cos < (real)-1.0) cos = (real)-1.0;
        if (cos > (real)1.0) cos = (real)1.0;
        if (Uniform() <= (real)0.5 * ((real)1.0 + cos * cos))
        {
            return cos;
        }
    }

    return (real)1.0;
}

// = EfficiencySimulator.cs:6957 ComptonCosine (с отбором S(x,Z)/Z)
RM_DEVF real Sim::ComptonCosine(real energyKev, int atomIndex)
{
    if (atomIndex < 0)
    {
        return ComptonCosine(energyKev);
    }

    const AtomG& atom = D.atoms[atomIndex];
    real k = (real)ScatteringData::InverseCmPerKev * energyKev;
    for (int guard = 0; guard < 1000; guard++)
    {
        real cos = ComptonCosine(energyKev);
        // ⚠ float: `1 − cos` у направления вперёд — угол < ~5e-4 рад неразличим (ранг С).
        real x = k * M_Sqrt(M_Max((real)0.0, (real)0.5 * ((real)1.0 - cos)));
        if (Uniform() * (real)atom.Z <= ::ScatteringFunction(atom, x))
        {
            return cos;
        }
    }

    return (real)-1.0;
}

// = EfficiencySimulator.cs:6979 ComptonCosine (метод Кана)
RM_DEVF real Sim::ComptonCosine(real energyKev)
{
    real a = energyKev / ElectronMassKev;
    real a1 = (real)1.0 + (real)2.0 * a;
    for (int guard = 0; guard < 1000; guard++)
    {
        // C#: `r1 = U(), r2 = U(), r3 = U()` — три выдачи по порядку.
        real r1 = Uniform();
        real r2 = Uniform();
        real r3 = Uniform();
        real ratio;
        if (r1 <= ((real)1.0 + (real)2.0 * a) / ((real)9.0 + (real)2.0 * a))
        {
            ratio = (real)1.0 + (real)2.0 * a * r2;
            if (r3 <= (real)4.0 * ((real)1.0 / ratio - (real)1.0 / (ratio * ratio)))
            {
                // ⚠ float: `(ratio − 1)/a` при малом a — разность близких (ранг С).
                return (real)1.0 - (ratio - (real)1.0) / a;
            }
        }
        else
        {
            ratio = a1 / ((real)1.0 + (real)2.0 * a * r2);
            real cos = (real)1.0 - (ratio - (real)1.0) / a;
            if (r3 <= (real)0.5 * (cos * cos + (real)1.0 / ratio))
            {
                return cos;
            }
        }
    }

    return (real)1.0;
}
