@echo off
rem P32 (copy of P26 wrapper): console code page 1251 before run_g4cf.bat - under 65001 (inherited from Bash)
rem cmd trips over the UTF-8 rem lines of run_g4cf.bat (P20 trap, code 255).
chcp 1251 >nul
call "C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat" %*
