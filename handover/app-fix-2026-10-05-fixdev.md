# Полоса fixdev — правка приборов и набора (AMBER163, AMBER164, AMBER165), 05.10.2026

Постановки — `TODO.md`, разбор — `handover/app-bug-review-2026-10-05.md` §1.1–1.3 (номера строк
там — по HEAD `f4abe6c8`). Работа в основном дереве, без коммита. Приёмка — проба отражением
`D:\BqMoni_Claude\fixdev\shots\FixdevProbe.cs` (на диске, не в git), собранная ОДНИМ исходником против двух
сборок: «до» (`bin\Debug_fixdev_before`, собрана до первой правки) и «после» (`bin\Debug_fixdev`).
Экземпляры `RadiaCodeIn`/`ObsidianIn` и контроллеры проба создаёт БЕЗ конструктора
(`FormatterServices.GetUninitializedObject`) — ни потоков Bluetooth, ни включения радио.
Выводы пробы: `D:\BqMoni_Claude\fixdev\shots\probe_Debug_fixdev_before.txt`, `probe_Debug_fixdev.txt`.

⚠ **С прибором (RadiaCode, Obsidian) и со звуковой картой НЕ проверено** — прибора нет. Проверена
машина состояний и логика форм на собранном exe; путь с живым BLE (`ConnectBLE`, ответы прибора)
пробой не пройден.

## AMBER163 — «Очистить» во время набора на звуковом входе

**Было.** `AudioInputDeviceController.ClearMeasurementResult` пуст. `DCControlPanel` обнулял
`TotalTime`/`ElapsedTime`, а такт `MeasurementController.OnTimer` считал время звукового входа
`DateTime.Now - StartTime + TotalTime` — снова от Start.

**Сделано.** `AudioInputDeviceController.cs:253` — при `ResultDataStatus.Recording` ставится
`resultData.StartTime = DateTime.Now` (как у AtomSpectra/RadiaCode/Obsidian). Вне набора не
трогается: следующий Start ставит `StartTime` сам.

**Замер** (набор идёт 10 мин, Clear, затем формула такта `OnTimer`):

| сборка | набор идёт | время после Clear | `StartTime` сдвинут |
|---|---|---|---|
| до | да | **600.0 с** | нет |
| после | да | **0.0 с** | да |
| до / после | нет (контроль) | 600.0 с / 600.0 с | нет / нет |

Факт без «Сделать»: счётчик отсчётов звука `PulseDetector.TimeInSamples` (→ `NumberOfSamples`
файла) по Clear не сбрасывается — он метит время импульсов, в cps/Бк не входит; пишется потоком
звука, сброс с потока окон был бы гонкой. Время набора по `DateTime.Now` — задуманное (таблица
«Чего делать НЕ надо»), не тронуто.

## AMBER164 — остановленный RadiaCode/Obsidian сам возвращается в набор

**Было.** Три входа (разбор §1.2): (а) поток чтения ставил `Connected`/`Reconnecting`/`Recording`
после `ConnectBLE` и паузы сброса, не глядя на `Stopped`; (б) сохранение конфигурации прибора при
остановленном наборе → `setDeviceSerial` → `device_serial_changed` → `run()` ставил `Starting`;
(в) экземпляр с умершим потоком (`Faulted` без адреса, «QUIT» разбора) оставался в реестре
`getInstance`. Найден попутно четвёртый вход того же рода: запись калибровки в остановленный
RadiaCode (`sendCommand("Calibration")` поднимает его в `Starting`) кончалась командой «Continue»
окна записи → `Connected` → опрос → `Recording`.

**Сделано.**
- `RadiaCodeIn.cs:271/276`, `ObsidianIn.cs:362` — `setStatusAuto` / `applyStatus`: переход
  самого экземпляра (поток чтения, `Dev_ConnectionStatusChanged`, ответ калибровки) проверяет и
  ставит состояние под одним `stateLock` и из `Stopped` не выводит; команды (`sendCommand`,
  `Dispose`) идут прежним безусловным `setStatus`. Все 20 (RadiaCode) / 15 (Obsidian) переходов
  потока и событий BLE переведены на него; события `Status` поднимаются вне замка, как прежде.
- (а) `RadiaCodeIn.cs:1155`, `ObsidianIn.cs:954` — отвергнутый `Connected` после `ConnectBLE`
  снимает поднятую связь (`DisconnectBLE`); `:1179` / `ObsidianIn` — после отвергнутого
  `Reconnecting` нет пятисекундного поиска.
- (б) `RadiaCodeIn.cs:1102`, `ObsidianIn.cs:901` — флаг `device_serial_changed` у `Stopped`
  гасится без запуска (у `Disconnected` — по-прежнему запуск: так стартуют разбор и запись
  калибровки на свежем экземпляре).
- (в) `RadiaCodeIn.cs:371/408`, `ObsidianIn.cs:133/163` — `getInstance` через `takeLiveInstance`:
  экземпляр с `thread_alive == false` вынимается из реестра, вместо него строится новый; у
  мёртвого снимается только связь (`DisconnectBLE`), без `Dispose` — тот поднял бы «Stopped»
  контроллеру, который как раз стартует набор на новом.
- Калибровка: `RadiaCodeIn.cs:64, 901, 919, 924` — «Calibration» из `Stopped`/`Disconnected`
  запоминает покой, «Continue» возвращает в него и снимает связь; «Start» память сбрасывает.
- Контроллеры, второй рубеж: `RadiaCodeDeviceController.cs:24`, `ObsidianDeviceController.cs:20`
  — признак `measuring` (поток окон): ставится в `StartMeasurement`, снимается в
  `StopMeasurement` и на статусах `Faulted/Stopped/Disconnected`. Без него статус «Recording»
  не включает документу `Recording` (`:156` / `:152`) — это закрывает и гонку, когда событие,
  отправленное ДО Stop, доезжает через `Post` ПОСЛЕ него; смена конфигурации при остановленном
  наборе адрес в экземпляр не толкает (`:49` / `:43`) — следующий Start берёт его сам.

**Замер** (одинаково для RadiaCode и Obsidian, где не оговорено):

| сценарий | до | после |
|---|---|---|
| (б) `Stopped` + `device_serial_changed`, настоящий `run()` 1.5 с | события `[Starting, Faulted]`, итог `Faulted` | событий нет, итог `Stopped` |
| (б) контроль: `Disconnected` + флаг | `[Starting, Faulted]` | `[Starting, Faulted]` (запуск жив) |
| (а) Stop, затем переход потока в `Connected` | `Connected` | `Stopped`, отвергнут; `Recording` — тоже отвергнут |
| (а) контроль: `Starting → Connected` потоком | — | `Connected`, принят |
| команда Start из итога (а) | `Starting` | `Starting` |
| (в) реестр, `guid` мёртвого экземпляра | отдаётся мёртвый | вынут из реестра, `getInstance` строит новый |
| (в) контроль: живой экземпляр | тот же | тот же |
| калибровка у остановленного RadiaCode, «Continue» | `Connected` | `Stopped` |
| контроль: калибровка у набирающего, «Continue» | `Connected` | `Connected` |
| контроллер: остановленный документ, поздний статус «Recording» | `Recording = True` | `False` |
| контроль: идущий набор, «Recording» / затем «Stopped» | — | `True` / `measuring = False`, `Recording = False` |

⚠ Для (в) «после» проба зовёт `takeLiveInstance`, а не `getInstance`: настоящий `getInstance` на
мёртвом `guid` построил бы `RadiaCodeIn` конструктором — потоки BLE, проверку радио и
пятисекундный поиск на машине Amber.

Факт без «Сделать»: `StopMeasurement`/`ClearMeasurementResult` контроллеров достают экземпляр
через `getInstance`; на мёртвом `guid` теперь строится новый (прежде отдавался мёртвый). Достичь
этого нельзя обычным путём — смерть потока (`Faulted`) сама останавливает набор
(`NotifyMeasurementStoppedByDevice`), Stop гаснет, а Clear у RadiaCode/Obsidian требует
статуса `Connected`/`Recording`.

## AMBER165 — в конфигурацию RadiaCode пишется BLE-адрес чужого прибора

**Было.** `RadiaCodeDeviceForm.LoadFormContents` не сбрасывал `adressBLE`/`currentBLEindex`,
`SaveFormContents` писал `adressBLE.ElementAt(currentBLEindex)` — адрес прошлого поиска.

**Сделано.** `RadiaCodeDeviceForm.cs:235-236` — сброс списка и индекса, при непустом
`AddressBLE` конфигурации список из него одного и индекс 0 (перенесено из
`ObsidianDeviceForm.LoadFormContents`). `ElementAt(-1)` не тронут (решение распорядителя).

**Замер** (форма после «поиска» в конфигурации A, адрес 111111111111):

| конфигурация | до | после |
|---|---|---|
| B, свой адрес 222222222222 | сохранено, адрес **111111111111** | сохранено, адрес **222222222222** |
| C, серийник без адреса | сохранено, адрес **111111111111** | `false` (`ElementAt(-1)`), адрес остался пустым |

## Приёмка целиком

- Сборка `Debug` в `bin\Debug_fixdev` — код 0, предупреждений в своих файлах нет.
- `python tools/check_all.py` — 41 из 44 зелёные; три отказа (`check_fsa_report_view`,
  `check_corpus_scenes`, `check_fsa_showcase`) — код 3 «каталог проб протух»: дерево правят и
  другие полосы. Собран свой каталог `build_all.ps1 -Bin bin\Debug_fixdev -Out
  tools\effmaker\probes\build_fixdev` (код 0, 301 проба, набор приложения `90328b3233e4`) и три
  сторожа прогнаны с `--probes` на него — все три код 0: `check_fsa_report_view` «ВСЕ СОШЛИСЬ»
  (326 строк ok, 0 ⛔), `check_corpus_scenes` 51/51 сцен, `check_fsa_showcase` 9 пар совпали с
  эталоном. Каталог после приёмки снят.
- Переводы строк: `RadiaCodeIn.cs` после `sed -i` Git Bash стал LF целиком — возвращён в CRLF
  побайтно (1654 CRLF, 0 LF); прочие файлы — CRLF.

## Находки

- `ObsidianDeviceForm.LoadFormContents` (не файл полосы): при конфигурации с серийником, но без
  адреса `currentBLEindex` не сбрасывается, а `SaveFormContents` в этом случае уходит в ветку
  «пусто» и ОБНУЛЯЕТ `DeviceSerial`, `AddressBLE` и `OBS_EnergyCalibration`. Конфигурация с
  серийником без адреса штатно не возникает (форма пишет их парой) — только старая или
  исправленная руками.
