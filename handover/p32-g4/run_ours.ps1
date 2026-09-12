# П32 12.09.2026, `F16` + `A64` — наша сторона: сырой отклик G4RawProbe (build_p32, HEAD cdf86bbf,
# шкала ЭНЕРГОВЫДЕЛЕНИЯ --no-light, бин 1 кэВ, допуск пика ноль, умолчания физики 16 = склад,
# kdip=1 умолчанием пробы) ДВУМЯ плечами --etr=0 / --etr=1 (физика 17, ключ ВЫКЛ на складе).
#   F16: ASN16_lu_side (CsI 15×18×60 брусок, банка Lu₂O₃ Ø42×15 сбоку), 200 кэВ, 8 млн.
#   A64: AS80_point0 (NaI Ø80×80 с обвязкой, точка вплотную), 661.657 и 1332.5 кэВ, 8 млн.
#   Контроль: AS80_bare_gap5 661.657 --etr=0 4 млн — обязан побайтно повторить
#   handover\p27-electron-transport\accept\ours_AS80_bare_gap5_661.657_etr0.csv (та же сборка HEAD).
# Сцены — копии в scenes\ (дампы --scene= сняты заходом размера, 22:0x).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p32"
$scenes = "$root\handover\p32-g4\scenes"
$art = "$root\handover\p32-g4"
$runs = @(
    @{ d = 'f16'; g = 'ASN16_lu_side'; e = 200;      n = 8000000; etr = 0; bands = '0-50,50-100,100-150,150-197' },
    @{ d = 'f16'; g = 'ASN16_lu_side'; e = 200;      n = 8000000; etr = 1; bands = '0-50,50-100,100-150,150-197' },
    @{ d = 'a64'; g = 'AS80_point0';   e = 661.657;  n = 8000000; etr = 0; bands = '13-477,478-659' },
    @{ d = 'a64'; g = 'AS80_point0';   e = 661.657;  n = 8000000; etr = 1; bands = '13-477,478-659' },
    @{ d = 'a64'; g = 'AS80_point0';   e = 1332.5;   n = 8000000; etr = 0; bands = '13-1118,1119-1330' },
    @{ d = 'a64'; g = 'AS80_point0';   e = 1332.5;   n = 8000000; etr = 1; bands = '13-1118,1119-1330' },
    @{ d = 'ctl'; g = 'AS80_bare_gap5'; e = 661.657; n = 4000000; etr = 0; bands = '13-477,478-659' }
)
New-Item -ItemType Directory -Force "$art\ctl" | Out-Null
Push-Location $bin
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)_etr$($r.etr)"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$($r.n)" `
        "--etr=$($r.etr)" "--bands=$($r.bands)" "--out=$art\$($r.d)\ours_$tag.csv" *> "$art\$($r.d)\ours_$tag.txt"
    "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ours.txt"
}
Pop-Location
"done ours $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ours.txt"
