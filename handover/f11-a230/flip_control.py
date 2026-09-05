# Положительный контроль A230: 'old' возвращает старую печать двери у предела (голый
# знак сторожа и на ветви узла), 'new' возвращает правку. Правит ТОЛЬКО вызов в ветви
# предела Visit; помощник Limit остаётся.
import io, sys
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\GlobalConfigManager.cs'
raw = io.open(p, 'rb').read(); bom = raw[:3] == b'\xef\xbb\xbf'
s = raw.decode('utf-8-sig')
new_a = "                    this.Limit(mark);\r\n"
old_a = "                    this.Guard(); // КОНТРОЛЬ A230: старая печать\r\n"
mode = sys.argv[1]
if mode == 'old':
    assert s.count(new_a) == 1
    s = s.replace(new_a, old_a)
else:
    assert s.count(old_a) == 1
    s = s.replace(old_a, new_a)
io.open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + s.encode('utf-8'))
print('flip', mode, 'ok')
