# П56 14.09.2026, AMBER22 — стенд: worktree D:\BqMoni_Claude\p56\wt (a99b6038) + Restore + Release + build_all, по коду возврата (A77)
param([string]$Tag = 'p56')
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p56\wt'
$here = 'D:\BqMoni_Claude\p56'
$log = "$here\build_$Tag.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss') tag=$Tag" | Out-File -Encoding utf8 $log
"HEAD стенда: " + (& git -C $wt rev-parse HEAD) | Out-File -Append -Encoding utf8 $log
$src = "$root\tools\CORPUS\corpus\geometries"
$n1 = (Copy-Item "$src\*.rmx" "$wt\tools\CORPUS\corpus\geometries\" -PassThru).Count
New-Item -ItemType Directory -Force "$wt\tools\CORPUS\corpus\geometries\response" | Out-Null
$n2 = (Copy-Item "$src\response\*.rmx" "$wt\tools\CORPUS\corpus\geometries\response\" -PassThru).Count
"rmx copied from ${src}: geometries $n1, response $n2" | Out-File -Append -Encoding utf8 $log
function Step($name, $sb) {
    $t0 = Get-Date
    $out = & $sb 2>&1
    $rc = $LASTEXITCODE
    $out | Out-File -Encoding utf8 "$here\build_$Tag.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date) - $t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 $log ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $out | Select-Object -Last 30; throw "шаг $name отказал кодом $rc" }
}
$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
$bin = "$wt\BecquerelMonitor\bin\Release_$Tag"
$prb = "$wt\tools\effmaker\probes\build_$Tag"
[System.IO.Directory]::SetCurrentDirectory($wt)
Set-Location $wt
Step 'restore' { & $msbuild $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo }
Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_$Tag\" "/p:IntermediateOutputPath=obj\Release_$Tag\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { ("  exe: {0} байт, {1}, sha256 {2}" -f $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName).Hash.Substring(0, 16)) | Tee-Object -Append $log }
Step 'probes' { & "$wt\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
("  sha256 exe проб = приложения: {0}" -f ((Get-FileHash "$prb\BecquerelMonitor.exe").Hash -eq (Get-FileHash "$bin\BecquerelMonitor.exe").Hash)) | Tee-Object -Append $log
'СБОРКА ПРОШЛА' | Tee-Object -Append $log
