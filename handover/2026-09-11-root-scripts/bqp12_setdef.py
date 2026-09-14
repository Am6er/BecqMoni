# -*- coding: utf-8 -*-
# setdef.py <transfer 0|1> <newmx 0|1>  — выставить три умолчания во ВРЕМЕННОЙ копии C:\Users\moroz\bqp12
import io, sys, re
root = r'C:\Users\moroz\bqp12'
tr = sys.argv[1] == '1'; nm = sys.argv[2] == '1'
def sub(path, pat, repl):
    s = io.open(path, encoding='utf-8-sig', newline='').read()
    n = len(re.findall(pat, s, flags=re.M))
    assert n == 1, (path, pat, n)
    s = re.sub(pat, repl, s, flags=re.M)
    io.open(path, 'w', encoding='utf-8-sig', newline='').write(s)
sub(root + r'\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs',
    r'^            this\.MatrixTransferByChannel = (true|false);',
    '            this.MatrixTransferByChannel = %s;' % ('true' if tr else 'false'))
sub(root + r'\BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs',
    r'^        public bool PeakToleranceHalfBin( = true)?;',
    '        public bool PeakToleranceHalfBin%s;' % (' = true' if nm else ''))
sub(root + r'\BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs',
    r'^        public bool SplitXrayShells( = true)?;',
    '        public bool SplitXrayShells%s;' % (' = true' if nm else ''))
print('transfer=%s newmx=%s' % (tr, nm))
