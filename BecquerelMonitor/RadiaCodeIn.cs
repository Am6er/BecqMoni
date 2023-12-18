using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BecquerelMonitor
{
    public class RadiaCodeIn : IDisposable
    {
        private Thread readerThread;
        private volatile bool thread_alive = true;
        private int[] hystogram_buffered = new int[1024];
        private enum State { Connecting, Connected };
        private enum LastCommand { Start, Stop };
        private LastCommand lastCommand = LastCommand.Stop;
        private int cps;
        private String deviceserial, addressble;
        private string guid;
        private volatile bool device_serial_changed;
        //protocol
        private const string RC_BLE_Service = "e63215e5-7003-49d8-96b0-b024798fb901";
        private const string RC_BLE_Characteristic = "e63215e6-7003-49d8-96b0-b024798fb901";
        private const string RC_BLE_Notify = "e63215e7-7003-49d8-96b0-b024798fb901";
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
                dev = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(addrBLE));
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

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] buffer = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(buffer);
            if (packet.NEWPACKET)
            {
                packet.SIZE = BitConverter.ToInt32(buffer, 0) + 4;
                packet.buffer = new byte[packet.SIZE];
                packet.NEWPACKET = false;
            }
            Array.Copy(buffer, 0, packet.buffer, packet.counter, buffer.Length);
            packet.counter += buffer.Length;
            if (packet.counter == packet.SIZE)
            {
                Trace.WriteLine("Recieved packet.");
                packet.DecodePacket();
                packet.COMPLETE = true;
            } else if (packet.counter > packet.SIZE)
            {
                packet = new RCSpectrum();
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
                case "Start": lastCommand = LastCommand.Start; break;
                case "Stop": lastCommand = LastCommand.Stop; break;
                default: lastCommand = LastCommand.Stop; break;
            }
        }

        private async void WritePacket(string packet)
        {
            byte[] input = packet.ToCharArray().Select(b => (byte)b).ToArray<byte>();
            DataWriter writer = new DataWriter();
            writer.WriteBytes(input);
            GattCommunicationStatus result = await characteristic.WriteValueAsync(writer.DetachBuffer());
            Trace.WriteLine(String.Format("Handshake command sent, result: {0}\r\n", result.ToString()));
        }

        private long timeNowMs()
        {
            return DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }


        public void run()
        {
            State state = State.Connecting;
            while (thread_alive)
            {
                if (device_serial_changed)
                {
                    device_serial_changed = false;
                    state = State.Connecting;
                }

                if (state == State.Connecting)
                {
                    try
                    {
                        Thread.Sleep(200);
                        if (addressble != null)
                        {
                            ConnectBLE(addressble);
                            Thread.Sleep(2000);
                            if (dev != null && service != null && characteristic != null && characteristicNotify != null)
                            {
                                state = State.Connected;
                            }
                        }
                        else
                        {
                            throw new Exception(Resources.ERREmptyPortName);
                        }
                    }
                    catch (Exception)
                    {
                        try
                        {
                            DisconnectBLE();
                        }
                        catch (Exception) { }
                        Thread.Sleep(500);
                        continue;
                    }
                }
                else if (state == State.Connected)
                {
                    try
                    {
                        if (lastCommand == LastCommand.Start)
                        {
                            packet = new RCSpectrum();
                            WritePacket("\x08\x00\x00\x00&\x08\x00\x81\x00\x02\x00\x00");
                            while (!packet.COMPLETE) Thread.Sleep(500);
                            if (packet.TIME_S != 0) this.cps = (int)(packet.SPECTRUM.Sum() / packet.TIME_S);
                            packet.SPECTRUM.CopyTo(hystogram_buffered, 0);
                            if (DataReady != null) DataReady(this, new RadiaCodeInDataReadyArgs(hystogram_buffered, (int)packet.TIME_S));
                        } else
                        {
                            Thread.Sleep(500);
                        }
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
                Trace.WriteLine("Try to disconnect..");
                try
                {
                    DisconnectBLE();
                }
                catch (Exception)  { }
                Trace.WriteLine("RadiaCodeIn thread termination request");
                thread_alive = false;
                readerThread.Join();
            }
        }

        private void DisconnectBLE()
        {
            if (service != null) service.Dispose();
            if (dev != null) dev.Dispose();
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
            int last = 0;
            int v;
            List<int> sp = new List<int>();
            int i = 32;
            while (i < SIZE)
            {
                ushort u16 = (ushort)(((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF));
                i += 2;
                int cnt = (u16 >> 4) & 0x0FFF;
                int vlen = u16 & 0x0F;
                Console.WriteLine(String.Format($"u16 {u16}, cnt {cnt}, vlen {vlen}, last {last}, br_size {i}"));
                for (int j = 0; j < cnt; j++)
                {
                    switch (vlen)
                    {
                        case 0:
                            v = 0; break;
                        case 1:
                            v = (buffer[i] & 0xFF); i += 1; break;
                        case 2:
                            v = last + (sbyte)(buffer[i] & 0xFF); i += 1; break;
                        case 3:
                            v = last + (short)(((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 2; break;
                        case 4:
                            v = last + (((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 3; break;
                        case 5:
                            v = last + (((buffer[i + 3] & 0xFF) << 24) | ((buffer[i + 2] & 0xFF) << 16) | ((buffer[i + 1] & 0xFF) << 8) | (buffer[i] & 0xFF)); i += 4; break;
                        default:
                            throw new Exception("Wtf");
                    }
                    last = v;
                    sp.Add(v);
                }
            }
            Spectrum = sp.ToArray();
        }
    }
}
