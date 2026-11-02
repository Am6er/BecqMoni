# store30: пишет store_run.cmd (ASCII, CRLF), обратные слэши через chr(92) — грабля heredoc/printf.
B = chr(92)
S = 'D:' + B + 'BqMoni_Claude' + B + 'store30'
def p(*parts):
    return B.join(parts)
lines = [
    '@echo off',
    'rem store30 (AMBER274, physics 30): photon dense x2 first, photon rest 3M without --force (stamp keeps the A77 pair, T36 keeps dense),',
    'rem then electron sections on GPU (sections.ps1), then corpus curves into the worktree spectra (the probe fills store response).',
    'set S=' + S,
    'set PR=' + p(S, 'wt', 'tools', 'effmaker', 'probes', 'build_s30'),
    'set DLL=' + p(S, 'wt', 'tools', 'effmaker', 'gpu', 'bin', 'rmgpu_f.dll'),
    'cd /d %S%',
    '"%PR%' + B + 'CorpusMatrixProbe.exe" --dir=%S%' + B + 'photon --gpu=%DLL% --target=0 --n=6000000 --only=RC103_point50,ASN16_point10_house,G1S_point25,G1S_point25_p24 > %S%' + B + 'logs' + B + 'photon_dense.log 2> %S%' + B + 'logs' + B + 'photon_dense.err',
    'echo dense exit %ERRORLEVEL% %DATE% %TIME% > %S%' + B + 'logs' + B + 'photon_dense_done.txt',
    '"%PR%' + B + 'CorpusMatrixProbe.exe" --dir=%S%' + B + 'photon --gpu=%DLL% --target=0 --n=3000000 > %S%' + B + 'logs' + B + 'photon_main.log 2> %S%' + B + 'logs' + B + 'photon_main.err',
    'echo main exit %ERRORLEVEL% %DATE% %TIME% > %S%' + B + 'logs' + B + 'photon_main_done.txt',
    '"C:' + B + 'Program Files' + B + 'PowerShell' + B + '7' + B + 'pwsh.exe" -NoProfile -File %S%' + B + 'scripts' + B + 'sections.ps1 > %S%' + B + 'logs' + B + 'sections.log 2> %S%' + B + 'logs' + B + 'sections.err',
    'echo sections exit %ERRORLEVEL% %DATE% %TIME% > %S%' + B + 'logs' + B + 'sections_done.txt',
    '"%PR%' + B + 'CorpusEffProbe.exe" --dir=%S%' + B + 'store --spectra=' + p(S, 'wt', 'tools', 'CORPUS', 'corpus', 'spectra') + ' --n=400000 > %S%' + B + 'logs' + B + 'curves.log 2> %S%' + B + 'logs' + B + 'curves.err',
    'echo curves exit %ERRORLEVEL% %DATE% %TIME% > %S%' + B + 'logs' + B + 'curves_done.txt',
]
with open(S.replace('D:', 'D:') + B + 'store_run.cmd', 'w', encoding='ascii', newline='\r\n') as f:
    f.write('\n'.join(lines) + '\n')
print('ok')

