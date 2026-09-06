# Пересчёт склада 44 матриц (A40), решение Amber 06.09.2026:
# физика 16, ПЛОСКИЕ 3 млн историй, догонка по шуму ВЫКЛЮЧЕНА (--target=0).
# Запускается ОТДЕЛЬНЫМ процессом, чтобы прогон на часы не зависел от сеанса.
$ErrorActionPreference = 'Continue'
# ⛔ Без этого отдельный (скрытый) pwsh декодирует вывод пробы кодовой страницей
# консоли, и весь русский текст лога уходит в кракозябры: проба печатает UTF-8,
# а родитель читает 866. Поймано на первом запуске.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
$R = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $R
$started = Get-Date
"НАЧАЛО: $started" | Out-File -Encoding utf8 "$R\handover\p1-e34\store_all44.log"
& "$R\tools\effmaker\probes\build_p1\CorpusMatrixProbe.exe" `
    "--dir=$R\tools\CORPUS\scripts\wd_p1" `
    --target=0 `
    "--dump=$R\handover\p1-e34\store_nodes.csv" 2>&1 |
    Out-File -Encoding utf8 -Append "$R\handover\p1-e34\store_all44.log"
$code = $LASTEXITCODE
$done = Get-Date
"КОНЕЦ: $done" | Out-File -Encoding utf8 -Append "$R\handover\p1-e34\store_all44.log"
"КОД ВОЗВРАТА: $code" | Out-File -Encoding utf8 -Append "$R\handover\p1-e34\store_all44.log"
"ЧАСОВ: $(($done - $started).TotalHours)" | Out-File -Encoding utf8 -Append "$R\handover\p1-e34\store_all44.log"
