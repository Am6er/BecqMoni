# store30 (AMBER274, физика 30): раздел электрона пробы к КАЖДОЙ фотонной матрице склада на GPU.
# Вход — photon\<сцена>.in + photon\<сцена>.rmx; выход — store\<сцена>.rmx (формат 12, раздел esec=4 со строками ветвей).
# Рецепт (журналы Э1, phys29c, f8cyl): BetaSectionProbeA274 --gpu=<rmgpu_f.dll> --n=200000, сетка по умолчанию 80 узлов
# 60…3600 кэВ, позитрон ВКЛ, строки линий lib:1 и ветвей lib:1 — умолчания пробы, зерно 0, капсула — из сцены.
# Проба пишет во временный каталог; в store\ файл переносится только при коде 0 (обрыв не оставит полфайла).
# Уже лежащий store\<сцена>.rmx — пропуск (пара A77 посчитана заранее тем же рецептом).
$ErrorActionPreference = 'Continue'
$S   = 'D:\BqMoni_Claude\store30'
$PR  = "$S\wt\tools\effmaker\probes\build_s30"
$DLL = "$S\wt\tools\effmaker\gpu\bin\rmgpu_f.dll"
$tmp = "$S\sect_tmp"
$logs = "$S\logs\sections"
New-Item -ItemType Directory -Force $tmp, $logs | Out-Null
$csv = "$S\logs\sections_times.csv"
if (-not (Test-Path $csv)) { 'scene,exit,seconds,start,end' | Out-File $csv -Encoding utf8 }
$keys = [string[]](Get-ChildItem "$S\photon\*.in" | ForEach-Object { $_.BaseName })
[Array]::Sort($keys, [StringComparer]::Ordinal)
$bad = 0; $done = 0; $skip = 0
foreach ($k in $keys) {
    $out = "$S\store\$k.rmx"
    if (Test-Path $out) { "$k : уже в store — пропуск"; $skip++; continue }
    if (-not (Test-Path "$S\photon\$k.rmx")) { "$k : ⛔ НЕТ фотонной матрицы photon\$k.rmx"; $bad++; continue }
    $part = "$tmp\$k.rmx"
    Remove-Item $part -ErrorAction SilentlyContinue
    $t0 = Get-Date
    & "$PR\BetaSectionProbeA274.exe" "--scene=$S\photon\$k.in" "--matrix=$S\photon\$k.rmx" "--out=$part" "--gpu=$DLL" '--n=200000' > "$logs\$k.log" 2> "$logs\$k.err"
    $code = $LASTEXITCODE
    $t1 = Get-Date
    $sec = [int]($t1 - $t0).TotalSeconds
    "{0},{1},{2},{3:HH:mm:ss},{4:HH:mm:ss}" -f $k, $code, $sec, $t0, $t1 | Out-File $csv -Append -Encoding utf8
    if ($code -eq 0 -and (Test-Path $part)) { Move-Item $part $out; $done++; "$k : код 0, $sec с" }
    else { $bad++; "$k : ⛔ код $code, $sec с — в store не положена" }
}
"разделов: посчитано $done, пропущено $skip, отказов $bad"
exit ($(if ($bad -eq 0) { 0 } else { 1 }))
