# П35, 12.09.2026, побочно по решению Amber по S154: пометка о строках `G 511.000` у β⁺-родителей
# в описании `decay_radiations` (database/scheme.md). LF и BOM сохраняются; база не трогается.
import io, sys
sys.stdout.reconfigure(encoding='utf-8')
PATH = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\database\scheme.md'
OLD = """дублированы строкой и числом: строка хранит запись оригинала с
неопределённостью, число — то, чем считают.

#### ⛔ Ловушка `D35`"""
NEW = """дублированы строкой и числом: строка хранит запись оригинала с
неопределённостью, число — то, чем считают.

⚠ **Аннигиляция у β⁺-родителей (`S154`, решение Amber 12.09.2026).** Норма
поставки: аннигиляционное излучение ГАММОЙ (`G 511.000`) не пишется — у `22NA`,
`65ZN`, `58CO` такой строки нет, — и код складывает `2·B⁺` сам; шесть
строк-исключений, где аннигиляция записана гаммой (перехода 510…512 кэВ в
наборе распада того же родителя в `schemedb.ensdf_gammas` нет) — `110SB`
`dr_pk` 5574, `150TB` 36575, `205PO` 1561, `205AT` 43859, `198TL` 34552, `203BI`
51123 — удаляет Amber своим `--apply`; у `191HGm` строка `G 511.000` (`dr_pk`
62867) НЕ СУДИТСЯ — набора распада в `schemedb.ensdf_gammas` нет — и остаётся
как есть. Разбор — `handover/handover-2026-09-12-p30-cleaner-s.md` §8.

#### ⛔ Ловушка `D35`"""
with io.open(PATH, encoding='utf-8-sig', newline='') as f:
    text = f.read()
n = text.count(OLD)
if n != 1:
    raise SystemExit('образец встречается %d раз' % n)
text = text.replace(OLD, NEW)
with io.open(PATH, 'w', encoding='utf-8-sig', newline='') as f:
    f.write(text)
print('исправлен', PATH)
