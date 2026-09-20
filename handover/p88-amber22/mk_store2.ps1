# П88, шаг 2: спектры стенда после перекалибровки ПРОБ (шкала пробы по её пикам + фон перекалиброванный) и по-сценные копии.
# Склад (store) уже собран (mk_store.ps1), опись переписывается mk_scenes_p88.py с новым списком спектров:
#   contact52k — корпусный; contact_cal0809 / edge93 / edge1709 — файлы Amber ПОСЛЕ шага 2;
#   *_s1 — после шага 1 (фон перекалиброван, проба P) — копии из backup\step2 — прямое A/B в одном прогоне.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p88\mk_store2.ps1
$ErrorActionPreference = 'Stop'
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p88'
$amber = 'C:\Users\moroz\YandexDisk\Спектры\!AS80x80'
$env:PYTHONIOENCODING = 'utf-8'
python "$p\py\mk_scenes_p88.py" "$p\store" --step2
if ($LASTEXITCODE -ne 0) { throw "mk_scenes_p88.py: код $LASTEXITCODE" }
New-Item -ItemType Directory -Force "$p\spectra" | Out-Null
Copy-Item 'D:\BqMoni_Claude\p67\spectra\contact52k.xml' "$p\spectra\contact52k.xml" -Force
Copy-Item "$amber\калибровка 08.09.2026\Th-232.xml" "$p\spectra\contact_cal0809.xml" -Force
Copy-Item "$p\backup\step2\Th-232.xml" "$p\spectra\contact_cal0809_s1.xml" -Force
Copy-Item "$amber\Th-232(медальон ребром) - дистанция 81мм.xml" "$p\spectra\edge93.xml" -Force
Copy-Item "$p\backup\step2\Th-232(медальон ребром) - дистанция 81мм.xml" "$p\spectra\edge93_s1.xml" -Force
Copy-Item "$amber\Th-232(медальон ребром) - дистанция 81мм с другого бока.xml" "$p\spectra\edge1709.xml" -Force
Copy-Item "$p\backup\step2\Th-232(медальон ребром) - дистанция 81мм с другого бока.xml" "$p\spectra\edge1709_s1.xml" -Force
Get-ChildItem "$p\spectra\*.xml" | ForEach-Object { "  {0,-28} {1,8} байт  sha256 {2}" -f $_.Name, $_.Length, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16).ToLower() }
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
