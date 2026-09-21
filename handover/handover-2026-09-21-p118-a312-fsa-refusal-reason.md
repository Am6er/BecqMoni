# П118 (21.09.2026): `A312` — отказ FSA называет причину (библиотека, опустевшая с матрицей)

Строка `A312` заведена П117 21.09.2026 (§4 [журнала П117](handover-2026-09-21-p117-amber-config-apply.md)).
Решение Amber 21.09.2026, вопросником: **«Пускать»**. Полоса — П118, дерево `master` от `f242f166`.

Артефакты (текст, без картинок) — `handover/p118-a312/`; временное, сборки и снимки экрана —
`D:\BqMoni_Claude\p118\` (снимки `shots\01…03*.png` — на диске, в git не идут).

## 1. Что было

На спектре Amber `!AS80x80\калибровка 08.09.2026\Am-241.xml` (копия `D:\BqMoni_Claude\p118\spectra\`) с кривой
прибора «Точка» и матрицей физики 22 окно отчёта FSA писало красную строку «Full-spectrum decomposition is not
possible for this spectrum» и пустую таблицу. `FsaAnalysisSession.Compute` подставлял общий `FSANotPossible` на
любой `null` из `FsaAnalyzer.Analyze`, кроме гейта геометрии (`A277`, `GeometryRefused`).

`return null` внутри `Analyze` (`FsaAnalyzer.cs`, до правки строки 4630–5536) — **семь мест, шесть причин**:

| # | место | условие | было | стало (`FsaRefusal`) |
|---|---|---|---|---|
| 1 | вход | нет спектра / отсчётов / калибровки ПШПВ / энергетической калибровки / библиотека пуста на входе | молча | `Input` |
| 2 | гейт геометрии (`A277`) | `RequireGeometry && (efficiency == null \|\| !HasGeometry)` | `GeometryRefused` | `Geometry` (признак `GeometryRefused` читает его же) |
| 3 | каналы | `channels < 32` | молча | `FewChannels` (константа `MinChannels`) |
| 4 | полоса фита | `chHi <= chLo + 10` после сведения полос и пола | молча | `NarrowBand` (константа `MinBandChannels`) |
| 5 | **библиотека опустела** | `library.Count == 0` после гейтов матрицы (рентген/вылет кристалла, `AMBER4`/`S172`), ключа SE/DE и 511, ножа линий ниже сетки (`A49`) | молча | **`LibraryEmptied`** |
| 6 | сетка дрейфа | `bestChi2 == Double.MaxValue` | молча | `NoFit` |
| 7 | итоговый фит | `FitHuber` вернул `null` | молча | `NoFit` |

## 2. Какое место сработало — измерено пробой, не догадкой

Стенд: `D:\BqMoni_Claude\p118\wd\` = каталог проб `build_p118` целиком + `config\` — копия конфига Amber
(`OneDrive\Desktop\Debug\config`, 43 файла, матрица «Точка» `4c5aa26d…rmx` sha `CD6CD612AA9F…` = у Amber);
`FsaStackShot --spectrum=…\Am-241.xml --efficiency='Точка' --infer --lib-dump`.

- **Сборка HEAD `f242f166` (до правки)** — `p118-a312/01-stackshot-am241-head-refusal.txt`: библиотека из NucBase —
  **ровно один образ `Xray-NaI` (Nuisance, 3 линии, `FromCrystal`)**; матрица привязана (`ResponseMatrix нет → …`);
  проба — «разложение не получилось», код 1. Какое из семи мест — по этому выводу не видно.
- **Сборка с правкой** — `p118-a312/02-stackshot-am241-p118-reason.txt`:
  `разложение не получилось: LibraryEmptied — library 1 -> 0: crystal x-ray 1, crystal escape 0, SE/DE images 0,
  annihilation 0 (matrix on); lines below matrix grid 0`.

То есть сработало место 5: гейт `AMBER4` (собственный рентген кристалла при живой матрице свободной колонкой не
идёт) снял единственный образ, библиотека опустела. Не дефект поставки и не дефект кривой — в составе спектра нет
ни одного нуклида (пики 26.6/59.5 кэВ «(unknown)», единственная подпись K-40 не прошла порог доли 30 %).

⚠ Ключ `--no-matrix` у `FsaStackShot` — «не настаивать на матрице», а не «не использовать»: при годной матрице
в складе она привязывается всё равно (`SETUP … ResponseMatrix нет → …` в обоих прогонах). Контроль «без матрицы
разбор идёт» поэтому сделан на анализаторе в пробе (§4), а не этим ключом.

## 3. Правка

Образец — `A277`: решение об отказе ОДНИМ местом в анализаторе, слова — в сеансе из ресурсов.

- `BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs` — перечисление `FsaRefusal` {`None`, `Input`, `Geometry`,
  `FewChannels`, `NarrowBand`, `LibraryEmptied`, `NoFit`}; свойства `Refusal` и `RefusalNote` (служебная
  подробность инвариантной культурой: сколько образов снято каким гейтом, края полосы, число каналов); константы
  `MinChannels = 32`, `MinBandChannels = 10` вместо литералов (число одно на проверку и на слова); один метод
  `Refuse(why, note)` — каждое из семи `return null` в `Analyze` стало `return this.Refuse(…)`; `GeometryRefused`
  теперь читает `Refusal == Geometry` (читатели — сеанс, `CorpusFsaProbe`, `FsaGateRescueProbe`,
  `IsoCurveActivityProbeP79` — видят прежний признак). Два локальных счётчика в цикле снятия образов (SE/DE и 511) —
  только для подробности. ⛔ Фит не тронут: ни одно условие отказа не изменено, изменено лишь то, что о нём можно
  спросить.
- `BecquerelMonitor/FullSpectrumAnalysis/FsaAnalysisSession.cs` — `public static string RefusalText(FsaRefusal,
  string efficiencyRefusal)`: `Geometry` → прежние `FSACurveRefused`/`FSANoGeometry` (`AMBER34` сохранена),
  `LibraryEmptied` → `FSALibraryEmptied`, `NarrowBand` → `FSABandTooNarrow` (`{0}` = `MinBandChannels`),
  `FewChannels` → `FSATooFewChannels` (`{0}` = `MinChannels`), `NoFit` → `FSANoFit`, `Input` → `FSANoCalibration`,
  неизвестное → `FSANotPossible`. `Compute` пишет причину и подробность в `Trace` («FSA refused: …»).
- `BecquerelMonitor/Properties/Resources.resx`, `Resources.ru.resx`, `Resources.Designer.cs` (парный
  генерируемый файл, без него не собирается) — пять новых строк, EN первичен, RU второй:

| ключ | EN | RU |
|---|---|---|
| `FSALibraryEmptied` | Full-spectrum decomposition: no component of the composition is left to fit — with a response matrix the crystal's own X-ray and escape images are carried by the matrix; add a nuclide to the composition | Полноспектральное разложение: в составе не осталось ни одной компоненты для фита — при матрице отклика собственный рентген и вылет кристалла несёт матрица; добавьте нуклид в состав |
| `FSABandTooNarrow` | Full-spectrum decomposition: the fit band is {0} channels or fewer — widen the peak-search energy range or check the fit floor and the energy calibration | Полноспектральное разложение: полоса фита не длиннее {0} каналов — расширьте диапазон энергий поиска пиков или проверьте пол фита и энергетическую калибровку |
| `FSATooFewChannels` | Full-spectrum decomposition needs at least {0} channels in the spectrum | Полноспектральному разложению нужно не меньше {0} каналов в спектре |
| `FSANoFit` | Full-spectrum decomposition: no fit converged on the drift grid for this spectrum | Полноспектральное разложение: ни один узел сетки дрейфа не дал решения для этого спектра |
| `FSANoCalibration` | Full-spectrum decomposition needs an energy calibration and an FWHM calibration of the spectrum | Полноспектральному разложению нужны энергетическая калибровка и калибровка ПШПВ спектра |

  Имён нуклидов и образов в словах нет: причина называет род лекарства (состав, полоса, калибровка).
  `check_resx.py`, `check_resx_designer.py`, `check_resx_letters.py`, `check_resx_zorder.py` — все 0.
- `tools/effmaker/probes/FsaStackShot.cs` — на `null` печатает `разложение не получилось: <причина> — <подробность>`
  (одна строка; этим и измерен §2).
- `tools/effmaker/probes/FsaReportViewProbe.cs` — новый раздел 14 (§4).

## 4. Положительный контроль — `FsaReportViewProbe`, раздел 14 (гонит сторож `check_fsa_report_view`)

Сцена Amber воспроизводится на контроле корпуса `AS80_Cs137_0cm` (третий документ из того же файла): состав из
NucBase, пики сняты → вывод состава пуст → библиотека сеанса = один образ `Xray-NaI` (как у `Am-241`); кривая под
СВОИМ guid `a312-probe-<новый>`, малая матрица (10 узлов × 4000 историй, 11.3 с на Debug, образец
`BoxCylStampProbeF64`) считается в пробе и кладётся в склад проб под этот guid, снимается в `finally` (ни один
настоящий спектр на такой guid не ссылается; под guid контроля класть нельзя — осталась бы годной ему и молча
меняла бы разделы выше). `p118-a312/03-fsareportviewprobe-std-build.log` (штатный `build`; тот же приговор на `build_p118`), код 0, «ВСЕ СОШЛИСЬ», строк ⛔ нет, 30 с:

- анализатор с матрицей: `null`, `Refusal = LibraryEmptied`, `GeometryRefused = false`, подробность
  `library 1 -> 0: crystal x-ray 1, …` — та же, что на `Am-241`;
- **контроль (а)**: тот же вход БЕЗ матрицы — результат есть, `Refusal = None`, подробности нет;
- живой сеанс документа через `FSAReportView` в `en-US` и `ru-RU`: результата нет, `Status` = `FSALibraryEmptied`
  своей культуры, ≠ `FSANotPossible`; в таблице ровно одна строка `Status`, и она несёт причину;
- остальные причины на анализаторе: `Input` (null-спектр; без ПШПВ; пустая библиотека на входе), `Geometry`
  (кривой нет, `GeometryRefused` истинен), `FewChannels` (клон спектра на `MinChannels − 1` каналов, подробность
  `channels=31`), `NarrowBand` (`FitToLibrary` при `MinEnergy = MaxEnergy = 661`: `band 1823..1823 of 8192 channels
  (660.8..660.8 keV), floor 20.2 keV`; `FsaBand.DefaultMode` возвращается в `finally`); `NoFit` не подброшен —
  дешёвого входа, на котором сетка дрейфа не даёт решения, нет, слова у него проверены как у остальных;
- слова: у каждого члена, кроме `None`, в обеих культурах текст есть, ≠ `FSANotPossible`, попарно разные;
  `Geometry` с отвергнутой кривой несёт слова кривой (`AMBER34`); неизвестное значение `(FsaRefusal)999` →
  `FSANotPossible`;
- уборка: файл малой матрицы снят.

Разделы 0–13 пробы — без изменений (прежний состав строк, снимки, повторяемость). Прежняя нумерация: «13» уже
занята повторяемостью (`A250`), новый раздел — 14.

## 5. Витрина и штатный каталог

`check_fsa_showcase.py` на штатном `probes\build`, пересобранном из `bin\Debug_Codex` с правкой (оба `/t:Rebuild`):
**код 0, 9 пар совпали с эталоном 21.09 16:00, 24 с** (`p118-a312/04-check_fsa_showcase.log`). Эталон НЕ
переобъявлялся — правка картинку не трогает, и так и должно быть.

## 6. Проверка экраном (распоряжение Amber 19.09.2026)

Приложение Release из HEAD+правка → `D:\BqMoni_Claude\p118\app\` (`/t:Rebuild`, `IntermediateOutputPath=obj\p118_rel\`);
`config\` сборки заменён копией конфига Amber целиком (43 файла; 10 поставочных лишних — `device\AtomSpectraVCP.xml`
и 9 ROI — тем самым сняты; вложенного `config\config` нет). Запуск `Start-Process` из `app\`; приложение подняло
её сеанс (вкладки Lu176, Cs 137 в домике, Th232, Чароит, Th-232, Lu-176, Радон деревня — из YandexDisk, только
чтение, «Save Automatically» снята). `Ctrl+O` → путь КОПИИ `D:\BqMoni_Claude\p118\spectra\Am-241.xml` набран в
поле имени (в её каталог не ходил).

- Вкладка `Am-241`, Efficiency «Точка - from the spectrum file» (файл после П117 уже несёт кривую прибора):
  окно отчёта FSA — красная строка состояния `FSA error: Full-spectrum decomposition: no component of the…` и
  строка таблицы с полным текстом `FSALibraryEmptied` (снимок `shots\01-…png`).
- Выбор кривой прибора «Точка» из списка (сценарий Amber): тот же отказ с тем же текстом
  (`shots\02-…png`, обрезка панели отчёта `shots\03-…png`): «Full-spectrum decomposition: no component of the
  composition is left to fit — with a response matrix the crystal's own X-ray and escape images are carried by the
  matrix; add a nuclide to the composition». Пики 26.64 / 59.52 / 123.7 кэВ «(unknown)», K-40 1388 кэВ — как в П117.
- Закрыто `Stop-Process`; её файлы не тронуты: спектры калибровки 20:55–21:00, конфиг 20:51 (до запуска); копия
  спектра не сохранялась.

Наблюдение (не строка): метка состояния НАД таблицей — одна строка без переноса, и длинный текст причины в ней
обрезан («…no component of the»); полный текст — в строке таблицы, как задумано решением Amber 07.09.2026
(«в таблице остаётся только ПРИЧИНА»). То же было бы и у `FSANoGeometry` (130+ знаков); правкой не вносится.

## 7. Приёмка

- `MSBuild` Debug `bin\p118` (для проб) и `bin\Debug_Codex` (штатный), Release `D:\…\p118\app` — все код 0;
  `build_all.ps1` → `build_p118` и штатный `build` (дважды, см. ниже) — код 0, каталоги заверены (`T226`),
  `check_fsa_docs` 0 («описаний 334, умолчаний 96» — две новые константы).
- `python tools/check_all.py` — первый прогон **41 из 42**: красен `check_probe_tuning` (`T243`) — раздел 14
  собирает `FsaAnalyzer` руками, и проба попала в перепись «считающих FSA», не отчитываясь о настройках. Починено
  в той же полосе: `FsaTuningReport.Snapshot()` в `Main` до разбора ключей, `FsaTuningReport.Print(analyzer)` перед
  `Analyze` в разделе 14; штатный `build` пересобран. Второй прогон — **ВСЕ ЗЕЛЕНЫ: 42 из 42, код 0, 92 с**
  (`p118-a312/05-check_all.log`; `check_fsa_report_view` 0 за 30 с, `check_fsa_showcase` 0, `check_registry` 0).
- Склад штатного каталога проб после прогона: файла `a312-probe-*` нет (уборка раздела 14 отработала);
  лежит только чужой `2992663a….rmx` от 01.09.2026 — матрица прежней физики, отпечатку контроля не годна и
  им не читается (не этой полосы, не трогал).

## 8. Чем закончилось

- `A312` — сделано: причина отказа `Analyze` различается семью местами / шестью членами `FsaRefusal` и доезжает
  до окна словами в обеих культурах; проба (раздел 14, сторож `check_fsa_report_view`) и экран это подтверждают.
  Закрывает распорядитель.
- Уборка после приёмки (по слову распорядителя): `bin\p118`, `obj\p118`, `obj\p118_rel`, `probes\build_p118`,
  `D:\BqMoni_Claude\p118\`.
