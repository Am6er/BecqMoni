// the board's reply compression level.
// Only fw feat/gzip-test (1.2.37-rc90 and later) shows gzip_level in GET /api/boot-config; release 1.2.37 accepts the POST with 200 {"ok":true}
// and changes nothing, so nothing is written unless the field is there, and success is checked by reading the value back.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor
{
    public static class AtomSpectraRestBootConfig
    {
        public const int Unsupported = -1;   // no gzip_level in /api/boot-config: this firmware does not support it
        public const int Unknown = -2;       // not read: no answer or not JSON
        public static readonly int[] Levels = { 0, 1, 6 };   // fw spectrum_gz_plan.h:24; anything else -> 400 (web_server.c:924-927)

        public static int ParseGzipLevel(string json)
        {
            try
            {
                Dictionary<string, object> o = AtomSpectraJson.Parse(json ?? "") as Dictionary<string, object>;
                if (o == null) return Unknown;
                if (!o.ContainsKey("gzip_level")) return Unsupported;
                double v = AtomSpectraJson.GetNumber(o, "gzip_level", double.NaN);
                if (double.IsNaN(v) || v != Math.Floor(v)) return Unknown;
                int n = (int)v;
                return Array.IndexOf(Levels, n) >= 0 ? n : Unknown;
            }
            catch (FormatException)
            {
                return Unknown;
            }
        }

        public static int ReadGzipLevel(AtomSpectraRestClient client, int deadlineMs)
        {
            AtomSpectraRestResponse r = client.Get("/api/boot-config", deadlineMs);
            return r.Ok ? ParseGzipLevel(r.Body) : Unknown;
        }

        // the level for the user ("not supported" from the resources)
        public static string Describe(int level)
        {
            if (level == Unsupported) return BecquerelMonitor.Properties.Resources.AtomSpectraGzipLevelUnsupported;
            return DescribeLog(level);
        }

        // the level for the log (English)
        private static string DescribeLog(int level)
        {
            if (level == Unsupported) return "not supported";
            if (level == Unknown) return "?";
            return level.ToString(CultureInfo.InvariantCulture);
        }

        private static string Text(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }

        // message: the user's text (resources); the log gets the English one
        private static bool Fail(out string message, string user, string log)
        {
            message = user;
            AtomSpectraTcpLog.Warn("REST", "gzip_level not set: " + log);
            return false;
        }

        // Success = 2xx AND {"ok":true} AND a fresh GET /api/boot-config shows the same value (gz_level in /api/system may lag).
        public static bool WriteGzipLevel(AtomSpectraRestClient client, int level, int deadlineMs, out string message)
        {
            if (Array.IndexOf(Levels, level) < 0)
            {
                message = "level " + level.ToString(CultureInfo.InvariantCulture) + " is not 0, 1 or 6";
                string log = message;
                return Fail(out message, Text(BecquerelMonitor.Properties.Resources.AtomSpectraGzipErrLevel, level), log);
            }
            int before = ReadGzipLevel(client, deadlineMs);
            if (before == Unsupported)
            {
                return Fail(out message, BecquerelMonitor.Properties.Resources.AtomSpectraGzipErrUnsupported, "this firmware has no gzip_level in /api/boot-config (not supported); nothing was sent");
            }
            if (before == Unknown)
            {
                return Fail(out message, BecquerelMonitor.Properties.Resources.AtomSpectraGzipErrNoRead, "GET /api/boot-config gave no usable answer; nothing was sent");
            }
            AtomSpectraRestResponse r = client.Post("/api/boot-config", "{\"gzip_level\":" + level.ToString(CultureInfo.InvariantCulture) + "}", deadlineMs);
            if (!r.Ok)
            {
                return Fail(out message, Text(BecquerelMonitor.Properties.Resources.AtomSpectraGzipErrPost, r.UserText),
                    "POST /api/boot-config: " + (r.Error ?? ("HTTP " + r.Status.ToString(CultureInfo.InvariantCulture))) + (string.IsNullOrEmpty(r.Body) ? "" : " (" + r.Body.Trim() + ")"));
            }
            if (!r.Accepted)
            {
                return Fail(out message, BecquerelMonitor.Properties.Resources.AtomSpectraGzipErrOkFalse, "the board answered {\"ok\":false}: the setting was not saved");
            }
            int after = ReadGzipLevel(client, deadlineMs);
            if (after != level)
            {
                return Fail(out message, Text(BecquerelMonitor.Properties.Resources.AtomSpectraGzipErrReadBack, Describe(after), level),
                    "the board answered ok, but GET /api/boot-config shows " + DescribeLog(after) + " instead of " + level.ToString(CultureInfo.InvariantCulture));
            }
            AtomSpectraTcpLog.Info("REST", "gzip_level " + DescribeLog(before) + " -> " + level.ToString(CultureInfo.InvariantCulture) + " (read back from /api/boot-config)");
            message = null;
            return true;
        }
    }
}
