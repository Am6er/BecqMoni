# П112 (19.09.2026) — «отвязанный хвост» образа рисуется в слое канала Compton (`AMBER45`, дописка)

Полоса П112, главное дерево, ветка `master`, HEAD `0aab363e`. Правка ОТОБРАЖЕНИЯ FSA, фит не тронут.
Временное — `D:\BqMoni_Claude\p112\` (логи, дампы, PNG — на диске, не в git).

## 0. Постановка

Вопрос Amber 19.09.2026, дословно: **«Почему комптон на цезии имеет такую просадку в районе рентгена свинца?»**
Решение Amber 19.09.2026 вопросником, дословно: **«Класть хвост в слой Compton».**

Повод — замер экраном П109 (`handover-2026-09-19-p109-gui-check-amber45-48.md`): на спектре Amber «Cs 137 в
домике» (ASN16, 250 М отсч., матрица `ASN16_point_house`) в режиме комбо «Matrix layer» = Compton слой Cs-137
имеет провал на две декады в 60–100 кэВ — там, где рентген Pb K (Kα 72.8/75.0, Kβ 84.9 кэВ).

## 1. Причина провала (по коду, до правки)

1. Ниже порога доверия матрицы `FsaAnalyzer.ResponseContinuumTrustFloorKev = 100.0`
   (`FsaAnalyzer.cs:10695`) континуум образа идёт в фит СВОЕЙ свободной колонкой — «отвязанный хвост» (`S175`,
   `FsaAnalyzer.cs:8270–8300`, `AttachTails` :8514, `AddTail` :8622); хвост кладётся образу в
   `FsaComponentResult.TailCurve` (`FsaResult.cs:62–107`), у члена ряда — его доля (`SplitChainMembers` :8696).
2. Нож подпорогового хвоста вынимает те же бины из каналов исхода `ChannelCurves` (S37) — кроме окон линий самого
   образа (у Cs-137 ниже 100 кэВ это только Ba K 32/36 кэВ ±2 ПШПВ ≈ до 60 кэВ). Тождество слоя:
   Σ каналов = `FsaStackLayer.Curve` − `ContinuumCurve` − `TailCurve` (`FsaResult.cs:541–573`).
3. В режиме «All» стопка строится лентами `Curve`, хвост входит в ленту — провала нет. В режиме одного канала
   стопка строилась из `ChannelCurves[канал]` (`FsaMatrixLayers.CurveOf`, `FsaPresentation.cs:164`, вызов —
   `EnergySpectrumView.Fsa.cs:559` в `BuildFsaFrameData`), и хвосту было некуда лечь: ни один канал его не несёт.
   Отсюда провал в 60–100 кэВ ровно в тех бинах, где у образа Cs-137 нет окон линий.

Число (плечо HEAD, `D:\BqMoni_Claude\p112\head_compton\compton_head.curves.csv`): слой Cs-137 канала Compton в бине
70.21 кэВ — **224.4 отсч.**, при том что лента «All» там же — 122 041 отсч.; хвост Cs-137 всего 13 607 621.1 отсч.,
из них в 60–100 кэВ 9 711 603.3.

По физике хвост — то же комптоновское плато образа ниже порога (описание `TailCurve`), поэтому его место — в канале
комптона. Так и сделано.

## 2. Что изменено

| файл | что |
|---|---|
| `BecquerelMonitor/FullSpectrumAnalysis/FsaPresentation.cs` | ЕДИНСТВЕННОЕ место правила — `FsaMatrixLayers.CurveOf` (:164): при `mode == Compton` кривая для отрисовки = `ChannelCurves[Compton] + TailCurve` НОВЫМ массивом; остальные каналы и «All» — как были. Новый помощник `FsaMatrixLayers.TailDrawn(mode)` (:205) — один ответ «где рисуется хвост» (All, Compton). Шапка `FsaMatrixLayer` дописана словами Amber с датой. `using System;` ради `Math.Max` |
| `BecquerelMonitor/FullSpectrumAnalysis/FsaResult.cs` | только комментарии: у `FsaComponentResult.TailCurve` (:62–107) и `FsaStackLayer.TailCurve` (:573) — правило экрана П112 с датой и словами Amber; тождество каналов не меняется |
| `BecquerelMonitor/EnergySpectrumView.Fsa.cs` | только комментарии: шапка класса, поле `fsaStackCurves`, блок режима в `BuildFsaFrameData`. Код отрисовки не тронут — кривую отдаёт `CurveOf` |
| `BecquerelMonitor/FSAReportView.cs` | комментарий у `requestedMatrixLayer` |
| `BecquerelMonitor/FSAReportView.resx` / `.ru.resx` | подсказка комбо `FSAReport_MatrixLayerTip`: EN «Draws only this response-matrix channel of every component; the Compton channel also carries the component's untied tail below the matrix trust floor. Nothing is recomputed, the table is unchanged.»; RU «Рисует у каждого компонента только этот канал матрицы отклика; канал комптона несёт и отвязанный хвост компонента ниже порога доверия матрицы. Ничего не пересчитывается, таблица прежняя.» |
| `tools/effmaker/probes/FsaChannelViewProbe.cs` | (б): у Compton ожидание = канал + хвост слоя, кривая стопки — ДРУГОЙ массив (канал не тронут); (в): хвост слоёв с каналами в Σ каналов ВХОДИТ (лежит в стопке Compton), вычитается только у слоя без канала Compton; новая (е): числом в бине наибольшего хвоста и в полосе 60–100 кэВ; раздел 0: `TailDrawn` перечислением; контроли 3 (хвост подсажен в стопку Peak) и 4 (хвост вынут из стопки Compton — картинка до П112) |
| `tools/fsa_showcase/reference/ASN16_Cs137_house__infer_eq_compton.json`, `…__infer_eq_peak.json` | эталоны витрины переобъявлены `snapshot.ps1` (см. §3.1); `…__infer_eq.json` (All) не тронут — совпал |

`ChannelCurves` НЕ трогались (их тождество и пробы каналов на нём стоят); `FsaAnalyzer.cs`, `EfficiencyMaker/*`,
`TODO.md`, `DONE.md` — не мои, не тронуты. Концы строк правленных файлов — байтами: только CRLF, BOM где был.

## 3. Приёмка

Сборка: приложение Debug `BecquerelMonitor\bin\p112` (`/p:IntermediateOutputPath=obj\p112\`, `GenerateManifests=false`)
— код 0 (`logs\msbuild_p112.log`); пробы `build_all.ps1 -Bin BecquerelMonitor\bin\p112 -Out tools\effmaker\probes\build_p112`
— код 0, 195 проб, каталог заверен (T226), `check_fsa_docs` 0 (`logs\build_all_p112.log`).

Базы до правки (HEAD, штатный `build` 13:05): витрина 3 пары «Cs 137 в домике» — 0 расхождений
(`logs\showcase_baseline_head.log`); `FsaChannelViewProbe` — код 0, оба контроля пойманы (`logs\channel_probe_baseline_head.log`).

### 3.1 Витрина (`T260`) — таблица diff против эталона 12:23, до переобъявления (`logs\showcase_diff_before_snapshot.log`)

| спектр | режим | полоса | компонент | было | стало | Δ |
|---|---|---|---|---|---|---|
| ASN16_Cs137_house | infer_eq_compton | 0–30 кэВ | Cs-137 | 5 927 948.118 | 7 739 639.848 | +1 811 691.730 (+30.56 %) |
| ASN16_Cs137_house | infer_eq_compton | 0–30 кэВ | Xray-Pb | 5.887 | 88 308.386 | +88 302.499 |
| ASN16_Cs137_house | infer_eq_compton | 30–100 кэВ | Cs-137 | 9 394 461.726 | 20 985 070.553 | +11 590 608.827 (+123.38 %) |
| ASN16_Cs137_house | infer_eq_compton | 30–100 кэВ | Xray-Pb | 187 294.757 | 829 273.999 | +641 979.242 (+342.76 %) |
| ASN16_Cs137_house | infer_eq_compton | 100–300 кэВ | Cs-137 | 59 195 758.076 | 59 401 078.584 | +205 320.508 (+0.35 %) |
| ASN16_Cs137_house | infer_eq_compton | шапка | matrix_layer_combo | подсказка комбо прежняя | подсказка с фразой о хвосте | ≠ (текст) |
| ASN16_Cs137_house | infer_eq_peak | шапка | matrix_layer_combo | подсказка комбо прежняя | подсказка с фразой о хвосте | ≠ (текст) |
| ASN16_Cs137_house | infer_eq (All) | — | — | — | — | совпал с эталоном |

Проверка: Σ Δ по числовым строкам = 1 811 691.730 + 88 302.499 + 11 590 608.827 + 641 979.242 + 205 320.508 =
**14 337 902.806** = весь отвязанный хвост слоёв с каналами (`untied` шапки: Cs-137 13 607 621.1 + Xray-Pb 730 281.7 =
14 337 902.8) — в стопку Compton легло ровно то, что было хвостом, ни больше ни меньше. В `Peak` числа Δ = 0 (только
строка подсказки комбо — она одна на все каналы, ключ `FSAReport_MatrixLayerTip`). `model`, `net`, `fit`, `tail`,
`continuum_raw` — по всем 8192 каналам Δ = 0 между HEAD и П112 (фит не тронут).

Переобъявление: `& 'tools\fsa_showcase\snapshot.ps1' -Probes tools\effmaker\probes\build_p112 -Only
'ASN16_Cs137_house__infer_eq_compton,ASN16_Cs137_house__infer_eq_peak'` — код 0, эталон объявлен 2026-09-19 15:13,
HEAD `0aab363e` (`logs\snapshot_p112.log`). ⚠ `-Only` — ОДНОЙ строкой в кавычках: без кавычек PowerShell делает из
запятой массив, и обёртка склеивает его пробелом — «в витрине нет …» код 2.

После: витрина все 9 пар с `--probes=build_p112` — **0 расхождений, 55 с** (`logs\showcase_all9_after.log`);
`--selftest` — прошла (своё плечо 0; яма `--tail-as-residual` отвергнута; подсадка отвергнута) (`logs\showcase_selftest.log`).

### 3.2 Провал по бинам — было / стало (слой Cs-137, канал Compton; `head_compton\compton_head.curves.csv` против `compton_p112.curves.csv`)

| кэВ | было, отсч. | стало, отсч. | × |
|---|---|---|---|
| 60.37 | 12 067.6 | 113 561.5 | 9.4 |
| 64.91 | 1 785.0 | 113 117.1 | 63.4 |
| **70.21** (минимум) | **224.4** | **111 634.3** | **497.6 (2.70 декады)** |
| 75.14 | 586.3 | 110 541.0 | 188.5 |
| 80.06 | 2 212.8 | 110 057.4 | 49.7 |
| 84.99 | 8 315.9 | 109 585.1 | 13.2 |
| 89.92 | 25 404.7 | 108 096.2 | 4.3 |
| 94.85 | 53 504.9 | 105 409.8 | 2.0 |
| 99.78 | 80 728.2 | 103 195.9 | 1.3 |

Полоса 60–100 кэВ (105 бинов): было 1 796 176.7, стало 11 507 780.0, Δ = 9 711 603.3 = хвост Cs-137 в этой полосе.
Ожидание распорядителя «с ~2e3 до ~1e5 отсчётов/канал» — измерено: 2.2e2…2.2e3 в яме → 1.1e5.

### 3.3 Проба `FsaChannelViewProbe` (из `tools\fsa_showcase\wd`, exe = `build_p112` по sha) — код 0 (`logs\channel_probe_p112.log`)

* 0: `TailDrawn`: All — True, Compton — True, остальные пять каналов — False.
* (а) «All» — побитово прежняя стопка; (б) Peak/EscapeAnnihilation/EscapeXrayK/EscapeAnnihilationDouble/EscapeXrayL —
  лента = свой канал; Compton — лента = `ChannelCurves[1]` + `TailCurve` слоя, 3 слоёв (rel 1e-9, abs 1e-6).
* (в) Σ каналов 229 105 504.398 = верх All 246 399 580.854 − подложка 8 286 298.136 − ленты без каналов 9 007 778.320;
  хвост в стопке Compton 14 337 902.810 (входит), без канала Compton 0 — сошлось на всех 8192 каналах.
  (До правки Σ каналов была 215 622 362.243 — на хвост меньше.)
* (е) слой Cs-137: бин наибольшего хвоста — канал 35, 6.74 кэВ (< порог 100.0): канал Compton 1 445.553, хвост
  120 551.231, нарисовано в Compton 121 996.783 = канал + хвост; в Peak 41.230 = канал, без хвоста; в All 122 041.084 =
  лента. Полоса 60–100 кэВ: канал 1 796 176.7 + хвост 9 711 603.3 = нарисовано 11 507 780.0; лента All 12 564 006.4.
* (г) без матрицы — режим недостижим, комбо погашено с причиной; с матрицей подсказка — новая, с фразой о хвосте.
* (д) закрашено пикселей: All 183 442, Peak 42 940, Compton 111 699 (было 108 034).
* Контроли: 1 (ChannelCurves[Peak] ×1.01) — ПОЙМАН (в), 742 точки; 2 (накопление Peak +1 %) — ПОЙМАН (б), 1488 точек;
  **3 (хвост 2 слоёв, 14 337 902.8 отсч., подсажен в стопку Peak) — ПОЙМАН (б), 500 точек, худшая канал 35 |Δ| = 120 551;
  4 (хвост вынут из стопки Compton — картинка до П112) — ПОЙМАН (б), 509 точек, |Δ| = 120 551.**

### 3.4 Сторожа

`check_resx`, `check_resx_designer`, `check_resx_letters`, `check_resx_zorder` — 0.

`python tools/check_all.py --quiet` с `BQ_FSA_SHOWCASE_PROBES` = `BQ_FSA_REPORT_VIEW_PROBES` = `BQ_CORPUS_SCENES_PROBES` =
`…\build_p112` (три сторожа, гоняющие пробы, берут каталог из среды; штатный `build` собран ДО моей правки и по
`T226` законно протух на четырёх моих `.cs`) — **41 из 42 зелёные** (`logs\check_all_p112_run2.log`).
Красный один — `check_registry.py` код 1, 4 находки N8, ЧУЖИЕ: строки `TODO.md` AMBER45–48 (правка `TODO.md` не моя,
в дереве до начала полосы) ссылаются на незакоммиченные журналы `handover-2026-09-19-p109-gui-check-amber45-48.md` и
`handover-2026-09-19-p110-amber46-47-store-audit.md`; зеленеет коммитом этих журналов. Мой код и мои файлы в
находках не участвуют. Первый прогон без переменных — `logs\check_all_p112.log`: те же + два кода 3 «каталог проб
протух» на штатном `build` (ожидаемо; после приёмки штатный `build` пересобрать из `Debug_Codex`, как П104).

## 4. Артефакты (на диске, не в git) — `D:\BqMoni_Claude\p112\`

`logs\*.log` (сборки, витрина до/после, snapshot, selftest, проба до/после, check_all ×2); `head_compton\` — дампы
и PNG стопки Compton плечом HEAD и плечом П112; `channel_probe_out\channel_view_*.png` — 7 снимков стопок пробы.

## 5. Находки

1. Хвост есть и у образа `Xray-Pb` (730 281.7 отсч., в стопке Compton +641 979 в 30–100 и +88 302 в 0–30 кэВ) — по
   тому же правилу лёг в его канал Compton. Не дефект: хвост образа рентгена защиты — его же континуум ниже порога.
   Строки не требует; факт в журнал.
2. Строка подсказки комбо одна на все каналы (`FSAReport_MatrixLayerTip`) — поэтому переобъявлён и эталон `infer_eq_peak`
   (текст, числа Δ = 0). Делать подсказку поканальной — код в `FSAReportView` ради одной фразы; не сделано нарочно.
3. В эталоне витрины поле `inputs.probes.dir` теперь `tools/effmaker/probes/build_p112` (каталог полосы, снимается после
   приёмки). Поле информационное, сторож судит числа и sha; при следующем переобъявлении из штатного `build` вернётся.
   Не строка.

## 6. Дописка в строку `AMBER45` (готовый текст)

> ✅ П112 19.09.2026 — вопрос Amber, дословно: «Почему комптон на цезии имеет такую просадку в районе рентгена
> свинца?»; решение вопросником, дословно: «Класть хвост в слой Compton» — отвязанный хвост образа (`S175`) в режиме
> канала Compton рисуется вместе с каналом (`FsaMatrixLayers.CurveOf`, одно место; `ChannelCurves` не тронуты);
> провал слоя Cs-137 в 60–100 кэВ снят (70 кэВ: 224 → 111 634 отсч., ×498), All и остальные каналы Δ = 0; эталоны
> витрины `infer_eq_compton`/`infer_eq_peak` переобъявлены; `FsaChannelViewProbe` +(е) и контроли 3/4 —
> `handover/handover-2026-09-19-p112-compton-layer-tail.md`.

## 7. После приёмки снять

`BecquerelMonitor\bin\p112\`, `BecquerelMonitor\obj\p112\`, `tools\effmaker\probes\build_p112\`; `D:\BqMoni_Claude\p112\`.
Штатный `tools\effmaker\probes\build` пересобрать из этого дерева (`Debug_Codex`), иначе `check_fsa_report_view`,
`check_corpus_scenes`, `check_fsa_showcase` в `check_all` — код 3 «протух» до пересборки.
