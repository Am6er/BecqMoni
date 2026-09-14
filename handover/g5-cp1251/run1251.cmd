@echo off
chcp 1251 >nul
set PYTHONIOENCODING=
set PYTHONUTF8=
cd /d "C:\Users\moroz\source\repos\BQ Eng res .NET 4.8"
python %*
exit /b %errorlevel%
