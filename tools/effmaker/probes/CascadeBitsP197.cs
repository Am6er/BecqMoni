using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace CascadeBitsP197
{
    /// <summary>
    /// П197 (01.10.2026): ПОБИТОВЫЙ СНИМОК СУММИРОВАТЕЛЯ КАСКАДОВ — приёмка
    /// ускорения `FsaCascadeSummer` (ход по паре `S177`: `NearestExit` готовым
    /// набором схемы, память третьих γ при паре γ+K, память видимой суммы).
    /// Правка скорости обязана не сдвинуть ни одного бита; csv корпуса печатают
    /// числа с округлением и этого не докажут, поэтому проба пишет КАЖДОЕ число
    /// суммирователя в виде «R» и 64-битным словом, а сверка сравнивает слова.
    ///
    ///   cascadebitsp197 --spectrum=X.xml --sample=152EU[,137CS] [--chain=Th-232]
    ///                   --out=снимок.txt [--ulp=N]
    ///   cascadebitsp197 --compare=a.txt,b.txt
    ///
    /// Снимок по составу спектра (общий вход `FsaSampleSpec.FromManifest`, как у
    /// `FsaCascadeProbe`; суммирователь — `FsaAnalyzer.CreateCascadeSummer` после
    /// `FsaMatrixBinding.Bind`, то есть с ключами разбора по умолчанию):
    ///   A. третьи кванты ВСЕХ пар каждого нуклида (`ThirdTable`) на ХОЛОДНОМ
    ///      суммирователе — путь без памяти;
    ///   B. поправки `For` каждого компонента на том же (память тёплая);
    ///   C. `For` на НОВОМ суммирователе — холодный путь поправок;
    ///   D. `ThirdTable` на нём же после `For` — тёплый путь третьих.
    ///
    /// `--ulp=N` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: N-е число снимка пишется на одну
    /// единицу последнего разряда больше (слово + 1). Сверка такого снимка с
    /// честным обязана отказать кодом 1 и назвать ровно это число — иначе
    /// «побитово» было бы заявлено, а не доказано.
    ///
    /// Сверка: код 0 — все строки совпали; 1 — расхождение (первые 20
    /// напечатаны); 2 — ошибка ключей/файлов.
    /// </summary>
    static class Program
    {
        static StreamWriter writer;
        static long counter;
        static long ulpAt;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, outPath = null, compare = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--compare=", StringComparison.Ordinal)) compare = a.Substring(10);
                else if (a.StartsWith("--ulp=", StringComparison.Ordinal))
                    ulpAt = long.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                    chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (compare != null)
            {
                return Compare(compare);
            }

            if (spectrumPath == null || outPath == null || (chains.Count == 0 && nuclides.Count == 0))
            {
                Console.Error.WriteLine("нужны --spectrum=, --out= и состав --sample=/--chain=");
                return 2;
            }

            foreach (string label in chains)
            {
                if (FsaSampleChain.FromLabel(label) == null)
                {
                    Console.Error.WriteLine("--chain={0}: неизвестный ряд", label);
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            string guid = rd.Efficiency != null ? rd.Efficiency.Guid : null;
            ResponseMatrix matrix = ResponseMatrixStore.Load(guid);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                Console.Error.WriteLine("матрицы сцены нет или отпечаток не сошёлся");
                return 2;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (writer = new StreamWriter(outPath, false, new UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                FsaCascadeSummer cold = NewSummer(rd, matrix);
                if (cold == null)
                {
                    Console.Error.WriteLine("суммирователь не создан");
                    return 2;
                }

                Line("# компонентов " + library.Count.ToString(CultureInfo.InvariantCulture)
                     + ", кривая света «" + cold.LightYieldName + "»");
                DumpThirds("A", cold, library);
                DumpFor("B", cold, library);
                FsaCascadeSummer fresh = NewSummer(rd, matrix);
                DumpFor("C", fresh, library);
                DumpThirds("D", fresh, library);
            }

            Console.WriteLine("снимок: {0} чисел за {1:F1} с -> {2}{3}", counter, watch.Elapsed.TotalSeconds, outPath,
                              ulpAt > 0 ? string.Format(CultureInfo.InvariantCulture,
                                                        " (КОНТРОЛЬ: число №{0} сдвинуто на 1 ulp)", ulpAt) : "");
            return 0;
        }

        static FsaCascadeSummer NewSummer(ResultData rd, ResponseMatrix matrix)
        {
            var analyzer = new FsaAnalyzer();
            FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
            return analyzer.CreateCascadeSummer();
        }

        static void Line(string text)
        {
            writer.WriteLine(text);
        }

        /// <summary>Число снимка: «метка  R  слово». Под `--ulp=N` N-е слово на единицу больше.</summary>
        static void Num(string label, double value)
        {
            counter++;
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (counter == ulpAt)
            {
                bits += 1;
                value = BitConverter.Int64BitsToDouble(bits);
            }

            writer.WriteLine(label + "\t" + value.ToString("R", CultureInfo.InvariantCulture) + "\t"
                             + bits.ToString("X16", CultureInfo.InvariantCulture));
        }

        static List<string> NuclidesOf(List<FsaComponent> library)
        {
            var names = new List<string>();
            foreach (FsaComponent component in library)
            {
                foreach (FsaLine line in component.Lines)
                {
                    string name = line.Nuclide ?? "";
                    if (name.Length > 0 && !names.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        static void DumpThirds(string section, FsaCascadeSummer summer, List<FsaComponent> library)
        {
            foreach (string nuclide in NuclidesOf(library))
            {
                List<double[]> pairs = FsaCascadeSummer.PairTable(nuclide);
                Line(string.Format(CultureInfo.InvariantCulture, "# {0} {1}: пар {2}", section, nuclide, pairs.Count));
                foreach (double[] pair in pairs)
                {
                    double sMax, sScheme;
                    string how;
                    List<double[]> rows = summer.ThirdTable(nuclide, pair[0], pair[1], out sMax, out sScheme, out how);
                    string head = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2:R}+{3:R}", section, nuclide,
                                                pair[0], pair[1]);
                    Line("# " + head + " " + how + ", третьих " + rows.Count.ToString(CultureInfo.InvariantCulture));
                    Num(head + " Smax", sMax);
                    Num(head + " Sscheme", sScheme);
                    foreach (double[] row in rows)
                    {
                        string rh = head + " m=" + row[0].ToString("R", CultureInfo.InvariantCulture);
                        Num(rh + " Pmax", row[1]);
                        Num(rh + " Pscheme", row[2]);
                        Num(rh + " epsT", row[3]);
                    }
                }
            }
        }

        static void DumpFor(string section, FsaCascadeSummer summer, List<FsaComponent> library)
        {
            foreach (FsaComponent component in library)
            {
                FsaCascadeSummer.Correction c = summer.For(component);
                string head = section + " " + component.Name;
                if (c == null)
                {
                    Line("# " + head + ": поправки нет");
                    continue;
                }

                Line(string.Format(CultureInfo.InvariantCulture, "# {0}: линий {1}, сумм {2}, срезанных {3}, конт. {4}, пар-конт. {5}, any {6}",
                                   head, c.LineFactors.Length, c.SumPeaks.Count, c.DroppedSumPeaks.Count,
                                   c.SumContinua.Count, c.PairContinua.Count, c.Any));
                for (int i = 0; i < c.LineFactors.Length; i++)
                {
                    Num(head + " factor[" + i.ToString(CultureInfo.InvariantCulture) + "]", c.LineFactors[i]);
                }

                foreach (FsaCascadeSummer.LineNote n in c.Notes)
                {
                    string nh = head + " note " + n.Nuclide + " " + n.EnergyKev.ToString("R", CultureInfo.InvariantCulture);
                    Num(nh + " cf", n.Cf);
                    Num(nh + " loss", n.Loss);
                    Num(nh + " in", n.InShare);
                    Num(nh + " direct", n.DirectArea);
                }

                DumpPeaks(head + " sum", c.SumPeaks);
                DumpPeaks(head + " dropped", c.DroppedSumPeaks);
                int k = 0;
                foreach (FsaCascadeSummer.SumContinuum s in c.SumContinua)
                {
                    string sh = head + " cont[" + (k++).ToString(CultureInfo.InvariantCulture) + "] " + s.Nuclide;
                    Num(sh + " shift", s.ShiftKev);
                    Num(sh + " third", s.ThirdKev);
                    Num(sh + " weight", s.Weight);
                }

                k = 0;
                foreach (FsaCascadeSummer.PairContinuum p in c.PairContinua)
                {
                    string ph = head + " pair[" + (k++).ToString(CultureInfo.InvariantCulture) + "] " + p.Nuclide;
                    Num(ph + " first", p.FirstKev);
                    Num(ph + " second", p.SecondKev);
                    Num(ph + " joint", p.Joint);
                }
            }
        }

        static void DumpPeaks(string head, List<FsaCascadeSummer.SumPeak> peaks)
        {
            int k = 0;
            foreach (FsaCascadeSummer.SumPeak p in peaks)
            {
                string ph = head + "[" + (k++).ToString(CultureInfo.InvariantCulture) + "] " + p.Nuclide
                            + (p.Trimmed ? " срез" : "");
                Num(ph + " energy", p.Energy);
                Num(ph + " area", p.Area);
                Num(ph + " from", p.FromKev);
                Num(ph + " with", p.WithKev);
                Num(ph + " third", p.ThirdKev);
            }
        }

        /// <summary>Строка в строку, текстом: слово «X16» входит в строку, так что расхождение в 1 ulp видно всегда.</summary>
        static int Compare(string pairSpec)
        {
            string[] files = pairSpec.Split(',');
            if (files.Length != 2 || !File.Exists(files[0]) || !File.Exists(files[1]))
            {
                Console.Error.WriteLine("--compare=a.txt,b.txt: нужны два существующих файла");
                return 2;
            }

            string[] a = File.ReadAllLines(files[0], Encoding.UTF8);
            string[] b = File.ReadAllLines(files[1], Encoding.UTF8);
            int numbers = 0, differ = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (!a[i].StartsWith("#", StringComparison.Ordinal))
                {
                    numbers++;
                }

                if (a[i] == b[i])
                {
                    continue;
                }

                differ++;
                if (differ <= 20)
                {
                    Console.WriteLine("  строка {0}:\n    a: {1}\n    b: {2}", i + 1, a[i], b[i]);
                }
            }

            Console.WriteLine("сверка: строк {0} / {1}, чисел {2}, расходится строк {3}",
                              a.Length, b.Length, numbers, differ);
            if (a.Length != b.Length || differ > 0)
            {
                Console.WriteLine("ИТОГ: РАСХОЖДЕНИЕ");
                return 1;
            }

            Console.WriteLine("ИТОГ: ПОБИТОВО");
            return 0;
        }

        /// <summary>«Cs-137» → «137CS» (nucid, как его зовёт nucdb); nucid — как есть.</summary>
        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0
                    ? label.ToUpperInvariant()
                    : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
            }

            return nucids;
        }

        /// <summary>Спектр — тем же чтением, что у `FsaCascadeProbe` (прибор общим правилом `S82`).</summary>
        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            string deviceNote = ProbeDeviceConfig.Attach(rd);
            Console.WriteLine("прибор: {0}", deviceNote);
            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
