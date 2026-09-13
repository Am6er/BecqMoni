# -*- coding: utf-8 -*-
# П16: ключ --anchor-skip=<кэВ,кэВ,…> — линии, которым ЗАПРЕЩЕНО быть опорами (приёмка выбросом узла).
import io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\bqp16'
def patch(rel, pairs):
    p = os.path.join(ROOT, rel)
    raw = open(p, 'rb').read()
    nl = '\r\n' if raw.count(b'\r\n') > raw.count(b'\n') // 2 else '\n'
    s = raw.decode('utf-8-sig')
    for old, new in pairs:
        old = old.replace('\n', nl); new = new.replace('\n', nl)
        n = s.count(old)
        if n != 1:
            raise SystemExit('%s: образец встречается %d раз(а): %s' % (rel, n, old[:120]))
        s = s.replace(old, new)
    open(p, 'wb').write(s.encode('utf-8'))
    print('%s: %d замен (%s)' % (rel, len(pairs), 'CRLF' if nl == '\r\n' else 'LF'))

patch('BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs', [
    ('''        public double AnchorMinFwhmChannels { get; set; }
''', '''        public double AnchorMinFwhmChannels { get; set; }

        /// <summary>
        /// (П16, ЗАМЕР — приёмка выбросом узла) Линии, кэВ, которым ЗАПРЕЩЕНО
        /// быть опорами: кандидат с такой линией ядра (±0.5 кэВ) получает отказ
        /// "skip" и в МНК не идёт, а его остаток печатается как у любого
        /// отказа. Пусто умолчанием; рычаг — `--anchor-skip=`.
        /// </summary>
        public double[] AnchorSkipKev { get; set; }
'''),
    ('''                bool narrow = fwhm < this.AnchorMinFwhmChannels;
''', '''                bool narrow = fwhm < this.AnchorMinFwhmChannels;
                bool skipped = false;
'''),
    ('''                    else if (narrow)
''', '''                    else if (this.AnchorSkipKev != null && IsSkipped(this.AnchorSkipKev, coreLineKev))
                    {
                        anchor.Refusal = "skip";
                        skipped = true;
                    }
                    else if (narrow)
'''),
    ('''        /// <summary>(П14) Решение 3×3 методом Гаусса с выбором ведущего; null при вырождении.</summary>
''', '''        /// <summary>(П16) Линия в списке выброшенных (±0.5 кэВ).</summary>
        static bool IsSkipped(double[] skip, double lineKev)
        {
            foreach (double s in skip)
            {
                if (Math.Abs(s - lineKev) <= 0.5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>(П14) Решение 3×3 методом Гаусса с выбором ведущего; null при вырождении.</summary>
'''),
])
patch('tools/effmaker/probes/CorpusFsaProbe.cs', [
    ('''                if (a.StartsWith("--anchor-beta-dchi=", StringComparison.Ordinal))
                {
                    o.AnchorBetaDchi = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                    continue;
                }
''', '''                if (a.StartsWith("--anchor-beta-dchi=", StringComparison.Ordinal))
                {
                    o.AnchorBetaDchi = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                    continue;
                }
                // (П16) выброс узла: линии, которым запрещено быть опорами
                if (a.StartsWith("--anchor-skip=", StringComparison.Ordinal))
                {
                    var list = new List<double>();
                    foreach (string part in a.Substring(14).Split(','))
                    {
                        if (part.Trim().Length > 0)
                        {
                            list.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                        }
                    }
                    o.AnchorSkip = list.ToArray();
                    continue;
                }
'''),
    ('''            if (o.AnchorBetaDchi >= 0.0)
            {
                analyzer.AnchorLightMinDeltaChi2 = o.AnchorBetaDchi;
            }
''', '''            if (o.AnchorBetaDchi >= 0.0)
            {
                analyzer.AnchorLightMinDeltaChi2 = o.AnchorBetaDchi;
            }

            if (o.AnchorSkip != null && o.AnchorSkip.Length > 0)
            {
                analyzer.AnchorSkipKev = o.AnchorSkip;
            }
'''),
    ('''            public double AnchorBetaDchi = -1.0;
''', '''            public double AnchorBetaDchi = -1.0;
            public double[] AnchorSkip = null;   // (П16) выброс узла
'''),
])
print('ГОТОВО')
