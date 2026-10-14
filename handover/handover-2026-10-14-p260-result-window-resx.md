# П260, 14.10.2026 (ночь 13→14.10): остаток П254 — ресурсы окна результата и подсветка ряда в стеке

Остаток полосы П254 (журнал `handover/handover-2026-10-14-p254-fsa-result-window.md`, коммит `a74a07d6`):
находки 7 и 8 (строки ресурсов окна Measurement Result) и находка 9 (подсветка строки связанного ряда в
стеке FSA). Сборка полосы — `bin\Debug_p260`, пробы — `build_p260`; конфиг Amber — КОПИЕЙ из
`%AppData%\BecqMoni\config` (только чтение) в `D:\BqMoni_Claude\p260\{before,after}\config`, спектры
`Th232_29.07.2022.xml` и `Lu176.xml` — копиями в `D:\BqMoni_Claude\p260\spectra`.

## 1. Ресурсы `DCResultView.resx` / `.ru.resx`

| ключ | en | ru |
|---|---|---|
| `DCResult_FsaOriginSimulation` (новый) | simulation | расчёт |
| `DCResult_FsaOriginLsrm` (новый) | LSRM | ЛСРМ |
| `DCResult_FsaOriginMeasurement` (новый) | measurement | измерение |
| `DCResult_FsaOriginManual` (новый) | manual | вручную |
| `DCResult_FsaScaleCurve` | без изменений: `FSA: Bq by {0} curve` | `FSA: Бк по кривой {0}` → `FSA: Бк по кривой ({0})` |
| `DCResult_ColumnSamplingTip` | «ROIs without a half-life…» → «Rows without a half-life…» | «Зоны без периода…» → «Строки без периода…» |

Читатель — `DCResultView.FsaTexts()` (`OriginName` = `DCResult_FsaOrigin` + имя члена `EfficiencyOrigin`).
Замер НАСТОЯЩИМ путём окна (`FsaTexts` → `Texts.NameOf` → `Summary.Describe`, отражением на сборках до и
после, скрипт `D:\BqMoni_Claude\p260\src\origin_words.ps1`, выводы `origin_words_{before,after}.txt`):

| | до | после |
|---|---|---|
| en, заголовок | `FSA: Bq by Simulation curve` | `FSA: Bq by simulation curve` (`LSRM`, `measurement`, `manual`) |
| ru, заголовок | `FSA: Бк по кривой Simulation` | `FSA: Бк по кривой (расчёт)` (`ЛСРМ`, `измерение`, `вручную`) |
| ru, причина «скрыто» | `…кривая без абсолютного уровня (Manual)` | `…кривая без абсолютного уровня (вручную)` |
| подсказка колонки | «ROIs…» / «Зоны…» | «Rows…» / «Строки…» |

Сторожа `check_resx`, `check_resx_letters`, `check_resx_zorder`, `check_resx_designer` — код 0 до и после;
формат BOM + CRLF сохранён (правка байтами).

## 2. Находка 9 — подсветка строки связанного ряда

`EnergySpectrumView.Fsa.cs`, `ShowFsaOverlay`: в обоих сравнениях (`muting`, `muted`) слой «выбран» по
`Name == highlight` ИЛИ `ChainRoot == highlight`. `ChainRoot` ставится только членам СВЯЗАННОГО ряда
(`FsaAnalyzer`: `component.Kind == Chain ? component.Name : null`); у одиночки, свободного члена, «прочего»
и подложки — null, поэтому выбор одиночки ряд не зацепит.

⚠ **Проба П254 правку не видит, и это не дефект правки.** Строка «СВЕДЕНИЕ» считает слои САМА по
`FsaPresentationBuilder.Build(…).Layers` (имя / `ChainRoot`), отрисовку не зовёт — до и после правки она
печатает одно и то же: «строка «Th-232» … зажигает слоёв 1 (0.11 %), а слоёв её ряда 8 (100.00 %)». Это
описание стека, а не суд над краской. Поэтому отрисовка мерилась внедеревной пробой
`D:\BqMoni_Claude\p260\src\HighlightChainProbeP260.cs` (приём F16: настоящий `ShowFsaOverlay` отражением,
маска лент той же `DrawFsaBand`; слой «горит» — ни один пиксель не сменился, «приглушён» — сменилось > 90 %),
сессия разбора П254 (`DbLookups`, `ChainEquilibrium`), одна и та же проба на сборках до и после:

| спектр, режим | подсветка | до | после |
|---|---|---|---|
| Th232_29.07.2022, NucBase, равновесие ВКЛ (8 слоёв — все члены ряда) | «Th-232» (строка ряда) | горит 1 слой (Th-232, 0.11 %, 16 px), приглушено 7 (99.89 %) | **горит 8 (100 %)**, приглушено 0 |
| то же | «Tl-208» / «Pb-212» (член по имени) | горит 1 (23.32 % / 34.66 %) | то же, побайтно |
| Th232_29.07.2022, равновесие ВЫКЛ (6 слоёв, `ChainRoot` пуст) | «Tl-208» / «Th-232» (не обнаружен) | 1 горит / ничего не приглушено | то же, побайтно |
| Lu176, одиночки | «Lu-176» / «K-40» (без ленты) | 1 горит / ничего не приглушено | то же, побайтно |
| AS80_Th232Medal (корпус, поставочный конфиг), равновесие ВКЛ, 15 слоёв | «Th-232» | — | горят 8 членов (79.65 %), приглушены 7: K-40 (одиночка) и приборные образы |
| то же | «Tl-208» | — | горит 1, приглушено 14 |

⚠ **Следствие для окна отчёта FSA.** Слой самого корня связанного ряда носит имя ряда, и строка
«Th-232 — ряд» / «Th-232 — chain» окна отчёта (`FSAReportView` толкает то же `FsaHighlight` = имя слоя)
теперь тоже зажигает ВЕСЬ ряд, а не свою ленту корня (0.11 %). Остальные строки членов («Tl-208 — ряд
Th-232») — как прежде. Различить два вызова внутри `EnergySpectrumView.Fsa.cs` нечем — одна строка имени;
развести можно только у вызывающих (`DCResultView.cs`, `FSAReportView.cs` — не файлы полосы).

Снимки (на диске, не в git): `D:\BqMoni_Claude\p260\shots\hl_before\p260_eqon_*.png`,
`…\hl_after\p260_eqon_*.png` (до — весь ряд тусклый, после — весь ряд в цвете); выводы
`hl_{before,after}_{eqon,eqoff,lu176}.txt`, `hl_after_medal.txt`.

## 3. Приёмка

- `FsaResultWindowProbeP254` на итоговой сборке, `--ui=en` и `--ui=ru`, `--shots=` — **ИТОГ: ВСЁ СОШЛОСЬ**
  оба; русский вывод совпал с итоговым выводом П254 (`p254_final2_ru.txt`) построчно, английский — кроме
  трёх строк «(0.00%)», снятых самой П254 после её `final_en`. Снимки окна:
  `D:\BqMoni_Claude\p260\shots\p254_{en,ru}\p254_*_{eq_off,eq_on,recording_stale}.png` — на диске, не в git.
- `check_fsa_showcase --probes build_p260 --skip-freshness` — **9 пар совпали с эталоном**: подсветка в
  витрину не входит (дамп кривых, долей и строк отчёта), витрина прежняя.
- `check_fsa_report_view --probes build_p260 --skip-freshness` — 4 из 4 (F16 с умолчанием слоя — ok).
- `check_corpus_scenes --probes build_p260 --skip-freshness` — 51 из 51.
- `check_all` — 42 из 45; красные три — «КАТАЛОГ ПРОБ ПРОТУХ» (T226, канонический каталог распорядителя:
  `check_fsa_report_view`, `check_corpus_scenes`, `check_fsa_showcase`), все три зелёные на каталоге полосы
  выше.
- Сборки: MSBuild Debug в `bin\Debug_p260` — 0 (до и после); `build_all` в `build_p260` — 0 (до и после).
  Каталоги сборки полосы, копии конфига и спектров сняты; на `D:\BqMoni_Claude\p260\` остались исходники
  внедеревных проб (`src\`), снимки (`shots\`) и текстовые выводы (`*.txt`).

## Изменённые файлы

`BecquerelMonitor/DCResultView.resx`, `BecquerelMonitor/DCResultView.ru.resx`,
`BecquerelMonitor/EnergySpectrumView.Fsa.cs`; журнал — этот файл.
