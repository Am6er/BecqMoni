import sys
m = sys.argv[1]
if m == 'utf8': sys.stdout.reconfigure(encoding='utf-8', errors='replace')
elif m == 'replace': sys.stdout.reconfigure(errors='replace')
print('enc=%s | привет ⛔ σ χ² → конец' % sys.stdout.encoding)
sys.exit(3)
