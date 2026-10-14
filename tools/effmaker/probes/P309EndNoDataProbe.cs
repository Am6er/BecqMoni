// Проба полосы П309 (15.10.2026): «ПУСК» ПРОДОЛЖЕНИЯ, НЕ ДАВШИЙ НИ ОДНОГО ЧТЕНИЯ, НЕ ДВИГАЕТ
// КОНЕЦ НАБОРА у AmplitudaSerial и AmplitudaUSB.
//
// Находка П309 (ревизия правок 14.10 по приёму данных): у загруженного или остановленного
// документа с известным началом «Пуск» начало не трогает (документ — база), но каждый конец
// сеанса — «Стоп» до первого чтения, блок не ответил за 20 с (NoAnswer), отказ сброса или
// пуска, у USB — прибор открылся, но не прислал ни кадра, — писал конец «сейчас», хотя
// отсчёты, живое и реальное время остались файловыми. Поправка на распад к дате отбора
// (`MeasurementResultManager.DecayToSamplingFactor`) усредняла тогда по [начало файла; сейчас].
// Правило — решение Amber 14.10.2026 для RadiaCode и Obsidian (П305, дословно «По первому
// пакету данных»: «при отказе до первого пакета файл не тронут»), перенесённое на два прибора,
// которых П305 не касалась.
//
// Без окон и без настоящих портов. AmplitudaSerial — НАСТОЯЩЕЙ дверью прибора
// (StartMeasurement → такт PumpTimer_Tick отражением → StopMeasurement), порт подменён
// фиктивным блоком на виртуальном времени через `AmplitudaSerialLine.PortFactory`.
// AmplitudaUSB настоящей дверью без HID-устройства не пускается: читатель создаётся
// неподнятым и кладётся в контроллер отражением, а то, что `StartCore` делает с временами
// продолжения, исполняет его же код (`RememberEndBeforeSession`, есть только после правки;
// что `StartCore` его зовёт — разбором IL). Конец сеанса — настоящий `StopMeasurement`.
// Один exe пробы работает со сборкой ДО правки (обязан показать дефект) и ПОСЛЕ.
//
//   P309EndNoDataProbe.exe       (из каталога, где лежит BecquerelMonitor.exe)
//
// Расклады (файл: начало 2024-02-07 12:28:49, конец +600 с, проба 2024-02-07 09:00,
// 123456 отсчётов, если не сказано иначе):
//   E1 Serial, блок молчит, «Стоп» сразу            — ПРАВКА: конец = конец файла;
//   E2 Serial, блок молчит, 20 с — NoAnswer (отказ) — ПРАВКА: конец = конец файла;
//   U1 USB, ни кадра, «Стоп»                        — ПРАВКА: конец = конец файла;
//   K1 Serial, блок считает, первое чтение легло, «Стоп» — КОНТРОЛЬ: конец «сейчас», отсчёты выросли;
//   K2 Serial, чистый документ, блок молчит, «Стоп» — КОНТРОЛЬ: начало и конец «сейчас»;
//   K3 Serial, данные, «Очистить» посреди набора, «Стоп» до нового чтения — КОНТРОЛЬ: конец «сейчас»;
//   K4 Serial, метка 1970 (П298), блок молчит, «Стоп» — КОНТРОЛЬ: начало = пуск − 600 с, конец «сейчас»;
//   KU USB, кадр пришёл, «Стоп»                     — КОНТРОЛЬ: конец «сейчас», отсчёты выросли;
//   KC USB, «Очистить» посреди набора, «Стоп»       — КОНТРОЛЬ: конец «сейчас».
//
// Коды: 0 — правило выполнено и контроли сработали (сборка ПОСЛЕ); 1 — дефект виден (сборка
// ДО); 3 — контроль НЕ сработал (замер пуст); 5 — обрыв.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using BecquerelMonitor;

static class P309EndNoDataProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const double CsHalfLifeYears = 30.08;
    const double CoHalfLifeYears = 5.2714;
    const double Held = 600.0;
    const long HeldCounts = 123456;
    static readonly DateTime FileStart = new DateTime(2024, 2, 7, 12, 28, 49, DateTimeKind.Local);
    static readonly DateTime FileSample = new DateTime(2024, 2, 7, 9, 0, 0, DateTimeKind.Unspecified);
    static readonly DateTime FileEnd = FileStart.AddSeconds(Held);
    static readonly Dictionary<string, FakePort> ports = new Dictionary<string, FakePort>();
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
            AmplitudaSerialLine.PortFactory = s =>
            {
                lock (ports)
                {
                    return ports[s.PortName];
                }
            };
            Serial();
            Console.WriteLine();
            Usb();
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

    static readonly MethodInfo Tick = typeof(AmplitudaSerialDeviceController).GetMethod("PumpTimer_Tick", F);

    static void Serial()
    {
        Console.WriteLine("== AmplitudaSerial — дверь прибора, фиктивный блок на виртуальном времени");

        // E1 — «Стоп» до первого чтения.
        {
            FakePort port;
            ResultData rd = SerialDoc(FileStart, FileSample, HeldCounts, Held, true, out port);
            AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
            try
            {
                Expect(c.StartMeasurement(rd), "E1: пуск не состоялся");
                DateTime t0 = DateTime.Now;
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("E1 файл, блок молчит, «Стоп» сразу", rd, t0, t1);
                Expect(Same(rd.EndTime, FileEnd), "E1: набор без единого чтения переписал конец файла на " + Show(rd.EndTime));
                Control(Same(rd.StartTime, FileStart) && rd.EnergySpectrum.TotalPulseCount == HeldCounts
                        && rd.EnergySpectrum.MeasurementTime == Held,
                    "E1: начало, отсчёты или реальное время изменились — расклад не тот");
            }
            finally
            {
                c.Dispose();
            }
        }

        // E2 — блок не ответил за 20 с: отказ NoAnswer через настоящий такт.
        {
            FakePort port;
            ResultData rd = SerialDoc(FileStart, FileSample, HeldCounts, Held, true, out port);
            AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
            try
            {
                Expect(c.StartMeasurement(rd), "E2: пуск не состоялся");
                DateTime t0 = DateTime.Now;
                Pump(c, port, () => !rd.ResultDataStatus.Recording, 60000);
                DateTime t1 = DateTime.Now;
                Control(!rd.ResultDataStatus.Recording, "E2: отказ NoAnswer не наступил за 60 с виртуального времени");
                Report("E2 файл, блок молчит, отказ NoAnswer (" + (port.NowMs / 1000.0).ToString("0.0", Inv) + " с виртуальных)", rd, t0, t1);
                Expect(Same(rd.EndTime, FileEnd), "E2: отказ без единого чтения переписал конец файла на " + Show(rd.EndTime));
                Control(Same(rd.StartTime, FileStart) && rd.EnergySpectrum.TotalPulseCount == HeldCounts,
                    "E2: начало или отсчёты изменились — расклад не тот");
            }
            finally
            {
                c.Dispose();
            }
        }

        // K1 — КОНТРОЛЬ: данные пришли — конец «сейчас», как прежде.
        {
            FakePort port;
            ResultData rd = SerialDoc(FileStart, FileSample, HeldCounts, Held, false, out port);
            AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
            try
            {
                Control(c.StartMeasurement(rd), "K1: пуск не состоялся");
                Pump(c, port, () => rd.EnergySpectrum.TotalPulseCount > HeldCounts, 60000);
                long after = rd.EnergySpectrum.TotalPulseCount;
                DateTime t0 = DateTime.Now;
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("K1 КОНТРОЛЬ файл, блок считает, чтение легло (" + (after - HeldCounts).ToString(Inv) + " отсчётов), «Стоп»", rd, t0, t1);
                Control(after > HeldCounts, "K1: ни одно чтение не легло в документ — контроль пуст");
                Control(Within(rd.EndTime, t0, t1) && Same(rd.StartTime, FileStart) && rd.EnergySpectrum.MeasurementTime > Held,
                    "K1: набор с данными — ждали конец «сейчас», начало файла и время больше 600 с");
            }
            finally
            {
                c.Dispose();
            }
        }

        // K2 — КОНТРОЛЬ: чистый документ — начало и конец «сейчас».
        {
            FakePort port;
            ResultData rd = SerialDoc(FileStart, FileSample, 0, 0.0, true, out port);
            AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
            try
            {
                DateTime t0 = DateTime.Now;
                Control(c.StartMeasurement(rd), "K2: пуск не состоялся");
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("K2 КОНТРОЛЬ чистый документ, блок молчит, «Стоп»", rd, t0, t1);
                Control(Within(rd.StartTime, t0, t1) && Within(rd.EndTime, t0, t1), "K2: чистый пуск — ждали начало и конец «сейчас»");
            }
            finally
            {
                c.Dispose();
            }
        }

        // K3 — КОНТРОЛЬ: «Очистить» посреди набора — документ уже другой, конец «сейчас».
        {
            FakePort port;
            ResultData rd = SerialDoc(FileStart, FileSample, HeldCounts, Held, false, out port);
            AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
            try
            {
                Control(c.StartMeasurement(rd), "K3: пуск не состоялся");
                Pump(c, port, () => rd.EnergySpectrum.TotalPulseCount > HeldCounts, 60000);
                Control(rd.EnergySpectrum.TotalPulseCount > HeldCounts, "K3: чтение не легло — контроль пуст");
                DateTime t0 = DateTime.Now;
                c.ClearMeasurementResult(rd);
                // Как DCControlPanel.ClearMeasurementResult: времена — ноль, спектр — заново.
                rd.ResultDataStatus.TotalTime = TimeSpan.Zero;
                rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
                rd.EnergySpectrum.Initialize();
                port.Silent = true;
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("K3 КОНТРОЛЬ файл, данные, «Очистить», блок замолчал, «Стоп»", rd, t0, t1);
                Control(Within(rd.StartTime, t0, t1) && Within(rd.EndTime, t0, t1),
                    "K3: после «Очистить» ждали начало и конец «сейчас»");
            }
            finally
            {
                c.Dispose();
            }
        }

        // K4 — КОНТРОЛЬ П298: метка 1970 — начало ставит «Пуск», конец «сейчас».
        {
            FakePort port;
            ResultData rd = SerialDoc(ResultData.UnknownStartTime, ResultData.UnknownStartTime, HeldCounts, Held, true, out port);
            AmplitudaSerialDeviceController c = new AmplitudaSerialDeviceController();
            try
            {
                DateTime t0 = DateTime.Now;
                Control(c.StartMeasurement(rd), "K4: пуск не состоялся");
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("K4 КОНТРОЛЬ П298 метка 1970, блок молчит, «Стоп»", rd, t0, t1);
                Control(Within(rd.StartTime, t0.AddSeconds(-Held), t1.AddSeconds(-Held)) && Within(rd.EndTime, t0, t1),
                    "K4: метка 1970 — ждали начало = пуск − 600 с и конец «сейчас»");
            }
            finally
            {
                c.Dispose();
            }
        }
    }

    static ResultData SerialDoc(DateTime start, DateTime sample, long counts, double real, bool silent, out FakePort port)
    {
        portSeq++;
        string name = "P309FAKE" + portSeq.ToString(Inv);
        port = new FakePort(1, silent);
        lock (ports)
        {
            ports[name] = port;
        }
        AmplitudaSerialDeviceConfig cfg = new AmplitudaSerialDeviceConfig();
        cfg.PortName = name;
        cfg.Address = 1;
        DeviceConfigInfo info = new DeviceConfigInfo();
        info.InputDeviceConfig = cfg;
        return NewDoc(info, start, sample, counts, real);
    }

    static ResultData NewDoc(DeviceConfigInfo info, DateTime start, DateTime sample, long counts, double real)
    {
        ResultData rd = new ResultData();
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
        rd.ResultDataStatus.PresetTime = 1000000;
        rd.StartTime = start;
        rd.EndTime = start.AddSeconds(real);
        rd.SampleInfo.Time = sample;
        return rd;
    }

    // Такт окна прибора (PumpTimer_Tick) отражением, пока условие не выполнится; виртуальное
    // время блока идёт и без обмена (иначе опрос раз в 3 с не наступит никогда).
    static void Pump(AmplitudaSerialDeviceController c, FakePort port, Func<bool> done, long maxVirtualMs)
    {
        long until = port.NowMs + maxVirtualMs;
        Stopwatch real = Stopwatch.StartNew();
        while (!done() && port.NowMs < until && real.ElapsedMilliseconds < 60000)
        {
            Tick.Invoke(c, new object[] { null, EventArgs.Empty });
            Thread.Sleep(2);
            port.Advance(20);
        }
        Tick.Invoke(c, new object[] { null, EventArgs.Empty });
    }

    // Один блок на линии: протокол AmplitudaSerialProtocol, время виртуальное.
    sealed class FakePort : IAmplitudaSerialPort
    {
        readonly object sync = new object();
        readonly Queue<byte> output = new Queue<byte>();
        readonly int address;
        readonly int[] channels = new int[1024];
        long nowMs;
        bool expectAddress = true;
        int pendingAddress;
        byte lastXor;
        bool running;
        double liveMs, carry;
        public volatile bool Silent;

        public FakePort(int address, bool silent)
        {
            this.address = address;
            Silent = silent;
        }

        public void Open() { }
        public void Close() { }
        public void Dispose() { }

        public void Write(byte value)
        {
            lock (sync)
            {
                if (expectAddress)
                {
                    pendingAddress = value;
                    expectAddress = false;
                    return;
                }
                expectAddress = true;
                if (Silent || pendingAddress != address) return;
                Execute(value);
            }
        }

        void Execute(byte command)
        {
            if (command == AmplitudaSerialProtocol.CommandStart) { running = true; return; }
            if (command == AmplitudaSerialProtocol.CommandStop) { running = false; return; }
            if (command == AmplitudaSerialProtocol.CommandClear) { Array.Clear(channels, 0, channels.Length); liveMs = 0; carry = 0; return; }
            if (command == AmplitudaSerialProtocol.CommandStatus) { output.Enqueue((byte)(running ? 0x01 : 0x00)); return; }
            if (command == AmplitudaSerialProtocol.CommandChecksum) { output.Enqueue(lastXor); return; }
            byte[] data = new byte[AmplitudaSerialProtocol.BlockBytes];
            if (command == AmplitudaSerialProtocol.CommandHeader)
            {
                int seconds = (int)(liveMs / 1000.0);
                data[34] = (byte)(seconds >> 8);
                data[35] = (byte)seconds;
            }
            else if (command >= AmplitudaSerialProtocol.CommandFirstPage && command < AmplitudaSerialProtocol.CommandFirstPage + 2)
            {
                int first = (command - AmplitudaSerialProtocol.CommandFirstPage) * 512;
                for (int i = 0; i < 512; i++)
                {
                    data[2 * i] = (byte)channels[first + i];
                    data[2 * i + 1] = (byte)(channels[first + i] >> 8);
                }
            }
            else
            {
                return;
            }
            lastXor = AmplitudaSerialProtocol.Xor(data, data.Length);
            foreach (byte b in data) output.Enqueue(b);
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            lock (sync)
            {
                if (output.Count == 0) { AdvanceLocked(timeoutMs); return 0; }
                int n = Math.Min(count, output.Count);
                for (int i = 0; i < n; i++) buffer[offset + i] = output.Dequeue();
                AdvanceLocked((long)Math.Ceiling(n * 0.57));
                return n;
            }
        }

        public void DiscardInput() { lock (sync) { output.Clear(); } }
        public void Sleep(int milliseconds) { lock (sync) { AdvanceLocked(milliseconds); } }
        public void Advance(long milliseconds) { lock (sync) { AdvanceLocked(milliseconds); } }
        public long NowMs { get { lock (sync) { return nowMs; } } }

        void AdvanceLocked(long milliseconds)
        {
            if (milliseconds <= 0) return;
            nowMs += milliseconds;
            if (!running || Silent) return;
            carry += 400.0 * milliseconds / 1000.0;
            int add = (int)carry;
            carry -= add;
            channels[252] += add;
            liveMs += milliseconds * 0.8;
        }
    }

    // ---------------------------------------------------------------- AmplitudaUSB

    static void Usb()
    {
        Console.WriteLine("== AmplitudaUSB — читатель без HID кладётся отражением, конец сеанса — настоящий StopMeasurement");
        Type t = typeof(AmplitudaUsbDeviceController);
        MethodInfo remember = t.GetMethod("RememberEndBeforeSession", F);
        bool calls = remember != null && Calls(t, "StartCore", remember);
        Console.WriteLine("  RememberEndBeforeSession: {0}; StartCore зовёт его (IL): {1}", remember != null ? "есть" : "НЕТ", calls ? "да" : "нет");
        Expect(calls, "USB: StartCore не запоминает конец до сеанса — набор без кадров переписывает конец");

        // U1 — ни кадра, «Стоп».
        {
            ResultData rd = NewDoc(new DeviceConfigInfo(), FileStart, FileSample, HeldCounts, Held);
            AmplitudaUsbDeviceController c = UsbSession(rd, remember);
            try
            {
                rd.EndTime = DateTime.Now;      // такт окна MainForm.OnTimer двигает конец, пока идёт набор
                DateTime t0 = DateTime.Now;
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("U1 файл, ни кадра, «Стоп»", rd, t0, t1);
                Expect(Same(rd.EndTime, FileEnd), "U1: набор без единого кадра переписал конец файла на " + Show(rd.EndTime));
                Control(Same(rd.StartTime, FileStart) && rd.EnergySpectrum.TotalPulseCount == HeldCounts,
                    "U1: начало или отсчёты изменились — расклад не тот");
            }
            finally
            {
                c.Dispose();
            }
        }

        // KU — КОНТРОЛЬ: кадр пришёл.
        {
            ResultData rd = NewDoc(new DeviceConfigInfo(), FileStart, FileSample, HeldCounts, Held);
            AmplitudaUsbDeviceController c = UsbSession(rd, remember);
            try
            {
                AmplitudaUsbReader reader = (AmplitudaUsbReader)t.GetField("reader", F).GetValue(c);
                AmplitudaUsbAccumulator acc = (AmplitudaUsbAccumulator)typeof(AmplitudaUsbReader).GetField("accumulator", F).GetValue(reader);
                acc.AddFrame(new ushort[] { 1000, 2000, 3000 }, 3, 1000000, 0.0);
                DateTime t0 = DateTime.Now;
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("KU КОНТРОЛЬ файл, кадр пришёл (3 события, 1 с), «Стоп»", rd, t0, t1);
                Control(Within(rd.EndTime, t0, t1) && rd.EnergySpectrum.TotalPulseCount == HeldCounts + 3
                        && Math.Abs(rd.EnergySpectrum.MeasurementTime - (Held + 1.0)) < 1e-6,
                    "KU: набор с кадром — ждали конец «сейчас», +3 отсчёта и +1 с");
            }
            finally
            {
                c.Dispose();
            }
        }

        // KC — КОНТРОЛЬ: «Очистить» посреди набора.
        {
            ResultData rd = NewDoc(new DeviceConfigInfo(), FileStart, FileSample, HeldCounts, Held);
            AmplitudaUsbDeviceController c = UsbSession(rd, remember);
            try
            {
                DateTime t0 = DateTime.Now;
                c.ClearMeasurementResult(rd);
                rd.ResultDataStatus.TotalTime = TimeSpan.Zero;
                rd.ResultDataStatus.ElapsedTime = TimeSpan.Zero;
                rd.EnergySpectrum.Initialize();
                c.StopMeasurement(rd);
                DateTime t1 = DateTime.Now;
                Report("KC КОНТРОЛЬ файл, «Очистить», «Стоп»", rd, t0, t1);
                Control(Within(rd.StartTime, t0, t1) && Within(rd.EndTime, t0, t1), "KC: после «Очистить» ждали начало и конец «сейчас»");
            }
            finally
            {
                c.Dispose();
            }
        }
    }

    // Состояние контроллера сразу после принятого «Пуска» продолжения (StartCore без HID):
    // читатель (неподнятый — его поток не нужен, кадры кладутся в его накопитель), документ,
    // пороги на весь диапазон, набранное время — как StartCore; времена — его же кодом.
    static AmplitudaUsbDeviceController UsbSession(ResultData rd, MethodInfo remember)
    {
        Type t = typeof(AmplitudaUsbDeviceController);
        AmplitudaUsbDeviceController c = new AmplitudaUsbDeviceController();
        AmplitudaUsbReader reader = new AmplitudaUsbReader(0x534B, 0x0874, "", 0.0);
        t.GetField("reader", F).SetValue(c, reader);
        t.GetField("resultData", F).SetValue(c, rd);
        t.GetField("lowerThreshold", F).SetValue(c, 0);
        t.GetField("upperThreshold", F).SetValue(c, 4095);
        t.GetField("deadSeconds", F).SetValue(c, 0.0);
        t.GetField("elapsedMicros", F).SetValue(c, rd.ResultDataStatus.TotalTime.Ticks / 10);
        if (remember != null)
        {
            remember.Invoke(c, new object[] { rd, rd.EnergySpectrum });
        }
        rd.ResultDataStatus.Recording = true;
        return c;
    }

    // ---------------------------------------------------------------- служебное

    static bool Calls(Type t, string caller, MethodInfo callee)
    {
        MethodInfo core = t.GetMethod(caller, F);
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

    static void Report(string name, ResultData rd, DateTime t0, DateTime t1)
    {
        double cs = MeasurementResultManager.DecayToSamplingFactor(CsHalfLifeYears, rd.SampleInfo.Time, rd.StartTime, rd.EndTime);
        double co = MeasurementResultManager.DecayToSamplingFactor(CoHalfLifeYears, rd.SampleInfo.Time, rd.StartTime, rd.EndTime);
        Console.WriteLine("  {0}:", name);
        Console.WriteLine("    начало {0}, конец {1}, проба {2}; отсчёты {3}, время {4} с; «Стоп» [{5} … {6}]",
            Show(rd.StartTime), Show(rd.EndTime), Show(rd.SampleInfo.Time), rd.EnergySpectrum.TotalPulseCount.ToString(Inv),
            rd.EnergySpectrum.MeasurementTime.ToString("0.###", Inv), t0.ToString("HH:mm:ss.fff", Inv), t1.ToString("HH:mm:ss.fff", Inv));
        Console.WriteLine("    длина набора по датам {0}; поправка на распад Cs-137 ×{1}, Co-60 ×{2}",
            Span(rd.EndTime - rd.StartTime), cs.ToString("0.000000", Inv), co.ToString("0.000000", Inv));
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
        return d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv);
    }

    static string Span(TimeSpan s)
    {
        double days = s.TotalDays;
        return days > 365.0 ? (days / 365.25).ToString("0.00", Inv) + " лет" : s.TotalSeconds.ToString("0.000", Inv) + " с";
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
