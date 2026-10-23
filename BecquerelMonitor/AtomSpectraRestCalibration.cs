using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BecquerelMonitor
{
    // Why a REST calibration was not taken: the code the window branches on (its text is in the resources), not the log's English Error.
    public enum AtomSpectraRestCalibReason
    {
        None,
        Network,     // no answer / an HTTP status
        NotObject,   // the answer is not a JSON object
        NoArray,     // no "calibration" / "calib" array
        NotSet,      // calib_set is false: the board has no calibration
        NotNumber,   // a slot that is not a number (Slot)
        Gap,         // a number after an empty slot (Slot)
        NotFinite,   // NaN or infinity in a slot (Slot)
        TooFew,      // fewer than 2 coefficients
        Malformed    // the answer does not parse
    }

    public static class AtomSpectraRestCalibration
    {
        // the reason of each result made here (the result type is shared with TCP and carries no code of its own)
        private sealed class Why
        {
            public AtomSpectraRestCalibReason Reason;
            public int Slot = -1;
            public string UserError;   // Network: the user's text of the failed request
        }

        private static readonly ConditionalWeakTable<AtomSpectraTcpCalibrationResult, Why> why = new ConditionalWeakTable<AtomSpectraTcpCalibrationResult, Why>();

        private static AtomSpectraTcpCalibrationResult Result(AtomSpectraRestCalibReason reason, int slot, AtomSpectraTcpCalibrationFailure failure, string error, double[] coefficients, string raw, string userError = null)
        {
            AtomSpectraTcpCalibrationResult r = new AtomSpectraTcpCalibrationResult(failure, error, coefficients, raw);
            why.Add(r, new Why { Reason = reason, Slot = slot, UserError = userError });
            return r;
        }

        // The reason of a result (one not made here is judged by its Failure alone)
        public static AtomSpectraRestCalibReason ReasonOf(AtomSpectraTcpCalibrationResult r)
        {
            if (r == null) return AtomSpectraRestCalibReason.Malformed;
            Why w;
            if (why.TryGetValue(r, out w)) return w.Reason;
            switch (r.Failure)
            {
                case AtomSpectraTcpCalibrationFailure.None: return AtomSpectraRestCalibReason.None;
                case AtomSpectraTcpCalibrationFailure.Connect:
                case AtomSpectraTcpCalibrationFailure.Busy:
                case AtomSpectraTcpCalibrationFailure.NoAnswer: return AtomSpectraRestCalibReason.Network;
                case AtomSpectraTcpCalibrationFailure.EmptyCoefficients: return AtomSpectraRestCalibReason.TooFew;
                case AtomSpectraTcpCalibrationFailure.InvalidNumber: return AtomSpectraRestCalibReason.NotFinite;
                case AtomSpectraTcpCalibrationFailure.GapInCoefficients: return AtomSpectraRestCalibReason.Gap;
                default: return AtomSpectraRestCalibReason.Malformed;
            }
        }

        // the slot the reason names (-1: none or not known)
        public static int SlotOf(AtomSpectraTcpCalibrationResult r)
        {
            Why w;
            return r != null && why.TryGetValue(r, out w) ? w.Slot : -1;
        }

        // the user's text of a failed request (null: not a request failure, or not known)
        public static string NetworkTextOf(AtomSpectraTcpCalibrationResult r)
        {
            Why w;
            return r != null && why.TryGetValue(r, out w) ? w.UserError : null;
        }

        private static string Inv(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        // channel k' of the folded spectrum sums fine channels m*k' .. m*k'+m-1 (the board and Compress alike);
        // it is placed at the centre of its group, x = m*k' + GroupCentre(m).
        // Assumption: the argument of the board's polynomial is the index of the channel centre (not confirmed by the board).
        public static double GroupCentre(int m)
        {
            return (m - 1) / 2.0;
        }

        private static double Binomial(int n, int k)
        {
            double r = 1;
            for (int t = 1; t <= k; t++) r = r * (n - k + t) / t;
            return r;
        }

        // "calib" is for calib_channels (8192 when the field is absent); for `channels` = calib_channels / m the
        // polynomial E(x), x = m*k' + s, is expanded in powers of k' (same degree): b_j = m^j * sum_i a_i C(i,j) s^(i-j).
        // m = 1: the slots unchanged. NaN (an empty slot) stays NaN. No whole m: null (the calibration is not taken).
        public static double[] ForChannels(double[] slots, int calibChannels, int channels)
        {
            if (slots == null) return null;
            if (calibChannels <= 0) calibChannels = AtomSpectraRestSpectrum.Channels;
            if (channels <= 0 || channels == calibChannels) return (double[])slots.Clone();
            if (channels > calibChannels || calibChannels % channels != 0) return null;
            int m = calibChannels / channels;
            double s = GroupCentre(m);
            double[] b = new double[slots.Length];
            for (int j = 0; j < slots.Length; j++)
            {
                double sum = 0;
                for (int i = j; i < slots.Length; i++) if (!double.IsNaN(slots[i])) sum += slots[i] * Binomial(i, j) * Math.Pow(s, i - j);
                b[j] = double.IsNaN(slots[j]) ? double.NaN : sum * Math.Pow(m, j);
            }
            return b;
        }

        public static AtomSpectraTcpCalibrationResult Read(AtomSpectraRestClient client, int deadlineMs)
        {
            AtomSpectraRestResponse r = client.Get("/api/device", deadlineMs);
            AtomSpectraTcpCalibrationResult result;

            if (!r.Ok)
            {
                AtomSpectraTcpLog.Warn("Cal", "REST calibration failed: " + r.Error);
                result = Result(AtomSpectraRestCalibReason.Network, -1,
                    AtomSpectraTcpCalibrationFailure.Connect,
                    r.Error,
                    null,
                    r.Body,
                    r.UserText);
            }
            else
            {
                result = ParseDevice(r.Body);
            }

            string coeffStr = "";
            if (result.Coefficients != null)
            {
                List<string> parts = new List<string>();
                foreach (double c in result.Coefficients)
                {
                    parts.Add(c.ToString("R", CultureInfo.InvariantCulture));
                }
                coeffStr = string.Join("; ", parts);
            }
            else
            {
                coeffStr = result.Error ?? "";
            }

            AtomSpectraTcpLog.Info("Cal", "REST calibration: " + result.Failure + " " + coeffStr);
            return result;
        }

        // the "calib" slots of a spectrum snapshot (null = calib_set false; NaN = an empty slot), the rules of ParseDevice
        public static AtomSpectraTcpCalibrationResult FromSlots(double[] slots)
        {
            if (slots == null)
                return Result(AtomSpectraRestCalibReason.NotSet, -1, AtomSpectraTcpCalibrationFailure.EmptyCoefficients, "calib_set is false", null, null);
            List<double> coeffs = new List<double>();
            bool emptySeen = false;
            for (int i = 0; i < Math.Min(5, slots.Length); i++)
            {
                if (double.IsNaN(slots[i])) { emptySeen = true; continue; }
                if (emptySeen)
                    return Result(AtomSpectraRestCalibReason.Gap, i, AtomSpectraTcpCalibrationFailure.GapInCoefficients, "Number after empty slot at index " + Inv(i), null, null);
                if (double.IsInfinity(slots[i]))
                    return Result(AtomSpectraRestCalibReason.NotFinite, i, AtomSpectraTcpCalibrationFailure.InvalidNumber, "Infinity at index " + Inv(i), null, null);
                coeffs.Add(slots[i]);
            }
            while (coeffs.Count > 0 && coeffs[coeffs.Count - 1] == 0.0) coeffs.RemoveAt(coeffs.Count - 1);
            return coeffs.Count < 2
                ? Result(AtomSpectraRestCalibReason.TooFew, -1, AtomSpectraTcpCalibrationFailure.EmptyCoefficients, "Fewer than 2 coefficients", null, null)
                : Result(AtomSpectraRestCalibReason.None, -1, AtomSpectraTcpCalibrationFailure.None, null, coeffs.ToArray(), null);
        }

        public static AtomSpectraTcpCalibrationResult ParseDevice(string json)
        {
            try
            {
                object root = AtomSpectraJson.Parse(json);
                if (!(root is Dictionary<string, object>))
                {
                    return Result(AtomSpectraRestCalibReason.NotObject, -1,
                        AtomSpectraTcpCalibrationFailure.Malformed,
                        "Root is not an object",
                        null,
                        json);
                }

                Dictionary<string, object> obj = (Dictionary<string, object>)root;
                // /api/device carries "calibration", /api/spectrum.json carries "calib" (5 slots), same coefficients
                List<object> calibArray = AtomSpectraJson.GetArray(obj, "calibration") ?? AtomSpectraJson.GetArray(obj, "calib");
                if (calibArray == null)
                {
                    return Result(AtomSpectraRestCalibReason.NoArray, -1,
                        AtomSpectraTcpCalibrationFailure.Malformed,
                        "Missing calibration array",
                        null,
                        json);
                }

                bool calibSet = AtomSpectraJson.GetBool(obj, "calib_set", true);
                if (!calibSet)
                {
                    return Result(AtomSpectraRestCalibReason.NotSet, -1,
                        AtomSpectraTcpCalibrationFailure.EmptyCoefficients,
                        "calib_set is false",
                        null,
                        json);
                }

                List<double> coeffs = new List<double>();
                bool emptySeen = false;

                int maxSlots = Math.Min(5, calibArray.Count);
                for (int i = 0; i < maxSlots; i++)
                {
                    object slot = calibArray[i];

                    if (slot == null)
                    {
                        emptySeen = true;
                        continue;
                    }

                    if (!(slot is double))
                    {
                        return Result(AtomSpectraRestCalibReason.NotNumber, i,
                            AtomSpectraTcpCalibrationFailure.Malformed,
                            "Slot " + Inv(i) + " is not a number",
                            null,
                            json);
                    }

                    double val = (double)slot;

                    if (emptySeen)
                    {
                        return Result(AtomSpectraRestCalibReason.Gap, i,
                            AtomSpectraTcpCalibrationFailure.GapInCoefficients,
                            "Number after empty slot at index " + Inv(i),
                            null,
                            json);
                    }

                    if (double.IsNaN(val) || double.IsInfinity(val))
                    {
                        return Result(AtomSpectraRestCalibReason.NotFinite, i,
                            AtomSpectraTcpCalibrationFailure.InvalidNumber,
                            "NaN or Infinity at index " + Inv(i),
                            null,
                            json);
                    }

                    coeffs.Add(val);
                }

                while (coeffs.Count > 0 && coeffs[coeffs.Count - 1] == 0.0)
                {
                    coeffs.RemoveAt(coeffs.Count - 1);
                }

                if (coeffs.Count < 2)
                {
                    return Result(AtomSpectraRestCalibReason.TooFew, -1,
                        AtomSpectraTcpCalibrationFailure.EmptyCoefficients,
                        "Fewer than 2 coefficients",
                        null,
                        json);
                }

                return Result(AtomSpectraRestCalibReason.None, -1,
                    AtomSpectraTcpCalibrationFailure.None,
                    null,
                    coeffs.ToArray(),
                    json);
            }
            catch (Exception ex)
            {
                return Result(AtomSpectraRestCalibReason.Malformed, -1,
                    AtomSpectraTcpCalibrationFailure.Malformed,
                    ex.Message,
                    null,
                    json);
            }
        }
    }
}
