using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MatrixAuditProbe
{
    /// <summary>
    /// ПРИЁМКА СКЛАДА МАТРИЦ: все `.rmx` каталога, четыре признака у каждой.
    ///
    /// ЗАЧЕМ. Приёмку склада нельзя вести ни датой файла, ни их числом. Замер
    /// 02.09.2026: все 44 корпусные матрицы лежали на месте, свежие, с верным
    /// числом узлов — и все негодные, потому что клеймо несло `phys=12` против
    /// нынешней 14. В тот же день пересборка корпуса выглядела прошедшей по
    /// времени файлов, а на деле упала на первом шаге. Свежесть и наличие
    /// признаками не являются; признаки лежат ВНУТРИ файла, и их четыре:
    /// клеймо, число узлов, число историй и достигнутый шум.
    ///
    /// ⛔ Своего разборщика формата здесь НЕТ нарочно: матрица читается тем же
    /// `ResponseMatrix.Load`, каким её читает приложение. Второй разборщик
    /// однажды разойдётся с первым молча (`S37`), и приёмка станет проверять
    /// не то, что грузится.
    ///
    ///     matrixauditprobe --dir=&lt;каталог с .rmx&gt; [--phys=14] [--hist=3000000]
    ///                      [--noise=1.0] [--cone-on=A,B] [--except=X:12000000]
    ///                      [--csv=out.csv] [--quiet]
    ///
    /// `--phys=` и `--hist=` — чего ЖДЁМ от склада; несовпадение печатается
    /// находкой. `--noise=` — порог достигнутого шума узла, выше которого узел
    /// называется шумным. Код возврата 1, если находки есть.
    ///
    /// ⛔ Порог шума применяется ТОЛЬКО к судимым узлам — от 50 кэВ и выше, и
    /// с шумом ниже 99 % (`A80`). Нижние узлы 5…20 кэВ дают ровно 100 % у
    /// любой матрицы, включая безупречную: континууму там лечь некуда. Они
    /// считаются отдельно и печатаются строкой «континуума нет», иначе
    /// приёмка отказывает всегда и перестаёт что-либо значить.
    ///
    /// `--cone-on=` — ПОИМЁННЫЙ список сцен, у которых конус обязан стоять; у
    /// всех прочих он обязан отсутствовать. Проверка двусторонняя нарочно:
    /// критерий «источник вне габарита сцены» считается кодом, и поймать надо
    /// не только «забыл включить», но и «включился там, где не должен» — иначе
    /// врущий критерий уедет в базу вместе с числами.
    ///
    /// `--except=&lt;сцена&gt;:&lt;историй&gt;` — сцена, которой историй положено ИНОЕ
    /// число (её считают отдельным прогоном). Можно повторять.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string dir = null, csv = null, nodesCsv = null;
            int wantPhys = 0, wantHist = 0;
            double noiseLimit = 1.0;
            bool quiet = false;
            var coneOnly = new List<string>();
            var except = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in args)
            {
                if (a == "--quiet") { quiet = true; continue; }
                if (a.StartsWith("--cone-on=", StringComparison.Ordinal))
                {
                    foreach (string s in a.Substring(10).Split(','))
                    {
                        if (s.Trim().Length > 0) coneOnly.Add(s.Trim());
                    }

                    continue;
                }

                if (a.StartsWith("--except=", StringComparison.Ordinal))
                {
                    string[] kv = a.Substring(9).Split(':');
                    if (kv.Length != 2)
                    {
                        Console.Error.WriteLine("--except= ждёт <сцена>:<историй>");
                        return 2;
                    }

                    except[kv[0]] = int.Parse(kv[1], CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
                else if (a.StartsWith("--phys=", StringComparison.Ordinal)) wantPhys = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--hist=", StringComparison.Ordinal)) wantHist = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--noise=", StringComparison.Ordinal)) noiseLimit = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodesCsv = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (dir == null || !Directory.Exists(dir))
            {
                Console.Error.WriteLine("нужен --dir=<каталог с .rmx>");
                return 2;
            }

            string[] files = Directory.GetFiles(dir, "*.rmx");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0)
            {
                Console.Error.WriteLine("в каталоге нет ни одной матрицы: " + dir);
                return 2;
            }

            var findings = new List<string>();
            var lines = new List<string>();
            var nodeLines = new List<string>();
            var physCount = new Dictionary<string, int>();
            var histCount = new Dictionary<int, int>();
            int noisy = 0, coneOn = 0, emptyTotal = 0;

            Console.WriteLine("склад: {0}", Path.GetFullPath(dir));
            Console.WriteLine("матриц: {0}", files.Length);
            Console.WriteLine();
            if (!quiet)
            {
                Console.WriteLine("{0,-38} {1,5} {2,6} {3,10} {4,5} {5,8} {6,8} {7,5}",
                                  "файл", "форм", "phys", "историй", "узл", "шум мед", "шум худш", "конус");
                Console.WriteLine(new string('-', 100));
            }

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                ResponseMatrix m;
                try
                {
                    m = ResponseMatrix.Load(path);
                }
                catch (Exception ex)
                {
                    findings.Add(name + ": НЕ ЧИТАЕТСЯ — " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                if (m == null)
                {
                    // ⛔ Load возвращает null молча, когда формат чужой (`A50`).
                    findings.Add(name + ": ОТВЕРГНУТА загрузчиком (чужой формат или обрубок)");
                    continue;
                }

                string phys = PhysOf(m.Stamp);
                int nodes = m.Energies != null ? m.Energies.Length : 0;
                double med = Median(m.NodeErrors);
                int empty;
                double worst = Worst(m.NodeErrors, m.Energies, out empty);
                emptyTotal += empty;
                bool cone = m.Options != null && m.Options.AnalogConeSampling;
                if (cone) coneOn++;

                physCount[phys] = physCount.TryGetValue(phys, out int pc) ? pc + 1 : 1;
                histCount[m.Histories] = histCount.TryGetValue(m.Histories, out int hc) ? hc + 1 : 1;

                if (wantPhys != 0 && phys != wantPhys.ToString(CultureInfo.InvariantCulture))
                {
                    findings.Add(name + ": физика " + phys + ", а ждали " + wantPhys);
                }

                string key = Path.GetFileNameWithoutExtension(path);
                int expectHist = except.TryGetValue(key, out int special) ? special : wantHist;
                if (expectHist != 0 && m.Histories != expectHist)
                {
                    findings.Add(name + ": историй " + m.Histories.ToString(CultureInfo.InvariantCulture)
                                 + ", а ждали " + expectHist.ToString(CultureInfo.InvariantCulture));
                }

                // ⛔ Конус проверяется В ОБЕ СТОРОНЫ: и «нет там, где нужен», и
                // «есть там, где не должен». Второе важнее — оно означает, что
                // врёт сам критерий отбора сцены, а не один прогон.
                if (coneOnly.Count > 0)
                {
                    bool wanted = Names(coneOnly, key);
                    if (wanted && !cone)
                    {
                        findings.Add(name + ": КОНУС НЕ ВКЛЮЧЁН, а сцена в списке дальних");
                    }
                    else if (!wanted && cone)
                    {
                        findings.Add(name + ": КОНУС ВКЛЮЧЁН, хотя сцены нет в списке дальних — критерий отбора врёт");
                    }
                }

                if (nodes == 0)
                {
                    findings.Add(name + ": НИ ОДНОГО УЗЛА");
                }

                if (worst > noiseLimit)
                {
                    noisy++;
                    findings.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: шум худшего узла {1:F2} % при пороге {2:F2} %", name, worst, noiseLimit));
                }

                if (!quiet)
                {
                    Console.WriteLine("{0,-38} {1,5} {2,6} {3,10} {4,5} {5,7:F2}% {6,7:F2}% {7,5}",
                                      Trim(name, 38), ResponseMatrix.FormatVersion, phys,
                                      m.Histories, nodes, med, worst, cone ? "да" : "нет");
                }

                // ⛔ ПОУЗЛОВОЙ СРЕЗ. Нужен, чтобы проверить, постоянна ли по
                // ЭНЕРГИИ величина k = шум·√(n·ε), — а не только по сценам.
                // Замер таблицы шума снят на одной энергии (662 кэВ), и
                // переносить её константу на узлы от 5 до 3000 кэВ без проверки
                // нельзя: шум после свёртки идёт от числа событий В ОКНЕ, окно
                // растёт примерно как √E, а ε с энергией падает — два действия
                // в разные стороны, и их отношение постоянным быть не обязано.
                //
                // ε узла берётся из САМОЙ строки отклика: сумма по каналам и
                // бинам и есть доля историй, давших отсчёт.
                // ⛔ (`A81`) k СЧИТАЕТСЯ ВСЕГДА И ВСЕГДА СУДИТСЯ — раньше он
                // только выгружался в CSV, и то лишь по ключу `--nodes=`.
                // Правило §11.3 («узлы выше 50 кэВ судятся по k своей полосы,
                // порог — отклонение вдвое») оставалось без исполнителя, и
                // приёмка проверяла происхождение склада, но не его качество.
                if (m.Energies != null)
                {
                    // ПРОХОД 1: k по всем узлам. Ноль означает «узел не
                    // судится» — ниже 50 кэВ или континуума нет (`A80`).
                    var ks = new List<double>(m.Energies.Length);
                    for (int i = 0; i < m.Energies.Length; i++)
                    {
                        double eps = RowSum(m.ChannelRows, i);
                        long hist = m.NodeHistories != null && i < m.NodeHistories.Length
                            ? m.NodeHistories[i] : m.Histories;
                        double err = m.NodeErrors != null && i < m.NodeErrors.Length
                            ? m.NodeErrors[i] : double.NaN;
                        double k = eps > 0.0 && hist > 0L
                            ? err * Math.Sqrt(hist * eps) : double.NaN;
                        bool judged = !double.IsNaN(k) && k > 0.0
                            && err < EmptyNoisePct && m.Energies[i] >= JudgeFromKev;
                        ks.Add(judged ? k : 0.0);

                        if (nodesCsv != null)
                        {
                            nodeLines.Add(string.Join(",", new[]
                            {
                                key,
                                m.Energies[i].ToString("F3", CultureInfo.InvariantCulture),
                                eps.ToString("E6", CultureInfo.InvariantCulture),
                                hist.ToString(CultureInfo.InvariantCulture),
                                err.ToString("F4", CultureInfo.InvariantCulture),
                                k.ToString("F2", CultureInfo.InvariantCulture)
                            }));
                        }
                    }

                    // ПРОХОД 2: каждый судимый узел — против скользящей медианы
                    // СВОИХ соседей (`A82`), а не против таблицы, снятой с чужой
                    // геометрии.
                    int offBand = 0;
                    string offWorst = null;
                    double offRatio = 1.0;
                    for (int i = 0; i < ks.Count; i++)
                    {
                        if (ks[i] <= 0.0)
                        {
                            continue;
                        }

                        double local = LocalMedian(ks, i);
                        if (local <= 0.0)
                        {
                            continue;
                        }

                        double ratio = ks[i] / local;
                        if (ratio > KBandFactor || ratio < 1.0 / KBandFactor)
                        {
                            offBand++;
                            if (Math.Abs(Math.Log(ratio)) > Math.Abs(Math.Log(offRatio)))
                            {
                                offRatio = ratio;
                                offWorst = string.Format(CultureInfo.InvariantCulture,
                                    "{0:F1} кэВ: k {1:F0} против {2:F0} у соседей ({3:F1}x)",
                                    m.Energies[i], ks[i], local, ratio);
                            }
                        }
                    }

                    if (offBand > 0)
                    {
                        findings.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}: {1} узлов выбиваются из хода k своей матрицы (порог {2:F0}x), худший — {3}",
                            name, offBand, KBandFactor, offWorst));
                    }
                }

                lines.Add(string.Join(",", new[]
                {
                    name, phys, m.Histories.ToString(CultureInfo.InvariantCulture),
                    nodes.ToString(CultureInfo.InvariantCulture),
                    med.ToString("F4", CultureInfo.InvariantCulture),
                    worst.ToString("F4", CultureInfo.InvariantCulture),
                    empty.ToString(CultureInfo.InvariantCulture),
                    cone ? "1" : "0",
                    m.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    m.BuildSeconds.ToString("F1", CultureInfo.InvariantCulture),
                    (m.Stamp ?? "")
                }));
            }

            Console.WriteLine();
            Console.WriteLine("  версия физики: {0}", Join(physCount));
            Console.WriteLine("  историй на узел: {0}", Join(histCount));
            Console.WriteLine("  с конусом: {0} из {1}", coneOn, files.Length);
            Console.WriteLine("  шумных (худший СУДИМЫЙ узел выше {0:F2} %): {1}", noiseLimit, noisy);

            // `A80`: не находка, а сторож нижней части шкалы — узлы 5…20 кэВ,
            // где континууму лечь некуда, есть у КАЖДОЙ матрицы. Числом они
            // всё равно должны быть на виду: «континуума нет» у половины
            // шкалы означало бы, что сломан не порог, а счёт.
            Console.WriteLine("  континуума нет (шум ≥ {0:F0} %): {1} узлов по складу, судятся узлы от {2:F0} кэВ",
                              EmptyNoisePct, emptyTotal, JudgeFromKev);

            if (csv != null)
            {
                using (var w = new StreamWriter(csv, false, new UTF8Encoding(true)))
                {
                    w.WriteLine("file,phys,histories,nodes,noise_median,noise_worst,nodes_empty,cone,created,seconds,stamp");
                    foreach (string l in lines)
                    {
                        w.WriteLine(l);
                    }
                }

                Console.WriteLine("  таблица: {0}", csv);
            }

            if (nodesCsv != null)
            {
                using (var w = new StreamWriter(nodesCsv, false, new UTF8Encoding(true)))
                {
                    w.WriteLine("scene,energy_kev,eps,histories,noise_pct,k");
                    foreach (string l in nodeLines)
                    {
                        w.WriteLine(l);
                    }
                }

                Console.WriteLine("  узлы: {0} строк в {1}", nodeLines.Count, nodesCsv);
            }

            Console.WriteLine();
            if (findings.Count == 0)
            {
                Console.WriteLine("СОШЛОСЬ: все {0} матрицы отвечают ожиданиям", files.Length);
                return 0;
            }

            Console.WriteLine("НАХОДОК: {0}", findings.Count);
            foreach (string f in findings)
            {
                Console.WriteLine("  " + f);
            }

            return 1;
        }

        /// <summary>
        /// Полная эффективность узла — сумма его строки отклика по ВСЕМ каналам
        /// и бинам. Строка нормирована на историю, поэтому сумма и есть доля
        /// историй, давших отсчёт.
        /// </summary>
        static double RowSum(float[][][] channels, int node)
        {
            if (channels == null)
            {
                return 0.0;
            }

            double sum = 0.0;
            foreach (float[][] channel in channels)
            {
                if (channel == null || node >= channel.Length || channel[node] == null)
                {
                    continue;
                }

                foreach (float v in channel[node])
                {
                    sum += v;
                }
            }

            return sum;
        }

        /// <summary>Есть ли имя в списке, без оглядки на регистр.</summary>
        static bool Names(List<string> list, string key)
        {
            foreach (string s in list)
            {
                if (string.Equals(s, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Версия физики из клейма — она в нём первой парой `phys=…`.</summary>
        static string PhysOf(string stamp)
        {
            if (string.IsNullOrEmpty(stamp))
            {
                return "?";
            }

            foreach (string part in stamp.Split(';'))
            {
                if (part.StartsWith("phys=", StringComparison.Ordinal))
                {
                    return part.Substring(5);
                }
            }

            return "?";
        }

        static double Median(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return double.NaN;
            }

            double[] copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }

        /// <summary>
        /// ⛔ (`A80`) ХУДШИЙ УЗЕЛ СЧИТАЕТСЯ ТОЛЬКО ПО СУДИМЫМ.
        ///
        /// Правила приёмки, на которых сошлись 02.09.2026 (§11.3 журнала
        /// `handover-2026-09-02-detector-physics.md`), говорят две вещи, и
        /// первая редакция пробы не исполняла ни одной: узлы НИЖЕ 50 кэВ по
        /// закону k(E) не судятся вовсе (для них проверяется только, что
        /// континуум набрался), а узлы с шумом ≥ 99 % — это «континуума нет»,
        /// отдельная строка, а НЕ находка.
        ///
        /// Без этого приёмка отказывала ВСЕГДА: нижние узлы 5…20 кэВ дают
        /// ровно 100 % у каждой матрицы, и код возврата 1 приходил и на
        /// безупречный склад. Сторож, который отказывает всегда, не значит
        /// ничего — он приучает не читать свой отказ.
        ///
        /// <paramref name="empty"/> — сколько узлов оказалось пустыми; они
        /// печатаются отдельной строкой, чтобы «континуума нет у половины
        /// шкалы» не осталось незамеченным.
        /// </summary>
        const double JudgeFromKev = 50.0;
        const double EmptyNoisePct = 99.0;

        /// <summary>
        /// Во сколько раз узел может отойти от медианы своей полосы, прежде
        /// чем стать находкой (`A81`, §11.3 журнала). Внутриполосный разброс
        /// самой таблицы 1.1…1.4, так что двойное отклонение — заведомо не
        /// природа шума, а происшествие.
        /// </summary>
        const double KBandFactor = 2.0;

        /// <summary>
        /// ⛔ ЭТАЛОННЫЕ k ПО ПОЛОСАМ — СНЯТЫ С `AS80_lu_front`, матрицы,
        /// посчитанной РАНЬШЕ тех, что ею принимаются (§11.2, правило против
        /// круга: основа приёмки по её ходу не пересматривается, иначе склад
        /// начнёт проверять сам себя и сойдётся всегда).
        ///
        /// k = шум·√(n·ε). Ниже 50 кэВ закон не работает вовсе — там медиана
        /// 1090 при разбросе 251…1742, и полоса не возвращается.
        /// </summary>
        /// <summary>Полуширина окна скользящей медианы k, в узлах (`A82`).</summary>
        const int KWindow = 5;

        /// <summary>
        /// ⛔ (`A82`) ОПОРА ДЛЯ УЗЛА — СКОЛЬЗЯЩАЯ МЕДИАНА k ЕГО СОСЕДЕЙ, А НЕ
        /// АБСОЛЮТНАЯ ТАБЛИЦА ПО ПОЛОСАМ.
        ///
        /// Таблица §11.2 снята с `AS80_lu_front` и между сценами не работает:
        /// замер 02.09.2026 в полосе 60…160 кэВ дал у эталона k = 236…370, а у
        /// `AS80_point0` в той же полосе 284 → 753 — плавно и монотонно. Это не
        /// выброс, а свойство геометрии (ε 0.38 против 0.07; у близкой точечной
        /// сцены вклады историй распределены шире). Абсолютный порог поэтому
        /// объявлял бы находки на безупречных матрицах — ровно `A80` в другой
        /// форме.
        ///
        /// Скользящая медиана ловит то, что и должна ловить приёмка: ВЫБРОС
        /// одного узла на гладком ходе k(E) СВОЕЙ матрицы. Случай «шумная вся
        /// матрица» этим критерием не ловится намеренно — его ловят число
        /// историй в клейме и порог `--noise=`, и путать их нельзя.
        /// </summary>
        static double LocalMedian(System.Collections.Generic.List<double> ks, int at)
        {
            var window = new System.Collections.Generic.List<double>();
            for (int i = at - KWindow; i <= at + KWindow; i++)
            {
                if (i < 0 || i >= ks.Count || i == at) continue;
                if (ks[i] > 0.0 && !double.IsNaN(ks[i])) window.Add(ks[i]);
            }

            if (window.Count < 3)
            {
                return 0.0;                 // судить не по чему — молчим
            }

            window.Sort();
            return window[window.Count / 2];
        }

        static double Worst(double[] values, double[] energies, out int empty)
        {
            empty = 0;
            if (values == null || values.Length == 0)
            {
                return double.NaN;
            }

            double top = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                double v = values[i];
                if (v >= EmptyNoisePct)
                {
                    empty++;
                    continue;
                }

                // Энергии может не быть (обрубок) — тогда судим по всем, что
                // есть: пропустить проверку молча хуже, чем судить строже.
                if (energies != null && i < energies.Length && energies[i] < JudgeFromKev)
                {
                    continue;
                }

                if (v > top)
                {
                    top = v;
                }
            }

            return top;
        }

        static string Trim(string s, int n)
        {
            return s.Length <= n ? s : s.Substring(0, n);
        }

        static string Join<T>(Dictionary<T, int> counts)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<T, int> pair in counts)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} — {1}", pair.Key, pair.Value));
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts.ToArray());
        }
    }
}
