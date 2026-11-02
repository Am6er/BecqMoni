# store30: пишет curves_run.cmd (ASCII, CRLF), обратные слэши через chr(92).
B = chr(92)
S = 'D:' + B + 'BqMoni_Claude' + B + 'store30'
def p(*parts):
    return B.join(parts)

# curves_run.cmd: кривые корпуса ПАРАЛЛЕЛЬНО складу на ЦП (низкий приоритет), каталог сцен без матриц —
# проба печатает «МАТРИЦЫ НЕТ» и кончает кодом 1 по этой причине; response склада кладёт mx_swap.py --into.
cl = [
    '@echo off',
    'rem store30: corpus efficiency curves (physics 30) on CPU, below-normal priority, in parallel with the GPU store count.',
    'rem The scene dir has no matrices on purpose: the probe prints MATRIX MISSING and exits 1 for that reason; store response is filled by mx_swap.py --into.',
    'set S=' + S,
    'set PR=' + p(S, 'wt', 'tools', 'effmaker', 'probes', 'build_s30'),
    'cd /d %S%',
    'start "" /b /wait /belownormal "%PR%' + B + 'CorpusEffProbe.exe" --dir=%S%' + B + 'curvesdir --spectra=' + p(S, 'wt', 'tools', 'CORPUS', 'corpus', 'spectra') + ' --n=400000 > %S%' + B + 'logs' + B + 'curves_par.log 2> %S%' + B + 'logs' + B + 'curves_par.err',
    'echo curves_par exit %ERRORLEVEL% %DATE% %TIME% > %S%' + B + 'logs' + B + 'curves_par_done.txt',
]
with open(S + B + 'curves_run.cmd', 'w', encoding='ascii', newline='\r\n') as f:
    f.write('\n'.join(cl) + '\n')
print('ok curves')
