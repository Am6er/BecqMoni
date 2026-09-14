# Полоса П40, 13.09.2026 — умолчания трёх полей физики 16 у `EfficiencySimulator` = умолчаниям склада (правило I на десять полей)

Решение Amber 13.09.2026, вопросником, дословно: **«Да, все три поля — одна физика целиком»** — ответ на
вопрос П38 (§10.7 её [журнала](handover-2026-09-13-p38-store-swap-corpus-rev21.md)): распространить
«одно место истины» (решение того же дня «Перевернуть — одна физика для всех», семь полей физики 17) на три
поля ключей физики 16, у которых умолчание поля симулятора оставалось своё (ВЫКЛ) при умолчании склада ВКЛ:
`LightSubKevCurve` и `LightCascadeSplit` (половины K-провала света, у склада `KDipLight = 1`) и
`SplitXrayShells` (разведение K/L-вылета по каналам).

`TODO.md`/`DONE.md` не тронуты, `git add`/`commit` не делались. Разделитель дробной части — точка.
Переводы строк считаны байтами: `EfficiencySimulator.cs` BOM + LF-целиком (CR 0, LF 7525 → 7545),
`check_matrix_keys.py` CRLF-целиком (CR = LF, 860 → 941).

## 0. Паспорт

| что | значение |
|---|---|
| дерево | ветка `pie`, `HEAD` = `83f79e86` (П38). Рядом чужое незакоммиченное: `FsaStackShot.cs`, снятый `app-silent-failures.md`, `handover/*mix-stack*` — не трогались |
| мои файлы | `BecquerelMonitor/EfficiencyMaker/EfficiencySimulator.cs`, `tools/check_matrix_keys.py`, этот журнал, `handover/p40-sim-defaults/` |
| сборка | `handover/p40-sim-defaults/build_p40.ps1` (`pwsh -File`, коды — `codes.txt`): приложение `bin\Release_p40` + `obj\Release_p40` **код 0** (09:57:00, ошибок 0), пробы `build_all.ps1 -Out build_p40` **код 0** (09:58:05: «каталог заверен (T226): приложение 4e0bc44042f8 (549), пробы 04d4de81f8b3 (188)»); sha256 exe приложения = exe каталога проб (`0967539c…`) |
| плечо «до» | `tools/effmaker/probes/build_p38` — сборка П38 от кода `HEAD` (exe `f43bfdf6…`, тот же, что в `codes.txt` П38): у неё три поля ещё ВЫКЛ — отражением подтверждено (§3.2) |

## 1. Что изменено в `EfficiencySimulator.cs`

Три поля берут умолчание тем же оборотом, что семь полей П38 — литерал остаётся ОДИН, в инициализаторе
поля `ResponseMatrixOptions`:

| поле | было | стало |
|---|---|---|
| `SplitXrayShells` | `public bool SplitXrayShells;` (пусто = `false`) | `= new ResponseMatrixOptions().SplitXrayShells;` |
| `LightSubKevCurve` | `= false;` | `= ResponseMatrixOptions.KDipCurveHalf(new ResponseMatrixOptions().KDipLight);` |
| `LightCascadeSplit` | `= false;` | `= ResponseMatrixOptions.KDipCascadeHalf(new ResponseMatrixOptions().KDipLight);` |

Раскладка уровня `KDipLight` на две половины — **та же функция**, которой её берут построитель
(`ResponseMatrixBuilder.MakeSimulator`: `KDipCurveHalf(options.KDipLight)`) и кривая
(`EfficiencyCalculation.Run`: `KDipCurveHalf(storePhysics.KDipLight)`), — `ResponseMatrixOptions.KDipCurveHalf(int)`
/ `KDipCascadeHalf(int)`; своей формулы `level == 1 || level == 2` у поля нет (`S37`: второе правило для
одной величины разъехалось бы молча). Комментарии трёх полей переписаны по образцу П38 (умолчание ПОЛЯ —
умолчание СКЛАДА, решение Amber 13.09.2026 дословно, П40; прежние числа — явным `= false` либо
`--kdip=0` там, где ключ есть); у `SplitXrayShells` абзац «Умолчание ВЫКЛЮЧЕНО, и это не отмена
решения…» переписан историей (склад ВКЛ с единого счёта П20, поле — с П40). Три комментария в теле
(`ResponseChannel.EscapeXrayK`, `EscapeXrayL`, `ChannelOf`), говорившие «умолчанием ключ выключен», —
поправлены. Код розыгрыша и каналов не трогался: `git diff` — 51 вставка / 31 удаление, все в
`///`-комментариях и трёх инициализаторах.

## 2. Сторож `tools/check_matrix_keys.py` — правило I на десять полей

* `ONE_TRUTH` — теперь кортежи `(поле симулятора, поле склада, раскладка)`: семь П38 с раскладкой `None`
  (ждётся дословно `new ResponseMatrixOptions().<поле>`), плюс `('LightSubKevCurve', 'KDipLight',
  'KDipCurveHalf')`, `('LightCascadeSplit', 'KDipLight', 'KDipCascadeHalf')` (ждётся дословно
  `ResponseMatrixOptions.<раскладка>(new ResponseMatrixOptions().KDipLight)`), `('SplitXrayShells',
  'SplitXrayShells', None)`. Ожидаемый текст строит `one_truth_text()`.
* Правило I дополнено: (1) раскладка обязана существовать в `ResponseMatrixOptions` как
  `public static bool <имя>(int …)`; (2) **оба штатных пути** (`MakeSimulator`, `Run`) обязаны ставить
  половину K-провала ТОЙ ЖЕ раскладкой — правило G видит лишь «ставит / не ставит», и своя формула
  `options.KDipLight == 1 || …` в построителе прошла бы у него как «ставит». Судится источник: хотя бы
  одно присваивание в теле зовёт `<раскладка>(` (у кривой поле ещё и копируется в клеймо
  `LightSubKevCurve = simulator.LightSubKevCurve`).
* Реестр `SIM`: доводы трёх строк дописаны (умолчание поля с 13.09.2026 — умолчание склада, правило I,
  П40); у `SplitXrayShells` причина «намеренно» осталась правдивой — кривая ключ не ставит и не видит
  (каналов у неё нет), а умолчание поля теперь и у неё `xrkl=1`, невидимый.
* Самопроверка: **7 → 10 порч** — добавлены (8) `LightCascadeSplit = false` у симулятора, (9) построитель
  раскладывает `KDipLight` своей формулой мимо `KDipCurveHalf`, (10) `SplitXrayShells` без
  инициализатора; счётчик `SPOILS = 10` сверяется с числом подставленных порч (печать не врёт при
  следующей порче). Шапка — правило I переписано с обоими решениями Amber.
* **Живое дерево: код 0** — «самопроверка: 10 подставленных порч пойманы», «одно место истины (правило I):
  10 полей симулятора берут умолчание склада (…, LightSubKevCurve <- KDipCurveHalf(KDipLight),
  LightCascadeSplit <- KDipCascadeHalf(KDipLight), SplitXrayShells)».
* **Положительный контроль сверх самопроверки** (`handover/p40-sim-defaults/guard_on_head.txt`): сторож
  П40, которому подсунут `EfficiencySimulator.cs` версии `HEAD` (до П40), — **3 нарушения правила I**,
  ровно три поля (`LightSubKevCurve` «false», `LightCascadeSplit` «false», `SplitXrayShells` «(пусто)»),
  ничего лишнего.

## 3. Контроли

### 3.1 (а) Склад не тронут — матрица `AS80_point0` умолчаниями против живого склада

`handover/p40-sim-defaults/ctrl_a.ps1`: копия `.in` в scratch (`…\scratchpad\p40\ctrl_a`), `build_p40\CorpusMatrixProbe
--threads=10 --target=0 --force` без единого ключа физики (09:59:28 → 10:04:58; 329.3 с на часах, 7.64 мкс на
историю, ядер 9.7 из 10, шум 0.60 %), **код 0 «ВСЕ СОШЛИСЬ»**; `MatrixDiffProbe` против живого
`tools/CORPUS/corpus/geometries/AS80_point0.rmx` **код 0** (`ctrl_a_matrix.log`, `ctrl_a_diff.log`):

| что | живой склад (П37/П38) | build_p40, умолчаниями |
|---|---|---|
| клеймо | `phys=17;541d94fe24de030dbe1d0374f01e44c65eba580838b032cc2c3d54d141fd0ff6` | **то же** |
| отпечаток тела | `e893d1dc78436500…` | **`e893d1dc78436500…` — ТЕЛА ТОЖДЕСТВЕННЫ** (тот же отпечаток, что у контроля П38) |
| пик / сумма / форма L1 по 140 узлам | — | 0.000 / 0.000 / 0.00; доля пика 0.7531 = 0.7531; смещение 0.000 ± 0.000 |
| sha256 живого `.rmx` до / после | `cf2dec48…` | `cf2dec48…` — **склад не тронут** (проба писала в scratch: `16916ecb…`) |

Вердикт: путь склада ставит все три поля явно от `ResponseMatrixOptions`, умолчания полей симулятора его
не трогают — ни бита. Четвёртый за двое суток положительный контроль детерминированности многопоточного
счёта (три сборки П37/П38/П40, одно тело).

### 3.2 (б) Прямой вызов сменился — отражение и пробы света

**Отражение из сборок** (`reflect.ps1` → `sim_defaults_reflection.txt`; `Assembly.LoadFrom`,
`new EfficiencySimulator(null)` против `new ResponseMatrixOptions()`, `PhysicsVersion=17` у обеих):

| каталог | `LightSubKevCurve` | `LightCascadeSplit` | `SplitXrayShells` | семь полей П38 | итог правила I |
|---|---|---|---|---|---|
| `build_p38` (HEAD, до) | sim=False / store=True | sim=False / store=True | sim=False / store=True | сошлись | **разошлись 3** |
| `build_p40` (после) | True / True | True / True | True / True | сошлись | **разошлись 0** |

**`LightAnchorProbe`** — проба света, которая без ключей берёт УМОЛЧАНИЯ СИМУЛЯТОРА, а с `--store` ставит
`SplitXrayShells` и обе половины K-провала от `ResponseMatrixOptions` (как строитель склада). Четыре плеча,
одно зерно, `AS80_point0.in`, `--energies=20,30,32,33.5,34,36,40,59.5,100,661.657 --n=200000 --bin=1`
(`ctrl_b.ps1`, логи `ctrl_b_anchor_*.log`, все коды 0). Столбец «свет/E» (якорь световой шкалы):

| E, кэВ | build_p38 без ключей (старое умолчание) | build_p38 `--store` | **build_p40 без ключей** | build_p40 `--store` |
|---|---|---|---|---|
| 20 | 1.142507 | 1.110804 | **1.110804** | 1.110804 |
| 32 | 1.107320 | 1.092420 | **1.092420** | 1.092420 |
| 33.5 | 1.090254 | 1.052720 | **1.052720** | 1.052720 |
| 34 | 1.113490 (ступенька ВВЕРХ над K-краем) | 1.071020 (провал) | **1.071020** | 1.071020 |
| 59.5 | 1.074684 | 1.066374 | **1.066374** | 1.066374 |
| 661.657 | 1.009376 | 1.007832 | **1.007832** | 1.007832 |

Сверка механически (`diff` таблиц): **build_p40 без ключей = build_p38 `--store` — 0 строк из 10 разошлось;
build_p40 без ключей = build_p40 `--store` — 0 из 10** (ключ `--store` стал пустым по построению);
**положительный контроль: build_p38 без ключей ≠ build_p38 `--store` — 10 строк из 10 разошлись** (и по свету,
и по счётчикам класса: 4400 против 4461 историй на 20 кэВ — каскад тянет свои случайные числа).

**`LightScaleProbe`** (build_p40, те же энергии, `ctrl_b_scale_p40_*.log`, коды 0) — абляция жива:
`--kdip=1` даёт K-провал (33.5 кэВ **1.0527**, 34 кэВ **1.0482** против 1.0903 / 1.0906 при `--kdip=0`;
шапка «NaI:Tl [Payne η=0.33 (база), ниже 1 кэВ Joy-Luo от 0.01, обрыв E_q=1 кэВ]»), `--kdip=0` — прежняя
кривая (ступенька вверх). ⚠ **Без ключа эта проба даёт `--kdip=0`, а НЕ умолчание поля** — посылка
задания («без ключей теперь даёт кривую с K-провалом») у этой пробы не сбывается по её же коду: `int kdip = 0`
и явные `sim.LightSubKevCurve = kdip == 1 || kdip == 2; sim.LightCascadeSplit = kdip == 1 || kdip == 3;`
перекрывают умолчание поля и дублируют раскладку своей формулой (§9). Проба, где смена умолчания видна, —
`LightAnchorProbe` (выше).

### 3.3 (в) `check_all.py`

`python tools/check_all.py` → **«ВСЕ ЗЕЛЕНЫ: 37 из 37 сторожей дали 0», код 0** (`check_all.log`;
`check_matrix_keys.py` — код 0, 2.3 с).

## 4. Пробы прямого вызова, у которых меняется свет / раскладка

`handover/p40-sim-defaults/direct_callers.py` → `direct_callers.txt`: `new EfficiencySimulator(` стоит в
**38 файлах** (два пути приложения, `MeasuredPoint.cs`, `Simulate.cs`, 34 пробы). Ставят половины K-провала
сами: `EfficiencyCalculation`, `ResponseMatrixBuilder`, `G4RawProbe` (от `store.KDipLight`, ключ `--kdip=`),
`LightAnchorProbe` (`--store`), `LightScaleProbe` (свой `kdip=0`, ключ `--kdip=`); `CorpusMatrixProbe` — через
`options.KDipLight` построителя. Ставят `SplitXrayShells` сами: `ResponseMatrixBuilder`, `CorpusMatrixProbe`
(`--xrkl=`), `LightAnchorProbe` (`--store`), `ResponseChannelProbe` (оба плеча явно — её проверка «без ключа
канал L пуст ровно» остаётся верной).

**Не ставят ни половин K-провала, ни `SplitXrayShells` и считают отклик — 25 проб, с 13.09.2026 считают
как склад:** `BoxSourceProbe`, `CoincCfProbe`, `GadrasProbe`, `GapProbeAmber1`, `IncidenceResponseProbe`,
`IsoFieldProbe`, `KappaPeakTotalProbe`, `LayerProbe`, `MarinelliSelfAbsProbeE10`, `MaterialNameProbeF67`,
`MaterialOrderProbe`, `MineProbe`, `OmegaProbe`, `PeakAgreeProbe`, `ResponseInterpProbe`, `ResponseMatrixProbe`,
`ResponseNoiseProbe`, `ResponseProbe`, `ResponseShapeProbe`, `RoundTrip`, `SceneCostProbe`, `MeasuredPoint.cs`,
`Simulate.cs` (+ `BoundScatterProbe`, `CascadeJointProbe`, `FacingProbe`, `RawCarryProbe`, `SbKeysProbe`,
`BoxCylStampProbeF64`, `BoxDeadFieldsProbe`, `SceneOfSpectrumProbe` строят симулятор не для отклика —
клеймо, имена веществ, порядок элементов; у части из них отклик считает `ResponseMatrixBuilder`, и поля
ставит он). Что именно меняется: K-провал двигает положение всего ниже ~120 кэВ относительно пика
(перекладка строки по свету, `RemapLightScale`) и меняет поток случайных чисел там, где разыгран оже-каскад
(`LightCascadeSplit`); `SplitXrayShells` перекладывает L-вылет из канала 3 в канал 5 (читают каналы —
только `IncidenceResponseProbe` из этого списка). Сумма отклика и пик выше ~120 кэВ — в пределах шума.

**Как воспроизвести прежние числа:** в коде пробы явно `LightSubKevCurve = false; LightCascadeSplit = false;
SplitXrayShells = false;` (плюс шесть присваиваний П38 §1.5 для физики 17); у `LightScaleProbe` и `G4RawProbe`
— ключом `--kdip=0`; у `LightAnchorProbe` прежнее плечо «умолчания симулятора» (П1 10.09.2026) ключом уже НЕ
воспроизводится (§9). Ни одна из 25 проб этой полосой не перепроверялась: их числа лежат в журналах с датой.

## 9. Находки — по порядку §9 навыка (новых строк реестра — 0; два кандидата дописки в родителя `F11`)

| находка | что сделано | разряд §9 / почему не строка |
|---|---|---|
| Посылка задания для контроля (б) («`LightScaleProbe` без ключей теперь даёт кривую с K-провалом») ШИРЕ реальности: проба ставит обе половины сама от своего `int kdip = 0` и дублирует раскладку формулой `kdip == 1 \|\| kdip == 2` / `kdip == 1 \|\| kdip == 3` вместо `KDipCurveHalf`/`KDipCascadeHalf` — умолчание поля до неё не доезжает, без ключа она = `--kdip=0`. Починка — две строки (`int kdip = new ResponseMatrixOptions().KDipLight;` и раскладка функциями склада, как у `G4RawProbe`), с положительным контролем «без ключа = `--kdip=1` до знака» | ничего: файл не мой; контроль (б) снят на `LightAnchorProbe`, где умолчание симулятора берётся честно (§3.2) | п. 2 — **дописка в `F11`** (§10.1), не строка: минуты, но чужой файл; отдельной строки не заслуживает |
| `LightAnchorProbe`: доводы шапки (строки 42–49) и печать «умолчания симулятора (как П1 10.09.2026) … плечо воспроизводимости прежних чисел» с 13.09.2026 неверны — ключ `--store` стал пустым по построению (§3.2: 0 строк из 10 разошлось), а плечо П1 без ключа больше не воспроизводится | ничего: файл не мой | п. 2 — той же допиской в `F11`; чинить вместе с предыдущей находкой (одна полоса, одна тема) |
| `IncidenceResponseProbe` (чужая, закоммичена в `HEAD`) читает каналы `ResponseByChannel` и не ставит `SplitXrayShells`: с этого дня канал 5 (L) у неё непуст, а канал 3 теряет L-вылет — числа в её журнале от старой раскладки | факт здесь и в §4 | п. 3 — факт; это и есть смысл решения «одна физика целиком» |
| 25 проб прямого вызова с сегодняшнего дня считают свет с K-провалом (и каскад своим потоком чисел) — их старые числа воспроизводятся только явным `= false` в коде | перечень и рецепт — §4 | п. 3 — факт; ровно то же, что П38 §1.5 сделала для семи полей |
| `ResponseChannelProbe` от смены умолчания не зависит (оба плеча ставят `SplitXrayShells` явно) | проверено по коду (`Make(geometry, histories, splitXray)`) | п. 3 — факт |
| Сборка приложения из чистого `obj\Release_p40` заняла 4 с (09:56:56 → 09:57:00) — как у П38 (3 с); exe новый (sha `0967539c…` ≠ `f43bfdf6…` П38, время 09:57:00), лог без ошибок | принято по sha и отражению (§3.2), не по времени | п. 3 — свойство инструмента |

## 10. Готовые тексты для распорядителя (`TODO.md` полосой НЕ правился)

### 10.1 Дописка в строку `F11` (пункт (а) — K-провал)

> ➕ **13.09.2026 (П40): умолчания полей симулятора для K-провала и `SplitXrayShells` = умолчаниям склада (решение Amber «Да, все три поля — одна физика целиком»)** — `LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(new ResponseMatrixOptions().KDipLight)`, `LightCascadeSplit = …KDipCascadeHalf(…)`, `SplitXrayShells = new ResponseMatrixOptions().SplitXrayShells`; правило I `check_matrix_keys.py` — 10 полей (самопроверка 10 порч, сторож на симуляторе версии `HEAD` красен ровно по трём). Контроли ([журнал](handover/handover-2026-09-13-p40-sim-defaults-phys16.md)): матрица `AS80_point0` умолчаниями = живой склад до бита (тело `e893d1dc78436500…`, клеймо то же, sha живого `.rmx` не менялся); `LightAnchorProbe` без ключей = `--store` 10 узлов из 10 до шестого знака (плечо `build_p38` без ключей расходилось 10 из 10 — положительный контроль); `LightScaleProbe --kdip=0/1` — абляция жива (33.5 кэВ 1.0903 → 1.0527); `check_all` 37/37. С этого дня 25 проб прямого вызова считают свет с K-провалом, `IncidenceResponseProbe` — L-вылет в канале 5; прежние числа — явным `= false`. ⚠ Остаток (чужие файлы, минуты): `LightScaleProbe` ставит половины сама от своего `kdip = 0` своей формулой (`kdip == 1 || kdip == 2`) — без ключа даёт `--kdip=0`, а не умолчание поля; взять `new ResponseMatrixOptions().KDipLight` и раскладку `KDipCurveHalf`/`KDipCascadeHalf`, как у `G4RawProbe`; у `LightAnchorProbe` шапка и печать «умолчания симулятора — плечо воспроизводимости П1» устарели (`--store` пуст по построению).

### 10.2 Строк реестра не заводится

Оба остатка — чужие файлы ценой в минуты и одной темы с `F11` (а); по §9 п. 2 они дописаны в родителя, а не
заведены соседками.

## 11. Что осталось на диске от полосы

| путь | что |
|---|---|
| `BecquerelMonitor/bin/Release_p40`, `BecquerelMonitor/obj/Release_p40`, `tools/effmaker/probes/build_p40` | сборка (вне git) |
| `handover/p40-sim-defaults/` | `build_p40.ps1`, `ctrl_a.ps1`, `ctrl_b.ps1`, `reflect.ps1`, `direct_callers.py`; `codes.txt`, `build_app.log`, `build_probes.log`, `guard_on_head.txt`, `sim_defaults_reflection.txt`, `ctrl_a_matrix.log`, `ctrl_a_diff.log`, `ctrl_b_anchor_{p38,p40}_{plain,store}.log`, `ctrl_b_scale_p40_{nokey,kdip0,kdip1}.log`, `check_all.log`, `direct_callers.txt` |
| scratch (`…\scratchpad\p40\ctrl_a`) | копия `AS80_point0.in` + `.rmx` контроля (а) — вне дерева; `edit_sim.py`, `edit_guard.py` — скрипты правок (байтовые, с якорями) |
