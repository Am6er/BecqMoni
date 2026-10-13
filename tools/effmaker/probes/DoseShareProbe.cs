using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace DoseShareProbe
{
    /// <summary>
    /// Полоса П159 (24.09.2026), `S194`: НЕПРЕРЫВНО ЛИ ПОКАЗАНИЕ ДОЗЫ ПО ДОЛЕ
    /// ПРИПИСАННОГО — ПО НАБОРУ СЧЁТА.
    ///
    /// Представительная энергия диапазона (`AMBER77`) берётся по центру тяжести
    /// приписанных отсчётов, только если приписанное — не меньше
    /// <see cref="DoseRateManager.RepresentativeMinShare"/> отсчётов диапазона;
    /// ниже диапазон стоит на середине. Прежде это был обрез: энергия прыгала
    /// с середины на подобранную (П157: `ASN16_Cs137_10cm`, 17.22…22.59 кэВ,
    /// 19.90 → 18.64 кэВ, показание 0.19 %).
    ///
    /// Как мерится. Спектр корпуса набирается по частям — вложенным
    /// прореживанием отсчётов (биномиально, зерно `--seed`): снимки набора на
    /// долях f = `--from` … 1 (`--snaps` отрезков), каждый снимок — отсчёты за
    /// время f·T, приведённые к полному времени (÷ f). Между соседними снимками
    /// проба идёт ПЛАВНО — спектр (1 − t)·A + t·B в `--steps` шагов, умноженный на
    /// `--scale` (каналы целые; округление — 1/scale отсчёта), время — полное.
    /// Так доля приписанного каждого диапазона меняется непрерывно, как при
    /// наборе, а дискретность прибавленных отсчётов (одна частица в пустом
    /// диапазоне с малой ε — честный скачок дозы, а не порог) из замера уходит:
    /// показание — линейная функция спектра, и скачок на пути может дать
    /// только ПЕРЕКЛЮЧАТЕЛЬ расчёта. Приёмка —
    ///
    ///   1. на каждом шаге, где доля приписанного диапазона пересекла прежний
    ///      порог, |Δ ln Ḣ| ≤ `--cross` (0.01 %) или ≤ `--medians` (10) медиан
    ///      |Δ ln Ḣ| своего отрезка — это и есть `S194`;
    ///   2. на любом шаге |Δ ln Ḣ| ≤ `--jump` (0.05 %; до `S195` — 0.15 %) —
    ///      сторож переключателей расчёта.
    ///
    /// Пять наибольших шагов печатаются, те, что больше 0.02 %, — с разбором по
    /// диапазонам (энергия, доля приписанного, доза до и после шага).
    ///
    /// (`S195`, П160) Остаток П159 закрыт: у `ASN16_Cs137_10cm` по матрице
    /// шаги до 0.09 % у верха шкалы (1742…3000 кэВ, энергии 2286.5 ↔ 2167 и
    /// 3000 ↔ 2780 кэВ) давала ложная остановка подбора энергии на краю
    /// диапазона, к которому центр строки падает, — теперь энергия берётся
    /// на восходящей ветви (`DoseRateManager.RisingBranch`), и наибольший шаг
    /// — 0.012 %. Прежняя сборка на `--jump=0.05` краснеет (1 из 24).
    ///
    /// Если у приложения есть мерный рычаг полосы доверия `representativeBand`
    /// (плавное доверие, П159), печатается и показание на полном спектре при
    /// ширине полосы 1.25…4 — обоснование ширины замером.
    ///
    ///     doseshareprobe [--dir=&lt;корпус&gt;] [--snaps=8] [--steps=400] [--from=0.2]
    ///                    [--seed=1] [--scale=1000] [--cross=0.01] [--medians=10] [--jump=0.05] [--quiet]
    ///                    [--set=&lt;рычаг DoseRateManager&gt;=&lt;число&gt;]
    ///
    /// Положительный контроль — проба на прежнем расчёте (обрез): краснеет.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static int snaps = 8;
        static int steps = 400;
        static double from = 0.2;
        static int seed = 1;
        static double scale = 1000.0;
        static double jumpPercent = 0.05;
        static double crossPercent = 0.01;
        static double crossMedians = 10.0;
        static bool quiet;
        static FieldInfo bandField;

        sealed class Scene
        {
            public string Spectrum;
            public string Matrix;
        }

        static readonly Scene[] Scenes =
        {
            new Scene { Spectrum = "AS80_Cs137_0cm", Matrix = "AS80_point0" },
            new Scene { Spectrum = "G1S16_Cs137_P5", Matrix = "G1S_point5_p16" },
            new Scene { Spectrum = "RC103_Cs137_0cm", Matrix = "RC103_point0" },
            new Scene { Spectrum = "ASN16_Cs137_10cm", Matrix = "ASN16_point10_house" },
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--snaps=", StringComparison.Ordinal))
                    snaps = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--steps=", StringComparison.Ordinal))
                    steps = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--from=", StringComparison.Ordinal))
                    from = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal))
                    seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--scale=", StringComparison.Ordinal))
                    scale = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--jump=", StringComparison.Ordinal))
                    jumpPercent = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--cross=", StringComparison.Ordinal))
                    crossPercent = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--medians=", StringComparison.Ordinal))
                    crossMedians = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--quiet") quiet = true;
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    // (`S194`, П159) Мерный рычаг расчёта: закрытое статическое
                    // поле `DoseRateManager` — отражением, для замера вариантов.
                    string[] kv = a.Substring(6).Split('=');
                    FieldInfo lever = kv.Length == 2
                        ? typeof(DoseRateManager).GetField(kv[0], BindingFlags.NonPublic | BindingFlags.Static)
                        : null;
                    if (lever == null || lever.FieldType != typeof(double))
                    {
                        Console.Error.WriteLine("нет рычага: " + a);
                        return 2;
                    }

                    lever.SetValue(null, double.Parse(kv[1], CultureInfo.InvariantCulture));
                    Console.WriteLine("рычаг " + kv[0] + " = " + kv[1]);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (!(from > 0.0 && from < 1.0) || steps < 2 || snaps < 1 || !(scale >= 1.0))
            {
                Console.Error.WriteLine("ключи: 0 < --from < 1, --steps ≥ 2, --snaps ≥ 1, --scale ≥ 1");
                return 2;
            }

            bandField = typeof(DoseRateManager).GetField("representativeBand", BindingFlags.NonPublic | BindingFlags.Static);
            double threshold = DoseRateManager.RepresentativeMinShare;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "П159 (S194): показание дозы по набору счёта; порог доли приписанного {0}; снимки набора {1}…1"
                + " ({2} отрезков по {3} шагов), зерно {4}, масштаб {5}; допуск на пересечении порога {6} % или {7}"
                + " медиан, на любом шаге {8} %; плавное доверие у приложения: {9}",
                threshold, from, snaps, steps, seed, scale, crossPercent, crossMedians, jumpPercent,
                bandField != null
                    ? "есть (полоса " + ((double)bandField.GetValue(null)).ToString("R", CultureInfo.InvariantCulture) + ")"
                    : "НЕТ"));

            foreach (Scene scene in Scenes)
            {
                ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", scene.Spectrum + ".xml"));
                if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
                {
                    Console.WriteLine("ОСНАСТКА: нет спектра или кривой с геометрией: " + scene.Spectrum);
                    return 2;
                }

                ResponseMatrix matrix = LoadMatrix(Path.Combine(corpusDir, "geometries", scene.Matrix + ".rmx"),
                                                   data.Efficiency.Geometry);
                if (matrix == null)
                {
                    Console.WriteLine("ОСНАСТКА: нет матрицы " + scene.Matrix);
                    return 2;
                }

                Scan(scene.Spectrum + " матрица", data, DoseRateInput.Of(data.Efficiency, matrix), threshold);
                Scan(scene.Spectrum + " «≈»", data, DoseRateInput.Of(data.Efficiency, null), threshold);
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Доля приписанного диапазона: (отсчёты − объяснено)/отсчёты.</summary>
        static double Share(DoseRateRange r)
        {
            return r.Counts > 0.0 ? (r.Counts - r.Explained) / r.Counts : double.NaN;
        }

        static void Scan(string name, ResultData data, DoseRateInput input, double threshold)
        {
            Head(name);
            EnergySpectrum es = data.EnergySpectrum;
            int[] full = (int[])es.Spectrum.Clone();
            var manager = new DoseRateManager(Config());
            DoseRate reference = manager.Calculate(data, input);
            if (!string.IsNullOrEmpty(reference.Refusal))
            {
                Console.WriteLine("  ОТКАЗ: " + reference.Refusal);
                Ok(false, name + ": расчёт отказал");
                return;
            }

            // Диапазоны у порога на полном спектре.
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  полный спектр: {0:R} мкЗв/ч, отсчётов {1}", reference.Rate, full.Sum(v => (long)v)));
            foreach (DoseRateRange r in reference.Ranges)
            {
                double s = Share(r);
                if (!r.Skipped && s > 0.5 * threshold && s < 3.0 * threshold)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "      {0,9:F2}…{1,9:F2} кэВ: доля приписанного {2:F4} ({3:F3} порога), энергия {4:F3}"
                        + " (середина {5:F3}), доза {6:E4} ({7:F3} % показания)",
                        r.LowKev, r.HighKev, s, s / threshold, r.RepresentativeKev, r.CenterKev, r.DoseRate,
                        100.0 * r.DoseRate / reference.Rate));
                }
            }

            // Снимки набора: вложенное прореживание, c_s ⊂ c_{s+1}, последний — полный
            // спектр; каждый приведён к полному времени (÷ f).
            var rng = new Random(seed);
            int n = full.Length;
            var fractions = new double[snaps + 1];
            var shots = new double[snaps + 1][];
            var current = new int[n];
            for (int s = 0; s <= snaps; s++)
            {
                double f = from + (1.0 - from) * s / snaps;
                double p = s == 0 ? from : (s == snaps ? 1.0 : (f - fractions[s - 1]) / (1.0 - fractions[s - 1]));
                for (int i = 0; i < n; i++)
                {
                    current[i] += Binomial(rng, full[i] - current[i], p);
                }

                fractions[s] = f;
                shots[s] = current.Select(v => v / f).ToArray();
            }

            double peak = shots.Max(v => v.Max());
            double m = Math.Min(scale, 1.0e9 / Math.Max(1.0, peak));
            var rates = new List<double>();
            var shares = new List<double[]>();
            var where = new List<double>();
            var place = new List<KeyValuePair<int, double>>();
            Func<int, double, int[]> mix = (s, t) =>
            {
                var mixed = new int[n];
                for (int i = 0; i < n; i++)
                {
                    mixed[i] = (int)Math.Round(m * ((1.0 - t) * shots[s][i] + t * shots[s + 1][i]));
                }

                return mixed;
            };
            DoseRate scaledFull;
            try
            {
                for (int s = 0; s < snaps; s++)
                {
                    for (int j = s == 0 ? 0 : 1; j <= steps; j++)
                    {
                        double t = (double)j / steps;
                        es.Spectrum = mix(s, t);
                        DoseRate d = manager.Calculate(data, input);
                        rates.Add(string.IsNullOrEmpty(d.Refusal) ? d.Rate / m : double.NaN);
                        shares.Add(d.Ranges.Select(r => r.Skipped ? double.NaN : Share(r)).ToArray());
                        where.Add(fractions[s] + t * (fractions[s + 1] - fractions[s]));
                        place.Add(new KeyValuePair<int, double>(s, t));
                    }
                }

                // Полный спектр ×m — тот, которым путь кончается.
                es.Spectrum = full.Select(v => (int)Math.Round(m * v)).ToArray();
                scaledFull = manager.Calculate(data, input);
            }
            finally
            {
                es.Spectrum = full;
            }

            int count = rates.Count;
            var deltas = new double[count - 1];
            for (int j = 1; j < count; j++)
            {
                deltas[j - 1] = 100.0 * Math.Log(rates[j] / rates[j - 1]);
            }

            double biggest = deltas.Any(double.IsNaN) ? double.PositiveInfinity : deltas.Max(v => Math.Abs(v));
            int worstAt = Array.FindIndex(deltas, v => double.IsNaN(v) || Math.Abs(v) == biggest) + 1;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  путь по набору {0:F2}…1: показание {1:F6}…{2:F6} мкЗв/ч (полный ×{3:R}: {4:F6});"
                + " наибольший |Δ ln Ḣ| за шаг {5:F4} % (набор {6:F4}→{7:F4})",
                from, rates.Min(), rates.Max(), m, rates[count - 1], biggest,
                where[Math.Max(0, worstAt - 1)], where[worstAt]));

            // Пять наибольших шагов; те, что больше 0.02 %, — с разбором по диапазонам.
            foreach (int j in Enumerable.Range(1, count - 1).OrderByDescending(j => Math.Abs(deltas[j - 1])).Take(5))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "      шаг набора {0:F4}→{1:F4}: Δ ln Ḣ = {2:+0.0000;-0.0000} %", where[j - 1], where[j], deltas[j - 1]));
                if (!(Math.Abs(deltas[j - 1]) > 0.02))
                {
                    continue;
                }

                DoseRate before, after;
                try
                {
                    es.Spectrum = mix(place[j - 1].Key, place[j - 1].Value);
                    before = manager.Calculate(data, input);
                    es.Spectrum = mix(place[j].Key, place[j].Value);
                    after = manager.Calculate(data, input);
                }
                finally
                {
                    es.Spectrum = full;
                }

                foreach (int k in Enumerable.Range(0, before.Ranges.Count)
                             .OrderByDescending(k => Math.Abs(after.Ranges[k].DoseRate - before.Ranges[k].DoseRate))
                             .Take(4))
                {
                    DoseRateRange p = before.Ranges[k], q = after.Ranges[k];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "        {0,9:F2}…{1,9:F2} кэВ: доза {2:E4} → {3:E4}, энергия {4:F3} → {5:F3}, доля приписанного"
                        + " {6:F4} → {7:F4}, своя {8:E4} → {9:E4}{10}{11}",
                        p.LowKev, p.HighKev, p.DoseRate / m, q.DoseRate / m, p.RepresentativeKev, q.RepresentativeKev,
                        Share(p), Share(q), p.OwnEfficiency, q.OwnEfficiency,
                        p.Skipped ? " (снят)" : "", q.Skipped ? " (→ снят)" : ""));
                }
            }

            // Медиана |Δ| по отрезку — фон, на котором видна ступенька.
            var segMedian = new double[snaps];
            for (int s = 0; s < snaps; s++)
            {
                double[] seg = deltas.Skip(s * steps).Take(steps).Select(Math.Abs).OrderBy(v => v).ToArray();
                segMedian[s] = seg[seg.Length / 2];
            }

            // Шаги, на которых доля приписанного пересекла порог.
            int crossings = 0, crossBad = 0;
            double crossWorst = 0.0;
            int bins = reference.Ranges.Count;
            for (int j = 1; j < count; j++)
            {
                for (int k = 0; k < bins; k++)
                {
                    double a = shares[j - 1][k], b = shares[j][k];
                    if (double.IsNaN(a) || double.IsNaN(b) || (a >= threshold) == (b >= threshold))
                    {
                        continue;
                    }

                    crossings++;
                    crossWorst = Math.Max(crossWorst, Math.Abs(deltas[j - 1]));
                    double med = segMedian[Math.Min(snaps - 1, (j - 1) / steps)];
                    if (Math.Abs(deltas[j - 1]) > crossPercent && Math.Abs(deltas[j - 1]) > crossMedians * med)
                    {
                        crossBad++;
                    }

                    DoseRateRange r = reference.Ranges[k];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "      набор {0:F4}: {1,8:F2}…{2,8:F2} кэВ, доля {3:F4} → {4:F4}, Δ ln Ḣ = {5:+0.0000;-0.0000} %"
                        + " ({6:F0} медиан отрезка)",
                        where[j], r.LowKev, r.HighKev, a, b, deltas[j - 1],
                        med > 0.0 ? Math.Abs(deltas[j - 1]) / med : double.PositiveInfinity));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  пересечений порога доли на пути: {0}; наибольший |Δ ln Ḣ| на них {1:F4} %; медиана отрезков {2:F5}…{3:F5} %",
                crossings, crossWorst, segMedian.Min(), segMedian.Max()));

            // Конец пути — полный спектр ×m. Показание от масштаба не зависит,
            // пока не сменилась маска каналов переполнения: правило крайнего
            // канала пуассоновское (`OverflowChannel`, 8σ), и ×m может сделать
            // «переполнением» крайний канал с горсткой отсчётов при пустых
            // соседях — тогда масштаб честно меняет показание, и это печатается.
            bool maskSame = OverflowChannel.Mask(full).SequenceEqual(
                OverflowChannel.Mask(full.Select(v => (int)Math.Round(m * v)).ToArray()));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  полный ×{0:R}: {1:R} мкЗв/ч ({2:+0.0000;-0.0000} % к полному; маска переполнения {3})",
                m, scaledFull.Rate / m, 100.0 * (scaledFull.Rate / m / reference.Rate - 1.0),
                maskSame ? "та же" : "ДРУГАЯ — крайний канал стал переполнением"));
            Ok(Math.Abs(rates[count - 1] * m / scaledFull.Rate - 1.0) < 1e-12
               && (!maskSame || Math.Abs(scaledFull.Rate / m / reference.Rate - 1.0) < 1e-9),
               string.Format(CultureInfo.InvariantCulture,
                   "{0}: конец пути — полный спектр ×{1:R} ({2:R} = {3:R}); при той же маске — показание полного",
                   name, m, rates[count - 1], scaledFull.Rate / m));
            Ok(crossBad == 0, string.Format(CultureInfo.InvariantCulture,
                "{0}: ступеньки на пороге доли нет — шагов пересечения со сдвигом больше {1} % и больше {2} медиан"
                + " отрезка: {3} из {4} (наибольший {5:F4} %)",
                name, crossPercent, crossMedians, crossBad, crossings, crossWorst));
            Ok(biggest <= jumpPercent, string.Format(CultureInfo.InvariantCulture,
                "{0}: скачков по набору нет — наибольший сдвиг показания за шаг {1:F4} % ≤ {2} %",
                name, biggest, jumpPercent));

            // Ширина полосы доверия — обоснование замером (только у сборки с рычагом).
            if (bandField != null)
            {
                double keep = (double)bandField.GetValue(null);
                try
                {
                    foreach (double band in new[] { 1.25, 1.5, 2.0, 3.0, 4.0 })
                    {
                        bandField.SetValue(null, band);
                        DoseRate d = manager.Calculate(data, input);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  полоса доверия {0:F2} (верх/низ, вокруг порога): показание {1:F8} ({2:+0.000;-0.000} % к штатной)",
                            band, d.Rate, 100.0 * (d.Rate / reference.Rate - 1.0)));
                    }
                }
                finally
                {
                    bandField.SetValue(null, keep);
                }
            }
        }

        /// <summary>Биномиальная выборка: прямо до 64, выше — нормальным приближением.</summary>
        static int Binomial(Random rng, int count, double p)
        {
            if (count <= 0 || p <= 0.0) return 0;
            if (p >= 1.0) return count;
            if (count <= 64)
            {
                int k = 0;
                for (int i = 0; i < count; i++)
                {
                    if (rng.NextDouble() < p) k++;
                }

                return k;
            }

            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double v = Math.Round(count * p + z * Math.Sqrt(count * p * (1.0 - p)));
            return (int)Math.Max(0.0, Math.Min(count, v));
        }

        // ------------------------------------------------------------------

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Console.WriteLine(new string('-', Math.Min(100, text.Length)));
        }

        static void Ok(bool ok, string what)
        {
            checks++;
            if (!ok) failed++;
            if (!quiet || !ok) Console.WriteLine("  [" + (ok ? "ок" : "НЕТ") + "]   " + what);
        }

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static ResponseMatrix LoadMatrix(string path, GeometryModel geometry)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("  матрица {0}: {1} (формат {2})", path, refusal, fileFormat);
                return null;
            }

            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                // Как у `DoseGridProbe`: склад посчитан прежним поколением физики,
                // клеймо пересчитывается в памяти, файл не пишется.
                matrix.Stamp = ResponseMatrix.ComputeStamp(geometry, matrix.Options);
            }

            return matrix;
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }
    }
}
