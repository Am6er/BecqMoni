import io, re
p = 'BecquerelMonitor/GlobalConfigForm.cs'
src = io.open(p, encoding='utf-8-sig', newline='').read()
orig = src

src = src.replace(
    'using BecquerelMonitor.Properties;\r\nusing System;\r\nusing System.Media;\r\n',
    'using BecquerelMonitor.Properties;\r\nusing System;\r\nusing System.Globalization;\r\nusing System.Media;\r\n', 1)

# 16 печатей в текстовые поля формы
n = 0
def rep(m):
    global n
    n += 1
    return m.group(1) + '.ToString(CultureInfo.InvariantCulture);'

src, cnt = re.subn(
    r'(this\.(?:doubleTextBox[0-9]+|integerTextBox[0-9]+)\.Text = [A-Za-z0-9_.\[\]]+)\.ToString\(\);',
    rep, src)

io.open(p, 'w', encoding='utf-8-sig', newline='').write(src)
print('замен ToString:', cnt, 'using добавлен:', 'System.Globalization' in src)
