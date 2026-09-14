# П55: обрезанные логи арбитра (P55/RESULT/HIST/пороги), CSV и txt нашей стороны, счётчики — в handover/p55-a72/.
import io, os, re, shutil
src = r'D:\BqMoni_Claude\p55'
dst = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p55-a72'
keep = re.compile(r'^(P55 flags|RESULT|HIST| Material : scene| Energy thresholds)')
os.makedirs(os.path.join(dst, 'g4'), exist_ok=True)
n = 0
for f in sorted(os.listdir(os.path.join(src, 'g4out'))):
    if not f.endswith('.log'):
        continue
    lines = [l for l in io.open(os.path.join(src, 'g4out', f), encoding='utf-8', errors='replace') if keep.match(l)]
    # пороги печатаются каждым потоком — оставить первые две строки Material/Energy
    out, seen = [], 0
    for l in lines:
        if l.startswith(' Material') or l.startswith(' Energy'):
            if seen >= 2:
                continue
            seen += 1
        out.append(l)
    io.open(os.path.join(dst, 'g4', f), 'w', encoding='utf-8', newline='').writelines(out)
    n += 1
shutil.copy(os.path.join(src, 'g4out', 'codes.txt'), os.path.join(dst, 'g4', 'codes.txt'))
for extra in ('time_rc103.log', 'tree_rc103.log', 'smoke_nodelta.log', 'smoke_lowcut.log'):
    p = os.path.join(src, 'g4', extra)
    if os.path.exists(p):
        lines = [l for l in io.open(p, encoding='utf-8', errors='replace') if keep.match(l)]
        io.open(os.path.join(dst, 'g4', extra), 'w', encoding='utf-8', newline='').writelines(lines[:2000])
os.makedirs(os.path.join(dst, 'ours'), exist_ok=True)
m = 0
for f in sorted(os.listdir(os.path.join(src, 'ours'))):
    if re.match(r'ours_[A-Za-z0-9_]+_[\d.]+_(ref|var40posend[01]|varposend[01])\.(csv|txt)$', f) or f.startswith('codes_'):
        shutil.copy(os.path.join(src, 'ours', f), os.path.join(dst, 'ours', f))
        m += 1
os.makedirs(os.path.join(dst, 'cnt'), exist_ok=True)
k = 0
for f in sorted(os.listdir(os.path.join(src, 'cnt'))):
    if f.endswith('.txt'):
        shutil.copy(os.path.join(src, 'cnt', f), os.path.join(dst, 'cnt', f))
        k += 1
shutil.copy(os.path.join(src, 'codes.txt'), os.path.join(dst, 'codes_build.txt'))
print('g4 логов %d, наших файлов %d, счётчиков %d' % (n, m, k))
