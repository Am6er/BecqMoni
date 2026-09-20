# П94 — `AMBER44` + правка заноса `M12` (одной полосой): ход

Worktree `D:\BqMoni_Claude\p94\wt` от `f744dbed` (master). Сборка `BecquerelMonitor\bin\Release_p94`,
`obj\Release_p94`; пробы `tools\effmaker\probes\build_p94`. Стенд П92 скопирован сюда:
`geo\` (15 геометрий), `scenes\` (15 дампов), арбитр — `D:\BqMoni_Claude\p92\g4\build\g4cf.exe`
(с рычагами killesc/killcarry/fullcarry). Артефакты — `handover/p94-amber44/` в дереве.

## Фазы

- [x] 0. Прочитаны CLAUDE.md, строка AMBER44, журналы П92/П55, разбор AMBER44, навык todo-work §4.
- [x] 1. Worktree заведён, `wt.diff` (G4RawProbe --detour=) применён, геометрии/сцены скопированы.
- [x] 2. Сборка стенда (worktree без правок физики) → положительный контроль: ours RC103_point0_p55 1461 40 млн
      побайтно = handover/p92-m12/ours/ours_RC103_point0_p55_1460.82_ref.csv.
- [x] 3. Правка А (занос через ElectronLoss) + Б (перенос в слоях обвязки: занос и возврат) + ключ `eltr`
      (ResponseMatrixOptions.ElectronLayerTransport, клеймо `eltr=1`, хвост `ELTR`, MakeSimulator, путь кривой,
      пробы --eltr=, check_matrix_keys.py).
- [x] 4a. Сборка с правкой (codes_build.txt 17:09:25, коды 0); ВЫКЛ побитово = П92: ours 1461 40M и detour0 20M — sha256 равны (bitwise/csv_off_sha.txt).
- [x] 4b. MatrixDiffProbe окончательной сборкой: RC103_point0, ASN16_lu_side, AS80_th_disk (умолчания) — тела тождественны и с HEAD-сборкой (build_stand), и с живым складом (bitwise/diff_*.txt).
- [x] 5. Приёмка ВКЛ окончательной сборкой (plan_7_final.csv, 18:43–19:22) — tables/cmp_*.md; разложение занос/возврат (eltr1_detour0), LayerReturnProbe (обратное рассеяние ×0.6 к Табате в PTFE), цена: plan_3_time + run_cost.ps1 (матрица ×1.07…1.13). Вердикт — журнал §0.
- [x] 6. Журнал handover/handover-2026-09-17-p94-amber44-electron-return.md, артефакты handover/p94-amber44/.
- [x] 7. Перенос в дерево (20:05; Debug_Codex код 0, probesuild код 0, check_all 40/41 — красен только check_registry N8 чужих журналов AMBER43–45) по sha-снимку, пересборка Debug_Codex + probes\build, check_all.py.
- [x] 8. Уборка (worktree снят; D:\BqMoni_Claude\p94 и p92\g4 оставлены до приёмки): bin/obj/build_p94/wd_p94/worktree.

## Команды возобновления

```powershell
$env:OS='Windows_NT'
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'D:\BqMoni_Claude\p94\wt\BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
  /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p94\' /p:IntermediateOutputPath='obj\Release_p94\'
pwsh D:\BqMoni_Claude\p94\wt\tools\effmaker\probes\build_all.ps1 -Bin 'D:\BqMoni_Claude\p94\wt\BecquerelMonitor\bin\Release_p94' -Out 'D:\BqMoni_Claude\p94\wt\tools\effmaker\probes\build_p94'
pwsh D:\BqMoni_Claude\p94\run_plan.ps1 -Side ours -Plan D:\BqMoni_Claude\p94\plan_1.csv
```
