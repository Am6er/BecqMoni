# Полоса fixeff — конструктор эффективности: геометрия, кривая, матрица (05.10.2026)

Строки `AMBER187`, `AMBER200`, `AMBER185`, `AMBER186` (разбор — §8.2–8.4, G.1, G.4
`handover/app-bug-review-2026-10-05.md`; решения Amber — `handover/app-fix-2026-10-05-coordinator.md`).
Матрицы и кривые корпуса не тронуты: клеймо матрицы, номер физики, формат `.rmx` прежние;
отпечаток геометрии — поле КРИВОЙ (`EfficiencyConfigData`), пустой в XML не пишется.

## Как мерено

Проба `FixEffProbe.cs` (вне дерева, сторожу не нужна; исходник и выводы `out_run_{base,new}_{ru,en}.txt`
лежат в `D:\BqMoni_Claude\fixeff\shots\`) — одна
исходная, собранная против двух сборок: БАЗОВОЙ (экспорт `HEAD f4abe6c8` в
`D:\BqMoni_Claude\fixeff\base`, `/restore`, Debug) и сборки полосы. Новые члены — отражением.
Каталоги запуска изолированы (склад матриц — `<каталог>\config\device\response`). Культура
потока `ru-RU` и `en-US`; вывод полосы в обеих одинаков. Геометрия —
`tools/effmaker/models/AS80_point5.in`.

## AMBER187 — редактор веществ

Было: `GeometryMaterialEditorForm.FieldChanged` разбирал вес и плотность только инвариантом;
неразобранный вес выбрасывал строку смеси молча, плотность «1,06» становилась 0 с отказом
«плотность должна быть больше нуля» без причины; вес `Infinity` проходил `> 0` и давал NaN-доли
(`GeometryMaterialLibrary.Compose`).

Сделано:
* `GeometryMaterialEditorForm.cs:734` `TryReadNumber` — точка, культура (`UserNumber.TryParseDouble`),
  запятая как точка (как `GeometryEditorPanel.TryGet`); `:623` плотность — неразобранная = NaN;
  `:655-677` начатая строка смеси идёт в состав всегда (вес NaN / пустое имя), набранный текст
  помнится для отказа; `:837`, `:868` отказ называет набранное; `:765` `MarkBadInputs` красит поле
  плотности и клетки смеси (`#FFE0E0`, цвет редактора геометрии).
* `GeometryMaterialLibrary.cs:953` `IsUsableAmount` (конечное, > 0) — в `Compose` и
  `TryDensityFromComponents`; ∞ и NaN в состав не идут.
* G.4: `GeometryScenes.cs:490` — NaN и ±∞ в длинах сцены — несогласованность
  `GeometryEditorErrorNotFiniteLength`.
* Ресурсы (оба `.resx` + Designer): `GeometryMaterialsErrorDensity` (теперь с `{0}`),
  `GeometryMaterialsErrorWeight`, `GeometryMaterialsErrorComponentNoName`,
  `GeometryEditorErrorNotFiniteLength`.

Замер (база → полоса), смесь CsI 0.5 + NaI 0.5:

| вход | база | полоса |
|---|---|---|
| вес «0,5» | составляющих 1, отказа нет | 2, отказа нет |
| плотность «1,06» | ρ = 0, «Плотность должна быть больше нуля.» | ρ = 1.06, отказа нет |
| вес «abc» | составляющих 1, отказа нет | 2, «Вес «Cesium iodide» в смеси — «abc» …», клетка красная |
| вес «Infinity» | отказа нет | отказ, клетка красная |
| плотность «1.O6» | ρ = 0, причина не названа | ρ = NaN, «Плотность «1.O6» не принята …», поле красное |
| вес «-1» | отказа нет | отказ |
| `Make`, вес ∞ | долей 3, NaN 2, сумма NaN | долей 2, NaN 0, сумма 1 |
| длина NaN / +∞ (G.4) | несогласованностей 0 | 1 (`NotFiniteLength`) |

Контроль: «0.5 / 0.5 / 1.06» — отказа нет в обеих; длина −5 — `NegativeLength` в обеих.

## AMBER200 — слой без вещества

Было: `CheckLayers()` звался один раз в `GeometryModel.Load`; `Warnings` — поле `[XmlIgnore]`,
копировалось `Clone`.

Сделано (`GeometryModel.cs`): предупреждения разбора `.in` — своё поле `ParseWarnings` (`:1560`);
`Warnings` (`:1581`) — свойство: разбор + `CheckLayers()` (`:1601`, открыт, возвращает новый
список), посчитанные по модели КАК ОНА ЕСТЬ при каждом чтении; `Clone` (`:1059`) переносит только
предупреждения разбора. Читатели `Warnings` (журнал счёта `EfficiencyCalculation.cs:~700`,
импорт ЛСРМ, конструктор кривой) получают свежую проверку без правки; вкладка эффективности формы
прибора показывает их под чертежом (`DeviceConfigForm.Efficiency.cs:543` `GeometryNotes`).
`OrVacuum` не тронут.

Замер (база → полоса): геометрия из XML, корпус без вещества — предупреждений 0 → 1; вещество
вернули — 0 → 0 (контроль); копия файла без плотности корпуса с исправленным веществом —
1 (устаревшее) → 0. Файл без плотности корпуса — 1 в обеих (контроль: прежний путь цел).

## AMBER185 — кривая для другой геометрии

Решение Amber 05.10.2026 «Отпечаток + предупреждение (Рекомендую)».

Сделано:
* `ResponseMatrix.cs:1857` `GeometryFingerprint` — 16 знаков SHA-256 ТОГО ЖЕ текста геометрии, что
  входит в клеймо матрицы (`GeometryText`/`StampView`); клеймо и формат матрицы не тронуты.
* `EfficiencyModel.cs` поле результата; `EfficiencyCalculation.cs:580` — ставится в начале счёта.
* `EfficiencyConfigData.cs:146` `GeometryFingerprint` (в XML только непустой —
  `ShouldSerializeGeometryFingerprint`), `:168` `CurveGeometryMismatch`; `Copy()` переносит.
* `EfficiencyMakerForm.cs:648` — отпечаток едет с кривой; при расхождении строка в журнал
  конструктора; `:892-920` `Finish` меняет `lastResult` ТОЛЬКО удачным счётом, прежняя кривая
  остаётся на графике и под «Сохранить».
* Вкладка эффективности (`DeviceConfigForm.Efficiency.cs:543`) и окно отчёта FSA
  (`FSAReportView.cs:1654`, строка-происшествие) — «Кривая эффективности посчитана для другой
  геометрии…». Импорт ЛСРМ с `.in` ставит отпечаток импортированной геометрии (`:921`).
* Ресурсы: `EfficiencyCurveOtherGeometry`, `FSAReportCurveOtherGeometryValue`,
  `EfficiencyMakerPreviousCurveKept`.

Замер: база — поля нет вовсе; «Стоп» после удачного счёта: прежняя кривая ЗАМЕНЕНА, «Сохранить»
погашена. Полоса — отпечаток `9cd59fc185d4d23c`; расхождение сразу False; после XML-круга и
`Copy()` False; геометрия +5 мм — True, вкладка говорит фразу; отпечаток пуст — False; XML кривой
без отпечатка элемента не несёт; счёт (2000 историй) — отпечаток результата = отпечатку
геометрии; «Стоп» — прежняя кривая ЦЕЛА, «Сохранить» доступна.

Экраном не проверено: строка окна отчёта FSA (нужен спектр со снимком кривой, у которой
отпечаток разошёлся; решение — тот же `CurveGeometryMismatch`, что промерен пробой).

## AMBER186 — матрица до сохранения конфигурации

Решение Amber 05.10.2026 «Временный файл + перенос (Рекомендую)».

Сделано (`EfficiencyMaker/ResponseMatrixStore.cs`): источник матрицы `ResponseMatrixSource`
(`Store`, `Pending`; сюда же ляжет `AMBER202`), временный файл `<guid>.rmx.pending` рядом со
складом (маска `*.rmx` его не берёт); `SavePending`, `CommitPending` (`File.Replace`/`File.Move`,
отказы словами), `DiscardPending`, `DiscardAllPending`, `DeleteRemoved` (не снимает Guid,
оставшийся у другой конфигурации — копия конфигурации делит Guid кривых). Читатели склада
(`FsaAnalysisSession`, `DoseRateManager`, `BecquerelCoefficient`) зовут прежние `Load(guid)` /
`PathOf(guid)` — только склад.
* `ResponseMatrixForm.cs:697` «Сохранить» пишет временный; окно показывает ждущую матрицу и
  строку «Ещё не на складе…» (`ResponseMatrixPendingNote`); `ResponseMatrixSaved` переписан.
* `DeviceConfigForm.Efficiency.cs:332` запись окном помечает конфигурацию изменённой; `:394`
  `CommitResponseMatrices`, `:435` `DiscardResponseMatrices`; `:506` поколение матрицы на вкладке
  — с ждущей (подпись `AMBER48` уходит сразу, как прежде).
* `DeviceConfigForm.cs` — только места записи: `button6_Click` (`:344`) и «Да» вопроса (`:1075`) —
  снимок Guid до записи, перенос после; «Нет» (`:1081`) — снять временные; закрытие формы (`:106`)
  — `DiscardAllPending`.
* Ресурс `ResponseMatrixCommitFailed`.

Замер пробой (склад: матрица 2 узла; окно: 3 узла): база — «Сохранить» трогает склад (True),
читатель склада видит 3 узла; полоса — склад не тронут, временный есть, читатель видит 2; «Нет» —
временный снят, склад 2; «Да» — склад 3, временный снят; удалённая кривая, Guid у другой
конфигурации — склад цел; ни у кого — снят; `DiscardAllPending` — временных нет.

Замер экраном (Release полосы в `D:\BqMoni_Claude\fixeff\app`, копия конфига Amber, RC-103,
кривая «Цилиндр» `755bbe16…`, 8 узлов 300–700 кэВ × 20000 историй): «Сохранить» окна — склад
445634 Б, sha `CCDE87C2…` прежний, рядом `.rmx.pending` 34056 Б; «Закрыть» формы → вопрос → «Нет» —
временного нет, sha прежний; повтор → «Да» — склад 34056 Б, sha `7B59148B…`, временных 0; удалить
кривую и «Сохранить» — `.rmx` снят, на складе 11 файлов из 12, прочие целы. Снимки — на диске, не
в git: `D:\BqMoni_Claude\fixeff\shots\amber186_1_saved_pending.png`, `amber186_2_question.png`,
`amber186_3_curve_deleted_saved.png`. ⚠ Экранная сборка собрана до последней правки (перенос
вызова `ShowGenerationNotes` и тела `PeekVersions` под сторожа `check_curve_generation`) —
поведение то же, правка чисто синтаксическая.

## Сторожа

`check_resx*.py` — 0. `check_curve_generation.py` — 0 (его самопроверка ищет литералы
`this.ShowGenerationNotes(GenerationNotes(` и `ResponseMatrix.PeekVersions(PathOf(` — сохранены).
`check_corpus_scenes.py --probes=tools\effmaker\probes\build_fixeff` — 0 (51 из 51 сцены, матрицы
на своих сценах). `check_corpus_generator.py` — 0. `check_fsa_report_view.py --probes=…build_fixeff` — 0 (326 ok, «ВСЕ СОШЛИСЬ»); `check_fsa_showcase.py --probes=…build_fixeff` — 0 (9 пар совпали с эталоном). В общем `check_all.py` эти три — код 3 «каталог проб протух»: штатный `tools/effmaker/probes/build` собран до правок волны (58 файлов разных полос), пересобирать его — не дело полосы.
`check_registry.py` красен ссылками `TODO.md` на незакоммиченные журналы волны 05.10 — не полосы.

## Остаток

* Удаление конфигурации прибора ЦЕЛИКОМ (`DeviceConfigForm.button4_Click` →
  `DeviceConfigManager.DeleteConfig`) `.rmx` её кривых не снимает: решение Amber говорит о кривых,
  удалённых из конфигурации; распространять снятие часов счёта на удаление всей конфигурации без её
  слова полоса не стала.
* В подробностях окна матрицы у ПОСЧИТАННОЙ, но не записанной матрицы «file: N KB» — размер
  файла склада (было и до полосы).
