# Полоса правки «fixkser» — остаток AMBER199: `KSeriesRule.Groups`, 05.10.2026

Строка `AMBER199`, остаток (разбор — [app-bug-review-2026-10-05.md](app-bug-review-2026-10-05.md) §7.1;
основная часть — [app-fix-2026-10-05-fixfsa.md](app-fix-2026-10-05-fixfsa.md)). Решение Amber
05.10.2026, вопросником, дословно: **«Править и переклеймить (Рекомендую)»**. Работа в основном
дереве, без коммита.

## Было

`KSeriesRule.Groups()` (`BecquerelMonitor/FullSpectrumAnalysis/KSeriesRule.cs`) при любом
исключении чтения `matdb.fluorescence_k` клал в статический `groups` пустой словарь и до
перезапуска процесса отвечал «таблицы нет»: строки группы K-M (`KpB1`) оставались на середине
текстового диапазона (`AMBER120`: у Lu-176 63.333 вместо 63.163 кэВ, над K-краем Hf). Отказ
не попадал в собиратель `FsaDatabaseFailures`, поэтому читатели выше — `FsaSampleLibrary.DecayLines`
(`LineCache`) и `CascadeAtomicData` (`Cache`) — запоминали линии без групп тоже.

## Сделано

`KSeriesRule.cs`, `catch` в `Groups()` (~строка 279): отказ чтения — `FsaDatabaseFailures.Note("matdb.sqlite",
"fluorescence_k", error)` и `return null` без записи в `groups`. Счёт отказов потока растёт, и оба
читателя выше свои кэши не пишут (их проверка `ThreadCount == failuresBefore` уже стоит после
полосы fixfsa). Нет файла `matdb.sqlite` — по-прежнему запоминается пустая таблица (это не отказ
чтения, так же как у `IccGrid.Load`). `FsaSampleLibrary.cs` не тронут.

## Замер (положительный контроль)

Проба `D:\BqMoni_Claude\fixkser\shots\KSerProbe.cs` (вне дерева: сторожу не нужна), один исходник,
отражением, против сборки ДО (`bin\Debug_fixkser_before`, дерево `f4abe6c8` + незакоммиченное
соседей) и ПОСЛЕ (`bin\Debug_fixkser`). Проба держит свою копию `matdb.sqlite` `FileShare.None`
и сама проверяет, что второе открытие отказано; зовёт `KSeriesRule.Groups()` и
`FsaSampleLibrary.DecayLines("176LU")` в собирателе, снимает замок, повторяет.

| шаг | до правки | после |
|---|---|---|
| замок держится | да | да |
| под замком: групп K / Kβ Lu-176 | 0 / 63.333 | 0 / 63.333 |
| под замком: собиратель | 0 строк (молча) | 1 — «matdb.sqlite (fluorescence_k): SQLite Error 14: 'unable to open database file'.», отказов процесса +2 |
| замок снят: групп K / Kβ Lu-176 | 0 / 63.333 (до перезапуска) | 87 / 63.163 |
| эталон свежего процесса без замка | 87 / 63.163 | 87 / 63.163 |

Итог пробы: до — «НЕ СОШЛОСЬ 3», после — «сошлось».

## Переклеймовка генератора корпуса

Что клеймится: `corpus_stamp.py` (`T244`) кладёт в `tools/CORPUS/corpus/generator.json` sha256
каждого файла набора генератора, `KSeriesRule.cs` — целиком (`READS`). Генератор же читает из
него ТОЛЬКО константы `MatchTolerance`, `GroupTolerance`, `AlphaMatchKev` и `BetaTotal`
(`chains._kseries_constants`, регэкспом) — `Groups()` питону не виден. Штатное лечение, которое
печатает сторож («`rebuild_corpus.py --from-library`»), переписывает спектры корпуса из
библиотеки Amber и требует `bg_from_spe` и склада — то есть трогает больше клейма; оно НЕ
делалось.

Доказательство, что выход генератора побитово тот же (`D:\BqMoni_Claude\fixkser\shots\kser_ab.py`):
`chains.KSERIES` до и после правки — `{AlphaMatchKev 0.03, GroupTolerance 0.006, MatchTolerance
1e-06}`; `chains.k_series_rule` по ВСЕМ 1777 родителям `nucdb` с K-рентгеном (92519 строк) с
исходником HEAD (`LFL_KSERIES_RULE_CS`) и дерева — sha256 выхода `627a9e63…` в обоих;
положительный контроль — копия с `GroupTolerance = 0.0001` даёт `3912824b…`.

Клеймо обновлено скриптом `D:\BqMoni_Claude\fixkser\shots\restamp.py`: он отказывает, если в наборе
изменилось что-то кроме `KSeriesRule.cs`; меняет отпечаток файла (`037b40a4649c` → `9a974dbc5f4e`)
и свёртку (`f5a9115e…` → `54628c97…`), `head`/`dirty` сборки корпуса (`aefe9401`) оставляет, пишет
поле `restamp` с причиной. Переводы строк — CRLF, как было. Корпус, склад, `out_rev44_*`, `out_mini`
не тронуты; прогона корпуса переклеймовка не требует.

`check_corpus_generator.py`: до правки — 0; после правки до переклеймовки — 1 («изменён
KSeriesRule.cs»); после — 0 («СОШЛОСЬ … 14 файлов», сводка побайтно); `--selftest` — 0.

## Приёмка

Каталог проб `tools\effmaker\probes\build_fixkser` (`build_all.ps1`, код 0):
`check_fsa_showcase --probes=…build_fixkser` — 9 пар из 9 совпали с эталоном;
`check_corpus_scenes --probes=…build_fixkser` — 51 из 51; `check_fsa_report_view` тем же каталогом —
код 0 («ВСЕ СОШЛИСЬ», 326 ok, 0 ⛔). `check_all`: красны `check_fsa_report_view`, `check_corpus_scenes`,
`check_fsa_showcase` (код 3 — штатный `build` протух от незакоммиченных правок всей волны) и
`check_registry` (ссылки реестра на незакоммиченные журналы волны) — не от этой полосы.

Выводы пробы и сторожей (`probe_*.txt`, `ref_*.txt`, `ab_*.txt`, `gen_*.txt`, `showcase.txt`,
`scenes.txt`, `check_all.txt`) — на диске в `D:\BqMoni_Claude\fixkser\shots\`, не в git.
