import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
n0 = len(s)
assert s.count(chr(13)+chr(10)) == s.count(chr(10)), 'смешанные переводы строк'
s = s.replace(chr(13)+chr(10), chr(10))

# 1. ключ
old = '''                if (a.StartsWith("--mc-component=", StringComparison.Ordinal))
                {
                    o.McComponent = a.Substring(15);
                    continue;
                }
'''
new = old + '''                if (a.StartsWith("--mc-dump=", StringComparison.Ordinal))
                {
                    o.McDump = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                    continue;
                }
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 2. поле
old = '''            /// <summary>Поверять только этот компонент (`--limits-mc`); null — все.</summary>
            public string McComponent;
'''
new = old + '''
            /// <summary>
            /// (`S106`, П46 13.09.2026) Дамп первых K розыгрышей каждой серии
            /// МК-поверки: оценка и порог каждого члена семьи, кто подавлен и
            /// чем (гейт по парциальной невязке / отсев), состав копии, шкала,
            /// χ²/ndf. 0 — не печатать.
            /// </summary>
            public int McDump;
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 3. документация ключа
old = '''    /// восстанавливала из дочерних, и `G1S16_Th228_P25` давал «ложных 100/100».
    ///
'''
new = '''    /// восстанавливала из дочерних, и `G1S16_Th228_P25` давал «ложных 100/100».
    /// `--mc-dump=K` (П46 13.09.2026) — печатать первые K розыгрышей каждой
    /// серии: оценки членов семьи, подавленные образы, счётчики гейта и отсева,
    /// состав копии, шкалу и χ²/ndf — инструмент, которым названа причина нулей.
    ///
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 4. дамп уровня перед розыгрышами
old = '''                int falsePositives = 0, detections = 0, failed = 0;
                var nullEstimates = new List<double>();
                var injectedEstimates = new List<double>();
                for (int run = 0; run < o.LimitsMc; run++)
                {
                    FsaResult replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                                    mu0, rng);
                    if (replay != null)
                    {
                        double estimate;
                        bool exceeded = Exceeded(replay, c.Name, out estimate);
                        nullEstimates.Add(estimate);
                        if (exceeded)
                        {
                            falsePositives++;
                        }
                    }
                    else
                    {
                        failed++;
                    }

                    replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                          mu1, rng);
                    if (replay != null)
                    {
                        double estimate;
                        if (Exceeded(replay, c.Name, out estimate))
                        {
                            detections++;
                        }

                        injectedEstimates.Add(estimate);
                    }
                    else
                    {
                        failed++;
                    }
                }
'''
new = '''                if (o.McDump > 0)
                {
                    double injected = 0.0, familyTotal = 0.0, modelTotal = 0.0;
                    for (int i = result.FirstChannel; i <= result.LastChannel && i < channels; i++)
                    {
                        injected += mu1[i] - mu0[i];
                        modelTotal += Math.Max(0.0, result.Model[i]);
                        foreach (FsaComponentResult member in family)
                        {
                            familyTotal += member.Curve[i];
                        }
                    }

                    Console.WriteLine("  МК-дамп {0} {1}: живое {2:F1} с; окно фита {3}..{4}; модель в окне {5:E3} отсч.,"
                                      + " семья {6:E3} отсч. ({7} комп.), амплитуда члена {8:E3} (= {9:E3} имп/с);"
                                      + " впрыск Σ(mu1−mu0) = {10:E3} отсч. = {11:F3} × семья; МДА/a* = {12:F3};"
                                      + " исходный: χ²/ndf {13:F3}, σ× {14:F3}, усил. {15:F4}, ноль {16:F2} кан., опор {17}",
                                      key, c.Name, liveTime, result.FirstChannel, result.LastChannel,
                                      modelTotal, familyTotal, family.Count, amplitude, c.CountRate,
                                      injected, familyTotal > 0.0 ? injected / familyTotal : double.NaN,
                                      c.DecisionThresholdRate > 0.0 ? c.DetectionLimitRate / c.DecisionThresholdRate : double.NaN,
                                      result.Chi2Ndf, result.SigmaInflation, result.Gain, result.OffsetChannels,
                                      result.ScaleAnchorsUsed);
                    foreach (FsaComponentResult member in family)
                    {
                        double memberTotal = 0.0;
                        for (int i = result.FirstChannel; i <= result.LastChannel && i < channels; i++)
                        {
                            memberTotal += member.Curve[i];
                        }

                        Console.WriteLine("    семья: {0,-10} кол.{1,-8} имп/с {2:E3} z {3:F2} a* {4:E3} МДА {5:E3} лента {6:E3} отсч. ΔD {7:F1}",
                                          member.Name, member.ChainRoot ?? "-", member.CountRate, member.Z,
                                          member.DecisionThresholdRate, member.DetectionLimitRate,
                                          memberTotal, member.ZoneDeltaD);
                    }

                    DumpReplay(result, analyzer, family, "исходный", -1, key);
                }

                int falsePositives = 0, detections = 0, failed = 0;
                var nullEstimates = new List<double>();
                var injectedEstimates = new List<double>();
                for (int run = 0; run < o.LimitsMc; run++)
                {
                    FsaResult replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                                    mu0, rng);
                    if (replay != null)
                    {
                        double estimate;
                        bool exceeded = Exceeded(replay, c.Name, out estimate);
                        nullEstimates.Add(estimate);
                        if (exceeded)
                        {
                            falsePositives++;
                        }

                        if (run < o.McDump)
                        {
                            DumpReplay(replay, analyzer, family, "нуль", run, key);
                        }
                    }
                    else
                    {
                        failed++;
                    }

                    replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                          mu1, rng);
                    if (replay != null)
                    {
                        double estimate;
                        if (Exceeded(replay, c.Name, out estimate))
                        {
                            detections++;
                        }

                        injectedEstimates.Add(estimate);
                        if (run < o.McDump)
                        {
                            DumpReplay(replay, analyzer, family, "впрыск", run, key);
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 5. сам дамп — перед RunSynthetic
old = '''        /// <summary>
        /// Один синтетический разбор: розыгрыш каналов пуассоном вокруг
        /// заданных средних, тот же анализатор, та же библиотека. null —
        /// разбор не удался.
        /// </summary>
        static FsaResult RunSynthetic('''
new = '''        /// <summary>
        /// (`S106`, П46 13.09.2026) Один розыгрыш МК-поверки словами: оценка
        /// и порог каждого члена семьи по строкам пределов копии, подавленные
        /// образы копии (кто был предъявлен и до отчёта не дожил, с z), счётчики
        /// гейта по парциальной невязке и отсева по значимости у анализатора,
        /// состав копии (имя = имп/с, z), шкала и χ²/ndf. Печатается для первых
        /// `--mc-dump=K` розыгрышей каждой серии.
        /// </summary>
        static void DumpReplay(FsaResult replay, FsaAnalyzer analyzer, List<FsaComponentResult> family,
                               string label, int run, string key)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "    {0} #{1}:", label, run);
            foreach (FsaComponentResult member in family)
            {
                FsaCharacteristicLimit found = null;
                foreach (FsaCharacteristicLimit limit in replay.CharacteristicLimits)
                {
                    if (string.Equals(limit.Name, member.Name, StringComparison.Ordinal))
                    {
                        found = limit;
                        break;
                    }
                }

                if (found == null)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0}=НЕТ СТРОКИ;", member.Name);
                    continue;
                }

                sb.AppendFormat(CultureInfo.InvariantCulture, " {0}={1:E3} a*={2:E3}{3}{4};",
                                member.Name, found.CountRate, found.DecisionThresholdRate,
                                found.Detected ? " вошёл" : " НЕ вошёл",
                                found.Degenerate ? " ВЫРОЖДЕН" : "");
            }

            sb.Append(" | подавлены:");
            if (replay.SuppressedImages == null || replay.SuppressedImages.Count == 0)
            {
                sb.Append(" нет");
            }
            else
            {
                foreach (FsaSuppressedImage image in replay.SuppressedImages)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0}(z {1:F2})", image.Name, image.Z);
                }
            }

            sb.AppendFormat(CultureInfo.InvariantCulture,
                            " | гейт: судил {0}, пощадил {1}, вернул {2}; отсев: {3}, судил {4}, оставил {5}",
                            analyzer.GateNuclidesJudged, analyzer.GateNuclidesSpared, analyzer.GateNuclidesRescued,
                            analyzer.RefitZState, analyzer.RefitZJudged, analyzer.RefitZKept);
            sb.Append(" | состав:");
            foreach (FsaComponentResult component in replay.Components)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0}={1:E2}(z {2:F1}{3})",
                                component.Name, component.CountRate, component.Z,
                                double.IsNaN(component.ZoneDeltaD) ? "" : string.Format(CultureInfo.InvariantCulture, ", ΔD {0:F1}", component.ZoneDeltaD));
            }

            sb.AppendFormat(CultureInfo.InvariantCulture,
                            " | χ²/ndf {0:F3}, σ× {1:F3}, усил. {2:F4}, ноль {3:F2} кан., опор {4}, окно {5}..{6}",
                            replay.Chi2Ndf, replay.SigmaInflation, replay.Gain, replay.OffsetChannels,
                            replay.ScaleAnchorsUsed, replay.FirstChannel, replay.LastChannel);
            Console.WriteLine(sb.ToString());
        }

        /// <summary>
        /// Один синтетический разбор: розыгрыш каналов пуассоном вокруг
        /// заданных средних, тот же анализатор, та же библиотека. null —
        /// разбор не удался.
        /// </summary>
        static FsaResult RunSynthetic('''
assert s.count(old) == 1; s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok', n0, '->', len(s))
