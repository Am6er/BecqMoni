# П16 11.09.2026: сборка копии C:\Users\moroz\bqp16 — приложение -> пробы -> оснастка.
# Каждый шаг проверяется кодом возврата (A77); при отказе — стоп.
#   & C:\Users\moroz\bqp16_build.ps1 -Store <склад>
param(
    [Parameter(Mandatory)][string]$Store
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\bqp16'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Debug_p16"
$prb  = "$root\tools\effmaker\probes\build_p16"
$wd   = "$root\tools\CORPUS\scripts\wd_p16"
$outroot = 'C:\Users\moroz\bqp16_out'
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

$null = Step 'msbuild' { & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' $proj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p16\" "/p:IntermediateOutputPath=obj\Debug_p16\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}" -f $_.Length, $_.LastWriteTime) }
$null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
# T45: свежий -Out не несёт NuGet-зависимостей и e_sqlite3.dll — докладываем из $bin
foreach ($f in @('Microsoft.Data.Sqlite.dll','SQLitePCLRaw.core.dll','SQLitePCLRaw.batteries_v2.dll','SQLitePCLRaw.provider.e_sqlite3.dll','SpecUtilsNet.dll')) {
    if ((Test-Path "$bin\$f") -and -not (Test-Path "$prb\$f")) { Copy-Item "$bin\$f" $prb }
}
# runtimes\ кладёт сам build_all.ps1 (сверено первым прогоном: «сборка\runtimes 3»); повторная
# копия поверх существующего каталога дала вложенный runtimes\runtimes — «постороннее», отказ T79.
Get-Item "$prb\CorpusFsaProbe.exe", "$prb\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
$null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $Store }
Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
Write-Output 'СБОРКА ПРОШЛА'
