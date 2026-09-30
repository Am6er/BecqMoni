# П181 30.09.2026 — ДИАГНОСТИЧЕСКИЕ прогоны полного корпуса с --dump-curves (разбор худших/лучших по полосам энергии).
#   -Arm r35 : сборка rev35 (P180 base, cac34379), склад ОСНОВНОГО дерева ТОЛЬКО ЧТЕНИЕМ, корпус — копия основного (p181\corpus_rev35)
#   -Arm r36 : сборка ветки (p181 rel), склад worktree (ночь), корпус worktree
# Выходы — D:\BqMoni_Claude\p181\out\full_<arm>, кривые — p181\curves\full_<arm>. Объявляемые каталоги НЕ трогаются.
param([Parameter(Mandatory)][string]$Arm, [string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'D:\BqMoni_Claude\p180\wt'
$art  = 'D:\BqMoni_Claude\p181\art'
$log  = "$art\arm_codes.txt"
switch ($Arm) {
    'r35' { $bin = "$root\BecquerelMonitor\bin\Release_p180_base"; $pb = 'D:\BqMoni_Claude\p180\build_p180_base'
            $store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
            $corpus = 'D:\BqMoni_Claude\p181\corpus_rev35'; $wd = "$root\tools\CORPUS\scripts\wd_p181r35" }
    'r36' { $bin = "$root\BecquerelMonitor\bin\Release_p181_rel"; $pb = 'D:\BqMoni_Claude\p181\build_p181_rel'
            $store = "$root\tools\CORPUS\corpus\geometries"; $corpus = "$root\tools\CORPUS\corpus"; $wd = "$root\tools\CORPUS\scripts\wd_p181" }
    default { exit 2 }
}
$nm = if ($Tag) { $Tag } else { $Arm }; $out = "D:\BqMoni_Claude\p181\out\full_${nm}dump"; $curves = "D:\BqMoni_Claude\p181\curves\full_$nm"
New-Item -ItemType Directory -Force $curves | Out-Null
Set-Location $root
if ($Arm -eq 'r35') {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $store -Force *> "$art\mk_appwd_dump_$Arm.log"   # -Force: сборка rev35 из ДРУГОГО набора исходников, чем worktree (осознанно, плечо диагностическое)
    "dump $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
}
& "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $out -Corpus $corpus -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store -Extra @("--dump-curves=$curves") -Force:($Arm -eq 'r35') *> "$art\run_dump_$Arm.log"
"dump $Arm full code=$LASTEXITCODE" | Out-File -Append $log
Get-Content $log | Select-String "dump $Arm " | ForEach-Object { $_.Line }


