# rev55: подмена склада корпуса на `uc_full`, умолчания λ и L₀, объявление базы (13.11.2026, полоса `d55`)

Полоса `d55`, основное дерево, `master` `2caf244d`. Временное — `D:\BqMoni_Claude\d55\` (логи — `logs\`). Снимок прежнего
склада — `D:\BqMoni_Claude\store_backup_rev54\`. ⛔ Ничего не закоммичено; `TODO.md`, `DONE.md` и память не правлены —
готовые тексты для шапки `TODO.md` и памятки `corpus-base-current` лежат в `D:\BqMoni_Claude\d55\` (§8).

Решения Amber 13.11.2026, дословно:

* о сроке внесения `uc` — «Ты СЕЙЧАС, в течении 4-х часов это делал! НЕ НОЧЬЮ!!!! А СЕЧАС в полосе a315u!»;
* о посчитанном складе — «Если считался склад на какой-то опции, пример a315u. ТО НЕ НАДО ПЕРЕСЧИТЫВАТЬ ЕГО С НУЛЯ!
  ПЕРЕИСПОЛЬЗОВАТЬ ПОСЧИТАННОЕ НАДО!»;
* о ключах λ и L₀ (вопросником) — «Да, одним пересчётом»;
* о NBT (вопросником) — «rev55 без NBT, NBT потом (Рекомендую)». Ключи `AnchorShiftNewton`, `AnchorShiftBracket`,
  `AnchorShiftTolSigma` остаются ВЫКЛ, полоса их не трогала.

Основание — критик `crit55` (`D:\BqMoni_Claude\crit55\`): склад `uc_full` к подмене готов; rev55 = плечо `LL` полосы
`a315u` (полная known 583.24, малая 422.26, паспорт 13 из 80 / 1 из 41); подмена — только вместе с умолчаниями
`CoulombLambda = true` и `FermiFiniteSize = true`, иначе разбор возвращает код 68 и бета пропадает. Работа `a315u` —
[журнал](handover-2026-11-13-a315u.md) (перенесён в `handover/` этой полосой без правок).

## 1. Патч `a315u`

`git apply D:\BqMoni_Claude\a315u\final.patch` (sha256 `d32982f3…`; `--check` — код 0, наложение — код 0). Двенадцать
файлов: пять исходников ядра GPU и `README.md` ядра, `tools/check_gpu_dll.py` (поле `uc=` паспорта), четыре пробы
(`BetaSectionProbeA274.cs`, `CorpusFsaProbe.cs`, `CorpusMatrixProbe.cs`, `FsaStackShot.cs` — ключи `--clambda=`/`--l0=`) и
штатная `BecquerelMonitor/rmgpu_f.dll` (`4038f8a7…` → `e68730b6…`, паспорт `…;uc=1;`). После наложения все 12 файлов
побайтно равны файлам worktree `a315u`.

## 2. Умолчания λ и L₀

`BecquerelMonitor/FullSpectrumAnalysis/FsaSampleLibrary.cs`: `FsaBetaLibrary.CoulombLambda = true`,
`FsaBetaLibrary.FermiFiniteSize = true`; в описании — решение Amber дословно с датой и почему умолчание и склад меняются
только вместе. Комментарии о прежнем умолчании «ВЫКЛ» поправлены в `ResponseMatrix.cs` (клеймо раздела, две строки) и в
описании формы беты `FsaSampleLibrary.cs`.

Другие места прежнего умолчания (поиск по всему дереву, кроме `bin/obj/build*`):

* ключи проб `--clambda=`/`--l0=` (`BetaSectionProbeA274`, `CorpusFsaProbe`, `CorpusMatrixProbe`, `FsaStackShot`) ставят
  значение только при явном ключе — умолчания не дублируют, правки не нужно;
* `FermiFiniteSizeProbeA274.cs` и `CrystalOwnActivityProbeA274.cs` (пробы приёмки ключей, сторожами не зовутся) проверяли
  «умолчание — ВЫКЛ». Проверка теперь «умолчание — ВКЛ (rev55)»; сразу после неё оба ключа явно выключаются, а в конце
  проверок λ значение L₀ восстанавливается — замеры этих проб написаны от базы ВЫКЛ и без L₀, их числа прежние;
* `ResponseCrystalSection.cs` (клеймо слоя собственной активности кристалла) и кэш весов `Weights` читают ключи прямо —
  поле `cl=`/`l0=` появляется само.

⚠ Следствие для приложения (не для корпуса): матрица с разделом электрона, посчитанная до rev55 (клеймо без `cl=`/`l0=`),
разбором больше не берётся — раздел нужно пересчитать. Это касается разделов живого конфига Amber; по заданию их пересчёт
— отдельной полосой, `%AppData%\BecqMoni` полоса не трогала.

## 3. Снимок и подмена склада

Скрипт `handover/store55/store55_swap.py` — копия `handover/store54/store54_swap.py`, сменены только
`STORE = D:\BqMoni_Claude\a315u\store\uc_full` и `BACKUP = D:\BqMoni_Claude\store_backup_rev54` (и шапка).

    python -I handover\store55\store55_swap.py backup   # код 0: снимок 51 + 51, guid_map 51, расхождений 0
    python -I handover\store55\store55_swap.py swap     # код 0: перенесено 51 по ключу, 51 response; verify — 0 расхождений
    python -I handover\store55\store55_swap.py verify   # код 0, множества sha256 по ключу и по guid РАВНЫ

* До снимка: живой склад (51 `.rmx`) побайтно равен `D:\BqMoni_Claude\a315u\store\live\` — копии склада rev54, от которой
  мерила `a315u`.
* Снимок `D:\BqMoni_Claude\store_backup_rev54\`: 51 `.rmx`, `response\` 51, `sha256_rmx.txt`, `sha256_response.txt`,
  `guid_map.txt`.
* Подменённые `.rmx` git не отслеживает (`.gitignore`), в `git status` подмены не видно.
* Прочие склады плеч `a315u` (`uc0`, `uc_N`, `uc_L`, `secLL` …) не тронуты; из `a315u\` и `crit55\` ничего не удалено.

Логи — `logs\swap_backup.txt`, `swap_swap.txt`, `swap_verify.txt`.

## 4. Сборка

| что | команда | код |
|---|---|---|
| приложение Release | MSBuild … `/restore /t:Build /p:Configuration=Release … /p:OutputPath='bin\Release_Codex\' /p:IntermediateOutputPath='obj\Release_Codex\'` | 0 |
| пробы Release в `build_rel` | `build_all.ps1 -Bin BecquerelMonitor\bin\Release_Codex -Out tools\effmaker\probes\build_rel` | **3** |
| пробы Release в `build_rev55` | `build_all.ps1 -Bin BecquerelMonitor\bin\Release_Codex -Out tools\effmaker\probes\build_rev55` | 0, «каталог заверен (T226)» |
| приложение Debug | MSBuild … `Debug_Codex` (рецепт `CLAUDE.md`) | 0 |
| пробы Debug в `build` | `build_all.ps1` (умолчания) | 0 |
| `python -I -X utf8 tools/check_gpu_dll.py` | после обновления `tools\effmaker\gpu\bin\` (ниже) | 0, «СХОДИТСЯ»; `--selftest` — «ПРОЙДЕНА» |

* `build_rel` отказал по `T88`/`T79`: в каталоге лежат семь исполняемых вне плана от 10.09.2026 (`DosePointsProbeF9.exe`,
  `PackGeometryProbe.exe`, `Program.exe`, `ScoutProbe.exe`, `SetProbe.exe` …) — чужие, не этой полосы. По правилу
  «отказавший сторож — собирать в ДРУГОЙ каталог» пробы собраны в свежий `build_rev55`, и счёт корпуса шёл им; `build_rel`
  не тронут, файлы из него не удалялись.
* `check_gpu_dll` сначала дал 1: «`BecquerelMonitor\rmgpu_f.dll` и `tools\effmaker\gpu\bin\rmgpu_f.dll` — разные файлы»
  — в основном дереве остался вывод сборки ядра 12.11 (`4038f8a7`). Вывод `build_gpu.cmd` из worktree `a315u` (те же
  исходники; `rmgpu_f.dll` = `e68730b6…`, как штатная) скопирован в `tools\effmaker\gpu\bin\` (каталог вне git), прежние
  файлы — в `D:\BqMoni_Claude\d55\gpu_bin_old\`. После — код 0.

Логи — `logs\build_release.txt`, `build_all_rel.txt`, `build_all_rev55.txt`, `build_debug.txt`, `build_all_debug.txt`,
`check_gpu_dll.txt`.

## 5. Корпус на новом складе и новом умолчании

    & tools\CORPUS\scripts\mk_appwd.ps1 -Bin <абс>\BecquerelMonitor\bin\Release_Codex -ProbeBuild <абс>\tools\effmaker\probes\build_rev55 -Wd <абс>\tools\CORPUS\scripts\wd_rev55   # 0
    & tools\CORPUS\scripts\run_appwd.ps1 -Out <абс>\tools\pie\out_rev55_full -Wd <абс>\tools\CORPUS\scripts\wd_rev55                                                     # 0, 339 с
    & tools\CORPUS\scripts\run_mini.ps1  -Out <абс>\tools\pie\out_rev55_mini -Wd …wd_rev55 -Bin …Release_Codex -ProbeBuild …build_rev55                                  # 0, 243 с

Ключей разбора нет — λ и L₀ теперь умолчания (у плеча `LL` они шли ключами `--clambda=1 --l0=1`). Счёт короче получаса —
отсоединённый запуск не понадобился. Клейма `.run.json`: `corpus=5fd9c70d…` (тот же корпус), `sources=5edb0210…`,
`head=2caf244d`, `dirty` — `ResponseMatrix.cs`, `FsaSampleLibrary.cs`, `CorpusFsaProbe.cs`.

### Сверка с плечом `LL` полосы `a315u`

Сравнитель — `handover/p343-rev48/cmp_runs.py` (все поля `*_spline_runs.csv` и все прочие `*_spline_*.csv` по спектру),
исключены `ms`, `cpu_ms`, `anchor_note`; логи `logs\cmp_*.txt`.

| пара | спектров | сдвинутых строк прогона | прочих файлов разошлось | файлы только в одном |
|---|---|---|---|---|
| `out_a315u_full_LL` ↔ `out_rev55_full` | 128 / 128 | **0** | **0** | нет |
| `out_a315u_mini_LL` ↔ `out_rev55_mini` | 59 / 59 | **0** | **0** | нет |
| те же пары, `anchor_note` НЕ исключён | 128 / 59 | 0 / 0 | 0 / 0 | — |
| положительный контроль: `out_rev54_full` ↔ `out_rev55_full` | 128 / 128 | 80 | 87 | — |
| `out_rev54_mini` ↔ `out_rev55_mini` | 59 / 59 | 35 | 40 | — |

**Числа rev55 совпали с плечом `LL` побитно, включая `anchor_note`.** Сравнитель видит сдвиг против rev54 — сравнение не
пустое.

### Лестница rev54 → rev55

`D:\BqMoni_Claude\d55\ladder.py` (копия `handover/store54/ladder.py`, путь логов — `d55`), выводы `logs\ladder_full.txt`,
`ladder_mini.txt`; Σχ²/ndf / χ² Пуассона, части не складываются.

| | `out_rev54` | `out_rev55` |
|---|---|---|
| **полная, known** (92 из 92) | 588.33 / 5965.02 | **583.24 / 5969.19** |
| полная, паспорт known: вне / Σ выходов | 13 из 80 / 130.2 п.п. | 13 из 80 / 130.4 п.п. |
| полная, unknown (2 из 36) | 249.64 / 414.12 | 249.64 / 414.12 |
| **малая, known** (45 из 45) | 427.33 / 4328.27 | **422.26 / 4332.65** |
| малая, паспорт known | 1 из 41 / 14.6 п.п. | 1 из 41 / 14.6 п.п. |
| малая, unknown (1 из 14) | 78.95 / 184.45 | 78.95 / 184.45 |

Ровно числа критика `crit55`. Полная: χ² сдвинут у 66 из 128, все known; наибольшие — `G1S24_Na22_P5` 97.899 → 93.044
(Пуассон 610.66 → 615.89; шум склада в неустойчивом взвешенном фите, журнал `a315u` §5), `G1S24_K40_Petri_2` −0.82 %,
`G1S24_K40_Mar_2` +0.72 %, `G1S24_Y88_P25` −0.61 %, `G1S24_Th232_Denta120` +0.57 %; паспорт — 51 отношение из 80, наибольшее
|Δ| 0.0033 (`G1S24_Na22_P5`), число «вне» то же. Малая — 30 из 59. Непонятные побитно (матрицы у них нет —
отрицательный контроль).

`score.py --members`: полная понятная 583.2 / 1.54, recall 100 %, фантомов 0; полная непонятная 249.6 / 124.82, 4 %;
малая понятная 422.3 / 2.76, 100 %, 0; малая непонятная 79.0 / 78.95, 4 %; матрица найдена и применена 92/92 и 45/45.

## 6. Витрина FSA

`python tools/check_fsa_showcase.py --limit=0` (до переобъявления) — код 1, лог `logs\showcase.txt`: 8 пар из 11 совпали с
эталоном 2026-11-12 23:23, три разошлись, у всех сторож называет причину «вход изменился: матрица». Строки таблицы
сторожа побитно равны таблице плеча `LL` (`D:\BqMoni_Claude\a315u\logs\showcase_LL.txt`; 547 строк, разнятся только
времена прогона). Свой склад витрины (радоновый фильтр) раздел не потерял — пары `ASN16_Radon_filter2` совпали.

| пара | матрица (сцена) | расхождений | наибольшие «было → стало» |
|---|---|---|---|
| `AS80_Th232_disk / infer_eq` | `AS80_th_disk` `d43c63d9…` → `5cc47879…` | 200 | 30–100 кэВ Xray-La 572.29 → 587.72 (+2.70 %); 0–30 кэВ tail 231.30 → 236.48 (+2.24 %); 0–30 кэВ Xray-La 1389.43 → 1419.44 (+2.16 %); 100–300 кэВ Xray-Pb 104.95 → 106.98 (+1.94 %); 100–300 кэВ missing_net 30580.4 → 30975.7 (+1.29 %) |
| `G1S24_Rn222Coal_eq01 / sample_eq` | `G1S_mar1l_coal_046_p24` `49dc2d35…` → `9e7777c9…` | 162 | 100–300 кэВ missing_net 1237.24 → 1242.94 (+0.46 %); 30–100 кэВ excess_net 618.94 → 616.25 (−0.43 %), Bi-212 168.59 → 167.86 (−0.43 %) |
| `G1S24_Rn222Coal_eq01 / sample_noeq` | то же | 185 | 0–30 кэВ tail 96.83 → 93.70 (−3.24 %, хвост Pb-214 — шум матрицы, `store54` §12.4); 30–100 кэВ missing_net 916.09 → 936.18 (+2.19 %); excess_net 813.08 → 823.54 (+1.29 %) |

Ожидание задания подтвердилось: ториевый диск Xray-Pb и Xray-La ≈ +2 %, угольная маринелли `sample_noeq` tail ≈ −3 %.

`& tools\fsa_showcase\snapshot.ps1` — код 0, «ЭТАЛОН ОБЪЯВЛЕН 2026-11-13 21:44 (HEAD 2caf244d): 11 пар, из них с
изменившимися числами 3»; прочие 8 — «числа те же» (переписано клеймо). Полная таблица 547 строк —
[showcase_rev55_diff.md](store55/showcase_rev55_diff.md) (тем же `compare()` сторожа, `D:\BqMoni_Claude\decl54\showdiff.py`).
Файлы эталона: `tools/fsa_showcase/reference/*.json` — 11 изменены (3 — числа и клеймо, 8 — только клеймо).

## 7. Объявление в `tools/CORPUS/README.md`

Правка — скриптом `D:\BqMoni_Claude\d55\readme_rev55.py`: заголовок «✅ ДЕЙСТВУЮЩАЯ БАЗА: 13.11.2026 (вечер) — rev55 …» с
решениями Amber дословно; таблица на `out_rev55_*`; строка отпечатка `corpus=5fd9c70d…` · `sources=5edb0210…` без `head=`
(стенд грязен — правки и объявление одним коммитом), с происхождением прогона, контролем и историей rev54 без пар в
обратных кавычках; абзац «Чем посчитано (rev55)» (прежний — «склад rev54 — история»); пункт 19 «rev54 … СНЯТА
13.11.2026», новый пункт 20 с лестницей; в «Как воспроизвести» — строка rev55 и плечо «склад rev54».

* `python tools/check_declared_base.py` — **код 0**: 4 строки сошлись, корпус и сборка (620 файлов, рабочее дерево)
  сошлись, клейма обоих каталогов сошлись (`logs\check_declared_base.txt`).
* `python tools/check_corpus_readme.py` — **код 0** (R1–R4 — 0).

⚠ Отпечаток `sources=` судится по рабочему дереву, пока строка не закоммичена, а после коммита — по коммиту. В коммит
обязаны войти все файлы набора разбора, изменённые здесь (`FsaSampleLibrary.cs`, `ResponseMatrix.cs`, `CorpusFsaProbe.cs`
и прочие из патча), иначе `check_declared_base` после коммита покраснеет.

## 8. Тексты для шапки `TODO.md` и памятки (правит распорядитель)

Собраны и проверены скриптом `D:\BqMoni_Claude\d55\mkreg55.py`: копии `TODO.md` со вставкой, `README.md` и памятки во
временном корне `D:\BqMoni_Claude\d55\regroot55\`, функция `check_registry.check_corpus_base` — «находок 0», все три места
— 13.11.2026 (вечер), `out_rev55_*`, вершина не названа (грязный стенд). Контроль — тот же корень с нынешним `TODO.md`
(rev54): «РАЗНЫЕ БАЗЫ», находок 1.

* строка 119 `TODO.md` целиком — `D:\BqMoni_Claude\d55\todo_line119_rev55.txt` (новый префикс rev55 + прежний текст rev54,
  переведённый в «⏮ rev54 (12.11.2026, ночь; СНЯТА 13.11.2026) = …»);
* строки таблицы 123–126 — `D:\BqMoni_Claude\d55\todo_rows_rev55.txt`;
* памятка целиком — `D:\BqMoni_Claude\d55\corpus-base-current.rev55.md` (новый абзац сверху, прежний — «rev54
  (`out_rev54_*`, СНЯТА 13.11.2026) = …», таблица, `description`).

Ссылки вставки на журналы (`handover/handover-2026-11-13-rev55.md`, `handover/handover-2026-11-13-a315u.md`) должны быть
в индексе git, иначе `check_registry` сочтёт их «есть на диске, git не знает» — оба добавлены `git add`.

## 9. `check_all`

`D:\BqMoni_Claude\a318\HiddenRun.exe 3000 python tools/check_all.py` после подмены, README, витрины и `git add` журналов
(лог `D:\BqMoni_Claude\d55\logs\check_all.txt`) — **код 1: «ОСТАНОВ: отказали 1 из 48 — tools/check_registry.py (код 1)»**,
зелёных 47, в том числе `check_declared_base`, `check_corpus_readme`, `check_corpus_scenes`, `check_gpu_dll`,
`check_electron_recipe`, `check_fsa_showcase` (369.8 с — витрина зелёная на новом эталоне), `check_fsa_report_view`
(473.2 с). Единственная находка `check_registry` — «РАЗНЫЕ БАЗЫ: `out_rev54_*` (шапка `TODO.md`) против `out_rev55_*`
(README)»: ожидаемое состояние до вставки распорядителя в `TODO.md` и памятку (§8); вставка проверена на временном корне —
находок 0.

## 10. Как откатить

1. `python -I handover\store55\store55_swap.py restore` — снимок rev54 обратно в живой склад (обе половины), сверка.
2. Умолчания `FsaBetaLibrary.CoulombLambda` и `FermiFiniteSize` — обратно `false` (иначе разбор склада rev54 не возьмёт
   раздел: при ВКЛ раздел без поля `cl=`/`l0=` не годен), проверки «умолчание — ВКЛ» двух проб приёмки — обратно.
3. Штатная `rmgpu_f.dll` — `git checkout` прежней (`4038f8a7…`) вместе с исходниками ядра и `check_gpu_dll.py`, если
   откатывается и `uc`; `tools\effmaker\gpu\bin\` — из `D:\BqMoni_Claude\d55\gpu_bin_old\`.
4. Эталон витрины — `git checkout tools/fsa_showcase/reference`; README — `git checkout tools/CORPUS/README.md`.

## 11. Файлы

* Изменены, в git: патч `a315u` (12 файлов); `BecquerelMonitor/FullSpectrumAnalysis/FsaSampleLibrary.cs`,
  `BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs`, `tools/effmaker/probes/FermiFiniteSizeProbeA274.cs`,
  `tools/effmaker/probes/CrystalOwnActivityProbeA274.cs`; `tools/CORPUS/README.md`; `tools/fsa_showcase/reference/*.json`
  (11).
* Новые, для git (добавлены `git add`): этот журнал; `handover/handover-2026-11-13-a315u.md`;
  `handover/store55/store55_swap.py`; `handover/store55/showcase_rev55_diff.md`.
* Вне git: живой склад `tools/CORPUS/corpus/geometries/*.rmx` и `response/` (подменены); `tools/pie/out_rev55_full`,
  `out_rev55_mini`; оснастка `tools/CORPUS/scripts/wd_rev55`; пробы `tools/effmaker/probes/build_rev55`; сборки
  `bin\Release_Codex`, `obj\Release_Codex` (сняты не были — база на них объявлена); `tools\effmaker\gpu\bin\` (обновлён);
  рабочий `tools/fsa_showcase/wd/`.
* Не тронуты: `TODO.md`, `DONE.md`, память, `*.sqlite`, поставочные `config/*`, `%AppData%\BecqMoni`, `D:\BqMoni_Claude\a315u\`,
  `D:\BqMoni_Claude\crit55\`, `tools\effmaker\probes\build_rel`.
