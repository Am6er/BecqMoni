# П104 — 18.09.2026: `AMBER45` — комбо «Matrix layer» на вкладке FSA Report: стопка ОДНОГО слоя матрицы отклика

Строка реестра: `AMBER45` (задача Amber, закрывает Amber). Разбор постановки —
[handover-2026-09-15-amber45-channel-view.md](handover-2026-09-15-amber45-channel-view.md).
Стенд полосы: worktree `D:\BqMoni_Claude\p104\wt` на `e1babbdc` (master), сборка `bin\Debug_p104`,
пробы `tools\effmaker\probes\build_p104`; снимки экрана — `D:\BqMoni_Claude\p104\shots\` (на диске, в git НЕ идут);
текстовые артефакты — `handover/p104-amber45/`.

**Задача Amber 15.09.2026, дословно:** «На вкладке FSA Report добавить режим отображения выбранного канала матрицы
отклика. Это отдельный режим отображения на ряду с существующим. То есть работает принцип либо существующий - либо
новый. На этом пока всё. Задача требует отдельных уточнений при реализации. Сейчас главное - зафиксировать её.»

**Описание вида, Amber 18.09.2026, дословно:** «В FSA Report в группе Display добавить combo box: "Matrix layer".
Значение по умолчанию - All (отображать как это выглядит сейчас). И доступные значения в этом комбо боксе - каждый
слой из существующих. При его выборе происходит его отрисовка на спектре.»

**Уточнения Amber 18.09.2026, вопросником, дословно:** вид слоя — «Стопка по компонентам, как сейчас»; части модели
без каналов — «Спрятать — только канал и спектр»; лента невязки — «Как есть, по своей галке»; витрина — «Да, два
эталона на Cs-137 в домике».

## 1. Что сделано

### 1.1 Окно отчёта `FSAReportView` — группа «Display»

* Подпись `matrixLayerLabel` «Matrix layer» / «Слой матрицы» и комбо `matrixLayerComboBox` (DropDownList) — после
  галки «Model residual», в потоке `displayFlow` группы `displayGroupBox` (метаданные `>>…` в `FSAReportView.resx`,
  ZOrder 4 и 5; `check_resx_zorder` — 0).
* Пункты: **All** и шесть каналов `EfficiencySimulator.ResponseChannel` по номеру. Подписи EN / RU (собственные
  ресурсы окна `FSAReport_MatrixLayer*`, по образцу `FSAReport_*`):

  | канал | EN | RU |
  |---|---|---|
  | All | All | Все |
  | Peak | Full-energy peak | Пик полного поглощения |
  | Compton | Compton (partial deposit) | Комптон (неполное поглощение) |
  | EscapeAnnihilation | Single escape (511 keV) | Одиночный вылет аннигиляции (511 кэВ) |
  | EscapeXrayK | K X-ray escape | Вылет K-рентгена кристалла |
  | EscapeAnnihilationDouble | Double escape (1022 keV) | Двойной вылет аннигиляции (1022 кэВ) |
  | EscapeXrayL | L X-ray escape | Вылет L-рентгена кристалла |

  Второй канал подписан «Compton (partial deposit)» нарочно: в него разбор кладёт всякую историю с утечкой
  (рассеянный квант, электрон, тормозное) — см. `FsaChannelShot.cs`, «второй канал — не «комптон»».
* Подсказки (EN / RU): при живой матрице — «Draws only this response-matrix channel of every component; nothing is
  recomputed, the table is unchanged.» / «Рисует у каждого компонента только этот канал матрицы отклика; ничего не
  пересчитывается, таблица прежняя.»; **погашенное** комбо — «No response matrix for this spectrum: the model has no
  channels to show, the whole stack is drawn.» / «У спектра нет матрицы отклика: каналов у модели нет, рисуется вся
  стопка.» Подсказка стоит и на подписи, и на комбо (погашенное комбо подсказку WinForms не показывает — подпись
  показывает).
* Обработчик `matrixLayerComboBox_SelectedIndexChanged` — ОТРИСОВОЧНЫЙ, по образцу галки невязки: не идёт через
  `ApplyCalculationChange`, конфигурацию не пишет, `session.Invalidate()`/`Consume()`/`RefreshReport()` не зовёт —
  одна перерисовка графика через `DocEnergySpectrum.FsaMatrixLayer`. Таблица не меняется (фит не пересчитывается).
* Просьба живёт в окне (`requestedMatrixLayer`, как `requestedGrouping`) и переезжает на график показываемого
  документа (`PushMatrixLayer` в `ReadDocument`); при `Dispose` окна график возвращается к «All» (тот же довод, что у
  ленты невязки `A248`). Пробам — `RequestedMatrixLayer`, `MatrixLayerAvailable`, `MatrixLayerText(layer)`.
* Погашение: `UpdateAvailability` — комбо живо только когда `FsaMatrixLayers.HasChannels(presentation.Layers)` (хоть у
  одного слоя есть `ChannelCurves`), то есть матрица жива; иначе комбо `Enabled=false`, показывает «All», подсказка —
  причина, просьба помнится и вступает в силу на следующем спектре с матрицей (как просьба о родителях). Без
  результата (идёт расчёт, отказ) — тоже погашено.

### 1.2 Перечисление и правила — `FullSpectrumAnalysis/FsaPresentation.cs`

* `enum FsaMatrixLayer { All = -1, Peak = (int)ResponseChannel.Peak, … EscapeXrayL }` — числа членов ПРИВЯЗАНЫ к
  номерам каналов симулятора (индекс в `ChannelCurves`), а не выбраны.
* `static class FsaMatrixLayers`: `Channels` (шесть по номеру; длину сверяет проба с `ResponseChannelCount`),
  `HasChannels(layers)`, `IsDrawn(layer, mode)`, `CurveOf(layer, mode)` (при канале — `ChannelCurves[канал]`, null —
  канала у слоя нет, рисуется пусто), `Drawn(layers, mode)`. Одни правила на график, окно и пробы.

### 1.3 Документ и вид

* `DocEnergySpectrum.FsaMatrixLayer` — одно значение на документ, как `FsaGrouping`; пишет окно, читает график.
* `EnergySpectrumView.Fsa.cs`: свойство `FsaMatrixLayer` (смена сбрасывает ТОЛЬКО кадровые массивы стопки —
  `fsaCumulative = null` — и перерисовывает; снимок представления, цвета и строки не перестраиваются);
  `BuildFsaFrameData` при канале собирает стопку из `FsaMatrixLayers.Drawn(layers, mode)` кривыми
  `ChannelCurves[канал]` — те же слои, тот же порядок, те же цвета (`ColorOf(name)`), а фон, сплайн, рассеяние,
  наложения, серое «прочее» (слои без `ChannelCurves`), разнесённая подложка `ContinuumCurve` и хвост `TailCurve`
  не рисуются; подслой сумм-пиков штрихуется только в канале `Peak` (сумм-пик — полное поглощение пары, разбор
  кладёт его в `Peak`, `FsaAnalyzer.AccumulateSumPeaks`). Новые поля кадра: `fsaFrameLayer` (в каком режиме собран
  кадр; без матрицы — `All`, что бы ни просили), `fsaStack` (нарисованные слои), `fsaStackCurves` (их кривые),
  `fsaModelTop` (верх ПОЛНОЙ модели: при «All» — та же ссылка, что верх стопки; при канале — сумма лент всех слоёв
  тем же порядком сложения, побитово тот же массив).
* `ShowFsaOverlay`: ленты — по `fsaStack`; **лента невязки и метки опор привязки — от `fsaModelTop`** (полная модель,
  решение Amber «Как есть, по своей галке»); белая линия верха стека — по верху СТОПКИ КАНАЛА (очерчивает то, что
  нарисовано). При «All» ни одна ветка не меняется: массивы те же, ссылки те же, картинка побитово прежняя (§2).

### 1.4 Оснастка

* `FsaStackShot --matrix-layer=All|Peak|Compton|EscapeAnnihilation|EscapeXrayK|EscapeAnnihilationDouble|EscapeXrayL`:
  ставит виду `fsaMatrixLayer` (то же поле, что ставит окно) и окну на снимке `RequestedMatrixLayer`; печатает
  `MATRIX_LAYER\t<просили>\t<собран>\t<слоёв>\t<имена>` и `MATRIX_LAYER_COMBO\t<enabled|disabled>\t<пункт>\t<подсказка>`;
  `--dump=` при канале выгружает НАРИСОВАННУЮ стопку (`fsaStackCurves`), при «All» — прежние столбцы, столбец в столбец.
* `FsaViewGroupsProbe`: перепись включает `ComboBox` (переключение соседним пунктом, отпечаток в обоих положениях);
  подпись `matrixLayerLabel` в мерке ширины `A127`; `--misplace=matrixLayerComboBox` — отказ поимённо.
* `tools/check_fsa_view_groups.py`: `ComboBox` + `SelectedIndexChanged` в переписи; два новых плеча самопроверки
  (комбо переложено в расчётный поток; обработчику комбо подставлен `ApplyCalculationChange` — контрол, объявленный
  расчётным при месте в группе показа).
* Новая проба `tools/effmaker/probes/FsaChannelViewProbe.cs` — §2.3.
* `tools/check_fsa_showcase.py`: `--only=` берёт и пару `ключ__режим` (снять эталон новым парам члена, не переписывая
  клеймо прежней); в шапку эталона входят `MATRIX_LAYER`/`MATRIX_LAYER_COMBO` ТОЛЬКО когда слой просили (шапки семи
  прежних пар — ключ в ключ прежние); `--selftest` — на прежней паре члена. `snapshot.ps1` — та же форма `-Only`.
* `tools/fsa_showcase/showcase.json`: у `ASN16_Cs137_house` +2 режима `infer_eq_peak`, `infer_eq_compton`
  (`--matrix-layer=Peak|Compton`); эталоны — два НОВЫХ файла `reference/ASN16_Cs137_house__infer_eq_peak.json`,
  `…__infer_eq_compton.json` (объявлены 2026-09-18 16:29, HEAD `e1babbdc`). Семь прежних эталонов не тронуты.

## 2. Приёмка (числа)

### 2.1 Существующий режим — побитово

* База: чистый HEAD `e1babbdc` собран в те же каталоги полосы, витрина 7 пар против эталона git (объявлен П101
  14:31, HEAD `9cae3b18`) — **0 расхождений** (`D:\BqMoni_Claude\p104\logs\showcase_head.log`); PNG/дампы сохранены в
  `D:\BqMoni_Claude\p104\head_ref\`.
* Сборка с правками: `check_fsa_showcase.py --probes=…build_p104` — **9 пар, 0 расхождений**
  ([check_fsa_showcase_9_pairs.log](p104-amber45/check_fsa_showcase_9_pairs.log)): семь прежних совпали с эталоном
  14:31, две новые — с эталоном 16:29.
* Область стопки PNG (левые 1600×700 px) семи прежних пар: HEAD против П104 — **побитово 7 из 7**
  ([png_stack_compare.md](p104-amber45/png_stack_compare.md)); правые 320 px (окно отчёта) изменились — там комбо.

### 2.2 Две новые пары витрины — числа полос

Полные таблицы — [pairs_bands_table.md](p104-amber45/pairs_bands_table.md). Столбцы слоёв дампа: All =
`Cs-137, Backscatter, Xray-Pb, Xray-Pb-L, pile-up`; Peak и Compton = `Cs-137, Xray-Pb, Xray-Pb-L` (Backscatter и
pile-up каналов не имеют — спрятаны). `model`, `net`, `fit`, невязка по полосам — одинаковы во всех трёх парах
(полная модель). Cs-137 по полосам, отсч. (All лента / Peak / Compton):

| полоса, кэВ | All | Peak | Compton |
|---|---|---|---|
| 0–30 | 17937084.7 | 10085442.8 | 5951838.2 |
| 30–100 | 37143974.3 | 14683267.1 | 9423943.0 |
| 100–300 | 65591592.8 | 0.0 | 59457410.1 |
| 300–700 | 113143578.5 | 60674534.2 | 51407867.1 |
| 700–1500 | 397571.9 | 382127.0 | 430.5 |

Шапки эталонов: `MATRIX_LAYER Peak Peak 3 Cs-137, Xray-Pb, Xray-Pb-L` / `MATRIX_LAYER_COMBO enabled Пик полного
поглощения …`; `MATRIX_LAYER Compton Compton 3 …` / `… Комптон (неполное поглощение) …` (культура стенда — ru).
Прогон пар: 3.5–3.9 с каждая.

### 2.3 `FsaChannelViewProbe` — тождества нарисованного (код 0)

Стенд: `tools\fsa_showcase\wd`, `--spectrum=..\spectra\ASN16_Cs137_house.xml --sample=137CS,40K --shield=82`
(матрица `ASN16_point_house`, 6 каналов; образов 5, с каналами 3; лог —
[fsa_channel_view_probe.log](p104-amber45/fsa_channel_view_probe.log)):

* 0: `FsaMatrixLayers.Channels.Length` = `ResponseChannelCount` = 6; номер каждого члена = номер канала.
* (а) «All»: 24576 накоплений (3 слоя × 8192) **побитово** равны независимому накоплению лент `Curve`; кривая стопки —
  та же ссылка, что `Curve` слоя; верх полной модели — та же ссылка, что верх стопки.
* (б) каждый из 6 каналов: стопка — 3 слоя (`All` без двух слоёв без каналов, тот же порядок), каждая лента =
  `ChannelCurves[канал]` слоя (rel 1e-9, abs 1e-6), кривая стопки — та же ссылка; слоёв без каналов в стопке нет.
  Σ стопки по спектру: Peak 88218484.987, Compton 126433457.348, EscapeAnnihilation 0, EscapeXrayK 752680.675,
  EscapeAnnihilationDouble 0, EscapeXrayL 19597.623 отсч.
* (в) Σ по каналам верхов стопок 215424220.633 = верх «All» 246897600.938 − подложка слоёв с каналами 7385731.698 −
  хвост 14827412.378 − ленты слоёв без каналов 9260236.229 — на всех 8192 каналах; верх полной модели при канале
  бит в бит равен верху «All».
* (г) без матрицы: просили Compton → кадр собран как All, стопка — все 6 слоёв; окно: комбо погашено, показан «Все»,
  подсказка-причина, просьба помнится (Compton), пунктов 7; с матрицей: комбо живо, показан «Комптон (неполное
  поглощение)», подсказка «меняет только график»; подсказки различаются.
* (д) закрашено пикселей: All 183281, Peak 42791, Compton 107964.
* Положительный контроль 1 (`ChannelCurves[Peak]` Cs-137 ×1.01, +858253.7 отсч.): (в) отказало на 742 точках,
  |Δ| до 9131.32 — ПОЙМАН. Контроль 2 (накопление слоя Cs-137 в стопке Peak ×1.01 на 889 каналах): (б) отказало
  на 1488 точках — ПОЙМАН.
* Гейт `AMBER19`: одиночка менеджера — пустышка (0 определений, 0 наборов).
* ⚠ На этой сцене подслоёв сумм-пиков нет (каскада у Cs-137 нет) — проверка «подслой только у Peak» вырождена;
  назвать это сцена с каскадом (Th-232) могла бы, но витрина Amber на цезии, а сцена Th-232 без `--shield` тут
  не нужна — пробе дан ключ, стенд с каскадом при желании: `--spectrum=..\spectra\AS80_Th232_disk.xml --chain=Th-232`.

### 2.4 Сторожа и пробы окна

* `FsaViewGroupsProbe` — 12 переключателей: 8 расчётных, 4 отрисовочных (в т.ч. `matrixLayerComboBox`,
  отпечаток не меняется), все на местах; порядок групп — показ ниже расчёта; подписи влезают (EN «Matrix layer»
  нужно 60 из 304, RU «Слой матрицы» 80 из 304); код 0. `--misplace=matrixLayerComboBox` — код 1, назван поимённо
  ([логи](p104-amber45/fsa_view_groups_probe.log), [misplace](p104-amber45/fsa_view_groups_probe_misplace.log)).
* `check_fsa_view_groups.py` — 12 переключателей, три плеча самопроверки пойманы, код 0
  ([лог](p104-amber45/check_fsa_view_groups.log)).
* `check_resx`, `check_resx_designer`, `check_resx_letters`, `check_resx_zorder`, `check_headless`, `check_fsa_docs`,
  `check_registry_refs`, `check_no_images` — 0 (в worktree). `check_all --quiet` в worktree: 36 из 41 зелёные; красные
  5 — стендовые, не полосы: четыре корпусных (`corpus_coverage`, `declared_base`, `corpus_generator`, `corpus_scenes`
  — в worktree нет артефактов склада/базы; в главном дереве их держит перенос П99) и `fsa_showcase` кодом 3
  (в worktree нет штатного `probes\build`; с `--probes=build_p104` — 0, см. выше).
* Переводы строк правленных файлов — байтами: только CRLF, голых LF/CR нет, BOM сохранён где был.

### 2.4а Главное дерево после переноса (HEAD `42291677`, 17:07)

Перенесено 25 файлов копией, sha256 против worktree — 0 расхождений (мои файлы в главном дереве между
`e1babbdc` и `42291677` не менялись — менялись только семь прежних эталонов витрины, переобъявленные П99/П101
15:12). `/t:Rebuild` в `bin\Debug_Codex` — код 0, в exe есть `FsaMatrixLayers` и `matrixLayerComboBox`;
`build_all.ps1 -Bin BecquerelMonitor\bin\Debug_Codex` — 195 проб (с `FsaChannelViewProbe`), sha exe приложения =
exe у проб (`C2D36F515E5C0591`). Сторожа: `check_resx`, `check_resx_designer`, `check_resx_letters`,
`check_resx_zorder`, `check_fsa_view_groups`, `check_headless`, `check_fsa_docs`, `check_no_images`,
`check_probe_tuning`, `check_dump_readers`, `check_probe_numbers` — все 0. Витрина штатным каталогом —
**9 пар, 0 расхождений, 51 с** (семь прежних — с эталоном 15:12, две новые — 16:29; лог
`D:\BqMoni_Claude\p104\logs\showcase_main.log`). `FsaViewGroupsProbe` — 0, `--misplace=matrixLayerComboBox` — 1
поимённо; `FsaChannelViewProbe` из `tools\fsa_showcase\wd` — 0, оба контроля пойманы (те же числа, что в §2.3).
`check_all --quiet` в главном дереве — **40 из 41 зелёные, 156 с**; единственный красный — `check_registry` (код 1,
4 находки N8): реестр ссылается на журналы, которых нет в git — `handover/handover-2026-09-15-amber43-layout-file.md`,
`…amber44-electron-return.md`, `…amber45-channel-view.md`, `handover/amber43/crashlog-2026-09-15.txt` — незакоммиченные
файлы распорядителя (`??` в `git status` до полосы), не полосы. Новых красных полоса не внесла
(лог `D:\BqMoni_Claude\p104\logs\check_all_main.log`).

### 2.5 Экран (песочница, снимки — `D:\BqMoni_Claude\p104\shots\`, в git не идут)

Песочница как у П95: `D:\BqMoni_Claude\p104\app\` = `bin\Debug_p104` целиком (sha256 exe `8D17B8AB…`) + КОПИЯ
конфига Amber `C:\Users\moroz\OneDrive\Desktop\Debug\config\` (её каталог — только чтение; `%AppData%` портативная
сборка не трогает) + для кривой «Точка в защите» (`c482e3bc-…`) положена матрица витрины `ASN16_point_house.rmx`
(sha `cf80a1be…`), чтобы числа совпали с витриной. ⚠ Собственная матрица Amber этого guid (sha `ed7f4763…`) — тоже
формата 9, замена ради формата не требовалась. Спектр — копия `YandexDisk\Спектры\!ASN16\Cs 137 в домике
24.11.2022.xml` (`D:\BqMoni_Claude\p104\Cs137_house.xml`); раскладка Amber подняла и её вкладки сеанса.

| снимок | что на нём |
|---|---|
| `shot_01_all_showfsa.png` | Cs137_house, «Show FSA», комбо «Matrix layer» = All в группе Display под галкой Model residual; стопка как прежде (Cs-137 94.87 %, χ²/ndf 12.10, невязка +2.2/−3.0 %, Response matrix used) |
| `shot_02_combo_open.png`, `crop_02_combo.png` | раскрытый список: All, Full-energy peak, Compton (partial deposit), Single escape (511 keV), K X-ray escape, Double escape (1022 keV), L X-ray escape |
| `shot_03_peak_layer.png` | слой «Full-energy peak»: только пик 662 кэВ Cs-137, пик Ba K-рентгена (32 кэВ) и Pb K-рентген (75–85 кэВ) оранжевым; спектр (зелёная линия) над пустой стопкой; лента невязки и якоря опор — от полной модели (якорь над 662 стоит там же, что при All); таблица не изменилась |
| `shot_04_compton_layer.png` | слой «Compton (partial deposit)»: плато комптона Cs-137 до края ≈477 кэВ, пика 662 нет; якорь опоры остался над 662 (полная модель) |
| `shot_05_lu176_request_carried.png` | переход на вкладку Lu-176 (матрица есть): просьба «Compton» переехала на новый документ, комбо живо; подсказка на комбо «Draws only this response-matrix channel…» |
| `shot_06_charoite.png`, `crop_06_charoite.png` | «Чароит в домике» (ряд Ra-226, матрица): просьба Compton держится, комбо живо |
| `shot_07_cs137_10cm_nomatrix.png`, `crop_07_nomatrix.png` | корпусный Cs-137 на 10 см без геометрии (разбора нет: «FSA error: Full-spectrum decomposition needs a detector»): комбо ПОГАШЕНО, показывает All |
| `shot_08_label_tip_noresult.png`, `crop_08_label_tip.png` | подсказка на подписи погашенного комбо: «No response matrix for this spectrum: the model has no channels to show, the whole stack is drawn.» |
| `shot_09_house_back_compton_restored.png` | возврат на Cs137_house: комбо снова «Compton (partial deposit)», стопка канала |
| `shot_10_back_to_all.png` | выбор All: прежняя стопка; область графика отличается от `shot_01` только положением курсорной панели (bbox разницы 47..691 px — панель курсора и её строка), стопка та же |

Приложение закрыто Alt+F4 штатно; в песочницу записаны `config\layout\ExpertMode.xml` (15770 байт) и
`config\BecquerelMonitor.xml` — каталог Amber не тронут (её `ExpertMode.xml` 16:28, 14890 байт, до запуска).

## 3. Решения полосы (не спрашивались — по правилу «ответ в дереве», в отчёте вслух)

1. **Лента невязки и метки опор — от ПОЛНОЙ модели** в обоих режимах («Как есть, по своей галке»): в режиме канала
   стопка ниже модели на всё спрятанное, и лента «от стопки» показала бы спрятанное как «не описано». Следствие на
   экране: в режимах кроме Peak якорь опоры (`AMBER17`) висит над пустым местом, где стоит верх полной модели
   (`shot_04`). ⚠ Проверить экраном Amber: устраивает ли; альтернатива — прятать якоря в режиме канала (одна строка
   в `ShowFsaOverlay`).
2. **Белая линия верха стека — по верху стопки канала**, а не полной модели: линия очерчивает нарисованное.
3. **Подслой сумм-пиков — только в канале Peak** (физика: сумм-пик — полное поглощение пары; разбор кладёт его в
   `Peak`). На витрине Amber (цезий) подслоя нет — проверено только на пустоте, §2.3.
4. **Просьба о слое живёт в окне и едет на каждый показываемый документ** (как группировка и лента невязки); окно
   скрыто (`HideOnClose`) — режим остаётся (как у ленты невязки); окно уничтожено — график возвращается к All.
5. **Погашение** — по факту «у стопки есть слой с раскладкой по каналам», тем же правилом, что рисует график:
   без результата, без матрицы, при старом формате матрицы, при образах не по матрице комбо погашено; пустой канал
   старой четырёхканальной матрицы рисуется пусто (лента нулевой высоты — ни одного пикселя).
6. **Образы рентгена защиты (`Xray-Pb`, `Xray-Pb-L`) — строятся по матрице и несут каналы**, потому рисуются в
   режиме канала; `Backscatter` и `pile-up` каналов не имеют и прячутся — это данные результата, не список имён.
7. Общие ресурсы `Properties/Resources.resx` не тронуты: все строки — в собственной паре окна (`FSAReport_*`), как у
   блока качества `A247` (генерируемый `Resources.Designer.cs` не входит в файлы полосы).

## 4. Находки

* Своё, починено на месте: первый вариант пробы сравнивал слои двух кадров по ссылке (`ReferenceEquals` слоёв разных
  построений) и давал ложную находку «в стопке Cs-137, ждали Cs-137» — сравнение переведено на слои ТОГО ЖЕ кадра
  (ссылка) и имена стопки «All» (порядок).
* Чужое, строкой не заводится (дешевле фразы): на PNG `FsaStackShot` (фон чёрный, цвет линии спектра из конфига
  проб) лента невязки и линия измерения почти не видны — свойство стенда пробы, на экране приложения (`shot_03`)
  обе видны. Не дефект приложения.
* Собственная матрица Amber для «Точка в защите» — формата 9 (проверено заголовком файла `BQRM`, формат 9), то есть
  на её машине комбо будет живо без подмен.
* ⚠ **Чужое, дороже фразы, файл не полосы — на строку (номер не выдумываю, даёт распорядитель): приёмка окна отчёта
  `FsaReportViewProbe` §7 (блок качества) КРАСНА с `AMBER17` (11.09.2026) на всякой сцене с матрицей.** Прогон на
  главной сборке (`--spectrum=tools\CORPUS\corpus\spectra\AS80_Th232Medal.xml --control=…AS80_Cs137_0cm.xml`): четыре
  находки «пометок 4 вместо 3» (ru-RU и en-US, обе сцены σ×) — после трёх пометок «матрица / кривая / суммирование»
  стоит четвёртая строка «Привязка шкалы, опор | нет» (`FSAReportView.AddAnchorRow`, печатается всегда при живой
  матрице), а проба (`marks`, `:748`; последняя правка `a701457a` 09.09.2026, `AMBER11`) ждёт ровно три. К П104
  отношения не имеет (таблица не менялась; пробу полоса не запускала до переноса — она не в приёмке `check_all` и
  не в `build_all`). Лечение — в пробе ждать строки опор (одну «none» или три с усилением и нулём) при
  `result.ResponseMatrixUsed`; ~20 строк, файл `tools/effmaker/probes/FsaReportViewProbe.cs` не в перечне полосы.
  ⚠ Второе: при `--out=` в несуществующий каталог проба падает `ExternalException` GDI+ в `SnapshotSection` (код
  −532462766) вместо отказа словами — каталог надо создавать заранее; мелочь той же строки.
  Лог — `D:\BqMoni_Claude\p104\logs\report_view_probe_main.log`.
* Строк в `TODO.md` полоса не заводит (реестр не правится по постановке); находка выше — распорядителю строкой.

## 5. Файлы (главное дерево, для коммита по именам)

Приложение: `BecquerelMonitor/FSAReportView.cs`, `FSAReportView.Designer.cs`, `FSAReportView.resx`,
`FSAReportView.ru.resx`, `EnergySpectrumView.Fsa.cs`, `DocEnergySpectrum.cs`, `FullSpectrumAnalysis/FsaPresentation.cs`.
Оснастка: `tools/effmaker/probes/FsaChannelViewProbe.cs` (новая), `FsaStackShot.cs`, `FsaViewGroupsProbe.cs`,
`tools/check_fsa_view_groups.py`, `tools/check_fsa_showcase.py`, `tools/fsa_showcase/showcase.json`,
`tools/fsa_showcase/snapshot.ps1`, `tools/fsa_showcase/reference/ASN16_Cs137_house__infer_eq_peak.json` (новый),
`tools/fsa_showcase/reference/ASN16_Cs137_house__infer_eq_compton.json` (новый).
Журнал: этот файл, `handover/p104-amber45/*` (8 текстовых файлов, PNG нет).

Снято после переноса: worktree `D:\BqMoni_Claude\p104\wt`, `bin\Debug_p104`, `obj\Debug_p104`, `probes\build_p104`.
Оставлено до приёмки: `D:\BqMoni_Claude\p104\` (snapshots `shots\`, песочница `app\`, `head_ref\`, `probe_out\`,
`logs\`, `status.md`).

## 6. Как повторить

```powershell
# витрина (9 пар) из штатного каталога проб
python tools\check_fsa_showcase.py
# проба тождеств режима слоя — из рабочего каталога витрины (после прогона выше)
Set-Location tools\fsa_showcase\wd
.\FsaChannelViewProbe.exe --spectrum=..\spectra\ASN16_Cs137_house.xml --sample=137CS,40K --shield=82
# окно: перепись и положительный контроль
tools\effmaker\probes\build\FsaViewGroupsProbe.exe
tools\effmaker\probes\build\FsaViewGroupsProbe.exe --misplace=matrixLayerComboBox   # обязан дать 1
python tools\check_fsa_view_groups.py
# снимок одного слоя пробой
tools\effmaker\probes\build\FsaStackShot.exe --spectrum=tools\fsa_showcase\spectra\ASN16_Cs137_house.xml --infer "--set=Cs-137 + K-40" --matrix-layer=Peak --screen --scale=pow --out=D:\...\peak.png
```

## 7. Дописка строки `AMBER45` (готовый текст; строку закрывает Amber)

✅ **Исполнено П104 18.09.2026** ([журнал](handover/handover-2026-09-18-p104-amber45-matrix-layer.md)). В группе
«Display» окна FSA Report — подпись и комбо «Matrix layer» / «Слой матрицы»: All (умолчание, стопка как есть) и шесть
каналов `ResponseChannel` с подписями на двух языках (Full-energy peak / Пик полного поглощения, Compton (partial
deposit) / Комптон (неполное поглощение), Single escape (511 keV), K X-ray escape, Double escape (1022 keV), L X-ray
escape). Переключатель отрисовочный (обработчик мимо `ApplyCalculationChange`; `check_fsa_view_groups` и
`FsaViewGroupsProbe` судят комбо, `--misplace=matrixLayerComboBox` — отказ поимённо); выбор живёт в окне и едет на
показываемый документ (`DocEnergySpectrum.FsaMatrixLayer` → `EnergySpectrumView.FsaMatrixLayer`), в конфиг не пишется,
таблица не меняется. В режиме слоя стопка — те же слои тем же порядком и цветами, каждый — `ChannelCurves[канал]`;
слои без каналов (фон, сплайн, рассеяние, наложения, «прочее»), разнесённая подложка и хвост не рисуются; подслой
сумм-пиков — только у Peak; лента невязки и якоря опор — от полной модели; без матрицы комбо погашено с подсказкой,
просьба помнится. Приёмка: витрина — семь прежних пар 0 расхождений и область стопки PNG побитово 7/7, две новые
пары `ASN16_Cs137_house__infer_eq_peak` / `__infer_eq_compton` объявлены (`FsaStackShot --matrix-layer=`,
`check_fsa_showcase --only=ключ__режим`); `FsaChannelViewProbe` — «All» побитово прежняя стопка (24576 накоплений),
лента каждого слоя = свой канал на всех 6 каналах, Σ каналов = All − подложка − хвост − спрятанное на 8192 каналах,
без матрицы режим недостижим, оба положительных контроля (подмена канала ×1.01, подмена кадра) пойманы;
`check_resx*`, `check_headless`, `check_fsa_docs`, `check_fsa_view_groups` — 0. **Проверить экраном Amber:**
(1) стопка Peak / Compton на «Cs 137 в домике» (снимки `shot_03`/`shot_04` у полосы, PNG витрины
`wd\out\ASN16_Cs137_house__infer_eq_peak.png`); (2) якорь опоры в режимах кроме Peak стоит над верхом ПОЛНОЙ
модели, то есть над пустым местом (решение полосы §3.1; прятать якоря в режиме слоя — одна строка); (3) подписи
каналов EN/RU и подсказка погашенного комбо.
