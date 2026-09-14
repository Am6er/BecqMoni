# П69 (S174): приёмка плеча Б (основное дерево с правкой) ОДНИМ движением от финальной сборки —
# рабочий каталог, развёртка (free/eq/plant/zoom/from100 × as80/radon1/radon2), пробы.
#
#   pwsh -File D:\BqMoni_Claude\p69\accept_b.ps1
#
# Порядок обязателен (A77): сперва сборка (build_b.ps1), потом ЭТОТ скрипт — он сверяет, что exe в
# каталоге проб не старше исходников анализатора и проб полосы, и кладёт его хеш в лог.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$r = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$d = 'D:\BqMoni_Claude\p69'
$h = Join-Path $r 'handover\p69-s174'
New-Item -ItemType Directory -Force "$h\probes", "$h\logs", "$h\rates", "$h\png" | Out-Null
$exe = Get-Item (Join-Path $r 'tools\effmaker\probes\build_p69\BecquerelMonitor.exe')
$src = Get-ChildItem (Join-Path $r 'BecquerelMonitor\FullSpectrumAnalysis\*.cs'), (Join-Path $r 'BecquerelMonitor\FSAReportView.cs'), (Join-Path $r 'BecquerelMonitor\Properties\Resources.resx'), (Join-Path $r 'BecquerelMonitor\FSAReportView.resx') | Sort-Object LastWriteTime | Select-Object -Last 1
if ($exe.LastWriteTime -lt $src.LastWriteTime) { "⛔ exe ($($exe.LastWriteTime)) старше $($src.Name) ($($src.LastWriteTime)) — сперва build_b.ps1"; exit 3 }
foreach ($probe in 'FsaStackShot', 'FsaDoubleCountProbe', 'CorpusFsaProbe') {
    $pe = Get-Item (Join-Path $r "tools\effmaker\probes\build_p69\$probe.exe")
    $ps = Get-Item (Join-Path $r "tools\effmaker\probes\$probe.cs")
    if ($pe.LastWriteTime -lt $ps.LastWriteTime) { "⛔ $probe.exe ($($pe.LastWriteTime)) старше $probe.cs ($($ps.LastWriteTime)) — сперва build_all"; exit 3 }
}
$sha = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.Substring(0, 16)
"плечо Б: exe $($exe.LastWriteTime) sha256 $sha"
$codes = @()

& pwsh -NoProfile -File "$d\mk_wd.ps1" -Arm b | Select-Object -Last 1;            $codes += "mk_wd_b=$LASTEXITCODE"
& pwsh -NoProfile -File "$d\sweep.ps1" -Arm b *> "$d\logs\sweep_b.log";           $codes += "sweep_b=$LASTEXITCODE"
Get-Content "$d\logs\sweep_b.log" -Encoding UTF8 | ForEach-Object { if ($_.Length -gt 200) { $_.Substring(0, 200) } else { $_ } }

# Пробы — из wd_b (матрицы через ResponseMatrixStore от каталога exe). Эталон корпуса — из чистого worktree.
$corpus = 'D:\BqMoni_Claude\p69\wt\tools\CORPUS\corpus\spectra'
Set-Location "$d\wd_b"
& .\FsaDoubleCountProbe.exe "--spectrum=$corpus\AS80_Th232Medal.xml" --chain=Th-232 *> "$h\probes\fsadoublecountprobe_as80.log";   $codes += "dc_as80=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$d\spectra\radon1_side.xml" --chain=Ra-226,Th-232 *> "$h\probes\fsadoublecountprobe_radon1.log"; $codes += "dc_radon1=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$d\spectra\radon2_side.xml" --chain=Ra-226,Th-232 *> "$h\probes\fsadoublecountprobe_radon2.log"; $codes += "dc_radon2=$LASTEXITCODE"
& .\FsaTieProbe.exe *> "$h\probes\fsatieprobe.log";               $codes += "tie=$LASTEXITCODE"
& .\FsaQualityRowProbe.exe *> "$h\probes\fsaqualityrowprobe.log"; $codes += "qrow=$LASTEXITCODE"
foreach ($f in Get-ChildItem "$h\probes\*.log") {
    $tail = (Select-String -Path $f.FullName -Pattern 'ВСЕ СОШЛИСЬ|НЕ СОШЛОСЬ|РАСХОЖДЕНИ|!!' | Select-Object -Last 1).Line
    "{0,-40} {1}" -f $f.Name, $tail
}

"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')" }
exit 0
