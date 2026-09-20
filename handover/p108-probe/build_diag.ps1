# Быстрая сборка ОДНОЙ пробы FsaReportViewProbe в отдельный каталог D:\BqMoni_Claude\p108\build_diag
# (копия штатного каталога проб + csc той же командой, что build_all.ps1). Только для стенда полосы;
# приёмка — ТОЛЬКО штатным build_all.ps1 в tools\effmaker\probes\build.
param([string]$Out = 'D:\BqMoni_Claude\p108\build_diag')
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$src = Join-Path $repo 'tools\effmaker\probes'
$std = Join-Path $src 'build'
if (-not (Test-Path $Out)) {
    New-Item -ItemType Directory -Force $Out | Out-Null
    Get-ChildItem $std | Where-Object { $_.Extension -ne '.exe' -or $_.Name -eq 'BecquerelMonitor.exe' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Recurse -Force }
}
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$Bin = $std
$refs = @("/r:$Bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$Bin\Microsoft.Data.Sqlite.dll",
    "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
$companions = @(Get-ChildItem $src -Filter *.cs | Where-Object {
    -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet) } | ForEach-Object { $_.FullName })
$exe = Join-Path $Out 'FsaReportViewProbe.exe'
$log = & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$exe" @refs (Join-Path $src 'FsaReportViewProbe.cs') @companions 2>&1
$log | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { Write-Host "csc: код $LASTEXITCODE"; exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $std 'FsaReportViewProbe.exe.config') -Destination $Out -Force -ErrorAction SilentlyContinue
Write-Host "собрано: $exe"
