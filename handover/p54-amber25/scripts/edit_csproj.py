# -*- coding: utf-8 -*-
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\BecquerelMonitor.csproj'
b = open(p, 'rb').read()
old = b'    <Compile Include="EfficiencyMaker\\EfficiencyFitter.cs" />\r\n'
new = b'    <Compile Include="EfficiencyMaker\\EfficiencyCurveIo.cs" />\r\n'
assert b.count(old) == 1, b.count(old)
b = b.replace(old, new)
open(p, 'wb').write(b)
print('csproj ok')
