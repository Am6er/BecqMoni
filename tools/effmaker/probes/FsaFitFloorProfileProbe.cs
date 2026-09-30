using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaFitFloorProfileProbe
{
    /// <summary>
    /// ⛔ ПРОФИЛЬ ПОРОГА АЦП ПО КОРПУСУ И ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРАВИЛА ПОЛА
    /// (`A309`, П34 12.09.2026).
    ///
    ///     fsafitfloorprofileprobe [--spectra=&lt;каталог&gt;] [--out=&lt;csv&gt;] [--fraction=&lt;доля&gt;]
    ///
    /// Читает каждый XML корпуса (проба и встроенный фон), и для каждого из
    /// двух спектров печатает: первый ненулевой канал
    /// (<see cref="FsaBand.AdcFloorOf(int[], EnergyCalibration)"/> — то, что
    /// брал ключ `adc`), порог ПО РАМПЕ
    /// (<see cref="FsaBand.AdcThresholdOf"/>) и чем он взят, а по паре — пол
    /// правила (<see cref="FsaBand.PairThreshold"/>), чей порог победил и
    /// режет ли пол хоть один отсчёт (<see cref="FsaBand.FloorCutsData"/>;
    /// пол над одними пустыми каналами разбор не применяет).
    /// Таблица уходит в csv (`--out=`) — это артефакт правила: по нему видно,
    /// на каких приборах рампа есть (AS80, AS1Pro), где правило отступает к
    /// первому каналу (жёсткий рез ASN8/G1S, шум ASN16, пик на пороге у
    /// Cd-109/Ba-133 на G1S) и где пол задал ФОН (ASN8, OBS).
    ///
    /// ⛔ ДО таблицы — ЧЕТЫРЕ СИНТЕТИЧЕСКИХ КОНТРОЛЯ, каждый с известным
    /// ответом; отказ любого — код 1, и таблица тогда ничего не значит:
    ///
    ///   1. S-кривая (erf) от пустых каналов к ровному континууму — порог
    ///      обязан лечь у ВЕРХА рампы: не ниже 0.85 её уровня и не на самом
    ///      континууме;
    ///   2. жёсткий рез (ступенька) — порог = первый ненулевой канал;
    ///   3. гауссов пик сразу над жёстким резом, континуума под ним нет —
    ///      правило обязано ОТСТУПИТЬ к первому каналу, а не залезть на
    ///      склон;
    ///   4. подъём континуума на десятки кэВ (поглощение окна) — тоже
    ///      первый канал: рампой это не считается.
    ///
    /// ⛔ (`S204`, П189) И КОНТРОЛИ МОДЕЛИ РАМПЫ
    /// (<see cref="FsaBand.RampProfileOf"/>, <see cref="FsaBand.PairRampProfile"/>):
    ///
    ///   7. логистика в каналах шире границы правила (шкала G1S, около трёх
    ///      кэВ на канал) — середина и ширина восстанавливаются, множитель
    ///      выше верха рампы — ровно единица;
    ///   8. спектры БЕЗ рампы для модели — жёсткий рез, рампа уже границы
    ///      (её режет пол), пик на пороге, подъём поглощения — рампы НЕТ, и
    ///      разбор тогда побитово прежний;
    ///   9. пара: у пробы жёсткий рез ниже рампы фона (`OBS_UGlass`) — рампа
    ///      фона НЕ прикладывается; у пробы пик на пороге (Cd-109) — рампа
    ///      берётся по фону; фона нет — по пробе; модель выключена — нет.
    ///
    /// Таблица корпуса несёт и рампу модели (чья, середина, ширина, верх) —
    /// артефакт правила `S204`: у каких спектров модель умножается и на что.
    ///
    /// Корпус — только чтение.
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string spectra = Path.Combine("tools", "CORPUS", "corpus", "spectra");
            string outCsv = null;
            bool fractionOverridden = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal))
                {
                    spectra = a.Substring(10);
                }
                else if (a.StartsWith("--out=", StringComparison.Ordinal))
                {
                    outCsv = a.Substring(6);
                }
                else if (a.StartsWith("--fraction=", StringComparison.Ordinal))
                {
                    double f;
                    if (!double.TryParse(a.Substring(11), NumberStyles.Float, CultureInfo.InvariantCulture, out f)
                        || f <= 0.0 || f > 1.0)
                    {
                        Console.Error.WriteLine("плохая доля: " + a);
                        return 64;
                    }

                    FsaBand.DefaultThresholdLevelFraction = f;
                    fractionOverridden = true;
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 64;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                              "правило порога: доля уровня {0:F2}, окно уровня {1:F1} кэВ (не меньше {2} каналов),"
                              + " рампа от подошвы {6:F2} уровня не шире {3:F0} кэВ, уровень держится не ниже {4:F2}"
                              + " в окне {5:F0} кэВ, хвост рампы {7:F3} ширины подъёма, искать до {8:F0} кэВ",
                              FsaBand.DefaultThresholdLevelFraction, FsaBand.ThresholdLevelWindowKev,
                              FsaBand.ThresholdMinWindowChannels, FsaBand.ThresholdRampMaxKev,
                              FsaBand.ThresholdPersistFraction, FsaBand.ThresholdPersistWindowKev,
                              FsaBand.ThresholdFootFraction, FsaBand.ThresholdTailShare, FsaBand.ThresholdScanKev));
            Console.WriteLine();
            Console.WriteLine("== положительные контроли ==");
            SelfTest();
            int rc = 0;
            if (bad > 0 && !fractionOverridden)
            {
                Console.WriteLine("⛔ КОНТРОЛИ НЕ ПРОШЛИ: {0} — таблица корпуса ниже ничего не значит", bad);
                return 1;
            }

            if (bad > 0)
            {
                // Контроли калиброваны под ПОСТАВОЧНУЮ долю; при уведённой
                // ключом доле их отказ — свойство плеча A/B, а не пробы, и
                // таблица нужна ровно затем, чтобы это плечо посчитать. Код
                // возврата всё равно не ноль: приёмкой такой прогон не служит.
                Console.WriteLine("⚠ КОНТРОЛИ НЕ ПРОШЛИ: {0} при уведённой доле — таблица печатается для A/B, приёмкой не служит", bad);
                rc = 1;
            }
            else
            {
                Console.WriteLine("контроли: ВСЕ СОШЛИСЬ");
            }
            Console.WriteLine();

            if (!Directory.Exists(spectra))
            {
                Console.Error.WriteLine("нет каталога спектров: " + spectra);
                return 2;
            }

            var rows = new List<string>();
            rows.Add("spectrum,kind,channels,first_ch,first_keV,thr_ch,thr_keV,how,pair_floor_keV,winner,"
                     + "ramp_source,ramp_mid_ch,ramp_width_ch,ramp_top_ch,ramp_how");
            int nRampModel = 0;
            int nRamp = 0, nHard = 0, nWide = 0, nPeak = 0, nSampleWins = 0, nBgWins = 0, nTie = 0, nNoBg = 0;
            int nMoot = 0;
            string[] files = Directory.GetFiles(spectra, "*.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("{0,-26} {1,-5} {2,6} {3,8} {4,8} {5,-70} {6,9} {7}",
                              "спектр", "кто", "1-й кан", "1-й кэВ", "порог", "чем взят", "пол пары", "чей");
            foreach (string path in files)
            {
                string key = Path.GetFileNameWithoutExtension(path);
                ResultData rd;
                try
                {
                    rd = Load(path);
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0,-26} ОТКАЗ чтения: {1}", key, e.Message);
                    continue;
                }

                EnergySpectrum sample = rd.EnergySpectrum;
                EnergySpectrum background = rd.BackgroundEnergySpectrum;
                if (background != null && (background.Spectrum == null || background.Spectrum.Length == 0))
                {
                    background = null;
                }

                string sampleHow;
                string backgroundHow;
                double pair = FsaBand.PairThreshold(sample.Spectrum, sample.EnergyCalibration, background, true,
                                                    out sampleHow, out backgroundHow);
                double sampleThr = FsaBand.AdcThresholdOf(sample.Spectrum, sample.EnergyCalibration, out sampleHow);
                double bgThr = background == null
                    ? 0.0
                    : FsaBand.AdcThresholdOf(background.Spectrum, background.EnergyCalibration, out backgroundHow);
                string winner = background == null
                    ? "фона нет"
                    : Math.Abs(sampleThr - bgThr) <= 1e-9 ? "равны" : sampleThr > bgThr ? "проба" : "ФОН";
                bool cuts = FsaBand.FloorCutsData(sample.Spectrum, sample.EnergyCalibration, background, true, pair);
                if (!cuts)
                {
                    winner += " (ниже пусто — не применяется)";
                    nMoot++;
                }
                if (background == null) nNoBg++;
                else if (Math.Abs(sampleThr - bgThr) <= 1e-9) nTie++;
                else if (sampleThr > bgThr) nSampleWins++;
                else nBgWins++;

                // (`S204`) рампа модели — той же парой, что зовёт разбор (фон в своей шкале)
                string rampHow;
                FsaBand.RampProfile rampModel = FsaBand.PairRampProfile(sample.Spectrum, sample.EnergyCalibration,
                                                                        background, true, out rampHow);
                if (rampModel != null)
                {
                    nRampModel++;
                }

                Console.WriteLine("{0,-26} рампа модели: {1}", key, rampModel != null ? rampHow : "нет — " + rampHow);

                Count(sampleHow, ref nRamp, ref nHard, ref nWide, ref nPeak);
                Report(key, "проба", sample, sampleThr, sampleHow, pair, winner, rows, rampModel, rampHow);
                if (background != null)
                {
                    Report(key, "фон", background, bgThr, backgroundHow, pair, winner, rows, rampModel, rampHow);
                }
            }

            Console.WriteLine();
            Console.WriteLine("пробы: рампа {0}, жёсткий рез {1}, подъём шире рампы {2}, пик на пороге {3};"
                              + " пол пары задала проба {4}, фон {5}, равны {6}, фона нет {7};"
                              + " пол не применяется (ниже пусто у обоих) у {8}",
                              nRamp, nHard, nWide, nPeak, nSampleWins, nBgWins, nTie, nNoBg, nMoot);
            Console.WriteLine("рампа модели (`S204`) у {0} из {1} спектров", nRampModel, files.Length);
            if (outCsv != null)
            {
                File.WriteAllLines(outCsv, rows.ToArray(), new UTF8Encoding(false));
                Console.WriteLine("таблица: " + outCsv);
            }

            return rc;
        }

        static void Count(string how, ref int ramp, ref int hard, ref int wide, ref int peak)
        {
            if (how.StartsWith("рампа", StringComparison.Ordinal)) ramp++;
            else if (how.StartsWith("жёсткий", StringComparison.Ordinal)) hard++;
            else if (how.Contains("шире рампы") || how.Contains("не устоялся")) wide++;
            else if (how.Contains("пик на пороге")) peak++;
        }

        static void Report(string key, string kind, EnergySpectrum s, double thr, string how,
                           double pair, string winner, List<string> rows,
                           FsaBand.RampProfile ramp, string rampHow)
        {
            double first = FsaBand.AdcFloorOf(s.Spectrum, s.EnergyCalibration);
            int firstCh = -1;
            int thrCh = -1;
            for (int ch = 0; ch < s.Spectrum.Length; ch++)
            {
                double e = s.EnergyCalibration.ChannelToEnergy(ch);
                if (firstCh < 0 && s.Spectrum[ch] > 0 && e > 0.0) firstCh = ch;
                if (thrCh < 0 && thr > 0.0 && e >= thr - 1e-9) thrCh = ch;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                              "{0,-26} {1,-5} {2,6} {3,8:F2} {4,8:F2} {5,-70} {6,9:F2} {7}",
                              key, kind, firstCh, first, thr, how, pair, winner));
            rows.Add(string.Join(",", new[]
            {
                key, kind, s.Spectrum.Length.ToString(CultureInfo.InvariantCulture),
                firstCh.ToString(CultureInfo.InvariantCulture),
                first.ToString("F3", CultureInfo.InvariantCulture),
                thrCh.ToString(CultureInfo.InvariantCulture),
                thr.ToString("F3", CultureInfo.InvariantCulture),
                "\"" + how.Replace("\"", "'") + "\"",
                pair.ToString("F3", CultureInfo.InvariantCulture),
                winner,
                ramp != null ? ramp.Source : "",
                ramp != null ? ramp.Mid.ToString("F3", CultureInfo.InvariantCulture) : "",
                ramp != null ? ramp.Width.ToString("F3", CultureInfo.InvariantCulture) : "",
                ramp != null ? ramp.Top.ToString(CultureInfo.InvariantCulture) : "",
                "\"" + (rampHow ?? "").Replace("\"", "'") + "\""
            }));
        }

        /// <summary>Шкала контролей: 0.4 кэВ на канал от −10 кэВ (как у AS80).</summary>
        static EnergyCalibration Scale()
        {
            var cal = new PolynomialEnergyCalibration();
            cal.PolynomialOrder = 1;
            cal.Coefficients = new double[] { -10.0, 0.4 };
            return cal;
        }

        static double Erf(double x)
        {
            // Abramowitz–Stegun 7.1.26, хватает на контроль
            double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
                              + 0.254829592) * t * Math.Exp(-x * x);
            return x >= 0.0 ? y : -y;
        }

        static void Check(string what, bool ok, string detail)
        {
            Console.WriteLine("  {0} {1}: {2}", ok ? "OK " : "⛔ ", what, detail);
            if (!ok) bad++;
        }

        static void SelfTest()
        {
            EnergyCalibration cal = Scale();
            const int n = 2048;

            // 1. S-кривая: p(E) = Φ((E − 12)/1.2), континуум ровный 9000
            int[] ramp = new int[n];
            for (int ch = 0; ch < n; ch++)
            {
                double e = cal.ChannelToEnergy(ch);
                double p = 0.5 * (1.0 + Erf((e - 12.0) / (1.2 * Math.Sqrt(2.0))));
                ramp[ch] = (int)Math.Round(9000.0 * p);
            }

            string how;
            double thr = FsaBand.AdcThresholdOf(ramp, cal, out how);
            double pAt = 0.5 * (1.0 + Erf((thr - 12.0) / (1.2 * Math.Sqrt(2.0))));
            double pEnd = 0.5 * (1.0 + Erf((thr + FsaBand.ThresholdLevelWindowKev - 12.0) / (1.2 * Math.Sqrt(2.0))));
            Check("S-кривая → порог у верха рампы",
                  how.StartsWith("рампа", StringComparison.Ordinal) && pAt >= 0.85 && pAt < 0.999,
                  string.Format(CultureInfo.InvariantCulture,
                                "порог {0:F2} кэВ, доля рампы в нём {1:F3}, через окно {2:F3}; {3}",
                                thr, pAt, pEnd, how));

            // 2. жёсткий рез на 27 кэВ, континуум ровный
            int[] hard = new int[n];
            for (int ch = 0; ch < n; ch++)
            {
                hard[ch] = cal.ChannelToEnergy(ch) >= 27.0 ? 5000 : 0;
            }

            thr = FsaBand.AdcThresholdOf(hard, cal, out how);
            Check("жёсткий рез → первый канал",
                  how.StartsWith("жёсткий", StringComparison.Ordinal) && Math.Abs(thr - 27.2) < 0.41,
                  string.Format(CultureInfo.InvariantCulture, "порог {0:F2} кэВ; {1}", thr, how));

            // 3. пик на пороге: рез на 8 кэВ прямо на нижнем склоне гауссова
            //    пика 14 кэВ (σ 2.3 кэВ — K-рентген серебра на NaI), континуума
            //    под ним нет. Уровень на склоне у вершины «держится» в окне
            //    уровня (за три кэВ пик падает на треть) — ловит только окно
            //    устойчивости.
            int[] peak = new int[n];
            for (int ch = 0; ch < n; ch++)
            {
                double e = cal.ChannelToEnergy(ch);
                double g = 40000.0 * Math.Exp(-0.5 * Math.Pow((e - 14.0) / 2.3, 2.0));
                peak[ch] = e >= 8.0 ? (int)Math.Round(g) : 0;
            }

            thr = FsaBand.AdcThresholdOf(peak, cal, out how);
            Check("пик на пороге → отступить к первому каналу",
                  how.Contains("пик на пороге") && Math.Abs(thr - 8.0) < 0.41,
                  string.Format(CultureInfo.InvariantCulture, "порог {0:F2} кэВ; {1}", thr, how));

            // 4. подъём континуума поглощением окна: n(E) = 6000·exp(−(25/E)³), от 5 кэВ
            int[] wide = new int[n];
            for (int ch = 0; ch < n; ch++)
            {
                double e = cal.ChannelToEnergy(ch);
                wide[ch] = e >= 5.0 ? (int)Math.Round(6000.0 * Math.Exp(-Math.Pow(25.0 / e, 3.0))) : 0;
            }

            // первый ненулевой канал у такого подъёма — там, где округление даёт единицу
            double first = FsaBand.AdcFloorOf(wide, cal);
            thr = FsaBand.AdcThresholdOf(wide, cal, out how);
            Check("подъём на десятки кэВ → первый канал",
                  how.Contains("шире рампы") && Math.Abs(thr - first) < 1e-9,
                  string.Format(CultureInfo.InvariantCulture, "порог {0:F2} кэВ, первый ненулевой {1:F2}; {2}",
                                thr, first, how));

            // 5. рампа, над которой сразу РАСТУЩАЯ структура: та же S-кривая на
            //    континууме 1200 плюс гауссов пик 22 кэВ (σ 3 кэВ) — L-рентген
            //    Am-241 на AS80, где уровень за рампой растёт примерно на
            //    четверть за окно уровня (здесь — на две пятых, жёстче
            //    корпуса). Порог обязан лечь у верха рампы, а не уйти на
            //    первый канал: уровень после рампы держится (растёт).
            int[] rampPeak = new int[n];
            for (int ch = 0; ch < n; ch++)
            {
                double e = cal.ChannelToEnergy(ch);
                double p = 0.5 * (1.0 + Erf((e - 12.0) / (1.2 * Math.Sqrt(2.0))));
                double g = 3000.0 * Math.Exp(-0.5 * Math.Pow((e - 22.0) / 3.0, 2.0));
                rampPeak[ch] = (int)Math.Round((1200.0 + g) * p);
            }

            thr = FsaBand.AdcThresholdOf(rampPeak, cal, out how);
            pAt = 0.5 * (1.0 + Erf((thr - 12.0) / (1.2 * Math.Sqrt(2.0))));
            Check("рампа со структурой над ней → порог у верха рампы",
                  how.StartsWith("рампа", StringComparison.Ordinal) && pAt >= 0.85 && thr < 17.0,
                  string.Format(CultureInfo.InvariantCulture,
                                "порог {0:F2} кэВ, доля рампы в нём {1:F3}; {2}", thr, pAt, how));

            // 6. пара: порог фона выше порога пробы → пол = фон
            int[] sample = hard;
            int[] bg = new int[n];
            for (int ch = 0; ch < n; ch++)
            {
                bg[ch] = cal.ChannelToEnergy(ch) >= 44.0 ? 100 : 0;
            }

            var bgSpec = new EnergySpectrum();
            bgSpec.Spectrum = bg;
            bgSpec.EnergyCalibration = cal;
            bgSpec.NumberOfChannels = n;
            string sHow, bHow;
            double pair = FsaBand.PairThreshold(sample, cal, bgSpec, true, out sHow, out bHow);
            Check("пара: рез фона выше реза пробы → пол по фону",
                  Math.Abs(pair - 44.0) < 0.41 && bHow != null,
                  string.Format(CultureInfo.InvariantCulture, "пол {0:F2} кэВ; проба: {1}; фон: {2}", pair, sHow, bHow));

            RampSelfTest();
        }

        /// <summary>Шкала G1S: около трёх кэВ на канал, нуль у −12 кэВ.</summary>
        static EnergyCalibration CoarseScale()
        {
            var cal = new PolynomialEnergyCalibration();
            cal.PolynomialOrder = 1;
            cal.Coefficients = new double[] { -12.0, 2.85 };
            return cal;
        }

        /// <summary>Логистика рампы в каналах на континууме, растущем вниз по шкале (как у фона G1S).</summary>
        static int[] Logistic(int n, double mid, double width, double level, int firstCh)
        {
            int[] a = new int[n];
            for (int ch = firstCh; ch < n; ch++)
            {
                double s = 1.0 / (1.0 + Math.Exp(-(ch - mid) / width));
                double cont = level * (1.0 + 0.4 * Math.Exp(-(ch - 12.0) / 60.0));
                a[ch] = (int)Math.Round(cont * s);
            }

            return a;
        }

        static EnergySpectrum Spec(int[] counts, EnergyCalibration cal)
        {
            var e = new EnergySpectrum();
            e.Spectrum = counts;
            e.EnergyCalibration = cal;
            e.NumberOfChannels = counts.Length;
            return e;
        }

        /// <summary>(`S204`, П189) Контроли модели рампы — пункты 7, 8, 9 шапки пробы.</summary>
        static void RampSelfTest()
        {
            EnergyCalibration g = CoarseScale();
            const int n = 1024;
            FsaRampModel stock = FsaBand.DefaultRampModel;
            FsaBand.DefaultRampModel = FsaRampModel.Model;
            try
            {
                // 7. рампа G1S: середина 9.3, ширина 0.95 канала, уровень 5000, первый отсчёт в канале 4
                int[] wide = Logistic(n, 9.3, 0.95, 5000.0, 4);
                string how;
                FsaBand.RampProfile r = FsaBand.RampProfileOf(wide, g, out how);
                string thrHow;
                FsaBand.AdcThresholdOf(wide, g, out thrHow);
                Check("рампа шире границы → логистика восстановлена",
                      r != null && Math.Abs(r.Mid - 9.3) < 0.25 && Math.Abs(r.Width - 0.95) < 0.2
                      && r.Top > 14 && r.Top < 20 && r.At(r.Top) == 1.0
                      && thrHow.Contains("шире рампы"),
                      r == null ? "рампы нет: " + how
                                : string.Format(CultureInfo.InvariantCulture,
                                                "середина {0:F2} (задано 9.30), ширина {1:F2} (0.95), верх {2}, точек {3}; правило пола: {4}",
                                                r.Mid, r.Width, r.Top, r.Points, thrHow));

                // 7б. множитель: ниже верха — логистика, выше — кривая не тронута ни в одном бите
                if (r != null)
                {
                    double[] curve = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        curve[i] = 1.0 + 0.001 * i;
                    }

                    double[] copy = (double[])curve.Clone();
                    r.Apply(curve);
                    bool above = true;
                    for (int i = r.Top; i < n; i++)
                    {
                        above &= curve[i].Equals(copy[i]);
                    }

                    double atMid = curve[9] / copy[9];
                    Check("множитель: выше верха побитово, ниже — логистика",
                          above && Math.Abs(atMid - r.At(9)) < 1e-12 && r.At(9) < 0.5 && curve[3] < 0.01 * copy[3],
                          string.Format(CultureInfo.InvariantCulture, "S(9) {0:F3}, S(3) {1:E2}, верх {2}",
                                        atMid, curve[3] / copy[3], r.Top));
                }

                // 8. рампы для модели нет: жёсткий рез, рампа AS80 (её режет пол), пик на пороге, поглощение
                int[] hard = new int[n];
                for (int ch = 10; ch < n; ch++)
                {
                    hard[ch] = 5000;
                }

                EnergyCalibration fine = Scale();
                int[] narrow = new int[2048];
                for (int ch = 0; ch < narrow.Length; ch++)
                {
                    double e = fine.ChannelToEnergy(ch);
                    narrow[ch] = (int)Math.Round(9000.0 * 0.5 * (1.0 + Erf((e - 12.0) / (1.2 * Math.Sqrt(2.0)))));
                }

                int[] peak = new int[n];
                for (int ch = 0; ch < n; ch++)
                {
                    double e = g.ChannelToEnergy(ch);
                    double s = 1.0 / (1.0 + Math.Exp(-(ch - 9.3) / 0.95));
                    peak[ch] = (int)Math.Round(s * (200.0 + 40000.0 * Math.Exp(-0.5 * Math.Pow((e - 22.0) / 3.8, 2.0))));
                }

                int[] absorb = new int[n];
                for (int ch = 0; ch < n; ch++)
                {
                    double e = g.ChannelToEnergy(ch);
                    absorb[ch] = e >= 5.0 ? (int)Math.Round(6000.0 * Math.Exp(-Math.Pow(40.0 / e, 3.0))) : 0;
                }

                string h1, h2, h3, h4;
                FsaBand.RampProfile r1 = FsaBand.RampProfileOf(hard, g, out h1);
                FsaBand.RampProfile r2 = FsaBand.RampProfileOf(narrow, fine, out h2);
                FsaBand.RampProfile r3 = FsaBand.RampProfileOf(peak, g, out h3);
                FsaBand.RampProfile r4 = FsaBand.RampProfileOf(absorb, g, out h4);
                Check("без рампы для модели: жёсткий рез / рампа в границе / пик на пороге / поглощение → нет",
                      r1 == null && r2 == null && r3 == null && r4 == null,
                      string.Format(CultureInfo.InvariantCulture, "рез: {0}; AS80: {1}; пик: {2}; поглощение: {3}",
                                    r1 == null ? h1 : "ЕСТЬ", r2 == null ? h2 : "ЕСТЬ",
                                    r3 == null ? h3 : "ЕСТЬ", r4 == null ? h4 : "ЕСТЬ"));

                // 9. пары
                EnergySpectrum bg = Spec(wide, g);
                int[] hardLow = new int[n];
                for (int ch = 4; ch < n; ch++)
                {
                    hardLow[ch] = 700;   // проба режется жёстко НИЖЕ середины рампы фона
                }

                string p1, p2, p3, p4;
                FsaBand.RampProfile q1 = FsaBand.PairRampProfile(hardLow, g, bg, true, out p1);
                FsaBand.RampProfile q2 = FsaBand.PairRampProfile(peak, g, bg, true, out p2);
                FsaBand.RampProfile q3 = FsaBand.PairRampProfile(wide, g, null, true, out p3);
                FsaBand.DefaultRampModel = FsaRampModel.Off;
                FsaBand.RampProfile q4 = FsaBand.PairRampProfile(peak, g, bg, true, out p4);
                FsaBand.DefaultRampModel = FsaRampModel.Model;
                Check("пара: рез пробы ниже рампы фона → нет; пик на пороге → по фону; фона нет → по пробе; выключено → нет",
                      q1 == null && q2 != null && q2.Source == "фону" && Math.Abs(q2.Mid - 9.3) < 0.25
                      && q3 != null && q3.Source.StartsWith("пробе", StringComparison.Ordinal) && q4 == null,
                      string.Format(CultureInfo.InvariantCulture, "рез: {0}; пик: {1}; без фона: {2}; выкл: {3}",
                                    q1 == null ? p1 : "ЕСТЬ", q2 != null ? p2 : "НЕТ", q3 != null ? p3 : "НЕТ",
                                    q4 == null ? "нет" : "ЕСТЬ"));
            }
            finally
            {
                FsaBand.DefaultRampModel = stock;
            }
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            if (file == null || file.ResultDataList == null || file.ResultDataList.Count == 0)
            {
                throw new InvalidDataException("в файле нет ResultData");
            }

            return file.ResultDataList[0];
        }
    }
}
