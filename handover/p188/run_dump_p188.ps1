# П188 30.09.2026 — ДИАГНОСТИЧЕСКИЕ прогоны полного корпуса с --dump-curves (разбор худших/лучших по полосам энергии).
#   -Arm r36 : сборка rev36 (выгрузка af8db260 `git archive` в p188\src36, Release_p188_r36), склад — снимок живого склада
#              rev36 (p188\store_backup, скопирован в src36\...\geometries), корпус — выгрузки (= основному дереву af8db260)
#   -Arm r37 : сборка ветки (p188 rel), склад worktree (ночь физики 26), корпус worktree
# Выходы — D:\BqMoni_Claude\p188\out\full_<arm>dump, кривые — p188\curves\full_<arm>. Объявляемые каталоги НЕ трогаются.
param([Parameter(Mandatory)][string]$Arm, [string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
$env:TEMP = 'D:\BqMoni_Claude\p188\tmp'; $env:TMP = $env:TEMP
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'D:\BqMoni_Claude\p183\wt'
$src  = 'D:\BqMoni_Claude\p188\src36'
$art  = 'D:\BqMoni_Claude\p188\art'
$log  = "$art\arm_codes.txt"
switch ($Arm) {
    'r36' { $bin = "$src\BecquerelMonitor\bin\Release_p188_r36"; $pb = 'D:\BqMoni_Claude\p188\build_p188_r36'
            $store = "$src\tools\CORPUS\corpus\geometries"; $corpus = "$src\tools\CORPUS\corpus"; $wd = "$root\tools\CORPUS\scripts\wd_p188r36" }
    'r37' { $bin = "$root\BecquerelMonitor\bin\Release_p188_rel"; $pb = 'D:\BqMoni_Claude\p188\build_p188_rel'
            $store = "$root\tools\CORPUS\corpus\geometries"; $corpus = "$root\tools\CORPUS\corpus"; $wd = "$root\tools\CORPUS\scripts\wd_p188" }
    default { exit 2 }
}
$nm = if ($Tag) { $Tag } else { $Arm }; $out = "D:\BqMoni_Claude\p188\out\full_${nm}dump"; $curves = "D:\BqMoni_Claude\p188\curves\full_$nm"
New-Item -ItemType Directory -Force $curves | Out-Null
Set-Location $root
if ($Arm -eq 'r36') {
    # -Force: сборка rev36 из ДРУГОГО набора исходников, чем worktree (осознанно, плечо диагностическое, как r35 у П181)
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $store -Force *> "$art\mk_appwd_dump_$Arm.log"
    "dump $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
}
& "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $out -Corpus $corpus -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store -Extra @("--dump-curves=$curves") -Force:($Arm -eq 'r36') *> "$art\run_dump_$Arm.log"
"dump $Arm full code=$LASTEXITCODE" | Out-File -Append $log
Get-Content $log | Select-String "dump $Arm " | ForEach-Object { $_.Line }
