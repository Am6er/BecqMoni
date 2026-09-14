# П34 12.09.2026, `A309` — стенд: чистое рабочее дерево HEAD (`T209`) в C:\Users\moroz\bqp34
# + мои три файла из основного дерева (FsaAnalyzer.cs, CorpusFsaProbe.cs, FsaFitFloorProfileProbe.cs).
# Основное дерево несёт чужие незакоммиченные правки (П35: FsaCompositionInference.cs, FsaInferProbeF51.cs;
# FsaStackShot.cs соседа) — собирать там нельзя: числа принадлежали бы смеси. Здесь: копия склада .rmx
# (в git его нет), Restore + Release_p34 + build_all -Out build_p34 + mk_appwd -Wd wd_p34.
# Каждый шаг — по коду возврата (A77). Лог — build_wt.log рядом.
#   pwsh -NoProfile -File "<repo>\handover\p34-fit-floor\build_wt.ps1" [-SkipRestore] [-SkipWorktree]
param([switch]$SkipRestore, [switch]$SkipWorktree)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp34'
$here = "$root\handover\p34-fit-floor"
$log = "$here\build_wt.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -Encoding utf8 $log

if (-not $SkipWorktree) {
    Set-Location $root
    if (-not (Test-Path $wt)) {
        & git worktree add --detach $wt HEAD *>> $log
        "worktree add code=$LASTEXITCODE" | Out-File -Append -Encoding utf8 $log
    }
    "HEAD стенда: " + (& git -C $wt rev-parse HEAD) | Out-File -Append -Encoding utf8 $log
    foreach ($f in @('BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs',
                     'tools\effmaker\probes\CorpusFsaProbe.cs',
                     'tools\effmaker\probes\FsaFitFloorProfileProbe.cs')) {
        Copy-Item "$root\$f" "$wt\$f" -Force
        "$f sha256 " + (Get-FileHash "$wt\$f").Hash.Substring(0, 16) | Out-File -Append -Encoding utf8 $log
    }
    $n1 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\*.rmx" "$wt\tools\CORPUS\corpus\geometries\" -PassThru).Count
    New-Item -ItemType Directory -Force "$wt\tools\CORPUS\corpus\geometries\response" | Out-Null
    $n2 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\response\*.rmx" "$wt\tools\CORPUS\corpus\geometries\response\" -PassThru).Count
    "rmx copied: geometries $n1, response $n2" | Out-File -Append -Encoding utf8 $log
}

function Step($name, $sb) {
    $t0 = Get-Date
    $out = & $sb 2>&1
    $rc = $LASTEXITCODE
    $out | Out-File -Encoding utf8 "$here\build.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date) - $t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 $log ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $out | Select-Object -Last 30; throw "шаг $name отказал кодом $rc" }
}

$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
$bin = "$wt\BecquerelMonitor\bin\Release_p34"
$prb = "$wt\tools\effmaker\probes\build_p34"
$wd = "$wt\tools\CORPUS\scripts\wd_p34"
$store = "$wt\tools\CORPUS\corpus\geometries"
[System.IO.Directory]::SetCurrentDirectory($wt)
Set-Location $wt
if (-not $SkipRestore) {
    Step 'restore' { & $msbuild $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo }
}
Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p34\" "/p:IntermediateOutputPath=obj\Release_p34\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { ("  exe: {0} байт, {1}, sha256 {2}" -f $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName).Hash.Substring(0, 16)) | Tee-Object -Append $log }
Step 'probes' { & "$wt\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
Step 'mkwd' { & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $store }
("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count) | Tee-Object -Append $log
("  sha256 exe проб = приложения: {0}" -f ((Get-FileHash "$prb\BecquerelMonitor.exe").Hash -eq (Get-FileHash "$bin\BecquerelMonitor.exe").Hash)) | Tee-Object -Append $log
'СБОРКА ПРОШЛА' | Tee-Object -Append $log
