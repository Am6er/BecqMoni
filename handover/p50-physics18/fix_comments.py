# -*- coding: utf-8 -*-
"""П50 13.09.2026 — комментарии «ВЫКЛ до единого счёта физики 18» → «ВКЛ с физики 18».
Только текст; каждая замена обязана сработать ровно один раз, иначе отказ."""
import io
import sys

WT = r'D:\BqMoni_Claude\p50\wt'
EDITS = [
    (r'tools\effmaker\probes\CorpusMatrixProbe.cs',
     u'// требует `etr=1`. Оба умолчанием ВЫКЛ (до единого счёта физики 18), входят\r\n'
     u'// в клеймо (`ecomp=1`, `bpath=N`) и в склад не лягут.',
     u'// требует `etr=1`. С 14.09.2026 — умолчания класса (`ecomp=1`, `bpath=2`),\r\n'
     u'// физика 18, единым счётом склада (П50; решение Amber 13.09.2026 «ecomp=1 +\r\n'
     u'// bpath=2»); ключи `--ecomp=0 --bpath=0` — рычаги абляции, матрица без них\r\n'
     u'// честно другая по клейму.'),
    (r'tools\effmaker\probes\CorpusEffProbe.cs',
     u'// кривой ключами склада (умолчания ВЫКЛ); в клеймо кривой — `ecomp=1`, `bpath=N`.',
     u'// кривой ключами склада (умолчания с 14.09.2026 — `ecomp=1`, `bpath=2`, физика 18,\r\n'
     u'// П50); в клеймо кривой — `ecomp=1`, `bpath=N` только включёнными.'),
    (r'tools\effmaker\probes\G4RawProbe.cs',
     u'    /// вдоль пути переноса (1 изотропно, 2 по электрону). Умолчания — склада\r\n'
     u'    /// (оба ВЫКЛ до единого счёта физики 18). Мерка: голые RC103 / AS80 на',
     u'    /// вдоль пути переноса (1 изотропно, 2 по электрону). Умолчания — склада\r\n'
     u'    /// (с 14.09.2026, физика 18, П50: `ecomp=1`, `bpath=2`; сверки П44\r\n'
     u'    /// воспроизводятся `--ecomp=0 --bpath=0`). Мерка: голые RC103 / AS80 на'),
    (r'tools\effmaker\probes\G4RawProbe.cs',
     u'            bool ecomp = store.ElectronAnyMaterial;     // `N4`/`F11` (г), П44 — умолчание склада (ВЫКЛ)\r\n'
     u'            int bpath = store.BremAlongPath;            // `M3`, П44 — умолчание склада (0)',
     u'            bool ecomp = store.ElectronAnyMaterial;     // `N4`/`F11` (г), П44 — умолчание склада (ВКЛ с физики 18, П50)\r\n'
     u'            int bpath = store.BremAlongPath;            // `M3`, П44 — умолчание склада (2 с физики 18, П50)'),
    (r'BecquerelMonitor\EfficiencyMaker\ElectronData.cs',
     u'    /// (клеймо `ecomp=1`, ВЫКЛ до единого счёта физики 18). Список',
     u'    /// (клеймо `ecomp=1`; ВЫКЛ до единого счёта, умолчанием ВКЛ с 14.09.2026 —\r\n'
     u'    /// физика 18, П50). Список'),
    (r'BecquerelMonitor\EfficiencyMaker\EstarCalculator.cs',
     u'    /// ВЫКЛ до единого счёта физики 18; склад матриц ключом не тронут.',
     u'    /// ВЫКЛ до единого счёта, умолчанием ВКЛ с 14.09.2026 — физика 18, единым\r\n'
     u'    /// счётом склада (П50).'),
    (r'BecquerelMonitor\EfficiencyMaker\BremsstrahlungData.cs',
     u'    /// <see cref="EfficiencySimulator.BremAlongPath"/> (клеймо `bpath=N`, ВЫКЛ\r\n'
     u'    /// до единого счёта физики 18): с переносом электрона (`etr=1`) кванты',
     u'    /// <see cref="EfficiencySimulator.BremAlongPath"/> (клеймо `bpath=N`; ВЫКЛ\r\n'
     u'    /// до единого счёта, умолчанием уровень 2 с 14.09.2026 — физика 18, П50):\r\n'
     u'    /// с переносом электрона (`etr=1`) кванты'),
    (r'database\scheme.md',
     u'`ElectronAnyMaterial` (`ecomp=1`, ВЫКЛ до единого счёта физики 18) кривая',
     u'`ElectronAnyMaterial` (`ecomp=1`; ВЫКЛ до единого счёта, умолчанием ВКЛ с\r\n'
     u'14.09.2026 — физика 18, П50) кривая'),
    (r'database\scheme.md',
     u'`ElectronAnyMaterial` (`ecomp=1`, ВЫКЛ до единого счёта физики 18): `ElectronData.ForComposition`',
     u'`ElectronAnyMaterial` (`ecomp=1`; ВЫКЛ до единого счёта, умолчанием ВКЛ с 14.09.2026 — физика 18, П50): `ElectronData.ForComposition`'),
]


def main():
    bad = 0
    for rel, old, new in EDITS:
        path = WT + '\\' + rel
        with io.open(path, 'r', encoding='utf-8', newline='') as f:
            text = f.read()
        # соглашение переводов строк файла: если в файле нет CRLF — образец и замена без CR
        if '\r\n' not in text:
            old_ = old.replace('\r\n', '\n')
            new_ = new.replace('\r\n', '\n')
        else:
            old_, new_ = old, new
        n = text.count(old_)
        if n != 1:
            print(u'ОТКАЗ %s: образец найден %d раз (нужно 1)' % (rel, n))
            bad += 1
            continue
        text = text.replace(old_, new_)
        with io.open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(text)
        print(u'ok  %s (%s)' % (rel, 'CRLF' if '\r\n' in text else 'LF'))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
