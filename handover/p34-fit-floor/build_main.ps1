# П34 13.09.2026 (ночь), `A309` — сборка в ОСНОВНОМ дереве (HEAD 2d4f1605 + мои файлы; чужих незакоммиченных
# исходников набора разбора в дереве нет — только FsaStackShot.cs соседа, он в набор не входит) — чтобы клеймо
# `.run.json` базы out_rev20_* сходилось с деревом после коммита. Свои каталоги: bin\Release_p34, obj\Release_p34,
# probes\build_p34, scripts\wd_p34. Каждый шаг по коду возврата (A77). Лог — build_main.log рядом.
param([switch]$SkipRestore)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$here = "$root\handover\p34-fit-floor"
$log = "$here\build_main.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss') HEAD " + (& git -C $root rev-parse HEAD) | Out-File -Encoding utf8 $log
function Step($name, $sb) {
    $t0 = Get-Date
    $out = & $sb 2>&1
    $rc = $LASTEXITCODE
    $out | Out-File -Encoding utf8 "$here\build_main.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date) - $t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 $log ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $out | Select-Object -Last 30; throw "шаг $name отказал кодом $rc" }
}
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin = "$root\BecquerelMonitor\bin\Release_p34"
$prb = "$root\tools\effmaker\probes\build_p34"
$wd = "$root\tools\CORPUS\scripts\wd_p34"
$store = "$root\tools\CORPUS\corpus\geometries"
[System.IO.Directory]::SetCurrentDirectory($root)
Set-Location $root
if (-not $SkipRestore) {
    Step 'restore' { & $msbuild $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo }
}
Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p34\" "/p:IntermediateOutputPath=obj\Release_p34\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { ("  exe: {0} байт, {1}, sha256 {2}" -f $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName).Hash.Substring(0, 16)) | Tee-Object -Append $log }
Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $store }
("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count) | Tee-Object -Append $log
("  sha256 exe проб = приложения: {0}" -f ((Get-FileHash "$prb\BecquerelMonitor.exe").Hash -eq (Get-FileHash "$bin\BecquerelMonitor.exe").Hash)) | Tee-Object -Append $log
'СБОРКА ПРОШЛА' | Tee-Object -Append $log
