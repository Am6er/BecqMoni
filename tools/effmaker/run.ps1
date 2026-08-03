# Пересборка приложения, харнесса effmaker и прогон конструктора кривой
# эффективности по пачкам корпуса.
#
#   pwsh tools/effmaker/run.ps1 [-SkipBuild] [-Groups ASN16,RC103]

param(
    [switch]$SkipBuild,
    [string[]]$Groups = @('ASN16', 'RC103')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$wdRoot = Join-Path $repo 'tools\CORPUS\scripts'
$out = Join-Path $repo 'tools\effmaker\out'
$roiDir = Join-Path $env:APPDATA 'BecqMoni\config\ROI'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'

# группа -> (рабочий каталог, кривая из ROI, цепочки)
$config = @{
    ASN16 = @{ Wd = 'wd_ASN16'; Roi = 'Nano - cilinder (close distance).xml'; Chains = 'Th-232,Ra-226' }
    RC103 = @{ Wd = 'wd_RC103'; Roi = 'RadiaCode - cilinder.xml';             Chains = 'Th-232,Ra-226' }
}

New-Item -ItemType Directory -Force $out | Out-Null

if (-not $SkipBuild) {
    & $msbuild (Join-Path $repo 'BecquerelMonitor\BecquerelMonitor.csproj') /t:Build `
        /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false `
        /p:OutputPath='bin\Debug_Codex\' /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'msbuild failed' }

    $exe = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex\BecquerelMonitor.exe'
    Get-ChildItem $wdRoot -Directory -Filter 'wd_*' |
        ForEach-Object { Copy-Item $exe (Join-Path $_.FullName 'BecquerelMonitor.exe') -Force }

    $ref = Join-Path $wdRoot 'wd_ASN16'
    & $csc /nologo /target:exe /platform:anycpu /langversion:7.3 `
        "/out:$ref\effmaker.exe" "/r:$ref\BecquerelMonitor.exe" `
        /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
        /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        (Join-Path $repo 'tools\effmaker\Program.cs')
    if ($LASTEXITCODE -ne 0) { throw 'csc failed' }
    Get-ChildItem $wdRoot -Directory -Filter 'wd_*' |
        Where-Object { $_.FullName -ne $ref } |
        ForEach-Object { Copy-Item "$ref\effmaker.exe" (Join-Path $_.FullName 'effmaker.exe') -Force }
}

foreach ($g in $Groups) {
    $c = $config[$g]
    if (-not $c) { throw "нет настройки для группы $g" }
    $wd = Join-Path $wdRoot $c.Wd
    $roi = Join-Path $roiDir $c.Roi

    Push-Location $wd
    try {
        Write-Host "=== $g : с кривой из ROI ===" -ForegroundColor Cyan
        & .\effmaker.exe "--input=spectra" "--ref=$roi" "--chains=$($c.Chains)" `
            "--out=$out\${g}_withref"

        Write-Host "=== $g : без исходной кривой ===" -ForegroundColor Cyan
        & .\effmaker.exe "--input=spectra" "--chains=$($c.Chains)" `
            "--out=$out\${g}_noref"
    }
    finally { Pop-Location }
}

Write-Host "done -> $out"
