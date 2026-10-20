// ═══════════════════════════════════════════════════════════════════════════
//  AMBER256 (2), 20.10.2026. ФОН С ДРУГИМ ЧИСЛОМ КАНАЛОВ ПРИНИМАЕТСЯ ВЕЗДЕ.
// ═══════════════════════════════════════════════════════════════════════════
//
//  Решение Amber 20.10.2026 вопросником, дословно: «Принимать везде
//  (Рекомендую)». Фон с другим числом каналов, чем у спектра, перекладывается
//  по энергии в шкалу спектра (`DocumentManager.AlignBackgroundChannels` →
//  `SpectrumAriphmetics.BackgroundInScaleOf`) в двух дверях:
//  `LoadBackgroundSpectrum` (новый спектр и документ, панель, ⟳, «Обновить фон»)
//  и `CheckDocument` (открытие файла и все импорты — фон, встроенный в файл).
//
//  ЧТО МЕРЯЕТСЯ (спектр 1024 канала по 1.5 кэВ; фон 2048 каналов по 0.75 кэВ —
//  та же шкала энергий, плоская подложка и линия 661.7 кэВ):
//
//    1. ДВЕРЬ ЗАГРУЗКИ. Файл фона на диске — 2048 каналов (положительный
//       контроль: проба видит, что перекладывать есть что); после
//       `LoadBackgroundSpectrum` у документа фон 1024 канала, калибровка —
//       спектра, интеграл тот же до отсчёта, времена прежние, линия на месте
//       по энергии.
//    2. ЧИТАТЕЛИ НЕ ЗАМЕТИЛИ. Вычитание (`Substract` — его же видят график и
//       поиск пиков) с переложенным фоном отличается от вычитания с исходным не
//       больше чем на отсчёт в канале: оба кладут фон по энергии.
//    3. ДВЕРЬ ОТКРЫТИЯ. Встроенный в файл фон 2048 каналов после `CheckDocument`
//       — 1024; фон с ТЕМ ЖЕ числом каналов и другой калибровкой не трогается
//       вовсе (тот же объект) — так у корпуса числа побитово прежние.
//    4. FSA. Настоящий `FsaAnalyzer` берёт переложенный фон (причины отказа
//       нет, фон вычтен); на исходном фоне 2048 каналов — по-прежнему «Фон не
//       взят» (положительный контроль: сторож разбора на месте и проба его
//       видит).
//
//    BackgroundChannelsProbeA256.exe --dir=<временный каталог>
//
//  Ожидание: «ВСЕ СОШЛИСЬ», код 0.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;

static class BackgroundChannelsProbeA256
{
    static int bad;
    static int checks;

    const double LineKev = 661.7;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        FsaTuningReport.Snapshot();

        string dir = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (dir == null)
        {
            Console.Error.WriteLine("нужен --dir=<временный каталог>");
            return 2;
        }

        Directory.CreateDirectory(dir);
        GlobalConfigManager.GetInstance();
        Console.WriteLine("сборка приложения: " + typeof(DocumentManager).Assembly.Location);

        EnergySpectrum spectrum = Make(1024, 1.5, 100, 0);
        EnergySpectrum raw = Make(2048, 0.75, 10, 20000);
        string bgPath = Path.Combine(dir, "bg2048.xml");
        Write(bgPath, raw);

        // 1. ДВЕРЬ ЗАГРУЗКИ
        Console.WriteLine();
        Console.WriteLine("=== 1. LoadBackgroundSpectrum: фон 2048 каналов к спектру 1024 ===");
        MethodInfo read = typeof(DocumentManager).GetMethod("ReadResultDataFile", BindingFlags.NonPublic | BindingFlags.Static);
        ResultDataFile onDisk = (ResultDataFile)read.Invoke(null, new object[] { bgPath });
        Same("в файле фона 2048 каналов (положительный контроль)", 2048,
             onDisk.ResultDataList[0].EnergySpectrum.NumberOfChannels);

        var rd = new ResultData();
        rd.EnergySpectrum = spectrum.Clone();
        rd.BackgroundSpectrumFile = Path.GetFileName(bgPath);
        rd.BackgroundSpectrumPathname = bgPath;
        DocumentManager.GetInstance().LoadBackgroundSpectrum(rd);
        EnergySpectrum got = rd.BackgroundEnergySpectrum;
        Same("фон загружен", true, got != null);
        if (got == null)
        {
            return Finish();
        }

        Same("каналов у фона — как у спектра", 1024, got.NumberOfChannels);
        Same("длина массива фона — как у спектра", 1024, got.Spectrum.Length);
        Same("калибровка фона — калибровка спектра", true, got.EnergyCalibration.Equals(spectrum.EnergyCalibration));
        // Интеграл хранится на ПЕРЕКРЫТИИ шкал (`RebinByEnergy`): верхняя половина
        // последнего канала фона (1535.25…1535.63 кэВ) лежит выше шкалы спектра.
        long overlap = (long)Math.Round(Sum(SpectrumAriphmetics.RebinByEnergy(raw.Spectrum,
            raw.EnergyCalibration, spectrum.EnergyCalibration, spectrum.NumberOfChannels)));
        Console.WriteLine("  Σ фона: в файле {0}, на перекрытии шкал {1}, у документа {2}", Sum(raw), overlap, Sum(got));
        Same("интеграл фона на перекрытии шкал тот же (отсчётов)", overlap, Sum(got));
        Same("за шкалой спектра — меньше канала фона", true, Sum(raw) - overlap <= 10);
        Same("TotalPulseCount = Σ каналов", Sum(got), got.TotalPulseCount);
        Same("полное время фона прежнее", raw.MeasurementTime, got.MeasurementTime);
        Same("живое время фона прежнее", raw.LiveTime, got.LiveTime);
        double c0 = Centroid(raw, 600.0, 720.0), c1 = Centroid(got, 600.0, 720.0);
        Console.WriteLine("  центр линии: в файле {0:F2} кэВ, у документа {1:F2} кэВ", c0, c1);
        Same("линия на месте по энергии (±0.5 кэВ)", true, Math.Abs(c1 - c0) <= 0.5);

        // 2. ЧИТАТЕЛИ
        Console.WriteLine();
        Console.WriteLine("=== 2. вычитание: переложенный фон против исходного ===");
        spectrum.LiveTime = 1000;
        EnergySpectrum minusRaw = new SpectrumAriphmetics(spectrum).Substract(raw);
        EnergySpectrum minusGot = new SpectrumAriphmetics(spectrum).Substract(got);
        int worst = 0;
        for (int i = 0; i < minusRaw.Spectrum.Length; i++) worst = Math.Max(worst, Math.Abs(minusRaw.Spectrum[i] - minusGot.Spectrum[i]));
        Console.WriteLine("  худшее расхождение канала: {0} отсчётов", worst);
        Same("вычитание то же до отсчёта", true, worst <= 1);

        // 3. ДВЕРЬ ОТКРЫТИЯ
        Console.WriteLine();
        Console.WriteLine("=== 3. CheckDocument: фон, встроенный в файл ===");
        MethodInfo check = typeof(DocumentManager).GetMethod("CheckDocument", BindingFlags.NonPublic | BindingFlags.Instance);
        var file = new ResultDataFile();
        var embedded = new ResultData();
        embedded.EnergySpectrum = spectrum.Clone();
        embedded.BackgroundEnergySpectrum = raw.Clone();
        file.ResultDataList.Add(embedded);
        Same("до проверки у встроенного фона 2048 каналов", 2048, embedded.BackgroundEnergySpectrum.NumberOfChannels);
        bool ok = (bool)check.Invoke(DocumentManager.GetInstance(), new object[] { file, false });
        Same("проверка прошла", true, ok);
        Same("после проверки — 1024 канала", 1024, embedded.BackgroundEnergySpectrum.NumberOfChannels);
        Same("интеграл встроенного фона на перекрытии тот же", overlap, Sum(embedded.BackgroundEnergySpectrum));

        var fileSame = new ResultDataFile();
        var sameCount = new ResultData();
        sameCount.EnergySpectrum = spectrum.Clone();
        EnergySpectrum other = Make(1024, 1.49, 10, 20000);
        sameCount.BackgroundEnergySpectrum = other;
        fileSame.ResultDataList.Add(sameCount);
        check.Invoke(DocumentManager.GetInstance(), new object[] { fileSame, false });
        Same("фон с тем же числом каналов (другая калибровка) — тот же объект", true,
             ReferenceEquals(other, sameCount.BackgroundEnergySpectrum));

        // 4. FSA
        Console.WriteLine();
        Console.WriteLine("=== 4. FsaAnalyzer: переложенный фон и исходный ===");
        FsaResult aligned = Analyze(spectrum, got);
        FsaResult refused = Analyze(spectrum, raw);
        Same("разбор с переложенным фоном состоялся", true, aligned != null);
        Same("разбор с исходным фоном состоялся", true, refused != null);
        if (aligned != null)
        {
            Console.WriteLine("  переложенный: причина = {0}, вычтен = {1}", aligned.BackgroundRejected ?? "(нет)", aligned.BackgroundUsed);
            Same("переложенный фон: причины отказа нет", null, aligned.BackgroundRejected);
            Same("переложенный фон вычтен", true, aligned.BackgroundUsed);
        }

        if (refused != null)
        {
            string want = string.Format(CultureInfo.InvariantCulture, Resources.FSABackgroundChannelMismatch, 2048, 1024);
            Console.WriteLine("  исходный: причина = «{0}», вычтен = {1}", refused.BackgroundRejected, refused.BackgroundUsed);
            Same("исходный фон 2048: «фон не взят» (положительный контроль)", want, refused.BackgroundRejected);
        }

        return Finish();
    }

    static int Finish()
    {
        Console.WriteLine();
        Console.WriteLine("проверок: " + checks.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    static FsaResult Analyze(EnergySpectrum spectrum, EnergySpectrum background)
    {
        var fwhm = new SimpleSqrtFwhmCalibration();
        fwhm.Coefficients = new double[] { 4.0, 0.5 };
        var component = new FsaComponent("Cs-137", FsaComponentKind.Single);
        component.Lines.Add(new FsaLine("Cs-137", LineKev, 85.1));
        component.TotalYieldPercent = 85.1;
        var analyzer = new FsaAnalyzer();
        analyzer.RequireGeometry = false;
        FsaTuningReport.Print(analyzer);
        try
        {
            FsaResult result = analyzer.Analyze(spectrum, background, fwhm, new List<FsaComponent> { component }, null);
            if (result == null)
            {
                Console.WriteLine("  разбор отказал: {0}", analyzer.Refusal);
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  разбор бросил {0}: {1}", ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // Плоская подложка `flat` в канале и линия LineKev (σ 15 кэВ) площадью `line`.
    static EnergySpectrum Make(int channels, double kevPerChannel, int flat, int line)
    {
        var spectrum = new EnergySpectrum();
        spectrum.NumberOfChannels = channels;
        int[] counts = new int[channels];
        for (int i = 0; i < channels; i++)
        {
            double e = kevPerChannel * i;
            double g = Math.Exp(-0.5 * Math.Pow((e - LineKev) / 15.0, 2)) / (15.0 * Math.Sqrt(2.0 * Math.PI));
            counts[i] = flat + (int)Math.Round(line * g * kevPerChannel);
        }

        spectrum.Spectrum = counts;
        spectrum.MeasurementTime = 1000;
        spectrum.LiveTime = 1000;
        spectrum.TotalPulseCount = Sum(spectrum);
        spectrum.ValidPulseCount = spectrum.TotalPulseCount;
        var calibration = new PolynomialEnergyCalibration();
        calibration.PolynomialOrder = 1;
        calibration.Coefficients = new double[] { 0.0, kevPerChannel };
        spectrum.EnergyCalibration = calibration;
        return spectrum;
    }

    static void Write(string path, EnergySpectrum background)
    {
        var file = new ResultDataFile();
        var data = new ResultData();
        data.EnergySpectrum = background;
        file.ResultDataList.Add(data);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            new XmlSerializer(typeof(ResultDataFile)).Serialize(stream, file);
        }
    }

    static long Sum(EnergySpectrum s)
    {
        long t = 0;
        foreach (int v in s.Spectrum) t += v;
        return t;
    }

    static double Sum(double[] values)
    {
        double t = 0.0;
        foreach (double v in values) t += v;
        return t;
    }

    static double Centroid(EnergySpectrum s, double fromKev, double toKev)
    {
        double w = 0.0, we = 0.0;
        for (int i = 0; i < s.Spectrum.Length; i++)
        {
            double e = s.EnergyCalibration.ChannelToEnergy(i);
            if (e < fromKev || e > toKev) continue;
            double v = s.Spectrum[i] - 10.0 * 2048.0 / s.Spectrum.Length;   // подложка: 10 на канал 0.75 кэВ
            w += v;
            we += v * e;
        }

        return w > 0.0 ? we / w : double.NaN;
    }

    static void Same<T>(string what, T want, T got)
    {
        checks++;
        bool ok = EqualityComparer<T>.Default.Equals(want, got);
        if (!ok) bad++;
        Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "✓" : "✗", what, want, got);
    }
}
