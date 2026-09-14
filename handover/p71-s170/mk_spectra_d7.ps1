# П71: копии двух спектров Co-60 с PointDistance 57 мм в узле Efficiency/Geometry — под матрицу плеча Δ = 7 мм.
$sp = 'D:\BqMoni_Claude\p71\wt\tools\CORPUS\corpus\spectra'
$d = 'D:\BqMoni_Claude\p71\spectra_d7'
New-Item -ItemType Directory -Force $d | Out-Null
foreach ($s in 'G1S16_Co60_P5','G1S24_Co60_P5') {
  $t = Get-Content "$sp\$s.xml" -Raw -Encoding UTF8
  $n = ([regex]::Matches($t, '<PointDistance>50</PointDistance>')).Count
  $t = $t -replace '<PointDistance>50</PointDistance>', '<PointDistance>57</PointDistance>'
  [IO.File]::WriteAllText("$d\$s.xml", $t, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("{0}: замен PointDistance {1}" -f $s, $n)
}
