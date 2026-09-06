# F72 / A263. ⛔ Отдельный контроль: у ЧЕТЫРЁХ из четырнадцати проб позиционный
# довод ОСМЫСЛЕН, и слепой `else` его убил бы. Здесь показано, что он жив.
#
# Каждой пробе три вызова, ДО и ПОСЛЕ:
#   (а) позиционный довод ВЕРНЫЙ            — прогон идёт (тот же код, что ДО);
#   (б) позиционный довод НЕСУЩЕСТВУЮЩИЙ    — проба говорит СВОЁ («нет каталога»,
#       «не найден»), а НЕ «не знаю ключа»: значит довод дошёл до своей ветки;
#   (в) неизвестный КЛЮЧ                    — «не знаю ключа», код 2.
# (б) — тот самый положительный контроль: без него «(а) прошло» одинаково
# объясняется и живой веткой, и веткой, которую никто не проверял.
[CmdletBinding(PositionalBinding = $false)]
param([int]$TimeoutSec = 200)

$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$before = Join-Path $repo 'tools\effmaker\probes\build_f72d'
$after = Join-Path $repo 'tools\effmaker\probes\build_f72c'
$work = Join-Path $repo 'handover\f72-a263\runs'
New-Item -ItemType Directory -Force $work | Out-Null
$M = Join-Path $repo 'tools\effmaker\models'
$GAD = Join-Path $repo 'tools\interspec\gadras'
$SPEC = Join-Path $repo 'tools\CORPUS\corpus\spectra\AS80_Cs137_0cm.xml'
$NOPE = Join-Path $repo 'tools\effmaker\models_NO_SUCH_DIR'
$NOSP = Join-Path $repo 'tools\CORPUS\corpus\spectra\NO_SUCH_FILE.xml'
$DEV = Join-Path $after 'config\device\AtomSpectraVCP.xml'
$ROI = @(Get-ChildItem (Join-Path $after 'config\ROI\*.xml'))[0].FullName

function Run-Probe {
    param([string]$Dir, [string]$Name, [string[]]$ProbeArgs)
    $si = [Diagnostics.ProcessStartInfo]::new()
    $si.FileName = (Join-Path $Dir ($Name + '.exe'))
    $si.WorkingDirectory = $Dir
    $si.UseShellExecute = $false
    $si.RedirectStandardOutput = $true
    $si.RedirectStandardError = $true
    $si.StandardOutputEncoding = [Text.Encoding]::UTF8
    $si.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($x in $ProbeArgs) { $si.ArgumentList.Add($x) }
    $pr = [Diagnostics.Process]::Start($si)
    $to = $pr.StandardOutput.ReadToEndAsync()
    $te = $pr.StandardError.ReadToEndAsync()
    $done = $pr.WaitForExit($TimeoutSec * 1000)
    if (-not $done) { try { $pr.Kill($true) } catch {}; $pr.WaitForExit(5000) | Out-Null }
    $txt = $to.Result + "`n" + $te.Result
    return @{ Code = $(if ($done) { $pr.ExitCode } else { 'ОБОРВАН' }); Txt = $txt }
}

function Say { param($R)
    $line = ($R.Txt -split "`r?`n" | Where-Object { $_.Trim() -ne '' -and $_ -notmatch '^BecqMoni:' } | Select-Object -First 1)
    if ($null -eq $line) { $line = '(пусто)' }
    if ($line.Length -gt 74) { $line = $line.Substring(0, 74) + '…' }
    return $line
}

$cases = @(
  @{ N = 'GadrasProbe';   Ok = @($GAD, '--n=200', "--csv=$work\p_gad.csv"); Bad = @($NOPE, '--n=200', "--csv=$work\p_gad.csv"); Key = @($GAD, '--n=200', '--nn=1') }
  @{ N = 'ResponseProbe'; Ok = @($GAD, '--n=200', "--csv=$work\p_res.csv", '--fast'); Bad = @($NOPE, '--n=200', "--csv=$work\p_res.csv", '--fast'); Key = @($GAD, '--fast', '--no-xrays') }
  @{ N = 'RoundTrip';     Ok = @($M, "$work\p_rt"); Bad = @($NOPE, "$work\p_rt"); Key = @($M, "$work\p_rt", '--brake=order') }
  @{ N = 'MeasuredPoint'; Ok = @($SPEC, '1000'); Bad = @($NOSP, '1000'); Key = @($SPEC, '1000', '--windo=4') }
  @{ N = 'DecayReadersProbe'; Ok = @('--limit=3', 'Cs-137'); Bad = @('--limit=3', 'Xx-999'); Key = @('--limit=3', '--limi=3') }
  @{ N = 'EffDipProbe'; Ok = @($DEV, '--hist=200'); Bad = @($DEV, 'НЕТ_ТАКОЙ_КРИВОЙ', '--hist=200'); Key = @($DEV, '--hist2=200') }
  @{ N = 'OrderProbe'; Ok = @('--chain=Th-232', $SPEC); Bad = @('--chain=Th-232', $NOSP); Key = @('--chain=Th-232', $SPEC, '--reff=x') }
)

$rows = @()
foreach ($c in $cases) {
    foreach ($arm in @('Ok', 'Bad', 'Key')) {
        $b = Run-Probe -Dir $before -Name $c.N -ProbeArgs $c.$arm
        $a = Run-Probe -Dir $after  -Name $c.N -ProbeArgs $c.$arm
        $rows += [pscustomobject]@{
            Проба   = $c.N
            плечо   = switch ($arm) { 'Ok' { 'позиц. ВЕРНЫЙ' } 'Bad' { 'позиц. НЕТ ТАКОГО' } 'Key' { 'неизв. КЛЮЧ' } }
            'ДО'    = $b.Code
            'ПОСЛЕ' = $a.Code
            'первая строка ПОСЛЕ' = (Say $a)
            'сказал «не знаю ключа»' = if ($a.Txt -match 'не знаю ключа') { 'да' } else { '-' }
        }
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $repo 'handover\f72-a263\positional.csv')
