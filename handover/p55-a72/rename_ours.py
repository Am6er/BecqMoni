# П55: починка имён файлов нашей стороны — run_ours.ps1 затирал параметр $Tag переменной $tag
# (PowerShell не различает регистр), имена росли цепочкой. Сцена — первый префикс, E — за ним,
# ключ — ПОСЛЕДНИЙ posendN в имени (порядок прогонов в скрипте), сборка — ref/var по подстроке.
import os, re, shutil
d = r'D:\BqMoni_Claude\p55\ours'
scenes = ['AS80_point0', 'AS80_th_disk', 'ASN16_lu_side', 'RC103_point0']
for f in sorted(os.listdir(d)):
    if not f.startswith('ours_') or not (f.endswith('.csv') or f.endswith('.txt')):
        continue
    body, ext = os.path.splitext(f[5:])
    scene = next((s for s in scenes if body.startswith(s + '_')), None)
    if scene is None:
        continue
    rest = body[len(scene) + 1:]
    m = re.match(r'([\d.]+)_', rest)
    if not m:
        continue
    e = m.group(1)
    tag = 'var' if '_var' in body else 'ref'
    keys = re.findall(r'posend\d', body)
    key = keys[-1] if keys else ''
    new = 'ours_%s_%s_%s%s%s' % (scene, e, tag, key, ext)
    if new != f:
        if os.path.exists(os.path.join(d, new)):
            print('уже есть', new, '— пропуск', f)
            continue
        shutil.move(os.path.join(d, f), os.path.join(d, new))
        print(f[:50], '->', new)
