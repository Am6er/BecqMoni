# П30 (S43, остаток S51): ключ --gamma-map=<каталог прогона> у CorpusFsaProbe —
# γ составного шума КАЖДОМУ спектру равным его же измеренной невязке ε
# (model_residual_pct / 100 из *_spline_runs.csv прежнего прогона). CRLF сохраняется.
import io
p='tools/effmaker/probes/CorpusFsaProbe.cs'
raw=io.open(p,'rb').read()
assert raw.count(b'\r\n')==raw.count(b'\n')
s=raw.decode('utf-8').replace('\r\n','\n')
def rep(a,b):
    global s
    assert s.count(a)==1, a[:70]
    s=s.replace(a,b)
rep("""                else if (a.StartsWith("--beta=", StringComparison.Ordinal))
                {
                    o.NoiseBeta = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
""","""                else if (a.StartsWith("--beta=", StringComparison.Ordinal))
                {
                    o.NoiseBeta = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gamma-map=", StringComparison.Ordinal))
                {
                    // (`S43`, остаток ~~`S51`~~; полоса П30 12.09.2026) γ КАЖДОМУ
                    // спектру — его же измеренная невязка ε прежнего прогона
                    // (`model_residual_pct` из `*_spline_runs.csv` каталога);
                    // прямая проверка гипотезы «ε — это и есть γ, оценённый по
                    // фиту». Спектр без строки в каталоге идёт с `--gamma=`.
                    o.GammaMap = ReadGammaMap(a.Substring(12));
                    if (o.GammaMap == null)
                    {
                        return 2;
                    }
                }
""")
rep("""            /// <summary>(S43) β коррелированности вычитаемого фона; 0 — выключено.</summary>
            public double NoiseBeta;
""","""            /// <summary>(S43) β коррелированности вычитаемого фона; 0 — выключено.</summary>
            public double NoiseBeta;

            /// <summary>(S43) `--gamma-map=`: γ по спектру из невязки ε прежнего прогона; null — нет.</summary>
            public Dictionary<string, double> GammaMap;
""")
rep("""                // (`T65`) Настройки прогона — ОДНИМ местом, тем же, из
                // которого их берёт на печать шапка.
                FsaAnalyzer analyzer = NewAnalyzer(o);
""","""                // (`T65`) Настройки прогона — ОДНИМ местом, тем же, из
                // которого их берёт на печать шапка.
                FsaAnalyzer analyzer = NewAnalyzer(o);
                double gammaMapped;
                if (o.GammaMap != null && o.GammaMap.TryGetValue(sample.Key, out gammaMapped))
                {
                    // (`S43`) γ = ε этого же спектра; печатается, чтобы плечо
                    // нельзя было спутать с глобальным `--gamma=`.
                    analyzer.NoiseGamma = gammaMapped;
                    Console.WriteLine("  {0}: γ по невязке прежнего прогона = {1}",
                                      sample.Key, gammaMapped.ToString("F4", CultureInfo.InvariantCulture));
                }
""")
# читатель карты — рядом с ReadMatter
rep("""        /// <summary>
        /// Только `materials.csv`, и только колонки прибора, — для `--lib=infer`.
""","""        /// <summary>
        /// (`S43`) Карта «спектр → ε» из `*_spline_runs.csv` каталога прежнего
        /// прогона: `model_residual_pct` (проценты) → доля. Строки `ERROR` и
        /// пустые пропускаются. null — каталога нет или строк не нашлось.
        /// </summary>
        static Dictionary<string, double> ReadGammaMap(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine("--gamma-map: каталога нет: " + dir);
                return null;
            }

            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(dir, "*_spline_runs.csv"))
            {
                string[] lines = File.ReadAllLines(file);
                if (lines.Length < 2)
                {
                    continue;
                }

                string[] header = lines[0].TrimStart('\uFEFF').Split(',');
                int iSpec = Array.IndexOf(header, "spectrum");
                int iEps = Array.IndexOf(header, "model_residual_pct");
                if (iSpec < 0 || iEps < 0)
                {
                    continue;
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    string[] cells = SplitCsv(lines[i]);
                    if (cells.Length <= Math.Max(iSpec, iEps))
                    {
                        continue;
                    }

                    double eps;
                    if (double.TryParse(cells[iEps], NumberStyles.Float, CultureInfo.InvariantCulture, out eps)
                        && eps > 0.0)
                    {
                        map[cells[iSpec]] = eps / 100.0;
                    }
                }
            }

            if (map.Count == 0)
            {
                Console.Error.WriteLine("--gamma-map: в каталоге нет строк с невязкой: " + dir);
                return null;
            }

            Console.WriteLine("γ по невязке прежнего прогона (--gamma-map): спектров {0}, каталог {1}", map.Count, dir);
            return map;
        }

        /// <summary>Разбор строки CSV с кавычками (поле `library_note` несёт запятые).</summary>
        static string[] SplitCsv(string line)
        {
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    cells.Add(cur.ToString());
                    cur.Length = 0;
                }
                else
                {
                    cur.Append(c);
                }
            }

            cells.Add(cur.ToString());
            return cells.ToArray();
        }

        /// <summary>
        /// Только `materials.csv`, и только колонки прибора, — для `--lib=infer`.
""")
io.open(p,'wb').write(s.replace('\n','\r\n').encode('utf-8'))
print('ok')
