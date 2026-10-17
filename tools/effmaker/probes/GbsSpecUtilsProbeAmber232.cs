using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BecquerelMonitor;

/// <summary>
/// Дверь «SpectraLine GBS» против двери «Импорт через SpecUtils...» на ОДНИХ И ТЕХ ЖЕ
/// файлах (`AMBER232`, постановка Amber 17.10.2026: «Удалить логику импорта
/// Spectraline GBS. Её дублирует полность SpecUtils.dll»).
///
/// Перед удалением мерится, что вторая дверь действительно отдаёт то же: число
/// каналов, отсчёты по каналам, живое и полное время, начало и конец набора,
/// энергетическую шкалу в пяти каналах, пометку спектра. Входы — файлы GBS
/// (`$SPEC_ID:` … `$ENER_DATA_X:` … `$COUNTS:`, ровно то, что разбирает
/// `DocumentManager.ImportDocumentGBS`) в нескольких вариантах калибровки, и
/// текстовые SPE ЛСРМ SpectraLine, переданные ключом `--spe=`.
///
///   GbsSpecUtilsProbeAmber232.exe --dir=&lt;рабочий каталог&gt; [--spe=a.spe;b.spe]
///
/// Ничего не судит кодом возврата сверх «запустилась»: печатает сравнение
/// построчно, решение — за человеком (0 — прогон дошёл до конца; 2 — ключи).
/// </summary>
static class GbsSpecUtilsProbeAmber232
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string dir = null;
        List<string> spe = new List<string>();
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--spe=", StringComparison.Ordinal))
            {
                foreach (string p in a.Substring(6).Split(';')) if (p.Trim().Length > 0) spe.Add(p.Trim());
            }
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (string.IsNullOrEmpty(dir)) { Console.Error.WriteLine("--dir=<каталог> обязателен"); return 2; }
        Directory.CreateDirectory(dir);

        Console.WriteLine("сборка: " + typeof(DocumentManager).Assembly.Location);
        Console.WriteLine();

        // Варианты файла GBS. Точки калибровки — НЕ на прямой (квадратичная шкала),
        // чтобы было видно, чью кривую взяла дверь.
        string[] dataX = { "$ENER_DATA_X:", "3", "100 50.2000", "500 250.4000", "900 452.1000" };
        string[] dataPlain = { "$ENER_DATA:", "3", "100 50.2000", "500 250.4000", "900 452.1000" };
        string[] fit = { "$ENER_FIT:", "0.300000 0.500000" };
        Compare(dir, "A: только $ENER_DATA_X, время через два пробела", Gbs(dataX, null, "598  600"));
        Compare(dir, "B: $ENER_DATA_X и $ENER_FIT (прямая 0.3 + 0.5·ch)", Gbs(dataX, fit, "598  600"));
        Compare(dir, "C: $ENER_DATA вместо $ENER_DATA_X", Gbs(dataPlain, null, "598  600"));
        Compare(dir, "D: только $ENER_DATA_X, время через ОДИН пробел", Gbs(dataX, null, "598 600"));

        foreach (string p in spe)
        {
            string copy = Path.Combine(dir, Path.GetFileName(p));
            File.Copy(p, copy, true);                 // каталог Amber — только чтение
            CompareFile("SPE ЛСРМ: " + Path.GetFileName(p), copy);
        }

        return 0;
    }

    static string Gbs(string[] energy, string[] fit, string measTim)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("$SPEC_ID:");
        sb.AppendLine("проба AMBER232: пометка спектра");
        sb.AppendLine("$DATE_MEA:");
        sb.AppendLine("10/16/2026 21:02:07");
        sb.AppendLine("$MEAS_TIM:");
        sb.AppendLine(measTim);
        sb.AppendLine("$DATA:");
        sb.AppendLine("0 1023");
        for (int i = 0; i < 1024; i++)
        {
            double x = i - 500.0;
            int c = (int)Math.Round(200.0 * Math.Exp(-i / 300.0) + 3000.0 * Math.Exp(-0.5 * x * x / 64.0) + 5.0);
            sb.AppendLine(c.ToString(IC));
        }

        foreach (string l in energy) sb.AppendLine(l);
        if (fit != null) foreach (string l in fit) sb.AppendLine(l);
        sb.AppendLine("$COUNTS:");
        sb.AppendLine("123456");
        return sb.ToString();
    }

    static void Compare(string dir, string title, string text)
    {
        string path = Path.Combine(dir, "gbs_" + title.Substring(0, 1) + ".spe");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        CompareFile(title, path);
    }

    static void CompareFile(string title, string path)
    {
        Console.WriteLine("=== " + title + " ===");
        Snapshot g = Run("GBS", path, (dm, doc) => dm.ImportDocumentGBS(doc, path));
        Snapshot s = Run("SpecUtils", path, (dm, doc) => dm.ImportDocumentSpecUtils(doc, path, 0));
        Print(g);
        Print(s);
        if (g.Error == null && s.Error == null)
        {
            int diff = 0;
            int n = Math.Min(g.Counts.Length, s.Counts.Length);
            for (int i = 0; i < n; i++) if (g.Counts[i] != s.Counts[i]) diff++;
            Console.WriteLine("  сверка: каналов {0} / {1}; несовпавших отсчётов {2}; энергии {3}",
                              g.Counts.Length, s.Counts.Length, diff, SameEnergy(g, s) ? "совпали (≤ 0.01 кэВ)" : "РАЗОШЛИСЬ");
        }

        Console.WriteLine();
    }

    sealed class Snapshot
    {
        public string Door, Error, Note, Name, Calibration, Start, End;
        public int[] Counts = new int[0];
        public double Live, Real;
        public long Total;
        public double[] Energy = new double[0];
        public int[] At = new int[0];
    }

    static Snapshot Run(string door, string path, Action<DocumentManager, DocEnergySpectrum> import)
    {
        Snapshot r = new Snapshot { Door = door };
        DocumentManager dm = DocumentManager.GetInstance();
        string docPath = Path.ChangeExtension(path, "." + door + ".xml");
        if (File.Exists(docPath)) File.Delete(docPath);
        DocEnergySpectrum doc = dm.CreateDocument(docPath);
        TextWriter err = Console.Error;
        StringWriter caught = new StringWriter();
        Console.SetError(caught);
        try
        {
            import(dm, doc);
        }
        catch (Exception e)
        {
            r.Error = e.GetType().Name + ": " + e.Message;
        }
        finally
        {
            Console.SetError(err);
        }

        string voices = caught.ToString().Trim();
        if (r.Error == null && voices.Length > 0) r.Error = "голос: " + voices.Replace("\r", " ").Replace("\n", " | ");
        ResultData rd = doc.ActiveResultData;
        EnergySpectrum es = rd.EnergySpectrum;
        r.Counts = (int[])es.Spectrum.Clone();
        r.Live = es.LiveTime;
        r.Real = es.MeasurementTime;
        r.Total = es.TotalPulseCount;
        r.Note = rd.SampleInfo.Note;
        r.Name = rd.SampleInfo.Name;
        r.Start = rd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", IC) + " " + rd.StartTime.Kind;
        r.End = rd.EndTime.ToString("yyyy-MM-dd HH:mm:ss", IC);
        PolynomialEnergyCalibration cal = es.EnergyCalibration as PolynomialEnergyCalibration;
        if (cal != null)
        {
            List<string> c = new List<string>();
            foreach (double v in cal.Coefficients) c.Add(v.ToString("G6", IC));
            r.Calibration = "порядок " + cal.PolynomialOrder.ToString(IC) + ": " + string.Join(" ", c);
        }
        else
        {
            r.Calibration = es.EnergyCalibration == null ? "нет" : es.EnergyCalibration.GetType().Name;
        }

        int n = r.Counts.Length;
        r.At = new[] { 0, 100, n / 2, Math.Max(0, n - 124), Math.Max(0, n - 1) };
        r.Energy = new double[r.At.Length];
        for (int i = 0; i < r.At.Length; i++) r.Energy[i] = es.EnergyCalibration.ChannelToEnergy(r.At[i]);
        return r;
    }

    static bool SameEnergy(Snapshot a, Snapshot b)
    {
        if (a.Energy.Length != b.Energy.Length) return false;
        for (int i = 0; i < a.Energy.Length; i++) if (Math.Abs(a.Energy[i] - b.Energy[i]) > 0.01) return false;
        return true;
    }

    static void Print(Snapshot r)
    {
        long sum = 0;
        foreach (int c in r.Counts) sum += c;
        List<string> e = new List<string>();
        for (int i = 0; i < r.At.Length; i++) e.Add(r.At[i].ToString(IC) + "→" + r.Energy[i].ToString("F2", IC));
        Console.WriteLine("  {0,-9} {1}", r.Door + ":", r.Error ?? "без отказа");
        Console.WriteLine("            каналов {0}, сумма {1}, всего имп. {2}; живое {3} с, полное {4} с",
                          r.Counts.Length, sum, r.Total, r.Live.ToString("G6", IC), r.Real.ToString("G6", IC));
        Console.WriteLine("            начало {0}, конец {1}", r.Start, r.End);
        Console.WriteLine("            калибровка {0}; энергии {1}", r.Calibration, string.Join(", ", e));
        Console.WriteLine("            имя «{0}», пометка «{1}»", r.Name, r.Note);
    }
}
