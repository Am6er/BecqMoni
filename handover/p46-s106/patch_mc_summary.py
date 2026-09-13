import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

# 1. предупреждение о привязке и аналитический сдвиг — перед дампом уровня
old = '''                if (o.McDump > 0)
                {
                    double injected = 0.0, familyTotal = 0.0, modelTotal = 0.0;
'''
new = '''                // (`S106`, П46 13.09.2026) ДВЕ ПРИЧИНЫ, по которым впрыск на
                // уровне МДА даёт нулевую оценку, — обе названы дампом
                // (`--mc-dump`) и обе печатаются здесь, чтобы читатель сводки
                // видел их без дампа.
                //
                // 1. Веса решателя — по ДАННЫМ (1/max(y,1), FsaAnalyzer.Analyze):
                //    E[(y−μ)/y] ≈ −1/μ, и градиент по колонке φ несёт сдвиг
                //    −Σφ/μ, не зависящий от амплитуды. В отсчётах он равен
                //    примерно числу каналов, по которым размазан образ: ряд
                //    Th-228 на G1S16 — ~1000 отсчётов при впрыске 1127, Lu-176
                //    на ASN16 — 426 при впрыске 308, Cs-137 — 51 при 935.
                //    Формула пределов описывает несмещённый линейный оценщик и
                //    этого сдвига не знает; NNLS обрезает смещённую оценку
                //    нулём. Отсюда же «ложных 0/100» П28 и «σ0 розыгрыша в
                //    2.4…7.4 раза меньше» — нулевая оценка сидит на −3σ.
                // 2. Привязка шкалы ВКЛ: копия без пиков опор не находит, и
                //    усиление/ноль/свет/нуль adc уходят к умолчаниям прибора —
                //    образ копии стоит не там, где впрыск (у Th-228 на G1S16:
                //    β 1 → 0, усиление 1.0082 → 1, нуль −0.93 → 0 кан). Для
                //    поверки ФОРМУЛЫ обе стороны обязаны быть на одной шкале:
                //    ключ `--no-anchor`.
                double biasGradient = 0.0, biasGram = 0.0, familySum = 0.0;
                for (int i = result.FirstChannel; i <= result.LastChannel && i < channels; i++)
                {
                    double phi = 0.0;
                    foreach (FsaComponentResult member in family)
                    {
                        phi += member.Curve[i];
                    }

                    phi /= amplitude;
                    double v = Math.Max(1.0, mu0[i]);
                    biasGradient += phi / v;
                    biasGram += phi * phi / v;
                    familySum += phi;
                }

                double biasAmplitude = biasGram > 0.0 ? biasGradient / biasGram : double.NaN;
                Console.WriteLine("  {0}: {1} — сдвиг оценки от весов по данным (−Σφ/μ, П46): ≈ {2:F0} отсч. = {3:F2} × МДА = {4:F2} × a*{5}",
                                  key, c.Name, biasAmplitude * familySum,
                                  mdaAmplitude > 0.0 ? biasAmplitude / mdaAmplitude : double.NaN,
                                  c.DecisionThresholdRate > 0.0 ? biasAmplitude / (c.DecisionThresholdRate * liveTime) : double.NaN,
                                  analyzer.AnchorScale
                                      ? "; ⚠ привязка ВКЛ: копия без пиков теряет опоры, образ копии не на шкале впрыска — поверять формулу с --no-anchor"
                                      : "");

                if (o.McDump > 0)
                {
                    double injected = 0.0, familyTotal = 0.0, modelTotal = 0.0;
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 2. документация ключа
old = '''    /// `--mc-level=F` (П46 13.09.2026) — множитель уровня впрыска: 0 —
'''
new = '''    /// ⛔ Две причины нулевой оценки на уровне МДА названы П46 13.09.2026
    /// (журнал `handover/handover-2026-09-13-p46-s106-mc-chain.md`): веса
    /// решателя по данным (сдвиг −Σφ/μ ≈ число каналов образа, в отсчётах) и
    /// привязка шкалы, которую копия без пиков теряет. Сводка печатает сдвиг
    /// по каждому компоненту; поверку формулы гнать с `--no-anchor`.
    /// `--mc-level=F` (П46 13.09.2026) — множитель уровня впрыска: 0 —
'''
assert s.count(old) == 1; s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
