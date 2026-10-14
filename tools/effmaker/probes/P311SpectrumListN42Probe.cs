// Проба полосы П311 (15.10.2026): СПИСОК СПЕКТРОВ И N42 — свежий проход.
// Журнал — handover/handover-2026-10-15-p311-data-output-review.md.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут
// пути от каталога входной сборки). Проба зовёт приложение только тем, что есть И
// ДО, И ПОСЛЕ правки полосы (`DCSpectrumListView.ShowSpectrumList`, поля формы —
// отражением, `DocumentManager.*`), поэтому один exe работает на обеих сборках:
// сборка ДО правки — положительный контроль.
//
//   P311SpectrumListN42Probe.exe --mode=list [--tmp=<каталог>]
//       «Экспорт...» списка спектров пишет спектры с ResultData.Selected, а видно
//       человеку выделение строк. Пять положений документа: открыт файл; импорт N42
//       в новый документ; «Новый» спектр; импорт файла в документ; смена активного
//       спектра (повторное обновление списка). В каждом — выделенные строки против
//       признака Selected (то, что уйдёт в файл), число строк в списке выделенных
//       XPTable против выделенных в таблице, доступность кнопки «Экспорт...».
//       Код 0 — всюду совпало; 1 — расхождения (поимённо).
//   P311SpectrumListN42Probe.exe --mode=n42 [--tmp=<каталог>]
//       граничные входы N42 и экспорт: файл 2006 года и Alpha Hound без элемента
//       InstrumentInformation (состояние документа после отказа), 2006 без
//       Calibration, 2012 с пустым ChannelData и с дробными отсчётами, экспорт с
//       живым временем 0 и с неизвестным началом (что в файле и что читает дверь
//       SpecUtils), время экспорта документа 16384 канала. Печать; код 0 —
//       документ ни в одном отказе не остался с числами отказавшего файла.
//
// Коды: 0 — ожидания сошлись; 1 — расхождения; 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BecquerelMonitor;

static class P311SpectrumListN42Probe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static DocumentManager dm;
    static string tmp;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string mode = Arg(args, "--mode=", "list");
        tmp = Arg(args, "--tmp=", Path.Combine(Path.GetTempPath(), "p311probe"));
        Directory.CreateDirectory(tmp);
        Console.WriteLine(ProbeTargetFramework.Describe());
        Console.WriteLine("приложение: " + typeof(DocumentManager).Assembly.Location + "  ("
            + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            dm = DocumentManager.GetInstance();
            if (mode == "list") return ListMode();
            if (mode == "n42") return N42Mode();
            Console.WriteLine("неизвестный --mode: " + mode);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
    }

    static string Arg(string[] args, string key, string fallback)
    {
        foreach (string a in args)
        {
            if (a.StartsWith(key, StringComparison.Ordinal)) return a.Substring(key.Length);
        }
        return fallback;
    }

    // ================================================================== список

    static object Field(object o, string name)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) throw new MissingFieldException(o.GetType().Name, name);
        return f.GetValue(o);
    }

    static int ListMode()
    {
        var view = new DCSpectrumListView(null);
        var model = (XPTable.Models.TableModel)Field(view, "tableModel1");
        var button4 = (System.Windows.Forms.Button)Field(view, "button4");
        int bad = 0;

        // Документ из трёх спектров, сохранённый файлом — вход положений A, C, D.
        string saved = Path.Combine(tmp, "three.xml");
        DocEnergySpectrum src = Synth(3, 1024, 3590.5, new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Local));
        src.Filename = saved;
        dm.SaveDocument(src);
        string n42 = Path.Combine(tmp, "three.n42");
        dm.WriteN42File(src, n42);

        // Документ открывается один раз (DocumentManager.OpenDocument отказывает второму
        // открытию того же файла) — каждому положению своя копия.
        Func<string, string> copy = tag =>
        {
            string p = Path.Combine(tmp, "three_" + tag + ".xml");
            File.Copy(saved, p, true);
            return p;
        };

        // A. Открыт файл.
        DocEnergySpectrum a = dm.OpenDocument(copy("a"));
        bad += Check("A открыт файл", view, model, button4, a);

        // B. Импорт N42 в новый документ (как пункт меню: CreateDocument + ImportDocumentN42).
        DocEnergySpectrum b = dm.CreateDocument();
        dm.ImportDocumentN42(b, n42);
        bad += Check("B импорт N42 в новый документ", view, model, button4, b);

        // C. «Новый» спектр (MainForm.AddNewSpectrum: doc.CreateResultData — новый активен).
        DocEnergySpectrum c = dm.OpenDocument(copy("c"));
        view.ShowSpectrumList(c);
        c.CreateResultData();
        bad += Check("C «Новый» спектр", view, model, button4, c);

        // D. Импорт файла в документ (MainForm.ImportSpectraIntoDocument: LoadDocument + Add).
        DocEnergySpectrum d = dm.OpenDocument(copy("d"));
        view.ShowSpectrumList(d);
        ResultDataFile loaded = dm.LoadDocument(d, copy("d_src"));
        foreach (ResultData rd in loaded.ResultDataList) d.ResultDataFile.ResultDataList.Add(rd);
        bad += Check("D импорт файла в документ", view, model, button4, d);

        // E. Смена активного без щелчка (обновление списка — UpdateAllView после «вверх/вниз»).
        DocEnergySpectrum e = dm.OpenDocument(copy("e"));
        view.ShowSpectrumList(e);
        e.ActiveResultDataIndex = 2;
        bad += Check("E смена активного, повторное обновление", view, model, button4, e);

        Console.WriteLine(bad == 0 ? "СПИСОК: ВСЕ ПОЛОЖЕНИЯ СОШЛИСЬ" : "СПИСОК: РАСХОЖДЕНИЙ " + bad.ToString(Inv));
        return bad == 0 ? 0 : 1;
    }

    static int Check(string title, DCSpectrumListView view, XPTable.Models.TableModel model,
                     System.Windows.Forms.Button button4, DocEnergySpectrum doc)
    {
        view.ShowSpectrumList(doc);
        var shown = new List<int>();
        for (int i = 0; i < model.Rows.Count; i++)
        {
            if (model.Rows[i].AnyCellsSelected) shown.Add(i);
        }
        var flags = new List<int>();
        for (int i = 0; i < doc.ResultDataFile.ResultDataList.Count; i++)
        {
            if (doc.ResultDataFile.ResultDataList[i].Selected) flags.Add(i);
        }
        int listed = model.Selections.SelectedItems.Length;
        int bad = 0;
        string verdict = "сошлось";
        if (!shown.SequenceEqual(flags)) { bad++; verdict = "⛔ «Экспорт...» запишет не выделенное"; }
        if (listed != shown.Count) { bad++; verdict += "; ⛔ в списке выделенных XPTable лишние строки"; }
        if (flags.Count == 0 && button4.Enabled) { bad++; verdict += "; ⛔ кнопка доступна при пустом экспорте"; }
        Console.WriteLine(string.Format(Inv,
            "{0}: спектров {1}, активный {2}; выделено в таблице [{3}]; уйдёт в файл (Selected) [{4}] — {5} шт.; "
            + "строк в списке выделенных XPTable {6}; «Экспорт...» {7} — {8}",
            title, doc.ResultDataFile.ResultDataList.Count, doc.ActiveResultDataIndex,
            string.Join(",", shown), string.Join(",", flags), flags.Count, listed,
            button4.Enabled ? "доступна" : "погашена", verdict));
        return bad;
    }

    static DocEnergySpectrum Synth(int spectra, int channels, double live, DateTime start)
    {
        DocEnergySpectrum d = dm.CreateDocument();
        for (int s = 0; s < spectra; s++)
        {
            ResultData rd = s == 0 ? d.ActiveResultData : d.CreateResultData();
            var es = new EnergySpectrum(1.0, channels);
            var cal = new PolynomialEnergyCalibration();
            cal.PolynomialOrder = 2;
            cal.Coefficients = new double[] { -3.25, 2.9375 * 1024.0 / channels, 1.25e-5 };
            es.EnergyCalibration = cal;
            long n = 0;
            var rnd = new Random(311 + s);
            for (int i = 0; i < channels; i++)
            {
                double x = i * 1024.0 / channels;
                double v = 40000.0 * Math.Exp(-x / 180.0) + 90000.0 * Math.Exp(-0.5 * Math.Pow((x - 226) / 7.5, 2));
                es.Spectrum[i] = (int)Math.Round(v + rnd.NextDouble() * (s + 1));
                n += es.Spectrum[i];
            }
            es.TotalPulseCount = n;
            es.ValidPulseCount = n;
            es.MeasurementTime = 3600.25;
            es.LiveTime = live;
            rd.EnergySpectrum = es;
            rd.StartTime = start;
            rd.EndTime = start.AddSeconds(3600.25);
            rd.SampleInfo.Time = start;
            rd.SampleInfo.Name = "S" + s.ToString(Inv);
        }
        d.ActiveResultDataIndex = 0;
        return d;
    }

    // ================================================================== N42

    static int N42Mode()
    {
        int bad = 0;
        var err = new StringWriter();
        TextWriter oldErr = Console.Error;
        Console.SetError(err);
        try
        {
            // Ожидания — поведение ПОСЛЕ правки П311; сборка до правки обязана разойтись
            // (положительный контроль). «факт» — печать без ожидания.
            bad += Case("2006 с InstrumentInformation (контроль)", "c2006_ok.n42", N42_2006(true, false), "import", "Manufacturer = PROBE");
            bad += Case("2006 без InstrumentInformation", "c2006_noinstr.n42", N42_2006(true, true), "import", "");
            bad += Case("2006 без Calibration", "c2006_nocal.n42", N42_2006(false, false), "refuse", "No calibration section");
            bad += Case("2006 пустой ChannelData", "c2006_empty.n42", N42_2006(true, false).Replace(Counts(64, ""), ""), "refuse", "спектр пуст");
            bad += Case("Alpha Hound с InstrumentInformation (контроль)", "crad_ok.n42", Rad(1024, true), "import", "Manufacturer = PROBE");
            bad += Case("Alpha Hound без InstrumentInformation", "crad_noinstr.n42", Rad(1024, false), "import", "");
            bad += Case("Alpha Hound NumberOfChannels=0", "crad_zero.n42", Rad(1024, true).Replace("NumberOfChannels=\"1024\"", "NumberOfChannels=\"0\""), "refuse", "спектр пуст");
            bad += Case("2012 пустой ChannelData", "c2012_empty.n42", Rad2012("", "PT295S"), "refuse", "спектр пуст");
            bad += Case("2012 здоровый (контроль)", "c2012_ok.n42", Rad2012(Counts(64, ""), "PT295S"), "import", "PROBE - P311");
            bad += Case("2012 без RadMeasurement", "c2012_nomeas.n42",
                        Regex.Replace(Rad2012(Counts(64, ""), "PT295S"), "  <RadMeasurement[\\s\\S]*</RadMeasurement>\\r\\n", ""),
                        "refuse", "импортируемых измерений в файле нет");
            bad += Case("2012 дробные отсчёты «1.0»", "c2012_float.n42", Rad2012(Counts(64, ".0"), "PT295S"), "fact", null);
        }
        finally
        {
            Console.SetError(oldErr);
        }
        Console.WriteLine("голоса (поток ошибок): " + err.ToString().Replace("\r", "").Replace("\n", " | "));

        // Экспорт: живое время 0 («неизвестно») и неизвестное начало.
        DocEnergySpectrum z = Synth(1, 1024, 0.0, ResultData.UnknownStartTime);
        string fz = Path.Combine(tmp, "live0_unknownstart.n42");
        dm.WriteN42File(z, fz);
        string text = File.ReadAllText(fz);
        Console.WriteLine("экспорт живое 0: LiveTimeDuration = " + Tag(text, "LiveTimeDuration")
            + "; RealTimeDuration = " + Tag(text, "RealTimeDuration") + "; StartDateTime = " + Tag(text, "StartDateTime"));
        var err2 = new StringWriter();
        Console.SetError(err2);
        try
        {
            DocEnergySpectrum su = dm.CreateDocument();
            dm.ImportDocumentSpecUtils(su, fz, 3600);
            Console.WriteLine(string.Format(Inv, "  дверь SpecUtils читает: живое {0}, полное {1}, начало {2:yyyy-MM-dd HH:mm:ss} ({3}), = UnknownStartTime: {4}",
                su.ActiveResultData.EnergySpectrum.LiveTime, su.ActiveResultData.EnergySpectrum.MeasurementTime,
                su.ActiveResultData.StartTime, su.ActiveResultData.StartTime.Kind, su.ActiveResultData.StartTime == ResultData.UnknownStartTime));
            DocEnergySpectrum nn = dm.CreateDocument();
            dm.ImportDocumentN42(nn, fz);
            Console.WriteLine(string.Format(Inv, "  дверь N42 читает: живое {0}, полное {1}, начало {2:yyyy-MM-dd HH:mm:ss} ({3}), = UnknownStartTime: {4}",
                nn.ActiveResultData.EnergySpectrum.LiveTime, nn.ActiveResultData.EnergySpectrum.MeasurementTime,
                nn.ActiveResultData.StartTime, nn.ActiveResultData.StartTime.Kind, nn.ActiveResultData.StartTime == ResultData.UnknownStartTime));
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ОТКАЗ импорта экспорта: " + ex.Message);
        }
        finally
        {
            Console.SetError(oldErr);
        }
        Console.WriteLine("  голоса импорта: «" + err2.ToString().Replace("\r", "").Replace("\n", " | ") + "»");

        // Скорость экспорта: 16384 канала, три спектра с фоном.
        DocEnergySpectrum big = Synth(3, 16384, 3590.5, new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Local));
        foreach (ResultData rd in big.ResultDataFile.ResultDataList) rd.BackgroundEnergySpectrum = rd.EnergySpectrum.Clone();
        string fb = Path.Combine(tmp, "big.n42");
        dm.WriteN42File(big, fb);
        var times = new List<double>();
        for (int k = 0; k < 3; k++)
        {
            var sw = Stopwatch.StartNew();
            dm.WriteN42File(big, fb);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(string.Format(Inv, "экспорт 3×(спектр+фон)×16384 канала: {0} мс (три замера), файл {1} байт, sha256 без n42DocUUID {2}",
            string.Join(" / ", times.Select(t => t.ToString("F0", Inv))), new FileInfo(fb).Length, Sha(fb)));

        Console.WriteLine(bad == 0 ? "N42: ВСЕ ОЖИДАНИЯ СОШЛИСЬ" : "N42: РАСХОЖДЕНИЙ " + bad.ToString(Inv));
        return bad == 0 ? 0 : 1;
    }

    static string Sha(string path)
    {
        string t = File.ReadAllText(path);
        t = Regex.Replace(t, "n42DocUUID=\"[^\"]*\"", "n42DocUUID=\"\"");
        using (var h = System.Security.Cryptography.SHA256.Create())
        {
            byte[] b = h.ComputeHash(Encoding.UTF8.GetBytes(t));
            return BitConverter.ToString(b).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }

    static string Tag(string xml, string name)
    {
        Match m = Regex.Match(xml, "<" + name + ">([^<]*)</" + name + ">");
        return m.Success ? m.Groups[1].Value : "<нет>";
    }

    /// <summary>
    /// Один вход N42: импорт дверью меню в документ со СВОИМ спектром (100 каналов по 9).
    /// Судится: исход против ожидания («import» — импортирован и примечание содержит
    /// <paramref name="noteHas"/>; «refuse» — отказ с причиной, содержащей
    /// <paramref name="noteHas"/>), и `A217` — после отказа в документе НЕ должно быть
    /// отсчётов отказавшего файла (сумма файла 253 на 64 канала у 2006/2012, у Alpha Hound —
    /// своя). Обнуление документа дверью (`Initialize`/сброс прибора в
    /// `DocumentManager.ImportDocumentN42` до разбора) — отдельный известный остаток,
    /// не судится.
    /// </summary>
    static int Case(string title, string name, string content, string expect, string noteHas)
    {
        string path = Path.Combine(tmp, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        long fileSum = 0;
        Match cd = Regex.Match(content, "<ChannelData[^>]*>([^<]*)</ChannelData>");
        if (cd.Success)
        {
            foreach (string tok in cd.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double v;
                if (double.TryParse(tok, NumberStyles.Float, Inv, out v)) fileSum += (long)v;
            }
        }
        DocEnergySpectrum d = dm.CreateDocument();
        var es = new EnergySpectrum(1.0, 100);
        for (int i = 0; i < 100; i++) es.Spectrum[i] = 9;
        es.TotalPulseCount = 900;
        es.ValidPulseCount = 900;
        es.EnergyCalibration = new PolynomialEnergyCalibration();
        d.ActiveResultData.EnergySpectrum = es;
        string outcome;
        string reason = "";
        bool refused = false;
        try
        {
            dm.ImportDocumentN42(d, path);
            outcome = "импортирован";
        }
        catch (Exception ex)
        {
            refused = true;
            reason = Short(ex);
            outcome = "ОТКАЗ: " + reason;
        }
        EnergySpectrum now = d.ActiveResultData.EnergySpectrum;
        long sum = 0;
        for (int i = 0; i < now.NumberOfChannels && i < now.Spectrum.Length; i++) sum += now.Spectrum[i];
        string note = d.ActiveResultData.SampleInfo.Note ?? "";
        bool spoiled = refused && fileSum > 0 && sum == fileSum;
        var why = new List<string>();
        if (spoiled) why.Add("⛔ отказ, а документ несёт отсчёты отказавшего файла (A217)");
        if (expect == "import")
        {
            if (refused) why.Add("⛔ ждали импорт");
            else if (noteHas != null && noteHas.Length > 0 && note.IndexOf(noteHas, StringComparison.Ordinal) < 0) why.Add("⛔ в примечании нет «" + noteHas + "»");
            else if (noteHas != null && noteHas.Length == 0 && note.Length != 0) why.Add("⛔ ждали пустое примечание");
            if (!refused && now.NumberOfChannels <= 0) why.Add("⛔ импортирован спектр из нуля каналов");
        }
        else if (expect == "refuse")
        {
            if (!refused) why.Add("⛔ ждали отказ" + (now.NumberOfChannels <= 0 ? " (импортирован спектр из нуля каналов)" : ""));
            else if (reason.IndexOf(noteHas, StringComparison.Ordinal) < 0) why.Add("⛔ причина не названа («" + noteHas + "» нет)");
        }
        Console.WriteLine(string.Format(Inv, "{0}: {1}; документ — каналов {2}, сумма {3} (файл {4}); примечание «{5}» — {6}",
            title, outcome, now.NumberOfChannels, sum, fileSum, note.Replace("\r", "").Replace("\n", " / "),
            expect == "fact" ? "факт" : (why.Count == 0 ? "сошлось" : string.Join("; ", why))));
        return expect == "fact" ? 0 : why.Count;
    }

    static string Short(Exception ex)
    {
        Exception e = ex;
        var parts = new List<string>();
        while (e != null)
        {
            parts.Add(e.GetType().Name + ": " + e.Message);
            e = e.InnerException;
        }
        return string.Join(" ← ", parts);
    }

    static string Counts(int n, string suffix)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(((i % 7) + 1).ToString(Inv)).Append(suffix);
        }
        return sb.ToString();
    }

    static string N42_2006(bool withCalibration, bool withoutInstrument)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
             + "<N42InstrumentData xmlns=\"" + BecquerelMonitor.N42.N42InstrumentData.Ns + "\">\r\n"
             + "  <Measurement>\r\n"
             + (withoutInstrument ? "" :
                "    <InstrumentInformation>\r\n"
              + "      <InstrumentType>Spectrometer</InstrumentType>\r\n"
              + "      <Manufacturer>PROBE</Manufacturer>\r\n"
              + "      <InstrumentModel>P311</InstrumentModel>\r\n"
              + "    </InstrumentInformation>\r\n")
             + "    <Spectrum Type=\"PHA\">\r\n"
             + "      <StartTime>2026-09-05T12:00:00Z</StartTime>\r\n"
             + "      <RealTime>PT300S</RealTime>\r\n"
             + "      <LiveTime>PT295S</LiveTime>\r\n"
             + "      <ChannelData>" + Counts(64, "") + "</ChannelData>\r\n"
             + "    </Spectrum>\r\n"
             + "  </Measurement>\r\n"
             + (withCalibration ?
                "  <Calibration Type=\"Energy\">\r\n"
              + "    <Equation Model=\"Polynomial\">\r\n"
              + "      <Coefficients>3.5 0.5</Coefficients>\r\n"
              + "    </Equation>\r\n"
              + "  </Calibration>\r\n" : "")
             + "</N42InstrumentData>\r\n";
    }

    static string Rad(int n, bool withInstrument)
    {
        var energies = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            double e = 5.0 + 0.5 * i + 1e-6 * i * i + 1e-11 * i * i * i + 1e-14 * i * i * i * i;
            if (i > 0) energies.Append(' ');
            energies.Append(e.ToString("0.######", Inv));
        }
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
             + "<RadiologicalInstrumentData xmlns=\"http://physics.nist.gov/N42/2006/N42\">\r\n"
             + "  <MeasurementGroup>\r\n"
             + "    <Measurement>\r\n"
             + "      <Spectrum>\r\n"
             + (withInstrument ?
                "        <InstrumentInformation>\r\n"
              + "          <Manufacturer>PROBE</Manufacturer>\r\n"
              + "        </InstrumentInformation>\r\n" : "")
             + "        <EnergyCalibration>\r\n"
             + "          <CalibrationEquation>List</CalibrationEquation>\r\n"
             + "          <ChannelEnergies>" + energies + "</ChannelEnergies>\r\n"
             + "        </EnergyCalibration>\r\n"
             + "        <ChannelData NumberOfChannels=\"" + n.ToString(Inv) + "\">" + Counts(n, "") + "</ChannelData>\r\n"
             + "        <LiveTime>295</LiveTime>\r\n"
             + "      </Spectrum>\r\n"
             + "    </Measurement>\r\n"
             + "  </MeasurementGroup>\r\n"
             + "</RadiologicalInstrumentData>\r\n";
    }

    static string Rad2012(string counts, string live)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
             + "<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\" n42DocUUID=\"p311\">\r\n"
             + "  <RadInstrumentInformation id=\"RadInstrument\">\r\n"
             + "    <RadInstrumentManufacturerName>PROBE</RadInstrumentManufacturerName>\r\n"
             + "    <RadInstrumentModelName>P311</RadInstrumentModelName>\r\n"
             + "    <RadInstrumentClassCode>Radionuclide Identifier</RadInstrumentClassCode>\r\n"
             + "    <RadInstrumentVersion>\r\n"
             + "      <RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\r\n"
             + "      <RadInstrumentComponentVersion>1</RadInstrumentComponentVersion>\r\n"
             + "    </RadInstrumentVersion>\r\n"
             + "  </RadInstrumentInformation>\r\n"
             + "  <EnergyCalibration id=\"EC1\">\r\n"
             + "    <CoefficientValues>3.5 12.5</CoefficientValues>\r\n"
             + "  </EnergyCalibration>\r\n"
             + "  <RadMeasurement id=\"M1\">\r\n"
             + "    <MeasurementClassCode>Foreground</MeasurementClassCode>\r\n"
             + "    <StartDateTime>2026-09-05T12:00:00Z</StartDateTime>\r\n"
             + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
             + "    <Spectrum id=\"S1\" energyCalibrationReference=\"EC1\">\r\n"
             + "      <LiveTimeDuration>" + live + "</LiveTimeDuration>\r\n"
             + "      <ChannelData>" + counts + "</ChannelData>\r\n"
             + "    </Spectrum>\r\n"
             + "  </RadMeasurement>\r\n"
             + "</RadInstrumentData>\r\n";
    }
}
