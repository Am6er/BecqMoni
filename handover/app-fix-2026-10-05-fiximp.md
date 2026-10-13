# Полоса правки `fiximp` — 05.10.2026

Строки: `AMBER167`, `AMBER169`, `AMBER172` (только (а)), `AMBER175`. Разбор — §2.1, 2.3, 2.6, 2.9
[app-bug-review-2026-10-05.md](app-bug-review-2026-10-05.md). Номера строк — по рабочему дереву
после правки (база `f4abe6c8` + незакоммиченные правки соседних полос).

## Как мерилось

Проба `FixImpProbe.cs` (не сторож — лежит вне дерева, `D:\BqMoni_Claude\fiximp\shots\`, там же оба
вывода `probe_before.txt` / `probe_after.txt`) сама сочиняет входы (Atom Spectra .txt, N42-2012,
CSV), импортирует их дверями приложения в документ `CreateDocument` и печатает поля. Одна и та же проба
собрана против двух сборок: `before` — дерево ДО правки (собрано первым делом в
`bin\Debug_fiximp_before`), `after` — после. Числа ниже — из этих двух выводов.

## AMBER167 — импорт Atom Spectra: даты и дробное время

**Было.** `ImportDocumentAtomSpectra` заполнял только `SampleInfo.Time`; `StartTime`/`EndTime`
оставались умолчанием `ResultData` («сейчас»). Время набора читалось `(int)XmlConvert.ToDouble`.

**Сделано.** `DocumentManager.cs:1942-1980` — дата файла (Time1, мс от эпохи) кладётся в
`SampleInfo.Time` и `StartTime`; `:1992-2002` — время набора `double`, `TotalTime`/`ElapsedTime`
дробные, `EndTime = StartTime + T` (соглашение `A156`/`A177` соседних дверей); `PresetTime`
остаётся целым (поле `int`, как у SpecUtils). Ноль в Time1 и дата вне пределов окна
(`AMBER175`) — `ResultData.UnknownStartTime` и голос `ERRMissingStartDateTime` один раз
(`:1916-1918`, `:2078-2083`), как у двери SpecUtils. Time1 понят как НАЧАЛО: так его пишет свой
экспорт (`SampleInfo.Time`), иначе круг .txt → .txt сдвигал бы дату.
⚠ Что Time1 означает в файлах самой Atom Spectra (начало или момент сохранения) — не
установлено; файлов Atom Spectra в дереве нет (`FORMAT: 3` — ни одного .txt), проба сочиняла свои.

**Замер** (`atom_dated.txt`: Time1 = 2024-01-15 10:00:00Z, время 30.9 с):

| | до | после |
|---|---|---|
| real / TotalTime | 30 / 30 | 30.9 / 30.9 |
| StartTime | «сейчас» (2026-10-05) | 2024-01-15 13:00:00 (местное) |
| EndTime | «сейчас» | 2024-01-15 13:00:30.900 |
| множитель распада Cs-137 | 1.064750 | 1.000000 |
| множитель распада I-131 | 2.0e+37 | 1.00002 |

Контроль `atom_nodate.txt` (Time1 = 0): до — дата 1970-01-01T03:00 молча, Start «сейчас»; после —
1970-01-01 (`UnknownStartTime`) и голос «В файле нет времени начала набора (таких измерений: 1)».

## AMBER169 — дверь SpecUtils

**(а) Было.** В ветви фона `resultData` — ResultData переднего спектра, и общий хвост писал в его
статус, `StartTime`, `SampleInfo.Time`, `EndTime` времена фона.
**Сделано.** `DocumentManager.cs:1071-1073` — множество ResultData, чей передний спектр уже лёг
из файла; `:1121` — пополняется в ветви переднего; `:1243-1278` — статус и даты ResultData фон
пишет, только если переднего в ней нет (файл из одного фона; фон раньше переднего — передний
затем перепишет своими). Живое и полное время фона по-прежнему в `BackgroundEnergySpectrum`.

| `fgbg.n42` (передний 600/590 с, 2024-01-01; фон 3600/3590 с, 2023-12-31) | до | после | дверь N42 |
|---|---|---|---|
| TotalTime / Preset | 3600 / 3600 | 600 / 600 | 600 / 600 |
| Start / End | 2023-12-31 00:00 / 01:00 | 2024-01-01 00:00 / 00:10 | 2024-01-01 00:00 / 00:10 |
| фон live / real | 3590 / 3600 | 3590 / 3600 | 3590 / 3600 |

Обратный порядок (`bgfg.n42`) — 600 / 2024-01-01 и до, и после (контроль: передний переписывал).

**(б) Было.** `livetime = (livetime == 0) ? presettime : livetime` и то же для полного.
Подстановку ввела Amber (`534fc8fa`) для CSV без времени — она сохранена.
**Сделано.** `DocumentManager.cs:1173-1196`: оба нуля → уставка в оба поля (как было); живое 0
при известном полном → живое остаётся 0, знаменатель берёт полное (`LiveTime.Effective`: «живое,
если > 0, иначе полное» — проверено чтением `Utils/LiveTime.cs:50-53`); полное 0 при известном
живом → полное = живому (попутно, тот же дефект зеркально: уставка 3600 при живом 86000 давала
`EndTime` и «Time:» экспорта на 23 ч раньше).

| вход, уставка 3600 | до: live / real / знаменатель | после | дверь N42 |
|---|---|---|---|
| `nolive.n42` (полное 86400, живого нет) | 3600 / 86400 / **3600** (×24) | 0 / 86400 / 86400 | 0 / 86400 / 86400 |
| `noreal.n42` (живое 86000, полного нет) | 86000 / **3600** / 86000 | 86000 / 86000 / 86000 | — |
| `notime.csv` (CSV без времени) | 3600 / 3600 / 3600 | 3600 / 3600 / 3600 | — |
| `notime2.csv` (одна колонка) | 10 / 11 / 10 | 10 / 11 / 10 | — |

CSV без времени ведёт себя как раньше — побитово те же поля и голоса. (`notime2.csv`: SpecUtils
сама читает первые два числа одноколоночного файла как времена — её поведение, до и после одно.)

**(в)** `(int)` суммы — не тронуто, по постановке.

## AMBER172 (а) — дверь N42-2012: калибровка по ссылке

**Было.** `rad.EnergyCalibration[i]` (номер измерения) либо `[0]`; `energyCalibrationReference`
не читался.
**Сделано.** `N42/Util.cs:1063-1100` — шкала ищется по `Spectrum[0].energyCalibrationReference`
(сравнение `id` порядковое); нет ссылки или она в никуда — прежний путь по номеру. Ссылка в
никуда считается (`:1011-1014`) и говорится один раз на файл (`:1499-1505`) новым ресурсом
`ERRCalibrationReferenceNotFoundN42` (англ. + рус.). (б) — не тронуто, ждёт решения Amber.

Калибровки входов: EC-A = «0 3 0», EC-B = «10 2 0»; печатается (c0 c1) после переноса краёв на
центры (+½ наклона к c0).

| вход | до | после |
|---|---|---|
| `calref.n42`: M1→EC-B, M2→EC-A | [0] 1.5 3 (EC-A), [1] 11 2 (EC-B) — перепутаны | [0] 11 2 (EC-B), [1] 1.5 3 (EC-A) |
| `calfirst.n42`: калибровочное M0 впереди, M1→EC-A | 11 2 (EC-B, по номеру 1) | 1.5 3 (EC-A) |
| `calbadref.n42`: M1→EC-Z, M2→EC-B | [0] EC-A, [1] EC-B, молча | то же + голос «…первая ссылка: EC-Z…» |
| `calnoref.n42`: ссылок нет | [0] EC-A, [1] EC-B | то же (запасной путь цел) |

## AMBER175 — дата раньше 1753 года роняла вкладку пробы

**Было.** `DCSampleInfoView.cs` клал `SampleInfo.Time`/`StartTime` в `DateTimePicker.Value` без
сторожа; `ParseMeasurementStartDateTime` (`RoundtripKind`) законно принимает `0001-01-01`.
Что «приборы без часов пишут 0001-01-01» — допущение ревизии, не проверялось; падение — проверено.

**Сделано.**
* `DocumentManager.cs:813-827` — `IsStartTimeDisplayable` (пределы `DateTimePicker`).
* `N42/Util.cs:1901-1922` — дата вне пределов бросает в `ParseMeasurementStartDateTime`, и оба
  разбора N42 (2006 и 2012) ведут её путём нечитаемой (`A157`/`A171`): `UnknownStartTime` + голос с
  самой записью.
* `DocumentManager.cs:1209-1225` (SpecUtils) и `:1957-1975` (Atom Spectra) — то же «времени нет».
* `DCSampleInfoView.cs:30-41, 139-158, 171-172, 271-272, 380-381` — показ у границы
  (`SetDateSafely`), настоящее значение хранится в `sampleTimeOutOfRange` и уходит в модель при
  `SaveFormContents`, пока человек поле не тронул (правило `A1` массы).

**Замер импорта** (`date0001.n42`, StartDateTime `0001-01-01T00:00:00`): дверь N42 до —
Start/Sample `0001-01-01`; после — `1970-01-01` и голос «…не прочитано время начала… первое:
0001-01-01T00:00:00 — ArgumentOutOfRangeException». Дверь SpecUtils эту дату сама отдаёт нулём —
1970-01-01 и голос и до, и после.
Положительный контроль механизма в пробе: `DateTimePicker.Value = 0001-01-01` →
`ArgumentOutOfRangeException`. `SetDateSafely(0001-01-01)`: показано 1753-01-01, сохранено
0001-01-01; `SetDateSafely(2024-01-01)`: показано 2024-01-01, сохранено null.

**Экран** (копия конфига Amber, документ BecqMoni `date0001_doc.xml` — корпусный
`AS80_Cs137_0cm.xml` с `<Time>` и `<StartTime>` = `0001-01-01T00:00:00`, то есть уже сохранённый
документ, которого двери импорта не касаются):
* до (сборка `before`): окно «Unhandled exception… Value of '01.01.0001 0:00:00' is not valid for
  'Value'», спектр не нарисован;
* после (Release-сборка `app`): вкладка «Sample Information» — Sampling/Measuring Date
  «1753/01/01 00:00», спектр и разбор на месте; два сохранения подряд (второе — после
  `SaveFormContents`) — в файле по-прежнему `<Time>0001-01-01T00:00:00</Time>` и
  `<StartTime>0001-01-01T00:00:00</StartTime>`: граница в модель не записана.

Снимки (на диске, не в git): `D:\BqMoni_Claude\fiximp\shots\amber175_before_unhandled_exception.png`,
`amber175_after_sample_tab_1753.png`.

## Сборка и сторожа

Debug в `bin\Debug_fiximp` — код 0; Release в `D:\BqMoni_Claude\fiximp\app` — код 0;
`check_resx`, `check_resx_designer`, `check_resx_letters`, `check_resx_zorder` — код 0.
`check_all.py`: 41 из 44 — код 0; три сторожа с каталогом проб (`check_fsa_report_view`,
`check_corpus_scenes`, `check_fsa_showcase`) отказали «КАТАЛОГ ПРОБ ПРОТУХ (T226)» — канонический
`probes\build` старше дерева, где правят несколько полос (изменено 14 файлов приложения). По
указанию сторожей собран свой каталог (`build_all.ps1 -Bin bin\Debug_fiximp -Out build_fiximp`,
код 0) и все три прогнаны с `--probes=`: `check_fsa_report_view` — 0 («ВСЕ СОШЛИСЬ», 326 строк),
`check_corpus_scenes` — 0 (51 из 51), `check_fsa_showcase` — 0 (9 пар с эталоном; первые две
попытки — `PermissionError` на общем рабочем каталоге витрины, занятом `FsaStackShot` соседней
полосы; третья, после его выхода, — 0). Канонический `probes\build` не тронут.

## Изменённые файлы

`BecquerelMonitor/DocumentManager.cs`, `BecquerelMonitor/N42/Util.cs`,
`BecquerelMonitor/DCSampleInfoView.cs`, `BecquerelMonitor/Properties/Resources.resx`,
`BecquerelMonitor/Properties/Resources.ru.resx`, `BecquerelMonitor/Properties/Resources.Designer.cs`
(одна строка ресурса `ERRCalibrationReferenceNotFoundN42`), этот журнал.
