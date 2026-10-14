# 14.10.2026 — П285: AmplitudaUSB, явный серийник занятого блока — отказ второму документу

Полоса распорядителя 14.10.2026. Вопрос — ревизия П252 №6, журнал распорядителя
`handover-2026-10-14-night-coordinator.md`, «Ждут решения Amber», п. 2:

> USB, явный серийник у двух конфигураций: HID отдаёт каждому дескриптору свою копию кадров — два
> документа пишут один блок. Можно ли так при явно заданном серийнике?

**Решение Amber 14.10.2026 вопросником, дословно: «Отказывать второму (Рекомендую)».** Второму
документу, который пускается на блок с тем же явно заданным серийником, пока блок набирает в другом
документе этого приложения, — отказ с сообщением.

Основа — аренда физического блока `AmplitudaUsbBlockLeases` полосы П257 (`f5503951`, журнал
`handover-2026-10-14-p257-usb-first-found.md`): там явный серийник уже БРАЛ аренду (чтобы «первый
найденный» его блок обходил), но чужую не уважал. Образец поведения и текста —
`ERRAmplitudaSerialBlockBusy` у AmplitudaSerial (аренда «порт + адрес», PR #35/#36).

## Что изменено

`BecquerelMonitor/AmplitudaUsbReader.cs`:
- новый код отказа читателя **`ErrorBlockBusy = −6`**;
- **`SelectCandidates`**: явный серийник, чей блок держит ДРУГОЙ читатель реестра, — пустой список и
  `allHeld = true`. Проверка идёт по ключу `sn:<серийник>` (без учёта регистра, как и прежде) ДО
  перечня найденных: отказ «занят» и тогда, когда блок на миг выдернут, а его читатель
  переподключается, — иначе второй документ получил бы «не найден» и после возврата кабеля мог бы
  подумать, что блок свободен;
- **`Open`**: при `allHeld` — `ErrorAllInUse` для «первого найденного» (как было), `ErrorBlockBusy`
  для явного серийника; аренда теперь берётся ИСКЛЮЧИТЕЛЬНО в обоих режимах
  (`TakeLease(key, true)`; у «первого найденного» и раньше было `true`). Проигранная гонка (другой
  документ взял блок между перечнем и арендой) закрывает дескриптор до первого чтения и даёт тот же
  код отказа своего режима;
- комментарии реестра, `TryTake`, конструктора, `SelectCandidates`, `Open`; ключ `sn:` вынесен в
  `SerialKey` (поведение `BlockKey` то же).

`BecquerelMonitor/AmplitudaUsbDeviceController.cs` — только `DescribeOpenError`: ветка
`ErrorBlockBusy` → `string.Format(CultureInfo.InvariantCulture, Resources.ERRAmplitudaUsbBlockBusy,
serialNumber)`. `StartCore` (место П298, строки `StartTime`) не тронут: отказ «Пуска» идёт прежним
путём `newReader.Start` → `Dispose` → `RefuseStart` (с окнами — сообщение, без окон — бросок, A316).

Ресурсы — `Properties/Resources.resx`, `Resources.ru.resx`, `Resources.Designer.cs` (свойство по
образцу соседнего `ERRAmplitudaUsbAllInUse`). Ключ **`ERRAmplitudaUsbBlockBusy`**:
- en: `The Amplituda USB device with serial number {0} is already being measured in another document (another device configuration names the same serial number or took this device as "(first found)"). Stop that measurement first.`
- ru: `Блок Amplituda USB с серийным номером {0} уже набирается в другом документе (другая конфигурация устройства указывает тот же серийный номер или взяла этот блок как «(первый найденный)»). Сначала остановите тот набор.`

Серийник — строка, подставляется как есть: группировки разрядов нет по устройству.

### Чего правка НЕ меняет

- **«Первый найденный»** — те же ветки `SelectCandidates`/`Open`, тот же код и текст отказа; все
  прежние проверки П257 зелёные и до, и после правки. Единственное отличие — у состояния, которое
  правка и устраняет: явный серийник больше не держит аренду одного блока вместе с «первым
  найденным».
- **Переподключение своего блока** — свой ключ в `HeldByOthers(this)` не входит, своя аренда своё
  переподключение не запирает (проверки `TestReconnectKeepsOwnUnit` зелёные).
- **Кнопка «Проверка» формы устройства** — читатель вне реестра (`leases == null`), открывает и
  набираемый блок, как раньше (проверка в `TestExplicitSerialOfMeasuredUnitIsRefused`).
- **Один документ на конфигурацию** — аренда по GUID конфигурации `MeasurementController`
  (`AcquireDeviceLease`, `ERRDeviceBusy`) не тронута; новая аренда работает между РАЗНЫМИ
  конфигурациями одного блока.

## Где снимается аренда (check callers first)

Владелец аренды — читатель; у контроллера все пути конца сеанса сходятся в `ReleaseReader` →
`reader.Dispose` → `Stop` → `CloseLease`. Обойдены все вызывающие:
- **«Стоп»**: `DCControlPanel.StopMeasurement` / `MainForm` → `MeasurementController.StopRecording` →
  `StopMeasurement` → `FinishSession` (`reader.Stop`, затем `ReleaseReader`); его `catch` — тоже
  `ReleaseReader`; окончание по заданному времени (`MeasurementController.OnTimer`) — тот же
  `StopRecording`;
- **отказ связи**: поток читателя сам снимает аренду в `finally` своего `Run` (окно
  переподключения истекло или исключение) — ещё до такта таймера; затем `PollTimer_Tick` →
  `Fail` → `FinishSession`;
- **отказ «Пуска»**: читатель снимает в обоих путях отказа `Start`, контроллер затем зовёт
  `newReader.Dispose`; `StartMeasurement.catch` — `ReleaseReader`; при отказе по аренде читатель
  аренды не берёт вовсе (проверено: аренд в реестре не прибавилось);
- **закрытие документа и удаление спектра**: `MainForm.StopRecordingOrTesting` (закрытие документа,
  закрытие окна, выход) → `StopRecording`; затем `UnsubscribeDocumentEvent` → `CleanupMeasurementController` →
  `deviceController.Dispose` → `ReleaseReader`; удаление спектра — `StopRecording` +
  `CleanupMeasurementController`;
- **повторный «Пуск» того же документа**: `StartCore` → `ReleaseReader` перед новым читателем.

Флаг `leaseClosed` (П257) держит и здесь: после `Stop`/`Dispose` поток, застрявший в `Open`, аренду
обратно не возьмёт.

## Приёмка

### `tools/AmplitudaUsbTests` (фиктивный HID — пустой файл с `FILE_FLAG_OVERLAPPED`, `LeaseTests.FakeBench`)

Было 121 проверка, стало **157/157** (три прогона подряд, ≈12.5 с на прогон; прибавка времени — один
тест отказа связи ждёт окно переподключения 10 с по-настоящему). Новые методы — 8, проверок — 40;
снята одна прежняя проверка-страж «явный серийник занятого блока — как раньше» и прежний метод
`TestExplicitSerialKeepsPriorBehaviourAndHoldsItsUnit` (его проверка «первый найденный обходит блок
явного серийника» перенесена в `TestExplicitSerialAndFirstFoundShareOneRegistry`).

- `TestSelectExplicitSerialOfHeldUnitIsARefusal` — отбор: занятый чужим явный серийник — отказ (и при
  другом регистре); чужая аренда ДРУГОГО блока не мешает; занят и выдернут — отказ, а не «не найден»;
  никем не занят и не найден — «не найден»; вне реестра — блок пробуется;
- `TestExplicitSerialOfMeasuredUnitIsRefused` — **две конфигурации с одним явным серийником**:
  первая пускается, вторая — `ErrorBlockBusy`, аренды не держит, потока не запускает, первая блок
  сохраняет; `s1` в другом регистре — тоже отказ; явный `S2` при занятом `S1` — пускается;
  «Проверка» формы — открывает;
- `TestExplicitSerialAndFirstFoundShareOneRegistry` — блок, взятый «первым найденным», явному
  серийнику — отказ; третий «первый найденный» при двух занятых — прежний `ErrorAllInUse`; «первый
  найденный» обходит блок явного серийника;
- `TestExplicitLeaseReleasedOnStop` / `…OnDispose` / `…WhenReaderThreadFails` — отказ второму, затем
  снятие аренды первого, затем **второй пускается**;
- `TestExplicitLeaseReleasedWhenLinkLost` — **отказ связи**: блок выдернут, пока первый читатель
  переподключается — второму «занят»; первый сдаётся по окну (≈10 с), аренда снята; блок ещё
  выдернут — «не найден»; блок вернулся — второй пускается;
- `TestExplicitLostRaceClosesHandleAndRefuses` — гонка: дескриптор закрыт, отказ `ErrorBlockBusy`,
  отказавший аренды не держит.

**Положительный контроль — те же тесты на читателе ДО правки** (`git show HEAD:…AmplitudaUsbReader.cs`
плюс ТОЛЬКО шов: строка `public const int ErrorBlockBusy = −6;`, без поведения): **16 из 40 новых
проверок падают, у каждого из 8 новых методов — хотя бы один отказ**; итог 141/157. Второй документ
на HEAD пускается (`got True, error 0`), отказ связи даёт «не найден» (`error −1`), гонка берёт
аренду поверх чужой. Все прежние проверки («первый найденный», переподключение, снятие аренды,
гонка «первого найденного») зелёные на обоих кодах.

### Проба отражением `tools/effmaker/probes/UsbExplicitSerialBusyProbeP285.cs`

Безоконно, через настоящий `AmplitudaUsbDeviceController.StartMeasurement`. Первый документ изображён
арендой `sn:P285-NO-SUCH-UNIT` в общем `AmplitudaUsbBlockLeases.Measurements`; серийник заведомо
несуществующий — настоящий блок проба не открывает ни на одном плече (перечень HID без прав доступа —
тот же, что у «Пуска»).

- **после правки** (`build_p285`): **19/19, код 0**. Ключ en и ru есть; `DescribeOpenError` отдаёт
  его текст с серийником; «Пуск» второй конфигурации — отказ с текстом ключа в en-US и в ru-RU, набор
  не идёт, аренд в реестре по-прежнему 1 (у первого); после снятия аренды первого («Стоп») — «Пуск»
  проходит аренду и доходит до поиска блока («не найден (s/n P285-NO-SUCH-UNIT)», не «занят»),
  реестр пуст; «первый найденный» — `DescribeOpenError(ErrorAllInUse)` = `ERRAmplitudaUsbAllInUse` в
  обеих культурах;
- **до правки** (приложение канонического каталога проб, сборка 14.10 11:34 — П257 есть, П285 нет;
  проба собрана тем же `csc` с теми же ссылками в `D:\BqMoni_Claude\p285\control`): **код 1, отказов
  5 из 15** — нет кода `ErrorBlockBusy`, нет ключа en и ru, второй документ в обеих культурах проходит
  мимо аренды к поиску блока («не найден» вместо «занят»).

### Прочее

- `tools/AmplitudaSerialTests` — **407/407**, код 0.
- Сборка Debug в `bin\Debug_p285` / `obj\p285` — код 0, предупреждений по файлам полосы нет;
  `build_all.ps1 -Out build_p285` — код 0, каталог заверен (T226).
- `check_resx`, `check_resx_letters`, `check_resx_designer` — **СОШЛОСЬ**.
- `check_all.py` — **42 из 46**; красные четыре — `check_fsa_report_view`, `check_corpus_scenes`,
  `check_fsa_showcase`, `check_bq_summing`: все «КАТАЛОГ ПРОБ ПРОТУХ (T226)» канонического каталога
  (набор приложения разошёлся с заверенным в 12 файлах, в том числе у параллельной полосы П298). Те же
  четыре с `--probes tools\effmaker\probes\build_p285` — **все четыре СОШЛОСЬ** (окно отчёта FSA 4/4,
  множитель суммирования 36 линий, сцены корпуса, витрина 9 пар). Канонический каталог пересоберёт
  распорядитель.
- **На железе НЕ мерено**: блока Amplituda USB на машине нет.

## Заметка для распорядителя (не строка — `TODO.md` полосе трогать нельзя)

- Комментарий в `StartCore` контроллера («with "(first found)" the reader skips units other documents
  measure») описывает только «первый найденный»; явный серийник теперь тоже отказывает. Метод —
  место П298, полоса его не трогала.
- В `DescribeOpenError` у ветки `ErrorAllInUse` остался запасной английский текст «до заведения
  ключа распорядителем» — ключ заведён, запас мёртв, поведения не меняет.

## Изменённые файлы

- `BecquerelMonitor/AmplitudaUsbReader.cs`
- `BecquerelMonitor/AmplitudaUsbDeviceController.cs` (только `DescribeOpenError`)
- `BecquerelMonitor/Properties/Resources.resx`, `Resources.ru.resx`, `Resources.Designer.cs` (ключ `ERRAmplitudaUsbBlockBusy`)
- `tools/AmplitudaUsbTests/LeaseTests.cs`
- `tools/effmaker/probes/UsbExplicitSerialBusyProbeP285.cs` (новый)
- этот журнал

Временное полосы (`D:\BqMoni_Claude\p285\`, `bin\Debug_p285`, `obj\p285`,
`tools\effmaker\probes\build_p285`) снято после приёмки.
