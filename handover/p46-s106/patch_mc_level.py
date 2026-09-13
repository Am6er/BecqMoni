import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

old = '''                if (a.StartsWith("--mc-dump=", StringComparison.Ordinal))
                {
                    o.McDump = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                    continue;
                }
'''
new = old + '''                if (a.StartsWith("--mc-level=", StringComparison.Ordinal))
                {
                    o.McLevel = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                    continue;
                }
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''            public int McDump;
'''
new = old + '''
            /// <summary>
            /// (`S106`, П46) Множитель уровня впрыска: 1 — на уровне МДА (штатно);
            /// 0 — положительный контроль мерки (впрыска нет, пропусков обязано
            /// быть ~100/100); больше 1 — развёртка «на каком уровне компонент
            /// вообще находится».
            /// </summary>
            public double McLevel = 1.0;
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''                double mdaAmplitude = c.DetectionLimitRate * liveTime;
'''
new = '''                double mdaAmplitude = c.DetectionLimitRate * liveTime * o.McLevel;
                if (o.McLevel != 1.0)
                {
                    Console.WriteLine("  {0}: {1} — уровень впрыска {2:F3} × МДА (ключ --mc-level)",
                                      key, c.Name, o.McLevel);
                }
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''    /// `--mc-dump=K` (П46 13.09.2026) — печатать первые K розыгрышей каждой
'''
new = '''    /// `--mc-level=F` (П46 13.09.2026) — множитель уровня впрыска: 0 —
    /// положительный контроль (пропусков обязано быть ~100/100), больше 1 —
    /// развёртка уровня, на котором компонент находится.
''' + old
assert s.count(old) == 1; s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
