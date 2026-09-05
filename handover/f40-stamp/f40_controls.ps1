# Контроли полосы F40 (`T226`/`T138`/`T233`): СТАРЫЙ сторож против НОВОГО на
# ОДНИХ И ТЕХ ЖЕ состояниях синтетического стенда.
#
#   pwsh handover\f40-stamp\f40_controls.ps1 [-Old <вершина git>]
#
# Стенд свой нарочно: настоящее дерево при волне полос движется, и замер посреди
# чужого счёта мерит смесь поколений. Старый сторож берётся из git той же
# командой, что и новый, — дот-сорсингом файла, а не пересказом его правил.
param([string]$Old = 'HEAD')

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$planNew = Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1'
$oldFile = Join-Path ([IO.Path]::GetTempPath()) ("appwd_plan_old_" + [Guid]::NewGuid().ToString('N').Substring(0, 6) + ".ps1")
Push-Location -LiteralPath $repo
try { git show "${Old}:tools/CORPUS/scripts/appwd_plan.ps1" | Set-Content -LiteralPath $oldFile -Encoding utf8 }
finally { Pop-Location }
if (-not (Test-Path -LiteralPath $oldFile)) { throw "не достался старый сторож из git ($Old)" }
"старый сторож: $Old -> $oldFile"
"новый сторож: $planNew"
""

# ── стенд ────────────────────────────────────────────────────────────────────
# Строится ЗАНОВО под каждый случай: перенос состояния между случаями — это
# как раз то, на чём замеры смешивают поколения.
function New-Stand {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("f40_stand_" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $r = Join-Path $root 'repo'; $b = Join-Path $root 'bin'; $p = Join-Path $root 'probes'
    foreach ($d in @((Join-Path $r 'BecquerelMonitor\config'), (Join-Path $r 'BecquerelMonitor\config\device'),
                     (Join-Path $r 'BecquerelMonitor\config\ROI'), (Join-Path $r 'tools\effmaker\probes'),
                     $b, (Join-Path $b 'runtimes\win-x64\native'), (Join-Path $b 'ru'), $p)) {
        New-Item -ItemType Directory -Force $d | Out-Null
    }
    $W = { param($f, $v) Set-Content -LiteralPath $f -Value $v -Encoding ascii }
    & $W (Join-Path $r 'BecquerelMonitor\config\NuclideDefinition.xml') ('<?xml version="1.0"?><NuclideDefinitionFile><NuclideDefinitions>' + ('<Nuclide/>' * 200) + '</NuclideDefinitions></NuclideDefinitionFile>')
    & $W (Join-Path $r 'BecquerelMonitor\config\BecquerelMonitor.xml') '<?xml version="1.0"?><GlobalConfigInfo/>'
    & $W (Join-Path $r 'BecquerelMonitor\config\device\d.xml') '<DeviceConfigInfo/>'
    & $W (Join-Path $r 'BecquerelMonitor\config\ROI\r.xml')    '<ROIConfigData/>'
    # Приложение: проект и ДВА файла в нём. Третий, `VneProekta.cs`, кладётся
    # случаем K2b — он лежит рядом, но в сборку не входит.
    & $W (Join-Path $r 'BecquerelMonitor\BecquerelMonitor.csproj') `
        '<?xml version="1.0"?><Project><ItemGroup><Compile Include="Alpha.cs" /><Compile Include="Beta.cs" /></ItemGroup></Project>'
    & $W (Join-Path $r 'BecquerelMonitor\Alpha.cs') 'class Alpha {}'
    & $W (Join-Path $r 'BecquerelMonitor\Beta.cs')  'class Beta {}'
    # Пробы: две с `Main` (`CorpusFsaProbe` обязательна — на неё стоит план) и
    # один довесок без `Main`, который входит в набор ОБЕИХ.
    & $W (Join-Path $r 'tools\effmaker\probes\CorpusFsaProbe.cs') 'class C { static int Main() { return 0; } }'
    & $W (Join-Path $r 'tools\effmaker\probes\SosedProbe.cs')     'class S { static int Main() { return 0; } }'
    & $W (Join-Path $r 'tools\effmaker\probes\Obshchee.cs')       'class Obshchee {}'
    & $W (Join-Path $b 'BecquerelMonitor.exe')        'app'
    & $W (Join-Path $b 'BecquerelMonitor.exe.config') '<configuration/>'
    & $W (Join-Path $b 'lib.dll')     'dll'
    & $W (Join-Path $b 'baza.sqlite') 'sqlite'
    & $W (Join-Path $b 'runtimes\win-x64\native\e_sqlite3.dll') 'native'
    & $W (Join-Path $b 'ru\x.resources.dll') 'ru'
    Copy-Item -LiteralPath (Join-Path $b 'BecquerelMonitor.exe') -Destination (Join-Path $p 'BecquerelMonitor.exe') -Force
    & $W (Join-Path $p 'CorpusFsaProbe.exe') 'exe1'
    & $W (Join-Path $p 'SosedProbe.exe')     'exe2'
    # Времена: исходники T-2ч, двоичные файлы T-1ч. Так «сборка не старше
    # исходников» верно у обоих сторожей, и оба на целом стенде молчат.
    $t0 = (Get-Date).AddHours(-2); $t1 = (Get-Date).AddHours(-1)
    foreach ($f in @('BecquerelMonitor\BecquerelMonitor.csproj', 'BecquerelMonitor\Alpha.cs', 'BecquerelMonitor\Beta.cs',
                     'tools\effmaker\probes\CorpusFsaProbe.cs', 'tools\effmaker\probes\SosedProbe.cs',
                     'tools\effmaker\probes\Obshchee.cs')) {
        (Get-Item -LiteralPath (Join-Path $r $f)).LastWriteTime = $t0
    }
    foreach ($f in @('BecquerelMonitor.exe', 'BecquerelMonitor.exe.config', 'lib.dll', 'baza.sqlite')) {
        (Get-Item -LiteralPath (Join-Path $b $f)).LastWriteTime = $t1
    }
    foreach ($f in @('BecquerelMonitor.exe', 'CorpusFsaProbe.exe', 'SosedProbe.exe')) {
        (Get-Item -LiteralPath (Join-Path $p $f)).LastWriteTime = $t1
    }
    [pscustomobject]@{ Root = $root; Repo = $r; Bin = $b; Probes = $p; T0 = $t0; T1 = $t1 }
}

# Один замер: свой процесс `pwsh` на каждый сторож — дот-сорсинг двух файлов с
# одноимёнными функциями в один сеанс дал бы победу последнему.
function Measure-Guard {
    param([string]$Guard, $Stand, [switch]$Certify)
    $code = @'
param($guard, $repo, $bin, $probes, $certify)
. $guard
$p = Get-AppWdPlan -Repo $repo -Bin $bin -Wd $probes -ProbeBuild $probes -ProbeCatalog
if ($certify -eq '1') { Write-AppWdStamp -Plan $p -Files 1; exit 0 }
$r = Test-AppWdBuild -Plan $p
@($r.Bad).Count
'@
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("f40_run_" + [Guid]::NewGuid().ToString('N').Substring(0, 6) + ".ps1")
    Set-Content -LiteralPath $tmp -Value $code -Encoding utf8
    try {
        $out = & pwsh -NoProfile -File $tmp $Guard $Stand.Repo $Stand.Bin $Stand.Probes $(if ($Certify) { '1' } else { '0' }) 2>&1
        if ($Certify) { return -1 }
        $last = @($out | Where-Object { $_ -match '^\d+$' } | Select-Object -Last 1)
        if ($last.Count -eq 0) { return "ОШИБКА: " + (($out | Select-Object -Last 3) -join ' / ') }
        [int]$last[0]
    } finally { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
}

$cases = @(
    @{ N='K2a'; Cert=$false; Want='0'
       What='чужая НОВАЯ проба .cs (с Main), exe не собран, время СЕЙЧАС (T165)'
       Do={ param($s) Set-Content -LiteralPath (Join-Path $s.Repo 'tools\effmaker\probes\ChuzhayaProba.cs') -Encoding ascii -Value 'class Ch { static int Main() { return 0; } }' } }
    @{ N='K2b'; Cert=$false; Want='0'
       What='.cs в BecquerelMonitor\ ВНЕ проекта, время СЕЙЧАС (T226)'
       Do={ param($s) Set-Content -LiteralPath (Join-Path $s.Repo 'BecquerelMonitor\VneProekta.cs') -Encoding ascii -Value 'class V {}' } }
    @{ N='K2c'; Cert=$false; Want='0'
       What='чужая проба правлена И пересобрана: её .cs новее ЧУЖОЙ старой .exe (T182)'
       Do={ param($s)
            $cs = Join-Path $s.Repo 'tools\effmaker\probes\SosedProbe.cs'
            Set-Content -LiteralPath $cs -Encoding ascii -Value 'class S { static int Main() { return 7; } }'
            (Get-Item -LiteralPath $cs).LastWriteTime = (Get-Date).AddMinutes(-30)
            (Get-Item -LiteralPath (Join-Path $s.Probes 'SosedProbe.exe')).LastWriteTime = (Get-Date).AddMinutes(-20) } }
    @{ N='K2d'; Cert=$true; Want='0'
       What='файл приложения ТРОНУТ (время СЕЙЧАС), содержимое ТО ЖЕ (T194/T226)'
       Do={ param($s) (Get-Item -LiteralPath (Join-Path $s.Repo 'BecquerelMonitor\Alpha.cs')).LastWriteTime = Get-Date } }
    @{ N='K1a'; Cert=$true; Want='>=1'
       What='содержимое файла ПРИЛОЖЕНИЯ подменено, LastWriteTime СОХРАНЁН (T233)'
       Do={ param($s)
            $f = Join-Path $s.Repo 'BecquerelMonitor\Alpha.cs'; $t = (Get-Item $f).LastWriteTime
            Set-Content -LiteralPath $f -Encoding ascii -Value 'class Alpha { int drugoe; }'
            (Get-Item -LiteralPath $f).LastWriteTime = $t } }
    @{ N='K1b'; Cert=$true; Want='>=1'
       What='содержимое ПРОБЫ подменено, LastWriteTime СОХРАНЁН (T233)'
       Do={ param($s)
            $f = Join-Path $s.Repo 'tools\effmaker\probes\CorpusFsaProbe.cs'; $t = (Get-Item $f).LastWriteTime
            Set-Content -LiteralPath $f -Encoding ascii -Value 'class C { static int Main() { return 9; } }'
            (Get-Item -LiteralPath $f).LastWriteTime = $t } }
    @{ N='K1c'; Cert=$true; Want='>=1'
       What='САМА СБОРКА подменена старой при тех же исходниках (T138)'
       Do={ param($s)
            $f = Join-Path $s.Bin 'BecquerelMonitor.exe'; $t = (Get-Item $f).LastWriteTime
            Set-Content -LiteralPath $f -Encoding ascii -Value 'app-staryi'
            Copy-Item -LiteralPath $f -Destination (Join-Path $s.Probes 'BecquerelMonitor.exe') -Force
            (Get-Item -LiteralPath $f).LastWriteTime = $t
            (Get-Item -LiteralPath (Join-Path $s.Probes 'BecquerelMonitor.exe')).LastWriteTime = $t } }
    @{ N='K3a'; Cert=$false; Want='>=1'
       What='ИСХОДНИК ПРИЛОЖЕНИЯ правлен и НОВЕЕ сборки — прежний отказ T41/B20/B21'
       Do={ param($s)
            $f = Join-Path $s.Repo 'BecquerelMonitor\Beta.cs'
            Set-Content -LiteralPath $f -Encoding ascii -Value 'class Beta { int novoe; }'
            (Get-Item -LiteralPath $f).LastWriteTime = Get-Date } }
    @{ N='K3b'; Cert=$false; Want='>=1'
       What='СВОЙ исходник пробы новее СВОЕЙ пробы — прежний отказ'
       Do={ param($s)
            $f = Join-Path $s.Repo 'tools\effmaker\probes\CorpusFsaProbe.cs'
            Set-Content -LiteralPath $f -Encoding ascii -Value 'class C { static int Main() { return 5; } }'
            (Get-Item -LiteralPath $f).LastWriteTime = Get-Date } }
    @{ N='K3c'; Cert=$false; Want='>=1'
       What='ДОВЕСОК (файл без Main) правлен и новее проб — входит в набор КАЖДОЙ'
       Do={ param($s)
            $f = Join-Path $s.Repo 'tools\effmaker\probes\Obshchee.cs'
            Set-Content -LiteralPath $f -Encoding ascii -Value 'class Obshchee { int x; }'
            (Get-Item -LiteralPath $f).LastWriteTime = Get-Date } }
    @{ N='K3d'; Cert=$false; Want='>=1'
       What='пробы собраны ПРОТИВ ДРУГОГО приложения — прежний отказ'
       Do={ param($s) Set-Content -LiteralPath (Join-Path $s.Probes 'BecquerelMonitor.exe') -Encoding ascii -Value 'chuzhoe' } }
    @{ N='K0';  Cert=$false; Want='0'
       What='ЦЕЛЫЙ стенд, ничего не тронуто (отрицательный контроль)'
       Do={ param($s) } }
    @{ N='K0z'; Cert=$true;  Want='0'
       What='ЦЕЛЫЙ стенд, каталог ЗАВЕРЕН (отрицательный контроль)'
       Do={ param($s) } }
)

"{0,-5} {1,-4} {2,-6} {3,-6} {4}" -f '№', 'зав.', 'СТАР', 'НОВ', 'состояние'
"".PadRight(110, '-')
$rows = @()
foreach ($c in $cases) {
    $s = New-Stand
    try {
        if ($c.Cert) { Measure-Guard -Guard $planNew -Stand $s -Certify | Out-Null }
        & $c.Do $s
        $o = Measure-Guard -Guard $oldFile -Stand $s
        $n = Measure-Guard -Guard $planNew -Stand $s
        $ok = if ($c.Want -eq '0') { $n -eq 0 } else { $n -ge 1 }
        $rows += [pscustomobject]@{ N = $c.N; Cert = $c.Cert; Old = $o; New = $n; Want = $c.Want; Ok = $ok; What = $c.What }
        "{0,-5} {1,-4} {2,-6} {3,-6} {4}" -f $c.N, $(if ($c.Cert) { 'да' } else { 'нет' }), $o, $n, $c.What
    } finally { Remove-Item -LiteralPath $s.Root -Recurse -Force -ErrorAction SilentlyContinue }
}
""
$bad = @($rows | Where-Object { -not $_.Ok })
if ($bad.Count) {
    "⛔ НЕ СОШЛОСЬ: " + (($bad | ForEach-Object { "{0} ждали {1}, вышло {2}" -f $_.N, $_.Want, $_.New }) -join '; ')
} else {
    "✅ ВСЕ {0} СЛУЧАЕВ СОШЛИСЬ С ОЖИДАНИЕМ" -f $rows.Count
}

# ── K4: отметка меняется РОВНО с набором ─────────────────────────────────────
""
"K4: отпечаток набора приложения"
$s = New-Stand
try {
    . $planNew
    $f = Join-Path $s.Repo 'BecquerelMonitor\Alpha.cs'
    $t = (Get-Item -LiteralPath $f).LastWriteTime
    $fp0 = (Get-AppWdSourceRecord -Repo $s.Repo).App.Fp
    (Get-Item -LiteralPath $f).LastWriteTime = Get-Date
    $fp1 = (Get-AppWdSourceRecord -Repo $s.Repo).App.Fp
    (Get-Item -LiteralPath $f).LastWriteTime = $t
    Set-Content -LiteralPath $f -Encoding ascii -Value 'class Alpha { int y; }'
    (Get-Item -LiteralPath $f).LastWriteTime = $t
    $fp2 = (Get-AppWdSourceRecord -Repo $s.Repo).App.Fp
    Set-Content -LiteralPath $f -Encoding ascii -Value 'class Alpha {}'
    (Get-Item -LiteralPath $f).LastWriteTime = $t
    $fp3 = (Get-AppWdSourceRecord -Repo $s.Repo).App.Fp
    Set-Content -LiteralPath (Join-Path $s.Repo 'BecquerelMonitor\VneProekta.cs') -Encoding ascii -Value 'class V {}'
    $fp4 = (Get-AppWdSourceRecord -Repo $s.Repo).App.Fp
    "  исходный набор            {0}" -f $fp0.Substring(0, 16)
    "  тронут (время)            {0}  {1}" -f $fp1.Substring(0, 16), $(if ($fp1 -eq $fp0) { 'ТОТ ЖЕ — верно' } else { '⛔ изменился' })
    "  правлено содержимое       {0}  {1}" -f $fp2.Substring(0, 16), $(if ($fp2 -ne $fp0) { 'ИНОЙ — верно' } else { '⛔ тот же' })
    "  правка откачена           {0}  {1}" -f $fp3.Substring(0, 16), $(if ($fp3 -eq $fp0) { 'ТОТ ЖЕ — верно' } else { '⛔ изменился' })
    "  добавлен .cs вне проекта  {0}  {1}" -f $fp4.Substring(0, 16), $(if ($fp4 -eq $fp0) { 'ТОТ ЖЕ — верно' } else { '⛔ изменился' })
} finally { Remove-Item -LiteralPath $s.Root -Recurse -Force -ErrorAction SilentlyContinue }
Remove-Item -LiteralPath $oldFile -Force -ErrorAction SilentlyContinue
