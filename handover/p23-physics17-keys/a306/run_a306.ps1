# П23 12.09.2026, приёмка `A306` (ключ PeakChannelByTolerance, `--pkch=1` у ResponseRowDumpProbe).
# Рецепт П20 §2: прямое плечо из геометрии (умолчания ResponseMatrixOptions, полубин 1.0 кэВ),
# сцена-копия ASN16_point0 (CsI 15×18×60, точка вплотную — выдача рецензенту 11.09.2026),
# узлы 32.194 и 661.657 кэВ.
#   off  — --peakw=0 без ключа: ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, обязан воспроизвести П20
#          (compton в бине пика 6.352E-03 / 1.4724E-01 = 4.14 %; на 662 — 0.03 %);
#   pkch — --peakw=0 --pkch=1: доля compton в бине пика → 0, Σ канала peak = вес бина пика,
#          сумма строки и вес бина пика — те же до бита (ключ не трогает розыгрыша).
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p23"
$geo = "$root\handover\p23-physics17-keys\scenes\ASN16_point0.in"
$out = "$root\handover\p23-physics17-keys\a306"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Push-Location $bin
& "$bin\ResponseRowDumpProbe.exe" "--geometry=$geo" --direct --peakw=0 --e=32.194,661.657 "--out=$out\off" > "$out\off.txt" 2>&1
"off code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
& "$bin\ResponseRowDumpProbe.exe" "--geometry=$geo" --direct --peakw=0 --pkch=1 --e=32.194,661.657 "--out=$out\pkch" > "$out\pkch.txt" 2>&1
"pkch code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
