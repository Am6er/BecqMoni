# П20 12.09.2026, замер `A306` при допуске ПОЛУБИНОМ (`--peakb=1`, умолчание склада).
# Проба — ResponseRowDumpProbe из worktree C:\Users\moroz\bqp20 (HEAD 98cb5532), Release_P20,
# плечо из геометрии (--geometry=, умолчания ResponseMatrixOptions, 3 млн историй на узел).
# Сцена — копия scenes\ASN16_point0.in: CsI 15×18×60, ПТФЭ 1.3/1.0, Al 1.8/2.0, крепление 2,
# точка вплотную к торцу корпуса, ПШПВ(662) 6.66 % — сцена выдачи рецензенту 11.09.2026.
# Плечи:
#   both      — direct_peakw1 (допуск ПШПВ/2 = 4.86 кэВ на 32 кэВ): ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ,
#               обязан воспроизвести 4.93 % бина (11.09); direct_peakw0 (полубин 1.0 кэВ) — ЗАМЕР;
#   zero      — direct_peakw0_peakb0 (нулевой допуск): 0.15 % бина (11.09) — второй контроль;
#   noscat    — direct_no_scat при полубине: абляция однократного рассеяния до кристалла.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = 'C:\Users\moroz\bqp20\tools\effmaker\probes\build_p20'
$geo = "$root\handover\p20-response-measures\scenes\ASN16_point0.in"
$out = "$root\handover\p20-response-measures\a306"
Push-Location $bin
& "$bin\ResponseRowDumpProbe.exe" "--geometry=$geo" --direct --peakw=both --e=32.194,661.657 "--out=$out\both" *> "$out\both.txt"
"both code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
& "$bin\ResponseRowDumpProbe.exe" "--geometry=$geo" --direct --peakw=0 --peakb=0 --e=32.194,661.657 "--out=$out\zero" *> "$out\zero.txt"
"zero code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
& "$bin\ResponseRowDumpProbe.exe" "--geometry=$geo" --direct --peakw=0 --ablate=scat --e=32.194,661.657 "--out=$out\noscat" *> "$out\noscat.txt"
"noscat code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
