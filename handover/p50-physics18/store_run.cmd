@echo off
rem П50 13.09.2026 — единый счёт склада ФИЗИКИ 18 (45 сцен): умолчания класса (ecomp=1, bpath=2 поверх
rem семи ключей физики 17), рецепт склада --threads=10 --target=0 (плоский счёт 3 млн историй, 140 узлов),
rem --force (пересчитает и две сцены контроля (б) — их клейма и тела ОБЯЗАНЫ совпасть: бесплатный
rem контроль детерминированности внутри прогона, как у П37 §4.1).
rem Отсоединённый процесс (Start-Process cmd /c), путь без пробелов — как у П20/П37.
rem Склад — КОПИЯ в worktree p50; живой склад tools\CORPUS\corpus\geometries основного дерева не читается и не пишется.
cd /d D:\BqMoni_Claude\p50\wt\tools\effmaker\probes\build_p50
echo start %DATE% %TIME%
D:\BqMoni_Claude\p50\wt\tools\effmaker\probes\build_p50\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p50\wt\tools\CORPUS\corpus\geometries --threads=10 --target=0 --force
echo exit code %ERRORLEVEL%
echo end %DATE% %TIME%
