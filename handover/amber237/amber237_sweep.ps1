# AMBER236: вывод состава FsaInferProbeF51 по всем спектрам корпуса, один каталог проб.
# Протокол П247 (AMBER221): поставочная библиотека каталога проб, регрессия «до/после»,
# не корпусные числа. Вызов: & D:\BqMoni_Claude\uglass\sweep.ps1 -Probes <каталог> -Out <каталог>
param(
    [Parameter(Mandatory = $true)][string]$Probes,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
$spectra = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
New-Item -ItemType Directory -Force $Out | Out-Null
$exe = Join-Path $Probes 'FsaInferProbeF51.exe'
if (-not (Test-Path $exe)) { throw "нет $exe" }
$files = Get-ChildItem -LiteralPath $spectra -Filter *.xml | Sort-Object Name
$bad = 0
Push-Location $Probes
try {
    foreach ($f in $files) {
        $dst = Join-Path $Out ($f.BaseName + '.txt')
        & $exe "--spectrum=$($f.FullName)" *> $dst
        $code = $LASTEXITCODE
        if ($code -ne 0) { $bad++ }
        Add-Content -LiteralPath (Join-Path $Out '_codes.tsv') -Value ("{0}`t{1}" -f $f.BaseName, $code)
    }
}
finally { Pop-Location }
"спектров $($files.Count), ненулевых кодов $bad"
