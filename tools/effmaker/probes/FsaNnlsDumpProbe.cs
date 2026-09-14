using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace FsaNnlsDumpProbe
{
    /// <summary>
    /// Прибор решателя (`A308`): дамп КАЖДОГО вызова `FsaAnalyzer.NnlsSolve`
    /// за один разбор — Грам (уже со штрафом на излом), правая часть, найденное
    /// решение, активные и забаненные колонки, порог, итерации, сходы.
    ///
    ///     fsannlsdumpprobe --out=<каталог> [--keep=N] [--fit-floor=off|adc|<кэВ>] -- <ключи FsaStackShot>
    ///
    /// Разбор гонит САМ `FsaStackShot.exe` (лежит рядом), загруженный
    /// отражением, — с теми же ключами, что и у снимка: сцена, библиотека,
    /// матрица, отчёт — те же, что видит человек, и чужая проба не правится.
    /// Приёмник `FsaAnalyzer.NnlsTraceSink` ставится ДО запуска; после — снимается.
    ///
    /// Выход в `--out=`:
    ///   * `calls.csv` — по строке на вызов: номер, m, итерации, бюджет, сходы,
    ///     активных, забаненных, порог, наибольшая диагональ, значение цели
    ///     f(x) = ½·xᵀGx − cᵀx, нарушение KKT (наибольший градиент w = c − Gx
    ///     среди НЕактивных незабаненных колонок, в долях порога) и наибольший
    ///     |w| среди активных;
    ///   * `calls.bin` — те же вызовы целиком (little-endian): int32 m, m·m double
    ///     Грам построчно, m double c, m double x, m byte active, m byte banned,
    ///     double tol, int32 iterations, int32 budget, int32 drops. Читатель —
    ///     `handover/p31-a308-nnls/read_calls.py`;
    ///   * `stackshot.txt` — что напечатал `FsaStackShot` (stdout + stderr).
    ///
    /// `--fit-floor=` — пол полосы ФИТА (`A302`): та же статика
    /// `FsaBand.DefaultFitFloor`, тот же разбор `TryParseFitFloor`, что у
    /// `CorpusFsaProbe`; у `FsaStackShot` своего ключа нет, а замер П31
    /// показал, что обвал чистого фита на сцене Amber держат ровно каналы
    /// ниже порога АЦП (данных 0, дисперсия 1, образы ненулевые).
    ///
    /// `--keep=N` — писать в `calls.bin` только ПОСЛЕДНИЕ N вызовов (сводка
    /// `calls.csv` — всегда все); 0 — все. Последний вызов — финальный фит
    /// разбора, он и есть решение, которое видит экран.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            string outDir = null;
            int keep = 0;
            var rest = new List<string>();
            bool passthrough = false;
            foreach (string a in args)
            {
                if (passthrough)
                {
                    rest.Add(a);
                }
                else if (a == "--")
                {
                    passthrough = true;
                }
                else if (a.StartsWith("--out=", StringComparison.Ordinal))
                {
                    outDir = a.Substring(6);
                }
                else if (a.StartsWith("--keep=", StringComparison.Ordinal))
                {
                    keep = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--fit-floor=", StringComparison.Ordinal))
                {
                    FsaFitFloor fitSource;
                    double fitKev;
                    if (!FsaBand.TryParseFitFloor(a.Substring(12), out fitSource, out fitKev))
                    {
                        Console.Error.WriteLine("неизвестное значение --fit-floor=: " + a.Substring(12) + " (off | adc | <кэВ>)");
                        return 2;
                    }

                    FsaBand.DefaultFitFloor = fitSource;
                    FsaBand.DefaultFitFloorKev = fitKev;
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a + " (ключи FsaStackShot — после `--`)");
                    return 2;
                }
            }

            if (string.IsNullOrEmpty(outDir) || rest.Count == 0)
            {
                Console.Error.WriteLine("нужны --out=<каталог> и ключи FsaStackShot после `--`");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            string probeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string shotPath = Path.Combine(probeDir, "FsaStackShot.exe");
            if (!File.Exists(shotPath))
            {
                Console.Error.WriteLine("рядом нет FsaStackShot.exe: " + shotPath);
                return 3;
            }

            var summary = new List<string>();
            summary.Add("call,m,iterations,budget,drops,active,banned,tol,maxdiag,objective,kkt_max_w_inactive,kkt_over_tol,active_max_abs_w,exhausted");
            var records = new List<byte[]>();
            int calls = 0;

            // Приёмник ставится ДО загрузки чужой сборки: BecquerelMonitor.exe
            // тем самым уже поднят в основном контексте, и FsaStackShot.exe
            // (LoadFrom) привяжется к ТОЙ ЖЕ сборке — статика одна на двоих.
            FsaAnalyzer.NnlsTraceSink = trace =>
            {
                calls++;
                int m = trace.X.Length;
                double maxdiag = 0.0;
                for (int k = 0; k < m; k++)
                {
                    if (trace.Gram[k, k] > maxdiag)
                    {
                        maxdiag = trace.Gram[k, k];
                    }
                }

                // Цель и градиент — по тому же Граму, что решал решатель.
                double objective = 0.0;
                double kktMax = double.NegativeInfinity;
                double activeMax = 0.0;
                int nActive = 0, nBanned = 0;
                for (int a = 0; a < m; a++)
                {
                    double gx = 0.0;
                    for (int b = 0; b < m; b++)
                    {
                        gx += trace.Gram[a, b] * trace.X[b];
                    }

                    objective += 0.5 * trace.X[a] * gx - trace.C[a] * trace.X[a];
                    double w = trace.C[a] - gx;
                    if (trace.Active[a])
                    {
                        nActive++;
                        if (Math.Abs(w) > activeMax)
                        {
                            activeMax = Math.Abs(w);
                        }
                    }
                    else if (!trace.Banned[a] && w > kktMax)
                    {
                        kktMax = w;
                    }

                    if (trace.Banned[a])
                    {
                        nBanned++;
                    }
                }

                summary.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13}",
                    calls, m, trace.Iterations, trace.Budget, trace.Drops, nActive, nBanned,
                    trace.Tol, maxdiag, objective,
                    double.IsNegativeInfinity(kktMax) ? 0.0 : kktMax,
                    double.IsNegativeInfinity(kktMax) ? 0.0 : kktMax / trace.Tol,
                    activeMax, trace.Iterations >= trace.Budget ? 1 : 0));

                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(m);
                    for (int a = 0; a < m; a++)
                    {
                        for (int b = 0; b < m; b++)
                        {
                            bw.Write(trace.Gram[a, b]);
                        }
                    }

                    for (int a = 0; a < m; a++)
                    {
                        bw.Write(trace.C[a]);
                    }

                    for (int a = 0; a < m; a++)
                    {
                        bw.Write(trace.X[a]);
                    }

                    for (int a = 0; a < m; a++)
                    {
                        bw.Write((byte)(trace.Active[a] ? 1 : 0));
                    }

                    for (int a = 0; a < m; a++)
                    {
                        bw.Write((byte)(trace.Banned[a] ? 1 : 0));
                    }

                    bw.Write(trace.Tol);
                    bw.Write(trace.Iterations);
                    bw.Write(trace.Budget);
                    bw.Write(trace.Drops);
                    bw.Flush();
                    records.Add(ms.ToArray());
                }

                if (keep > 0 && records.Count > keep)
                {
                    records.RemoveAt(0);
                }
            };

            int code;
            string shotOut;
            TextWriter savedOut = Console.Out;
            TextWriter savedErr = Console.Error;
            var capture = new StringWriter();
            try
            {
                // Вывод снимка — и в файл, и на экран: проба обязана говорить то же,
                // что говорил бы сам FsaStackShot.
                var tee = new TeeWriter(savedOut, capture);
                Console.SetOut(tee);
                Console.SetError(new TeeWriter(savedErr, capture));
                Assembly shot = Assembly.LoadFrom(shotPath);
                MethodInfo entry = shot.EntryPoint;
                object result = entry.Invoke(null, new object[] { rest.ToArray() });
                code = result is int ? (int)result : 0;
            }
            catch (TargetInvocationException ex)
            {
                Console.SetOut(savedOut);
                Console.SetError(savedErr);
                Console.Error.WriteLine("FsaStackShot упал: " + ex.InnerException);
                code = 4;
            }
            finally
            {
                Console.SetOut(savedOut);
                Console.SetError(savedErr);
                FsaAnalyzer.NnlsTraceSink = null;
                shotOut = capture.ToString();
            }

            File.WriteAllText(Path.Combine(outDir, "stackshot.txt"), shotOut, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(outDir, "calls.csv"), summary, new UTF8Encoding(false));
            using (var fs = File.Create(Path.Combine(outDir, "calls.bin")))
            {
                foreach (byte[] r in records)
                {
                    fs.Write(r, 0, r.Length);
                }
            }

            Console.WriteLine("nnls-dump: вызовов {0}, записано целиком {1}, код снимка {2}, каталог {3}",
                              calls, records.Count, code, outDir);
            return code;
        }

        sealed class TeeWriter : TextWriter
        {
            readonly TextWriter a, b;

            public TeeWriter(TextWriter a, TextWriter b)
            {
                this.a = a;
                this.b = b;
            }

            public override Encoding Encoding
            {
                get { return a.Encoding; }
            }

            public override void Write(char value)
            {
                a.Write(value);
                b.Write(value);
            }

            public override void Write(string value)
            {
                a.Write(value);
                b.Write(value);
            }

            public override void WriteLine(string value)
            {
                a.WriteLine(value);
                b.WriteLine(value);
            }

            public override void Flush()
            {
                a.Flush();
                b.Flush();
            }
        }
    }
}
