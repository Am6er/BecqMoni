# П24 12.09.2026: одно плечо прогона в СТЕНДЕ (worktree C:\Users\moroz\bqp30); выход — <root>\tools\pie\<Arm>.
#   pwsh -NoProfile -Command "& 'C:\Users\moroz\bqp30\handover\p30-cleaner-s\p24_run.ps1' -Arm out_p24_ctrl -Extra '--refit-z-rel=0','--sum-light=electron'"
#   … -Mini                                  — штатная малая база (run_mini.ps1, список corpus\mini.csv)
#   … -List <файл.csv>                       — свой список (третья выборка)
# Полное плечо: run_appwd.ps1 + score.py --part=known/unknown --members -> <here>\<Arm>_score.txt.
# ⛔ Массив -Extra доезжает целым только через `&` (T84): из Bash — pwsh -NoProfile -Command "& '<файл>' …".
param(
    [Parameter(Mandatory)][string]$Arm,
    [string[]]$Extra = @(),
    [switch]$Mini,
    [string]$List = '',
    [switch]$Force,
    [switch]$ScoreOnly
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
# python печатает UTF-8; без этого pwsh из Bash читал бы его OEM-страницей и score-файл выходил бы кашей
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$root = 'C:\Users\moroz\bqp30'
$bin  = "$root\BecquerelMonitor\bin\Release_P30"
$prb  = "$root\tools\effmaker\probes\build_p30"
$wd   = "$root\tools\CORPUS\scripts\wd_p30"
$store = "$root\tools\CORPUS\corpus\geometries"
$out  = "$root\tools\pie\$Arm"
$here = "$root\handover\p30-cleaner-s"
$logf = "$here\$Arm.log"
[System.IO.Directory]::SetCurrentDirectory($root)
Set-Location $root
$t0 = Get-Date
$rc = 0
$extra = @() + $Extra
if (-not $ScoreOnly) {
$pass = @{ Out = $out; Wd = $wd; Bin = $bin; ProbeBuild = $prb; Store = $store; Extra = $extra }
if ($Force) { $pass['Force'] = $true }
if ($List) {
    $pass['List'] = $List
    & "$root\tools\CORPUS\scripts\run_mini.ps1" @pass *> $logf
} elseif ($Mini) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" @pass *> $logf
} else {
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" @pass *> $logf
}
$rc = $LASTEXITCODE
}
$line = "run {0} exit={1} ({2} s), ключи: {3}{4}{5}" -f $Arm, $rc, [int]((Get-Date)-$t0).TotalSeconds, ($Extra -join ' '), $(if ($List) { " [список $List]" } elseif ($Mini) { ' [малая база]' } else { ' [полный корпус]' }), $(if ($Force) { ' [-Force]' } else { '' })
Write-Output $line
Add-Content -Encoding utf8 "$here\run.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
$sel = Get-Content $logf | Select-String -Pattern '^\s*(known|unknown|итого)\s|ПРОГОН|положение по свету|нуль шкалы образа|образ наложений|отсев по значимости|заслон|сумм|вынос|БЕЗ МАТРИЦЫ|НЕ СОШ|ОСНАСТКА|sum chi2|ОТКАЗ|изменено:' | ForEach-Object { $_.Line.Trim() }
$sel | Select-Object -First 20 | ForEach-Object { Write-Output $_ }
if (-not $Mini -and -not $List -and $rc -eq 0) {
    $sc = "$here\${Arm}_score.txt"
    if (Test-Path $sc) { Remove-Item $sc }
    foreach ($part in @('known', 'unknown')) {
        "=== score.py --part=$part ===" | Out-File -Append -Encoding utf8 $sc
        & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$out" "--part=$part" --members 2>&1 | Out-File -Append -Encoding utf8 $sc
    }
    $sc2 = Get-Content $sc | Select-String -Pattern '^\s*итого\s|sum chi2' | ForEach-Object { $_.Line.Trim() }
    $sc2 | ForEach-Object { Write-Output $_ }
}
