# src_hash.ps1 — отпечаток исходников ядра GPU (`AMBER219`, П245): sha256 над байтами файлов
# `*.cu`, `*.cuh`, `*.h`, `*.inc` этого каталога, отсортированных по имени ПОРЯДКОВО (ordinal,
# в нижнем регистре — не по культуре: та ставит `_` и `.` по-своему) и склеенных подряд.
# Печатает 16 первых знаков в нижнем регистре — ровно то, что `build_gpu.cmd` вшивает в
# `rm_build_info()` ключом `src=`, а `tools\check_gpu_dll.py` пересчитывает тем же правилом.
# Поменяется правило здесь — поменять и там (у сторожа есть `--selftest`).
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$names = @(Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Extension -in '.cu', '.cuh', '.h', '.inc' } |
           ForEach-Object { $_.Name })
[Array]::Sort($names, [System.StringComparer]::OrdinalIgnoreCase)
$sha = [System.Security.Cryptography.SHA256]::Create()
$stream = New-Object System.IO.MemoryStream
foreach ($n in $names) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $dir $n))
    $stream.Write($bytes, 0, $bytes.Length)
}
$hash = $sha.ComputeHash($stream.ToArray())
$hex = -join ($hash | ForEach-Object { $_.ToString('x2') })
Write-Output $hex.Substring(0, 16)
