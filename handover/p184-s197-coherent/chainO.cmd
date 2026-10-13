@echo off
rem P184 chain O: our G4RawProbe on physics 25: recheck of P178 px runs, coherent off/on with --photox
cd /d D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184
echo start our_px60 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
"D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184\G4RawProbe.exe" --geometry=D:\BqMoni_Claude\p184\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in --energy=60 --n=20000000 --bin=0.1 --no-light --peakw --seed=19860 --out=D:\BqMoni_Claude\p184\g4\our_px60.csv --photox=0.9770 > D:\BqMoni_Claude\p184\g4\our_px60.txt 2>&1
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\our_px60.txt
echo done our_px60 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
echo start our_px32 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
"D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184\G4RawProbe.exe" --geometry=D:\BqMoni_Claude\p184\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in --energy=32 --n=40000000 --bin=0.1 --no-light --peakw --seed=19832 --out=D:\BqMoni_Claude\p184\g4\our_px32.csv --photox=1.0104 > D:\BqMoni_Claude\p184\g4\our_px32.txt 2>&1
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\our_px32.txt
echo done our_px32 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
echo start our_off32 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
"D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184\G4RawProbe.exe" --geometry=D:\BqMoni_Claude\p184\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in --energy=32 --n=120000000 --bin=0.1 --no-light --peakw --seed=39832 --out=D:\BqMoni_Claude\p184\g4\our_off32.csv --coh=0 --photox=1.0104 > D:\BqMoni_Claude\p184\g4\our_off32.txt 2>&1
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\our_off32.txt
echo done our_off32 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
echo start our_on32b %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
"D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184\G4RawProbe.exe" --geometry=D:\BqMoni_Claude\p184\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in --energy=32 --n=80000000 --bin=0.1 --no-light --peakw --seed=49832 --out=D:\BqMoni_Claude\p184\g4\our_on32b.csv --photox=1.0104 > D:\BqMoni_Claude\p184\g4\our_on32b.txt 2>&1
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\our_on32b.txt
echo done our_on32b %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
echo start our_off60 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
"D:\BqMoni_Claude\p184\wt\tools\effmaker\probes\build_P184\G4RawProbe.exe" --geometry=D:\BqMoni_Claude\p184\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in --energy=60 --n=40000000 --bin=0.1 --no-light --peakw --seed=39860 --out=D:\BqMoni_Claude\p184\g4\our_off60.csv --coh=0 --photox=0.9770 > D:\BqMoni_Claude\p184\g4\our_off60.txt 2>&1
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\our_off60.txt
echo done our_off60 %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
echo ALLDONE %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainO_status.txt
