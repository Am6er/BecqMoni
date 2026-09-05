# Полоса B15, 05.09.2026: A145 этап 3 — окно отчёта `FSAReportView`, ручная таблица с графика снята

Ветка `pie`, основное дерево `C:\Users\moroz\source\repos\BQ Eng res .NET 4.8` (HEAD `ed398e09` + незакоммиченные
этапы 1–2 + правки этой полосы). Свои каталоги: приложение `BecquerelMonitor\bin\Debug_B15` (`obj\B15`), пробы
`tools\effmaker\probes\build_b15` (108 проб, `build_all.ps1` код 0), оснастка `tools\CORPUS\scripts\wd_b15`
(`mk_appwd.ps1`, сторож T63: 273 файла по sha256 сошлись, склад матриц `mini16`). Коммитов нет (распоряжение Amber).
Трактовки — по `handover/a145-fsa-display-groups.md`; журналы этапов: `handover-2026-09-05-b4-a145-stage1.md`,
`handover-2026-09-05-b8-a145-stage2.md`.

## Что сделано

### 1. `FSAReportView` (новые `FSAReportView.cs`, `.Designer.cs`, `.resx`, `.ru.resx`; строки в `.csproj`)

DockPanel-вид по образцу соседних `DC*View` (`ToolWindow`, `HideOnClose = true`, `ApplyResources`, шрифт `$this.Font`,
свойства XPTable с enum и шрифт заголовка — кодом, в `.resx` XPTable только в метаданных `>>`). Сверху вниз четыре
группы `Dock=Top`+`AutoSize` (источник состава — две радиокнопки; группировка — две радиокнопки; модель цепочек —
«Равновесие ряда»; пять флажков дополнительных компонентов в вертикальном `FlowLayoutPanel` без переноса в столбцы)
и `XPTable` на весь остаток (`EnableWordWrap`, колонки образец/компонент/значение, колонка «Компонент» получает всю
свободную ширину при `Resize`). Строки — из `FsaPresentationBuilder.Build(session.Result, EffectiveGrouping,
session.ResponseMatrixOldFormat).Rows`, ТЕМ ЖЕ вызовом, что у графика; род строки читается из `FsaReportRow.Kind`,
образец — `FsaPalette.SumPeakHatchColor` (перенесён из отрисовки, одно правило на ленту и образец).

Состояния (по документу): нет спектра — одна строка «Спектр не выбран» и все элементы выключены; результата нет —
одна строка (`FSACalculating` при счёте, иначе `session.Status`, то есть слова отказа `A95` с цепочкой причины);
пересчёт при старом результате — оранжевая первая строка «Пересчёт… строки ниже — от прежнего расчёта» (`A32`).
Состояние «пересчёт завершился ошибкой при старом результате» по устройству сеанса не бывает (отказ публикуется с
пустым результатом) — строка реестра ниже.

Окно — потребитель: пока видимо (`Visible && !IsHidden`), зовёт `EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null)`
— тот же довод, что у графика, иначе два потребителя заказывали бы два отпечатка. Расчётные переключатели идут одним
путём `ApplyCalculationChange`: копия спектра → умолчание прибора (`SaveOptionsToDevice`, `SaveConfigQuiet`, семь
полей) → `session.Invalidate()` → заказ потребителями (окно; график через `RefreshView`, если он в `ShowFSA`).
Группировка — только `document.FsaGrouping` (одно значение на документ) и перестройка таблицы; просьба родителей
помнится (`RequestedGrouping`) и восстанавливается, когда снова допустима. Подсказки — по Properties.Resources:
«меняет расчёт» / «только группировка» / причины недоступности / при матрице — «управляет только отдельной
дополнительной компонентой».

### 2. График, документ, главная форма, панель пиков

* `EnergySpectrumView.Fsa.cs` (1086 → 822 строк): сняты `DrawFsaOwnTable`, `DrawFsaRows`, `DrawFsaSwatch`,
  `DrawFsaQualityRow`, `FsaTableRowCount`, `FsaCollapsibleBudget`, `FsaTableBudget`, `FsaQualityMarksWidth/Format`,
  `FsaSumPeakHatchColor` (→ `FsaPalette`), бюджет высоты и усечение. Оставлены имена, которые зовёт чужой
  `EnergySpectrumView.cs`: `ShowFsaTable` (теперь рисует только короткую строку состояния `DrawFsaStatus`:
  «считается / невозможно / ошибка»; при живом результате и идущем пересчёте — оранжевое «считается»),
  `ResetCursorPanelBounds`, `RegisterCursorPanel`. Уведомление о старой матрице из `Completed` вида убрано.
* `DocEnergySpectrum.cs`: событие `FsaModeEntered(Explicit)` — явная команда меню (`true`) и циклическая кнопка
  (`false`); свойство `FsaGrouping` (↔ `view.FsaGrouping`); подписка на `Completed` своего сеанса — предупреждение о
  матрице прежнего формата (`A50`) показывает ДОКУМЕНТ вне отрисовки и независимо от видимости отчёта;
  `FsaModeEnteredEventArgs`.
* `MainForm`: поле `dcFsaReportView`, создание в `InitializeToolViews`, восстановление из persist-string,
  `SetDocument` в обеих ветках `dockPanel1_ActiveDocumentChanged`, `ActiveResultDataChanged`, подписка на
  `FsaModeEntered` в `Subscribe/UnsubscribeDocumentEvent`, `ShowFsaReportView(bool activate)` (без активации —
  фокус возвращается документу), пункт меню «Вид → &FSA Report / От&чёт FSA» (`fsaReportStripMenuItem`,
  `check_menu_accelerators.py` — столкновений 0 в обеих культурах).
* `DCPeakDetectionView.*`: два флажка FSA, обработчики `ApplyFsaFlag`/`SaveFsaFlagsToDevice`, `fsaToolTip` и их
  ресурсы сняты; ZOrder метаданных перенумерован (`check_resx_zorder.py`: расхождений 0). Ресурс `FSARowsDidNotFit`
  снят из трёх файлов `Properties/Resources.*`.
* `FsaAnalysisSession.RunCount` — счётчик запусков (читатель — приёмка критериев 4 и 7).

### 3. Пробы

* `FsaReportViewProbe.cs` (новая): 11 разделов, 157 проверок, положительные контроли; документы собираются из
  файлов корпуса `new DocEnergySpectrum(path)` + `ResultDataFile`, окно живёт в форме-носителе; снимки
  `handover/b15-report-en.png`, `b15-report-ru.png`.
* `FsaFlagsProbe`: раздел `S77` переписан под окно отчёта (панель пиков проверяется на ОТСУТСТВИЕ галок).
* `FsaPaletteProbe`: раздел «потолок таблицы» (`S73`) снят вместе с правилом.
* `FsaStackShot`: вместо `DrawFsaOwnTable` — `DrawFsaStatus`; справа к стеку приклеивается снимок настоящего
  `FSAReportView` на том же сеансе (радиокнопка источника — по `--infer`, отпечаток подложен, чтобы окно не
  заказало свой счёт).

## Приёмка — 13 критериев (все артефакты в `handover/b15-*`)

| # | чем доказано | число | положительный контроль |
|---|---|---|---|
| 1 | `FsaReportViewProbe` §1 (отражение): 9 методов таблицы отсутствуют, типа `FsaOverlay` в сборке нет, ресурса и свойства `FSARowsDidNotFit` нет; grep по дереву: `FsaOverlay.cs` нет ни в дереве, ни в `.csproj`, `DrawFsaOwnTable/DrawFsaRows/FsaTableBudget/FsaQualityMarks` — 0 в коде (только имена в самой пробе и одна историческая ссылка в комментарии построителя) | 16/16 | `DrawFsaStatus`, `ShowFsaTable`, `FsaAnalysisSession`, `FSACalculating` найдены |
| 2 | §2: persist-string `BecquerelMonitor.FSAReportView`, `MainForm.GetContentFromPersistString` отдаёт `FSAReportView` и второй раз ТО ЖЕ окно; `Visible=false/true` — строки на месте (18); `SetDocument(A)`/`(B)` — `Presentation.Source` ReferenceEquals результату сеанса ИМЕННО того документа; `SetDocument(null)` — одна строка «Спектр не выбран», элементы выключены | 12/12 | чужая persist-строка окна не даёт; результаты A и B — разные объекты |
| 3 | §3 на `G1S16_Co60_P5` (кривая есть): `ShowFsaToolStripMenuItem_Click` → режим `ShowFSA`, событие одно, `Explicit=true`; цикл кнопки из `ShowContinuum` → `ShowFSA`, `Explicit=false`; на настоящем `MainForm` с подпиской окно создано и видно в `dockPanel1`; крестик вкладки (`DockPane.CloseActiveContent`) → `IsHidden=true, IsDisposed=false`, режим графика остался `ShowFSA`, результат сеанса тот же объект; выход графика из `ShowFSA` отчёт не прячет | 14/14 | выход из `ShowFSA` события не поднимает (0), режим уже не FSA |
| 4 | §4: окно (`ProbeConsumer`) + график (`RefreshView` в `ShowFSA`) — `RunCount` = **1**; `Presentation.Source` окна и `GetFsaPresentation(result).Source` графика — один объект `FsaResult`; отпечаток сеанса = `BuildStamp` активного спектра | 6/6 | `Invalidate()` + `Consume` → запусков 2 (счётчик живой) |
| 5 | §5: на настоящих спектрах роды `Layer, SumPeaks, Undetected, Residual, Quality`; собранная из настоящего результата сцена (без фона, свёрнутый кандидат `Rn-220` 0.114 %, сумм-пики) даёт все **семь** родов; окно 320×160: строк модели 29 = строк в XPTable 29 | 9/9 | видимых без прокрутки 0 < 29 — прокрутка, а не потеря |
| 6 | §6 в `ru-RU` и `en-US`: текст ячейки XPTable = `FsaPresentationBuilder.QualityText`: «χ²/ndf · подавлен · старая матрица · суммирование (без кривой) !» / «χ²/ndf · suppressed · old matrix · summing (no eff) !», все пять пометок, многоточия нет, `WordWrap` у ячейки, `2,94`/`2.94` в колонке значения; `FsaQualityRowProbe` код 0 | 26/26 | урезанный многоточием текст сторож отвергает |
| 7 | §7 на `ASN16_Th232` NucBase+равновесие: родители → отпечаток тот же, `RunCount` тот же (4), событий 0, `doc.FsaGrouping=Parents`, строк меньше; дочерние — обратно (26 строк); флажок «Атомный рентген» настоящим `CheckBox`: конфигурация спектра и умолчание прибора записаны, отпечаток другой (`-xray`), запуск **ровно один** (5), событие **одно**; серия из двух флажков подряд — одна публикация, отпечаток последнего | 22/22 | обратное переключение — ещё один запуск (6), отпечаток исходный |
| 8 | §8: по пикам — родители и равновесие недоступны, подсказки `FSAReportTipParentsNeedNucBase` / `FSAReportTipEquilibriumNeedsNucBase`, просьба «родители» помнится, к графику уходят дочерние; NucBase+равновесие — родители доступны и просьба восстановлена, к графику ушли родители; корней 1, \|Δкривой\|/max **0.0E+0**, \|Δдоли\| **0.0E+0**; равновесие выкл → недоступны, помнится; `G1S16_Co60_P5` (нет ряда) — недоступны, подсказка «…: в составе нет ряда распада»; `FsaSessionProbe` §4 — код 0 | 26/26 | — (тождество на машинном нуле; контроль неполной суммы — в `FsaSessionProbe`) |
| 9 | `FsaDoubleCountProbe` (`G1S16_Th228_P5 --chain=Th-232`) код 0, `FsaFlagsProbe` код 0 (фасад → 4 ключа, `BackscatterWithMatrix` опущен, `EscapeGate` не тронут, у фасада нет опасных членов) | коды 0 | ловушки A168/A170 внутри проб срабатывают |
| 10 | `FsaSessionProbe` (`ASN16_Th232`, контроль `G1S16_Co60_P5`) код 0: серия из 8 снимков — публикация одна, отпечаток последнего; смена спектра посреди счёта — результат B, поколение отвергает A | код 0 | (б)/(г) отвергают, (в) публикует |
| 11 | §11: флаг «Каскадное суммирование» в A → конфигурация A записана, `Stamp` копии B не изменился (`peaks\|eq\|xray\|sum\|bs\|esc\|pu`), ссылки разные; `FsaFlagsProbe`: клон/XML/`AdoptFrom`/старый XML для пяти флажков | 4/4 + код 0 | флаг возвращён |
| 12 | §12: `b15-report-en.png`, `b15-report-ru.png` (320×640); по модели: обрезанных 0, наложений 0, чужого шрифта 0 в обеих культурах, шрифт заголовка XPTable = шрифт формы, таблице 304×329; `.resx`: XPTable только в `>>Type` (6 записей), enum XPTable не сериализован; `check_resx_zorder.py --form FSAReportView` — 9 контейнеров, расхождений 0 | 12/12 | подброшенное усечение (`AutoSize=false, Width=40`) — 1 обрезанная; подброшенный `Courier New` — 1 чужой шрифт |
| 13 | `run_mini.ps1` (`b15-run_mini-after.txt` против `b8-run_mini-after.txt`): known 42 — **556.3 / 5.92 / 100 % / 0**, подавлен 1; unknown 17 — **205.3 / 4.95 / 96 % / 0**, подавлен 1; невязка модели 29.4 % / 32.8 %; построчный diff — только пути и время | Δ Σχ² = **0.0** | — |

Регресс (все на финальной сборке 01:15, оснастка `wd_b15`): `FsaSessionProbe` 0, `FsaQualityRowProbe` 0,
`FsaRobustnessProbe` 0, `FsaPaletteProbe` 0, `FsaFlagsProbe` 0, `FsaStampProbe` 0, `FsaDoubleCountProbe` 0, `ChainProbe`
0, `EscapeGateProbe` 0, `RefusalWordsProbe --arm=fsa` 0 («ПРИЧИНА НАЗВАНА»), `FsaStackShot` ×3 0
(`b15-stackshot-th228-peaks.png`: те же 14 строк, что у B8 — 5 слоёв, 4 сумм-пика, 3 серых предела, невязка
`+4,9 / −8,7 %`, качество `χ²/ndf · матрица · суммирование 3,32`; `--infer --calculating`: на графике оранжевое
«считается…», в окне оранжевая строка «Пересчёт…» над 6 строками), `FsaReportViewProbe` 0 (157/157).

Сверки ресурсов, коды ДО (снимок HEAD `ed398e09` через `git archive`) → ПОСЛЕ: `check_resx.py` 0 → **1**,
`check_resx_letters.py` 0 → **1**, `check_resx_designer.py --no-format` 1 → 1, `check_menu_accelerators.py` 0 → 0,
`check_resx_zorder.py` 0 → **1**. Все три единицы «после» — ЧУЖИЕ и не про мои файлы: плечо формата ловит
незакоммиченные `EfficiencyMakerForm.resx` и `EfficiencyMakerForm.ru.resx` (LF вместо CRLF; у zorder — тот же
`EfficiencyMakerForm.resx`); designer — чужие `DoseRate.cs:292,309` (`DoseRateEnergyNotPositive`, `DoseRateNoElement`
без ключей), та же единица и в HEAD. Содержательные плечи: непереведённых 0, находок письма 0, расхождений
zorder 0 (контейнеров 87), столкновений ускорителей 0. ⚠ Третьим файлом чужого формата сначала была моя
`DCPeakDetectionView.ru.resx` — в рабочей копии у её хвоста (13 строк, `textColumn4.Text` и далее) стояли голые LF ещё
до моей правки; приведена к чистому CRLF.

## Что найдено и НЕ сделано

* **Состояние «пересчёт завершился ошибкой при старом результате → старые строки + красная строка ошибки»** (таблица
  состояний документа) не реализовано: `FsaAnalysisSession.Finish` публикует отказ с `result = null` («на каждом пути
  отказа оно становится null»), и старых строк к тому моменту нет ни у графика, ни у окна. Менять семантику сеанса
  (держать старый результат под чужим отпечатком) — решение Amber; окно показывает одну строку ошибки.
* Подсказка причины недопустимости родителей (`ParentGroupingRefusal`) — служебная строка, не переводится (строка
  реестра B4b про enum остаётся).
* `DocEnergySpectrum` в пробе поднимает `ROIConfigManager`, а `wd_*` каталога `config\ROI` не несёт — в потоке ошибок
  строка «Не удалось загрузить конфигурационный файл ROI»; на приёмку не влияет, но каждая проба с документом будет её
  печатать.
* `tools/effmaker/probes/README.md` (чужой) описывает `FsaQualityRowProbe`/`FsaStackShot` по-старому — не правил.
* Раскладка по умолчанию `config/layout/ExpertMode.xml` окно не содержит (как и `DCFwhmCalibrationView`) — окно
  открывается из меню «Вид» или входом в `ShowFSA` и запоминается в раскладке пользователя.
* Пять флажков на корпусном пути (`CorpusFsaProbe`) сеанс и окно не трогают — Δ = 0 по построению.

## Тронутые файлы (для `git add`)

Новые: `BecquerelMonitor/FSAReportView.cs`, `FSAReportView.Designer.cs`, `FSAReportView.resx`, `FSAReportView.ru.resx`;
`tools/effmaker/probes/FsaReportViewProbe.cs`; журнал `handover/handover-2026-09-05-b15-a145-stage3.md`; артефакты
`handover/b15-*`. Изменённые: `BecquerelMonitor/BecquerelMonitor.csproj`, `DCPeakDetectionView.cs`,
`DCPeakDetectionView.Designer.cs`, `DCPeakDetectionView.resx`, `DCPeakDetectionView.ru.resx`, `DocEnergySpectrum.cs`,
`EnergySpectrumView.Fsa.cs`, `MainForm.cs`, `MainForm.Designer.cs`, `MainForm.resx`, `MainForm.ru.resx`,
`Properties/Resources.resx`, `Properties/Resources.ru.resx`, `Properties/Resources.Designer.cs` (⚠ в трёх последних
лежат и чужие незакоммиченные ключи `Activity*Refused`), `FullSpectrumAnalysis/FsaAnalysisSession.cs`,
`FullSpectrumAnalysis/FsaPalette.cs`; `tools/effmaker/probes/FsaFlagsProbe.cs`, `FsaPaletteProbe.cs`, `FsaStackShot.cs`.
Вместе с этапом 2 (тот же `git add`): `FsaPresentation.cs`, `FsaPresentationBuilder.cs`, `FsaSessionProbe.cs`,
`FsaQualityRowProbe.cs`, `FsaStampProbe.cs`, `RefusalWordsProbe.cs`, удаление `FsaOverlay.cs`.

Новые ключи ресурсов: `FSAReportView.resx/.ru.resx` — `sourceGroupBox`, `sourcePeaksRadio`, `sourceNucBaseRadio`,
`groupingGroupBox`, `parentsRadio`, `daughtersRadio`, `chainGroupBox`, `equilibriumCheckBox`, `extrasGroupBox`,
`atomicXrayCheckBox`, `cascadeSummingCheckBox`, `backscatterCheckBox`, `escapeCheckBox`, `pileUpCheckBox` (`.Text`),
`componentColumn.Text`, `valueColumn.Text`, `$this.Text`, `$this.TabText`; `MainForm.resx/.ru.resx` —
`fsaReportStripMenuItem.Text`; `Properties/Resources.*` — `FSAReportNoSpectrum`, `FSAReportRecalculating`,
`FSAReportTipCalculation`, `FSAReportTipGrouping`, `FSAReportTipParentsNeedNucBase`, `FSAReportTipParentsRefused`,
`FSAReportTipEquilibriumNeedsNucBase`, `FSAReportTipMatrixExtra`; снят `FSARowsDidNotFit`.
