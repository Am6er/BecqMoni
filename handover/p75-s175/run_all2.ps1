# П75 (S175, второе решение Amber «В слои образов по S76 везде»): повтор приёмки одной цепочкой —
# сборка основного дерева → патч заново в wt_b (git checkout + git apply) → сборка wt_b → приёмка → малая база Б → check_all.
# Плечо А (wt_a, HEAD 08030c57) и стенд радона — с первой цепочки (не менялись).
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
if ((Step 'main_build3' { pwsh -NoProfile -File "$d\main_build.ps1" }) -ne 0) { "ОСТАНОВ: сборка основного дерева"; $steps; exit 1 }
$files = @('BecquerelMonitor/EnergySpectrumView.Fsa.cs','BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs','BecquerelMonitor/FullSpectrumAnalysis/FsaPresentationBuilder.cs','BecquerelMonitor/FullSpectrumAnalysis/FsaResult.cs','tools/effmaker/probes/CorpusFsaProbe.cs','tools/effmaker/probes/FsaChannelSplitProbe.cs','tools/effmaker/probes/FsaDoubleCountProbe.cs','tools/effmaker/probes/FsaStackShot.cs')
Set-Location $repo
git diff -- $files | Out-File -Encoding utf8NoBOM "$d\s175.patch"
Set-Location "$d\wt_b"
git checkout -- . 2>&1 | Out-Null
git apply "$d\s175.patch"
"apply code $LASTEXITCODE"
git status --short
if ((Step 'wt_build_b2' { pwsh -NoProfile -File "$d\wt_build.ps1" -Arm b }) -ne 0) { "ОСТАНОВ: сборка wt_b"; $steps; exit 1 }
Step 'accept2' { pwsh -NoProfile -File "$d\accept.ps1" } | Out-Null
[void](Step 'mini_b2' { pwsh -NoProfile -File "$d\mini.ps1" -Arm b })
[void](Step 'check_all2' { Set-Location $repo; python tools\check_all.py })
"ИТОГ: " + ($steps -join '; ')
exit 0
