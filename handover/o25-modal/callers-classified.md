# `A241`: разбор ВСЕХ вызывающих `AppUi.Report`, 05.09.2026 (полоса О25)

Перечень получен машинно — `handover/o25-modal/enum_callers.py`, вывод
`handover/o25-modal/callers.txt`. Комментарии и литералы гасятся С СОХРАНЕНИЕМ
СМЕЩЕНИЙ, поэтому номера строк точны, а `<see cref="AppUi.Report"/>` в описании
за вызов не считается.

**Живых вызовов в приложении: 99.** Наивный `grep` даёт 105 — шесть попаданий
сидят в комментариях и описаниях. Ещё 9 — в самих пробах
(`ReasonProbe` 5, `N42RoundTripProbe` 3, `RoiLoadProbe` 1), к приложению не
относятся.

## Что сделано по каждому: правка ОДНА, в самой двери

`AppUi.Report` — единственная дверь (так объявлено в её же описании: «Второго
способа заводить нельзя»). Поэтому маршалинг заведён В НЕЙ, а не в 99 местах:
дверь сама спрашивает `Application.OpenForms`, есть ли поток окон, сама решает,
нужен ли перенос, и сама переносит через `Control.BeginInvoke`. Ни один
вызывающий не правился — и в этом смысл: разложить решение по 99 местам значит
завести 99 мест, где о нём забудут.

## Разряды по потоку

| разряд | вызовов | что с ними стало |
|---|---:|---|
| 1. поток окон гарантирован | 73 | поведение БАЙТ В БАЙТ прежнее: `InvokeRequired == false`, окно поднимается тут же и синхронно (плечо `поток окон` пробы) |
| 2. поток окон ЕЩЁ не поднят (запуск, менеджеры-одиночки) | 24 | поведение НАЗВАНО: живых окон нет, окно поднимается на вызывающем потоке — как было. При запуске вызывающий и есть будущий поток окон; терять сообщение ради чистоты правила нельзя |
| 3. ФОНОВЫЙ поток доказан | 2 | окно теперь поднимает поток окон, с хозяином; фоновый поток не встаёт |

99 = 73 + 24 + 2.

⚠ Разряд 2 переходит в разряд 3 сам собой: менеджер-одиночка, впервые
затронутый С ФОНОВОГО потока при уже поднятом окне, попадает под маршалинг —
и это ровно то, чего строка `A241` требовала.

### Разряд 3 — фоновый поток доказан

| место | чем доказано |
|---|---|
| `BecquerelMonitor/WinMM/WaveIn.cs:323` `WaveIn.MaintainBuffers` | тело ОТДЕЛЬНОГО потока `bufferMaintainerThread` (заводится на `:168`); сказано и в комментарии самого места |
| `BecquerelMonitor/PolynomialEnergyCalibration.cs:272` `UnusableCalibration` | ⛔ **тело `Parallel.For`, и по тексту это НЕ ВИДНО.** Цепочка: `SpectrumAriphmetics.Substract` (`:341`, `:353`) → тело цикла → `EnergyCalibration.EnergyToChannel` → `PolynomialEnergyCalibration.EnrgToChannel` (`:276`) → `UnusableCalibration` (`:300`, `:311`) → дверь. `Substract` зовут и с потока окон (`EnergySpectrumView.cs:1529`, `:2045`; `MainForm.cs:2579`), и из `Task.Run` (`PeakDetector.cs:27`, крутится в `DCPeakDetectionView.cs:184`) |

⛔ Второе место — довод в пользу `BeginInvoke` против `Invoke`, и довод
решающий: `Invoke` из рабочего потока `Parallel.For`, запущенного С ПОТОКА
ОКОН, даёт взаимную блокировку намертво. А до правки там было хуже: окон
поднималось столько, сколько рабочих потоков упёрлось в дурную калибровку,
все за главным окном, и `Parallel.For` не заканчивался НИКОГДА.

### Разряд 2 — поток окон ещё не поднят

| файл | вызовов |
|---|---:|
| `GlobalConfigManager.cs` (`LoadConfigFile`, `SaveConfigFile`) | 2 |
| `DeviceConfigManager.cs` (`LoadAllConfigFiles`, `CreateConfig`, `DuplicateConfig`, `SaveConfig`, `DeleteConfig`) | 9 |
| `ROIConfigManager.cs` (те же роды) | 9 |
| `NuclideDefinitionManager.cs` (`GetInstance`, `LoadDefinitionFile`, `SaveDefinitionFile`) | 3 |
| `NucBase/DataBase.cs:87` `CreateConnection` | 1 |

### Разряд 1 — поток окон гарантирован

| файл | вызовов | чем гарантирован |
|---|---:|---|
| `DocumentManager.cs` | 26 | все вызывающие — `MainForm` (открытие, запись, ввоз, вывоз из меню): `MainForm.cs:483, 1066, 1082, 1445, 2508, 2721, 2748, 2921, 2960, 2979` |
| `N42/Util.cs` | 9 | единственный вызывающий — `DocumentManager.ImportDocumentN42`/`ImportDocumentSpecUtils` |
| `MeasurementController.cs` | 7 | пуск и остановка набора из видов |
| `MainForm.cs` | 5 | сам поток окон |
| `AudioInputDeviceController.cs` | 5 | `StartMeasurement`, зовётся из `MeasurementController.StartRecording` |
| `AtomSpectraDeviceController.cs` | 3 | `update_hystogram` — через `MainForm.originalContext.Post` (`:276`); `StartMeasurement`, `AttachToDevice` — из `MeasurementController` |
| `DocEnergySpectrum.cs` | 3 | `FsaSessionCompleted` — внутри `this.BeginInvoke` (`:997`); `EnsureFsaFwhm` — из `ShowFsaToolStripMenuItem_Click`; третий — обработчик кнопки |
| `GeometryMaterialEditorForm.cs`, `ObsidianDeviceForm.cs`, `RadiaCodeDeviceForm.cs`, `SpectrumAriphmetics.cs` (`CombineWith` из `MainForm.CombineSpectrums`), `ObsidianDeviceController.cs`, `RadiaCodeDeviceController.cs` | по 2 | обработчики окон и `originalContext.Post` (`:175`, `:173`) |
| `AboutForm.cs`, `DeviceConfigForm.cs`, `NuclideSetForm.cs` | по 1 | обработчики окон |

⚠ **Приборные потоки чтения в перечне `A238` названы неточно.** Три прослойки
из четырёх (`AtomSpectra`, `Obsidian`, `RadiaCode`) свои сообщения УЖЕ переносят
сами — `MainForm.originalContext.Post`, и в комментариях мест это записано.
Дверь их не трогает: `InvokeRequired` там `false`. Настоящий приборный фоновый
путь ровно один — `WaveIn.MaintainBuffers`, и он в разряде 3.

## `AppUi.AskYesNo` — факт, не задача

Вторая дверь того же класса, `AskYesNo`, НЕ маршалится и оставлена как есть.
Довод замером: все пять её вызывающих — пути документа
(`DocumentManager.cs:94, 350, 445, 1230, 1476`), а их вызывающие все до одного
в `MainForm` (перечень выше). С фонового потока `AskYesNo` сегодня недостижима.
Маршалить её пришлось бы `Invoke` — ответ нужен вызывающему, — а это ровно та
взаимная блокировка, от которой уходит `Report`. Правка ради недостижимого
случая завела бы риск, которого сейчас нет.
