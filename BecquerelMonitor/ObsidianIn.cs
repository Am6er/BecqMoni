using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace BecquerelMonitor
{
    public class ObsidianIn : IDisposable
    {
        private const string OBS_HISTORY_SERVICE = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
        private const string OBS_COMMAND_CHARACTERISTIC = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
        private const string OBS_NOTIFY_CHARACTERISTIC = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
        private const int SpectrumChannels = 1024;
        private const int SpectrumTimeSize = 2;
        private const int SpectrumTrailerSize = 2;
        private const int SpectrumPayloadSize = SpectrumChannels * 2 + SpectrumTimeSize;
        private const int SpectrumPacketSize = SpectrumPayloadSize + SpectrumTrailerSize;
        private const int SpectrumCompletionGuardChunkSize = 10;
        private static readonly byte[] OBS_GET_SPECTRUM = { 0x20, 0x00, 0x02, 0x00 };
        private static readonly byte[] OBS_RESET_SPECTRUM = { 0x21, 0x00, 0x01, 0x00 };

        private Thread readerThread;
        private Thread discoveryThread;
        private volatile bool thread_alive = true;
        private readonly int[] hystogram_buffered = new int[SpectrumChannels];
        private readonly object packetLock = new object();
        private State state = State.Disconnected;
        private double cps;
        private string deviceserial;
        private string addressble;
        private readonly string guid;
        private volatile bool device_serial_changed;
        private BluetoothLEDevice dev = null;
        private GattDeviceService service = null;
        private GattCharacteristic characteristic;
        private GattCharacteristic characteristicNotify = null;
        private ObsidianSpectrumPacket packet = new ObsidianSpectrumPacket();
        private BluetoothLEAdvertisementWatcher watcher;
        private bool trshoot = false;
        private int trshootCount = 0;

        private enum State
        {
            Connecting,
            Connected,
            Disconnected,
            Resetting
        }

        public event EventHandler<ObsidianInDataReadyArgs> DataReady;
        public event EventHandler<EventArgs> PortFailure;
        public event EventHandler<ObsidianStatusArgs> Status;
        public event EventHandler<ObsidianTroubleShootArgs> TroubleShoot;

        private static List<ObsidianIn> instances = new List<ObsidianIn>();

        public ObsidianIn(string guid, bool troubleshoot = false)
        {
            this.guid = guid;
            trshoot = troubleshoot;
            Trace.WriteLine("ObsidianIn instance created " + guid);

            readerThread = new Thread(run);
            readerThread.Name = "ObsidianIn";
            readerThread.Start();

            discoveryThread = new Thread(doDiscovery);
            discoveryThread.Name = "ObsidianDiscovery";
            discoveryThread.Start();
        }

        public string GUID
        {
            get { return guid; }
        }

        public double CPS
        {
            get { return cps; }
        }

        public static List<ObsidianIn> getAllInstances()
        {
            return instances;
        }

        public static ObsidianIn getInstance(string guid, bool troubleshoot = false)
        {
            foreach (ObsidianIn instance in instances)
            {
                if (instance != null && guid.Equals(instance.GUID))
                {
                    return instance;
                }
            }
            ObsidianIn newInstance = new ObsidianIn(guid, troubleshoot);
            instances.Add(newInstance);
            return newInstance;
        }

        public static void cleanUp(string guid)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                ObsidianIn instance = instances[i];
                if (instance != null && instance.GUID.Equals(guid))
                {
                    instances.RemoveAt(i);
                    instance.Dispose();
                    Trace.WriteLine("Instance " + guid + " removed!");
                    return;
                }
            }
        }

        public static void finishAll()
        {
            foreach (ObsidianIn instance in instances)
            {
                if (instance != null)
                {
                    instance.Dispose();
                }
            }
            instances.Clear();
        }

        private void sendTroubleShoot(string text)
        {
            if (TroubleShoot != null && trshoot)
            {
                TroubleShoot(this, new ObsidianTroubleShootArgs(text));
            }
        }

        private void setStatus(State nextState)
        {
            state = nextState;
            if (Status != null)
            {
                Status(this, new ObsidianStatusArgs(getStateString()));
            }
        }

        public string getStateString()
        {
            switch ((int)state)
            {
                case 0: return "Connecting";
                case 1: return "Connected";
                case 2: return "Disconnected";
                case 3: return "Resetting";
                default: return "Unknown";
            }
        }

        public void setDeviceSerial(string devSerial, string addrBle)
        {
            if (addrBle != null)
            {
                deviceserial = devSerial;
                addressble = addrBle;
                device_serial_changed = true;
            }
            else
            {
                sendTroubleShoot("Address BLE is empty, nothing to test");
            }
        }

        public void sendCommand(string command)
        {
            Trace.WriteLine("Command sent: " + command);
            switch (command)
            {
                case "Start":
                    setStatus(State.Connecting);
                    Thread.Sleep(100);
                    break;
                case "Stop":
                    setStatus(State.Disconnected);
                    DisconnectBLE();
                    break;
                case "Reset":
                    setStatus(State.Resetting);
                    Thread.Sleep(100);
                    break;
                default:
                    setStatus(State.Disconnected);
                    break;
            }
        }

        private async System.Threading.Tasks.Task TestBT()
        {
            try
            {
                Trace.WriteLine("Check BT status");
                sendTroubleShoot("Check BT status...");
                RadioAccessStatus access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed)
                {
                    sendTroubleShoot("Error! current user isn't allowed to use radio module.");
                    return;
                }
                BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync();
                if (adapter != null)
                {
                    Radio btRadio = await adapter.GetRadioAsync();
                    if (btRadio.State != RadioState.On)
                    {
                        Trace.WriteLine("BT was disabled, enabling it");
                        sendTroubleShoot("BT was disabled, enabling it.");
                        await btRadio.SetStateAsync(RadioState.On);
                    }
                    else
                    {
                        Trace.WriteLine($"BT status: {btRadio.State}");
                        sendTroubleShoot($"BT status: {btRadio.State}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception while enabling BT: {ex.Message} {ex.StackTrace}");
                sendTroubleShoot($"Exception while enabling BT: {ex.Message} {ex.StackTrace}");
            }
        }

        private async void doDiscovery()
        {
            await TestBT();
            Trace.WriteLine("Run discovery, to awake device");
            if (watcher == null)
            {
                watcher = new BluetoothLEAdvertisementWatcher();
            }
            watcher.ScanningMode = BluetoothLEScanningMode.Active;
            watcher.Received -= Watcher_Recived;
            watcher.Received += Watcher_Recived;
            try
            {
                watcher.Start();
                Thread.Sleep(5000);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex.Message}: {ex.StackTrace}", "Error doing discovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (PortFailure != null)
                {
                    PortFailure(this, null);
                }
            }
            finally
            {
                watcher.Stop();
                watcher.Received -= Watcher_Recived;
            }
        }

        private void Watcher_Recived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            return;
        }

        private async void ConnectBLE(string addrBLE)
        {
            try
            {
                Trace.WriteLine($"Try to connect BLE at addr: {addrBLE}");
                sendTroubleShoot($"Try to connect BLE at addr: {addrBLE}");
                dev = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(addrBLE));
                if (dev != null)
                {
                    dev.ConnectionStatusChanged += Dev_ConnectionStatusChanged;
                    GattDeviceServicesResult servicesResult = await dev.GetGattServicesForUuidAsync(Guid.Parse(OBS_HISTORY_SERVICE));
                    if (servicesResult != null && servicesResult.Status == GattCommunicationStatus.Success && servicesResult.Services.Count > 0)
                    {
                        service = servicesResult.Services[0];
                        GattCharacteristicsResult characteristicsResult = await service.GetCharacteristicsForUuidAsync(Guid.Parse(OBS_COMMAND_CHARACTERISTIC));
                        if (characteristicsResult != null && characteristicsResult.Status == GattCommunicationStatus.Success && characteristicsResult.Characteristics.Count > 0)
                        {
                            characteristic = characteristicsResult.Characteristics[0];
                        }
                        GattCharacteristicsResult charNotifyResult = await service.GetCharacteristicsForUuidAsync(Guid.Parse(OBS_NOTIFY_CHARACTERISTIC));
                        if (charNotifyResult != null && charNotifyResult.Status == GattCommunicationStatus.Success && charNotifyResult.Characteristics.Count > 0)
                        {
                            characteristicNotify = charNotifyResult.Characteristics[0];
                            characteristicNotify.ValueChanged += Characteristic_ValueChanged;
                            if (characteristicNotify.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                            {
                                await characteristicNotify.WriteClientCharacteristicConfigurationDescriptorAsync(
                                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
            }
        }

        private void Dev_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (dev != null)
            {
                sendTroubleShoot($"Device connection status changed: {dev.ConnectionStatus}");
            }
            if (dev == null && state == State.Connected)
            {
                Trace.WriteLine("Disconnect device event");
                sendTroubleShoot("Device connection status changed: dev disconnected");
                setStatus(State.Connecting);
            }
            if (dev != null && dev.ConnectionStatus == BluetoothConnectionStatus.Disconnected && state != State.Disconnected)
            {
                Trace.WriteLine("Disconnect device event");
                setStatus(State.Connecting);
            }
        }

        private void ResetPacket()
        {
            lock (packetLock)
            {
                packet = new ObsidianSpectrumPacket();
            }
        }

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] buffer = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(buffer);
                lock (packetLock)
                {
                    if (packet.BROKEN || packet.COMPLETE)
                    {
                        return;
                    }
                    if (packet.Counter + buffer.Length > SpectrumPacketSize)
                    {
                        packet.BROKEN = true;
                        string overflowMessage = $"Drop packet because size: {packet.Counter + buffer.Length} > expected size: {SpectrumPacketSize}";
                        sendTroubleShoot(overflowMessage);
                        Trace.WriteLine(overflowMessage);
                        return;
                    }
                    packet.Append(buffer);

                    string decodeReason = null;
                    if (packet.Counter == SpectrumPayloadSize)
                    {
                        decodeReason = "payload-size";
                    }
                    else if (packet.Counter == SpectrumPacketSize)
                    {
                        decodeReason = "full-packet-size";
                    }
                    else if (packet.Counter > SpectrumPayloadSize && buffer.Length == SpectrumCompletionGuardChunkSize)
                    {
                        decodeReason = "guard-final-chunk-size";
                    }

                    if (decodeReason != null)
                    {
                        packet.Decode();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (packetLock)
                {
                    packet.BROKEN = true;
                }
                sendTroubleShoot($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
                Trace.WriteLine($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
            }
        }

        private async void WritePacket(byte[] packetBytes)
        {
            DataWriter writer = new DataWriter();
            writer.WriteBytes(packetBytes);
            if (characteristic != null)
            {
                try
                {
                    await characteristic.WriteValueAsync(writer.DetachBuffer());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                    setStatus(State.Connecting);
                }
            }
        }

        public void run()
        {
            while (thread_alive)
            {
                if (device_serial_changed)
                {
                    device_serial_changed = false;
                    setStatus(State.Connecting);
                }

                switch (state)
                {
                    case State.Disconnected:
                        Thread.Sleep(500);
                        break;

                    case State.Connecting:
                        try
                        {
                            if (addressble != null)
                            {
                                DisconnectBLE();
                                ConnectBLE(addressble);
                                if (trshoot)
                                {
                                    trshootCount++;
                                    if (trshootCount == 3)
                                    {
                                        sendTroubleShoot("Error! 3 attempts was made to connect device, no success connection.");
                                        sendTroubleShoot("QUIT");
                                        Thread.Sleep(500);
                                        thread_alive = false;
                                        break;
                                    }
                                }
                                for (int i = 0; i <= 100; i++)
                                {
                                    Thread.Sleep(100);
                                    if (!thread_alive)
                                    {
                                        break;
                                    }
                                    if (dev != null && service != null && characteristic != null && characteristicNotify != null)
                                    {
                                        if (trshoot)
                                        {
                                            trshootCount = 0;
                                        }
                                        setStatus(State.Connected);
                                        ResetPacket();
                                        break;
                                    }
                                }
                                if (state != State.Connected)
                                {
                                    doDiscovery();
                                }
                            }
                            else
                            {
                                if (trshoot)
                                {
                                    sendTroubleShoot("QUIT");
                                }
                                Thread.Sleep(500);
                                thread_alive = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                            DisconnectBLE();
                            Thread.Sleep(500);
                        }
                        break;

                    case State.Resetting:
                        try
                        {
                            if (!thread_alive)
                            {
                                break;
                            }
                            if (dev == null || service == null || characteristic == null || characteristicNotify == null)
                            {
                                setStatus(State.Connecting);
                                break;
                            }
                            ResetPacket();
                            sendTroubleShoot("Send reset spectrum command");
                            WritePacket(OBS_RESET_SPECTRUM);
                            Thread.Sleep(500);
                            setStatus(State.Connected);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                            setStatus(State.Connecting);
                            if (PortFailure != null)
                            {
                                PortFailure(this, null);
                            }
                        }
                        break;

                    case State.Connected:
                        try
                        {
                            if (!thread_alive)
                            {
                                break;
                            }
                            if (dev == null || service == null || characteristic == null || characteristicNotify == null)
                            {
                                setStatus(State.Connecting);
                                break;
                            }
                            ResetPacket();
                            sendTroubleShoot("Send get Spectrum command");
                            WritePacket(OBS_GET_SPECTRUM);
                            int counter = 0;
                            while (true)
                            {
                                bool complete;
                                bool broken;
                                lock (packetLock)
                                {
                                    complete = packet.COMPLETE;
                                    broken = packet.BROKEN;
                                }
                                if (complete || broken || !thread_alive || state != State.Connected)
                                {
                                    break;
                                }
                                Thread.Sleep(200);
                                counter++;
                                if (counter >= 38)
                                {
                                    lock (packetLock)
                                    {
                                        packet.BROKEN = true;
                                        sendTroubleShoot($"Spectrum receive timeout. total={packet.Counter}");
                                    }
                                    setStatus(State.Connecting);
                                }
                            }
                            if (!thread_alive)
                            {
                                break;
                            }
                            int[] spectrum = null;
                            int elapsedTime = 0;
                            lock (packetLock)
                            {
                                if (packet.BROKEN || state != State.Connected || packet.SPECTRUM == null)
                                {
                                    break;
                                }
                                spectrum = packet.SPECTRUM.ToArray();
                                elapsedTime = packet.TIME_S;
                                sendTroubleShoot($"Packet received. total={packet.Counter}, extra={packet.ExtraBytesCount}, trailer={packet.TrailerValue}");
                            }
                            sendTroubleShoot($"Spectrum real time: {elapsedTime}");
                            spectrum.CopyTo(hystogram_buffered, 0);
                            long sum = hystogram_buffered.Sum(i => (long)i);
                            if (elapsedTime != 0)
                            {
                                cps = sum / (double)elapsedTime;
                            }
                            sendTroubleShoot($"Spectrum cps: {cps}");
                            sendTroubleShoot($"Spectrum total counts: {sum}");
                            if (trshoot)
                            {
                                sendTroubleShoot("QUIT");
                                Thread.Sleep(500);
                                thread_alive = false;
                                break;
                            }
                            if (DataReady != null)
                            {
                                DataReady(this, new ObsidianInDataReadyArgs(hystogram_buffered, elapsedTime, (int)sum));
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"{ex.Message} {ex.StackTrace}");
                            setStatus(State.Connecting);
                            if (PortFailure != null)
                            {
                                PortFailure(this, null);
                            }
                        }
                        break;
                }
            }
            Trace.WriteLine("ObsidianIn thread stopped " + guid);
            sendTroubleShoot($"ObsidianIn thread stopped {guid}");
        }

        public void Dispose()
        {
            if (readerThread != null)
            {
                Trace.WriteLine("ObsidianIn thread termination request");
                thread_alive = false;
                Trace.WriteLine("Try to disconnect..");
                DisconnectBLE();
                if (readerThread.IsAlive)
                {
                    readerThread.Join();
                }
                if (discoveryThread != null && discoveryThread.IsAlive)
                {
                    discoveryThread.Join();
                }
            }
        }

        private void DisconnectBLE()
        {
            sendTroubleShoot("Disconnect BLE service");
            Trace.WriteLine("Disconnect BLE service");
            try
            {
                if (characteristicNotify != null)
                {
                    characteristicNotify.ValueChanged -= Characteristic_ValueChanged;
                    characteristicNotify = null;
                }
                characteristic = null;
                if (service != null)
                {
                    service.Dispose();
                }
                service = null;
                if (dev != null)
                {
                    dev.ConnectionStatusChanged -= Dev_ConnectionStatusChanged;
                    dev.Dispose();
                }
                dev = null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception during disconnect: {ex.Message} {ex.StackTrace}");
            }
            Thread.Sleep(1000);
            GC.Collect();
        }

        private class ObsidianSpectrumPacket
        {
            public byte[] Buffer = new byte[SpectrumPacketSize];
            public int Counter;
            public bool COMPLETE;
            public bool BROKEN;
            public int[] SPECTRUM;
            public int TIME_S;

            public int ExtraBytesCount
            {
                get { return Math.Max(0, Counter - SpectrumPayloadSize); }
            }

            public int TrailerValue
            {
                get
                {
                    if (Counter < SpectrumPacketSize)
                    {
                        return -1;
                    }

                    return BitConverter.ToUInt16(Buffer, SpectrumPayloadSize);
                }
            }

            public void Append(byte[] chunk)
            {
                Array.Copy(chunk, 0, Buffer, Counter, chunk.Length);
                Counter += chunk.Length;
            }

            public void Decode()
            {
                SPECTRUM = new int[SpectrumChannels];
                for (int i = 0; i < SpectrumChannels; i++)
                {
                    SPECTRUM[i] = BitConverter.ToUInt16(Buffer, i * 2);
                }
                TIME_S = BitConverter.ToUInt16(Buffer, SpectrumChannels * 2);
                COMPLETE = true;
            }
        }
    }

    public class ObsidianInDataReadyArgs : EventArgs
    {
        private readonly int[] hystogram;
        private readonly int elapsed_time;
        private readonly long sum;

        public int[] Hystogram
        {
            get { return hystogram; }
        }

        public long SUM
        {
            get { return sum; }
        }

        public int ElapsedTime
        {
            get { return elapsed_time; }
        }

        public ObsidianInDataReadyArgs(int[] hyst, int elapsedTime, long sum)
        {
            hystogram = new int[hyst.Length];
            hyst.CopyTo(hystogram, 0);
            elapsed_time = elapsedTime;
            this.sum = sum;
        }
    }

    public class ObsidianTroubleShootArgs : EventArgs
    {
        private readonly string text;

        public string Text
        {
            get { return text; }
        }

        public ObsidianTroubleShootArgs(string text)
        {
            this.text = text;
        }
    }

    public class ObsidianStatusArgs : EventArgs
    {
        private readonly string status;

        public string Status
        {
            get { return status; }
        }

        public ObsidianStatusArgs(string status)
        {
            this.status = status;
        }
    }

    public class ObsidianCalibrationIO : IDisposable
    {
        private const string OBS_SETTINGS_SERVICE = "fe641600-00b0-4240-ba50-05ca45bf8aaa";
        private const string OBS_CALIBRATION_A0 = "fe641621-00b0-4240-ba50-05ca45bf8aaa";
        private const string OBS_CALIBRATION_A1 = "fe641622-00b0-4240-ba50-05ca45bf8aaa";
        private const string OBS_CALIBRATION_A2 = "fe641623-00b0-4240-ba50-05ca45bf8aaa";

        private BluetoothLEDevice device;
        private GattDeviceService settingsService;
        private GattCharacteristic a0Characteristic;
        private GattCharacteristic a1Characteristic;
        private GattCharacteristic a2Characteristic;

        public bool Connect(string addressBle)
        {
            if (string.IsNullOrWhiteSpace(addressBle))
            {
                return false;
            }

            EnsureBluetoothEnabled();

            device = BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(addressBle)).AsTask().GetAwaiter().GetResult();
            if (device == null)
            {
                return false;
            }

            GattDeviceServicesResult serviceResult =
                device.GetGattServicesForUuidAsync(Guid.Parse(OBS_SETTINGS_SERVICE)).AsTask().GetAwaiter().GetResult();
            if (serviceResult == null || serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            {
                return false;
            }

            settingsService = serviceResult.Services[0];
            a0Characteristic = GetCharacteristic(OBS_CALIBRATION_A0);
            a1Characteristic = GetCharacteristic(OBS_CALIBRATION_A1);
            a2Characteristic = GetCharacteristic(OBS_CALIBRATION_A2);

            return a0Characteristic != null && a1Characteristic != null && a2Characteristic != null;
        }

        public PolynomialEnergyCalibration ReadCalibration()
        {
            if (a0Characteristic == null || a1Characteristic == null || a2Characteristic == null)
            {
                return null;
            }

            PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration();
            calibration.PolynomialOrder = 2;
            calibration.Coefficients = new double[3];
            calibration.Coefficients[0] = ReadFloatCharacteristic(a0Characteristic);
            calibration.Coefficients[1] = ReadFloatCharacteristic(a1Characteristic);
            calibration.Coefficients[2] = ReadFloatCharacteristic(a2Characteristic);
            return calibration;
        }

        public bool WriteCalibration(PolynomialEnergyCalibration calibration)
        {
            if (calibration == null || calibration.Coefficients == null || calibration.PolynomialOrder < 2 || calibration.Coefficients.Length < 3)
            {
                return false;
            }

            if (a0Characteristic == null || a1Characteristic == null || a2Characteristic == null)
            {
                return false;
            }

            return WriteFloatCharacteristic(a0Characteristic, (float)calibration.Coefficients[0])
                && WriteFloatCharacteristic(a1Characteristic, (float)calibration.Coefficients[1])
                && WriteFloatCharacteristic(a2Characteristic, (float)calibration.Coefficients[2]);
        }

        private static void EnsureBluetoothEnabled()
        {
            RadioAccessStatus access = Radio.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
            if (access != RadioAccessStatus.Allowed)
            {
                throw new InvalidOperationException("Bluetooth access denied.");
            }

            BluetoothAdapter adapter = BluetoothAdapter.GetDefaultAsync().AsTask().GetAwaiter().GetResult();
            if (adapter == null)
            {
                throw new InvalidOperationException("Bluetooth adapter not found.");
            }

            Radio radio = adapter.GetRadioAsync().AsTask().GetAwaiter().GetResult();
            if (radio != null && radio.State != RadioState.On)
            {
                radio.SetStateAsync(RadioState.On).AsTask().GetAwaiter().GetResult();
            }
        }

        private GattCharacteristic GetCharacteristic(string uuid)
        {
            GattCharacteristicsResult result =
                settingsService.GetCharacteristicsForUuidAsync(Guid.Parse(uuid)).AsTask().GetAwaiter().GetResult();
            if (result == null || result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            {
                return null;
            }

            return result.Characteristics[0];
        }

        private static float ReadFloatCharacteristic(GattCharacteristic characteristic)
        {
            GattReadResult result = characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            if (result == null || result.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException("Failed to read calibration characteristic.");
            }

            DataReader reader = DataReader.FromBuffer(result.Value);
            byte[] buffer = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(buffer);
            if (buffer.Length < 4)
            {
                throw new InvalidOperationException("Calibration characteristic value is too short.");
            }

            return BitConverter.ToSingle(buffer, 0);
        }

        private static bool WriteFloatCharacteristic(GattCharacteristic characteristic, float value)
        {
            DataWriter writer = new DataWriter();
            writer.WriteBytes(BitConverter.GetBytes(value));
            GattCommunicationStatus status = characteristic.WriteValueAsync(writer.DetachBuffer()).AsTask().GetAwaiter().GetResult();
            return status == GattCommunicationStatus.Success;
        }

        public void Dispose()
        {
            if (settingsService != null)
            {
                settingsService.Dispose();
                settingsService = null;
            }

            if (device != null)
            {
                device.Dispose();
                device = null;
            }
        }
    }
}
