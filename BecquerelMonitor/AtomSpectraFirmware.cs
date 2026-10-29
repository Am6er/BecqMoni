// AtomSpectra board firmware version check.
// Warning only; never blocks operation.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    public static class AtomSpectraFirmware
    {
        // source - answer of the ESP32 contour, 2026-10-24 (git diff firmware-v1.2.38 firmware-v1.2.39):
        // mDNS service _atomspectra._tcp and the board memory fix appear in 1.2.39;
        // on 1.2.38 the device search and Start may not work (REST protocol is identical).
        public const string RecommendedVersion = "1.2.39";
        public const int MaxShownVersion = 24;   // characters of the version put into the hint

        private static readonly Regex VersionRegex = new Regex(
            @"^([0-9]+)\.([0-9]+)\.([0-9]+)(?:[-+_ ].*)?$",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly int RecMajor;
        private static readonly int RecMinor;
        private static readonly int RecPatch;

        static AtomSpectraFirmware()
        {
            int m, mi, p;
            if (!TryParse(RecommendedVersion, out m, out mi, out p))
            {
                throw new InvalidOperationException("Invalid recommended version constant.");
            }
            RecMajor = m;
            RecMinor = mi;
            RecPatch = p;
        }

        public static bool TryParse(string version, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrEmpty(version))
                return false;

            string trimmed = version.Trim();
            if (trimmed.Length == 0)
                return false;

            Match match = VersionRegex.Match(trimmed);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major))
                return false;
            if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor))
                return false;
            if (!int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch))
                return false;

            return true;
        }

        public static bool? IsBelowRecommended(string version)
        {
            int major, minor, patch;
            if (!TryParse(version, out major, out minor, out patch))
                return null;

            if (major != RecMajor)
                return major < RecMajor;
            if (minor != RecMinor)
                return minor < RecMinor;
            return patch < RecPatch;
        }

        public static string WarningText(string version)
        {
            bool? below = IsBelowRecommended(version);
            if (below != true)
                return null;

            string shown = version.Trim();   // an odd long answer of the board must not push the hint out of the result label
            if (shown.Length > MaxShownVersion)
                shown = shown.Substring(0, MaxShownVersion) + "...";
            return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraFirmwareBelow, shown, RecommendedVersion);
        }

        public static string AppendWarning(string text, string version)
        {
            string warning = WarningText(version);
            if (warning == null)
                return text;

            if (string.IsNullOrEmpty(text))
                return warning;

            return text + Environment.NewLine + warning;
        }

        public static bool IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome outcome)
        {
            return outcome == AtomSpectraTcpBeginOutcome.ConnectFailed
                || outcome == AtomSpectraTcpBeginOutcome.NoAnswer
                || outcome == AtomSpectraTcpBeginOutcome.LinkClosed;
        }

        public static string ReadVersion(AtomSpectraRestClient client, int timeoutMs)
        {
            try
            {
                if (client == null)
                    return null;

                AtomSpectraRestResponse response = client.Get("/api/status", timeoutMs);
                if (response == null || !response.Ok || string.IsNullOrEmpty(response.Body))
                    return null;

                Dictionary<string, object> dict = AtomSpectraJson.Parse(response.Body) as Dictionary<string, object>;
                if (dict == null)
                    return null;

                return AtomSpectraJson.GetString(dict, "fw_version");
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
