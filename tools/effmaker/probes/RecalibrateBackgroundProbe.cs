// ═══════════════════════════════════════════════════════════════════════════
//  Полоса П88, 17.09.2026. AMBER22: ПЕРЕКАЛИБРОВКА ШКАЛЫ ФОНА «КАК В ПРИЛОЖЕНИИ»
// ═══════════════════════════════════════════════════════════════════════════
//
//  ПОСТАНОВКА (Amber 17.09.2026, консоль, дословно: «перекалибровка шкалы фона
//  Фон дом 09.09.2026 в приложении - сделай сам»; вопросником того же дня —
//  объём «Файл + копии в трёх спектрах», запись «На место, копии до правки — в
//  D:\BqMoni_Claude\p88\backup»). Дефект (П67, находка 3): файл фона несёт
//  скопированную ШКАЛУ ПРОБЫ P (побитово те же пять коэффициентов), при которой
//  собственные пики фона K-40 1461 и Bi-214 1764 садятся на −76 / −95 кэВ; та же
//  шкала лежит во встроенном <BackgroundEnergySpectrum> каждой пробы.
//
//  ЧТО ДЕЛАЕТ ПРОБА — то, что сделала бы Amber в BecqMoni, только без окон и с
//  положительными контролями на каждом шаге:
//    1. читает файлы ШТАТНЫМ сериализатором приложения (XmlSerializer(ResultDataFile),
//       как DocumentManager.OpenDocument) и ПЕРВЫМ ДЕЛОМ меряет контроль формата:
//       «прочитал → записал без правок» побайтно против файла (XmlWriter с теми же
//       настройками, что у DocumentManager.SaveDocument: UTF-8 с BOM, отступы, CRLF).
//       Единственное допустимое различие — дописанная строка `<ROIConfigReference />`
//       у файлов, где элемента не было (инициализатор поля ResultData; приложение
//       при сохранении дописывает её так же). Иное различие — правки не будет (код 2);
//    2. находит центроиды опорных пиков ФОНА в КАНАЛАХ: гауссиана над линейной
//       подложкой, спутники группы (583 у 609, 969 у 911, 1729 у 1764, 338 у 352,
//       242 у 238) той же ширины, положение привязано разностью энергий через
//       местное усиление, амплитуда своя. Сид — линейная шкала по двум самым
//       заметным пикам выше 1 МэВ (K-40 и Tl-208 2614), затем три прохода
//       «фит → полином → фит». Опора принимается, когда пик значим (A/σA ≥ 5),
//       центроид определён (σc ≤ 0.1 ПШПВ), не упёрся в границу и ширина — пика;
//       непринятые печатаются с числами;
//    3. ⚠ НИЗ ШКАЛЫ — ПЕРЕНОСОМ ИЗ КАЛИБРОВКИ ПРИБОРА 08.09.2026, а не по пикам фона.
//       Разбор П88 (журнал §2): у фона комнаты НЕТ чистой линии ниже 600 кэВ —
//       238 (Pb-212) не значим (A/σA 0.1), 352 слита с Ac-228 338, рентген свинца
//       75/85 — неразрешённый горб; полином любой степени, подогнанный по опорам
//       511…2614, ВНИЗУ экстраполирует и ставит 75 кэВ на 59, 238 на 224 — хуже
//       нынешней (скопированной) шкалы, которая внизу почти верна. Зато сама P
//       (шкала пробы, лежащая в этом же файле) внизу ДОКАЗАНА: Amber строила её
//       08.09.2026 по источникам (Am-241 59.5, Lu-176 88/202/307, Pb-212 239,
//       Tl-208 583, Cs-137 662, Tl-208 2614; каналы этих опор лежат в таблице
//       ПШПВ файла), и ошибается лишь между 662 и 2614, где опор нет. Поэтому
//       опоры ниже 600 кэВ берутся из P: канал фона = P⁻¹(E)·r, r — отношение
//       усилений фон/проба, измеренное на 2614 (единственная линия, где P верна и
//       которая в фоне чистая). Прочие точки P и слабые пики фона (511, 609, 1764,
//       смесь 338/352) — КОНТРОЛЬ, в подгонку не идут, печатаются с невязкой;
//    4. строит PolynomialEnergyCalibration через ТОТ ЖЕ решатель, что окно калибровки
//       (Utils.CalibrationSolver.SolveGuarded по CalibrationPoint с ЦЕЛЫМ каналом —
//       окно берёт канал под курсором, дробных у него нет). Степень 3 умолчанием:
//       2-я ни при каких опорах не укладывает эту шкалу лучше ±4…7 кэВ (сравнение
//       печатается рядом — §3), у NaI с этим АЦП нелинейность кубическая, шкала
//       самой пробы у Amber 4-й степени по той же причине;
//    5. пишет шкалу в <EnergySpectrum><EnergyCalibration> файла фона и в
//       <BackgroundEnergySpectrum><EnergyCalibration> каждой копии; всё остальное
//       не трогается ни на байт — после записи файл сверяется с эталоном п. 1:
//       различие обязано лежать ВНУТРИ целевого блока <EnergyCalibration>;
//    6. до записи — копии всех файлов в --backup (с подкаталогом источника),
//       sha256 до/после печатаются.
//
//  РЕЖИМ --sample= (шаг 2 полосы, решение Amber 17.09.2026, дословно: «Перекалибровать три пробы по их пикам»):
//    шкала САМОЙ пробы (<EnergySpectrum><EnergyCalibration>) — по её пикам ряда Th-232 (238.6, 583.2 (+609 фона),
//    911.2 (+969), 2614.5) и K-40 1460.8 фона комнаты в ней же; контроль — 338 (+328/352), 727, 969, 1588 (+1631);
//    низ — тем же переносом из её файловой шкалы P (калибровка прибора 08.09) по усилению, измеренному на её 2614;
//    та же кубика тем же решателем. Встроенный фон (уже перекалиброван), ПШПВ, отсчёты, времена — не трогаются
//    (проверяется после записи побитово). Каждая проба считается и пишется порознь.
//
//  КЛЮЧИ
//    --sample=<файл пробы>         повторяемый: режим пробы (см. выше); с --bg/--copy не смешивать
//    --bg=<файл фона>              обязателен в режиме фона
//    --copy=<файл пробы>           повторяемый: копии шкалы в его встроенный фон
//    --backup=<каталог>            куда положить копии до правки (обязателен при --write)
//    --order=3                     степень полинома (умолчание 3)
//    --window=1.0                  полуширина окна фита в ПШПВ (умолчание 1.0; 0.8 и 1.3 — чувствительность)
//    --no-transfer                 без переноса низа из P — только пики фона (для сравнения)
//    --write                       без него — только счёт и печать (сухой прогон)
//    --check-copies                центроиды тех же пиков во ВСТРОЕННЫХ фонах копий
//                                  (у контакта 08.09 встроен снимок 47 908 с того же фона)
//
//  КОДЫ: 0 — всё сошлось (невязки опор ≤ 2 кэВ — до 1 чисто, 1…2 с оговоркой в
//  печати, — шкала годна; при --write файлы записаны и проверены); 1 — невязка
//  опоры > 2 кэВ или шкала не принята CheckCalibration — НИЧЕГО НЕ ПИСАЛОСЬ;
//  2 — контроль формата не прошёл (файл переписывать нельзя); 3 — отказ
//  ввода/вывода; 4 — проверка после записи нашла различие вне целевого блока.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

static class RecalibrateBackgroundProbe
{
    enum Kind { Peak, Transfer }

    // ── опорные линии: главная, имя, спутники (кэВ), опора ли, род ──
    // Энергии — табличные (NNDC/LARA), три знака. 352 — только КОНТРОЛЬ: у NaI она
    // сливается с Ac-228 338 (13.6 кэВ при ПШПВ ~28), и центроид смеси зависит от
    // отношения Th/U в комнате, а не от шкалы (П67: «садится на плечо»). 511 и 1764
    // — контроль: слабы (A/σA 7 и 3.5), их центроиды ходят на ±3…9 кэВ. 609 —
    // контроль: дублет с 583 при ПШПВ 114 кан и расстоянии 73 кан вырожден,
    // центроид 609 ходит на ±10 кан от того, какую амплитуду 583 принять (П88 §2).
    sealed class Line
    {
        public double E; public string Name; public double[] Sat; public bool Anchor; public Kind Kind;
        public Line(double e, string name, double[] sat, bool anchor, Kind kind) { E = e; Name = name; Sat = sat; Anchor = anchor; Kind = kind; }
    }
    static readonly Line[] Lines = new Line[]
    {
        new Line(59.541, "Am-241·P", new double[0], true, Kind.Transfer),
        new Line(88.34, "Lu-176·P", new double[0], false, Kind.Transfer),
        new Line(201.83, "Lu-176·P", new double[0], false, Kind.Transfer),
        new Line(238.632, "Pb-212", new double[] { 242.0 }, false, Kind.Peak),
        new Line(238.632, "Pb-212·P", new double[0], true, Kind.Transfer),
        new Line(306.78, "Lu-176·P", new double[0], false, Kind.Transfer),
        new Line(351.932, "Pb-214", new double[] { 338.32 }, false, Kind.Peak),
        new Line(511.0, "annih", new double[0], false, Kind.Peak),
        new Line(583.187, "Tl-208·P", new double[0], true, Kind.Transfer),
        new Line(609.312, "Bi-214", new double[] { 583.187 }, false, Kind.Peak),
        new Line(661.657, "Cs-137·P", new double[0], false, Kind.Transfer),
        new Line(911.204, "Ac-228", new double[] { 968.97 }, true, Kind.Peak),
        new Line(1120.29, "Bi-214", new double[0], true, Kind.Peak),
        new Line(1460.82, "K-40", new double[0], true, Kind.Peak),
        new Line(1764.49, "Bi-214", new double[] { 1729.6 }, false, Kind.Peak),
        new Line(2614.51, "Tl-208", new double[0], true, Kind.Peak),
    };
    const double E2614 = 2614.51;

    // ── линии ПРОБЫ (ториевый диск + фон комнаты в ней же) для режима --sample ──
    // 238 — самая сильная линия диска (Pb-214 242 фона ничтожна); 583 — с 609 фона (дублет, амплитуда своя);
    // 911 — с 969 (Ac-228 964.8/969.0, разрешимы: 58 кэВ при ПШПВ ~52); K-40 — фон комнаты в самой пробе (1.9 cps на
    // континууме диска); 2614 — чистая. Контроль: 338 (+328 Ac-228, +352 Pb-214 фона — смесь), 727 Bi-212 (6.6 %),
    // 1588 (+1631 Ac-228) — слабые/смешанные, в подгонку не идут. Низ (< 238) — перенос из P: 59.5 опора,
    // 88/202/307/662 — контроль переноса.
    static readonly Line[] SampleLines = new Line[]
    {
        new Line(59.541, "Am-241·P", new double[0], true, Kind.Transfer),
        new Line(88.34, "Lu-176·P", new double[0], false, Kind.Transfer),
        new Line(201.83, "Lu-176·P", new double[0], false, Kind.Transfer),
        new Line(238.632, "Pb-212", new double[0], true, Kind.Peak),
        new Line(306.78, "Lu-176·P", new double[0], false, Kind.Transfer),
        new Line(338.32, "Ac-228", new double[] { 328.0, 351.932 }, false, Kind.Peak),
        new Line(583.187, "Tl-208", new double[] { 609.312 }, true, Kind.Peak),
        new Line(661.657, "Cs-137·P", new double[0], false, Kind.Transfer),
        new Line(727.33, "Bi-212", new double[0], false, Kind.Peak),
        new Line(911.204, "Ac-228", new double[] { 968.97 }, true, Kind.Peak),
        new Line(1460.82, "K-40", new double[0], true, Kind.Peak),
        new Line(1588.2, "Ac-228", new double[] { 1630.6 }, false, Kind.Peak),
        new Line(2614.51, "Tl-208", new double[0], true, Kind.Peak),
    };

    sealed class Fit
    {
        public Line L; public double C, SigC, Fwhm, FwhmCal, A, SigA, Chi2, Seed; public int Lo, Hi; public double[] SatA;
        public bool Ok; public string Why;
    }

    static readonly XmlWriterSettings AppXmlSettings = new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true };
    static int worst = 0;
    static double transferMapA = 0.0, transferMapB = 1.0;   // последняя карта переноса P → съёмка (для печати)

    static string F(double v, int d) { return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); }
    static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
    static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        string bgPath = null, backup = null;
        List<string> copies = new List<string>();
        List<string> samples = new List<string>();
        int order = 3; double window = 1.0; bool write = false, checkCopies = false, transfer = true;
        foreach (string a in args)
        {
            if (a.StartsWith("--sample=")) samples.Add(a.Substring(9));
            else if (a.StartsWith("--bg=")) bgPath = a.Substring(5);
            else if (a.StartsWith("--copy=")) copies.Add(a.Substring(7));
            else if (a.StartsWith("--backup=")) backup = a.Substring(9);
            else if (a.StartsWith("--order=")) order = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--window=")) window = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a == "--write") write = true;
            else if (a == "--check-copies") checkCopies = true;
            else if (a == "--no-transfer") transfer = false;
            else { Console.WriteLine("неизвестный ключ: " + a); return 3; }
        }
        if (write && backup == null) { Console.WriteLine("--write без --backup= не делается: копии до правки обязательны (решение Amber 17.09.2026)"); return 3; }
        if (samples.Count > 0)
        {
            if (bgPath != null || copies.Count > 0) { Console.WriteLine("--sample= не смешивается с --bg=/--copy="); return 3; }
            return RunSamples(samples, backup, order, window, transfer, write);
        }
        if (bgPath == null) { Console.WriteLine("нужен --bg=<файл фона> или --sample=<файл пробы>"); return 3; }

        Console.WriteLine("RecalibrateBackgroundProbe (П88, AMBER22): шкала фона по его пикам (верх) и переносу калибровки прибора (низ), сериализатором приложения");
        Console.WriteLine("  фон   : " + bgPath);
        foreach (string c in copies) Console.WriteLine("  копия : " + c);
        Console.WriteLine("  степень " + I(order) + ", окно ±" + F(window, 1) + " ПШПВ, низ " + (transfer ? "переносом из P" : "БЕЗ переноса (одни пики фона)") + ", режим " + (write ? "ЗАПИСЬ" : "сухой прогон"));
        Console.WriteLine();

        // ── §1 положительный контроль формата ──
        Console.WriteLine("§1 Формат: «прочитал → записал без правок» побайтно против файла");
        List<string> all = new List<string> { bgPath }; all.AddRange(copies);
        Dictionary<string, byte[]> original = new Dictionary<string, byte[]>();
        Dictionary<string, byte[]> baseline = new Dictionary<string, byte[]>();
        Dictionary<string, ResultDataFile> loaded = new Dictionary<string, ResultDataFile>();
        bool formatOk = true;
        foreach (string p in all)
        {
            byte[] bytes;
            ResultDataFile rdf;
            try
            {
                bytes = File.ReadAllBytes(p);
                rdf = Deserialize(bytes);
            }
            catch (Exception ex) { Console.WriteLine("  ОТКАЗ чтения " + p + ": " + ex.Message); return 3; }
            original[p] = bytes; loaded[p] = rdf;
            byte[] again = Serialize(rdf);
            baseline[p] = again;
            int diff = FirstDiff(bytes, again);
            bool same = diff < 0 && bytes.Length == again.Length;
            string verdict;
            if (same) verdict = "ПОБИТОВО";
            else
            {
                // Единственное допустимое различие — ВСТАВКА пустого элемента, которого в файле не было, а у
                // приложения он есть всегда: `ResultData.roiConfigReference = new ROIConfigReference()` в
                // инициализаторе поля, и при отсутствии <ROIConfigReference> в файле сериализатор дописывает
                // `<ROIConfigReference />`. Так же поступит и само приложение при сохранении этого файла — это не
                // моя правка, а его штатное поведение; всё прочее обязано совпасть байт в байт.
                byte[] stripped = WithoutOneLine(again, "<ROIConfigReference />");
                if (stripped != null && stripped.Length == bytes.Length && FirstDiff(stripped, bytes) < 0)
                    verdict = "ПОБИТОВО с поправкой: сериализатор ДОПИСЫВАЕТ строку <ROIConfigReference /> (+" + I(again.Length - bytes.Length) + " байт; в файле элемента не было, приложение при сохранении сделает то же)";
                else
                {
                    string inserted = InsertedOnly(bytes, again);
                    verdict = "РАЗЛИЧИЕ с байта " + I(diff) + " (длины " + I(bytes.Length) + " / " + I(again.Length) + ")" + (inserted != null ? "; вставка: «" + inserted.Trim() + "»" : "");
                    formatOk = false;
                }
            }
            Console.WriteLine("  " + Path.GetFileName(p) + ": " + I(bytes.Length) + " байт, BOM " + (bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? "да" : "нет")
                + ", CRLF " + I(CountCrlf(bytes)) + ", sha256 " + Sha(bytes) + " → " + verdict);
        }
        if (!formatOk)
        {
            Console.WriteLine("  ⛔ сериализатор приложения не воспроизводит файл байт в байт — переписывать такой файл нельзя (код 2)");
            return 2;
        }
        Console.WriteLine();

        // ── §2 центроиды пиков фона и перенос низа ──
        ResultDataFile bgFile = loaded[bgPath];
        ResultData bgData = bgFile.ResultDataList[0];
        EnergySpectrum bg = bgData.EnergySpectrum;
        PolynomialEnergyCalibration fileCal = (PolynomialEnergyCalibration)bg.EnergyCalibration;
        fileCal.CheckCalibration(bg.NumberOfChannels);
        FwhmCalibration fwhmCal = bgData.FwhmCalibration;
        if (fwhmCal == null) { Console.WriteLine("  ОТКАЗ: у фона нет калибровки ПШПВ — окна фита строить нечем"); return 3; }
        Console.WriteLine("§2 Пики фона (" + I(bg.NumberOfChannels) + " каналов, " + bg.TotalPulseCount.ToString(CultureInfo.InvariantCulture) + " отсчётов, живое " + F(bg.LiveTime, 1) + " с; ПШПВ " + fwhmCal.GetType().Name + ")");
        Console.WriteLine("  файловая шкала P: " + string.Join(" ", fileCal.Coefficients.Select(R).ToArray()));
        double[] newCoef;
        List<Fit> fits = FitAll(bg, fwhmCal, fileCal, window, order, transfer, true, Lines, out newCoef);
        if (fits == null) return 3;

        Console.WriteLine("  чувствительность центроидов ПИКОВ к окну (±0.8 / ±1.3 ПШПВ), в каналах и кэВ новой шкалы:");
        foreach (double w in new double[] { 0.8, 1.3 })
        {
            double[] tmp;
            List<Fit> alt = FitAll(bg, fwhmCal, fileCal, w, order, transfer, false, Lines, out tmp);
            if (alt == null) continue;
            StringBuilder sb = new StringBuilder("    ±" + F(w, 1) + ": ");
            for (int i = 0; i < alt.Count; i++)
            {
                Fit f = alt[i], main = fits[i];
                if (f.L.Kind != Kind.Peak || !f.Ok || !main.Ok) continue;
                sb.Append(f.L.Name + " " + F(f.C - main.C, 1) + " кан (" + F(Poly(newCoef, f.C) - Poly(newCoef, main.C), 1) + " кэВ)  ");
            }
            Console.WriteLine(sb.ToString());
        }
        Console.WriteLine();

        // ── §3 шкала решателем приложения ──
        Console.WriteLine("§3 Шкала: CalibrationSolver.SolveGuarded по CalibrationPoint (ЦЕЛЫЙ канал, как окно калибровки), степень " + I(order));
        List<CalibrationPoint> points = new List<CalibrationPoint>();
        foreach (Fit f in fits.Where(x => x.Ok && x.L.Anchor))
        {
            int ch = (int)Math.Round(f.C);
            points.Add(new CalibrationPoint(ch, (decimal)f.L.E, bg.Spectrum[ch]));
        }
        int usedOrder;
        double[] solved = CalibrationSolver.SolveGuarded(points, order, bg.NumberOfChannels, false, out usedOrder);
        if (solved == null) { Console.WriteLine("  ОТКАЗ решателя: SolveGuarded вернул null"); return 1; }
        double[] plain = CalibrationSolver.Solve(points, order);
        Console.WriteLine("  опор " + I(points.Count) + " (" + string.Join(", ", fits.Where(x => x.Ok && x.L.Anchor).Select(x => x.L.Name + "@" + I((int)Math.Round(x.C))).ToArray()) + "), степень запрошена " + I(order) + ", принята " + I(usedOrder)
            + (usedOrder != order ? " ⚠ ПОНИЖЕНА (BendOk/CheckCalibration)" : ""));
        Console.WriteLine("  коэффициенты: " + string.Join(" ", solved.Select(R).ToArray()));
        Console.WriteLine("  Solve без сторожа той же степени: " + (SameArray(plain, solved) ? "побитово те же" : string.Join(" ", plain.Select(R).ToArray())));
        PolynomialEnergyCalibration newCal = new PolynomialEnergyCalibration { PolynomialOrder = solved.Length - 1, Coefficients = (double[])solved.Clone() };
        bool calOk = newCal.CheckCalibration(bg.NumberOfChannels);
        Console.WriteLine("  CheckCalibration(" + I(bg.NumberOfChannels) + "): " + (calOk ? "годна" : "⛔ НЕ ГОДНА") + "; MSE окна калибровки: " + F(CalibrationSolver.MSE(solved, points), 5)
            + "; E(0) = " + F(newCal.ChannelToEnergy(0), 1) + ", E(" + I(bg.NumberOfChannels - 1) + ") = " + F(newCal.ChannelToEnergy(bg.NumberOfChannels - 1), 1) + " кэВ");
        // для сведения — соседние степени по тем же опорам и вариант «одни пики фона, степень 2»
        foreach (int alt in new int[] { 2, 4 })
        {
            if (alt == order || points.Count < alt + 1) continue;
            int u; double[] c = CalibrationSolver.SolveGuarded(points, alt, bg.NumberOfChannels, false, out u);
            if (c == null) continue;
            Console.WriteLine("  для сведения — степень " + I(alt) + " (принята " + I(u) + ") по тем же опорам: невязки " + string.Join(" ", points.Select(pt => F((double)pt.Energy - Poly(c, pt.Channel), 1)).ToArray()) + "; E(240) = " + F(Poly(c, 240), 1) + " — НЕ пишется");
        }
        if (transfer)
        {
            List<CalibrationPoint> onlyPeaks = fits.Where(x => x.Ok && x.L.Kind == Kind.Peak && (x.L.Anchor || x.L.Name == "annih" || x.L.Name == "Bi-214") && x.L.E > 500).Select(x => new CalibrationPoint((int)Math.Round(x.C), (decimal)x.L.E, 0)).ToList();
            int u; double[] c = onlyPeaks.Count >= 3 ? CalibrationSolver.SolveGuarded(onlyPeaks, 2, bg.NumberOfChannels, false, out u) : null;
            if (c != null)
                Console.WriteLine("  для сведения — ОДНИ пики фона ≥ 511 (" + string.Join(", ", onlyPeaks.Select(pt => F((double)pt.Energy, 0)).ToArray()) + "), степень 2: невязки " + string.Join(" ", onlyPeaks.Select(pt => F((double)pt.Energy - Poly(c, pt.Channel), 1)).ToArray())
                    + "; внизу E(240) = " + F(Poly(c, 240), 1) + " (по P·r ≈ " + F(fileCal.ChannelToEnergy(240 / RatioOf(fits, fileCal, bg.NumberOfChannels)), 1) + "), E(699) = " + F(Poly(c, 699), 1) + " — НЕ пишется");
        }
        Console.WriteLine();

        // ── §4 таблица до/после ──
        Console.WriteLine("§4 Опоры и контроль: канал, файловая шкала → новая шкала (кэВ), невязка по целому и по дробному каналу");
        Console.WriteLine("  {0,-9} {1,9} {2,10} {3,6} {4,10} {5,8} {6,9} {7,9} {8,8}  {9}", "линия", "E табл", "канал", "σc", "E файл", "Δ файл", "E новая", "Δ целый", "Δ дробн", "статус");
        double maxAbsRes = 0;
        foreach (Fit f in fits)
        {
            double eFile = fileCal.ChannelToEnergy(f.C);
            double eNewInt = newCal.ChannelToEnergy(Math.Round(f.C));
            double eNewFrac = Poly(solved, f.C);
            string status = !f.Ok ? "НЕ ПРИНЯТА: " + f.Why : (f.L.Anchor ? "ОПОРА" : "контроль") + (f.L.Kind == Kind.Transfer ? " (перенос P·r)" : "");
            Console.WriteLine("  {0,-9} {1,9} {2,10} {3,6} {4,10} {5,8} {6,9} {7,9} {8,8}  {9}", f.L.Name, F(f.L.E, 2), F(f.C, 2), f.L.Kind == Kind.Transfer ? "—" : F(f.SigC, 2), F(eFile, 1), F(eFile - f.L.E, 1),
                F(eNewInt, 1), F(eNewInt - f.L.E, 2), F(eNewFrac - f.L.E, 2), status);
            if (f.Ok && f.L.Anchor) maxAbsRes = Math.Max(maxAbsRes, Math.Abs(eNewInt - f.L.E));
        }
        // Порог два: до 1 кэВ — чисто; 1…2 кэВ — принято с оговоркой (0.02…0.05 ПШПВ этого прибора, меньше цены
        // клика Amber по пику в окне калибровки — ±1…3 кэВ, см. контроль Cs-137·P); больше 2 кэВ — записи не будет.
        bool refuse = maxAbsRes > 2.0 || !calOk;
        Console.WriteLine("  наибольшая |невязка| по опорам: " + F(maxAbsRes, 2) + " кэВ " + (maxAbsRes <= 1.0 ? "(≤ 1 — чисто)" : maxAbsRes <= 2.0 ? "⚠ 1…2 кэВ — принято с оговоркой (≤ 0.05 ПШПВ; разбор — журнал П88 §2)" : "⛔ > 2 кэВ — шкала НЕ пишется (код 1)"));
        if (refuse) worst = Math.Max(worst, 1);
        Console.WriteLine();

        // ── §5 встроенные фоны копий ──
        if (checkCopies && copies.Count > 0)
        {
            Console.WriteLine("§5 Встроенные фоны копий: те же пики, центроиды против файла фона (сдвиг > 0.5 кан — другое усиление или другой снимок)");
            foreach (string p in copies)
            {
                EnergySpectrum ebg = loaded[p].ResultDataList[0].BackgroundEnergySpectrum;
                if (ebg == null) { Console.WriteLine("  " + Path.GetFileName(p) + ": встроенного фона НЕТ"); continue; }
                PolynomialEnergyCalibration ecal = (PolynomialEnergyCalibration)ebg.EnergyCalibration;
                ecal.CheckCalibration(ebg.NumberOfChannels);
                Console.WriteLine("  " + Path.GetFileName(p) + ": фон " + ebg.TotalPulseCount.ToString(CultureInfo.InvariantCulture) + " отсчётов, живое " + F(ebg.LiveTime, 1) + " с, шкала "
                    + (SameArray(ecal.Coefficients, fileCal.Coefficients) ? "= файла фона побитово" : "ДРУГАЯ: " + string.Join(" ", ecal.Coefficients.Select(R).ToArray()))
                    + ", отсчёты " + (SameCounts(ebg, bg) ? "= файла фона" : "ДРУГИЕ (другой снимок)"));
                double[] tmp;
                List<Fit> ef = FitAll(ebg, fwhmCal, ecal, window, order, transfer, false, Lines, out tmp);
                if (ef == null) continue;
                StringBuilder sb = new StringBuilder("    ");
                for (int i = 0; i < ef.Count; i++)
                {
                    Fit f = ef[i], main = fits[i];
                    if (f.L.Kind != Kind.Peak) continue;
                    if (!f.Ok || !main.Ok) { sb.Append(f.L.Name + " —  "); continue; }
                    sb.Append(f.L.Name + " " + F(f.C, 1) + " (" + (f.C - main.C >= 0 ? "+" : "") + F(f.C - main.C, 1) + " кан; " + F(Poly(solved, f.C) - f.L.E, 1) + " кэВ новой шкалой)  ");
                }
                Console.WriteLine(sb.ToString());
            }
            Console.WriteLine();
        }

        if (!write || refuse)
        {
            Console.WriteLine((refuse ? "ОТКАЗ: шкала не принята — " : "сухой прогон: ") + "файлы не тронуты. Код " + I(worst));
            return worst;
        }

        // ── §6 копии до правки ──
        Console.WriteLine("§6 Копии до правки → " + backup);
        Directory.CreateDirectory(backup);
        string bgDir = Path.GetDirectoryName(Path.GetFullPath(bgPath));
        foreach (string p in all)
        {
            string full = Path.GetFullPath(p);
            string dir = Path.GetDirectoryName(full);
            string rel = dir.StartsWith(bgDir, StringComparison.OrdinalIgnoreCase) && dir.Length > bgDir.Length ? dir.Substring(bgDir.Length).TrimStart('\\', '/') : "";
            string dest = Path.Combine(backup, rel, Path.GetFileName(full));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllBytes(dest, original[p]);
            byte[] back = File.ReadAllBytes(dest);
            bool ok = FirstDiff(back, original[p]) < 0 && back.Length == original[p].Length;
            Console.WriteLine("  " + dest + " : " + I(back.Length) + " байт, sha256 " + Sha(back) + (ok ? " = исходник" : " ⛔ НЕ СОШЛАСЬ"));
            if (!ok) return 3;
        }
        Console.WriteLine();

        // ── §7 запись ──
        Console.WriteLine("§7 Запись: новая шкала в <EnergySpectrum> фона и в <BackgroundEnergySpectrum> копий (AtomicFileWriter + XmlWriter, как SaveDocument)");
        foreach (string p in all)
        {
            ResultDataFile rdf = loaded[p];
            ResultData rd = rdf.ResultDataList[0];
            EnergySpectrum target = p == bgPath ? rd.EnergySpectrum : rd.BackgroundEnergySpectrum;
            if (target == null) { Console.WriteLine("  " + Path.GetFileName(p) + ": встроенного фона НЕТ — пропуск"); continue; }
            byte[] before = original[p];
            byte[] basis = baseline[p];      // «прочитал → записал без правок» (§1): от него различие обязано лежать внутри блока
            int blockStart, blockEnd;
            if (!FindCalBlock(basis, p == bgPath ? "<EnergySpectrum>" : "<BackgroundEnergySpectrum>", out blockStart, out blockEnd))
            { Console.WriteLine("  " + Path.GetFileName(p) + ": целевой блок <EnergyCalibration> в эталоне не найден — запись отменена"); return 4; }
            target.EnergyCalibration = new PolynomialEnergyCalibration { PolynomialOrder = solved.Length - 1, Coefficients = (double[])solved.Clone() };
            try
            {
                AtomicFileWriter.Write(p, stream =>
                {
                    using (XmlWriter w = XmlWriter.Create(stream, AppXmlSettings))
                    {
                        new XmlSerializer(typeof(ResultDataFile)).Serialize(w, rdf);
                        w.Flush();
                    }
                });
            }
            catch (Exception ex) { Console.WriteLine("  ОТКАЗ записи " + p + ": " + ex.Message); return 3; }
            byte[] after = File.ReadAllBytes(p);
            int prefix = FirstDiff(basis, after);
            int suffix = CommonSuffix(basis, after);
            bool inside = prefix >= blockStart && (basis.Length - suffix) <= blockEnd && (after.Length - suffix) >= prefix;
            Console.WriteLine("  " + Path.GetFileName(p) + ": " + I(before.Length) + " → " + I(after.Length) + " байт; против эталона §1 (" + I(basis.Length) + " байт) различие с байта " + I(prefix) + " до " + I(basis.Length - suffix)
                + " (целевой блок " + I(blockStart) + "…" + I(blockEnd) + ") → " + (inside ? "ТОЛЬКО внутри блока калибровки" : "⛔ ВНЕ БЛОКА (код 4)"));
            Console.WriteLine("    sha256 до    " + Sha(before));
            Console.WriteLine("    sha256 после " + Sha(after));
            if (!inside) worst = Math.Max(worst, 4);
            ResultDataFile check = Deserialize(after);
            ResultDataFile was = Deserialize(before);
            ResultData crd = check.ResultDataList[0];
            ResultData wrd = was.ResultDataList[0];
            PolynomialEnergyCalibration got = (PolynomialEnergyCalibration)(p == bgPath ? crd.EnergySpectrum : crd.BackgroundEnergySpectrum).EnergyCalibration;
            Console.WriteLine("    перечитано: степень " + I(got.PolynomialOrder) + ", коэффициенты " + (SameArray(got.Coefficients, solved) ? "= решателя побитово" : "⛔ НЕ ТЕ: " + string.Join(" ", got.Coefficients.Select(R).ToArray())));
            if (!SameArray(got.Coefficients, solved)) worst = Math.Max(worst, 4);
            if (p != bgPath)
            {
                PolynomialEnergyCalibration probeCal = (PolynomialEnergyCalibration)crd.EnergySpectrum.EnergyCalibration;
                PolynomialEnergyCalibration probeCalBefore = (PolynomialEnergyCalibration)wrd.EnergySpectrum.EnergyCalibration;
                bool sameProbe = SameArray(probeCal.Coefficients, probeCalBefore.Coefficients) && probeCal.PolynomialOrder == probeCalBefore.PolynomialOrder;
                Console.WriteLine("    шкала САМОЙ пробы: " + (sameProbe ? "не изменилась (побитово)" : "⛔ ИЗМЕНИЛАСЬ"));
                if (!sameProbe) worst = Math.Max(worst, 4);
            }
            bool sameCounts = SameCounts(crd.EnergySpectrum, wrd.EnergySpectrum) && SameCounts(crd.BackgroundEnergySpectrum, wrd.BackgroundEnergySpectrum)
                && crd.EnergySpectrum.LiveTime == wrd.EnergySpectrum.LiveTime && crd.EnergySpectrum.MeasurementTime == wrd.EnergySpectrum.MeasurementTime;
            Console.WriteLine("    отсчёты и времена: " + (sameCounts ? "не изменились" : "⛔ ИЗМЕНИЛИСЬ"));
            if (!sameCounts) worst = Math.Max(worst, 4);
        }
        Console.WriteLine();
        Console.WriteLine("Код " + I(worst));
        return worst;
    }

    // ── режим --sample: шкала самой пробы по её пикам, каждая проба порознь ──
    static int RunSamples(List<string> samples, string backup, int order, double window, bool transfer, bool write)
    {
        Console.WriteLine("RecalibrateBackgroundProbe --sample (П88, шаг 2): шкала ПРОБЫ по её пикам (238/583/911/K-40/2614) и переносу низа из P");
        Console.WriteLine("  степень " + I(order) + ", окно ±" + F(window, 1) + " ПШПВ, низ " + (transfer ? "переносом из P" : "БЕЗ переноса") + ", режим " + (write ? "ЗАПИСЬ" : "сухой прогон"));
        Console.WriteLine();
        int code = 0;
        foreach (string p in samples)
        {
            int c = RunOneSample(p, backup, order, window, transfer, write);
            code = Math.Max(code, c);
            Console.WriteLine();
        }
        Console.WriteLine("Код " + I(code));
        return code;
    }

    static int RunOneSample(string p, string backup, int order, double window, bool transfer, bool write)
    {
        Console.WriteLine("=== " + p);
        byte[] bytes;
        ResultDataFile rdf;
        try { bytes = File.ReadAllBytes(p); rdf = Deserialize(bytes); }
        catch (Exception ex) { Console.WriteLine("  ОТКАЗ чтения: " + ex.Message); return 3; }
        // §1 формат
        byte[] again = Serialize(rdf);
        int diff = FirstDiff(bytes, again);
        bool same = diff < 0 && bytes.Length == again.Length;
        string verdict;
        if (same) verdict = "ПОБИТОВО";
        else
        {
            byte[] stripped = WithoutOneLine(again, "<ROIConfigReference />");
            if (stripped != null && stripped.Length == bytes.Length && FirstDiff(stripped, bytes) < 0)
                verdict = "ПОБИТОВО с поправкой: сериализатор ДОПИСЫВАЕТ строку <ROIConfigReference /> (+" + I(again.Length - bytes.Length) + " байт)";
            else { Console.WriteLine("  §1 формат: РАЗЛИЧИЕ с байта " + I(diff) + " (длины " + I(bytes.Length) + " / " + I(again.Length) + ") — переписывать нельзя (код 2)"); return 2; }
        }
        Console.WriteLine("  §1 формат: " + I(bytes.Length) + " байт, BOM " + (bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? "да" : "нет") + ", CRLF " + I(CountCrlf(bytes)) + ", sha256 " + Sha(bytes) + " → " + verdict);

        ResultData rd = rdf.ResultDataList[0];
        EnergySpectrum sp = rd.EnergySpectrum;
        PolynomialEnergyCalibration fileCal = (PolynomialEnergyCalibration)sp.EnergyCalibration;
        fileCal.CheckCalibration(sp.NumberOfChannels);
        FwhmCalibration fwhmCal = rd.FwhmCalibration;
        if (fwhmCal == null) { Console.WriteLine("  ОТКАЗ: у пробы нет калибровки ПШПВ — окна фита строить нечем"); return 3; }
        PolynomialEnergyCalibration bgCalBefore = rd.BackgroundEnergySpectrum != null ? (PolynomialEnergyCalibration)rd.BackgroundEnergySpectrum.EnergyCalibration : null;
        Console.WriteLine("  §2 проба: " + I(sp.NumberOfChannels) + " каналов, " + sp.TotalPulseCount.ToString(CultureInfo.InvariantCulture) + " отсчётов, живое " + F(sp.LiveTime, 1) + " с; файловая шкала P: " + string.Join(" ", fileCal.Coefficients.Select(R).ToArray())
            + (bgCalBefore != null ? "; встроенный фон: " + string.Join(" ", bgCalBefore.Coefficients.Select(R).ToArray()) : "; встроенного фона нет"));
        double[] newCoef;
        List<Fit> fits = FitAll(sp, fwhmCal, fileCal, window, order, transfer, true, SampleLines, out newCoef);
        if (fits == null) return 3;
        Console.WriteLine("  чувствительность центроидов ПИКОВ к окну (±0.8 / ±1.3 ПШПВ), кан и кэВ новой шкалы:");
        foreach (double w in new double[] { 0.8, 1.3 })
        {
            double[] tmp;
            List<Fit> alt = FitAll(sp, fwhmCal, fileCal, w, order, transfer, false, SampleLines, out tmp);
            if (alt == null) continue;
            StringBuilder sb = new StringBuilder("    ±" + F(w, 1) + ": ");
            for (int i = 0; i < alt.Count; i++)
            {
                Fit f = alt[i], main = fits[i];
                if (f.L.Kind != Kind.Peak || !f.Ok || !main.Ok) continue;
                sb.Append(f.L.Name + " " + F(f.L.E, 0) + " " + F(f.C - main.C, 1) + " кан (" + F(Poly(newCoef, f.C) - Poly(newCoef, main.C), 1) + " кэВ)  ");
            }
            Console.WriteLine(sb.ToString());
        }
        // §3 решатель
        List<CalibrationPoint> points = new List<CalibrationPoint>();
        foreach (Fit f in fits.Where(x => x.Ok && x.L.Anchor))
        {
            int ch = (int)Math.Round(f.C);
            points.Add(new CalibrationPoint(ch, (decimal)f.L.E, sp.Spectrum[ch]));
        }
        int usedOrder;
        double[] solved = CalibrationSolver.SolveGuarded(points, order, sp.NumberOfChannels, false, out usedOrder);
        if (solved == null) { Console.WriteLine("  ОТКАЗ решателя: SolveGuarded вернул null"); return 1; }
        double[] plain = CalibrationSolver.Solve(points, order);
        Console.WriteLine("  §3 опор " + I(points.Count) + " (" + string.Join(", ", fits.Where(x => x.Ok && x.L.Anchor).Select(x => x.L.Name + " " + F(x.L.E, 0) + "@" + I((int)Math.Round(x.C))).ToArray()) + "), степень запрошена " + I(order) + ", принята " + I(usedOrder) + (usedOrder != order ? " ⚠ ПОНИЖЕНА" : ""));
        Console.WriteLine("     коэффициенты: " + string.Join(" ", solved.Select(R).ToArray()) + (SameArray(plain, solved) ? " (Solve без сторожа — те же)" : " (Solve без сторожа ДРУГИЕ: " + string.Join(" ", plain.Select(R).ToArray()) + ")"));
        PolynomialEnergyCalibration newCal = new PolynomialEnergyCalibration { PolynomialOrder = solved.Length - 1, Coefficients = (double[])solved.Clone() };
        bool calOk = newCal.CheckCalibration(sp.NumberOfChannels);
        Console.WriteLine("     CheckCalibration: " + (calOk ? "годна" : "⛔ НЕ ГОДНА") + "; MSE " + F(CalibrationSolver.MSE(solved, points), 5) + "; E(0) = " + F(newCal.ChannelToEnergy(0), 1) + ", E(" + I(sp.NumberOfChannels - 1) + ") = " + F(newCal.ChannelToEnergy(sp.NumberOfChannels - 1), 1) + " кэВ");
        foreach (int alt in new int[] { 2, 4 })
        {
            if (alt == order || points.Count < alt + 1) continue;
            int u; double[] c = CalibrationSolver.SolveGuarded(points, alt, sp.NumberOfChannels, false, out u);
            if (c == null) continue;
            Console.WriteLine("     для сведения — степень " + I(alt) + " (принята " + I(u) + "): невязки " + string.Join(" ", points.Select(pt => F((double)pt.Energy - Poly(c, pt.Channel), 1)).ToArray()) + "; E(240) = " + F(Poly(c, 240), 1) + " — НЕ пишется");
        }
        // §4 таблица
        Console.WriteLine("  §4 {0,-9} {1,9} {2,10} {3,6} {4,10} {5,8} {6,9} {7,9} {8,8}  {9}", "линия", "E табл", "канал", "σc", "E файл", "Δ файл", "E новая", "Δ целый", "Δ дробн", "статус");
        double maxAbsRes = 0;
        foreach (Fit f in fits)
        {
            double eFile = fileCal.ChannelToEnergy(f.C);
            double eNewInt = newCal.ChannelToEnergy(Math.Round(f.C));
            double eNewFrac = Poly(solved, f.C);
            string status = !f.Ok ? "НЕ ПРИНЯТА: " + f.Why : (f.L.Anchor ? "ОПОРА" : "контроль") + (f.L.Kind == Kind.Transfer ? " (перенос P·r)" : "");
            Console.WriteLine("     {0,-9} {1,9} {2,10} {3,6} {4,10} {5,8} {6,9} {7,9} {8,8}  {9}", f.L.Name, F(f.L.E, 2), F(f.C, 2), f.L.Kind == Kind.Transfer ? "—" : F(f.SigC, 2), F(eFile, 1), F(eFile - f.L.E, 1), F(eNewInt, 1), F(eNewInt - f.L.E, 2), F(eNewFrac - f.L.E, 2), status);
            if (f.Ok && f.L.Anchor) maxAbsRes = Math.Max(maxAbsRes, Math.Abs(eNewInt - f.L.E));
        }
        bool refuse = maxAbsRes > 2.0 || !calOk;
        Console.WriteLine("     наибольшая |невязка| по опорам: " + F(maxAbsRes, 2) + " кэВ " + (maxAbsRes <= 1.0 ? "(≤ 1 — чисто)" : maxAbsRes <= 2.0 ? "⚠ 1…2 кэВ — принято с оговоркой (≤ 0.05 ПШПВ)" : "⛔ > 2 кэВ — шкала НЕ пишется (код 1)"));
        if (refuse) return 1;
        if (!write) { Console.WriteLine("  сухой прогон: файл не тронут"); return 0; }
        // §6 копия до правки
        Directory.CreateDirectory(backup);
        string dest = Path.Combine(backup, Path.GetFileName(p));
        if (File.Exists(dest)) { Console.WriteLine("  ⛔ копия " + dest + " уже есть — прежние копии не затираются, дайте другой --backup="); return 3; }
        File.WriteAllBytes(dest, bytes);
        byte[] back = File.ReadAllBytes(dest);
        bool okb = FirstDiff(back, bytes) < 0 && back.Length == bytes.Length;
        Console.WriteLine("  §6 копия до правки: " + dest + " (" + I(back.Length) + " байт, sha256 " + Sha(back) + (okb ? " = исходник)" : " ⛔ НЕ СОШЛАСЬ)"));
        if (!okb) return 3;
        // §7 запись
        int blockStart, blockEnd;
        if (!FindCalBlock(again, "<EnergySpectrum>", out blockStart, out blockEnd)) { Console.WriteLine("  целевой блок не найден — запись отменена"); return 4; }
        sp.EnergyCalibration = new PolynomialEnergyCalibration { PolynomialOrder = solved.Length - 1, Coefficients = (double[])solved.Clone() };
        try
        {
            AtomicFileWriter.Write(p, stream =>
            {
                using (XmlWriter w = XmlWriter.Create(stream, AppXmlSettings))
                {
                    new XmlSerializer(typeof(ResultDataFile)).Serialize(w, rdf);
                    w.Flush();
                }
            });
        }
        catch (Exception ex) { Console.WriteLine("  ОТКАЗ записи: " + ex.Message); return 3; }
        byte[] after = File.ReadAllBytes(p);
        int prefix = FirstDiff(again, after);
        int suffix = CommonSuffix(again, after);
        bool inside = prefix >= blockStart && (again.Length - suffix) <= blockEnd && (after.Length - suffix) >= prefix;
        int code = 0;
        Console.WriteLine("  §7 запись: " + I(bytes.Length) + " → " + I(after.Length) + " байт; против эталона §1 (" + I(again.Length) + ") различие с байта " + I(prefix) + " до " + I(again.Length - suffix) + " (целевой блок " + I(blockStart) + "…" + I(blockEnd) + ") → " + (inside ? "ТОЛЬКО внутри блока калибровки пробы" : "⛔ ВНЕ БЛОКА (код 4)"));
        Console.WriteLine("     sha256 до    " + Sha(bytes));
        Console.WriteLine("     sha256 после " + Sha(after));
        if (!inside) code = 4;
        ResultData crd = Deserialize(after).ResultDataList[0];
        ResultData wrd = Deserialize(bytes).ResultDataList[0];
        PolynomialEnergyCalibration got = (PolynomialEnergyCalibration)crd.EnergySpectrum.EnergyCalibration;
        Console.WriteLine("     перечитано: степень " + I(got.PolynomialOrder) + ", коэффициенты " + (SameArray(got.Coefficients, solved) ? "= решателя побитово" : "⛔ НЕ ТЕ"));
        if (!SameArray(got.Coefficients, solved)) code = 4;
        bool bgSame = (crd.BackgroundEnergySpectrum == null && wrd.BackgroundEnergySpectrum == null) ||
            (crd.BackgroundEnergySpectrum != null && wrd.BackgroundEnergySpectrum != null
             && SameArray(((PolynomialEnergyCalibration)crd.BackgroundEnergySpectrum.EnergyCalibration).Coefficients, ((PolynomialEnergyCalibration)wrd.BackgroundEnergySpectrum.EnergyCalibration).Coefficients)
             && SameCounts(crd.BackgroundEnergySpectrum, wrd.BackgroundEnergySpectrum) && crd.BackgroundEnergySpectrum.LiveTime == wrd.BackgroundEnergySpectrum.LiveTime);
        bool fwhmSame = crd.FwhmCalibration != null && wrd.FwhmCalibration != null && SameArray(crd.FwhmCalibration.Coefficients, wrd.FwhmCalibration.Coefficients)
            && crd.FwhmCalibration.CalibrationPeaks.Count == wrd.FwhmCalibration.CalibrationPeaks.Count;
        bool cntSame = SameCounts(crd.EnergySpectrum, wrd.EnergySpectrum) && crd.EnergySpectrum.LiveTime == wrd.EnergySpectrum.LiveTime && crd.EnergySpectrum.MeasurementTime == wrd.EnergySpectrum.MeasurementTime;
        Console.WriteLine("     встроенный фон (шкала, отсчёты, живое): " + (bgSame ? "не изменился" : "⛔ ИЗМЕНИЛСЯ") + "; калибровка ПШПВ: " + (fwhmSame ? "не изменилась" : "⛔ ИЗМЕНИЛАСЬ") + "; отсчёты и времена пробы: " + (cntSame ? "не изменились" : "⛔ ИЗМЕНИЛИСЬ"));
        if (!bgSame || !fwhmSame || !cntSame) code = 4;
        return code;
    }

    // отношение усилений фон/проба по 2614: канал пика 2614 в фоне / канал 2614 по P
    static double RatioOf(List<Fit> fits, PolynomialEnergyCalibration fileCal, int n)
    {
        Fit t = fits.FirstOrDefault(f => f.L.Kind == Kind.Peak && f.L.E == E2614);
        if (t == null || !t.Ok) return 1.0;
        return t.C / fileCal.EnergyToChannel(E2614, n);
    }

    // ── подгонка всех групп с итерацией шкалы ──
    static List<Fit> FitAll(EnergySpectrum sp, FwhmCalibration fwhmCal, PolynomialEnergyCalibration fileCal, double window, int order, bool transfer, bool verbose, Line[] lines, out double[] coef)
    {
        int n = sp.NumberOfChannels;
        double[] y = new double[n];
        for (int i = 0; i < n; i++) y[i] = sp.Spectrum[i];
        int guard = 100;   // последние каналы — переполнение АЦП (у AS80 канал 8191 держит 148 804 отсчёта)

        // сид: K-40 и Tl-208 2614 — максимумы сглаженного спектра в грубых областях ФАЙЛОВОЙ шкалы
        double[] sm = Boxcar(y, 21);
        int cK = ArgMaxBetween(sm, ChannelOf(fileCal, 1150.0, n), ChannelOf(fileCal, 1800.0, n), n - guard);
        int cT = ArgMaxBetween(sm, ChannelOf(fileCal, 2300.0, n), Math.Min(ChannelOf(fileCal, 2950.0, n), n - guard), n - guard);
        if (cK <= 0 || cT <= cK) { Console.WriteLine("  ОТКАЗ сида: K-40 " + I(cK) + ", 2614 " + I(cT)); coef = null; return null; }
        double g = (E2614 - 1460.82) / (cT - cK);
        coef = new double[] { 1460.82 - g * cK, g };
        if (verbose)
            Console.WriteLine("  сид: K-40 у канала " + I(cK) + " (по файловой шкале " + F(fileCal.ChannelToEnergy(cK), 1) + " кэВ), 2614 у " + I(cT) + " (" + F(fileCal.ChannelToEnergy(cT), 1) + " кэВ); линейный сид E = " + F(coef[0], 3) + " + " + F(coef[1], 6) + "·ch");

        List<Fit> fits = null;
        double ratio = 1.0;
        for (int pass = 0; pass < 3; pass++)
        {
            fits = new List<Fit>();
            Dictionary<Line, Fit> peakFits = new Dictionary<Line, Fit>();
            foreach (Line L in lines.Where(l => l.Kind == Kind.Peak))
            {
                double c0 = ChannelOfPoly(coef, L.E, n);
                double gl = (Poly(coef, c0 + 1) - Poly(coef, c0 - 1)) / 2.0;
                double fw = fwhmCal.ChannelToFwhm(c0);
                double[] d = L.Sat.Select(es => (es - L.E) / gl).ToArray();
                double lo = c0, hi = c0;
                foreach (double dj in d) { lo = Math.Min(lo, c0 + dj); hi = Math.Max(hi, c0 + dj); }
                int iLo = (int)Math.Max(0, Math.Floor(lo - window * fw));
                int iHi = (int)Math.Min(n - guard - 1, Math.Ceiling(hi + window * fw));
                Fit f = FitGroup(y, iLo, iHi, c0, fw, d);
                f.L = L; f.Seed = c0; f.FwhmCal = fw;
                f.Ok = true; f.Why = "";
                if (f.A <= 0 || f.SigA <= 0 || f.A / f.SigA < 5.0) { f.Ok = false; f.Why = "пик не значим (A/σA = " + F(f.SigA > 0 ? f.A / f.SigA : 0, 1) + ")"; }
                else if (f.SigC > 0.1 * fw) { f.Ok = false; f.Why = "центроид не определён (σc = " + F(f.SigC, 1) + " кан при ПШПВ " + F(fw, 1) + ")"; }
                else if (Math.Abs(f.C - c0) > 0.55 * fw) { f.Ok = false; f.Why = "упёрся в границу окна"; }
                else if (f.Fwhm / fw < 0.6 || f.Fwhm / fw > 1.5) { f.Ok = false; f.Why = "ширина не пика (ПШПВ/калибр. = " + F(f.Fwhm / fw, 2) + ")"; }
                peakFits[L] = f;
            }
            Fit t2614 = peakFits[lines.First(l => l.Kind == Kind.Peak && l.E == E2614)];
            if (!t2614.Ok) { Console.WriteLine("  ОТКАЗ: пик 2614 не принят — переносить низ нечем"); coef = null; return null; }
            ratio = t2614.C / fileCal.EnergyToChannel(E2614, n);
            // Карта каналов «P → эта съёмка»: у фона — одно усиление по 2614 (своего 238 у фона нет); у ПРОБЫ — линейная
            // по двум её собственным пикам, 238 и 2614 (там P верна): усиление И ноль. Между 08.09 (P) и 14–17.09 ушёл
            // не только коэффициент, но и ноль — чистое усиление ставило собственный 238 пробы на −2…−2.5 кэВ.
            double mapA = 0.0, mapB = ratio;
            Line l238 = lines.FirstOrDefault(l => l.Kind == Kind.Peak && l.E == 238.632);
            if (l238 != null && peakFits[l238].Ok)
            {
                double x1 = fileCal.EnergyToChannel(238.632, n), y1 = peakFits[l238].C;
                double x2 = fileCal.EnergyToChannel(E2614, n), y2 = t2614.C;
                mapB = (y2 - y1) / (x2 - x1); mapA = y1 - mapB * x1;
            }
            transferMapA = mapA; transferMapB = mapB;
            foreach (Line L in lines)
            {
                if (L.Kind == Kind.Peak) { fits.Add(peakFits[L]); continue; }
                double ct = mapA + mapB * fileCal.EnergyToChannel(L.E, n);
                Fit f = new Fit { L = L, C = ct, SigC = 0, Fwhm = 0, FwhmCal = fwhmCal.ChannelToFwhm(ct), A = 0, SigA = 0, Chi2 = 0, SatA = new double[0], Lo = 0, Hi = 0 };
                f.Ok = transfer; f.Why = transfer ? "" : "перенос выключен (--no-transfer)";
                fits.Add(f);
            }
            List<Fit> anchors = fits.Where(x => x.Ok && x.L.Anchor).ToList();
            if (anchors.Count < order + 1) { Console.WriteLine("  ОТКАЗ: принятых опор " + I(anchors.Count) + " < " + I(order + 1)); coef = null; return null; }
            coef = PolyFit(anchors.Select(x => x.C).ToArray(), anchors.Select(x => x.L.E).ToArray(), order);
        }
        if (verbose)
        {
            Console.WriteLine("  отношение усилений съёмка/P по 2614: " + F(ratio, 5) + " (пик 2614 здесь " + F(fits.First(f => f.L.Kind == Kind.Peak && f.L.E == E2614).C, 1) + ", по P — " + F(fileCal.EnergyToChannel(E2614, n), 1) + ")"
                + (transferMapA != 0.0 ? "; карта переноса по 238 и 2614: канал = " + F(transferMapA, 2) + " + " + F(transferMapB, 5) + "·P⁻¹(E)" : "; перенос одним усилением"));
            Console.WriteLine("  {0,-9} {1,9} {2,12} {3,10} {4,7} {5,12} {6,9} {7,8} {8,7}  {9}", "линия", "E табл", "окно", "канал", "σc", "ПШПВ/калибр", "A", "A/σA", "χ²/ndf", "спутники A");
            foreach (Fit f in fits)
            {
                if (f.L.Kind == Kind.Transfer)
                    Console.WriteLine("  {0,-9} {1,9} {2,12} {3,10} {4,7} {5,12} {6,9} {7,8} {8,7}  {9}", f.L.Name, F(f.L.E, 2), "перенос P·r", F(f.C, 2), "—", "—", "—", "—", "—", f.Ok ? "" : "← " + f.Why);
                else
                    Console.WriteLine("  {0,-9} {1,9} {2,12} {3,10} {4,7} {5,12} {6,9} {7,8} {8,7}  {9}{10}", f.L.Name, F(f.L.E, 2), I(f.Lo) + "–" + I(f.Hi), F(f.C, 2), F(f.SigC, 2),
                        F(f.Fwhm, 1) + "/" + F(f.FwhmCal, 1), F(f.A, 0), F(f.SigA > 0 ? f.A / f.SigA : 0, 1), F(f.Chi2, 2), string.Join(" ", f.SatA.Select(v => F(v, 0)).ToArray()), f.Ok ? "" : "  ← " + f.Why);
            }
            Console.WriteLine("  полином " + I(order) + " ст. по дробным каналам принятых опор: " + string.Join(" ", coef.Select(R).ToArray()));
        }
        return fits;
    }

    // ── гауссиана (+ спутники той же ширины на привязанных смещениях) над линейной подложкой, LM ──
    static Fit FitGroup(double[] y, int lo, int hi, double c0, double fw, double[] d)
    {
        int m = hi - lo + 1;
        double[] x = new double[m], yy = new double[m], w = new double[m];
        double ymin = double.MaxValue;
        for (int i = 0; i < m; i++) { x[i] = lo + i; yy[i] = y[lo + i]; w[i] = 1.0 / Math.Sqrt(Math.Max(yy[i], 1.0)); ymin = Math.Min(ymin, yy[i]); }
        double s0 = fw / 2.3548;
        int k = d.Length;
        int np = 5 + k;                       // c, s, b0, b1, A, A_j…
        double[] p = new double[np];
        int ic = (int)Math.Round(c0) - lo; ic = Math.Max(0, Math.Min(m - 1, ic));
        double A0 = Math.Max(yy[ic] - ymin, 1.0);
        p[0] = c0; p[1] = s0; p[2] = ymin; p[3] = 0.0; p[4] = A0;
        for (int j = 0; j < k; j++) p[5 + j] = 0.3 * A0;
        double[] lb = new double[np], ub = new double[np];
        lb[0] = c0 - 0.6 * fw; ub[0] = c0 + 0.6 * fw;
        lb[1] = 0.6 * s0; ub[1] = 1.6 * s0;
        lb[2] = double.NegativeInfinity; ub[2] = double.PositiveInfinity;
        lb[3] = double.NegativeInfinity; ub[3] = double.PositiveInfinity;
        for (int j = 4; j < np; j++) { lb[j] = 0.0; ub[j] = double.PositiveInfinity; }

        Func<double[], double[]> resid = q =>
        {
            double[] r = new double[m];
            for (int i = 0; i < m; i++)
            {
                double u = (x[i] - c0) / fw;
                double model = q[2] + q[3] * u + q[4] * Math.Exp(-0.5 * Sq((x[i] - q[0]) / q[1]));
                for (int j = 0; j < k; j++) model += q[5 + j] * Math.Exp(-0.5 * Sq((x[i] - q[0] - d[j]) / q[1]));
                r[i] = (model - yy[i]) * w[i];
            }
            return r;
        };
        double[] cov;
        double chi2 = Lm(resid, p, lb, ub, out cov);
        int ndf = Math.Max(m - np, 1);
        double scale = Math.Max(chi2 / ndf, 1.0);
        Fit f = new Fit();
        f.Lo = lo; f.Hi = hi; f.C = p[0]; f.SigC = Math.Sqrt(Math.Max(cov[0], 0) * scale); f.Fwhm = p[1] * 2.3548; f.A = p[4]; f.SigA = Math.Sqrt(Math.Max(cov[4], 0) * scale);
        f.Chi2 = chi2 / ndf; f.SatA = new double[k];
        for (int j = 0; j < k; j++) f.SatA[j] = p[5 + j];
        return f;
    }

    static double Sq(double v) { return v * v; }

    // Левенберг–Марквардт с численным якобианом и зажимом в границах; возвращает χ², cov — диагональ (JᵀJ)⁻¹
    static double Lm(Func<double[], double[]> resid, double[] p, double[] lb, double[] ub, out double[] covDiag)
    {
        int np = p.Length;
        double lambda = 1e-3;
        double[] r = resid(p);
        double chi2 = Dot(r, r);
        double[,] jtj = new double[np, np];
        double[] jtr = new double[np];
        for (int iter = 0; iter < 300; iter++)
        {
            double[][] J = Jacobian(resid, p, r, lb, ub);
            for (int a = 0; a < np; a++)
            {
                jtr[a] = 0;
                for (int i = 0; i < r.Length; i++) jtr[a] += J[a][i] * r[i];
                for (int b = 0; b < np; b++)
                {
                    double s = 0;
                    for (int i = 0; i < r.Length; i++) s += J[a][i] * J[b][i];
                    jtj[a, b] = s;
                }
            }
            bool improved = false;
            for (int tries = 0; tries < 30 && !improved; tries++)
            {
                double[,] aM = new double[np, np];
                for (int a = 0; a < np; a++) for (int b = 0; b < np; b++) aM[a, b] = jtj[a, b] + (a == b ? lambda * (jtj[a, a] + 1e-12) : 0);
                double[] step = SolveLinear(aM, jtr.Select(v => -v).ToArray());
                if (step == null) { lambda *= 10; continue; }
                double[] q = new double[np];
                for (int a = 0; a < np; a++) q[a] = Math.Min(ub[a], Math.Max(lb[a], p[a] + step[a]));
                double[] r2 = resid(q);
                double chi2b = Dot(r2, r2);
                if (chi2b < chi2)
                {
                    double rel = (chi2 - chi2b) / Math.Max(chi2, 1e-30);
                    Array.Copy(q, p, np); r = r2; chi2 = chi2b; lambda = Math.Max(lambda / 10, 1e-12); improved = true;
                    if (rel < 1e-9) iter = 300;
                }
                else lambda *= 10;
            }
            if (!improved) break;
        }
        double[][] Jf = Jacobian(resid, p, r, lb, ub);
        for (int a = 0; a < np; a++) for (int b = 0; b < np; b++) { double s = 0; for (int i = 0; i < r.Length; i++) s += Jf[a][i] * Jf[b][i]; jtj[a, b] = s; }
        double[,] inv = Invert(jtj);
        covDiag = new double[np];
        for (int a = 0; a < np; a++) covDiag[a] = inv == null ? double.NaN : inv[a, a];
        return chi2;
    }

    static double[][] Jacobian(Func<double[], double[]> resid, double[] p, double[] r0, double[] lb, double[] ub)
    {
        int np = p.Length;
        double[][] J = new double[np][];
        for (int a = 0; a < np; a++)
        {
            double h = 1e-6 * Math.Max(Math.Abs(p[a]), 1.0);
            double[] q = (double[])p.Clone();
            q[a] = p[a] + h;
            if (q[a] > ub[a]) { q[a] = p[a] - h; }
            double[] r1 = resid(q);
            double hh = q[a] - p[a];
            J[a] = new double[r0.Length];
            for (int i = 0; i < r0.Length; i++) J[a][i] = (r1[i] - r0[i]) / hh;
        }
        return J;
    }

    static double Dot(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    static double[] SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        double[,] m = (double[,])a.Clone(); double[] v = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col; double best = Math.Abs(m[col, col]);
            for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > best) { best = Math.Abs(m[r, col]); piv = r; }
            if (best < 1e-300) return null;
            if (piv != col) { for (int c = 0; c < n; c++) { double t = m[col, c]; m[col, c] = m[piv, c]; m[piv, c] = t; } double tv = v[col]; v[col] = v[piv]; v[piv] = tv; }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                if (f == 0) continue;
                for (int c = col; c < n; c++) m[r, c] -= f * m[col, c];
                v[r] -= f * v[col];
            }
        }
        double[] x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = v[r];
            for (int c = r + 1; c < n; c++) s -= m[r, c] * x[c];
            x[r] = s / m[r, r];
        }
        return x;
    }

    static double[,] Invert(double[,] a)
    {
        int n = a.GetLength(0);
        double[,] inv = new double[n, n];
        for (int col = 0; col < n; col++)
        {
            double[] e = new double[n]; e[col] = 1.0;
            double[] x = SolveLinear(a, e);
            if (x == null) return null;
            for (int r = 0; r < n; r++) inv[r, col] = x[r];
        }
        return inv;
    }

    // МНК-полином по дробным каналам (нормальные уравнения с центрированием — степень ≤ 4, точек единицы)
    static double[] PolyFit(double[] x, double[] y, int order)
    {
        int np = order + 1, n = x.Length;
        double xm = x.Average();
        double[,] ata = new double[np, np]; double[] aty = new double[np];
        for (int i = 0; i < n; i++)
        {
            double[] row = new double[np]; double t = 1.0;
            for (int j = 0; j < np; j++) { row[j] = t; t *= (x[i] - xm); }
            for (int a = 0; a < np; a++) { aty[a] += row[a] * y[i]; for (int b = 0; b < np; b++) ata[a, b] += row[a] * row[b]; }
        }
        double[] cc = SolveLinear(ata, aty);
        double[] outc = new double[np];
        for (int j = 0; j < np; j++)
            for (int i = 0; i <= j; i++)
                outc[i] += cc[j] * Binom(j, i) * Math.Pow(-xm, j - i);
        return outc;
    }
    static double Binom(int n, int k) { double r = 1; for (int i = 1; i <= k; i++) r = r * (n - k + i) / i; return r; }

    static double Poly(double[] c, double x) { double s = 0, t = 1; for (int i = 0; i < c.Length; i++) { s += c[i] * t; t *= x; } return s; }

    static double ChannelOfPoly(double[] c, double e, int n)
    {
        double lo = 0, hi = n - 1;
        if (Poly(c, lo) >= e) return lo;
        if (Poly(c, hi) <= e) return hi;
        for (int i = 0; i < 100; i++) { double mid = 0.5 * (lo + hi); if (Poly(c, mid) < e) lo = mid; else hi = mid; }
        return 0.5 * (lo + hi);
    }
    static int ChannelOf(PolynomialEnergyCalibration cal, double e, int n) { return (int)Math.Round(cal.EnergyToChannel(e, n)); }

    static double[] Boxcar(double[] y, int width)
    {
        int n = y.Length, h = width / 2;
        double[] out_ = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - h), b = Math.Min(n - 1, i + h); double s = 0;
            for (int j = a; j <= b; j++) s += y[j];
            out_[i] = s / (b - a + 1);
        }
        return out_;
    }
    static int ArgMaxBetween(double[] v, int lo, int hi, int cap)
    {
        lo = Math.Max(0, lo); hi = Math.Min(Math.Min(hi, cap), v.Length - 1);
        if (hi <= lo) return -1;
        int best = lo;
        for (int i = lo; i <= hi; i++) if (v[i] > v[best]) best = i;
        return best;
    }

    // ── файлы ──
    static ResultDataFile Deserialize(byte[] bytes)
    {
        using (MemoryStream ms = new MemoryStream(bytes))
            return (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(ms);
    }
    static byte[] Serialize(ResultDataFile rdf)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            using (XmlWriter w = XmlWriter.Create(ms, AppXmlSettings))
            {
                new XmlSerializer(typeof(ResultDataFile)).Serialize(w, rdf);
                w.Flush();
            }
            return ms.ToArray();
        }
    }
    static int FirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : n;
    }
    static int CommonSuffix(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length), k = 0;
        while (k < n && a[a.Length - 1 - k] == b[b.Length - 1 - k]) k++;
        return k;
    }
    // Если b = a со ВСТАВКОЙ одного куска (и только ей) — вернуть вставленный текст, иначе null
    static string InsertedOnly(byte[] a, byte[] b)
    {
        if (b.Length <= a.Length) return null;
        int prefix = FirstDiff(a, b);
        if (prefix < 0) return null;
        int suffix = CommonSuffix(a, b);
        if (prefix + suffix < a.Length) return null;
        int insLen = b.Length - a.Length;
        int start = a.Length - suffix;
        if (start < 0 || start + insLen > b.Length) return null;
        return Encoding.UTF8.GetString(b, start, insLen);
    }
    // Убрать из текста ОДНУ строку (отступ + element + CRLF), содержащую ровно этот элемент; нет такой — null
    static byte[] WithoutOneLine(byte[] a, string element)
    {
        string t = Encoding.UTF8.GetString(a);
        int i = t.IndexOf(element, StringComparison.Ordinal);
        if (i < 0) return null;
        int lineStart = t.LastIndexOf('\n', i);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = t.IndexOf('\n', i);
        if (lineEnd < 0) return null;
        lineEnd++;
        string line = t.Substring(lineStart, lineEnd - lineStart);
        if (line.Trim() != element) return null;
        return Encoding.UTF8.GetBytes(t.Substring(0, lineStart) + t.Substring(lineEnd));
    }
    static int CountCrlf(byte[] a) { int c = 0; for (int i = 1; i < a.Length; i++) if (a[i - 1] == 13 && a[i] == 10) c++; return c; }
    static string Sha(byte[] a) { using (SHA256 s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(a)).Replace("-", "").ToLowerInvariant(); }
    static bool SameArray(double[] a, double[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i])) return false;
        return true;
    }
    static bool SameCounts(EnergySpectrum a, EnergySpectrum b)
    {
        if (a == null || b == null) return a == b;
        if (a.NumberOfChannels != b.NumberOfChannels) return false;
        for (int i = 0; i < a.NumberOfChannels; i++) if (a.Spectrum[i] != b.Spectrum[i]) return false;
        return true;
    }
    // байтовые границы блока <EnergyCalibration>…</EnergyCalibration>, идущего первым после открывающего тега контейнера
    static bool FindCalBlock(byte[] bytes, string container, out int start, out int end)
    {
        start = end = -1;
        string t = Encoding.UTF8.GetString(bytes);     // BOM → U+FEFF первым символом; пересчёт в байты ниже его учитывает
        int i = t.IndexOf(container, StringComparison.Ordinal);
        if (i < 0) return false;
        int s = t.IndexOf("<EnergyCalibration", i, StringComparison.Ordinal);
        if (s < 0) return false;
        int e = t.IndexOf("</EnergyCalibration>", s, StringComparison.Ordinal);
        if (e < 0) return false;
        e += "</EnergyCalibration>".Length;
        start = Encoding.UTF8.GetByteCount(t.Substring(0, s));
        end = Encoding.UTF8.GetByteCount(t.Substring(0, e));
        return true;
    }
}
