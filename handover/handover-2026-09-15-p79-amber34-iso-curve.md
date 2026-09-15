# П79 — `AMBER34`: кривая сцены поля `ISO` (см²) в активности, выделении, разборе FSA и нормировке спектра, 15.09.2026

## Постановка

Строка `AMBER34` (P1, дефект приложения, найдена ревизией математики/физики среза
merge — `handover/math-physics-review-2026-09-15.md` §AMBER34): кривая эффективности,
посчитанная для сцены изотропного поля (`GeometrySceneKind.Iso`), нормирована на
ЕДИНИЧНЫЙ ФЛЮЕНС — её значения площадь в см², клеймо несёт `norm=fluence`. Сверяла это
только мощность дозы (`DoseRateInput.Of`); зоны, выделение, разбор FSA и нормировка
спектра брали её через `FsaEfficiency.FromConfig` как ДОЛЮ НА КВАНТ, а фильтр
`Efficiency > 1.0` в `FsaEfficiency.FromPoints` молча выбрасывал точки.

Перепроверка постановки (`handover/handover-2026-09-15-amber-todo.md`): деление скорости
на площадь даёт 1/(с·см²), а не «Бк·см²»; отдельный пример площади не доказывает
выпадение всех точек; отказ интерполятора без проверки автоматического K зоны
недостаточен.

## Решения Amber 15.09.2026, вопросником, дословно

1. «В автоматическом режиме скрыть активность и показать причину; явно ручной режим
   сохранить (рекомендую).» — автоматический режим = `ROIDefinitionData.AutoBecquerelCoefficient == true`;
   ручной = сохранённое число K зоны, его не трогать; панель выделения ручного режима не имеет.
2. «Разбор идёт, Бк скрыты с причиной (рекомендую).» — разбор FSA с кривой `ISO` ИДЁТ
   (кривая годится как форма), беккерели не выводятся, причина рядом.

## Что оказалось не так, как думали

* **В отчёте FSA беккерелей НЕТ и не было.** `FsaComponentResult.CountRate`, пределы
  обнаружения и доли — в имп/с и процентах; `FsaAnalyzer.ComputeCharacteristicLimits`
  прямо говорит: «перевод в Бк требует абсолютного уровня кривой … врать беккерелями
  здесь нельзя». Скрывать в отчёте нечего — решение 2 сводится к тому, что разбор идёт,
  а окно отчёта называет род кривой в строке «Efficiency curve» (см. ниже).
* **Но в FSA есть одно место, где матрица берётся АБСОЛЮТНО, а не формой: каскадное
  суммирование.** `FsaCascadeSummer.PeakEfficiency/TotalEfficiency` — суммы строк
  матрицы, а у матрицы сцены поля (`NORM` = единичный флюенс) это площади в см²;
  суммирование перемножает их как вероятности регистрации квантов одного распада.
  У G1S Ø63×63 A_пик ≈ 14 см² против ε ≈ 0.05: сумм-пик вышел бы в сотни раз выше
  физического, молча. По существу у сцены без источника вероятности второго кванта
  того же распада нет вовсе — она зависит от положения источника. Поэтому при матрице
  поля суммирование не создаётся, а окно отчёта называет причину.
* **Старое поведение зоны было хуже, чем «кривой нет».** На старой сборке (положительный
  контроль ниже) у кривой `ISO` G1S все точки > 1 выбрасывались → `FromConfig` = null →
  `Resolve` отвечал `BqCoeffNoCurve` и подставлял СОХРАНЁННЫЙ K — а `MeasurementResultManager`
  причину не читал: строка была `IsValid = True`, активность 937 324 Бк по K = 777 при
  включённой галочке «авто». То есть не «кривой нет», а молчаливая активность по
  запасному числу. Панель выделения говорила «the efficiency curve gives no value at
  661.66 keV» — тоже не про то, что случилось.
* **Кривые из `ROI/*.xml` приложение не читает вовсе:** у `ROIConfigData` нет свойства
  кривой, `XmlSerializer` бросает элемент `<ROIEfficiency>` (`ROIConfigManager.TraceDroppedElement`,
  W15). Точка 1471.85 на 20 кэВ в поставочном `BecquerelMonitor/config/ROI/Obsidian Marinelli 0.5.xml`
  ни на что не влияет; поставочный файл по приказу Amber 05.09.2026 не принимается.
* **Замер распорядителя подтверждён:** точек > 1 в кривых корпуса и витрины нет, клейма
  `norm=fluence` в корпусе нет — витрина FSA осталась побитово на эталоне без переобъявления.

## Что сделано, по потребителям

### `FsaEfficiency` (`FullSpectrumAnalysis/FsaEfficiency.cs`)

* `FromConfig(config, out string refusal)` — новая перегрузка; прежняя `FromConfig(config)`
  оставлена (зовёт новую, причину теряет — для читателей, которым нужен только факт).
* Нормировка читается ОДНИМ правилом с дозой: `DoseRateInput.StampNormalization(config.ComputeStamp)`;
  кривая несёт `Normalization`, `IsPerUnitFluence`, `Name`.
* `FromPoints`: фильтр `> 1.0` снят как молчаливый. У кривой ДОЛЕЙ точка выше единицы —
  отказ ВСЕЙ кривой с причиной `FsaEfficiencyPointAboveUnity` (энергия и значение точки,
  инвариантной культурой); у кривой ПОЛЯ фильтра нет вовсе. Неположительные точки
  по-прежнему пропускаются (в поставочных файлах есть нули — прежнее правило).

### Зоны (`Utils/BecquerelCoefficient.Resolve`, `MeasurementResultManager.Translate`)

* `Result` получил `Refused` (отказ, не откат) и `StatusText` (короткая причина для
  клетки таблицы); `Source.Refused`.
* Авто + кривая поля: `Value = 0`, `Refused = true`, `Problem = BqCoeffFieldCurve` (с именем
  кривой), `StatusText = ResultFieldCurve` («field curve (cm²), no K»). `Translate` при
  `needsCoefficient && coefficient.Refused` даёт строку `IsValid = false` с этим статусом —
  тем же механизмом, что `ResultNoCoefficient`.
* Авто + отвергнутая кривая (точка > 1 у долей): как «кривой нет» — сохранённый K,
  `Problem = BqCoeffCurveRefused` с причиной точки. Поведение прежних мягких проблем
  (`BqCoeffNoCurve`, `OutOfRange`, нет выхода/энергии) не менялось.
* Ручной режим (галочка снята): кривую не спрашивает — побитово как был (проба: A и dA
  у `Point` и `Iso` совпадают до бита при K = 777).
* `ROIConfigForm.ShowBecquerelCoefficient`: подсказка галочки уже несла `k.Problem`;
  при отказе поля K теперь пусты, а не «0».

### Выделение (`BecquerelCoefficient.ForLine`, `EnergySpectrumView.ActivityCurveRefusal`)

* `LineProblem.FieldCurve` и `LineProblem.CurveRefused`, `LineResult.Refusal` (слова).
* На панели: `ActivityFieldCurveRefused` (с именем кривой) и `ActivityCurveRefused` (с
  причиной точки) — тем же местом, что `OutOfRange`/`NoCurve`. Ручного режима у
  выделения нет — всегда отказ (решение 1).

### Разбор FSA (`FsaAnalysisSession`, `FsaAnalyzer`, `FsaResult`, `FSAReportView`)

* `Capture`: кривая поля доезжает до анализатора как форма (`FromConfig` на неё не null —
  иначе гейт геометрии `RequireGeometry` отказал бы разбору целиком вопреки решению 2);
  отвергнутая кривая — `Job.EfficiencyRefusal`, и отказ гейта называет причину кривой
  (`FSACurveRefused`), а не геометрию.
* `FsaResult.EfficiencyPerUnitFluence`, `FsaResult.CascadeSummingRefusedFieldMatrix`;
  `FsaAnalyzer`: при матрице с `Normalization == PerUnitFluence` суммировщик не создаётся
  (`cascadeRefusedFieldMatrix`), образы по матрице строятся как прежде.
* Окно отчёта, блок качества: строка «Efficiency curve» при кривой поля =
  `FSAReportEfficiencyFieldCurve` («used as a shape: field-scene curve (cm² per unit
  fluence); becquerels are not derived from it», красным с вниманием); строка «Cascade
  summing» при матрице поля = `FSAReportSummingFieldMatrix`. На кривых долей строки
  побитово прежние (витрина).

### Нормировка спектра кривой (`Utils/SpectrumAriphmetics`, `DocEnergySpectrum`, `MainForm`, `EnergySpectrumView`)

⚠ Первое решение этой полосы («кривая поля делит как есть — флюенс 1/см², величина
честная») ОТОЗВАНО распорядителем тем же днём и здесь поправлено: отсчёты хранятся
`int[]` (`EnergySpectrum.Spectrum`, проверено пробой отражением), и деление на
A > 1 см² округляет малые каналы в ноль — 5 отсч. / 13.82 см² = 0.362 → **0**,
200 / 13.82 = 14.47 → 14. Это неверное число у человека на графике, а не «другая
величина»; у кривой долей ε ≤ 1 деление только увеличивает число, потери нет.
Правило теперь то же, что у активности — отказ словами:

* `SpectrumAriphmetics.NormalizeRefusal(efficiency)` — ОДИН предикат на всех
  читателей режима: null — нормируется; иначе причина: кривая не выбрана
  (`NormalizeNoCurve`), меньше двух годных точек (`NormalizeCurveEmpty`), кривая
  долей отвергнута точкой выше единицы (`NormalizeCurveRefused` — с точкой и
  энергией), кривая СЦЕНЫ ПОЛЯ (`NormalizeFieldCurve` — с именем и причиной про
  целочисленный спектр). `NormalizeSpectrum` при непустой причине оставляет спектр
  как есть — ровно то, что делала при отсутствующей кривой всегда.
* `DocEnergySpectrum`: пункт «Show Normalized by Efficiency Spectrum» заперт только
  когда кривой нет ВОВСЕ (как было); при выбранной, но негодной кривой пункт доступен,
  а щелчок называет причину дверью `AppUi.Report` и режим не меняет; вход в режим
  (`IsNormalizeByEfficiencyAvailable`, цикл кнопки) — по тому же предикату.
* `MainForm.NormalizeSpectrum` (команда «Normalize spectrum with ROI»): вместо
  `MessageBox` с текстом про K зоны («кривая не выбрана … взято сохранённое значение»)
  — `AppUi.Report` с причиной предиката.
* `EnergySpectrumView` (спектр сравнения в режиме нормировки): признак — тот же
  предикат; кривая поля и отвергнутая кривая ведут себя как отсутствующая (спектр в
  ноль), слов там нет по устройству — их говорит активному спектру пункт меню.

Поведение на кривой долей без отказа — побитово прежнее (проба: канал 662 = 40200 / ε
0.000439848 = 91 395 304; витрина FSA на эталоне).

### Ресурсы (`Properties/Resources.resx`, `.ru.resx`, `.Designer.cs`)

Тринадцать ключей, в обоих языках, добавлены байтами (CRLF, BOM сохранены; чужое снятие
`EfficiencyMakerCalcHint` полосой П77 не тронуто): `FsaEfficiencyPointAboveUnity`,
`BqCoeffCurveRefused`, `BqCoeffFieldCurve`, `ResultFieldCurve`, `ActivityFieldCurveRefused`,
`ActivityCurveRefused`, `FSACurveRefused`, `FSAReportEfficiencyFieldCurve`,
`FSAReportSummingFieldMatrix`; доделка — `NormalizeNoCurve`, `NormalizeCurveEmpty`,
`NormalizeCurveRefused`, `NormalizeFieldCurve`.

## Проба — `tools/effmaker/probes/IsoCurveActivityProbeP79.cs`

Без ключей, без окон (`FSAReportView` создаётся, но не показывается — строки блока
качества снимаются тем же `MakeQualityRows`, что рисует окно). Две кривые одной
геометрии, синтетика в духе П2 (`handover-2026-09-12-p2-iso-field.md` §3.1: A_пик = 13.82 см²
на 662 кэВ): `Point` — ε(662) = 4.4e-4, клеймо `phys=18; hist=200000; grid=30-3000 keV/34 std`;
`Iso` — A(662) = 13.82, то же клеймо + `; norm=fluence`. Сцена: пик 662 кэВ (σ 12 кэВ,
40 000 отсч.) на континууме 200/канал, фон 300/канал за 2000 с; нетто в ±40 кэВ —
1 206 338 отсч. за 1000 с = 1206.338 имп/с. Новые члены берутся отражением и по имени,
чтобы проба собиралась и на старой сборке и там отказывала числами.

Числа приёмки (новая сборка `bin\Debug_p79` / `probes\build_p79`, «ВСЕ СОШЛИСЬ», код 0;
вывод — `D:\BqMoni_Claude\p79\probe_new.txt`):

| путь | `Point` (доли) | `Iso` (см²) |
|---|---|---|
| `FromConfig` | кривая есть, не поле | кривая есть, `IsPerUnitFluence` = true, A(662) = 13.82 |
| зона авто | K = 2670.6548 = 100/(4.4e-4·85.1), A = 3 221 712.4 ± 161 112.6 Бк, `IsValid` = true | `Refused`, K = 0, `IsValid` = false, статус «field curve (cm²), no K», причина с именем кривой |
| зона ручная, K = 777 | A = 937 324.63 Бк | побитово те же A и dA (`4696298769321497198` / `4665891838359452856`) |
| выделение | A = 3 221 712.4 Бк, отказа нет | A = 0, отказ «the efficiency curve “G1S ISO field (проба)” is a field-scene curve (cm² per unit fluence, not fractions per quantum): activity not shown» |
| FSA | разбор есть, 2 компонента, χ²/ndf 0.170 | разбор ИДЁТ, 2 компонента, χ²/ndf 0.170, `EfficiencyPerUnitFluence`; окно: «Efficiency curve» = `FSAReportEfficiencyFieldCurve` |
| FSA + матрица поля | — | образы по матрице, суммирование не применено, `CascadeSummingRefusedFieldMatrix`; окно: «Cascade summing» = `FSAReportSummingFieldMatrix` |

Подсадка 1.5 в кривую долей (точка 661.657 кэВ): кривая отвергнута целиком, причина
«point 661.66 keV: efficiency 1.5 is above 1 — a fraction per emitted quantum cannot
exceed unity»; зона авто — K = 777 сохранённый, причина с точкой; выделение — A = 0,
отказ с точкой; нормировка — «Normalize by efficiency: the efficiency curve “G1S point
50 cm (проба)” is refused — point 661.66 keV: efficiency 1.5 …» (не «не выбрана»);
кривая ПОЛЯ с тем же 1.5 строится без причины. Прежнее «кривой нет»
(`Efficiency = null`) — как было: K сохранённый, не отказ, причина есть.

Нормировка спектра (раздел 6, доделка): спектр хранится `int[]`; 5 / 13.82 → 0,
200 / 13.82 → 14 (14.47); `Point` — причины нет, канал 662 = 40200 / 0.000439848 =
91 395 304; `Iso` — отказ `NormalizeFieldCurve` с именем кривой, спектр оставлен как
есть (канал 662 = 40200, канал 100 = 200); кривой нет — `NormalizeNoCurve`.

**Положительный контроль** — worktree `D:\BqMoni_Claude\p79\wt` на HEAD `6aa928b2`
(до правки), приложение `bin\Debug_p79old` + пробы `build_old` (`build_all.ps1` код 0),
скопирована только эта проба: **«НЕ СОШЛОСЬ: 31», код 1** (`D:\BqMoni_Claude\p79\probe_old.txt`).
Старая сборка показала: `Iso` → `FromConfig` = null; зона авто `IsValid = True`, K = 777
(сохранённый, молча), причина «No efficiency curve is chosen»; выделение — «the efficiency
curve gives no value at 661.66 keV»; FSA — гейт геометрии, разбора нет; подсадка 1.5 —
выброшена молча, K = 3269.49 по двум оставшимся точкам, активность 3 944 115 Бк показана.
Второй контроль после доделки (worktree поднят заново на том же HEAD, снова снят):
**«НЕ СОШЛОСЬ: 42», код 1** (`D:\BqMoni_Claude\p79\probe_old2.txt`) — к прежним 31
добавились: «в сборке нет `SpectrumAriphmetics.NormalizeRefusal`», отказ нормировки
пуст на подсадке и на кривой поля, ресурсов `Normalize*` нет. Worktree снят
(`git worktree remove`), журналы сборок и прогонов оставлены в `D:\BqMoni_Claude\p79\`.

## Сторожа

* `check_resx`, `check_resx_designer`, `check_resx_letters`, `check_resx_zorder`,
  `check_headless`, `check_probe_numbers`, `check_probe_tuning`, `check_registry_refs`,
  `check_fsa_docs` — код 0. Переводы строк: все правленные файлы CRLF без одиночных LF,
  BOM сохранён (счёт байтами до и после).
* `python tools/check_fsa_showcase.py --probes=tools/effmaker/probes/build_p79` —
  **код 0, «ВИТРИНА СОШЛАСЬ С ЭТАЛОНОМ: 7 пар»** без переобъявления эталона (свежесть
  каталога сошлась с деревом, T226). ⚠ Сторож печатает «вход изменился, а картинка нет:
  матрица …qk (`AS80_th_disk.qk`)» — сайдкар угловой таблицы в дереве, не моё.
* `python tools/check_all.py` (после доделки — тот же итог) — **код 1, 36 из 39 зелены**;
  три красных НЕ от этой полосы,
  все три красны и на чистом HEAD в worktree (проверено там же):
  * `check_corpus_generator` (код 1): `SUMMARY.md` расходится с пересборкой (на HEAD в
    worktree — и `summary.csv`, и `SUMMARY.md`; после checkout сторожа краснеют от CRLF
    рабочей копии — памятка `branch-pie-merged-into-master`);
  * `check_registry` (код 1, находок 8): «известное расхождение изменилось:
    NuclideDefinition.xml» (две копии `config/`, T66; на HEAD то же) + семь строк
    `AMBER22…AMBER38` ссылаются на ещё не закоммиченный
    `handover/handover-2026-09-15-amber-todo.md` распорядителя;
  * `check_fsa_showcase` по умолчанию (код 3): штатный каталог `probes/build` заверен
    14.09 23:15 и протух против дерева (81 файл приложения — чужие незакоммиченные правки и
    CRLF; законный код 3 по описанию сторожа). На каталоге полосы — код 0, см. выше.
* Соседние пробы на новой сборке: `BqCoeffProbe` — «ВСЕ СОШЛИСЬ», код 0;
  `FsaRobustnessProbe` — «НЕ СОШЛОСЬ: 1», код 1 — **и на старой сборке ровно так же**
  (см. «Находки»).

## Что не сделано и почему

* ~~Квантование `NormalizeSpectrum` при A > 1 см²~~ и ~~читатели «кривая не выбрана» для
  отвергнутой кривой~~ — сделаны в той же полосе по указанию распорядителя (раздел
  «Нормировка спектра кривой» выше), после того как эти файлы отданы полосе.
* **Хранение нормированного спектра в `int[]`** само по себе не менялось (общее хранение
  `EnergySpectrum`): кривая поля до деления не допускается, а у кривых долей потери нет.
* **Пометка рода кривой на графике** (легенда FSA, `FsaPresentationBuilder.QualityText`,
  `EnergySpectrumView.Fsa.cs`) — не добавлялась: причина стоит в окне отчёта, а правка
  отображения принимается по витрине и по одной в день (`display-change-accepted-on-showcase`).
* `FsaCompositionInference.Collect`, `FsaSampleLibrary.OfSpectrum`, `FloorAtFraction`,
  веса линий образа, обратное рассеяние — используют кривую формой, нормировка им
  безразлична; не трогались.

## Находки

1. **`FsaRobustnessProbe` красна с гейта геометрии `A277`** — плечо «линия на первом узле
   остаётся допустимой» зовёт `Analyze(…, efficiency: null)`, а `RequireGeometry` (умолчание
   с 10.09.2026) на null-кривой отказывает; на HEAD `6aa928b2` — «НЕ СОШЛОСЬ: 1». В реестрах
   строки нет. Лечение — одна строка в пробе (`RefitZ`-набор полей уже свой: добавить
   `RequireGeometry = false` либо дать кривую с геометрией). Не мой файл, не моя строка —
   возвращено распорядителю текстом.
2. ~~Читатели отвергнутой кривой~~ — закрыто доделкой (`NormalizeRefusal` у трёх читателей).
3. ~~Квантование нормированного спектра при A > 1 см²~~ — закрыто доделкой: кривой поля
   отказано словами, а не поделено с обнулением каналов.
