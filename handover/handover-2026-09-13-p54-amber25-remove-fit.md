# 13–14.09.2026, полоса П54 — `AMBER25`: снятие фита кривой эффективности по спектрам

Дерево `C:\Users\moroz\source\repos\BQ Eng res .NET 4.8`, ветка `pie`, HEAD `cb1e7cf5`.
Малые артефакты — `handover/p54-amber25/` (приёмочная проба, кривые до/после, логи сторожей).
Временное — `D:\BqMoni_Claude\p54\` (снято после приёмки).

## 1. Постановка

Amber, 13.09.2026, консоль, дословно (о вкладке «Fit to measured spectra» конструктора
кривой эффективности — эмпирическом восстановлении кривой из спектров по вековому
равновесию): **«Этот функционал нужно убрать. Его не должно остаться.»** Строка реестра —
`AMBER25`; закрывает только Amber, полоса возвращает текст дописки (§7).

## 2. Что снято (весь путь)

### Приложение

| файл | что |
|---|---|
| `BecquerelMonitor/EfficiencyMaker/EfficiencyFitter.cs` | **удалён целиком** (1557 строк: серии, гауссов фит площадей, кластеризация линий, решение системы, `Run`, `Believable`, `Evaluate`/`Shape`/`BuildCurve`, `LoadReferenceCurve`/`SaveCurve` файлового входа-выхода харнесса) |
| `BecquerelMonitor/Utils/SpectrumScout.cs` | **удалён**: разведка «нужна ли спектру конфигурация прибора» — единственный потребитель был `EfficiencyMakerForm.AskFallbackDevice` |
| `EfficiencyMaker/EfficiencyModel.cs` | сняты `EfficiencyLine`, `EfficiencyObservation`, `EfficiencyFitInput`, `EfficiencyLibrary` (`BuildChains`, `AllKnownLines`, `SetReject`); `EfficiencyLevelSource` = `None`, `Simulation` (сняты `Reference`/`Anchor`/`ShapeOnly`); `EfficiencyFitResult` = `LevelSource`, `MinEnergy`, `MaxEnergy`, `Curve`, `ComputeStamp`, `Error`, `Ok` (сняты `Observations`, `Coefficients`, `SeriesKeys`, `SeriesOffsets`, `Level`, `Chi2Ndf`, `ReferenceCurve`, `AcceptedCount`). Имя контейнера НЕ переименовано — его читают `DoseRate`, `EfficiencyCalculation`, `EfficiencyCurveGraph`, десять проб и `tools/check_matrix_keys.py` |
| `EfficiencyMakerForm.cs` / `.Designer.cs` | вкладка `tabPageFit` со всеми полями (`spectraGrid`, `spectrumColumn`/`nuclideSetColumn`, три кнопки списка, `runButton`, `optionsGroupBox` с `orderNumericUpDown`, `minIntensity*`, `minSignificance*`, `backgroundCheckBox`, `anchor*`), подсказки `hints`/`SetUpHints`, `LoadChains`/`ReloadNuclideSets`/`GuessChain`/`Simplify`, `PackGeometryComplaints` (+`Add`/`Complain`), `BuildInput`, `AskFallbackDevice`, `TryParse`, `runButton_Click`, `UpdateGraphMode`, `OnActivated`, `FitTabHeight`; `Start` — одна кнопка вместо пары; `Finish` — только итог расчёта из геометрии; `SaveIntoConfig` — `Origin = Simulation` без развилки по `LevelSource`; `exportButton_Click` → `EfficiencyCurveIo.ExportCsv`. Форма: 1592 → 988 строк, дизайнер 550 → 229 (`wc -l`) |
| `EfficiencyMakerForm.resx` / `.ru.resx` | сняты 12 подписей снятых контролов; остались `$this.Text`, `calculateButton`, `saveButton`, `exportButton` |
| `EfficiencyMaker/EfficiencyCurveGraph.cs` | сняты `ShowDifference`, полоса «разность» (`DrawDifference`, `NiceSpan`, `Interpolator`, `DiffPanel*`), точки наблюдений и легенда серий (`SeriesColors`); остались опорная кривая пунктиром и посчитанная |
| `Properties/Resources.resx` / `.ru.resx` / `Resources.Designer.cs` | сняты **51** мёртвая строка `EfficiencyMaker*` (обе пары, en/ru, и свойства генератора): `ColumnNuclideSet`, `ColumnSpectrum`, `DeviceGoneQuestion`, `DeviceGoneTitle`, `FitDone`, `GraphDiffAxis`, `GraphDiffNoReference`, `GraphDiffNoResult`, `ImpossibleCurve`, `LevelAnchor`, `LevelReference`, `LevelShapeOnly`, `NoChains`, `NoChainsChecked`, `NoLiveTime`, `NoSetForSpectrum`, `NoSpectra`, `PackAmounts`, `PackDevices`, `PackGeometries`, `ReasonBlend`, `ReasonImpossible`, `ReasonImpossibleLog`, `ReasonLoneLine`, `ReasonNoPeak`, `ReasonOutlier`, `ReasonSeriesScatter`, `ReasonWeak`, `ReasonWindow`, `ReferenceImpossibleLog`, `Run`, `Running`, `SeriesDropped`, `SetDuplicateName`, `SetElementXray`, `SetGone`, `SetNoIntensity`, `SetSkipped`, `SetTooFewLines`, `Singular`, `SpectrumDone`, `SpectrumFilter`, `Status`, `TabFit`, `TipAnchorEfficiency`, `TipAnchorEnergy`, `TipMinIntensity`, `TipMinSignificance`, `TipOrder`, `TooFewLines`, `WholeLibrary`. Мёртвость каждой проверена `grep` по `BecquerelMonitor/` и `tools/` (включая незакоммиченный `EfficiencyCurveIo.cs`). Оставлена `EfficiencyMakerMenu` — мертва ДО полосы и не про фит (§6) |
| `BecquerelMonitor.csproj` | `EfficiencyFitter.cs` → `EfficiencyCurveIo.cs`; снята строка `Utils\SpectrumScout.cs` |
| `DeviceConfigForm.Efficiency.cs` | новая конфигурация заводится с `Origin = Simulation` (стояло `Measurement` — задел под фит) |
| `EfficiencyConfigData.cs` | значение `EfficiencyOrigin.Measurement` **оставлено** — `Origin` хранится в файле конфигурации, старые кривые должны читаться; описание говорит, что путь снят |
| `DoseRate.cs`, `PolynomialEnergyCalibration.cs`, `Package.cs` | комментарии: ссылки на `EfficiencyFitter`/`run.ps1` переведены в прошедшее время |

### Осталось (нужно расчёту из геометрии и другим потребителям)

* **Новый** `BecquerelMonitor/EfficiencyMaker/EfficiencyCurveIo.cs`: `LoadResultData(path, index)` /
  `LoadResultData(path, index, fallbackDeviceGuid)` (чтение спектра с калибровками, `A240` — причина
  отказа ПШПВ в тексте броска) и `ExportCsv(path, result)` (только кривая `E_keV,eps,err_pct`;
  второй таблицы наблюдений больше нет). Потребители: `tools/effmaker/MeasuredPoint.cs`, проба
  `FwhmReaderProbeF62 --mode=effmaker`, кнопка «Export CSV…» формы.
* `Evaluate`/`Shape`/`PivotEnergy`, `LoadReferenceCurve`/`SaveCurve` **не переехали**: после снятия у
  них нет ни одного потребителя (`Evaluate` стоял на полях фита `Coefficients`/`Level`/`ReferenceCurve`,
  файловый вход-выход был «только для офлайн-харнесса», который снят). Переезд мёртвого кода
  противоречил бы «не должно остаться».
* `EfficiencyLibrary.BuildChains` ушла: вне фита её звали только `ChainProbe` (§3 «согласие
  потребителей» — второй потребитель), `XrayLinesProbe` (§5 «в кривую не идёт») и `SetProbe`;
  `tools/nucdb/fill_intensity.py` ссылался на неё в докстринге как на потребителя соглашения о
  нормировке — докстринг переписан (соглашение живо, читатель — `FsaLibrary.BuildFromPeaks`).

### Обслуга `tools/effmaker/`

| снято | почему |
|---|---|
| `Program.cs` | консольный харнесс фита (`effmaker --input …`) |
| `EtlCheck.cs` | сверка площадей фиттера с `.efr` LSRM — без фиттера мёртв |
| `run.ps1`, `plot_curves.py`, `compare_scratch.py` | сборка/прогон/картинки харнесса фита; читали `out/*_curve.csv` |
| `probes/OrderProbe.cs` | степень полинома фита |
| `probes/SetProbe.cs` | список наборов вкладки фита и её подсказки |
| `probes/PackGeometryProbe.cs` | `PackGeometryComplaints` (E6) |
| `probes/ScoutProbe.cs` | `SpectrumScout` + выбиратель запасного прибора |

Оставлены (не про фит): `Simulate.cs`, `MeasuredPoint.cs` (ε(662) по реальному спектру против
расчёта; `LoadResultData` → `EfficiencyCurveIo`), `peak_area.py` (спутник `MeasuredPoint`: площадь
прямым суммированием «как real.cs»), `lsrm_eff.py` + `lsrm_points.csv` (разбор форматов LSRM и его
таблица — данные LSRM, не наш фит), `curves/`, `models/`, `handover-response-matrix.md`,
`ground_halfspace.py`, `borehole.py`, `coherent_share.py`, `import_lsrm_materials.py`,
`run_peakorigin.ps1`, `probes/`.

Правлены: `probes/BoundProbeF59.cs` (снят раздел A222.5 «опорная кривая фиттера» и `Extrapolated`;
35 → 33 проверки), `probes/ChainProbe.cs` (снята проверка `BuildChains`), `probes/XrayLinesProbe.cs`
(снят раздел 5 и помощник `InSet`), `probes/FwhmReaderProbeF62.cs` и `MeasuredPoint.cs` (вызов
`EfficiencyCurveIo.LoadResultData`), `probes/MakerSaveProbe.cs`, `probes/SetColorProbe.cs`
(комментарии), `probes/CalcRestoreProbe.cs` (попутно, §6), `README.md`, `probes/README.md`,
`tools/nucdb/fill_intensity.py`.

`out/` не тронут: он в `.gitignore` целиком и держит не только фит (`a101`, `mini16`, `p6_iso`,
`response*`, `gadras*`, `peak_origin.csv` …). Фитовые артефакты 03.08.2026 в нём — `ASN16_*_curve.csv`,
`*_roi.xml`, `*_curves.png`, `ASN16_scratch_vs_geometry.png`, `ASN16_from_app.xml`, `RC103_*`,
`one_ASN16_*` — локальные, не в git; снимать ли — решает Amber (§7).

## 3. Приёмка — числом и с положительным контролем

Сборки: Debug `bin\Debug_p54` (`obj\Debug_p54`), Release `bin\Release_p54_before` (HEAD) и
`bin\Release_p54_after`; пробы `tools\effmaker\probes\build_p54` (`build_all.ps1 -Bin … -Out …`).

1. **Сборка.** Приложение Debug и Release после правки — код 0, ни одной ошибки/предупреждения
   в `/v:minimal`. `build_all.ps1` до: 187 файлов «все собрались» (плюс 5 довесков); после:
   **181 файл «все собрались»** (плюс 5 довесков; 187 − 6 снятых), «каталог заверен (T226): приложение 156a75099bb8 (549 файлов), пробы 91b3256126f9 (186)», код 0 — последняя сборка ПОСЛЕ всех правок, включая две правки комментариев после прогона проб (лог `handover/p54-amber25/build_all_after.log`).
2. **`git grep`** по `EfficiencyFitter|EfficiencyFitInput|EfficiencyObservation|tabPageFit|spectraGrid|EtlCheck|peak_area`
   вне `handover/**`, `DONE.md`, `TODO.md` — в коде **0**; оставшиеся вхождения поимённо (§5).
3. **Окно конструктора отражением** (приёмочная проба `AcceptP54`, `handover/p54-amber25/AcceptP54.cs`,
   собрана голым `csc` против каталога проб; исполнялась из копии рантайма в `D:\BqMoni_Claude\p54\accept_*`):
   * ДО (положительный контроль — проба видит то, что снимается): вкладок **3** («Geometry editor»,
     «Calculate from geometry», «Fit to measured spectra»), 12 полей фита ЕСТЬ, 8 методов ЕСТЬ,
     `ShowDifference` ЕСТЬ, типы `EfficiencyFitter`/`EfficiencyFitInput`/`EfficiencyObservation`/
     `EfficiencyLibrary`/`EfficiencyLine` в сборке ЕСТЬ, `EfficiencyLevelSource` = None, Reference,
     Anchor, ShapeOnly, Simulation — «НЕ СОШЛОСЬ: 27», код 1;
   * ПОСЛЕ: вкладок **2** («Geometry editor», «Calculate from geometry» / `tabPageCalculate`); все 12 полей и 8 методов фита — «нет»; кнопки формы (Calculate from geometry, Save curve, Export CSV…) и все кнопки редактора геометрии (Save/Clone…/Delete шаблонов, From device calibration, шесть «…», Import from LSRM…) — у каждой обработчик `Click` есть; `ShowDifference` — нет; типов `EfficiencyFitter`/`EfficiencyFitInput`/`EfficiencyObservation`/`EfficiencyLibrary`/`EfficiencyLine` в сборке нет; `EfficiencyLevelSource` = None, Simulation — «ВСЕ СОШЛИСЬ», код 0 (`handover/p54-amber25/after/accept.log`).
   * `check_menu_accelerators.py` 0, `check_headless.py` 0 («безоконный путь 0, оконные 144»).
4. **До бита.** Кривая из геометрии умолчаниями приложения (`EfficiencyCalculation.Run(geometry,
   new EfficiencyCalculationOptions(), null, null)` — ровно путь кнопки «Посчитать из геометрии»;
   `physics = null`, `importance = null`) на `tools/CORPUS/corpus/geometries/AS80_point0.in`,
   узлы печатью `"R"`:
   * ДО: 38 узлов, клеймо `phys=17; hist=200000; grid=5-3000 keV/38 std; kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1`,
     sha256 текста узлов `7e031b8ab6445e97da2657db29a41ff6508d0425289e490570a079fd6391e6a8`, 13.0 с;
   * ПОСЛЕ: 38 узлов, клеймо посимвольно то же, sha256 `7e031b8ab6445e97da2657db29a41ff6508d0425289e490570a079fd6391e6a8` — **файлы узлов `before/curve_AS80_point0.txt` и `after/curve_AS80_point0.txt` совпали побайтно (`cmp`)**, 10.2 с.
   * `MakerSaveProbe` — «ВСЕ СОШЛИСЬ», код 0 и до, и после; единственная разница логов — исчезла
     строка «BecqMoni: nuclide library: … 152 entries» (форма больше не поднимает менеджер наборов
     при открытии). `CalcRestoreProbe` — ДО код 1 (красна с 23.08, §6.1), ПОСЛЕ «СОШЛОСЬ», код 0.
     `DoseRateFromCurveProbe` — «проверок 49, непрошедших 2» и до, и после (те же две среды, §6.4;
     остальные 47 сошлись; разница логов — только «записан файл сцены» у первого прогона).
     `GeometryLayoutProbe` («СОШЛОСЬ: состояний разметки проверено 56»), `FwhmReaderProbeF62
     --mode=effmaker` («СТОРОЖ ПРОШЁЛ: все плечи сошлись» — читатель `EfficiencyCurveIo.LoadResultData`
     живой, причина отказа ПШПВ в броске на месте), `CurveGenerationProbe` («ВСЕ СОШЛИСЬ») — логи
     ПОСЛЕ построчно равны ДО. `ChainProbe`, `XrayLinesProbe` — «ВСЕ СОШЛИСЬ», минус снятые разделы.
     `BoundProbeF59` — 35 → 33 проверки, отказов 2 и до, и после (A120, §6.2). Логи —
     `handover/p54-amber25/before/`, `after/`.
5. **`python tools/check_all.py` — код 0**, «ВСЕ ЗЕЛЕНЫ: 37 из 37 сторожей дали 0» (68 с;
   лог `handover/p54-amber25/check_all.log`). Отдельно `check_resx.py`, `check_resx_designer.py`,
   `check_resx_letters.py`, `check_resx_zorder.py`, `check_build_recipe.py`, `check_matrix_keys.py` — 0.

## 4. Как делалось

* Порядок: замер ДО (сборка HEAD, пробы, кривая, окно отражением) → правка → сборка → пересборка проб
  → те же замеры ПОСЛЕ. Кривая «до» снята с HEAD-сборки той же пробой, что и «после».
* ⚠ Грабля своя: правка комментария в `EfficiencyMakerForm.cs` ВО ВРЕМЯ `build_all.ps1` — сторож
  `T41` честно отказал («файл новее exe»), приложение пересобрано, `build_all` повторён. Второй
  повтор — из-за оставшихся в `build_p54` exe снятых проб (`T79`/`T88`: сторож их называет и не
  удаляет сам) — снял свои шесть exe/config, повторил.
* Последний круг: после прогона проб ещё две правки КОММЕНТАРИЕВ (`EfficiencyCurveIo.cs`,
  `BoundProbeF59.cs` — убрано имя снятого типа) → приложение Debug/Release и `build_all` собраны
  заново (181/181, заверено), пробы повторно не гонялись — правки в комментариях.
* `python` через heredoc Bash дважды ел обратные слэши (`\N` в пути) — правки резx/csproj/комментариев
  писались файлами-скриптами в `D:\BqMoni_Claude\p54\` (копии — в `handover/p54-amber25/scripts/`).

## 5. Где фит остался в дереве как история

* `tools/effmaker/README.md` — шапка со списком снятого; §«~~Метод~~», «Журнал фита», «Что получилось
  (03.08.2026)», «Кривая „с нуля“», «Поверка по градуировочным спектрам LSRM» (команды `EtlCheck`),
  «Остаточное завышение ASN16» (`EfficiencyFitter.MeasureLine`, `peak_area.py`), три пункта
  «Открытых пунктов» зачёркнуты — всё помечено «снят 13.09.2026», история не переписана.
* `tools/effmaker/probes/README.md` — разделы `ScoutProbe`/`SetProbe`/`PackGeometryProbe` — «СНЯТА 13.09.2026»,
  `OrderProbe` в шапке (история T3) с пометкой.
* Комментарии-«снято»: `EfficiencyMakerForm.cs` (шапка), `EfficiencyModel.cs`, `EfficiencyCurveGraph.cs`,
  `EfficiencyCurveIo.cs`, `EfficiencyConfigData.cs`, `DeviceConfigForm.Efficiency.cs`, `DoseRate.cs`,
  `Package.cs`, `BoundProbeF59.cs`, `ChainProbe.cs`, `XrayLinesProbe.cs`, `MakerSaveProbe.cs`,
  `tools/nucdb/fill_intensity.py`.
* Не тронуты (журналы): `tools/pie/s42-calibration-readers.md:66` (ссылка на `EfficiencyFitter.cs:1353-1384`
  как на образец комментария), `tools/effmaker/handover-2026-08-05.md`, `handover/**`, `DONE.md`,
  памятки. `tools/effmaker/probes/CascadeClampProbe.cs:223` — `peak_area` это имя колонки CSV, не файл.
* Реестр: `TODO.md` строка «Брать площади для эмпирической кривой из FSA (~~`E7`~~)» в таблице «Чего
  делать НЕ надо» называет `EfficiencyFitter.cs`/`peak_area.py` живыми — теперь фиттера нет; правит
  Amber (реестр полоса не трогает).

## 6. Находки

1. **`CalcRestoreProbe` была красной с 23.08.2026** (`E36` перевёл заводской низ 40 → 5 кэВ, проба ждала
   40): «пусто 5-3000 кэВ … РАСХОЖДЕНИЕ: границы», код 1 на HEAD. Починено на месте — два числа
   40 → 5 (ожидание границ и условие «сказано вслух» — второе всплыло только после первой правки),
   с пометкой в коде; после — «СОШЛОСЬ», код 0. Строки не завожу (дешевле часа, свои файлы).
2. **`BoundProbeF59` красна на HEAD в разделе A120** (не мой раздел): «включённых умолчанием: 5 (ждали 3)»
   и «`PositronOffset` сам по себе клейма НЕ двигает» — ожидания пробы отстали от решения 12.09.2026
   «семь ключей ВКЛ» (`e+off` среди них). Это про умолчания `ResponseMatrixOptions` — файл полосы П50
   (физика 18 сегодня ночью), и ожидания могут сдвинуться ещё раз; не трогал. В `TODO.md` строки нет
   (`grep BoundProbeF59` — 0 в TODO, 3 в DONE). **Строка на Amber:** «`BoundProbeF59` A120: два ожидания
   про умолчания ключей матрицы отстали от 12.09; поправить после П50» (P3, обслуга).
3. `EfficiencyMakerMenu` («Efficiency maker...») — мёртвая строка ресурсов ДО полосы, не про фит
   (`git grep` — только `Resources.*`); не снимал, чтобы не выходить за постановку. Одна строка
   в обеих парах — снять вместе с любой следующей правкой ресурсов.
4. `DoseRateFromCurveProbe` на этой машине даёт «ПРОВАЛОВ 2» из 49 и до, и после — нет матриц сцены
   ISO в `tools/effmaker/out/p6_iso` (среда, не код: «посчитать: corpusmatrixprobe --dir=… --force»).
5. `D app-silent-failures.md` в `git status` — не моё, стояло до полосы.

## 7. Текст дописки в `AMBER25` (не закрытие)

См. отчёт полосы; дублируется здесь:

> **СДЕЛАНО 13–14.09.2026 (П54), закрывает Amber.** Снят весь путь: `EfficiencyFitter.cs`,
> `Utils/SpectrumScout.cs`, вкладка `tabPageFit` со всеми полями и обработчиками, режим «разность»
> графика, типы фита в `EfficiencyModel.cs` (уровни `Reference`/`Anchor`/`ShapeOnly`),
> `EfficiencyLibrary.BuildChains` (живого потребителя вне фита не было), 51 строка ресурсов
> `EfficiencyMaker*` en/ru + `Resources.Designer.cs`, 12 подписей в `EfficiencyMakerForm.resx`/`.ru.resx`;
> обслуга: `tools/effmaker/Program.cs`, `EtlCheck.cs`, `run.ps1`, `plot_curves.py`, `compare_scratch.py`,
> пробы `OrderProbe`/`SetProbe`/`PackGeometryProbe`/`ScoutProbe`; раздел «Метод» и пункты фита
> `tools/effmaker/README.md` помечены «снят 13.09.2026», история не переписана. Осталось:
> контейнер `EfficiencyFitResult` (`Curve`/`MinEnergy`/`MaxEnergy`/`LevelSource.Simulation`/`ComputeStamp`/
> `Error`/`Ok`, имя прежнее — читатели не тронуты), помощники `LoadResultData`(×2)/`ExportCsv` (только кривая)
> — новый `EfficiencyMaker/EfficiencyCurveIo.cs`; `Evaluate`/`Shape`/`SaveCurve`/`LoadReferenceCurve` не
> переехали — потребителей нет; `EfficiencyOrigin.Measurement` оставлен ради чтения старых конфигураций;
> `peak_area.py`, `lsrm_eff.py`, `lsrm_points.csv`, `MeasuredPoint.cs` — не про фит, остались.
> Приёмка: сборка Debug/Release 0, `build_all` 181/181 (187 − 6 снятых); `git grep` по именам фита в коде 0
> (остатки — «снято» в README/комментариях, журналы); окно отражением — ДВЕ вкладки, кнопки с
> обработчиками, типов фита в сборке нет (ДО — 3 вкладки, 27 расхождений: положительный контроль);
> кривая из геометрии умолчаниями на `AS80_point0` до/после — побайтно тождественна (38 узлов, sha256 `7e031b8a…`, клеймо `phys=17; … rayl2=1` то же); `MakerSaveProbe`,
> `DoseRateFromCurveProbe` — как было; `check_all` 0 (37/37). Попутно починена `CalcRestoreProbe`
> (красна с `E36` 23.08). Журнал `handover/handover-2026-09-13-p54-amber25-remove-fit.md`. Снятые
> файлы — `git rm` за Amber (список в журнале §2). Решить Amber: снимать ли из `tools/effmaker/out/`
> (не в git) фитовые артефакты 03.08.2026 и строку про `BoundProbeF59` A120 (§6.2).

## 8. Файлы

Снятые (для `git rm`): `BecquerelMonitor/EfficiencyMaker/EfficiencyFitter.cs`,
`BecquerelMonitor/Utils/SpectrumScout.cs`, `tools/effmaker/Program.cs`, `tools/effmaker/EtlCheck.cs`,
`tools/effmaker/run.ps1`, `tools/effmaker/plot_curves.py`, `tools/effmaker/compare_scratch.py`,
`tools/effmaker/probes/OrderProbe.cs`, `tools/effmaker/probes/SetProbe.cs`,
`tools/effmaker/probes/PackGeometryProbe.cs`, `tools/effmaker/probes/ScoutProbe.cs`.

Новые: `BecquerelMonitor/EfficiencyMaker/EfficiencyCurveIo.cs`, этот журнал, `handover/p54-amber25/`.

Правленые: `BecquerelMonitor/BecquerelMonitor.csproj`, `DeviceConfigForm.Efficiency.cs`, `DoseRate.cs`,
`EfficiencyConfigData.cs`, `EfficiencyMaker/EfficiencyCurveGraph.cs`, `EfficiencyMaker/EfficiencyModel.cs`,
`EfficiencyMakerForm.cs`, `EfficiencyMakerForm.Designer.cs`, `EfficiencyMakerForm.resx`,
`EfficiencyMakerForm.ru.resx`, `Package.cs`, `PolynomialEnergyCalibration.cs`,
`Properties/Resources.resx`, `Properties/Resources.ru.resx`, `Properties/Resources.Designer.cs`;
`tools/effmaker/MeasuredPoint.cs`, `tools/effmaker/README.md`, `tools/effmaker/probes/README.md`,
`tools/effmaker/probes/BoundProbeF59.cs`, `CalcRestoreProbe.cs`, `ChainProbe.cs`,
`FwhmReaderProbeF62.cs`, `MakerSaveProbe.cs`, `SetColorProbe.cs`, `XrayLinesProbe.cs`;
`tools/nucdb/fill_intensity.py`.

Не тронуты по запрету: `EfficiencyMaker/ResponseMatrix.cs`, `EfficiencySimulator.cs`,
`tools/effmaker/handover-response-matrix.md`, `tools/check_matrix_keys.py` (П50), `handover/*p50*`,
поставочные конфиги, `TODO.md`/`DONE.md`.
