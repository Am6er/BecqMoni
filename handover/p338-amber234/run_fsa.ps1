# П338 AMBER234: наш FSA на их спектрах. Рабочий каталог — копия проб; в config\device РОВНО наш прибор
# (T33: лишний прибор с тем же GUID — модальное окно) и матрицы по guid кривых, вписанных CorpusEffProbe.
param([switch]$NoCopy)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$R = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$L = 'D:\BqMoni_Claude\p338'
$wd = "$L\wd"; $out = "$L\fsa"
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $NoCopy) {
    if (Test-Path $wd) { Remove-Item $wd -Recurse -Force }
    robocopy "$L\probes" $wd /E /XD "$L\probes\config\device" /NFL /NDL /NJH /NJS /NP | Out-Null
}
New-Item -ItemType Directory -Force "$wd\config\device\response" | Out-Null
Get-ChildItem "$wd\config\device\*.xml" -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item "$L\show\config\device\*.xml" "$wd\config\device\"
Copy-Item "$L\show\config\NuclideDefinition.xml" "$wd\config\NuclideDefinition.xml" -Force
Get-ChildItem "$wd\config\device\response\*" -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item "$L\show\store\response\*.rmx" "$wd\config\device\response\"
Write-Host ('приборов: {0}; матриц: {1}' -f @(Get-ChildItem "$wd\config\device\*.xml").Count, @(Get-ChildItem "$wd\config\device\response\*.rmx").Count)
Get-ChildItem "$wd\config\device\response\*.rmx" | ForEach-Object { Write-Host ('   ' + $_.Name + '  ' + (Get-FileHash $_.FullName).Hash.Substring(0, 16)) }

$runs = @(
    @{ key = 'berry'; spec = "$L\show\spectra\GS2020_berry_482g.xml"; set = 'Cs-137 + K-40'; sample = '137CS,40K'; chain = '' },
    @{ key = 'th232'; spec = "$L\show\spectra\GS2020_Th232_oisn10.xml"; set = 'Th-232'; sample = ''; chain = 'Th-232' }
)
Push-Location $wd
try {
    foreach ($r in $runs) {
        $k = $r.key
        # 1) как увидит человек: снимок стека настоящим кодом отрисовки
        & .\FsaStackShot.exe "--spectrum=$($r.spec)" --infer "--set=$($r.set)" --screen --scale=pow --width=1600 `
            "--dump=$out\$k.shot.curves.csv" "--rates=$out\$k.shot.rates.csv" "--out=$out\$k.shot.png" *> "$out\$k.shot.log"
        Write-Host ("{0}: FsaStackShot код {1}" -f $k, $LASTEXITCODE)
        # 2) состав из базы, пол фита поставочный и 150 кэВ (окно подгонки как у них)
        foreach ($floor in 'ship', '150') {
            $a = @("--spectrum=$($r.spec)", "--out=$out\$k.dump.$floor")
            if ($r.sample) { $a += "--sample=$($r.sample)" }
            if ($r.chain) { $a += "--chain=$($r.chain)" }
            if ($floor -ne 'ship') { $a += "--fit-floor=$floor" }
            & .\FsaComponentDumpProbe.exe @a *> "$out\$k.dump.$floor.log"
            Write-Host ("{0}: FsaComponentDumpProbe пол {1} код {2}" -f $k, $floor, $LASTEXITCODE)
        }
    }
} finally { Pop-Location }
