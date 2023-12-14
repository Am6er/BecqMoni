using BecquerelMonitor.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BecquerelMonitor
{
    public class RadiaCodeIn : IDisposable
    {
        private BluetoothLEDevice dev;
        private Thread readerThread;
        private volatile bool thread_alive = true;
        private ManualResetEvent onDataReceivedEvent = new ManualResetEvent(false);
        private byte[] input_buffer = new byte[1024];
        private int[] hystogram = new int[1024];
        private int[] hystogram_buffered = new int[1024];
        private enum State { Connecting, Connected };
        private int cps;
        private String deviceserial, addressble;
        private int invalid_pulses;
        private string guid;
        private volatile bool device_serial_changed;
        //protocol
        private const int MAX_DATA_COUNT = 65535;
        private int packet_cmd;
        private int packet_length;
        private List<byte> packet = new List<byte>();
        private ConcurrentQueue<byte> outcomming = new ConcurrentQueue<byte>();
        private ConcurrentQueue<string> received_lines = new ConcurrentQueue<string>();

        private Timer timer;
        private Object Lock = new Object();

        private static List<RadiaCodeIn> instances = new List<RadiaCodeIn>();

        public string DeviceSerial
        {
            get
            {
                return this.deviceserial;
            }
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
            dev = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(addrBLE));
            if (dev != null)
            {
                GattDeviceServicesResult servisesResult = await dev.GetGattServicesAsync();
                if (servisesResult.Status == GattCommunicationStatus.Success)
                {

                }
            }
        }

        public RadiaCodeIn(string guid)
        {
            this.guid = guid;
            Trace.WriteLine("RadiaCodeIn instance created " + guid);
            ConnectBLE(addressble);
            



            port = new SerialPort();
            port.DataBits = 8;
            port.Parity = Parity.None;
            port.StopBits = StopBits.One;
            port.DtrEnable = false;
            port.RtsEnable = false;
            if (deviceserial != null) port.PortName = deviceserial;
            port.DataReceived += Port_DataReceived;
            readerThread = new Thread(this.run);
            readerThread.Name = "RadiaCodeIn";
            readerThread.Start();

            timer = new Timer(timer_Elapsed, null, 0, Timeout.Infinite);
        }

        private void timer_Elapsed(object state)
        {
            lock (Lock)
            {
                outcomming.Enqueue(0xff);
                onDataReceivedEvent.Set();
            }
            try
            {
                timer.Change(1000, Timeout.Infinite);
            }
            catch { }

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
            this.onDataReceivedEvent.Set();
        }

        public void updateConfig(InputDeviceConfig conf)
        {
            if (conf is RadiaCodeDeviceConfig)
            {
                RadiaCodeDeviceConfig config = (RadiaCodeDeviceConfig)conf;
                Trace.WriteLine("Config changed: Device Serial = " + config.DeviceSerial + " BLE addr = " + config.AddressBLE);
            }
        }

        public void sendCommand(string command)
        {
            Trace.WriteLine("Command sent: " + command);
            byte[] ascii = Encoding.ASCII.GetBytes(command);
            byte[] array = new byte[ascii.Length + 1];
            array[0] = 0x03;
            Array.Copy(ascii, 0, array, 1, ascii.Length);
            string ignore;
            while (received_lines.TryDequeue(out ignore)) ;
            send_packet(array);
        }

        public bool waitForAnswer(string answer, int timeout_ms)
        {
            long ms = timeNowMs() + timeout_ms;
            string str = null;
            while (!received_lines.TryDequeue(out str) && (timeNowMs() < ms)) ;
            if (str == null) return false;
            int index = str.IndexOf('\r');
            if (index != -1)
            {
                str = str.Substring(0, index);
            }
            return answer.Equals(str);
        }

        public String getCommandOutput(int timeout_ms)
        {
            long ms = timeNowMs() + timeout_ms;
            string str = null;
            while (!received_lines.TryDequeue(out str) && (timeNowMs() < ms)) ;
            if (str == null) return "";
            int index = str.IndexOf('\r');
            return str;
        }

        private long timeNowMs()
        {
            return DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            onDataReceivedEvent.Set();
        }

        public ushort crc16(ushort crc, byte data)
        {
            crc = (ushort)(crc ^ data);
            for (byte i = 0; i < 8; ++i)
            {
                if (((ushort)(crc & 0x0001)) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001); // polynomial used by MODBUS
                else
                    crc = (ushort)(crc >> 1); // right shift
            }
            return crc;
        }


        public void run()
        {
            byte[] tx_buffer = new byte[1024];
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
                        if (port.IsOpen) port.Close();
                        if (deviceserial != null)
                        {
                            port.PortName = this.deviceserial;
                            port.Open();
                            state = State.Connected;
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
                            port.Close();
                        }
                        catch (Exception)
                        {

                        }
                        Thread.Sleep(100);
                        continue;
                    }
                }
                else if (state == State.Connected)
                {
                    try
                    {
                        byte b;
                        int size = 0;
                        while (outcomming.TryDequeue(out b))
                        {
                            tx_buffer[size++] = b;
                            if (size >= tx_buffer.Length)
                            {
                                port.Write(tx_buffer, 0, tx_buffer.Length);
                                size = 0;
                            }
                        }
                        port.Write(tx_buffer, 0, size);

                        int incoming = port.BytesToRead;
                        if (incoming == 0)
                        {
                            onDataReceivedEvent.Reset();
                            onDataReceivedEvent.WaitOne();
                        }
                        if (incoming > input_buffer.Length) incoming = input_buffer.Length;
                        port.Read(input_buffer, 0, incoming);
                        for (int i = 0; i < incoming; i++)
                        {
                            if (packet_cmd == 0x01) //hystogram
                            {
                                int offset = packet[0] | (packet[1] << 8);
                                int count = (packet_length - 2) / 4;
                                for (int j = 0; j < count; j++)
                                {
                                    int index = offset + j;
                                    if (index < hystogram.Length)
                                    {
                                        int value = packet[j * 4 + 2] | (packet[j * 4 + 3] << 8) | (packet[j * 4 + 4] << 16) | (packet[j * 4 + 5] << 24);
                                        hystogram[index] = value & 0x7FFFFFF;
                                    }
                                }
                            }
                            else if (packet_cmd == 0x04) //elapsed time
                            {
                                int elapsed_time = (packet[0] | (packet[1] << 8) | (packet[2] << 16) | (packet[3] << 24));
                                elapsed_time &= 0x7FFFFFF;
                                cps = packet[6] | (packet[7] << 8) | (packet[8] << 16) | (packet[9] << 24);
                                if (packet.Count > 10)
                                {
                                    invalid_pulses = (packet[10] | (packet[11] << 8) | (packet[12] << 16) | (packet[13] << 24));
                                }
                                hystogram.CopyTo(hystogram_buffered, 0);
                                int channels = DeviceConfigManager.GetInstance().DeviceConfigMap[GUID].NumberOfChannels;
                                if (hystogram_buffered.Length > channels)
                                {
                                    int[] hystogram_compress = new int[channels];
                                    int mul = hystogram_buffered.Length / channels;
                                    try
                                    {
                                        for (int ch = 0; ch < channels; ch++)
                                        {
                                            for (int cch = 0; cch < mul; cch++)
                                            {
                                                hystogram_compress[ch] += hystogram_buffered[ch * mul + cch];
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.WriteLine(ex);
                                    }
                                    if (DataReady != null) DataReady(this, new RadiaCodeInDataReadyArgs(hystogram_compress, elapsed_time, invalid_pulses));
                                }
                                else
                                {
                                    if (DataReady != null) DataReady(this, new RadiaCodeInDataReadyArgs(hystogram_buffered, elapsed_time, invalid_pulses));
                                }
                            }
                            else if (packet_cmd == 0x03) //printf
                            {
                                byte[] arr = new byte[packet_length];
                                packet.CopyTo(0, arr, 0, packet_length);
                                string str = Encoding.ASCII.GetString(arr, 0, packet_length);
                                received_lines.Enqueue(str);
                                Trace.WriteLine("Line received: " + str);
                            }
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

        private void send_packet(byte[] data)
        {
            lock (Lock)
            {
                ushort packet_crc = 0xFFFF;
                outcomming.Enqueue(0xFF);

                onDataReceivedEvent.Set();
            }
        }

        public double CPS
        {
            get { return (double)this.cps; }
        }


        public int InvalidPulses
        {
            get { return invalid_pulses; }
        }

        public void Dispose()
        {
            if (timer != null) timer.Dispose();
            if (readerThread != null)
            {
                Trace.WriteLine("Try to close port...");
                try
                {
                    if (port.IsOpen)
                    {
                        port.DiscardInBuffer();
                        port.DiscardOutBuffer();
                        port.Close();
                    }
                }
                catch (Exception)
                {

                }
                Trace.WriteLine("RadiaCodeIn thread termination request");
                thread_alive = false;
                onDataReceivedEvent.Set();
                readerThread.Join();
            }
        }

        public event EventHandler<RadiaCodeInDataReadyArgs> DataReady;
        public event EventHandler<EventArgs> PortFailure;
    }

    public class RadiaCodeInDataReadyArgs : EventArgs
    {
        private int[] hystogram;
        private int cpu_load;
        private int elapsed_time;
        private int invalid_pulses;

        public int InvalidPulses
        {
            get { return this.invalid_pulses; }
        }

        public int[] Hystogram
        {
            get { return hystogram; }
        }

        public int ElapsedTime
        {
            get { return elapsed_time; }
        }

        public int CPULoad
        {
            get { return cpu_load; }
        }

        public RadiaCodeInDataReadyArgs(int[] hyst, int elapsed_time, int invalid_pulses)
        {
            this.hystogram = hyst;
            this.invalid_pulses = invalid_pulses;
            this.elapsed_time = elapsed_time;
        }
    }
}
