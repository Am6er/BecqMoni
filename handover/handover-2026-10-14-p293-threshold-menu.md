# П293 (14.10.2026): пункты «порог от курсора» гаснут у приборов без поля порога

## Постановка

Вопрос вопросника 14.10.2026: «Пункт меню «Нижний/верхний порог» у приборов, где поля порога
нет (AmplitudaUSB, RadiaCode, Obsidian; П252 №11): открывает настройки и молча ничего не
делает. Что делать?»

Решение Amber 14.10.2026, дословно: **«Гасить пункт у таких приборов (Рекомендую)»** —
«Пункт меню недоступен, если у прибора нет поля порога. Без новых строк ресурсов.»

## Где пункт и как он работает

* Контекстное меню графика спектра `DocEnergySpectrum.contextMenuStrip1`, подменю
  «Конфигурация устройства (D)» → `setLowerThresholdLToolStripMenuItem` («Установить нижний
  порог спектра по курсору (L)») и `setUpperThresholdHToolStripMenuItem` (… верхний … (H)).
* Доступность считалась в `DocEnergySpectrum.contextMenuStrip1_Opening` — на каждом открытии
  меню — только по курсору (курсор на спектре, канал в пределах).
* Щелчок: событие `DocEnergySpectrum.SetLowerThreshold`/`SetUpperThreshold` →
  `MainForm.DocEnergySpectrum_SetLowerThreshold`/`…Upper…` (единственные подписчики) →
  `ShowDeviceConfigForm(deviceConfig)` → `DeviceConfigForm.SetLowerThreshold`/`SetUpperThreshold`:
  ищет прибор в таблице по GUID (не нашёл — `return`), открывает вкладку прибора и пишет число
  в `inputDeviceForm.LowerThresholdTextBox`/`UpperThresholdTextBox`; при `null` — ничего.

## У каких приборов поле есть — по коду

`InputDeviceForm.LowerThresholdTextBox`/`UpperThresholdTextBox` в базе возвращают `null`.

| тип (`DeviceType.Id`) | страница | поля порога для меню |
|---|---|---|
| AudioInputDevice | `AudioInputDeviceForm` | **есть** (`doubleTextBox1/2`, порог PRA) |
| AtomSpectraVCP | `AtomSpectraVCPDeviceForm` | нет — свойства переопределены, но отдают поля `doubleTextBox1/2`, объявленные `= null` и нигде не присваиваемые (остаток декомпиляции) |
| RadiaCode | `RadiaCodeDeviceForm` | нет |
| Obsidian | `ObsidianDeviceForm` | нет |
| AmplitudaUSB | `AmplitudaUsbDeviceForm` | нет для меню: свои пороги есть, но это `NumericUpDown` в кодах АЦП (0…4095), страница их как `TextBox` не отдаёт |
| AmplitudaSerial | `AmplitudaSerialDeviceForm` | нет |
| (вне реестра) `SerialInputDeviceConfig` | окно ставит пустую `InputDeviceForm` | нет |

⚠ **Список из вопроса уже реальности:** кроме AmplitudaUSB, RadiaCode, Obsidian поля нет ещё у
**AtomSpectraVCP** и **AmplitudaSerial**. Поле есть только у AudioInput. Следствие для Amber:
у её приборов (все — Atom Spectra, Obsidian, RC-101/103) пункты теперь погашены всегда; до
правки они были доступны и щелчок не делал ничего.

## Признак — свойство конфигурации прибора

`InputDeviceConfig.HasThresholdFields` (виртуальное, только чтение, `[XmlIgnore]`, умолчание
`false`); `AudioInputDeviceConfig` переопределяет → `true`. Меню:
`enabled = <курсор, как прежде> && InputDeviceConfig != null && InputDeviceConfig.HasThresholdFields`.

Почему так, а не иначе:

* **Не перечень имён типов в меню** — новый прибор сломал бы правило молча.
* **Не страница прибора (`InputDeviceForm`)**: меню знает только конфигурацию документа, а
  страница существует лишь внутри открытого окна настроек; строить `UserControl` прибора на
  каждом открытии меню — создавать элементы управления ради вопроса.
* **Не отражением «переопределено ли свойство страницы»**: AtomSpectraVCP переопределяет его и
  всё равно отдаёт `null` — такой признак соврал бы ровно на главном приборе Amber.
* **Умолчание `false`**: новый прибор без своего ответа получает ПОГАШЕННЫЙ пункт — это видно,
  а не «окно открылось, ничего не произошло». Прибор, который заведёт поля и забудет признак,
  тоже увидит погашенный пункт, а проба П293 назовёт расхождение признака со страницей.
* Признак со страницей связан контрактом в комментарии `InputDeviceConfig.HasThresholdFields`;
  согласие проверяет проба `ThresholdMenuProbeP293` (столбец «признак» против «поле порога»).

Новых строк ресурсов нет; `*.resx` не тронуты.

## Приёмка

Проба `tools/effmaker/probes/ThresholdMenuProbeP293.cs` (меню не показывается): для каждого
зарегистрированного типа — документу конфигурация этого типа, курсор в канал 512 из 1024,
`contextMenuStrip1_Opening` отражением, доступность читается с самих пунктов; «поле есть» —
от страницы прибора (`LowerThresholdTextBox`/`UpperThresholdTextBox` не `null`). Контроли:
курсор вне спектра гасит пункты у всех в обеих сборках; среди типов есть и с полем, и без.
Признак приложения читается отражением — один exe пробы для обеих сборок.

| тип | поле | ДО (код 1) | ПОСЛЕ (код 0) |
|---|---|---|---|
| AudioInputDevice | есть | доступен | доступен |
| AtomSpectraVCP | нет | **доступен** (дефект) | погашен |
| RadiaCode | нет | **доступен** | погашен |
| Obsidian | нет | **доступен** | погашен |
| AmplitudaUSB | нет | **доступен** | погашен |
| AmplitudaSerial | нет | **доступен** | погашен |
| `SerialInputDeviceConfig` (вне реестра) | нет | **доступен** | погашен |
| `InputDeviceConfig = null` | нет | **доступен** | погашен (без исключения) |
| курсор вне спектра (все типы) | — | погашен | погашен |

Обе сборки — `bin\Debug_p293` (до правки, затем `/t:Rebuild` после), пробы `build_p293` через
`build_all.ps1`: до — код 0; после — первый раз код 1 (полоса П292 правила
`ResponseMatrixForm.cs`/`Resources.designer.cs` между сборкой и сверкой свежести, все 327 проб
собрались), повтор — код 0, каталог заверен (T226).

**Документ «без прибора»** (решает код как есть, правкой не менялось):

* свежий документ (`new DocEnergySpectrum`) получает первый прибор списка (в поставочном
  каталоге проб — RadiaCode) — пункт погашен по правилу его типа;
* спектр, чей прибор не найден в списке (`DocumentManager.PrepareDeviceConfig` не нашёл GUID),
  остаётся с `DeviceConfig` по умолчанию: `new DeviceConfigInfo()` → `AudioInputDeviceConfig`,
  GUID пустой. Признак — `true`, пункт **доступен** (так же, как до правки); щелчок откроет
  настройки, `DeviceConfigForm.SetLowerThreshold` прибора по GUID не найдёт и выйдет молча.
  Это тот же симптом, но другая причина (прибора нет вовсе), и решение 14.10.2026 его не
  покрывает — передано распорядителю.

**Экран** (по рецепту: Release-сборка в `D:\BqMoni_Claude\p293\app`, `config\` — копия
`%AppData%\BecqMoni\config`, `windows-mcp`, замок `screen.lock`): документ «Чароит в домике»,
прибор «1.Atom Spectra Nano 16 Pro RadiaScan 701A», курсор на канале 4917 — правый щелчок,
«Конфигурация устройства (D)» → оба пункта порога серые; «Сохранить (S)» серый, остальные
доступны. Набор не начинался. Снимок `shots\atom_menu_after.png` — на диске
`D:\BqMoni_Claude\p293\` (снят вместе с каталогом после приёмки), не в git. Конфиг Amber после
сверен: 43 файла, размеры и времена записи совпали. Прибора AudioInput в её конфиге нет —
положительный контроль «доступен» дан пробой.

`tools/check_menu_accelerators.py` — СОШЛОСЬ (столкновений 0). `python tools/check_all.py` —
код 1: красные 4 из 46 (`check_fsa_report_view`, `check_corpus_scenes`, `check_fsa_showcase`,
`check_bq_summing`), все — T226 «каталог проб протух»: канонический `tools\effmaker\probes\build`
заверен в 11:36 из `Debug_Codex`, а исходники изменены (три файла П293 и два — П292).
Пересобирает распорядитель.

## Остатки (строк реестра полоса не заводит — `TODO.md` не её файл)

1. Спектр с прибором, которого нет в списке, — пункт доступен и молча ничего не делает (см.
   выше). Лечение в той же точке — гасить и при GUID вне `DeviceConfigManager.DeviceConfigList` —
   требует слова Amber: решение 14.10.2026 говорило о приборах без поля.
2. `AtomSpectraVCPDeviceForm.UpperThresholdTextBox`/`LowerThresholdTextBox` — мёртвые
   переопределения, отдающие никогда не присвоенные `null`-поля. Безвредны (признак от них не
   зависит); снять — косметика.
3. AmplitudaUSB: пороги у прибора есть (коды АЦП), но меню их не заполняет — другие единицы
   (меню пишет `канал × ChannelPitch`) и другой вид поля. Сделать меню рабочим для него — новая
   возможность, не решалась.

## Файлы

* `BecquerelMonitor/InputDeviceConfig.cs` — признак `HasThresholdFields` (умолчание `false`).
* `BecquerelMonitor/AudioInputDeviceConfig.cs` — `HasThresholdFields => true`.
* `BecquerelMonitor/DocEnergySpectrum.cs` — `contextMenuStrip1_Opening`: условие признака.
* `tools/effmaker/probes/ThresholdMenuProbeP293.cs` — проба.
* этот журнал.
