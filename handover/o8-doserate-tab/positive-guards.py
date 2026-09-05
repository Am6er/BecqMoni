# -*- coding: utf-8 -*-
u"""Положительный контроль четырёх сторожей resx НА КОПИИ дерева.

Каждому подсовывается ровно один заведомо плохой вход; сторож обязан ОТКАЗАТЬ
и назвать имя. Затем та же копия БЕЗ порчи — там обязана быть тишина.
"""
from __future__ import print_function
import io
import os
import shutil
import subprocess
import sys

REPO = u'C:/Users/moroz/source/repos/BQ Eng res .NET 4.8'
BASE = os.path.dirname(os.path.abspath(__file__))
SAB = os.path.join(BASE, u'sab')

FILES = [u'DeviceConfigForm.resx', u'DeviceConfigForm.ru.resx',
         u'DeviceConfigForm.Designer.cs', u'DeviceConfigForm.cs']


def fresh(tag):
    root = os.path.join(SAB, tag)
    if os.path.exists(root):
        shutil.rmtree(root)
    os.makedirs(os.path.join(root, u'Properties'))
    for f in FILES:
        shutil.copy2(os.path.join(REPO, u'BecquerelMonitor', f), os.path.join(root, f))
    shutil.copy2(os.path.join(REPO, u'BecquerelMonitor', u'Properties', u'Resources.resx'),
                 os.path.join(root, u'Properties', u'Resources.resx'))
    return root


def edit(path, old, new):
    s = io.open(path, encoding='utf-8-sig', newline='').read()
    if s.count(old) != 1:
        raise SystemExit(u'ОТКАЗ порчи: «%s» встречается %d раз' % (old[:40], s.count(old)))
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as f:
        f.write(s.replace(old, new))


def run(script, argv):
    env = dict(os.environ)
    env['PYTHONIOENCODING'] = 'utf-8'
    p = subprocess.Popen([sys.executable, os.path.join(REPO, u'tools', script)] + argv,
                         cwd=REPO, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
    out = p.communicate()[0].decode('utf-8', 'replace')
    return p.returncode, out


def report(title, code, out, needle):
    hit = [l for l in out.splitlines() if needle in l]
    print(u'  %-28s код %d  %s' % (title, code, u'НАЗВАЛ: ' + hit[0].strip() if hit else u'— ничего не назвал'))
    return bool(hit)


ok = True

# ------------------------------------------------------------- check_resx.py
print(u'== check_resx.py: русский перевод снят ==')
root = fresh(u'resx')
edit(os.path.join(root, u'DeviceConfigForm.ru.resx'),
     u'''  <data name="labelEffNote.Text" xml:space="preserve">\r
    <value>*важна только форма кривой; диапазон берётся у шкалы прибора</value>\r
  </data>\r
''', u'')
code, out = run(u'check_resx.py', [root, u'--no-format', u'--list'])
ok &= report(u'порча: ключа нет', code, out, u'labelEffNote')

root = fresh(u'resx_empty')
edit(os.path.join(root, u'DeviceConfigForm.ru.resx'),
     u'<value>*важна только форма кривой; диапазон берётся у шкалы прибора</value>',
     u'<value></value>')
code, out = run(u'check_resx.py', [root, u'--no-format', u'--list'])
report(u'порча: значение пусто', code, out, u'labelEffNote')
root = fresh(u'resx_clean')
code, out = run(u'check_resx.py', [root, u'--no-format'])
print(u'  %-28s код %d  %s' % (u'без порчи', code,
                               u'тишина' if code == 0 else u'ЛОЖНАЯ ТРЕВОГА'))
ok &= (code == 0)

# ----------------------------------------------------- check_resx_letters.py
print(u'== check_resx_letters.py: кириллическая С в английской подписи ==')
root = fresh(u'letters')
edit(os.path.join(root, u'DeviceConfigForm.resx'),
     u'<value>Curve from a file*</value>',
     u'<value>\u0421urve from a file*</value>')
code, out = run(u'check_resx_letters.py', [root, u'--no-format', u'--list'])
ok &= report(u'порча', code, out, u'buttonLoadEff')
root = fresh(u'letters_clean')
code, out = run(u'check_resx_letters.py', [root, u'--no-format'])
print(u'  %-28s код %d  %s' % (u'без порчи', code,
                               u'тишина' if code == 0 else u'ЛОЖНАЯ ТРЕВОГА'))
ok &= (code == 0)

# ---------------------------------------------------- check_resx_designer.py
print(u'== check_resx_designer.py: обращение к ключу, которого нет ==')
root = fresh(u'designer')
edit(os.path.join(root, u'DeviceConfigForm.cs'),
     u'        void BuildDoseRateTab()\n        {\n',
     u'        void BuildDoseRateTab()\n        {\n'
     u'            string sab = Resources.ResourceManager.GetString("O8SabotageNoSuchKey");\n')
code, out = run(u'check_resx_designer.py', [root, u'--no-format', u'--list'])
ok &= report(u'порча', code, out, u'O8SabotageNoSuchKey')
root = fresh(u'designer_clean')
code, out = run(u'check_resx_designer.py', [root, u'--no-format'])
print(u'  %-28s код %d  %s' % (u'без порчи', code,
                               u'тишина' if code == 0 else u'ЕСТЬ НАХОДКИ (см. вывод)'))

# ------------------------------------------------------ check_resx_zorder.py
print(u'== check_resx_zorder.py: у списка снята метаданная ZOrder ==')
root = fresh(u'zorder')
edit(os.path.join(root, u'DeviceConfigForm.resx'),
     u'''  <data name="&gt;&gt;comboDoseRateSpectrum.ZOrder" xml:space="preserve">\r
    <value>3</value>\r
  </data>\r
''', u'')
code, out = run(u'check_resx_zorder.py', [u'--root', root, u'--form', u'DeviceConfigForm', u'--no-format'])
ok &= report(u'порча', code, out, u'comboDoseRateSpectrum')
root = fresh(u'zorder_clean')
code, out = run(u'check_resx_zorder.py', [u'--root', root, u'--form', u'DeviceConfigForm', u'--no-format'])
print(u'  %-28s код %d  %s' % (u'без порчи', code,
                               u'тишина' if code == 0 else u'ЛОЖНАЯ ТРЕВОГА'))
ok &= (code == 0)

print(u'\nПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: ' + (u'все сторожа смотрят' if ok else u'ЕСТЬ СЛЕПОЙ'))
sys.exit(0 if ok else 1)
