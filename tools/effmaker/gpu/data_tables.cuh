// data_tables.cuh — структуры-зеркала классов ДАННЫХ горячего пути: сечения XCOM,
// флуоресценция K/L, EPICS по оболочкам, разрядка EADL, рассеяние на атоме, ESTAR,
// тормозное толстой/тонкой мишени, кривая света. Полоса П221 (`AMBER160`), полоса А.
//
// Правила (README.md, «Данные устройства»; data.cuh):
// * имя поля = имя поля C#; скаляр `double` → `real`;
// * массив `T[] Name` → `int Name` (СМЕЩЕНИЕ в арену R/I/B) + `int NameLen`;
//   ⚠ null в C# упаковывается как длина 0 — проверки «!= null» переносятся как «Len > 0»;
// * зубчатый `T[][] Name` → `int Name` (смещение плоского) + `int NameOff` (смещение
//   n + 1 начал строк в арене I, относительно плоского) + `int NameLen` (число строк);
//   строка null — длина 0;
// * ссылка на объект → индекс в таблицу `D` (−1 = null).
// Первое поле записей, найденных по Z (`*ByZ`), — `Z`.
//
// Читатель упаковки — host_tables.h, писатель — tools/effmaker/probes/GpuPackTables.cs:
// порядок полей в потоке задан ими, а НЕ порядком объявления здесь.
#pragma once
#include "common.cuh"

// = PartialCrossSections.cs:6 PhotonProcess — тот же порядок, что индекс LogChannels.
enum class PhotonProcess : int
{
    Coherent = 0,
    Incoherent = 1,
    Photoelectric = 2,
    PairProduction = 3
};

// = MaterialDatabase.cs:35 MaterialDatabase.Element
// Не перенесены (горячий путь не читает, `AMBER74`): Total, LogTotal.
struct ElementG
{
    int Z;
    int EnergyKev; int EnergyKevLen;              // кэВ, строго по возрастанию
    real AtomicWeight;
    int Channels; int ChannelsOff; int ChannelsLen;          // [5][N]
    int LogEnergyKev; int LogEnergyKevLen;
    int LogChannels; int LogChannelsOff; int LogChannelsLen; // [4][N], NaN где ≤ 0
    int LogPairNuclearShape; int LogPairNuclearShapeLen;     // NaN где канал закрыт
    int LogPairElectronShape; int LogPairElectronShapeLen;
};

// = MaterialDatabase.cs:165 MaterialDatabase.Fluorescence
struct FluorescenceG
{
    int Z;
    real KEdgeKev;
    real KFraction;
    real OmegaK;
    real OmegaKMeasured;
    int LineKev; int LineKevLen;                  // Kα1, Kα2, Kβ
    int LineWeight; int LineWeightLen;
    int LEdgeKev; int LEdgeKevLen;                // L1, L2, L3
    int OmegaL; int OmegaLLen;
    int OmegaLSupply; int OmegaLSupplyLen;        // Len 0 = null
    int CkEadl; int CkEadlLen;
    int CkSupply; int CkSupplyLen;                // Len 0 = null
    int LineKevL; int LineKevLOff; int LineKevLLen;          // [3][≈10…30]
    int LineWeightL; int LineWeightLOff; int LineWeightLLen;
    // Свойство C# `HasL` (MaterialDatabase.cs:329) проверяет четыре ссылки на null; null
    // в упаковке неотличим от пустого массива, поэтому значение свойства кладёт писатель.
    int hasL;
};

// = MaterialDatabase.cs:353 MaterialDatabase.PhotoShellModel
// Не перенесено: памятка `lastFracs` (Memo[64]) — значение считается заново (README).
struct PhotoShellModelG
{
    int Z;
    real kEdgeKev;
    real lowFromKev, highFromKev;
    int lowK; int lowKLen;                        // a1..a6
    int lowTotal; int lowTotalLen;
    int highK; int highKLen;
    int highTotal; int highTotalLen;
    int tableE; int tableEOff; int tableELen;     // [оболочка][узлы], кэВ
    int tableCs; int tableCsOff; int tableCsLen;  // барн
    int logTableE; int logTableEOff; int logTableELen;    // Len 0 — логарифмов нет (запасной путь)
    int logTableCs; int logTableCsOff; int logTableCsLen;
};

// = MaterialDatabase.cs:925 MaterialDatabase.Relaxation.Transitions
// Len 0 = null (C# проверяет `radCum != null`, `augCum == null`, `radKev.Length == 0`).
struct TransitionsG
{
    int radCum; int radCumLen;
    int radKev; int radKevLen;
    int radFrom; int radFromLen;                  // арена I
    real radSum;
    int augCum; int augCumLen;
    int augKev; int augKevLen;
    int augFrom; int augFromLen;                  // арена I
    int augEjected; int augEjectedLen;            // арена I
    real augSum;
    // (`AMBER161`, П227) Накопления radCum/augCum не убывают (проверка читателя
    // упаковки) — выбор перехода двоичным поиском; 0 — линейным проходом, как в C#.
    int cumMonotone;
};

// = MaterialDatabase.cs:856 MaterialDatabase.Relaxation
// Не перенесены словари `bindingKev`, `transitions` — источник при загрузке; горячий
// путь читает массивы по обозначению EADL (`IndexByShell` зовётся при загрузке всегда).
struct RelaxationG
{
    int Z;
    int bindingByShell; int bindingByShellLen;            // кэВ по обозначению EADL
    int transitionsByShell; int transitionsByShellLen;    // арена I: индекс в D.transitions, −1 = null
    int shellsByBinding; int shellsByBindingLen;          // арена I, по убыванию связи
    int bindingByOrder; int bindingByOrderLen;
};

// = ScatteringData.cs:78 ScatteringData.Atom
// Статическая сетка `ScatteringData.momentumGrid` (одна на процесс) кладётся в каждую
// запись — 31 число, зато `SampleMomentumAu` не зависит от глобального состояния.
struct AtomG
{
    int Z;
    int sfX; int sfXLen;                          // S(x,Z): x, см⁻¹
    int sfV; int sfVLen;
    int ffT; int ffTLen;                          // t = x²
    int ffF2; int ffF2Len;                        // F²
    int ffCum; int ffCumLen;                      // ∫F² dt
    int shellCum; int shellCumLen;                // Len 0 = профилей нет (ShellCount = 0)
    int shellCumMonotone;                         // (`AMBER161`) shellCum не убывает — двоичный поиск
    int shellBindKev; int shellBindKevLen;
    int profCum; int profCumOff; int profCumLen;  // [оболочка][31]
    int momentumGrid; int momentumGridLen;        // = ScatteringData.momentumGrid
    int cohNormLog; int cohNormLogLen;            // 600, построены `EnsureNorms` до упаковки
    int incNormLog; int incNormLogLen;
};

// = ElectronData.cs:88 ElectronData.Material
// Памятка `logs` (`Material.Logs`, ElectronData.cs:101) — три массива логарифмов,
// вынуждена `LogsNow` при упаковке; поля `logs.Energy` → `logsEnergy` и т.д.
// Len 0 у логов — `logs.X == null` (запасной `LogLog` без логарифмов).
struct ElectronMaterialG
{
    int Energy; int EnergyLen;                    // МэВ
    int Range; int RangeLen;                      // г/см²
    int Yield; int YieldLen;
    int logsEnergy; int logsEnergyLen;
    int logsRange; int logsRangeLen;
    int logsYield; int logsYieldLen;
};

// = BremsstrahlungData.cs:265 ThickTargetBrem
struct ThickTargetBremG
{
    real MinKev;
    int node; int nodeLen;                        // кэВ, и по T, и по k
    int logNode; int logNodeLen;
    int cumulative; int cumulativeOff; int cumulativeLen;    // [T][k]
    int photons; int photonsLen;
    int radiatedKev; int radiatedKevLen;
    int anchorFactor; int anchorFactorLen;
    int thinAbove; int thinAboveOff; int thinAboveLen;       // [T][k]
    int thinPhotons; int thinPhotonsLen;
    int thinRadiated; int thinRadiatedLen;
};

// = MaterialDatabase.cs:753 MaterialDatabase.LightYieldCurve
// Памятка `logNodes` (LogMemo, MaterialDatabase.cs:790) вынуждена `LogNodes` при
// упаковке и лежит готовым массивом. Строки `Material`, `Variant` — не нужны.
struct LightYieldCurveG
{
    int energyKev; int energyKevLen;
    int yieldRel; int yieldRelLen;
    int logNodes; int logNodesLen;
};
