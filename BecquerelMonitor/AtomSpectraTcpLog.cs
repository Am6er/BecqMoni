using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BecquerelMonitor
{
    public enum AtomSpectraTcpLogLevel
    {
        Info,
        Warn,
        Error,
        Debug   // device answers (-inf may carry the serial); the log window hides it by default
    }

    public sealed class AtomSpectraTcpLogLine
    {
        public long Seq { get; }
        public DateTime Time { get; }
        public AtomSpectraTcpLogLevel Level { get; }
        public string Source { get; }
        public string Text { get; }

        public AtomSpectraTcpLogLine(long seq, DateTime time, AtomSpectraTcpLogLevel level, string source, string text)
        {
            Seq = seq;
            Time = time;
            Level = level;
            Source = source;
            Text = text;
        }

        public override string ToString()
        {
            string levelTag;
            switch (Level)
            {
                case AtomSpectraTcpLogLevel.Warn:
                    levelTag = "WARN";
                    break;
                case AtomSpectraTcpLogLevel.Error:
                    levelTag = "ERROR";
                    break;
                case AtomSpectraTcpLogLevel.Debug:
                    levelTag = "DEBUG";
                    break;
                default:
                    levelTag = "INFO";
                    break;
            }

            return Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + levelTag + " [" + Source + "] " + Text;
        }
    }

    public static class AtomSpectraTcpLog
    {
        public const int Capacity = 2000;

        private static readonly object sync = new object();
        private static readonly AtomSpectraTcpLogLine[] ring = new AtomSpectraTcpLogLine[Capacity];
        private static int start;
        private static int count;
        private static long seq;

        public static event EventHandler<AtomSpectraTcpLogLine> LineAdded;

        // Status line of the log window: transport, host, link state (set by the device controller).
        // Age of the last board answer for "data is N s old" (REST session; null = none) and its threshold, s.
        public static volatile Func<double> DataAgeSeconds;
        public static volatile int StaleAfterSeconds = 20;

        private static volatile string statusText;
        public static string StatusText { get { return statusText; } }
        public static void SetStatus(string transport, string host, string state)
        {
            // the log window is shared; say that this is the LAST session's state and when it was set
            statusText = "last session: " + (transport ?? "?") + "  |  " + (host ?? "") + "  |  " + (state ?? "") + "  |  " + DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static AtomSpectraTcpLogLine Add(AtomSpectraTcpLogLevel level, string source, string text)
        {
            string maskedText = Mask(text ?? "");
            string safeSource = source ?? "?";

            AtomSpectraTcpLogLine line;
            lock (sync)
            {
                long newSeq = ++seq;
                line = new AtomSpectraTcpLogLine(newSeq, DateTime.Now, level, safeSource, maskedText);

                if (count == Capacity)
                {
                    ring[start] = line;
                    start = (start + 1) % Capacity;
                }
                else
                {
                    ring[(start + count) % Capacity] = line;
                    count++;
                }
            }

            // Raise event outside the lock to avoid deadlocks
            var handler = LineAdded;
            if (handler != null)
            {
                foreach (Delegate d in handler.GetInvocationList())
                {
                    try
                    {
                        ((EventHandler<AtomSpectraTcpLogLine>)d)(null, line);
                    }
                    catch
                    {
                        // Ignore subscriber exceptions
                    }
                }
            }

            return line;
        }

        public static AtomSpectraTcpLogLine Info(string source, string text)
        {
            return Add(AtomSpectraTcpLogLevel.Info, source, text);
        }

        public static AtomSpectraTcpLogLine Warn(string source, string text)
        {
            return Add(AtomSpectraTcpLogLevel.Warn, source, text);
        }

        public static AtomSpectraTcpLogLine Error(string source, string text)
        {
            return Add(AtomSpectraTcpLogLevel.Error, source, text);
        }

        public static AtomSpectraTcpLogLine Debug(string source, string text)
        {
            return Add(AtomSpectraTcpLogLevel.Debug, source, text);
        }

        public static List<AtomSpectraTcpLogLine> Snapshot()
        {
            List<AtomSpectraTcpLogLine> result = new List<AtomSpectraTcpLogLine>(count);
            lock (sync)
            {
                for (int i = 0; i < count; i++)
                {
                    result.Add(ring[(start + i) % Capacity]);
                }
            }
            return result;
        }

        public static int Count
        {
            get
            {
                lock (sync)
                {
                    return count;
                }
            }
        }

        public static void Clear()
        {
            lock (sync)
            {
                start = 0;
                count = 0;
                Array.Clear(ring, 0, Capacity);
                // seq keeps counting
            }
        }

        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // CSRF tokens and passwords must not appear in the log
            text = Regex.Replace(text, @"(X-CSRF-Token\s*[:=]\s*)\S+", "$1***", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"""token""\s*:\s*""[^""]*""", "\"token\": \"***\"", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(token=)[^&\s]+", "$1***", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(password\s*[:=]\s*)\S+", "$1***", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(Authorization\s*[:=]\s*)\S+(\s+\S+)?", "$1***", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(Bearer|Basic)\s+[A-Za-z0-9\-._~+/=]{6,}", "$1 ***");
            text = Regex.Replace(text, @"(://)[^/@\s]+@", "$1***@");   // user:password@ in a URL

            return text;
        }

        public static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
