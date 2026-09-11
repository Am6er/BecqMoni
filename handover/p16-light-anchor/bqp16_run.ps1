# П16 11.09.2026: одно плечо малой базы в копии bqp16 (сборка уже сделана bqp16_build.ps1).
#   & C:\Users\moroz\bqp16_run.ps1 -Arm lin
#   & C:\Users\moroz\bqp16_run.ps1 -Arm q4 -Extra '--anchor-poly=2','--anchor-poly-min=4'
param(
    [Parameter(Mandatory)][string]$Arm,
    [string[]]$Extra = @()
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\bqp16'
$bin  = "$root\BecquerelMonitor\bin\Debug_p16"
$prb  = "$root\tools\effmaker\probes\build_p16"
$wd   = "$root\tools\CORPUS\scripts\wd_p16"
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$out  = "C:\Users\moroz\bqp16_out\$Arm"
$dump = "C:\Users\moroz\bqp16_out\${Arm}_dump"
Set-Location $root
$t0 = Get-Date
$extra = @("--dump-curves=$dump") + $Extra
$log = & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store -Extra $extra 2>&1
$rc = $LASTEXITCODE
$log | Out-File -Encoding utf8 "C:\Users\moroz\bqp16_out\$Arm.log"
Write-Output ("run {0} exit={1} ({2} s), ключи: {3}" -f $Arm, $rc, [int]((Get-Date)-$t0).TotalSeconds, ($Extra -join ' '))
$log | Select-String -Pattern "ПРОГОН МАЛОЙ|sum chi2|model residual|итого|привязка шкалы|БЕЗ МАТРИЦЫ|НЕ СОШ|recall|фантом|подавлен" | Select-Object -First 14
Write-Output ("дампов: {0}" -f (Get-ChildItem "$dump\*_chi.csv" -ErrorAction SilentlyContinue).Count)
