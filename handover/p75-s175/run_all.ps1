# П75 (S175): вся приёмка одной цепочкой (одна ждалка): сборки wt_a/wt_b → стенд радона (матрица из build_p75
# основного дерева) → приёмка (плечи A/B/G/R, пробы) → малая база А, Б → check_all. Логи — logs\.
# Основное дерево (build_p75) уже собрано main_build.ps1 (код 0) — здесь не пересобирается.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$d = 'D:\BqMoni_Claude\p75'
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
if ((Step 'wt_build_a' { pwsh -NoProfile -File "$d\wt_build.ps1" -Arm a }) -ne 0) { "ОСТАНОВ: сборка wt_a"; $steps; exit 1 }
if ((Step 'wt_build_b' { pwsh -NoProfile -File "$d\wt_build.ps1" -Arm b }) -ne 0) { "ОСТАНОВ: сборка wt_b"; $steps; exit 1 }
if ((Step 'radon_stand' { pwsh -NoProfile -File "$d\radon_stand.ps1" }) -ne 0) { "ОСТАНОВ: стенд радона"; $steps; exit 1 }
Step 'accept' { pwsh -NoProfile -File "$d\accept.ps1" } | Out-Null
[void](Step 'mini_a' { pwsh -NoProfile -File "$d\mini.ps1" -Arm a })
[void](Step 'mini_b' { pwsh -NoProfile -File "$d\mini.ps1" -Arm b })
[void](Step 'check_all' { Set-Location $repo; python tools\check_all.py })
"ИТОГ: " + ($steps -join '; ')
exit 0
