@echo off
rem P140 -- positive control of the DETACHED environment with the FIXED PATH block of night_rev33.cmd.
setlocal
set PATH=C:\Users\moroz\AppData\Local\Python\pythoncore-3.14-64;C:\Program Files\PowerShell\7;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0;C:\Program Files\Git\cmd
set PYTHONIOENCODING=utf-8
set PWSH="C:\Program Files\PowerShell\7\pwsh.exe"
set LOGS=D:\BqMoni_Claude\p140\logs
cd /d D:\BqMoni_Claude\p122\wt\tools\effmaker\probes\build_p140
echo ==== python by name ==== > %LOGS%\env_check.log
python -c "import sys;print(sys.executable)" >> %LOGS%\env_check.log 2>&1
set RC=%errorlevel%
echo python RC=%RC% >> %LOGS%\env_check.log
echo ==== pwsh by name ==== >> %LOGS%\env_check.log
pwsh -NoProfile -Command "exit 7" >> %LOGS%\env_check.log 2>&1
echo pwsh-by-name RC=%errorlevel% (expect 7) >> %LOGS%\env_check.log
echo ==== pwsh full path + corpus_arm quoting, expect rc=2 ==== >> %LOGS%\env_check.log
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p140\corpus_arm.ps1' -Stage selfcheck" >> %LOGS%\env_check.log 2>&1
set RC=%errorlevel%
echo pwsh-full RC=%RC% (expect 2) >> %LOGS%\env_check.log
echo ==== git ==== >> %LOGS%\env_check.log
git --version >> %LOGS%\env_check.log 2>&1
echo git RC=%errorlevel% >> %LOGS%\env_check.log
echo ==== score.py --help (python + tools/pie reachable) ==== >> %LOGS%\env_check.log
python D:\BqMoni_Claude\p122\wt\tools\pie\score.py --help > nul 2>&1
echo score RC=%errorlevel% >> %LOGS%\env_check.log
echo exit 0 %date% %time% > %LOGS%\env_check_done.txt