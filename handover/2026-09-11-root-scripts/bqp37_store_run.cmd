@echo off
rem П37 13.09.2026 — единый счёт склада ФИЗИКИ 17 (45 сцен): умолчания класса (семь ключей ВКЛ),
rem рецепт склада --threads=10 --target=0 (плоский счёт 3 млн историй, 140 узлов), --force.
rem Отсоединённый процесс (Start-Process cmd /c), путь без пробелов — как у П20 (store_a17_run.cmd).
rem Склад — КОПИЯ в worktree bqp37; живой склад tools\CORPUS\corpus\geometries основного дерева не читается и не пишется.
cd /d C:\Users\moroz\bqp37\tools\effmaker\probes\build_p37
echo start %DATE% %TIME%
C:\Users\moroz\bqp37\tools\effmaker\probes\build_p37\CorpusMatrixProbe.exe --dir=C:\Users\moroz\bqp37\tools\CORPUS\corpus\geometries --threads=10 --target=0 --force
echo exit code %ERRORLEVEL%
echo end %DATE% %TIME%
