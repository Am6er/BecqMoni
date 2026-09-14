# П70 (AMBER30): стенд бисекции — на каждую точку свой worktree, своя сборка Release,
# своя FsaStackShot.exe (тем же csc-рецептом, что build_all.ps1: /langversion:7.3 /d:TRACE,
# ссылки те же, довески — все .cs без Main из tools\effmaker\probes и tools\effmaker),
# свой рабочий каталог wd_<hash> = Release-сборка + FsaStackShot.exe + КОПИЯ config\ Amber
# (склад матриц, приборы, NuclideDefinition.xml — как в её сборке).
#
#   & D:\BqMoni_Claude\p70\bisect_build.ps1 -Hashes 025a65a9,a6f2b227,...
#
# Коды: 0 — все точки собраны; иначе 1, с перечнем отказов. Журнал — logs\build_<hash>.log.
param([string[]]$Hashes = @('025a65a9'))
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$lane = 'D:\BqMoni_Claude\p70'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$amberCfg = Join-Path $lane 'amber_debug\config'
New-Item -ItemType Directory -Force (Join-Path $lane 'logs') | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$failed = @()

foreach ($h in $Hashes) {
    $t0 = Get-Date
    $wt = Join-Path $lane "wt_$h"
    $log = Join-Path $lane "logs\build_$h.log"
    "=== $h ===" | Tee-Object -FilePath $log
    if (-not (Test-Path (Join-Path $wt '.git'))) {
        Push-Location $repo
        git worktree add --detach $wt $h 2>&1 | Tee-Object -FilePath $log -Append
        $rc = $LASTEXITCODE
        Pop-Location
        if ($rc -ne 0) { "worktree add код $rc" | Tee-Object -FilePath $log -Append; $failed += "$h(worktree)"; continue }
    }
    $proj = Join-Path $wt 'BecquerelMonitor\BecquerelMonitor.csproj'
    $bin = Join-Path $wt 'BecquerelMonitor\bin\Release_p70'

    & $msbuild $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo 2>&1 | Out-File -FilePath $log -Append -Encoding utf8
    $rcRestore = $LASTEXITCODE
    "restore код $rcRestore" | Tee-Object -FilePath $log -Append
    if ($rcRestore -ne 0) { $failed += "$h(restore)"; continue }

    & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p70\" /v:m /nologo 2>&1 | Out-File -FilePath $log -Append -Encoding utf8
    $rcBuild = $LASTEXITCODE
    "build код $rcBuild" | Tee-Object -FilePath $log -Append
    if ($rcBuild -ne 0 -or -not (Test-Path (Join-Path $bin 'BecquerelMonitor.exe'))) { $failed += "$h(build)"; continue }
    $exeHash = (Get-FileHash (Join-Path $bin 'BecquerelMonitor.exe') -Algorithm SHA256).Hash
    "exe sha256 $exeHash  $((Get-Item (Join-Path $bin 'BecquerelMonitor.exe')).LastWriteTime.ToString('HH:mm:ss'))" | Tee-Object -FilePath $log -Append
    if (-not (Test-Path (Join-Path $bin 'runtimes\win-x64\native\e_sqlite3.dll'))) { "⛔ нет runtimes\ в сборке" | Tee-Object -FilePath $log -Append; $failed += "$h(runtimes)"; continue }

    # пробa: FsaStackShot + довески (все .cs без Main в probes\ и effmaker\ верхнего уровня)
    $probeDir = Join-Path $wt 'probes_p70'
    New-Item -ItemType Directory -Force $probeDir | Out-Null
    $srcDirs = @((Join-Path $wt 'tools\effmaker\probes'), (Join-Path $wt 'tools\effmaker'))
    $all = @()
    foreach ($d in $srcDirs) { $all += Get-ChildItem (Join-Path $d '*.cs') -File -Force }
    $companions = @($all | Where-Object { -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet) } | ForEach-Object { $_.FullName })
    $refs = @("/r:$bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
              '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$bin\Microsoft.Data.Sqlite.dll",
              "/r:$bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
    $src = Join-Path $wt 'tools\effmaker\probes\FsaStackShot.cs'
    $exe = Join-Path $probeDir 'FsaStackShot.exe'
    $cscLog = & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$exe" @refs $src @companions 2>&1
    $rcCsc = $LASTEXITCODE
    "csc FsaStackShot код $rcCsc (довесков $($companions.Count))" | Tee-Object -FilePath $log -Append
    $cscLog | Out-File -FilePath $log -Append -Encoding utf8
    if ($rcCsc -ne 0) { $failed += "$h(csc)"; continue }

    # рабочий каталог: сборка + проба + config Amber
    $wd = Join-Path $lane "wd_$h"
    robocopy $bin $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { "robocopy bin код $LASTEXITCODE" | Tee-Object -FilePath $log -Append; $failed += "$h(wd)"; continue }
    Copy-Item $exe $wd -Force
    Copy-Item (Join-Path $bin 'BecquerelMonitor.exe.config') (Join-Path $wd 'FsaStackShot.exe.config') -Force
    robocopy $amberCfg (Join-Path $wd 'config') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { "robocopy config код $LASTEXITCODE" | Tee-Object -FilePath $log -Append; $failed += "$h(cfg)"; continue }
    $nRmx = (Get-ChildItem (Join-Path $wd 'config\device\response\*.rmx') -File).Count
    "wd ${wd}: матриц $nRmx; за $([int]((Get-Date) - $t0).TotalSeconds) с" | Tee-Object -FilePath $log -Append
}
"ИТОГ: отказов $($failed.Count) $($failed -join ' ')"
if ($failed.Count -gt 0) { exit 1 }
exit 0
