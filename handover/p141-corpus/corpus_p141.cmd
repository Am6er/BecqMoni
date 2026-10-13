@echo off
rem P141 23.09.2026 -- corpus run on `master`: PRICE of yesterday's FSA edits on the rev32 base.
rem Amber 23.09.2026 (console): "Zapuskay raschet korpusa seychas, no ne sledi za nim".
rem Amber 23.09.2026 (questionnaire): "Na `master` -- tsena vcherashnih pravok FSA na baze rev32".
rem REHEARSAL OF THE PRICE, not a re-declaration of the base: live store and out_rev32_* are read only.
rem Chain: every next step runs ONLY when the previous returned 0. Each step: own log + own *_done.txt.
rem Codes of *_done.txt: see tools\CORPUS\scripts\detached_run.ps1 -Decode <code>.
rem PATH is set EXPLICITLY and %PATH% is NOT appended: the user PATH carries a stray double quote
rem ("...\170\DTS\Binn";...) and cmd finds no program listed after it -- pwsh among them
rem (measured by P140 23.09.2026, handover\p140-night\env_path.log). pwsh is called by full path.
setlocal
set PATH=C:\Users\moroz\AppData\Local\Python\pythoncore-3.14-64;C:\Program Files\PowerShell\7;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0;C:\Program Files\Git\cmd
set PYTHONIOENCODING=utf-8
set PWSH="C:\Program Files\PowerShell\7\pwsh.exe"
set ARM=D:\BqMoni_Claude\p141\corpus_arm.ps1
set LOGS=D:\BqMoni_Claude\p141\logs
cd /d D:\BqMoni_Claude\p141
echo start %date% %time% > %LOGS%\_start.txt

rem ===== step 1: run workdir wd_p141 (mk_appwd + check_appwd), LIVE corpus store =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage wd; exit $LASTEXITCODE" >> %LOGS%\wd.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\1_wd_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 2: corpus run, SMALL base (mini.csv) =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage mini; exit $LASTEXITCODE" >> %LOGS%\mini.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\2_mini_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 3: corpus run, FULL corpus =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage full; exit $LASTEXITCODE" >> %LOGS%\full.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\3_full_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 4: summary against the DECLARED rev32 numbers + ladder rev32 -> p141 =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage cmp; exit $LASTEXITCODE" >> %LOGS%\cmp.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\4_cmp_done.txt
if not "%RC%"=="0" goto fail

echo ALL OK %date% %time% > %LOGS%\ALL_DONE.txt
goto end
:fail
echo CHAIN STOPPED at RC=%RC% %date% %time% > %LOGS%\CHAIN_FAILED.txt
:end
echo finish %date% %time% > %LOGS%\_finish.txt