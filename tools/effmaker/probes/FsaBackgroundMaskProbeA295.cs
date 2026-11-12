using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER295`) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ АВТОМАСКИ ФОНА (`FsaAnalyzer.BackgroundExcessMask`) И НОЖА ПРИ КРАСНОЙ
    /// СТРОКЕ (`FsaAnalyzer.BackgroundBandKnife`); оба включены умолчанием, ключи выключения — для сравнения.
    ///
    ///   (1) шум: фон равен спектру в ожидании, 200 зёрен по 1024 канала — ни одного снятого канала;
    ///       отбор по знаку канала снял бы половину, контрольная сумма печатается;
    ///   (2) окно сверх шума на тех же зёрнах снимается и не выходит за свои края дальше ширины окна;
    ///   (3) нормальный спектр корпуса: снятых каналов столько, сколько признак нашёл без подмены;
    ///   (4) тот же спектр, фон завышен узкой полосой выше порога доверия: участок снят, вне полосы плюс ширина
    ///       окна — ничего, строка окна называет его, плечо без ключа не снимает ни канала, нож не включён;
    ///   (5) фон завышен в коридоре ножа, с матрицей (`--store=`): и маска, и нож, строка окна говорит, откуда
    ///       активность; без матрицы отвязывать нечего, и строки нет;
    ///   (6) цепочка привязки при маске на объявленном составе, как в корпусе (`--medal=`, `--store=`,
    ///       `--medal-chain=`, `--medal-shield=`): шкала и χ² не хуже, опор ширины не меньше, нож вне коридора не
    ///       включён: холодная привязка здесь теряла вторую опору ширины;
    ///   (7) лабораторный фон на сильнейшей линии (`--lab-line=`): сторож отдаёт разбор без маски, побитно как без ключей, горит
    ///       красная строка, строки маски нет;
    ///   (8) `MaskRefusal` на собранных результатах: опор меньше; участок на сильнейшей линии; на слабой — нет;
    ///       мешающий образ не в счёт;
    ///   (9) (остаток `AMBER295`, полоса `a295`) окно опоры, задевшее участок маски (`--lab-line=`): фон завышен
    ///       ниже линии так, что участок входит в окно её опоры, а саму линию не накрывает. Без рычага
    ///       `FsaAnalyzer.AnchorWindowSkipsMasked` опора при маске отпадает и сторож отказывает маске «опор
    ///       меньше»; с рычагом опора остаётся и маска принята;
    ///   (10) граница правила «окно, снятое маской больше чем наполовину, — не опора» (тот же рычаг, сторож
    ///       выключен, чтобы видеть проход с маской): узкая сильная подмена ниже окна поднимается по каналу; пока снято не больше
    ///       половины окна, опора на линии берётся, первый же шаг за половину — опоры нет.
    ///
    /// Коды: 0 — сошлось; 1 — нет; 2 — ключи или файл.
    /// </summary>
    static class FsaBackgroundMaskProbeA295
    {
        static int bad;

        /// <summary>(9) Рычаг окна опоры и сторож маски — в хук анализатора (<see cref="Run"/>).</summary>
        static bool leverSkipMasked;

        static bool guardOn = true;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null, medal = null, store = null, medalChain = null;
            double labLine = double.NaN;
            var medalShield = new List<int>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--medal=", StringComparison.Ordinal)) medal = a.Substring(8);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a.StartsWith("--lab-line=", StringComparison.Ordinal)) labLine = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--medal-chain=", StringComparison.Ordinal)) medalChain = a.Substring(14);
                else if (a.StartsWith("--medal-shield=", StringComparison.Ordinal)) medalShield.Add(int.Parse(a.Substring(15), CultureInfo.InvariantCulture));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            Noise();
            if (path != null)
            {
                if (!File.Exists(path)) { Console.Error.WriteLine("нет файла: " + path); return 2; }
                Spectrum(path, store, labLine);
            }

            if (medal != null)
            {
                if (!File.Exists(medal) || store == null || medalChain == null) { Console.Error.WriteLine("нужны --medal=<файл>, --store=<склад>, --medal-chain=<ряд>"); return 2; }
                Medal(medal, store, medalChain, medalShield);
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Сумма по окну на шуме порог не проходит, отбор по знаку снял бы половину.</summary>
        static void Noise()
        {
            const int n = 1024, width = 12;
            long noiseMasked = 0, signPicked = 0;
            int injectedHit = 0, injectedOutside = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var random = new Random(seed);
                double[] e = new double[n], v = new double[n];
                int[] w = new int[n];
                for (int i = 0; i < n; i++)
                {
                    double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                    double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    v[i] = 100.0;
                    e[i] = 10.0 * g;
                    w[i] = width;
                    if (e[i] > 0.0) signPicked++;
                }

                foreach (bool m in FsaAnalyzer.ExcessWindows(e, v, w, 5.0))
                {
                    if (m) noiseMasked++;
                }

                // окно сверх шума: 3σ на канал на 40 каналах
                for (int i = 400; i < 440; i++)
                {
                    e[i] += 30.0;
                }

                bool[] hit = FsaAnalyzer.ExcessWindows(e, v, w, 5.0);
                for (int i = 0; i < n; i++)
                {
                    if (!hit[i]) continue;
                    if (i >= 400 && i < 440) injectedHit++;
                    else if (i < 400 - width || i >= 440 + width) injectedOutside++;
                }
            }

            Console.WriteLine("(1) шум, 200 зёрен: окно снимает {0} каналов, отбор по знаку снял бы {1}", noiseMasked, signPicked);
            Same("(1) шум не снят", 0L, noiseMasked);
            Same("(1) отбор по знаку снял бы почти половину", true, signPicked > 90000);
            Same("(2) окно сверх шума снято целиком на всех зёрнах", 200 * 40, injectedHit);
            Same("(2) вне окна плюс ширина не снято ничего", 0, injectedOutside);
        }

        static void Spectrum(string path, string store, double labLine)
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            ResultData plain = Load(path, nuclides);
            Same("у спектра есть фон", true, plain.BackgroundEnergySpectrum != null);
            FsaResult off = Run(plain, false);
            FsaResult on = Run(plain, true);
            Same("(3) разбор состоялся", true, off != null && on != null);
            if (off == null || on == null)
            {
                return;
            }

            Same("(3) ключ ВЫКЛ: участков нет", (object)null, off.BackgroundMaskBands);
            Console.WriteLine("(3) нормальный спектр: снято каналов {0}, χ²П {1:R} и {2:R}", on.BackgroundMaskChannels, off.Chi2NdfPoisson, on.Chi2NdfPoisson);
            Same("(3) снято 0 каналов", 0, on.BackgroundMaskChannels);
            Same("(3) не сработав, ключ не меняет ни бита", off.Chi2NdfPoisson.ToString("R", CultureInfo.InvariantCulture), on.Chi2NdfPoisson.ToString("R", CultureInfo.InvariantCulture));

            ResultData doped = Load(path, nuclides);
            const double lo = 200.0, hi = 260.0;
            Dope(doped, lo, hi);

            FsaResult maskedOff = Run(doped, false);
            FsaResult masked = Run(doped, true);
            Same("(4) разбор состоялся", true, masked != null && maskedOff != null);
            if (masked == null || maskedOff == null)
            {
                return;
            }

            Same("(4) плечо без ключа не снимает", 0, maskedOff.BackgroundMaskChannels);
            Same("(4) участок снят", true, masked.BackgroundMaskChannels > 0);
            Same("(4) красная строка признака горит", true, masked.BackgroundOversubtracted != null);
            bool inside = true, overlap = false;
            foreach (FsaBandWarning band in masked.BackgroundMaskBands ?? new List<FsaBandWarning>())
            {
                Console.WriteLine("(4) участок {0:F1}–{1:F1} кэВ, z {2:F1}, отношение {3:F2}, каналов {4}", band.LoKev, band.HiKev, band.Z, band.Ratio, band.Bands);
                inside &= band.LoKev >= lo - 40.0 && band.HiKev <= hi + 40.0;
                overlap |= band.HiKev >= lo && band.LoKev <= hi;
            }

            Same("(4) участок на месте подмены", true, overlap);
            Same("(4) вне подмены ничего", true, inside);
            string row = FSAReportView.BackgroundMaskText(masked);
            Console.WriteLine("(4) строка окна: " + (row ?? "—"));
            Same("(4) строка называет участок", true, row != null && row.Contains("–"));
            Console.WriteLine("(4) χ²П без ключа {0:F3}, с маской {1:F3}", maskedOff.Chi2NdfPoisson, masked.Chi2NdfPoisson);
            Same("(4) χ²П с маской не хуже", true, masked.Chi2NdfPoisson <= maskedOff.Chi2NdfPoisson);
            Same("(4) окно вне коридора: нож не включён", false, masked.BackgroundBandKnifeApplied);
            Same("(4) строки ножа нет", (string)null, FSAReportView.BackgroundKnifeText(masked));

            ResultData lab = Load(path, nuclides);
            double strongestKev = labLine;
            Dope(lab, strongestKev * 0.92, strongestKev * 1.08);
            FsaResult guardedOff = Run(lab, false);
            FsaResult guarded = Run(lab, true);
            Same("(7) разбор состоялся", true, guardedOff != null && guarded != null);
            if (guardedOff != null && guarded != null)
            {
                Console.WriteLine("(7) фон завышен у {0:F1} кэВ; отказ сторожа: {1}", strongestKev, guarded.BackgroundMaskRefused ?? "—");
                Same("(7) сторож отклонил маску", true, guarded.BackgroundMaskRefused != null);
                Same("(7) снято каналов", 0, guarded.BackgroundMaskChannels);
                Same("(7) строки маски нет", (string)null, FSAReportView.BackgroundMaskText(guarded));
                Same("(7) красная строка горит", true, FSAReportView.BackgroundOversubtractedText(guarded) != null);
                Same("(7) χ²П побитно как без ключей", guardedOff.Chi2NdfPoisson, guarded.Chi2NdfPoisson);
                Same("(7) усиление побитно как без ключей", guardedOff.Gain, guarded.Gain);
            }

            Refusals();
            AnchorWindow(path, nuclides, labLine);
            HalfWindow(path, nuclides, labLine);

            ResultData corridor = Load(path, nuclides);
            Dope(corridor, 45.0, 85.0);
            FsaResult bare = Run(corridor, true);
            Same("(5) без матрицы разбор состоялся", true, bare != null);
            if (bare != null)
            {
                Same("(5) без матрицы отвязывать нечего: нож не значится", false, bare.BackgroundBandKnifeApplied);
                Same("(5) без матрицы строки ножа нет", (string)null, FSAReportView.BackgroundKnifeText(bare));
            }

            if (store == null)
            {
                Console.WriteLine("(5) нож с матрицей: нет --store=, не мерено");
                bad++;
                return;
            }

            EfficiencyMaker.ResponseMatrix own = EfficiencyMaker.ResponseMatrix.Load(Path.Combine(store, corridor.Efficiency.Guid + ".rmx"));
            Same("(5) матрица склада есть", true, own != null);
            FsaResult knife = own != null ? Run(corridor, true, own) : null;
            Same("(5) разбор состоялся", true, knife != null);
            if (knife != null)
            {
                FsaBandWarning red = knife.BackgroundOversubtracted;
                Console.WriteLine("(5) окно строки {0}, участков {1}, нож {2}, отвязано {3} из {4}", red != null ? red.LoKev.ToString("F1", CultureInfo.InvariantCulture) + "–" + red.HiKev.ToString("F1", CultureInfo.InvariantCulture) : "—",
                                  knife.BackgroundMaskBands != null ? knife.BackgroundMaskBands.Count : 0, knife.BackgroundBandKnifeApplied, knife.KnifeUntiedBins, knife.KnifeBinsBelow);
                Same("(5) участок снят", true, knife.BackgroundMaskChannels > 0);
                Same("(5) окно в коридоре: нож включён", true, knife.BackgroundBandKnifeApplied);
                Same("(5) нож отвязал бины", true, knife.KnifeUntiedBins > 0);
                string line = FSAReportView.BackgroundKnifeText(knife);
                Console.WriteLine("(5) строка окна: " + (line ?? "—"));
                Same("(5) строка ножа есть", true, line != null);
            }
        }

        /// <summary>Правило сторожа на собранных результатах.</summary>
        static void Refusals()
        {
            var component = new FsaComponent("образ", FsaComponentKind.Single);
            component.Lines.Add(new FsaLine("образ", 500.0, 0.9));
            component.Lines.Add(new FsaLine("образ", 200.0, 0.1));
            var nuisance = new FsaComponent("мешающий", FsaComponentKind.Nuisance);
            nuisance.Lines.Add(new FsaLine("мешающий", 75.0, 1.0));
            var library = new List<FsaComponent> { component, nuisance };
            var current = new FsaResult { ScaleAnchorsUsed = 2 };
            Func<double, double, int, string> refusal = (lo, hi, anchors) =>
                FsaAnalyzer.MaskRefusal(current, new FsaResult
                {
                    ScaleAnchorsUsed = anchors,
                    BackgroundMaskBands = new List<FsaBandWarning> { new FsaBandWarning { LoKev = lo, HiKev = hi } }
                }, library);
            Same("(8) ничего не задето", true, refusal(600.0, 700.0, 2) == null);
            Same("(8) опор меньше", true, refusal(600.0, 700.0, 1) != null);
            Same("(8) участок на сильнейшей линии", true, refusal(450.0, 550.0, 2) != null);
            Same("(8) участок на слабой линии", true, refusal(150.0, 250.0, 2) == null);
            Same("(8) участок на мешающем образе", true, refusal(60.0, 90.0, 2) == null);
        }

        /// <summary>Взятая опора шкалы на линии <paramref name="lineKev"/>; null — нет.</summary>
        static FsaScaleAnchor AnchorAt(FsaResult result, double lineKev)
        {
            return result == null ? null : AnchorIn(result.ScaleAnchors, lineKev);
        }

        /// <summary>Взятая опора на линии <paramref name="lineKev"/> в списке опор; null — нет.</summary>
        static FsaScaleAnchor AnchorIn(List<FsaScaleAnchor> anchors, double lineKev)
        {
            if (anchors == null)
            {
                return null;
            }

            foreach (FsaScaleAnchor anchor in anchors)
            {
                if (anchor.Used && Math.Abs(anchor.LineKev - lineKev) < 0.5)
                {
                    return anchor;
                }
            }

            return null;
        }

        /// <summary>
        /// (9) Окно опоры, задевшее участок маски. Полоса подмены подбирается замером: её верхний край поднимается
        /// от трети окна ниже нижнего края окна опоры, пока участок маски (сторож выключен — чтобы увидеть участки) не войдёт в окно
        /// опоры, не дойдя до самой линии.
        /// </summary>
        static void AnchorWindow(string path, NuclideDefinitionManager nuclides, double lineKev)
        {
            if (double.IsNaN(lineKev))
            {
                Console.WriteLine("(9) окно опоры у маски: нет --lab-line=, не мерено");
                bad++;
                return;
            }

            ResultData plain = Load(path, nuclides);
            FsaResult bare = Run(plain, false);
            FsaScaleAnchor anchor = AnchorAt(bare, lineKev);
            Same("(9) без подмены на линии стоит взятая опора", true, anchor != null);
            if (anchor == null)
            {
                return;
            }

            EnergyCalibration calibration = plain.EnergySpectrum.EnergyCalibration;
            double winLo = calibration.ChannelToEnergy(anchor.FirstChannel), winHi = calibration.ChannelToEnergy(anchor.LastChannel);
            double window = winHi - winLo;
            ResultData doped = null;
            FsaBandWarning touching = null;
            double edge = double.NaN;
            try
            {
                guardOn = false;
                for (int step = 0; step <= 16 && touching == null; step++)
                {
                    edge = winLo - 0.3 * window + 0.05 * window * step;
                    ResultData candidate = Load(path, nuclides);
                    Dope(candidate, edge - 1.5 * window, edge);
                    FsaResult seen = Run(candidate, true);
                    if (seen == null || seen.BackgroundMaskBands == null)
                    {
                        continue;
                    }

                    foreach (FsaBandWarning band in seen.BackgroundMaskBands)
                    {
                        // участок вошёл в окно опоры снизу и до линии не дошёл на пятую долю окна
                        if (band.HiKev > winLo + 0.1 * window && band.HiKev < lineKev - 0.2 * window && band.LoKev < winLo)
                        {
                            touching = band;
                            doped = candidate;
                        }
                    }
                }
            }
            finally
            {
                guardOn = true;
            }

            Same("(9) подмена, задевающая окно опоры, найдена", true, touching != null);
            if (touching == null)
            {
                return;
            }

            Console.WriteLine("(9) линия {0:F1} кэВ, окно опоры {1:F1}–{2:F1} кэВ; подмена до {3:F1} кэВ; участок маски {4:F1}–{5:F1} кэВ",
                              lineKev, winLo, winHi, edge, touching.LoKev, touching.HiKev);
            FsaResult without = Run(doped, false);
            FsaResult off = Run(doped, true);
            FsaResult on;
            try
            {
                leverSkipMasked = true;
                on = Run(doped, true);
            }
            finally
            {
                leverSkipMasked = false;
            }

            Same("(9) разбор состоялся", true, without != null && off != null && on != null);
            if (without == null || off == null || on == null)
            {
                return;
            }

            Console.WriteLine("(9) без маски опор {0}; рычаг выкл: отказ «{1}», снято {2}; рычаг вкл: отказ «{3}», снято {4}, опор {5}, усиление {6:F6} против {7:F6}",
                              without.ScaleAnchorsUsed, off.BackgroundMaskRefused ?? "—", off.BackgroundMaskChannels,
                              on.BackgroundMaskRefused ?? "—", on.BackgroundMaskChannels, on.ScaleAnchorsUsed, on.Gain, without.Gain);
            Same("(9) без маски опора на линии взята", true, AnchorAt(without, lineKev) != null);
            Same("(9) рычаг выкл: сторож отказал маске", true, off.BackgroundMaskRefused != null);
            Same("(9) рычаг выкл: разбор побитно как без маски", without.Chi2NdfPoisson, off.Chi2NdfPoisson);
            Same("(9) рычаг вкл: маска принята", (string)null, on.BackgroundMaskRefused);
            Same("(9) рычаг вкл: участок снят", true, on.BackgroundMaskChannels > 0);
            // откат привязки (χ² без неё лучше) отдаёт шкалу файла: опора тогда значится в списке отменённой подстройки
            Console.WriteLine("(9) рычаг вкл: откат привязки {0}", on.AnchorRolledBack);
            Same("(9) рычаг вкл: опора на линии взята в проходе с маской", true,
                 on.AnchorRolledBack ? AnchorIn(on.AnchorRollbackScaleAnchors, lineKev) != null : AnchorAt(on, lineKev) != null);
        }

        /// <summary>Опора на линии взята в проходе с привязкой: у отката она значится в списке отменённой подстройки.</summary>
        static bool TakenAt(FsaResult result, double lineKev)
        {
            return result != null && (result.AnchorRolledBack
                ? AnchorIn(result.AnchorRollbackScaleAnchors, lineKev) != null
                : AnchorAt(result, lineKev) != null);
        }

        /// <summary>
        /// (10) Граница правила половины окна. Судится запись опоры на линии в разборе с маской (сторож выключен):
        /// её окно и значимость. Правило обнуляет значимость, когда каналов окна под участками маски больше
        /// половины; иначе значимость окна этой линии — десятки. Взятой опора при этом может значиться по прежнему
        /// проходу привязки, поэтому признак — значимость, а не «взята».
        /// </summary>
        static void HalfWindow(string path, NuclideDefinitionManager nuclides, double lineKev)
        {
            if (double.IsNaN(lineKev))
            {
                Console.WriteLine("(10) граница половины окна: нет --lab-line=, не мерено");
                bad++;
                return;
            }

            ResultData plain = Load(path, nuclides);
            FsaScaleAnchor anchor = AnchorAt(Run(plain, false), lineKev);
            Same("(10) без подмены на линии стоит взятая опора", true, anchor != null);
            if (anchor == null)
            {
                return;
            }

            EnergyCalibration calibration = plain.EnergySpectrum.EnergyCalibration;
            int first = anchor.FirstChannel, size = anchor.LastChannel - anchor.FirstChannel + 1;
            int wrong = 0, justBelow = 0, justAbove = 0, beyond = 0;
            try
            {
                guardOn = false;
                leverSkipMasked = true;
                // узкая сильная подмена ниже окна: участок маски тянется от неё вверх на ширину окна признака, и его
                // верхний край идёт по каналу вслед за подменой
                for (int channel = first - size / 2 - 6; channel <= first && beyond < 3; channel++)
                {
                    double edge = calibration.ChannelToEnergy(channel) + 0.5 * (calibration.ChannelToEnergy(channel + 1) - calibration.ChannelToEnergy(channel));
                    ResultData candidate = Load(path, nuclides);
                    Dope(candidate, calibration.ChannelToEnergy(channel - 8), edge, 30.0);
                    FsaResult seen = Run(candidate, true);
                    if (seen == null || seen.BackgroundMaskBands == null)
                    {
                        continue;
                    }

                    FsaScaleAnchor record = null;
                    foreach (FsaScaleAnchor a in (seen.AnchorRolledBack ? seen.AnchorRollbackScaleAnchors : seen.ScaleAnchors) ?? new List<FsaScaleAnchor>())
                    {
                        if (Math.Abs(a.LineKev - lineKev) < 0.5)
                        {
                            record = a;
                        }
                    }

                    if (record == null)
                    {
                        continue;
                    }

                    int n = record.LastChannel - record.FirstChannel + 1, masked = 0;
                    for (int k = record.FirstChannel; k <= record.LastChannel; k++)
                    {
                        double kev = calibration.ChannelToEnergy(k);
                        foreach (FsaBandWarning band in seen.BackgroundMaskBands)
                        {
                            if (kev >= band.LoKev && kev <= band.HiKev)
                            {
                                masked++;
                                break;
                            }
                        }
                    }

                    bool over = 2 * masked > n, dead = !(record.Z > 0.0);
                    Console.WriteLine("(10) край подмены {0:F1} кэВ: окно {1}…{2}, снято {3} из {4}, значимость {5:F1}{6}",
                                      edge, record.FirstChannel, record.LastChannel, masked, n, record.Z, over == dead ? "" : " — НЕ ПО ПРАВИЛУ");
                    if (over != dead)
                    {
                        wrong++;
                    }

                    if (!over && 2 * masked >= n - 5)
                    {
                        justBelow++;
                    }

                    if (over && 2 * masked <= n + 5)
                    {
                        justAbove++;
                    }

                    if (over)
                    {
                        beyond++;
                    }
                }
            }
            finally
            {
                guardOn = true;
                leverSkipMasked = false;
            }

            Console.WriteLine("(10) шагов у границы: снято не больше половины {0}, больше половины {1}; не по правилу {2}", justBelow, justAbove, wrong);
            Same("(10) у границы есть шаг «не больше половины»", true, justBelow > 0);
            Same("(10) у границы есть шаг «больше половины»", true, justAbove > 0);
            Same("(10) значимость нулевая тогда и только тогда, когда снято больше половины окна", 0, wrong);
        }

        /// <summary>Фон выше спектра на треть (или в <paramref name="factor"/> раз) в шкале пробы в полосе <paramref name="lo"/>…<paramref name="hi"/> кэВ.</summary>
        static void Dope(ResultData rd, double lo, double hi, double factor = 1.33)
        {
            EnergySpectrum background = rd.BackgroundEnergySpectrum;
            EnergyCalibration calibration = background.EnergyCalibration;
            double scale = rd.EnergySpectrum.LiveTime / background.LiveTime;
            int[] spectrum = rd.EnergySpectrum.Spectrum;
            for (int i = 0; i < background.Spectrum.Length; i++)
            {
                double kev = calibration.ChannelToEnergy(i);
                if (kev >= lo && kev <= hi)
                {
                    background.Spectrum[i] = (int)Math.Ceiling(factor * spectrum[i] / scale);
                }
            }
        }

        /// <summary>Цепочка привязки при маске: шкала и χ² не хуже плеча без ключей, нож вне коридора не включён.</summary>
        static void Medal(string path, string store, string chainLabel, List<int> shield)
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            ResultData rd = Load(path, nuclides);
            string file = Path.Combine(store, rd.Efficiency.Guid + ".rmx");
            EfficiencyMaker.ResponseMatrix matrix = EfficiencyMaker.ResponseMatrix.Load(file);
            Same("(6) матрица склада есть", true, matrix != null);
            if (matrix == null)
            {
                return;
            }

            FsaSampleChain chain = FsaSampleChain.FromLabel(chainLabel);
            Same("(6) ряд известен", true, chain != null);
            if (chain == null)
            {
                return;
            }

            FsaSampleSpec spec = FsaSampleSpec.Declared(rd, new List<FsaSampleChain> { chain }, new List<string>(), true, true);
            spec.ShieldElements.AddRange(shield);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            FsaResult off = Declared(rd, library, matrix, false);
            FsaResult on = Declared(rd, library, matrix, true);
            Same("(6) разбор состоялся", true, off != null && on != null);
            if (off == null || on == null)
            {
                return;
            }

            FsaBandWarning red = on.BackgroundOversubtracted;
            Console.WriteLine("(6) окно строки {0}; снято {1} кан.; χ²П {2:F3} -> {3:F3}; усиление {4:F6} -> {5:F6}; ширина ×{6:F4} по {7} -> ×{8:F4} по {9}",
                              red != null ? red.LoKev.ToString("F1", CultureInfo.InvariantCulture) + "–" + red.HiKev.ToString("F1", CultureInfo.InvariantCulture) : "—",
                              on.BackgroundMaskChannels, off.Chi2NdfPoisson, on.Chi2NdfPoisson, off.Gain, on.Gain, off.WidthScale, off.WidthAnchorsUsed, on.WidthScale, on.WidthAnchorsUsed);
            Same("(6) участок снят", true, on.BackgroundMaskChannels > 0);
            Same("(6) χ²П при маске не хуже", true, on.Chi2NdfPoisson <= off.Chi2NdfPoisson);
            Same("(6) опор шкалы не меньше", true, on.ScaleAnchorsUsed >= off.ScaleAnchorsUsed);
            Same("(6) опор ширины не меньше", true, on.WidthAnchorsUsed >= off.WidthAnchorsUsed);
            Same("(6) окно вне коридора: нож не включён", false, on.BackgroundBandKnifeApplied);
        }

        /// <summary>Разбор объявленного состава, как у корпусной пробы.</summary>
        static FsaResult Declared(ResultData rd, List<FsaComponent> library, EfficiencyMaker.ResponseMatrix matrix, bool mask)
        {
            var an = new FsaAnalyzer();
            FsaMatrixBinding.Bind(an, rd.Efficiency.Geometry, matrix);
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                an.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                an.MinEnergy = peakConfig.Min_Range;
                an.MaxEnergy = peakConfig.Max_Range;
            }

            an.SampleBeta = false;
            an.BackgroundExcessMask = mask;
            an.BackgroundBandKnife = mask;
            FsaTuningReport.Print(an);
            return an.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library, FsaEfficiency.FromConfig(rd.Efficiency));
        }

        static FsaResult Run(ResultData rd, bool mask, EfficiencyMaker.ResponseMatrix matrix = null)
        {
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            FsaAnalysisSession.ProbeAnalyzerHook = an =>
            {
                if (matrix != null)
                {
                    FsaMatrixBinding.Bind(an, rd.Efficiency.Geometry, matrix);
                }

                an.BackgroundExcessMask = mask;
                an.BackgroundBandKnife = mask;
                an.AnchorWindowSkipsMasked = leverSkipMasked;
                an.BackgroundMaskGuard = guardOn;
                FsaTuningReport.Print(an);
            };
            try
            {
                session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, FsaCalculationOptions.Of(rd));
                return done.WaitOne(TimeSpan.FromSeconds(600)) ? session.Result : null;
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
            }
        }

        static void Same<T>(string what, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "OK " : "⛔ ", what, expected, actual);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            ProbeDeviceConfig.Attach(rd);
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

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
