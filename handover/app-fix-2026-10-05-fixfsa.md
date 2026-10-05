# Полоса правки «fixfsa» — ядро и сеанс FSA, 05.10.2026

Строки `AMBER180`, `AMBER199` (разбор — [app-bug-review-2026-10-05.md](app-bug-review-2026-10-05.md)
§6.2, §7.1; перепроверка — [app-fix-2026-10-05-coordinator.md](app-fix-2026-10-05-coordinator.md)).
Работа в основном дереве, без коммита. Замерная проба — `D:\BqMoni_Claude\fixfsa\FixFsaProbe.cs`
(вне дерева: сторожу не нужна), один исходник собран против сборки ДО правки
(`bin\Debug_fixfsa_before`, дерево на `f4abe6c8` + незакоммиченное соседей) и ПОСЛЕ
(`bin\Debug_fixfsa`); новое читается отражением. Спектр — `AS80_Cs137_0cm.xml` корпуса (часть
known, кривая `AS80_point0`, матрицы в стенде пробы нет).

## AMBER180 — отпечаток разбора без живого времени

**Было.** `FsaAnalysisSession.BuildStamp` нёс `MeasurementTime`, но не `LiveTime`; разбор делит на
`EffectiveLiveTime` (`FsaAnalyzer.cs` ~9639) и по `LiveTime` выбирает убыль наложений
(`PileUpLossFor`, `PileUpCapFor`). «Применить поправку на мёртвое время» меняет только `LiveTime` →
`EnsureUpToDate` молчал. Обход всего, что снимок (`Capture`) отдаёт анализатору и выводу состава,
нашёл ещё семь входов вне отпечатка: `Min_Range`/`Max_Range` (`analyzer.MinEnergy/MaxEnergy` и
`FsaSampleSpec.OfSpectrum`), `Min_SNR` (порог ожидаемых линий `FsaCompositionInference.Score` —
прямо, а не только через список пиков: оговорка перепроверки «добавлять не надо» уже реальности),
у фона — живое и полное время (`backgroundScale`), число каналов и собственная шкала (перекладка
`RebinBackgroundToSpectrum`), прибор — кривизна тракта (`AdoptDevice`), мёртвое время
(`CoincidenceWindowSec`), вещество кристалла; содержание определений нуклидов (энергия и выход
линии — путь по пикам `FsaLibrary.BuildFromPeaks` берёт линии из всех определений).

**Сделано** (`BecquerelMonitor/FullSpectrumAnalysis/FsaAnalysisSession.cs`): в `BuildStamp` — `LiveTime`
("R"), `BackgroundStamp` (отсчёты, каналы, `LiveTime`, `MeasurementTime`, шкала фона тремя точками
`CalibrationStamp`), `PeakConfigStamp` (`Min_Range`, `Max_Range`, `Min_SNR`), `DeviceStamp`
(`TractCurvature`, `DeadTimeOf`, `CrystalMaterialName`); в `NuclideSetStamp` — свёртка
`DefinitionsHash` (имя, энергия, выход всех определений; и без активного набора). Новые части стоят
ДО строки настроек, хвост отпечатка (`FsaSessionProbe.Tail`) прежний.

Попутно (тот же обход): снимок вывода состава `CompositionInput` не нёс прибора, и путь «из баз»
у кривой без геометрии терял второй источник долей кристалла (`A276`, `CrystalMaterialName`) —
теперь едет минимальная копия прибора с одним этим полем (`DeviceInput`).

**Замер** (`stamp_before.txt` / `stamp_after.txt`, `PileUp` выключен ради чистого отношения):

| шаг | до правки | после |
|---|---|---|
| первый разбор → запусков | 1 | 1 |
| контроль: повтор без смены → запусков | 1 | 1 |
| `LiveTime` 10192.296 → 9184.5 с: отпечаток | тот же | сменился |
| запусков после смены | 1 (пересчёта нет) | 2 |
| Cs-137, имп/с | 5457.2473 → 5457.2473 | 5457.2473 → 6056.0596 |
| отношение имп/с / отношение живых времён | 1.000000 / 1.109728 | 1.109728 / 1.109728 |

Прочие входы, только отпечаток (до → после): `Min_Range`, `Max_Range`, `Min_SNR`, фон `LiveTime`,
фон `MeasurementTime`, шкала фона, кривизна тракта, вещество кристалла, выход линии определения —
все девять «тот же» → «сменился»; контроль «два снятия подряд — один отпечаток» и «после всех
возвратов отпечаток прежний» — зелёные в обеих сборках. Итог пробы: до — «НЕ СОШЛОСЬ 12», после —
«сошлось».

## AMBER199 — отказ чтения базы кэшировался на весь процесс и не доходил до человека

**Было.** Читатели `nucdb`/`schemedb`/`matdb` клали ответ отказа в статический кэш; признаки
(`FsaCascadeSummer.Failure`, записки отчёта сборки) вне классов не читал никто.

**Сделано.**
* `FsaSampleLibrary.cs` — новый класс `FsaDatabaseFailures` (в конце файла: `.csproj` не мой):
  `Note(база, ключ, исключение)` — счётчик потока и процесса, журнал трассировки, собиратель потока
  `Begin`/`End`. Читатель берёт счётчик потока ДО чтения и пишет кэш, только если он не вырос (ловит
  и отказ вложенного читателя). Места: `EquilibriumFactors` (`FactorCache`), `EquilibriumMembers`
  (`EquilibriumCache`), `ChainBranches` (`ChainCache`), `ChainDepths` (`DepthCache`), `DecayLines`
  (`LineCache`), `KnownNucids` (справочник не запоминается), `PrimordialAncestors` (`AncestorCache`),
  оба перехвата помех спутника (`AMBER118`) — назван отказ.
* `FsaCascadeSummer.cs` — `BaseData`: `null` и отказ вложенного (схемы, изомеры дочки) в `Cache` не
  кладутся; `Note` у `Load`, `AttachDaughterIsomers`, `DaughterNames`, кривой света (`matdb`);
  `DatabasePresent` запоминает только «файл есть».
* `CascadeAtomicData.cs` — `Of`/`Nuclear`: `Failed` и отказ вложенного — не в кэш (+`Note`);
  `IccGrid.Load` (`matdb`) — отказ не запоминается; `EnsdfWalk.Load` — `Note`, ленивое `Walk` не
  ставит «прочитано»; `LevelScheme.Of` — `null` не кэшируется, `Read` — `Note`.
* `AngularCorrelation.cs` — `SchemeOf`: отказ чтения не кэшируется (отрицательный ответ «нуклида
  нет» — по-прежнему), `Load` — `Note`.
* `FsaResult.cs` — `DatabaseFailures`. `FsaAnalysisSession.Compute` открывает собиратель на весь
  фоновый счёт; при результате — в `DatabaseFailures`, без результата — приписка
  `FSADatabaseFailed` к причине (`DatabaseFailureText`).
* `FSAReportView.cs` (`MakeQualityRows`, после строки расхождений поставок) — строка-происшествие
  `FSAReportDatabaseFailedRow`, справа первая строка отказа и «(+N)», в подсказке все.
* `Properties/Resources.resx`, `Resources.ru.resx`, `Resources.Designer.cs` — `FSADatabaseFailed`,
  `FSAReportDatabaseFailedRow`.

⚠ `KSeriesRule.Groups` (`matdb.fluorescence_k`) НЕ исправлен: `KSeriesRule.cs` входит в набор
генератора корпуса (`corpus_stamp.py`), правка краснит `check_corpus_generator` (клеймо T244),
а лечится это пересборкой корпуса по разрешению Amber. Правка проверена и откачена; остаток — в
отчёте полосы.

**Замер** (положительный контроль в каждом режиме: проба держит файл базы `FileShare.None` и
сама проверяет, что второе открытие отказано — «замок держится»):

* `lib` — nucdb занята в миг первой сборки, `Build({137CS})`: до — под замком 0 линий, после снятия
  замка 0 линий (отказ закэширован); после — 0 линий под замком (отказов в собирателе 2), 5 линий
  после снятия.
* `dblookups` — состав «из баз», nucdb занята в миг первого разбора, затем снята и пересчёт: до —
  оба разбора `Xray-NaI` один, χ²/ndf 48.14, в окне ни слова; после — под замком строка окна
  «Базы ядерных данных не прочитаны… | nucdb.sqlite (nuclides): SQLite Error 14: 'unable to open
  database file'. (+5)», после снятия — `K-40 100.72`, `Cs-137 5294.57`, χ²/ndf 3.92, строки нет.
* `session` — путь по пикам, schemedb занята: до — в окне ни слова; после — строка окна
  («nucdb.sqlite (152EU) … (+3)»: открытие nucdb с присоединённой схемой тоже отказывает), после
  снятия — строки нет. Числа этой сцены от schemedb не зависят (матрицы в стенде нет — суммирования
  нет): разбор под замком, после снятия и эталон свежего процесса совпали (Cs-137 5451.6210,
  χ²/ndf 3.866981267992522).

Корпусные числа не сдвинулись: `tools/check_fsa_showcase.py --probes=tools/effmaker/probes/build_fixfsa`
— 9 пар из 9 совпали с эталоном; `tools/check_corpus_scenes.py` тем же каталогом — 51 из 51.
Ресурсы: `check_resx`, `check_resx_designer`, `check_resx_letters`, `check_resx_zorder` — код 0.
`tools/check_fsa_report_view.py --probes=…build_fixfsa` — код 0 («ВСЕ СОШЛИСЬ», 326 ok, 0 ⛔);
`check_corpus_generator` — код 0 (после отката `KSeriesRule.cs`). Каталог `build_fixfsa` собран
из дерева с незакоммиченными правками соседних полос; штатный `build` протух от них же (код 3 у
трёх сторожей `check_all` — не от этой полосы), `check_registry` красен ссылками реестра на
незакоммиченные журналы соседей.

Проба и её выводы (`FixFsaProbe.cs`, `stamp_*.txt`, `lib_*.txt`, `session_*.txt`, `ref_*.txt`,
`dblookups_*.txt`) — на диске в `D:\BqMoni_Claude\fixfsa\shots\`, не в git.

Экраном не проверялось: строка окна проверена пробой на настоящем `FSAReportView` (таблица
`ReportTable`, `Tag` строк), стол общий.

## Остаток

* `KSeriesRule.Groups` — отказ чтения `matdb` по-прежнему запоминается (см. выше).
* Разбор, опубликованный с отказом базы, сам не перезапускается: отпечаток прежний, и повтор идёт
  при следующем пересчёте (любая смена входа, флажка, набора). Строка окна так и говорит.
