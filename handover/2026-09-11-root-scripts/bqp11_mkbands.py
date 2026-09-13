# -*- coding: utf-8 -*-
# П11: сделать bqp11_bands.py из bqp9_bands.py — корень и метрика аргументами
import io
s = io.open(r'C:\Users\moroz\bqp9_bands.py', encoding='utf-8').read()
old_head = "# Разложение χ²/ndf по энергетическим полосам для четырёх плеч (П9 11.09.2026)"
new_head = ("# Разложение χ²/ndf по энергетическим полосам для четырёх плеч (П9 11.09.2026;\n"
            "# копия П11: корень выходов — вторым аргументом)\n"
            "# python bqp11_bands.py <sol|rep> [<корень, умолч. C:\\Users\\moroz\\bqp11_out>]")
assert s.count(old_head) == 1
s = s.replace(old_head, new_head)
old_root = "root = r'C:\\Users\\moroz\\bqp9_out'"
new_root = "root = sys.argv[2] if len(sys.argv) > 2 else r'C:\\Users\\moroz\\bqp11_out'"
assert s.count(old_root) == 1, s[:400]
s = s.replace(old_root, new_root)
io.open(r'C:\Users\moroz\bqp11_bands.py', 'w', encoding='utf-8').write(s)
print('ok')
