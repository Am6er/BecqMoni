# П30 (S106): МК-поверка МДА обеими половинами (--limits-mc=100, --refit-z=0 обязателен) на трёх понятных спектрах малой базы.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$cells = @(
  @{ Arm = 'mc_cs137'; Only = 'G1S16_Cs137_P25' },
  @{ Arm = 'mc_y88';   Only = 'G1S24_Y88_P25' },
  @{ Arm = 'mc_lu176'; Only = 'ASN16_Lu176_P0' },
  @{ Arm = 'mc_th228'; Only = 'G1S16_Th228_P25' }
)
foreach ($c in $cells) {
  & 'C:\Users\moroz\bqp30\handover\p30-cleaner-s\p30_run.ps1' -Arm ("out_p30_mini_" + $c.Arm) -Mini -Extra ('--only=' + $c.Only), '--limits-mc=100', '--refit-z=0'
}
