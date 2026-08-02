# Сборка pie.exe и прогон полноспектральной декомпозиции по NaI/CsI-группам
# корпуса (ASN16, AS80x80, RC103, RC103g). Использует готовые рабочие каталоги
# tools/CORPUS/scripts/wd_* (конфиг устройства + спектры корпуса).
#
#   pwsh tools/pie/run_corpus.ps1 [-Mode snip|spline|both] [-SkipBuild] [-DumpModels]

param(
    [ValidateSet('snip', 'spline', 'both')]
    [string]$Mode = 'both',
    [switch]$SkipBuild,
    [switch]$DumpModels
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pieDir = Join-Path $repo 'tools\pie'
$outDir = Join-Path $pieDir 'out'
$wdRoot = Join-Path $repo 'tools\CORPUS\scripts'
$effTable = Join-Path $repo 'tools\CORPUS\data\eff_by_spectrum_lsrm.csv'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'

# группа -> (workdir, есть ли кривые эффективности)
$groups = @(
    @{ Name = 'ASN16';   Wd = 'wd_ASN16';   Eff = $true  },
    @{ Name = 'AS80x80'; Wd = 'wd_AS80x80'; Eff = $false },
    @{ Name = 'RC103';   Wd = 'wd_RC103';   Eff = $true  },
    @{ Name = 'RC103g';  Wd = 'wd_RC103g';  Eff = $true  }
)

New-Item -ItemType Directory -Force $outDir | Out-Null

if (-not $SkipBuild) {
    $refWd = Join-Path $wdRoot $groups[0].Wd
    $exe = Join-Path $refWd 'pie.exe'
    $bqRef = Join-Path $refWd 'BecquerelMonitor.exe'
    $src = Join-Path $pieDir 'Program.cs'
    & $csc /nologo /target:exe /platform:anycpu /langversion:7.3 `
        "/out:$exe" "/r:$bqRef" `
        /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
        /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        "$src"
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
    foreach ($g in $groups | Select-Object -Skip 1) {
        Copy-Item $exe (Join-Path (Join-Path $wdRoot $g.Wd) 'pie.exe') -Force
    }
    Write-Host "built: $exe"
}

$modes = if ($Mode -eq 'both') { @('snip', 'spline') } else { @($Mode) }

foreach ($m in $modes) {
    foreach ($g in $groups) {
        $wd = Join-Path $wdRoot $g.Wd
        $prefix = Join-Path $outDir "$($g.Name)_$m"
        $cliArgs = @(
            "--input=spectra",
            "--mode=$m",
            "--out=$prefix"
        )
        if ($g.Eff) { $cliArgs += "--eff-curve=$effTable" }
        if ($DumpModels) { $cliArgs += "--dump-model=$(Join-Path $outDir "models_$($g.Name)_$m")" }
        Write-Host "=== $($g.Name) mode=$m eff=$($g.Eff) ===" -ForegroundColor Cyan
        Push-Location $wd
        try {
            & (Join-Path $wd 'pie.exe') @cliArgs
            if ($LASTEXITCODE -ne 0) { Write-Warning "$($g.Name)/$m exit code $LASTEXITCODE" }
        }
        finally { Pop-Location }
    }
}

Write-Host "done -> $outDir"
