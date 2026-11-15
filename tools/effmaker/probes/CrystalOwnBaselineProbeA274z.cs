using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace CrystalOwnBaselineProbeA274z
{
    /// <summary>
    /// (`AMBER274`, полоса `a274z` 15.11.2026) ГОДНОСТЬ СЧЁТА РЯДА ПО АЛЬФАМ: опыт с подложенным гамма-континуумом
    /// и порог подложки (`CrystalOwnActivityBuilder.AlphaCountMaxBaselineShare`); опыт с последним каналом спектра
    /// и порог ухода групп альф за верх шкалы (`AlphaOverflowMaxShare`).
    ///
    /// Вопрос: почему счёт ряда по альфам у проб с Th/U ниже счёта фона той же поры на 1–4 % (критик `crit274f`).
    /// Опыт: к спектру ФОНА (счёт годен, чужих линий нет) добавляется гладкий гамма-континуум известной величины и
    /// формы, счёт повторяется. Формы:
    ///   «постоянная» — одинаково на кэВ от низа шкалы до её верха: ровно то, что вычитает модель счёта;
    ///   «ступень»    — одинаково на кэВ до комптоновского края линии `--line=` и нуль выше: так устроен континуум
    ///                  верхней линии пробы (выше края его нет).
    /// Величина — доля счёта окна альф, которую дал бы этот уровень, протянутый на всё окно (то есть прирост
    /// подложки счёта). Имя линии пробе не нужно: энергия — ключом.
    ///
    /// Судится:
    ///   1. величина 0 — счёт побитно прежний (опыт ничего не трогает сам);
    ///   2. «постоянная» — активность не меняется (|сдвиг| ≤ 0.15 %): модель счёта верна там, где континуум
    ///      постоянен. ⚠ Не нуль: веса подгонки — 1/max(y, 1) по данным, и каналы окна с отсчётами не больше одного
    ///      получают вес 1 и тянут подложку (замер: до 0.12 % у фона с двумя такими каналами у верха окна, до
    ///      0.28 % у самого короткого фона с 45 такими каналами);
    ///   3. «ступень» — активность падает при любой подложенной величине: это и есть занижение у проб. Печатается
    ///      отношение сдвига к приросту подложки счёта (у проб с Th/U оно 0.4…0.75), не судится;
    ///   4. порог: у каждого подложенного случая, который счёт назвал годным, сдвиг активности не больше
    ///      `--max-bias=` (0.77 % — разброс фонов); самый сильный случай «ступени» назван негодным.
    ///   5. «переполнение» — в последний канал спектра добавлена доля счёта окна (у АЦП он копит всё, что выше
    ///      шкалы): активность НЕ меняется ни на бит (последний канал в окно счёта не входит — решение Amber
    ///      15.11.2026 вопросником, дословно: «Исключить (Рекомендую)»), до порога счёт годен, выше — назван
    ///      негодным (признак по-прежнему смотрит на последний канал).
    ///   6. «выброс» — доля счёта окна добавлена в ОДИН канал посреди окна (так оседает переполнение у спектра,
    ///      сложенного из наборов с разной шкалой): канал снят (в счёт идёт медиана соседей), активность не
    ///      сдвинулась (|сдвиг| ≤ 0.01 %), добавленное ушло в мерило ушедшего за шкалу; выше порога счёт негоден.
    /// На списке спектров (`--expect-unreliable=`): годность счёта каждого спектра — как названо ключом.
    ///
    ///     CrystalOwnBaselineProbeA274z --device=прибор.xml --spectra=list.csv --out=каталог --tags=B06,B10
    ///         [--shape=доб.FWHM,доля полки,длина полки] [--series=Бк,yyyy-MM-dd] [--bin=1] [--histories=200000]
    ///         [--line=2614.511] [--max-bias=0.77] [--expect-unreliable=S05,S06,…]
    ///         [--shares=0,0.5,1,2,3,5] (подложенные доли, %; первая — 0, последняя — самая сильная) [--flat-tol=0.15]
    /// Список — как у `CrystalOwnAlphaProbeA274f`: `метка;файл отсчётов;живое время;c0 c1 …;FWHM % на 662;верх;[дата]`.
    /// </summary>
    internal static class Program
    {
        static int bad;

        static void Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "СОШЛОСЬ     " : "НЕ СОШЛОСЬ  ") + what);
            if (!ok)
            {
                bad++;
            }
        }

        sealed class Row
        {
            public string Tag;
            public double[] Counts;
            public double Live;
            public double[] C;
            public double Fwhm662;
            public DateTime Date = ResultData.UnknownStartTime;

            public double EnergyAt(double ch)
            {
                double e = 0.0, p = 1.0;
                foreach (double k in this.C)
                {
                    e += k * p;
                    p *= ch;
                }

                return e;
            }

            public double FwhmAt(double e)
            {
                return this.Fwhm662 * Math.Sqrt(Math.Max(e, 1.0) / 661.657);
            }

            public PolynomialEnergyCalibration Calibration()
            {
                var cal = new PolynomialEnergyCalibration { PolynomialOrder = this.C.Length - 1, Coefficients = (double[])this.C.Clone() };
                cal.CheckCalibration(this.Counts.Length);
                return cal;
            }

            public SqrtFwhmCalibration FwhmCalibration()
            {
                double s = this.C[1], k2 = this.Fwhm662 * this.Fwhm662 / 661.657;
                return new SqrtFwhmCalibration { Coefficients = new[] { k2 * this.C[0] / (s * s), k2 / s, 0.0 } };
            }

            public int[] Raw()
            {
                return this.Counts.Select(x => (int)Math.Round(x)).ToArray();
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string outDir = null, spectra = null, devicePath = null, shape = null, series = null, tags = null, expect = null;
            int histories = 200000;
            double bin = 1.0, line = 2614.511, maxBias = 0.77, flatTol = 0.15;
            double[] shares = { 0.0, 0.5, 1.0, 2.0, 3.0, 5.0 };
            foreach (string a in args)
            {
                string v = a.Contains("=") ? a.Substring(a.IndexOf('=') + 1) : "";
                if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = v;
                else if (a.StartsWith("--device=", StringComparison.Ordinal)) devicePath = v;
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) bin = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--line=", StringComparison.Ordinal)) line = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--max-bias=", StringComparison.Ordinal)) maxBias = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--flat-tol=", StringComparison.Ordinal)) flatTol = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--shares=", StringComparison.Ordinal)) shares = v.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                else if (a.StartsWith("--shape=", StringComparison.Ordinal)) shape = v;
                else if (a.StartsWith("--series=", StringComparison.Ordinal)) series = v;
                else if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectra = v;
                else if (a.StartsWith("--tags=", StringComparison.Ordinal)) tags = v;
                else if (a.StartsWith("--expect-unreliable=", StringComparison.Ordinal)) expect = v;
                else { Console.Error.WriteLine("ключ не понят: " + a); return 2; }
            }

            if (outDir == null || devicePath == null || !File.Exists(devicePath) || spectra == null || !File.Exists(spectra))
            {
                Console.Error.WriteLine("нужны --out=, --device=<XML прибора в копии конфигурации> и --spectra=<список>");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            DeviceConfigInfo device;
            using (var stream = File.OpenRead(devicePath))
            {
                device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(stream);
            }

            EfficiencyConfigData eff = device.ActiveEfficiency
                                       ?? (device.EfficiencyConfigs != null && device.EfficiencyConfigs.Count > 0 ? device.EfficiencyConfigs[0] : null);
            if (eff == null || eff.Geometry == null)
            {
                Console.Error.WriteLine("у прибора нет конфигурации эффективности с геометрией");
                return 3;
            }

            eff = eff.Copy();
            if (shape != null)
            {
                double[] p = shape.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                eff.AlphaGroupShape = new AlphaGroupShape { ExtraFwhmShare = p[0], ShelfShare = p[1], ShelfLength = p[2], Origin = "ключ пробы" };
            }

            if (series != null)
            {
                string[] p = series.Split(',');
                eff.CrystalSeriesActivity = new CrystalSeriesActivity
                {
                    ActivityBq = double.Parse(p[0], CultureInfo.InvariantCulture),
                    Date = DateTime.ParseExact(p[1], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Origin = "ключ пробы",
                };
            }

            GeometryModel g = eff.Geometry;
            string recipeNote, note;
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274,
                                                                             ResponseCrystalSection.RecipeFor(g, eff, out recipeNote), out note);
            CrystalOwnEntry series227 = null;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (entry != null && entry.Free && entry.AlphaPerDecay != null)
                {
                    series227 = entry;
                }
            }

            if (series227 == null)
            {
                Console.Error.WriteLine("в разделе слоя нет свободной записи ряда с частью альф (источник QF прибора пуст?)");
                return 3;
            }

            double[] known = CrystalOwnActivityBuilder.KnownPerSecond(section);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "порог годности счёта: подложка ≤ {0:0.#} % окна альф; линия в окне: z ≥ {1:0.#} и ≥ {2:0.#} % счёта окна",
                100.0 * CrystalOwnActivityBuilder.AlphaCountMaxBaselineShare, CrystalOwnActivityBuilder.AlphaLineMinZ,
                100.0 * CrystalOwnActivityBuilder.AlphaLineMinShare));

            List<Row> rows = ReadList(spectra);
            var text = new StringBuilder("tag;A_Bq;base_pct;line_share_pct;overflow_pct;hot_channels;reliable;entry;entry_Bq\n");

            // ---- Список: годность счёта каждого спектра и запись ряда на входе разбора ----
            HashSet<string> unreliable = expect != null ? new HashSet<string>(expect.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) : null;
            CrystalOwnSeriesFallback fallback = eff.CrystalSeriesActivity != null
                ? new CrystalOwnSeriesFallback { ActivityBq = eff.CrystalSeriesActivity.ActivityBq, Date = eff.CrystalSeriesActivity.Date }
                : null;
            Console.WriteLine();
            Console.WriteLine("== счёт по альфам на спектрах списка ==");
            Console.WriteLine("метка |  A, Бк   подл.%  линия % счёта окна  ушло за шкалу % счёта окна  каналов-выбросов  годен | запись ряда на входе разбора");
            int wrong = 0;
            foreach (Row row in rows)
            {
                CrystalOwnActivityBuilder.AlphaCount c = CrystalOwnActivityBuilder.CountByAlphas(series227, bin, row.Counts, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known);
                if (fallback != null)
                {
                    fallback.SpectrumStart = row.Date;
                }

                string hookNote;
                ResponseCrystalSection hooked = CrystalOwnActivityBuilder.FixSeriesByAlphas(section, row.Raw(), row.Live, row.Calibration(), row.FwhmCalibration(),
                                                                                           null, fallback, out hookNote);
                CrystalOwnEntry h = null;
                foreach (CrystalOwnEntry entry in hooked.Entries)
                {
                    if (entry != null && entry.AlphaPerDecay != null)
                    {
                        h = entry;
                    }
                }

                string state = h == null ? "снята" : h.Free ? "свободная" : (hookNote ?? "").Contains("из поля прибора") ? "поле" : "счёт";
                string entryBq = h != null && !h.Free ? h.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) : "";
                double basePct = c.Counts > 0.0 ? 100.0 * c.Baseline / c.Counts : 0.0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-4} | {1,7:F5}  {2,5:F2}  {3,6:F2}  {4,6:F2}  {5}  {6} | {7} {8}",
                                                row.Tag, c.ActivityBq, basePct, 100.0 * c.LineShare, 100.0 * c.OverflowShare, c.HotChannels, c.Reliable ? "да " : "НЕТ", state, entryBq));
                text.Append(row.Tag).Append(';').Append(c.ActivityBq.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                    .Append(basePct.ToString("F3", CultureInfo.InvariantCulture)).Append(';')
                    .Append((100.0 * c.LineShare).ToString("F3", CultureInfo.InvariantCulture)).Append(';')
                    .Append((100.0 * c.OverflowShare).ToString("F3", CultureInfo.InvariantCulture)).Append(';')
                    .Append(c.HotChannels.ToString(CultureInfo.InvariantCulture)).Append(';').Append(c.Reliable ? 1 : 0).Append(';')
                    .Append(state).Append(';').Append(entryBq).Append('\n');
                if (unreliable != null && c.Reliable == unreliable.Contains(row.Tag))
                {
                    wrong++;
                    Console.WriteLine("      ⚠ годность не та, что названа ключом --expect-unreliable");
                }

                if (unreliable != null && fallback != null && !c.Reliable && state != "поле")
                {
                    wrong++;
                    Console.WriteLine("      ⚠ счёт не годен, поле прибора есть, а запись ряда — не из поля");
                }
            }

            File.WriteAllText(Path.Combine(outDir, "count.csv"), text.ToString(), new UTF8Encoding(false));
            if (unreliable != null)
            {
                Check("годность счёта у всех спектров списка — как названо ключом (негодных названо "
                      + unreliable.Count.ToString(CultureInfo.InvariantCulture) + "); негодный счёт при поле прибора — ряд из поля", wrong == 0);
            }

            // ---- Опыт: подложенный континуум ----
            if (tags != null)
            {
                double edgeKev = line / (1.0 + 510.99895 / (2.0 * line));
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "== опыт: к фону подложен гамма-континуум; «ступень» — до комптоновского края линии {0:F1} кэВ ({1:F1} кэВ) ==", line, edgeKev));
                var want = new HashSet<string>(tags.Split(','));
                foreach (Row row in rows)
                {
                    if (!want.Contains(row.Tag))
                    {
                        continue;
                    }

                    CrystalOwnActivityBuilder.AlphaCount c0 = CrystalOwnActivityBuilder.CountByAlphas(series227, bin, row.Counts, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known);
                    double base0 = 100.0 * c0.Baseline / c0.Counts;
                    double above = (c0.WindowHiKev - Math.Max(edgeKev, c0.WindowLoKev)) / (c0.WindowHiKev - c0.WindowLoKev);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}: счёт {1:F5} Бк, подложка {2:F2} %, окно {3:F0}–{4:F0} кэВ-экв; доля окна выше края {5:F3}",
                        row.Tag, c0.ActivityBq, base0, c0.WindowLoKev, c0.WindowHiKev, above));
                    Check(row.Tag + ": счёт фона годен (опыт ставится на годном счёте)", c0.Reliable);
                    Console.WriteLine("  форма       подложено, %   A, Бк     сдвиг A, %   подложка, %   сдвиг / прирост подложки   годен");
                    bool strongestFlagged = false;
                    foreach (string form in new[] { "постоянная", "ступень" })
                    {
                        foreach (double share in shares)
                        {
                            // уровень на кэВ: протянутый на всё окно, он даёт share % счёта окна
                            double perKev = share / 100.0 * c0.Counts / (c0.WindowHiKev - c0.WindowLoKev);
                            var y = (double[])row.Counts.Clone();
                            for (int i = 0; i < y.Length; i++)
                            {
                                double lo = row.EnergyAt(i - 0.5), hi = row.EnergyAt(i + 0.5);
                                double top = form == "ступень" ? Math.Min(hi, edgeKev) : hi;
                                if (top > lo)
                                {
                                    y[i] += perKev * (top - lo);
                                }
                            }

                            CrystalOwnActivityBuilder.AlphaCount c = CrystalOwnActivityBuilder.CountByAlphas(series227, bin, y, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known);
                            double shift = 100.0 * (c.ActivityBq / c0.ActivityBq - 1.0);
                            double basePct = 100.0 * c.Baseline / c.Counts;
                            double grown = basePct - base0;
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-11} {1,8:F1}      {2,8:F5}   {3,8:F3}     {4,8:F2}     {5,12:F2}              {6}",
                                                            form, share, c.ActivityBq, shift, basePct, grown > 0.0 ? shift / grown : 0.0, c.Reliable ? "да" : "НЕТ"));
                            string what = row.Tag + ", " + form + " " + share.ToString("0.#", CultureInfo.InvariantCulture) + " %: ";
                            if (share == 0.0)
                            {
                                Check(what + "счёт побитно прежний", c.ActivityBq == c0.ActivityBq && c.Baseline == c0.Baseline && c.Reliable == c0.Reliable);
                                continue;
                            }

                            if (form == "постоянная")
                            {
                                Check(what + "активность не сдвинулась (|сдвиг| ≤ " + flatTol.ToString("0.##", CultureInfo.InvariantCulture) + " %)", Math.Abs(shift) <= flatTol);
                            }
                            else
                            {
                                Check(what + "активность упала", shift < 0.0);
                                if (share == shares[shares.Length - 1])
                                {
                                    strongestFlagged = !c.Reliable;
                                }
                            }

                            if (c.Reliable)
                            {
                                Check(what + "счёт назван годным — сдвиг не больше " + maxBias.ToString("0.##", CultureInfo.InvariantCulture) + " %",
                                      Math.Abs(shift) <= maxBias);
                            }
                        }
                    }

                    Check(row.Tag + ": самая сильная «ступень» названа негодной", strongestFlagged);

                    // «переполнение»: доля счёта окна добавлена в последний канал спектра
                    double limit = 100.0 * CrystalOwnActivityBuilder.AlphaOverflowMaxShare;
                    Console.WriteLine("  в последний канал, %   A, Бк     сдвиг A, %   последний канал, % счёта окна   годен");
                    foreach (double share in new[] { 0.0, 1.0, 4.0, 6.0 })
                    {
                        var y = (double[])row.Counts.Clone();
                        y[y.Length - 1] += share / 100.0 * c0.Counts;
                        CrystalOwnActivityBuilder.AlphaCount c = CrystalOwnActivityBuilder.CountByAlphas(series227, bin, y, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known);
                        double shift = 100.0 * (c.ActivityBq / c0.ActivityBq - 1.0);
                        bool over = 100.0 * c.OverflowShare > limit;
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,12:F1}          {1,8:F5}   {2,8:F3}     {3,12:F2}                 {4}",
                                                        share, c.ActivityBq, shift, 100.0 * c.OverflowShare, c.Reliable ? "да" : "НЕТ"));
                        string what = row.Tag + ", переполнение " + share.ToString("0.#", CultureInfo.InvariantCulture) + " %: ";
                        if (share == 0.0)
                        {
                            Check(what + "счёт побитно прежний", c.ActivityBq == c0.ActivityBq && c.Reliable == c0.Reliable);
                            continue;
                        }

                        if (!(c0.OverflowShare > 0.0))
                        {
                            // окно до верха шкалы не доходит — последний канал в счёт не входит, и признак молчит
                            Check(what + "окно до верха шкалы не доходит — счёт не изменился и годен", shift == 0.0 && c.Reliable);
                            continue;
                        }

                        Check(what + "активность не изменилась ни на бит (последний канал в счёт не входит)", c.ActivityBq == c0.ActivityBq && c.Baseline == c0.Baseline);
                        Check(what + (over ? "счёт назван негодным (группы альф за верхом шкалы)" : "счёт годен (последний канал ниже порога)"),
                              c.Reliable == !over && c.Overflow == over);
                    }

                    // «выброс»: доля счёта окна добавлена в один канал посреди окна
                    int mid = 0;
                    for (int i = 0; i < row.Counts.Length; i++)
                    {
                        if (row.EnergyAt(i) < 0.5 * (c0.WindowLoKev + c0.WindowHiKev))
                        {
                            mid = i;
                        }
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  в канал {0}, %   A, Бк     сдвиг A, %   ушло за шкалу, % счёта окна   каналов-выбросов   годен", mid));
                    foreach (double share in new[] { 1.0, 4.0 })
                    {
                        var y = (double[])row.Counts.Clone();
                        y[mid] += share / 100.0 * c0.Counts;
                        CrystalOwnActivityBuilder.AlphaCount c = CrystalOwnActivityBuilder.CountByAlphas(series227, bin, y, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known);
                        double shift = 100.0 * (c.ActivityBq / c0.ActivityBq - 1.0);
                        bool over = 100.0 * c.OverflowShare > limit;
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,12:F1}          {1,8:F5}   {2,8:F4}     {3,12:F2}                 {4}               {5}",
                                                        share, c.ActivityBq, shift, 100.0 * c.OverflowShare, c.HotChannels, c.Reliable ? "да" : "НЕТ"));
                        string what = row.Tag + ", выброс " + share.ToString("0.#", CultureInfo.InvariantCulture) + " %: ";
                        Check(what + "канал снят, активность не сдвинулась (|сдвиг| ≤ 0.01 %)", c.HotChannels == c0.HotChannels + 1 && Math.Abs(shift) <= 0.01);
                        Check(what + "добавленное ушло в мерило (± 0.05 % счёта окна)", Math.Abs(100.0 * (c.OverflowShare - c0.OverflowShare) - share) <= 0.05);
                        Check(what + (over ? "счёт назван негодным" : "счёт годен"), c.Reliable == !over && c.Overflow == over);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static List<Row> ReadList(string spectra)
        {
            var rows = new List<Row>();
            foreach (string raw in File.ReadAllLines(spectra))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] f = raw.Split(';');
                var row = new Row
                {
                    Tag = f[0],
                    Counts = File.ReadAllLines(f[1]).Where(l => l.Length > 0).Select(l => double.Parse(l, CultureInfo.InvariantCulture)).ToArray(),
                    Live = double.Parse(f[2], CultureInfo.InvariantCulture),
                    C = f[3].Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray(),
                    Fwhm662 = double.Parse(f[4], CultureInfo.InvariantCulture) / 100.0 * 661.657,
                };
                if (f.Length > 6 && f[6].Length > 0)
                {
                    row.Date = DateTime.ParseExact(f[6], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                rows.Add(row);
            }

            return rows;
        }
    }
}
