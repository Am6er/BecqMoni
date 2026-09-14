# П70 (AMBER30): приёмка плеча правки ОДНИМ движением от каталога проб wt_fix\tools\effmaker\probes\build_p70:
#   * wd_fix = каталог проб + config Amber (склад, приборы, библиотека) + живая матрица AS80_th_disk (копия);
#   * снимки Cs-137 в домике: плечо А (умолчание — хвост в подложке), плечо Б (--tail-as-residual = П68),
#     крупный план 0–150 кэВ обоих; эталон AS80_Th232Medal (--chain=Th-232) в обоих плечах;
#   * сверка rates: А против Б (всё, кроме share_pct — побитово), А против a6f2b227 и Б против 025a65a9 (стенд бисекции);
#   * пробы FsaDoubleCountProbe (AS80, Cs), FsaTieProbe, FsaQualityRowProbe.
#   pwsh -File D:\BqMoni_Claude\p70\fix_accept.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane = 'D:\BqMoni_Claude\p70'
$wt = "$lane\wt_fix"
$build = "$wt\tools\effmaker\probes\build_p70"
$wd = "$lane\wd_fix"
$out = "$lane\out\fix"
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$cs = "$lane\Cs 137 в домике 24.11.2022.xml"
$as80 = "$wt\tools\CORPUS\corpus\spectra\AS80_Th232Medal.xml"
New-Item -ItemType Directory -Force $out | Out-Null

# сторож свежести (A77): exe и пробы не старше исходников правки
$exe = Get-Item "$build\BecquerelMonitor.exe"
$src = Get-ChildItem "$wt\BecquerelMonitor\FullSpectrumAnalysis\*.cs" | Sort-Object LastWriteTime | Select-Object -Last 1
if ($exe.LastWriteTime -lt $src.LastWriteTime) { "⛔ exe ($($exe.LastWriteTime)) старше $($src.Name) ($($src.LastWriteTime)) — сперва fix_build.ps1"; exit 3 }
foreach ($probe in 'FsaStackShot', 'FsaDoubleCountProbe') {
    $pe = Get-Item "$build\$probe.exe"; $ps = Get-Item "$wt\tools\effmaker\probes\$probe.cs"
    if ($pe.LastWriteTime -lt $ps.LastWriteTime) { "⛔ $probe.exe старше $probe.cs — сперва fix_build.ps1"; exit 3 }
}
"плечо правки: exe $($exe.LastWriteTime.ToString('HH:mm:ss')) sha256 $((Get-FileHash $exe.FullName -Algorithm SHA256).Hash)"

# рабочий каталог
robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy build код $LASTEXITCODE"; exit 4 }
robocopy "$lane\amber_debug\config" "$wd\config" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy config код $LASTEXITCODE"; exit 4 }
Copy-Item "$repo\tools\CORPUS\corpus\geometries\response\c2b5212c-6b5a-50d8-7870-0b0c3104daa2.rmx" "$wd\config\device\response\" -Force
"wd: матриц $((Get-ChildItem "$wd\config\device\response\*.rmx").Count), приборов $((Get-ChildItem "$wd\config\device\*.xml").Count)"

$codes = @()
Push-Location $wd
$runs = @(
    @{ key = 'cs_a';      spectrum = $cs;   args = @('--infer', '--set=Cs-137 + K-40', '--lines=Xray-Pb') },
    @{ key = 'cs_b';      spectrum = $cs;   args = @('--infer', '--set=Cs-137 + K-40', '--lines=Xray-Pb', '--tail-as-residual') },
    @{ key = 'cs_a_zoom'; spectrum = $cs;   args = @('--infer', '--set=Cs-137 + K-40', '--from=0', '--to=150', '--ceiling=400000') },
    @{ key = 'cs_b_zoom'; spectrum = $cs;   args = @('--infer', '--set=Cs-137 + K-40', '--from=0', '--to=150', '--ceiling=400000', '--tail-as-residual') },
    @{ key = 'cs_b_plant'; spectrum = $cs;  args = @('--infer', '--set=Cs-137 + K-40', '--tail-as-residual', '--plant-tail') },
    @{ key = 'as80_a';    spectrum = $as80; args = @('--chain=Th-232') },
    @{ key = 'as80_b';    spectrum = $as80; args = @('--chain=Th-232', '--tail-as-residual') }
)
foreach ($r in $runs) {
    $k = $r.key
    $a = @("--spectrum=$($r.spectrum)") + $r.args + @("--out=$out\$k.png", "--rates=$out\rates_$k.csv", "--dump=$out\curves_$k.csv", '--screen', '--scale=pow', '--width=1600')
    & .\FsaStackShot.exe @a > "$out\$k.log" 2>&1
    $codes += "$k=$LASTEXITCODE"
    "--- $k (код $LASTEXITCODE)"
    Select-String -Path "$out\$k.log" -Pattern '^(chi2/ndf|ROW|отвязанный хвост|⛔ ПОДСАДКА)' | ForEach-Object { $_.Line }
    Select-String -Path "$out\$k.log" -Pattern 'UntiedTailAsResidual' | ForEach-Object { '  ' + $_.Line.Substring(0, [Math]::Min(160, $_.Line.Length)) }
}

& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80.log"; $codes += "dc_as80=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$cs" --sample=137CS *> "$out\dc_cs.log";       $codes += "dc_cs=$LASTEXITCODE"
& .\FsaTieProbe.exe *> "$out\tie.log";               $codes += "tie=$LASTEXITCODE"
& .\FsaQualityRowProbe.exe *> "$out\qrow.log";       $codes += "qrow=$LASTEXITCODE"
Pop-Location
foreach ($f in 'dc_as80', 'dc_cs', 'tie', 'qrow') {
    $tail = (Select-String -Path "$out\$f.log" -Pattern 'ВСЕ СОШЛИСЬ|РАСХОЖДЕНИ|!!|AMBER30|TAIL' | Select-Object -Last 4).Line
    "{0,-10} {1}" -f $f, ($tail -join ' | ')
}
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')" }
exit 0
