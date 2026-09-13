# П41 13.09.2026 — приёмка (б): выигрыш важностного розыгрыша на полевой сцене и несмещённость.
# SceneCostProbe --ab: плечи ВКЛ/ВЫКЛ на сосудной (ASN16_lu_side, как в корпусе) и полевой сцене
# (Ground того же прибора по грунту), эталон несмещённости — ДЛИННЫЙ аналоговый прогон
# (nBig = 40 млн историй), ВКЛ — 2 млн; плечо-порча «ВКЛ БЕЗ ВЕСА» обязано выпасть из 3σ.
# Три энергии: 662 (как П18), 100 (низ) и лунка (--scene=borehole, маринелли-ветка розыгрыша).
# Коды возврата — в codes.txt (A77).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p41"
$art = "$root\handover\p41-e29"
$scratch = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\c622bbcd-f473-47e4-9f7b-f2056ac28f0a\scratchpad\p41'
Set-Location $root
"ctrl_b start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"

# 662 кэВ, земля: эталон 40 млн, ВКЛ 2 млн; заодно полевая сцена — файлом для пути кривой.
& "$bin\SceneCostProbe.exe" --ab --nbig=40000000 --nab=2000000 --energy=662 `
    "--save-ground=$scratch\ground\ASN16_ground.in" > "$art\ctrl_b_ab_662.log" 2>&1
"ctrl_b ab662 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

# 100 кэВ, земля (низ шкалы).
& "$bin\SceneCostProbe.exe" --ab --nbig=40000000 --nab=2000000 --energy=100 > "$art\ctrl_b_ab_100.log" 2>&1
"ctrl_b ab100 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

# 662 кэВ, лунка (маринелли-ветка розыгрыша): квадратуры нет, только пункт 1 и A/B.
& "$bin\SceneCostProbe.exe" --ab --scene=borehole --nbig=40000000 --nab=2000000 --energy=662 > "$art\ctrl_b_ab_borehole_662.log" 2>&1
"ctrl_b borehole662 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

# Контроль формы С ВКЛЮЧЁННЫМ розыгрышем (--imp=1): усечённая сцена против полной, 4 млн каждая —
# квадратура и прогон обязаны сойтись, и у контроля обязаны быть зубы.
& "$bin\SceneCostProbe.exe" --imp=1 --nbig=4000000 --energy=662 > "$art\ctrl_b_imp1_662.log" 2>&1
"ctrl_b imp1_662 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
# Сосудная сцена СИЛЬНЕЕ: ВКЛ против ВЫКЛ по 4 млн историй каждое, другое зерно, без контроля формы
# (плечи большой сцены здесь короткие и не в счёт — читать только строки «малая (сосуд)»).
& "$bin\SceneCostProbe.exe" --ab --nsmall=4000000 --nbig=2000000 --nab=4000000 --energy=662 --seed=20260913 --skip-shape > "$art\ctrl_b_vessel4m.log" 2>&1
"ctrl_b vessel4m code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_b end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
