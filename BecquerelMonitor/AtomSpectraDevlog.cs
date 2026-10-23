using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BecquerelMonitor
{
    // GET /api/devlog?since=N of the board (fw usb_host_cdc.c: ring of the analyzer text packets). Moved out of the log form
    // so the settings console reuses it without WinForms; AtomSpectraTcpLogForm.FormatDevlog forwards here.
    public static class AtomSpectraDevlog
    {
        public static string Format(string json, long since, out long next)
        {
            next = since;
            StringBuilder sb = new StringBuilder();
            Dictionary<string, object> o = AtomSpectraJson.Parse(json) as Dictionary<string, object>;
            if (o == null) return "";

            List<object> lines = AtomSpectraJson.GetArray(o, "lines");
            if (lines != null)
            {
                foreach (object item in lines)
                {
                    Dictionary<string, object> l = item as Dictionary<string, object>;
                    if (l == null) continue;

                    long seq = (long)AtomSpectraJson.GetNumber(l, "seq", 0);
                    if (seq <= since) continue;

                    double t = AtomSpectraJson.GetNumber(l, "t", 0);
                    string body = (AtomSpectraJson.GetString(l, "text") ?? "").TrimEnd('\r', '\n').Replace("\r\n", "\n").Replace("\n", Environment.NewLine + "        ");
                    sb.Append("#" + seq.ToString(CultureInfo.InvariantCulture) + " t=" + (t / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s: " + body + Environment.NewLine);
                    if (seq > next) next = seq;
                }
            }

            double n = AtomSpectraJson.GetNumber(o, "next", -1);
            if (n > next) next = (long)n;
            if (n >= 0 && n < since) next = (long)n;   // the board restarted, its numbering began again

            return sb.ToString();
        }
    }
}
