# П109 (19.09.2026): проверка экраном `AMBER45` и `AMBER48` на копии конфига Amber

Распоряжение Amber 19.09.2026, консоль, дословно: **«Работай по всем задачам из TODO. По задачам
с GUI: Сам запускай приложение и смотри, исправилось или нет.»** Решение вопросником того же дня:
**«Вычёркивать по моей проверке экраном»** (AMBER45/AMBER48 — вычёркивает распорядитель после
проверки экраном, не ждать взгляда Amber).

## 1. Стенд

- Сборка HEAD `0aab363e`, Release, `/t:Rebuild`, `OutputPath=D:\BqMoni_Claude\p109\app\`
  (`IntermediateOutputPath=obj\p109\`), код возврата 0.
- Портативная раскладка: `config\` рядом с exe = **копия** `C:\Users\moroz\OneDrive\Desktop\Debug\config`
  (50 файлов, поставочные 10 файлов из сборки сняты, чтобы копия равнялась конфигу Amber байт в байт;
  19 матриц `.rmx`). ⛔ Каталоги Amber не тронуты — проверено по `LastWriteTime` после закрытия:
  `Cs 137 в домике 24.11.2022.xml` 18.09 13:04:32, `1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml`
  18.09 17:27:38, `c482e3bc-….rmx` 18.09 13:04:10.
- Запуск `BecquerelMonitor.exe` из `D:\BqMoni_Claude\p109\app`, PID 25964, 14:38–14:48; приложение
  подняло её сеанс: семь вкладок спектров (файлы читались из её каталогов, только чтение).
- Управление окном — `windows-mcp` (снимки, клики, ввод); снимки на диске
  `D:\BqMoni_Claude\p109\shots\amber45_layer_all.png`, `amber45_layer_compton.png` (в git не идут).

## 2. `AMBER48` — подпись поколений на вкладке Efficiency

Сценарий Amber (18.09.2026): «посчиталась матрица → нажал сохранить → предупреждение не исчезло».

| шаг | что на экране |
|---|---|
| Edit Device Config → Efficiency, кривая «Точка в защите» (Nano 16 Pro RadiaScan 701A) | `100 curve points, 4 to 2999 keV computed with: phys=19; hist=1000000 …` и красным: **«Curve computed with transport generation 19, this build computes generation 21 – recompute it.»** |
| Response matrix… | «Out of date: computed by another generation… Matrix generation: physics 19, file format 9 – current code: physics 21, format 9», 100 узлов 5–3000 кэВ, 1000000 историй на узел, посчитана 18.09.2026 13:03 за 313 с |
| Recompute (100000 историй, 15 потоков) | «Valid for the current geometry… physics 21, file format 9… took 23 s. Done in 0:23. Not saved yet.» |
| **Save to the current geometry** | «Saved: D:\BqMoni_Claude\p109\app\config\device\response\c482e3bc-….rmx»; **вкладка за окном освежилась сразу**, не закрывая окно: под первой строкой появилась вторая — «Curve is generation 19, its response matrix is generation 21 – two generations side by side.» |
| Edit… → Calculate from geometry (100000 историй) → Save curve → закрыть редактор | подпись: `computed with: phys=21; hist=100000; … eltr=1; elmix=1; lbrem=1`, **красных строк нет** |
| Save конфига | FSA пересчитался: «Response matrix: used», «Cascade summing: applied», Cs-137 95.36 %, χ²/ndf 12.62 (было «not used», 35.77) |

Итог: дефект (подпись не освежалась после записи матрицы) исправлен — подпись меняется в момент
записи (`MatrixSaved`) и по закрытию окна. ⚠ Первая красная строка после записи матрицы остаётся по
делу: она про **кривую** (поколение 19), а не про матрицу; исчезает после пересчёта самой кривой
(Edit… → Calculate from geometry → Save curve). Вторая строка теперь называет это состояние прямо.

## 3. `AMBER45` — комбо «Matrix layer» в FSA Report, группа Display

Спектр «Cs 137 в домике 24.11.2022», кривая «Точка в защите», матрица физики 21 (из п. 2), режим
вида «Show FSA (full-spectrum decomposition)».

| значение комбо | что нарисовано |
|---|---|
| **All** (умолчание) | стопка как прежде: Cs-137 (пик 662 + комптон), Backscatter (бугор ~200 кэВ), X-ray Pb, Pile-up (зелёный выше 700 кэВ) |
| **Compton (partial deposit)** | только комптоновская полка Cs-137 до края ~477 кэВ и спад; пика 662 нет; спектр — зелёной линией поверх |
| **Full-energy peak** | пик 662 кэВ и рентген Ba ~32 кэВ (стопка по компонентам: Cs-137, X-ray Pb) |

Список комбо: All, Full-energy peak, Compton (partial deposit), Single escape (511 keV), K X-ray escape,
Double escape (1022 keV), L X-ray escape — «каждый слой из существующих», как просила Amber.
Переключение перерисовывает спектр сразу, без пересчёта FSA.

Итог: сделано, как описано Amber 18.09.2026 (умолчание All = прежний вид; слой — стопка по
компонентам, по своей галке «Model residual»).

## 4. Попутные наблюдения (не строки — уходят в П110 по `AMBER46`)

- `Lu-176.xml` (кривая «Цилиндр», 80x80, матрица `50a57c5a` от 14.09): при открытии окно
  «The response matrix … was computed with file format 8, while this build reads format 9. The
  decomposition is computed WITHOUT the matrix» — в отчёте FSA «Response matrix: old format».
- Матрица **формата 9, но физики 19** (`c482e3bc`, 18.09 13:03): в отчёте FSA «Response matrix:
  **not used**» до пересчёта. То есть к пересчёту у Amber идут все матрицы с физикой < 21, а не
  только формата ≤ 8 — передано полосе П110 (ревизия склада) для проверки по коду.

## 5. Что снять после приёмки

`D:\BqMoni_Claude\p109\app` (сборка + копия конфига с двумя пересчитанными на 100000 историй
объектами — «Точка в защите» кривая и матрица), `obj\p109`; `D:\BqMoni_Claude\p109\shots` — оставить
до взгляда Amber; `D:\BqMoni_Claude\p109\deliver` — место для готовых матриц по `AMBER46`/`AMBER47`
(решение Amber 19.09.2026: «В копии, готовое — в D:\BqMoni_Claude\p109\deliver»).
