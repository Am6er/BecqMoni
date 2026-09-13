import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\CorpusFsaProbe.cs'
s = io.open(p, encoding='utf-8', newline='').read()
assert s.count(chr(13)+chr(10)) == s.count(chr(10))
s = s.replace(chr(13)+chr(10), chr(10))

# трасса решателя на впрыске: ставим приёмник вокруг RunSynthetic для дампуемых розыгрышей
old = '''                    int[] drawn;
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
new = '''                    int[] drawn;
                    FsaAnalyzer.NnlsTrace lastTrace = null;
                    int traceCalls = 0;
                    if (run < o.McDump)
                    {
                        FsaAnalyzer.NnlsTraceSink = t => { lastTrace = t; traceCalls++; };
                    }

                    try
                    {
                        replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                              mu1, rng, out drawn);
                    }
                    finally
                    {
                        FsaAnalyzer.NnlsTraceSink = null;
                    }

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
                            DumpTrace(lastTrace, traceCalls, library, run);
                        }
                    }
'''
assert s.count(old) == 1; s = s.replace(old, new)

old = '''        /// <summary>
        /// (`S106`, П46) Куда делся впрыск — по невязке копии. Печатает
'''
new = '''        /// <summary>
        /// (`S106`, П46) Последний вызов решателя копии (финальный фит): по
        /// каждой колонке — решение x, активна / забанена, градиент
        /// w = c − G·x при решении, диагональ Грама и правая часть. Первые
        /// колонки идут в порядке библиотеки (после матричного образа — его
        /// подпороговый хвост без компонента), дальше — шапки континуума и
        /// прочие готовые колонки; имена печатаются по порядку библиотеки как
        /// ПОДСКАЗКА, не как факт (колонки без образа пропущены решателем).
        /// </summary>
        static void DumpTrace(FsaAnalyzer.NnlsTrace t, int calls, List<FsaComponent> library, int run)
        {
            if (t == null)
            {
                Console.WriteLine("      трасса #{0}: решатель не звался", run);
                return;
            }

            int m = t.X.Length;
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                            "      трасса #{0}: вызовов решателя {1}; последний: колонок {2}, итераций {3}/{4}, сходов {5}, порог {6:E2}; библиотека:",
                            run, calls, m, t.Iterations, t.Budget, t.Drops, t.Tol);
            foreach (FsaComponent component in library)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, " {0}[{1}{2}]", component.Name, component.Kind,
                                component.Derived ? ",произв." : "");
            }

            Console.WriteLine(sb.ToString());
            int active = 0, banned = 0;
            for (int k = 0; k < m; k++)
            {
                if (t.Active[k]) active++;
                if (t.Banned[k]) banned++;
            }

            Console.WriteLine("        активных {0}, забанено {1}; первые 12 колонок (индекс: x, состояние, w=c−Gx, G_kk, c_k):", active, banned);
            for (int k = 0; k < Math.Min(12, m); k++)
            {
                double gx = 0.0;
                for (int b = 0; b < m; b++)
                {
                    if (t.X[b] != 0.0)
                    {
                        gx += t.Gram[k, b] * t.X[b];
                    }
                }

                Console.WriteLine("        {0,3}: x={1:E3} {2}{3} w={4:E3} G={5:E3} c={6:E3}",
                                  k, t.X[k], t.Active[k] ? "АКТ" : "нет", t.Banned[k] ? " БАН" : "",
                                  t.C[k] - gx, t.Gram[k, k], t.C[k]);
            }
        }

        /// <summary>
        /// (`S106`, П46) Куда делся впрыск — по невязке копии. Печатает
'''
assert s.count(old) == 1; s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(chr(10), chr(13)+chr(10)))
print('ok')
