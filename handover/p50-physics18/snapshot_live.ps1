# П50 — снимок живого склада (ТОЛЬКО ЧТЕНИЕ): имя, размер, mtime до мс всех файлов geometries\ и response\,
# плюс sha256 45 .rmx. Снимается ДО работы и ПОСЛЕ — расхождений быть не должно (живой склад не трогаем).
param([string]$Tag = 'before')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = 'D:\BqMoni_Claude\p50\art'
$g = "$root\tools\CORPUS\corpus\geometries"
Get-ChildItem $g -File -Recurse | Sort-Object FullName | ForEach-Object {
    "{0}`t{1}`t{2}" -f $_.FullName.Substring($g.Length + 1), $_.Length, $_.LastWriteTimeUtc.ToString('yyyy-MM-dd HH:mm:ss.fff')
} | Out-File "$art\live_store_$Tag.txt" -Encoding utf8
if ($Tag -eq 'before') {
    Get-ChildItem $g -Filter *.rmx | Sort-Object Name | ForEach-Object {
        "{0}  {1}  {2}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Length, $_.Name
    } | Out-File "$art\live_store_phys17_sha256.txt" -Encoding utf8
}
"live snapshot ${Tag}: $((Get-Content "$art\live_store_$Tag.txt").Count) files $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
