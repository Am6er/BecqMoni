# П73 (V10): сквозная активность через зоны ROI (RoiActivityProbe, код приложения) на четырёх спектрах стенда.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p73\run_roi.ps1
# Паспорт Cs-137 9.25 кБк на 02.01.2002, T1/2 = 30.08 л: 14.09.2026 -> 5235.6; 23.01.2024 -> 5564.3; 03.12.2022 -> 5712.2 Бк.
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p73'
$out = "$p\out_roi"
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location "$p\wd"
$rows = @(
  @{ s = 'RC103_Cs137_50mm';       roi = 'P73 RC103 Cs-137'; A = 5235.6 },
  @{ s = 'RC103_Cs137_50mm_recal'; roi = 'P73 RC103 Cs-137'; A = 5235.6 },
  @{ s = 'RC103_Cs137_0cm';        roi = 'P73 RC103 Cs-137'; A = 5564.3 },
  @{ s = 'ASN16_Cs137_10cm';       roi = 'P73 ASN16 Cs-137'; A = 5712.2 }
)
$codes = @()
foreach ($r in $rows) {
  $log = "$out\$($r.s).txt"
  & .\RoiActivityProbe.exe "--spectrum=$p\spectra\$($r.s).xml" "--roi=$p\roi\$($r.roi).xml" "--passport=$($r.A)" > $log 2>&1
  $codes += "$($r.s)=$LASTEXITCODE"
  "=== $($r.s)  код $LASTEXITCODE"
  Get-Content $log
}
Pop-Location
"коды: $($codes -join ' ')"
$codes | Set-Content "$out\codes.txt"
