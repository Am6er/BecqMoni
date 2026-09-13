# П48, T257: плечи замера. Перед этим — cycle.ps1 (приложение, build_head с HEAD-пробой, build_p48, оснастка wd_p48).
# Копии оснастки — в D:\BqMoni_Claude\p48\ (распоряжение Amber 13.09.2026): проба берёт config\ от каталога СВОЕГО exe.
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wd = "$repo\tools\CORPUS\scripts\wd_p48"
$art = "$repo\handover\p48-t257"
foreach ($n in 'wd_head','wd_head_file') {
  $d = "D:\BqMoni_Claude\p48\$n"
  if (Test-Path $d) { Remove-Item $d -Recurse -Force }
  Copy-Item $wd $d -Recurse
  Copy-Item 'D:\BqMoni_Claude\p48\build_head\FsaStackShot.exe' "$d\FsaStackShot.exe" -Force
  Copy-Item 'D:\BqMoni_Claude\p48\build_head\FsaStackShot.exe.config' "$d\FsaStackShot.exe.config" -Force -ErrorAction SilentlyContinue
}
Copy-Item "$repo\config\NuclideDefinition.xml" 'D:\BqMoni_Claude\p48\wd_head_file\config\NuclideDefinition.xml'   # копия, оригинал не трогается
Copy-Item "$wd\FsaStackShot.exe" 'D:\BqMoni_Claude\p48\wd_head_file\FsaStackShot_p48.exe'
Copy-Item "$wd\FsaStackShot.exe.config" 'D:\BqMoni_Claude\p48\wd_head_file\FsaStackShot_p48.exe.config' -ErrorAction SilentlyContinue
$codes = @()
$K = '--sample=241AM,44TI,152EU,137CS'
Set-Location 'D:\BqMoni_Claude\p48\wd_head'
& '.\FsaStackShot.exe' "--spectrum=$repo\tools\CORPUS\corpus\spectra\G1S16_Mix_Mar.xml" $K --scale=pow --pow=4 "--out=D:\BqMoni_Claude\p48\ctrl_head_nofile.png" *> "$art\control_head_without_NuclideDefinition_Mar.log"
$codes += "контроль (3) HEAD-exe без файла: код $LASTEXITCODE, png: $(Test-Path 'D:\BqMoni_Claude\p48\ctrl_head_nofile.png')"
foreach ($s in 'G1S16_Mix_Mar','G1S16_Mix_Denta100','G1S16_Mix_Petri') {
  $spec = "$repo\tools\CORPUS\corpus\spectra\$s.xml"
  Set-Location $wd
  & '.\FsaStackShot.exe' "--spectrum=$spec" $K --scale=pow --pow=4 --lib-dump "--out=$art\${s}_after_pow4.png" *> "$art\${s}_after.log"; $codes += "$s после pow4: $LASTEXITCODE"
  & '.\FsaStackShot.exe' "--spectrum=$spec" $K --scale=pow --pow=4 --to=250 "--out=$art\${s}_after_pow4_low250.png" *> "$art\${s}_after_low250.log"; $codes += "$s после low250: $LASTEXITCODE"
  Set-Location 'D:\BqMoni_Claude\p48\wd_head_file'
  & '.\FsaStackShot.exe' "--spectrum=$spec" $K --scale=pow --pow=4 --lib-dump "--out=$art\${s}_before_pow4.png" *> "$art\${s}_before.log"; $codes += "$s до (HEAD-exe, файл есть): $LASTEXITCODE"
  & '.\FsaStackShot_p48.exe' "--spectrum=$spec" $K --scale=pow --pow=4 --lib-dump "--out=D:\BqMoni_Claude\p48\${s}_after_withfile.png" *> "$art\${s}_after_withfile.log"; $codes += "$s после при файле (мой exe): $LASTEXITCODE"
}
$spec = "$repo\tools\CORPUS\corpus\spectra\G1S16_Mix_Mar.xml"
Set-Location $wd
& '.\FsaStackShot.exe' "--spectrum=$spec" --infer --scale=pow --pow=4 "--out=D:\BqMoni_Claude\p48\infer_nofile.png" *> "$art\G1S16_Mix_Mar_infer_after_without_file.log"; $codes += "--infer мой exe без файла: $LASTEXITCODE (ожидание: отказ менеджера, как раньше)"
Set-Location 'D:\BqMoni_Claude\p48\wd_head_file'
& '.\FsaStackShot.exe' "--spectrum=$spec" --infer --scale=pow --pow=4 "--out=D:\BqMoni_Claude\p48\infer_head.png" *> "$art\G1S16_Mix_Mar_infer_before.log"; $codes += "--infer HEAD-exe с файлом: $LASTEXITCODE"
& '.\FsaStackShot_p48.exe' "--spectrum=$spec" --infer --scale=pow --pow=4 "--out=D:\BqMoni_Claude\p48\infer_p48.png" *> "$art\G1S16_Mix_Mar_infer_after.log"; $codes += "--infer мой exe с файлом: $LASTEXITCODE"
& '.\FsaStackShot_p48.exe' "--spectrum=$spec" $K --spoil=manager --scale=pow --pow=4 "--out=D:\BqMoni_Claude\p48\spoil_withfile.png" *> "$art\control_gate_spoil_manager_with_file_Mar.log"; $codes += "гейт: --spoil=manager при файле: $LASTEXITCODE (ожидание 12)"
Set-Location $wd
& '.\FsaStackShot.exe' "--spectrum=$spec" $K --spoil=manager --scale=pow --pow=4 "--out=D:\BqMoni_Claude\p48\spoil_nofile.png" *> "$art\control_gate_spoil_manager_without_file_Mar.log"; $codes += "гейт: --spoil=manager без файла: $LASTEXITCODE (ожидание: падение до счёта)"
$codes
Set-Location $repo
$env:PYTHONIOENCODING = 'utf-8'
python "$art\stand\compare.py" | Tee-Object -FilePath "$art\compare_before_after.md"
