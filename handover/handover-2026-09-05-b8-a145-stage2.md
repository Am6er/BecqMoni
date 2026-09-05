# Полоса B8, 05.09.2026: A145 этап 2 — сеанс расчёта и построитель представления вместо `FsaOverlay`

Ветка `pie`. Работа велась в рабочем дереве git `C:\Users\moroz\source\repos\bq_b8`
(HEAD `585a6f39` + 12 файлов этапа 1, скопированных побайтно из основного дерева, + правки
этой полосы), потому что основное дерево не собирается чужим `EfficiencyMakerForm.Designer.cs`
(`airWarningLabel`). Свои каталоги: приложение `BecquerelMonitor\bin\Debug_B8` (`obj\B8`),
пробы `tools\effmaker\probes\build_b8`, оснастка `tools\CORPUS\scripts\wd_b8` (собрана
`mk_appwd.ps1` ИЗ дерева `bq_b8`, склад матриц — `mini16` основного дерева). Коммитов нет.
Трактовки — по `handover/a145-fsa-display-groups.md` (разделы «Владение расчётом и данными»,
«Единая модель представления», «Что удалить», «Критерии приёмки») и по журналу этапа 1
`handover/handover-2026-09-05-b4-a145-stage1.md`.

## Что сделано

### 1. `FsaAnalysisSession` (`BecquerelMonitor/FullSpectrumAnalysis/FsaAnalysisSession.cs`, новый)

Расчётная половина прежнего `FsaOverlay`, без обязанностей UI. Принадлежит документу:
`DocEnergySpectrum` заводит поле `fsaSession` и отдаёт его виду в конструкторе
(`this.view.FsaSession = this.fsaSession`), свойство `DocEnergySpectrum.FsaSession` — точка
подключения окна отчёта (этап 3); `OnFormClosed` зовёт `Reset()`.

Контракт (по документу): `Result`, `Stamp`, `Status`, `IsRunning`, `ResponseMatrixOldFormat`,
событие `Completed` (из фонового потока, один раз на серию), `EnsureUpToDate(rd, bg)` и
`EnsureUpToDate(rd, bg, FsaCalculationOptions)`, `IsUpToDate`, `Invalidate()` (обесценить кэш,
не трогая результат — для «потребителя нет»), `Reset()` (сброс + поколение),
`TakeResponseMatrixNotice()`, `static BuildStamp(rd, bg[, options])` — ОТКРЫТЫЙ.

* **Пять флажков доезжают до расчёта.** Снимок `FsaCalculationOptions.Of(rd)` берётся на
  UI-потоке; `options.ApplyTo(analyzer)` после `new FsaAnalyzer()`, `options.ApplyTo(spec)`
  вместо `spec.Equilibrium = …`, `FsaLibrary.BuildFromPeaks(peaks, defs, crystal,
  options.AtomicXray)`, в отпечатке `options.Stamp` вместо двух кусков `db/peaks` и `eq/free`.
  Пока их переключает только конфигурация/пробы — UI этапом 3.
* **Правило последнего снимка (критерий 10).** Снимок входных данных (`Job`: клоны спектра,
  фона, калибровок, кривой, списков, настроенный анализатор, отпечаток, поколение) берётся
  на UI-потоке ЦЕЛИКОМ. Если счёт идёт, новый снимок становится в очередь из ОДНОГО места
  (`pending`), вытесняя предыдущий; по окончании счёта очередь запускается сама
  (`Finish` → `Start(next)`), без участия потребителя. Результат счёта, к моменту окончания
  ВЫТЕСНЕННОГО или чужого поколения, НЕ публикуется. Итог: на серию из N переключений —
  ровно одна публикация, отпечаток последнего снимка. `Completed` поднимается только когда
  сеанс дошёл до покоя.
* **Критерий 7.** Отпечаток не знает о группировке; группировка живёт в представлении.
* Признак «старая матрица» и заготовленное сообщение (`A50`) ставятся при снимке, как прежде
  (`ResponseMatrixFormProbe` спрашивает их до окончания счёта). Слова отказа (`A95`) — те же.
* Задвижка для проб: закрытое статическое `probeGate` (`WaitHandle`), которое фоновый счёт
  ждёт перед разбором; в приложении всегда `null`. Без неё гонки критерия 10 не воспроизвести.

### 2. `FsaPresentationBuilder` + модель (`FsaPresentation.cs`, `FsaPresentationBuilder.cs`, новые)

Модель: `FsaGrouping {Daughters, Parents}`, `FsaReportRowKind {Status, Layer, SumPeaks,
Undetected, UndetectedFolded, NoBackground, Residual, Quality}` (семь родов + строка
состояния), `FsaSwatchKind {None, Solid, SumPeakHatch, ResidualCross}`,
`FsaReportRow {Kind, Name, Value, Swatch, Color, Layer, Muted, Warning, Collapsible}`,
`FsaPresentation {Source, RequestedGrouping, Grouping, ParentGroupingAllowed,
ParentGroupingRefusal, MatrixOldFormat, Layers, Colors, Rows, QualityText, ColorOf()}`.

`FsaPresentationBuilder.Build(result, grouping, matrixOldFormat)` — чистый, без вида и
`Graphics`: слои (`BuildStackedLayers(DefaultMaxNamedLayers)` у дочерних, `BuildParentLayers`
у родителей), цвета одной раздачей `FsaPalette.Assign`, строки в фиксированном порядке
документа. Сюда дословно переехали из отрисовки: `QualityText` (бывший
`EnergySpectrumView.FsaQualityText`), `RowName` (`S72`), `ShareText` (`S85`), `LimitText`,
`LimitSharePercent` (`S68`), `Undetected/UndetectedNamed/UndetectedFolded` (`S9`, `S69`),
`MinTotalYieldPercent` (бывший `FsaMinTotalYieldPercent`), `ResidualColor` (`A28`).
Правила `S44`, `S51`, `S86`, `S87`, `S104`, `S111`, `A96` не изменены.

**Родительский режим** — только при `result.ParentGroupingAllowed`; иначе показываются
дочерние, `Grouping = Daughters`, `RequestedGrouping` помнит просьбу, причина —
`ParentGroupingRefusal`. Слияние ДО выбора верхних N: берутся ВСЕ дочерние слои без свёртки
(`BuildStackedLayers(int.MaxValue)` — тот же знаменатель, тот же отсев `S87`), члены одного
`DecayChainRoot` (кроме приборных) складываются в слой корня (`Kind = Chain`, `ChainRoot =
DecayChainRoot = корень`, кривые, сумм-пики и доли суммируются); затем верхние N по доле,
остаток — «прочее», порядок ранг/доля как у дочерних. Строки необнаруженных кандидатов в
обоих режимах одинаковы (у связанного ряда предел один — колонки ряда; члены свободного ряда
до родительского режима не доходят по правилу допустимости).

### 3. `FsaOverlay.cs` удалён; график читает сеанс и модель

`BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` удалён из дерева и из `.csproj`
(строка заменена тремя новыми). `EnergySpectrumView.Fsa.cs` (1560 → 780 строк): поле
`fsaSession` + `internal FsaSession` (сеанс документа; вид без документа — пробы — заводит
свой при первом обращении, подписка переезжает вместе с сеансом), `internal FsaGrouping`
(смена перестраивает только представление и перерисовывает), кэш `FsaPresentation`
(перестраивается при смене результата, группировки или признака старой матрицы). Своего
анализа у вида нет (критерий 4); ленты, штриховка сумм-пиков, невязка и линии рисуются по
`presentation.Layers/Colors`. Имена `UpdateFsaOverlay`, `ResetFsaOverlay`, `ShowFsaOverlay`,
`ShowFsaTable`, `FsaNetSpectrum`, `ExtendBoundariesWithFsaModel` ОСТАВЛЕНЫ — их зовёт чужой
`EnergySpectrumView.cs`, и отражением — `FsaStackShot`.

**Что осталось временно** (по разрешению задания, до `FSAReportView` этапа 3): ручная
таблица под панелью курсора — `DrawFsaOwnTable` / `DrawFsaRows` / `DrawFsaSwatch` /
`DrawFsaQualityRow`, бюджет высоты `FsaTableBudget` (`S73`, его читает `FsaPaletteProbe`),
строка `FSARowsDidNotFit`, усечение хвоста качества `FsaQualityMarksWidth/Format` (`S104`),
зависимость от `CursorPanelWidth` (в чужом `EnergySpectrumView.cs`). Рисует она ИЗ МОДЕЛИ:
один проход по `presentation.Rows`, род строки — из `Kind`, а не из текста. Тонкой обёртки
`FsaOverlay` нет — класс удалён целиком.

### 4. Пробы

* `FsaSessionProbe.cs` (новая): четыре раздела, каждый с положительным контролем (ниже).
* `FsaQualityRowProbe.cs` переписана: пиксельная приёмка старой легенды снята; проверяется
  СТРОКА МОДЕЛИ (`FsaReportRowKind.Quality`) в `ru-RU` и `en-US` — четыре сочетания матричной
  пометки, цепь `FsaAnalysisSession.matrixOldFormat → ResponseMatrixOldFormat → построитель →
  строка`, полная сцена `подавлен · старая матрица · суммирование (без кривой) !` без
  многоточия, число в своей колонке; положительный контроль — вырезанная пометка, урезанный
  многоточием текст, подменённая строка.
* `FsaStampProbe`: отражение на `FsaOverlay.BuildStamp` заменено открытым
  `FsaAnalysisSession.BuildStamp`; три старые проверки и 128 раскладок — как были.
* `FsaStackShot`: результат кладётся в `FsaSession` вида (через `FsaSession`, поля `result`,
  `running`, `status` сеанса).
* `RefusalWordsProbe`, `ResponseMatrixFormProbe` (не «мои», но иначе не собирались): `new
  FsaOverlay()` → `new FsaAnalysisSession()`, подпись та же; в основном дереве правка
  наложена ТЕКСТОМ на чужие незакоммиченные версии, а не копией файла.
* Комментарии с именем `FsaOverlay` в своих файлах (`FsaAnalyzer.cs`, `DocEnergySpectrum.cs`,
  `CorpusFsaProbe.cs`) переписаны; в чужих (`DCPeakDetectionView.cs`, `EnergySpectrum.cs`,
  `GlobalConfigManager.cs`, `SerialInputDeviceConfig.cs`, `CorpusEffProbe.cs`, и в чужих
  свежих строках `RefusalWordsProbe.cs:60,272` про `FsaOverlay.cs:251`) — не тронуты.

## Числа (сборка `bq_b8` 05.09.2026 ~00:20, оснастка `wd_b8`, все выводы в `handover/b8-*`)

**`FsaSessionProbe`** на `ASN16_Th232.xml` (ряд Th-232, без матрицы) с контролем
`G1S16_Co60_P5.xml` (без ряда, 1024 канала против 8192) — `b8-probe-session.txt`, код 0,
«ВСЕ СОШЛИСЬ»:

| раздел | мера | получено |
|---|---|---|
| 1 отпечаток | 128 раскладок настроек на НАСТОЯЩЕМ спектре | 128 разных, столкновений 0, одиночных перестановок без смены 0 |
| 1 группировка | построение в обоих положениях после счёта | слоёв 12 → 5, строк 16 → 9; отпечаток сеанса тот же, событий не прибавилось, сеанс не занят |
| 2 серия | 8 снимков за закрытой задвижкой (первый — по пикам, последний — NucBase+равновесие) | публикаций **1**; отпечаток = последнего снимка, ≠ первого; результат с СВЯЗАННЫМ рядом (родители допустимы); контроль: первый снимок в одиночку ряда не даёт |
| 3 (а) | счёт A на задвижке, `Reset`, заказ B, задвижка открыта | результат B (1024 канала), отпечаток B, событий 1 |
| 3 (б) | счёт A, `Reset`, без заказа | результат отвергнут: `Result == null`, отпечаток пуст |
| 3 (в) контроль | тот же счёт A без сброса | опубликован (8192 канала) |
| 3 (г) | поколение поднято отражением, без сброса очереди | результат отвергнут |
| 4 построитель | Th-232, NucBase+равновесие, корней 1 (Th-232), членов 8 | доля 86.602 % = Σ 86.602 %; \|Δкривой\|/max **0.0E+0**, \|Δсумм-пиков\|/max 0.0E+0, \|Δдоли\| 0.0E+0; верх стека в обоих режимах \|Δ\|/max 0.0E+0; контроль «сумма без одного члена» — отказал |
| 4 без ряда | `G1S16_Co60_P5`, NucBase+равновесие | родители недопустимы, причина «в составе нет ряда распада», при запросе родителей показаны дочерние, запрошенное запомнено |
| 4 по пикам | `ASN16_Th232` по подписям пиков | родители недопустимы |

Строки состава идут первыми и их 12 = слоёв; цвет строки = цвет слоя из одной раздачи в
обоих режимах; строка качества — полный текст построителя, без многоточия.

**`FsaQualityRowProbe`** — `b8-probe-qualityrow.txt`, код 0, «ВСЕ СОШЛИСЬ»: обе культуры,
пять сцен, самая тяжёлая по-русски «χ²/ndf · подавлен · старая матрица · суммирование (без
кривой) !», по-английски «χ²/ndf · suppressed · old matrix · summing (no eff) !» — строка
модели равна тексту, все пять пометок на месте, многоточия нет, `2,94`/`2.94` в колонке
значения; четыре положительных контроля отказали как должны.

**Регресс** (та же оснастка): `FsaRobustnessProbe` 0, `FsaPaletteProbe` 0 (`ASN16_Cs137`),
`FsaFlagsProbe` 0, `FsaStampProbe` 0 (`ASN16_Th232`, 128/128 и три старые проверки),
`FsaDoubleCountProbe` 0 (`G1S16_Th228_P5 --chain=Th-232`), `ChainProbe` 0, `EscapeGateProbe` 0,
`RefusalWordsProbe --arm=fsa` 0 («ПРИЧИНА НАЗВАНА» — слова отказа сеанса те же), `FsaStackShot`
0 три раза (`G1S16_Th228_P5`: по пикам с матрицей — `b8-stackshot-th228-peaks.png`, снимок
глазами: 5 строк состава, 4 строки сумм-пиков штрихом в цвете слоя, 3 серых «< … %», невязка
клеткой `+4,9 / −8,7 %`, качество `χ²/ndf · матрица · суммирование  3,32`; `--infer`;
`--infer --calculating`). Все пробы собрались: 100 (`build_all.ps1`, код 0), оснастка
`mk_appwd.ps1` — 257 файлов по sha256, клейма склада и оснастки сошлись.

**Критерий 13, `run_mini.ps1`** (`b8-run_mini-after.txt` против `b4-run_mini-after.txt`):

| часть | до (B4b) | после (B8) | Δ Σχ² |
|---|---|---|---|
| known (42) | 556.3 / 5.92 / 100 % / 0, подавлен 1 | 556.3 / 5.92 / 100 % / 0, подавлен 1 | 0.0 |
| unknown (17) | 205.3 / 4.95 / 96 % / 0, подавлен 1 | 205.3 / 4.95 / 96 % / 0, подавлен 1 | 0.0 |

Невязка модели: known 29.4 % (13.7..52.6), unknown 32.8 % (21.7..37.2) — без изменений.
Корпусный путь (`CorpusFsaProbe`) сеанс не зовёт; Δ = 0 говорит, что перенос фасада и
модели результата ничего не сдвинул.

## Что НЕ сделано и что найдено

* **Родительский режим на спектре с матрицей и сумм-пиками пробой НЕ подтверждён.**
  `FsaSessionProbe` на `G1S16_Th228_P5` и `G1S24_Th228_P5` (`b8-probe-session-th228.txt`,
  `b8-probe-session-g1s24th228.txt`, код 1) не дошёл до ряда: вывод состава из пиков (`S57`)
  на точечном источнике Th-228 даёт «Th-232 23 % (5/22) — доля ниже порога 30 %, оборванный
  ряд: не ищется» и NucBase-путь собирает библиотеку из одного рентгена Pb + подложки
  (см. `b8-probe-stackshot.txt`, `--infer`). Это НЕ дефект сеанса и не этого этапа — так же
  вёл себя `FsaOverlay` при галке «Из NucBase», — но человек с включённым источником
  «Из NucBase» на Th-228 увидит ровно это; строка реестра предложена ниже. Тождество
  «родитель = Σ дочерних» доказано на ASN16_Th232 (8 членов, машинный ноль); суммирование
  сумм-пиков в родителе покрыто тем же кодом (`AddInto`), но на нуле сумм-пиков.
* В основном дереве `bin\Debug_B8`/`build_b8` не собирались — нечем, пока чужой
  `EfficiencyMakerForm` не сойдётся; все числа — с дерева `bq_b8`, файлы перенесены
  побайтно (`cmp` на каждом), `git diff --stat` основного дерева: 11 файлов изменено,
  3 новых в `FullSpectrumAnalysis`, 1 новая проба, `FsaOverlay.cs` удалён.
* `tools/effmaker/probes/README.md` (чужой, правится другой полосой) описывает
  `FsaQualityRowProbe` и `FsaStackShot` по-старому (пиксели, `FsaOverlay`) — обновить
  этапом 3 или отдельной строкой.
* Пять флажков по-прежнему нечем переключить из окна (этап 3); прежние два флажка в
  `DCPeakDetectionView` не тронуты (этап 3).

## Что остаётся этапу 3 (имена, которые он обязан читать)

* Сеанс: `DocEnergySpectrum.FsaSession` (тип `FsaAnalysisSession`); `FSAReportView`
  подписывается на `Completed` (фоновый поток — маршалить `BeginInvoke`), читает `Result`,
  `Status`, `IsRunning`, `ResponseMatrixOldFormat`, зовёт `EnsureUpToDate(activeResultData,
  backgroundAvailable)` при показе как потребитель; `Invalidate()` — когда потребителя нет.
* Представление: `FsaPresentationBuilder.Build(session.Result, grouping,
  session.ResponseMatrixOldFormat)` — ТОТ ЖЕ вызов, что у графика (`GetFsaPresentation`);
  строки XPTable — `presentation.Rows` (`Kind`, `Name`, `Value`, `Swatch`, `Color`, `Muted`,
  `Warning`); состояния без результата — `FsaPresentationBuilder.OfStatus(text)` /
  `FsaReportRowKind.Status`. Группировка графика — `EnergySpectrumView.FsaGrouping`
  (internal); окну и графику держать одно значение — через документ.
* Радиокнопка родителей: `FsaCalculationOptions.Of(rd).ParentGroupingPossible` И
  `presentation.ParentGroupingAllowed`; подсказка — `ParentGroupingRefusal` (служебная
  строка; для перевода нужен enum — строка реестра B4b).
* Снять из `EnergySpectrumView.Fsa.cs`: `DrawFsaOwnTable`, `DrawFsaRows`, `DrawFsaSwatch`,
  `DrawFsaQualityRow`, `FsaTableRowCount`, `FsaCollapsibleBudget`, `FsaTableBudget` (и его
  читателя в `FsaPaletteProbe`), `FsaQualityMarksWidth/Format`, `ShowFsaTable`,
  `ResetCursorPanelBounds/RegisterCursorPanel` (их зовёт `EnergySpectrumView.cs` — чужой),
  ресурс `FSARowsDidNotFit`; оставить короткую строку состояния на графике.
* Предупреждение о старой матрице сейчас показывает вид из `Completed` (`AppUi.Report`)
  — переносить туда, где не зависит от видимости отчёта и не из `Paint`.

## Артефакты

`handover/b8-run_mini-after.txt`, `tools/pie/out_mini_b8_after/`; `handover/b8-probe-session.txt`,
`b8-probe-session-th228.txt`, `b8-probe-session-g1s24th228.txt` (два отрицательных, см. выше),
`b8-probe-qualityrow.txt`, `b8-probe-robustness.txt`, `b8-probe-palette.txt`, `b8-probe-flags.txt`,
`b8-probe-stamp.txt`, `b8-probe-doublecount-G1S16_Th228_P5.txt`, `b8-probe-chain.txt`,
`b8-probe-escapegate.txt`, `b8-probe-refusalwords.txt`, `b8-probe-stackshot*.txt`,
`b8-stackshot-th228*.png`. Дерево `bq_b8` оставлено: без него числа не воспроизвести.
