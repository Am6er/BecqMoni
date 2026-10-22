using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace BecquerelMonitor
{
    public static class AtomSpectraJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            Parser p = new Parser(text);
            object result = p.ParseValue();
            p.SkipWhitespace();
            if (p.Pos < p.Text.Length)
            {
                throw new FormatException("Trailing characters at position " + p.Pos);
            }
            return result;
        }

        public static double GetNumber(Dictionary<string, object> o, string key, double fallback)
        {
            if (o == null) return fallback;
            object val;
            if (!o.TryGetValue(key, out val)) return fallback;
            if (val is double) return (double)val;
            return fallback;
        }

        public static string GetString(Dictionary<string, object> o, string key)
        {
            if (o == null) return null;
            object val;
            if (!o.TryGetValue(key, out val)) return null;
            if (val is string) return (string)val;
            return null;
        }

        public static bool GetBool(Dictionary<string, object> o, string key, bool fallback)
        {
            if (o == null) return fallback;
            object val;
            if (!o.TryGetValue(key, out val)) return fallback;
            if (val is bool) return (bool)val;
            return fallback;
        }

        public static List<object> GetArray(Dictionary<string, object> o, string key)
        {
            if (o == null) return null;
            object val;
            if (!o.TryGetValue(key, out val)) return null;
            if (val is List<object>) return (List<object>)val;
            return null;
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            public Parser(string text)
            {
                _text = text;
                _pos = 0;
            }

            public string Text { get { return _text; } }
            public int Pos { get { return _pos; } }

            public void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _text.Length)
                {
                    throw new FormatException("Unexpected end of input at position " + _pos);
                }

                char c = _text[_pos];

                // Bare tokens first (nan, NaN, inf, -inf, Infinity, -Infinity): "nan" must not be read as a broken "null".
                if (TryParseBareNumber())
                {
                    return ParseNumber();
                }

                if (c == '{')
                {
                    return ParseObject();
                }
                if (c == '[')
                {
                    return ParseArray();
                }
                if (c == '"')
                {
                    return ParseString();
                }
                if (c == 't')
                {
                    Expect("true");
                    return true;
                }
                if (c == 'f')
                {
                    Expect("false");
                    return false;
                }
                if (c == 'n')
                {
                    Expect("null");
                    return null;
                }

                // Standard number
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    return ParseNumber();
                }

                throw new FormatException("Unexpected character '" + c + "' at position " + _pos);
            }

            private bool TryParseBareNumber()
            {
                // Check if current position starts with a bare number token
                string rest = _text.Substring(_pos);
                if (rest.StartsWith("nan", StringComparison.Ordinal) ||
                    rest.StartsWith("NaN", StringComparison.Ordinal) ||
                    rest.StartsWith("inf", StringComparison.Ordinal) ||
                    rest.StartsWith("-inf", StringComparison.Ordinal) ||
                    rest.StartsWith("Infinity", StringComparison.Ordinal) ||
                    rest.StartsWith("-Infinity", StringComparison.Ordinal))
                {
                    // Ensure it's not part of a larger identifier (next char must be non-alphanumeric or end)
                    int len = 0;
                    if (rest.StartsWith("-inf", StringComparison.Ordinal)) len = 4;
                    else if (rest.StartsWith("-Infinity", StringComparison.Ordinal)) len = 9;
                    else if (rest.StartsWith("inf", StringComparison.Ordinal)) len = 3;
                    else if (rest.StartsWith("Infinity", StringComparison.Ordinal)) len = 8;
                    else if (rest.StartsWith("nan", StringComparison.Ordinal)) len = 3;
                    else if (rest.StartsWith("NaN", StringComparison.Ordinal)) len = 3;

                    if (_pos + len < _text.Length)
                    {
                        char next = _text[_pos + len];
                        if (char.IsLetterOrDigit(next) || next == '_')
                        {
                            return false;
                        }
                    }
                    return true;
                }
                return false;
            }

            private object ParseNumber()
            {
                int start = _pos;
                bool isNegative = false;

                if (_pos < _text.Length && _text[_pos] == '-')
                {
                    isNegative = true;
                    _pos++;
                }

                // Check for bare tokens
                string rest = _text.Substring(_pos);
                if (rest.StartsWith("nan", StringComparison.Ordinal) || rest.StartsWith("NaN", StringComparison.Ordinal))
                {
                    _pos += 3;
                    return double.NaN;
                }
                if (rest.StartsWith("inf", StringComparison.Ordinal) || rest.StartsWith("Infinity", StringComparison.Ordinal))
                {
                    if (rest.StartsWith("Infinity", StringComparison.Ordinal)) _pos += 8;
                    else _pos += 3;
                    return isNegative ? double.NegativeInfinity : double.PositiveInfinity;
                }

                // Standard number parsing
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }

                string numStr = _text.Substring(start, _pos - start);
                try
                {
                    return double.Parse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    throw new FormatException("Invalid number at position " + start + ": " + ex.Message);
                }
            }

            private string ParseString()
            {
                if (_text[_pos] != '"')
                {
                    throw new FormatException("Expected '\"' at position " + _pos);
                }
                _pos++;
                StringBuilder sb = new StringBuilder();

                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == '"')
                    {
                        _pos++;
                        return sb.ToString();
                    }
                    if (c == '\\')
                    {
                        _pos++;
                        if (_pos >= _text.Length)
                        {
                            throw new FormatException("Unexpected end of input in string escape at position " + _pos);
                        }
                        char esc = _text[_pos];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_pos + 4 >= _text.Length)
                                {
                                    throw new FormatException("Invalid unicode escape at position " + _pos);
                                }
                                string hex = _text.Substring(_pos + 1, 4);
                                try
                                {
                                    int code = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                    sb.Append((char)code);
                                    _pos += 4;
                                }
                                catch (Exception)
                                {
                                    throw new FormatException("Invalid unicode escape at position " + _pos);
                                }
                                break;
                            default:
                                throw new FormatException("Invalid escape character '" + esc + "' at position " + _pos);
                        }
                        _pos++;
                    }
                    else
                    {
                        sb.Append(c);
                        _pos++;
                    }
                }

                throw new FormatException("Unterminated string at position " + _pos);
            }

            private Dictionary<string, object> ParseObject()
            {
                if (_text[_pos] != '{')
                {
                    throw new FormatException("Expected '{' at position " + _pos);
                }
                _pos++;
                Dictionary<string, object> dict = new Dictionary<string, object>(StringComparer.Ordinal);

                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '}')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != '"')
                    {
                        throw new FormatException("Expected string key at position " + _pos);
                    }
                    string key = ParseString();

                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != ':')
                    {
                        throw new FormatException("Expected ':' at position " + _pos);
                    }
                    _pos++;

                    object value = ParseValue();
                    dict[key] = value;

                    SkipWhitespace();
                    if (_pos >= _text.Length)
                    {
                        throw new FormatException("Unexpected end of input in object at position " + _pos);
                    }

                    if (_text[_pos] == '}')
                    {
                        _pos++;
                        return dict;
                    }
                    if (_text[_pos] != ',')
                    {
                        throw new FormatException("Expected ',' or '}' at position " + _pos);
                    }
                    _pos++;
                }
            }

            private List<object> ParseArray()
            {
                if (_text[_pos] != '[')
                {
                    throw new FormatException("Expected '[' at position " + _pos);
                }
                _pos++;
                List<object> list = new List<object>();

                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == ']')
                {
                    _pos++;
                    return list;
                }

                while (true)
                {
                    object value = ParseValue();
                    list.Add(value);

                    SkipWhitespace();
                    if (_pos >= _text.Length)
                    {
                        throw new FormatException("Unexpected end of input in array at position " + _pos);
                    }

                    if (_text[_pos] == ']')
                    {
                        _pos++;
                        return list;
                    }
                    if (_text[_pos] != ',')
                    {
                        throw new FormatException("Expected ',' or ']' at position " + _pos);
                    }
                    _pos++;
                }
            }

            private void Expect(string token)
            {
                if (_pos + token.Length > _text.Length)
                {
                    throw new FormatException("Unexpected end of input at position " + _pos);
                }
                if (_text.Substring(_pos, token.Length) != token)
                {
                    throw new FormatException("Expected '" + token + "' at position " + _pos);
                }
                _pos += token.Length;
            }
        }
    }

    public sealed class AtomSpectraRestResponse
    {
        public int Status;
        public string Body;
        public int WireBytes;
        public int BodyBytes;   // the decoded body; with ContentEncoding set, WireBytes is the compressed size on the network
        public string ContentEncoding;
        public long ElapsedMs;
        public string Error;
        public bool BodyBroken;   // the board answered (Status is kept) but the body was cut short or corrupt (gzip)

        private static bool BodySaysRefused(string body)
        {
            try
            {
                Dictionary<string, object> o = AtomSpectraJson.Parse(body ?? "") as Dictionary<string, object>;
                return o != null && !AtomSpectraJson.GetBool(o, "ok", true);
            }
            catch (FormatException)
            {
                return false;   // a non-JSON 2xx body (empty) carries no refusal
            }
        }

        public bool SpectrometerDead;   // header X-Spectrometer-Dead: "1" (the board sends "0" or "1", fw spectrum_http_cache.c:279-280)

        // The request certainly never reached the board: the connection was refused or the name did not resolve.
        // Only then may a POST be repeated without risk of doing the command twice.
        public bool NotSent;

        internal static bool IsNotSent(WebException ex)
        {
            return ex != null && (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.NameResolutionFailure
                || ex.Status == WebExceptionStatus.ProxyNameResolutionFailure);
        }

        public bool Ok
        {
            get { return Error == null && Status >= 200 && Status < 300; }
        }

        // A POST is accepted only on 2xx AND not {"ok":false}: the board answers 200 {"ok":false}
        // when the analyzer refuses the command (web_server.c:341).
        public bool Accepted
        {
            get { return Ok && !BodySaysRefused(Body); }
        }

        // POST /api/reset answers {"ok":true,"sent":false} when the analyzer did not get -rst (fw web_server.c:563-571).
        public bool SentFalse
        {
            get
            {
                try
                {
                    Dictionary<string, object> o = AtomSpectraJson.Parse(Body ?? "") as Dictionary<string, object>;
                    return o != null && !AtomSpectraJson.GetBool(o, "sent", true);
                }
                catch (FormatException)
                {
                    return false;
                }
            }
        }
    }

    public enum AtomSpectraRestAuth { None, Basic, Bearer }

    public sealed class AtomSpectraRestClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _baseUrl;   // scheme://host[:port][/prefix], no trailing slash
        private string _csrfToken;

        public string Host { get { return _host; } }
        public int Port { get { return _port; } }
        public string BaseUrl { get { return _baseUrl; } }

        public bool UseSystemProxy = true;                               // system proxy, "do not use" switch
        public AtomSpectraRestAuth AuthKind = AtomSpectraRestAuth.None;  // for a proxy in front of the board
        public string AuthUser;
        public string AuthSecret;                                        // never written to the log

        // The last client in use: the log window's "Board log" tab reads /api/devlog through it.
        public static volatile AtomSpectraRestClient Current;

        // The header asks the board for gzip/deflate; replies are decoded by Content-Encoding.
        public bool SendAcceptEncoding = true;

        public AtomSpectraRestClient(string host, int port)
            : this("http://" + (host ?? "") + (port == 80 ? "" : ":" + port) + "/")
        {
        }

        // "http://atomspectra.local/", "https://proxy.example/board1/": scheme, host, port and a path prefix.
        public AtomSpectraRestClient(string baseUrl)
        {
            Uri u = new Uri(baseUrl, UriKind.Absolute);
            _host = u.Host;
            _port = u.Port;
            _baseUrl = u.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        public AtomSpectraRestResponse Get(string path, int deadlineMs)
        {
            return Send("GET", path, null, false, deadlineMs);
        }

        // a repeated attempt of a poll goes over a connection of its own (own group, no keep-alive), so it never waits behind the
        // aborted one on a pooled connection; the board serves one request at a time.
        public AtomSpectraRestResponse Get(string path, int deadlineMs, string connectionGroup)
        {
            return Send("GET", path, null, false, deadlineMs, connectionGroup);
        }

        public AtomSpectraRestResponse Post(string path, string body, int deadlineMs)
        {
            if (_csrfToken == null)
            {
                RefreshToken(deadlineMs);
            }

            AtomSpectraRestResponse resp = Send("POST", path, body, true, deadlineMs);

            if (resp.Status == 403)
            {
                AtomSpectraTcpLog.Info("REST", "403 on " + path + ": CSRF token refreshed, one retry");
                RefreshToken(deadlineMs);
                resp = Send("POST", path, body, true, deadlineMs);
            }

            return resp;
        }

        public AtomSpectraRestResponse Command(string text, int deadlineMs)
        {
            return Post("/api/command", text, deadlineMs);
        }

        public AtomSpectraRestResponse Reset(int deadlineMs)
        {
            return Post("/api/reset", "", deadlineMs);
        }

        public bool RefreshToken(int deadlineMs)
        {
            AtomSpectraRestResponse resp = Get("/api/csrf-token", deadlineMs);
            if (!resp.Ok)
            {
                return false;
            }

            try
            {
                object parsed = AtomSpectraJson.Parse(resp.Body);
                if (parsed is Dictionary<string, object>)
                {
                    Dictionary<string, object> dict = (Dictionary<string, object>)parsed;
                    string token = AtomSpectraJson.GetString(dict, "token");
                    if (token != null)
                    {
                        _csrfToken = token;
                        AtomSpectraTcpLog.Info("REST", "CSRF token received");
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore parse errors
            }

            return false;
        }

        private AtomSpectraRestResponse Send(string method, string path, string body, bool withToken, int deadlineMs, string connectionGroup = null)
        {
            Stopwatch sw = Stopwatch.StartNew();
            AtomSpectraRestResponse result = new AtomSpectraRestResponse();
            result.Status = 0;
            result.Body = null;
            result.WireBytes = 0;
            result.ContentEncoding = "";
            result.ElapsedMs = 0;
            result.Error = null;

            HttpWebRequest req = null;
            bool bodyPending = method == "POST" && !string.IsNullOrEmpty(body);   // the command text is the body: unwritten = not done
            try
            {
                req = (HttpWebRequest)WebRequest.Create(BaseUrl + path);
                req.Method = method;
                req.Proxy = UseSystemProxy ? WebRequest.DefaultWebProxy : null;
                // A POST gets its own connection: a pooled one the board has silently dropped would turn
                // "not sent" into "sent, no answer"; polls keep the connection.
                req.KeepAlive = method != "POST";
                if (connectionGroup != null)
                {
                    req.ConnectionGroupName = connectionGroup;
                    req.KeepAlive = false;
                }
                if (method == "POST")
                {
                    // KeepAlive=false alone still lets a POST ride an idle pooled poll connection; its own group
                    // gives every command a fresh connect, so a refused connect really means "not sent"
                    req.ConnectionGroupName = "command";
                }
                req.AllowAutoRedirect = false;
                req.Timeout = deadlineMs;
                req.ReadWriteTimeout = deadlineMs;
                // gzip/deflate are decoded by hand after the read (Decode): the framework's AutomaticDecompression hid
                // Content-Encoding and the size on the wire, so the log always said "plain". fw feat/gzip-test compresses /api/spectrum.json.
                req.AutomaticDecompression = DecompressionMethods.None;

                if (SendAcceptEncoding)
                {
                    req.Headers[HttpRequestHeader.AcceptEncoding] = "gzip";   // the board sends gzip only
                }

                string auth = AuthorizationHeader();
                if (auth != null)
                {
                    req.Headers[HttpRequestHeader.Authorization] = auth;
                }

                if (withToken)
                {
                    req.Headers["X-CSRF-Token"] = _csrfToken ?? "";
                }

                // Timeout and ReadWriteTimeout do not bound a body that trickles in slowly;
                // the poll must never outlive its period, so no request queue can build up.
                using (Timer killer = new Timer(_ => { try { req.Abort(); } catch { } }, null, deadlineMs, Timeout.Infinite))
                {
                    if (method == "POST")
                    {
                        byte[] data = Encoding.ASCII.GetBytes(body ?? "");
                        req.ContentType = "text/plain";
                        req.ContentLength = data.Length;

                        using (Stream reqStream = req.GetRequestStream())
                        {
                            reqStream.Write(data, 0, data.Length);
                        }
                        bodyPending = false;
                    }

                    HttpWebResponse resp = null;
                    try
                    {
                        resp = (HttpWebResponse)req.GetResponse();
                    }
                    catch (WebException ex)
                    {
                        if (ex.Response is HttpWebResponse)
                        {
                            resp = (HttpWebResponse)ex.Response;
                        }
                        else
                        {
                            result.Status = 0;
                            result.NotSent = AtomSpectraRestResponse.IsNotSent(ex);
                            result.Error = ex.Status + ": " + ex.Message;
                            result.ElapsedMs = sw.ElapsedMilliseconds;
                            LogRequest(method, path, withToken, result);
                            return result;
                        }
                    }

                    using (resp)
                    {
                        result.Status = (int)resp.StatusCode;

                        using (Stream respStream = resp.GetResponseStream())
                        {
                            using (MemoryStream ms = new MemoryStream())
                            {
                                byte[] buffer = new byte[4096];
                                int bytesRead;
                                string readProblem = null;   // a body that breaks off AFTER the status keeps the status
                                try
                                {
                                    while ((bytesRead = respStream.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        ms.Write(buffer, 0, bytesRead);
                                        if (ms.Length > MaxBodyBytes) break;   // no more is read than the limit
                                    }
                                }
                                catch (Exception ex)
                                {
                                    readProblem = "body read broke off: " + ex.Message;
                                }
                                if (readProblem == null && ms.Length <= MaxBodyBytes && resp.ContentLength >= 0 && ms.Length < resp.ContentLength)
                                {
                                    readProblem = "body cut short: " + ms.Length + " of " + resp.ContentLength + " B";   // closed early, no exception
                                }
                                // ms holds the bytes as they came over the network; gzip/deflate are decoded here
                                result.WireBytes = (int)ms.Length;
                                result.ContentEncoding = (resp.ContentEncoding ?? "").Trim().ToLowerInvariant();
                                result.SpectrometerDead = (resp.Headers["X-Spectrometer-Dead"] ?? "").Trim() == "1";
                                string problem = null; bool broken = false;
                                byte[] decoded = readProblem != null ? new byte[0] : Decode(ms.ToArray(), result.ContentEncoding, out problem, out broken);
                                if (readProblem != null) { problem = readProblem; broken = true; }
                                result.BodyBytes = decoded.Length;
                                result.Body = Encoding.UTF8.GetString(decoded);
                                if (problem != null) { result.Error = problem; result.BodyBroken = broken; }   // the status stays
                            }
                        }

                        if (result.Status < 200 || result.Status >= 300)
                        {
                            if (result.Error == null) result.Error = "HTTP " + result.Status;   // the reason of a broken body is not overwritten
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = 0;
                // connect refused, or aborted at the deadline before the body went out (Windows retries a refused
                // connect for about 2 s, so a short command deadline ends as RequestCanceled, not ConnectFailure)
                result.NotSent = AtomSpectraRestResponse.IsNotSent(ex as WebException) || bodyPending;
                result.Error = ex.Message;
            }
            finally
            {
                if (connectionGroup != null && method == "GET") CloseConnections(connectionGroup);
            }

            result.ElapsedMs = sw.ElapsedMilliseconds;
            LogRequest(method, path, withToken, result);
            return result;
        }

        // closes the connections of a group of this board ("" / null = the default group of the polls). A repeated attempt has a group
        // of its own, closed after the attempt, so the groups do not pile up in the service point; the failed first attempt is closed
        // before the repeat, so the board never serves two connections of one poll.
        public void CloseConnections(string group)
        {
            try
            {
                ServicePointManager.FindServicePoint(new Uri(BaseUrl + "/"), UseSystemProxy ? WebRequest.DefaultWebProxy : null).CloseConnectionGroup(group ?? "");
            }
            catch (Exception)
            {
                // a service point that cannot be found has nothing to close
            }
        }

        // a body over this size (read from the wire or decoded) is refused; the spectrum of N=8192 is about 26 KB
        public const int MaxBodyBytes = 1 << 20;

        // gzip / deflate by Content-Encoding; anything else is returned as it came. problem != null: the body is not handed on;
        // broken: it was cut short or corrupt (the attempt is repeated), otherwise it is over the limit (it is not).
        private static byte[] Decode(byte[] wire, string encoding, out string problem, out bool broken)
        {
            problem = null;
            broken = false;
            if (wire.Length > MaxBodyBytes) { problem = "body is over the limit of " + MaxBodyBytes + " B"; return new byte[0]; }
            if (encoding != "gzip" && encoding != "deflate") return wire;
            try
            {
                using (MemoryStream input = new MemoryStream(wire))
                using (Stream unzip = encoding == "gzip"
                    ? (Stream)new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress)
                    : new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] chunk = new byte[8192];
                    int n;
                    while ((n = unzip.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        output.Write(chunk, 0, n);
                        if (output.Length > MaxBodyBytes) { problem = "decoded body is over the limit of " + MaxBodyBytes + " B"; return new byte[0]; }
                    }
                    if (encoding == "gzip") CheckGzipTrailer(wire, output.Length);
                    return output.ToArray();
                }
            }
            catch (System.IO.InvalidDataException ex)
            {
                problem = ex.Message;
                broken = true;
                return new byte[0];
            }
        }

        // A gzip body cut short (the connection closed, or the board stopped writing) is not a whole answer: the framework's GZipStream
        // may hand back what it decoded without a word, so the trailer (RFC 1952: CRC32, then ISIZE = the length mod 2^32) is checked.
        // The error makes the attempt a failed one (the poll repeats it inside its period).
        // CRC32 is checked by the framework's GZipStream itself (a wrong one throws InvalidDataException); one gzip member is
        // assumed (the board sends one): of several members the ISIZE of the last one would not match the whole output.
        private static void CheckGzipTrailer(byte[] wire, long decodedLength)
        {
            if (wire.Length < 18) throw new System.IO.InvalidDataException("gzip body is cut short (" + wire.Length + " B)");
            uint isize = BitConverter.ToUInt32(wire, wire.Length - 4);
            if (isize != (uint)(decodedLength & 0xFFFFFFFFL)) throw new System.IO.InvalidDataException("gzip body is cut short (the trailer does not match: " + isize + " <> " + decodedLength + ")");
        }

        private string AuthorizationHeader()
        {
            if (AuthKind == AtomSpectraRestAuth.Bearer && !string.IsNullOrEmpty(AuthSecret))
            {
                return "Bearer " + AuthSecret;
            }
            if (AuthKind == AtomSpectraRestAuth.Basic && (!string.IsNullOrEmpty(AuthUser) || !string.IsNullOrEmpty(AuthSecret)))
            {
                return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes((AuthUser ?? "") + ":" + (AuthSecret ?? "")));
            }
            return null;
        }

        private void LogRequest(string method, string path, bool withToken, AtomSpectraRestResponse result)
        {
            // No secret goes to the log: neither the CSRF token nor the Authorization value.
            string logLine = method + " " + BaseUrl + path +
                (withToken ? " [CSRF]" : "") + (AuthKind != AtomSpectraRestAuth.None ? " [auth " + AuthKind + "]" : "") +
                " -> " + (result.Status == 0 ? "no response" : result.Status.ToString()) +
                ", " + result.WireBytes + " B" +
                (result.ContentEncoding.Length > 0 ? " (compressed: " + result.ContentEncoding + ", body " + result.BodyBytes + " B)" : " (plain)") +
                ", " + result.ElapsedMs + " ms" +
                (result.Error != null ? ", " + result.Error : "");

            if (result.Error != null)
            {
                AtomSpectraTcpLog.Warn("REST", logLine);
            }
            else
            {
                AtomSpectraTcpLog.Info("REST", logLine);
            }
        }
    }
}
