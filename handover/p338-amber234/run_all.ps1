# П338 AMBER234: сборка Release в каталог полосы, пробы, контроль на 2 узлах, склад двух сцен
# (умолчания приложения), матрицы сравнения (npl=0, бин 1 кэВ, узлы их npz), кривые в спектры.
# Запуск: & 'D:\BqMoni_Claude\p338\run_all.ps1' [-Step N]   (N — с какого шага)
param([int]$Step = 1)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$R = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$L = 'D:\BqMoni_Claude\p338'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
function Say($m) { Write-Host ("[{0,6:F0} с] {1}" -f $sw.Elapsed.TotalSeconds, $m) }

if ($Step -le 1) {
    Say 'шаг 1: сборка приложения Release → app\'
    & $msb "$R\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$L\app\" '/p:IntermediateOutputPath=obj\Release_p338\' `
        /v:minimal /nologo *> "$L\build_app.log"
    $c = $LASTEXITCODE; Say "   MSBuild код $c"; if ($c -ne 0) { Get-Content "$L\build_app.log" -Tail 20; exit 11 }
}
if ($Step -le 2) {
    Say 'шаг 2: пробы build_all.ps1 → probes\'
    & "$R\tools\effmaker\probes\build_all.ps1" -Bin "$L\app" -Out "$L\probes" *> "$L\build_probes.log"
    $c = $LASTEXITCODE; Say "   build_all код $c"; if ($c -ne 0) { Get-Content "$L\build_probes.log" -Tail 20; exit 12 }
    foreach ($e in 'CorpusMatrixProbe.exe', 'CorpusEffProbe.exe', 'MatrixAuditProbe.exe', 'FsaStackShot.exe', 'FsaComponentDumpProbe.exe') {
        if (-not (Test-Path "$L\probes\$e")) { Say "⛔ нет $e"; exit 12 }
    }
}
$store = "$L\show\store"
Push-Location "$L\probes"
try {
    if ($Step -le 3) {
        Say 'шаг 3: контроль на 2 узлах (A77)'
        $chk = "$L\check2"; if (Test-Path $chk) { Remove-Item $chk -Recurse -Force }; New-Item -ItemType Directory $chk | Out-Null
        Copy-Item "$store\*.in" $chk
        & .\CorpusMatrixProbe.exe "--dir=$chk" --nodes=2 --emin=30 --emax=31 --n=400000 --threads=10 --target=0 --jnodes=0 *> "$L\control_2nodes.log"
        $c = $LASTEXITCODE; Say "   код $c (1 = «шумные», ожидаемо на 400 k)"
        Get-Content "$L\control_2nodes.log" | Select-String 'клейм|мкс|ОТКАЗ|Exception|ошиб' | Select-Object -First 10 | ForEach-Object { Say ('   ' + $_.Line) }
        if ($c -notin 0, 1) { exit 13 }
    }
    if ($Step -le 4) {
        Say 'шаг 4: склад — полные матрицы двух сцен (умолчания приложения)'
        & .\CorpusMatrixProbe.exe "--dir=$store" --threads=10 --target=0 *> "$L\count.log"
        $c = $LASTEXITCODE; Say "   код $c"
        Get-Content "$L\count.log" | Select-String 'клейм|СОШЛИСЬ|ШУМН|мкс|часах|пропущ' | Select-Object -Last 8 | ForEach-Object { Say ('   ' + $_.Line) }
        if ($c -ne 0) { exit 14 }
    }
    if ($Step -le 5) {
        Say 'шаг 5: кривые и guid в спектры (CorpusEffProbe)'
        & .\CorpusEffProbe.exe "--dir=$store" "--spectra=$L\show\spectra" *> "$L\eff.log"
        $c = $LASTEXITCODE; Say "   код $c"
        Get-Content "$L\eff.log" | Select-String 'guid|узл|медиан|спектр|пропущена|НЕТ' | Select-Object -Last 8 | ForEach-Object { Say ('   ' + $_.Line) }
        if ($c -ne 0) { exit 15 }
    }
    if ($Step -le 6) {
        Say 'шаг 6: матрицы сравнения — сцена ОИСН-10, npl=0, бин 1 кэВ, по узлу в каждой энергии их npz'
        $energies = '20', '25', '28', '30', '32', '32.8', '33.5', '36', '40', '46.5', '50', '59.5', '66', '74.97', '84.986', '93.347'
        foreach ($e in $energies) {
            $d = "$L\cmp\E$e"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null
            Copy-Item "$store\GS2020_mar1l_oisn10.in" $d
            $hi = ([double]::Parse($e, [Globalization.CultureInfo]::InvariantCulture) * 1.0001).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
            & .\CorpusMatrixProbe.exe "--dir=$d" --nodes=2 "--emin=$e" "--emax=$hi" --bin=1 --npl=0 --n=3000000 --threads=10 --target=0 --jnodes=0 *> "$d\count.log"
            Say ("   {0} кэВ: код {1}" -f $e, $LASTEXITCODE)
        }
    }
} finally { Pop-Location }
Say 'готово'
exit 0
