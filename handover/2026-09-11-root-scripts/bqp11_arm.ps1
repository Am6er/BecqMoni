# П11 11.09.2026: одно плечо плана 2×2 в копии C:\Users\moroz\bqp11 —
# умолчания -> сборка приложения -> пробы -> оснастка -> прогон малой базы с дампом.
#   & C:\Users\moroz\bqp11_arm.ps1 -Arm a -Transfer 0 -NewMx 0 -Store <склад>  [-NoAnchor]
# Каждый шаг проверяется кодом возврата (A77); при отказе — стоп.
param(
    [Parameter(Mandatory)][string]$Arm,
    [Parameter(Mandatory)][string]$Transfer,
    [Parameter(Mandatory)][string]$NewMx,
    [Parameter(Mandatory)][string]$Store,
    [string]$OutName = '',
    [switch]$NoAnchor,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\bqp11'
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Debug_p11$Arm"
$prb  = "$root\tools\effmaker\probes\build_p11$Arm"
$wd   = "$root\tools\CORPUS\scripts\wd_p11$Arm"
if (-not $OutName) { $OutName = $Arm }
$out  = "C:\Users\moroz\bqp11_out\$OutName"
$dump = "C:\Users\moroz\bqp11_out\${OutName}_dump"
Set-Location $root

function Step($name, $sb) {
    $t0 = Get-Date
    $log = & $sb 2>&1
    $rc = $LASTEXITCODE
    $log | Out-File -Encoding utf8 "C:\Users\moroz\bqp11_out\$OutName.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date)-$t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 "C:\Users\moroz\bqp11_out\$OutName.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $log | Select-Object -Last 15; throw "шаг $name отказал кодом $rc" }
    return $log
}

New-Item -ItemType Directory -Force 'C:\Users\moroz\bqp11_out' | Out-Null
if (-not $SkipBuild) {
    $null = Step 'setdef' { python C:\Users\moroz\bqp11_setdef.py $Transfer $NewMx }
    Write-Output (Get-Content "C:\Users\moroz\bqp11_out\$OutName.setdef.log")
    $null = Step 'msbuild' { & $msb $proj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p11$Arm\" "/p:IntermediateOutputPath=obj\Debug_p11$Arm\" /v:m /nologo }
    Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}" -f $_.Length, $_.LastWriteTime) }
    $null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
    Get-Item "$prb\CorpusFsaProbe.exe", "$prb\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
    $null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $Store }
    Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
}
$extra = @("--dump-curves=$dump")
if ($NoAnchor) { $extra += '--no-anchor' }
$log = Step 'run' { & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd -Bin $bin -ProbeBuild $prb -Store $Store -Extra $extra }
$log | Select-String -Pattern "ПРОГОН МАЛОЙ|sum chi2|model residual|итого|привязка шкалы|БЕЗ МАТРИЦЫ|НЕ СОШ" | Select-Object -First 12
Write-Output ("дампов: {0}" -f (Get-ChildItem "$dump\*_chi.csv").Count)
