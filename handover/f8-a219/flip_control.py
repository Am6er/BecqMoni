# Положительный контроль A219: 'old' возвращает старую печать двери (голый знак сторожа у
# петли, ничего у уже названного звена), 'new' возвращает правку. Правит ТОЛЬКО тела двух
# ветвей Visit; сам помощник Above остаётся.
import io, sys
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\GlobalConfigManager.cs'
raw = io.open(p, 'rb').read(); bom = raw[:3] == b'\xef\xbb\xbf'
s = raw.decode('utf-8-sig'); nl = '\r\n'
new_a = "                        if (mark.Length > 0)\r\n                        {\r\n                            this.Above(mark);\r\n                        }\r\n                        else\r\n                        {\r\n                            this.Guard();\r\n                        }\r\n                        return;\r\n"
old_a = "                        this.Guard(); // КОНТРОЛЬ A219: старая печать\r\n                        return;\r\n"
new_b = "                else if (mark.Length > 0)\r\n                {\r\n                    // Текст ветви уже стоит выше (`A129`: второй раз не\r\n                    // называть) — но ветвь обязана быть видна (`A219`).\r\n                    this.Above(mark);\r\n                }\r\n"
old_b = "                // КОНТРОЛЬ A219: старая печать — у названного звена ничего\r\n"
mode = sys.argv[1]
if mode == 'old':
    assert s.count(new_a) == 1 and s.count(new_b) == 1
    s = s.replace(new_a, old_a).replace(new_b, old_b)
else:
    assert s.count(old_a) == 1 and s.count(old_b) == 1
    s = s.replace(old_a, new_a).replace(old_b, new_b)
io.open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + s.encode('utf-8'))
print('flip', mode, 'ok')
