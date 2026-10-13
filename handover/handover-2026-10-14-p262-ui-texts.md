# П262 (14.10.2026): тексты интерфейса по проверке экраном П259

Полоса закрывает пункты 9–14 журнала `handover-2026-10-14-p259-ui-sweep.md` (правки ресурсов,
вынесенные из полосы П259, потому что `*.resx` были вне её участка). Решения Amber не требовалось:
только тексты, поведение и код не тронуты.

## Что сделано

| # | файл | ключ | было → стало |
|---|---|---|---|
| 1 | `MainForm.resx` / `MainForm.ru.resx` | `NormalizeSpectrumStripMenuItem.Text` | «Normalize spectrum with ROI» → «Normalize spectrum by efficiency»; «Нормализовать спектр по ROI» → «Нормализовать спектр по эффективности» |
| 2 | `DCFWHMCalibrationView.resx` | `label1.Text` | «Determine FWHM calibration peaks by selecting them into ROI» → «Select peaks for calibration...» (русская пара уже верна, не тронута) |
| 3 | `Utils/FWHMCalibrationGraph.ru.resx` (новый) + `BecquerelMonitor.csproj` | `$this.Text` | «График калибровки ПШПВ»; запись `EmbeddedResource` + `DependentUpon` один в один как у `CalibrationGraph.ru.resx` |
| 4 | `GlobalConfigForm.resx` | `textBox1.Text` | «manupulating» → «manipulating», «environement» → «environment» |
| 5 | `GlobalConfigForm.ru.resx` | `groupBox7.Text`, `label21.Text` | «Ед.. инкремента калиб. энергии» → «Ед. инкремента калиб. энергии»; «1й коэффициент» → «1-й коэфф.» |
| 6 | `DeviceConfigForm.ru.resx` | `label5.Text` | «Список конфигураций устройств» → «Конфигурации» |
| 7 | `DocEnergySpectrum.resx` / `.ru.resx` | `closeCToolStripMenuItem.Text` | «Close(&C)» → «Close (&C)»; «Закрыть(&C)» → «Закрыть (&C)» (менялся только пробел) |
| 8 | `Properties/Resources.resx`, `Resources.Designer.cs` | `Uncertain` | «Uncertain» → «Uncertainty»; комментарий свойства в `Designer.cs` в тон |

## Проверенное по ходу

* Читатели ключа `Uncertain` во всём дереве (`*.cs`, `*.resx`, `*.xml`, `*.py`, `*.ps1`): единственный —
  `DCResultView.cs:222` и `:225` (заголовок столбца «Uncertain 2σ»). Другого читателя, где прежнее
  слово было бы уместно, нет; `DetectedWithUncertain` — другой ключ, не тронут.
* У `FWHMCalibrationGraph` есть нейтральный `.resx` с `$this.Text` = «FWHM calibration chart» и `Localizable=True`;
  `.ru.resx` у формы раньше не было, поэтому в русском интерфейсе заголовок оставался английским.
* В новый `FWHMCalibrationGraph.ru.resx` по образцу `CalibrationGraph.ru.resx` вошли три ключа:
  `$this.Text` и оба пункта контекстного меню («Сохранить новые точки калибровки», «Отменить изменения»):
  их английские значения в обеих формах одинаковы, а без них меню парной формы было бы русским, а этой —
  английским.
* Фактический текст пункта 7 был «Close(&C)» без пробела (как и писал журнал П259), не «Close (&C)».

## Приёмка

* Правки байтами, BOM и CRLF сохранены (`.gitattributes`: `*.resx eol=crlf`). Новый файл — копия образца
  с одной заменой, BOM один.
* `tools/check_resx.py`, `check_resx_letters.py`, `check_resx_zorder.py`, `check_resx_designer.py` — код 0.
* Сборка Debug в свой `bin\Debug_p262\` (`/p:GenerateManifests=false`) — код 0. В
  `ru\BecquerelMonitor.resources.dll` есть `BecquerelMonitor.Utils.FWHMCalibrationGraph.ru.resources`,
  `$this.Text` читается как «График калибровки ПШПВ». Каталоги `bin\Debug_p262`, `obj\p262` сняты.
* `python tools/check_all.py`: 42 из 45 зелёные; красные только три «КАТАЛОГ ПРОБ ПРОТУХ» (T226) —
  `check_fsa_report_view`, `check_corpus_scenes`, `check_fsa_showcase` (набор исходников приложения разошёлся
  с заверенным 13.10.2026 в 22:23 каталогом проб: 28 файлов, в списке первыми идут `amplituda*`, к которым
  полоса не прикасалась; пересборка каталога проб — дело распорядителя, не этой полосы).

## Остаток (не правилось: вне рецепта)

Русская пара `Uncertain` в `Properties/Resources.ru.resx` — «Неопред.», то есть русский заголовок столбца
окна результата читается «Неопред. 2σ». Это тот же дефект, что был в английском («Uncertain 2σ» вместо
«Uncertainty 2σ»): подпись, хотя столбец показывает погрешность. Рецепт П262 `Resources.ru.resx` не назвал,
поэтому не тронут; правка — одна строка (например «Погрешность»), решает распорядитель.
