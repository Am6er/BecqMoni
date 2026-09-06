# -*- coding: utf-8 -*-
"""F72. Сверка двух переписей: F70 (по СЛОВУ в файле) и F72 (по ХВОСТУ цепочки)."""
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scan_unknown_key as S   # он печатает свою перепись; берём только rows

ROOT = S.ROOT
DENY_WORDS = ['неизвестн', 'не знаю ключа', 'не знаю аргумента', 'unknown', 'неопознан', 'лишний ключ']

f70_silent, f70_deny = set(), set()
for r in S.rows:
    p = os.path.join(ROOT, r['path'])
    low = io.open(p, encoding='utf-8-sig', errors='replace').read().lower()
    (f70_deny if any(w in low for w in DENY_WORDS) else f70_silent).add(r['path'])

f72_silent = set(r['path'] for r in S.rows if not r['refuses'])
f72_deny = set(r['path'] for r in S.rows if r['refuses'])

print()
print("=== СВЕРКА ===")
print("F70 (по слову):  отказ %d, молча %d" % (len(f70_deny), len(f70_silent)))
print("F72 (по хвосту): отказ %d, молча %d" % (len(f72_deny), len(f72_silent)))
print()
print("F70 счёл ОТКАЗОМ, F72 — МОЛЧАНИЕМ (слово есть, ветки нет):")
for p in sorted(f70_deny & f72_silent):
    print("   " + p)
print()
print("F70 счёл МОЛЧАНИЕМ, F72 — ОТКАЗОМ (ветка есть, слова нет):")
for p in sorted(f70_silent & f72_deny):
    print("   " + p)
