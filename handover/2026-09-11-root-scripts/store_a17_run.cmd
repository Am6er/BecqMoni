@echo off
cd /d "C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_rel_store17"
echo start %DATE% %TIME%
"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_rel_store17\CorpusMatrixProbe.exe" --dir=C:\Users\moroz\store_a17 --threads=10 --target=0 --peakb=1 --xrkl=1 --kdip=1
echo exit code %ERRORLEVEL%
echo end %DATE% %TIME%
