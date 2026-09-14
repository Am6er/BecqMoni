# П70 (AMBER30): вся приёмка одной цепочкой (одна ждалка): сборка основного дерева → сборки wt_a/wt_b →
# стенд радона (матрица) → приёмка основного дерева → малая база А, Б → check_all. Логи — logs\.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$d = 'D:\BqMoni_Claude\p70'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$steps = @()
function Step([string]$name, [scriptblock]$body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $body *> "$d\logs\$name.log"
    $c = $LASTEXITCODE
    $script:steps += "$name=$c ($([int]$sw.Elapsed.TotalSeconds) s)"
    Write-Host "${name}: код $c, $([int]$sw.Elapsed.TotalSeconds) с"
    return $c
}
if ((Step 'main_build' { pwsh -NoProfile -File "$d\main_build.ps1" }) -ne 0) { "ОСТАНОВ: сборка основного дерева"; $steps; exit 1 }
if ((Step 'wt_build_a' { pwsh -NoProfile -File "$d\wt_build.ps1" -Arm a }) -ne 0) { "ОСТАНОВ: сборка wt_a"; $steps; exit 1 }
if ((Step 'wt_build_b' { pwsh -NoProfile -File "$d\wt_build.ps1" -Arm b }) -ne 0) { "ОСТАНОВ: сборка wt_b"; $steps; exit 1 }
if ((Step 'radon_stand' { pwsh -NoProfile -File "$d\radon_stand.ps1" }) -ne 0) { "ОСТАНОВ: стенд радона"; $steps; exit 1 }
Step 'accept_main' { pwsh -NoProfile -File "$d\accept_main.ps1" } | Out-Null
[void](Step 'mini_a' { pwsh -NoProfile -File "$d\mini.ps1" -Arm a })
[void](Step 'mini_b' { pwsh -NoProfile -File "$d\mini.ps1" -Arm b })
[void](Step 'check_all' { Set-Location $repo; python tools\check_all.py })
"ИТОГ: " + ($steps -join '; ')
exit 0
