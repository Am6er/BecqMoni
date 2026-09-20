# П88 (AMBER22): склад полосы = копия склада П67 (17 матриц формата 8, физика 18) + сцены с Al-оправой + новая опись;
# спектры полосы и по-сценные копии. Живой склад корпуса и каталог П67 — только чтение.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p88\mk_store.ps1
$ErrorActionPreference = 'Stop'
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p88'
$amber = 'C:\Users\moroz\YandexDisk\Спектры\!AS80x80'
# 1. склад
& robocopy 'D:\BqMoni_Claude\p67\store' "$p\store" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy склада: код $LASTEXITCODE" }
"склад: {0} .rmx, {1} .in" -f (Get-ChildItem "$p\store\*.rmx").Count, (Get-ChildItem "$p\store\*.in").Count
# 2. сцены Al + опись
$env:PYTHONIOENCODING = 'utf-8'
python "$p\py\mk_scenes_p88.py" "$p\store"
if ($LASTEXITCODE -ne 0) { throw "mk_scenes_p88.py: код $LASTEXITCODE" }
# 3. спектры полосы (после перекалибровки — из YandexDisk; до — из backup; корпусный контакт — копия П67)
New-Item -ItemType Directory -Force "$p\spectra" | Out-Null
Copy-Item 'D:\BqMoni_Claude\p67\spectra\contact52k.xml' "$p\spectra\contact52k.xml" -Force
Copy-Item "$amber\калибровка 08.09.2026\Th-232.xml" "$p\spectra\contact_cal0809.xml" -Force
Copy-Item "$p\backup\калибровка 08.09.2026\Th-232.xml" "$p\spectra\contact_cal0809_bg0.xml" -Force
Copy-Item "$amber\Th-232(медальон ребром) - дистанция 81мм.xml" "$p\spectra\edge93.xml" -Force
Copy-Item "$p\backup\Th-232(медальон ребром) - дистанция 81мм.xml" "$p\spectra\edge93_bg0.xml" -Force
Copy-Item "$amber\Th-232(медальон ребром) - дистанция 81мм с другого бока.xml" "$p\spectra\edge1709.xml" -Force
Copy-Item "$p\backup\Th-232(медальон ребром) - дистанция 81мм с другого бока.xml" "$p\spectra\edge1709_bg0.xml" -Force
Get-ChildItem "$p\spectra\*.xml" | ForEach-Object { "  {0,-28} {1,8} байт  sha256 {2}" -f $_.Name, $_.Length, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16).ToLower() }
# 4. по-сценные копии по описи (без узла <Efficiency>; его ставит CorpusEffProbe в attach_p88.ps1)
New-Item -ItemType Directory -Force "$p\spectra_scenes" | Out-Null
$rows = Import-Csv "$p\store\index.csv"
$n = 0
foreach ($r in $rows) {
  $sp = ($r.spectrum -split '__')[0]
  $dst = "$p\spectra_scenes\$($r.spectrum).xml"
  if (-not (Test-Path $dst)) { Copy-Item "$p\spectra\$sp.xml" $dst; $n++ }
}
"по-сценных копий создано: $n (строк описи $($rows.Count))"
exit 0
