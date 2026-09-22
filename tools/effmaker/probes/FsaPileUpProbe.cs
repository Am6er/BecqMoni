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

namespace FsaPileUpProbe
{
    /// <summary>
    /// (`AMBER49`, П120 22.09.2026) ОБРАЗ СЛУЧАЙНЫХ НАЛОЖЕНИЙ ПРОТИВ СИНТЕТИЧЕСКИХ
    /// НАЛОЖЕНИЙ С ИЗВЕСТНОЙ ДОЛЕЙ. Ревизия П119 утверждала, что колонка
    /// `FsaAnalyzer.BuildPileUpComponent` вынимает из спектра ОДИН отсчёт на
    /// пару вместо ДВУХ (`(s⊗s)/N − s` вместо `(s⊗s)/N − 2·s`), и потому
    /// активности всех нуклидов занижены на долю наложений `Rτ`. Здесь это
    /// меряется ЧИСЛОМ, а не чтением: в измеренный спектр подсаживаются
    /// наложения с известной долей `f` (розыгрыш пар: два отсчёта уходят со
    /// своих энергий, один приходит на сумму), и восстановленная скорость
    /// счёта нуклида сравнивается со скоростью на чистом спектре.
    ///
    /// Ожидание, если ревизия права: ДО правки смещение ≈ −f (первый порядок
    /// по `f`), ПОСЛЕ — в шуме. Положительный контроль в каждом прогоне —
    /// плечо `PileUp = false` на том же подсаженном спектре: без образа
    /// наложений убыль 2f ловить нечем, и смещение обязано быть СИЛЬНЕЕ −f
    /// (измерено 22.09.2026: между −f и −2f — часть прихода сумм-континуума
    /// ложится на образ нуклида и континуум). Если и оно слабее −f — подсадка
    /// не доехала, и мерка не мерит ничего.
    ///
    ///   fsapileupprobe --spectrum=X.xml [--set=<набор>] [--frac=0.02,0.05]
    ///                  [--seed=1] [--form=energy|light|both] [--huber=3] [--out=table.csv]
    ///
    /// Состав — как у витрины `Cs 137 в домике` (`--infer` с набором `--set=`):
    /// поиск пиков → вывод состава → библиотека из базы; библиотека собирается
    /// ОДИН раз на чистом спектре и подаётся всем плечам — иначе подсаженный
    /// сумм-пик мог бы сменить состав, и мерка смешала бы две вещи. Матрица —
    /// из склада `config\device\response\&lt;guid&gt;.rmx` рядом с пробой, как
    /// у `FsaStackShot`; запускать из рабочего каталога витрины (или его копии).
    ///
    /// Координата сложения (`--form=`): `energy` — сумма энергий, анализатор с
    /// `PileUpLightForm = false` (образ в той же координате — чистая мерка
    /// множителя убыли); `light` — сумма по свету Λ(E) = r(E)·E той же кривой,
    /// что берёт анализатор (`PileUpCurveUsed`, `FsaLightScale` отражением:
    /// класс внутренний), анализатор с умолчанием приложения. `both` — оба.
    /// Розыгрыш детерминирован зерном. `--huber=` — порог М-оценки Хубера
    /// анализатора (диагностика: при большой подсадке сумм-пик отбрасывается
    /// как выброс, и колонка недобирает; без ключа — умолчание анализатора).
    ///
    /// Печатает строки `PILEUP\t&lt;форма&gt;\t&lt;f&gt;\t&lt;пар&gt;\t&lt;за шкалой&gt;\t
    /// &lt;A_on/A0−1&gt;\t&lt;A_off/A0−1&gt;\t&lt;χ² on&gt;\t&lt;χ² off&gt;\t&lt;амплитуда образа/N&gt;`
    /// и итоговую таблицу; `--out=` — та же таблица в csv (точка — разделитель).
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null, setName = null, outPath = null, form = "both";
            var fracs = new List<double> { 0.02, 0.05 };
            int seed = 1;
            double huberM = double.NaN;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--form=", StringComparison.Ordinal)) form = a.Substring(7);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--huber=", StringComparison.Ordinal)) huberM = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--frac=", StringComparison.Ordinal))
                {
                    fracs.Clear();
                    foreach (string s in a.Substring(7).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        fracs.Add(double.Parse(s, CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (form != "energy" && form != "light" && form != "both")
            {
                Console.Error.WriteLine("--form= принимает energy | light | both");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (setName != null && !SelectSet(nuclides, setName))
            {
                return 2;
            }

            EnergySpectrum es = rd.EnergySpectrum;
            EnergyCalibration cal = es.EnergyCalibration;
            int channels = es.NumberOfChannels;
            int[] raw = es.Spectrum;
            long total = 0;
            foreach (int v in raw) total += v;
            double live = es.EffectiveLiveTime;
            double deadTime = rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null
                ? rd.DeviceConfig.InputDeviceConfig.DeadTime() : 0.0;
            double rate = live > 0.0 ? total / live : 0.0;
            Console.WriteLine("спектр: {0}; каналов {1}; всего {2} отсчётов; живое {3} с; {4} имп/с; мёртвое прибора {5} мкс; Rτ прибора {6}",
                              Path.GetFileName(spectrumPath), channels, total, F(live, "F1"), F(rate, "F1"),
                              F(deadTime * 1e6, "F3"), F(rate * deadTime, "E3"));

            // Состав — как у витрины: поиск пиков с подписями набора → вывод состава → база.
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            FsaCompositionInference.Report report;
            FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, rd, out report);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            Console.WriteLine("состав: " + report);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return 1;
            }

            var names = new List<string>();
            foreach (FsaComponent c in library) names.Add(c.Name + "(" + c.Kind + ")");
            Console.WriteLine("библиотека ({0}): {1}", library.Count, string.Join(", ", names));

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                Console.Error.WriteLine("матрицы нет или она не годна геометрии спектра ({0}) — запускать из рабочего каталога витрины",
                                        refusal);
                return 1;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            var peakConfig = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;

            Func<bool, bool, FsaAnalyzer> make = (pileUp, lightForm) =>
            {
                var an = new FsaAnalyzer();
                FsaMatrixBinding.Bind(an, rd.Efficiency.Geometry, matrix);
                an.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
                if (peakConfig != null)
                {
                    an.MinEnergy = peakConfig.Min_Range;
                    an.MaxEnergy = peakConfig.Max_Range;
                }

                an.PileUp = pileUp;
                an.PileUpLightForm = lightForm;
                if (!double.IsNaN(huberM))
                {
                    an.HuberM = huberM;
                }

                return an;
            };

            var forms = new List<bool>();
            if (form == "energy" || form == "both") forms.Add(false);
            if (form == "light" || form == "both") forms.Add(true);

            var table = new StringBuilder();
            table.AppendLine("form,frac,pairs,beyond_scale,bias_on,bias_off,chi2_clean,chi2_on,chi2_off,pile_amp_over_n_clean,pile_amp_over_n_on,target");
            int bad = 0;
            foreach (bool lightForm in forms)
            {
                string formName = lightForm ? "light" : "energy";
                Console.WriteLine();
                Console.WriteLine("=== форма {0} ===", formName);

                // Чистый спектр — опора.
                FsaAnalyzer a0 = make(true, lightForm);
                if (forms.IndexOf(lightForm) == 0)
                {
                    // (`T243`) Отчёт о настройках — ДО первого Analyze и один раз на пробу.
                    FsaTuningReport.Print(a0, "чистый спектр, наложения ВКЛ");
                }

                FsaResult r0 = a0.Analyze(es, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library, efficiency);
                if (r0 == null)
                {
                    Console.Error.WriteLine("чистый спектр: разложение не получилось: {0} — {1}", a0.Refusal, a0.RefusalNote);
                    return 1;
                }

                // Нуклид, за которым следим: образ распада с НАИБОЛЬШЕЙ
                // скоростью счёта на чистом спектре (у витрины — Cs-137; K-40
                // там ниже порога и даёт ноль). Имени в коде нет.
                string target = null;
                double rate0 = 0.0;
                foreach (FsaComponentResult c in r0.Components)
                {
                    if ((c.Kind == FsaComponentKind.Single || c.Kind == FsaComponentKind.Chain) && c.CountRate > rate0)
                    {
                        target = c.Name;
                        rate0 = c.CountRate;
                    }
                }

                if (target == null || !(rate0 > 0.0))
                {
                    Console.Error.WriteLine("на чистом спектре ни один образ распада не получил скорости — следить не за чем");
                    return 1;
                }

                double pile0 = RateOf(r0, FsaResult.PileUpLayerName) * r0.LiveTime;
                string curve = a0.PileUpCurveUsed;

                // Окно сумм-пика для диагностики: где колонка наложений
                // закрепляет амплитуду — ±3 % вокруг удвоенной энергии
                // сильнейшей линии образа-цели (у витрины 2×661.657). Окно
                // диагностическое, приговор по нему не выносится.
                double sumLineKev = 0.0, best = 0.0;
                foreach (FsaComponent c in library)
                {
                    if (!string.Equals(c.Name, target, StringComparison.Ordinal)) continue;
                    foreach (FsaLine line in c.Lines)
                    {
                        if (line.Intensity > best) { best = line.Intensity; sumLineKev = 2.0 * line.Energy; }
                    }
                }

                double sumLo = sumLineKev * 0.97, sumHi = sumLineKev * 1.03;
                Console.WriteLine("окно сумм-пика {0}…{1} кэВ: данные {2}, модель {3}, образ наложений {4}",
                                  F(sumLo, "F1"), F(sumHi, "F1"), F(WindowSum(raw, cal, sumLo, sumHi), "F0"),
                                  F(WindowSum(r0.Model, cal, sumLo, sumHi), "F0"),
                                  F(WindowSum(CurveOf(r0, FsaResult.PileUpLayerName), cal, sumLo, sumHi), "F0"));
                Console.WriteLine("чистый: {0} = {1} 1/с; χ²/ndf {2}; образ наложений: амплитуда {3} = {4}·N, z {5}; кривая формы: {6}; полоса {7}…{8}",
                                  target, F(rate0, "F4"), F(r0.Chi2Ndf, "F3"), F(pile0, "E4"), F(pile0 / total, "E4"),
                                  F(ZOf(r0, FsaResult.PileUpLayerName), "F1"), curve ?? "(энергия)", r0.FirstChannel, r0.LastChannel);
                if (deadTime > 0.0)
                {
                    // Амплитуда колонки в единицах N против Rτ прибора — с
                    // нынешней нормировкой колонки приход равен A·(s⊗s)/N², то
                    // есть A/N — доля пар, а Rτ — она же по физике.
                    Console.WriteLine("        амплитуда/N против Rτ прибора: {0} (1 — колонка мерит ровно пары)",
                                      F(pile0 / total / (rate * deadTime), "F3"));
                }

                // Координата сложения: та, в которой анализатор строит образ.
                Func<double, double> coord = e => e;
                if (lightForm && curve != null && curve != "energy")
                {
                    Type scale = typeof(FsaAnalyzer).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaLightScale");
                    MethodInfo rel = scale != null ? scale.GetMethod("Relative", BindingFlags.Public | BindingFlags.Static) : null;
                    if (rel == null)
                    {
                        Console.Error.WriteLine("FsaLightScale.Relative отражением не нашлась — форма по свету недоступна");
                        return 1;
                    }

                    string curveName = curve;
                    coord = e => e * (double)rel.Invoke(null, new object[] { curveName, e });
                }

                foreach (double f in fracs)
                {
                    long pairs, beyond;
                    EnergySpectrum synth = Synthesize(es, f, seed, coord, r0.FirstChannel, r0.LastChannel, out pairs, out beyond);
                    FsaAnalyzer aOn = make(true, lightForm);
                    FsaResult rOn = aOn.Analyze(synth, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library, efficiency);
                    FsaAnalyzer aOff = make(false, lightForm);
                    FsaResult rOff = aOff.Analyze(synth, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library, efficiency);
                    if (rOn == null || rOff == null)
                    {
                        Console.Error.WriteLine("f={0}: разложение не получилось (on: {1}, off: {2})", F(f, "F3"),
                                                aOn.Refusal, aOff.Refusal);
                        bad++;
                        continue;
                    }

                    double biasOn = RateOf(rOn, target) / rate0 - 1.0;
                    double biasOff = RateOf(rOff, target) / rate0 - 1.0;
                    double pileOn = RateOf(rOn, FsaResult.PileUpLayerName) * rOn.LiveTime / total;
                    Console.WriteLine("   окно сумм-пика: данные {0}, модель с образом {1} (образ {2}), модель без образа {3}",
                                      F(WindowSum(synth.Spectrum, cal, sumLo, sumHi), "F0"),
                                      F(WindowSum(rOn.Model, cal, sumLo, sumHi), "F0"),
                                      F(WindowSum(CurveOf(rOn, FsaResult.PileUpLayerName), cal, sumLo, sumHi), "F0"),
                                      F(WindowSum(rOff.Model, cal, sumLo, sumHi), "F0"));
                    Console.WriteLine("PILEUP\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}",
                                      formName, F(f, "F4"), pairs, beyond, F(biasOn, "+0.00000;-0.00000"),
                                      F(biasOff, "+0.00000;-0.00000"), F(rOn.Chi2Ndf, "F3"), F(rOff.Chi2Ndf, "F3"),
                                      F(pileOn, "E4"));
                    Console.WriteLine("   f={0}: пар {1} ({2} за шкалой); {3}: с образом {4} (ожидание: −f = {5} до правки, 0 после), без образа {6} (контроль: сильнее −f, убыль 2f = {7} ловить нечем); амплитуда образа/N {8} (было {9}, +f → {10})",
                                      F(f, "F4"), pairs, beyond, target, F(biasOn, "+0.00000;-0.00000"), F(-f, "+0.00000;-0.00000"),
                                      F(biasOff, "+0.00000;-0.00000"), F(-2 * f, "+0.00000;-0.00000"),
                                      F(pileOn, "E4"), F(pile0 / total, "E4"), F(pile0 / total + f, "E4"));
                    table.AppendLine(string.Join(",", formName, F(f, "R"), pairs.ToString(CultureInfo.InvariantCulture),
                                                 beyond.ToString(CultureInfo.InvariantCulture), F(biasOn, "R"), F(biasOff, "R"),
                                                 F(r0.Chi2Ndf, "R"), F(rOn.Chi2Ndf, "R"), F(rOff.Chi2Ndf, "R"),
                                                 F(pile0 / total, "R"), F(pileOn, "R"), target));

                    // Контроль обязан видеть подсадку: без образа наложений
                    // убыль 2f никто не ловит; смещение слабее −f — отказ мерки.
                    if (double.IsNaN(biasOn) || double.IsNaN(biasOff) || biasOff > -f)
                    {
                        Console.WriteLine("   ⛔ контроль: без образа смещение {0} слабее −f — подсадка не доехала, мерка не мерит",
                                          F(biasOff, "+0.00000;-0.00000"));
                        bad++;
                    }
                }
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, table.ToString(), new UTF8Encoding(false));
                Console.WriteLine("таблица: " + outPath);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "контроли сошлись" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Подсадить наложения долей f: N_пар = round(f·N) пар, у каждой два
        /// отсчёта уходят из своих каналов, один приходит в канал суммы в
        /// координате coord (энергия либо свет). Розыгрыш — по распределению
        /// ИСХОДНОГО спектра в полосе фита [chLo..chHi] (канал переполнения и
        /// подпороговые каналы в розыгрыше не участвуют). Внутри канала
        /// координата равномерна между его краями. Сумма за верхним краем
        /// шкалы — потеря обоих отсчётов без прихода (считается отдельно).
        /// </summary>
        static EnergySpectrum Synthesize(EnergySpectrum es, double f, int seed, Func<double, double> coord,
                                         int chLo, int chHi, out long pairs, out long beyond)
        {
            EnergySpectrum synth = es.Clone();
            int[] s = synth.Spectrum;
            int channels = s.Length;
            EnergyCalibration cal = es.EnergyCalibration;

            // Края каналов в координате сложения: edge[ch] — нижний край канала ch.
            double[] edge = new double[channels + 1];
            for (int ch = 0; ch <= channels; ch++)
            {
                double e = cal.ChannelToEnergy(ch - 0.5);
                edge[ch] = e > 0.0 ? coord(e) : 0.0;
            }

            // Кумулятивное распределение исходного спектра по полосе.
            long inBand = 0;
            long[] cum = new long[channels];
            for (int ch = 0; ch < channels; ch++)
            {
                if (ch >= chLo && ch <= chHi && s[ch] > 0)
                {
                    inBand += s[ch];
                }

                cum[ch] = inBand;
            }

            pairs = (long)Math.Round(f * inBand);
            beyond = 0;
            var rng = new Random(seed);
            for (long p = 0; p < pairs; p++)
            {
                int a = Draw(cum, inBand, rng), b = Draw(cum, inBand, rng);
                if (s[a] <= 0 || s[b] <= 0 || (a == b && s[a] < 2))
                {
                    p--;        // канал уже опустел — пара разыгрывается заново
                    continue;
                }

                double xa = edge[a] + rng.NextDouble() * (edge[a + 1] - edge[a]);
                double xb = edge[b] + rng.NextDouble() * (edge[b + 1] - edge[b]);
                double sum = xa + xb;
                s[a]--;
                s[b]--;
                int target = Locate(edge, sum);
                if (target < 0 || target >= channels)
                {
                    beyond++;
                    continue;
                }

                s[target]++;
            }

            long totalNow = 0;
            foreach (int v in s) totalNow += v;
            synth.TotalPulseCount = totalNow;
            synth.ValidPulseCount = totalNow;
            return synth;
        }

        /// <summary>Канал по кумулятивному распределению: первый ch с cum[ch] &gt; u.</summary>
        static int Draw(long[] cum, long total, Random rng)
        {
            long u = (long)(rng.NextDouble() * total);
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] > u) hi = mid; else lo = mid + 1;
            }

            return lo;
        }

        /// <summary>Канал, чей интервал [edge[ch], edge[ch+1]) содержит x; −1 ниже шкалы, channels — выше.</summary>
        static int Locate(double[] edge, double x)
        {
            int channels = edge.Length - 1;
            if (x < edge[0]) return -1;
            if (x >= edge[channels]) return channels;
            int lo = 0, hi = channels - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (edge[mid] <= x) lo = mid; else hi = mid - 1;
            }

            return lo;
        }

        static double RateOf(FsaResult r, string name)
        {
            foreach (FsaComponentResult c in r.Components)
            {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) return c.CountRate;
            }

            return 0.0;
        }

        static double[] CurveOf(FsaResult r, string name)
        {
            foreach (FsaComponentResult c in r.Components)
            {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) return c.Curve;
            }

            return null;
        }

        /// <summary>Сумма по каналам, чья энергия центра лежит в [lo, hi] кэВ; null — 0.</summary>
        static double WindowSum(double[] curve, EnergyCalibration cal, double lo, double hi)
        {
            if (curve == null) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < curve.Length; i++)
            {
                double e = cal.ChannelToEnergy(i);
                if (e >= lo && e <= hi) sum += curve[i];
            }

            return sum;
        }

        static double WindowSum(int[] curve, EnergyCalibration cal, double lo, double hi)
        {
            double sum = 0.0;
            for (int i = 0; i < curve.Length; i++)
            {
                double e = cal.ChannelToEnergy(i);
                if (e >= lo && e <= hi) sum += curve[i];
            }

            return sum;
        }

        static double ZOf(FsaResult r, string name)
        {
            foreach (FsaComponentResult c in r.Components)
            {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) return c.Z;
            }

            return double.NaN;
        }

        static string F(double v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            var have = new List<string>();
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set == null) continue;
                have.Add(set.Name);
                if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    Console.WriteLine("набор: {0}", set.Name);
                    return true;
                }
            }

            Console.Error.WriteLine("набора «{0}» нет; есть: {1}", name, string.Join(", ", have.ToArray()));
            return false;
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
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));
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
