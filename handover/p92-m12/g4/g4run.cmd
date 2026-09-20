@echo off
rem P92: console code page 1251 before run_g4cf.bat (P20/P26 trap: 65001 from Bash -> code 255)
chcp 1251 >nul
call "D:\BqMoni_Claude\p92\g4\run_g4cf.bat" %*
