using BecquerelMonitor;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DeviceIngestProbeP267
{
    /// <summary>
    /// П267 (14.10.2026), повторная ревизия приёма данных приборов из волны
    /// 05.10.2026 (`c1051872`). Без железа: контроллеры и экземпляры приборов
    /// заводятся БЕЗ конструктора (<c>FormatterServices.GetUninitializedObject</c>)
    /// — ни потоков Bluetooth, ни открытия последовательного порта. Порт COM4
    /// (живой Atom Spectra на машине Amber) не трогается: экземпляр
    /// <c>AtomSpectraVCPIn</c> здесь без потока чтения и без имени порта, команда
    /// лишь кладётся в очередь отправки, которую проба читает отражением.
    ///
    /// Три плеча, каждое — до/после правки одним исходником:
    ///
    ///   1. ВРЕМЯ ВМЕСТЕ С ОТСЧЁТАМИ. RadiaCode, Obsidian и AtomSpectra кладут
    ///      гистограмму прибора в спектр из <c>update_hystogram</c> (поток окон,
    ///      через <c>Post</c>), а время спектра (<c>MeasurementTime</c>,
    ///      <c>LiveTime</c>) ставил только такт <c>MeasurementController.OnTimer</c>
    ///      (100 мс). «Стоп», нажатый между пакетом и тактом, оставлял в спектре
    ///      отсчёты пакета N со временем пакета N−1. Сцена: пакет 10 240 отсчётов
    ///      за 100 с → такт → пакет 11 264 за 110 с → «Стоп».
    ///
    ///   2. «ОЧИСТИТЬ» НЕ ПИШУЩЕГОСЯ СПЕКТРА НЕ СБРАСЫВАЕТ ПРИБОР, КОТОРЫЙ ПИШЕТ
    ///      ДРУГОЙ. Экземпляр прибора один на `guid` конфигурации; контроллер
    ///      остановленного спектра A остаётся на него подписан, и «Очистить» на A
    ///      слал прибору сброс накопителя (AtomSpectra — всегда, RadiaCode и
    ///      Obsidian — по статусу, который A слышит от набора B) — набор B терял
    ///      всё набранное. Аренда прибора (<c>deviceLeases</c>) — у B.
    ///
    ///   3. НЕТ ВЛОЖЕННЫХ ОКОН. AtomSpectra с несовпавшим числом каналов: каждый
    ///      пакет (раз в секунду) поднимал сообщение ДО остановки набора, а модальное
    ///      окно крутит свой цикл сообщений — следующий пакет, доехавший через
    ///      <c>Post</c>, открывал второе окно поверх первого, и так каждую секунду.
    ///      Цикл модального окна здесь подменён <c>Application.DoEvents</c> в потоке
    ///      ошибок, куда <c>AppUi.Report</c> пишет без окон.
    ///
    /// Код возврата: 0 — всё сошлось; 1 — не сошлось ожидание правки (так обязана
    /// отвечать сборка ДО правки — положительный контроль); 3 — не сошёлся контроль
    /// (то, что обязано быть одинаковым в обеих сборках).
    /// </summary>
    static class Program
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly Assembly App = typeof(ResultData).Assembly;
        static int bad;
        static int ctlBad;

        static void Say(string s) { Console.WriteLine(s); }

        static void Expect(bool ok, string what)
        {
            if (!ok) bad++;
            Say((ok ? "  ok        " : "  ⛔ ПРАВКА  ") + what);
        }

        static void Control(bool ok, string what)
        {
            if (!ok) ctlBad++;
            Say((ok ? "  контроль  " : "  ⛔ КОНТРОЛЬ ") + what);
        }

        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Say("DeviceIngestProbeP267: " + App.Location);

            Say("");
            Say("== 1. время спектра вместе с отсчётами того же пакета ==");
            TimeWithCounts("RadiaCode", "BecquerelMonitor.RadiaCodeDeviceController", new RadiaCodeDeviceConfig());
            TimeWithCounts("Obsidian", "BecquerelMonitor.ObsidianDeviceController", new ObsidianDeviceConfig());
            AtomSpectraDeviceConfig atomCfg = new AtomSpectraDeviceConfig();
            atomCfg.DeadTimeValue = 5.0E-06;
            TimeWithCounts("AtomSpectra", "BecquerelMonitor.AtomSpectraDeviceController", atomCfg);

            Say("");
            Say("== 2. «Очистить» на НЕ пишущемся спектре и прибор, который пишет другой спектр ==");
            ClearAtomSpectra();
            ClearBle("RadiaCode", "BecquerelMonitor.RadiaCodeDeviceController", App.GetType("BecquerelMonitor.RadiaCodeIn", true));
            ClearBle("Obsidian", "BecquerelMonitor.ObsidianDeviceController", App.GetType("BecquerelMonitor.ObsidianIn", true));

            Say("");
            Say("== 3. AtomSpectra, число каналов не совпало: сообщение одно, без вложенных ==");
            NestedReports();

            Say("");
            Say(string.Format(CultureInfo.InvariantCulture, "ИТОГ: не сошлось ожиданий правки {0}, контролей {1}", bad, ctlBad));
            return ctlBad > 0 ? 3 : (bad > 0 ? 1 : 0);
        }

        // ---------------------------------------------------------------- 1

        static object NewController(string typeName, ResultData rd, string status)
        {
            Type t = App.GetType(typeName, true);
            object c = FormatterServices.GetUninitializedObject(t);
            PulseDetector pd = new PulseDetector();
            pd.EnergySpectrum = rd.EnergySpectrum;
            t.GetField("pulseDetector", F).SetValue(c, pd);
            t.GetField("resultData", F).SetValue(c, rd);
            t.GetField("resultDataStatus", F).SetValue(c, rd.ResultDataStatus);
            FieldInfo st = t.GetField("status", F);
            if (st != null) st.SetValue(c, status);
            return c;
        }

        static ResultData NewResultData(string guid, InputDeviceConfig cfg, int channels)
        {
            ResultData rd = new ResultData();
            DeviceConfigInfo info = new DeviceConfigInfo();
            info.InputDeviceConfig = cfg;
            info.Guid = guid;
            rd.DeviceConfig = info;
            rd.EnergySpectrum = new EnergySpectrum(1.0, channels);
            rd.ResultDataStatus.PresetTime = 1000000;
            return rd;
        }

        static int[] Hist(int n, int perChannel)
        {
            int[] h = new int[n];
            for (int i = 0; i < n; i++) h[i] = perChannel;
            return h;
        }

        static object Packet(string device, int perChannel, int seconds)
        {
            int[] h = Hist(1024, perChannel);
            long sum = 1024L * perChannel;
            switch (device)
            {
                case "RadiaCode": return new RadiaCodeInDataReadyArgs(h, seconds, sum);
                case "Obsidian": return new ObsidianInDataReadyArgs(h, seconds, sum);
                default: return new AtomSpectraVCPInDataReadyArgs(h, 0, seconds, 0);
            }
        }

        static void Update(object controller, object packet)
        {
            controller.GetType().GetMethod("update_hystogram", F).Invoke(controller, new object[] { packet });
        }

        static void TimeWithCounts(string device, string controllerType, InputDeviceConfig cfg)
        {
            foreach (bool tickBeforeStop in new[] { false, true })
            {
                ResultData rd = NewResultData("p267-time-" + device + (tickBeforeStop ? "-tick" : ""), cfg, 1024);
                object c = NewController(controllerType, rd, "Recording");
                MeasurementController mc = new MeasurementController(null, rd);
                mc.DeviceController = (DeviceController)c;
                rd.MeasurementController = mc;
                rd.ResultDataStatus.Recording = true;

                Update(c, Packet(device, 10, 100));     // пакет N−1: 10 240 отсчётов за 100 с
                mc.OnTimer(null, EventArgs.Empty);      // такт окна
                Update(c, Packet(device, 11, 110));     // пакет N: 11 264 за 110 с
                if (tickBeforeStop)
                {
                    mc.OnTimer(null, EventArgs.Empty);  // контроль: такт успел до «Стопа»
                }
                // «Стоп» как DCControlPanel.StopMeasurement: прибора нет (deviceGuid
                // пуст), контроллер набора доводит своё; набор снят.
                mc.StopRecording();
                rd.ResultDataStatus.Recording = false;

                EnergySpectrum sp = rd.EnergySpectrum;
                double expectedLive = BecquerelMonitor.Utils.LiveTime.Calculate(110.0, sp.TotalPulseCount, cfg.DeadTime());
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0}{1}: отсчётов {2}, MeasurementTime {3} с, LiveTime {4} с (ждём 110 и {5}), панель {6} с",
                    device, tickBeforeStop ? " [такт до «Стопа»]" : " [«Стоп» между пакетом и тактом]",
                    sp.TotalPulseCount, R(sp.MeasurementTime), R(sp.LiveTime), R(expectedLive),
                    R(rd.ResultDataStatus.ElapsedTime.TotalSeconds));
                bool same = sp.TotalPulseCount == 11264 && sp.MeasurementTime == 110.0 && sp.LiveTime == expectedLive;
                if (tickBeforeStop)
                {
                    Control(same, line);
                }
                else
                {
                    Expect(same, line);
                }
            }
        }

        // ---------------------------------------------------------------- 2

        static IDictionary Leases()
        {
            return (IDictionary)typeof(MeasurementController).GetField("deviceLeases", S).GetValue(null);
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

        static void DropAtom(object o)
        {
            Type t = typeof(AtomSpectraVCPIn);
            IList list = (IList)t.GetField("instances", S).GetValue(null);
            lock (t.GetField("instancesLock", S).GetValue(null))
            {
                list.Remove(o);
            }
        }

        // Сколько раз команда легла в очередь отправки: кадр `0x03` + ASCII команды
        // (байты < 0x80 протокол не экранирует).
        static int Sent(object atom, string command)
        {
            ConcurrentQueue<byte> q = (ConcurrentQueue<byte>)typeof(AtomSpectraVCPIn).GetField("outcomming", F).GetValue(atom);
            byte[] all = q.ToArray();
            byte[] cmd = Encoding.ASCII.GetBytes(command);
            int n = 0;
            for (int i = 0; i + cmd.Length < all.Length; i++)
            {
                if (all[i] != 0x03) continue;
                bool eq = true;
                for (int j = 0; j < cmd.Length && eq; j++) eq = all[i + 1 + j] == cmd[j];
                if (eq) n++;
            }
            return n;
        }

        static void ClearQueue(object atom)
        {
            typeof(AtomSpectraVCPIn).GetField("outcomming", F).SetValue(atom, new ConcurrentQueue<byte>());
        }

        static void ClearAtomSpectra()
        {
            const string guid = "p267-clear-atom";
            object atom = BareAtom(guid);
            try
            {
                foreach (string scene in new[] { "other", "nobody", "self" })
                {
                    ResultData rdA = NewResultData(guid, new AtomSpectraDeviceConfig(), 1024);
                    object a = NewController("BecquerelMonitor.AtomSpectraDeviceController", rdA, null);
                    a.GetType().GetField("deviceGuid", F).SetValue(a, guid);
                    MeasurementController mcA = new MeasurementController(null, rdA);
                    mcA.DeviceController = (DeviceController)a;
                    rdA.MeasurementController = mcA;
                    MeasurementController mcB = new MeasurementController(null, NewResultData(guid, new AtomSpectraDeviceConfig(), 1024));
                    IDictionary leases = Leases();
                    leases.Remove(guid);
                    if (scene == "other") leases[guid] = mcB;
                    if (scene == "self")
                    {
                        leases[guid] = mcA;
                        rdA.ResultDataStatus.Recording = true;
                    }
                    ClearQueue(atom);
                    ((DeviceController)a).ClearMeasurementResult(rdA);
                    int rst = Sent(atom, "-rst");
                    leases.Remove(guid);
                    switch (scene)
                    {
                        case "other":
                            Expect(rst == 0, "AtomSpectra: A остановлен, прибор пишет B → сброс прибору «-rst» ушёл " + rst + " раз (ждём 0)");
                            break;
                        case "nobody":
                            Control(rst == 1, "AtomSpectra: A остановлен, прибор свободен → «-rst» " + rst + " раз (ждём 1: следующий Пуск A продолжит с нуля)");
                            break;
                        default:
                            Control(rst == 1, "AtomSpectra: A пишется сам → «-rst» " + rst + " раз (ждём 1)");
                            break;
                    }
                }
            }
            finally
            {
                DropAtom(atom);
            }
        }

        static object BareBle(Type t, string guid, string state)
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
            SetBleState(o, state);
            t.GetField("thread_alive", F).SetValue(o, true);
            IList list = (IList)t.GetField("instances", S).GetValue(null);
            lock (t.GetField("instancesLock", S).GetValue(null))
            {
                list.Add(o);
            }
            return o;
        }

        static void SetBleState(object o, string state)
        {
            FieldInfo sf = o.GetType().GetField("state", F);
            sf.SetValue(o, Enum.Parse(sf.FieldType, state));
        }

        static string BleState(object o)
        {
            return o.GetType().GetField("state", F).GetValue(o).ToString();
        }

        static void ClearBle(string device, string controllerType, Type inType)
        {
            string guid = "p267-clear-" + device;
            object dev = BareBle(inType, guid, "Recording");
            try
            {
                foreach (string scene in new[] { "other", "nobody", "self" })
                {
                    SetBleState(dev, "Recording");
                    InputDeviceConfig cfg = device == "RadiaCode" ? (InputDeviceConfig)new RadiaCodeDeviceConfig() : new ObsidianDeviceConfig();
                    ResultData rdA = NewResultData(guid, cfg, 1024);
                    // Статус «Recording» A слышит от набора B: контроллер остановленного
                    // спектра остаётся подписан на общий экземпляр прибора.
                    object a = NewController(controllerType, rdA, "Recording");
                    a.GetType().GetField("deviceGuid", F).SetValue(a, guid);
                    MeasurementController mcA = new MeasurementController(null, rdA);
                    mcA.DeviceController = (DeviceController)a;
                    rdA.MeasurementController = mcA;
                    MeasurementController mcB = new MeasurementController(null, NewResultData(guid, cfg, 1024));
                    IDictionary leases = Leases();
                    leases.Remove(guid);
                    if (scene == "other") leases[guid] = mcB;
                    if (scene == "self")
                    {
                        leases[guid] = mcA;
                        rdA.ResultDataStatus.Recording = true;
                        a.GetType().GetField("measuring", F).SetValue(a, true);
                    }
                    ((DeviceController)a).ClearMeasurementResult(rdA);
                    string after = BleState(dev);
                    leases.Remove(guid);
                    switch (scene)
                    {
                        case "other":
                            Expect(after == "Recording", device + ": A остановлен, прибор пишет B → прибор " + after + " (ждём Recording, без сброса)");
                            break;
                        case "nobody":
                            Control(after == "Resetting", device + ": A остановлен, прибор свободен, статус Recording → " + after + " (ждём Resetting, как было)");
                            break;
                        default:
                            Control(after == "Resetting", device + ": A пишется сам → " + after + " (ждём Resetting)");
                            break;
                    }
                }
            }
            finally
            {
                IList list = (IList)inType.GetField("instances", S).GetValue(null);
                lock (inType.GetField("instancesLock", S).GetValue(null))
                {
                    list.Remove(dev);
                }
            }
        }

        // ---------------------------------------------------------------- 3

        /// <summary>
        /// Поток ошибок, в который <c>AppUi.Report</c> пишет без окон. Строка
        /// сообщения здесь крутит цикл сообщений — ровно то, что делает модальное
        /// <c>MessageBox</c> в окнах: присланное через <c>Post</c> исполняется
        /// ВНУТРИ открытого окна.
        /// </summary>
        sealed class ModalLikeWriter : TextWriter
        {
            public readonly string Marker;
            public int Count;
            public int Depth;
            public int MaxDepth;

            public ModalLikeWriter(string marker) { this.Marker = marker; }

            public override Encoding Encoding { get { return Encoding.UTF8; } }

            public override void Write(char value) { }

            public override void WriteLine(string value)
            {
                if (value == null || value.IndexOf(this.Marker, StringComparison.Ordinal) < 0)
                {
                    return;
                }
                this.Count++;
                this.Depth++;
                if (this.Depth > this.MaxDepth) this.MaxDepth = this.Depth;
                for (int i = 0; i < 5; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                this.Depth--;
            }
        }

        static void NestedReports()
        {
            Type resources = App.GetType("BecquerelMonitor.Properties.Resources", true);
            string text = (string)resources.GetProperty("ERRIncompatibleChannelParameters", S).GetValue(null, null);
            string marker = text.Length > 24 ? text.Substring(0, 24) : text;

            WindowsFormsSynchronizationContext ctx = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ctx);
            SynchronizationContext saved = MainForm.originalContext;
            MainForm.originalContext = ctx;
            TextWriter savedErr = Console.Error;
            try
            {
                foreach (int posts in new[] { 1, 3 })
                {
                    ResultData rd = NewResultData("p267-nested-" + posts, new AtomSpectraDeviceConfig(), 1024);
                    object c = NewController("BecquerelMonitor.AtomSpectraDeviceController", rd, null);
                    MeasurementController mc = new MeasurementController(null, rd);
                    mc.DeviceController = (DeviceController)c;
                    rd.MeasurementController = mc;
                    rd.ResultDataStatus.Recording = true;

                    ModalLikeWriter w = new ModalLikeWriter(marker);
                    Console.SetError(w);
                    MethodInfo dataReady = c.GetType().GetMethod("DataIn_DataReady", F);
                    for (int i = 0; i < posts; i++)
                    {
                        // прибор шлёт 8192 канала, у документа 1024
                        dataReady.Invoke(c, new object[] { null, new AtomSpectraVCPInDataReadyArgs(Hist(8192, 1), 0, 10 + i, 0) });
                    }
                    for (int i = 0; i < 20; i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(5);
                    }
                    Console.SetError(savedErr);
                    string line = string.Format(CultureInfo.InvariantCulture,
                        "пакетов в очереди {0}: сообщений {1}, наибольшая вложенность {2}, набор {3}",
                        posts, w.Count, w.MaxDepth, rd.ResultDataStatus.Recording ? "идёт" : "снят");
                    if (posts == 1)
                    {
                        Control(w.Count == 1 && w.MaxDepth == 1 && !rd.ResultDataStatus.Recording, line + " (ждём 1, 1, снят)");
                    }
                    else
                    {
                        Expect(w.Count == 1 && w.MaxDepth == 1 && !rd.ResultDataStatus.Recording, line + " (ждём 1, 1, снят)");
                    }
                }
            }
            finally
            {
                Console.SetError(savedErr);
                MainForm.originalContext = saved;
            }
        }
    }
}
