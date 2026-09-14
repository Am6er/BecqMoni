# П75 (S175): приёмка ОДНИМ движением. Плечи:
#   A — HEAD 08030c57 (worktree wt_a, каталог проб build_p75a): хвост в подложке → серый слой (П70);
#   B — основное дерево с правкой (build_p75): хвост в слое и доле своего образа (S175, умолчание);
#   G — то же B, но подсадки --tail-to-continuum --plant-grey-floor (положительный контроль: обязано = A до знака);
#   C — B с --tail-to-continuum (картинка a6f2b227: хвост в подложке, S76 везде); F — B с --plant-grey-floor (П75 до второго решения);
#   R — B с --tail-as-residual (яма AMBER30, контроль П70 не ломать).
# Рабочие каталоги: wd_main_a/wd_radon_a (плечо A), wd_main/wd_radon (B, G, R) — mk_wd.ps1.
# Спектры: Cs 137 в домике (--infer, набор Cs-137 + K-40, config Amber), эталон AS80_Th232Medal (прибор Amber
# и корпусный), фильтр ASN16 спектр 2 (free/eq). Пробы: FsaDoubleCountProbe (AS80 ×2, Cs, radon2),
# FsaTieProbe, FsaQualityRowProbe, FsaChannelSplitProbe (AS80 eq).
#   pwsh -File D:\BqMoni_Claude\p75\accept.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane = 'D:\BqMoni_Claude\p75'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$buildB = "$repo\tools\effmaker\probes\build_p75"
$buildA = "$lane\wt_a\tools\effmaker\probes\build_p75a"
$out = "$lane\out\accept"
$cs = "$lane\spectra\Cs 137 в домике 24.11.2022.xml"
$as80 = "$repo\tools\CORPUS\corpus\spectra\AS80_Th232Medal.xml"
$radon2 = "$lane\radon\spectra\radon2_side.xml"
New-Item -ItemType Directory -Force $out | Out-Null

# сторож свежести (A77): exe и пробы плеча B не старше исходников
$exe = Get-Item "$buildB\BecquerelMonitor.exe"
$src = Get-ChildItem "$repo\BecquerelMonitor\FullSpectrumAnalysis\*.cs", "$repo\BecquerelMonitor\EnergySpectrumView.Fsa.cs" | Sort-Object LastWriteTime | Select-Object -Last 1
if ($exe.LastWriteTime -lt $src.LastWriteTime) { "⛔ exe ($($exe.LastWriteTime)) старше $($src.Name) ($($src.LastWriteTime)) — сперва main_build.ps1"; exit 3 }
foreach ($probe in 'FsaStackShot', 'FsaDoubleCountProbe', 'FsaChannelSplitProbe') {
    $pe = Get-Item "$buildB\$probe.exe"; $ps = Get-Item "$repo\tools\effmaker\probes\$probe.cs"
    if ($pe.LastWriteTime -lt $ps.LastWriteTime) { "⛔ $probe.exe старше $probe.cs — сперва main_build.ps1"; exit 3 }
}
"плечо B (правка, основное дерево): exe $($exe.LastWriteTime.ToString('HH:mm:ss')) sha256 $((Get-FileHash $exe.FullName -Algorithm SHA256).Hash)"
"плечо A (HEAD 08030c57, wt_a): exe sha256 $((Get-FileHash "$buildA\BecquerelMonitor.exe" -Algorithm SHA256).Hash)"

& pwsh -NoProfile -File "$lane\mk_wd.ps1" -Build $buildB -Suffix ''
if ($LASTEXITCODE -ne 0) { "mk_wd B код $LASTEXITCODE"; exit 4 }
& pwsh -NoProfile -File "$lane\mk_wd.ps1" -Build $buildA -Suffix '_a'
if ($LASTEXITCODE -ne 0) { "mk_wd A код $LASTEXITCODE"; exit 4 }

$codes = @()
function Shot([string]$wdir, [string]$key, [string]$spectrum, [string[]]$extra) {
    Push-Location $wdir
    $a = @("--spectrum=$spectrum") + $extra + @("--out=$out\$key.png", "--rates=$out\rates_$key.csv", "--dump=$out\curves_$key.csv", '--screen', '--scale=pow', '--width=1600')
    & .\FsaStackShot.exe @a > "$out\$key.log" 2>&1
    $c = $LASTEXITCODE
    Pop-Location
    $script:codes += "$key=$c"
    "--- $key (код $c)"
    Select-String -Path "$out\$key.log" -Pattern '^(chi2/ndf|ROW|отвязанный хвост|хвост в слоях|серый слой|невязка \(S174\) от|⛔ ПОДСАДКА|разнос сплайна)' | ForEach-Object { $_.Line.Substring(0, [Math]::Min(170, $_.Line.Length)) }
}
$wdA = "$lane\wd_main_a"; $wrA = "$lane\wd_radon_a"
$wdB = "$lane\wd_main";   $wrB = "$lane\wd_radon"
$csArgs = @('--infer', '--set=Cs-137 + K-40', '--lines=Xray-Pb')
$zoom = @('--from=0', '--to=150', '--ceiling=400000')
Shot $wdA 'cs_a'      $cs   $csArgs
Shot $wdB 'cs_b'      $cs   $csArgs
Shot $wdB 'cs_g'      $cs   ($csArgs + @('--tail-to-continuum', '--plant-grey-floor'))
Shot $wdB 'cs_c'      $cs   ($csArgs + '--tail-to-continuum')
Shot $wdB 'cs_f'      $cs   ($csArgs + '--plant-grey-floor')
Shot $wdB 'cs_r'      $cs   ($csArgs + '--tail-as-residual')
Shot $wdA 'cs_a_zoom' $cs   ($csArgs + $zoom)
Shot $wdB 'cs_b_zoom' $cs   ($csArgs + $zoom)
Shot $wdB 'cs_g_zoom' $cs   ($csArgs + $zoom + @('--tail-to-continuum', '--plant-grey-floor'))
Shot $wdB 'cs_f_zoom' $cs   ($csArgs + $zoom + '--plant-grey-floor')
Shot $wdB 'cs_r_zoom' $cs   ($csArgs + $zoom + '--tail-as-residual')
Shot $wdA 'as80amb_a' $as80 @('--chain=Th-232')
Shot $wdB 'as80amb_b' $as80 @('--chain=Th-232')
Shot $wrA 'radon2_free_a' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium')
Shot $wrB 'radon2_free_b' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium')
Shot $wrB 'radon2_free_g' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--tail-to-continuum', '--plant-grey-floor')
Shot $wrB 'radon2_free_c' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--tail-to-continuum')
Shot $wrB 'radon2_free_r' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--tail-as-residual')
Shot $wrA 'radon2_eq_a'   $radon2 @('--chain=Ra-226,Th-232')
Shot $wrB 'radon2_eq_b'   $radon2 @('--chain=Ra-226,Th-232')
Shot $wrB 'radon2_eq_g'   $radon2 @('--chain=Ra-226,Th-232', '--tail-to-continuum', '--plant-grey-floor')
Shot $wrB 'radon2_eq_c'   $radon2 @('--chain=Ra-226,Th-232', '--tail-to-continuum')
Shot $wrA 'radon2_free_a_zoom' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--from=0', '--to=300')
Shot $wrB 'radon2_free_b_zoom' $radon2 @('--chain=Ra-226,Th-232', '--no-equilibrium', '--from=0', '--to=300')
Shot $wrA 'as80_a' $as80 @('--chain=Th-232')
Shot $wrB 'as80_b' $as80 @('--chain=Th-232')

Push-Location $wdB
& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80amb.log"; $codes += "dc_as80amb=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$cs" --sample=137CS *> "$out\dc_cs.log";        $codes += "dc_cs=$LASTEXITCODE"
& .\FsaTieProbe.exe *> "$out\tie.log";               $codes += "tie=$LASTEXITCODE"
& .\FsaQualityRowProbe.exe *> "$out\qrow.log";       $codes += "qrow=$LASTEXITCODE"
Pop-Location
Push-Location $wrB
& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80.log";              $codes += "dc_as80=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$radon2" --chain=Ra-226,Th-232 *> "$out\dc_radon2.log";   $codes += "dc_radon2=$LASTEXITCODE"
& .\FsaChannelSplitProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\split_as80.log";          $codes += "split_as80=$LASTEXITCODE"
& .\FsaChannelSplitProbe.exe "--spectrum=$radon2" --chain=Ra-226,Th-232 *> "$out\split_radon2.log"; $codes += "split_radon2=$LASTEXITCODE"
Pop-Location
foreach ($f in 'dc_as80amb', 'dc_cs', 'dc_as80', 'dc_radon2', 'tie', 'qrow', 'split_as80', 'split_radon2') {
    $tail = @((Select-String -Path "$out\$f.log" -Pattern 'ВСЕ СОШЛИСЬ|НЕ СОШЛОСЬ|всё сошлось|!!' | Select-Object -Last 3).Line)
    "{0,-12} {1}" -f $f, (($tail | Where-Object { $_ } | ForEach-Object { $_.Substring(0, [Math]::Min(110, $_.Length)) }) -join ' | ')
}
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')" }
exit 0
