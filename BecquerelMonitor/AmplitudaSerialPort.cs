using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace BecquerelMonitor
{
    public sealed class AmplitudaSerialSettings
    {
        public string PortName = "";
        public int BaudRate = 19200;
        public int GapMs = 20;          // silence before the address byte and between address and command

        public bool SameParameters(AmplitudaSerialSettings other)
        {
            return other != null && BaudRate == other.BaudRate && GapMs == other.GapMs;
        }
    }

    // The serial port as the line sees it. The clock and Sleep are part of the interface so
    // that tests run a fake block on virtual time.
    public interface IAmplitudaSerialPort : IDisposable
    {
        void Open();                    // throws on failure; may be called again after Close
        void Close();
        void Write(byte value);
        int Read(byte[] buffer, int offset, int count, int timeoutMs);      // 0 = timeout
        void DiscardInput();
        void Sleep(int milliseconds);
        long NowMs { get; }
    }

    public sealed class AmplitudaSerialComPort : IAmplitudaSerialPort
    {
        const int WriteTimeoutMs = 2000;

        readonly AmplitudaSerialSettings settings;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly byte[] one = new byte[1];
        SerialPort port;

        public AmplitudaSerialComPort(AmplitudaSerialSettings settings)
        {
            this.settings = settings;
        }

        public void Open()
        {
            Close();
            SerialPort created = new SerialPort(settings.PortName, settings.BaudRate, Parity.None, 8, StopBits.Two);
            try
            {
                created.Handshake = Handshake.None;
                // The stock driver raises both lines; a block powered from the port lives on them.
                created.DtrEnable = true;
                created.RtsEnable = true;
                created.WriteTimeout = WriteTimeoutMs;
                created.Open();
            }
            catch (Exception)
            {
                created.Dispose();
                throw;
            }
            port = created;
        }

        public void Close()
        {
            if (port != null)
            {
                try { port.Dispose(); } catch (Exception) { }
                port = null;
            }
        }

        public void Write(byte value)
        {
            one[0] = value;
            port.Write(one, 0, 1);
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            port.ReadTimeout = Math.Max(1, timeoutMs);
            try
            {
                return port.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                return 0;
            }
        }

        public void DiscardInput()
        {
            port.DiscardInBuffer();
        }

        public void Sleep(int milliseconds)
        {
            if (milliseconds > 0)
            {
                Thread.Sleep(milliseconds);
            }
        }

        public long NowMs
        {
            get { return clock.ElapsedMilliseconds; }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
