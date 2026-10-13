using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace FsaPairContinuumProbeP166
{
    /// <summary>
    /// (`AMBER124`/`AMBER125`/`AMBER134`, П166 28.09.2026) АРБИТР GEANT4 НА
    /// ПРИЛОЖЕНИИ ЦЕЛИКОМ: спектр — гистограмма поглощённой энергии ион-режима
    /// `g4cf ionhist` (истина: распад с каскадом, изотропно), свёрнутая с ПШПВ
    /// и калибровкой спектра-шаблона на его каналы; разбор — `FsaAnalyzer` с
    /// матрицей той же сцены и объявленным составом (один нуклид), без фона и
    /// без колонки наложений. Живое время ставится так, чтобы истинная
    /// активность была `--activity=` Бк.
    ///
    ///   fsapaircontinuumprobep166 --template=X.xml --g4=ion.log --matrix=S.rmx
    ///                             --sample=60CO [--activity=1000]
    ///                             [--arms=all|base] [--bands=30:1100,1400:2450]
    ///
    /// Печатает на каждое плечо (умолчание; «без сумм-континуума»
    /// `CascadePairContinuum = false`; «без хода по схеме» `CascadeBranchSum =
    /// false`; «оба выкл» — поведение до П166): активность / истина − 1, χ²/ndf,
    /// и по полосам энергии — данные, модель и (модель − данные)/данные. Цель
    /// правки видна по полосам: у прежней модели континуум каскадной пары
    /// перебран (+18 % на 30–1100 кэВ у Co-60 на контакте G1S по одним
    /// гистограммам Geant4), а полоса сумм-континуума 1400–2450 пуста.
    ///
    /// ⚠ Матрица — наша (`EfficiencySimulator`), истина — Geant4: абсолютное
    /// согласие ограничено расхождением двух переносов (единицы процентов);
    /// судится РАЗНИЦА плеч по полосам. Имён нуклидов в пробе нет — состав
    /// ключом.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string template = null, g4 = null, matrixPath = null, arms = "all";
            var sample = new List<string>();
            double activity = 1000.0;
            var bands = new List<double[]> { new[] { 30.0, 1100.0 }, new[] { 1100.0, 1400.0 }, new[] { 1400.0, 2450.0 }, new[] { 2450.0, 2560.0 } };
            foreach (string a in args)
            {
                if (a.StartsWith("--template=", StringComparison.Ordinal)) template = a.Substring(11);
                else if (a.StartsWith("--g4=", StringComparison.Ordinal)) g4 = a.Substring(5);
                else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    sample.AddRange(a.Substring(9).ToUpperInvariant().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--activity=", StringComparison.Ordinal))
                    activity = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--arms=", StringComparison.Ordinal)) arms = a.Substring(7);
                else if (a.StartsWith("--bands=", StringComparison.Ordinal))
                {
                    bands.Clear();
                    foreach (string b in a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] lh = b.Split(':');
                        bands.Add(new[] { double.Parse(lh[0], CultureInfo.InvariantCulture), double.Parse(lh[1], CultureInfo.InvariantCulture) });
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (template == null || g4 == null || matrixPath == null || sample.Count == 0)
            {
                Console.Error.WriteLine("нужны --template=, --g4=, --matrix=, --sample=");
                return 2;
            }

            long decays;
            double binKev;
            double[] hist = LoadG4(g4, out decays, out binKev);
            if (hist == null)
            {
                Console.Error.WriteLine("в логе Geant4 нет HISTBEGIN/HIST: " + g4);
                return 2;
            }

            MatrixRefusal refusal;
            int format;
            ResponseMatrix matrix = ResponseMatrix.Load(matrixPath, out refusal, out format);
            if (matrix == null)
            {
                Console.Error.WriteLine("матрица не прочитана: " + refusal);
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            ResultData rd = Load(template);
            Console.WriteLine("прибор: " + ProbeDeviceConfig.Attach(rd));
            EnergySpectrum es = rd.EnergySpectrum.Clone();
            EnergyCalibration cal = es.EnergyCalibration;
            int channels = es.NumberOfChannels;
            double live = decays / activity;
            es.Spectrum = Fold(hist, binKev, cal, rd.FwhmCalibration, channels);
            es.LiveTime = live;
            es.MeasurementTime = live;
            long total = 0;
            foreach (int c in es.Spectrum) total += c;
            es.TotalPulseCount = total;
            es.ValidPulseCount = total;
            Console.WriteLine("Geant4: распадов {0}, бин {1} кэВ; спектр: {2} отсчётов на {3} каналов, живое {4} с (истина {5} Бк)",
                              decays, F(binKev, "G"), total, channels, F(live, "F1"), F(activity, "G"));
            Console.WriteLine("матрица: {0} (клеймо {1})", Path.GetFileName(matrixPath), matrix.Stamp);

            FsaSampleLibrary.Report built;
            FsaSampleSpec spec = FsaSampleSpec.Declared(rd, null, sample, false, true);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec, out built);
            Console.WriteLine("состав: {0}; {1}", string.Join(", ", sample), built);
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            var peakConfig = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;

            var armList = new List<KeyValuePair<string, bool[]>>
            {
                new KeyValuePair<string, bool[]>("умолчание (все правки П166)", new[] { true, true }),
                new KeyValuePair<string, bool[]>("без сумм-континуума пар (AMBER124/125 выкл)", new[] { false, true }),
                new KeyValuePair<string, bool[]>("без хода по схеме (AMBER134 выкл)", new[] { true, false }),
                new KeyValuePair<string, bool[]>("оба выкл (до П166)", new[] { false, false })
            };
            if (arms == "base")
            {
                armList.RemoveRange(1, 2);
            }

            bool printed = false;
            foreach (KeyValuePair<string, bool[]> arm in armList)
            {
                var an = new FsaAnalyzer();
                FsaMatrixBinding.Bind(an, rd.Efficiency != null ? rd.Efficiency.Geometry : null, matrix);
                an.PileUp = false;
                if (peakConfig != null)
                {
                    an.MinEnergy = peakConfig.Min_Range;
                    an.MaxEnergy = peakConfig.Max_Range;
                }

                an.CascadePairContinuum = arm.Value[0];
                an.CascadeBranchSum = arm.Value[1];
                if (!printed)
                {
                    FsaTuningReport.Print(an, arm.Key);
                    printed = true;
                }

                FsaResult r = an.Analyze(es, null, rd.FwhmCalibration, library, efficiency);
                Console.WriteLine();
                Console.WriteLine("=== {0} ===", arm.Key);
                if (r == null)
                {
                    Console.WriteLine("  разложение не получилось: {0} — {1}", an.Refusal, an.RefusalNote);
                    continue;
                }

                foreach (FsaComponentResult c in r.Components)
                {
                    if (c.Kind == FsaComponentKind.Nuisance || !(c.CountRate > 0.0))
                    {
                        continue;
                    }

                    Console.WriteLine("  {0}: активность {1} Бк, к истине {2}", c.Name, F(c.CountRate, "F2"),
                                      F(c.CountRate / activity - 1.0, "+0.0000;-0.0000"));
                }

                Console.WriteLine("  χ²/ndf {0}", F(r.Chi2Ndf, "F4"));
                foreach (double[] band in bands)
                {
                    double data = 0.0, model = 0.0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        double e = cal.ChannelToEnergy(ch);
                        if (e < band[0] || e >= band[1])
                        {
                            continue;
                        }

                        data += es.Spectrum[ch];
                        model += r.Model != null && ch < r.Model.Length ? r.Model[ch] : 0.0;
                    }

                    Console.WriteLine("BAND\t{0}\t{1}–{2} кэВ\tданные {3}\tмодель {4}\t(м−д)/д {5}", arm.Key,
                                      F(band[0], "G"), F(band[1], "G"), F(data, "F0"), F(model, "F0"),
                                      F(data > 0.0 ? model / data - 1.0 : double.NaN, "+0.0000;-0.0000"));
                }
            }

            Console.WriteLine();
            Console.WriteLine("ход по схеме: линий {0}, отказов (партнёра нет в схеме) {1}, схем ENSDF прочитано {2}",
                              FsaCascadeSummer.BranchWalks, FsaCascadeSummer.BranchWalkFallbacks,
                              CascadeAtomicData.EnsdfWalk.Loaded);
            return 0;
        }

        /// <summary>Гистограмма `HISTBEGIN bins=… bin_kev=… decays=…` / `HIST i n` лога g4cf.</summary>
        static double[] LoadG4(string path, out long decays, out double binKev)
        {
            decays = 0;
            binKev = 0.0;
            double[] hist = null;
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("HISTBEGIN", StringComparison.Ordinal))
                {
                    Match m = Regex.Match(line, @"bins=(\d+) bin_kev=([\d.]+) decays=(\d+)");
                    hist = new double[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)];
                    binKev = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    decays = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                }
                else if (hist != null && line.StartsWith("HIST ", StringComparison.Ordinal))
                {
                    string[] p = line.Split(' ');
                    int i = int.Parse(p[1], CultureInfo.InvariantCulture);
                    if (i >= 0 && i < hist.Length)
                    {
                        hist[i] = double.Parse(p[2], CultureInfo.InvariantCulture);
                    }
                }
            }

            return hist;
        }

        /// <summary>
        /// Свёртка гистограммы Geant4 с ПШПВ спектра-шаблона на его каналы:
        /// каждый бин (кроме нулевого — «ничего не оставил») — гауссиан с σ =
        /// ПШПВ(канал)/2.3548, доля в канал — по краям ±½ канала; сумма
        /// округляется в целые отсчёты.
        /// </summary>
        static int[] Fold(double[] hist, double binKev, EnergyCalibration cal, FwhmCalibration fwhm, int channels)
        {
            var sum = new double[channels];
            for (int k = 1; k < hist.Length - 1; k++)
            {
                if (!(hist[k] > 0.0))
                {
                    continue;
                }

                double centre = cal.EnergyToChannel(k * binKev, channels);
                if (!(centre > 0.0) || centre >= channels)
                {
                    continue;
                }

                double sigma = Math.Max(0.3, fwhm.ChannelToFwhm(centre) / 2.3548);
                int lo = Math.Max(0, (int)Math.Floor(centre - 6.0 * sigma));
                int hi = Math.Min(channels - 1, (int)Math.Ceiling(centre + 6.0 * sigma));
                for (int ch = lo; ch <= hi; ch++)
                {
                    double share = Phi((ch + 0.5 - centre) / sigma) - Phi((ch - 0.5 - centre) / sigma);
                    sum[ch] += hist[k] * share;
                }
            }

            var result = new int[channels];
            for (int ch = 0; ch < channels; ch++)
            {
                result[ch] = (int)Math.Round(sum[ch]);
            }

            return result;
        }

        /// <summary>Нормальная функция распределения (Абрамовиц–Стиган 7.1.26).</summary>
        static double Phi(double x)
        {
            double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x) / Math.Sqrt(2.0));
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t
                       * Math.Exp(-x * x / 2.0);
            return x >= 0.0 ? 0.5 * (1.0 + y) : 0.5 * (1.0 - y);
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            return file.ResultDataList[0];
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
