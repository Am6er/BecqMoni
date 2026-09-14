# П19 12.09.2026: сборка КОПИИ C:\Users\moroz\bqp19 — приложение (Debug_p19) -> пробы (build_p19) -> оснастка (wd_p19).
# Каждый шаг проверяется кодом возврата (A77); при отказе — стоп. Склад матриц — рабочего дерева (только чтение).
#   & C:\Users\moroz\bqp19_build.ps1 [-Restore]
param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\bqp19'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Debug_p19"
$prb  = "$root\tools\effmaker\probes\build_p19"
$wd   = "$root\tools\CORPUS\scripts\wd_p19"
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$outroot = 'C:\Users\moroz\bqp19_out'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
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

if ($Restore) {
    $null = Step 'restore' { & $msbuild $proj /t:Restore /p:Configuration=Debug /p:Platform=AnyCPU /v:m /nologo }
}
$null = Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p19\" "/p:IntermediateOutputPath=obj\Debug_p19\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}" -f $_.Length, $_.LastWriteTime) }
$null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
# T45: свежий -Out не несёт NuGet-зависимостей — докладываем из $bin (runtimes\ кладёт сам build_all.ps1)
foreach ($f in @('Microsoft.Data.Sqlite.dll','SQLitePCLRaw.core.dll','SQLitePCLRaw.batteries_v2.dll','SQLitePCLRaw.provider.e_sqlite3.dll','SpecUtilsNet.dll')) {
    if ((Test-Path "$bin\$f") -and -not (Test-Path "$prb\$f")) { Copy-Item "$bin\$f" $prb }
}
Get-Item "$prb\CorpusFsaProbe.exe", "$prb\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
$null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $store }
Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
Write-Output 'СБОРКА ПРОШЛА'
