using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace EtlCheck
{
    /// <summary>
    /// Проверка нашего извлечения площадей и абсолютной эффективности по
    /// градуировочным спектрам LSRM.
    ///
    /// Данные: `.etl` — сами спектры с сертифицированной активностью, живым
    /// временем, фоном и калибровкой; `.efr` — что из тех же спектров получила
    /// программа LSRM (площадь и эффективность на каждую линию). То есть у
    /// одних и тех же отсчётов есть два независимых разбора, и наш можно
    /// сверить с чужим построчно.
    ///
    /// Площади считает САМ `EfficiencyFitter` — тот код, что стоит за пунктом
    /// меню, а не его копия в харнессе. Для этого спектры переносятся в наш
    /// формат и подаются фиттеру файлами.
    ///
    ///   etlcheck --etl=X.etl --efr=A.efr[;B.efr] --work=dir [--min=200] [--max=2800]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                string etl = null, work = null;
                List<string> efr = new List<string>();
                double min = 200.0, max = 2800.0;
                foreach (string arg in args)
                {
                    int eq = arg.IndexOf('=');
                    string key = eq > 0 ? arg.Substring(0, eq) : arg;
                    string value = eq > 0 ? arg.Substring(eq + 1) : "";
                    switch (key)
                    {
                        case "--etl": etl = value; break;
                        case "--efr": efr.AddRange(value.Split(';')); break;
                        case "--work": work = value; break;
                        case "--min": min = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--max": max = double.Parse(value, CultureInfo.InvariantCulture); break;
                        default: throw new ArgumentException("Unknown option: " + arg);
                    }
                }

                if (etl == null || work == null)
                {
                    Console.Error.WriteLine("--etl and --work are required");
                    return 1;
                }

                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();

                Directory.CreateDirectory(work);
                List<Sample> samples = LoadEtl(etl);
                Console.WriteLine("спектров в библиотеке: {0}", samples.Count);

                // ПШПВ-калибровки в файле нет: считаем её по самим спектрам.
                FwhmCalibration fwhm = FitFwhm(samples, min, max);
                Console.WriteLine();

                foreach (Sample s in samples)
                {
                    s.Path = Path.Combine(work, Sanitize(s.Geometry + "__" + s.Source) + ".xml");
                    Write(s, fwhm);
                }

                Dictionary<string, Dictionary<double, double>> truthEps;
                Dictionary<string, Dictionary<double, double>> truth = LoadEfr(efr, out truthEps);
                Console.WriteLine("эталонных линий из .efr: {0}",
                                  truth.Values.Sum(d => d.Count));
                Console.WriteLine();

                foreach (string geometry in samples.Select(s => s.Geometry).Distinct())
                {
                    Compare(geometry, samples.Where(s => s.Geometry == geometry).ToList(),
                            truth, truthEps, min, max);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        // ------------------------------------------------------------------
        // Разбор .etl / .efr
        // ------------------------------------------------------------------

        sealed class Sample
        {
            public string Geometry, Source, Nuclide, Path;
            public double Activity, ActivityUncertainty, LiveTime, FonTime;
            public double[] Energy;
            public int[] Data, Fon;
        }

        static List<Sample> LoadEtl(string path)
        {
            List<Sample> list = new List<Sample>();
            Sample current = null;
            string head = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.GetEncoding(1251)))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    head = line.Substring(1, line.Length - 2);
                    string[] parts = head.Split(';');
                    current = new Sample
                    {
                        Geometry = parts.Length > 1 ? parts[1] : "",
                        Source = parts.Length > 2 ? parts[2].Split(',').Last() : "",
                    };
                    list.Add(current);
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                switch (key)
                {
                    case "Nuclid": current.Nuclide = value; break;
                    case "LiveTime": current.LiveTime = D(value); break;
                    case "FonTime": current.FonTime = D(value); break;
                    case "Energy":
                        {
                            double[] v = Nums(value);
                            current.Energy = v.Skip(1).ToArray();   // c0..cN
                            break;
                        }
                    case "Data": current.Data = Nums(value).Select(x => (int)x).ToArray(); break;
                    case "FData": current.Fon = Nums(value).Select(x => (int)x).ToArray(); break;
                    default:
                        if (current.Nuclide != null && key == current.Nuclide)
                        {
                            double[] v = Nums(value);
                            current.Activity = v[0];
                            current.ActivityUncertainty = v.Length > 1 ? v[1] : 0.0;
                        }

                        break;
                }
            }

            return list.Where(s => s.Data != null && s.Data.Length > 0).ToList();
        }

        /// <summary>
        /// Что получила из тех же спектров программа LSRM: «геометрия|источник»
        /// -> энергия -> площадь пика, и то же самое по эффективности.
        /// </summary>
        static Dictionary<string, Dictionary<double, double>> LoadEfr(
            IEnumerable<string> paths, out Dictionary<string, Dictionary<double, double>> epsMap)
        {
            var map = new Dictionary<string, Dictionary<double, double>>();
            epsMap = new Dictionary<string, Dictionary<double, double>>();
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }

                string key = null;
                foreach (string raw in File.ReadAllLines(path, Encoding.GetEncoding(1251)))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string[] parts = line.Substring(1, line.Length - 2).Split(';');
                        key = parts.Length > 2 ? parts[1] + "|" + parts[2] : null;
                        if (key != null && !map.ContainsKey(key))
                        {
                            map[key] = new Dictionary<double, double>();
                        }

                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (key == null || eq <= 0)
                    {
                        continue;
                    }

                    double energy;
                    if (!double.TryParse(line.Substring(0, eq), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out energy))
                    {
                        continue;
                    }

                    string[] fields = line.Substring(eq + 1).Split(',');
                    if (fields.Length > 0 && fields[fields.Length - 1].Trim().ToLower() == "no")
                    {
                        continue;                     // LSRM сам эту линию отверг
                    }

                    // 0 — эффективность, 3 — площадь пика по разбору LSRM
                    map[key][energy] = fields.Length > 3 ? D(fields[3]) : 0.0;
                    if (!epsMap.ContainsKey(key))
                    {
                        epsMap[key] = new Dictionary<double, double>();
                    }

                    epsMap[key][energy] = D(fields[0]);
                }
            }

            return map;
        }

        // ------------------------------------------------------------------
        // ПШПВ по самим спектрам
        // ------------------------------------------------------------------

        /// <summary>
        /// Калибровки разрешения в библиотеке нет, а без неё фиттер работать не
        /// может. Считаем её по сильным линиям самих спектров: трёхпараметрический
        /// фит (амплитуда, центр, ширина) на гауссиане с линейной подложкой,
        /// потом ПШПВ² = c0 + c1·ch + c2·ch² взвешенным МНК.
        /// </summary>
        static FwhmCalibration FitFwhm(List<Sample> samples, double min, double max)
        {
            // Только ЧИСТЫЕ ОДИНОЧНЫЕ линии. Первый заход брал ещё 238.6, 351.9
            // и 911.2 — и все три соврали: 911 у Ac-228 стоит рядом с 969 и на
            // одиночной гауссиане вышло 10 % вместо 6, а 238.6 и 351.9 сидят на
            // крутом континууме, и линейная подложка съедает крылья, отчего
            // ширина занижается. С ними калибровка давала на 239 кэВ 7.7 %
            // вместо ожидаемых 11-12.
            var strong = new Dictionary<string, double[]>
            {
                { "Cs-137", new[] { 661.657 } },
                { "K-40", new[] { 1460.822 } },
                { "Th-232", new[] { 2614.511 } },
                { "Ra-226", new[] { 609.320, 1764.491 } },
            };

            List<double[]> points = new List<double[]>();
            foreach (Sample s in samples)
            {
                double[] energies;
                if (s.Nuclide == null || !strong.TryGetValue(s.Nuclide, out energies))
                {
                    continue;
                }

                foreach (double e in energies)
                {
                    if (e < min || e > max)
                    {
                        continue;
                    }

                    double channel, width, quality;
                    if (FitLine(s, e, out channel, out width, out quality))
                    {
                        points.Add(new[] { channel, width, quality });
                        double perCh = EnergyOf(s, channel + 0.5) - EnergyOf(s, channel - 0.5);
                        Console.WriteLine("   {0,7:F1} кэВ  канал {1,6:F1}  ПШПВ {2,5:F2} кан = "
                                          + "{3,5:F1} кэВ = {4,5:F2} %   {5} {6}",
                                          e, channel, width, width * perCh,
                                          100.0 * width * perCh / e, s.Geometry, s.Source);
                    }
                }
            }

            Console.WriteLine("ПШПВ: годных линий {0}", points.Count);
            double[] c = SolveQuadratic(points);
            SqrtFwhmCalibration calibration = new SqrtFwhmCalibration();
            calibration.Coefficients = c;
            Console.WriteLine("ПШПВ² = {0:F4} + {1:F5}·ch + {2:F8}·ch²", c[0], c[1], c[2]);

            // печать для глаза: что получилось на опорных энергиях
            Sample any = samples[0];
            foreach (double e in new[] { 239.0, 662.0, 1461.0, 2615.0 })
            {
                double ch = ChannelOf(any, e);
                double f = calibration.ChannelToFwhm(ch);
                double perCh = EnergyOf(any, ch + 0.5) - EnergyOf(any, ch - 0.5);
                Console.WriteLine("   {0,6:F0} кэВ: канал {1,6:F1}, ПШПВ {2,5:F2} кан = {3,5:F1} кэВ = {4:F2} %",
                                  e, ch, f, f * perCh, 100.0 * f * perCh / e);
            }

            return calibration;
        }

        /// <summary>Гауссиана + линейная подложка, перебор по ширине и центру.</summary>
        static bool FitLine(Sample s, double energy, out double channel, out double fwhm,
                            out double quality)
        {
            channel = fwhm = quality = 0.0;
            double guessCh = ChannelOf(s, energy);
            double perCh = EnergyOf(s, guessCh + 0.5) - EnergyOf(s, guessCh - 0.5);
            if (!(perCh > 0.0) || guessCh < 5.0 || guessCh > s.Data.Length - 6)
            {
                return false;
            }

            double guessFwhm = 0.07 * energy / perCh;          // 7 % — типично для NaI
            double scale = s.FonTime > 0.0 ? s.LiveTime / s.FonTime : 0.0;
            int half = (int)Math.Ceiling(3.0 * guessFwhm);
            int lo = Math.Max(0, (int)Math.Round(guessCh) - half);
            int hi = Math.Min(s.Data.Length - 1, (int)Math.Round(guessCh) + half);
            if (hi - lo < 6)
            {
                return false;
            }

            double[] y = new double[hi - lo + 1], w = new double[hi - lo + 1];
            for (int i = lo; i <= hi; i++)
            {
                double bg = s.Fon != null && i < s.Fon.Length ? scale * s.Fon[i] : 0.0;
                y[i - lo] = s.Data[i] - bg;
                w[i - lo] = 1.0 / Math.Max(s.Data[i] + scale * scale * (bg / Math.Max(scale, 1e-9)), 1.0);
            }

            double best = double.MaxValue;
            for (int fi = 0; fi <= 40; fi++)
            {
                double f = guessFwhm * (0.55 + 0.03 * fi);
                double sigma = f / 2.3548200450309493;
                for (int ci = -12; ci <= 12; ci++)
                {
                    double centre = guessCh + 0.1 * ci * guessFwhm;
                    double chi2, amplitude;
                    if (!LinearFit(y, w, lo, centre, sigma, out amplitude, out chi2))
                    {
                        continue;
                    }

                    if (amplitude > 0.0 && chi2 < best)
                    {
                        best = chi2;
                        channel = centre;
                        fwhm = f;
                    }
                }
            }

            quality = best;
            return best < double.MaxValue && fwhm > 0.0;
        }

        /// <summary>При заданных центре и ширине — линейный МНК на амплитуду и подложку.</summary>
        static bool LinearFit(double[] y, double[] w, int lo, double centre, double sigma,
                              out double amplitude, out double chi2)
        {
            amplitude = 0.0;
            chi2 = 0.0;
            int n = y.Length;
            const int m = 3;                       // гауссиана, единица, наклон
            double[,] a = new double[m, m];
            double[] b = new double[m];
            double mid = lo + 0.5 * (n - 1);
            for (int i = 0; i < n; i++)
            {
                double d = (lo + i - centre) / sigma;
                double[] basis = { Math.Exp(-0.5 * d * d), 1.0, (lo + i - mid) / n };
                for (int p = 0; p < m; p++)
                {
                    for (int q = 0; q < m; q++)
                    {
                        a[p, q] += w[i] * basis[p] * basis[q];
                    }

                    b[p] += w[i] * basis[p] * y[i];
                }
            }

            double[] x;
            if (!Gauss(a, b, m, out x))
            {
                return false;
            }

            amplitude = x[0];
            for (int i = 0; i < n; i++)
            {
                double d = (lo + i - centre) / sigma;
                double model = x[0] * Math.Exp(-0.5 * d * d) + x[1] + x[2] * ((lo + i - mid) / n);
                chi2 += w[i] * (y[i] - model) * (y[i] - model);
            }

            return true;
        }

        static bool Gauss(double[,] a, double[] b, int m, out double[] x)
        {
            double[,] work = new double[m, m + 1];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    work[i, j] = a[i, j];
                }

                work[i, m] = b[i];
            }

            for (int c = 0; c < m; c++)
            {
                int piv = c;
                for (int r = c + 1; r < m; r++)
                {
                    if (Math.Abs(work[r, c]) > Math.Abs(work[piv, c]))
                    {
                        piv = r;
                    }
                }

                if (Math.Abs(work[piv, c]) < 1e-12)
                {
                    x = null;
                    return false;
                }

                if (piv != c)
                {
                    for (int j = 0; j <= m; j++)
                    {
                        double t = work[c, j];
                        work[c, j] = work[piv, j];
                        work[piv, j] = t;
                    }
                }

                double d = work[c, c];
                for (int j = 0; j <= m; j++)
                {
                    work[c, j] /= d;
                }

                for (int r = 0; r < m; r++)
                {
                    if (r == c || work[r, c] == 0.0)
                    {
                        continue;
                    }

                    double f = work[r, c];
                    for (int j = 0; j <= m; j++)
                    {
                        work[r, j] -= f * work[c, j];
                    }
                }
            }

            x = new double[m];
            for (int i = 0; i < m; i++)
            {
                x[i] = work[i, m];
            }

            return true;
        }

        /// <summary>
        /// ПШПВ² = c0 + c1·ch по точкам (канал, ПШПВ). Квадратичный член здесь
        /// вреден: четыре точки, все выше 600 кэВ, и парабола по ним уходит вниз
        /// на краю. Третий коэффициент остаётся нулевым.
        /// </summary>
        static double[] SolveQuadratic(List<double[]> points)
        {
            const int m = 2;
            double[,] a = new double[m, m];
            double[] b = new double[m];
            foreach (double[] p in points)
            {
                double[] basis = { 1.0, p[0] };
                double value = p[1] * p[1];
                for (int i = 0; i < m; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        a[i, j] += basis[i] * basis[j];
                    }

                    b[i] += basis[i] * value;
                }
            }

            double[] x;
            if (!Gauss(a, b, m, out x))
            {
                return new double[] { 100.0, 1.0, 0.0 };
            }

            return new double[] { x[0], x[1], 0.0 };
        }

        // ------------------------------------------------------------------
        // Перенос в наш формат
        // ------------------------------------------------------------------

        static void Write(Sample s, FwhmCalibration fwhm)
        {
            PolynomialEnergyCalibration energy = new PolynomialEnergyCalibration();
            energy.PolynomialOrder = s.Energy.Length - 1;
            double[] c = new double[s.Energy.Length];
            Array.Copy(s.Energy, c, c.Length);
            energy.Coefficients = c;

            EnergySpectrum spectrum = new EnergySpectrum(1.0, s.Data.Length);
            Array.Copy(s.Data, spectrum.Spectrum, s.Data.Length);
            spectrum.EnergyCalibration = energy;
            spectrum.LiveTime = s.LiveTime;
            spectrum.MeasurementTime = s.LiveTime;
            spectrum.ValidPulseCount = s.Data.Sum(v => (long)v);
            spectrum.TotalPulseCount = spectrum.ValidPulseCount;

            ResultData data = new ResultData();
            data.EnergySpectrum = spectrum;
            data.FwhmCalibration = fwhm.Clone();
            data.SampleInfo.Name = new CDATA(s.Geometry + " " + s.Source);

            if (s.Fon != null && s.Fon.Length == s.Data.Length && s.FonTime > 0.0)
            {
                EnergySpectrum background = new EnergySpectrum(1.0, s.Fon.Length);
                Array.Copy(s.Fon, background.Spectrum, s.Fon.Length);
                background.EnergyCalibration = energy.Clone();
                background.LiveTime = s.FonTime;
                background.MeasurementTime = s.FonTime;
                data.BackgroundEnergySpectrum = background;
            }

            ResultDataFile file = new ResultDataFile();
            file.ResultDataList.Add(data);
            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            using (FileStream stream = new FileStream(s.Path, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(stream, file);
            }
        }

        // ------------------------------------------------------------------
        // Сверка
        // ------------------------------------------------------------------

        static void Compare(string geometry, List<Sample> samples,
                            Dictionary<string, Dictionary<double, double>> truth,
                            Dictionary<string, Dictionary<double, double>> truthEps,
                            double min, double max)
        {
            EfficiencyFitInput input = new EfficiencyFitInput
            {
                MinEnergy = min,
                MaxEnergy = max,
                SubtractBackground = true,
            };
            // Каждый источник — свой нуклид. Фиттер по устройству ищет в спектре
            // все цепочки сразу (он писан под пробы), и в спектре цезия находит
            // «линии» тория на пустом месте. Здесь цепочка задаётся явно.
            input.SpectrumFiles.AddRange(samples
                .Where(s => s.Nuclide == "Th-232" || s.Nuclide == "Ra-226")
                .Select(s => s.Path));
            input.Chains.AddRange(samples
                .Where(s => s.Nuclide == "Th-232" || s.Nuclide == "Ra-226")
                .Select(s => s.Nuclide).Distinct());
            if (input.SpectrumFiles.Count == 0)
            {
                return;
            }

            EfficiencyFitResult result = EfficiencyFitter.Run(
                input, m => Console.WriteLine("    [фиттер] " + m), null);
            Console.WriteLine("=== {0}: спектров {1}, наблюдений {2}, принято {3}{4}",
                              geometry, samples.Count, result.Observations.Count,
                              result.Observations.Count(o => o.Accepted),
                              string.IsNullOrEmpty(result.Error) ? "" : ", " + result.Error);

            Dictionary<string, Sample> byName = samples.ToDictionary(
                s => Path.GetFileNameWithoutExtension(s.Path), s => s);

            // Сверяем ПЛОЩАДИ: они не зависят ни от активности, ни от выхода на
            // распад, ни от нашей библиотеки линий. Это чистая проверка того,
            // что оба разбора вынули из одних и тех же отсчётов.
            List<double> ratios = new List<double>();
            Console.WriteLine("    {0,9} {1,12} {2,12} {3,7}   {4,10} {5,10} {6,7}  {7}",
                              "E, кэВ", "наша S", "LSRM S", "отн.",
                              "наш eps", "LSRM eps", "отн.", "источник");
            foreach (EfficiencyObservation o in result.Observations
                         .Where(o => !o.Accepted).OrderBy(o => o.Energy))
            {
                Console.WriteLine("    [отказ] {0,8:F2}  {1,-28} {2}", o.Energy, o.Nuclide, o.Reason);
            }

            foreach (EfficiencyObservation o in result.Observations
                         .Where(o => o.Accepted).OrderBy(o => o.Energy))
            {
                Sample s;
                if (!byName.TryGetValue(o.Spectrum, out s))
                {
                    continue;
                }

                string key = geometry + "|" + s.Source;
                double area = Nearest(truth, key, o.Energy);
                double eps = Nearest(truthEps, key, o.Energy);
                if (!(area > 0.0))
                {
                    continue;                     // у LSRM этой линии нет — сверять не с чем
                }

                double ourEps = s.Activity > 0.0 && o.Intensity > 0.0
                    ? o.NetCounts / (o.LiveTime * s.Activity * o.Intensity / 100.0)
                    : 0.0;
                double k = o.NetCounts / area;
                ratios.Add(k);
                Console.WriteLine("    {0,9:F2} {1,12:N0} {2,12:N0} {3,7:F3}   {4,10:E3} {5,10:E3} {6,7}  {8} ({7})",
                                  o.Energy, o.NetCounts, area, k, ourEps, eps,
                                  eps > 0.0 && ourEps > 0.0
                                      ? (ourEps / eps).ToString("F3", CultureInfo.InvariantCulture) : "-",
                                  o.Nuclide, s.Source);
            }

            if (ratios.Count > 0)
            {
                ratios.Sort();
                double median = ratios[ratios.Count / 2];
                Console.WriteLine("    ИТОГ по площадям: медиана наша/LSRM = {0:F3}, "
                                  + "разброс {1:F3}..{2:F3} по {3} линиям",
                                  median, ratios[0], ratios[ratios.Count - 1], ratios.Count);
            }

            Console.WriteLine();
        }

        // ------------------------------------------------------------------

        /// <summary>Значение LSRM для линии, ближайшей по энергии (в пределах 1.5 кэВ).</summary>
        static double Nearest(Dictionary<string, Dictionary<double, double>> map,
                              string key, double energy)
        {
            Dictionary<double, double> lines;
            if (!map.TryGetValue(key, out lines))
            {
                return 0.0;
            }

            foreach (KeyValuePair<double, double> pair in lines)
            {
                if (Math.Abs(pair.Key - energy) < 1.5)
                {
                    return pair.Value;
                }
            }

            return 0.0;
        }

        static double ChannelOf(Sample s, double energy)
        {
            double lo = 0.0, hi = s.Data.Length - 1.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (EnergyOf(s, mid) < energy)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return 0.5 * (lo + hi);
        }

        static double EnergyOf(Sample s, double channel)
        {
            double value = 0.0;
            for (int i = s.Energy.Length - 1; i >= 0; i--)
            {
                value = value * channel + s.Energy[i];
            }

            return value;
        }

        static double D(string v)
        {
            return double.Parse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static double[] Nums(string v)
        {
            return v.Split(',').Where(p => p.Trim().Length > 0).Select(D).ToArray();
        }

        static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Replace(' ', '_');
        }
    }
}
