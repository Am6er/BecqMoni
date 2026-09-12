# П26 12.09.2026, `AMBER22` п. 2 — абляции FSA на корпусном диске AS80_Th232Medal.
# Стенд: worktree C:\Users\moroz\bqp26 (HEAD 1adfd26e, Release_P26, build_p26, склад .rmx — копия
# корпусного), оснастка wd_p26 там же (mk_appwd.ps1 без -Force). Запуск: pwsh -File <этот файл> [имена плеч]
# ⛔ run_mini.ps1 зовётся ОПЕРАТОРОМ `&` (T84: массив -Extra через командную строку схлопывается).
# Плечо `ctl` — список П22 целиком (контроль = out_p22_disk строка в строку); остальные — один диск.
param([string[]]$Arms = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp26'
$here = "$root\handover\p26-amber22\fsa"
$wd = "$wt\tools\CORPUS\scripts\wd_p26"
$log = "$here\arms.log"

if (-not (Test-Path "$wd\CorpusFsaProbe.exe")) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_P26" -Wd $wd `
        -ProbeBuild "$wt\tools\effmaker\probes\build_p26" *> "$here\mk_appwd.log"
    "mk_appwd code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
    if ($LASTEXITCODE -ne 0) { exit 3 }
}

$all = [ordered]@{
    ctl       = @{ list = 'list_ctl.csv';  extra = @() }
    disk      = @{ list = 'list_disk.csv'; extra = @() }
    huber0    = @{ list = 'list_disk.csv'; extra = @('--huber=0') }
    nomatrix  = @{ list = 'list_disk.csv'; extra = @('--no-matrix') }
    nocascade = @{ list = 'list_disk.csv'; extra = @('--no-cascade') }
    nopileup  = @{ list = 'list_disk.csv'; extra = @('--no-pileup') }
    knots32   = @{ list = 'list_disk.csv'; extra = @('--knots=32') }
    knots512  = @{ list = 'list_disk.csv'; extra = @('--knots=512') }
    kfwhm8    = @{ list = 'list_disk.csv'; extra = @('--knot-fwhm=8') }
    kfwhm2    = @{ list = 'list_disk.csv'; extra = @('--knot-fwhm=2') }
    rough10   = @{ list = 'list_disk.csv'; extra = @('--roughness=10') }
    snip      = @{ list = 'list_disk.csv'; extra = @('--mode=snip') }
    noanchor  = @{ list = 'list_disk.csv'; extra = @('--no-anchor') }
    nobg      = @{ list = 'list_disk.csv'; extra = @('--no-background') }
    noeq      = @{ list = 'list_disk.csv'; extra = @('--no-equilibrium') }
    floor200  = @{ list = 'list_disk.csv'; extra = @('--fit-floor=200') }
    floor120  = @{ list = 'list_disk.csv'; extra = @('--fit-floor=120') }
    noatomic  = @{ list = 'list_disk.csv'; extra = @('--no-atomic') }
    noxray    = @{ list = 'list_disk.csv'; extra = @('--no-xray') }
    noescape  = @{ list = 'list_disk.csv'; extra = @('--no-escape') }
    nobacksc  = @{ list = 'list_disk.csv'; extra = @('--no-backscatter') }
    stretch   = @{ list = 'list_disk.csv'; extra = @('--matrix-transfer=stretch') }
    nomx_noeq = @{ list = 'list_disk.csv'; extra = @('--no-matrix', '--no-equilibrium') }
    huber0eq  = @{ list = 'list_disk.csv'; extra = @('--huber=0', '--no-equilibrium') }
    bswm      = @{ list = 'list_disk.csv'; extra = @('--backscatter-with-matrix') }
    bswm_noeq = @{ list = 'list_disk.csv'; extra = @('--backscatter-with-matrix', '--no-equilibrium') }
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
# из `pwsh -File` список приходит одной строкой «a,b» (T84) — режем сами
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $a = $all[$name]
    $out = "$root\tools\pie\out_p26_$name"
    $curves = "$here\curves_$name"
    New-Item -ItemType Directory -Force $curves | Out-Null
    $extra = @("--dump-curves=$curves", '--residuals=12') + $a.extra
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -List "$here\$($a.list)" -Out $out -Wd $wd `
        -Bin "$wt\BecquerelMonitor\bin\Release_P26" -ProbeBuild "$wt\tools\effmaker\probes\build_p26" `
        -Extra $extra -SkipScore *> "$here\run_$name.log"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') extra=[$($a.extra -join ' ')]" | Out-File -Append $log
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
