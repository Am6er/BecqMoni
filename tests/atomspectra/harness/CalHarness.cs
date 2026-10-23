using System;
using System.Globalization;
using BecquerelMonitor;

namespace CalHarness
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: CalHarness <host> <port> <expectedFailure> [expectedCoefficients]");
                return 1;
            }

            string host = args[0];
            int port;
            if (!int.TryParse(args[1], out port))
            {
                Console.WriteLine("Invalid port: " + args[1]);
                return 1;
            }

            string expectedFailureStr = args[2];
            string expectedCoefficientsStr = args.Length > 3 ? args[3] : null;

            bool isLiveMode = string.Equals(expectedFailureStr, "any", StringComparison.OrdinalIgnoreCase);
            AtomSpectraTcpCalibrationFailure expectedFailure;
            if (!isLiveMode)
            {
                if (!Enum.TryParse<AtomSpectraTcpCalibrationFailure>(expectedFailureStr, true, out expectedFailure))
                {
                    Console.WriteLine("Invalid expectedFailure: " + expectedFailureStr);
                    return 1;
                }
            }
            else
            {
                expectedFailure = AtomSpectraTcpCalibrationFailure.None;
            }

            double[] expectedCoefficients = null;
            if (!isLiveMode && expectedFailure == AtomSpectraTcpCalibrationFailure.None && expectedCoefficientsStr != null)
            {
                string[] parts = expectedCoefficientsStr.Split(',');
                expectedCoefficients = new double[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out expectedCoefficients[i]))
                    {
                        Console.WriteLine("Invalid coefficient at index " + i + ": " + parts[i]);
                        return 1;
                    }
                }
            }

            try
            {
                AtomSpectraTcpCalibrationResult result = AtomSpectraTcpCalibrationReader.Read(
                    () => new AtomSpectraTcpTransport(host, port, 3000),
                    2000
                );

                Console.WriteLine("failure: " + result.Failure);
                Console.WriteLine("error: " + (string.IsNullOrEmpty(result.Error) ? "none" : result.Error));

                string rawAnswer = result.RawAnswer;
                if (rawAnswer == null)
                {
                    Console.WriteLine("raw answer (escaped): <null>");
                }
                else
                {
                    string escaped = rawAnswer.Replace("\r", "\\r").Replace("\n", "\\n");
                    Console.WriteLine("raw answer (escaped): " + escaped);
                }

                if (result.Coefficients != null)
                {
                    string[] coeffStrs = new string[result.Coefficients.Length];
                    for (int i = 0; i < result.Coefficients.Length; i++)
                    {
                        coeffStrs[i] = result.Coefficients[i].ToString("R", CultureInfo.InvariantCulture);
                    }
                    Console.WriteLine("coefficients: " + string.Join("; ", coeffStrs));
                }

                if (isLiveMode)
                {
                    if (result.Failure == AtomSpectraTcpCalibrationFailure.None)
                    {
                        Console.WriteLine("RESULT: PASS");
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine("RESULT: FAIL (live mode: failure is " + result.Failure + ")");
                        return 1;
                    }
                }

                int n = expectedCoefficients != null ? expectedCoefficients.Length : 0;
                int matched = 0;
                if (result.Coefficients != null)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (i < result.Coefficients.Length &&
                            BitConverter.DoubleToInt64Bits(result.Coefficients[i]) == BitConverter.DoubleToInt64Bits(expectedCoefficients[i]))
                        {
                            matched++;
                        }
                    }
                }

                if (n > 0)
                {
                    Console.WriteLine("coefficients matched: " + matched + "/" + n);
                }

                bool failureMatched = result.Failure == expectedFailure;
                bool coeffsMatched = (n == 0) || (matched == n && result.Coefficients != null && result.Coefficients.Length == n);

                if (failureMatched && coeffsMatched)
                {
                    Console.WriteLine("RESULT: PASS");
                    return 0;
                }
                else
                {
                    string reason = "";
                    if (!failureMatched)
                    {
                        reason += "failure mismatch (expected " + expectedFailure + ", got " + result.Failure + ")";
                    }
                    if (!coeffsMatched)
                    {
                        if (reason.Length > 0) reason += "; ";
                        reason += "coefficients mismatch (matched " + matched + "/" + n + ", length " + (result.Coefficients != null ? result.Coefficients.Length.ToString() : "null") + ")";
                    }
                    Console.WriteLine("RESULT: FAIL (" + reason + ")");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL (exception) " + ex.Message);
                return 3;
            }
        }
    }
}
