# -*- coding: utf-8 -*-
# Правка одной строки run_amber.ps1: путь к матрице с обратными слэшами (через heredoc они не доезжают).
import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p26-amber22\amber\run_amber.ps1'
lines = io.open(p, encoding='utf-8', newline='').read().split('\n')
good = r'    $rmx = "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx"'
n = 0
for i, l in enumerate(lines):
    if l.strip().startswith('$rmx = '):
        lines[i] = good
        n += 1
io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
print('заменено строк:', n)
