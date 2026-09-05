# ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОЙ САМОПРОВЕРКИ (`T79`, `T226`; F46 06.09.2026).
#
# «Самопроверка прошла» значит что-то только тогда, когда известно, что она
# УМЕЕТ НЕ ПРОЙТИ. Ровно этим 05.09.2026 и обманулся стенд: он мерил набор
# приложения, пробы остались на правиле времени, и `build_all.ps1` отчитался
# кодом 0 при наполовину сделанной правке.
#
# Стенд поднимается в СВОЁМ корне (`$PSScriptRoot\..\..\..` у `build_all.ps1`),
# и трогать общее дерево не нужно: самопроверка стоит ДО сборки проб и до
# построения плана репозитория, поэтому в теневом корне довольно двух файлов.
param(
    [string]$Bin = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\bin\Debug_F46'
)
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

# Каждая порча — ЗАМЕНА В ТЕКСТЕ сторожа. Не сработала замена — это отказ
# замера, а не «порча не понадобилась»: молча пропущенная порча выглядела бы
# как пройденный контроль (`T79`).
$spoil = @(
    @{ Id = 'П1'; What = 'пробы судятся ВРЕМЕНЕМ ВСЕГДА (состояние до правки F46)'
       From = '$byTime = $Certifying -or -not $wasFp'
       To   = '$byTime = $true' }
    @{ Id = 'П2'; What = 'сверка набора ПРОБ с заверенным снята (сторож слеп к T233 у проб)'
       From = '} elseif ($wasFp -ne $set.Fp) {'
       To   = '} elseif ($false) {' }
    @{ Id = 'П3'; What = 'отпечаток не зависит от СОДЕРЖИМОГО — считается по одним путям'
       From = '"{0}`t{1}" -f $k, $(if ($v -is [string]) { $v } else { $v.Sha })'
       To   = '"{0}" -f $k' }
    @{ Id = 'П4'; What = 'сверка САМИХ ПРОБ с заверенными снята (подмена exe проходит)'
       From = 'if (-not $Certifying -and $prevBin -and $prevBin.PSObject.Properties[$key]) {'
       To   = 'if ($false) {' }
)
$src = Get-Content -LiteralPath (Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1') -Raw

# ⚠ Судим НЕ ПО КОДУ ВОЗВРАТА, а по клейму самопроверки. В теневом корне лежат
#   ровно два файла, поэтому ПОСЛЕ самопроверки скрипт законно отказывает кодом
#   3 («Get-AppWdProbeSources не вернула ни одного .cs»): по коду целый сторож и
#   порченый выглядели бы одинаково, и замер мерил бы не то (поймано первым
#   прогоном этого же контроля).
$banner = 'СТОРОЖ ПРОВАЛИЛ САМОПРОВЕРКУ'
Write-Host '=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОПРОВЕРКИ build_all.ps1 ==='
Write-Host 'целый сторож обязан пройти самопроверку, каждый порченый — провалить'
Write-Host ''
$rows = @()
foreach ($c in (@(@{ Id = 'ЦЕЛ'; What = 'сторож НЕ ТРОНУТ (отрицательный контроль)'; From = ''; To = '' }) + $spoil)) {
    $t = Join-Path $env:TEMP ('f46pc_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force (Join-Path $t 'tools\CORPUS\scripts')   | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $t 'tools\effmaker\probes')  | Out-Null
    $text = $src
    $applied = $true
    # ⚠ `.Contains`, а НЕ `-like`: у `-like` квадратные скобки — это класс
    #   символов, и `$v -is [string]` внутри образца не совпал бы ни с чем.
    #   Порча тихо не применялась, а контроль печатал «не сошлось» (F46).
    if ($c.From) {
        if (-not $text.Contains($c.From)) { $applied = $false }
        else { $text = $text.Replace($c.From, $c.To) }
    }
    Set-Content -LiteralPath (Join-Path $t 'tools\CORPUS\scripts\appwd_plan.ps1') -Value $text -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $repo 'tools\effmaker\probes\build_all.ps1') `
              -Destination (Join-Path $t 'tools\effmaker\probes\build_all.ps1') -Force
    $out = Join-Path $t 'out'
    $log = & pwsh -NoProfile -File (Join-Path $t 'tools\effmaker\probes\build_all.ps1') -Bin $Bin -Out $out 2>&1
    $code = $LASTEXITCODE
    $fell = @($log | Where-Object { ([string]$_).Contains($banner) }).Count -gt 0
    $why  = @($log | Where-Object { $_ -match 'сторож ОТКАЗАЛ|сторож ПРОМОЛЧАЛ|отпечаток набора|ЦЕЛОМ стенде' } | Select-Object -First 1)
    $rows += [pscustomobject]@{
        Id = $c.Id; What = $c.What; Applied = $applied; Code = $code; Fell = $fell
        Want = ($c.Id -ne 'ЦЕЛ')
        Why  = ([string]$why).Trim()
    }
    Remove-Item -LiteralPath $t -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host ('{0,-5} {1,-12} {2,-12} {3,-5} {4}' -f 'ID', 'самопроверка', 'ждали', 'код', 'порча сторожа')
Write-Host ('-' * 118)
foreach ($r in $rows) {
    $mark = if (-not $r.Applied) { '⛔ ПОРЧА НЕ ПРИМЕНИЛАСЬ — замер негоден' }
            elseif ($r.Fell -eq $r.Want) { '' } else { '⛔ НЕ СОШЛОСЬ' }
    Write-Host ('{0,-5} {1,-12} {2,-12} {3,-5} {4}  {5}' -f $r.Id,
                $(if ($r.Fell) { 'ПРОВАЛЕНА' } else { 'пройдена' }),
                $(if ($r.Want) { 'ПРОВАЛЕНА' } else { 'пройдена' }), $r.Code, $r.What, $mark)
    if ($r.Why) { Write-Host ('        сказал: ' + $r.Why) }
}
$bad = @($rows | Where-Object { -not $_.Applied -or $_.Fell -ne $_.Want })
Write-Host ''
if ($bad.Count -eq 0) { Write-Host ('✅ САМОПРОВЕРКА УМЕЕТ НЕ ПРОЙТИ: {0} порчи из {0} пойманы, целый сторож прошёл' -f $spoil.Count) }
else { Write-Host ('⛔ НЕ СОШЛОСЬ: {0}' -f $bad.Count) -ForegroundColor Red }
