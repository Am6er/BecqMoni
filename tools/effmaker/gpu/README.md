# `tools/effmaker/gpu` — счёт матрицы отклика на GPU (`AMBER160`, полоса П221)

Постановка Amber 02.10.2026: «CorpusMatrixProbe — портировать на GPU». Решения
вопросником того же дня, дословно: приёмка — **«float + статистика (Рекомендую)»**,
объём — **«Только оснастка (Рекомендую)»**. Отсюда:

* это ОСНАСТКА: нативная `rmgpu.dll` (CUDA C++) рядом с пробами и ключ `--gpu` у
  `CorpusMatrixProbe`; приложение `BecquerelMonitor` и поставка ClickOnce не меняются;
* физика НЕ пишется второй раз «по мотивам»: каждая функция горячего пути —
  ПОСТРОЧНЫЙ перенос своего C#-оригинала (`EfficiencySimulator.cs`,
  `ElectronTransport.cs`, `MaterialDatabase.cs`, `PartialCrossSections.cs`,
  `ScatteringData.cs`, `ElectronData.cs`, `BremsstrahlungData.cs`) с тем же именем;
* данные (сечения, кривая света, таблицы тормозного, сцена) GPU не строит и не читает
  из баз — их собирает ТОТ ЖЕ C#-код приложения, а упаковщик пробы
  (`GpuPack.cs`, отражением) копирует готовые массивы на устройство. Поэтому смена
  `matdb.sqlite` (например, новая кривая света) доезжает до GPU без правок здесь.

## Две ступени приёмки

1. **Сверка переноса по историям** (`real = double`, генератор xorshift64* CPU).
   GPU получает состояние ГСЧ CPU на начало истории и обязан повторить её занос,
   канал и свет. Расхождения допустимы только как редкие развилки на разнице
   последнего разряда `log`/`exp` CUDA и .NET; их доля печатается.
2. **Рабочий режим** (`real = float`, Philox4x32-10 со счётчиком «узел, история»):
   матрица узел к узлу против склада rev41 в пределах шума ГСЧ (`MatrixDiffProbe`),
   затем корпусный прогон FSA не хуже базы.

## Рабочий счёт: стадии и шаги (`AMBER161`, П227)

Ступень 1 (xorshift, выход историй) идёт прежними ядрами «нить — история»
(`WeightedKernel`, `AnalogKernel`). Рабочий счёт (`rm_run`, Philox, без выхода историй) — иначе:

* **взвешенная ветвь — стадиями** (`WeightedStage1…5`, kernels.cuh): история разрезана на
  части (`WeightedFrontRun`, `WeightedPrimaryCrystal`, `ScatterToCrystal`, `InCrystal` +
  `ScatteredFinish`), каждая — своё ядро по уплотнённому списку историй, которым она нужна.
  Между стадиями — `WBuf`: ГСЧ (номер блока Philox и остаток слов; слова — повтором блока)
  и кэш луча (начало сбора; стадия собирает заново — кэш не прозрачен, `A315`). Поэтому
  стадии повторяют одно ядро ПО КАЖДОЙ ИСТОРИИ; `BQ_GPU_STAGED=0` — одно ядро;
* **аналоговая ветвь — шагами с подкачкой и пачкой** (`AnalogKernelRegen`): нить делает
  шаг переноса (`AnalogStep` — одна итерация цикла `for (guard …)` C#) за проход, кончившаяся
  история сменяется новой; шаг, входящий в кристалл, ждёт, пока таких не станет
  `BQ_GPU_BATCH`/8 активных нитей варпа (умолчание 8). `BQ_GPU_REGEN=0` — ядро «нить —
  история», `=2` — положительный контроль (чужие потоки, суммы обязаны разойтись).

Сверка правок ядра — повторителем на записи `double`-сборки: 288 из 288 запусков в 1e-12
(`double`, `-fmad=false`); `float` — до округления (компилятор иначе сливает FMA). Замеры и
отвергнутые пути (`__noinline__`, подкачка без пачки, потолки регистров) — журнал
`handover/handover-2026-10-03-p227-gpu-event.md`.

## Соглашения переноса (обязательны для всех файлов `*.cuh`)

* Тип с плавающей точкой — `real` (`common.cuh`); `double` — только там, где он нужен
  ВСЕГДА (накопители сумм по историям, ГСЧ).
* Экземплярный метод C# `T Name(args)` класса `EfficiencySimulator` →
  `__device__ T Sim::Name(args)`; объявление — в своём `decl_<модуль>.inc`
  (входит в тело `struct Sim`), определение — в `sim_<модуль>.cuh`.
* `out T x` и `ref T x` → `T& x`. Массив `double[] a` → `real* a` + длина, если она
  нужна. `this.поле` → `поле`. `Math.Xxx` → `M_Xxx` из `common.cuh` (там же
  `Math.Round` с округлением к чётному, как в .NET).
* Изменяемое СОСТОЯНИЕ ИСТОРИИ (метки, свет, очереди, кэш луча, кэши μ областей) —
  поля `Sim`, своя копия у каждой нити. Неизменяемые ДАННЫЕ — структуры в
  `data.cuh`, доступ через глобальный указатель `D` (`const DevData*`).
* Методы классов данных C# (`Element`, `Fluorescence`, `PhotoShellModel`,
  `Relaxation`, `ScatteringData.Atom`, `ElectronData.Material`, `ThickTargetBrem`,
  `LightYieldCurve`) → свободные функции `__device__` с тем же именем и первым
  аргументом-структурой: `Atom.FormFactor(x)` → `FormFactor(const AtomG& a, real x)`.
  Статические `PartialCrossSections.MassTotal(...)` → `MassTotal(...)`.
* Методы `Region` с кэшем на энергию → методы `Sim` с номером области первым
  аргументом: `region.Mu(E, woc)` → `RegMu(r, E, woc)`; кэш — массив в `Sim`.
* Памятки, которые не меняют чисел (`LFractions` Memo, `Scatterers.Product*`,
  `LogsNow`), НЕ переносятся — значение считается заново.
* Счётчики-диагностика (`Count*`, `Sum*`) переносятся, если дешевы; их отсутствие
  помечается комментарием `// не перенесено: <имя>`.
* Начальное состояние нити: каждый модуль даёт `RM_DEV void Init<Модуль>()` (`InitGeom`,
  `InitPhoton`, `InitElectron`, `InitBranch`) — обнуляет свои поля так, как их ставит
  конструктор/`EnsureBuilt` C# (кэши μ — «энергии нет», кэш луча пуст, очереди пусты).
  Ядро зовёт все четыре перед первой историей нити.
* Рекурсия (`InCrystal` ↔ `ElectronLoss` ↔ `TransportElectron` → тормозное → `InCrystal`)
  переносится КАК ЕСТЬ: CUDA её держит, стек нити ставит хост (`cudaLimitStackSize`).
  Такие функции — `RM_DEVF` (без `__forceinline__`).
* Каждая функция начинается комментарием `// = <Файл>.cs:<строка> <Имя>` — по нему
  сторож сверки найдёт оригинал, когда тот поменяется.

## Данные устройства (`data.cuh`, `DevData D`)

Структура данных C# переносится ЗЕРКАЛЬНО: тип `Xxx` → `XxxG`, имена полей — те же,
что в C#. Правила:

* скаляр — то же имя и тип (`double` → `real`; поле, где нужна полная точность
  всегда, — `double` с пометкой);
* массив `T[] Name` → `const real* Name; int NameLen;` (у `int[]`/`bool[]` — `const int*`,
  `const unsigned char*`);
* зубчатый `T[][] Name` и прямоугольный `T[,] Name` → плоский `const real* Name` плюс
  `const int* NameOff` (начала строк, длина `n + 1`): `a.Name[i][j]` →
  `a.Name[a.NameOff[i] + j]`, `a.Name[i].Length` → `a.NameOff[i + 1] − a.NameOff[i]`;
* ссылка на объект данных → индекс `int` в таблицу его типа в `D` (−1 = null), массив
  ссылок → `const int*`;
* словарь по Z (`MaterialDatabase.TryGet(z, …)`, `FluorescenceOf(z)`, `PhotoShellOf`,
  `RelaxationOf`, `ScatteringData.Of`) → `D.<таблица>ByZ[z]` (−1 — нет);
* словарь по веществу (`FluorescersOf(material)`, `ScatterersOf`, `CarryMedium`,
  `LayerBrem`, кэши `ElectronTransport.cs`) → поле-индекс в `MaterialG` вещества;
  вещество области — `RegionG.Material` (индекс в `D.materials`).

Таблицы `D` (имена обязательны, модули пишут по ним):

| таблица | тип | C#-оригинал |
|---|---|---|
| `elements`, `elementsByZ` | `ElementG` | `MaterialDatabase.Element` |
| `fluor`, `fluorByZ` | `FluorescenceG` | `MaterialDatabase.Fluorescence` |
| `photoShell`, `photoShellByZ` | `PhotoShellModelG` | `MaterialDatabase.PhotoShellModel` |
| `relax`, `relaxByZ` | `RelaxationG` (+ `TransitionsG`) | `MaterialDatabase.Relaxation` |
| `atoms`, `atomsByZ` | `AtomG` | `ScatteringData.Atom` |
| `electronMats` | `ElectronMaterialG` | `ElectronData.Material` |
| `brems` | `ThickTargetBremG` | `ThickTargetBrem` |
| `lightYield` (одна, −1 — нет) | `LightYieldCurveG` | `MaterialDatabase.LightYieldCurve` |
| `materials` | `MaterialG` | `GeometryMaterial` + кэши симулятора по веществу |
| `fluorescers` | `FluorescersG` | `EfficiencySimulator.Fluorescers` |
| `scatterers` | `ScatterersG` | `EfficiencySimulator.Scatterers` |
| `scatterElements` | `ScatterElementG` | `EfficiencySimulator.ScatterElement` |
| `regions`, `nRegions` | `RegionG` | `EfficiencySimulator.Region` (без кэшей) + плоские `reg*E` |
| `scene` | `SceneG` | поля сцены симулятора: `crystal`, `sphereZ/R`, `pathSceneZ/R`, `scene*`, источник, `electron`, `bremTable`, `lightYield` |

## Сборка

`build_gpu.cmd` (nvcc 12.8, `-arch=sm_86`, VS 2022 x64) → `bin\rmgpu.dll` (double) и
`bin\rmgpu_f.dll` (float). Пути без пробелов не нужны: команда собирает из своего каталога.
Доп. ключи nvcc — переменной `RM_EXTRA`.

⚠ Файл `.cmd` — ТОЛЬКО ASCII и переводы CRLF: `cmd` режет строки UTF-8 (в том числе в `rem`)
на куски и исполняет обрывки как команды (02.10.2026: «'22' is not recognized…», сборка
молча не прошла, а прогон взял прежнюю DLL). Пояснения — здесь:

* `-fmad=false` у double-сборки НЕ украшение: .NET Framework 4.8 (RyuJIT x64) не сливает
  `a*b+c` в FMA, а nvcc сливает по умолчанию — ступень 1 (сверка истории с CPU побитово)
  расходилась бы на последнем разряде почти на каждой операции;
* `-maxrregcount=64` у float-сборки — замер П221 02.10.2026 (G1S24, 300 тыс. историй на узел,
  постоянные нити): 17.3 с ядер против 20.3 без потолка, 19.1 при 128, 18.5 при 96.
  Перемерено П227 03.10.2026 повторителем, четыре круга вперемешку (частота ноутбука под
  нагрузкой плавает — одиночный замер врёт до 20 %): медиана 21.2 с при 64, 22.3 при 128,
  24.1 без потолка — потолок 64 подтверждён.
* раскрой запуска — ОДНА ИСТОРИЯ НА НИТЬ, блок 128 (`rm_run`, `blocks == 0`). Постоянные
  нити П221 медленнее: повторитель — медиана 21.2 с против 18.6 с у сетки; проба на 3 млн
  (petri, 144 узла) — ядра 214.1 / 220.5 с против 196.5 / 196.9 с; тела матриц побайтно те же.
  Блок 64 и 256 при постоянных нитях — 22.7 и 22.2 с: размер блока ВЛИЯЕТ, 128 лучший из
  перемеренных (прежняя запись «почти не влияет» — неверна).
* `-O3`, `-arch=sm_86` (RTX 3070 Laptop); `/utf-8` — исходники с русскими комментариями.

Той же командой собирается `bin\rm_replay.exe` (`cl`, x64) — нативный повторитель.

## Повторитель и профиль (`AMBER161`, П227)

`BQ_GPU_DUMP=<каталог>` у пробы с `--gpu` пишет `calls.txt` (init, настройки битами
double, `rm_cfg_commit`, `rm_load blobN.bin`, параметры каждого `rm_run` и ПРИРАЩЕНИЕ его
сумм — `rm_run` прибавляет к массивам хоста) и упаковки сцены. `rm_replay <dll> <каталог>
[--only=N] [--repeat=K] [--tol=X]` зовёт ту же DLL тем же порядком без .NET и сверяет суммы
(код 0 — все в допуске; атомарные сложения в другом порядке дают ~1e-15).

Зачем: проба AnyCPU (заголовок PE32, процесс 64-битный) под Nsight Compute падает
`0xC000007B` — `ncu` внедряется по заголовку; нативный повторитель профилируется штатно:

```
ncu --set full -o prof -f bin\rm_replay.exe bin\rmgpu_f.dll <запись> --only=<номер run>
```

Профиль по строкам — DLL с `-lineinfo` (`RM_EXTRA=-lineinfo`), разбор отчёта питоном
Nsight (`host\target-windows-x64\python\bin\python.exe`, модуль `extras\python\ncu_report`).
⚠ Счётчики GPU обычному пользователю Windows закрыты (`ERR_NVGPUCTRPERM`) — доступ всем
включён Amber 03.10.2026 в NVIDIA Control Panel (Developer → Manage GPU Performance Counters).
⚠ Nsight Compute 2025.1 из CUDA 12.8 на этой машине падает `0xC0000409` даже на
`--query-metrics` — работает 2026.3.1 (`winget Nvidia.Nsight.Compute`).
