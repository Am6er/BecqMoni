using System;
using System.Globalization;

namespace BecquerelMonitor
{
    public enum AtomSpectraTransport { TcpBridge, Rest }

    // Settings of an Atom Spectra analyzer reached over TCP through a Wi-Fi bridge board
    // (atomspectra-waterfall-esp32, port 8234) instead of a COM port.
    // The class name is the XML element name in device config files: never rename it.
    public partial class AtomSpectraTcpDeviceConfig : InputDeviceConfig
    {
        public const string DeviceTypeId = "AtomSpectraTCP";
        public const int DefaultTcpPort = 8234;

        private string host = "";
        private int tcpPort = DefaultTcpPort;
        private double deadTime = 0;
        public const int DefaultRestPort = 80, DefaultRestPollSeconds = 10, MinRestPollSeconds = 1, MaxRestPollSeconds = 60;
        private AtomSpectraTransport transport = AtomSpectraTransport.TcpBridge;
        private int restPort = DefaultRestPort;
        private int restPollSeconds = DefaultRestPollSeconds;
        private int restLimitSeconds;

        // REST: the whole board URL (scheme, host, port, path prefix); empty = http://Host[:RestPort]/.
        public string RestUrl { get; set; } = "";
        // Never send a POST (several observers of one board).
        public bool RestObserveOnly { get; set; }
        // Board-side limit for OUR new acquisition (-sta N), s; 0 = none. Never sent when joining.
        public int RestLimitSeconds { get { return restLimitSeconds; } set { restLimitSeconds = Math.Max(0, value); } }
        // The system proxy is used unless this is set.
        public bool RestNoProxy { get; set; }
        // Authorization for a proxy in front of the board; the secret is stored DPAPI-protected (CurrentUser).
        public AtomSpectraRestAuth RestAuthKind { get; set; }
        public string RestAuthUser { get; set; } = "";
        public string RestAuthSecretProtected { get; set; } = "";

        public string Host
        {
            get { return host; }
            set
            {
                host = value == null ? "" : value.Trim();
            }
        }

        // A DNS name or an IPv4/IPv6 address without port or scheme ("192.168.0.50:8234" and "http://..." are refused).
        public static bool IsValidHost(string host)
        {
            return !string.IsNullOrWhiteSpace(host) && Uri.CheckHostName(host.Trim()) != UriHostNameType.Unknown;
        }

        public int TcpPort
        {
            get { return tcpPort; }
            set
            {
                tcpPort = Math.Max(1, Math.Min(65535, value));
            }
        }

        // Transport: TCP bridge (default, so old configs load as before) or the board's REST API.
        public AtomSpectraTransport Transport { get { return transport; } set { transport = value; } }
        public int RestPort { get { return restPort; } set { restPort = Math.Max(1, Math.Min(65535, value)); } }

        // Polling period, s: 1..60, default 10 (Albert's decision). An out-of-range value from an old file is clamped and logged.
        public int RestPollSeconds
        {
            get { return restPollSeconds; }
            set
            {
                int clamped = Math.Max(MinRestPollSeconds, Math.Min(MaxRestPollSeconds, value));
                if (clamped != value) AtomSpectraTcpLog.Warn("Session", "RestPollSeconds " + value.ToString(CultureInfo.InvariantCulture) + " is out of 1..60, set to " + clamped.ToString(CultureInfo.InvariantCulture));
                restPollSeconds = clamped;
            }
        }

        // Seconds; read from the device's -inf answer on connect (AtomSpectraDeviceConfig.DeadTimeFromInfo), as the COM type does.
        public double DeadTimeValue
        {
            get { return deadTime; }
            set { deadTime = value; }
        }

        public AtomSpectraTcpDeviceConfig()
        {
        }

        public AtomSpectraTcpDeviceConfig(AtomSpectraTcpDeviceConfig other)
        {
            host = other.host;
            tcpPort = other.tcpPort;
            deadTime = other.deadTime;
            transport = other.transport;
            restPort = other.restPort;
            restPollSeconds = other.restPollSeconds;
            restLimitSeconds = other.restLimitSeconds;
            RestUrl = other.RestUrl;
            RestObserveOnly = other.RestObserveOnly;
            RestNoProxy = other.RestNoProxy;
            RestAuthKind = other.RestAuthKind;
            RestAuthUser = other.RestAuthUser;
            RestAuthSecretProtected = other.RestAuthSecretProtected;
        }

        public override InputDeviceConfig Clone()
        {
            return new AtomSpectraTcpDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return deadTime;
        }
    }
}
