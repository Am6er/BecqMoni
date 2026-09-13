# -*- coding: utf-8 -*-
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8')
GOOD = r"'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'"
PAT = re.compile(r"'C:\\Program Files\\Microsoft Visual Studio[^']*MSBuild\.exe'")
for p in [r'C:\Users\moroz\bqp16_build.ps1',
          r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p16-light-anchor\bqp16_build.ps1']:
    s = io.open(p, encoding='utf-8').read()
    s2, n = PAT.subn(lambda m: GOOD, s)
    io.open(p, 'w', encoding='utf-8').write(s2)
    i = s2.find('MSBuild.exe')
    print(n, repr(s2[i - 88:i + 12]))
