using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Globalization;
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

    // A failure of the bridge link. Message is the text for the user (resources; it reaches the {2} of ERRAtomSpectraTcpConnect
    // and the test-connection result), LogText the same in English for the log.
    public sealed class AtomSpectraTcpLinkException : IOException
    {
        public string LogText { get; private set; }

        public AtomSpectraTcpLinkException(string userText, string logText, Exception inner) : base(userText, inner)
        {
            LogText = logText;
        }

        // The English text of any exception, for the log.
        public static string LogOf(Exception ex)
        {
            AtomSpectraTcpLinkException link = ex as AtomSpectraTcpLinkException;
            return link != null ? link.LogText : ex == null ? "" : ex.Message;
        }

        // A socket error: the system's own text inside our resource.
        public static AtomSpectraTcpLinkException Network(string userFormat, string logPrefix, Exception ex)
        {
            return new AtomSpectraTcpLinkException(string.Format(CultureInfo.InvariantCulture, userFormat, ex.Message), logPrefix + ": " + ex.Message, ex);
        }
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
                throw new AtomSpectraTcpLinkException(Resources.AtomSpectraTcpErrNoHost, "no host is set", null);
            }

            if (port < 1 || port > 65535)
            {
                throw new AtomSpectraTcpLinkException(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpErrBadPort, port),
                    "port " + port.ToString(CultureInfo.InvariantCulture) + " is outside 1..65535", null);
            }

            TcpClient created = new TcpClient();
            try
            {
                // BeginConnect + bounded wait because TcpClient.Connect has no timeout of its own.
                IAsyncResult pending = created.BeginConnect(host, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(connectTimeoutMs))
                {
                    throw new AtomSpectraTcpLinkException(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpErrTimeout, connectTimeoutMs),
                        string.Format(CultureInfo.InvariantCulture, "connection to {0}:{1} timed out after {2} ms", host, port, connectTimeoutMs), null);
                }

                created.EndConnect(pending);
                created.NoDelay = true;
                created.SendTimeout = WriteTimeoutMs;
                created.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch (SocketException ex)
            {
                created.Close();
                throw AtomSpectraTcpLinkException.Network(Resources.AtomSpectraTcpErrNetwork, "TCP connect failed", ex);
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
                    throw AtomSpectraTcpLinkException.Network(Resources.AtomSpectraTcpErrWrite, "TCP write failed", ex);
                }
                catch (ObjectDisposedException ex)
                {
                    throw new AtomSpectraTcpLinkException(Resources.AtomSpectraTcpErrClosed, "TCP write failed: the socket is closed", ex);
                }

                if (sent <= 0)
                {
                    throw new AtomSpectraTcpLinkException(Resources.AtomSpectraTcpErrClosed, "TCP write returned no bytes", null);
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
                throw AtomSpectraTcpLinkException.Network(Resources.AtomSpectraTcpErrRead, "TCP read failed", ex);
            }
            catch (ObjectDisposedException ex)
            {
                throw new AtomSpectraTcpLinkException(Resources.AtomSpectraTcpErrClosed, "TCP read failed: " + ex.Message, ex);
            }

            if (received == 0)
            {
                throw new AtomSpectraTcpLinkException(Resources.AtomSpectraTcpErrBoardClosed, "the board closed the connection", null);
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
                throw new AtomSpectraTcpLinkException(Resources.AtomSpectraTcpErrClosed, "the TCP connection is closed", null);
            }
            return local.Client;
        }
    }
}
