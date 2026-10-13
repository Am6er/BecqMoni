@echo off
rem P175: g4cf RC-103 marinelli 60 keV, scene %1, N %2, log %3, seed %4
call "C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat" vacuum seed %4 scene %1 hist %5 %2 0.1 > %3 2> %3.err
echo code=%ERRORLEVEL% >> %3.err
