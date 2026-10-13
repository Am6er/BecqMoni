# Полоса правки «fixconn» — остаток `AMBER201`: строка подключения SQLite склейкой пути, 05.10.2026

Строка `AMBER201` (разбор — [app-bug-review-2026-10-05.md](app-bug-review-2026-10-05.md)), находка полосы
fix201ef ([app-fix-2026-10-05-fix201ef.md](app-fix-2026-10-05-fix201ef.md) §Находки): после того как
читатели `matdb.sqlite` в `EfficiencyMaker/*` перешли на построитель `MaterialDatabase.ReadOnlyConnection`,
та же склейка `"Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"` оставалась в восьми местах вне
полосы. Работа в основном дереве, без коммита.

## Было

Путь каталога программы вклеивался в строку подключения как есть. Каталог с `;` в имени разрезал
строку: поставщик брал хвост пути за ключ и отказывал (`Connection string keyword '…;mode' is not
supported`). Из такого каталога не открывались `nucdb.sqlite` (редактор нуклидов, библиотека FSA,
каскады), `schemedb.sqlite` (угловые корреляции) и таблица `matdb.fluorescence_k` (правило K-серии).

## Сделано

Все восемь мест — через `EfficiencyMaker.MaterialDatabase.ReadOnlyConnection(path, true)`
(`SqliteConnectionStringBuilder`: `DataSource`, `Mode = ReadOnly`, `Cache = Shared` — тот же режим,
что был у склейки; пишущих среди них нет, `NucBase/DataBase.cs` тоже открывал только чтение):

| место | база |
|---|---|
| `BecquerelMonitor/FullSpectrumAnalysis/AngularCorrelation.cs:626` (`Load`) | `schemedb` |
| `BecquerelMonitor/FullSpectrumAnalysis/CascadeAtomicData.cs:3002` (`OpenRead`) | `nucdb`, `matdb` |
| `BecquerelMonitor/FullSpectrumAnalysis/FsaCascadeSummer.cs:3976` (`Load`), `:4261` (`AttachDaughterIsomers`), `:4908` (`DaughterNames`) | `nucdb` |
| `BecquerelMonitor/FullSpectrumAnalysis/FsaSampleLibrary.cs:4663` (`OpenRead`) | `nucdb` |
| `BecquerelMonitor/FullSpectrumAnalysis/KSeriesRule.cs:254` (`Groups`) | `matdb` |
| `BecquerelMonitor/NucBase/DataBase.cs:65` (`CreateConnection`, внутри прежнего `try`) | `nucdb` |

`MaterialDatabase.cs` не тронут (перегрузка не понадобилась). После правки `"Data Source="` в
`BecquerelMonitor/**/*.cs` осталось только в описании самого помощника.

## Замер (положительный контроль)

Сборки `bin\Debug_fixconn_before` (дерево до правки, с незакоммиченным соседей) и `bin\Debug_fixconn`.
Каждая скопирована в обычный каталог и в каталог с `;` в имени: `D:\BqMoni_Claude\fixconn\plain_*` и
`D:\BqMoni_Claude\fixconn\a;b_*`.

**Проба `ConnProbe.cs`** (разовая, вне дерева, `D:\BqMoni_Claude\fixconn\shots\`; отражением) зовёт всех читателей: конструктор
`NucBase.DataBase`, `AngularCorrelation.SchemeOf(28, 60)`, `CascadeAtomicData.OpenRead(nucdb)`,
`FsaSampleLibrary.OpenRead(nucdb)`, `KSeriesRule.Groups()`, `FsaCascadeSummer.BaseData("207BI")`
(три читателя сразу) — и печатает отпечаток прочитанного:

| каталог | открылось | собиратель `FsaDatabaseFailures` | отпечаток |
|---|---|---|---|
| обычный, до | 6 из 6 | 0 | `796922202C30DB04` |
| обычный, после | 6 из 6 | 0 | `796922202C30DB04` |
| `a;b`, до | **0 из 6** | 3 | `7541150527C21E85` |
| `a;b`, после | 6 из 6 | 0 | `796922202C30DB04` |

Отказ «до» — дословно `ArgumentException: Connection string keyword 'b_before\nucdb.sqlite;mode' is
not supported` (у `schemedb` и `matdb` — то же со своим именем); `DataBase` — словами
`ERRNucBaseOpenDatabase` с путём; `SchemeOf` бросал `ArgumentException` наружу (ловит только
`SqliteException`), `BaseData` — `null`, групп K — 0. Числа «после»: схема Ni-60 — 728 переходов,
255 уровней; групп K — 87; Bi-207 — 1 дочерний, 5 линий, 4 пары; в `nucdb` 19 таблиц.

**Разбор FSA на спектре** (`FsaStackShot` из рабочего каталога витрины, собранного на
`build_fixconn`; спектр `AS80_Th232_disk`, `--infer --set=Th-232`, ключи витрины):

| каталог | код | `curves.csv` | `rates.csv` | строк `ROW` |
|---|---|---|---|---|
| обычный, до | 0 | `8A093A3BEE9F9744` | `E5F9D7888C8A34B0` | 11 |
| обычный, после | 0 | `8A093A3BEE9F9744` | `E5F9D7888C8A34B0` | 11 |
| `a;b`, до | 0 | `212DA875ECD7DD53` | `39780394D560E6EF` | 4 |
| `a;b`, после | 0 | `8A093A3BEE9F9744` | `E5F9D7888C8A34B0` | 11 |

«До» из `a;b` разбор молча (код 0) теряет ряд тория: в строке состава «ряд 232TH: отказ базы —
Connection string keyword 'b_before\nucdb.sqlite;mode' is not supported», остаются рентген и
континуум. «После» лог из `a;b` совпал с обычным с точностью до пути. Контроль — обычный каталог до
и после побитово.

## Переклеймовка генератора корпуса

`KSeriesRule.cs` входит в набор генератора (`corpus_stamp.READS`), поэтому после правки
`check_corpus_generator.py` дал 1 («изменён KSeriesRule.cs, в клейме 9a974dbc5f4e, в дереве
f47f043d8152»). Сделано так же, как у полосы fixkser ([app-fix-2026-10-05-fixkser.md](app-fix-2026-10-05-fixkser.md)):

* `chains.KSERIES` с исходником до правки и дерева — `{AlphaMatchKev 0.03, GroupTolerance 0.006,
  MatchTolerance 1e-06}`; `chains.k_series_rule` по всем 1777 родителям `nucdb` с K-рентгеном
  (92519 строк) — sha256 выхода `627a9e63…` в обоих; положительный контроль — копия с
  `GroupTolerance = 0.0001` даёт `3912824b…` (скрипт `kser_ab.py` полосы fixkser, копия — `D:\BqMoni_Claude\fixconn\shots\`).
* Клеймо `tools/CORPUS/corpus/generator.json` переписано скриптом `D:\BqMoni_Claude\fixconn\shots\restamp.py`
  (тот же, что у fixkser: отказ, если в наборе изменилось что-то кроме `KSeriesRule.cs`; плюс
  сохраняет переводы строк файла — CRLF): отпечаток файла `9a974dbc5f4e` → `f47f043d8152`, свёртка
  `54628c97…` → `805bb71a…`, `head` (`aefe9401`) прежний, в `restamp` дописана причина. Корпус, склад,
  базы не тронуты.
* `check_corpus_generator.py`: до правки — 0, после правки до переклеймовки — 1, после — 0
  («СОШЛОСЬ … 14 файлов», сводка побайтно); `--selftest` — 0.

## Пробы с той же склейкой (только перечень, не правились)

`tools/effmaker/probes/`: `AngularProbe.cs:594`, `CascadeClampProbe.cs:451`, `CascadePairProbe.cs:252`,
`CascadeYieldGuardProbe.cs:53`, `ChainRuleProbeF58.cs:415`, `CoincCfProbe.cs:91`,
`DecayReadersProbe.cs:314, 412, 478`, `EstarPotentialProbe.cs:167`, `FsaBetaPlusShareProbe.cs:248`,
`NucidProbe.cs:100`, `PeakOriginProbe.cs:770` — 13 мест в 11 файлах. Пробы запускаются из каталогов
оснастки без `;` в имени; приложения не касается.

## Приёмка

Сборка `bin\Debug_fixconn` — код 0, предупреждений в файлах полосы нет. Каталог проб
`tools\effmaker\probes\build_fixconn` (`build_all.ps1` — код 0). На нём: `check_fsa_showcase` — 0
(«ВИТРИНА СОШЛАСЬ С ЭТАЛОНОМ: 9 пар»), `check_corpus_scenes` — 0 («сцен 51: сошлось 51»), `check_fsa_report_view` —
0 (326 строк ok, 0 ⛔, «ВСЕ СОШЛИСЬ»); `check_matdb_fingerprint` — 0; `check_corpus_generator` — 0. `check_all`: 40 из 44; красны `check_fsa_report_view`, `check_corpus_scenes`, `check_fsa_showcase` (код 3 — штатный `probes\build` протух от незакоммиченных правок всей волны; на `build_fixconn` все три зелёные) и `check_registry` (код 1, ссылки реестра на незакоммиченные журналы волны; эту полосу не называет) — не от этой полосы.

Выводы пробы и сторожей (`probe_*.txt`, `fsa_*.log`, `ab_*.txt`, `gen_*.txt`, `showcase.txt`, `scenes.txt`,
`report_view.txt`, `check_all.txt`) — на диске в `D:\BqMoni_Claude\fixconn\shots\`, не в git.
