// Проба полосы П300 (14.10.2026): «ОЧИСТИТЬ» → «ПУСК» ДОКУМЕНТА С МЕТКОЙ 1970 В НАЧАЛЕ И
// ВО ВРЕМЕНИ ПРОБЫ (`ResultData.UnknownStartTime`, правило `A207`). Остаток полосы П298
// (её контроль S2): начало становится «сейчас», время пробы остаётся 1970, и поправка на
// распад к дате отбора (`MeasurementResultManager.DecayToSamplingFactor`) считает задержку
// пробы ≈ 56.8 года — Cs-137 ×3.70, Co-60 ×1758.
//
// Правило (умолчание распорядителя по принятым решениям П263 и П298, не новое решение
// Amber): начало известно, проба — метка → проба = начало; известная проба не трогается
// НИКОГДА. Место одно — `MeasurementController.SampleTimeFollowsStart`, его зовут
// `StartRecording`, `AttachToDevice` и `ClearMeasurementResult` после прибора (`finally`).
//
// Без окон, без настоящих портов и BLE. Пуск — НАСТОЯЩЕЙ дверью `MeasurementController`
// (аренда прибора, `CreateDeviceController`, `StartMeasurement` прибора):
//   * AmplitudaSerial — порт подменён молчащим блоком (`AmplitudaSerialLine.PortFactory`),
//     как у П298; «Стоп» — тоже дверью;
//   * AtomSpectra — экземпляр `AtomSpectraVCPIn` заведён БЕЗ конструктора (без потока и
//     без порта, как у П267), ответы прибора «-ok» кладёт поток пробы; ⛔ COM4 (живой
//     Atom Spectra) не открывается: порт не открывает никто, имя порта подменное;
//   * RadiaCode, Obsidian — экземпляры BLE без конструктора (без потоков), как у П267;
//     «Стоп» не зовётся (он рвёт связь BLE), конец = начало + 600 с, как «Стоп» П298 у USB;
//   * AmplitudaUSB и звуковой вход дверью без железа не пускаются (поиск HID; открытие
//     WaveIn и громкость системы): правило — отражением на том состоянии, которое оставляет
//     их чистый пуск (`StartTime = DateTime.Now`), а что дверь зовёт правило для ЛЮБОГО
//     прибора — разбором IL `MeasurementController`.
// «Очистить» — как `DCControlPanel.ClearMeasurementResult` без окна-вопроса: дверь
// `MeasurementController.ClearMeasurementResult`, обнуление времён, `ClearSpectrum`.
//
//   ClearStartUnknownSampleProbeP300.exe   (из каталога, где лежит BecquerelMonitor.exe)
//
// Коды: 0 — правило выполнено и контроли сработали (так обязана отвечать сборка ПОСЛЕ);
// 1 — дефект виден (так обязана отвечать сборка ДО); 3 — контроль НЕ сработал (замер пуст);
// 5 — обрыв.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Xml.Serialization;
using BecquerelMonitor;

static class ClearStartUnknownSampleProbeP300
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly App = typeof(ResultData).Assembly;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double CsHalfLifeYears = 30.08;
    const double CoHalfLifeYears = 5.2714;
    const double Held = 600.0;
    const long HeldCounts = 123456;
    static readonly DateTime Unknown = ResultData.UnknownStartTime;
    static readonly DateTime KnownStart = new DateTime(2024, 2, 7, 12, 28, 49, DateTimeKind.Unspecified);
    static readonly DateTime KnownSample = new DateTime(2024, 2, 7, 9, 0, 0, DateTimeKind.Unspecified);
    static int defect, control, seq;
    static MethodInfo rule;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = Inv;
        CultureInfo.DefaultThreadCurrentCulture = Inv;
        try
        {
            Console.WriteLine(ProbeTargetFramework.Describe());
            Console.WriteLine("приложение: " + App.Location);
            rule = typeof(MeasurementController).GetMethod("SampleTimeFollowsStart", S);
            Console.WriteLine("правило MeasurementController.SampleTimeFollowsStart: {0}", rule != null ? "есть" : "НЕТ");
            // nameof, а не строки: сторож check_headless читает имена в литералах пробы как вызов
            // отражением и находил бы одноимённые методы DCControlPanel (с окном-вопросом).
            foreach (string door in new[] { nameof(MeasurementController.StartRecording),
                                            nameof(MeasurementController.AttachToDevice),
                                            nameof(MeasurementController.ClearMeasurementResult) })
            {
                bool calls = rule != null && Calls(typeof(MeasurementController).GetMethod(door, F), rule);
                Console.WriteLine("  {0} зовёт правило (IL): {1}", door, calls ? "да" : "нет");
                Expect(calls, door + " не зовёт SampleTimeFollowsStart");
            }
            Console.WriteLine();

            Setup();
            Serial();
            Console.WriteLine();
            Atom();
            Console.WriteLine();
            Ble("RadiaCode", "BecquerelMonitor.RadiaCodeDeviceController", "BecquerelMonitor.RadiaCodeIn", new RadiaCodeDeviceConfig());
            Console.WriteLine();
            Ble("Obsidian", "BecquerelMonitor.ObsidianDeviceController", "BecquerelMonitor.ObsidianIn", new ObsidianDeviceConfig());
            Console.WriteLine();
            ClearWithoutStart();
            Console.WriteLine();
            UsbAndAudio();
            Console.WriteLine();
            RuleGrid();
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

    // ---------------------------------------------------------------- обстановка

    static void Setup()
    {
        DeviceType.InitializeDeviceTypes();
        // Список приборов не читается с диска: проба кладёт в него свои записи сама.
        object manager = typeof(DeviceConfigManager).GetField("instance", S).GetValue(null);
        typeof(DeviceConfigManager).GetField("listLoaded", F).SetValue(manager, true);
        AmplitudaSerialLine.PortFactory = s => new SilentPort();
    }

    static DeviceConfigInfo Register(string type, InputDeviceConfig cfg)
    {
        seq++;
        DeviceConfigInfo info = new DeviceConfigInfo();
        info.Guid = "p300-" + type + "-" + seq.ToString(Inv);
        info.DeviceType = type;
        info.Name = "P300 " + type;
        info.InputDeviceConfig = cfg;
        DeviceConfigManager.GetInstance().DeviceConfigMap[info.Guid] = info;
        return info;
    }

    static ResultData NewDoc(DeviceConfigInfo info, DateTime start, DateTime sample, long counts, double real)
    {
        ResultData rd = new ResultData();
        rd.DeviceConfig = info;
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
        rd.ResultDataStatus.PresetTime = 1000000;
        rd.StartTime = start;
        rd.EndTime = start.AddSeconds(real);
        rd.SampleInfo.Time = sample;
        rd.MeasurementController = new MeasurementController(null, rd);
        return rd;
    }

    // «Очистить» — как DCControlPanel.ClearMeasurementResult, без окна-вопроса и без показа.
    static void Clear(ResultData rd)
    {
        rd.MeasurementController.ClearMeasurementResult();
        ResultDataStatus st = rd.ResultDataStatus;
        st.TotalTime = TimeSpan.FromSeconds(0.0);
        st.ElapsedTime = TimeSpan.FromSeconds(0.0);
        rd.EnergySpectrum.Initialize();                 // DocEnergySpectrum.ClearSpectrum
        rd.PulseCollection.Pulses.Clear();
        rd.CountRates.Clear();
        st.TotalTime = TimeSpan.FromSeconds(0.0);
    }

    sealed class Outcome
    {
        public DateTime T0, T1, Start, Sample, End;
        public bool Started;
        public string Failure;
    }

    static Outcome Start(ResultData rd)
    {
        Outcome o = new Outcome();
        o.T0 = DateTime.Now;
        try
        {
            o.Started = rd.MeasurementController.StartRecording();
        }
        catch (Exception ex)
        {
            o.Failure = ex.GetType().Name + ": " + ex.Message;
        }
        o.T1 = DateTime.Now;
        o.Start = rd.StartTime;
        o.Sample = rd.SampleInfo.Time;
        o.End = rd.EndTime;
        return o;
    }

    static void Dispose(ResultData rd)
    {
        IDisposable d = rd.MeasurementController.DeviceController as IDisposable;
        if (d != null)
        {
            try { d.Dispose(); } catch (Exception ex) { Console.WriteLine("    (Dispose: " + ex.Message + ")"); }
        }
    }

    // ---------------------------------------------------------------- AmplitudaSerial

    static void Serial()
    {
        Console.WriteLine("== AmplitudaSerial — дверь MeasurementController, молчащий блок вместо порта");

        // S1 — главный расклад: файл без всех трёх времён → «Очистить» → «Пуск» → «Стоп».
        ResultData d1 = NewSerialDoc(Unknown, Unknown, HeldCounts, Held);
        Clear(d1);
        Outcome s1 = Start(d1);
        d1.MeasurementController.StopRecording();
        s1.End = d1.EndTime;
        Dispose(d1);
        Print("S1 метка 1970 в начале и пробе → Очистить → Пуск → Стоп", s1);
        Expect(s1.Started, "S1: Пуск не состоялся: " + s1.Failure);
        Expect(Within(s1.Start, s1.T0, s1.T1) && Same(s1.Sample, s1.Start),
            "S1: ждали начало = пуск и пробу = начало, а проба " + Show(s1.Sample));
        Factors("S1", s1);
        // Запись: проба пишется тем же видом, что начало, и читается тем же мгновением.
        string xml = Write(d1);
        ResultData back = ((ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(new StringReader(xml))).ResultDataList[0];
        Console.WriteLine("    запись S1: <StartTime>{0}</StartTime> <Time>{1}</Time>; круг: проба {2}",
            Element(xml, "StartTime"), Element(xml, "Time"), Show(back.SampleInfo.Time));
        Expect(back.SampleInfo.Time == s1.Sample && back.StartTime == s1.Start, "S1: круг записи меняет начало или пробу");

        // S2 — КОНТРОЛЬ: проба известна — побайтно прежняя.
        ResultData d2 = NewSerialDoc(Unknown, KnownSample, HeldCounts, Held);
        Clear(d2);
        Outcome s2 = Start(d2);
        d2.MeasurementController.StopRecording();
        Dispose(d2);
        Print("S2 КОНТРОЛЬ начало 1970, проба известна", s2);
        Control(s2.Started && Within(s2.Start, s2.T0, s2.T1) && Same(s2.Sample, KnownSample),
            "S2: известная проба изменилась или пуск не состоялся");

        // S3 — КОНТРОЛЬ: документ без меток — как прежде (чистый пуск: начало «сейчас», проба та же).
        ResultData d3 = NewSerialDoc(KnownStart, KnownSample, HeldCounts, Held);
        Clear(d3);
        Outcome s3 = Start(d3);
        d3.MeasurementController.StopRecording();
        Dispose(d3);
        Print("S3 КОНТРОЛЬ документ без меток", s3);
        Control(s3.Started && Within(s3.Start, s3.T0, s3.T1) && Same(s3.Sample, KnownSample),
            "S3: документ без меток: начало не «сейчас» или проба изменилась");

        // S4 — КОНТРОЛЬ П298: продолжение без «Очистить» — начало = пуск − 600 с, проба = начало.
        ResultData d4 = NewSerialDoc(Unknown, Unknown, HeldCounts, Held);
        Outcome s4 = Start(d4);
        d4.MeasurementController.StopRecording();
        Dispose(d4);
        Print("S4 КОНТРОЛЬ П298: продолжение файла с меткой", s4);
        Control(s4.Started && Within(s4.Start, s4.T0.AddSeconds(-Held), s4.T1.AddSeconds(-Held)) && Same(s4.Sample, s4.Start),
            "S4: правило П298 (начало = пуск − 600 с, проба = начало) не выполнено");
    }

    static ResultData NewSerialDoc(DateTime start, DateTime sample, long counts, double real)
    {
        AmplitudaSerialDeviceConfig cfg = new AmplitudaSerialDeviceConfig();
        cfg.PortName = "P300FAKE" + (seq + 1).ToString(Inv);   // подменный порт: COM не открывается
        return NewDoc(Register(AmplitudaSerialDeviceConfig.DeviceTypeId, cfg), start, sample, counts, real);
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

    // ---------------------------------------------------------------- AtomSpectra

    static volatile bool answering;
    static volatile bool responderStop;

    static void Atom()
    {
        Console.WriteLine("== AtomSpectra — дверь MeasurementController, экземпляр прибора без потока и порта, ответы «-ok» — от пробы");
        AtomSpectraDeviceConfig cfg = new AtomSpectraDeviceConfig();
        cfg.ComPortName = "P300FAKE";          // не открывается: потока чтения у экземпляра нет
        cfg.BaudRate = 600000;
        DeviceConfigInfo info = Register("AtomSpectraVCP", cfg);
        object atom = BareAtom(info.Guid);
        Thread responder = new Thread(() => Respond(atom)) { IsBackground = true };
        responder.Start();
        try
        {
            // A1 — главный расклад.
            answering = true;
            ResultData d1 = NewDoc(info, Unknown, Unknown, HeldCounts, Held);
            Clear(d1);
            Outcome a1 = Start(d1);
            d1.MeasurementController.StopRecording();
            a1.End = d1.EndTime;
            Print("A1 метка 1970 в начале и пробе → Очистить → Пуск → Стоп", a1);
            Expect(a1.Started, "A1: Пуск не состоялся: " + a1.Failure);
            Expect(Within(a1.Start, a1.T0, a1.T1) && Same(a1.Sample, a1.Start), "A1: ждали пробу = начало, а проба " + Show(a1.Sample));
            Factors("A1", a1);

            // A2 — КОНТРОЛЬ: известная проба.
            ResultData d2 = NewDoc(info, Unknown, KnownSample, HeldCounts, Held);
            Clear(d2);
            Outcome a2 = Start(d2);
            d2.MeasurementController.StopRecording();
            Print("A2 КОНТРОЛЬ начало 1970, проба известна", a2);
            Control(a2.Started && Within(a2.Start, a2.T0, a2.T1) && Same(a2.Sample, KnownSample), "A2: известная проба изменилась или пуск не состоялся");

            // A3 — КОНТРОЛЬ: документ без меток.
            ResultData d3 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Clear(d3);
            Outcome a3 = Start(d3);
            d3.MeasurementController.StopRecording();
            Print("A3 КОНТРОЛЬ документ без меток", a3);
            Control(a3.Started && Within(a3.Start, a3.T0, a3.T1) && Same(a3.Sample, KnownSample), "A3: документ без меток: начало не «сейчас» или проба изменилась");

            // A4 — прибор молчит, пуск отказан, а начало прибор уже сдвинул: файл с отсчётами.
            answering = false;
            ResultData d4 = NewDoc(info, Unknown, Unknown, HeldCounts, Held);
            Outcome a4 = Start(d4);
            Print("A4 прибор молчит: пуск отказан, отсчёты файла (600 с) на месте", a4);
            Console.WriteLine("    отказ: {0}", a4.Failure ?? (a4.Started ? "нет, пуск состоялся" : "false"));
            // П303 (14.10.2026): отказанный пуск начало больше не сдвигает — обе метки остаются,
            // и это тоже годный исход; контроль — только что пуск отказан, а начало либо «сейчас»
            // (сборка до П303), либо прежняя метка (после).
            Control(!a4.Started && (Within(a4.Start, a4.T0, a4.T1) || Same(a4.Start, Unknown)),
                "A4: ждали отказ пуска с началом «сейчас» (до П303) или прежним (после П303)");
            Expect(Same(a4.Sample, a4.Start), "A4: отказанный пуск сдвинул начало, а проба осталась " + Show(a4.Sample));
            Factors("A4", a4);

            // A6 — НАХОДКА, не правило П300 и на код пробы не влияет: тот же отказанный пуск у
            // файла с ИЗВЕСТНЫМ началом — прибор переписывает начало «сейчас», отсчёты же файловые.
            // Починено полосой П303 (приёмка — `RefusedStartProbeP303`): после неё «не тронуто».
            ResultData d6 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Outcome a6 = Start(d6);
            Print("A6 НАХОДКА: прибор молчит, у файла известное начало", a6);
            double cs6 = Factor(CsHalfLifeYears, a6), cs6was = MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, KnownSample, KnownStart, KnownStart.AddSeconds(Held));
            Console.WriteLine("    начало файла {0} → {1} после отказа ({2}); Cs-137 ×{3} (было ×{4})",
                Show(KnownStart), Show(a6.Start), Same(a6.Start, KnownStart) ? "не тронуто" : "ПЕРЕПИСАНО",
                cs6.ToString("0.000000", Inv), cs6was.ToString("0.000000", Inv));

            // A5 — «Очистить» после «Стопа» (без нового «Пуска»): прибор сдвигает начало.
            answering = true;
            ResultData d5 = NewDoc(info, Unknown, Unknown, HeldCounts, Held);
            Outcome p5 = Start(d5);
            d5.MeasurementController.StopRecording();
            DateTime c0 = DateTime.Now;
            Clear(d5);
            DateTime c1 = DateTime.Now;
            Outcome a5 = new Outcome { T0 = c0, T1 = c1, Start = d5.StartTime, Sample = d5.SampleInfo.Time, End = d5.EndTime, Started = p5.Started };
            Print("A5 Пуск → Стоп → Очистить (без Пуска)", a5);
            Control(p5.Started && Within(a5.Start, c0, c1), "A5: «Очистить» не сдвинул начало (ждали «сейчас»)");
            Expect(a5.Sample != Unknown && Same(a5.Sample, p5.Start),
                "A5: ждали пробу = начало первого Пуска, а проба " + Show(a5.Sample));
            Factors("A5", a5);
        }
        finally
        {
            responderStop = true;
            responder.Join(2000);
            IList list = (IList)typeof(AtomSpectraVCPIn).GetField("instances", S).GetValue(null);
            int foreign = 0;
            lock (typeof(AtomSpectraVCPIn).GetField("instancesLock", S).GetValue(null))
            {
                foreach (object o in list)
                {
                    if (!ReferenceEquals(o, atom)) foreign++;
                }
                list.Remove(atom);
            }
            Console.WriteLine("  экземпляров AtomSpectraVCPIn, заведённых самим приложением: {0} (ждём 0 — ни потока, ни порта)", foreign);
            Control(foreign == 0, "AtomSpectra: приложение завело свой экземпляр прибора — проба вышла за пределы подмены");
        }
    }

    static object BareAtom(string guid)
    {
        Type t = typeof(AtomSpectraVCPIn);
        object o = FormatterServices.GetUninitializedObject(t);
        t.GetField("guid", F).SetValue(o, guid);
        t.GetField("outcomming", F).SetValue(o, new ConcurrentQueue<byte>());
        t.GetField("received_lines", F).SetValue(o, new ConcurrentQueue<string>());
        t.GetField("Lock", F).SetValue(o, new object());
        t.GetField("showLock", F).SetValue(o, new object());
        t.GetField("onDataReceivedEvent", F).SetValue(o, new ManualResetEvent(false));
        t.GetField("thread_alive", F).SetValue(o, true);
        IList list = (IList)t.GetField("instances", S).GetValue(null);
        lock (t.GetField("instancesLock", S).GetValue(null))
        {
            list.Add(o);
        }
        return o;
    }

    // Прибор, который отвечает «-ok» на каждый кадр команды. Кадр кладётся в очередь
    // отправки под `Lock` целиком, поэтому и вынимается под ним же.
    static void Respond(object atom)
    {
        Type t = typeof(AtomSpectraVCPIn);
        ConcurrentQueue<byte> outq = (ConcurrentQueue<byte>)t.GetField("outcomming", F).GetValue(atom);
        ConcurrentQueue<string> lines = (ConcurrentQueue<string>)t.GetField("received_lines", F).GetValue(atom);
        object gate = t.GetField("Lock", F).GetValue(atom);
        while (!responderStop)
        {
            lock (gate)
            {
                if (!outq.IsEmpty)
                {
                    byte b;
                    while (outq.TryDequeue(out b)) { }
                    if (answering)
                    {
                        lines.Enqueue("-ok");
                    }
                }
            }
            Thread.Sleep(1);
        }
    }

    // ---------------------------------------------------------------- RadiaCode, Obsidian

    static void Ble(string device, string controllerType, string inType, InputDeviceConfig cfg)
    {
        Console.WriteLine("== {0} — дверь MeasurementController, экземпляр BLE без потоков; «Стоп» — через 600 с по часам пробы", device);
        cfg.GetType().GetProperty("AddressBLE").SetValue(cfg, "P300-NO-BLE", null);
        cfg.GetType().GetProperty("DeviceSerial").SetValue(cfg, "p300-serial", null);
        DeviceConfigInfo info = Register(device, cfg);
        Type t = App.GetType(inType, true);
        object dev = BareBle(t, info.Guid);
        try
        {
            Outcome r1 = BleRun(info, Unknown, Unknown, true);
            r1.End = r1.Start.AddSeconds(Held);
            Print("R1 метка 1970 в начале и пробе → Очистить → Пуск", r1);
            Expect(r1.Started, device + " R1: Пуск не состоялся: " + r1.Failure);
            Expect(Within(r1.Start, r1.T0, r1.T1) && Same(r1.Sample, r1.Start), device + " R1: ждали пробу = начало, а проба " + Show(r1.Sample));
            Factors(device + " R1", r1);

            Outcome r2 = BleRun(info, Unknown, KnownSample, true);
            Print("R2 КОНТРОЛЬ начало 1970, проба известна", r2);
            Control(r2.Started && Within(r2.Start, r2.T0, r2.T1) && Same(r2.Sample, KnownSample), device + " R2: известная проба изменилась или пуск не состоялся");

            Outcome r3 = BleRun(info, KnownStart, KnownSample, true);
            Print("R3 КОНТРОЛЬ документ без меток", r3);
            Control(r3.Started && Within(r3.Start, r3.T0, r3.T1) && Same(r3.Sample, KnownSample), device + " R3: документ без меток: начало не «сейчас» или проба изменилась");
        }
        finally
        {
            IList list = (IList)t.GetField("instances", S).GetValue(null);
            int foreign = 0;
            lock (t.GetField("instancesLock", S).GetValue(null))
            {
                foreach (object o in list)
                {
                    if (!ReferenceEquals(o, dev)) foreign++;
                }
                list.Remove(dev);
            }
            Console.WriteLine("  экземпляров {0}, заведённых самим приложением: {1} (ждём 0 — ни потоков, ни BLE)", t.Name, foreign);
            Control(foreign == 0, device + ": приложение завело свой экземпляр прибора — проба вышла за пределы подмены");
        }
    }

    static Outcome BleRun(DeviceConfigInfo info, DateTime start, DateTime sample, bool clear)
    {
        ResultData rd = NewDoc(info, start, sample, HeldCounts, Held);
        if (clear)
        {
            Clear(rd);
        }
        Outcome o = Start(rd);
        // «Стоп» прибора рвёт связь BLE — здесь он не зовётся; набор снимается вручную.
        rd.ResultDataStatus.Recording = false;
        Leases().Remove(info.Guid);
        return o;
    }

    static object BareBle(Type t, string guid)
    {
        object o = FormatterServices.GetUninitializedObject(t);
        foreach (FieldInfo f in t.GetFields(F))
        {
            if (f.FieldType == typeof(object)) f.SetValue(o, new object());
            else if (f.Name == "packet") f.SetValue(o, Activator.CreateInstance(f.FieldType, true));
            else if (f.Name == "hystogram_buffered") f.SetValue(o, new int[1024]);
            else if (f.Name == "lastFailure") f.SetValue(o, "");
        }
        t.GetField("guid", F).SetValue(o, guid);
        FieldInfo sf = t.GetField("state", F);
        sf.SetValue(o, Enum.Parse(sf.FieldType, "Connected"));
        t.GetField("thread_alive", F).SetValue(o, true);
        IList list = (IList)t.GetField("instances", S).GetValue(null);
        lock (t.GetField("instancesLock", S).GetValue(null))
        {
            list.Add(o);
        }
        return o;
    }

    static IDictionary Leases()
    {
        return (IDictionary)typeof(MeasurementController).GetField("deviceLeases", S).GetValue(null);
    }

    // ---------------------------------------------------------------- «Очистить» без «Пуска»

    static void ClearWithoutStart()
    {
        Console.WriteLine("== «Очистить» без «Пуска»");

        // C0 — документ только что открыт из файла: контроллера прибора ещё нет, начало не
        // трогается никем. КОНТРОЛЬ: одинаково в обеих сборках.
        ResultData d0 = NewDoc(Register(AmplitudaSerialDeviceConfig.DeviceTypeId, new AmplitudaSerialDeviceConfig()), Unknown, Unknown, HeldCounts, Held);
        Clear(d0);
        Outcome c0 = new Outcome { Start = d0.StartTime, Sample = d0.SampleInfo.Time, End = d0.EndTime };
        Console.WriteLine("  C0 файл с меткой → Очистить: начало {0}, проба {1}, конец {2} (из файла), отсчётов {3}, время {4} с",
            Show(c0.Start), Show(c0.Sample), Show(c0.End), d0.EnergySpectrum.TotalPulseCount.ToString(Inv),
            d0.EnergySpectrum.MeasurementTime.ToString("0.###", Inv));
        double cs0 = Factor(CsHalfLifeYears, c0), co0 = Factor(CoHalfLifeYears, c0);
        Console.WriteLine("    поправка на распад: Cs-137 ×{0}, Co-60 ×{1}", cs0.ToString("0.000000", Inv), co0.ToString("0.000000", Inv));
        Control(c0.Start == Unknown && c0.Sample == Unknown && Math.Abs(cs0 - 1.0) < 1e-4 && Math.Abs(co0 - 1.0) < 1e-4,
            "C0: «Очистить» без контроллера тронул начало или пробу");

        // C1 — страховка в «Очистить»: контроллер прибора есть, начало ещё метка, а «Очистить»
        // прибора ставит начало «сейчас» (RadiaCode, прибор пишет ДРУГОЙ спектр — П267).
        RadiaCodeDeviceConfig cfg = new RadiaCodeDeviceConfig();
        DeviceConfigInfo info = Register("RadiaCode", cfg);
        ResultData d1 = NewDoc(info, Unknown, Unknown, HeldCounts, Held);
        Type ct = App.GetType("BecquerelMonitor.RadiaCodeDeviceController", true);
        object c = Activator.CreateInstance(ct);
        ct.GetField("deviceGuid", F).SetValue(c, info.Guid);
        d1.MeasurementController.DeviceController = (DeviceController)c;
        MeasurementController other = new MeasurementController(null, NewDoc(info, KnownStart, KnownSample, 0, 0.0));
        Leases()[info.Guid] = other;
        DateTime t0 = DateTime.Now;
        Clear(d1);
        DateTime t1 = DateTime.Now;
        Leases().Remove(info.Guid);
        Outcome c1 = new Outcome { T0 = t0, T1 = t1, Start = d1.StartTime, Sample = d1.SampleInfo.Time, End = d1.EndTime };
        Print("C1 контроллер есть, начало — метка, Очистить (прибор пишет другой спектр)", c1);
        Control(Within(c1.Start, t0, t1), "C1: «Очистить» прибора не поставил начало «сейчас» — страховку мерить нечем");
        Expect(Same(c1.Sample, c1.Start), "C1: «Очистить» сдвинул начало, а проба осталась " + Show(c1.Sample));
        Factors("C1", c1);
    }

    // ---------------------------------------------------------------- AmplitudaUSB, звуковой вход

    static void UsbAndAudio()
    {
        Console.WriteLine("== AmplitudaUSB и звуковой вход — дверью без железа не пускаются; состояние их чистого пуска + правило отражением");
        foreach (string device in new[] { "AmplitudaUSB", "AudioInput" })
        {
            // Чистый пуск обоих ставит StartTime = DateTime.Now (AmplitudaUsbDeviceController.StartCore,
            // AudioInputDeviceController.StartMeasurement); дальше дверь зовёт правило (IL выше).
            ResultData rd = new ResultData();
            rd.SampleInfo.Time = Unknown;
            rd.StartTime = DateTime.Now;
            rd.EndTime = rd.StartTime.AddSeconds(Held);
            if (rule != null)
            {
                rule.Invoke(null, new object[] { rd });
            }
            Outcome o = new Outcome { Start = rd.StartTime, Sample = rd.SampleInfo.Time, End = rd.EndTime };
            Console.WriteLine("  {0}: начало {1}, проба {2}", device, Show(o.Start), Show(o.Sample));
            Expect(Same(o.Sample, o.Start), device + ": проба осталась " + Show(o.Sample));
            Factors(device, o);
        }
    }

    // ---------------------------------------------------------------- само правило

    static void RuleGrid()
    {
        Console.WriteLine("== Правило отражением: (начало × проба)");
        if (rule == null)
        {
            Console.WriteLine("  правила нет — сличать нечего");
            return;
        }
        DateTime now = DateTime.Now;
        var cases = new[]
        {
            new { Start = now, Sample = Unknown, Want = now, Name = "начало известно, проба метка → проба = начало" },
            new { Start = now, Sample = KnownSample, Want = KnownSample, Name = "начало известно, проба известна → не трогать" },
            new { Start = Unknown, Sample = Unknown, Want = Unknown, Name = "обе метки → обе остаются" },
            new { Start = Unknown, Sample = KnownSample, Want = KnownSample, Name = "начало метка, проба известна → не трогать" },
            new { Start = KnownStart, Sample = DateTime.SpecifyKind(Unknown, DateTimeKind.Local), Want = KnownStart, Name = "метка с видом Local — тоже метка" },
        };
        foreach (var k in cases)
        {
            ResultData rd = new ResultData();
            rd.StartTime = k.Start;
            rd.SampleInfo.Time = k.Sample;
            rule.Invoke(null, new object[] { rd });
            bool ok = Same(rd.SampleInfo.Time, k.Want) && Same(rd.StartTime, k.Start);
            Console.WriteLine("  {0}: проба {1}{2}", k.Name, Show(rd.SampleInfo.Time), ok ? "" : "  ≠ ждали " + Show(k.Want));
            Expect(ok, "правило: " + k.Name);
        }
        ResultData empty = new ResultData();
        empty.SampleInfo = null;
        rule.Invoke(null, new object[] { empty });
        rule.Invoke(null, new object[] { null });
        Console.WriteLine("  без карточки пробы и без документа — без исключения");
    }

    // ---------------------------------------------------------------- служебное

    static void Factors(string name, Outcome o)
    {
        double cs = Factor(CsHalfLifeYears, o), co = Factor(CoHalfLifeYears, o);
        Console.WriteLine("    {0}: задержка пробы {1}, набор по датам {2}, Cs-137 ×{3}, Co-60 ×{4}",
            name, Span(o.Start - o.Sample), Span(o.End - o.Start), cs.ToString("0.000000", Inv), co.ToString("0.000000", Inv));
        Expect(Math.Abs(cs - 1.0) < 1e-4 && Math.Abs(co - 1.0) < 1e-4,
            name + ": множитель распада не ≈ 1 (Cs-137 ×" + cs.ToString("0.000000", Inv) + ", Co-60 ×" + co.ToString("0.000000", Inv) + ")");
    }

    static double Factor(double halfLife, Outcome o)
    {
        return MeasurementResultManager.DecayToSamplingFactor(halfLife, o.Sample, o.Start, o.End);
    }

    static bool Calls(MethodInfo caller, MethodInfo callee)
    {
        if (caller == null) return false;
        byte[] il = caller.GetMethodBody().GetILAsByteArray();
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28) continue;
            try
            {
                if (caller.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)) == callee) return true;
            }
            catch (ArgumentException) { }
        }
        return false;
    }

    static string Write(ResultData rd)
    {
        ResultDataFile file = new ResultDataFile();
        file.InitFormatVersion();
        file.ResultDataList.Add(rd);
        StringWriter w = new StringWriter(Inv);
        new XmlSerializer(typeof(ResultDataFile)).Serialize(w, file);
        return w.ToString();
    }

    static string Element(string xml, string name)
    {
        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        System.Xml.XmlNode n = doc.SelectSingleNode("//ResultData/" + name) ?? doc.SelectSingleNode("//ResultData/SampleInfo/" + name);
        return n != null ? n.InnerText : null;
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
        return Math.Abs(days) > 365.0 ? (days / 365.0).ToString("0.00", Inv) + " лет" : s.TotalSeconds.ToString("0.000", Inv) + " с";
    }

    static void Print(string name, Outcome o)
    {
        Console.WriteLine("  {0}: окно [{1} … {2}], начало {3}, проба {4}",
            name, o.T0.ToString("HH:mm:ss.fff", Inv), o.T1.ToString("HH:mm:ss.fff", Inv), Show(o.Start), Show(o.Sample));
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
