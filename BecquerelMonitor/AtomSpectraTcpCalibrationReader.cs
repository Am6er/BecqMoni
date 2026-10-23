// Read-only -cal over the TCP bridge for the AtomSpectra TCP type; parsing and CRC ported from
// DeviceConfigForm.cs:1920-1967 (the COM branch) without AtomSpectraVCPIn; the board serves one client, so the read
// refuses while a measurement of this type holds the same board, and always closes its connection.

using BecquerelMonitor.Hash;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public enum AtomSpectraTcpCalibrationFailure
    {
        None,
        Busy,
        Connect,
        NoAnswer,
        Malformed,
        BadCrc,
        EmptyCoefficients,
        InvalidNumber,          // NaN or Infinity in a slot
        GapInCoefficients       // an empty slot followed by a filled one
    }

    public sealed class AtomSpectraTcpCalibrationResult
    {
        public AtomSpectraTcpCalibrationFailure Failure { get; private set; }
        public string Error { get; private set; }
        public double[] Coefficients { get; private set; }
        public string RawAnswer { get; private set; }

        public AtomSpectraTcpCalibrationResult(
            AtomSpectraTcpCalibrationFailure failure,
            string error,
            double[] coefficients,
            string rawAnswer)
        {
            Failure = failure;
            Error = error;
            Coefficients = coefficients;
            RawAnswer = rawAnswer;
        }
    }

    public static class AtomSpectraTcpCalibrationReader
    {
        public const int AnswerTimeoutMs = 2000;

        public static AtomSpectraTcpCalibrationResult Parse(string answer)
        {
            if (string.IsNullOrEmpty(answer))
            {
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.NoAnswer,
                    null,
                    null,
                    answer);
            }

            string[] parts = answer.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (parts.Length < 11)
            {
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Malformed,
                    string.Format(CultureInfo.InvariantCulture, "expected 11 lines, got {0}", parts.Length),
                    null,
                    answer);
            }

            // Build coefficient strings: pairs of consecutive lines
            string[] coeffStrings = new string[5];
            for (int i = 0; i < 5; i++)
            {
                coeffStrings[i] = parts[i * 2] + parts[i * 2 + 1];
            }

            // CRC check: concatenation of parts[0..9]
            string crcInput = "";
            for (int i = 0; i < 10; i++)
            {
                crcInput += parts[i];
            }
            byte[] crcBytes = Encoding.ASCII.GetBytes(crcInput);
            uint computedCrc = Crc32.Compute(crcBytes);

            string crcHex = parts[10].Trim();
            uint receivedCrc;
            if (!uint.TryParse(crcHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out receivedCrc))
            {
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Malformed,
                    "invalid CRC hex: " + crcHex,
                    null,
                    answer);
            }

            if (receivedCrc != computedCrc)
            {
                string error = string.Format(
                    CultureInfo.InvariantCulture,
                    "CRC mismatch: received 0x{0:X8}, computed 0x{1:X8}",
                    receivedCrc,
                    computedCrc);
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.BadCrc,
                    error,
                    null,
                    answer);
            }

            // Parse coefficients, skipping FFFFFFFFFFFFFFFF
            List<double> coeffList = new List<double>();
            bool emptySeen = false;
            for (int i = 0; i < 5; i++)
            {
                if (string.Equals(coeffStrings[i], "FFFFFFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase))
                {
                    emptySeen = true;
                    continue;
                }
                // The donor compacts the list, and a filled slot after an empty one silently takes a lower power.
                if (emptySeen)
                {
                    return new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.GapInCoefficients,
                        "slot " + i + " is filled after an empty slot", null, answer);
                }

                ulong raw;
                if (!ulong.TryParse(coeffStrings[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out raw))
                {
                    return new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.Malformed,
                        "invalid coefficient hex: " + coeffStrings[i],
                        null,
                        answer);
                }

                byte[] floatVals = BitConverter.GetBytes(raw);
                double value = BitConverter.ToDouble(floatVals, 0);
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.InvalidNumber,
                        "slot " + i + " is not a finite number: " + coeffStrings[i], null, answer);
                }
                coeffList.Add(value);
            }

            // Remove trailing zero coefficients from the end of the list
            // (the donor indexes [4]..[0] directly and throws when a slot was FFFF; here the loop runs over the real count)
            while (coeffList.Count > 0 && coeffList[coeffList.Count - 1] == 0.0)
            {
                coeffList.RemoveAt(coeffList.Count - 1);
            }

            if (coeffList.Count < 2)
            {
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.EmptyCoefficients,
                    null,
                    null,
                    answer);
            }

            double[] coefficients = new double[coeffList.Count];
            for (int i = 0; i < coeffList.Count; i++)
            {
                coefficients[i] = coeffList[i];
            }

            return new AtomSpectraTcpCalibrationResult(
                AtomSpectraTcpCalibrationFailure.None,
                null,
                coefficients,
                answer);
        }

        public static AtomSpectraTcpCalibrationResult Read(Func<IAtomSpectraTcpTransport> transportFactory, int timeoutMs)
        {
            IAtomSpectraTcpTransport transport = transportFactory();
            try
            {
                transport.Open();
            }
            catch (Exception ex)
            {
                try { transport.Dispose(); } catch { }
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Connect,
                    ex.Message,
                    null,
                    null);
            }

            try
            {
                // Send the -cal command
                byte[] command = AtomSpectraShproto.EncodeText("-cal");
                transport.Write(command, 0, command.Length);

                // Read response packets until deadline
                AtomSpectraShproto codec = new AtomSpectraShproto();
                StringBuilder text = new StringBuilder();
                byte[] buffer = new byte[4096];
                long deadline = transport.NowMs + timeoutMs;
                AtomSpectraTcpCalibrationResult last = null;

                // The answer may arrive in one or several text packets, so packets are joined and parsed after each one.
                while (transport.NowMs < deadline)
                {
                    int n;
                    try
                    {
                        n = transport.Read(buffer, 0, buffer.Length, 100);
                    }
                    catch (IOException ex)
                    {
                        if (last != null)
                        {
                            return last;
                        }
                        return new AtomSpectraTcpCalibrationResult(
                            AtomSpectraTcpCalibrationFailure.Connect,
                            ex.Message,
                            null,
                            null);
                    }

                    for (int i = 0; i < n; i++)
                    {
                        if (codec.Feed(buffer[i]) && codec.Command == 0x03)
                        {
                            // Append payload bytes as ASCII text
                            int len = codec.Length;
                            if (len > 0 && codec.Packet != null)
                            {
                                byte[] payload = new byte[len];
                                for (int j = 0; j < len; j++)
                                {
                                    payload[j] = codec.Packet[j];
                                }
                                text.Append(Encoding.ASCII.GetString(payload));
                            }

                            last = Parse(text.ToString());
                            if (last.Failure == AtomSpectraTcpCalibrationFailure.None)
                            {
                                return last;
                            }
                        }
                    }
                }

                // After deadline
                if (text.Length == 0)
                {
                    return new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.NoAnswer,
                        string.Format(CultureInfo.InvariantCulture, "no text answer to -cal in {0} ms", timeoutMs),
                        null,
                        null);
                }

                return last;
            }
            finally
            {
                try { transport.Close(); } catch { }
                try { transport.Dispose(); } catch { }
            }
        }

        public static AtomSpectraTcpCalibrationResult Read(AtomSpectraTcpDeviceConfig config)
        {
            // REST: GET /api/device (the web server serves several clients, so a running measurement is no obstacle)
            if (config != null && config.Transport == AtomSpectraTransport.Rest)
            {
                string urlError;
                AtomSpectraRestClient client = config.CreateRestClient(out urlError);
                return client == null
                    ? new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.Connect, urlError, null, null)
                    : AtomSpectraRestCalibration.Read(client, AnswerTimeoutMs);
            }

            if (config == null || string.IsNullOrEmpty(config.Host))
            {
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Connect,
                    "no host is set",
                    null,
                    null);
            }

            if (AtomSpectraTcpSession.IsHostInUse(config.Host, config.TcpPort))
            {
                AtomSpectraTcpLog.Warn("Cal", "-cal refused: " + config.Host + ":" + config.TcpPort + " is busy with a measurement");
                return new AtomSpectraTcpCalibrationResult(
                    AtomSpectraTcpCalibrationFailure.Busy,
                    null,
                    null,
                    null);
            }

            Stopwatch sw = Stopwatch.StartNew();
            AtomSpectraTcpCalibrationResult result = Read(
                () => new AtomSpectraTcpTransport(config.Host, config.TcpPort, AtomSpectraTcpTransport.DefaultConnectTimeoutMs),
                AnswerTimeoutMs);
            AtomSpectraTcpLog.Info("Cal", "sent -cal to " + config.Host + ":" + config.TcpPort + ": " + (result == null ? "?" : result.Failure.ToString())
                + " in " + sw.ElapsedMilliseconds + " ms" + (result != null && result.Error != null ? " (" + result.Error + ")" : ""));
            return result;
        }

        public static void ReadAsync(
            AtomSpectraTcpDeviceConfig config,
            Control owner,
            Action<AtomSpectraTcpCalibrationResult> done)
        {
            // Copy config values so the user can edit the form meanwhile
            string host = config != null ? config.Host : null;
            int port = config != null ? config.TcpPort : 0;
            AtomSpectraTcpDeviceConfig copy = new AtomSpectraTcpDeviceConfig
            {
                Host = host,
                TcpPort = port
            };
            if (config != null)
            {
                copy = (AtomSpectraTcpDeviceConfig)config.Clone();   // REST needs the URL, proxy and authorization too
            }

            Thread thread = new Thread(delegate()
            {
                AtomSpectraTcpCalibrationResult result;
                try
                {
                    result = Read(copy);
                }
                catch (Exception ex)
                {
                    result = new AtomSpectraTcpCalibrationResult(
                        AtomSpectraTcpCalibrationFailure.Malformed,
                        ex.Message,
                        null,
                        null);
                }

                if (owner != null && !owner.IsDisposed && owner.IsHandleCreated)
                {
                    try
                    {
                        owner.BeginInvoke(new MethodInvoker(delegate
                        {
                            done(result);
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                        // Owner was disposed or handle destroyed; ignore
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "AtomSpectraTcpCal";
            thread.Start();
        }

        // The button of DeviceConfigForm: read on a worker thread, then on the UI thread either a message box
        // (the resources of the COM branch) or the fields filled as the COM branch does (DeviceConfigForm.cs:1977-2000).
        // fieldsInOrder: [0] numericUpDown7, [1] numericUpDown2, [2] numericUpDown1, [3] numericUpDown9, [4] numericUpDown8.
        public static void ReadIntoFormAsync(AtomSpectraTcpDeviceConfig config, Control owner, Control button, Func<bool> stillCurrent,
            Func<PolynomialEnergyCalibration> calibration, Control[] fieldsInOrder, Action markDirty, Func<int> channels = null)
        {
            button.Enabled = false;
            ReadAsync(config, owner, delegate (AtomSpectraTcpCalibrationResult result)
            {
                button.Enabled = true;
                if (!stillCurrent()) return;      // another device config was selected meanwhile
                string error = Describe(result, config);
                // through AppUi.Report: a bare MessageBox here hangs a run without windows (S100, tools/check_headless.py)
                if (error != null) { AppUi.Report(error, Resources.ErrorDialogTitle, MessageBoxIcon.Warning); return; }
                if (!ApplyResult(result, config, calibration, fieldsInOrder, markDirty, channels))
                    AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpCalNotTaken, channels == null ? 0 : channels()),
                        Resources.ErrorDialogTitle, MessageBoxIcon.Warning);
            });
        }

        // the board's answer given to the page: a seam for the tests (no worker thread, no form)
        internal static bool ApplyResult(AtomSpectraTcpCalibrationResult result, AtomSpectraTcpDeviceConfig config,
            Func<PolynomialEnergyCalibration> calibration, Control[] fieldsInOrder, Action markDirty, Func<int> channels)
        {
            // the page edits the profile of N channels. REST: the board's 8192-channel polynomial folded to N (the same point as at Start);
            // TCP: the scale of -cal at N other than 8192 is not established, so the button takes nothing (false = not taken, the caller says so)
            int n = channels == null ? 0 : channels();
            double[] coefficients = result.Coefficients;
            bool rest = config != null && config.Transport == AtomSpectraTransport.Rest;
            if (rest && n > 0) coefficients = AtomSpectraRestCalibration.ForChannels(coefficients, 0, n);
            else if (!rest && n > 0 && n != AtomSpectraRestSpectrum.Channels) coefficients = null;
            if (coefficients == null) return false;
            ApplyToFields(calibration(), coefficients, fieldsInOrder);
            markDirty();
            return true;
        }

        // the model AND the fields of the Energy Calibration page (SaveFormContents reads the fields back)
        public static void ApplyToFields(PolynomialEnergyCalibration target, double[] coefficients, Control[] fieldsInOrder)
        {
            foreach (Control field in fieldsInOrder) field.Text = "0";
            target.PolynomialOrder = coefficients.Length - 1;
            target.Coefficients = (double[])coefficients.Clone();
            for (int i = 0; i < coefficients.Length && i < fieldsInOrder.Length; i++)
                fieldsInOrder[i].Text = coefficients[i].ToString(CultureInfo.InvariantCulture);
        }

        public static string Describe(AtomSpectraTcpCalibrationResult result, AtomSpectraTcpDeviceConfig config)
        {
            if (result == null || result.Failure == AtomSpectraTcpCalibrationFailure.None)
            {
                return null;
            }

            string host = config != null ? config.Host : "";
            int port = config == null ? 0 : config.Transport == AtomSpectraTransport.Rest ? config.RestPort : config.TcpPort;

            switch (result.Failure)
            {
                case AtomSpectraTcpCalibrationFailure.Busy:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpCalBusy, host, port);
                case AtomSpectraTcpCalibrationFailure.Connect:
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpConnect, host, port, result.Error);
                case AtomSpectraTcpCalibrationFailure.BadCrc:
                    return Resources.ERRIncorrectCRC;
                case AtomSpectraTcpCalibrationFailure.EmptyCoefficients:
                case AtomSpectraTcpCalibrationFailure.InvalidNumber:
                    return Resources.ERREmptyCoefficients;
                case AtomSpectraTcpCalibrationFailure.GapInCoefficients:
                    return Resources.ERRAtomSpectraTcpCalGap;
                case AtomSpectraTcpCalibrationFailure.NoAnswer:
                case AtomSpectraTcpCalibrationFailure.Malformed:
                    return Resources.ERRReadDataFromPort_Empty;
                default:
                    return null;
            }
        }
    }
}
