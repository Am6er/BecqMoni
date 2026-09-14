# П55 (A72 оценка, 14.09.2026): арбитр Geant4 (копия D:\BqMoni_Claude\p55\g4 с рычагами) —
# умолчание против «наших приближений»: nofluct (п.1 Ландау), nodelta / lowcut (п.2 δ-электроны),
# killesc (п.3 возврат из обвязки). Мир — vacuum у всех (у нас воздуха нет; разности внутри арбитра
# от мира не зависят). Зерно арбитра не задаётся — одно и то же у всех прогонов (события вне
# кристалла общие, разности коррелированы).
# ⛔ Кодировку консоли не трогать (П20/П26): g4run.cmd ставит 1251 сам.
$ErrorActionPreference = 'Continue'
$g4 = 'D:\BqMoni_Claude\p55\g4\g4run.cmd'
$sc = 'D:\BqMoni_Claude\p55\scenes'
$out = 'D:\BqMoni_Claude\p55\g4out'
New-Item -ItemType Directory -Force $out | Out-Null
$codes = "$out\codes.txt"
$runs = @(
    @{ g = 'RC103_point0';  e = 1460.82;  n = 40000000; v = @('def','nofluct','nodelta','lowcut','killesc') },
    @{ g = 'ASN16_lu_side'; e = 1460.82;  n = 8000000;  v = @('def','nofluct','nodelta','lowcut','killesc') },
    @{ g = 'AS80_point0';   e = 1460.82;  n = 4000000;  v = @('def','nofluct','nodelta','killesc') },
    @{ g = 'AS80_th_disk';  e = 2614.511; n = 8000000;  v = @('def','nofluct','nodelta','killesc') },
    @{ g = 'RC103_point0';  e = 661.657;  n = 40000000; v = @('def','nofluct','lowcut','killesc') },
    @{ g = 'ASN16_lu_side'; e = 661.657;  n = 8000000;  v = @('def','nofluct','lowcut','killesc') },
    @{ g = 'AS80_point0';   e = 661.657;  n = 4000000;  v = @('def','nofluct','killesc') },
    @{ g = 'RC103_point0';  e = 59.541;   n = 40000000; v = @('def','nofluct','lowcut','killesc') },
    @{ g = 'ASN16_lu_side'; e = 59.541;   n = 8000000;  v = @('def','lowcut','killesc') },
    @{ g = 'RC103_point0';  e = 2614.511; n = 40000000; v = @('def','nofluct','nodelta','killesc') }
)
foreach ($r in $runs) {
    foreach ($v in $r.v) {
        $tag = "$($r.g)_$($r.e)_$v"
        $flags = @()
        if ($v -ne 'def') { $flags += $v }
        $t0 = Get-Date
        & $g4 @flags vacuum scene "$sc\$($r.g).scene" hist $r.e $r.n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
        "g4 $tag code=$LASTEXITCODE n=$($r.n) $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
    }
}
"done g4 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
