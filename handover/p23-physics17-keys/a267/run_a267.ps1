# П23 12.09.2026, приёмка `A267` (ключ LightBinUnified, `--binof` у LightAnchorProbe).
# Рецепт П20 §1: --bin=2 --peakb --store --nodes=10-70, 400 тыс. историй, сцены-копии.
# Плечи на каждой сцене:
#   off   — без ключа: ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, обязан воспроизвести П20 побитово
#           (AS80 +2.703 % на 32.993 кэВ, ASN16 +2.542 %);
#   binof — с ключом: сдвиг 0 по построению; столбец «свет/E» обязан совпасть со
#           столбцом «единое» плеча off (тот же поток) — это и есть «пила ушла».
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p23"
$out = "$root\handover\p23-physics17-keys\a267"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Push-Location $bin
foreach ($scene in 'AS80_point0', 'ASN16_lu_side') {
    $geo = "$root\handover\p23-physics17-keys\scenes\$scene.in"
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" --bin=2 --peakb --store --nodes=10-70 > "$out\${scene}_off.txt" 2>&1
    "$scene off code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" --bin=2 --peakb --store --nodes=10-70 --binof > "$out\${scene}_binof.txt" 2>&1
    "$scene binof code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
