# П206 (01.10.2026): ночной счёт склада 49 сцен — слияние П199 + П200, проверка двух сцен, запуск

Команда Amber 01.10.2026, дословно: **«Запускай ночной счет, как полоса освободится»**. Пока идёт
счёт — компьютер не выключать и из системы не выходить (`T262`: выключение убивает счёт, сон — нет).

## 1. Ветка `night-1001`

Worktree `D:\BqMoni_Claude\p206\wt`, ветка `night-1001` от `master` `53b7b992`:

| коммит | что |
|---|---|
| `8d6e5c0e` | слияние `p199-format11` (`070a8e1d`): κ пар `AMBER147`, ε_p по разрешению `AMBER145`, формат 11 — без конфликтов |
| `d0cee18c` | слияние `p200-light` (`7aec6034`): кривая света в `matdb.sqlite`, таблицы `FsaLightScale` `AMBER152` — без конфликтов (git слил `FsaAnalyzer.cs` и `CorpusMatrixProbe.cs` сам) |
| `1f3a2cb9` | патч П199 `summer_amber145.patch` на `FsaCascadeSummer.cs` (смещение 10 и 136 строк от ускорения П197 `T264`, смысл тот же: `peak[i]` в `Create` — `PeakEfficiencyResolution`, если массив есть) и шапка класса тем же движением |

Проверка слияния: набор изменённых строк `git diff master HEAD -- FsaAnalyzer.cs CorpusMatrixProbe.cs` (122)
совпал построчно с объединением правок обеих веток от `612a4463` (122) — ни одна сторона не потеряна.
`matdb.sqlite` = блоб `p200-light` (`83f525f0…`), `nucdb.sqlite` = блоб `master` (П196/П202). Эталоны витрины
`tools/fsa_showcase/reference/*` ветки не трогали — остались от `master`.

## 2. Сборка

Release `bin\Release_p206` / `obj\p206`, `/restore`, `GenerateManifests=false` — код 0; `build_all.ps1 -Out
D:\BqMoni_Claude\p206\build_night` — **код 0**, 287 проб (+5 довеском), sha256 exe приложения = exe проб
(`875EB70E2E94CC00…`), `matdb.sqlite` каталога = worktree, отпечаток переноса **6110517f284f3d33** (`Fp.exe` П200).
Журнал сборки — `D:\BqMoni_Claude\p206\build_all.log`.

## 3. Рецепт

`D:\BqMoni_Claude\p206\store_run.cmd` (ASCII, CRLF): дальние `RC103_point50,ASN16_point10_house,G1S_point25`
`--n=6000000`, затем все `--n=3000000` (густые пропускаются — «гуще штатной»), `--threads=10 --target=0`, ключей
физики нет — посимвольно рецепт П185 (`D:\BqMoni_Claude\p185\night\store_run.cmd`, база rev37/rev38), П199 и П200.
Склад `D:\BqMoni_Claude\p206\store\` — 49 `.in` из worktree (sha256 = `p199\night\store` 49/49), матриц нет.

## 4. Две сцены до запуска (`A77`)

`CorpusMatrixProbe --only=G1S_point5,ASN16_lu_side` тем же рецептом в `D:\BqMoni_Claude\p206\check2` — код 0,
15.8 мин, «ВСЕ СОШЛИСЬ». `MatrixStampProbe`: клеймо файла = клейму кода при умолчаниях (состав клейма несёт
`mdb=6110517f284f3d33`, `kjnt=point` / `kjnt=5x4`, `peps=fwhm` — строки `ResponseMatrix.ComputeStamp`), формат 11.

| сравнение | тело | Q_k | κ | EPRS | почему так |
|---|---|---|---|---|---|
| `G1S_point5` против `p200\store3` (свет есть, κ-правки нет, формат 10) | **побитово** (отпечаток `b0e53a87…`) | 142/142 | у store3 прежняя таблица (min κ 0.940, НЕ ПРИНЯТА), у ночи κ ≡ 1 | у store3 блока нет | κ и EPRS ГСЧ тела не тянут; свет тот же |
| `G1S_point5` против `p199\store2` (κ есть, света нет) | различны: сумма 0.000 %, пик +0.029 ± 0.004 %, форма L1 медиана 2.57 % | 142/142 | побитово (24/24 пары `JointFactor`) | **побитово 142/142** | кривая света двигает бины, не поглощённую энергию; EPRS мерит недобор энергии |
| `ASN16_lu_side` против `p199\store2` | различны: сумма 0.000 %, пик +0.049 %, форма 11.25 % | 146/146 | побитово (24/24; таблица 1.84 / 5.0 %, подставлено 138, min κ 1.070 — ПРИНЯТА) | **побитово 146/146** | то же; κ из сумм пиков по энергии — от света не зависит |

Положительный контроль сличения (`D:\BqMoni_Claude\p206\eprs_cmp.ps1`, Windows PowerShell отражением):
`AS80_point0` против `G1S_point5` — EPRS побитово 0/142, κ 0 различий (обе точечные, κ ≡ 1);
`ASN16_lu_side` против `G1S_point5` — κ 24 из 24 различны. Сличение различия видит.

## 5. Запуск

`detached_run.ps1 -SelfTest` — «САМОПРОВЕРКА ПРОШЛА»; `detached_run.ps1 -Cmd D:\BqMoni_Claude\p206\store_run.cmd`
в 20:47:13, PID `cmd` **5024**, родитель 7788 (`WmiPrvSE`), ребёнок `CorpusMatrixProbe` 25212 с ключами рецепта
(`D:\BqMoni_Claude\p206\store_run.cmd.launch.txt`). Первая сцена `ASN16_point10_house` (6 М): 243.7 с, κ ≡ 1,
ε_p(разр.)/Σ Peak 1.0549 / 1.0744 / 1.0446 / 1.0050, шум 4.56 % «тихо»; клеймо = коду при 6 М историй.
Логи: `D:\BqMoni_Claude\p206\count.log`, `count.err`, `count_far_done.txt`, `count_done.txt`, `dumps\`.
Оценка (П199 §5): 510–530 мин ⇒ конец ≈ **05:20–05:40 02.10.2026**; в начале счёта машину делили полосы П204/П205.

## 6. Утром

1. `count_done.txt` = «exit 0», `count.log` — «ВСЕ СОШЛИСЬ» в обоих проходах, 49 `.rmx` формат 11.
2. Первые две единицы против независимого счёта: `G1S_point5` и `ASN16_lu_side` ночного склада против
   `D:\BqMoni_Claude\p206\check2` — `MatrixDiffProbe` «ТЕЛА ТОЖДЕСТВЕННЫ», Q_k, κ и EPRS побитово
   (`eprs_cmp.ps1` с путями ночи).
3. `KappaScanP199 D:\BqMoni_Claude\p206\store` — «ВСЕ ПРИНЯТЫ»; `MatrixAuditProbe --phys=26 --hist=3000000
   --except=RC103_point50:6000000 --except=ASN16_point10_house:6000000 --except=G1S_point25:6000000 --noise=6`;
   `CurveVsMatrixP199` на трёх сценах П199.
4. Слить `night-1001` в `master`; склад → `geometries/`, `mx_swap.py --from=tools/CORPUS/corpus/geometries --store`;
   `check_corpus_scenes.py` (49/49); витрина `rebuild_store.ps1 -Force` (≈ 23 мин) и `snapshot.ps1` с таблицей diff;
   кривые НЕ пересчитывать (физика 26), но `mdb=` в клейме кривой сменился — сверить `check_curve_generation`.
5. Полный корпус и малая база → `out_rev39_*` (до того — `S209`, генератор корпуса), объявление базы в трёх местах.
   Рабочие матрицы Amber (её `Debug\config`) после слияния получат отказ «формат 10» — пересчёт в EffMaker.

## 7. `check_all.py` (worktree, до запуска)

Код 1: 38 из 44 зелёные; 6 отказов — те же средовые, что у П199 §4: `check_corpus_coverage` (нет `_corpus_raw`),
`check_declared_base` (нет `out_rev38_full`), `check_corpus_generator` (CRLF `SUMMARY.md` worktree),
`check_fsa_report_view`, `check_corpus_scenes`, `check_fsa_showcase` (нет `probes\build`; два последних и по существу
красны до ночного склада). `check_matrix_keys`, `check_matdb_fingerprint`, `check_curve_generation` — 0.
Вывод — `D:\BqMoni_Claude\p206\check_all.txt`.

Снимков экрана нет.
