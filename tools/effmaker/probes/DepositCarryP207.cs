using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace DepositCarryP207
{
    /// <summary>
    /// П207 (01.10.2026, `T265` вариант А): ПРИЁМКА КЭША ОБРАЗОВ ЧЕРЕЗ ПРОХОДЫ ФОНА
    /// (<c>FsaAnalyzer.DepositCarry</c>) — побитово и с положительным контролем
    /// на каждый вход ключа. Образец — `SumDepositBitsP203`.
    ///
    ///   depositcarryp207 --spectrum=X.xml --sample=..[,..] [--chain=..] --out=снимок.txt
    ///                    [--carry=0|1] [--keys]
    ///   depositcarryp207 --compare=a.txt,b.txt
    ///
    /// Снимок: разбор ЦЕЛИКОМ (матрица — <c>FsaMatrixBinding.Bind</c>) с кэшем
    /// (`--carry=1`, умолчание) или без (`--carry=0`); затем каждая гистограмма
    /// поглощения последнего прохода (кэш <c>deposits</c>: лента, хвост,
    /// подслой сумм, каналы) — длиной, суммой и SHA-256 своих 64-битных слов, и
    /// итог разбора (χ²/ndf, модель, скорость и z каждого компонента) — битами.
    /// Снимки «с кэшем» и «без» обязаны совпасть строка в строку.
    ///
    /// `--keys` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ КЛЮЧА после разбора с кэшем: для каждого
    /// входа ключа (β света, маска каналов, ключи суммирования, нож доверия,
    /// запас длины, правило переноса, калибровка и ПШПВ по ссылке, содержимое
    /// компонента) кэш прохода чистится, вход меняется, и <c>DepositOf</c>
    /// обязан ПРОМАХНУТЬСЯ (число построений растёт на число компонентов) и
    /// отдать то же, что честное построение <c>BuildResponseDeposit</c> при новом
    /// входе; вход возвращается — обязан попасть и отдать прежнее. Отдельно —
    /// контроль самой сверки: под новым ключом в кэш подсаживается СТАРАЯ
    /// гистограмма (так выглядел бы ключ, забывший вход), и сверка с честным
    /// построением обязана это поймать.
    ///
    /// Коды: 0 — всё сошлось; 1 — расхождение; 2 — ошибка ключей/файлов/разбора.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly Type T = typeof(FsaAnalyzer);

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null, outPath = null, compare = null;
            bool carry = true, keys = false;
            var chains = new List<string>();
            var nuclides = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--compare=", StringComparison.Ordinal)) compare = a.Substring(10);
                else if (a == "--carry=0") carry = false;
                else if (a == "--carry=1") carry = true;
                else if (a == "--keys") keys = true;
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

            if (keys && !carry)
            {
                Console.Error.WriteLine("--keys проверяет кэш: с --carry=0 проверять нечего");
                return 2;
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

            analyzer.DepositCarry = carry;
            FsaTuningReport.Print(analyzer, "кэш образов через проходы");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            double seconds = watch.Elapsed.TotalSeconds;
            if (result == null)
            {
                Console.Error.WriteLine("разбор не состоялся: " + analyzer.Refusal);
                return 2;
            }

            var deposits = (IDictionary)T.GetField("deposits", Any).GetValue(analyzer);
            var entries = new List<KeyValuePair<FsaComponent, object>>();
            foreach (DictionaryEntry e in deposits)
            {
                entries.Add(new KeyValuePair<FsaComponent, object>((FsaComponent)e.Key, e.Value));
            }

            // порядок снимка — по имени и по хэшу содержимого (имена членов рядов бывают одинаковы)
            entries = entries.OrderBy(e => e.Key.Name, StringComparer.Ordinal)
                             .ThenBy(e => ContentHash(e.Key), StringComparer.Ordinal).ToList();
            using (var w = new StreamWriter(outPath, false, new UTF8Encoding(false)))
            {
                w.NewLine = "\n";
                w.WriteLine("# гистограмм " + entries.Count.ToString(CultureInfo.InvariantCulture));
                w.WriteLine("chi2ndf\t" + result.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture));
                w.WriteLine(Line("model", result.Model));
                if (result.Components != null)
                {
                    foreach (FsaComponentResult c in result.Components)
                    {
                        w.WriteLine("component\t" + c.Name + "\t" + c.CountRate.ToString("R", CultureInfo.InvariantCulture)
                                    + "\t" + c.Z.ToString("R", CultureInfo.InvariantCulture));
                    }
                }

                foreach (var e in entries)
                {
                    string head = e.Key.Name + " " + ContentHash(e.Key).Substring(0, 12);
                    foreach (string row in DepositLines(head, e.Value))
                    {
                        w.WriteLine(row);
                    }
                }
            }

            Console.WriteLine("снимок: гистограмм {0}, кэш {1}: попаданий {2}, построений {3}; разбор {4:F2} с -> {5}",
                              entries.Count, carry ? "ВКЛ" : "выкл", analyzer.DepositCarryHits, analyzer.DepositCarryMisses,
                              seconds, outPath);
            if (!keys)
            {
                return 0;
            }

            return CheckKeys(analyzer, rd, entries.Select(e => e.Key).ToList());
        }

        /// <summary>Строки снимка одной гистограммы: лента, хвост, подслой, каналы.</summary>
        static IEnumerable<string> DepositLines(string head, object deposit)
        {
            if (deposit == null)
            {
                yield return head + "\tnull";
                yield break;
            }

            Type d = deposit.GetType();
            yield return Line(head + " values", (double[])d.GetField("Values").GetValue(deposit));
            yield return Line(head + " tail", (double[])d.GetField("Tail").GetValue(deposit));
            yield return Line(head + " sumPart", (double[])d.GetField("SumPart").GetValue(deposit));
            var channels = (double[][])d.GetField("Channels").GetValue(deposit);
            if (channels == null)
            {
                yield return head + " channels\tnull";
            }
            else
            {
                for (int c = 0; c < channels.Length; c++)
                {
                    yield return Line(head + " ch" + c.ToString(CultureInfo.InvariantCulture), channels[c]);
                }
            }

            yield return head + " cascadeApplied\t" + ((bool)d.GetField("CascadeApplied").GetValue(deposit) ? "1" : "0");
        }

        /// <summary>«метка  длина  сумма(R)  SHA-256 64-битных слов».</summary>
        static string Line(string label, double[] values)
        {
            if (values == null)
            {
                return label + "\tnull";
            }

            byte[] raw = new byte[values.Length * 8];
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
                Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(values[i])), 0, raw, i * 8, 8);
            }

            using (SHA256 sha = SHA256.Create())
            {
                return label + "\t" + values.Length.ToString(CultureInfo.InvariantCulture) + "\t"
                       + sum.ToString("R", CultureInfo.InvariantCulture) + "\t"
                       + BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "");
            }
        }

        /// <summary>Хэш содержимого компонента (имя, вид, линии битами) — порядок снимка, не ключ кэша.</summary>
        static string ContentHash(FsaComponent c)
        {
            var sb = new StringBuilder();
            sb.Append(c.Name).Append('|').Append((int)c.Kind).Append('|').Append(c.Lines.Count);
            foreach (FsaLine l in c.Lines)
            {
                sb.Append('|').Append(l.Nuclide).Append(':').Append(BitConverter.DoubleToInt64Bits(l.Energy))
                  .Append(':').Append(BitConverter.DoubleToInt64Bits(l.Intensity));
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
            }
        }

        sealed class Mutation
        {
            public string Name;
            public Action Apply, Restore;
            public bool AllMiss = true;  // обязан ли промахнуться каждый компонент
            public bool? ChangesNumbers;  // ожидается ли другое число: нет — смена ссылки на равную копию; null — вход может не задевать этот спектр
        }

        /// <summary>Положительный контроль ключа — см. описание класса.</summary>
        static int CheckKeys(FsaAnalyzer analyzer, ResultData rd, List<FsaComponent> components)
        {
            var deposits = (IDictionary)T.GetField("deposits", Any).GetValue(analyzer);
            var carry = (IDictionary)T.GetField("depositCarry", Any).GetValue(analyzer);
            var refs = (IList)T.GetField("carryRefs", Any).GetValue(analyzer);
            MethodInfo depositOf = T.GetMethod("DepositOf", Any);
            MethodInfo depositKey = T.GetMethod("DepositKey", Any);
            MethodInfo build = T.GetMethod("BuildResponseDeposit", Any);
            FieldInfo light = T.GetField("driftLight", Any);
            FieldInfo margin = T.GetField("lightMarginBins", Any);
            if (depositOf == null || depositKey == null || build == null || light == null || margin == null)
            {
                Console.Error.WriteLine("отражение: нет DepositOf/DepositKey/BuildResponseDeposit/driftLight/lightMarginBins");
                return 2;
            }

            // калибровка и ПШПВ — те, что разбор клал в ключ (по ссылке)
            EnergyCalibration calibration = null;
            FwhmCalibration fwhm = null;
            int nCal = 0, nFwhm = 0;
            foreach (object o in refs)
            {
                if (o is EnergyCalibration ec) { nCal++; if (calibration == null) calibration = ec; }
                if (o is FwhmCalibration fc) { nFwhm++; if (fwhm == null) fwhm = fc; }
            }

            if (calibration == null || fwhm == null || nCal != 1 || nFwhm != 1)
            {
                Console.Error.WriteLine("в ключах разбора калибровок {0}, ПШПВ {1} — ждалось по одной", nCal, nFwhm);
                return 2;
            }

            int channels = rd.EnergySpectrum.NumberOfChannels;
            Func<FsaComponent, object> viaCache = c =>
                depositOf.Invoke(analyzer, new object[] { c, calibration, fwhm, channels });
            // честное построение — тот же DepositOf с выключенным кэшем через проходы
            Func<FsaComponent, string> fresh = c =>
            {
                analyzer.DepositCarry = false;
                deposits.Clear();
                object d = depositOf.Invoke(analyzer, new object[] { c, calibration, fwhm, channels });
                deposits.Clear();
                analyzer.DepositCarry = true;
                return string.Join("\n", DepositLines("", d));
            };
            // базовый снимок через кэш
            var baseline = new Dictionary<FsaComponent, string>();
            deposits.Clear();
            int m0 = analyzer.DepositCarryMisses;
            foreach (FsaComponent c in components) baseline[c] = string.Join("\n", DepositLines("", viaCache(c)));
            int bad = 0;
            if (analyzer.DepositCarryMisses != m0)
            {
                Console.WriteLine("ОТКАЗ: базовый проход по кэшу дал {0} построений, ждалось 0", analyzer.DepositCarryMisses - m0);
                bad++;
            }

            bool sumPeaks = analyzer.CascadeSumPeaks, layer = analyzer.SumLayerIncludesContinuum,
                 escape = analyzer.CascadeEscapeLoss, transfer = analyzer.ResponseMatrix.TransferByChannel;
            int mask = analyzer.MatrixChannelMask;
            double floor = analyzer.ResponseContinuumTrustFloorKev;
            double beta = (double)light.GetValue(analyzer);
            // β у форм line/peak — признак 0/1 (0.37 кладёт то же, что 1), поэтому другое число даёт только
            // другой признак; β = 0 разбор проходит сам (первый проход), и часть гистограмм в кэше законно есть —
            // у этого входа промахи не судятся, судится честность отданного
            double betaOther = beta == 0.0 ? 1.0 : 0.0;
            int marginBins = (int)margin.GetValue(analyzer);
            // есть ли у разбора образ с каскадными добавками: без них ключи суммирования образ не задевают
            bool? cascadeAny = null;
            foreach (FsaComponent c in components)
            {
                if (baseline[c].Contains(" cascadeApplied\t1")) cascadeAny = true;
            }
            EnergyCalibration calibrationCopy = calibration.Clone();
            FwhmCalibration fwhmCopy = fwhm.Clone();
            EnergyCalibration calUsed = calibration;
            FwhmCalibration fwhmUsed = fwhm;
            var mutations = new List<Mutation>
            {
                new Mutation { Name = "β света", ChangesNumbers = true, AllMiss = false,
                    Apply = () => light.SetValue(analyzer, betaOther), Restore = () => light.SetValue(analyzer, beta) },
                new Mutation { Name = "маска каналов", ChangesNumbers = true,
                    Apply = () => analyzer.MatrixChannelMask = 1, Restore = () => analyzer.MatrixChannelMask = mask },
                new Mutation { Name = "сумм-пики", ChangesNumbers = cascadeAny,
                    Apply = () => analyzer.CascadeSumPeaks = !sumPeaks, Restore = () => analyzer.CascadeSumPeaks = sumPeaks },
                new Mutation { Name = "подслой с континуумом", ChangesNumbers = cascadeAny,
                    Apply = () => analyzer.SumLayerIncludesContinuum = !layer, Restore = () => analyzer.SumLayerIncludesContinuum = layer },
                new Mutation { Name = "вынос партнёром", ChangesNumbers = null,
                    Apply = () => analyzer.CascadeEscapeLoss = !escape, Restore = () => analyzer.CascadeEscapeLoss = escape },
                new Mutation { Name = "нож доверия", ChangesNumbers = true,
                    Apply = () => analyzer.ResponseContinuumTrustFloorKev = floor + 50.0, Restore = () => analyzer.ResponseContinuumTrustFloorKev = floor },
                new Mutation { Name = "запас длины", ChangesNumbers = true,
                    Apply = () => margin.SetValue(analyzer, marginBins + 3), Restore = () => margin.SetValue(analyzer, marginBins) },
                new Mutation { Name = "правило переноса", ChangesNumbers = true,
                    Apply = () => analyzer.ResponseMatrix.TransferByChannel = !transfer, Restore = () => analyzer.ResponseMatrix.TransferByChannel = transfer },
                new Mutation { Name = "калибровка (равная копия)", ChangesNumbers = false,
                    Apply = () => calibration = calibrationCopy, Restore = () => calibration = calUsed },
                new Mutation { Name = "ПШПВ (равная копия)", ChangesNumbers = false,
                    Apply = () => fwhm = fwhmCopy, Restore = () => fwhm = fwhmUsed },
            };

            Console.WriteLine("контроль ключа: компонентов {0}", components.Count);
            foreach (Mutation m in mutations)
            {
                m.Apply();
                deposits.Clear();
                int before = analyzer.DepositCarryMisses;
                int stale = 0, changed = 0;
                foreach (FsaComponent c in components)
                {
                    object dep = viaCache(c);
                    string got = string.Join("\n", DepositLines("", dep));
                    if (got != baseline[c]) changed++;
                    // честное построение при новом входе — то же, что отдал кэш
                    if (got != fresh(c)) stale++;
                }

                int misses = analyzer.DepositCarryMisses - before;
                m.Restore();
                deposits.Clear();
                int back = analyzer.DepositCarryMisses;
                int backDiffer = 0;
                foreach (FsaComponent c in components)
                {
                    if (string.Join("\n", DepositLines("", viaCache(c))) != baseline[c]) backDiffer++;
                }

                int backMisses = analyzer.DepositCarryMisses - back;
                bool ok = (!m.AllMiss || misses == components.Count) && stale == 0 && (m.ChangesNumbers == null || (changed > 0) == m.ChangesNumbers.Value)
                          && backMisses == 0 && backDiffer == 0;
                if (!ok) bad++;
                Console.WriteLine("  {0,-26} промахов {1}/{2}{9}, другое число у {3}{4}, расхождений с честным построением {5}; возврат: промахов {6}, отличий {7} — {8}",
                                  m.Name, misses, components.Count, changed, m.ChangesNumbers == null ? " (не судится)" : (m.ChangesNumbers.Value ? "" : " (ждалось 0)"),
                                  stale, backMisses, backDiffer, ok ? "ВЕРНО" : "ОТКАЗ", m.AllMiss ? "" : " (не судится)");
            }

            // контроль самой сверки: ключ, «забывший» β, — старая гистограмма под новым ключом
            {
                FsaComponent victim = components.First(c => !baseline[c].StartsWith("\tnull", StringComparison.Ordinal));
                light.SetValue(analyzer, betaOther);
                deposits.Clear();
                string newKey = (string)depositKey.Invoke(analyzer, new object[] { victim, calibration, fwhm, channels });
                light.SetValue(analyzer, beta);
                deposits.Clear();
                object old = viaCache(victim);
                light.SetValue(analyzer, betaOther);
                deposits.Clear();
                carry[newKey] = old;   // подсадка
                object got = viaCache(victim);
                bool caught = string.Join("\n", DepositLines("", got)) != fresh(victim);
                light.SetValue(analyzer, beta);
                carry.Remove(newKey);
                deposits.Clear();
                if (!caught) bad++;
                Console.WriteLine("  КОНТРОЛЬ СВЕРКИ: подсаженная старая гистограмма «{0}» под ключом с другим β — {1}",
                                  victim.Name, caught ? "ПОЙМАНА (верно)" : "НЕ ПОЙМАНА (ОТКАЗ)");
            }

            // содержимое компонента: копия с одной линией на 1 % сильнее — другой ключ
            {
                FsaComponent src = components.First(c => c.Lines.Count > 0);
                var copy = new FsaComponent(src.Name, src.Kind) { DecayChainRoot = src.DecayChainRoot, TotalYieldPercent = src.TotalYieldPercent };
                for (int i = 0; i < src.Lines.Count; i++)
                {
                    FsaLine l = src.Lines[i];
                    copy.Lines.Add(new FsaLine(l.Nuclide, l.Energy, i == 0 ? l.Intensity * 1.01 : l.Intensity, l.AnnihilationIntensity));
                }

                var same = new FsaComponent(src.Name, src.Kind) { DecayChainRoot = src.DecayChainRoot, TotalYieldPercent = src.TotalYieldPercent };
                foreach (FsaLine l in src.Lines) same.Lines.Add(new FsaLine(l.Nuclide, l.Energy, l.Intensity, l.AnnihilationIntensity));
                same.WeightsAreFinal = src.WeightsAreFinal; copy.WeightsAreFinal = src.WeightsAreFinal;
                same.Derived = src.Derived; copy.Derived = src.Derived;
                same.FromCrystal = src.FromCrystal; copy.FromCrystal = src.FromCrystal;
                same.CrystalEscape = src.CrystalEscape; copy.CrystalEscape = src.CrystalEscape;
                same.EscapeParent = src.EscapeParent; copy.EscapeParent = src.EscapeParent;
                same.AmplitudeCap = src.AmplitudeCap; copy.AmplitudeCap = src.AmplitudeCap;
                same.Ties = src.Ties; copy.Ties = src.Ties;
                deposits.Clear();
                int b0 = analyzer.DepositCarryMisses;
                object dSame = viaCache(same);
                int mSame = analyzer.DepositCarryMisses - b0;
                int b1 = analyzer.DepositCarryMisses;
                object dCopy = viaCache(copy);
                int mCopy = analyzer.DepositCarryMisses - b1;
                bool sameOk = mSame == 0 && string.Join("\n", DepositLines("", dSame)) == baseline[src];
                bool copyOk = mCopy == 1 && string.Join("\n", DepositLines("", dCopy)) != baseline[src] && string.Join("\n", DepositLines("", dCopy)) == fresh(copy);
                if (!sameOk || !copyOk) bad++;
                Console.WriteLine("  содержимое «{0}»: новый объект с теми же линиями — промахов {1}, число {2}; линия ×1.01 — промахов {3}, число {4} — {5}",
                                  src.Name, mSame, sameOk ? "прежнее" : "ДРУГОЕ", mCopy, copyOk ? "другое и честное" : "НЕ ТО",
                                  sameOk && copyOk ? "ВЕРНО" : "ОТКАЗ");
            }

            Console.WriteLine("ИТОГ КОНТРОЛЯ КЛЮЧА: {0}", bad == 0 ? "ВСЁ ВЕРНО" : "ОТКАЗОВ " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Строка в строку (как у `SumDepositBitsP203`).</summary>
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
            int differ = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] == b[i]) continue;
                differ++;
                if (differ <= 20) Console.WriteLine("  строка {0}:\n    a: {1}\n    b: {2}", i + 1, a[i], b[i]);
            }

            Console.WriteLine("сверка: строк {0} / {1}, расходится {2}", a.Length, b.Length, differ);
            bool same = a.Length == b.Length && differ == 0;
            Console.WriteLine("ИТОГ: " + (same ? "ПОБИТОВО" : "РАСХОЖДЕНИЕ"));
            return same ? 0 : 1;
        }

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

        /// <summary>Спектр — тем же чтением, что у `SumDepositBitsP203`.</summary>
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
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            Console.WriteLine("прибор: {0}", ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
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
