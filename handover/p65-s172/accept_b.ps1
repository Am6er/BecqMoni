# П65 (S172): приёмка плеча Б ОДНИМ движением от финальной сборки — рабочие каталоги, развёртка,
# крупные снимки и дампы низа шкалы, пробы, оснастка корпуса и малая база.
#
#   pwsh -File D:\BqMoni_Claude\p65\accept_b.ps1        # копия этого файла (путь без пробелов)
#
# Порядок обязателен (A77): сперва сборка (build_b.ps1), потом ЭТОТ скрипт — он сверяет, что exe в
# каталоге проб не старше исходников анализатора, и кладёт его хеш в каждый лог.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$r = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$d = 'D:\BqMoni_Claude\p65'
$h = Join-Path $r 'handover\p65-s172'
$exe = Get-Item (Join-Path $r 'tools\effmaker\probes\build_p65\BecquerelMonitor.exe')
$src = Get-ChildItem (Join-Path $r 'BecquerelMonitor\FullSpectrumAnalysis\*.cs') | Sort-Object LastWriteTime | Select-Object -Last 1
if ($exe.LastWriteTime -lt $src.LastWriteTime) { "⛔ exe ($($exe.LastWriteTime)) старше $($src.Name) ($($src.LastWriteTime)) — сперва build_b.ps1"; exit 3 }
$sha = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.Substring(0, 16)
"плечо Б: exe $($exe.LastWriteTime) sha256 $sha"
$codes = @()

& pwsh -NoProfile -File "$d\mk_wd.ps1" -Arm b | Select-Object -Last 1;            $codes += "mk_wd_b=$LASTEXITCODE"
& pwsh -NoProfile -File "$d\mk_wd.ps1" -Arm b -NoMatrix | Select-Object -Last 1;  $codes += "mk_wd_b_nomx=$LASTEXITCODE"
& pwsh -NoProfile -File "$d\sweep.ps1" -Arm b *> "$d\logs\sweep_b.log";           $codes += "sweep_b=$LASTEXITCODE"
Get-Content "$d\logs\sweep_b.log" -Encoding UTF8

Set-Location "$d\wd_b"
& .\FsaStackShot.exe --spectrum=$d\spectra\radon2_side.xml --chain=Ra-226,Th-232 --no-equilibrium --from=0 --to=300 "--out=$d\out_b\radon2_free_zoom.png" --screen --scale=pow --width=1400 > "$d\out_b\radon2_free_zoom.log" 2>&1; $codes += "zoom=$LASTEXITCODE"
foreach ($s in 'radon1', 'radon2') {
    & .\FsaStackShot.exe "--spectrum=$d\spectra\${s}_side.xml" --chain=Ra-226,Th-232 --no-equilibrium "--out=$d\out_b\${s}_free_dump.png" "--dump=$d\out_b\${s}_free_curves.csv" --scale=pow --width=1400 > "$d\out_b\${s}_free_dump.log" 2>&1; $codes += "dump_$s=$LASTEXITCODE"
}

# Пробы — из wd_b (матрицы через ResponseMatrixStore от каталога exe).
$corpus = Join-Path $r 'tools\CORPUS\corpus\spectra'
& .\FsaDoubleCountProbe.exe "--spectrum=$corpus\AS80_Th232Medal.xml" --chain=Th-232 *> "$h\probes\fsadoublecountprobe_as80.log";   $codes += "dc_as80=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$d\spectra\radon1_side.xml" --chain=Ra-226,Th-232 *> "$h\probes\fsadoublecountprobe_radon1.log"; $codes += "dc_radon1=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$d\spectra\radon2_side.xml" --chain=Ra-226,Th-232 *> "$h\probes\fsadoublecountprobe_radon2.log"; $codes += "dc_radon2=$LASTEXITCODE"
& .\CrystalXrayGateProbe.exe "--spectrum=$corpus\AS80_Th232Medal.xml" --chain=Th-232 *> "$h\probes\crystalxraygateprobe_as80.log";   $codes += "cx_as80=$LASTEXITCODE"
& .\CrystalXrayGateProbe.exe "--spectrum=$d\spectra\radon2_side.xml" --chain=Ra-226,Th-232 *> "$h\probes\crystalxraygateprobe_radon2.log"; $codes += "cx_radon2=$LASTEXITCODE"
& .\FsaTieProbe.exe *> "$h\probes\fsatieprobe.log";               $codes += "tie=$LASTEXITCODE"
& .\FsaQualityRowProbe.exe *> "$h\probes\fsaqualityrowprobe.log"; $codes += "qrow=$LASTEXITCODE"

# Оснастка корпуса и малая база — оператором вызова & из этой сессии (T84).
Set-Location $r
& "$r\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$r\BecquerelMonitor\bin\Release_p65" -Wd "$r\tools\CORPUS\scripts\wd_p65b" -ProbeBuild "$r\tools\effmaker\probes\build_p65" *> "$d\logs\mk_appwd_b.log"; $codes += "mk_appwd_b=$LASTEXITCODE"
& "$r\tools\CORPUS\scripts\run_mini.ps1" -Out "$d\out_mini_p65b" -Wd "$r\tools\CORPUS\scripts\wd_p65b" -Bin "$r\BecquerelMonitor\bin\Release_p65" -ProbeBuild "$r\tools\effmaker\probes\build_p65" *> "$d\logs\run_mini_b.log"; $codes += "run_mini_b=$LASTEXITCODE"
Get-Content "$d\logs\run_mini_b.log" -Encoding UTF8 | Select-String 'ОСНАСТКА СВЕЖАЯ|ПРОГОН|итого|sum chi2'

"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')" }
exit 0
