# -*- coding: utf-8 -*-
"""П47 13.09.2026 (A310) — строка сдвига −Σφ/μ в сводке МК: при весах по модели сдвиг НЕ действует, и строка
говорит это сама (иначе читатель сводки плеча ВКЛ примет предсказание сдвига за ожидание). Однократно."""
import io

NL = '\r\n'
p = 'tools/effmaker/probes/CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
old = """                Console.WriteLine("  {0}: {1} — сдвиг оценки от весов по данным (−Σφ/μ, П46): ≈ {2:F0} отсч. = {3:F2} × МДА = {4:F2} × a*{5}",
                                  key, c.Name, biasAmplitude * familySum,
                                  mdaAmplitude > 0.0 ? biasAmplitude / mdaAmplitude : double.NaN,
                                  c.DecisionThresholdRate > 0.0 ? biasAmplitude / (c.DecisionThresholdRate * liveTime) : double.NaN,
                                  analyzer.AnchorScale
                                      ? "; ⚠ привязка ВКЛ: копия без пиков теряет опоры, образ копии не на шкале впрыска — поверять формулу с --no-anchor"
                                      : "");
""".replace('\n', NL)
new = """                // (`A310`, П47) При весах по МОДЕЛИ (`ModelWeights`) сдвиг
                // −Σφ/μ не действует — печатается как справка «был бы при
                // весах по данным», чтобы читатель плеча ВКЛ не принял его за
                // ожидание.
                Console.WriteLine("  {0}: {1} — сдвиг оценки от весов по данным (−Σφ/μ, П46){6}: ≈ {2:F0} отсч. = {3:F2} × МДА = {4:F2} × a*{5}",
                                  key, c.Name, biasAmplitude * familySum,
                                  mdaAmplitude > 0.0 ? biasAmplitude / mdaAmplitude : double.NaN,
                                  c.DecisionThresholdRate > 0.0 ? biasAmplitude / (c.DecisionThresholdRate * liveTime) : double.NaN,
                                  analyzer.AnchorScale
                                      ? "; ⚠ привязка ВКЛ: копия без пиков теряет опоры, образ копии не на шкале впрыска — поверять формулу с --no-anchor"
                                      : "",
                                  analyzer.ModelWeights
                                      ? " — НЕ ДЕЙСТВУЕТ: веса решателя по МОДЕЛИ (A310), справочно"
                                      : "");
""".replace('\n', NL)
assert s.count(old) == 1, s.count(old)
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
