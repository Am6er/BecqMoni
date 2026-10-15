// Проба полосы П321 (15.10.2026): «ПОЛУЧИТЬ ВСЕ ПИКИ» В КАЛИБРОВКЕ ПШПВ БЕРЁТ ОПОРЫ ТОЛЬКО ИЗ
// СПЕКТРОВ С ТЕМ ЖЕ ЧИСЛОМ КАНАЛОВ (решение Amber вопросником 15.10.2026, дословно: «Брать только из
// документов, где число каналов такое же.»; находка П314, вопрос В2).
//
// Без окон, отражением, по образцу P314FwhmProbe (плечо E). Один exe работает со сборкой ДО правки
// (обязан показать, что опоры чужой сетки входят в сбор и кривая искажается) и ПОСЛЕ (обязан показать,
// что они не входят, а человеку названо их число); контроль обязан сойтись на обеих.
//
//   P321FwhmSameGridProbe.exe        (из каталога, где лежит BecquerelMonitor.exe)
//
// Плечи:
//   R  ресурс MSGFwhmPeaksOtherGridSkipped: есть в нейтральном и в русском resx, несёт «{0}».
//      Контроль: соседний ERRPeakExist читается и в нейтральной, и в русской культуре (русский
//      сателлит прибором достижим) — иначе плечо мерит пустоту.
//   C  четыре сцены «Получить все пики»:
//        1. активный 1024 канала, рядом 4096 (чужая сетка);
//        2. активный 4096 каналов, рядом 1024;
//        3. смешанно: активный 1024, рядом 4096 (чужой) и 1024 (свой);
//        4. КОНТРОЛЬ: два документа одной сетки (1024 + 1024, и 4096 + 4096) — опоры обоих берутся
//           на обеих сборках и сообщения нет.
//      Для каждой: опоры активного после сбора, число чужих среди них (дефект, если есть),
//      кривая степенной формы на тех же опорах против истинной ПШПВ (дефект, если отклонение
//      больше 10 % на 120 / 660 / 1332 кэВ), строка человеку из потока ошибок (дефект, если опоры
//      пропущены, а строки с их числом нет, либо строка есть, а пропуска не было).
//   N  активный документ без спектра: сбор не падает и ничего не меняет.
//
// Коды: 0 — дефектов нет, контроли сошлись (сборка ПОСЛЕ); 1 — дефект виден (сборка ДО);
// 3 — контроль НЕ сошёлся (замер пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

static class P321FwhmSameGridProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    const string Key = "MSGFwhmPeaksOtherGridSkipped";
    static int defect, control;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = Inv;
        CultureInfo.DefaultThreadCurrentCulture = Inv;
        try
        {
            Console.WriteLine(ProbeTargetFramework.Describe());
            Console.WriteLine("приложение: " + typeof(ResultData).Assembly.Location);
            Console.WriteLine("собрано: " + File.GetLastWriteTime(typeof(ResultData).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", Inv));
            Console.WriteLine();
            ArmResource();
            ArmCases();
            ArmNoSpectrum();
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ: дефектов " + defect.ToString(Inv) + ", несработавших контролей " + control.ToString(Inv));
        if (control > 0) return 3;
        return defect > 0 ? 1 : 0;
    }

    // ================================================================== R — ресурс

    static ResourceManager Manager()
    {
        Type t = typeof(ResultData).Assembly.GetType("BecquerelMonitor.Properties.Resources");
        if (t == null) throw new InvalidOperationException("класс Properties.Resources не найден");
        PropertyInfo p = t.GetProperty("ResourceManager", S);
        if (p == null) throw new InvalidOperationException("Resources.ResourceManager не найден");
        return (ResourceManager)p.GetValue(null);
    }

    /// <summary>Текст ресурса по текущей культуре интерфейса, через СВОЙСТВО (как зовёт код); null — свойства нет.</summary>
    static string ViaProperty(string name)
    {
        Type t = typeof(ResultData).Assembly.GetType("BecquerelMonitor.Properties.Resources");
        PropertyInfo p = t.GetProperty(name, S);
        return p == null ? null : (string)p.GetValue(null);
    }

    static void ArmResource()
    {
        Console.WriteLine("== R. Ресурс " + Key);
        ResourceManager rm = Manager();
        CultureInfo en = CultureInfo.GetCultureInfo("en-US");
        CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");

        // Положительный контроль: сосед читается в обеих культурах, и русский отличается от нейтрального.
        string neighborEn = rm.GetString("ERRPeakExist", en);
        string neighborRu = rm.GetString("ERRPeakExist", ru);
        Control(neighborEn != null && neighborRu != null && neighborEn != neighborRu,
                "R: сосед ERRPeakExist не читается в двух культурах (русский сателлит недостижим) — плечо мерит пустоту");

        string tEn = rm.GetString(Key, en);
        string tRu = rm.GetString(Key, ru);
        Console.WriteLine("  en: " + (tEn ?? "<нет>"));
        Console.WriteLine("  ru: " + (tRu ?? "<нет>"));
        if (tEn == null) Defect("R: ключа " + Key + " нет в нейтральном resx");
        else if (!tEn.Contains("{0}")) Defect("R: в нейтральном тексте нет «{0}»");
        if (tRu == null) Defect("R: ключа " + Key + " нет в русском resx");
        else if (!tRu.Contains("{0}")) Defect("R: в русском тексте нет «{0}»");
        if (tEn != null && tRu != null && tEn == tRu) Defect("R: русский текст совпадает с нейтральным (не переведён)");
        if (tEn != null && tRu != null)
        {
            string fe = string.Format(Inv, tEn, 7);
            string fr = string.Format(Inv, tRu, 7);
            Console.WriteLine("  с числом 7: «" + fe + "» / «" + fr + "»");
            if (!fe.Contains("7") || !fr.Contains("7")) Defect("R: число не подставилось");
        }
        string viaProp = ViaProperty(Key);
        Console.WriteLine("  свойство Resources." + Key + ": " + (viaProp == null ? "<нет>" : "есть"));
        if (viaProp == null) Defect("R: свойства Resources." + Key + " нет (Designer не обновлён)");
        Console.WriteLine();
    }

    // ================================================================== C — сцены

    static double Fkev(double en) { return Math.Sqrt(4.0 + 3.2320 * en); }

    sealed class Scene
    {
        public int N;
        public double Gain;
        public int[] Chs;
        public Scene(int n, double gain, params int[] chs) { N = n; Gain = gain; Chs = chs; }
    }

    static void ArmCases()
    {
        Console.WriteLine("== C. «Получить все пики» (один и тот же детектор: F(E) = sqrt(4 + 3.232·E) кэВ)");
        Case("1. активный 1024 (3 кэВ/кан), рядом чужой 4096 (0.75 кэВ/кан)",
             new Scene(1024, 3.0, 40, 220, 444), new[] { new Scene(4096, 0.75, 880, 1564) });
        Case("2. активный 4096 (0.75 кэВ/кан), рядом чужой 1024 (3 кэВ/кан)",
             new Scene(4096, 0.75, 160, 880, 1776), new[] { new Scene(1024, 3.0, 40, 391) });
        Case("3. смешанно: активный 1024, рядом чужой 4096 и свой 1024",
             new Scene(1024, 3.0, 40, 220, 444), new[] { new Scene(4096, 0.75, 880, 1564), new Scene(1024, 3.0, 100, 700) });
        Case("4. КОНТРОЛЬ: два документа одной сетки 1024 + 1024",
             new Scene(1024, 3.0, 40, 220, 444), new[] { new Scene(1024, 3.0, 100, 700) });
        Case("5. КОНТРОЛЬ: два документа одной сетки 4096 + 4096",
             new Scene(4096, 0.75, 160, 880, 1776), new[] { new Scene(4096, 0.75, 400, 3000) });
        Console.WriteLine();
    }

    static void Case(string label, Scene active, Scene[] others)
    {
        Console.WriteLine("  " + label);
        DocumentManager.GetInstance().DocumentList.Clear();
        Stand a = Stand.Make("p321-active", active.N, active.Gain);
        var stands = new List<Stand> { a };
        var ca = Calibrate(active, "активного");
        a.Rd.FwhmCalibration = ca;
        int k = 0;
        int sameGridExtra = 0, foreignCount = 0;
        foreach (Scene o in others)
        {
            Stand b = Stand.Make("p321-other-" + (k++).ToString(Inv), o.N, o.Gain);
            b.Rd.FwhmCalibration = Calibrate(o, "соседнего");
            stands.Add(b);
            if (o.N == active.N) sameGridExtra += o.Chs.Length; else foreignCount += o.Chs.Length;
        }
        // Положительный контроль стенда: сосед виден виду (кнопка считает опоры всех документов).
        int visible = (int)a.Invoke("isCalibrationPeaksExist");
        Control(visible == active.Chs.Length + sameGridExtra + foreignCount,
                "C: вид не видит опор соседних документов (" + visible.ToString(Inv) + ") — замер пуст");

        a.Invoke("UpdateFwhmCalibration", true);
        TextWriter oldErr = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try { a.Invoke("GetAllPeaksButton_Click", null, EventArgs.Empty); }
        finally { Console.SetError(oldErr); }
        string said = captured.ToString().Trim();

        List<CalibrationPeak> got = a.Rd.FwhmCalibration.CalibrationPeaks;
        var want = new HashSet<int>(active.Chs);
        foreach (Scene o in others) if (o.N == active.N) foreach (int ch in o.Chs) want.Add(ch);
        Console.WriteLine("    опоры активного после сбора (канал; ПШПВ, кан.): " + string.Join(", ", got.Select(p => "(" + p.Channel.ToString(Inv) + "; " + p.FWHM.ToString("0.0#", Inv) + ")")));
        Console.WriteLine("    ожидается каналов своей сетки: " + want.Count.ToString(Inv) + ", получено: " + got.Count.ToString(Inv) + ", чужих опор в сборе: " + got.Count(p => !want.Contains(p.Channel)).ToString(Inv) + ", за пределом шкалы " + active.N.ToString(Inv) + ": " + got.Count(p => p.Channel >= active.N).ToString(Inv));
        if (got.Any(p => !want.Contains(p.Channel)))
            Defect("C[" + label.Substring(0, 1) + "]: в сбор вошли опоры спектра другой сетки (каналы " + string.Join(", ", got.Where(p => !want.Contains(p.Channel)).Select(p => p.Channel.ToString(Inv))) + ")");
        if (want.Any(ch => !got.Any(p => p.Channel == ch)))
            Defect("C[" + label.Substring(0, 1) + "]: не взята опора спектра ТОЙ ЖЕ сетки (каналы " + string.Join(", ", want.Where(ch => !got.Any(p => p.Channel == ch)).Select(ch => ch.ToString(Inv))) + ")");

        // Кривая степенной формы на тех же опорах против истины.
        var power = new PowerFwhmCalibration { CalibrationPeaks = CalibrationPeak.ClonePeaks(got) };
        bool okp = power.PerformCalibration(active.N);
        var sb = new StringBuilder();
        double worst = 0;
        foreach (double en in new[] { 120.0, 660.0, 1332.0 })
        {
            double now = power.ChannelToFwhm(en / active.Gain) * active.Gain;
            double dev = (now / Fkev(en) - 1) * 100;
            worst = Math.Max(worst, Math.Abs(dev));
            sb.Append(en.ToString("0", Inv) + " кэВ " + now.ToString("0.0", Inv) + " (" + dev.ToString("+0.0;-0.0", Inv) + " %)  ");
        }
        Console.WriteLine("    степенная на опорах после сбора: принята " + okp.ToString() + "; " + sb.ToString().TrimEnd());
        if (okp && worst > 10.0)
            Defect("C[" + label.Substring(0, 1) + "]: кривая после сбора искажена на " + worst.ToString("0.0", Inv) + " % (чужие опоры в чужих единицах)");

        // Строка человеку.
        string expected = null;
        string tpl = ViaProperty(Key);
        if (tpl != null) expected = string.Format(Inv, tpl, foreignCount);
        Console.WriteLine("    сказано человеку: " + (said.Length == 0 ? "<ничего>" : said.Replace("\r", "").Replace("\n", " | ")));
        if (foreignCount > 0)
        {
            bool hasLine = said.Length > 0 && (expected == null ? said.Contains("BecqMoni") : said.Contains(expected));
            if (!hasLine) Defect("C[" + label.Substring(0, 1) + "]: опоры других сеток не взяты/взяты молча — строки с их числом (" + foreignCount.ToString(Inv) + ") нет");
        }
        else if (said.Length > 0)
        {
            Defect("C[" + label.Substring(0, 1) + "]: пропуска не было, а человеку сказано: " + said);
        }
        foreach (Stand s in stands) s.Drop();
    }

    static SimpleSqrtFwhmCalibration Calibrate(Scene d, string who)
    {
        var c = new SimpleSqrtFwhmCalibration();
        foreach (int ch in d.Chs) c.CalibrationPeaks.Add(new CalibrationPeak { Channel = ch, Energy = ch * d.Gain, FWHM = Fkev(ch * d.Gain) / d.Gain });
        Control(c.PerformCalibration(d.N), "C: кривая " + who + " документа не подогналась");
        return c;
    }

    // ================================================================== N — документ без спектра

    static void ArmNoSpectrum()
    {
        Console.WriteLine("== N. Активный документ без спектра: сбор не падает и ничего не меняет");
        DocumentManager.GetInstance().DocumentList.Clear();
        Stand a = Stand.Make("p321-nospectrum", 1024, 3.0);
        a.Rd.FwhmCalibration = Calibrate(new Scene(1024, 3.0, 40, 220, 444), "без спектра");
        int before = a.Rd.FwhmCalibration.CalibrationPeaks.Count;
        a.Rd.EnergySpectrum = null;
        try
        {
            a.Invoke("GetAllPeaksButton_Click", null, EventArgs.Empty);
            int after = a.Rd.FwhmCalibration.CalibrationPeaks.Count;
            Console.WriteLine("  опор до " + before.ToString(Inv) + ", после " + after.ToString(Inv));
            if (after != before) Defect("N: сбор без спектра изменил опоры");
        }
        catch (Exception ex)
        {
            Defect("N: сбор без спектра упал: " + Inner(ex));
        }
        a.Drop();
        Console.WriteLine();
    }

    // ================================================================== стенд

    sealed class Stand
    {
        public DocEnergySpectrum Doc;
        public ResultData Rd;
        public MainForm Form;
        public DCFwhmCalibrationView View;

        public static Stand Make(string name, int channels, double kevPerChannel)
        {
            DocEnergySpectrum doc = DocumentManager.GetInstance().CreateDocument(name + ".xml");
            if (doc == null) throw new InvalidOperationException("документ " + name + " не создан");
            ResultData rd = doc.ActiveResultData;
            var es = new EnergySpectrum(1.0, channels);
            es.EnergyCalibration = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new[] { 0.0, kevPerChannel } };
            for (int i = 0; i < channels; i++)
            {
                double d = (i - channels / 4.0) / 8.0;
                es.Spectrum[i] = 20 + (int)(4000.0 * Math.Exp(-0.5 * d * d));
            }
            es.MeasurementTime = 300.0;
            es.TotalPulseCount = es.Spectrum.Sum(x => (long)x);
            rd.EnergySpectrum = es;

            MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "activeDocument", doc);
            SetField(form, "documentManager", DocumentManager.GetInstance());
            try { SetField(form, "dcPeakDetectionView", new DCPeakDetectionView(form)); } catch (Exception) { }
            return new Stand { Doc = doc, Rd = rd, Form = form, View = new DCFwhmCalibrationView(form) };
        }

        public object Invoke(string method, params object[] args)
        {
            MethodInfo mi = typeof(DCFwhmCalibrationView).GetMethod(method, F);
            if (mi == null) throw new MissingMethodException(method);
            try { return mi.Invoke(View, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }

        public void Drop()
        {
            DocumentManager.GetInstance().DocumentList.Remove(Doc);
        }
    }

    // ================================================================== мелочь

    static void SetField(object o, string name, object value)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(o, value); return; }
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static string Inner(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        string at = ex.StackTrace == null ? "" : ex.StackTrace.Split('\n')[0].Trim();
        return ex.GetType().Name + " " + at;
    }

    static void Defect(string text)
    {
        defect++;
        Console.WriteLine("  ДЕФЕКТ: " + text);
    }

    static void Control(bool ok, string text)
    {
        if (ok) return;
        control++;
        Console.WriteLine("  КОНТРОЛЬ НЕ СОШЁЛСЯ: " + text);
    }
}
