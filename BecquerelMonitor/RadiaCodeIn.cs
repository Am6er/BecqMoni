using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using Windows.Devices.Radios;
using Windows.Devices.Bluetooth.Advertisement;

namespace BecquerelMonitor
{
    public class RadiaCodeIn : IDisposable
    {
        private Thread readerThread, discoveryThread;
        private volatile bool thread_alive = true;
        private int[] hystogram_buffered = new int[1024];
        private enum State { Connecting, Connected, Disconnected, Resetting, Calibration, CalibrationDone, CalibrationFail };
        private State state = State.Disconnected;
        private double cps;
        private String deviceserial, addressble;
        private string guid;
        private volatile bool device_serial_changed;
        //protocol
        private const string RC_BLE_Service = "e63215e5-7003-49d8-96b0-b024798fb901";
        private const string RC_BLE_Characteristic = "e63215e6-7003-49d8-96b0-b024798fb901";
        private const string RC_BLE_Notify = "e63215e7-7003-49d8-96b0-b024798fb901";
        private const string RC_SET_EXCHANGE = "\x08\x00\x00\x00\x07\x00\x00\x80\x01\xff\x12\xff";
        private const string RC_GET_SPECTRUM = "\x08\x00\x00\x00&\x08\x00\x80\x00\x02\x00\x00";
        private const string RC_RESET_SPECTRUM = "\x0c\x00\x00\x00'\x08\x00\x82\x00\x02\x00\x00\x00\x00\x00\x00";
        private const string RC_VS_ENERGY_CALIB = "\x18\x00\x00\x00'\x08\x00\x83\x02\x02\x00\x00\x0c\x00\x00\x00";
        BluetoothLEDevice dev = null;
        GattDeviceService service = null;
        GattCharacteristic characteristic, characteristicNotify = null;
        RCSpectrum packet = new RCSpectrum();
        private BluetoothLEAdvertisementWatcher watcher;

        public event EventHandler<RadiaCodeInDataReadyArgs> DataReady;
        public event EventHandler<EventArgs> PortFailure;
        public event EventHandler<RadiaCodeStatusArgs> Status;
        public event EventHandler<RadiaCodeTroubleShootArgs> TroubleShoot;
        private static List<RadiaCodeIn> instances = new List<RadiaCodeIn>();
        private bool trshoot = false;
        private bool calibration = false;
        private bool calibration_sent = false;
        private PolynomialEnergyCalibration polynomialEnergyCalibration;

        float A0, A1, A2;

        public void setCalibration(PolynomialEnergyCalibration cal)
        {
            this.polynomialEnergyCalibration = cal;
        }

        public static void cleanUp(string guid)
        {
            foreach (RadiaCodeIn s in instances)
            {
                if (s.GUID.Equals(guid))
                {
                    instances.Remove(s);
                    s.Dispose();
                    Trace.WriteLine("Instance " + guid + " removed!");
                    return;
                }
            }
        }

        private void sendTroubleShoot(string text)
        {
            if (TroubleShoot != null && this.trshoot)
            {

                TroubleShoot(this, new RadiaCodeTroubleShootArgs(text));
            }
        }

        private void setStatus(State state)
        {
            this.state = state;
            if (Status != null) Status(this, new RadiaCodeStatusArgs(getStateString()));
        }

        public string getStateString()
        {
            switch ((int)state)
            {
                case 0: return "Connecting";
                case 1: return "Connected";
                case 2: return "Disconnected";
                case 3: return "Resetting";
                case 4: return "Calibration";
                case 5: return "Calibration done";
                case 6: return "Calibration fail";
                default: return "Unknown";
            }
        }

        public static List<RadiaCodeIn> getAllInstances()
        {
            return instances;
        }

        public static RadiaCodeIn getInstance(string guid, bool troubleshoot = false)
        {
            foreach (RadiaCodeIn s in instances)
            {
                if (s == null) continue;
                if (guid.Equals(s.GUID))
                {
                    return s;
                }
            }
            RadiaCodeIn instance = new RadiaCodeIn(guid, troubleshoot);
            instances.Add(instance);
            return instance;
        }

        public static void finishAll()
        {
            foreach (RadiaCodeIn s in instances)
            {
                if (s != null) s.Dispose();
            }
            instances.Clear();
        }

        private async Task TestBT()
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
                if (null != adapter)
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

        private void doDiscovery()
        {
            Trace.WriteLine("Run discovery, to awaiken device");
            if (watcher == null) watcher = new BluetoothLEAdvertisementWatcher();
            watcher.ScanningMode = BluetoothLEScanningMode.Active;
            watcher.Received += Watcher_Recived;
            watcher.Start();
            Thread.Sleep(5000);
            watcher.Stop();
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
                    GattDeviceServicesResult servisesResult = await dev.GetGattServicesForUuidAsync(Guid.Parse(RC_BLE_Service));
                    if (servisesResult != null && servisesResult.Status == GattCommunicationStatus.Success)
                    {
                        service = servisesResult.Services[0];
                        GattCharacteristicsResult characteristicsResult = await service.GetCharacteristicsForUuidAsync(Guid.Parse(RC_BLE_Characteristic));
                        if (characteristicsResult != null && characteristicsResult.Status == GattCommunicationStatus.Success)
                        {
                            characteristic = characteristicsResult.Characteristics[0];
                        }
                        GattCharacteristicsResult charNotifyResult = await service.GetCharacteristicsForUuidAsync(Guid.Parse(RC_BLE_Notify));
                        if (charNotifyResult != null && charNotifyResult.Status == GattCommunicationStatus.Success)
                        {

                            characteristicNotify = charNotifyResult.Characteristics[0];
                            characteristicNotify.ValueChanged += Characteristic_ValueChanged;
                            if (characteristicNotify.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                            {
                                GattCommunicationStatus status = await characteristicNotify.WriteClientCharacteristicConfigurationDescriptorAsync(
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
            if (dev != null) sendTroubleShoot($"Device connection status changed: {dev.ConnectionStatus}");
            if (dev == null && state == State.Connected)
            {
                Trace.WriteLine("Disconnect device event");
                sendTroubleShoot($"Device connection status changed: dev disconnected");
                setStatus(State.Connecting);
                //if (PortFailure != null) PortFailure(this, null);
            }
            if (dev != null && dev.ConnectionStatus == BluetoothConnectionStatus.Disconnected && state != State.Disconnected)
            {
                Trace.WriteLine("Disconnect device event");
                setStatus(State.Connecting);
                //if (PortFailure != null) PortFailure(this, null);
            }
        }

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] buffer = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(buffer);
                //Trace.WriteLine("Got buffer: " + string.Join(",", buffer));
                //skip exhange packet
                if (string.Join(",", buffer).StartsWith("16,0,0,0,7,0,0,128"))
                {
                    if (buffer.Length > 17 && buffer[16] == 1)
                    {
                        sendTroubleShoot("Exchange response: BLE_IF ready");
                        Trace.WriteLine("Exchange response: BLE_IF ready");
                    } else
                    {
                        sendTroubleShoot("Error! Exchange response: BLE_IF busy");
                        Trace.WriteLine("Exchange response: BLE_IF ready");
                    }
                    return;
                }
                //calibration response
                if (calibration && calibration_sent)
                {
                    if (string.Join(",", buffer).StartsWith("8,0,0,0,39,8,0,131"))
                    {
                        if (buffer.Length > 9 && buffer[8] == 1)
                        {
                            Trace.WriteLine("Calibration response - calibration done.");
                            setStatus(State.CalibrationDone);
                            calibration = false;
                            calibration_sent = false;
                            return;
                        } else
                        {
                            Trace.WriteLine("Calibration response - calibration fail.");
                            setStatus(State.CalibrationFail);
                            calibration = false;
                            calibration_sent = false;
                            return;
                        }
                    }
                    return;
                }
                if (buffer.Length > 14 && BitConverter.ToUInt32(buffer, 4) == 2147485734 && BitConverter.ToUInt32(buffer, 4) == 1)
                {
                    packet = new RCSpectrum();
                }
                if (packet.NEWPACKET)
                {
                    packet.SIZE = BitConverter.ToInt32(buffer, 0) + 4;
                    if (packet.SIZE < 20)
                    {
                        packet.BROKEN = true;
                        Trace.WriteLine("Drop packet because it is not spectrum packet");
                        return;
                    }
                    packet.buffer = new byte[packet.SIZE];
                    packet.NEWPACKET = false;
                }
                if (buffer.Length > packet.buffer.Length - packet.counter)
                {
                    packet.BROKEN = true;
                    Trace.WriteLine("Drop packet because size > expected size.");
                    return;
                }
                Array.Copy(buffer, 0, packet.buffer, packet.counter, buffer.Length);
                packet.counter += buffer.Length;
                if (packet.counter == packet.SIZE)
                {
                    // Trace.WriteLine($"Packet size: {packet.SIZE}");
                    if (packet.SIZE == 12)
                    {
                        packet.BROKEN = true;
                        Trace.WriteLine("Drop packet because it is not spectrum packet");
                        return;
                    }
                    try
                    {
                        packet.DecodePacket();
                        List<float> calibration = packet.GetCalibration();
                        this.A0 = calibration[0];
                        this.A1 = calibration[1];
                        this.A2 = calibration[2];
                    } catch (Exception ex)
                    {
                        Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                        packet.BROKEN = true;
                        Trace.WriteLine("Drop packet because it is not spectrum packet");
                        return;
                    }
                    
                    if (packet.SPECTRUM.Length == 1024)
                    {
                        packet.COMPLETE = true;
                    }
                    else
                    {
                        packet.BROKEN = true;
                        sendTroubleShoot($"Drop packet because spectrum channels: {packet.SPECTRUM.Length}. Expected: 1024 channels.");
                        Trace.WriteLine($"Drop packet because spectrum channels: {packet.SPECTRUM.Length}. Expected: 1024 channels.");
                        return;
                    }
                }
                else if (packet.counter > packet.SIZE)
                {
                    packet.BROKEN = true;
                    Trace.WriteLine($"Drop packet because size: {packet.counter} > expected size: {packet.SIZE}");
                    return;
                }
            } catch (Exception ex)
            {
                packet.BROKEN = true;
                sendTroubleShoot($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
                Trace.WriteLine($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
                return;
            }
        }

        public RadiaCodeIn(string guid, bool troubleshoot = false)
        {
            this.guid = guid;
            this.trshoot = troubleshoot;
            Trace.WriteLine("RadiaCodeIn instance created " + guid);

            readerThread = new Thread(this.run);
            readerThread.Name = "RadiaCodeIn";
            readerThread.Start();

            discoveryThread = new Thread(doDiscovery);
            discoveryThread.Name = "Discovery";
            discoveryThread.Start();
        }

        public string GUID
        {
            get { return this.guid; }
        }

        public async void setDeviceSerial(string devSerial, string addrBle)
        {
            if (addrBle != null)
            {
                this.deviceserial = devSerial;
                this.addressble = addrBle;
                this.device_serial_changed = true;
                await TestBT();
            } else
            {
                sendTroubleShoot("Address BLE is empty, nothing to test");
            }
        }

        public void sendCommand(string command)
        {
            Trace.WriteLine("Command sent: " + command);
            switch (command)
            {
                case "Start": setStatus(State.Connecting); Thread.Sleep(100); break;
                case "Stop": setStatus(State.Disconnected); DisconnectBLE(); break;
                case "Reset": setStatus(State.Resetting); Thread.Sleep(100); break;
                case "Calibration":
                    {
                        calibration = true;
                        if (this.state == State.Disconnected) setStatus(State.Connecting);
                        Thread.Sleep(100);
                        break;
                    }
                case "Continue": setStatus(State.Connected); Thread.Sleep(100); break;
                default: setStatus(State.Disconnected); break;
            }
        }

        public PolynomialEnergyCalibration GetCalibration()
        {
            if (this.A0 != 0 && this.A1 != 0 && this.A2 != 0)
            {
                PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration();
                calibration.PolynomialOrder = 2;
                calibration.Coefficients = new double[3];
                calibration.Coefficients[0] = this.A0;
                calibration.Coefficients[1] = this.A1;
                calibration.Coefficients[2] = this.A2;
                return calibration;
            }
            return null;
        }

        private async void WritePacket(string packet)
        {
            byte[] input = packet.ToCharArray().Select(b => (byte)b).ToArray<byte>();
            DataWriter writer = new DataWriter();
            writer.WriteBytes(input);
            if (characteristic != null)
            {
                try
                {
                    GattCommunicationStatus result = await characteristic.WriteValueAsync(writer.DetachBuffer());
                } catch (Exception ex) {
                    Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                    setStatus(State.Connecting);
                }
            }
        }

        private async void WriteCalibration(PolynomialEnergyCalibration calibration)
        {
            byte[] a0 = BitConverter.GetBytes(Convert.ToSingle(calibration.Coefficients[0]));
            byte[] a1 = BitConverter.GetBytes(Convert.ToSingle(calibration.Coefficients[1]));
            byte[] a2 = BitConverter.GetBytes(Convert.ToSingle(calibration.Coefficients[2]));
            byte[] rc_vs_array = RC_VS_ENERGY_CALIB.ToCharArray().Select(b => (byte)b).ToArray<byte>();

            byte[] payload = rc_vs_array.Concat(a0).Concat(a1).Concat(a2).ToArray();
            byte[] payload1 = payload.Take(18).ToArray();
            byte[] payload2 = payload.Skip(18).ToArray();

            DataWriter writer = new DataWriter();
            if (characteristic != null)
            {
                try
                {
                    writer.WriteBytes(payload1);
                    GattCommunicationStatus result = await characteristic.WriteValueAsync(writer.DetachBuffer());
                    writer.WriteBytes(payload2);
                    result = await characteristic.WriteValueAsync(writer.DetachBuffer());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                    setStatus(State.Connecting);
                }
            }
        }

        int trshootCount = 0;

        public void run()
        {
            while (thread_alive)
            {
                //Trace.WriteLine($"Current state is {state}");
                if (device_serial_changed)
                {
                    device_serial_changed = false;
                    setStatus(State.Connecting);
                }

                switch (state)
                {
                    case State.Disconnected:
                        {
                            Thread.Sleep(500);
                            break;
                        }

                    case State.Connecting:
                        {
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
                                            sendTroubleShoot("Error! 3 attempts was maded to connect device, no success connection.");
                                            sendTroubleShoot("QUIT");
                                            Thread.Sleep(500);
                                            thread_alive = false;
                                            break;
                                        }
                                    }
                                    for (int i = 0; i <= 100; i++)
                                    {
                                        Thread.Sleep(100);
                                        if (!thread_alive) break;
                                        if (dev != null && service != null && characteristic != null && characteristicNotify != null)
                                        {
                                            if (trshoot) trshootCount = 0;
                                            setStatus(State.Connected);
                                            packet = new RCSpectrum();
                                            Trace.WriteLine("RC_SET_EXCHANGE");
                                            sendTroubleShoot("Send RC_SET_EXCHANGE handshake");
                                            WritePacket(RC_SET_EXCHANGE);
                                            Thread.Sleep(100);
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
                                    // addressble is empty.
                                    if (this.trshoot)
                                    {
                                        sendTroubleShoot("QUIT");
                                    }
                                    Thread.Sleep(500);
                                    thread_alive = false;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                                DisconnectBLE();
                                Thread.Sleep(500);
                            }
                            break;
                        }

                    case State.Resetting:
                        {
                            try
                            {
                                if (!thread_alive) break;
                                if (dev == null || service == null || characteristic == null || characteristicNotify == null)
                                {
                                    setStatus(State.Connecting);
                                    break;
                                }
                                packet = new RCSpectrum();
                                WritePacket(RC_RESET_SPECTRUM);
                                Thread.Sleep(1000);
                                setStatus(State.Connecting);
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
                                setStatus(State.Connecting);
                                if (PortFailure != null) PortFailure(this, null);
                            }
                            break;
                        }

                    case State.Calibration:
                        {
                            if (polynomialEnergyCalibration != null && !calibration_sent)
                            {
                                packet = new RCSpectrum();
                                WriteCalibration(this.polynomialEnergyCalibration);
                                calibration_sent = true;
                                Trace.WriteLine("Calibration sent");
                            }
                            if (calibration_sent)
                            {
                                Thread.Sleep(200);
                            }
                            break;
                        }

                    case State.CalibrationDone:
                        {
                            Thread.Sleep(200);
                            Trace.WriteLine($"State: CalibrationDone");
                            break;
                        }

                    case State.CalibrationFail:
                        {
                            Thread.Sleep(200);
                            Trace.WriteLine($"State: CalibrationFail");
                            break;
                        }

                    case State.Connected:
                        {
                            try
                            {
                                if (!thread_alive) break;
                                if (dev == null || service == null || characteristic == null || characteristicNotify == null)
                                {
                                    setStatus(State.Connecting);
                                    break;
                                }
                                if (calibration)
                                {
                                    setStatus(State.Calibration);
                                    break;
                                }
                                packet = new RCSpectrum();
                                sendTroubleShoot("Send get Spectrum command");
                                WritePacket(RC_GET_SPECTRUM);
                                int counter = 0;
                                while (!packet.COMPLETE)
                                {
                                    if (packet.BROKEN || !thread_alive || state != State.Connected) break;
                                    Thread.Sleep(200);
                                    counter++;
                                    if (counter >= 38)
                                    {
                                        packet.BROKEN = true;
                                        setStatus(State.Connecting);
                                    }
                                    // Trace.WriteLine($"Current state is {state}, packet: {packet.SIZE}");
                                }
                                if (!thread_alive) break;
                                if (packet.BROKEN || state != State.Connected || packet.SPECTRUM == null) break;
                                sendTroubleShoot("Packet recieved");
                                sendTroubleShoot($"Spectrum real time: {packet.TIME_S}");
                                sendTroubleShoot($"Spectrum calibration: A0={packet.A0} A1={packet.A1} A2={packet.A2}");
                                packet.SPECTRUM.CopyTo(hystogram_buffered, 0);
                                ulong sum = 0;
                                Parallel.For(0, hystogram_buffered.Length, i =>
                                {
                                    sum += (ulong)hystogram_buffered[i];
                                });
                                if (packet.TIME_S != 0) this.cps = sum / packet.TIME_S;
                                sendTroubleShoot($"Spectrum cps: {this.cps}");
                                sendTroubleShoot($"Spectrum total counts: {sum}");
                                if (this.trshoot)
                                {
                                    sendTroubleShoot("QUIT");
                                    Thread.Sleep(500);
                                    thread_alive = false;
                                    break;
                                }
                                //Trace.WriteLine($"Data sent {sum}");
                                if (DataReady != null) DataReady(this, new RadiaCodeInDataReadyArgs(hystogram_buffered, (int)packet.TIME_S, (int)sum));
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"{ex.Message} {ex.StackTrace}");
                                setStatus(State.Connecting);
                                if (PortFailure != null) PortFailure(this, null);
                            }

                            break;
                        }
                }
            }
            Trace.WriteLine("RadiaCodeIn thread stopped " + guid);
            sendTroubleShoot($"RadiaCodeIn thread stopped {guid}");
        }

        public double CPS
        {
            get { return (double)this.cps; }
        }

        public void Dispose()
        {
            if (readerThread != null)
            {
                Trace.WriteLine("RadiaCodeIn thread termination request");
                thread_alive = false;
                Trace.WriteLine("Try to disconnect..");
                DisconnectBLE();
                readerThread.Join();
                discoveryThread.Join();
            }
        }

        private void DisconnectBLE()
        {
            sendTroubleShoot("Disconnect BLE service");
            Trace.WriteLine("Disconnect BLE service");
            if (service != null) service.Dispose(); service = null;
            if (dev != null) dev.Dispose(); dev = null;
            Thread.Sleep(1000);
            GC.Collect();
        }
    }

    public class RadiaCodeInDataReadyArgs : EventArgs
    {
        private int[] hystogram;
        private int elapsed_time;
        private int sum;

        public int[] Hystogram
        {
            get { return hystogram; }
        }

        public int SUM
        {
            get { return sum; }
        }

        public int ElapsedTime
        {
            get { return elapsed_time; }
        }

        public RadiaCodeInDataReadyArgs(int[] hyst, int elapsed_time, int sum)
        {
            this.hystogram = hyst;
            this.elapsed_time = elapsed_time;
            this.sum = sum;
        }
    }

    public class RadiaCodeTroubleShootArgs : EventArgs
    {
        private string text = "";

        public string Text
        {
            get { return text; }
        }

        public RadiaCodeTroubleShootArgs(string text)
        {
            this.text = text;
        }
    }

    public class RadiaCodeStatusArgs : EventArgs
    {
        private string status = "Unknown";

        public string Status
        {
            get { return status; }
        }

        public RadiaCodeStatusArgs(string status)
        {
            this.status = status;
        }
    }

    public class RCSpectrum
    {
        uint Time_s;
        float a0, a1, a2;
        int[] Spectrum;
        int size;
        public byte[] buffer;
        public int counter = 0;
        bool newPacket = true;
        bool complete = false;
        bool broken = false;

        public bool BROKEN
        {
            get { return this.broken; }
            set { this.broken = value; }
        }

        public bool COMPLETE
        {
            get { return this.complete; }
            set { this.complete = value; }
        }

        public uint TIME_S
        {
            get { return this.Time_s; }
            set { this.Time_s = value; }
        }

        public float A0
        {
            get { return this.a0; }
            set { this.a0 = value; }
        }

        public float A1
        {
            get { return this.a1; }
            set { this.a1 = value; }
        }

        public float A2
        {
            get { return this.a2; }
            set { this.a2 = value; }
        }

        public int[] SPECTRUM
        {
            get { return this.Spectrum; }
            set { this.Spectrum = value; }
        }

        public bool NEWPACKET
        {
            get { return this.newPacket; }
            set { this.newPacket = value; }
        }

        public int SIZE
        {
            get { return this.size; }
            set { this.size = value; }
        }

        public RCSpectrum() { }

        public List<float> GetCalibration()
        {
            return new List<float> { this.A0, this.A1,  this.A2 };
        }

        public void DecodePacket()
        {
            Time_s = BitConverter.ToUInt32(buffer, 16);
            a0 = BitConverter.ToSingle(buffer, 20);
            a1 = BitConverter.ToSingle(buffer, 24);
            a2 = BitConverter.ToSingle(buffer, 28);
            // ZipData spectrum starts from index = 32
            int last_value = 0;
            int result;
            List<int> sp = new List<int>();
            int i = 32;
            while (i < SIZE)
            {
                ushort position = (ushort)(((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF));
                i += 2;
                int count_occurences = (position >> 4) & 0x0FFF;
                int var_length = position & 0x0F;
                // Trace.WriteLine($"position {position}, count_occurences {count_occurences}, var_length {var_length},  last_value {last_value}, size {SIZE - i}");
                for (int j = 0; j < count_occurences; j++)
                {
                    switch (var_length)
                    {
                        case 0:
                            result = 0; break;
                        case 1:
                            result = (buffer[i] & 0xFF); i += 1; break;
                        case 2:
                            result = last_value + (sbyte)(buffer[i] & 0xFF); i += 1; break;
                        case 3:
                            result = last_value + (short)(((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 2; break;
                        case 4:
                            result = last_value + (((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)) & 0xFFFFFF; i += 3; break;
                        case 5:
                            result = last_value + (((buffer[i + 3] & 0xFF) << 24) | ((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)) & 0xFFFFFF; i += 4; break;
                        default:
                            throw new Exception("Wtf");
                    }
                    last_value = result;
                    sp.Add(result);
                }
            }
            Spectrum = sp.ToArray();
        }
    }
}
