# Прочитать 26 слов отказа ОБРАТНО из СОБРАННОЙ сборки — через ту самую
# обёртку `DoseRateCoefficients.Text`, которой пользуется приложение (`A237`).
#
# ⛔ Читается не `.resx`, а выход сборки: нейтральные ресурсы внутри
# `BecquerelMonitor.exe` и сателлит `ru\BecquerelMonitor.resources.dll`. Иначе
# не доказано, что перевод доезжает до человека.
param(
    [string]$Bin = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\bin\Debug_O11',
    [string]$Out = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\o11-refusal-words\readback.txt'
)

$ErrorActionPreference = 'Stop'
[System.IO.Directory]::SetCurrentDirectory($Bin)
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $Bin 'BecquerelMonitor.exe'))
$t = $asm.GetType('BecquerelMonitor.DoseRateCoefficients')
$m = $t.GetMethod('Text', [System.Reflection.BindingFlags]'Public,Static')

$keys = @(
  'DoseRateBadEfficiency','DoseRateBadScale','DoseRateCurveBadEnergy','DoseRateCurveBadValue',
  'DoseRateCurveTooShort','DoseRateEmptyRange','DoseRateEmptySpectrum','DoseRateEnergyNotPositive',
  'DoseRateEtalonEmpty','DoseRateFileUnusable','DoseRateLsrmNoHeader','DoseRateLsrmNoPoints',
  'DoseRateLsrmShortLine','DoseRateNoCalibration','DoseRateNoEfficiency','DoseRateNoElement',
  'DoseRateNoExpected','DoseRateNoGrid','DoseRateNoScale','DoseRateNoSpectrum','DoseRateNoTime',
  'DoseRateNotFinite','DoseRateOutsideCurve','DoseRateOutsideIcrp','DoseRateOutsideXcom',
  'DoseRatePartialCoverage')

# ⚠ Маячок вместо настоящей запасной надписи: если ключа в ресурсах НЕТ,
# обёртка вернёт именно его — и это видно, а не спрятано за совпадением.
$beacon = '@@ЗАПАСНОЕ@@'

$lines = New-Object System.Collections.Generic.List[string]
$satellite = Join-Path $Bin 'ru\BecquerelMonitor.resources.dll'
$lines.Add("сателлит ru: $satellite  есть=$([System.IO.File]::Exists($satellite))")
$lines.Add('')

$read = @{}
foreach ($culture in @('en-US','ru-RU')) {
    [System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::GetCultureInfo($culture)
    [System.Threading.Thread]::CurrentThread.CurrentCulture   = [System.Globalization.CultureInfo]::GetCultureInfo($culture)
    $read[$culture] = @{}
    $lines.Add("=== культура $culture")
    foreach ($k in $keys) {
        $v = $m.Invoke($null, @([object]$k, [object]$beacon))
        $read[$culture][$k] = $v
        $lines.Add(("{0}`t{1}" -f $k, $v))
    }
    $lines.Add('')
}

$missing = 0; $same = 0
foreach ($k in $keys) {
    if ($read['en-US'][$k] -eq $beacon) { $missing++; $lines.Add("КЛЮЧА НЕТ (en): $k") }
    if ($read['ru-RU'][$k] -eq $beacon) { $missing++; $lines.Add("КЛЮЧА НЕТ (ru): $k") }
    if ($read['en-US'][$k] -eq $read['ru-RU'][$k]) { $same++; $lines.Add("СТРОКИ СОВПАЛИ: $k") }
}
$lines.Add('')
$lines.Add("ключей: $($keys.Count)")
$lines.Add("вернулось запасное (ключа нет в собранных ресурсах): $missing")
$lines.Add("русская строка не отличается от английской: $same")
if ($missing -eq 0 -and $same -eq 0) { $lines.Add('СОШЛОСЬ') } else { $lines.Add('РАЗОШЛОСЬ') }

[System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($true)))
$lines[-4..-1] | ForEach-Object { $_ }
if ($missing -eq 0 -and $same -eq 0) { exit 0 } else { exit 1 }
