// Проба полосы П303 (14.10.2026): ОТКАЗАННЫЙ «ПУСК» AtomSpectra НЕ ТРОГАЕТ НАЧАЛО ДОКУМЕНТА.
//
// Находка полосы П300 (её проба, случай A6): `AtomSpectraDeviceController.StartMeasurement`
// (и `AttachToDevice`) ставил `resultData.StartTime = DateTime.Now` у нового контроллера ДО
// того, как узнавал, принял ли прибор команды, и оставлял его на отказе. Файл с известным
// началом и 600 с отсчётов → «Пуск», прибор молчит → пуск отказан, отсчёты файловые, а
// начало «сейчас»: Cs-137 при известной пробе ×1.063838 вместо ×1.000009, и после
// «Сохранить» настоящее начало потеряно. Правка: «сейчас» — только принятому пуску.
//
// ⛔ Граница (решение Amber 14.10.2026 «Оставить как есть», П267): принятый пуск,
// продолжение спектра A после набора B и сброс прибора НЕ меняются. Это меряют контроли:
// поток байтов команд прибору и состояние документа печатаются строками «СЛЕД», и у сборок
// до и после правки эти строки обязаны совпасть побайтно (сличает полоса, `fc` по выводам).
//
// Без окон, без настоящих портов и BLE (⛔ к COM4 подключён живой Atom Spectra):
//   * AtomSpectra — экземпляр `AtomSpectraVCPIn` заведён БЕЗ конструктора (без потока и
//     порта, как у П267 и П300); ответы «-ok» кладёт поток пробы, он же записывает байты
//     команд; «прибор молчит» — поток байты забирает, а не отвечает;
//   * RadiaCode, Obsidian — экземпляры BLE без конструктора (без потоков), как у П300.
// Пуск — НАСТОЯЩЕЙ дверью `MeasurementController` (аренда прибора, `CreateDeviceController`,
// `StartMeasurement` прибора).
//
//   RefusedStartProbeP303.exe   (из каталога, где лежит BecquerelMonitor.exe)
//
// Коды: 0 — отказ начало не трогает и контроли сработали (так обязана отвечать сборка ПОСЛЕ);
// 1 — дефект виден (так обязана отвечать сборка ДО); 3 — контроль НЕ сработал (замер пуст);
// 5 — обрыв.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using BecquerelMonitor;

static class RefusedStartProbeP303
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly App = typeof(ResultData).Assembly;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double CsHalfLifeYears = 30.08;
    const double Held = 600.0;
    const long HeldCounts = 123456;
    static readonly DateTime Unknown = ResultData.UnknownStartTime;
    static readonly DateTime KnownStart = new DateTime(2024, 2, 7, 12, 28, 49, DateTimeKind.Unspecified);
    static readonly DateTime KnownSample = new DateTime(2024, 2, 7, 9, 0, 0, DateTimeKind.Unspecified);
    static int defect, control, seq;

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
            Console.WriteLine();
            Setup();
            Atom();
            Console.WriteLine();
            Ble("RadiaCode", "BecquerelMonitor.RadiaCodeIn", new RadiaCodeDeviceConfig());
            Console.WriteLine();
            Ble("Obsidian", "BecquerelMonitor.ObsidianIn", new ObsidianDeviceConfig());
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
        Trace.Listeners.Add(Commands);
    }

    static DeviceConfigInfo Register(string type, InputDeviceConfig cfg)
    {
        seq++;
        DeviceConfigInfo info = new DeviceConfigInfo();
        info.Guid = "p303-" + type + "-" + seq.ToString(Inv);
        info.DeviceType = type;
        info.Name = "p303 " + type;
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

    sealed class Outcome
    {
        public DateTime T0, T1, Start, Sample, End;
        public bool Started;
        public string Failure;
        public long Counts;
        public double Real;
        public string Wire = "", Sent = "";
    }

    static Outcome Act(ResultData rd, Func<bool> door)
    {
        Outcome o = new Outcome();
        Wire();                                      // байты прежних действий — не этому
        Commands.Take();
        o.T0 = DateTime.Now;
        try
        {
            o.Started = door();
        }
        catch (Exception ex)
        {
            o.Failure = ex.GetType().Name + ": " + ex.Message;
        }
        o.T1 = DateTime.Now;
        Thread.Sleep(30);                            // поток пробы забирает последний кадр
        o.Wire = Wire();
        o.Sent = Commands.Take();
        o.Start = rd.StartTime;
        o.Sample = rd.SampleInfo.Time;
        o.End = rd.EndTime;
        o.Counts = rd.EnergySpectrum.TotalPulseCount;
        o.Real = rd.EnergySpectrum.MeasurementTime;
        return o;
    }

    static Outcome Start(ResultData rd)
    {
        return Act(rd, () => rd.MeasurementController.StartRecording());
    }

    static Outcome Attach(ResultData rd)
    {
        return Act(rd, () => rd.MeasurementController.AttachToDevice());
    }

    static Outcome Stop(ResultData rd)
    {
        return Act(rd, () => { rd.MeasurementController.StopRecording(); return true; });
    }

    static void Dispose(ResultData rd)
    {
        IDisposable d = rd.MeasurementController.DeviceController as IDisposable;
        if (d != null)
        {
            try { d.Dispose(); } catch (Exception ex) { Console.WriteLine("    (Dispose: " + ex.Message + ")"); }
        }
    }

    // ---------------------------------------------------------------- AtomSpectra

    static volatile bool answering;
    static volatile bool responderStop;
    static readonly List<byte> wire = new List<byte>();

    static void Atom()
    {
        Console.WriteLine("== AtomSpectra — дверь MeasurementController, экземпляр прибора без потока и порта; ответы «-ok» — от пробы");
        AtomSpectraDeviceConfig cfg = new AtomSpectraDeviceConfig();
        cfg.ComPortName = "p303fake";          // не открывается: потока чтения у экземпляра нет
        cfg.BaudRate = 600000;
        DeviceConfigInfo info = Register("AtomSpectraVCP", cfg);
        object atom = BareAtom(info.Guid);
        Thread responder = new Thread(() => Respond(atom)) { IsBackground = true };
        responder.Start();
        try
        {
            double csWas = MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, KnownSample, KnownStart, KnownStart.AddSeconds(Held));
            Console.WriteLine("  файл: начало {0}, проба {1}, {2} отсчётов за {3} с; Cs-137 у файла ×{4}",
                Show(KnownStart), Show(KnownSample), HeldCounts.ToString(Inv), Held.ToString("0", Inv), csWas.ToString("0.000000", Inv));

            // F1 — ГЛАВНЫЙ расклад (случай A6 пробы П300): прибор молчит, пуск отказан.
            answering = false;
            ResultData d1 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Outcome f1 = Start(d1);
            Print("F1 прибор молчит → Пуск отказан; у файла известное начало", f1);
            Control(!f1.Started && f1.Failure != null && f1.Counts == HeldCounts,
                "F1: ждали отказ пуска с отсчётами файла на месте (" + (f1.Failure ?? "отказа нет") + ")");
            double cs1 = Factor(f1);
            Console.WriteLine("    начало {0}; Cs-137 ×{1} (у файла ×{2})", Same(f1.Start, KnownStart) ? "НЕ ТРОНУТО" : "ПЕРЕПИСАНО",
                cs1.ToString("0.000000", Inv), csWas.ToString("0.000000", Inv));
            Expect(Same(f1.Start, KnownStart) && Same(f1.Sample, KnownSample) && Same(f1.End, KnownStart.AddSeconds(Held)),
                "F1: отказанный пуск сдвинул начало (" + Show(f1.Start) + ") или пробу/конец");
            Expect(Math.Abs(cs1 - csWas) < 1e-9, "F1: множитель распада Cs-137 ×" + cs1.ToString("0.000000", Inv) + " вместо ×" + csWas.ToString("0.000000", Inv));
            string saved = Element(Write(d1), "StartTime");
            Console.WriteLine("    «Сохранить» после отказа: <StartTime>{0}</StartTime>", saved);
            Expect(DateTime.Parse(saved, Inv, DateTimeStyles.RoundtripKind) == KnownStart, "F1: в файл уходит не начало файла");

            // F4 — КОНТРОЛЬ: после отказа прибор ответил, «Пуск» того же документа ещё раз — принят,
            // начало «сейчас», и прибору уходит полный пуск нового документа (-sto, -rst, -sta):
            // отказ флаг нового документа не снял — ни до правки, ни после.
            answering = true;
            Outcome f4 = Start(d1);
            Stop(d1);
            Print("F4 КОНТРОЛЬ тот же документ F1, прибор ответил → Пуск", f4);
            Control(f4.Started && Within(f4.Start, f4.T0, f4.T1) && Same(f4.Sample, KnownSample),
                "F4: повторный пуск не принят, начало не «сейчас» или проба изменилась");
            Trail("F4 повтор после отказа", f4);
            answering = false;
            Dispose(d1);

            // F2 — то же у «Подключиться» (AtomSpectra Pro): отказ начало не трогает.
            ResultData d2 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Outcome f2 = Attach(d2);
            Leases().Remove(info.Guid);              // без окон отказ — бросок, и дверь аренду не снимает
            Print("F2 прибор молчит → Подключиться отказано; у файла известное начало", f2);
            Control(!f2.Started && f2.Failure != null && f2.Counts == HeldCounts, "F2: ждали отказ подключения");
            Expect(Same(f2.Start, KnownStart) && Same(f2.Sample, KnownSample),
                "F2: отказанное подключение сдвинуло начало (" + Show(f2.Start) + ")");
            Factors("F2", f2, csWas);
            Dispose(d2);

            // F3 — файл с метками 1970 в начале и пробе: отказ оставляет документ как был.
            ResultData d3 = NewDoc(info, Unknown, Unknown, HeldCounts, Held);
            Outcome f3 = Start(d3);
            Print("F3 прибор молчит → Пуск отказан; у файла метки 1970", f3);
            Control(!f3.Started && f3.Failure != null, "F3: ждали отказ пуска");
            Expect(Same(f3.Start, Unknown) && Same(f3.Sample, Unknown),
                "F3: отказанный пуск сдвинул начало (" + Show(f3.Start) + ") — документ не тот, что был");
            Factors("F3", f3, MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, Unknown, Unknown, Unknown.AddSeconds(Held)));
            Dispose(d3);

            answering = true;
            // S1 — КОНТРОЛЬ: принятый пуск файла с известным началом — как был: начало «сейчас»,
            // проба та же, прибору -sto, -rst, -sta (сброс прибора), затем -inf (мёртвое время).
            ResultData d5 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Outcome s1 = Start(d5);
            Outcome s1stop = Stop(d5);
            Print("S1 КОНТРОЛЬ принятый Пуск файла с известным началом", s1);
            Control(s1.Started && Within(s1.Start, s1.T0, s1.T1) && Same(s1.Sample, KnownSample),
                "S1: принятый пуск: начало не «сейчас» или проба изменилась");
            Trail("S1 Пуск", s1);
            Trail("S1 Стоп", s1stop);

            // S2 — КОНТРОЛЬ: принятое «Подключиться» — как было.
            ResultData d6 = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            Outcome s2 = Attach(d6);
            d6.MeasurementController.DetachFromDevice();
            Print("S2 КОНТРОЛЬ принятое Подключиться", s2);
            Control(s2.Started && Within(s2.Start, s2.T0, s2.T1) && Same(s2.Sample, KnownSample),
                "S2: принятое подключение: начало не «сейчас» или проба изменилась");
            Trail("S2 Подключиться", s2);
            Dispose(d6);

            // B1 — КОНТРОЛЬ, решение Amber «Оставить как есть»: спектр A, затем спектр B тем же
            // прибором, затем продолжение A. Продолжению уходит только -sta, начало A — прежнее.
            ResultData da = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            Outcome a1 = Start(da);
            Stop(da);
            ResultData db = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            Outcome b1 = Start(db);
            Stop(db);
            Outcome a2 = Start(da);
            Stop(da);
            Print("B1 КОНТРОЛЬ первый Пуск A", a1);
            Print("B1 КОНТРОЛЬ Пуск B", b1);
            Print("B1 КОНТРОЛЬ продолжение A после B", a2);
            Control(a1.Started && b1.Started && a2.Started && Same(a2.Start, a1.Start) && Within(a1.Start, a1.T0, a1.T1),
                "B1: продолжение A после B сдвинуло начало A или пуск не принят");
            Trail("B1 первый Пуск A", a1);
            Trail("B1 Пуск B", b1);
            Trail("B1 продолжение A", a2);
            Control(a2.Sent == "-sta|-inf", "B1: продолжению A ушло не «-sta» (+ «-inf»), а " + a2.Sent);
            Dispose(da);
            Dispose(db);
            Dispose(d5);
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

    // Прибор, который отвечает «-ok» на каждый кадр команды (или молчит) и записывает байты.
    // Кадр кладётся в очередь отправки под `Lock` целиком, поэтому и вынимается под ним же.
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
                    lock (wire)
                    {
                        while (outq.TryDequeue(out b)) wire.Add(b);
                    }
                    if (answering)
                    {
                        lines.Enqueue("-ok");
                    }
                }
            }
            Thread.Sleep(1);
        }
    }

    static string Wire()
    {
        lock (wire)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in wire) sb.Append(b.ToString("X2", Inv));
            wire.Clear();
            return sb.ToString();
        }
    }

    // Имена команд — из `Trace` прибора («Command sent: …»); байты — из очереди отправки.
    static readonly CommandListener Commands = new CommandListener();

    sealed class CommandListener : TraceListener
    {
        readonly List<string> sent = new List<string>();
        public override void Write(string message) { }
        public override void WriteLine(string message)
        {
            const string head = "Command sent: ";
            if (message == null || !message.StartsWith(head, StringComparison.Ordinal)) return;
            lock (sent) sent.Add(message.Substring(head.Length));
        }
        public string Take()
        {
            lock (sent)
            {
                string s = string.Join("|", sent);
                sent.Clear();
                return s;
            }
        }
    }

    // Строка для сличения сборок: ни одного времени — только то, что обязано совпасть побайтно.
    static void Trail(string name, Outcome o)
    {
        Console.WriteLine("  СЛЕД {0}: принят={1}; отказ={2}; команды={3}; байты={4}; начало={5}; проба={6}; отсчёты={7}; время={8}",
            name, o.Started, o.Failure == null ? "нет" : "есть", o.Sent, o.Wire,
            Within(o.Start, o.T0, o.T1) ? "сейчас" : (Same(o.Start, KnownStart) ? "файла" : (Same(o.Start, Unknown) ? "метка" : "прежнее")),
            Same(o.Sample, KnownSample) ? "известная" : (Same(o.Sample, o.Start) ? "= начало" : (Same(o.Sample, Unknown) ? "метка" : "иная")),
            o.Counts.ToString(Inv), o.Real.ToString("R", Inv));
    }

    // ---------------------------------------------------------------- RadiaCode, Obsidian

    static void Ble(string device, string inType, InputDeviceConfig cfg)
    {
        Console.WriteLine("== {0} — дверь MeasurementController, экземпляр BLE без потоков", device);
        cfg.GetType().GetProperty("AddressBLE").SetValue(cfg, "p303-no-ble", null);
        cfg.GetType().GetProperty("DeviceSerial").SetValue(cfg, "p303-serial", null);
        DeviceConfigInfo info = Register(device, cfg);
        Type t = App.GetType(inType, true);
        object dev = BareBle(t, info.Guid);
        try
        {
            double csWas = MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, KnownSample, KnownStart, KnownStart.AddSeconds(Held));

            // R1 — синхронный отказ (конфигурация не того прибора): начало не трогается — в ОБЕИХ
            // сборках, контроллеры BLE ставят начало только после всех проверок.
            ResultData r1d = NewDoc(Register(device, new AtomSpectraDeviceConfig()), KnownStart, KnownSample, HeldCounts, Held);
            Outcome r1 = Start(r1d);
            Print("R1 КОНТРОЛЬ Пуск отказан (конфигурация не та)", r1);
            Control(!r1.Started && Same(r1.Start, KnownStart) && Same(r1.Sample, KnownSample),
                device + " R1: синхронный отказ сдвинул начало или пуск принят");

            // R2 — СВЕДЕНИЕ, не дефект этой пробы: пуск у BLE принимается сразу, ответ прибора
            // приходит позже статусом. Прибор так и не дал данных и сообщил «Faulted» (нет адреса
            // BLE) — набор остановлен, отсчёты файловые, начало — момент «Пуска».
            ResultData r2d = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Outcome r2 = Start(r2d);
            object controller = r2d.MeasurementController.DeviceController;
            controller.GetType().GetMethod("setStatus", F, null, new[] { typeof(string) }, null).Invoke(controller, new object[] { "Faulted" });
            r2.Counts = r2d.EnergySpectrum.TotalPulseCount;
            r2.End = r2d.EndTime;
            Print("R2 СВЕДЕНИЕ Пуск принят, затем статус «Faulted» без единого пакета", r2);
            Console.WriteLine("    набор идёт: {0}; отсчётов {1} (файловые); начало {2}; конец {3}; Cs-137 ×{4} (у файла ×{5})",
                r2d.ResultDataStatus.Recording, r2.Counts.ToString(Inv),
                Within(r2.Start, r2.T0, r2.T1) ? "«сейчас»" : "прежнее", Show(r2.End),
                Factor(r2).ToString("0.000000", Inv), csWas.ToString("0.000000", Inv));
            Control(r2.Started, device + " R2: пуск не принят — сведению не о чем говорить");
            Leases().Remove(info.Guid);
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

    // ---------------------------------------------------------------- служебное

    static void Factors(string name, Outcome o, double want)
    {
        double cs = Factor(o);
        Console.WriteLine("    {0}: задержка пробы {1}, набор по датам {2}, Cs-137 ×{3}",
            name, Span(o.Start - o.Sample), Span(o.End - o.Start), cs.ToString("0.000000", Inv));
        Expect(Math.Abs(cs - want) < 1e-9, name + ": множитель распада ×" + cs.ToString("R", Inv) + " вместо ×" + want.ToString("R", Inv));
    }

    static double Factor(Outcome o)
    {
        return MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, o.Sample, o.Start, o.End);
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
        System.Xml.XmlNode n = doc.SelectSingleNode("//ResultData/" + name);
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
        Console.WriteLine("  {0}: окно [{1} … {2}], начало {3}, проба {4}{5}",
            name, o.T0.ToString("HH:mm:ss.fff", Inv), o.T1.ToString("HH:mm:ss.fff", Inv), Show(o.Start), Show(o.Sample),
            o.Failure != null ? "; отказ: " + o.Failure : "");
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
