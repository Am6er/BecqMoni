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
  постоянные нити): 17.3 с ядер против 20.3 без потолка, 19.1 при 128, 18.5 при 96; размер
  блока 64…256 на время почти не влияет.
* `-O3`, `-arch=sm_86` (RTX 3070 Laptop); `/utf-8` — исходники с русскими комментариями.
