# -*- coding: utf-8 -*-
"""П93: восстановить версию `FsaCascadeSummer.cs` ПОЛОСЫ П90 (доли по схеме + срез числом 24) из
текущего файла дерева (П90 + срез П93) — для промежуточного плеча лестницы «схема + срез 24» на полном
корпусе. Снимает ровно правки П93 (обратные замены); каждая замена обязана найтись ровно один раз.

    python handover/p93-s176/revert_cut_for_arm.py <исходник дерева> <куда положить>

Приёмка реконструкции — не текстом, а поведением: `FsaCascadeProbe --describe` на `G1S16_Eu152_P5`
из плеча обязан дать лог, побитово равный `handover/p90-s176/ours/cf_p5_fix_ang1.log` (П90).
"""
import io, re, sys

src, dst = sys.argv[1], sys.argv[2]
with io.open(src, encoding='utf-8-sig', newline='') as f:
    text = f.read()
nl = '\r\n' if '\r\n' in text else '\n'

def cut_between(text, start_marker, end_marker, keep_end=True):
    a = text.index(start_marker)
    b = text.index(end_marker, a)
    return text[:a] + (text[b:] if keep_end else text[b + len(end_marker):])

def replace_once(text, old, new):
    n = text.count(old)
    if n != 1:
        raise SystemExit('ожидалось 1 вхождение, найдено %d: %r' % (n, old[:60]))
    return text.replace(old, new)

# 1. SumPeak.Trimmed — снять свойство целиком (от его summary до следующего конструктора).
text = cut_between(text,
    '            /// <summary>' + nl + '            /// Срезан ПРАВИЛОМ ПЛОЩАДИ',
    '            public SumPeak(double energy, double area, string nuclide,' + nl + '                           double fromKev, double withKev, double thirdKev)')

# 2. Константы: SumPeakAreaShare + MaxSumPeaks(96) -> MaxSumPeaks = 24.
text = cut_between(text,
    '        /// <summary>' + nl + '        /// СРЕЗ СУММ-ПИКОВ НУКЛИДА — ПО ПЛОЩАДИ.',
    '        const int MaxSumPeaks = 96;', keep_end=False)
text = replace_once(text,
    nl + nl + nl + '        /// <summary>' + nl + '        /// ЖУРНАЛ ТРОЙНЫХ СУММ',
    nl + nl + '        /// <summary>Больше этого числа сумм-пиков на компонент не берём.</summary>' + nl +
    '        const int MaxSumPeaks = 24;' + nl + nl + '        /// <summary>' + nl + '        /// ЖУРНАЛ ТРОЙНЫХ СУММ')

# 3. Compute: вернуть общий список и срез числом.
old_loop = ('                // Суммы нуклида собираются в СВОЙ список: срез по площади' + nl +
            '                // (`SumPeakAreaShare`, решение Amber 17.09.2026) меряется от' + nl +
            '                // суммарной площади сумм-пиков ЭТОГО нуклида, а не компонента.' + nl +
            '                var nuclidePeaks = new List<SumPeak>();' + nl +
            '                var nuclideDropped = new List<SumPeak>();' + nl +
            '                this.CollectSumPeaks(component, nuclide, data, scale, strongest, nuclidePeaks,' + nl +
            '                                     continua, nuclideDropped);' + nl +
            '                TrimByArea(nuclidePeaks, nuclideDropped);' + nl +
            '                sumPeaks.AddRange(nuclidePeaks);' + nl +
            '                dropped.AddRange(nuclideDropped);' + nl +
            '            }' + nl + nl +
            '            if (sumPeaks.Count > 0)' + nl +
            '            {' + nl +
            '                any = true;' + nl +
            '                // Порядок — по убыванию площади, как и печатает `Describe`;' + nl +
            '                // счёту он безразличен.' + nl +
            '                sumPeaks.Sort((a, b) => b.Area.CompareTo(a.Area));' + nl +
            '            }')
new_loop = ('                this.CollectSumPeaks(component, nuclide, data, scale, strongest, sumPeaks, continua,' + nl +
            '                                     dropped);' + nl +
            '            }' + nl + nl +
            '            if (sumPeaks.Count > 0)' + nl +
            '            {' + nl +
            '                any = true;' + nl +
            '                sumPeaks.Sort((a, b) => b.Area.CompareTo(a.Area));' + nl +
            '                if (sumPeaks.Count > MaxSumPeaks)' + nl +
            '                {' + nl +
            '                    // Срезанное не пропадает: отчёт обязан назвать, ПОЧЕМУ' + nl +
            '                    // суммы нет в образе (П90, `S176`).' + nl +
            '                    dropped.AddRange(sumPeaks.GetRange(MaxSumPeaks, sumPeaks.Count - MaxSumPeaks));' + nl +
            '                    sumPeaks.RemoveRange(MaxSumPeaks, sumPeaks.Count - MaxSumPeaks);' + nl +
            '                }' + nl +
            '            }')
text = replace_once(text, old_loop, new_loop)

# 4. TrimByArea — снять метод целиком (от summary до summary CollectSumPeaks).
text = cut_between(text,
    '        /// <summary>' + nl + '        /// Срез сумм-пиков одного нуклида ПО ПЛОЩАДИ',
    '        /// <summary>' + nl + '        /// Сумм-пики нуклида: пары, чья сумма НЕ попала')

# 5. Describe: снять Tally и сводку среза, вернуть заголовок и пометку.
text = cut_between(text,
    '        /// <summary>Счётчик сводки среза в <see cref="Describe"/>',
    '        public string Describe(FsaComponent component)')
text = cut_between(text,
    '            // Сводка среза по нуклидам (`S176`, П93)',
    '            // Посчитанное, но в образ не попавшее — срез по площади и порог')
text = replace_once(text,
    '            // Посчитанное, но в образ не попавшее — срез по площади и порог',
    '            // Посчитанное, но в образ не попавшее — срез `MaxSumPeaks` и порог')
text = replace_once(text,
    '                    "  посчитано, но в образ НЕ идёт ({0}: срез {1:F0} % Σ площади нуклида, не более {2}, и порог SumPeakFloor):",' + nl +
    '                    correction.DroppedSumPeaks.Count, SumPeakAreaShare * 100.0, MaxSumPeaks);',
    '                    "  посчитано, но в образ НЕ идёт ({0}: срез MaxSumPeaks = {1} и порог SumPeakFloor):",' + nl +
    '                    correction.DroppedSumPeaks.Count, MaxSumPeaks);')
text = replace_once(text,
    '                        "  {0,11:F2}   {1,-22}  {2,-10}  {3,12:E4}  (не в образе: {4})",' + nl +
    '                        peak.Energy, parts, peak.Nuclide, peak.Area, peak.Trimmed ? "срез" : "порог");',
    '                        "  {0,11:F2}   {1,-22}  {2,-10}  {3,12:E4}  (не в образе)",' + nl +
    '                        peak.Energy, parts, peak.Nuclide, peak.Area);')

code = '\n'.join(l for l in text.splitlines() if not l.strip().startswith('///'))
for bad in ('SumPeakAreaShare', 'TrimByArea', 'Trimmed', 'Tally('):
    if bad in code:
        raise SystemExit('остался след правки П93 в КОДЕ (описания не в счёт): ' + bad)
with io.open(dst, 'w', encoding='utf-8-sig', newline='') as f:
    f.write(text)
print('ok', dst, 'MaxSumPeaks = 24' in text)
