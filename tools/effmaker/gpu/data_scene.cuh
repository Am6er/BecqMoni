// data_scene.cuh — неизменяемые данные СЦЕНЫ на устройстве: вещества сцены с кэшами
// симулятора по веществу, флуоресценты, рассеиватели, элементы слоёв, области, сцена
// и источник. Полоса П221 (`AMBER160`), часть Б. Соглашения — README.md («Данные
// устройства»): массивы — СМЕЩЕНИЯ в арены `R`/`I`/`B` (`RR(off)[i]`, `II(off)[i]`),
// длина — соседнее поле `<Имя>Len`; ссылка на объект данных — индекс в таблицу `D`
// (−1 = null).
//
// Пишет `tools/effmaker/probes/GpuPackScene.cs` (WriteScene), читает `host_scene.h`.
// Порядок полей в записи — порядок записи там; рассинхрон ловит `End()`.
#pragma once
#include "common.cuh"

// Предел областей сцены на устройстве. Раскладка `Build` даёт не больше 13
// (`map_geometry.md` §1.4), корпус — не больше 12; C# держит 64 (маска `ulong`).
// Больше — отказ упаковки в `Read_regions`, а не тихая порча.
#define RM_MAX_REG 16

// Событий разбора луча — как `4 * count + 4` у `CollectCrossings`
// (EfficiencySimulator.cs:4477), на пределе областей.
#define RM_MAX_EVENTS (4 * RM_MAX_REG + 4)

// Предел СУММЫ элементов снимков составов по всем областям — под массивы пары
// узлов `bracketLo/Hi` нити (`Region.EnsureBracket`). Больше — отказ `Read_regions`.
#define RM_MAX_SNAP_TOTAL 160

// PhotonProcess (`PartialCrossSections.cs:6`) определён полосой А в data_tables.cuh
// (`enum class PhotonProcess`), который data.cuh включает РАНЬШЕ этого файла.

// Вид розыгрыша точки вылета (`Sampler` и наследники, EfficiencySimulator.cs:2982).
// ISO (`IsoFieldSampler`) и важностный (`ImportanceSampler`) НЕ перенесены:
// упаковка на них отказывает исключением (`GpuPackScene.cs`).
#define RM_SOURCE_POINT 0
#define RM_SOURCE_CYLINDER 1
#define RM_SOURCE_MARINELLI 2
#define RM_SOURCE_BOX 3

// = `GeometryMaterial` (GeometryModel.cs:43) + кэши симулятора ПО ВЕЩЕСТВУ —
// всё, что горячие пути `EfficiencySimulator.cs`/`ElectronTransport.cs` спрашивают
// словарём по веществу. Индекс вещества = порядок первой встречи в `regionArray`
// (ссылочное равенство — как ключ `Dictionary<GeometryMaterial, …>` симулятора;
// `GeometryMaterial` не переопределяет `Equals`).
struct MaterialG
{
    real Density;

    // Состав В ТОМ ПОРЯДКЕ, в каком перечисляет `Fractions` (после `SortFractions`,
    // EfficiencySimulator.cs:1756; `A162` — порядок есть вход розыгрыша элемента),
    // ВСЕ записи, включая нулевые доли (так снимает `Region.Snapshot`).
    int Z;        int ZLen;          // I
    int Fraction; int FractionLen;   // R
    // Индекс в `D.elements` на каждый Z (= `MaterialDatabase.TryGet`, −1 — нет в
    // поставке). Не пишется C#: читатель берёт из `elementsByZ` полосы А.
    int Element;  int ElementLen;    // I

    int fluorescers;    // = FluorescersOf(material)  (EfficiencySimulator.cs:1939) → D.fluorescers
    int scatterers;     // = ScatterersOf(material)   (EfficiencySimulator.cs:6554) → D.scatterers
    // Имена полей ниже — имена методов-кэшей C# (так их читает полоса Г, sim_electron.cuh).
    int CarryMedium;    // = CarryMedium(material)    (EfficiencySimulator.cs:7691) → D.electronMats
    int LayerBrem;      // = LayerBrem(material)      (EfficiencySimulator.cs:7716) → D.brems, −1 — null

    // = LayerRadiationLength(material) (ElectronTransport.cs:193), г/см².
    real LayerRadiationLength;
    // = LayerScatterElements(material) (ElectronTransport.cs:300): подряд лежащие
    // записи `D.scatterElements[LayerScatterElements … + LayerScatterElementsLen)`.
    int LayerScatterElements; int LayerScatterElementsLen;
    // = LayerBremZ(material) (ElectronTransport.cs:718).
    real LayerBremZ;
};

// = `EfficiencySimulator.Fluorescers` (EfficiencySimulator.cs:1675), без буфера
// `Weight`: тот — рабочий массив розыгрыша, на устройстве он у нити (полоса фотона).
struct FluorescersG
{
    int Material;                    // → D.materials
    int Z;        int ZLen;          // I
    int Fraction; int FractionLen;   // R
    int Data;     int DataLen;       // I: → D.fluor  (= MaterialDatabase.FluorescenceOf(Z))
    int Shells;   int ShellsLen;     // I: → D.photoShell, −1 — null (ключ KFractionByEnergy ВЫКЛ или данных нет)
};

// = `EfficiencySimulator.Scatterers` (EfficiencySimulator.cs:1697). Памятки
// `ProductIncoherent/Coherent` и их энергии НЕ переносятся (README: памятки, не
// меняющие чисел, считаются заново).
struct ScatterersG
{
    int Z;            int ZLen;            // I
    int MassFraction; int MassFractionLen; // R
    int Atom;         int AtomLen;         // I: → D.atoms (= ScatteringData.Of(Z))
};

// = `ElectronTransport.ScatterElement` (ElectronTransport.cs:274).
struct ScatterElementG
{
    int Z;
    real AtomsPerCm3;
    real Z13;
    real ZZ1;
    bool Mott;
};

// = `EfficiencySimulator.Region` (EfficiencySimulator.cs:2023) БЕЗ кэшей на
// энергию (те — поля `Sim`, decl_geom.inc). Снимок состава `Snapshot`
// (EfficiencySimulator.cs:2083) — неизменяем после первого обращения, поэтому здесь:
// он совпадает с составом вещества области, и смещения указывают в те же массивы.
struct RegionG
{
    bool IsBox;
    real RIn, ROut;      // кольцо
    real AX, AY;         // полуразмеры бруса
    real ZMin, ZMax;
    int Material;        // → D.materials
    bool IsCrystal;
    bool ThresholdPair;  // = XcomPairThreshold симулятора (Register, :2649)
    real PhotoScale;     // 1 у кристалла, иначе OutsidePhotoScale (Register, :2650)

    // --- снимок состава (`Snapshot`) ---
    int SnapElement;     // I: → D.elements, −1 — нет в поставке (`elements[i] == null`)
    int SnapZ;           // I: `elementZ`
    int SnapFraction;    // R: `fractions`
    int SnapLen;         // `elements.Length`
    real SnapDensity;    // `density`
    // Начало элементов этой области в массивах пары узлов нити
    // (`Sim::bracketLo/Hi`), сумма SnapLen предыдущих областей; ставит читатель.
    int SnapBase;

    // --- плоская геометрия с вычтенным Eps (`regZMinE…regAYE`,
    // EfficiencySimulator.cs:1818-1839). Значения — ИЗ МАССИВОВ СИМУЛЯТОРА
    // (вычитание сделано в double), а не пересчитаны здесь.
    // ⚠ float: Eps = 1e-9 см ниже ULP float уже при |z| ≈ 0.01 см — во float
    // эти числа равны (float)ZMin и т.д.; полуоткрытость держится порядком сравнений.
    bool regBox;
    real regZMinE, regZMaxE, regROutE, regRInE, regAXE, regAYE;
};

// Поля сцены симулятора, которые читают горячие пути. Ровно одна запись.
struct SceneG
{
    int crystal;              // = regionArray.IndexOf(crystal) (:1568), −1 — нет
    int crystalMaterial;      // = geometry.Crystal → D.materials (CrystalChannels :3602, VacancyXray :5406, :5562)

    real sphereZ, sphereR;    // :1569, Build :2831-2833
    real pathSceneZ, pathSceneR; // :1574, EnsureBuilt :1796
    // :1580; у пустой сцены sceneZMin = double.MaxValue, sceneZMax = double.MinValue.
    // ⚠ float: double.MaxValue во float — бесконечность; SceneBounds сравнивает
    // `sceneZMin > sceneZMax` — смысл сохраняется.
    real sceneRMax, sceneZMin, sceneZMax;

    // --- источник (`source`, Sampler) ---
    int sourceKind;           // RM_SOURCE_*
    real PointZ;              // PointSampler.z (:3131)
    real CylR, CylZ0, CylZ1;  // CylinderSampler.r, z0, z1 (:3186)
    real MarRIn, MarROut, MarZ0, MarZ1, MarZCap, MarCapFraction; // MarinelliSampler (:3215-3216)
    real BoxAX, BoxAY, BoxZ0, BoxZ1; // BoxSampler.ax, ay, z0, z1 (:3159)

    int electron;             // :1647 → D.electronMats, −1 — null
    int bremTable;            // :1657 → D.brems, −1 — null
    int lightYield;           // :1650 → D.lightYields, −1 — null (шкала пропорциональна)
    int waterTable;           // = WaterTable() (:7683) → D.electronMats — таблица пустоты
    bool crystalHasPartials;  // :1646
    real crystalRadiationLength; // = CrystalRadiationLength() (ElectronTransport.cs:156), г/см²
};
