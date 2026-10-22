using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace BecquerelMonitor
{
    // Mirrors the transport-behind-an-interface pattern of AmplitudaSerialPort.cs so tests can put another stream behind the acquisition.
    public interface IAtomSpectraTcpTransport : IDisposable
    {
        void Open();
        void Close();
        bool IsOpen { get; }
        void Write(byte[] buffer, int offset, int count);
        int Read(byte[] buffer, int offset, int count, int timeoutMs);
        long NowMs { get; }
    }

    public sealed class AtomSpectraTcpTransport : IAtomSpectraTcpTransport
    {
        public const int DefaultConnectTimeoutMs = 3000;
        private const int WriteTimeoutMs = 2000;

        private readonly string host;
        private readonly int port;
        private readonly int connectTimeoutMs;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TcpClient client;

        public AtomSpectraTcpTransport(string host, int port, int connectTimeoutMs)
        {
            this.host = host == null ? "" : host.Trim();
            this.port = port;
            this.connectTimeoutMs = Math.Max(100, connectTimeoutMs);
        }

        public string Host
        {
            get { return host; }
        }

        public int Port
        {
            get { return port; }
        }

        public bool IsOpen
        {
            get
            {
                return client != null && client.Client != null && client.Connected;
            }
        }

        public long NowMs
        {
            get { return clock.ElapsedMilliseconds; }
        }

        public void Open()
        {
            Close();

            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentException("no host is set");
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port", port, "port must be between 1 and 65535");
            }

            TcpClient created = new TcpClient();
            try
            {
                // BeginConnect + bounded wait because TcpClient.Connect has no timeout of its own.
                IAsyncResult pending = created.BeginConnect(host, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(connectTimeoutMs))
                {
                    throw new TimeoutException(string.Format("connection to {0}:{1} timed out after {2} ms", host, port, connectTimeoutMs));
                }

                created.EndConnect(pending);
                created.NoDelay = true;
                created.SendTimeout = WriteTimeoutMs;
                created.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch (Exception)
            {
                created.Close();
                throw;
            }

            client = created;
        }

        public void Close()
        {
            if (client != null)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // ignore
                }
                client = null;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            Socket socket = OpenSocket();
            int remaining = count;
            int currentOffset = offset;

            while (remaining > 0)
            {
                int sent;
                try
                {
                    sent = socket.Send(buffer, currentOffset, remaining, SocketFlags.None);
                }
                catch (SocketException ex)
                {
                    throw new IOException("TCP write failed: " + ex.Message, ex);
                }
                catch (ObjectDisposedException ex)
                {
                    throw new IOException("TCP write failed", ex);
                }

                if (sent <= 0)
                {
                    throw new IOException("TCP write returned no bytes");
                }

                currentOffset += sent;
                remaining -= sent;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            Socket socket = OpenSocket();
            int waitUs = (int)Math.Min(int.MaxValue, Math.Max(1L, timeoutMs) * 1000L);

            // Poll+Receive instead of a socket receive timeout, because after a timed-out blocking receive the Windows socket state is indeterminate.
            int received;
            try
            {
                if (!socket.Poll(waitUs, SelectMode.SelectRead))
                {
                    return 0;
                }
                received = socket.Receive(buffer, offset, count, SocketFlags.None);
            }
            catch (SocketException ex)
            {
                throw new IOException("TCP read failed: " + ex.Message, ex);
            }
            catch (ObjectDisposedException ex)
            {
                throw new IOException("TCP read failed: " + ex.Message, ex);
            }

            if (received == 0)
            {
                throw new IOException("the board closed the connection");
            }

            return received;
        }

        public void Dispose()
        {
            Close();
        }

        private Socket OpenSocket()
        {
            TcpClient local = client;
            if (local == null || local.Client == null)
            {
                throw new IOException("the TCP connection is closed");
            }
            return local.Client;
        }
    }
}
