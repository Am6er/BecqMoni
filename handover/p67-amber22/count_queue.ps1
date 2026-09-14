$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p='D:\BqMoni_Claude\p67'; $b="$p\wt\tools\effmaker\probes\build_p67"
$keys = @('AS80_p67_edge93_r33','AS80_p67_contact_r33','AS80_p67_face81_r33',
          'AS80_p67_edge93_r28','AS80_p67_edge93_r42','AS80_p67_contact_r28','AS80_p67_contact_r42','AS80_p67_face81_r28','AS80_p67_face81_r42',
          'AS80_p67_edge93_r38','AS80_p67_contact_r38','AS80_p67_face81_r38',
          'AS80_p67_edge93_p13','AS80_p67_contact_p13',
          'AS80_p67_edge93_r33_ring05','AS80_p67_edge93_r33_ring20',
          'AS80_p67_face81_p13')
Push-Location $b
"queue start $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
foreach ($k in $keys) {
  $t0 = Get-Date
  & .\CorpusMatrixProbe.exe --dir=$p\store --only=$k --threads=10 --target=0 > "$p\count_$k.log" 2>&1
  "$k code=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
}
Pop-Location
"queue done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\count_queue.log"
