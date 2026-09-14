# П17 11.09.2026: одно плечо малой базы в РАБОЧЕМ дереве (сборка Debug_p17 / build_p17 уже сделана).
#   & C:\Users\moroz\bqp17_arm.ps1 -Arm a -Store <scratchpad>\mini_a16
#   & C:\Users\moroz\bqp17_arm.ps1 -Arm b -Store <scratchpad>\mini_a17
# Каждый шаг проверяется кодом возврата (A77); при отказе — стоп.
param(
    [Parameter(Mandatory)][string]$Arm,
    [Parameter(Mandatory)][string]$Store,
    [string[]]$Extra = @()
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin  = "$root\BecquerelMonitor\bin\Debug_p17"
$prb  = "$root\tools\effmaker\probes\build_p17"
$wd   = "$root\tools\CORPUS\scripts\wd_p17$Arm"
$out  = "C:\Users\moroz\bqp17_out\$Arm"
$dump = "C:\Users\moroz\bqp17_out\${Arm}_dump"
Set-Location $root

function Step($name, $sb) {
    $t0 = Get-Date
    $log = & $sb 2>&1
    $rc = $LASTEXITCODE
    $log | Out-File -Encoding utf8 "C:\Users\moroz\bqp17_out\$Arm.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date)-$t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 "C:\Users\moroz\bqp17_out\$Arm.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $log | Select-Object -Last 15; throw "шаг $name отказал кодом $rc" }
    return $log
}

New-Item -ItemType Directory -Force 'C:\Users\moroz\bqp17_out' | Out-Null
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}" -f $_.Length, $_.LastWriteTime) }
$null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $Store }
Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
$extra = @("--dump-curves=$dump") + $Extra
$log = Step 'run' { & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $Store -Extra $extra }
$log | Select-String -Pattern "ПРОГОН МАЛОЙ|sum chi2|model residual|итого|привязка шкалы|БЕЗ МАТРИЦЫ|НЕ СОШ|recall|фантом|подавлен" | Select-Object -First 14
Write-Output ("дампов: {0}" -f (Get-ChildItem "$dump\*_curves.csv" -ErrorAction SilentlyContinue).Count)
