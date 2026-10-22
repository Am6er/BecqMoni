using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunConsole(string scenario, string[] args)
        {
            switch (scenario)
            {
                case "console-rest":
                {
                    string text = AtomSpectraDeviceConsole.SendRest(Client(), "-inf", 3000);
                    Console.WriteLine(text);
                    Check(text.StartsWith(">> -inf"), "the output starts with the command");
                    Check(text.Contains("mock answer to -inf"), "the analyzer's text answer from the board log is shown");
                    Check(!text.Contains("hello"), "board log lines from before the command are not shown");
                    bool posted = Requests().Any(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/command" && Str(r, "body").Trim() == "-inf");
                    Check(posted, "POST /api/command with the raw text -inf reached the mock");
                    return true;
                }
                case "console-rest-notext":
                {
                    string text = AtomSpectraDeviceConsole.SendRest(Client(), "-inf", 2000);
                    Console.WriteLine(text);
                    Check(text.Contains("(no text from the analyzer"), "no answer in the board log is said so");
                    Check(!text.Contains("mock answer"), "nothing is made up");
                    return true;
                }
                case "console-rest-refused":
                {
                    string text = AtomSpectraDeviceConsole.SendRest(Client(), "-inf", 2000);
                    Console.WriteLine(text);
                    Check(text.Contains("did not accept"), "a refused command is reported");
                    return true;
                }
                case "console-refresh":
                {
                    var saved = AtomSpectraDeviceConsole.Resolve;
                    try
                    {
                        AtomSpectraDeviceConsole.Resolve = n =>
                        {
                            if (n == "atomspectra.local") return new[] { IPAddress.Loopback };
                            throw new SocketException(11001);
                        };
                        var r = AtomSpectraDeviceConsole.Refresh(null, port, false, 2000);
                        Console.WriteLine(r.Message);
                        Check(r.Found, "the board is found by the name atomspectra.local");
                        Check(r.Host == "127.0.0.1", "address 127.0.0.1");
                        Check(r.Serial == "00000001", "serial from GET /api/device");
                        Check(Posts() == 0, "refresh sends no POST");
                        Check(Requests().Any(q => Str(q, "path") == "/api/device"), "GET /api/device was asked");
                    }
                    finally
                    {
                        AtomSpectraDeviceConsole.Resolve = saved;
                    }
                    return true;
                }
                case "console-refresh-none":
                {
                    var saved = AtomSpectraDeviceConsole.Resolve;
                    try
                    {
                        AtomSpectraDeviceConsole.Resolve = n =>
                        {
                            throw new SocketException(11001);
                        };
                        var r1 = AtomSpectraDeviceConsole.Refresh(null, 1, false, 1000);
                        Console.WriteLine(r1.Message);
                        Check(!r1.Found && r1.Message.Contains("not resolved"), "an unknown name is reported");

                        AtomSpectraDeviceConsole.Resolve = n => new[] { IPAddress.Loopback };
                        var r2 = AtomSpectraDeviceConsole.Refresh("atomspectra.local", 1, false, 1000);
                        Console.WriteLine(r2.Message);
                        Check(!r2.Found && r2.Message.Contains("no board answered"), "a name without a board is reported");
                    }
                    finally
                    {
                        AtomSpectraDeviceConsole.Resolve = saved;
                    }
                    return true;
                }
                case "console-address":
                {
                    string h;
                    int p;

                    Check(AtomSpectraDeviceConsole.TryParseAddress("192.0.2.19", 8234, out h, out p) && h == "192.0.2.19" && p == 8234, "host only: default port");
                    Check(AtomSpectraDeviceConsole.TryParseAddress("atomspectra.local:80", 8234, out h, out p) && h == "atomspectra.local" && p == 80, "name:port");
                    Check(AtomSpectraDeviceConsole.TryParseAddress("[fe80::1]:8080", 80, out h, out p) && h == "fe80::1" && p == 8080, "[v6]:port");
                    Check(!AtomSpectraDeviceConsole.TryParseAddress("192.0.2.19:0", 80, out h, out p), "port 0 refused");
                    Check(!AtomSpectraDeviceConsole.TryParseAddress("192.0.2.19:65536", 80, out h, out p), "port 65536 refused");
                    Check(!AtomSpectraDeviceConsole.TryParseAddress("http://192.0.2.19/", 80, out h, out p), "a URL is not an address");
                    Check(!AtomSpectraDeviceConsole.TryParseAddress("user@host", 80, out h, out p), "user@ refused");
                    Check(!AtomSpectraDeviceConsole.TryParseAddress("", 80, out h, out p), "empty refused");

                    Check(AtomSpectraDeviceConsole.FormatAddress("192.0.2.19", 8234, 8234) == "192.0.2.19", "default port is not written");
                    Check(AtomSpectraDeviceConsole.FormatAddress("::1", 80, 8234) == "[::1]:80", "IPv6 is bracketed");

                    Check(AtomSpectraDeviceConsole.ClampPoll(0) == 1 && AtomSpectraDeviceConsole.ClampPoll(61) == 60 && AtomSpectraDeviceConsole.ClampPoll(10) == 10, "poll interval 1..60");

                    var cfg = new AtomSpectraTcpDeviceConfig();
                    cfg.RestPollSeconds = 0;
                    int a = cfg.RestPollSeconds;
                    cfg.RestPollSeconds = 61;
                    int b = cfg.RestPollSeconds;
                    cfg.RestPollSeconds = 10;
                    Check(a == 1 && b == 60 && cfg.RestPollSeconds == 10, "configuration clamps the poll interval to 1..60");

                    return true;
                }
                case "rest-unreachable-text":   // REST start on an unreachable board, not the bridge's "one client" text
                {
                    var s = new AtomSpectraRestSession(new AtomSpectraRestClient("http://127.0.0.1:1/"), 5, false, 0);
                    var o = s.Begin(true, 1000);
                    string text = s.UnreachableText();
                    Console.WriteLine(o + ": " + text);
                    Check(text.Contains("127.0.0.1:1") && (text.Contains("5 s") || text.Contains("5 с")), "address and poll period are named");
                    Check(!text.Contains("one client") && !text.Contains("одного клиента") && !text.Contains("minute") && !text.Contains("минут"), "no bridge 'one client / slot busy a minute'");
                    s.Dispose();
                    return true;
                }
                case "console-tcp":   // bridge mock (mock_cmd.py) answers every text command with "-ok"
                {
                    string text = AtomSpectraDeviceConsole.SendTcp("127.0.0.1", port, "-inf", 2000);
                    Console.WriteLine(text);
                    Check(text.StartsWith(">> -inf"), "the output starts with the command");
                    Check(text.Contains("-ok"), "the bridge's text answer is shown");
                    Check(Requests().Any(r => Str(r, "cmd") == "-inf"), "the bridge received -inf");
                    return true;
                }
                case "rest-linklost-text":   // UI test REST showed the TCP bridge text ("retries every 60 s")
                {
                    var s = new AtomSpectraRestSession(new AtomSpectraRestClient("http://127.0.0.1:18080/"), 3, true, 0);
                    string text = s.LinkLostText();
                    Console.WriteLine(text);
                    Check(text.Contains("127.0.0.1:18080"), "the board address is named");
                    Check(text.Contains("every 3 s") || text.Contains("каждые 3 с"), "the poll period (3 s) is named");
                    Check(!text.Contains("60"), "no TCP reconnect interval of 60 s");
                    s.Dispose();
                    return true;
                }
                default:
                    return RunForm(scenario, args);   // settings form (RestHarnessForm.cs)
            }
        }
    }
}
