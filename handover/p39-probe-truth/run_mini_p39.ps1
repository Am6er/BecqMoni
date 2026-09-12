# П39: прогон малой базы на оснастке wd_p39 (умолчания) и сверка с out_rev20_mini строка в строку.
param([string]$Repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8', [string]$Tag = 'out_p39_mini')
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$env:PYTHONIOENCODING = 'utf-8'
Set-Location $Repo
$out = "$Repo\tools\pie\$Tag"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$sw = [Diagnostics.Stopwatch]::StartNew()
& "$Repo\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd "$Repo\tools\CORPUS\scripts\wd_p39" 2>&1 | Tee-Object -FilePath "$Repo\handover\p39-probe-truth\$Tag.log"
$rc = $LASTEXITCODE
"run_mini rc=$rc, {0} с" -f $sw.Elapsed.TotalSeconds.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture)
if ($rc -eq 0) {
    & python "$Repo\handover\p24-out-rev19\p24_keyed.py" "$Repo\tools\pie\out_rev20_mini" $out 2>&1 | Tee-Object -FilePath "$Repo\handover\p39-probe-truth\$Tag.keyed.txt"
    "keyed rc=$LASTEXITCODE"
}
exit $rc
