# -*- coding: utf-8 -*-
def edit(p, pairs):
    b=open(p,'rb').read(); t=b.decode('utf-8'); n=0
    for old,new in pairs:
        c=t.count(old)
        assert c==1, (p, old[:70], c)
        t=t.replace(old,new); n+=1
    open(p,'wb').write(t.encode('utf-8')); print(p, n, 'replacements')
CR='\r\n'
W='D:/BqMoni_Claude/p87/wt/'
edit(W+'tools/CORPUS/scripts/appwd_plan.ps1', [
 ("        # (`N14`, П49 13.09.2026) Сайдкары угловых корреляций `<ключ>.qk` лежат"+CR+
  "        # В САМОМ складе (`geometries/`, в git — текст на килобайт) и едут в тот"+CR+
  "        # же `response` рабочего каталога: разбор ищет их там же, где матрицу"+CR+
  "        # (`FsaMatrixBinding`), по отпечатку геометрии, а не по имени файла."+CR+
  "        Get-ChildItem (Join-Path $storeDir '*.qk') -File -Force -ErrorAction SilentlyContinue | ForEach-Object {"+CR+
  "            $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $rspDir $_.Name); Why = 'угловые корреляции Q_k' })"+CR+
  "        }"+CR,
  "        # (`AMBER46`, П87 16.09.2026) Сайдкаров угловых корреляций `<ключ>.qk` БОЛЬШЕ"+CR+
  "        # НЕТ: Q_k(E) сцены лежат в самой матрице (блок формата 9), едут с `.rmx`"+CR+
  "        # и отдельного переноса не требуют. Постановка Amber 16.09.2026: «Никаких"+CR+
  "        # сайдкаров. Стоп.»"+CR),
])
edit(W+'tools/check_fsa_showcase.py', [
 ("    out = [(rmx, guid + u'.rmx')]"+CR+
  "    qk = os.path.join(base, key + u'.qk')"+CR+
  "    if os.path.isfile(qk):"+CR+
  "        out.append((qk, guid + u'.qk'))"+CR+
  "    return out, None",
  "    # (`AMBER46`, П87 16.09.2026) Сайдкаров `.qk` больше нет: Q_k(E) сцены лежат в"+CR+
  "    # самой матрице (блок формата 9) и едут с `.rmx`."+CR+
  "    return [(rmx, guid + u'.rmx')], None"),
 ("    sync_dir_exact(response, os.path.join(cfg, u'device', u'response'), (u'.rmx', u'.qk'))",
  "    sync_dir_exact(response, os.path.join(cfg, u'device', u'response'), (u'.rmx',))"),
])
