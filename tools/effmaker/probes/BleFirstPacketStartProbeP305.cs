// Проба полосы П305 (14.10.2026): НАЧАЛО НАБОРА RadiaCode И Obsidian СТАВИТ ПЕРВЫЙ ПАКЕТ ДАННЫХ,
// А НЕ НАЖАТИЕ; ЗВУКОВОЙ ВХОД — ТОЛЬКО ПОСЛЕ ПРИНЯТОГО waveIn.Start().
//
// Решение Amber 14.10.2026 вопросником, дословно: «По первому пакету данных (Рекомендую)».
// Находка П303: «Пуск» у BLE принимается сразу, ответ прибора приходит позже статусом; отказ
// («Faulted») до первого пакета оставлял загруженному файлу его отсчёты при начале «Пуска» —
// Cs-137 при известной пробе ×1.0638 вместо ×1.00001.
//
// Правило после правки (`MeasurementController.StartSetByFirstPacket`): начало = приход первого
// пакета − время, которое прибор насчитал в нём (пакет несёт накопитель прибора целиком со своим
// временем); первый пакет — пришедший ПОСЛЕ «Пуска» нового документа или «Очистить»; набор без
// единого пакета документ не меняет — ни начало, ни конец; правило П300 (проба с меткой 1970
// следует за началом) — после пакета, с итоговым началом.
//
// Без окон и без железа (⛔ настоящий BLE и звуковые устройства не открываются, COM4 не трогается):
//   * RadiaCode, Obsidian — экземпляры прибора без конструктора (без потоков и BLE), как у П267,
//     П300, П303; «поток чтения» — сама проба: статусы и пакеты подаются закрытыми методами
//     экземпляра (`setStatusAuto`, `RaiseDataReady`), то есть ТЕМ ЖЕ путём событий, что у прибора;
//     поток окон — очередь `MainForm.originalContext`, которую проба выбирает сама (так видно и
//     пакет, стоящий в очереди окна, пока человек нажимает «Очистить»);
//   * такт окна — как `MainForm.OnTimer`: `MeasurementController.OnTimer` и «конец = сейчас»
//     у спектра, который пишется (`AMBER168`);
//   * «Пуск», «Стоп», «Очистить» — НАСТОЯЩИМИ дверьми `MeasurementController`;
//   * звуковой вход: порядок «начало / WaveIn.Start» — разбором IL (обе сборки); отказ Start —
//     поведением (сборка с правкой): `WaveIn` без конструктора, чей `Start()` отказывает «уже
//     запущен» ДО обращения к WinMM.
//
//   BleFirstPacketStartProbeP305.exe   (из каталога, где лежит BecquerelMonitor.exe)
//
// Коды: 0 — правило выполнено и контроли сработали (так обязана отвечать сборка ПОСЛЕ);
// 1 — дефект виден (так обязана отвечать сборка ДО); 3 — контроль НЕ сработал (замер пуст);
// 5 — обрыв.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using WinMM;

static class BleFirstPacketStartProbeP305
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
    static readonly DateTime KnownEnd = KnownStart.AddSeconds(Held);
    static readonly QueueContext Ui = new QueueContext();
    static int defect, control, seq;
    static bool fixedBuild;

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
            fixedBuild = typeof(RadiaCodeInDataReadyArgs).GetProperty("Received") != null;
            Console.WriteLine("сборка: {0}", fixedBuild
                ? "с правкой (у пакета прибора есть момент прихода)"
                : "без правки (у пакета прибора момента прихода нет)");
            Console.WriteLine("Cs-137 у файла: начало {0}, проба {1}, {2} отсчётов за {3} с — ×{4}",
                Show(KnownStart), Show(KnownSample), HeldCounts.ToString(Inv), Held.ToString("0", Inv),
                Decay(KnownSample, KnownStart, KnownEnd).ToString("0.000000", Inv));
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
            }
            finally
            {
                MainForm.originalContext = saved;
            }
            Audio();
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
        info.Guid = "lane305-" + type + "-" + seq.ToString(Inv);
        info.DeviceType = type;
        info.Name = "lane305 " + type;
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

    // «Пуск», «Стоп» — дверьми MeasurementController; затем поток окон доделывает присланное.
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
        rd.EnergySpectrum.Initialize();                 // DocEnergySpectrum.ClearSpectrum
        rd.PulseCollection.Pulses.Clear();
        rd.CountRates.Clear();
    }

    // Такт окна — как MainForm.OnTimer у спектра, который пишется (`AMBER168`), но БЕЗ правки П307:
    // конец «сейчас» и тогда, когда начало ждёт первого пакета, — худший случай для возврата конца
    // при отказе. Настоящий такт (с правкой П307) меряет `EndWaitsFirstPacketProbeP307`.
    static void Tick(ResultData rd)
    {
        if (!rd.ResultDataStatus.Recording) return;
        rd.MeasurementController.OnTimer(null, EventArgs.Empty);
        if (rd.ResultDataStatus.Recording)
        {
            rd.EndTime = DateTime.Now;
        }
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

    // Переход, который делает сам экземпляр (поток чтения или событие BLE): `setStatusAuto`.
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

    // Пакет так, как его отдаёт поток чтения: «Recording», затем `RaiseDataReady`. Возвращает
    // момент прихода. ⚠ Поток окон проба качает сама — пакет можно оставить в очереди.
    // ⚠ Пауза перед пакетом — не украшение: у прибора первый пакет приходит не раньше, чем
    //   через подключение BLE (секунды) или паузу сброса (0.5–1 с), а приложение считает
    //   первым пакет, пришедший СТРОГО позже нажатия; пакет того же мгновения часов, что и
    //   нажатие, — прежний (`AwaitFirstPacket`).
    static DateTime Raise(object dev, Type argsType, int deviceSeconds)
    {
        Thread.Sleep(20);
        if (State(dev) != "Recording") Status(dev, "Recording");
        int[] hist = new int[1024];
        for (int i = 0; i < hist.Length; i++) hist[i] = 1;
        long sum = hist.Length;
        DateTime received = DateTime.Now;
        object args = fixedBuild
            ? Activator.CreateInstance(argsType, new object[] { hist, deviceSeconds, sum, received })
            : Activator.CreateInstance(argsType, new object[] { hist, deviceSeconds, sum });
        dev.GetType().GetMethod("RaiseDataReady", F).Invoke(dev, new object[] { args });
        return received;
    }

    static DateTime Deliver(object dev, Type argsType, int deviceSeconds)
    {
        DateTime r = Raise(dev, argsType, deviceSeconds);
        Ui.Pump();
        return r;
    }

    // ---------------------------------------------------------------- RadiaCode, Obsidian

    static void Ble(string device, string inType, InputDeviceConfig cfg, Type argsType)
    {
        Console.WriteLine("== {0} — двери MeasurementController, экземпляр прибора без потоков и BLE; пакеты и статусы подаёт проба", device);
        cfg.GetType().GetProperty("AddressBLE").SetValue(cfg, "lane305-no-ble", null);
        cfg.GetType().GetProperty("DeviceSerial").SetValue(cfg, "lane305-serial", null);
        DeviceConfigInfo info = Register(device, cfg);
        Type t = App.GetType(inType, true);
        object dev = BareBle(t, info.Guid);
        double csFile = Decay(KnownSample, KnownStart, KnownEnd);
        try
        {
            // K1 — ГЛАВНЫЙ: файл с известным началом → «Пуск» → такты окна → «Faulted» до первого пакета.
            ResultData d1 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            DateTime t0 = DateTime.Now;
            bool ok1 = Start(d1);
            DateTime t1 = DateTime.Now;
            Tick(d1);
            Thread.Sleep(40);
            Tick(d1);
            DateTime tickEnd = d1.EndTime;
            Status(dev, "Faulted");
            Ui.Pump();
            Console.WriteLine("  K1 Пуск → такты окна → «Faulted» без единого пакета: набор {0}, отсчётов {1}",
                d1.ResultDataStatus.Recording ? "идёт" : "снят", d1.EnergySpectrum.TotalPulseCount.ToString(Inv));
            Console.WriteLine("     начало {0} ({1}), проба {2}, конец {3}",
                Show(d1.StartTime), Where(d1.StartTime, t0, t1), Show(d1.SampleInfo.Time), Show(d1.EndTime));
            double cs1 = Decay(d1.SampleInfo.Time, d1.StartTime, d1.EndTime);
            Console.WriteLine("     Cs-137 ×{0} (у файла ×{1}); с файловым началом, но концом такта было бы ×{2}",
                cs1.ToString("0.000000", Inv), csFile.ToString("0.000000", Inv),
                Decay(KnownSample, KnownStart, tickEnd).ToString("0.000000", Inv));
            Control(ok1 && !d1.ResultDataStatus.Recording && d1.EnergySpectrum.TotalPulseCount == HeldCounts,
                device + " K1: ждали принятый пуск, снятый статусом «Faulted», с отсчётами файла");
            Expect(Same(d1.StartTime, KnownStart) && Same(d1.SampleInfo.Time, KnownSample) && Same(d1.EndTime, KnownEnd),
                device + " K1: отказ до первого пакета изменил документ: начало " + Show(d1.StartTime) + ", конец " + Show(d1.EndTime));
            Expect(Math.Abs(cs1 - csFile) < 1e-9, device + " K1: Cs-137 ×" + cs1.ToString("0.000000", Inv) + " вместо ×" + csFile.ToString("0.000000", Inv));
            Dispose(d1);

            // K2 — прибор переподключается без конца (Faulted не приходит), человек жмёт «Стоп».
            ResultData d2 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            t0 = DateTime.Now;
            Start(d2);
            t1 = DateTime.Now;
            Status(dev, "Reconnecting");
            Ui.Pump();
            Tick(d2);
            Thread.Sleep(40);
            Tick(d2);
            Stop(d2);
            double cs2 = Decay(d2.SampleInfo.Time, d2.StartTime, d2.EndTime);
            Console.WriteLine("  K2 Пуск → переподключение → «Стоп» до первого пакета: начало {0} ({1}), конец {2}, Cs-137 ×{3}",
                Show(d2.StartTime), Where(d2.StartTime, t0, t1), Show(d2.EndTime), cs2.ToString("0.000000", Inv));
            Expect(Same(d2.StartTime, KnownStart) && Same(d2.SampleInfo.Time, KnownSample) && Same(d2.EndTime, KnownEnd)
                   && Math.Abs(cs2 - csFile) < 1e-9,
                device + " K2: «Стоп» до первого пакета изменил документ (×" + cs2.ToString("0.000000", Inv) + ")");
            Dispose(d2);

            // K3 — пакет через ≈1.1 с после «Пуска», прибор насчитал в нём 1 с.
            ResultData d3 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            t0 = DateTime.Now;
            Start(d3);
            t1 = DateTime.Now;
            Tick(d3);
            DateTime between = d3.StartTime;
            Thread.Sleep(1100);
            DateTime r3 = Deliver(dev, argsType, 1);
            Console.WriteLine("  K3 Пуск [{0} … {1}] → пакет пришёл {2}, прибор насчитал 1 с: до пакета начало {3}; после — {4}",
                Clock(t0), Clock(t1), Clock(r3), Where(between, t0, t1), Show(d3.StartTime));
            Console.WriteLine("     начало − «Пуск»: {0} с; начало − (приход − 1 с): {1} с; проба {2}",
                Seconds(d3.StartTime - t0), Seconds(d3.StartTime - r3.AddSeconds(-1)), Show(d3.SampleInfo.Time));
            Expect(Same(d3.StartTime, r3.AddSeconds(-1)) && Same(d3.SampleInfo.Time, KnownSample),
                device + " K3: начало не «приход − время прибора» (" + Show(d3.StartTime) + ")");
            Control(d3.EnergySpectrum.TotalPulseCount == 1024 && d3.EnergySpectrum.MeasurementTime == 1.0,
                device + " K3: пакет не лёг в документ — правило мерить нечем");
            Stop(d3);
            Control(d3.EndTime >= r3, device + " K3: «Стоп» после пакета не поставил конец «сейчас»");
            Dispose(d3);

            // K4 — новый документ (метки 1970 в начале и пробе), пакет с нулём времени прибора
            // (прибор только что сброшен): начало = приход, проба = начало (правило П300 после пакета).
            ResultData d4 = NewDoc(info, Unknown, Unknown, 0, 0.0);
            t0 = DateTime.Now;
            Start(d4);
            t1 = DateTime.Now;
            string waiting = "начало " + Where(d4.StartTime, t0, t1) + ", проба " + (Same(d4.SampleInfo.Time, Unknown) ? "метка" : Show(d4.SampleInfo.Time));
            Thread.Sleep(30);
            DateTime r4 = Deliver(dev, argsType, 0);
            Console.WriteLine("  K4 новый документ: до пакета {0}; пакет {1} (0 с прибора) → начало {2}, проба {3}",
                waiting, Clock(r4), Show(d4.StartTime), Same(d4.SampleInfo.Time, d4.StartTime) ? "= начало" : Show(d4.SampleInfo.Time));
            Expect(Same(d4.StartTime, r4) && Same(d4.SampleInfo.Time, d4.StartTime),
                device + " K4: у нового документа начало не момент первого пакета или проба не за ним");
            Stop(d4);
            Dispose(d4);

            // K5 — новый документ, прибор несёт накопленное за час: начало = приход − 3600 с.
            ResultData d5 = NewDoc(info, Unknown, Unknown, 0, 0.0);
            Start(d5);
            DateTime r5 = Deliver(dev, argsType, 3600);
            Console.WriteLine("  K5 новый документ, в пакете 3600 с прибора: начало {0} = приход − {1} с; проба {2}",
                Show(d5.StartTime), Seconds(r5 - d5.StartTime), Same(d5.SampleInfo.Time, d5.StartTime) ? "= начало" : Show(d5.SampleInfo.Time));
            Expect(Same(d5.StartTime, r5.AddSeconds(-3600)) && Same(d5.SampleInfo.Time, d5.StartTime),
                device + " K5: начало не «приход − время прибора» или проба не за ним");
            Stop(d5);
            Dispose(d5);

            // K6 — «Очистить» посреди набора: пакет, снятый ДО сброса и ждущий в очереди окна, начала
            // не ставит; первый после сброса — ставит (миг сброса = приход − время прибора).
            ResultData d6 = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            Start(d6);
            DateTime p1 = Deliver(dev, argsType, 50);
            DateTime s61 = d6.StartTime;
            Thread.Sleep(20);
            DateTime stale = Raise(dev, argsType, 55);          // в очереди окна, не прокачан
            Thread.Sleep(20);
            DateTime c0 = DateTime.Now;
            Clear(d6);
            DateTime c1 = DateTime.Now;
            string afterClear = State(dev);
            int pumped = Ui.Pump();                              // поток окон: прежний пакет, затем статус
            DateTime s62 = d6.StartTime;
            Thread.Sleep(30);
            DateTime p2 = Deliver(dev, argsType, 1);
            DateTime s63 = d6.StartTime;
            Console.WriteLine("  K6 Пуск → пакет {0} (50 с) → пакет {1} в очереди окна → Очистить [{2} … {3}] → пакет {4} (1 с)",
                Clock(p1), Clock(stale), Clock(c0), Clock(c1), Clock(p2));
            Console.WriteLine("     прибору ушло: {0}; из очереди окна исполнено {1}", afterClear, pumped.ToString(Inv));
            Console.WriteLine("     начало: после первого пакета {0}; после «Очистить» и прежнего пакета {1} ({2}); после пакета сброса {3}",
                Show(s61), Show(s62), Where(s62, c0, c1), Show(s63));
            Control(afterClear == "Resetting", device + " K6: «Очистить» не послал прибору сброс — расклад не тот");
            Expect(Same(s62, s61), device + " K6: «Очистить» или пакет, снятый до сброса, сдвинул начало (" + Show(s62) + ")");
            Expect(Same(s63, p2.AddSeconds(-1)), device + " K6: первый пакет после сброса не поставил начало «приход − время прибора»");
            Control(Same(d6.SampleInfo.Time, KnownSample), device + " K6: известная проба изменилась");
            Stop(d6);
            Dispose(d6);

            // K7 — «Очистить», когда прибор пишет ДРУГОЙ спектр (П267: сброса нет): начало не «сейчас»;
            // следующий набор этого спектра ставит его по своему первому пакету.
            ResultData d7 = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
            Start(d7);
            Deliver(dev, argsType, 10);
            Stop(d7);
            DateTime s71 = d7.StartTime;
            Leases()[info.Guid] = new MeasurementController(null, NewDoc(info, KnownStart, KnownSample, 0, 0.0));
            c0 = DateTime.Now;
            Clear(d7);
            c1 = DateTime.Now;
            Leases().Remove(info.Guid);
            string otherState = State(dev);
            DateTime s72 = d7.StartTime;
            Start(d7);
            DateTime p7 = Deliver(dev, argsType, 500);
            DateTime s73 = d7.StartTime;
            Console.WriteLine("  K7 прибор пишет другой спектр → Очистить [{0} … {1}]: прибор {2}; начало {3} ({4}); продолжение → пакет {5} (500 с) → начало {6}",
                Clock(c0), Clock(c1), otherState, Show(s72), Where(s72, c0, c1), Clock(p7), Show(s73));
            Control(otherState == "Stopped", device + " K7: «Очистить» при чужой аренде тронул прибор (" + otherState + ")");
            Expect(Same(s72, s71), device + " K7: «Очистить» при чужой аренде поставил начало (" + Show(s72) + ")");
            Expect(Same(s73, p7.AddSeconds(-500)), device + " K7: следующий набор не поставил начало по первому пакету");
            Stop(d7);
            Dispose(d7);

            // K8 — известное начало, проба — метка (`A207`): отказ до пакета документ не трогает ничем;
            // принятый набор — проба за ИТОГОВЫМ началом (правило П300 после пакета).
            ResultData d8 = NewDoc(info, KnownStart, Unknown, HeldCounts, Held);
            t0 = DateTime.Now;
            Start(d8);
            t1 = DateTime.Now;
            Status(dev, "Faulted");
            Ui.Pump();
            Console.WriteLine("  K8 начало файла, проба-метка → Пуск → «Faulted»: начало {0} ({1}), проба {2}",
                Show(d8.StartTime), Where(d8.StartTime, t0, t1), Same(d8.SampleInfo.Time, Unknown) ? "метка" : Show(d8.SampleInfo.Time));
            Expect(Same(d8.StartTime, KnownStart) && Same(d8.SampleInfo.Time, Unknown) && Same(d8.EndTime, KnownEnd),
                device + " K8: отказ до первого пакета изменил документ");
            Start(d8);
            DateTime p8 = Deliver(dev, argsType, 2);
            Console.WriteLine("     → Пуск → пакет {0} (2 с): начало {1}, проба {2}",
                Clock(p8), Show(d8.StartTime), Same(d8.SampleInfo.Time, d8.StartTime) ? "= начало" : Show(d8.SampleInfo.Time));
            Control(Same(d8.SampleInfo.Time, d8.StartTime), device + " K8: проба не следует за началом (правило П300)");
            Expect(Same(d8.StartTime, p8.AddSeconds(-2)), device + " K8: начало не по первому пакету");
            Stop(d8);
            Dispose(d8);

            // K9 — КОНТРОЛЬ (одинаково в обеих сборках): продолжение без «Очистить» начала не двигает.
            ResultData d9 = NewDoc(info, Unknown, KnownSample, 0, 0.0);
            Start(d9);
            Deliver(dev, argsType, 5);
            Stop(d9);
            DateTime s91 = d9.StartTime;
            Start(d9);
            DateTime p9 = Deliver(dev, argsType, 9);
            Stop(d9);
            Console.WriteLine("  K9 КОНТРОЛЬ Пуск → пакет → Стоп → Пуск → пакет → Стоп: начало {0} → {1}; конец {2}",
                Show(s91), Show(d9.StartTime), Show(d9.EndTime));
            Control(Same(d9.StartTime, s91) && d9.EndTime >= p9, device + " K9: продолжение сдвинуло начало или конец не «сейчас»");
            Dispose(d9);

            // K10 — КОНТРОЛЬ: синхронный отказ (конфигурация не того прибора) документ не трогает.
            ResultData d10 = NewDoc(Register(device, new AtomSpectraDeviceConfig()), KnownStart, KnownSample, HeldCounts, Held);
            bool ok10 = Start(d10);
            Control(!ok10 && Same(d10.StartTime, KnownStart) && Same(d10.EndTime, KnownEnd),
                device + " K10: синхронный отказ пуска изменил документ или пуск принят");
            Leases().Remove(d10.DeviceConfig.Guid);
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

    // ---------------------------------------------------------------- звуковой вход

    static void Audio()
    {
        Console.WriteLine("== Звуковой вход — без звуковой карты: порядок разбором IL, отказ Start — поведением");
        Type ct = typeof(AudioInputDeviceController);
        // nameof, а не строка: сторож check_headless читает имена в литералах пробы как вызов
        // отражением и находил бы одноимённый метод DCControlPanel (с окном) — как у П300.
        MethodInfo startMeasurement = ct.GetMethod(nameof(AudioInputDeviceController.StartMeasurement), F);
        MethodInfo begin = ct.GetMethod("BeginRecording", F);
        MethodInfo setStart = typeof(ResultData).GetProperty("StartTime").GetSetMethod();
        MethodInfo waveStart = typeof(WaveIn).GetMethod("Start", Type.EmptyTypes);
        List<MethodBase> calls = Calls(startMeasurement, ct);
        int iSet = calls.FindIndex(m => m == setStart);
        int iStart = calls.FindIndex(m => m == waveStart);
        Console.WriteLine("  A1 IL пуска: вызовов {0}; первое «начало =» — №{1}, WaveIn.Start — №{2}; отдельный BeginRecording: {3}",
            calls.Count.ToString(Inv), iSet.ToString(Inv), iStart.ToString(Inv), begin != null ? "есть" : "нет");
        Control(iSet >= 0 && iStart >= 0, "A1: в IL пуска не найдены ни «начало =», ни WaveIn.Start — мерить нечем");
        Expect(iSet > iStart, "A1: начало ставится ДО WaveIn.Start — отказ старта оставит файлу начало «Пуска»");

        if (begin == null)
        {
            Console.WriteLine("  A2 поведение: метода BeginRecording в этой сборке нет — меряется только порядок (A1)");
            return;
        }
        // A2 — `WaveIn` без конструктора, чей Start() отказывает «уже запущен» до обращения к WinMM.
        DeviceConfigInfo info = Register("AudioInput", new AudioInputDeviceConfig());
        ResultData rd = NewDoc(info, KnownStart, KnownSample, HeldCounts, Held);
        object controller = FormatterServices.GetUninitializedObject(ct);
        WaveIn w = (WaveIn)FormatterServices.GetUninitializedObject(typeof(WaveIn));
        typeof(WaveIn).GetField("startStopLock", F).SetValue(w, new object());
        typeof(WaveIn).GetField("bufferingLock", F).SetValue(w, new object());
        typeof(WaveIn).GetField("bufferMaintainerThread", F).SetValue(w, new Thread(() => { }));
        string failure = null;
        try
        {
            begin.Invoke(controller, new object[] { rd, w, 0 });
        }
        catch (TargetInvocationException ex)
        {
            failure = ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
        }
        GC.SuppressFinalize(w);
        object handlers = typeof(WaveIn).GetField("DataReady", F).GetValue(w);
        double cs = Decay(rd.SampleInfo.Time, rd.StartTime, rd.EndTime);
        Console.WriteLine("  A2 Start() отказал ({0}): набор {1}, подписка на буферы {2}, начало {3}, Cs-137 ×{4}",
            failure ?? "отказа нет", rd.ResultDataStatus.Recording ? "ИДЁТ" : "снят", handlers == null ? "снята" : "ОСТАЛАСЬ",
            Same(rd.StartTime, KnownStart) ? "файла" : Show(rd.StartTime), cs.ToString("0.000000", Inv));
        Control(failure != null, "A2: подменный WaveIn не отказал — мерить нечем");
        Expect(Same(rd.StartTime, KnownStart) && Same(rd.EndTime, KnownEnd) && Same(rd.SampleInfo.Time, KnownSample),
            "A2: отказ WaveIn.Start изменил документ");
        Expect(!rd.ResultDataStatus.Recording && handlers == null,
            "A2: отказ WaveIn.Start оставил набор идущим или подписку на буферы");
    }

    // Последовательность вызовов метода по IL; вызовы закрытых методов своего типа — раскрываются.
    static List<MethodBase> Calls(MethodInfo method, Type own)
    {
        List<MethodBase> list = new List<MethodBase>();
        byte[] il = method.GetMethodBody().GetILAsByteArray();
        int i = 0;
        while (i < il.Length)
        {
            short value = il[i] == 0xFE ? unchecked((short)(0xFE00 | il[i + 1])) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            OpCode op = Ops[value];
            int operand = i;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: i += 1; break;
                case OperandType.InlineVar: i += 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: i += 8; break;
                case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                default: i += 4; break;
            }
            if (op.OperandType == OperandType.InlineMethod && (op == OpCodes.Call || op == OpCodes.Callvirt))
            {
                MethodBase m = method.Module.ResolveMethod(BitConverter.ToInt32(il, operand));
                MethodInfo mi = m as MethodInfo;
                if (mi != null && mi.DeclaringType == own && !mi.IsPublic && mi.GetMethodBody() != null && mi != method)
                {
                    list.AddRange(Calls(mi, own));
                }
                else
                {
                    list.Add(m);
                }
            }
        }
        return list;
    }

    static readonly Dictionary<short, OpCode> Ops = BuildOps();

    static Dictionary<short, OpCode> BuildOps()
    {
        Dictionary<short, OpCode> d = new Dictionary<short, OpCode>();
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            OpCode op = (OpCode)f.GetValue(null);
            d[op.Value] = op;
        }
        return d;
    }

    // ---------------------------------------------------------------- служебное

    static double Decay(DateTime sample, DateTime start, DateTime end)
    {
        return MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, sample, start, end);
    }

    static bool Same(DateTime a, DateTime b)
    {
        return a.Ticks == b.Ticks && a.Kind == b.Kind;
    }

    static string Where(DateTime x, DateTime lo, DateTime hi)
    {
        if (x >= lo && x <= hi) return "«сейчас» нажатия";
        if (Same(x, KnownStart)) return "файла";
        if (Same(x, Unknown)) return "метка";
        return "прежнее";
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
