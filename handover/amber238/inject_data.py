# -*- coding: utf-8 -*-
"""Заменить вшитые данные страницы docs/how-it-works.html (AMBER238).

Страница — единственный исходник: данные лежат в ней же, в
<script id="data" type="application/json">…</script>. Этот скрипт подменяет
только содержимое этого блока свежим data.json из extract_data.py.

Запуск: python -I inject_data.py <docs/how-it-works.html> <data.json>
"""
import io, re, sys

html_path, data_path = sys.argv[1], sys.argv[2]
html = io.open(html_path, encoding='utf-8').read()
data = io.open(data_path, encoding='utf-8').read().strip()
if '</' in data:
    sys.exit('в данных встретилось «</» — блок script оборвётся')
head = '<script id="data" type="application/json">'
pat = re.compile(re.escape(head) + r'.*?</script>', re.S)
if len(pat.findall(html)) != 1:
    sys.exit('блок данных не найден или найден не один')
html = pat.sub(lambda m: head + data + '</script>', html)
io.open(html_path, 'w', encoding='utf-8', newline='\n').write(html)
print('данные заменены:', len(data), 'символов')
