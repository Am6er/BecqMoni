# Полоса G4 (`T237`): прогнать набор проб одного плеча из каталога build_g4 с
# пределом времени на пробу, вывод каждой — в файл `<OutDir>\<имя>.txt`
# (первой строкой — код возврата). Рабочий каталог — build_g4 (config\ у
# приложения относительный).
#
#   pwsh handover\g4-target-framework\run_g4.ps1 -Suffix _before -OutDir handover\g4-target-framework\before
param([string]$Suffix = '', [string]$OutDir, [int]$CapSec = 300, [string[]]$Only = @())
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wd = Join-Path $repo 'tools\effmaker\probes\build_g4'
$spectra = Join-Path $repo 'tools\CORPUS\corpus\spectra'
# Каталог вывода — АБСОЛЮТНЫЙ: пробы бегут из build_g4, и относительный путь
# к csv уехал бы туда (поймано: DirectoryNotFoundException у F41/F49/LabelTruth).
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }
$scratch = Join-Path $OutDir 'csv'
New-Item -ItemType Directory -Force $OutDir, $scratch | Out-Null
$set = [ordered]@{
    'CalibGraphProbeF35'        = @()
    'CultureProbeO14'           = @()
    'CultureProbeO14_noattr'    = @()
    'GraphCultureProbeF27'      = @()
    'LabelPathProbeF49'         = @("--spectra=$spectra", "--csv=$scratch\f49.csv")
    'MatrixRefusalProbeP8'      = @()
    'ModalThreadProbeO25'       = @()
    'PeakFwhmUnitsProbeF41'     = @("--spectra=$spectra", "--dump=$scratch\f41.csv")
    'RestCultureProbeF28'       = @()
    'RestCultureProbeF47'       = @()
    'FsaBackgroundMarkProbeF48' = @()
    'ModalReachProbeF22'        = @()
    'ReasonProbe'               = @()
    'LabelTruthProbe'           = @("--spectra=$spectra", "--csv=$scratch\labels.csv")
}
$Only = @(($Only -join ',') -split ',' | Where-Object { $_ })
foreach ($name in $set.Keys) {
    if ($Only.Count -and $name -notin $Only) { continue }
    $exe = Join-Path $wd ($name + $Suffix + '.exe')
    if (-not (Test-Path -LiteralPath $exe)) { "НЕТ $exe"; continue }
    $outFile = Join-Path $OutDir ($name + '.txt')
    $errFile = Join-Path $OutDir ($name + '.err.txt')
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # ⛔ `Start-Process -ArgumentList` режет путь с пробелами (этот репозиторий:
    #    `BQ Eng res .NET 4.8`) — грабля из CLAUDE.md, поймана и здесь: три
    #    пробы отказали «неизвестный ключ: Eng». Каждый довод — в кавычках.
    $argv = @($set[$name] | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } })
    if ($argv.Count) {
        $p = Start-Process -FilePath $exe -ArgumentList $argv -WorkingDirectory $wd -PassThru -NoNewWindow `
                -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    } else {
        $p = Start-Process -FilePath $exe -WorkingDirectory $wd -PassThru -NoNewWindow `
                -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    }
    $done = $p.WaitForExit($CapSec * 1000)
    if (-not $done) { try { $p.Kill() } catch {}; $code = 'ПРЕДЕЛ ' + $CapSec + ' с' } else { $code = $p.ExitCode }
    $sw.Stop()
    "{0,-28} код {1,-14} {2,7:F1} с  {3} строк" -f ($name + $Suffix), $code, $sw.Elapsed.TotalSeconds, (Get-Content -LiteralPath $outFile -ErrorAction SilentlyContinue | Measure-Object -Line).Lines
    Set-Content -LiteralPath (Join-Path $OutDir ($name + '.code')) -Value $code -Encoding ascii
}
