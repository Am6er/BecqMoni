// Проба полосы П298 (14.10.2026): «ПУСК» ДОКУМЕНТА С НЕИЗВЕСТНЫМ НАЧАЛОМ (метка 1970,
// `ResultData.UnknownStartTime`, правило `A207`) у AmplitudaSerial и AmplitudaUSB.
// Решение Amber 14.10.2026 вопросником, дословно: «Начало = сейчас − набранное время
// (Рекомендую)» — «То же правило, что ты выбрала утром, только за конец берётся момент
// «Пуска». Длина набора будет честной. Только для файла с меткой 1970 у
// AmplitudaSerial/USB.» Время пробы, если оно тоже метка, — начало (правило П263).
//
// Без окон и без настоящих портов. AmplitudaSerial — НАСТОЯЩЕЙ дверью прибора
// (StartMeasurement → StopMeasurement), порт подменён молчащим блоком через
// `AmplitudaSerialLine.PortFactory` (ни один COM-порт не открывается). AmplitudaUSB
// настоящей дверью без прибора не пускается (читатель HID ищет устройство сам, шва нет):
// правило берётся отражением (`ResumeUnknownStart`), а что `StartCore` его зовёт —
// разбором IL. Один exe пробы работает и со сборкой ДО правки (обязан показать дефект),
// и ПОСЛЕ.
//
//   ResumeUnknownStartProbeP298.exe       (из каталога, где лежит BecquerelMonitor.exe)
//
// Расклады AmplitudaSerial (набрано 600 с, отсчёты 123456, если не сказано иначе):
//   S1 начало = проба = 1970                      — ПРАВКА: начало = пуск − 600 с, проба = начало;
//                                                   после «Стопа» множитель распада ≈ 1;
//   S4 начало 1970, проба известна                — ПРАВКА: начало = пуск − 600 с, проба та же;
//   S5 начало = проба = 1970, отсчёты есть, время 0 — ПРАВКА: начало = пуск;
//   S6 начало = проба = 1970, отсчётов нет, время 600 с — ПРАВКА: начало = пуск − 600 с;
//   S2 чистый документ (0 отсчётов, 0 с), метка 1970 — КОНТРОЛЬ: начало = «сейчас», проба не
//                                                   тронута (как до правки, обе сборки);
//   S3 известное начало                           — КОНТРОЛЬ: начало и проба побайтно те же.
// AmplitudaUSB: те же S1/S4/S5/S6 правилом отражением; S2/S3 — ветки до правила (код).
// Правило обеих дверей — одно: сетка (время × проба) даёт побитно одно и то же.
// Запись: документ S1 после «Стопа», S2 и S3 — XmlSerializer(ResultDataFile), как
// сохраняет приложение; состав элементов и вид записи начала сличаются.
//
// Коды: 0 — правило выполнено и контроли сработали (так обязана отвечать сборка ПОСЛЕ);
// 1 — дефект виден (так обязана отвечать сборка ДО); 3 — контроль НЕ сработал (замер
// пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using BecquerelMonitor;

static class ResumeUnknownStartProbeP298
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double CsHalfLifeYears = 30.08;
    const double CoHalfLifeYears = 5.2714;
    const double Held = 600.0;
    const long HeldCounts = 123456;
    static readonly DateTime Unknown = ResultData.UnknownStartTime;
    static readonly DateTime KnownStart = new DateTime(2024, 2, 7, 12, 28, 49, DateTimeKind.Unspecified);
    static readonly DateTime KnownSample = new DateTime(2024, 2, 7, 9, 0, 0, DateTimeKind.Unspecified);
    static int defect, control, portSeq;

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
            MethodInfo serialRule = Rule(typeof(AmplitudaSerialDeviceController));
            MethodInfo usbRule = Rule(typeof(AmplitudaUsbDeviceController));
            Console.WriteLine("правило ResumeUnknownStart: Serial {0}, USB {1}",
                serialRule != null ? "есть" : "НЕТ", usbRule != null ? "есть" : "НЕТ");
            bool serialCalls = serialRule != null && Calls(typeof(AmplitudaSerialDeviceController), serialRule);
            bool usbCalls = usbRule != null && Calls(typeof(AmplitudaUsbDeviceController), usbRule);
            Console.WriteLine("StartCore зовёт правило (IL): Serial {0}, USB {1}", serialCalls ? "да" : "нет", usbCalls ? "да" : "нет");
            Expect(serialCalls, "Serial: StartCore не зовёт ResumeUnknownStart");
            Expect(usbCalls, "USB: StartCore не зовёт ResumeUnknownStart");
            Console.WriteLine();

            AmplitudaSerialLine.PortFactory = s => new SilentPort();
            Serial();
            Console.WriteLine();
            Usb(usbRule);
            Console.WriteLine();
            SameRule(serialRule, usbRule);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine("ОБРЫВ: " + ex);
            return 5;
        }
        Console.WriteLine("ИТОГ: дефектов {0}, несработавших контролей {1}", defect, control);
        if (control > 0) return 3;
        return defect > 0 ? 1 : 0;
    }

    // ---------------------------------------------------------------- AmplitudaSerial

    sealed class Outcome
    {
        public DateTime T0, T1, Start, Sample, End;
        public double Real;
        public bool Started;
        public string Xml;
    }

    static void Serial()
    {
        Console.WriteLine("== AmplitudaSerial — дверь прибора (StartMeasurement/StopMeasurement), молчащий блок вместо порта");

        // S1 — главный расклад, с «Стопом» и записью.
        Outcome s1 = RunSerial(NewDoc(Unknown, Unknown, HeldCounts, Held), true);
        Print("S1 метка 1970, проба 1970, 600 с", s1);
        bool s1ok = Within(s1.Start, s1.T0.AddSeconds(-Held), s1.T1.AddSeconds(-Held)) && s1.Sample == s1.Start;
        Expect(s1ok, "S1: ждали начало = пуск − 600 с и пробу = начало, а начало " + Show(s1.Start) + ", проба " + Show(s1.Sample));
        double cs1 = Factor(CsHalfLifeYears, s1), co1 = Factor(CoHalfLifeYears, s1);
        Console.WriteLine("    после «Стопа»: конец {0}, реальное время {1} с, длина набора по датам {2}, Cs-137 ×{3}, Co-60 ×{4}",
            Show(s1.End), s1.Real.ToString("0.###", Inv), Span(s1.End - s1.Start),
            cs1.ToString("0.000000", Inv), co1.ToString("0.000000", Inv));
        Expect(Math.Abs(cs1 - 1.0) < 1e-4 && Math.Abs(co1 - 1.0) < 1e-4, "S1: множитель распада не ≈ 1 (Cs-137 ×" + cs1.ToString("0.000000", Inv) + ")");
        Expect(s1.Start.Kind == DateTimeKind.Local, "S1: вид начала " + s1.Start.Kind + ", а у «сейчас» чистого пуска — Local");

        // S4 — проба известна.
        Outcome s4 = RunSerial(NewDoc(Unknown, KnownSample, HeldCounts, Held), false);
        Print("S4 метка 1970, проба известна, 600 с", s4);
        Expect(Within(s4.Start, s4.T0.AddSeconds(-Held), s4.T1.AddSeconds(-Held)) && Same(s4.Sample, KnownSample),
            "S4: ждали начало = пуск − 600 с и пробу прежней");

        // S5 — отсчёты есть, времени нет.
        Outcome s5 = RunSerial(NewDoc(Unknown, Unknown, HeldCounts, 0.0), false);
        Print("S5 метка 1970, отсчёты есть, время 0", s5);
        Expect(Within(s5.Start, s5.T0, s5.T1) && s5.Sample == s5.Start, "S5: ждали начало = пуск и пробу = начало");

        // S6 — время есть, отсчётов нет.
        Outcome s6 = RunSerial(NewDoc(Unknown, Unknown, 0, Held), false);
        Print("S6 метка 1970, отсчётов нет, 600 с", s6);
        Expect(Within(s6.Start, s6.T0.AddSeconds(-Held), s6.T1.AddSeconds(-Held)) && s6.Sample == s6.Start,
            "S6: ждали начало = пуск − 600 с и пробу = начало");

        // S2 — чистый документ: КОНТРОЛЬ, как до правки.
        Outcome s2 = RunSerial(NewDoc(Unknown, Unknown, 0, 0.0), false, true);
        Print("S2 КОНТРОЛЬ чистый документ с меткой 1970", s2);
        Control(Within(s2.Start, s2.T0, s2.T1) && s2.Sample == Unknown && s2.Start.Kind == DateTimeKind.Local,
            "S2: чистый документ — ждали начало = «сейчас» и пробу нетронутой (1970)");

        // S3 — известное начало: КОНТРОЛЬ, побайтно как было.
        Outcome s3 = RunSerial(NewDoc(KnownStart, KnownSample, HeldCounts, Held), false, true);
        Print("S3 КОНТРОЛЬ известное начало", s3);
        Control(Same(s3.Start, KnownStart) && Same(s3.Sample, KnownSample), "S3: известное начало или проба изменились");

        // Запись: как сохраняет приложение.
        Console.WriteLine("  запись (XmlSerializer(ResultDataFile)):");
        string fmt = @"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?[+-]\d\d:\d\d$";
        string st1 = Element(s1.Xml, "StartTime"), st2 = Element(s2.Xml, "StartTime"), st3 = Element(s3.Xml, "StartTime");
        Console.WriteLine("    S1 <StartTime>{0}</StartTime>  <EndTime>{1}</EndTime>  <Time>{2}</Time>", st1, Element(s1.Xml, "EndTime"), Element(s1.Xml, "Time"));
        Console.WriteLine("    S2 <StartTime>{0}</StartTime>  (чистый пуск, «сейчас»)", st2);
        Console.WriteLine("    S3 <StartTime>{0}</StartTime>  (известное начало, продолжение)", st3);
        bool sameShape = Regex.IsMatch(st1 ?? "", fmt) == Regex.IsMatch(st2 ?? "", fmt);
        Control(Regex.IsMatch(st2 ?? "", fmt), "S2: запись начала чистого пуска не в виде с поясом — образец вида пуст");
        Expect(Regex.IsMatch(st1 ?? "", fmt) && sameShape, "S1: начало записано не тем видом, что «сейчас» чистого пуска: " + st1);
        string e1 = Shape(s1.Xml), e2 = Shape(s2.Xml), e3 = Shape(s3.Xml);
        Console.WriteLine("    состав элементов ResultData: S1 = S3 {0}, S1 = S2 {1} ({2} элементов)", e1 == e3 ? "да" : "НЕТ", e1 == e2 ? "да" : "НЕТ",
            e1.Split('/').Length.ToString(Inv));
        Control(e2 == e3, "S2 и S3: состав элементов записи разный — образец пуст");
        Expect(e1 == e3, "S1: состав элементов записи не тот, что у обычного продолжения");
        // Круг: записанное начало читается тем же моментом, признак «время было в файле» встаёт.
        ResultDataFile back = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(new StringReader(s1.Xml));
        ResultData rb = back.ResultDataList[0];
        Console.WriteLine("    круг S1: начало {0}, StartTimeInFile {1}, проба {2}", Show(rb.StartTime), rb.StartTimeInFile, Show(rb.SampleInfo.Time));
        Expect(rb.StartTimeInFile && rb.StartTime == s1.Start && rb.SampleInfo.Time == s1.Sample, "S1: круг записи меняет начало или пробу");
    }

    static ResultData NewDoc(DateTime start, DateTime sample, long counts, double real)
    {
        ResultData rd = new ResultData();
        DeviceConfigInfo info = new DeviceConfigInfo();
        AmplitudaSerialDeviceConfig cfg = new AmplitudaSerialDeviceConfig();
        portSeq++;
        cfg.PortName = "P298FAKE" + portSeq.ToString(Inv);     // подменный порт: COM не открывается
        info.InputDeviceConfig = cfg;
        rd.DeviceConfig = info;                                 // Guid = null: менеджер конфигураций не нужен
        EnergySpectrum es = new EnergySpectrum(1.0, 1024);
        if (counts > 0)
        {
            es.Spectrum[100] = (int)counts;
        }
        es.TotalPulseCount = counts;
        es.ValidPulseCount = counts;
        es.MeasurementTime = real;
        es.LiveTime = real * 0.99;
        rd.EnergySpectrum = es;
        rd.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(real);
        rd.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(real);
        rd.StartTime = start;
        rd.EndTime = start.AddSeconds(real);
        rd.SampleInfo.Time = sample;
        return rd;
    }

    static Outcome RunSerial(ResultData rd, bool stop, bool xml = false)
    {
        AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
        Outcome o = new Outcome();
        try
        {
            o.T0 = DateTime.Now;
            o.Started = c.StartMeasurement(rd);
            o.T1 = DateTime.Now;
            o.Start = rd.StartTime;
            o.Sample = rd.SampleInfo.Time;
            if (stop)
            {
                c.StopMeasurement(rd);
            }
        }
        finally
        {
            c.Dispose();
        }
        o.End = rd.EndTime;
        o.Real = rd.EnergySpectrum.MeasurementTime;
        if (stop || xml)
        {
            ResultDataFile file = new ResultDataFile();
            file.InitFormatVersion();
            file.ResultDataList.Add(rd);
            StringWriter w = new StringWriter(Inv);
            new XmlSerializer(typeof(ResultDataFile)).Serialize(w, file);
            o.Xml = w.ToString();
        }
        Expect(o.Started, "StartMeasurement вернул false");
        return o;
    }

    sealed class SilentPort : IAmplitudaSerialPort
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        public void Open() { }
        public void Close() { }
        public void Write(byte value) { }
        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            Thread.Sleep(Math.Max(0, Math.Min(timeoutMs, 20)));
            return 0;
        }
        public void DiscardInput() { }
        public void Sleep(int milliseconds) { if (milliseconds > 0) Thread.Sleep(milliseconds); }
        public long NowMs { get { return clock.ElapsedMilliseconds; } }
        public void Dispose() { }
    }

    // ---------------------------------------------------------------- AmplitudaUSB

    static void Usb(MethodInfo usbRule)
    {
        Console.WriteLine("== AmplitudaUSB — правило отражением (дверь прибора без HID-устройства не пускается)");
        DateTime pressed = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Local);
        if (usbRule == null)
        {
            // До правки ветки нет: начало остаётся 1970, «Стоп» ставит конец по часам.
            double cs = MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, Unknown, Unknown, pressed.AddSeconds(Held));
            double co = MeasurementResultManager.DecayToSamplingFactor(CoHalfLifeYears, Unknown, Unknown, pressed.AddSeconds(Held));
            Console.WriteLine("  правила нет: S1 начало остаётся 1970; конец = «Стоп» → набор {0}, Cs-137 ×{1}, Co-60 ×{2} (по коду до правки, не дверью)",
                Span(pressed.AddSeconds(Held) - Unknown), cs.ToString("0.000000", Inv), co.ToString("0.000000", Inv));
            Expect(false, "USB: правила нет — документ с меткой 1970 продолжается от 1970");
            return;
        }
        ResultData d1 = NewDoc(Unknown, Unknown, HeldCounts, Held);
        usbRule.Invoke(null, new object[] { d1, Held, pressed });
        DateTime end = pressed.AddSeconds(Held);       // «Стоп» через ещё 600 с набора
        double f1 = MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, d1.SampleInfo.Time, d1.StartTime, end);
        Console.WriteLine("  S1 пуск {0}: начало {1}, проба {2}; «Стоп» через 600 с → набор {3}, Cs-137 ×{4}",
            Show(pressed), Show(d1.StartTime), Show(d1.SampleInfo.Time), Span(end - d1.StartTime), f1.ToString("0.000000", Inv));
        Expect(d1.StartTime == pressed.AddSeconds(-Held) && d1.SampleInfo.Time == d1.StartTime && Math.Abs(f1 - 1.0) < 1e-4,
            "USB S1: ждали начало = пуск − 600 с, пробу = начало, множитель ≈ 1");
        ResultData d4 = NewDoc(Unknown, KnownSample, HeldCounts, Held);
        usbRule.Invoke(null, new object[] { d4, Held, pressed });
        Console.WriteLine("  S4: начало {0}, проба {1}", Show(d4.StartTime), Show(d4.SampleInfo.Time));
        Expect(d4.StartTime == pressed.AddSeconds(-Held) && Same(d4.SampleInfo.Time, KnownSample), "USB S4");
        ResultData d5 = NewDoc(Unknown, Unknown, HeldCounts, 0.0);
        usbRule.Invoke(null, new object[] { d5, 0.0, pressed });
        Console.WriteLine("  S5: начало {0}, проба {1}", Show(d5.StartTime), Show(d5.SampleInfo.Time));
        Expect(d5.StartTime == pressed && d5.SampleInfo.Time == pressed, "USB S5");
        // Источник набранного времени у USB — status.TotalTime в микросекундах (как в StartCore).
        long micros = TimeSpan.FromSeconds(Held).Ticks / 10;
        Console.WriteLine("  набранное USB: status.TotalTime 600 с → {0} мкс → {1} с", micros.ToString(Inv), (micros / 1.0E+06).ToString("0.######", Inv));
        Expect(micros / 1.0E+06 == Held, "USB: перевод микросекунд в секунды не точен");
    }

    // ---------------------------------------------------------------- одно правило

    static void SameRule(MethodInfo serialRule, MethodInfo usbRule)
    {
        Console.WriteLine("== Одно правило у двух дверей: сетка (набранное время × проба), пуск 2026-10-14 12:00:00 Local");
        if (serialRule == null || usbRule == null)
        {
            Console.WriteLine("  правила нет — сличать нечего");
            return;
        }
        DateTime pressed = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Local);
        double[] grid = { 0.0, 0.5, Held, 1.0e9, 1.0e13, -1.0, double.NaN, double.PositiveInfinity };
        int diff = 0;
        foreach (double t in grid)
        {
            foreach (DateTime sample in new[] { Unknown, KnownSample })
            {
                ResultData a = NewDoc(Unknown, sample, HeldCounts, 0.0);
                ResultData b = NewDoc(Unknown, sample, HeldCounts, 0.0);
                serialRule.Invoke(null, new object[] { a, t, pressed });
                usbRule.Invoke(null, new object[] { b, t, pressed });
                bool same = Same(a.StartTime, b.StartTime) && Same(a.SampleInfo.Time, b.SampleInfo.Time);
                if (!same) diff++;
                if (sample == Unknown)
                {
                    Console.WriteLine("  {0,8} с: начало {1}, проба {2}{3}", t.ToString("G4", Inv), Show(a.StartTime), Show(a.SampleInfo.Time), same ? "" : "  ≠ USB");
                }
            }
        }
        Expect(diff == 0, "правила Serial и USB расходятся в " + diff.ToString(Inv) + " точках");
        // Неотнимаемое время оставляет метку.
        foreach (double t in new[] { -1.0, double.NaN, double.PositiveInfinity, 1.0e13 })
        {
            ResultData a = NewDoc(Unknown, Unknown, HeldCounts, 0.0);
            serialRule.Invoke(null, new object[] { a, t, pressed });
            Expect(a.StartTime == Unknown && a.SampleInfo.Time == Unknown, "время " + t.ToString("G4", Inv) + " с: метка 1970 не сохранилась");
        }
    }

    // ---------------------------------------------------------------- служебное

    static MethodInfo Rule(Type t)
    {
        return t.GetMethod("ResumeUnknownStart", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }

    // Зовёт ли StartCore правило: перебор кодов call (0x28) в IL и разрешение токена.
    static bool Calls(Type t, MethodInfo callee)
    {
        MethodInfo core = t.GetMethod("StartCore", BindingFlags.Instance | BindingFlags.NonPublic);
        if (core == null) return false;
        byte[] il = core.GetMethodBody().GetILAsByteArray();
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28) continue;
            try
            {
                if (core.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)) == callee) return true;
            }
            catch (ArgumentException) { }
        }
        return false;
    }

    static double Factor(double halfLife, Outcome o)
    {
        return MeasurementResultManager.DecayToSamplingFactor(halfLife, o.Sample, o.Start, o.End);
    }

    static bool Within(DateTime x, DateTime lo, DateTime hi)
    {
        return x >= lo && x <= hi;
    }

    static bool Same(DateTime a, DateTime b)
    {
        return a.Ticks == b.Ticks && a.Kind == b.Kind;
    }

    static string Show(DateTime d)
    {
        return d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv) + " " + d.Kind;
    }

    static string Span(TimeSpan s)
    {
        double days = s.TotalDays;
        return days > 365.0 ? (days / 365.0).ToString("0.00", Inv) + " лет" : s.TotalSeconds.ToString("0.000", Inv) + " с";
    }

    static void Print(string name, Outcome o)
    {
        Console.WriteLine("  {0}: пуск [{1} … {2}], начало {3}, проба {4}, пуск − начало {5} с",
            name, o.T0.ToString("HH:mm:ss.fff", Inv), o.T1.ToString("HH:mm:ss.fff", Inv), Show(o.Start), Show(o.Sample),
            (o.T1 - o.Start).TotalSeconds.ToString("0.000", Inv));
    }

    static string Element(string xml, string name)
    {
        if (xml == null) return null;
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xml);
        XmlNode n = doc.SelectSingleNode("//ResultData/" + name) ?? doc.SelectSingleNode("//ResultData/SampleInfo/" + name);
        return n != null ? n.InnerText : null;
    }

    // Имена дочерних элементов ResultData и SampleInfo по порядку — «состав записи».
    static string Shape(string xml)
    {
        if (xml == null) return "";
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xml);
        List<string> names = new List<string>();
        foreach (XmlNode n in doc.SelectSingleNode("//ResultData").ChildNodes)
        {
            names.Add(n.Name);
            if (n.Name == "SampleInfo")
            {
                foreach (XmlNode m in n.ChildNodes) names.Add("SampleInfo." + m.Name);
            }
        }
        return string.Join("/", names);
    }

    static void Expect(bool ok, string what)
    {
        if (ok) return;
        defect++;
        Console.WriteLine("    ДЕФЕКТ: " + what);
    }

    static void Control(bool ok, string what)
    {
        if (ok) return;
        control++;
        Console.WriteLine("    КОНТРОЛЬ НЕ СРАБОТАЛ: " + what);
    }
}
