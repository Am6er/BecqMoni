# Полоса П5 (12.09.2026): `AMBER19` — гейт «корпус только по нуклидам из базы»

Строка `AMBER19` (P1, задача Amber 12.09.2026, консоль, дословно): «Проверь, при
прогонке корпуса используются ли нуклиды из базы? Нужно так: берётся список
известных нуклидов в этом спектре и дёргается всё из базы. Никаких поставочных
конфигов из приложения. Если надо — сделай это правило гейтом прогонки.»

Решение Amber 12.09.2026 (вопросником, дословно): **«Оставить»** — про второй
поставочный файл `BecquerelMonitor.xml`: правило о нуклидах, файл остаётся, полоса
доказывает замером, что числа корпуса от него не зависят (замер §5.3).

Worktree `C:\Users\moroz\bqa19` от `2a47f483`, сборка `bin\Debug_P5`, пробы
`probes\build_p5`, оснастка `scripts\wd_p5`, склад матриц — ГЛАВНОГО дерева
(`tools\CORPUS\corpus\geometries`, только чтение). Полный корпус не гонялся.

## 1. Снимок на старте (sha256 ГЛАВНЫХ версий моих файлов)

```
94C8D31729978B2D2A8C0CFFD94CD5F093F394F5E72E51A23094E1B61839FBB0  BecquerelMonitor/PeakDetector.cs
0DCD563F0C1B77E8F043F0B8545946855511694D5974905633AE6134752C308B  BecquerelMonitor/NuclideDefinitionManager.cs
A245088B4D87CCD075D96A65F5A4C8D27873D7B72E08C2003FF13D0EB635C179  tools/effmaker/probes/CorpusFsaProbe.cs   (незакоммиченная версия П3, --matrix-transfer=)
FFE0E3F7B150BD33F3A51F7E211E527F3EF5C50FAA522ED731F0611E86981055  tools/CORPUS/scripts/appwd_plan.ps1
DCD1F31BE31C6D25A4553CAF4AEA398DB939E6B6C9B03610CE9945794151D63E  tools/CORPUS/scripts/mk_appwd.ps1
26BB2848986C36EAD5E85BFB8930128758B4EB2C0C28FF7A83067430D2975302  tools/CORPUS/scripts/check_appwd.ps1
E600FE92E907570E1569FE7E426D0EDBB4CD26CA408B0526E93582AE5AE54828  tools/CORPUS/scripts/run_appwd.ps1
0EA4937C5A374ACA55A4FA23E8E58A6059377838518C7AFA9E7F3BFCCA438136  tools/check_corpus_library.py
BE4A5B72F6819A2A2CB6A6B52CC08D537663B6F73CA56F37D7F2AB058DF05296  tools/CORPUS/README.md
```

Из них только `CorpusFsaProbe.cs` в главном дереве был незакоммичен (П3); он
скопирован в worktree первым движением, и все правки легли поверх версии П3.

## 2. Сверка посылки с деревом — ДО правки

Подтвердилось:

* `PeakDetector.cs:1223` — `NuclideDefinitionManager nuclideManager =
  NuclideDefinitionManager.GetInstance();` инициализатором поля: каждый `new
  PeakDetector()` поднимал менеджер, и файл был обязателен. Комментарий пробы
  «менеджер больше не поднимается вовсе» (`CorpusFsaProbe.cs:99`, `:771`) — неверен.
* `appwd_plan.ps1` шаг 4 клал `NuclideDefinition.xml` в оснастку как «поставочный
  конфиг»; `Test-AppWdLibrary` без файла отказывал.
* `Package.NuclideDefinition` = `config\NuclideDefinition.xml` ОТ КАТАЛОГА СБОРКИ
  (`S102`), не от текущего; в штатной оснастке они совпадают (`run_appwd.ps1` зовёт
  `wd_app\CorpusFsaProbe.exe` из `wd_app`). Гейт проверяет ОБА пути.
* Потребители `NuclideDefinitionManager` на корпусном пути после `PeakDetector`: нет.
  `FsaAnalysisSession.cs:485/881` (окно) и `NucBase.cs:986` (форма) пробой не
  зовутся; `FullSpectrumAnalysis/*` других обращений не содержит.

⛔ РАСХОЖДЕНИЯ посылки (числом):

1. **«`GetInstance` без файла ПИШЕТ четырёхзаписную заготовку (`:180`)» — неверно для
   корпуса.** Заготовка пишется ТОЛЬКО В ОКНАХ; безоконный вызов без файла БРОСАЕТ
   `InvalidOperationException` (`NuclideDefinitionManager.cs:167–175`, решение
   `S100` 27.08.2026) и на диск ничего не пишет. Значит появление файла от самой
   пробы невозможно, а «поймать подъём ДО того, как файл появится» сводится к
   «поймать попытку подъёма». Обе стороны закрыты: файл — первая дверь на старте,
   попытка — вторая дверь в конце (§3в).
2. **«Отражением по статическому экземпляру» — недостаточно.** У экземпляра есть
   только `isLoaded`, и после броска он остаётся `false`; а `RunOne` глотает ЛЮБОЕ
   исключение в `row.Error` (`CorpusFsaProbe.cs:2167`). То есть чужой подъём
   менеджера посреди прогона без файла по `isLoaded` был бы НЕВИДИМ. Измерено
   порчей `--spoil=manager`: подъём кончился `InvalidOperationException`,
   проглочен, `isLoaded` = false — и только счётчик `RaiseCount` = 1 дал код 12.
   Поэтому в `NuclideDefinitionManager` добавлен статический счётчик обращений
   (задача это допускала: «если без него нельзя» — нельзя, показано выше).
3. **Пробы без явного списка** (перебор всех 36 вызовов `DetectPeak(` дерева
   скриптом, аргументы считаны по скобкам): с явным списком 35; без списка ровно
   ДВА вызова — `tools/CORPUS/probes/PeakFinderProbe.cs:114–115`. Эта проба
   поднимает менеджер ЯВНО (`:38`), берёт `<workdir>` = `wd_<группа>` от
   `mkconfig.py` (библиотека из `nucdb`, не поставочная), `build_all.ps1` её не
   собирает, в конвейер (`run_mini` → `run_appwd` → `CorpusFsaProbe`) не входит.
   После (а) поведение НЕ изменилось: менеджер к `DetectPeak` уже поднят, ленивая
   ветвь отдаёт тот же список. `LabelTruthProbe.cs:626/841/949` и
   `XrayLabelProbeF53.cs:398` делают `new PeakDetector()` БЕЗ `DetectPeak` и
   подкладывают `nuclideDefinitions` отражением — поле осталось, менеджер они
   поднимают сами (`:129`, `:180`); дымовой прогон `LabelTruthProbe --selftest` из
   `build_p5` — код 0.
4. В оснастке корпуса файлов стало 441 (П3: 442) — разница ровно один
   `NuclideDefinition.xml`.

## 3. Сделано

**(а) `BecquerelMonitor/PeakDetector.cs`** — инициализатор поля снят; менеджер
поднимается ЛЕНИВО методом `SuppliedDefinitions()` и только когда явного списка не
дали (`nuclideDefinitions ?? this.SuppliedDefinitions()`). Поведение приложения не
меняется: единственный оконный вызов `DCPeakDetectionView.cs:196` подаёт снимок
списка явно.

**(а′) `BecquerelMonitor/NuclideDefinitionManager.cs`** — статический счётчик
`RaiseCount` (инкремент ПЕРВЫМ действием `GetInstance`, до попытки чтения; сбросить
нельзя). Только признак, как разрешено строкой.

**(б) `tools/CORPUS/scripts/appwd_plan.ps1`** — шаг 4: `NuclideDefinition.xml`
кладётся ТОЛЬКО каталогу проб (`-ProbeCatalog`; там десятки проб приложения читают
библиотеку по праву, и `build_all.ps1` там же гонит `--band-selftest`), оснастке
корпуса — только `BecquerelMonitor.xml`. План несёт признак `ProbeCatalog`;
`Test-AppWdLibrary` по нему ПЕРЕВЁРНУТ: оснастка корпуса — файл ОБЯЗАН
ОТСУТСТВОВАТЬ (есть — отказ с отпечатком и именем копии), каталог проб — прежняя
проверка (есть, ≥ 100 записей). Сторож печатает словами «библиотека: в оснастке
корпуса НЕТ по правилу AMBER19». `Remove-AppWdExtra` (`mk_appwd.ps1`) выносит файл
из прежних оснасток как постороннее загружаемое — второго списка нет. Шапки
`mk_appwd.ps1`, `run_appwd.ps1`, `check_appwd.ps1` поправлены там, где стояла
ложная посылка «без файла прогон возьмёт заготовку».

**(в) `tools/effmaker/probes/CorpusFsaProbe.cs`** — в `SuppliedLibraryGuard` две
двери кодом 12: `RefuseIfSuppliedFile()` на старте (после `--print-settings` и
`--band-selftest`, до чтения `parts.csv`; проверяет путь менеджера
`Package.NuclideDefinition` И `config\NuclideDefinition.xml` от текущего каталога;
в `--out=` ни файла) и `RefuseIfManagerRaised()` в конце, ДО `Write(rows)` (по
`RaiseCount`; результат не пишется). Ключ порчи `--spoil=manager` поднимает менеджер
ОТРАЖЕНИЕМ (текстовый сторож не должен видеть в контроле нарушения; ловит гейт
времени исполнения). Шапка прогона называет гейт. Заодно `{0:n0}` → `{0:f0}` в строке
«итог по частям корпуса» — под инвариантной культурой `n0` группировал разряды
запятой против правила Amber 05.09.2026 (свой файл, читателей у строки в оснастке
нет — проверено `grep` по `tools/**/*.py`).

**(г) `tools/check_corpus_library.py`** — судит и `BecquerelMonitor/PeakDetector.cs`
особым правилом: `GetInstance` на глубине скобок ≤ 2 (тело класса = инициализатор
поля) или внутри члена с заголовком `PeakDetector(` (конструктор) — отказ; внутри
метода — тишина. `--selftest` трёхсторонний: инициализатор 1, конструктор 1,
ленивый метод 0 — сошлось; и на НАСТОЯЩЕМ старом файле (`git show
HEAD:BecquerelMonitor/PeakDetector.cs`) — 1 находка, `:1223`.

**`tools/CORPUS/README.md`** — абзац о гейте в разделе «Полноспектральный разбор
корпуса кодом ПРИЛОЖЕНИЯ»; `check_corpus_readme.py` код 0.

## 4. Сборка

* MSBuild `Debug_P5` — код 0. `build_all.ps1 -Out build_p5` — код 0, 173 пробы, каталог
  заверен (`T226`), самопроверка сторожа полосы и разметки прошла; каталогу проб
  библиотека кладётся по-прежнему («библиотека нуклидов: 152 записей, sha
  7aaa0b01c9bd (поставочная)»).
* `mk_appwd.ps1 -Wd wd_p5 -Store <склад главного дерева>` — код 0, положено 441,
  «ОСНАСТКА СВЕЖАЯ: 441 файлов сошлись», «перекладка: клейма склада и оснастки
  сошлись», `config\NuclideDefinition.xml` в оснастке — нет.

## 5. Приёмка

### 5.1. Малая база без файла = `out_rev17_mini` строка в строку

`run_mini.ps1 -Out tools\pie\out_p5_mini -Wd wd_p5 -Store <главный склад>` — код 0,
25.7 с. Шапка: «гейт библиотеки (AMBER19): поставочного config\NuclideDefinition.xml в
каталоге прогона НЕТ»; конец: «NuclideDefinitionManager за прогон не поднимался
(обращений 0)». Строки `nuclide library: …` в потоке ошибок НЕТ (раньше её писал
`LoadDefinitionFile`).

| часть | спектров | найдена / примен. | Σχ²/ndf | медиана | recall | фантомов |
|---|---|---|---|---|---|---|
| понятная (known) | 42 | 42 / 42 | 306.4 | 4.13 | 100 % | 0 |
| непонятная (unknown) | 17 (2 разобраны, 15 — «нет геометрии», `A277`) | 0 / 0 | 54.5 | 27.25 | 7 % | 0 |

Сравнение с `out_rev17_mini` ГЛАВНОГО дерева скриптом (`runs`, `components`,
`anchors`, `limits`; без колонок `ms`, `cpu_ms`): **16 файлов, 407 строк сравнено,
расхождений 0.**

### 5.2. Положительные контроли — каждый отказал

| контроль | что | результат |
|---|---|---|
| подложенный `wd_p5\config\NuclideDefinition.xml` (поставочный, 7aaa0b01c9bd) | `check_appwd.ps1` | **код 2**: «1. В ОСНАСТКЕ КОРПУСА ЛЕЖИТ config\NuclideDefinition.xml … ПРАВИЛО AMBER19», «2. ЛИШНЕЕ В ОСНАСТКЕ» |
| то же | `run_appwd.ps1` | **код 2**, «ПРОБА НЕ ЗАПУЩЕНА», каталога `-Out` нет |
| то же, проба напрямую из `wd_p5` (мимо сторожа) | `CorpusFsaProbe.exe` | **код 12** на старте, каталога `-Out` нет |
| `--spoil=manager` (2 спектра) | `run_appwd.ps1` | сторож зелен, проба считает оба спектра, в конце **код 12**: «поднимали 1 раз(а) … Результат в --out= НЕ ЗАПИСАН», в `-Out` 0 файлов; порча кончилась `InvalidOperationException` (`S100`) — то есть по `isLoaded` её было бы не видно |
| `check_corpus_library.py --selftest` | подлог ×3, ложная тревога 0, инициализатор 1 / конструктор 1 / ленивый 0 | **СОШЛОСЬ**, код 0; на `HEAD:PeakDetector.cs` — 1 находка `:1223` |

### 5.3. A/B поставочного `BecquerelMonitor.xml` (решение «Оставить»)

Плечо B — копия с 7 изменёнными полями: `DefaultSmoothingMothod` None →
WeightedMovingAverage, `NumberOfSMADataPoints` 6 → 12, `NumberOfWMADataPoints` 6 → 15,
`CountLimit` 10000 → 500, `Language` «» → `ru`, `ConfidenceLevel` 1.645 → 2.576,
добавлен `ProgresiveSmooth` true. 5 спектров (`G1S16_Cs137_P5`, `G1S16_Eu152_P5`,
`ASN16_Lu176`, `AS80_Th232WT20`, `G1S24_Bi207_P5`): **16 файлов, 63 строки, расхождений
0.** Положительный контроль чтения: проба печатает «main config:
…\wd_p5\config\BecquerelMonitor.xml», а битый файл роняет её
`InvalidOperationException` (код −532462766) — файл читается именно оттуда. Конфиг
восстановлен, sha сошлась с поставочным.

### 5.4. `check_all.py` в worktree

Код 1: **31 из 34 зелены**, красны три — все по устройству worktree, все три в
ГЛАВНОМ дереве код 0 (проверено там же, только чтение):

* `check_corpus_coverage.py` — `gaussfit_check.py` код 3: «124 спектров не посчитано» —
  в worktree нет сырья корпуса;
* `check_declared_base.py` — «каталог tools\pie\out_rev17_full … не найден» — не в git;
* `check_fsa_view_groups.py` — «порчу подставить не удалось»: самопроверка ищет строку
  с `\n`, а `FSAReportView.Designer.cs` выписан в worktree с CRLF (`core.autocrlf=true`;
  в главном дереве файл LF). Факт в журнал; строки не завожу — ловушка только
  worktree-выписки, чисел не портит.

`check_corpus_library.py`, `check_corpus_readme.py`, `check_headless.py`,
`check_probe_numbers.py` — код 0.

## 6. Перенос в главное дерево

Порядок по заданию: sha256 главной версии = снимку старта → копия; иначе — не
копировать, назвать. Главное дерево держит четыре моих файла с LF в рабочей копии
(`mk_appwd.ps1`, `check_appwd.ps1`, `run_appwd.ps1`, `check_corpus_library.py`; индекс
LF у всех, `autocrlf=true`) — переносятся с концами строк ГЛАВНОЙ копии, побайтно
проверено. Итог переноса — в отчёте полосы (§7 ниже дописывается после копирования).

## 7. Находки — по порядку правил

1. **Починено на месте** (свои файлы): `{0:n0}` в строке итога `CorpusFsaProbe`;
   комментарий пробы «менеджер не поднимается вовсе»; ложная посылка «без файла —
   заготовка» в трёх шапках оснастки.
2. **Факт — в журнал** (строки не завожу): расхождения посылки §2; CRLF-ловушка
   `check_fsa_view_groups.py` в worktree; `PeakFinderProbe.cs` как единственная проба
   без списка — вне конвейера, без изменений.
3. **Новых строк: 0.**

### 6.1. Итог переноса (12.09.2026, после приёмки)

Все девять главных версий сошлись со снимком старта — параллельных правок не было;
скопированы все девять, журнал — новым файлом. sha256 (первые 16) после переноса:

```
5CB66518C9F64E52  BecquerelMonitor/PeakDetector.cs              (CRLF, как было)
31C2C5A23903595B  BecquerelMonitor/NuclideDefinitionManager.cs  (CRLF, BOM)
2BE1D8060E0712CD  tools/effmaker/probes/CorpusFsaProbe.cs       (CRLF, BOM; поверх версии П3)
5D4F117735158231  tools/CORPUS/scripts/appwd_plan.ps1           (CRLF, BOM)
1791CC258C26507F  tools/CORPUS/scripts/mk_appwd.ps1             (LF, BOM — как в главной копии)
867F438DC26F8FA6  tools/CORPUS/scripts/check_appwd.ps1          (LF, без BOM — как в главной копии)
1B826D6B8C569901  tools/CORPUS/scripts/run_appwd.ps1            (LF, BOM — как в главной копии)
29665B1E77042F16  tools/check_corpus_library.py                 (LF, без BOM)
11E7A9AC1EDBA707  tools/CORPUS/README.md                        (CRLF, BOM)
BE414C4065AECD9D  handover/handover-2026-09-12-p5-nuclide-gate.md (новый, LF)
```

В главном дереве после переноса: `check_corpus_library.py` код 0, `--selftest`
СОШЛОСЬ, `check_corpus_readme.py` код 0, **`check_all.py` — ВСЕ ЗЕЛЕНЫ: 34 из 34**
(с незакоммиченными правками соседних полос как есть).

⚠ **Следующий прогон в главном дереве — сперва `mk_appwd.ps1`.** Оснастка `wd_app`
главного дерева (и все `wd_*` прежних заходов: 95 из 124 каталогов) ещё держит
`config\NuclideDefinition.xml`; по новому правилу сторож на нём ОТКАЖЕТ (код 2), а
пересборка оснастки вынесет файл сама (`Remove-AppWdExtra`). Пересборка и так
нужна: `PeakDetector.cs` и `CorpusFsaProbe.cs` изменились, сторож `T41` без свежей
сборки приложения и `build_all.ps1` откажет тоже.
