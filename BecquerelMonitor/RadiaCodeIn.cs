using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Documents;

namespace BecquerelMonitor
{
    public class RadiaCodeIn : IDisposable
    {
        private Thread readerThread;
        private volatile bool thread_alive = true;
        private int[] hystogram_buffered = new int[1024];
        private enum State { Connecting, Connected, Disconnected, Resetting };
        private State state = State.Disconnected;
        private int cps;
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
        BluetoothLEDevice dev = null;
        GattDeviceService service = null;
        GattCharacteristic characteristic, characteristicNotify = null;
        RCSpectrum packet = new RCSpectrum();

        private Timer timer;

        private static List<RadiaCodeIn> instances = new List<RadiaCodeIn>();

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

        public static List<RadiaCodeIn> getAllInstances()
        {
            return instances;
        }

        public static RadiaCodeIn getInstance(string guid)
        {
            foreach (RadiaCodeIn s in instances)
            {
                if (s == null) continue;
                if (guid.Equals(s.GUID))
                {
                    return s;
                }
            }
            RadiaCodeIn instance = new RadiaCodeIn(guid);
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

        private async void ConnectBLE(string addrBLE)
        {
            try
            {
                Trace.WriteLine($"Try to connect BLE at addr: {addrBLE}");
                dev = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(addrBLE));
                dev.ConnectionStatusChanged += Dev_ConnectionStatusChanged;
                if (dev != null)
                {
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
                Trace.WriteLine(ex.Message);
            }
        }

        private void Dev_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (dev == null && state != State.Connecting)
            {
                state = State.Disconnected;
                if (PortFailure != null) PortFailure(this, null);
            }
            if (dev != null && dev.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Trace.WriteLine("Disconnect device event");
                state = State.Disconnected;
                if (PortFailure != null) PortFailure(this, null);
            }
        }

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] buffer = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(buffer);
                //skip exhange packet
                if (string.Join(",", buffer).StartsWith("16,0,0,0,7,0,0,128,51,255,3,255")) return;
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
                    } catch (Exception)
                    {
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
                Trace.WriteLine($"Drop packet because EXCEPTION: {ex.Message} at {ex.StackTrace}");
                return;
            }
        }

        public RadiaCodeIn(string guid)
        {
            this.guid = guid;
            Trace.WriteLine("RadiaCodeIn instance created " + guid);
            ConnectBLE(addressble);
            readerThread = new Thread(this.run);
            readerThread.Name = "RadiaCodeIn";
            readerThread.Start();
        }

        public string GUID
        {
            get { return this.guid; }
        }

        public void setDeviceSerial(string deviceSerial, string addressBle)
        {
            this.deviceserial = deviceSerial;
            this.addressble = addressBle;
            this.device_serial_changed = true;
        }

        public void sendCommand(string command)
        {
            Trace.WriteLine("Command sent: " + command);
            switch (command)
            {
                case "Start": state = State.Connecting; break;
                case "Stop": state = State.Disconnected; break;
                case "Reset": state = State.Resetting; break;
                default: state = State.Disconnected; break;
            }
        }

        public bool isConnected()
        {
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(200);
                if (state == State.Connected) return true;
            }
            return false;
        }

        public PolynomialEnergyCalibration GetCalibration()
        {
            if (packet.A0 != 0 && packet.A1 != 0 && packet.A2 != 0)
            {
                PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration();
                calibration.PolynomialOrder = 2;
                calibration.Coefficients = new double[3];
                calibration.Coefficients[0] = packet.A0;
                calibration.Coefficients[1] = packet.A1;
                calibration.Coefficients[2] = packet.A2;
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
                } catch (Exception) {
                    state = State.Connecting;
                }
            }
        }

        public void SetExchange()
        {
            if (dev != null && service != null && characteristic != null && characteristicNotify != null && state == State.Connected)
            {
                try
                {
                    packet = new RCSpectrum();
                    WritePacket(RC_SET_EXCHANGE);
                    Trace.WriteLine("RC_SET_EXCHANGE");
                    Thread.Sleep(200);
                } catch (Exception) { }
            }
        }

        public void run()
        {
            while (thread_alive)
            {
                //Trace.WriteLine($"Current state is {state}");

                if (state == State.Disconnected)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                if (device_serial_changed)
                {
                    device_serial_changed = false;
                    state = State.Connecting;
                }

                if (state == State.Connecting)
                {
                    try
                    {
                        if (addressble != null)
                        {
                            ConnectBLE(addressble);
                            for (int i = 0; i <= 50; i++)
                            {
                                Thread.Sleep(200);
                                if (dev != null && service != null && characteristic != null && characteristicNotify != null)
                                {
                                    state = State.Connected;
                                    SetExchange();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            throw new Exception(Resources.ERREmptyPortName);
                        }
                    }
                    catch (Exception)
                    {
                        DisconnectBLE();
                        Thread.Sleep(1000);
                        continue;
                    }
                }
                else if (state == State.Resetting)
                {
                    try
                    {
                        if (!thread_alive) break;
                        if (dev == null || service == null || characteristic == null || characteristicNotify == null)
                        {
                            state = State.Connecting;
                            continue;
                        }
                        packet = new RCSpectrum();
                        WritePacket(RC_RESET_SPECTRUM);
                        Thread.Sleep(1000);
                        state = State.Connecting;
                    }
                    catch (Exception)
                    {
                        state = State.Connecting;
                        if (PortFailure != null) PortFailure(this, null);
                    }
                }
                else if (state == State.Connected)
                {
                    try
                    {
                        if (!thread_alive) break;
                        if (dev == null || service == null || characteristic == null || characteristicNotify == null)
                        {
                            state = State.Connecting;
                            continue;
                        }
                        packet = new RCSpectrum();
                        WritePacket(RC_GET_SPECTRUM);
                        int counter = 0;
                        while (!packet.COMPLETE)
                        {
                            if (packet.BROKEN || !thread_alive || state != State.Connected) break;
                            Thread.Sleep(400);
                            counter++;
                            if (counter >= 20) { packet.BROKEN = true; state = State.Connecting; }
                            Trace.WriteLine($"Current state is {state}, packet: {packet.SIZE}");
                        }
                        if (!thread_alive) break;
                        if (packet.BROKEN || state != State.Connected) continue;
                        packet.SPECTRUM.CopyTo(hystogram_buffered, 0);
                        if (packet.TIME_S != 0) this.cps = (int)(hystogram_buffered.Sum() / packet.TIME_S);
                        if (DataReady != null) DataReady(this, new RadiaCodeInDataReadyArgs(hystogram_buffered, (int)packet.TIME_S));
                    }
                    catch (Exception)
                    {
                        state = State.Connecting;
                        if (PortFailure != null) PortFailure(this, null);
                    }
                }
            }
            Trace.WriteLine("RadiaCodeIn thread stopped " + guid);
        }

        public double CPS
        {
            get { return (double)this.cps; }
        }

        public void Dispose()
        {
            if (timer != null) timer.Dispose();
            if (readerThread != null)
            {
                Trace.WriteLine("RadiaCodeIn thread termination request");
                thread_alive = false;
                Trace.WriteLine("Try to disconnect..");
                DisconnectBLE();
                readerThread.Join();
            }
        }

        private void DisconnectBLE()
        {
            Trace.WriteLine("Disconnect BLE service");
            if (service != null) service.Dispose();
            Thread.Sleep(500);
            if (dev != null) dev.Dispose();
            Thread.Sleep(500);
        }

        public event EventHandler<RadiaCodeInDataReadyArgs> DataReady;
        public event EventHandler<EventArgs> PortFailure;
    }

    public class RadiaCodeInDataReadyArgs : EventArgs
    {
        private int[] hystogram;
        private int elapsed_time;

        public int[] Hystogram
        {
            get { return hystogram; }
        }

        public int ElapsedTime
        {
            get { return elapsed_time; }
        }

        public RadiaCodeInDataReadyArgs(int[] hyst, int elapsed_time)
        {
            this.hystogram = hyst;
            this.elapsed_time = elapsed_time;
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
                            result = last_value + (((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 3; break;
                        case 5:
                            result = last_value + (((buffer[i + 3] & 0xFF) << 24) | ((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 4; break;
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
