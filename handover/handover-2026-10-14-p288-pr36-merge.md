# 14.10.2026 — П288: PR #36 автора поверх ночного A318 и П280 (`AmplitudaSerial`)

## Постановка

Вопрос Amber 14.10.2026 вопросником: «PR #36 автора и наш ночной `45fbfc64` (A318, уже в
origin/master) делают одни и те же пять пунктов в тех же файлах AmplitudaSerial. У автора —
проверка на блоке и в окне, у нас — только фиктивный блок. Рядом идёт полоса П280 (те же файлы,
момент чтения заголовка). Что делаем?»

**Решение Amber, дословно: «Взять #36 сами, наше снять».** Выбранный вариант: «Влить master в
ветку автора, в пересечении брать его код (проверен на блоке), сохранить наши непересекающиеся
добавки (текст отказа при другой паузе, откат времени и т.п.) и П280, тесты обеих сторон.»

- PR: https://github.com/Am6er/BecqMoni/pull/36 — `Motokichirou/BecqMoni`, ветка
  `amplituda-serial-2`, голова `bdef2502`, база `master` (общий предок с нашим `master` — `13bade5b`).
- Наша сторона: `45fbfc64` (A318, П249; журнал
  [handover-2026-10-14-p249-a318-amplitudaserial.md](handover-2026-10-14-p249-a318-amplitudaserial.md)),
  `018a6d5f` (П280; журнал
  [handover-2026-10-14-p280-amplitudaserial-live-moment.md](handover-2026-10-14-p280-amplitudaserial-live-moment.md)).
  П250 `b998fc54` файлов AmplitudaSerial не трогал, только ресурсы и панель скорости счёта.
- Проверка автора на блоке — описание PR и его комментарий 13.10.2026 20:45 (окно: пропадание
  питания — одно окно, «Стоп» при выключенном блоке — без сообщения, «Стоп» после отключения
  питания — 6–8 с и сообщение о неполном «Стопе», уставка с автосохранением — сохранение не ждёт окна).

Работа — в worktree `D:\BqMoni_Claude\p288\wt`, локальная ветка `p288-pr36` (= `pull/36/head`) +
коммит слияния `master`. Ни `push`, ни `gh pr merge`, ни комментариев в PR — отправку решает Amber.

Влит `master` = `56f6db12` — это `018a6d5f` (П280) и его следствия в ресурсах (текст инструкции
AmplitudaSerial и подсказка мёртвого времени, en/ru), слились без конфликта. После слияния `master`
ушёл до `ac5596a0` (П287, П281, журнал, П289); файлов AmplitudaSerial, ресурсов и проекта эти коммиты
не трогают, повторное слияние чистое (`git merge-tree` — без конфликтов).

## Конфликты git

Семь файлов: `AmplitudaSerialLine.cs`, `AmplitudaSerialAcquisition.cs`,
`AmplitudaSerialDeviceController.cs`, `tools/AmplitudaSerialTests/AcquisitionTests.cs`,
`FakeBlockPort.cs`, `LiveSmoke.cs`, `run-tests.ps1`. Ресурсы (`Resources*.resx`,
`Resources.Designer.cs`) и `BecquerelMonitor.csproj` слились сами: автор ресурсов не трогал, в
проекте у него одна строка `AmplitudaSerialReporter.cs`. Сам собой слился и `LineTests.cs` — с
тестами обеих сторон.

## Пересечения: что было у нас, что у автора, что осталось и почему

| пункт | у нас (A318, `45fbfc64`) | у автора (#36, `bdef2502`) | осталось | почему |
|---|---|---|---|---|
| (2) бюджет «Стопа» | `AmplitudaSerialTiming.StopBudgetMs(каналы, пауза)` = 2 с + 2 худших чужих шага + свой останов дважды. 1024 канала: 12.4 с при 20 мс, 24.7 с при 200 мс; 8192: 34.3 и 66.7 с | `AmplitudaSerialLine.StopBudgetMs(каналы)` = прежняя формула + 2·(пауза − 20) на каждый обмен худшего случая. 1024: 8.0 и 14.84 с; 8192: 27.5 и 49.46 с | автора. Класс `AmplitudaSerialTiming` снят целиком, константы линии снова `private` | пересечение; у автора замерено на блоке (2.6 с при бюджете 8.0; 5.9 с при 14.8). Наши тесты «Стоп за медленным чужим чтением» перенесены на его бюджет и проходят (числа ниже) |
| `Join` линии | `AmplitudaSerialTiming.JoinTimeoutMs(пауза)` = шаг + 4 команды + 1 с: 4.1 и 7.7 с | `JoinMs` = max(3000, шаг + 2 команды + 0.5 с): 3.9 и 6.8 с | автора | пересечение; наш поведенческий тест `TestReleaseWaitsForASlowStepAtALongGap` (шаг держится 4.4 с при паузе 200 мс; «стоп» обязан уйти до закрытия порта) проходит на его `Join` и оставлен — у автора только числа `JoinMs` |
| (3) одно окно за раз | `AmplitudaSerialNotices`: одни ворота на все документы (`static`), накопленное — одним окном, при закрытии программы молчит, упавшее окно не запирает ворота | `AmplitudaSerialReporter`: на контроллер; повтор показываемого или ждущего схлопывается; прочее — окно за окном | класс автора и его вызовы. Добавлено наше, чего у автора нет: (а) `static` — одни ворота на все документы; (б) третий конструктор с `closing`, контроллер передаёт `MainForm.ApplicationClosing`; (в) упавшее окно не выходит из `Drain` и не уносит очередь | (а) у автора при N документах модальный цикл первого окна разбирает `Post` второго — окна снова вложены (автор сам пишет «до N, по одному на документ»); (б) пункт 4 ревизии 13.10.2026; (в) перенесённый тест на коде автора краснеет обеими проверками — исключение выходит из `Post` в `Application.ThreadException` (окно аварии), сообщение за ним теряется |
| (5) отменённое чтение | `Cancel` снимает из всех трёх очередей любой запрос (и Status, и Control), `Completed` не зовётся; набор ведёт список `outstanding` всех невернувшихся | `Cancel` только для Read: из очереди — сразу, `Completed` с ошибкой `cancelled`; идущее — на границе шага (`Step`); набор держит один `pendingRead` | автора. Сняты `outstanding`, `InboxItem.Request`, наши `Cancel`/`Remove`, обрезка отменённых в `Run`, `ReadJob.Cancelled` | пересечение. Наш тест `TestAbortTakesItsReadOffTheLine` (Abort, когда последнее чтение уже НА ПРОВОДЕ, держится на заголовке) проходит на коде автора и оставлен: у автора на уровне набора — только чтение, ждущее в очереди |
| «Стоп» до первого ответа | условие `!session.Armed` — и до ответа, и пока блок не проверен пустым после старта, перелива, очистки; без отката | условие `!answered`, `RollBackToLastGood()` | код автора, условие расширено: `!answered \|\| !session.Armed` | `!answered` влечёт `!Armed` (сессия взводится только после удачного чтения). Вторая ветка — наше: тест `TestStopBeforeTheBlockIsClearedReadsNothing` на коде автора краснеет — «Стоп» читает заголовок блока, содержимое которого не принадлежит документу; при замолчавшем посреди перезапуска блоке это кончалось бы «неполным Стопом» без потери. Откат на второй ветке ничего не меняет: на всех путях, снимающих взвод, `realCommitted == realAtLastGood`. Наш `TestStopBeforeTheFirstAnswerEndsAtOnce` снят — дубль `TestStopBeforeTheFirstAnswerFinishesQuietly` автора (у того проверок больше) |
| комментарии такта, `FinishSession`, «Стопа» | про `notices` | про `reporter` | автора; в двух наших комментариях `notices` → `reporter` | — |
| `LiveSmoke` / `run-tests.ps1` | П280: аргумент 5 — число каналов, остановка по часам (`TimeView.Clock`), печать мёртвого времени и скорости | аргумент 5 — пауза, `Thread.Sleep(20)`, печать длительности «Стопа» против бюджета | оба: аргумент 5 — пауза (как у автора), 6 — каналы; остановка по часам (П280), шаг 20 мс и печать «Стопа» (автор), бюджет — по числу каналов, не по 1024; в `run-tests.ps1` оба ключа `-Gap` и `-Channels` | на проводе ничего не меняется; ключи именованные, порядок в exe внутренний |
| `FakeBlockPort.Write` | П280: `WireMs.Add(nowMs)` | `OnWire` после записи кадра | оба: сперва `WireMs`, потом `OnWire` | — |

### Снятые наши тесты

- `NoticesTests.cs` целиком (4 теста, 9 проверок). `TestOneBoxAtATimeAndNeverInsideTheCaller` и
  `TestGateReopensAfterTheBoxIsClosed` покрыты тестами автора (`ReporterTests`: повторный вход в
  показ, схлопывание, отложенный показ, повторный показ после разбора); их ожидание «накопленное —
  одним окном через пустую строку» — свойство нашего механизма, у автора окна идут друг за другом.
  `TestFailedBoxDoesNotJamTheGate` и `TestNothingIsShownWhileTheApplicationCloses` ловят то, чего
  тесты автора не ловят, и **перенесены** в `ReporterTests` на его механизм:
  `TestFailedBoxIsContained` и `TestNothingIsShownWhileTheApplicationCloses`.
- `AcquisitionTests.TestStopBeforeTheFirstAnswerEndsAtOnce` — дубль теста автора.

### Оставленные наши тесты на коде автора

Наш тестовый стенд (`FakeBlock.FlakyTries`, `ArmHold`/`ReleaseHold`, `WireCountAtDispose`, страницы
по `Channels.Length`) сохранён — на нём стоят эти тесты.

| тест | что ловит | на коде автора `bdef2502` |
|---|---|---|
| `TestStopBudgetCoversASlowLineAt1024Channels` / `…At8192Channels` | «Стоп» за чужим чтением со всеми тремя попытками на шаг при паузе 200 мс укладывается в бюджет | проходит: 1024 канала — «Стоп» занял 13367 мс линии при бюджете 14840 (запас 1.5 с, 10 %); 8192 — 32757 при 49460 |
| `TestAbortTakesItsReadOffTheLine` | после `Abort` ни одной страницы мёртвого чтения, уже идущего по проводу | проходит |
| `TestStopBeforeTheBlockIsClearedReadsNothing` | «Стоп» до проверки блока пустым не читает блок | **краснеет**: прочитан 1 заголовок |
| `LineTests.TestReleaseWaitsForASlowStepAtALongGap` | «стоп» уходит до закрытия порта при шаге 4.4 с | проходит |
| `LineTests.TestOpenPauseIsKnownForTheRefusal` | пауза открытой линии для текста отказа | **краснеет**: `OpenGapMs` у автора нет |

## Наши непересекающиеся добавки — сохранены как были

- `Pump()` разбирает только пришедшее к вызову (гонка стенда на виртуальном времени, П249).
- `RefuseStart`: отказ «Старта» без окон — броском, `catch` в `StartMeasurement` перебрасывает
  (ревизия П252, пункт 5; образец USB из A316).
- `AmplitudaSerialLine.OpenGapMs` и `DescribeAcquireError` с `{1}`; текст
  `ERRAmplitudaSerialSettingsDiffer` en/ru — из `master` без изменений (ревизия, пункт 12).
- Ресурсы — из `master` без изменений: автор ресурсов не трогал, конфликта нет.

## П280 — целиком

Ничто из П280 не пересекалось с механизмами автора по смыслу; текстом — только `FakeBlockPort`,
`LiveSmoke`, `run-tests.ps1` (таблица выше). В слиянии: отметки `HeaderMs`/`PageMs`/`SentMs`,
`ReadBlock(…, out askedMs)`, `segmentAnchorMs`, `CountsMomentMs`, `AlignOpenLive`, `RealSeconds`
(время данных) / `RealSecondsNow` (часы) / `ElapsedTicks`, `OpenSegmentLive`/`AdjustOpenLive`,
`Publish` пишет `ElapsedTicks(PresetTime)`; тесты `MomentTests`, `MomentApiTests`, правки
`SessionTests` и `AcquisitionTests`. Ветка автора «Стоп до первого ответа» откатывает время
`RollBackToLastGood()` и ставит `finished` — после этого `RealSeconds` = `realCommitted`, как у
любого конца набора по П280.

## Приёмка

Раннеры тестов собраны `csc` по рецепту `run-tests.ps1`, но в `D:\BqMoni_Claude\p288\` (сам
`run-tests.ps1` пишет exe рядом с исходниками). Сторона «до» — выгрузка `git archive` своей ревизии.

| дерево | `AmplitudaSerialTests` | `AmplitudaUsbTests` |
|---|---|---|
| ветка автора `bdef2502` | 319 из 319 | 81 из 81 |
| `master` `56f6db12` (A318 + П280) | 359 из 359 | 121 из 121 |
| слияние | **407 из 407**, шесть прогонов подряд | **121 из 121** |

Счёт проверок: общий предок `13bade5b` — 261; автор +58; наша сторона +98 (A318 +45, П280 +53);
снято 15 (`NoticesTests` — 9, `TestStopBeforeTheFirstAnswerEndsAtOnce` — 6); перенесено 5
(`TestFailedBoxIsContained` — 2, `TestNothingIsShownWhileTheApplicationCloses` — 3).
261 + 58 + 98 − 15 + 5 = 407.

**Положительный контроль — тесты слияния на коде автора** (исходники `bdef2502`, тесты слияния без
`MomentApiTests` и без теста закрытия — им нужны члены, которых у автора нет): 368 прошли, 15 упали,
и все пятнадцать ожидаемы:
- 11 — П280 (8 проверок `MomentTests`, 3 `SessionTests` на +0.5 с у открытого отрезка) — П280 у
  автора нет, числа те же, что в журнале П280 на коде до правки;
- `TestStopBeforeTheBlockIsClearedReadsNothing` — 1 (прочитан заголовок непроверенного блока);
- `TestOpenPauseIsKnownForTheRefusal` — 1 (`OpenGapMs` нет);
- `TestFailedBoxIsContained` — 2 (исключение вышло из `Post`; «queued behind it» потеряно, показано
  только «after»).

**Мутация** закрытия: из `AmplitudaSerialReporter` слияния убраны обе проверки `closing()` —
`TestNothingIsShownWhileTheApplicationCloses` краснеет двумя проверками (2 окна при закрытии).

**Медленный «Стоп» против бюджета автора** (печать вставлена в копию теста, не в дерево): пауза
200 мс, чужое чтение со всеми тремя попытками на шаг — 1024 канала: «Стоп» занял 13367 мс линии при
бюджете 14840; 8192 канала: 32757 при 49460. Это те же 13367 и 32757 мс, что П249 мерил на коде
до A318: длительность здесь задаёт чужое чтение, а не отмена, и бюджет автора её покрывает.

**Сборка приложения** в worktree: `MSBuild … -restore /t:Build /p:Configuration=Debug
/p:SignManifests=false /p:GenerateManifests=false /p:OutputPath=bin\Debug_p288\
/p:IntermediateOutputPath=obj\p288\` — 0 ошибок, 0 предупреждений во всём проекте. В exe есть
`AmplitudaSerialReporter`, `StopBudgetMs`, `JoinMs`, `QueuedReads`, `OpenGapMs`, `RefuseStart`,
`ElapsedTicks`, `RealSecondsNow`; нет `AmplitudaSerialNotices` и `AmplitudaSerialTiming`.

**`python tools/check_all.py`** в worktree: 39 из 46 зелёные, красные 7 — окружение копии, не
слияние: нет каталога проб `tools/effmaker/probes/build` (`check_fsa_report_view`,
`check_corpus_scenes`, `check_fsa_showcase`, `check_bq_summing` — T226), нет сырья корпуса
`_corpus_raw` (`check_corpus_coverage`), нет каталога базы `tools/pie/out_rev46_full`
(`check_declared_base`), сводка корпуса не пересобрана в копии (`check_corpus_generator`). Ни один
сторож `check_*.py` не читает файлы `Amplituda*` (поиск по дереву — ноль совпадений).
`check_registry`, `check_no_images`, `check_probe_numbers` — зелёные.

Переводы строк: блобы — LF, как были (`git diff --stat` против обоих родителей без переписанных
целиком файлов); BOM у всех правленых файлов сохранён.

Приложение с прибором не запускалось: к COM4 подключён живой Atom Spectra, блока «Амплитуда» нет.
Показ окон проверен только тестами на поддельном цикле сообщений.

## Что осталось

- Отправка в ветку автора (`--no-follow-tags`, см. [журнал PR #34/#35](handover-2026-10-13-pr34-pr35-amplituda.md))
  и слияние PR — решение Amber.
- Автору стоит знать о трёх наших добавках к его механизмам (одни ворота на все документы, тишина
  при закрытии программы, упавшее окно не выходит в цикл сообщений) и о расширенном условии
  «Стопа» до проверки блока: в окне на блоке они им не проверены.
- Запас бюджета автора на медленной линии — 10 % (13367 мс при 14840, 1024 канала, пауза 200 мс,
  три попытки на каждый чужой шаг). Это худший случай стенда, не замер на блоке; факт, не задача.
- ⚠ Один неопознанный отказ: первый прогон копии с печатью (исходники слияния; тесты `Program`,
  `FakeBlockPort`, `AcquisitionTests`, `MomentTests`) дал 218 из 219 — имя упавшей проверки в тот
  прогон не записывалось. Повтор той же сборки 6 × 219 из 219, полный набор слияния 6 × 407 из 407 —
  не воспроизвелось. Похоже на гонку стенда на виртуальном времени под чужой нагрузкой (ср. П249,
  «Попутно»); причина не установлена.
- Строка `A318` в `TODO.md` и журнал П249 называют `AmplitudaSerialNotices` и
  `AmplitudaSerialTiming`, которых после слияния нет. `TODO.md` эта полоса не трогала по заданию —
  поправка строки за распорядителем.
