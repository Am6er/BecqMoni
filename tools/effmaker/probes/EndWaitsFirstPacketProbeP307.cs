// Проба полосы П307 (14.10.2026): ПОКА НАЧАЛО НАБОРА ЖДЁТ ПЕРВОГО ПАКЕТА (П305, RadiaCode и Obsidian),
// ТАКТ ОКНА КОНЕЦ НАБОРА НЕ ДВИГАЕТ; ПЕРВЫЙ ПАКЕТ СТАВИТ НАЧАЛО И КОНЕЦ РАЗОМ.
//
// Остаток П305 (журнал `handover/handover-2026-10-14-p305-ble-start-first-packet.md`, «Попутно»): начало
// ставит первый пакет данных, а такт `MainForm.OnTimer` (`AMBER168`) уже двигал конец «сейчас». У
// загруженного файла живое окно результата до первого пакета показывало распад по [начало файла;
// сейчас] — Cs-137 ×1.03 вместо ×1.00001 у файла 2.7-летней давности, — и автосохранение того же такта
// писало эту пару в файл. Правка: `MainForm.OnTimer` не двигает конец, пока
// `MeasurementController.StartAwaitsFirstPacket`; `MeasurementController.StartSetByFirstPacket` ставит
// конец = миг пакета вместе с началом (иначе до следующего такта конец стоял бы раньше начала).
//
// ⛔ Такт окна — НАСТОЯЩИЙ `MainForm.OnTimer` (отражением, на форме без конструктора: ни окна, ни
// дескриптора; все счётчики панелей отведены за край, активного документа нет — исполняется только
// цикл по документам). Не копия: копия такта мерила бы пробу, а не приложение.
// Без железа (⛔ ни BLE, ни COM4): экземпляры RadiaCodeIn/ObsidianIn без конструктора, как у П305;
// пакеты и статусы подаёт проба закрытыми методами экземпляра; поток окон — очередь, которую качает
// проба; «Пуск», «Стоп», «Очистить» — настоящими дверьми `MeasurementController`.
//
//   EndWaitsFirstPacketProbeP307.exe   (из каталога, где лежит BecquerelMonitor.exe)
//
// Коды: 0 — правило выполнено, контроли сработали (сборка ПОСЛЕ); 1 — дефект виден (сборка ДО);
// 3 — контроль НЕ сработал (замер пуст); 5 — обрыв.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using BecquerelMonitor;

static class EndWaitsFirstPacketProbeP307
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly App = typeof(ResultData).Assembly;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double CsHalfLifeYears = 30.08;
    const double I131HalfLifeYears = 8.0252 / 365.0;
    const double Held = 600.0;
    const long HeldCounts = 123456;
    static readonly DateTime Unknown = ResultData.UnknownStartTime;
    static readonly DateTime KnownStart = new DateTime(2024, 2, 7, 12, 28, 49, DateTimeKind.Unspecified);
    static readonly DateTime KnownSample = new DateTime(2024, 2, 7, 9, 0, 0, DateTimeKind.Unspecified);
    static readonly DateTime KnownEnd = KnownStart.AddSeconds(Held);
    static readonly QueueContext Ui = new QueueContext();
    static int defect, control, seq;
    static bool fixedBuild;
    static MainForm window;
    static List<DocEnergySpectrum> windowDocs;
    static MethodInfo onTimer;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Console.WriteLine(ProbeTargetFramework.Describe());
            Console.WriteLine("приложение: " + App.Location);
            fixedBuild = typeof(MeasurementController).GetProperty("StartAwaitsFirstPacket", F) != null;
            Console.WriteLine("сборка: {0}", fixedBuild
                ? "с правкой (у контроллера набора есть признак «начало ждёт пакета»)"
                : "без правки (признака у контроллера набора нет)");
            Console.WriteLine("Cs-137 у файла: начало {0}, проба {1}, {2} отсчётов за {3} с — ×{4}",
                Show(KnownStart), Show(KnownSample), HeldCounts.ToString(Inv), Held.ToString("0", Inv),
                Decay(CsHalfLifeYears, KnownSample, KnownStart, KnownEnd).ToString("0.000000", Inv));
            Console.WriteLine();
            Setup();
            SynchronizationContext saved = MainForm.originalContext;
            MainForm.originalContext = Ui;
            try
            {
                Ble("RadiaCode", "BecquerelMonitor.RadiaCodeIn", new RadiaCodeDeviceConfig(), typeof(RadiaCodeInDataReadyArgs));
                Console.WriteLine();
                Ble("Obsidian", "BecquerelMonitor.ObsidianIn", new ObsidianDeviceConfig(), typeof(ObsidianInDataReadyArgs));
                Console.WriteLine();
                Others();
                Console.WriteLine();
            }
            finally
            {
                MainForm.originalContext = saved;
            }
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
        object manager = typeof(DeviceConfigManager).GetField("instance", S).GetValue(null);
        typeof(DeviceConfigManager).GetField("listLoaded", F).SetValue(manager, true);

        // Окно — форма без конструктора: ни дескриптора, ни панелей. Своя опись документов, свой
        // общий конфиг (конфиг Amber не читается), счётчики панелей и автосохранения — за краем,
        // активного документа нет: такт исполняет только цикл по документам.
        window = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        GC.SuppressFinalize(window);
        DocumentManager dm = (DocumentManager)FormatterServices.GetUninitializedObject(typeof(DocumentManager));
        windowDocs = new List<DocEnergySpectrum>();
        typeof(DocumentManager).GetField("documentList", F).SetValue(dm, windowDocs);
        GlobalConfigManager gcm = (GlobalConfigManager)FormatterServices.GetUninitializedObject(typeof(GlobalConfigManager));
        typeof(GlobalConfigManager).GetField("globalConfig", F).SetValue(gcm, new GlobalConfigInfo());
        Type mf = typeof(MainForm);
        mf.GetField("documentManager", F).SetValue(window, dm);
        mf.GetField("globalConfigManager", F).SetValue(window, gcm);
        mf.GetField("countsRateManager", F).SetValue(window, new CountsRateManager());
        mf.GetField("activeDocument", F).SetValue(window, null);
        mf.GetField("doUpdatePulseView", F).SetValue(window, false);
        foreach (string name in new[] { "count2000", "count1000", "count500", "count200", "countChart", "countTemp" })
        {
            mf.GetField(name, F).SetValue(window, int.MinValue / 2);
        }
        mf.GetField("countAutoSave", F).SetValue(window, long.MinValue / 2);
        onTimer = mf.GetMethod("OnTimer", F, null, new[] { typeof(object), typeof(EventArgs) }, null);
        if (onTimer == null) throw new InvalidOperationException("нет MainForm.OnTimer(object, EventArgs)");
    }

    /// <summary>Поток окон: присланное через <c>Post</c> ждёт, пока проба не прокачает очередь.</summary>
    sealed class QueueContext : SynchronizationContext
    {
        readonly Queue<KeyValuePair<SendOrPostCallback, object>> queue = new Queue<KeyValuePair<SendOrPostCallback, object>>();

        public override void Post(SendOrPostCallback d, object state)
        {
            lock (queue) queue.Enqueue(new KeyValuePair<SendOrPostCallback, object>(d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            d(state);
        }

        public int Pump()
        {
            int n = 0;
            while (true)
            {
                KeyValuePair<SendOrPostCallback, object> item;
                lock (queue)
                {
                    if (queue.Count == 0) return n;
                    item = queue.Dequeue();
                }
                item.Key(item.Value);
                n++;
            }
        }
    }

    static DeviceConfigInfo Register(string type, InputDeviceConfig cfg)
    {
        seq++;
        DeviceConfigInfo info = new DeviceConfigInfo();
        info.Guid = "lane307-" + type + "-" + seq.ToString(Inv);
        info.DeviceType = type;
        info.Name = "lane307 " + type;
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

    // Документ в описи окна: такт окна обходит именно её.
    static DocEnergySpectrum Show(ResultData rd)
    {
        DocEnergySpectrum doc = (DocEnergySpectrum)FormatterServices.GetUninitializedObject(typeof(DocEnergySpectrum));
        GC.SuppressFinalize(doc);
        ResultDataFile file = new ResultDataFile();
        file.ResultDataList = new List<ResultData> { rd };
        doc.ResultDataFile = file;
        windowDocs.Add(doc);
        return doc;
    }

    static void Hide(DocEnergySpectrum doc)
    {
        windowDocs.Remove(doc);
    }

    // Такт окна — НАСТОЯЩИЙ MainForm.OnTimer. Возвращает миг ПЕРЕД тактом.
    static DateTime Tick()
    {
        DateTime before = DateTime.Now;
        onTimer.Invoke(window, new object[] { null, EventArgs.Empty });
        return before;
    }

    static bool Start(ResultData rd)
    {
        bool ok = rd.MeasurementController.StartRecording();
        Ui.Pump();
        return ok;
    }

    static void Stop(ResultData rd)
    {
        rd.MeasurementController.StopRecording();
        Ui.Pump();
    }

    // «Очистить» — как DCControlPanel.ClearMeasurementResult без окна-вопроса и без показа.
    static void Clear(ResultData rd)
    {
        rd.MeasurementController.ClearMeasurementResult();
        ResultDataStatus st = rd.ResultDataStatus;
        st.TotalTime = TimeSpan.FromSeconds(0.0);
        st.ElapsedTime = TimeSpan.FromSeconds(0.0);
        rd.EnergySpectrum.Initialize();
        rd.PulseCollection.Pulses.Clear();
        rd.CountRates.Clear();
    }

    static void Dispose(ResultData rd)
    {
        IDisposable d = rd.MeasurementController.DeviceController as IDisposable;
        if (d != null)
        {
            try { d.Dispose(); } catch (Exception ex) { Console.WriteLine("    (Dispose: " + ex.Message + ")"); }
        }
        Leases().Remove(rd.DeviceConfig.Guid);
    }

    static IDictionary Leases()
    {
        return (IDictionary)typeof(MeasurementController).GetField("deviceLeases", S).GetValue(null);
    }

    // Признак П305 у контроллера прибора (закрытое поле; есть в обеих сборках).
    static bool Awaits(ResultData rd)
    {
        object dc = rd.MeasurementController.DeviceController;
        return dc != null && (bool)dc.GetType().GetField("startAwaitsPacket", F).GetValue(dc);
    }

    // ---------------------------------------------------------------- «поток чтения» прибора

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

    static void Status(object dev, string state)
    {
        Type t = dev.GetType();
        object value = Enum.Parse(t.GetField("state", F).FieldType, state);
        t.GetMethod("setStatusAuto", F).Invoke(dev, new object[] { value });
    }

    static string State(object dev)
    {
        return dev.GetType().GetField("state", F).GetValue(dev).ToString();
    }

    // Пакет так, как его отдаёт поток чтения: «Recording», затем `RaiseDataReady`; поток окон
    // прокачивается. Возвращает момент прихода. Пауза — как у П305: первым считается пакет,
    // пришедший СТРОГО позже нажатия.
    static DateTime Deliver(object dev, Type argsType, int deviceSeconds)
    {
        Thread.Sleep(20);
        if (State(dev) != "Recording") Status(dev, "Recording");
        int[] hist = new int[1024];
        for (int i = 0; i < hist.Length; i++) hist[i] = 1;
        long sum = hist.Length;
        DateTime received = DateTime.Now;
        object a = Activator.CreateInstance(argsType, new object[] { hist, deviceSeconds, sum, received });
        dev.GetType().GetMethod("RaiseDataReady", F).Invoke(dev, new object[] { a });
        Ui.Pump();
        return received;
    }

    // ---------------------------------------------------------------- RadiaCode, Obsidian

    static void Ble(string device, string inType, InputDeviceConfig cfg, Type argsType)
    {
        Console.WriteLine("== {0} — настоящий такт окна MainForm.OnTimer; пакеты и статусы подаёт проба", device);
        cfg.GetType().GetProperty("AddressBLE").SetValue(cfg, "lane307-no-ble", null);
        cfg.GetType().GetProperty("DeviceSerial").SetValue(cfg, "lane307-serial", null);
        DeviceConfigInfo info = Register(device, cfg);
        Type t = App.GetType(inType, true);
        object dev = BareBle(t, info.Guid);
        double csFile = Decay(CsHalfLifeYears, KnownSample, KnownStart, KnownEnd);
        // Свидетель такта: соседний спектр другого прибора, который пишется, в той же описи окна.
        // Его конец обязан идти за часами в ОБЕИХ сборках — иначе такт не исполнился.
        ResultData witness = Witness();
        DocEnergySpectrum witnessDoc = Show(witness);
        try
        {
            // W1 — ГЛАВНЫЙ: загруженный файл → «Пуск» → такты окна до первого пакета → «Faulted».
            ResultData d1 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            DocEnergySpectrum doc1 = Show(d1);
            bool ok1 = Start(d1);
            bool awaiting1 = Awaits(d1);
            DateTime k1 = Tick();
            Thread.Sleep(40);
            DateTime k2 = Tick();
            DateTime endWait = d1.EndTime;
            double csWait = Decay(CsHalfLifeYears, d1.SampleInfo.Time, d1.StartTime, d1.EndTime);
            Console.WriteLine("  W1 файл → Пуск → два такта окна до первого пакета: начало {0}, конец {1}",
                Where(d1.StartTime), Where(endWait, k2));
            Console.WriteLine("     окно результата: Cs-137 ×{0} (у файла ×{1}); начало ждёт пакета: {2}; отсчётов {3}",
                csWait.ToString("0.000000", Inv), csFile.ToString("0.000000", Inv), awaiting1 ? "да" : "нет",
                d1.EnergySpectrum.TotalPulseCount.ToString(Inv));
            Control(ok1 && awaiting1 && d1.ResultDataStatus.Recording && d1.EnergySpectrum.TotalPulseCount == HeldCounts,
                device + " W1: ждали принятый пуск, ждущий пакета, с отсчётами файла");
            Control(witness.EndTime >= k2, device + " W1: конец свидетеля не пошёл за тактом — такт окна не исполнился");
            Expect(Same(endWait, KnownEnd), device + " W1: такт окна сдвинул конец до первого пакета (" + Show(endWait) + ")");
            Expect(Math.Abs(csWait - csFile) < 1e-9,
                device + " W1: окно результата до пакета — Cs-137 ×" + csWait.ToString("0.000000", Inv) + " вместо ×" + csFile.ToString("0.000000", Inv));
            Status(dev, "Faulted");
            Ui.Pump();
            Tick();
            Console.WriteLine("     → «Faulted»: начало {0}, конец {1}, Cs-137 ×{2}",
                Where(d1.StartTime), Where(d1.EndTime, k2),
                Decay(CsHalfLifeYears, d1.SampleInfo.Time, d1.StartTime, d1.EndTime).ToString("0.000000", Inv));
            Control(!d1.ResultDataStatus.Recording && Same(d1.StartTime, KnownStart) && Same(d1.EndTime, KnownEnd),
                device + " W1: «Faulted» до пакета не вернул документ файла (правило П305)");
            Hide(doc1);
            Dispose(d1);

            // W2 — файл → «Пуск» → такты → пакет (прибор насчитал 1 с): начало и конец ставит пакет РАЗОМ;
            // дальше конец идёт за тактом, как раньше (`AMBER168`).
            ResultData d2 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            DocEnergySpectrum doc2 = Show(d2);
            Start(d2);
            Tick();
            Thread.Sleep(1100);
            DateTime lastTick = Tick();
            DateTime r2 = Deliver(dev, argsType, 1);
            DateTime s2 = d2.StartTime, e2 = d2.EndTime;
            Console.WriteLine("  W2 файл → Пуск → такты → пакет {0} (1 с прибора): начало {1}, конец {2}; конец − начало {3} с",
                Clock(r2), Clock(s2), Clock(e2), Seconds(e2 - s2));
            Expect(s2 <= e2, device + " W2: после первого пакета конец раньше начала");
            Expect(Same(e2, r2), device + " W2: первый пакет не поставил конец = миг пакета (конец " + Clock(e2) + ", такт был " + Clock(lastTick) + ")");
            Control(Same(s2, r2.AddSeconds(-1)), device + " W2: начало не «приход − время прибора» (правило П305)");
            Thread.Sleep(30);
            DateTime k3 = Tick();
            Console.WriteLine("     следующий такт {0}: конец {1} — за часами: {2}", Clock(k3), Clock(d2.EndTime), d2.EndTime >= k3 ? "да" : "НЕТ");
            Control(d2.EndTime >= k3 && Same(d2.StartTime, s2), device + " W2: после первого пакета конец не идёт за тактом (AMBER168)");
            Stop(d2);
            Hide(doc2);
            Dispose(d2);

            // W3 — новый документ (метки 1970 в начале и пробе), пакет с нулём времени прибора.
            ResultData d3 = NewDoc(info, Unknown, Unknown, 0, 0.0);
            DocEnergySpectrum doc3 = Show(d3);
            Start(d3);
            DateTime k4 = Tick();
            Thread.Sleep(30);
            Tick();
            DateTime e3w = d3.EndTime;
            DateTime r3 = Deliver(dev, argsType, 0);
            DateTime s3 = d3.StartTime, e3 = d3.EndTime;
            Console.WriteLine("  W3 новый документ → Пуск → такты: конец {0}; пакет {1} (0 с прибора): начало {2}, конец {3}",
                Same(e3w, Unknown) ? "метка" : Where(e3w, k4), Clock(r3), Clock(s3), Clock(e3));
            Expect(Same(e3w, Unknown), device + " W3: такт окна сдвинул конец нового документа до первого пакета");
            Expect(s3 <= e3, device + " W3: после первого пакета конец раньше начала (" + Clock(e3) + " < " + Clock(s3) + ")");
            Control(Same(s3, r3) && Same(d3.SampleInfo.Time, s3), device + " W3: начало не миг пакета или проба не за ним (П305/П300)");
            Stop(d3);
            Hide(doc3);
            Dispose(d3);

            // W4 — «Очистить» посреди набора: до первого пакета после сброса начало ждёт — конец стоит.
            ResultData d4 = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            DocEnergySpectrum doc4 = Show(d4);
            Start(d4);
            Deliver(dev, argsType, 50);
            Thread.Sleep(20);
            DateTime k5 = Tick();
            DateTime e4a = d4.EndTime;
            Clear(d4);
            Ui.Pump();
            bool awaiting4 = Awaits(d4);
            Thread.Sleep(30);
            DateTime k6 = Tick();
            DateTime e4w = d4.EndTime;
            DateTime r4 = Deliver(dev, argsType, 1);
            DateTime s4 = d4.StartTime, e4 = d4.EndTime;
            Console.WriteLine("  W4 Пуск → пакет → такт {0} → Очистить → такт {1}: конец {2}; пакет сброса {3} (1 с): начало {4}, конец {5}",
                Clock(k5), Clock(k6), Same(e4w, e4a) ? "прежний (" + Clock(e4a) + ")" : Clock(e4w), Clock(r4), Clock(s4), Clock(e4));
            Control(awaiting4 && e4a >= k5, device + " W4: после «Очистить» начало не ждёт пакета или конец до сброса не шёл — расклад не тот");
            Expect(Same(e4w, e4a), device + " W4: такт окна сдвинул конец, пока начало после «Очистить» ждало пакета");
            Expect(s4 <= e4 && Same(e4, r4), device + " W4: пакет сброса не поставил конец = миг пакета");
            Control(Same(s4, r4.AddSeconds(-1)), device + " W4: начало после сброса не «приход − время прибора» (П305)");
            Stop(d4);
            Hide(doc4);
            Dispose(d4);

            // W5 — ОСТАТОК, не дефект этой правки (одинаково в обеих сборках): продолжение без «Очистить».
            // Начало не ждёт пакета (оно уже стоит), и такт двигает конец до первого пакета продолжения;
            // набор без пакетов возвращает прежний конец (П305). Прежний набор кончился «двое суток назад».
            ResultData d5 = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            DocEnergySpectrum doc5 = Show(d5);
            Start(d5);
            Deliver(dev, argsType, 600);
            Stop(d5);
            DateTime oldEnd = DateTime.Now.AddDays(-2);
            d5.StartTime = oldEnd.AddSeconds(-600);
            d5.EndTime = oldEnd;
            d5.SampleInfo.Time = d5.StartTime;
            Start(d5);
            bool awaiting5 = Awaits(d5);
            DateTime k7 = Tick();
            double iOld = Decay(I131HalfLifeYears, d5.SampleInfo.Time, d5.StartTime, oldEnd);
            double iWait = Decay(I131HalfLifeYears, d5.SampleInfo.Time, d5.StartTime, d5.EndTime);
            Console.WriteLine("  W5 ОСТАТОК: продолжение (начало ждёт пакета: {0}) → такт до первого пакета: конец {1}; I-131 ×{2} вместо ×{3}",
                awaiting5 ? "да" : "нет", Same(d5.EndTime, oldEnd) ? "прежний" : Where(d5.EndTime, k7),
                iWait.ToString("0.0000", Inv), iOld.ToString("0.0000", Inv));
            Status(dev, "Faulted");
            Ui.Pump();
            Console.WriteLine("     → «Faulted»: конец {0}", Same(d5.EndTime, oldEnd) ? "прежний (П305)" : Show(d5.EndTime));
            Hide(doc5);
            Dispose(d5);
        }
        finally
        {
            Hide(witnessDoc);
            Leases().Remove(witness.DeviceConfig.Guid);
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

    // Спектр прибора Amplituda (последовательный порт), который «пишется»: контроллер без
    // конструктора, порт не открывается; такт окна обязан двигать его конец.
    static ResultData Witness()
    {
        return Recording(Register("AmplitudaSerial", new AmplitudaSerialDeviceConfig()), App.GetType("BecquerelMonitor.AmplitudaSerialDeviceController", true));
    }

    static ResultData Recording(DeviceConfigInfo info, Type controllerType)
    {
        ResultData rd = NewDoc(info, DateTime.Now.AddSeconds(-10), KnownSample, HeldCounts, Held);
        object controller = FormatterServices.GetUninitializedObject(controllerType);
        GC.SuppressFinalize(controller);
        typeof(MeasurementController).GetField("deviceController", F).SetValue(rd.MeasurementController, controller);
        rd.ResultDataStatus.Recording = true;
        return rd;
    }

    // ---------------------------------------------------------------- прочие приборы

    static void Others()
    {
        Console.WriteLine("== Прочие приборы — КОНТРОЛЬ (одинаково в обеих сборках): конец идёт за тактом, как раньше");
        List<string> starters = new List<string>();
        Type marker = App.GetType("BecquerelMonitor.IDeviceStartsOnFirstPacket", true);
        foreach (Type ty in App.GetTypes())
        {
            if (typeof(DeviceController).IsAssignableFrom(ty) && !ty.IsAbstract && marker.IsAssignableFrom(ty))
            {
                starters.Add(ty.Name);
            }
        }
        starters.Sort(StringComparer.Ordinal);
        Console.WriteLine("  признак «начало ждёт пакета» несут: {0}", string.Join(", ", starters));
        Control(starters.Count == 2 && starters.Contains("RadiaCodeDeviceController") && starters.Contains("ObsidianDeviceController"),
            "признак П305 несёт не ровно пара RadiaCode/Obsidian — ветка такта у прочих могла измениться");

        AtomSpectraDeviceConfig atom = new AtomSpectraDeviceConfig();
        atom.BaudRate = 9600;   // ⛔ не 38400/115200: такт не спрашивает порт (`-sho`), COM4 не трогается
        object[][] devices =
        {
            new object[] { "AtomSpectra", atom, App.GetType("BecquerelMonitor.AtomSpectraDeviceController", true) },
            new object[] { "AmplitudaSerial", new AmplitudaSerialDeviceConfig(), App.GetType("BecquerelMonitor.AmplitudaSerialDeviceController", true) },
            new object[] { "AmplitudaUsb", new AmplitudaUsbDeviceConfig(), App.GetType("BecquerelMonitor.AmplitudaUsbDeviceController", true) },
            new object[] { "AudioInput", new AudioInputDeviceConfig(), App.GetType("BecquerelMonitor.AudioInputDeviceController", true) },
        };
        foreach (object[] d in devices)
        {
            ResultData rec = Recording(Register((string)d[0], (InputDeviceConfig)d[1]), (Type)d[2]);
            ResultData idle = Recording(Register((string)d[0], (InputDeviceConfig)d[1]), (Type)d[2]);
            idle.ResultDataStatus.Recording = false;
            DateTime idleEnd = idle.EndTime;
            DocEnergySpectrum a = Show(rec), b = Show(idle);
            DateTime k = Tick();
            Hide(a);
            Hide(b);
            Console.WriteLine("  {0,-16} пишется: конец {1}; остановлен: конец {2}",
                (string)d[0], rec.EndTime >= k ? "= такт" : Show(rec.EndTime), Same(idle.EndTime, idleEnd) ? "не тронут" : Show(idle.EndTime));
            Control(rec.EndTime >= k && rec.ResultDataStatus.Recording, d[0] + ": такт окна не двинул конец спектра, который пишется");
            Control(Same(idle.EndTime, idleEnd), d[0] + ": такт окна тронул конец остановленного спектра");
        }
    }

    // ---------------------------------------------------------------- служебное

    static double Decay(double halfLifeYears, DateTime sample, DateTime start, DateTime end)
    {
        return MeasurementResultManager.DecayToSamplingFactor(halfLifeYears, sample, start, end);
    }

    static bool Same(DateTime a, DateTime b)
    {
        return a.Ticks == b.Ticks && a.Kind == b.Kind;
    }

    static string Where(DateTime x)
    {
        if (Same(x, KnownStart)) return "файла";
        if (Same(x, KnownEnd)) return "файла";
        if (Same(x, Unknown)) return "метка";
        return Show(x);
    }

    static string Where(DateTime x, DateTime tick)
    {
        if (Same(x, KnownEnd)) return "файла";
        if (x >= tick) return "такта (" + Clock(x) + ")";
        return Where(x);
    }

    static string Show(DateTime d)
    {
        return d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv) + " " + d.Kind;
    }

    static string Clock(DateTime d)
    {
        return d.ToString("HH:mm:ss.fff", Inv);
    }

    static string Seconds(TimeSpan s)
    {
        return s.TotalSeconds.ToString("0.000", Inv);
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
