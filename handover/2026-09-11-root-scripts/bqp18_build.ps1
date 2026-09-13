# П18 11.09.2026: сборка РАБОЧЕГО ДЕРЕВА — приложение (Debug_p18) -> пробы (build_p18) -> оснастка (wd_p18).
# Каждый шаг проверяется кодом возврата (A77); при отказе — стоп.
#   & C:\Users\moroz\bqp18_build.ps1
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Debug_p18"
$prb  = "$root\tools\effmaker\probes\build_p18"
$wd   = "$root\tools\CORPUS\scripts\wd_p18"
$store = "$root\tools\CORPUS\corpus\geometries"
$outroot = 'C:\Users\moroz\bqp18_out'
Set-Location $root
New-Item -ItemType Directory -Force $outroot | Out-Null

function Step($name, $sb) {
    $t0 = Get-Date
    $log = & $sb 2>&1
    $rc = $LASTEXITCODE
    $log | Out-File -Encoding utf8 "$outroot\build.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date)-$t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 "$outroot\build.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $log | Select-Object -Last 25; throw "шаг $name отказал кодом $rc" }
    return $log
}

$null = Step 'msbuild' { & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' $proj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p18\" "/p:IntermediateOutputPath=obj\Debug_p18\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}" -f $_.Length, $_.LastWriteTime) }
$null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
Get-Item "$prb\CorpusFsaProbe.exe", "$prb\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
$null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $store }
Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
Write-Output 'СБОРКА ПРОШЛА'
