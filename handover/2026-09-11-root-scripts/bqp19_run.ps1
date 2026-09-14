# П19 12.09.2026: одно плечо — малая база (-Mini) или полный корпус — в копии bqp19 (сборка bqp19_build.ps1 уже сделана).
#   & C:\Users\moroz\bqp19_run.ps1 -Arm mini_lin -Mini
#   & C:\Users\moroz\bqp19_run.ps1 -Arm line -Extra '--anchor-light=line'
param(
    [Parameter(Mandatory)][string]$Arm,
    [string[]]$Extra = @(),
    [switch]$Mini
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\bqp19'
$bin  = "$root\BecquerelMonitor\bin\Debug_p19"
$prb  = "$root\tools\effmaker\probes\build_p19"
$wd   = "$root\tools\CORPUS\scripts\wd_p19"
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$out  = "C:\Users\moroz\bqp19_out\$Arm"
$dump = "C:\Users\moroz\bqp19_out\${Arm}_dump"
Set-Location $root
$t0 = Get-Date
$extra = @("--dump-curves=$dump") + $Extra
if ($Mini) {
    $log = & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store -Extra $extra 2>&1
} else {
    $log = & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store -Extra $extra 2>&1
}
$rc = $LASTEXITCODE
$log | Out-File -Encoding utf8 "C:\Users\moroz\bqp19_out\$Arm.log"
Write-Output ("run {0} exit={1} ({2} s), ключи: {3}" -f $Arm, $rc, [int]((Get-Date)-$t0).TotalSeconds, ($Extra -join ' '))
$log | Select-String -Pattern "ПРОГОН|sum chi2|model residual|итого|привязка шкалы|положение по свету|БЕЗ МАТРИЦЫ|НЕ СОШ|recall|фантом|подавлен" | Select-Object -First 16
Write-Output ("дампов: {0}" -f (Get-ChildItem "$dump\*_curves.csv" -ErrorAction SilentlyContinue).Count)
