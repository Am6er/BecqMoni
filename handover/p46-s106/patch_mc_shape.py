import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

# 1. в DumpReplay — привязка и свет
old = '''            sb.AppendFormat(CultureInfo.InvariantCulture,
                            " | χ²/ndf {0:F3}, σ× {1:F3}, усил. {2:F4}, ноль {3:F2} кан., опор {4}, окно {5}..{6}",
                            replay.Chi2Ndf, replay.SigmaInflation, replay.Gain, replay.OffsetChannels,
                            replay.ScaleAnchorsUsed, replay.FirstChannel, replay.LastChannel);
            Console.WriteLine(sb.ToString());
        }
'''
new = '''            sb.AppendFormat(CultureInfo.InvariantCulture,
                            " | χ²/ndf {0:F3}, σ× {1:F3}, усил. {2:F4}, ноль {3:F2} кан., опор {4}, окно {5}..{6}; свет: {7} β={8:F4} форма {9}; привязка: {10}",
                            replay.Chi2Ndf, replay.SigmaInflation, replay.Gain, replay.OffsetChannels,
                            replay.ScaleAnchorsUsed, replay.FirstChannel, replay.LastChannel,
                            replay.AnchorLightCurve, replay.AnchorLightBeta, replay.AnchorLightForm,
                            replay.AnchorNote ?? "-");
            Console.WriteLine(sb.ToString());
        }

        /// <summary>
        /// (`S106`, П46) Форма образа компонента в КОПИИ против формы, которой
        /// он впрыснут (лента исходного разбора): обе нормируются на единицу
        /// амплитуды, печатается коэффициент корреляции по окну фита, а по трём
        /// самым сильным пикам впрыска — положение вершины у обеих (канал) и
        /// отношение высот. Печатается только когда компонент в копию вошёл
        /// (иначе его ленты в результате нет).
        /// </summary>
        static void DumpShape(FsaResult replay, FsaResult original, List<FsaComponentResult> family,
                              double[] mu0, double[] mu1, ResultData rd, int run)
        {
            int channels = replay.Model.Length;
            double[] replayCurve = new double[channels];
            double replayRate = 0.0;
            foreach (FsaComponentResult member in family)
            {
                foreach (FsaComponentResult rc in replay.Components)
                {
                    if (string.Equals(rc.Name, member.Name, StringComparison.Ordinal) && rc.Curve != null)
                    {
                        replayRate = rc.CountRate;
                        for (int i = 0; i < channels; i++)
                        {
                            replayCurve[i] += rc.Curve[i];
                        }
                    }
                }
            }

            if (!(replayRate > 0.0))
            {
                Console.WriteLine("      форма #{0}: компонент в копию не вошёл — ленты нет, сравнивать нечего", run);
                return;
            }

            int lo = Math.Max(0, replay.FirstChannel), hi = Math.Min(channels - 1, replay.LastChannel);
            double sxy = 0.0, sxx = 0.0, syy = 0.0, sumA = 0.0, sumB = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double a = mu1[i] - mu0[i];
                double b = replayCurve[i];
                sxy += a * b; sxx += a * a; syy += b * b; sumA += a; sumB += b;
            }

            double corr = sxx > 0.0 && syy > 0.0 ? sxy / Math.Sqrt(sxx * syy) : double.NaN;
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                            "      форма #{0}: корреляция впрыска с лентой копии {1:F4}; Σ впрыска {2:E3}, Σ ленты копии {3:E3} (имп/с копии {4:E3})",
                            run, corr, sumA, sumB, replayRate);

            var taken = new List<int>();
            for (int pick = 0; pick < 3; pick++)
            {
                int best = -1;
                for (int i = lo; i <= hi; i++)
                {
                    double d = mu1[i] - mu0[i];
                    if (!(d > 0.0)) continue;
                    bool near = false;
                    foreach (int t in taken)
                    {
                        double f = Math.Max(2.0, rd.FwhmCalibration.ChannelToFwhm(t));
                        if (Math.Abs(i - t) <= 2.0 * f) { near = true; break; }
                    }

                    if (near) continue;
                    if (best < 0 || d > mu1[best] - mu0[best]) best = i;
                }

                if (best < 0) break;
                taken.Add(best);
                double fwhm = Math.Max(2.0, rd.FwhmCalibration.ChannelToFwhm(best));
                int a0 = Math.Max(lo, (int)Math.Floor(best - 1.5 * fwhm)), b0 = Math.Min(hi, (int)Math.Ceiling(best + 1.5 * fwhm));
                int bestReplay = a0;
                double injWin = 0.0, repWin = 0.0;
                for (int i = a0; i <= b0; i++)
                {
                    if (replayCurve[i] > replayCurve[bestReplay]) bestReplay = i;
                    injWin += mu1[i] - mu0[i];
                    repWin += replayCurve[i];
                }

                sb.AppendFormat(CultureInfo.InvariantCulture,
                                "; пик {0:F0} кэВ: вершина впрыска кан. {1}, копии кан. {2} (Δ {3:+0;-0} кан., ПШПВ {4:F1}); площадь ±1.5 ПШПВ: впрыск {5:F0}, копия {6:F0} (доля от Σ: {7:F4} / {8:F4})",
                                rd.EnergySpectrum.EnergyCalibration.ChannelToEnergy(best), best, bestReplay, bestReplay - best, fwhm,
                                injWin, repWin, sumA > 0 ? injWin / sumA : double.NaN, sumB > 0 ? repWin / sumB : double.NaN);
            }

            Console.WriteLine(sb.ToString());
        }
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 2. звать DumpShape на впрыске
old = '''                            DumpReplay(replay, analyzer, family, "впрыск", run, key);
                            DumpResidual(replay, drawn, mu0, mu1, rd, "впрыск", run);
                            DumpTrace(lastTrace, traceCalls, library, run);
'''
new = '''                            DumpReplay(replay, analyzer, family, "впрыск", run, key);
                            DumpResidual(replay, drawn, mu0, mu1, rd, "впрыск", run);
                            DumpShape(replay, result, family, mu0, mu1, rd, run);
                            DumpTrace(lastTrace, traceCalls, library, run);
'''
assert s.count(old) == 1; s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
