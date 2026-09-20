# П92: дампы сцен (--scene=) для арбитра из каждой геометрии, короткий прогон 2000 историй.
$probe = 'D:\BqMoni_Claude\p92\wt\tools\effmaker\probes\build_p92\G4RawProbe.exe'
$geo = 'D:\BqMoni_Claude\p92\geo'
$sc = 'D:\BqMoni_Claude\p92\scenes'
foreach ($f in Get-ChildItem "$geo\*.in") {
    $name = $f.BaseName
    & $probe "--geometry=$($f.FullName)" --energy=661.657 --n=2000 --no-light --bin=1 "--scene=$sc\$name.scene" "--out=$sc\$name.smoke.csv" 2>&1 | Out-Null
    "$name code=$LASTEXITCODE"
}
