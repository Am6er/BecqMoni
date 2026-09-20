# П81, 15.09.2026 — две задачи Amber из консоли: раздел «For developers» из README в CLAUDE.md; каталог `LSRM Geometries/` снят

> 🔨 **Нашли проблему — строкой в [`TODO.md`](../TODO.md) в корне.** Сюда — подробности.

Полоса П81, ветка `master`, HEAD на старте `6aa928b2` (merge `pie` → `master`); в дереве
незакоммиченные правки полосы «amber-todo» (П77–П80, в индексе — «коммитить нельзя»). Обе
задачи разобраны скиллом `analyst` (правило 4 `CLAUDE.md`), вопросы заданы вопросником ДО
работы. `git add`/`commit` не делались (правки этой полосы застейджены поимённо — см. §5).
Временное — `D:\BqMoni_Claude\p81\` (выводы проб). Сборочный каталог проб `build_p81` снят
после приёмки.

## 1. Задача 1 — «Из README.md перенести: For developers / Разработчикам в CLAUDE.md»

Постановка Amber 15.09.2026, консоль, дословно: **«Из README.md перенести: For developers /
Разработчикам в CLAUDE.md»**.

Разбор до вопросов: после `---` в README три раздела для разработчиков (`## For developers`,
`## Среда: систематические грабли`, `## Где лежат базы данных`); абзац о `TODO.md` в CLAUDE.md
уже есть (раздел «🔨 EVERY PROBLEM GOES INTO `TODO.md`», полнее); CLAUDE.md в `.gitignore`
(`T2`, решение Amber 02.09.2026 «оставить как есть») — перенесённое уходит с GitHub; строк в
реестрах нет; сторожа корневой README не читают.

Решения Amber 15.09.2026, вопросником, дословно:
* объём — **«Только раздел «For developers»»**;
* таблица завещаний (последнее в ней — 17.08.2026 при подписи «читать первым делом свежее») —
  **«Обновить при переносе»**.

Сделано:
* `README.md`: раздел `## For developers / Разработчикам` (25 строк: абзац о `TODO.md` EN+RU +
  таблица шести завещаний) снят; `---` и два остальных раздела на месте. 92 → 67 строк, CRLF и
  BOM сохранены (правка байтами, `move_dev_section.py` во временном каталоге сессии).
* `CLAUDE.md`: подраздел `### Завещания — сквозные, по итогам работы; читать первым делом
  свежее` после «The other rules» — 14 строк: **8 свежих** (ревизия математики/физики среза
  merge 15.09; перепроверка AMBER П77–П80; база `out_rev24_*` П76; витрина FSA П74; единый
  счёт физики 18 П50/П51; каналы отклика S3 + выдача рецензенту; ревизия FSA 07–08.09; перенос
  TODO → DONE П52) + 6 прежних дословно. Абзац о `TODO.md` не дублируется — правило уже в
  CLAUDE.md. Все 14 путей проверены на существование скриптом (assert).

## 2. Задача 2 — «Удалить LSRM Geometries»

Постановка Amber 15.09.2026, консоль, дословно: **«Удалить LSRM Geometries»**.

Разбор до вопросов: каталог — 15 файлов в git (124 КБ): 7 моделей `.in` и 8 экспортированных
кривых ЛСРМ `.txt`; побайтовых копий в дереве нет; модели с нашими ключами `DS_CrystalBox*`
лежат копиями в `tools/effmaker/models/`; экспорты — единственные экземпляры, эталон устарел
(разбор 06.08.2026: свежий TCCFCALC выше на ×1.25–1.42). Приложение каталог не читает (два
упоминания — в комментариях). Сторожа `check_*.py` — нет. Читали по умолчанию **шесть проб**:
`DoseRateProbe`, `BoundProbeF59`, `DoseBoundProbeF65` (кривые), `LsrmGeometryImportProbe`,
`MatrixRefusalProbeP8`, `CultureProbeO14` (модели).

Решения Amber 15.09.2026, вопросником, дословно:
* восемь экспортов — **«Удалить вместе с каталогом»**;
* копии семи моделей в `tools/effmaker/models/` — **«Остаются»**.

### 2.1 Находка после вопросника — два экспорта живут в поставке теми же точками

Поставочные `config/ROI/Obsidian Marinelli 0.5.xml` и `RadiaCode Marinelli 0.5.xml` — **тот же
набор из 150 точек**, что и экспорты `Obsidian - marinelli 0.5.txt` / `RadiaCode - marinelli
0.5.txt` (сличено точка в точку скриптом `cmp_roi.py` до снятия; в `BoundProbeF59.A222.1` это
же было измерено 06.09.2026). Остальные десять ROI-конфигов точек эффективности не несут.
Поставочные — только ЧТЕНИЕ (приказ Amber 05.09.2026), и этого хватает: две пробы, которым
нужна НАСТОЯЩАЯ кривая, переведены на них.

### 2.2 Что сделано с каждой пробой

| проба | было | стало |
|---|---|---|
| `DoseBoundProbeF65` | читала экспорт Obsidian (точка 20 кэВ ε = 1471.85) | читает поставочную `config/ROI/Obsidian Marinelli 0.5.xml` (XML), та же точка; `--break=drop` жив |
| `LsrmGeometryImportProbe` (`AMBER18`, ввоз ЛСРМ с геометрией) | пара из `LSRM Geometries/` | без `--lsrm=` пара собирается ИЗ ДЕРЕВА: текст экспорта пишется в рабочий каталог из поставочной `RadiaCode Marinelli 0.5.xml` (погрешности как есть — правило `T174` отсекает ту же первую точку, 149 из 150), модель — `tools/effmaker/models/RadiaCode_Marinelli0.5.in`; ⚠ копия — БРУСОК (`DS_CrystalBox*`), у модели диаметр/высота нули — сверка §1 идёт по сторонам бруска, после XML — по стороне; `--lsrm=<каталог>` — настоящая пара |
| `DoseRateProbe` | плечо `T174` (восемь настоящих экспортов против якоря + порча настоящего файла + цена отсечения для Am-241) по умолчанию | плечо `T174` — ТОЛЬКО с `--lsrm=`; без ключа пропускается ВСЛУХ («ПЛЕЧО НЕ ГОНЯЕТСЯ…»), отказом не считается; порча читателя приложения идёт по синтетическому экспорту того же формата (150 строк, первая с погрешностью 554 %); `--sabotage=lsrm` без `--lsrm=` — отказ кодом 2 |
| `BoundProbeF59` | A222.1/A222.2 на экспортах; геометрия для A183 — первый `.in` из `LSRM Geometries\Models` | A222.1/A222.2 — только с `--lsrm=`, без ключа пропуск вслух; A222.3 (поставочная с 1471.85) и A222.4 идут всегда — `--break=bound` держится на них; геометрия — `tools\effmaker\models\Nano16Pro.in` (та же сцена) |
| `MatrixRefusalProbeP8` | первый `.in` из `LSRM Geometries\Models` | `tools\effmaker\models\Nano16Pro.in` (та же сцена) |
| `CultureProbeO14` | плечо «не сломано» по `LSRM Geometries\Models` | по `tools\effmaker\models` (14 `.in`) |

Документы: `tools/effmaker/README.md` (разбор `.in`, раздел сверки с эталонами — помечен
историей), `tools/effmaker/probes/README.md` (тёзки, каталоги `GeomEncodingProbe`, строки
запуска `effcfgprobe`/`specffprobe` — второй довод теперь `<кривая.txt>` с пояснением),
`tools/effmaker/models/README.md`, `tools/CORPUS/README.md` (§ данных), `tools/tccfcalc2/README.md`
(один путь), `CLAUDE.md` (Conventions), комментарии `GeomEncodingProbe.cs`/`RoundTrip.cs`.
**Оставлено как история** (датированные замеры, не указания): комментарии
`GeometryMaterialLibrary.cs:469`, `GeometryModel.cs:1037`, `run_tccf2.py:146`, журналы
`handover/`, `DONE.md`, `tools/effmaker/handover-response-matrix.md`. `AGENTS.md:520` (личный
файл Amber, gitignored) упоминает каталог — не правился.

### 2.3 Приёмка

Сборка приложения Debug → `bin\Debug_Codex` (код 0), `build_all.ps1` → `build_p81`: **189 проб
собрались**, каталог заверен (`T226`). Прогоны (выводы — `D:\BqMoni_Claude\p81\*.txt`):

| прогон | ждали | код |
|---|---|---|
| `DoseRateProbe` | 0 | **0** (39 проверок; плечо `T174` пропущено вслух, порча по синтетике — 149 точек из 150) |
| `DoseRateProbe --sabotage=lsrm` | 2 (нужен `--lsrm=`) | **2** |
| `DoseRateProbe --sabotage=mu` | 0 (отказ пойман) | **0** |
| `DoseBoundProbeF65` | 0 | **0** |
| `DoseBoundProbeF65 --break=drop` | 0 (отказ пойман) | **0** |
| `BoundProbeF59` | 0 | **0** |
| `BoundProbeF59 --break=bound` | 1 (отказ) | **1** |
| `LsrmGeometryImportProbe` | 0 | **0** (46 проверок; первый прогон — 2 провала на диаметре/высоте бруска, см. 2.2, исправлено) |
| `LsrmGeometryImportProbe --sabotage=badin` / `=stamp` | 0 / 0 | **0 / 0** |
| `MatrixRefusalProbeP8` | 0 | **0** (геометрия `tools\effmaker\models\Nano16Pro.in`) |
| `CultureProbeO14` | 0 | **0** |

`python tools/check_all.py` — **39/39, код 0** (после §3).

## 3. Попутно: `check_registry` краснел от CRLF рабочей копии — починен на месте

Первый `check_all` дал 38/39: `check_registry` — «ИЗВЕСТНОЕ РАСХОЖДЕНИЕ ИЗМЕНИЛОСЬ:
NuclideDefinition.xml» при пустом `git status` по `config/`. Причина измерена: сторож хешировал
БАЙТЫ рабочей копии, а после `git checkout master` она с CRLF (`core.autocrlf=true`); записанные
25.08.2026 пары отпечатков были СМЕШАННЫМИ — `NuclideDefinition.xml` от LF (= индекс git), обе
ROI от CRLF. Это не моя правка и не поставочный конфиг (он неизменён): память
`branch-pie-merged-into-master` обещала «строку на 15.09» — строки не было.

Сделано (дешевле часа, свои файлы — по правилу «находка становится строкой в последнюю
очередь»): `sha256_of` хеширует содержимое с `CRLF → LF` (= индексу git); две пары ROI в
`CONFIG_COPIES_KNOWN` перезаписаны на LF-отпечатки (`f183eeb0…/9b992698…`,
`3e3ad482…/7fc4eeee…`), пара `NuclideDefinition.xml` та же. Положительный контроль: CRLF- и
LF-варианты одного текста дают ОДИН отпечаток, файл с одним изменённым байтом — другой;
`check_registry --selftest` код 0; `check_registry` код 0 («совпадают побайтно 22, расходятся 3»).

## 4. Что осталось (не строки — факты)

* Плечо `T174` `DoseRateProbe` (восемь настоящих экспортов против якоря, снятого глазами) и
  плечи A222.1/A222.2 `BoundProbeF59` по умолчанию НЕ гоняются — только с `--lsrm=<каталог с
  экспортами>` (у Amber экспорты есть вне дерева). Пропуск печатается вслух и отказом не
  считается. Читатель приложения `ReadLsrmEfficiencyExport` судится и без них: синтетика
  того же формата + две поставочные кривые.
* `effcfgprobe`/`specffprobe` вторым доводом ждут текстовый экспорт ЛСРМ — в дереве таких
  файлов нет; README говорит, откуда взять.

## 5. Третья задача — «Удалить BecqMoni_Localization.zip»

Постановка Amber 15.09.2026, консоль, дословно: **«Удалить BecqMoni_Localization.zip»**.
Разбор: файл в корне, в git с 18.03.2023 (коммит Amber `1feede45` «Localization file»), 59 КБ;
внутри один `BecqMoni_Localization.xlsx` — сводная таблица переводов 2023 г. (лист на форму,
колонки Variable / English / …, 1332 значения). Упоминаний в дереве нет вовсе (код, скрипты,
документы, сторожа), строк в реестрах нет, не поставочный конфиг — вопросов не осталось,
вопросник не запускался. Сделано: `git rm BecqMoni_Localization.zip` (история остаётся);
`check_all` после снятия — см. ниже. Строка реестра не заводилась: сирота без читателей,
решений Amber, кроме самой постановки, нет.

## 6. Файлы полосы

Снято: `LSRM Geometries/` (15 файлов, `git rm`), `BecqMoni_Localization.zip` (`git rm`). Правки: `README.md`, `CLAUDE.md` (gitignored),
`tools/effmaker/probes/{DoseRateProbe,DoseBoundProbeF65,BoundProbeF59,LsrmGeometryImportProbe,MatrixRefusalProbeP8,CultureProbeO14,GeomEncodingProbe,RoundTrip}.cs`,
`tools/effmaker/{README,models/README,probes/README}.md`, `tools/CORPUS/README.md`,
`tools/tccfcalc2/README.md`, `tools/check_registry.py`, `TODO.md` (строки `AMBER39`, `AMBER40`),
этот журнал.
