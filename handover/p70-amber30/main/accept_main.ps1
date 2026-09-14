# П70 (AMBER30): приёмка правки в ОСНОВНОМ дереве ОДНИМ движением от каталога проб build_p70:
#   * wd_main  = каталог проб + config Amber (склад, приборы, библиотека) + копия AS80_th_disk.rmx —
#                спектр Amber «Cs 137 в домике» (--infer, набор Cs-137 + K-40) и эталон AS80 (Amber-прибор);
#   * wd_radon = каталог проб + корпусные приборы ASN16/AS80 (HEAD) + матрица ASN16_rn_side полосы + AS80_th_disk;
#                NuclideDefinition.xml снят — фильтр ASN16 спектр 2 (стенд П69: --chain=Ra-226,Th-232,
#                free = --no-equilibrium, eq) и эталон AS80 (корпусный прибор, как у П69: 14.115);
#   * плечи: А — умолчание (хвост в подложке → серый слой S174), Б — --tail-as-residual (= П68);
#   * пробы FsaDoubleCountProbe (AS80, Cs, radon2), FsaTieProbe, FsaQualityRowProbe; сверка rates.
#   pwsh -File D:\BqMoni_Claude\p70\accept_main.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane = 'D:\BqMoni_Claude\p70'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = "$repo\tools\effmaker\probes\build_p70"
$out = "$lane\out\main"
$cs = "$lane\Cs 137 в домике 24.11.2022.xml"
$as80 = "$repo\tools\CORPUS\corpus\spectra\AS80_Th232Medal.xml"
$radon2 = "$lane\radon\spectra\radon2_side.xml"
$as80rmx = "$repo\tools\CORPUS\corpus\geometries\response\c2b5212c-6b5a-50d8-7870-0b0c3104daa2.rmx"
New-Item -ItemType Directory -Force $out | Out-Null

# сторож свежести (A77)
$exe = Get-Item "$build\BecquerelMonitor.exe"
$src = Get-ChildItem "$repo\BecquerelMonitor\FullSpectrumAnalysis\*.cs", "$repo\BecquerelMonitor\EnergySpectrumView.Fsa.cs" | Sort-Object LastWriteTime | Select-Object -Last 1
if ($exe.LastWriteTime -lt $src.LastWriteTime) { "⛔ exe ($($exe.LastWriteTime)) старше $($src.Name) ($($src.LastWriteTime)) — сперва main_build.ps1"; exit 3 }
foreach ($probe in 'FsaStackShot', 'FsaDoubleCountProbe') {
    $pe = Get-Item "$build\$probe.exe"; $ps = Get-Item "$repo\tools\effmaker\probes\$probe.cs"
    if ($pe.LastWriteTime -lt $ps.LastWriteTime) { "⛔ $probe.exe старше $probe.cs — сперва main_build.ps1"; exit 3 }
}
"правка в основном дереве: exe $($exe.LastWriteTime.ToString('HH:mm:ss')) sha256 $((Get-FileHash $exe.FullName -Algorithm SHA256).Hash)"

# wd_main — config Amber
$wd = "$lane\wd_main"
robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy build код $LASTEXITCODE"; exit 4 }
robocopy "$lane\amber_debug\config" "$wd\config" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy config код $LASTEXITCODE"; exit 4 }
Copy-Item $as80rmx "$wd\config\device\response\" -Force
"wd_main: матриц $((Get-ChildItem "$wd\config\device\response\*.rmx").Count), приборов $((Get-ChildItem "$wd\config\device\*.xml").Count)"

# wd_radon — корпусные приборы, склад полосы
$wr = "$lane\wd_radon"
robocopy $build $wr /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy build (radon) код $LASTEXITCODE"; exit 4 }
$dev = "$wr\config\device"
Get-ChildItem "$dev\*.xml" -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item "$repo\tools\CORPUS\corpus\devices\1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml" $dev
Copy-Item "$repo\tools\CORPUS\corpus\devices\Atom Spectra 80x80.xml" $dev
if (Test-Path "$wr\config\NuclideDefinition.xml") { Remove-Item "$wr\config\NuclideDefinition.xml" -Force }
New-Item -ItemType Directory -Force "$dev\response" | Out-Null
Get-ChildItem "$dev\response\*.rmx" -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item "$lane\radon\store\response\*.rmx" "$dev\response\"
Copy-Item $as80rmx "$dev\response\" -Force
"wd_radon: матриц $((Get-ChildItem "$dev\response\*.rmx").Count), приборов $((Get-ChildItem "$dev\*.xml").Count)"

$codes = @()
function Shot([string]$wdir, [string]$key, [string]$spectrum, [string[]]$extra) {
    Push-Location $wdir
    $a = @("--spectrum=$spectrum") + $extra + @("--out=$out\$key.png", "--rates=$out\rates_$key.csv", "--dump=$out\curves_$key.csv", '--screen', '--scale=pow', '--width=1600')
    & .\FsaStackShot.exe @a > "$out\$key.log" 2>&1
    $c = $LASTEXITCODE
    Pop-Location
    $script:codes += "$key=$c"
    "--- $key (код $c)"
    Select-String -Path "$out\$key.log" -Pattern '^(chi2/ndf|ROW|отвязанный хвост|серый слой|пороги отображения|невязка)' | ForEach-Object { $_.Line }
    Select-String -Path "$out\$key.log" -Pattern 'UntiedTailAsResidual' | ForEach-Object { '  ' + $_.Line.Substring(0, [Math]::Min(120, $_.Line.Length)) }
}
$csArgs = @('--infer', '--set=Cs-137 + K-40', '--lines=Xray-Pb')
Shot $wd 'cs_a'      $cs   $csArgs
Shot $wd 'cs_b'      $cs   ($csArgs + '--tail-as-residual')
Shot $wd 'cs_a_zoom' $cs   ($csArgs + @('--from=0', '--to=150', '--ceiling=400000'))
Shot $wd 'cs_b_zoom' $cs   ($csArgs + @('--from=0', '--to=150', '--ceiling=400000', '--tail-as-residual'))
Shot $wd 'as80amb_a' $as80 @('--chain=Th-232')
Shot $wd 'as80amb_b' $as80 @('--chain=Th-232', '--tail-as-residual')
Shot $wr 'radon2_free_a' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium')
Shot $wr 'radon2_free_b' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--tail-as-residual')
Shot $wr 'radon2_eq_a'   $radon2 @('--chain=Ra-226,Th-232')
Shot $wr 'radon2_eq_b'   $radon2 @('--chain=Ra-226,Th-232', '--tail-as-residual')
Shot $wr 'radon2_free_a_zoom' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--from=0', '--to=300')
Shot $wr 'as80_a' $as80 @('--chain=Th-232')
Shot $wr 'as80_b' $as80 @('--chain=Th-232', '--tail-as-residual')

Push-Location $wd
& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80amb.log"; $codes += "dc_as80amb=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$cs" --sample=137CS *> "$out\dc_cs.log";        $codes += "dc_cs=$LASTEXITCODE"
& .\FsaTieProbe.exe *> "$out\tie.log";               $codes += "tie=$LASTEXITCODE"
& .\FsaQualityRowProbe.exe *> "$out\qrow.log";       $codes += "qrow=$LASTEXITCODE"
Pop-Location
Push-Location $wr
& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80.log";              $codes += "dc_as80=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$radon2" --chain=Ra-226,Th-232 *> "$out\dc_radon2.log";   $codes += "dc_radon2=$LASTEXITCODE"
Pop-Location
foreach ($f in 'dc_as80amb', 'dc_cs', 'dc_as80', 'dc_radon2', 'tie', 'qrow') {
    $tail = (Select-String -Path "$out\$f.log" -Pattern 'ВСЕ СОШЛИСЬ|НЕ СОШЛОСЬ|!!' | Select-Object -Last 3).Line
    "{0,-12} {1}" -f $f, (($tail | ForEach-Object { $_.Substring(0, [Math]::Min(110, $_.Length)) }) -join ' | ')
}
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')" }
exit 0
