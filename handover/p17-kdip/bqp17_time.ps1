# П17: цена ключа на историю — одна сцена, тот же Release-бинарь, kdip=0 против kdip=1
# (склад mini_a16 считался сборкой build_A16 — Debug, 2.5× медленнее, сравнивать с ним нельзя).
$S = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\0950e013-1859-461a-be5d-e855b6858d3c\scratchpad'
$probe = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_rel_p17'
foreach ($k in @('0', '1')) {
    $d = "$S\p17_time$k"
    New-Item -ItemType Directory -Force $d | Out-Null
    Copy-Item "$S\mini_a16\G1S_point5.in" $d
    $t0 = Get-Date
    & "$probe\CorpusMatrixProbe.exe" "--dir=$d" --threads=10 --target=0 --n=300000 --peakb=1 --xrkl=1 "--kdip=$k" 2>&1 | Out-File -Encoding utf8 "$S\p17_time$k.log"
    "kdip=$k exit=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s"
    Get-Content "$S\p17_time$k.log" | Select-String -Pattern 'клеймо|время|счёт|обход'
}
