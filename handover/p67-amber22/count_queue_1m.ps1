# П67: очередь матриц с 1 000 000 историй на узел (машина делится с П66: 3 М — ~20+ мин на сцену, 17 сцен не уложить);
# порядок — по важности; первая сцена edge93_r33 посчитана 3 М (count_queue.log) и повторяется 1 М в store_1m — контроль
# «1 М против 3 М» на одной сцене.
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p='D:\BqMoni_Claude\p67'; $b="$p\wt\tools\effmaker\probes\build_p67"
$keys = @('AS80_p67_contact_r33','AS80_p67_face81_r33',
          'AS80_p67_edge93_r28','AS80_p67_edge93_r42','AS80_p67_contact_r28','AS80_p67_contact_r42','AS80_p67_face81_r28','AS80_p67_face81_r42',
          'AS80_p67_edge93_r38','AS80_p67_contact_r38','AS80_p67_face81_r38',
          'AS80_p67_edge93_p13','AS80_p67_contact_p13',
          'AS80_p67_edge93_r33_ring05','AS80_p67_edge93_r33_ring20',
          'AS80_p67_face81_p13')
Push-Location $b
"queue1m start $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
foreach ($k in $keys) {
  $t0 = Get-Date
  & .\CorpusMatrixProbe.exe --dir=$p\store --only=$k --n=1000000 --threads=10 --target=0 > "$p\count_$k.log" 2>&1
  "$k n=1M code=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
}
# контроль 1 М против 3 М на первой сцене — отдельный склад
New-Item -ItemType Directory -Force "$p\store_1m" | Out-Null
Copy-Item "$p\store\AS80_p67_edge93_r33.in" "$p\store_1m\"; Copy-Item "$p\store\index.csv" "$p\store_1m\"
$t0 = Get-Date
& .\CorpusMatrixProbe.exe --dir=$p\store_1m --only=AS80_p67_edge93_r33 --n=1000000 --threads=10 --target=0 > "$p\count_AS80_p67_edge93_r33_1m.log" 2>&1
"AS80_p67_edge93_r33 (store_1m) n=1M code=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
Pop-Location
"queue1m done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
