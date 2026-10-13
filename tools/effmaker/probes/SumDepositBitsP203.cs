using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace SumDepositBitsP203
{
    /// <summary>
    /// П203 (01.10.2026, `T265`): ПОБИТОВЫЙ СНИМОК ГИСТОГРАММ ПОГЛОЩЕНИЯ С
    /// КАСКАДНЫМИ ДОБАВКАМИ — приёмка ускорения <c>FsaAnalyzer.AccumulateSumPeaks</c>
    /// и того, что она зовёт (сумм-континуум, свёртка пар). Образец —
    /// `CascadeBitsP197` (там — числа суммирователя; здесь — то, что из них
    /// кладётся в образ).
    ///
    ///   sumdepositbitsp203 --spectrum=X.xml --sample=152EU[,137CS] [--chain=Th-232]
    ///                      --out=снимок.txt [--ulp=N]
    ///   sumdepositbitsp203 --compare=a.txt,b.txt
    ///
    /// Разбор идёт ЦЕЛИКОМ, как у `FsaComponentDumpProbe` (матрица — через
    /// `FsaMatrixBinding.Bind`), чтобы состояние анализатора (суммирователь,
    /// кривая света, правило переноса матрицы, маска каналов) было ровно тем,
    /// что ставит сам разбор. Потом для КАЖДОГО компонента из кэша гистограмм
    /// (там и члены рядов, нарезанные разбором) закрытый
    /// <c>BuildResponseDeposit</c> зовётся заново при четырёх β положения по
    /// свету (0 — путь `AccumulateShifted`; 0.37, 1 и β разбора — путь
    /// `AccumulateLight` с множителем ≠ 1) и при двух значениях
    /// <c>SumLayerIncludesContinuum</c> (true кладёт сумм-континуум и свёртку пар
    /// ещё и в подслой, то есть в ОДИН массив без каналов — второй путь
    /// приёмника). Каждый массив (лента, подслой, семь каналов) пишется длиной
    /// и SHA-256 своих 64-битных слов: числа напрямую дали бы гигабайты, а
    /// хэш слов меняется от любого бита.
    ///
    /// `--ulp=N` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: у N-го массива снимка первое
    /// ненулевое число берётся на единицу последнего разряда больше. Сверка с
    /// честным снимком обязана отказать кодом 1 ровно на этой строке.
    ///
    /// Сверка: код 0 — все строки совпали; 1 — расхождение (первые 20
    /// напечатаны); 2 — ошибка ключей/файлов/разбора.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static StreamWriter writer;
        static long arrays;
        static long ulpAt;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

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

            var analyzer = new FsaAnalyzer();
            FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
            analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            FsaTuningReport.Print(analyzer, "снимок образов");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разбор не состоялся: " + analyzer.Refusal);
                return 2;
            }

            Type type = typeof(FsaAnalyzer);
            FieldInfo depositsField = type.GetField("deposits", Any);
            FieldInfo lightField = type.GetField("driftLight", Any);
            FieldInfo cascadeField = type.GetField("cascade", Any);
            MethodInfo build = type.GetMethod("BuildResponseDeposit", Any);
            if (depositsField == null || lightField == null || cascadeField == null || build == null)
            {
                Console.Error.WriteLine("отражение: нет поля/метода (deposits, driftLight, cascade, BuildResponseDeposit)");
                return 2;
            }

            var components = new List<FsaComponent>();
            foreach (DictionaryEntry e in (IDictionary)depositsField.GetValue(analyzer))
            {
                components.Add((FsaComponent)e.Key);
            }

            double finalLight = (double)lightField.GetValue(analyzer);
            bool layerBefore = analyzer.SumLayerIncludesContinuum;
            using (writer = new StreamWriter(outPath, false, new UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                writer.WriteLine("# компонентов " + components.Count.ToString(CultureInfo.InvariantCulture)
                                 + ", суммирователь " + (cascadeField.GetValue(analyzer) != null ? "есть" : "НЕТ")
                                 + ", β разбора " + finalLight.ToString("R", CultureInfo.InvariantCulture)
                                 + ", chi2ndf " + result.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture));
                foreach (bool layer in new[] { false, true })
                {
                    analyzer.SumLayerIncludesContinuum = layer;
                    foreach (double beta in new[] { 0.0, 0.37, 1.0, finalLight })
                    {
                        lightField.SetValue(analyzer, beta);
                        for (int k = 0; k < components.Count; k++)
                        {
                            FsaComponent component = components[k];
                            object[] call = { component, true, null, null };
                            double[] values = (double[])build.Invoke(analyzer, call);
                            string head = string.Format(CultureInfo.InvariantCulture, "layer={0} beta={1:R} [{2}] {3}",
                                                        layer ? 1 : 0, beta, k, component.Name);
                            Dump(head + " values", values);
                            Dump(head + " sumPart", (double[])call[2]);
                            double[][] parts = (double[][])call[3];
                            if (parts == null)
                            {
                                writer.WriteLine(head + " channels\tnull");
                            }
                            else
                            {
                                for (int c = 0; c < parts.Length; c++)
                                {
                                    Dump(head + " ch" + c.ToString(CultureInfo.InvariantCulture), parts[c]);
                                }
                            }
                        }
                    }
                }
            }

            analyzer.SumLayerIncludesContinuum = layerBefore;
            lightField.SetValue(analyzer, finalLight);
            Console.WriteLine("снимок: компонентов {0}, массивов {1} за {2:F1} с -> {3}{4}", components.Count, arrays,
                              watch.Elapsed.TotalSeconds, outPath,
                              ulpAt > 0 ? string.Format(CultureInfo.InvariantCulture,
                                                        " (КОНТРОЛЬ: массив №{0} сдвинут на 1 ulp)", ulpAt) : "");
            return 0;
        }

        /// <summary>Массив снимка: «метка  длина  сумма(R)  SHA-256 слов». Под `--ulp=N` у N-го — первое ненулевое +1 ulp.</summary>
        static void Dump(string label, double[] values)
        {
            if (values == null)
            {
                writer.WriteLine(label + "\tnull");
                return;
            }

            arrays++;
            byte[] raw = new byte[values.Length * 8];
            double sum = 0.0;
            bool planted = arrays != ulpAt;
            for (int i = 0; i < values.Length; i++)
            {
                long bits = BitConverter.DoubleToInt64Bits(values[i]);
                if (!planted && values[i] != 0.0)
                {
                    bits += 1;
                    planted = true;
                }

                sum += BitConverter.Int64BitsToDouble(bits);
                byte[] b = BitConverter.GetBytes(bits);
                Buffer.BlockCopy(b, 0, raw, i * 8, 8);
            }

            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "");
            }

            writer.WriteLine(label + "\t" + values.Length.ToString(CultureInfo.InvariantCulture) + "\t"
                             + sum.ToString("R", CultureInfo.InvariantCulture) + "\t" + hash);
        }

        /// <summary>Строка в строку, текстом: хэш слов входит в строку, так что расхождение в 1 ulp видно всегда.</summary>
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
            int rows = 0, differ = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (!a[i].StartsWith("#", StringComparison.Ordinal))
                {
                    rows++;
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

            Console.WriteLine("сверка: строк {0} / {1}, массивов {2}, расходится строк {3}",
                              a.Length, b.Length, rows, differ);
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

        /// <summary>Спектр — тем же чтением, что у `CascadeBitsP197` (прибор общим правилом `S82`).</summary>
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
