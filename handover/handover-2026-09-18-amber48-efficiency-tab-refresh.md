# `AMBER48` — подпись поколений на вкладке Efficiency не освежалась после записи матрицы (18.09.2026, распорядитель)

> 🔨 **Нашли проблему — строкой в [`TODO.md`](../TODO.md) в корне.** Строка `AMBER48` заведена по слову Amber, правка сделана
> распорядителем в главном дереве (три файла приложения и проб, один скрипт навыка); полосы П99/П100/П101 в это время
> шли в своих worktree и этих файлов не трогали.

## 1. Постановка Amber (консоль, 18.09.2026, три снимка окна, дословно)

> «Запиши задачу AMBER. 1.Картинка 1, посчиталась матрица. 2.Картинка 2. Нажал сохранить. 3.Картинка 3.
> Предупреждение не исчезло (см стрелка).»

Снимки (на экране Amber, в git не идут — распоряжение 17.09.2026): окно Edit Device Configuration, прибор
«Atom Spectra 80x80», вкладка Efficiency, кривая «Th медальон», клеймо кривой `phys=19; hist=1000000;
grid=7.4-3000 keV/94 log; kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1; ecomp=1; bpath=2; eltr=1`.

1. Окно «Response matrix» после пересчёта: 100 узлов 7–3000 кэВ, бин 2 кэВ, 1 000 000 историй на узел, 10 потоков,
   731 с; «Matrix generation: physics 19, file format 9 — current code: physics 19, format 9», «Valid for the current
   geometry», «Done in 12:10. Not saved yet», отпечаток тела «none (taken from the file)».
2. После «Save to the current geometry»: «Saved: …\Debug\config\device\response\04cb27d4-….rmx», отпечаток тела
   `322c041c33ed7dcb…`, кнопка записи погашена. Подпись вкладки ЗА окном по-прежнему «Curve is generation 19, its
   response matrix is generation 18 — two generations side by side.»
3. Окно закрыто — та же подпись стоит (стрелка Amber).

## 2. Разбор

Подпись строится в `DeviceConfigForm.Efficiency.cs` → `UpdateEfficiencyView()`: поколение матрицы читается из
ЗАГОЛОВКА ФАЙЛА склада `ResponseMatrixStore.PeekVersions(config.Guid)` (`A119`, 04.09.2026), и больше ниоткуда.
`efficiencyMatrixButton_Click` после `ShowDialog` вкладку не освежал — подпись и сводка жили с момента выбора кривой
(смена конфигурации в списке, открытие окна заново). Файл и клеймо записывались верно: окно матрицы читает тот же
заголовок и говорит «physics 19». Дефект — только освежение вкладки.

Сверка с реестром: `A119` (подпись поколений, закрыта), `W11` (выключатель матрицы, пометка «изменено» после окна) —
о самом освежении строки не было.

## 3. Правка

| файл | что |
|---|---|
| `BecquerelMonitor/ResponseMatrixForm.cs` | событие `MatrixSaved` и `OnMatrixSaved()`; `SaveClick` поднимает его после удачной записи; отказ записи — `AppUi.Report` вместо `MessageBox.Show` (обработчик назван литералом в пробе, и сторож безоконного пути `check_headless` счёл его достижимым — правило `S100`; в приложении `Report` показывает то же окно) |
| `BecquerelMonitor/DeviceConfigForm.Efficiency.cs` | `efficiencyMatrixButton_Click` подписывается на `MatrixSaved` → `UpdateEfficiencyView()` (вкладка видна за модальным окном — снимок 2) и освежает вкладку ещё раз по закрытию окна, на любой исход; обработчик `responseMatrixForm_MatrixSaved` |
| `tools/effmaker/probes/CurveGenerationProbe.cs` | новый раздел `TabRefresh()` (ниже) + читатель IL `Calls()`; `[STAThread]` на `Main` (форма) |
| `.claude/skills/todo-work/scripts/todo_edit.py` | попутно: операция `after` вставляла строку БЕЗ хвостового CR — в `TODO.md` (CRLF) появлялся одинокий LF (git: «LF will be replaced by CRLF»); теперь хвост берётся у строки-опоры; контроль — вставка во временную копию: 431 CRLF, 0 голых LF |

## 4. Приёмка

`CurveGenerationProbe` (канонический `probes\build`, собран `build_all.ps1 -Bin BecquerelMonitor\bin\Debug_Codex`,
коды 0; символ `OnMatrixSaved` в exe найден), раздел «вкладка после записи матрицы (AMBER48)», код 0, «ВСЕ СОШЛИСЬ»:

| шаг | измерено |
|---|---|
| настоящая `DeviceConfigForm` без показа, кривая с клеймом `phys=19`, файл склада за её guid — физики 18 | подпись «Curve is generation 19, its response matrix is generation 18 — two generations side by side.» |
| файл переписан физикой 19, вкладку НЕ освежали | подпись ПРЕЖНЯЯ — **положительный контроль**: дефект Amber воспроизведён на стенде |
| `ResponseMatrixForm(кривая)` + подписка обработчика вкладки на `MatrixSaved` + `OnMatrixSaved()` | подпись «» — ушла |
| IL `efficiencyMatrixButton_Click` | зовёт `add_MatrixSaved` и `UpdateEfficiencyView` |
| IL `SaveClick` | зовёт `OnMatrixSaved` |
| контроль читателя IL: `GenerationNotes` | `UpdateEfficiencyView` не зовёт |

Файл стенда — `probes\build\config\device\response\<guid>.rmx` (портативная раскладка, каталог сборки проб), снимается в
`finally`; конфиг Amber не трогался.

`python tools/check_all.py` — 40/41: красен только `check_registry` прежними N8 (`AMBER46`, `D50`, старые `DONE.md`);
`check_headless` после замены `MessageBox.Show` → `AppUi.Report` — «ЧИСТО». `check_curve_generation` — код 0.

Экраном — Amber, на своей сборке из коммита: пересчёт → «Save to the current geometry» → подпись должна уйти сразу,
не дожидаясь закрытия окна.

## 5. Что осталось

Ничего в коде. Закрывает строку Amber после проверки экраном (серия `AMBER`).
