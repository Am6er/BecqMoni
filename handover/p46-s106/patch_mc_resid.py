import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

# 1. RunSynthetic отдаёт разыгранный спектр
old = '''        static FsaResult RunSynthetic(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                      FsaAnalyzer analyzer, FsaEfficiency efficiency,
                                      double[] mean, Random rng)
        {
            EnergySpectrum synthetic = rd.EnergySpectrum.Clone();
            int[] counts = synthetic.Spectrum;
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = SamplePoisson(rng, mean[i]);
            }

            return analyzer.Analyze(synthetic, background, rd.FwhmCalibration, library, efficiency);
        }
'''
new = '''        static FsaResult RunSynthetic(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                      FsaAnalyzer analyzer, FsaEfficiency efficiency,
                                      double[] mean, Random rng)
        {
            int[] drawn;
            return RunSynthetic(rd, background, library, analyzer, efficiency, mean, rng, out drawn);
        }

        /// <summary>То же, но с разыгранными отсчётами наружу — для дампа невязки (`--mc-dump`).</summary>
        static FsaResult RunSynthetic(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                      FsaAnalyzer analyzer, FsaEfficiency efficiency,
                                      double[] mean, Random rng, out int[] drawn)
        {
            EnergySpectrum synthetic = rd.EnergySpectrum.Clone();
            int[] counts = synthetic.Spectrum;
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = SamplePoisson(rng, mean[i]);
            }

            drawn = counts;
            return analyzer.Analyze(synthetic, background, rd.FwhmCalibration, library, efficiency);
        }

        /// <summary>
        /// (`S106`, П46) Куда делся впрыск — по невязке копии. Печатает
        /// согласованный отклик остатка на форму впрыска (1 — весь впрыск
        /// остался в остатке, 0 — фит его целиком куда-то забрал) и по трём
        /// самым сильным пикам формы впрыска (окно ±1 ПШПВ): впрыснуто отсчётов,
        /// остаток фита, на сколько подложка копии поднялась над нулевой
        /// моделью, на сколько поднялась вся модель копии.
        /// </summary>
        static void DumpResidual(FsaResult replay, int[] drawn, double[] mu0, double[] mu1,
                                 ResultData rd, string label, int run)
        {
            int channels = replay.Model.Length;
            int lo = Math.Max(0, replay.FirstChannel), hi = Math.Min(channels - 1, replay.LastChannel);
            double[] r = new double[channels];
            double num = 0.0, den = 0.0, injectedTotal = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double bg = replay.Background != null ? replay.Background[i] : 0.0;
                r[i] = drawn[i] - bg - replay.Model[i];
                double d = mu1[i] - mu0[i];
                double v = Math.Max(1.0, mu1[i]);
                num += r[i] * d / v;
                den += d * d / v;
                injectedTotal += d;
            }

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                            "      невязка {0} #{1}: впрыснуто {2:E3} отсч.; в остатке осталось {3:F3} впрыска (согласованный отклик)",
                            label, run, injectedTotal, den > 0.0 ? num / den : double.NaN);

            // Три самых сильных пика формы впрыска: локальные максимумы d по окну ±1 ПШПВ.
            double[] d2 = new double[channels];
            for (int i = lo; i <= hi; i++)
            {
                d2[i] = mu1[i] - mu0[i];
            }

            var taken = new List<int>();
            for (int pick = 0; pick < 3; pick++)
            {
                int best = -1;
                for (int i = lo; i <= hi; i++)
                {
                    if (!(d2[i] > 0.0))
                    {
                        continue;
                    }

                    bool near = false;
                    foreach (int t in taken)
                    {
                        double f = Math.Max(2.0, rd.FwhmCalibration.ChannelToFwhm(t));
                        if (Math.Abs(i - t) <= 2.0 * f)
                        {
                            near = true;
                            break;
                        }
                    }

                    if (near)
                    {
                        continue;
                    }

                    if (best < 0 || d2[i] > d2[best])
                    {
                        best = i;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                taken.Add(best);
                double fwhm = Math.Max(2.0, rd.FwhmCalibration.ChannelToFwhm(best));
                int a = Math.Max(lo, (int)Math.Floor(best - fwhm)), b = Math.Min(hi, (int)Math.Ceiling(best + fwhm));
                double inj = 0.0, res = 0.0, contRise = 0.0, modelRise = 0.0;
                for (int i = a; i <= b; i++)
                {
                    double bg = replay.Background != null ? replay.Background[i] : 0.0;
                    inj += d2[i];
                    res += r[i];
                    contRise += replay.Continuum[i] + bg - mu0[i];
                    modelRise += replay.Model[i] + bg - mu0[i];
                }

                sb.AppendFormat(CultureInfo.InvariantCulture,
                                "; пик {0:F0} кэВ (кан. {1}..{2}): впрыск {3:F0}, остаток {4:F0}, подложка +{5:F0}, модель +{6:F0}",
                                rd.EnergySpectrum.EnergyCalibration.ChannelToEnergy(best), a, b, inj, res, contRise, modelRise);
            }

            Console.WriteLine(sb.ToString());
        }
'''
assert s.count(old) == 1; s = s.replace(old, new)

# 2. в цикле семьи — брать drawn и печатать невязку
old = '''                    replay = RunSynthetic(rd, background, library, analyzer, efficiency,
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
'''
new = '''                    int[] drawn;
                    replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                          mu1, rng, out drawn);
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
                            DumpResidual(replay, drawn, mu0, mu1, rd, "впрыск", run);
                        }
                    }
'''
assert s.count(old) == 1; s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
