param(
    [Parameter(Mandatory)][string]$Arm,
    [string[]]$Extra = @(),
    [switch]$Mini,
    [switch]$Dump
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin  = "$root\BecquerelMonitor\bin\Debug_p21b"
$prb  = "$root\tools\effmaker\probes\build_p21b"
$wd   = "$root\tools\CORPUS\scripts\wd_p21b"
$store = "$root\tools\CORPUS\corpus\geometries"
$out  = "C:\Users\moroz\p21b_out\$Arm"
$dumpDir = "C:\Users\moroz\p21b_out\${Arm}_dump"
$logf = "C:\Users\moroz\p21b_out\$Arm.log"
Set-Location $root
$t0 = Get-Date
$extra = @() + $Extra
if ($Dump) { $extra = @("--dump-curves=$dumpDir") + $extra }
if ($Mini) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store -Extra $extra *> $logf
} else {
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store -Extra $extra *> $logf
}
$rc = $LASTEXITCODE
Write-Output ("run {0} exit={1} ({2} s), ключи: {3}" -f $Arm, $rc, [int]((Get-Date)-$t0).TotalSeconds, ($Extra -join ' '))
Get-Content $logf | Select-String -Pattern '^\s*(known|unknown|итого)\s|ПРОГОН|положение по свету|БЕЗ МАТРИЦЫ|НЕ СОШ|ОСНАСТКА' | ForEach-Object { $_.Line.Trim() } | Select-Object -First 8
if ($Dump) { Write-Output ("дампов: {0}" -f (Get-ChildItem "$dumpDir\*_curves.csv" -ErrorAction SilentlyContinue).Count) }
