# F72 / A263. Два плеча на каждую правленую пробу:
#   (1) неизвестное ИМЯ ключа -> код 2 и ключ НАЗВАН;
#   (2) законный вызов проходит КАК ПРЕЖДЕ (сверка с ДО-сборкой из HEAD).
# ДО — tools\effmaker\probes\build_f72b (те же 14 проб, собранные из HEAD тем же
# csc с теми же ссылками и довесками), ПОСЛЕ — build_f72.
[CmdletBinding(PositionalBinding = $false)]
param([string[]]$Only = @(), [int]$TimeoutSec = 90,
      [string]$BeforeDir = 'build_f72d', [string]$AfterDir = 'build_f72c')

$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$before = Join-Path $repo ('tools\effmaker\probes\' + $BeforeDir)
$after = Join-Path $repo ('tools\effmaker\probes\' + $AfterDir)
$work = Join-Path $repo 'handover\f72-a263\runs'
New-Item -ItemType Directory -Force $work | Out-Null

# ⛔ ArgumentList КОЛЛЕКЦИЕЙ, а не строкой (`Start-Process -ArgumentList` режет
# путь этого дерева по пробелам — «BQ Eng res .NET 4.8», грабля из `CLAUDE.md`).
# Поймано на себе первым же прогоном: пробе доставались обрывки пути, и она
# отказывала «не знаю ключа: Eng» — отказ осмысленный и не про то, чем заняты.
function Run-Probe {
    param([string]$Dir, [string]$Name, [string[]]$ProbeArgs, [string]$Tag)
    $exe = Join-Path $Dir ($Name + '.exe')
    $out = Join-Path $work ("$Tag.txt")
    $err = Join-Path $work ("$Tag.err.txt")
    $si = [Diagnostics.ProcessStartInfo]::new()
    $si.FileName = $exe
    $si.WorkingDirectory = $Dir
    $si.UseShellExecute = $false
    $si.RedirectStandardOutput = $true
    $si.RedirectStandardError = $true
    $si.StandardOutputEncoding = [Text.Encoding]::UTF8
    $si.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($x in $ProbeArgs) { $si.ArgumentList.Add($x) }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $pr = [Diagnostics.Process]::Start($si)
    $to = $pr.StandardOutput.ReadToEndAsync()
    $te = $pr.StandardError.ReadToEndAsync()
    $done = $pr.WaitForExit($TimeoutSec * 1000)
    if (-not $done) { try { $pr.Kill($true) } catch {} ; $pr.WaitForExit(5000) | Out-Null }
    $sw.Stop()
    [IO.File]::WriteAllText($out, $to.Result, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($err, $te.Result, [Text.UTF8Encoding]::new($false))
    $code = if ($done) { $pr.ExitCode } else { 'ОБОРВАН' }
    return @{ Code = $code; Sec = $sw.Elapsed.TotalSeconds; Out = $out; Err = $err }
}

# Имя каталога сборки печатают сами пробы — оно ОБЯЗАНО различаться
# (build_f72b против build_f72) и сверке вывода не подлежит.
function Norm {
    param([string]$T)
    if ($null -eq $T) { return '' }
    return ($T -replace 'build_f72[a-z]?', 'build_fXX')
}

function Said-Refusal {
    param([hashtable]$R, [string]$Key)
    $t = ''
    foreach ($f in @($R.Out, $R.Err)) { if (Test-Path $f) { $t += (Get-Content -Raw -LiteralPath $f -ErrorAction SilentlyContinue) } }
    return ($t -match 'не знаю ключа') -and ($t -match [regex]::Escape($Key))
}

# ── таблица случаев ─────────────────────────────────────────────────────────
# Legit  — законный вызов, короткий; Bogus — он же плюс НЕИЗВЕСТНОЕ имя ключа.
$M = Join-Path $repo 'tools\effmaker\models'
$G = Join-Path $repo 'tools\CORPUS\corpus\geometries'
$A = $after                    # поставочный config\ лежит рядом с пробами
$SPECDIR = Join-Path $repo 'tools\CORPUS\corpus\spectra'
$SPECTRUM = Join-Path $SPECDIR 'AS80_Cs137_0cm.xml'
$ROI = @(Get-ChildItem (Join-Path $A 'config\ROI\*.xml'))[0].FullName
$cases = @(
  @{ N = 'StringsProbeF55';        L = @("--out=$work\s55.txt");                                        B = '--nosuchkey=1' }
  @{ N = 'CultureProbeO14';        L = @("--out=$work\o14.txt");                                        B = '--modalcontrol' }
  @{ N = 'BoundProbeF59';          L = @("--report=$work\f59.txt");                                     B = '--brake=x' }
  @{ N = 'MatrixRefusalProbeP8';   L = @("--out=$work\p8.txt", "--geometry=$M\Nano16Pro.in");           B = '--geometr=x' }
  @{ N = 'RoundTrip';              L = @("$M", "$work\rt");                                             B = '--brake=order' }
  @{ N = 'GadrasProbe';            L = @((Join-Path $repo 'tools\interspec\gadras'), '--n=200', "--csv=$work\gad.csv"); B = '--nn=200' }
  # ⚠ ResponseProbe обходит ПОДКАТАЛОГИ довода: на `models` их нет и вывод пуст —
  # такой «прошёл» не меряет ничего. Берём поставку GADRAS, где подкаталоги есть.
  @{ N = 'ResponseProbe';          L = @((Join-Path $repo 'tools\interspec\gadras'), '--n=200', "--csv=$work\resp.csv", '--fast'); B = '--no-xrays' }
  @{ N = 'ResponseMatrixProbe';    L = @("--geometry=$M\Nano16Pro.in", '--nodes=2', '--n=200', '--emin=100', '--emax=200'); B = '--node=2' }
  @{ N = 'ResponseChannelProbe';   L = @("--geometry=$M\Nano16Pro.in", '--n=200', '--bin=10', '--e=300'); B = '--ee=300' }
  @{ N = 'ResponseShapeProbe';     L = @("--geometry=$M\Nano16Pro.in", '--nodes=2', '--n=200', '--ref=200', '--energy=300'); B = '--no-xrayz' }
  @{ N = 'ResponseInterpProbe';    L = @("--geometry=$M\Nano16Pro.in", '--nodes=2', '--n=200', '--ref=200', "--csv=$work\int.csv"); B = '--node=2' }
  @{ N = 'ResponseMatrixFormProbe'; L = @("--geometry=$M\Nano16Pro.in", '--nodes=2', '--histories=200', '--threads=1'); B = '--movee=height' }
  # ⚠ Без --spectrum= проба отказывает кодом 2 ДО разбора остального: такой
  # «прошёл» не меряет ничего, поэтому спектр настоящий.
  # ⛔ Скобки вокруг склейки ОБЯЗАТЕЛЬНЫ: в PowerShell запятая связывает КРЕПЧЕ
  # плюса, и «"--spectrum=" + (путь), "--out=…", …» склеивается в ОДИН довод.
  # Поймано на себе: проба падала NotSupportedException в Load — отказ
  # осмысленный и не про то, чем заняты. Тот же класс, что чинит сама `A263`.
  @{ N = 'FsaPaletteProbe';        L = @(("--spectrum=" + (Join-Path $repo 'tools\CORPUS\corpus\spectra\AS80_Cs137_0cm.xml')), "--out=$work\pal", '--width=400', '--height=300'); B = '--spectr=x' }
  # ⚠ У MeasuredPoint доводы СМЕШАНЫ: args[2]/args[3] читаются как геометрия и
  # расстояние, когда доводов ЧЕТЫРЕ и больше. Поэтому и законный, и мусорный
  # вызов держатся на ТРЁХ доводах — иначе мерилось бы падение на args[2],
  # а не молчание разбора (поймано первым прогоном: ДО дал 0xE0434352).
  @{ N = 'MeasuredPoint';          L = @((Join-Path $repo 'tools\CORPUS\corpus\spectra\AS80_Cs137_0cm.xml'), '1000', '--window=4')
                                   B0 = @((Join-Path $repo 'tools\CORPUS\corpus\spectra\AS80_Cs137_0cm.xml'), '1000', '--windo=4')
                                   B = '--windo=4' }

  # ── лёгкие: те же два плеча ───────────────────────────────────────────────
  @{ N = 'CalibGraphProbeF35';     L = @("--out=$work\f35.txt");                                        B = '--shw=ru' }
  @{ N = 'CalibrationNanProbeF48'; L = @();                                                             B = '--corpu=x' }
  @{ N = 'CascadeXrayProbe';       L = @('--window=3');                                                 B = '--windo=3' }
  # Штатный вызов CrashLogProbe — БЕЗ ключей (README); --mark=/--no-subscribe
  # понимает только ребёнок, и родитель составляет их сам.
  @{ N = 'CrashLogProbe';          L = @();                                                             B = '--no-subscrib' }
  @{ N = 'DecayReadersProbe';      L = @('--limit=3', 'Cs-137');                                        B = '--limi=3' }
  @{ N = 'EffDipProbe';            L = @((Join-Path $A 'config\device\AtomSpectraVCP.xml'), '--hist=200'); B = '--hist2=200' }
  @{ N = 'GraphCultureProbeF27';   L = @("--out=$work\f27.txt");                                        B = '--modalcontrol' }
  @{ N = 'GridStampProbeF45';      L = @('--fast');                                                     B = '--fastt' }
  @{ N = 'ModalThreadProbeO25';    L = @("--out=$work\o25.txt");                                        B = '--negativ' }
  # ⚠ --ref= у OrderProbe — ОПОРНАЯ КРИВАЯ (EfficiencyFitter.LoadReferenceCurve),
  # а не конфигурация ROI, как читается по строке подсказки. Ключ необязателен;
  # подсунутый ROI-xml роняет пробу ОДИНАКОВО на обеих сторонах — не мерит ничего.
  @{ N = 'OrderProbe';             L = @('--chain=Th-232', $SPECTRUM);                                  B = '--reff=x' }
  @{ N = 'PeakFwhmUnitsProbeF41';  L = @(("--spectra=" + $SPECDIR), "--dump=$work\f41.csv");            B = '--spectr=x' }
  @{ N = 'PeakOriginProbe';        L = @(("--spectra=" + $SPECDIR), "--csv=$work\po.csv");              B = '--grup=x' }
  @{ N = 'RestCultureProbeF28';    L = @("--out=$work\rc28.txt");                                       B = '--modalcontrol' }
  @{ N = 'RestCultureProbeF47';    L = @("--out=$work\rc47.txt", '--os=ru-RU');                         B = '--sweeep' }
  @{ N = 'RoiLoadProbe';           L = @(("--dir=" + (Join-Path $A 'config\ROI')));                     B = '--dirr=x' }
)

$rows = @()
foreach ($c in $cases) {
    if ($Only.Count -gt 0 -and $c.N -notin $Only) { continue }
    $n = $c.N
    Write-Host ("=== " + $n) -ForegroundColor Cyan

    # плечо 1: неизвестный ключ
    $bogus = if ($c.ContainsKey('B0')) { @($c.B0) } else { @($c.L) + @($c.B) }
    $b1 = Run-Probe -Dir $before -Name $n -ProbeArgs $bogus -Tag "$n.bogus.before"
    $a1 = Run-Probe -Dir $after  -Name $n -ProbeArgs $bogus -Tag "$n.bogus.after"
    $b1s = Said-Refusal $b1 $c.B
    $a1s = Said-Refusal $a1 $c.B

    # плечо 2: законный вызов
    $b2 = Run-Probe -Dir $before -Name $n -ProbeArgs @($c.L) -Tag "$n.legit.before"
    $a2 = Run-Probe -Dir $after  -Name $n -ProbeArgs @($c.L) -Tag "$n.legit.after"
    $same = $false
    if ($b2.Code -eq $a2.Code) {
        $tb = Norm $(if (Test-Path $b2.Out) { Get-Content -Raw -LiteralPath $b2.Out } else { '' })
        $ta = Norm $(if (Test-Path $a2.Out) { Get-Content -Raw -LiteralPath $a2.Out } else { '' })
        $same = ($tb -eq $ta)
    }
    # Положительный контроль плеча 1: ДО мусорный ключ не менял НИЧЕГО —
    # ни кода, ни вывода. Это и есть «глотает молча», а не «мы так думаем».
    $tb1 = Norm $(if (Test-Path $b1.Out) { Get-Content -Raw -LiteralPath $b1.Out } else { '' })
    $tb2 = Norm $(if (Test-Path $b2.Out) { Get-Content -Raw -LiteralPath $b2.Out } else { '' })
    $swallowed = ($b1.Code -eq $b2.Code) -and ($tb1 -eq $tb2) -and (-not $b1s)
    $rows += [pscustomobject]@{
        Проба          = $n
        'ключ'         = $c.B
        'ДО глотал'    = if ($swallowed) { 'да' } else { 'НЕТ' }
        'ДО код'       = $b1.Code
        'ПОСЛЕ код'    = $a1.Code
        'ПОСЛЕ назвал' = if ($a1s) { 'да' } else { 'НЕТ' }
        'зак. ДО'      = $b2.Code
        'зак. ПОСЛЕ'   = $a2.Code
        'зак. вывод'   = if ($same) { 'тот же' } else { 'РАЗОШЁЛСЯ' }
        'с'            = ('{0:F1}' -f $a2.Sec)
    }
    $rows[-1] | Format-List | Out-String | Write-Host
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $repo 'handover\f72-a263\arms.csv')
