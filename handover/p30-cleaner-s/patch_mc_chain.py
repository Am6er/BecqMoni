# П30 (S106): нулевая гипотеза МК-поверки для ЧЛЕНА РЯДА — снимать из модели ВЕСЬ ряд (все компоненты
# с тем же DecayChainRoot) и впрыскивать весь ряд в масштабе МДА/a члена; иначе связка равновесия
# восстанавливает снятый член из его дочерних, и «ложных 100/100» мерит связку, а не порог. CRLF сохраняется.
import io
p='tools/effmaker/probes/CorpusFsaProbe.cs'
raw=io.open(p,'rb').read()
assert raw.count(b'\r\n')==raw.count(b'\n')
s=raw.decode('utf-8').replace('\r\n','\n')
def rep(a,b):
    global s
    assert s.count(a)==1, (s.count(a), a[:70])
    s=s.replace(a,b)
rep("""                double mdaAmplitude = c.DetectionLimitRate * liveTime;
                double[] mu0 = new double[channels];
""","""                double mdaAmplitude = c.DetectionLimitRate * liveTime;

                // (`S106`, полоса П30 12.09.2026) Нулевая гипотеза ЧЛЕНА РЯДА —
                // ряд целиком. Снятый из модели один член связка равновесия
                // восстанавливает из его же дочерних (Th-228 из Pb-212/Tl-208),
                // и «ложных 100/100» мерило связку, а не порог. Поэтому из
                // модели вынимаются ВСЕ компоненты с тем же корнем ряда, и
                // впрыскиваются они же — в масштабе МДА/a самого члена.
                var family = new List<FsaComponentResult> { c };
                if (!string.IsNullOrEmpty(c.DecayChainRoot))
                {
                    foreach (FsaComponentResult other in result.Components)
                    {
                        if (!ReferenceEquals(other, c) && other.Curve != null
                            && string.Equals(other.DecayChainRoot, c.DecayChainRoot, StringComparison.Ordinal))
                        {
                            family.Add(other);
                        }
                    }
                }

                if (family.Count > 1)
                {
                    Console.WriteLine("  {0}: {1} — член ряда {2}: нулевая гипотеза и впрыск — ряд целиком ({3} компонентов)",
                                      key, c.Name, c.DecayChainRoot, family.Count);
                }

                double[] mu0 = new double[channels];
""")
rep("""                    double without = result.Model[i] - c.Curve[i];
                    if (without < 0.0)
                    {
                        without = 0.0;
                    }

                    double bg = result.Background != null ? result.Background[i] : 0.0;
                    mu0[i] = without + bg;
                    mu1[i] = mu0[i] + mdaAmplitude * (c.Curve[i] / amplitude);
""","""                    double familyCurve = 0.0;
                    foreach (FsaComponentResult member in family)
                    {
                        familyCurve += member.Curve[i];
                    }

                    double without = result.Model[i] - familyCurve;
                    if (without < 0.0)
                    {
                        without = 0.0;
                    }

                    double bg = result.Background != null ? result.Background[i] : 0.0;
                    mu0[i] = without + bg;
                    mu1[i] = mu0[i] + mdaAmplitude * (familyCurve / amplitude);
""")
io.open(p,'wb').write(s.replace('\n','\r\n').encode('utf-8'))
print('ok')
