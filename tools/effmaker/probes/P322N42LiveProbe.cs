// Проба полосы П322 (15.10.2026): ЖИВОЕ ВРЕМЯ В ЭКСПОРТЕ N42 — ДО и ПОСЛЕ правки.
// Журнал — handover/handover-2026-10-15-p322-n42-live-and-limits.md. Решение Amber
// 15.10.2026 вопросником, дословно: «Живое или полное (Рекомендую)» — в файл идёт то, на
// что делит само приложение (EnergySpectrum.EffectiveLiveTime): живое, если задано, иначе
// полное. Прежде у спектра без живого времени в файле стояло «PT0S».
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут пути от
// каталога входной сборки). Проба зовёт приложение только тем, что есть И ДО, И ПОСЛЕ
// правки (DocumentManager.WriteN42File / ImportDocumentN42 / ImportDocumentSpecUtils /
// OpenDocument, сеанс FSA документа), поэтому один исходник собирается на обеих сборках:
// сборка ДО — положительный контроль (обязана показать «PT0S»).
//
//   P322N42LiveProbe.exe --mode=export [--tmp=<каталог>]
//       сочинённый документ: спектр живое 0 / полное 3600.25 с и фон живое 0 / полное
//       7200.5 с → что в файле (LiveTimeDuration переднего и фона), что читают обратно
//       дверь N42 и дверь SpecUtils, знаменатель (EffectiveLiveTime) до и после круга;
//       контроль — тот же документ с живым 3590.5 / 7180.25 с: в файле ровно они, отпечаток
//       файла (sha256 без n42DocUUID) сверяется между сборками.
//   P322N42LiveProbe.exe --mode=corpus --corpus=<каталог *.xml> [--tmp=<каталог>]
//       каждый спектр корпуса выгружается ДВАЖДЫ: как есть (у всех живое задано —
//       отпечаток обязан совпасть со сборкой ДО побайтно) и с обнулённым живым у всех
//       спектров и фонов (так выглядят 585 из 907 спектров Amber): у каждого RadMeasurement
//       LiveTimeDuration обязан равняться RealTimeDuration, а файл без значений
//       LiveTimeDuration — совпасть со сборкой ДО (правка трогает только это поле).
//   P322N42LiveProbe.exe --mode=fsa --spectrum=<xml> [--tmp=<каталог>]
//       круг экспорт → импорт → разбор: спектр корпуса с обнулённым живым (спектр и фон)
//       выгружается, импортируется дверью N42, получает прибор, кривую эффективности (с
//       геометрией — без неё FSA отказывает) и кривую ПШПВ исходного (N42 их не несёт),
//       ищутся пики и идёт разбор FSA (спектр нужен с кривой в файле: AS80_Th232Medal,
//       AS80_Cs137_0cm — те же, что у check_fsa_report_view). Печатаются числа разбора (формат R); их сверяют
//       между сборками ДО и ПОСЛЕ. Внутри сборки — плечо «живое 0»: тот же импортированный
//       спектр с LiveTime = 0 (то, что давал импорт до правки) обязан дать ТЕ ЖЕ числа.
//
// Коды: 0 — ожидания ПОСЛЕ правки сошлись; 4 — расхождения (на сборке ДО — ожидаемо в
// export/corpus); 5 — обвязка.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;

static class P322N42LiveProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;
    static string tmp;
    static int defects;
    static readonly StringWriter appErr = new StringWriter();

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string mode = Arg(args, "--mode=", "export");
        tmp = Arg(args, "--tmp=", Path.Combine(Path.GetTempPath(), "p322n42"));
        Directory.CreateDirectory(tmp);
        Console.WriteLine(ProbeTargetFramework.Describe());
        string exe = typeof(DocumentManager).Assembly.Location;
        Console.WriteLine("приложение: " + exe + "  (" + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
        TextWriter realErr = Console.Error;
        Console.SetError(appErr);
        int code;
        try
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            dm = DocumentManager.GetInstance();
            if (mode == "export") code = ExportMode();
            else if (mode == "corpus") code = CorpusMode(Arg(args, "--corpus=", null));
            else if (mode == "fsa") code = FsaMode(Arg(args, "--spectrum=", null));
            else { Console.WriteLine("неизвестный --mode: " + mode); code = 5; }
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ обвязка: " + ex);
            code = 5;
        }
        finally
        {
            Console.SetError(realErr);
        }
        string err = appErr.ToString().Trim();
        Console.WriteLine();
        Console.WriteLine("голоса приложения (поток ошибок): " + (err.Length == 0 ? "—" : Flat(err).Substring(0, Math.Min(600, Flat(err).Length))));
        return code;
    }

    // ================================================================== export

    static int ExportMode()
    {
        // ПЛЕЧО 1 — живое не задано.
        DocEnergySpectrum z = Synth(0.0, 0.0);
        string fz = Path.Combine(tmp, "live0.n42");
        dm.WriteN42File(z, fz);
        string tz = File.ReadAllText(fz);
        List<string[]> mz = Measurements(tz);
        Console.WriteLine("=== живое не задано: спектр живое 0 / полное 3600.25, фон живое 0 / полное 7200.5");
        foreach (string[] m in mz)
        {
            Console.WriteLine("  {0}: RealTimeDuration {1}, LiveTimeDuration {2}", m[0], m[1], m[2]);
        }
        Verdict("экспорт: живое переднего = полное (PT3600.25S)", Live(mz, "Foreground"), Live(mz, "Foreground") == "PT3600.25S");
        Verdict("экспорт: живое фона = полное (PT7200.5S)", Live(mz, "Background"), Live(mz, "Background") == "PT7200.5S");
        Verdict("экспорт: «PT0S» в файле нет", tz.Contains("PT0S") ? "есть" : "нет", !tz.Contains("PT0S"));

        EnergySpectrum src = z.ActiveResultData.EnergySpectrum;
        DocEnergySpectrum back = new DocEnergySpectrum();
        dm.ImportDocumentN42(back, fz);
        EnergySpectrum b = back.ActiveResultData.EnergySpectrum;
        EnergySpectrum bbg = back.ActiveResultData.BackgroundEnergySpectrum;
        Console.WriteLine(string.Format(Inv, "  дверь N42 читает: живое {0:R}, полное {1:R}, знаменатель {2:R} (у исходного {3:R}); фон: живое {4}, полное {5}, знаменатель {6}",
            b.LiveTime, b.MeasurementTime, b.EffectiveLiveTime, src.EffectiveLiveTime,
            bbg == null ? "-" : bbg.LiveTime.ToString("R", Inv), bbg == null ? "-" : bbg.MeasurementTime.ToString("R", Inv),
            bbg == null ? "-" : bbg.EffectiveLiveTime.ToString("R", Inv)));
        Verdict("круг N42: знаменатель переднего тот же, побитово", b.EffectiveLiveTime.ToString("R", Inv),
                b.EffectiveLiveTime == src.EffectiveLiveTime);
        Verdict("круг N42: знаменатель фона тот же, побитово", bbg == null ? "фона нет" : bbg.EffectiveLiveTime.ToString("R", Inv),
                bbg != null && bbg.EffectiveLiveTime == z.ActiveResultData.BackgroundEnergySpectrum.EffectiveLiveTime);
        Verdict("круг N42: поле «живое» = полное (новое; до правки было 0)", b.LiveTime.ToString("R", Inv), b.LiveTime == 3600.25);

        DocEnergySpectrum su = dm.CreateDocument();
        dm.ImportDocumentSpecUtils(su, fz, 3600);
        EnergySpectrum s = su.ActiveResultData.EnergySpectrum;
        Console.WriteLine(string.Format(Inv, "  дверь SpecUtils (библиотека InterSpec) читает: живое {0:R}, полное {1:R}", s.LiveTime, s.MeasurementTime));
        Verdict("SpecUtils читает живое = 3600.25, а не 0", s.LiveTime.ToString("R", Inv), s.LiveTime == 3600.25);

        // ПЛЕЧО 2 — контроль: живое задано, файл обязан быть прежним.
        DocEnergySpectrum l = Synth(3590.5, 7180.25);
        string fl = Path.Combine(tmp, "live_set.n42");
        dm.WriteN42File(l, fl);
        string tl = File.ReadAllText(fl);
        List<string[]> ml = Measurements(tl);
        Console.WriteLine("=== контроль: живое задано (3590.5 / 7180.25)");
        foreach (string[] m in ml)
        {
            Console.WriteLine("  {0}: RealTimeDuration {1}, LiveTimeDuration {2}", m[0], m[1], m[2]);
        }
        Verdict("контроль: живое переднего как есть", Live(ml, "Foreground"), Live(ml, "Foreground") == "PT3590.5S");
        Verdict("контроль: живое фона как есть", Live(ml, "Background"), Live(ml, "Background") == "PT7180.25S");
        Console.WriteLine("  КОНТРОЛЬ-ОТПЕЧАТОК файла с живым (sha256 без n42DocUUID; сверять со сборкой ДО): " + Sha(Mask(tl, false)));
        Console.WriteLine("  ОТПЕЧАТОК файла без живого, значения LiveTimeDuration скрыты (сверять со сборкой ДО): " + Sha(Mask(tz, true)));

        Console.WriteLine();
        Console.WriteLine("ИТОГ export: расхождений с ожиданием ПОСЛЕ правки " + defects.ToString(Inv));
        return defects == 0 ? 0 : 4;
    }

    static DocEnergySpectrum Synth(double live, double bgLive)
    {
        DocEnergySpectrum d = dm.CreateDocument();
        ResultData rd = d.ActiveResultData;
        rd.EnergySpectrum = Spectrum(1024, 3600.25, live, 322);
        rd.BackgroundEnergySpectrum = Spectrum(1024, 7200.5, bgLive, 323);
        DateTime start = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Local);
        rd.StartTime = start;
        rd.EndTime = start.AddSeconds(3600.25);
        rd.SampleInfo.Time = start;
        rd.SampleInfo.Name = "P322";
        return d;
    }

    static EnergySpectrum Spectrum(int channels, double full, double live, int seed)
    {
        var es = new EnergySpectrum(1.0, channels);
        var cal = new PolynomialEnergyCalibration();
        cal.PolynomialOrder = 2;
        cal.Coefficients = new double[] { -3.25, 2.9375, 1.25e-5 };
        es.EnergyCalibration = cal;
        var rnd = new Random(seed);
        long n = 0;
        for (int i = 0; i < channels; i++)
        {
            double v = 40000.0 * Math.Exp(-i / 180.0) + 90000.0 * Math.Exp(-0.5 * Math.Pow((i - 226) / 7.5, 2));
            es.Spectrum[i] = (int)Math.Round(v + rnd.NextDouble());
            n += es.Spectrum[i];
        }
        es.TotalPulseCount = n;
        es.ValidPulseCount = n;
        es.MeasurementTime = full;
        es.LiveTime = live;
        return es;
    }

    // ================================================================== corpus

    static int CorpusMode(string corpus)
    {
        if (corpus == null || !Directory.Exists(corpus))
        {
            Console.WriteLine("⛔ обвязка: нужен --corpus=<каталог *.xml>");
            return 5;
        }
        string[] files = Directory.GetFiles(corpus, "*.xml");
        Array.Sort(files, StringComparer.Ordinal);
        Console.WriteLine("=== корпус: " + Path.GetFullPath(corpus) + ", спектров " + files.Length.ToString(Inv));
        Console.WriteLine("ФАЙЛ\tкак есть: sha (без UUID)\tживое 0: sha (без UUID и значений живого)\tживое 0: измерений\tживое = полному\tPT0S");
        int opened = 0, measAll = 0, measOk = 0, pt0 = 0, refused = 0;
        string dir = Path.Combine(tmp, "corpus");
        Directory.CreateDirectory(dir);
        foreach (string f in files)
        {
            string name = Path.GetFileNameWithoutExtension(f);
            DocEnergySpectrum doc;
            try
            {
                doc = dm.OpenDocument(f);
            }
            catch (Exception ex)
            {
                Console.WriteLine(name + "\t⛔ не открылся: " + Flat(ex.Message));
                refused++;
                continue;
            }
            if (doc == null)
            {
                Console.WriteLine(name + "\t⛔ не открылся: null");
                refused++;
                continue;
            }
            opened++;
            string p1 = Path.Combine(dir, name + ".asis.n42");
            dm.WriteN42File(doc, p1);
            string t1 = File.ReadAllText(p1);
            foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
            {
                if (rd.EnergySpectrum != null) rd.EnergySpectrum.LiveTime = 0.0;
                if (rd.BackgroundEnergySpectrum != null) rd.BackgroundEnergySpectrum.LiveTime = 0.0;
            }
            string p2 = Path.Combine(dir, name + ".live0.n42");
            dm.WriteN42File(doc, p2);
            string t2 = File.ReadAllText(p2);
            List<string[]> ms = Measurements(t2);
            int ok = ms.Count(m => m[2] == m[1]);
            int zero = ms.Count(m => m[2] == "PT0S");
            measAll += ms.Count;
            measOk += ok;
            pt0 += zero;
            Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", name, Sha(Mask(t1, false)), Sha(Mask(t2, true)),
                              ms.Count.ToString(Inv), ok.ToString(Inv), zero.ToString(Inv));
            try { doc.Dispose(); } catch { }
            File.Delete(p1);
            File.Delete(p2);
        }
        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "ИТОГ corpus: открыто {0} из {1}, отказов {2}; у спектров с обнулённым живым измерений {3}, живое = полному у {4}, «PT0S» у {5}",
                          opened, files.Length, refused, measAll, measOk, pt0));
        if (refused > 0 || opened == 0) return 5;
        return measOk == measAll && pt0 == 0 ? 0 : 4;
    }

    // ================================================================== fsa

    static int FsaMode(string spectrum)
    {
        if (spectrum == null || !File.Exists(spectrum))
        {
            Console.WriteLine("⛔ обвязка: нужен --spectrum=<файл xml>");
            return 5;
        }
        Application.EnableVisualStyles();
        NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
        string name = Path.GetFileNameWithoutExtension(spectrum);
        DocEnergySpectrum doc = dm.OpenDocument(spectrum);
        ResultData src = doc.ActiveResultData;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "SETUP\t{0}: прибор {1}; каналов {2}; полное {3:R}, живое {4:R}; фон {5}",
                          name, ProbeDeviceConfig.Attach(src), src.EnergySpectrum.NumberOfChannels,
                          src.EnergySpectrum.MeasurementTime, src.EnergySpectrum.LiveTime,
                          src.BackgroundEnergySpectrum == null ? "нет" : "есть"));
        // Так выглядит спектр Amber без живого времени: живое 0 у спектра и у фона.
        foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
        {
            if (rd.EnergySpectrum != null) rd.EnergySpectrum.LiveTime = 0.0;
            if (rd.BackgroundEnergySpectrum != null) rd.BackgroundEnergySpectrum.LiveTime = 0.0;
        }
        double denomSrc = src.EnergySpectrum.EffectiveLiveTime;
        double denomSrcBg = src.BackgroundEnergySpectrum != null ? src.BackgroundEnergySpectrum.EffectiveLiveTime : double.NaN;

        string path = Path.Combine(tmp, name + ".live0.n42");
        dm.WriteN42File(doc, path);
        List<string[]> ms = Measurements(File.ReadAllText(path));
        foreach (string[] m in ms)
        {
            Console.WriteLine("  в файле {0}: RealTimeDuration {1}, LiveTimeDuration {2}", m[0], m[1], m[2]);
        }

        DocEnergySpectrum back = new DocEnergySpectrum();
        dm.ImportDocumentN42(back, path);
        ResultData rd2 = back.ActiveResultData;
        // Прибор и кривая — как у исходного: N42 их не несёт (П284), а сравнивается здесь
        // ТОЛЬКО то, что меняет правка, — поле живого времени.
        rd2.DeviceConfigReference = src.DeviceConfigReference;
        Console.WriteLine("SETUP\tимпорт: прибор {0}", ProbeDeviceConfig.Attach(rd2));
        // Кривая эффективности с геометрией — та, что лежит в исходном файле: без геометрии
        // разбор FSA отказывает («needs a detector geometry»), а N42 её не несёт.
        rd2.Efficiency = src.Efficiency;
        if (src.FwhmCalibration != null)
        {
            rd2.FwhmCalibration = src.FwhmCalibration.Clone();
        }
        Console.WriteLine("SETUP\tимпорт: кривая {0}, геометрия {1}, ПШПВ {2}",
                          rd2.Efficiency != null ? rd2.Efficiency.Name : "(нет)",
                          rd2.Efficiency != null && rd2.Efficiency.HasGeometry ? "есть" : "нет",
                          rd2.FwhmCalibration != null ? "из исходного" : "умолчание");
        if (rd2.FwhmCalibration == null && rd2.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            if (cfg.FwhmCalibration == null && rd2.EnergySpectrum != null)
            {
                cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd2.EnergySpectrum.EnergyCalibration);
            }
            if (cfg.FwhmCalibration != null)
            {
                rd2.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
        }
        if (rd2.MeasurementController == null)
        {
            rd2.MeasurementController = new MeasurementController(null, rd2);
        }
        var source = rd2.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
        if (source != null)
        {
            source.DbLookupsForFsa = true;
            source.ChainEquilibrium = true;
        }
        EnergySpectrum es2 = rd2.EnergySpectrum;
        EnergySpectrum bg2 = rd2.BackgroundEnergySpectrum;
        Console.WriteLine(string.Format(Inv, "  импортировано: живое {0:R}, полное {1:R}, знаменатель {2:R} (у исходного {3:R}); фон: живое {4}, знаменатель {5} (у исходного {6:R})",
            es2.LiveTime, es2.MeasurementTime, es2.EffectiveLiveTime, denomSrc,
            bg2 == null ? "-" : bg2.LiveTime.ToString("R", Inv), bg2 == null ? "-" : bg2.EffectiveLiveTime.ToString("R", Inv), denomSrcBg));
        // ⚠ Мерка — знаменатель, который давал импорт ДО правки: живое 0 → полное ПОСЛЕ
        //   круга (Effective(0, полное) = полное). С исходным он совпадает до сетки 100 нс
        //   (`A147`: 55470.709087999996 → 55470.709088 — у ОБОИХ времён, до правки тоже),
        //   поэтому остаток к исходному печатается и судится допуском 1E-6 с.
        Verdict("круг: знаменатель спектра = полное после круга (как давал импорт до правки), побитово",
                es2.EffectiveLiveTime.ToString("R", Inv) + " / " + es2.MeasurementTime.ToString("R", Inv),
                es2.EffectiveLiveTime == es2.MeasurementTime);
        Verdict("круг: знаменатель спектра к исходному — в пределах сетки N42 (1E-6 с)",
                "остаток " + Math.Abs(es2.EffectiveLiveTime - denomSrc).ToString("E3", Inv) + " с",
                Math.Abs(es2.EffectiveLiveTime - denomSrc) <= 1e-6);
        if (src.BackgroundEnergySpectrum != null)
        {
            Verdict("круг: знаменатель фона = полное фона после круга, побитово",
                    bg2 == null ? "фона нет" : bg2.EffectiveLiveTime.ToString("R", Inv) + " / " + bg2.MeasurementTime.ToString("R", Inv),
                    bg2 != null && bg2.EffectiveLiveTime == bg2.MeasurementTime);
            Verdict("круг: знаменатель фона к исходному — в пределах сетки N42 (1E-6 с)",
                    bg2 == null ? "фона нет" : "остаток " + Math.Abs(bg2.EffectiveLiveTime - denomSrcBg).ToString("E3", Inv) + " с",
                    bg2 != null && Math.Abs(bg2.EffectiveLiveTime - denomSrcBg) <= 1e-6);
        }

        rd2.DetectedPeaks = new PeakDetector().DetectPeak(rd2, BackgroundMode.Invisible, SmoothingMethod.None,
                                                          nuclides.ActiveSet, nuclides.NuclideDefinitions);
        string peaks = PeakDigest(rd2);
        var sw = Stopwatch.StartNew();
        string asImported = RunFsa(back, rd2);
        sw.Stop();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "=== РАЗБОР импортированного (живое как прочитано: {0:R}), {1:F1} с", es2.LiveTime, sw.Elapsed.TotalSeconds));
        Console.WriteLine("  ПИКИ sha " + Sha(peaks) + "; РАЗБОР sha " + Sha(asImported));
        foreach (string line in asImported.Split('\n'))
        {
            if (line.Length > 0) Console.WriteLine("  | " + line);
        }

        // Плечо «живое 0»: то, что давал импорт ДО правки (в файле стояло PT0S).
        es2.LiveTime = 0.0;
        if (bg2 != null) bg2.LiveTime = 0.0;
        string live0 = RunFsa(back, rd2);
        Console.WriteLine("=== РАЗБОР того же спектра с живым 0 (как импортировал бы файл до правки)");
        Console.WriteLine("  РАЗБОР sha " + Sha(live0));
        Verdict("разбор: живое = полное и живое 0 дают одни числа (формат R)",
                Sha(asImported) + " / " + Sha(live0), asImported == live0 && asImported.Length > 0 && !asImported.StartsWith("(нет", StringComparison.Ordinal));
        if (asImported != live0)
        {
            foreach (string line in live0.Split('\n'))
            {
                if (line.Length > 0) Console.WriteLine("  ! " + line);
            }
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ fsa " + name + ": расхождений " + defects.ToString(Inv));
        return defects == 0 ? 0 : 4;
    }

    static string RunFsa(DocEnergySpectrum doc, ResultData rd)
    {
        FsaAnalysisSession session = doc.FsaSession;
        session.Reset();
        session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null);
        for (int i = 0; i < 2400 && session.IsRunning; i++)
        {
            Thread.Sleep(50);
            Application.DoEvents();
        }
        Application.DoEvents();
        if (session.IsRunning) return "(нет: сеанс не дошёл до покоя за 120 с)";
        FsaResult r = session.Result;
        if (r == null || r.Components == null) return "(нет результата; состояние сеанса: " + Flat(session.Status) + ")";
        var sb = new StringBuilder();
        sb.Append("chi2ndf ").Append(r.Chi2Ndf.ToString("R", Inv))
          .Append(" chi2poisson ").Append(r.Chi2NdfPoisson.ToString("R", Inv))
          .Append(" resid+ ").Append(r.ResidualExcessShare.ToString("R", Inv))
          .Append(" resid- ").Append(r.ResidualMissingShare.ToString("R", Inv)).Append('\n');
        foreach (FsaComponentResult c in r.Components)
        {
            sb.Append(c.Name).Append(" | ").Append(c.Kind)
              .Append(" | доля ").Append(c.SharePercent.ToString("R", Inv))
              .Append(" | скорость ").Append(c.CountRate.ToString("R", Inv))
              .Append(" | z ").Append(c.Z.ToString("R", Inv)).Append('\n');
        }
        if (r.CharacteristicLimits != null)
        {
            foreach (FsaCharacteristicLimit l in r.CharacteristicLimits)
            {
                sb.Append("предел ").Append(l.Name).Append(" | ").Append(l.Kind).Append(" | ").Append(l.Detected ? "+" : "-")
                  .Append(" | ").Append(l.CountRate.ToString("R", Inv)).Append('\n');
            }
        }
        return sb.ToString();
    }

    static string PeakDigest(ResultData rd)
    {
        var sb = new StringBuilder();
        List<Peak> peaks = rd.DetectedPeaks;
        sb.Append("пиков ").Append((peaks == null ? -1 : peaks.Count).ToString(Inv)).Append('\n');
        if (peaks != null)
        {
            foreach (Peak p in peaks)
            {
                sb.Append(p.Energy.ToString("R", Inv)).Append(' ').Append(p.Channel.ToString(Inv))
                  .Append(' ').Append(p.Nuclide != null ? p.Nuclide.Name : "-").Append('\n');
            }
        }
        return sb.ToString();
    }

    // ================================================================== помощники

    /// <summary>По одному на RadMeasurement: { класс, RealTimeDuration, LiveTimeDuration }.</summary>
    static List<string[]> Measurements(string text)
    {
        var list = new List<string[]>();
        foreach (Match m in Regex.Matches(text, "<RadMeasurement\\b[\\s\\S]*?</RadMeasurement>"))
        {
            string block = m.Value;
            list.Add(new[] { Tag(block, "MeasurementClassCode"), Tag(block, "RealTimeDuration"), Tag(block, "LiveTimeDuration") });
        }
        return list;
    }

    static string Live(List<string[]> ms, string cls)
    {
        string[] m = ms.FirstOrDefault(x => x[0] == cls);
        return m == null ? "(нет измерения " + cls + ")" : m[2];
    }

    static string Tag(string text, string tag)
    {
        Match m = Regex.Match(text, "<" + tag + ">([^<]*)</" + tag + ">");
        return m.Success ? m.Groups[1].Value : "(нет)";
    }

    static string Mask(string text, bool hideLive)
    {
        string t = Regex.Replace(text, "n42DocUUID=\"[^\"]*\"", "n42DocUUID=\"\"");
        if (hideLive) t = Regex.Replace(t, "<LiveTimeDuration>[^<]*</LiveTimeDuration>", "<LiveTimeDuration/>");
        return t;
    }

    static string Sha(string text)
    {
        using (SHA256 h = SHA256.Create())
        {
            byte[] b = h.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
            return BitConverter.ToString(b).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }

    static void Verdict(string what, string got, bool clean)
    {
        if (!clean) defects++;
        Console.WriteLine("{0}\t{1}\t{2}", what, got ?? "(null)", clean ? "ЧИСТО" : "ДЕФЕКТ");
    }

    static string Arg(string[] args, string key, string fallback)
    {
        foreach (string a in args)
        {
            if (a.StartsWith(key, StringComparison.Ordinal)) return a.Substring(key.Length);
        }
        return fallback;
    }

    static string Flat(string s)
    {
        return (s ?? "").Replace("\r", " ").Replace("\n", " | ");
    }
}
