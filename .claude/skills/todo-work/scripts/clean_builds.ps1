param([switch]$Apply)
# Чистка сборочных каталогов (решение Amber 13.09.2026: «Все четыре места», «Канонические + моложе 24 ч»).
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$cut  = (Get-Date).AddHours(-24)
$sets = @(
  @{ Dir = "$root\BecquerelMonitor\bin";       Keep = @('Debug','Debug_Codex','Release_Codex');              Pattern = '*' },
  @{ Dir = "$root\BecquerelMonitor\obj";       Keep = @('Debug','Release','Debug_Codex','Release_Codex');    Pattern = '*' },
  @{ Dir = "$root\tools\effmaker\probes";      Keep = @('build','build_rel');                                Pattern = 'build*' },
  @{ Dir = "$root\tools\CORPUS\scripts";       Keep = @('wd_app');                                           Pattern = 'wd_*' }
)
$totalDel = 0L; $nDel = 0; $nKeep = 0; $failed = @()
foreach ($s in $sets) {
  Get-ChildItem -LiteralPath $s.Dir -Directory -Filter $s.Pattern | ForEach-Object {
    $d = $_
    $files = Get-ChildItem -LiteralPath $d.FullName -Recurse -File -ErrorAction SilentlyContinue
    $size = ($files | Measure-Object Length -Sum).Sum; if (-not $size) { $size = 0 }
    $newest = ($files | Measure-Object LastWriteTime -Maximum).Maximum
    if (-not $newest) { $newest = $d.LastWriteTime }
    $isKeep = ($s.Keep -contains $d.Name) -or ($newest -ge $cut)
    if ($isKeep) {
      $nKeep++
      "KEEP  {0,8:F0} МБ  {1:yyyy-MM-dd HH:mm}  {2}" -f ($size/1MB), $newest, $d.FullName.Substring($root.Length+1)
    } else {
      $nDel++; $totalDel += $size
      if ($Apply) {
        try {
          Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction Stop
          "DEL   {0,8:F0} МБ  {1:yyyy-MM-dd HH:mm}  {2}" -f ($size/1MB), $newest, $d.FullName.Substring($root.Length+1)
        } catch {
          $failed += $d.FullName
          "FAIL  {0,8:F0} МБ  {1}  -- {2}" -f ($size/1MB), $d.FullName.Substring($root.Length+1), $_.Exception.Message
        }
      } else {
        "WOULD {0,8:F0} МБ  {1:yyyy-MM-dd HH:mm}  {2}" -f ($size/1MB), $newest, $d.FullName.Substring($root.Length+1)
      }
    }
  }
}
""
"итого: к удалению {0} каталогов, {1:F1} ГБ; остаётся {2}; отказов {3}" -f $nDel, ($totalDel/1GB), $nKeep, $failed.Count
if ($failed.Count) { "не удалились (заняты?):"; $failed }
