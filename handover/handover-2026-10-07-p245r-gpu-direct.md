# П245-R, 07.10.2026: GPU-порт в приложении — отражение снято, прямой доступ (`AMBER219`)

Полоса распорядителя П245 (`AMBER219`, шаг после коммита `781b6ccd`). Worktree
`D:\BqMoni_Claude\p245r\wt`, ветка `p245r-gpu-direct` от `781b6ccd`. Коммитов нет, ничего не
застейжено — ветку сливает распорядитель.

## Постановка

Решение Amber 07.10.2026 вопросником, дословно: **«Перевести на прямой доступ сейчас»**.
C#-часть GPU-порта (`BecquerelMonitor\EfficiencyMaker\GpuPack.cs`, `GpuPackScene.cs`,
`GpuPackTables.cs`, `GpuMatrixRun.cs`) переехала из проб в приложение, но читала закрытые
члены симулятора и классов данных отражением (`GpuReflect.Field/Set/Call/Get/D/I/B` — 161
строка, `GpuNode.SetProperty` — 13, `GetProperties` по `Joint*`). Внутри сборки это дефект
качества: переименование члена ловил не компилятор, а отказ при счёте.

## Что сделано

1. **Видимость — и только она.** 103 члена в 83 строках шести файлов приложения стали
   `internal` (у свойств — `private set` → `internal set`). Тела не тронуты: сверка
   `git diff -U0` — каждая из 83 изменённых строк за вычетом слова `internal` равна старой
   байт в байт; переводы строк сосчитаны байтами (CR = LF во всех правленых файлах), BOM на месте.

   | файл | членов | что |
   |---|---|---|
   | `EfficiencySimulator.cs` | 80 | типы (10): `Fluorescers`, `Scatterers`, `Region`, `Sampler`, `PointSampler`, `CylinderSampler`, `MarinelliSampler`, `BoxSampler`, `IsoFieldSampler`, `ImportanceSampler`; поля (41): `resolutionWeighted`, `resolutionAnalog`, `geometry`, `regionArray`, `regBox`, `regZMinE`…`regAYE` (6), `crystal`, `sphereZ`, `sphereR`, `pathSceneZ`, `pathSceneR`, `sceneRMax`, `sceneZMin`, `sceneZMax`, `source`, `crystalHasPartials`, `electron`, `lightYield`, `bremTable`, `channelHistograms`, `lightSum`, `lightBinSplit` и 14 полей источников (`z`; `r`,`z0`,`z1`; `rIn`,`rOut`,`z0`,`z1`,`zCap`,`capFraction`; `ax`,`ay`,`z0`,`z1`); методы (8): `EnsureBuilt`, `FluorescersOf`, `ScatterersOf`, `WaterTable`, `CarryMedium`, `LayerBrem`, `SmearedContinuumError`, `FinishRun`; сеттеры (21): `LastAngularMoments`, `LastResolutionPeakExtra`, `LastPhotonLightScale`, `LastPhotonLightScaleSplit` и 17 у `AngularMomentSums` (`N`, моменты `AngularMomentSums.S0…S04` и `AngularMomentSums.T0…T04`) |
   | `ElectronTransport.cs` | 5 | тип `ScatterElement`; `CrystalRadiationLength`, `LayerRadiationLength`, `LayerScatterElements`, `LayerBremZ` (часть `EfficiencySimulator`; в перечне задания не было, но `ScatterElement` назван) |
   | `ResponseMatrixBuilder.cs` | 4 | `MakeSimulator`, `ResolutionHalfWidth`, `BuildJoint`, `FillResolutionPeak` |
   | `ScatteringData.cs` | 4 | статическое `momentumGrid`; у `Atom` — `cohNormLog`, `incNormLog`, `EnsureNorms` |
   | `BremsstrahlungData.cs` | 9 | поля `ThickTargetBrem`: `node`, `logNode`, `cumulative`, `photons`, `radiatedKev`, `anchorFactor`, `thinAbove`, `thinPhotons`, `thinRadiated` |
   | `MaterialDatabase.cs` | 1 | `LightYieldCurve.LogNodes` |

   Не тронуты (уже открыты): `ResponseMatrix.cs` — сеттеры `Joint*` публичные, `ComputeStamp`,
   `NormalizationOf`, `AngularAttenuation.FromMoments` публичные; `ElectronData.cs` — `Logs` и
   `LogsNow` уже `internal`; поля `PhotoShellModel`, `Relaxation`, `Transitions`,
   `LightYieldCurve` уже `internal`.

2. **Четыре файла упаковщика — прямой доступ.** Каждое `GpuReflect.*` заменено обращением к
   члену; вид источника — `switch` по типу (`case EfficiencySimulator.PointSampler _:` …)
   вместо сравнения имени типа строкой, тексты отказов прежние; `GpuNode.SetProperty`
   (публичный, внешних пользователей нет — `grep` по дереву) снят; копия `Joint*` из временной
   матрицы — девять присваиваний поимённо в порядке объявления (их и только их пишет
   `BuildJoint`, `ResponseMatrixBuilder.cs:575–603`). Открытый API не менялся: `RmGpu`,
   `GpuBuild.Build` (обе), `GpuNode.ResponseByChannel`/`NodeKey`, `GpuPack.Pack`/`Settings`,
   `GpuHistoryOut`, `GpuSlots`, `GpuProbe`, `GpuProbeReason`.

3. **`GpuReflect` переехал в пробу** `tools\effmaker\probes\GpuMatrixCheck.cs` — закрытым
   вложенным классом `GpuCheck.GpuReflect`, имя и тело прежние. `GpuPackDumpProbe.cs` не тронут.

4. **Отражение, оставленное нарочно, — одно: `GpuPack.Settings`** (`GetFields` по ОТКРЫТЫМ
   простым полям симулятора). Это не доступ к закрытому, а самообновляющийся список
   настроек: новое открытое поле само уходит в `rm_cfg_set`, и натив отказывает на незнакомом
   имени. Явный список потерял бы новую настройку молча. В перечне задания (`Field/Set/Call`,
   `SetProperty`, `GetProperties` по `Joint*`) его нет. Поля, ставшие `internal`, в него не
   попадают (`BindingFlags.Public`) — это проверяет сверка настроек ниже (69 настроек, те же).

## Приёмка (артефакты — `D:\BqMoni_Claude\p245r\tmp\`)

* **Сборка.** `msbuild /t:Restore` — 0; приложение `Release`, `bin\Release_p245r` /
  `obj\Release_p245r` — код 0 (`build_app.log`); `build_all.ps1 -Bin
  BecquerelMonitor\bin\Release_p245r -Out tools\effmaker\probes\build_p245r` — код 0, «все
  собрались: 301 файлов (плюс 7 без Main)», `CorpusMatrixProbe.cs` и `GpuPackDumpProbe.cs` — ok
  (`build_probes.log`). Exe рядом с пробами = exe сборки (sha256 `5a9da981b214a424`), в нём
  `GpuReflect` 0 вхождений, в старом (`D:\BqMoni_Claude\p245\app`) — 1: мерили НОВЫЙ код.
* **⛔ Главная — упаковка байт в байт, 51 сцена склада** (`--dir=D:\BqMoni_Claude\p235\store
  --compare=D:\BqMoni_Claude\p245\blobs_base`, базы сняты старым кодом): **«ВСЕ СОШЛИСЬ (51
  сцен)», код 0**, 51 строка «сошлось» (`dump_compare.log`). Положительный контроль
  `--poison`: **«расходятся: 51 из 51», код 1** (`dump_compare_poison.log`).
* **Матрица на GPU по сцене, до/после** (`CorpusMatrixProbe --gpu=…\gpu\bin\rmgpu_f.dll
  --target=0 --n=300000 --force`; «до» — `build_p245` основного дерева, старый код):
  `RC103_point0` — «ТЕЛА ТОЖДЕСТВЕННЫ (побайтно, по отпечатку)» `6fab06db8a982d24`;
  `G1S_denta120_oisn06_057_p24` (не точечная — κ пар считается, путь копии `Joint*`
  проходит) — «ТЕЛА ТОЖДЕСТВЕННЫ» `0af832943fa80ab0`, строки сводки «κ пар»/«сетка κ»/«ε_p»/
  «Q_k»/клеймо до и после совпали дословно. Блок Q_k: побитово 0/144 и 5/144 узлов при
  наибольшем |Δ| < 5e-6 — ровно та же картина, что у старого кода против себя же (`p245\
  diff_*_chunk_vs_nochunk.log`: 0/144 и 5/144) — порядок атомарных сложений. Положительный
  контроль сверки матриц: 3 млн против 300 тыс. историй — «ТЕЛА РАЗЛИЧНЫ», пик до 12.4 %
  (`diff_rc103_control.log`).
* **Ступень 1 (проба с переехавшим `GpuReflect`)**: `--gpu=…\rmgpu.dll --gpu-check=2000`,
  четыре узла × две ветви — «прогонов с развилками 0», числа гистограмм до и после те же до
  последнего знака (`check1_before.log`, `check1_after.log`); контроль
  `BQ_GPU_CHECK_POISON=1` — «историй с любой развилкой 1000 (50.0000 %)» (`check1_poison.log`).
* **`python tools\check_all.py` в worktree — код 1, красных 7 из 44, ни один не от правки**
  (`check_all.log`): `fsa_report_view`, `corpus_scenes`, `fsa_showcase` — код 3, нет
  канонического каталога проб `tools\effmaker\probes\build` (в worktree его нет, он в
  `.gitignore`); `corpus_coverage` (код 1, `gaussfit_check.py` падает трассой
  `FileNotFoundError` на отсутствующем `_corpus_raw`), `declared_base` (код 2, нет
  `tools\pie\out_rev46_full`), `corpus_generator` (код 1, сводка пересобирается без `*.rmx`
  склада — «❗ нет матрицы») — данные, которые `.gitignore` не несёт в свежий worktree, в
  основном дереве они есть; **`gpu_dll` (код 1)** — см. находку 1: DLL та же байт в байт, что
  в основном дереве (sha256 `5e6a7c93569354d5`), копировать было нечего. Сторожа, которых
  правка касается по существу, зелёные: `matrix_keys`, `registry`, `probe_numbers`, `headless`.
* **Grep** (`grep_proof.log`): `GpuReflect` во всём `BecquerelMonitor\*.cs` — 0 (было 161
  строка в четырёх файлах); в `EfficiencyMaker\Gpu*.cs` `GetField(` 0, `GetProperty(` 0,
  `GetMethod(` 0, `GetProperties(` 0, `SetProperty(` 0, `.Invoke(` 0; остаётся `GetFields(` 1 —
  `GpuPack.Settings` (п. 4).

## Находки

1. **Отпечаток исходников ядра зависит от переводов строк рабочей копии.** `check_gpu_dll.py`
   (и, по описанию, `src_hash.ps1`) хэширует `tools\effmaker\gpu\*.cu|cuh|h|inc` побайтно. В
   основном дереве `physics.h` лежит с LF (`git ls-files --eol`: `w/lf`), остальные 21 — CRLF;
   свежий checkout при `core.autocrlf=true` выписывает `physics.h` с CRLF, отпечаток
   `284352900b6cf41f` против паспорта DLL `841552154209a452` — `gpu_dll` красный в ЛЮБОМ свежем
   worktree или клоне. Замер: та же папка с `physics.h`, приведённым к LF, даёт
   `841552154209a452`. В полосе не чинилось: правило отпечатка живёт в двух местах
   (`check_gpu_dll.py`, `src_hash.ps1`) вне перечня полосы, а его смена требует пересборки
   DLL (новый `src=`), которую задание запрещает. ✅ К концу полосы в основном дереве уже
   лежит незакоммиченная правка распорядителя — отпечаток без байтов CR в обоих местах и в
   `README.md`, самопроверка с CRLF; отдельная строка реестра не нужна, нужна пересборка DLL
   под новый `src=` вместе с этой правкой.
2. **Копия `Joint*` в GPU-пути — поимённый список из девяти полей.** Прежний перебор
   отражением подхватывал новое поле сам; теперь поле `Joint*`, которое начнёт писать
   `BuildJoint`, до матрицы GPU-пути не доедет молча. На месте стоит предупреждение в
   комментарии; машинного сторожа нет — он был бы правкой чужого файла (`check_matrix_keys.py`).
3. Мелочь для слияния, не строка: `tools\effmaker\gpu\README.md:15` ещё говорит «упаковщик пробы
   (`GpuPack.cs`, отражением)» — после этой полосы неверно (строка 131 того же файла уже
   описывает снятие отражения). Файл вне перечня полосы — не тронут.
4. Факт без «Сделать»: `CorpusMatrixProbe --gpu-check` возвращает 0 и при развилках (печатает
   «прогонов с развилками N») — по замыслу ступени 1 (редкие развилки допустимы), читатель —
   человек.

## Уборка

После приёмки сняты `BecquerelMonitor\bin\Release_p245r`, `BecquerelMonitor\obj\Release_p245r`,
`tools\effmaker\probes\build_p245r`. Worktree оставлен. Скриншотов нет.
