@echo off
rem P175: our raw response RC-103 marinelli 60 keV. %1 geometry, %2 N, %3 out prefix, %4 seed, %5.. extra keys
set PB=D:\BqMoni_Claude\p175\wt\tools\effmaker\probes\build_p175
cd /d %PB%
"%PB%\G4RawProbe.exe" --geometry=%1 --energy=%5 --n=%2 --bin=0.1 --no-light --peakw --seed=%4 --out=%3.csv %6 %7 %8 > %3.txt 2>&1
echo code=%ERRORLEVEL% >> %3.txt
