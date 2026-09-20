# -*- coding: utf-8 -*-
r"""П85: дописать в шапку tools/g4cf/run_g4cf.bat ключ `corr`. Переводы строк рабочей копии сохраняются."""
import io
p = 'tools/g4cf/run_g4cf.bat'
t = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if t.count('\r\n') == t.count('\n') and t.count('\n') > 0 else '\n'
old = ("rem    Ниже — предупреждение на случай, когда его забыли (это и есть читатель\n"
       "rem    признака: раньше признак был, а потребителя у него не было).\n").replace('\n', nl)
new = old + ("rem\n"
             "rem Ключ `corr` (П85, `AMBER42`, 15.09.2026) — угловые γ–γ корреляции каскада в\n"
             "rem    RDM (`G4DeexPrecoParameters::SetCorrelatedGamma(true)` до `/run/initialize`);\n"
             "rem    без ключа арбитр изотропен, как и был. Ставится там же, где `vacuum`, в\n"
             "rem    любом порядке: run_g4cf.bat corr scene <файл> ion <Z> <A> <N> <окна…>.\n"
             "rem    Читатель: строка `SETUP correlatedGamma=1` в stdout и сводка RDM\n"
             "rem    «Enable correlated gamma emission 1». Шапка g4cf.cc — что именно читает флаг.\n").replace('\n', nl)
if old not in t:
    raise SystemExit('фрагмент шапки не нашёлся')
t = t.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
