$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location "$root\tools\CORPUS\scripts\wd_p49"
$sp = "$root\tools\CORPUS\corpus\spectra"
$out = 'D:\BqMoni_Claude\p49'
foreach ($k in 0,1) {
  # (в) нейтральная мерка: сумм-пик Co-60 2505 на двух спектрах, участок 2400–2620
  foreach ($s in 'G1S16_Co60_P5','G1S16_Co60_P25','G1S24_Co60_P5') {
    & '.\FsaCascadeProbe.exe' "--spectrum=$sp\$s.xml" --sample=Co-60 "--angcorr=$k" --lines=4 --scan=2400:2620 *> "$out\cf_${s}_ang$k.log"
  }
  # CF по нуклидам строки: Cs-134 (в корпусе нет — CF от матрицы сцены), Y-88, Eu-152, Bi-207, Th-228
  & '.\FsaCascadeProbe.exe' "--spectrum=$sp\G1S16_Eu152_P5.xml" --sample=Eu-152,Cs-134,Y-88,Bi-207 "--angcorr=$k" --lines=8 *> "$out\cf_multi_p5_ang$k.log"
  & '.\FsaCascadeProbe.exe' "--spectrum=$sp\G1S16_Th228_P5.xml" --chain=Th-228 "--angcorr=$k" --lines=8 *> "$out\cf_th228_p5_ang$k.log"
  & '.\FsaCascadeProbe.exe' "--spectrum=$sp\AS80_Th232Medal.xml" --chain=Th-232 "--angcorr=$k" --lines=8 *> "$out\cf_amber_disk_ang$k.log"
}
"done" | Out-File "$out\cf_done.txt"
