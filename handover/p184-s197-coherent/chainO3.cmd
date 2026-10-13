@echo off
rem P184 chain O3: extra ours at 60 keV with --photox, after chain O
cd /d D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184
:w
findstr /c:ALLDONE D:\BqMoni_Claude\p184\g4\chainO_status.txt >nul 2>&1 || (ping -n 31 127.0.0.1 >nul & goto w)
echo start our_on60b %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO3_status.txt
"D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184\G4RawProbe.exe" --geometry=D:\BqMoni_Claude\p184\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in --energy=60 --n=60000000 --bin=0.1 --no-light --peakw --seed=29860 --out=D:\BqMoni_Claude\p184\g4\our_on60b.csv --photox=0.9770 > D:\BqMoni_Claude\p184\g4\our_on60b.txt 2>&1
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\our_on60b.txt
echo done our_on60b %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO3_status.txt
echo ALLDONE %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO3_status.txt
