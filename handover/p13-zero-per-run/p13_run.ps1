# П13 12.09.2026 (S169, нуль по съёмке): одно плечо прогона в ГЛАВНОМ дереве; выход — tools\pie\<Arm>.
#   & 'handover\p13-zero-per-run\p13_run.ps1' -Arm out_p13_adc -Extra '--anchor-zero=adc'
#   … -List handover\p13-zero-per-run\list_p13.csv  — малая база + добор (третья выборка; run_mini.ps1 -List)
#   … -Mini                                            — штатная малая база (run_mini.ps1)
# Полное плечо: run_appwd.ps1 + score.py --part=known/unknown --members -> <Arm>_score.txt.
# ⛔ Звать ОПЕРАТОРОМ ВЫЗОВА `&` из текущей сессии (T84): массив -Extra через pwsh -File схлопывается.
param(
    [Parameter(Mandatory)][string]$Arm,
    [string[]]$Extra = @(),
    [switch]$Mini,
    [string]$List = '',
    # ⚠ -Force — ТОЛЬКО когда сторож оснастки красен из-за ЧУЖОЙ незакоммиченной
    #   правки, которой в сборке нет (П10 §2); положительный контроль обязателен:
    #   плечо с -Force против того же плеча без него — 0 расхождений (p8_cmp.py).
    [switch]$Force
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin  = "$root\BecquerelMonitor\bin\Debug_P13"
$prb  = "$root\tools\effmaker\probes\build_p13z"
$wd   = "$root\tools\CORPUS\scripts\wd_app"
$out  = "$root\tools\pie\$Arm"
$here = "$root\handover\p13-zero-per-run"
$logf = "$here\$Arm.log"
Set-Location $root
$t0 = Get-Date
$extra = @() + $Extra
$pass = @{ Out = $out; Wd = $wd; Bin = $bin; ProbeBuild = $prb; Extra = $extra }
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
$line = "run {0} exit={1} ({2} s), ключи: {3}{4}{5}" -f $Arm, $rc, [int]((Get-Date)-$t0).TotalSeconds, ($Extra -join ' '), $(if ($List) { " [список $List]" } elseif ($Mini) { ' [малая база]' } else { ' [полный корпус]' }), $(if ($Force) { ' [-Force]' } else { '' })
Write-Output $line
Add-Content -Encoding utf8 "$here\run.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
Get-Content $logf | Select-String -Pattern '^\s*(known|unknown|итого)\s|ПРОГОН|положение по свету|нуль шкалы образа|образ наложений|БЕЗ МАТРИЦЫ|НЕ СОШ|ОСНАСТКА|sum chi2|ОТКАЗ|изменено:' | ForEach-Object { $_.Line.Trim() } | Select-Object -First 14
if (-not $Mini -and -not $List -and $rc -eq 0) {
    $sc = "$here\${Arm}_score.txt"
    if (Test-Path $sc) { Remove-Item $sc }
    foreach ($part in @('known', 'unknown')) {
        "=== score.py --part=$part ===" | Out-File -Append -Encoding utf8 $sc
        & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$out" "--part=$part" --members 2>&1 | Out-File -Append -Encoding utf8 $sc
    }
    Get-Content $sc | Select-String -Pattern '^\s*итого\s|sum chi2' | ForEach-Object { $_.Line.Trim() }
}
