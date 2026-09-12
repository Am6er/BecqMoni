# Полоса П11 (12.09.2026): пробы оснастки корпуса — состав из базы (следствие `AMBER19`, хвост (2) `T257`)

Дерево: ветка `pie`, `HEAD fb6ed404`, рабочая копия с незакоммиченными правками соседних
полос (П10 — `FsaAnalyzer.cs`, `--pileup-light=` в `CorpusFsaProbe.cs`; чужой
`FsaStackShot.cs` — не тронут). Сборка `bin\Debug_P11` (+ `obj\Debug_P11`), пробы
`probes\build_p11` (175 + 5 довесков), оснастка `scripts\wd_p11` (445 файлов, склад
главного дерева, 44 матрицы, `config\NuclideDefinition.xml` в ней НЕТ по `AMBER19`).
Полный корпус не гонялся.

## 1. Посылка и что подтвердилось

П8 назвала три пробы (`FsaChannelSplitProbe`, `FsaDoubleCountProbe`,
`FsaDeterminismProbeF29`), которые поднимают `NuclideDefinitionManager` и после гейта
`AMBER19` падают в оснастке. Задание велело составить список ЗАМЕРОМ: все 40 файлов
проб с `NuclideDefinitionManager.GetInstance` (в задании «39»; `grep -l` по рабочей
копии и по `HEAD` дают по 40, списки совпадают) прогнаны дымовым вызовом ДВАЖДЫ — из
оснастки `wd_p11` (файла нет) и из каталога проб `build_p11` (файл есть, 152 записи).
Скрипт и логи: `scratchpad\smoke.ps1`, `smoke_wd.csv`, `smoke_bp.csv`.

**Оснастка, до правки: 38 из 40 падают** броском `InvalidOperationException` «nuclide
library could not be loaded and there is no UI» (`S100`), код −532462766, ещё до первого
числа; 2 не падают потому, что дымовой вызов (`--selftest`) до менеджера не доходит
(`LabelTruthProbe`, `XrayLabelProbeF53`). То есть посылка П8 была УЖЕ реальности в
тринадцать раз — но падать в оснастке и ЖИТЬ в оснастке — разные вещи.

**Каталог проб, до правки** — тот же вызов с файлом: разряд решается по тому, чего пробе
не хватает БЕЗ оснастки. Признак разряда (а): проба берёт матрицу из склада
`config\device\response` рядом с exe (`ResponseMatrixStore.Load` без обходного ключа) —
в каталоге проб она честно говорит «матрицы НЕТ (NoFile)» и мерить ей нечем.

## 2. Таблица разрядов (замер, а не греп)

| проба | оснастка до | каталог проб до | почему | разряд |
|---|---|---|---|---|
| `FsaChannelSplitProbe` | S100 | код 1 «матрица: НЕТ — NoFile … опыт негоден» | склад | **(а)** |
| `FsaDoubleCountProbe` | S100 | код 1 «в складе нет файла на Guid» | склад | **(а)** |
| `ResponseRowDumpProbe` | S100 | код 1 «матрицы НЕТ (NoFile)» | склад; состава нет вовсе, менеджер поднимался голым вызовом | **(а)** |
| `FsaChannelShot` | S100 | код 1 «матрицы НЕТ … каналов не существует» | склад | **(а)** |
| `FsaComponentDumpProbe` | S100 | код 1 «матрицы НЕТ (NoFile)» | склад | **(а)** |
| `FsaSumPeakAccountProbe` | S100 | код 1 «без матрицы сумм-пики не строятся вовсе» | склад | **(а)** |
| `FsaCascadeProbe` | S100 | код 1 «матрица: нет, отпечаток НЕ сошёлся» | склад (`--rebuild` туда же пишет) | **(а)** |
| `CrystalXrayGateProbe` | S100 | код 1 «матрица не взята (отказ NoFile) — раздел 1 мерить нечем» | склад | **(а)** |
| `FsaStackShot` | S100 | код 1 «матрицы нет (NoFile)» | склад | **(а), ЧУЖОЙ** — не тронут, остаток `T257` |
| `FsaDeterminismProbeF29` | S100 | **код 0** «ВСЕ СОШЛИСЬ» (`--runs=2 --phase=fsa`) | сеанс приложения, состав по подписям (`--source=nucbase|peaks`); склад не нужен | (б) |
| `FsaGateRescueProbe` | S100 | код 2 «МАТРИЦА НЕ ВЗЯТА … дайте --matrix=<файл .rmx>» | с 10.09 (`T256`) нарочно берёт матрицу ключом из каталога проб; спектр и сет Amber | (б) |
| `AnnihilationGateProbe`, `EscapeOrphanProbe` | S100 | код 1 (дымовые сеты не те) | сеты Amber на её спектрах, вывод состава по подписям; склада не трогают | (б) |
| `TrustFloorProbe` | S100 | код 2 «кривая не нашлась ни в одном приборе» | самодостаточна (`--geometry=`, матрица в памяти) | (б) |
| `FsaInferProbeF51`, `FsaStampProbe`, `FsaPaletteProbe`, `NumericCultureProbeF68` | S100 | код 0 | пробы приложения | (б) |
| `FsaReportViewProbe`, `FsaResidualProbeF21`, `FsaSelectionProbeF16`, `FsaSessionProbe`, `PeakHighlightProbeG9`, `SelectionPanelProbeG10`, `RefusalWordsProbe` | S100 | 2 / 1 / 1 / 1 / 1 / 0 / 1 | окна приложения, подписи из поставочного списка по праву | (б) |
| `BqActivityProbe`, `ChainProbe`, `FwhmReaderProbeF62`, `FwhmVoiceProbeF54`, `IntensityLinesProbe`, `LabelPathProbeF49`, `LabelTruthProbe`, `NuclideSetMemoryProbe`, `OrderProbe`, `PeakOriginProbe`, `S109ActivityProbe`, `SetColorProbe`, `SetProbe`, `XrayLabelProbeF53`, `XrayLinesProbe` | S100 (2 — не доходят) | 0 (OrderProbe — своё падение на `--ref=` ROI, к менеджеру не относится) | сеты/списки приложения | (б) |

Итог: **(а) — 8 проб + чужой `FsaStackShot`**; из четырнадцати кандидатов задания
шесть оказались (б): `FsaDeterminismProbeF29`, `FsaGateRescueProbe`,
`AnnihilationGateProbe`, `EscapeOrphanProbe`, `TrustFloorProbe`, `FsaStampProbe` — им в
оснастке делать нечего, и они не тронуты.

## 3. Сделано

### 3.1. Общий вход в приложении (`T257`, хвост (2)) — `BecquerelMonitor/FullSpectrumAnalysis/FsaSampleLibrary.cs`

Копий сборки спецификации оказалось не три, а **пять**: `CorpusFsaProbe.SpecOf`,
`FsaCompositionInference.Infer`, `FsaStackShot.DeclaredSpec`, `FsaChannelSplitProbe.SpecOf`,
`FsaDoubleCountProbe.SpecOf` — и они уже разошлись (две последние не добирали элементы пробы
из геометрии; корпусная не ставила вещество кристалла; пробы переводили метку ряда в nucid
своим `NucidOf`, который «U-238u» дал бы корень «238uU» молча).

* `FsaSampleChain.FromLabel(label)` + `KnownLabels` — словарь меток манифеста (перенесён из
  `CorpusFsaProbe.ChainOf` вместе с особым случаем `U-238u`); неизвестная метка — `null`.
* `FsaSampleSpec.OfSpectrum(rd)` — обстановка спектра: кривая (`S98`), порог АЦП (`A73`),
  окно поиска пиков, `CrystalMaterialName` прибора (`A276`), кристалл с долями и элементы
  пробы из геометрии (`S84`). Единственная копия.
* `FsaSampleSpec.Declared(rd, chains, nuclides, equilibrium, atomic)` и
  `FsaSampleSpec.FromManifest(rd, chainLabels, nuclides, equilibrium, atomic)` (метки словами
  манифеста; неизвестная — `ArgumentException`).
* `FsaCompositionInference.Infer` зовёт `OfSpectrum` (своя копия снята; `EfficiencyConfigData`/
  `GeometryModel` там больше не нужны). `CorpusFsaProbe.SpecOf` = `FromManifest` + добор
  `materials.csv` (данные корпуса, остаются в пробе); `ReadTruth` проверяет метки через
  `FromLabel` и печатает список известных.
* ⛔ `FsaStackShot.DeclaredSpec` — чужая незакоммиченная правка, НЕ тронута: остаток `T257`
  (для владельца — одна строка: `FsaSampleSpec.Declared(rd, null, nuclides, equilibrium, atomic)`).

### 3.2. Восемь проб разряда (а) — состав из базы по ключам

Общий образец у всех: `--sample=<nucid,...>` («Cs-137» тоже понимается) и/или
`--chain=<метка,...>` (метки манифеста; неизвестная — код 2 со списком известных);
`FsaSampleSpec.FromManifest` → `FsaSampleLibrary.Build` → пики подписываются
`FsaSampleLibrary.AsDefinitions(library)` (как `CorpusFsaProbe --lib=sample`); `--set=` —
отказ кодом 2 с подсказкой; `NuclideDefinitionManager` не поднимается; в конце ВСЕГДА
печатается `NuclideDefinitionManager за прогон: обращений N`, N > 0 — код 12
(`SuppliedLibraryGate`, вторая дверь как у `CorpusFsaProbe.RefuseIfManagerRaised`; по
`isLoaded` подъёма не видно — без файла он бросает, `S100`).

| проба | что снято | что добавлено |
|---|---|---|
| `FsaChannelSplitProbe` | голый `GetInstance()`; своя `SpecOf` | `--sample=` синоним `--nuclides=`; `SpecOf` → `FromManifest`; проверка меток; гейт |
| `FsaDoubleCountProbe` | голый `GetInstance()`; своя `SpecOf`; `NucidOf` | то же (`--crystal=` добирается поверх); гейт; починено ожидание `Ann-511` (§5) |
| `ResponseRowDumpProbe` | голый `GetInstance()` | гейт (состава у пробы нет) |
| `FsaChannelShot` | `--set=`, `SelectSet`, `BuildFromPeaks` по поставочному | `--sample=`/`--chain=`; библиотека из базы; гейт |
| `FsaComponentDumpProbe` | `--set=`, `BuildFromPeaks`, «библиотека прибора: N определений» | то же; печать «определений из базы» |
| `FsaSumPeakAccountProbe` | `--set=`, `SelectSet`, `BuildFromPeaks` | то же; флаги из `FsaCalculationOptions.Of(rd)` как у плеч |
| `FsaCascadeProbe` | `BuildFromPeaks` по поставочному | `--sample=`/`--chain=`; гейт; шапка про склад |
| `CrystalXrayGateProbe` | `--set=`, `Infer` из подписей, `Load(path, nuclides)` | `--sample=`/`--chain=`; спецификация одна на оба плеча, флаги плеча через `options.ApplyTo(spec)`; гейт |

### 3.3. Сторож `tools/check_corpus_library.py`

Список `RIG_PROBES` — явный, восемь проб с причиной у каждой (`FsaStackShot` не входит —
чужой); правила `RIG_FORBIDDEN`: `GetInstance`, `.NuclideDefinitions`, `.NuclideSets`,
`.ActiveSet`; `FsaLibrary.BuildFromPeaks` пробам оснастки РАЗРЕШЁН (в `FsaDoubleCountProbe`
он строит библиотеку «по пикам» из определений ИЗ БАЗЫ — это её замер `A168`). `--selftest`
дополнен: подъём, подложенный внутрь `Main` НАСТОЯЩЕГО `FsaChannelSplitProbe.cs`, — ровно
одна находка; чистый текст — ноль; `BuildFromPeaks` — ноль. Контроль на настоящих старых
текстах (`git show HEAD:…`, восемь проб): находок 1 / 1 / 1 / 7 / 7 / 6 / 4 / 7 — все
восемь старых версий сторож отверг бы.

### 3.4. Вне списка файлов задания — `tools/check_crystal_material.py` (правило 5)

Сторож `A276` судил ТЕКСТОМ, что `FsaCompositionInference` кладёт `spec.CrystalMaterialName =
resultData.DeviceConfig.CrystalMaterialName`; после сведения копий присваивание живёт в
`FsaSampleSpec.OfSpectrum`, и сторож стал красным по устаревшей посылке при сохранённом
поведении. Правило 5 принимает обе формы: прямое присваивание в `Infer` ЛИБО вызов
`FsaSampleSpec.OfSpectrum(resultData)` в `Infer` И присваивание внутри `OfSpectrum` в
`FsaSampleLibrary.cs`. Самопроверка сторожа (две порченые копии) — прежняя, сошлась.
⚠ Файл не из списка задания; правка 6 строк + строка шапки, названа в отчёте отдельно.

## 4. Приёмка

| что | результат |
|---|---|
| MSBuild `Debug_P11` | код 0 (трижды) |
| `build_all.ps1 -Out build_p11` | код 0, 175 проб + 5 довесков, каталог заверен; в наборе приложения помимо моих — `FsaAnalyzer.cs` П10 (ключ `--pileup-light=`, умолчание выкл) |
| `mk_appwd.ps1 -Wd wd_p11` | код 0, «ОСНАСТКА СВЕЖАЯ: 445 файлов сошлись», «библиотека: в оснастке корпуса НЕТ по правилу AMBER19» |
| восемь проб (а) в `wd_p11` штатным вызовом (`scratchpad\smoke_a.ps1`, `smoke_a2.csv`) | **все код 0, `обращений 0`** у каждой; `--spoil=cf` → 1, `--set=` → 2, `--chain=Pu-239` → 2 со списком известных, без состава → 2 |
| `FsaChannelSplitProbe` числа П3 | `AS80_Cs137_0cm --nuclides=137CS`: χ²/ndf **8.3264**, `--no-anchor` **12.0228**, «каскада нет — проверка не применима», плечи совпали; `G1S16_Eu152_P5`: 12.2834, каскад 30 из 30, «CF тронул канал: да/да»; `G1S16_Th228_P5 --chain=Th-228`: 4.1727, 26 из 46, да/да; все тождества — «да», ИТОГ «всё сошлось»; `--spoil=cf` на Eu-152 — код 1 (НЕТ/НЕТ). Числа П3 (8.33 / 12.02 / 12.2834 / 4.1727) воспроизведены |
| `CorpusFsaProbe` малая база `--anchor-zero` умолчание (`run_mini.ps1 -Out out_p11_mini -Wd wd_p11 -Bin Debug_P11 -ProbeBuild build_p11`) | код 0; шапка «гейт библиотеки (AMBER19): …файла НЕТ», конец «менеджер за прогон не поднимался (обращений 0)»; понятная 42: **Σχ² 306.4, медиана 4.13, recall 100 %, фантомов 0** = `out_rev17_mini`; сверка `scratchpad\cmp_mini.py` с `out_rev17_mini` (маска `ms`/`cpu_ms`): **16 файлов, 407 строк, расхождений 0**; `FsaAnalyzer.cs` на момент прогона sha `40A116BD2E766A92` (версия П10 с ключом, умолчание выкл) |
| `check_corpus_library.py` | код 0 (11 файлов ЧИСТО); `--selftest` СОШЛОСЬ: подлог 3, ложная тревога 0, `PeakDetector` 1/1/0, пробы оснастки — чистая 0 / с подлогом 1 / `BuildFromPeaks` 0 |
| `check_crystal_material.py` | код 0, «(общим входом FsaSampleSpec.OfSpectrum)», самопроверка сошлась |
| `python tools/check_all.py` | **ВСЕ ЗЕЛЕНЫ: 34 из 34** |
| пробы (б) в `build_p11` после правки (`smoke_bp2.csv`) | коды ВСЕ ТЕ ЖЕ, что до правки (`smoke_bp.csv`), у всех 32 — приложение изменилось (сведён `Infer`), пробы по подписям ведут себя прежне |
| переводы строк моих файлов (байтами) | CRLF у `FsaCompositionInference.cs`, `CorpusFsaProbe.cs`, `FsaCascadeProbe.cs`; LF у остальных; BOM как было; голых CR нет (`scratchpad\p11_before_sha.txt` / `p11_after_sha.txt`) |

## 5. Попутная находка — починена на месте (мой файл)

`FsaDoubleCountProbe` на документированной сцене (`G1S16_Th228_P5 --chain=Th-232`, П20
06.09) давала **код 1**: «матрица есть/нет, вылеты вкл: `Ann-511` есть — False вместо True».
Не от моей правки: HEAD-версия пробы, собранная против `Debug_P11` и запущенная в КОПИИ
оснастки с подложенным поставочным файлом (`scratchpad\old_probe.ps1`, `wd_old`), даёт те же
два отказа. Причина — ожидание устарело 08.09 с гейтом столкновения `AMBER7`: у Th-ряда
своя линия Tl-208 510.77 кэВ, и `Ann-511` фиту не предъявляется. Ожидание теперь
`escapeOn && AnnihilationCollides == null`, столкновение печатается; на сцене — «ВСЕ
СОШЛИСЬ», код 0. (`AS80_Th232_v2` из того же журнала П20 сегодня без кривой — часть
`unknown`, разбора нет; не сцена этой пробы.)

## 6. Находки — по порядку

1. **Починено на месте**: ожидание `Ann-511` в `FsaDoubleCountProbe` (§5); словарь меток
   рядов в одном месте (`U-238u` у двух проб был бы «238uU»); голый подъём менеджера у
   трёх проб; посылка «третья копия» у `T257` — копий пять.
2. **Дописка в существующие строки**: `T257` хвост (2) и ~~`AMBER19`~~ — тексты в отчёте.
3. **Факт — в журнал, без строки**: (i) правило 5 `check_crystal_material.py` судило по
   тексту одного файла и красне́ло от рефакторинга при сохранённом поведении — поправлено
   (§3.4); (ii) у шести кандидатов задания разряд (б) — измерено (§2); (iii) в дымовом
   прогоне каталога проб красны на МИНИМАЛЬНЫХ (не документированных) ключах пробы (б):
   `FsaResidualProbeF21` («галочек в блоке ровно шесть — 5 вместо 6», «наша — шестая»),
   `FsaSelectionProbeF16` («все подписи блока помещаются в колонку — 1 вместо 0»),
   `PeakHighlightProbeG9` («полоса ПШПВ видна»), `RefusalWordsProbe --arm=all`
   (известно с 08.09: плечи `matdb` ждут порченой базы) — коды одинаковы ДО и ПОСЛЕ моей
   правки, к `AMBER19` не относятся, не разбирались; логи в `scratchpad\smoke_out_bp*`.
   Первые три — распорядителю: либо чужая полоса дня (окно отчёта менялось П1/П10), либо
   старое; строку заводить не мне.
4. **Новых строк: 0.** Ни `git add`, ни коммитов.
