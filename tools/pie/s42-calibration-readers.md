# S42: инвентаризация читателей калибровок (14.08.2026, ночь)

Правило «проверь вызовы» перед правкой `CalibrationSolver` /
`PolynomialEnergyCalibration` / `SqrtFwhmCalibration` (гипотеза §3
[fsa-hypotheses-2026-08.md](fsa-hypotheses-2026-08.md)). Снято агентом-разведкой
по всему дереву; здесь — выжимка с координатами. Правка НЕ начата.

## Главное, что решает постановку

1. **`PolynomialEnergyCalibration` НЕ знает своих опорных точек.** В классе
   только порядок, коэффициенты, `maxChannels`/`maxEnergy` и кэш
   (`PolynomialEnergyCalibration.cs:415-425`). Список `CalibrationPoint` живёт
   в `ResultData.CalibrationPoints` — **`[XmlIgnore]`, в файл не пишется**
   (`ResultData.cs:345-356`) — и в локальном поле формы
   (`DeviceConfigForm.cs:2307`). После сохранения и перезапуска опоры
   ПОТЕРЯНЫ, остаются одни коэффициенты: у всех спектров корпуса в XML только
   `<PolynomialOrder>` и `<Coefficients>`. Касательную «за крайними опорами»
   классу не построить — либо новые сериализуемые поля границ
   (`XmlSerializer` незнакомые элементы молча игнорирует, обратной
   совместимости в проекте не делаем — путь открыт), либо касательную считает
   солвер и пишет готовые краевые параметры.

2. **У ПШПВ — наоборот**: `FwhmCalibration.CalibrationPeaks` сериализуются
   (`FwhmCalibration.cs:71-72`), границы опор доступны классу. НО у спектров
   корпуса блок пуст — `calibrate.py:449` пишет `<CalibrationPeaks />`;
   механизм есть, данных нет.

3. **Погрешностей коэффициентов не существует нигде**: `CalibrationSolver`
   возвращает голый `double[]`, ковариационной матрицы нет ни в одном месте
   проекта. Автопонижение ранга «σ(старшего) больше его самого» — НОВАЯ
   функциональность солвера (σ²·diag((AᵀWA)⁻¹)), а не чтение готового.

4. **Питон корпуса обе половины S42 уже решил** —
   `tools/CORPUS/scripts/calibrate.py:231-272`: guard `max_bend = 0.15`
   (изгиб относительно прямой через собственные опоры, порог
   `max(40 кэВ, 0.15·E)`, отсчёт от `max(5, 0.5·ch_min)`) плюс понижение
   порядка в цикле при провале; то же в `corpus_calib.py:190-220`. В
   докстринге разобран живой дефект: квадратичная по опорам 689–2510 на
   канале 8191 даёт 5133 кэВ (+70 %). **Критерии переносить оттуда, не
   изобретать.**

## Куда правка дотянется (узкие места)

* `ChannelToEnergy` уже клампит вход по `[0, maxChannels]`
  (`PolynomialEnergyCalibration.cs:151-152`) — это границы СПЕКТРА, не опор.
* `CheckCalibration` (`:80-133`) гоняет монотонность по всем каналам; его
  вердикт читают `DeviceConfigManager.cs:194` (блокирует сохранение),
  `DocumentManager.cs:132,204` (авто-починка при открытии),
  `PeakStabilizer.cs:109`, `tools/pie/Program.cs:187` и ещё десяток мест —
  касательная изменит вердикты по отбраковываемым сейчас спектрам.
* Ручное понижение ранга УЖЕ есть: `Downgrade` (`:135-146`), зовётся из
  `DCEnergyCalibrationView.cs:328`, `DeviceConfigForm.cs:1012`,
  `DocumentManager.cs:155,228` (хвостовые нули). Автопонижение обязано с
  ними ужиться.
* Прямые читатели `Coefficients[]` мимо методов: панель крутилок
  (`ToolStripEnergyCalibrationControl.cs:43-60`), график
  (`EnergySpectrumView.cs:4206-4207` — `[0]`/`[1]` как смещение/усиление),
  масштабирование при смене числа каналов
  (`SpectrumAriphmetics.cs:1396-1422`), приборы RadiaCode/Obsidian —
  **принимают только порядок 2** (`RadiaCodeIn.cs:661-703`,
  `ObsidianIn.cs:1191-1220`) с параллельным фитом порядка 2 в двух формах.
* Экстраполяция живьём: вниз — `FwhmCalibration.cs:36` (дефолтная точка ПШПВ
  на канале 0), `PeakDetector.cs:227` (`Min_Range` 20–30 кэВ ниже всех
  опор); вверх — `ChannelToEnergy(numberOfChannels)` в четырёх местах
  отрисовки, `FsaAnalyzer.cs:2842`, `PeakDetector.cs:228` (`Max_Range`
  2800–3000). В `EfficiencyFitter.cs:1353-1384` — готовый комментарий про
  ровно эту болезнь («кубическая уходит в минус за концом спектра → 0 lines
  measured»).
* Финдер зовёт `ChannelToFwhm` по ВСЕЙ шкале (`PeakFilter.cs:70-73`,
  свёртка), у `DefaultCalibration` опор всего две (канал 0 и `Ch_Fwhm`).
* Единственное место, уже смотрящее на нижнюю опору, —
  `SpectrumAriphmetics.LowEnergyFwhmFloor` (`:1230-1266`) — прецедент.

## Латентные баги (знать, в S42 не чинить)

* Копирующий конструктор (`:54-60`) не копирует `dirty`;
  `ConcatSpectrum`/`RestoreSpectrum` масштабируют `Coefficients[i]` мимо
  сеттера без `InvalidateCache()` → `maxEnergy` от старой шкалы.
* `maxChannels` — изменяемое состояние: его пишут и `CheckCalibration`, и
  `EnergyToChannel`; результат `ChannelToEnergy(n)` зависит от порядка
  вызовов.

## Готовые инструменты поверки

`tools/CORPUS/probes/CalibrationProbe.cs` (сверка с ручным Горнером на семи
каналах + замыкание туда-обратно + вердикт `CheckCalibration`),
`check_corpus.py`, `PeakOriginProbe`/`PeakAgreeProbe`.

`NonlinearEnergyCalibration` нигде не создаётся (подтверждено) — не трогаем
по приказу.
