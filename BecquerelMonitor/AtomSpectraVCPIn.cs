using RJCP.IO.Ports;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace BecquerelMonitor
{
    public class AtomSpectraVCPIn : IDisposable
    {
        //public static int cps_static;
        //public static int elapsed_time_static;
        private SerialPortStream port = null;
        private Thread readerThread;
        private volatile bool thread_alive = true;
        private ManualResetEvent onDataReceivedEvent = new ManualResetEvent(false);
        private byte[] input_buffer = new byte[1024];
        private int[] hystogram = new int[8192];
        private int[] hystogram_buffered = new int[8192];
        private int cpu_load;
        private enum State { Connecting, Connected };
        private int cps;
        private String name;
        private int invalid_pulses;
        private string guid;
        private volatile bool port_name_changed;
        //protocol
        private const byte SHPROTO_START = (0xFE | 0x80);
        private const byte SHPROTO_ESC = (0xFD | 0x80);
        private const byte SHPROTO_FINISH = (0xA5 | 0x80);
        private const int MAX_DATA_COUNT = 65535;
        private bool rx_esc = false;
        private ushort rx_crc = 0;
        private int packet_cmd;
        private int packet_length;
        private List<byte> packet = new List<byte>();
        private ConcurrentQueue<byte> outcomming = new ConcurrentQueue<byte>();
        private ConcurrentQueue<string> received_lines = new ConcurrentQueue<string>();

        private Timer timer;
        private Object Lock = new Object();

        private static List<AtomSpectraVCPIn> instances = new List<AtomSpectraVCPIn>();

        public static void cleanUp(string guid)
        {
            foreach (AtomSpectraVCPIn s in instances)
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

        public static AtomSpectraVCPIn getInstance(string guid)
        {
            foreach (AtomSpectraVCPIn s in instances)
            {
                if (s == null) continue;
                if (guid.Equals(s.GUID))
                {
                    return s;
                }
            }
            AtomSpectraVCPIn instance = new AtomSpectraVCPIn(guid);
            instances.Add(instance);
            return instance;
        }

        public static void finishAll()
        {
            foreach (AtomSpectraVCPIn s in instances)
            {
                if (s != null) s.Dispose();
            }
            instances.Clear();
        }

        public AtomSpectraVCPIn(string guid)
        {
            this.guid = guid;
            Trace.WriteLine("AtomSpectraVCPIn instance created " + guid);
            port = new SerialPortStream();
            port.BaudRate = 600000;
            port.DataBits = 8;
            port.Parity = Parity.None;
            port.StopBits = StopBits.One;
            port.DtrEnable = false;
            port.RtsEnable = false;
            if (name != null) port.PortName = name;
            port.DataReceived += Port_DataReceived;
            readerThread = new Thread(this.run);
            readerThread.Name = "AtomSpectraVCPIn";
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
            timer.Change(1000, Timeout.Infinite);
        }

        public string GUID
        {
            get { return this.guid; }
        }

        public void setPort(string com)
        {
            this.name = com;
            this.port_name_changed = true;
            try
            {
                //if (port.IsOpen)
               // {
                //    port.DiscardInBuffer();
                //    port.DiscardOutBuffer();
                //    port.Close();
               // }
            }
            catch (Exception)
            {

            }
            this.onDataReceivedEvent.Set();
        }

        public void updateConfig(InputDeviceConfig conf)
        {
            if (conf is AtomSpectraDeviceConfig)
            {
                AtomSpectraDeviceConfig config = (AtomSpectraDeviceConfig)conf;
                Trace.WriteLine("Config changed: COM = " + config.ComPortName);
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
            while (received_lines.TryDequeue(out ignore));
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

        public bool search_packet(byte rx_byte)
        {
            switch (rx_byte)
            {
                case SHPROTO_START: // clear all buffers
                    packet.Clear();
                    rx_esc = false;
                    rx_crc = 0xFFFF;
                    packet_length = 0;
                    break;
                case SHPROTO_ESC:
                    rx_esc = true; // set esc status, skip esc byte
                    break;
                case SHPROTO_FINISH: // got packet, mark it to be parsed
                    packet_length -= 3;
                    if (rx_crc != 0)
                    {
                        // crc16 with data and crc itself gives zero result
                        return false;
                    }
                    return true;
                default: // just add byte to packet
                    if (rx_esc)
                    {
                        rx_esc = false;
                        rx_byte = (byte)(~rx_byte);
                    }
                    if (packet.Count < MAX_DATA_COUNT)
                    {
                        if (packet_length == 0)
                        {
                            packet_cmd = rx_byte;
                        }
                        else
                        {
                            packet.Add(rx_byte);
                        }
                        packet_length++;
                    }
                    rx_crc = crc16(rx_crc, rx_byte);
                    break;
            }
            return false;
        }

        public void run()
        {
            byte[] tx_buffer = new byte[1024];
            State state = State.Connecting;
            while (thread_alive)
            {
                if (port_name_changed)
                {
                    port_name_changed = false;
                    state = State.Connecting;
                }

                if (state == State.Connecting)
                {
                    try
                    {
                        if(port.IsOpen) port.Close();
                        if (name != null)
                        {
                            port.PortName = this.name;
                            port.Open();
                            state = State.Connected;
                        }
                        else
                        {
                            throw new Exception("Empty port name");
                        }
                    }
                    catch (Exception)
                    {
                        try
                        {
                            port.Close();
                        } catch (Exception)
                        {

                        }
                        Thread.Sleep(100);
                        continue;
                    }
                }
                else if(state == State.Connected)
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
                            if (!search_packet(input_buffer[i])) continue;
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
                                cpu_load = packet[4] | (packet[5] << 8);
                                cps = packet[6] | (packet[7] << 8) | (packet[8] << 16) | (packet[9] << 24);
                                if (packet.Count > 10)
                                {
                                    invalid_pulses = (packet[10] | (packet[11] << 8) | (packet[12] << 16) | (packet[13] << 24));
                                }
                                //elapsed_time_static = elapsed_time;
                                //cps_static = cps;
                                hystogram.CopyTo(hystogram_buffered, 0);
                                if (DataReady != null) DataReady(this, new AtomSpectraVCPInDataReadyArgs(hystogram_buffered, cpu_load, elapsed_time, invalid_pulses));
                            } else if(packet_cmd == 0x03) //printf
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
            Trace.WriteLine("AtomSpectraVCPIn thread stopped " + guid);
        }

        private void send_packet(byte[] data)
        {
            lock (Lock)
            {
                ushort packet_crc = 0xFFFF;
                outcomming.Enqueue(0xFF);
                outcomming.Enqueue(SHPROTO_START);
                foreach (byte d in data)
                {
                    packet_crc = crc16(packet_crc, d);
                    add2buff_escape(d);
                }
                add2buff_escape((byte)(packet_crc & 0xff));
                add2buff_escape((byte)(packet_crc >> 8));
                outcomming.Enqueue(SHPROTO_FINISH);
                onDataReceivedEvent.Set();
            }
        }

        public double CPS
        {
            get { return (double) this.cps;  }
        }

        private void add2buff_escape(byte tx_byte)
        {
            if (tx_byte == SHPROTO_ESC || tx_byte == SHPROTO_START || tx_byte == SHPROTO_FINISH)
            {
                outcomming.Enqueue(SHPROTO_ESC);
                outcomming.Enqueue((byte)((~tx_byte) & 0xFF));
            }
            else
            {
                outcomming.Enqueue(tx_byte);
            }
        }

        public int InvalidPulses
        {
            get { return invalid_pulses;  }
        }

        public void Dispose()
        {
            if(timer != null) timer.Dispose();
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
                Trace.WriteLine("AtomSpectraVCPIn thread termination request");
                thread_alive = false;
                onDataReceivedEvent.Set();
                readerThread.Join();
            }
        }

        public event EventHandler<AtomSpectraVCPInDataReadyArgs> DataReady;
        public event EventHandler<EventArgs> PortFailure;
    }

    public class AtomSpectraVCPInDataReadyArgs : EventArgs
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

        public AtomSpectraVCPInDataReadyArgs(int[] hyst, int cpu_load, int elapsed_time, int invalid_pulses)
        {
            this.hystogram = hyst;
            this.invalid_pulses = invalid_pulses;
            this.elapsed_time = elapsed_time;
            this.cpu_load = cpu_load;
        }
    }
}
