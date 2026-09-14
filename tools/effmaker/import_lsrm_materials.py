# -*- coding: utf-8 -*-
u"""Втянуть таблицу веществ ЛСРМ в засев библиотеки геометрий (E20).

ЧТО ЗА ФАЙЛ. `C:\\LSRM\\NuclideMaster\\GeometryMaster\\materials.dat` —
поставочная таблица веществ их же конструктора геометрий (GMaster.exe, 2008 г.):
287 строк, в каждой 45 полей через `;` —

    имя (40 знаков) ; формула ; синоним ; плотность ; 20 пар «Z ; массовая доля»

Файл нашла Amber 16.08.2026, и он ОПРОВЕРГАЕТ премису строки `E20`
(«файла веществ в поставке ЛСРМ нет, срисовать их редактор не с чего»).

ЧТО БЕРЁТСЯ. Имя, плотность и МАССОВЫЕ ДОЛИ. Формула из файла НЕ берётся как
источник состава, только как подпись: записана она там для человека —
полимеры стоят как `(C2F4)n`, а у воды прямая опечатка `H20`, которую наш
разбор формулы прочёл бы как двадцать водородов. Доли — величины NIST, и
сходятся к единице у всех 287 строк (проверено).

ЧТО НЕ БЕРЁТСЯ.
  * Вещества, которые в засеве УЖЕ ЕСТЬ под тем же именем: у наших 29 состав и
    вид выверены руками, а три плотности отличаются НАРОЧНО — `SiO2` 1.6 и
    `CaCO3` 1.5 у нас НАСЫПНЫЕ (проба, `M5`), а не монолитные 2.32 и 2.8;
    `CdTe` 5.85 против 6.2. Затирать их таблицей нельзя.
  * `Lanthanum dioxysulfide` — у ЛСРМ плотность 0.000e+00, то есть в их же
    файле дыра. Вещество с нулевой плотностью редактор не примет, и заводить
    его, выдумав число, нельзя.

ВИД («куда годится») у ввезённых — `Other`: в файле ЛСРМ его нет, а разложить
287 веществ по нашим пяти видам можно только угадыванием. В списках редактора
они идут после веществ своего вида, за разделителем; назначить вид — одно
движение в редакторе веществ.

    python tools/effmaker/import_lsrm_materials.py [--dat=<путь>] [--apply]

⛔ `--apply` ВЫКЛЮЧЕН по умолчанию: без него печатается только сводка.
С ним переписывается `BecquerelMonitor/EfficiencyMaker/GeometryMaterialSeed.cs`
— файл СГЕНЕРИРОВАННЫЙ, руками не правится.
"""
import argparse
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
OUT = os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker', 'GeometryMaterialSeed.cs')
DAT = r'C:\LSRM\NuclideMaster\GeometryMaster\materials.dat'

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def read_dat(path):
    rows = []
    raw = io.open(path, encoding='cp1251', errors='replace').read()
    for line in raw.split('\n'):
        if not line.strip():
            continue
        f = [x.strip() for x in line.split(';')]
        if len(f) < 44:
            raise ValueError(u'строка короче 44 полей: %r' % line[:60])
        fractions = {}
        for i in range(4, 44, 2):
            z, w = int(f[i]), float(f[i + 1])
            if z > 0 and w > 0.0:
                fractions[z] = fractions.get(z, 0.0) + w
        rows.append(dict(name=f[0], formula=f[1], synonym=f[2],
                         density=float(f[3]), fractions=fractions))
    return rows


def seeded_names():
    u"""Имена веществ вшитого списка — их таблица не трогает."""
    import re
    src = io.open(os.path.join(REPO, 'BecquerelMonitor', 'EfficiencyMaker',
                               'GeometryMaterialLibrary.cs'), encoding='utf-8').read()
    return set(re.findall(r'add\("[^"]*",\s*"([^"]+)"', src))


def cs_string(text):
    return '"' + text.replace('\\', '\\\\').replace('"', '\\"') + '"'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dat', default=DAT)
    ap.add_argument('--apply', action='store_true')
    args = ap.parse_args()

    rows = read_dat(args.dat)
    have = seeded_names()

    taken, skipped_known, skipped_bad = [], [], []
    for r in rows:
        if r['name'] in have:
            skipped_known.append(r['name'])
        elif not (r['density'] > 0.0) or not r['fractions']:
            skipped_bad.append((r['name'], r['density'], len(r['fractions'])))
        else:
            taken.append(r)

    print(u'в файле ЛСРМ         : %d' % len(rows))
    print(u'уже в засеве по имени: %d (%s)'
          % (len(skipped_known), ', '.join(sorted(skipped_known)[:4]) + u', …'))
    print(u'негодных             : %d' % len(skipped_bad))
    for name, d, n in skipped_bad:
        print(u'   %-32s ρ=%g, элементов %d' % (name, d, n))
    print(u'ВВОЗИТСЯ             : %d' % len(taken))

    lines = []
    add = lines.append
    add(u'// СГЕНЕРИРОВАННЫЙ ФАЙЛ. Руками не правится.')
    add(u'// Источник: LSRM GeometryMaster, materials.dat (2008), %d веществ.' % len(rows))
    add(u'// Генератор: tools/effmaker/import_lsrm_materials.py --apply')
    add(u'//')
    add(u'// Состав задан МАССОВЫМИ ДОЛЯМИ из того же файла, а не формулой:')
    add(u'// формула там записана для человека ((C2F4)n, и опечатка H20 у воды),')
    add(u'// а доли — величины NIST и сходятся к единице у всех строк.')
    add(u'using System.Collections.Generic;')
    add(u'')
    add(u'namespace BecquerelMonitor.EfficiencyMaker')
    add(u'{')
    add(u'    /// <summary>Вещества конструктора геометрий ЛСРМ (E20, ввоз 16.08.2026).</summary>')
    add(u'    public static class GeometryMaterialSeed')
    add(u'    {')
    add(u'        /// <summary>Строка таблицы: имя, формула-подпись, плотность, «Z:доля …».</summary>')
    add(u'        public static readonly string[][] Rows =')
    add(u'        {')
    for r in sorted(taken, key=lambda x: x['name']):
        parts = ' '.join('%d:%.6g' % (z, r['fractions'][z]) for z in sorted(r['fractions']))
        formula = '' if r['formula'] in ('-', '') else r['formula']
        add(u'            new[] { %s, %s, "%.6g", %s },'
            % (cs_string(r['name']), cs_string(formula), r['density'], cs_string(parts)))
    add(u'        };')
    add(u'')
    add(u'        /// <summary>Разобрать «1:0.1119 8:0.8881» в Z -> доля.</summary>')
    add(u'        public static Dictionary<int, double> Fractions(string packed)')
    add(u'        {')
    add(u'            Dictionary<int, double> result = new Dictionary<int, double>();')
    add(u'            if (string.IsNullOrEmpty(packed))')
    add(u'            {')
    add(u'                return result;')
    add(u'            }')
    add(u'')
    add(u'            foreach (string part in packed.Split(\' \'))')
    add(u'            {')
    add(u'                int colon = part.IndexOf(\':\');')
    add(u'                int z;')
    add(u'                double w;')
    add(u'                if (colon > 0')
    add(u'                    && int.TryParse(part.Substring(0, colon), System.Globalization.NumberStyles.Integer,')
    add(u'                                    System.Globalization.CultureInfo.InvariantCulture, out z)')
    add(u'                    && double.TryParse(part.Substring(colon + 1), System.Globalization.NumberStyles.Float,')
    add(u'                                       System.Globalization.CultureInfo.InvariantCulture, out w)')
    add(u'                    && z > 0 && w > 0.0)')
    add(u'                {')
    add(u'                    result[z] = w;')
    add(u'                }')
    add(u'            }')
    add(u'')
    add(u'            return result;')
    add(u'        }')
    add(u'    }')
    add(u'}')
    text = u'\r\n'.join(lines) + u'\r\n'

    if not args.apply:
        print()
        print(u'--apply не задан, файл не тронут: %s' % OUT)
        return

    io.open(OUT, 'w', encoding='utf-8-sig', newline='').write(text)
    print()
    print(u'записано: %s (%d строк)' % (OUT, len(lines)))


if __name__ == '__main__':
    main()
