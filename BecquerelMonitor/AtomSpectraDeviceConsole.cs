// Settings console of the AtomSpectra network device type (after the COM type's "device features" tab):
// address host[:port], "Refresh" (name resolution + GET /api/device, only GET), and a command line.
// REST: POST /api/command answers only {"ok":true|false} (fw web_server.c:322-342); the analyzer's text answer is taken from
// the board log GET /api/devlog?since=N (fw usb_host_cdc.c: every CMD_TEXT packet, ring of 8 x 480 chars).
// "Refresh" is not a mDNS browser: it resolves ONE name (atomspectra.local by default) through the system resolver.
// Texts for a person come from Properties.Resources; the log lines stay English.

using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BecquerelMonitor
{
    // What a console command does to the board. Only Query is known to change nothing.
    public enum AtomSpectraCommandKind
    {
        Query,              // -inf, -cal without values, -sho
        Reset,              // -rst: erases the spectrum on the board
        Stop,               // -sto
        Start,              // -sta [seconds]
        CalibrationWrite,   // -cal with values
        Unknown             // anything else: the program does not know what it changes
    }

    public static class AtomSpectraDeviceConsole
    {
        public const string DefaultBoardName = "atomspectra.local";

        // the output windows keep the newest text up to these limits (older text is dropped)
        public const int OutputMaxChars = 200 * 1024;
        public const int OutputMaxLines = 2000;

        public static Func<string, IPAddress[]> Resolve = Dns.GetHostAddresses;

        public static AtomSpectraCommandKind Classify(string command)
        {
            string[] parts = (command ?? "").Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return AtomSpectraCommandKind.Unknown;
            string head = parts[0].ToLowerInvariant();
            bool bare = parts.Length == 1;
            switch (head)
            {
                case "-inf":
                case "-sho":
                    return bare ? AtomSpectraCommandKind.Query : AtomSpectraCommandKind.Unknown;
                case "-cal":
                    return bare ? AtomSpectraCommandKind.Query : AtomSpectraCommandKind.CalibrationWrite;
                case "-rst":
                    return AtomSpectraCommandKind.Reset;
                case "-sto":
                    return AtomSpectraCommandKind.Stop;
                case "-sta":
                    return AtomSpectraCommandKind.Start;
                default:
                    return AtomSpectraCommandKind.Unknown;
            }
        }

        // What the command does, for the confirmation; null for a query.
        public static string EffectText(AtomSpectraCommandKind kind)
        {
            switch (kind)
            {
                case AtomSpectraCommandKind.Reset: return Resources.AtomSpectraConsoleEffectReset;
                case AtomSpectraCommandKind.Stop: return Resources.AtomSpectraConsoleEffectStop;
                case AtomSpectraCommandKind.Start: return Resources.AtomSpectraConsoleEffectStart;
                case AtomSpectraCommandKind.CalibrationWrite: return Resources.AtomSpectraConsoleEffectCalibration;
                case AtomSpectraCommandKind.Unknown: return Resources.AtomSpectraConsoleEffectUnknown;
                default: return null;
            }
        }

        // Why a REST request failed, for a person (the raw error text stays in the log of the client).
        public static string Problem(AtomSpectraRestResponse r)
        {
            if (r == null || r.Status <= 0) return Resources.AtomSpectraRestNoAnswer;
            if (r.BodyBroken) return Resources.AtomSpectraRestBodyBroken;
            return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraRestHttpStatus, r.Status);
        }

        // The newest text is at the start (the console writes on top): keep the head within the limits.
        public static string ClipHead(string text, int maxChars, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            int end = Math.Min(text.Length, maxChars);
            int lines = 0;
            for (int i = 0; i < end; i++)
            {
                if (text[i] == '\n' && ++lines >= maxLines) { end = i + 1; break; }
            }
            if (end >= text.Length) return text;
            int cut = text.LastIndexOf('\n', end - 1);   // whole lines only
            return cut >= 0 ? text.Substring(0, cut + 1) : text.Substring(0, end);
        }

        // The newest text is at the end (an appending window): keep the tail within the limits.
        public static string ClipTail(string text, int maxChars, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            int start = Math.Max(0, text.Length - maxChars);
            if (start > 0)
            {
                int nl = text.IndexOf('\n', start - 1);   // whole lines only
                if (nl >= 0) start = nl + 1;
            }
            int lines = 0;
            for (int i = text.Length - 2; i >= start; i--)   // a line break at the very end closes the newest line
            {
                if (text[i] == '\n' && ++lines >= maxLines) { start = i + 1; break; }
            }
            return start == 0 ? text : text.Substring(start);
        }

        public static bool TryParseAddress(string text, int defaultPort, out string host, out int port)
        {
            host = null;
            port = defaultPort;

            if (string.IsNullOrEmpty(text))
                return false;

            text = text.Trim();
            if (text.Length == 0)
                return false;

            if (text.Contains("://") || text.Contains("/") || text.Contains("@"))
                return false;

            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                int close = text.IndexOf(']');
                if (close < 0)
                    return false;

                string v6 = text.Substring(1, close - 1);
                if (v6.Length == 0)
                    return false;

                string rest = text.Substring(close + 1);
                if (rest.Length > 0)
                {
                    if (rest[0] != ':')
                        return false;
                    string portPart = rest.Substring(1);
                    if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out int p) || p < 1 || p > 65535)
                        return false;
                    port = p;
                }

                if (Uri.CheckHostName(v6) == UriHostNameType.Unknown)
                    return false;

                host = v6;
                return true;
            }

            int colonCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ':')
                    colonCount++;
            }

            if (colonCount == 1)
            {
                int idx = text.IndexOf(':');
                string h = text.Substring(0, idx);
                string portPart = text.Substring(idx + 1);

                if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out int p) || p < 1 || p > 65535)
                    return false;

                if (Uri.CheckHostName(h) == UriHostNameType.Unknown)
                    return false;

                host = h;
                port = p;
                return true;
            }

            if (colonCount > 1)
            {
                if (Uri.CheckHostName(text) == UriHostNameType.Unknown)
                    return false;
                host = text;
                return true;
            }

            if (Uri.CheckHostName(text) == UriHostNameType.Unknown)
                return false;

            host = text;
            return true;
        }

        public static string FormatAddress(string host, int port, int defaultPort)
        {
            if (string.IsNullOrEmpty(host))
                return "";

            string h = host.Contains(":") ? "[" + host + "]" : host;
            if (port == defaultPort)
                return h;

            return h + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        public static int ClampPoll(int seconds)
        {
            return Math.Max(1, Math.Min(60, seconds));
        }

        public sealed class RefreshResult
        {
            public bool Found;
            public string Host;
            public string Serial;
            public string Message;
            public string DeviceJson;   // the GET /api/device body (calibration for the Energy Calibration page)
            public string BootConfigJson;   // the GET /api/boot-config body (gzip_level), null when not read
        }

        public static RefreshResult Refresh(string name, int httpPort, bool useProxy, int timeoutMs)
        {
            if (string.IsNullOrEmpty(name))
                name = DefaultBoardName;

            IPAddress[] list;
            try
            {
                list = Resolve(name);
            }
            catch (SocketException ex)
            {
                AtomSpectraTcpLog.Info("Refresh", "name " + name + " not resolved: " + ex.Message);
                return NotResolved(name);
            }
            catch (ArgumentException ex)
            {
                AtomSpectraTcpLog.Info("Refresh", "name " + name + " not resolved: " + ex.Message);
                return NotResolved(name);
            }

            if (list == null || list.Length == 0)
            {
                AtomSpectraTcpLog.Info("Refresh", "name " + name + " not resolved");
                return NotResolved(name);
            }

            var ordered = list
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ThenBy(a => a.ToString())
                .ToArray();

            var addressStrings = ordered.Select(a => a.ToString()).ToList();

            foreach (var a in ordered)
            {
                string h = a.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + a + "]" : a.ToString();
                string url = "http://" + h + (httpPort == 80 ? "" : ":" + httpPort.ToString(CultureInfo.InvariantCulture)) + "/";
                var client = new AtomSpectraRestClient(url);
                client.UseSystemProxy = useProxy;

                AtomSpectraRestResponse r = client.Get("/api/device", timeoutMs);
                if (r.Ok)
                {
                    try
                    {
                        object parsed = AtomSpectraJson.Parse(r.Body);
                        Dictionary<string, object> d = parsed as Dictionary<string, object>;
                        if (d != null)
                        {
                            string serial = AtomSpectraJson.GetString(d, "serial");
                            var res = new RefreshResult
                            {
                                Found = true,
                                Host = a.ToString(),
                                Serial = serial ?? "?",
                                Message = "found " + name + " -> " + a,
                                DeviceJson = r.Body
                            };
                            AtomSpectraRestResponse boot = client.Get("/api/boot-config", timeoutMs);   // read only (gzip_level)
                            res.BootConfigJson = boot.Ok ? boot.Body : null;
                            AtomSpectraTcpLog.Info("Refresh", res.Message);
                            return res;
                        }
                    }
                    catch (FormatException)
                    {
                        // move on to the next address
                    }
                }
            }

            string addresses = string.Join(", ", addressStrings);
            AtomSpectraTcpLog.Info("Refresh", name + " resolves to " + addresses + ", but no board answered GET /api/device");
            return new RefreshResult
            {
                Found = false,
                Message = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleNoBoardAnswered, name, addresses)
            };
        }

        // Message is for a person (the status line); the log has the resolver's own words
        private static RefreshResult NotResolved(string name)
        {
            return new RefreshResult { Found = false, Message = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleNotResolved, name) };
        }

        public static string SendRest(AtomSpectraRestClient client, string command, int waitMs)
        {
            command = (command ?? "").Trim();
            if (command.Length == 0)
                return "";

            long baseline = 0;
            long next;
            try
            {
                AtomSpectraRestResponse baseResp = client.Get("/api/devlog?since=0", 3000);
                if (baseResp.Ok)
                {
                    AtomSpectraDevlog.Format(baseResp.Body, 0, out next);
                    baseline = next;
                }
            }
            catch (FormatException)
            {
                // keep baseline 0
            }

            AtomSpectraRestResponse r = client.Command(command, 3000);
            if (!r.Accepted)
            {
                // the board's own answer ({"ok":false}) is shown as it is
                string why = r.Ok ? string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraRestHttpStatus, r.Status) + " " + r.Body : Problem(r);
                string msg = ">> " + command + Environment.NewLine +
                    string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleNotAccepted, why) + Environment.NewLine;
                AtomSpectraTcpLog.Info("Console", "REST command " + command + ": not accepted (" + (r.Error ?? ("HTTP " + r.Status.ToString(CultureInfo.InvariantCulture))) + ")");
                return msg;
            }

            var sb = new StringBuilder();
            long since = baseline;
            DateTime start = DateTime.UtcNow;
            DateTime lastText = DateTime.MinValue;

            while (true)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - start).TotalMilliseconds >= waitMs)
                    break;

                Thread.Sleep(300);

                now = DateTime.UtcNow;
                if ((now - start).TotalMilliseconds >= waitMs)
                    break;

                string path = "/api/devlog?since=" + since.ToString(CultureInfo.InvariantCulture);
                AtomSpectraRestResponse resp = client.Get(path, 2000);
                if (resp.Ok)
                {
                    try
                    {
                        string t = AtomSpectraDevlog.Format(resp.Body, since, out next);
                        if (t.Length > 0)
                        {
                            sb.Append(t);
                            lastText = DateTime.UtcNow;
                        }
                        since = Math.Max(since, next);
                    }
                    catch (FormatException)
                    {
                        // ignore
                    }
                }

                if (sb.Length > 0 && (DateTime.UtcNow - lastText).TotalMilliseconds >= 700)
                    break;
            }

            string result;
            if (sb.Length > 0)
            {
                result = ">> " + command + Environment.NewLine + sb.ToString();
                AtomSpectraTcpLog.Info("Console", "REST command " + command + ": answer " + sb.Length.ToString(CultureInfo.InvariantCulture) + " chars");
            }
            else
            {
                result = ">> " + command + Environment.NewLine +
                    string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleNoText, (waitMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture), r.Body) + Environment.NewLine;
                AtomSpectraTcpLog.Info("Console", "REST command " + command + ": no answer in the board log");
            }

            return result;
        }

        public static string SendTcp(string host, int port, string command, int waitMs)
        {
            command = (command ?? "").Trim();
            if (command.Length == 0)
                return "";

            AtomSpectraTcpSession live = AtomSpectraTcpSession.FindLive(host, port);
            if (live != null)
            {
                string a = live.Query(command, waitMs);
                string result = ">> " + command + Environment.NewLine +
                    (string.IsNullOrEmpty(a) ? Resources.AtomSpectraConsoleNoAnswer : a.TrimEnd('\r', '\n')) + Environment.NewLine;
                AtomSpectraTcpLog.Info("Console", "TCP command via the open session");
                return result;
            }

            string where = host + ":" + port.ToString(CultureInfo.InvariantCulture);
            var acq = new AtomSpectraTcpAcquisition(
                () => new AtomSpectraTcpTransport(host, port, 1500),
                60000,
                0,
                "console " + where);

            try
            {
                acq.Start();
                if (!acq.WaitConnected(2500))
                {
                    AtomSpectraTcpLog.Info("Console", "TCP command: no connection to " + where);
                    return ">> " + command + Environment.NewLine + string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleNoConnection, host, port) + Environment.NewLine;
                }

                acq.SendCommand(command);

                var sb = new StringBuilder();
                string first = acq.GetCommandOutput(waitMs);
                if (!string.IsNullOrEmpty(first))
                    sb.Append(first.TrimEnd('\r', '\n')).Append(Environment.NewLine);

                while (true)
                {
                    string more = acq.GetCommandOutput(500);
                    if (string.IsNullOrEmpty(more))
                        break;
                    sb.Append(more.TrimEnd('\r', '\n')).Append(Environment.NewLine);
                }

                string result;
                if (sb.Length > 0)
                {
                    result = ">> " + command + Environment.NewLine + sb.ToString();
                    AtomSpectraTcpLog.Info("Console", "TCP command: answer " + sb.Length.ToString(CultureInfo.InvariantCulture) + " chars");
                }
                else
                {
                    result = ">> " + command + Environment.NewLine +
                        string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleNoAnswerWithin, waitMs) + Environment.NewLine;
                    AtomSpectraTcpLog.Info("Console", "TCP command: no answer within " + waitMs.ToString(CultureInfo.InvariantCulture) + " ms");
                }

                return result;
            }
            finally
            {
                acq.Dispose();
            }
        }
    }
}
