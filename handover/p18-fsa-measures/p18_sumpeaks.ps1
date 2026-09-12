# П18-FSA-замеры 12.09.2026 (`S167`): положение сумм-пиков у модели и у данных, обеими кривыми света.
#   & 'handover\p18-fsa-measures\p18_sumpeaks.ps1' [-Arms 'electron,photon,noanchor'] [-Window 1.0]
# Проба запускается ИЗ оснастки wd_p18 (матрицы и конфигурации приборов — там); выход — рядом с этим файлом.
param(
    [string]$Arms = 'electron,photon,noanchor',
    [double]$Window = 1.0,
    [string[]]$Keys = @('ASN16_Lu176', 'ASN16_Lu176_P0', 'AS80_Lu176', 'G1S16_Co60_P5', 'G1S24_Y88_P5', 'G1S24_Bi207_P5', 'G1S16_Ce139_P5', 'G1S24_Co60_P5', 'G1S16_Y88_P5')
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wd = "$root\tools\CORPUS\scripts\wd_p18"
$here = "$root\handover\p18-fsa-measures"
$spectra = "$root\tools\CORPUS\corpus\spectra"
# состав — из manifest.csv (столбцы chains / nuclides)
$manifest = Import-Csv "$root\tools\CORPUS\corpus\manifest.csv"
$win = $Window.ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
Push-Location $wd
try {
    foreach ($key in $Keys) {
        $row = $manifest | Where-Object { $_.key -eq $key }
        if (-not $row) { Write-Output "нет в манифесте: $key"; continue }
        $args = @("--spectrum=$spectra\$key.xml", "--arms=$Arms", "--window=$win", "--dump=$here\sum_$key.csv")
        if ($row.chains) { $args += "--chain=$($row.chains)" }
        if ($row.nuclides) { $args += "--nuclides=$($row.nuclides)" }
        $log = "$here\sum_$key.log"
        & "$wd\SumPeakLightProbe.exe" @args *> $log
        $rc = $LASTEXITCODE
        Write-Output ("{0}: код {1}" -f $key, $rc)
        Get-Content $log | Select-String -Pattern '^плечо|^GROUP|^кривая суммы|подслой' | ForEach-Object { '  ' + $_.Line }
    }
} finally {
    Pop-Location
}
