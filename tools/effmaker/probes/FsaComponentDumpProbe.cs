using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaComponentDumpProbe
{
    /// <summary>
    /// СОСТАВ РАЗБОРА ПОИМЁННО: какие линии у каждого компонента и где лежит
    /// его лента.
    ///
    /// Заведена 11.09.2026 по разбору внешнего рецензента. Он спросил
    /// «фактические списки `FsaLine` для Cs-137 и Ba-131», и вопрос оказался
    /// не праздным: по картинке видно только СУММУ состава, а спор «это линия
    /// нуклида или артефакт подгонки» решается лишь списком линий и лентой
    /// того компонента, которому её приписали.
    ///
    /// ⛔ Своего разбора здесь НЕТ: библиотека собирается тем же
    /// `FsaLibrary.BuildFromPeaks`, которым её собирает окно, а считает тот же
    /// `FsaAnalyzer`. Проба только печатает то, что у них получилось.
    ///
    ///     fsacomponentdumpprobe --spectrum=&lt;файл.xml&gt; [--out=&lt;префикс&gt;]
    ///                           [--set=Имя] [--matrix-any]
    ///
    /// Печатает состав библиотеки и состав разбора; пишет `&lt;префикс&gt;.csv`
    /// (канал, энергия, измерение, модель, лента каждого компонента) и
    /// `&lt;префикс&gt;-lines.csv` (компонент, энергия линии, выход).
    /// </summary>
    static class Program
    {
        static readonly string[] ChannelNames =
        {
            "peak", "compton", "esc_se", "esc_xray", "esc_de"
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            string outPrefix = null;
            string setName = null;
            bool matrixAny = false;
            bool noCascade = false;
            string stagesOf = null;
            var apparent = new List<string>();

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else if (a.StartsWith("--out=", StringComparison.Ordinal))
                {
                    outPrefix = a.Substring(6);
                }
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    setName = a.Substring(6);
                }
                else if (a == "--matrix-any")
                {
                    matrixAny = true;
                }
                else if (a == "--no-cascade")
                {
                    noCascade = true;
                }
                else if (a.StartsWith("--stages=", StringComparison.Ordinal))
                {
                    stagesOf = a.Substring(9);
                }
                else if (a.StartsWith("--apparent=", StringComparison.Ordinal))
                {
                    apparent.Add(a.Substring(11));
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (outPrefix == null)
            {
                outPrefix = "components";
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));
            if (setName != null)
            {
                foreach (NuclideSet set in nuclides.NuclideSets)
                {
                    if (set != null && string.Equals(set.Name, setName, StringComparison.Ordinal))
                    {
                        nuclides.ActiveSet = set;
                    }
                }
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            bool stampOk = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат {1})", refusal, fileFormat);
                return 1;
            }

            if (!stampOk && !matrixAny)
            {
                Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ; осознанно — ключ --matrix-any");
                return 1;
            }

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);

            Console.WriteLine("библиотека прибора: {0} определений",
                              CountOf(nuclides.NuclideDefinitions));
            Console.WriteLine("найдено пиков: {0}; образов собрано: {1}", peaks.Count, library.Count);
            Console.WriteLine();
            Console.WriteLine("=== СОСТАВ БИБЛИОТЕКИ: линии каждого образа ===");

            var lineRows = new List<string>();
            lineRows.Add("component;kind;energy_kev;intensity_pct");
            foreach (FsaComponent component in library)
            {
                Console.WriteLine("  {0,-14} {1,-10} линий {2}",
                                  component.Name, component.Kind, component.Lines.Count);
                foreach (FsaLine line in component.Lines)
                {
                    Console.WriteLine("      {0,10} кэВ   выход {1,8} %",
                                      F(line.Energy, 3), F(line.Intensity, 4));
                    lineRows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0};{1};{2};{3}", component.Name, component.Kind,
                        F(line.Energy, 4), F(line.Intensity, 5)));
                }
            }

            string material = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);
            var analyzer = new FsaAnalyzer { ResponseMatrix = matrix, ScintillatorMaterial = material };
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

            // ⛔ Выключаются ОБЕ половины каскада, а не одна: поправка `CF`
            // правит амплитуду линии, а сумм-пики ДОБАВЛЯЮТ в образ энергии,
            // которых в списке линий нет. Одного `CascadeSumPeaks = false`
            // мало, если сам сумматор остался включён — на это указал внешний
            // рецензент 11.09.2026, и он прав.
            if (noCascade)
            {
                analyzer.CascadeSumming = false;
                analyzer.CascadeSumPeaks = false;
                Console.WriteLine("⚠ КАСКАД ВЫКЛЮЧЕН ЦЕЛИКОМ: CascadeSumming = CascadeSumPeaks = false");
            }

            FsaTuningReport.Print(analyzer, "состав разбора");

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("разбор : chi2/ndf {0}, состав {1}",
                              F(result.Chi2Ndf, 3), result.Components.Count);

            // ⛔ ВТОРОЙ ПРОХОД ПО ТОЙ ЖЕ БИБЛИОТЕКЕ — не украшение. Анализатор
            // достраивает состав сам (образы вылета, обратное рассеяние,
            // наложения), и вопрос «откуда у образа одной линии пик за 500 кэВ»
            // решается только сравнением списка ДО и ПОСЛЕ разбора.
            Console.WriteLine();
            Console.WriteLine("=== ЛИНИИ ОБРАЗОВ ПОСЛЕ РАЗБОРА (та же библиотека) ===");
            foreach (FsaComponent component in library)
            {
                Console.WriteLine("  {0,-14} {1,-10} линий {2}",
                                  component.Name, component.Kind, component.Lines.Count);
                if (component.Lines.Count <= 12)
                {
                    foreach (FsaLine line in component.Lines)
                    {
                        Console.WriteLine("      {0,10} кэВ   выход {1,8} %   нуклид {2}",
                                          F(line.Energy, 3), F(line.Intensity, 4), line.Nuclide);
                    }
                }
            }

            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int channels = rd.EnergySpectrum.NumberOfChannels;

            // Измерение — то же, что видит человек: за вычетом фона, который
            // подобрал сам разбор.
            double[] measured = new double[channels];
            int[] raw = rd.EnergySpectrum.Spectrum;
            for (int i = 0; i < channels; i++)
            {
                double back = result.Background != null && i < result.Background.Length
                    ? result.Background[i] : 0.0;
                measured[i] = (i < raw.Length ? raw[i] : 0.0) - back;
            }

            // Где лента компонента вообще не ноль — тот самый вопрос «может ли
            // образ одной линии 31 кэВ давать отсчёты при 160».
            Console.WriteLine();
            Console.WriteLine("=== ГДЕ ЛЕЖИТ ЛЕНТА КАЖДОГО КОМПОНЕНТА ===");
            Console.WriteLine("{0,-14} {1,10} {2,10} {3,14} {4,12}",
                              "компонент", "от, кэВ", "до, кэВ", "площадь", "максимум, кэВ");
            foreach (FsaComponentResult component in result.Components)
            {
                double[] curve = component.Curve;
                if (curve == null)
                {
                    continue;
                }

                int lo = -1, hi = -1, top = -1;
                double sum = 0.0, best = 0.0;
                for (int i = 0; i < curve.Length && i < channels; i++)
                {
                    if (!(curve[i] > 0.0))
                    {
                        continue;
                    }

                    if (lo < 0)
                    {
                        lo = i;
                    }

                    hi = i;
                    sum += curve[i];
                    if (curve[i] > best)
                    {
                        best = curve[i];
                        top = i;
                    }
                }

                if (lo < 0)
                {
                    continue;
                }

                Console.WriteLine("{0,-14} {1,10} {2,10} {3,14} {4,12}",
                                  component.Name,
                                  F(calibration.ChannelToEnergy(lo), 1),
                                  F(calibration.ChannelToEnergy(hi), 1),
                                  F(sum, 1),
                                  F(calibration.ChannelToEnergy(top), 1));
            }

            if (stagesOf != null)
            {
                Stages(analyzer, library, stagesOf, matrix.BinKev);
            }

            if (apparent.Count > 0)
            {
                Apparent(analyzer, apparent);
            }

            var rows = new List<string>();
            var header = new StringBuilder("channel;energy_kev;measured;model");
            foreach (FsaComponentResult component in result.Components)
            {
                header.Append(';').Append(component.Name);
            }

            rows.Add(header.ToString());
            for (int i = 0; i < channels; i++)
            {
                var sb = new StringBuilder();
                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(';');
                sb.Append(F(calibration.ChannelToEnergy(i), 3)).Append(';');
                sb.Append(F(i < measured.Length ? measured[i] : 0.0, 3)).Append(';');
                sb.Append(F(result.Model != null && i < result.Model.Length ? result.Model[i] : 0.0, 3));
                foreach (FsaComponentResult component in result.Components)
                {
                    double[] curve = component.Curve;
                    sb.Append(';').Append(F(curve != null && i < curve.Length ? curve[i] : 0.0, 3));
                }

                rows.Add(sb.ToString());
            }

            File.WriteAllLines(outPrefix + ".csv", rows, new UTF8Encoding(false));
            File.WriteAllLines(outPrefix + "-lines.csv", lineRows, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("записано: {0}.csv и {0}-lines.csv", outPrefix);
            return 0;
        }

        /// <summary>
        /// СТАДИИ ОБРАЗА ОДНОГО КОМПОНЕНТА — то, что просил внешний рецензент
        /// 11.09.2026: ненулевые вклады после обычных линий, после каскадных
        /// добавок и перед уширением, с энергией, весом и происхождением.
        ///
        /// Читается ОТРАЖЕНИЕМ из кэша `FsaAnalyzer.deposits`, как это уже
        /// делает `FsaChannelSplitProbe`: своего построения здесь нет, иначе
        /// проба мерила бы не то, что пошло в фит. `Values` — гистограмма
        /// целиком (она и идёт в уширение), `SumPart` — ТОЛЬКО каскадные
        /// добавки, значит «после обычных линий» = `Values − SumPart`.
        ///
        /// Происхождение добавок печатает сам сумматор (`Describe`): энергия
        /// суммы, слагаемые и площадь.
        /// </summary>
        static void Stages(FsaAnalyzer analyzer, List<FsaComponent> library, string name,
                           double binKev)
        {
            FieldInfo field = typeof(FsaAnalyzer).GetField(
                "deposits", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                Console.Error.WriteLine("⛔ поля кэша `deposits` нет — проба смотрит не туда");
                return;
            }

            var cache = field.GetValue(analyzer) as System.Collections.IDictionary;
            if (cache == null || cache.Count == 0)
            {
                Console.Error.WriteLine("⛔ кэш гистограмм пуст");
                return;
            }

            foreach (System.Collections.DictionaryEntry entry in cache)
            {
                var component = entry.Key as FsaComponent;
                if (component == null
                    || !string.Equals(component.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object deposit = entry.Value;
                if (deposit == null)
                {
                    Console.WriteLine("у образа {0} гистограммы нет вовсе", name);
                    return;
                }

                double[] values = FieldOf<double[]>(deposit, "Values");
                double[] sumPart = FieldOf<double[]>(deposit, "SumPart");
                double[] tail = FieldOf<double[]>(deposit, "Tail");

                Console.WriteLine();
                Console.WriteLine("=== СТАДИИ ОБРАЗА «{0}» (бин {1} кэВ) ===",
                                  name, F(binKev, 2));
                Console.WriteLine("линий в образе: {0}", component.Lines.Count);
                Console.WriteLine("длина гистограммы: {0} бинов (верх {1} кэВ)",
                                  values != null ? values.Length : 0,
                                  F(values != null ? (values.Length - 1) * binKev : 0.0, 1));
                Console.WriteLine("каскадных добавок в гистограмме: {0}",
                                  sumPart != null ? "есть" : "нет");
                Console.WriteLine("подпороговый хвост: {0}", tail != null ? "отвязан" : "нет");
                Console.WriteLine();
                Console.WriteLine("{0,8} {1,10} {2,16} {3,16} {4,16}",
                                  "бин", "кэВ", "только линии", "каскадные суммы", "всего");

                int shown = 0;
                for (int b = 0; values != null && b < values.Length; b++)
                {
                    double total = values[b];
                    double sum = sumPart != null && b < sumPart.Length ? sumPart[b] : 0.0;
                    double lines = total - sum;
                    if (!(total > 0.0) && !(sum > 0.0))
                    {
                        continue;
                    }

                    // Печатаются ВСЕ ненулевые бины каскадной части и только
                    // заметные бины линий: у линии их полторы сотни, у добавок
                    // единицы, и вопрос стоит о добавках.
                    if (!(sum > 0.0) && shown > 24)
                    {
                        continue;
                    }

                    Console.WriteLine("{0,8} {1,10} {2,16} {3,16} {4,16}",
                                      b, F(b * binKev, 1), E(lines), E(sum), E(total));
                    shown++;
                }

                FsaCascadeSummer summer = FieldOf<FsaCascadeSummer>(analyzer, "cascade");
                if (summer != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("=== ЧТО ДОБАВИЛ КАСКАДНЫЙ СУММАТОР (его собственный отчёт) ===");
                    Console.WriteLine(summer.Describe(component));
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("каскадного сумматора у разбора НЕТ (выключен)");
                }

                return;
            }

            Console.WriteLine("образа «{0}» в кэше нет", name);
        }

        /// <summary>
        /// (`AMBER16`) ВИДИМАЯ СУММА ПАРЫ против арифметической. Внешний
        /// рецензент 11.09.2026 заметил, что в отчёте сумматора равенства
        /// «123.80 + 30.97 = 157.89» арифметически неверны на +3.12 кэВ, и
        /// попросил две проверки: при пропорциональном световыходе результат
        /// обязан равняться обычной сумме, а `ApparentSum(E, 0)` — самой `E`.
        /// Обе делаются здесь, на живой кривой света разбора.
        /// </summary>
        static void Apparent(FsaAnalyzer analyzer, List<string> pairs)
        {
            FsaCascadeSummer summer = FieldOf<FsaCascadeSummer>(analyzer, "cascade");
            if (summer == null)
            {
                Console.Error.WriteLine("⛔ каскадного сумматора у разбора нет — считать нечем");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== ВИДИМАЯ СУММА ПРОТИВ АРИФМЕТИЧЕСКОЙ ===");
            Console.WriteLine("{0,-28} {1,14} {2,14} {3,10}",
                              "слагаемые, кэВ", "арифм. сумма", "ApparentSum", "разница");
            foreach (string text in pairs)
            {
                string[] parts = text.Split(',');
                double a = parts.Length > 0 ? Parse(parts[0]) : 0.0;
                double b = parts.Length > 1 ? Parse(parts[1]) : 0.0;
                double c = parts.Length > 2 ? Parse(parts[2]) : 0.0;
                double plain = a + b + c;
                double seen = summer.ApparentSum(a, b, c);
                Console.WriteLine("{0,-28} {1,14} {2,14} {3,10}",
                                  text, F(plain, 3), F(seen, 3), F(seen - plain, 3));
            }

            // ⛔ Контроль, без которого таблица выше ничего не значит: сумма с
            // нулём обязана вернуть саму энергию. Если вернёт другое —
            // прибавка не «непропорциональность», а лишний пьедестал.
            Console.WriteLine();
            Console.WriteLine("контроль ApparentSum(E, 0) — обязан вернуть E:");
            foreach (double e in new[] { 30.973, 123.8, 661.657, 1460.82 })
            {
                double seen = summer.ApparentSum(e, 0.0);
                Console.WriteLine("   E = {0,9}   ApparentSum = {1,9}   разница {2,9}",
                                  F(e, 3), F(seen, 3), F(seen - e, 5));
            }
        }

        static double Parse(string text)
        {
            double value;
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                   out value) ? value : 0.0;
        }

        static T FieldOf<T>(object target, string name) where T : class
        {
            if (target == null)
            {
                return null;
            }

            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(target) as T : null;
        }

        static string E(double value)
        {
            return value.ToString("E4", CultureInfo.InvariantCulture);
        }

        static int CountOf(IEnumerable<NuclideDefinition> definitions)
        {
            int n = 0;
            if (definitions != null)
            {
                foreach (NuclideDefinition d in definitions)
                {
                    n++;
                }
            }

            return n;
        }

        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
        }

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

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                rd.FwhmCalibration = cfg.FwhmCalibration;
            }

            return rd;
        }
    }
}
