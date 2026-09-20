# П88 (AMBER22): матрицы сцен с Al-оправой тем же рецептом, что стенд П67 — 1 000 000 историй на узел, физика 18 (умолчания
# сборки 8b164a98, формат 8), --threads=10 --target=0. Порядок: сперва edge93_r33_al (главная), затем остальные.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p88\count_al.ps1 [ключ ...]   (без аргументов — все четыре)
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p88'; $b = "$p\wt\tools\effmaker\probes\build_p88"
$keys = if ($args.Count -gt 0) { $args } else { @('AS80_p88_edge93_r33_al', 'AS80_p88_edge93_r28_al', 'AS80_p88_edge93_r38_al', 'AS80_p88_contact_r33_al') }
New-Item -ItemType Directory -Force "$p\logs" | Out-Null
Push-Location $b
"count_al start $(Get-Date -Format 'HH:mm:ss') ключи: $($keys -join ' ')" | Out-File -Append "$p\logs\count_queue.log"
foreach ($k in $keys) {
  $t0 = Get-Date
  & .\CorpusMatrixProbe.exe --dir=$p\store --only=$k --n=1000000 --threads=10 --target=0 > "$p\logs\count_$k.log" 2>&1
  "$k n=1M code=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\logs\count_queue.log"
}
Pop-Location
"count_al done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\logs\count_queue.log"
