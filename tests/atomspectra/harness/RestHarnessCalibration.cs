// the board calibration goes to the Energy Calibration page (form) and, at Start, to the document and the device configuration.
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static readonly double[] MockCalib = { 3.65, 0.41, 4.5e-05, -1.3e-08, 1.2e-12 };
        static int saveCount;

        static bool Pump(Func<bool> done, int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(20);
                if (done()) return true;
            }
            return done();
        }

        static bool RunCalibration(string scenario)
        {
            if (scenario != "form-calib" && scenario != "form-calib-unset" && scenario != "start-calib")
                return false;

            if (scenario == "start-calib")
                return RunStartCalibration();

            bool unset = scenario == "form-calib-unset";
            Type t = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
            if (t == null)
            {
                Console.WriteLine("n/a: the settings form is not in this build");
                return true;
            }

            var thread = new Thread(() =>
            {
                try
                {
                    var f = BindingFlags.NonPublic | BindingFlags.Instance;

                    var host = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(-3000, -3000),
                        ShowInTaskbar = false,
                        ClientSize = new Size(490, 599)
                    };

                    var form = (Control)Activator.CreateInstance(t);
                    host.Controls.Add(form);

                    var cfg = new AtomSpectraTcpDeviceConfig();
                    cfg.Transport = AtomSpectraTransport.Rest;
                    cfg.RestUrl = "http://127.0.0.1:" + port + "/";

                    t.GetMethod("LoadFormContents").Invoke(form, new object[] { cfg });
                    host.Show();
                    Application.DoEvents();

                    double[] got = null;
                    int calls = 0;
                    bool hasTarget = false;

                    var targetField = t.GetField("calibrationTarget", f);
                    if (targetField != null)
                    {
                        targetField.SetValue(form, new Action<double[]>(c => { got = c; calls++; }));
                        hasTarget = true;
                    }
                    else
                    {
                        Check(false, "the form has no calibration target (calibrationTarget)");
                    }

                    if (hasTarget)
                    {
                        Func<string, Control> c = n => (Control)t.GetField(n, f).GetValue(form);
                        const string older = "OLDER LINE OF THE OUTPUT";   // PR37 fix, REST: the newest line goes on top, as the console writes
                        c("textOutput").Text = older + Environment.NewLine;

                        t.GetMethod("buttonRestTest_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                        Pump(() => c("buttonRestTest").Enabled, 8000);
                        Application.DoEvents();
                        Console.WriteLine("test: " + c("labelRestResult").Text + " | output: " + c("textOutput").Text.Replace("\r\n", " / "));
                        string outText = c("textOutput").Text;

                        if (!unset)
                        {
                            Check(got != null && got.SequenceEqual(MockCalib), "Test connection (REST): the board calibration (3.65; 0.41; 4.5e-05; -1.3e-08; 1.2e-12) is given to the Energy Calibration page");
                            Check(outText.Contains("3.65"), "Test connection (REST): the output names the coefficients");
                            Check(outText.IndexOf("3.65", StringComparison.Ordinal) < outText.IndexOf(older, StringComparison.Ordinal), "the calibration line is written above the older output (newest on top, as the console)");
                        }
                        else
                        {
                            Check(calls == 0, "Test connection (REST), calib_set=false: the Energy Calibration page is not touched");
                            Check(outText.Contains("calib_set"), "Test connection (REST), calib_set=false: the output says the board has no calibration");
                            Check(outText.IndexOf("calib_set", StringComparison.Ordinal) < outText.IndexOf(older, StringComparison.Ordinal), "the line is written above the older output (newest on top, as the console)");
                        }

                        // both connection tests write their result to the log; the status says whose session it is
                        Check(AtomSpectraTcpLog.Snapshot().Any(l => l.Text.Contains("Test connection (REST)")), "Test connection (REST): the result is in the log");
                        ((RadioButton)c("radioTcp")).Checked = true;
                        c("textAddress").Text = "127.0.0.1:" + port;
                        t.GetMethod("buttonTest_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                        Pump(() => c("buttonTest").Enabled, 8000);
                        Application.DoEvents();
                        Check(AtomSpectraTcpLog.Snapshot().Any(l => l.Text.Contains("Test connection (TCP)") && l.Text.Contains("127.0.0.1:" + port)), "Test connection (TCP): the result is in the log");
                        ((RadioButton)c("radioRest")).Checked = true;
                        AtomSpectraTcpLog.SetStatus("REST", "192.0.2.46:80", "stopped (Detached)");
                        Console.WriteLine("status: " + AtomSpectraTcpLog.StatusText);
                        Check(AtomSpectraTcpLog.StatusText.StartsWith("last session: REST"), "the log status says it is the last session's state");

                        got = null;
                        int callsBefore = calls;
                        var saved = AtomSpectraDeviceConsole.Resolve;
                        try
                        {
                            AtomSpectraDeviceConsole.Resolve = n =>
                            {
                                if (n == "atomspectra.local") return new[] { IPAddress.Loopback };
                                throw new SocketException(11001);
                            };

                            var portField = t.GetField("restAddressPort", f);
                            if (portField != null) portField.SetValue(form, port);

                            c("textAddress").Text = "";
                            t.GetMethod("buttonRefresh_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                            Pump(() => c("buttonRefresh").Enabled, 8000);
                            Application.DoEvents();
                            Console.WriteLine("refresh: " + c("labelStatus").Text + " | output: " + c("textOutput").Text.Replace("\r\n", " / "));

                            if (!unset)
                            {
                                Check(got != null && got.SequenceEqual(MockCalib), "Refresh: the board calibration is given to the Energy Calibration page");
                            }
                            else
                            {
                                Check(calls == callsBefore, "Refresh, calib_set=false: the Energy Calibration page is not touched");
                            }
                        }
                        finally
                        {
                            AtomSpectraDeviceConsole.Resolve = saved;
                        }
                    }

                    host.Close();
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    Check(false, "calibration form test threw " + ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return true;
        }

        static bool SameOrBothNull(double[] a, double[] b)
        {
            return a == null ? b == null : b != null && a.SequenceEqual(b);
        }

        static bool CountSave<T>(T config)
        {
            saveCount++;
            return true;
        }

        static bool RunStartCalibration()
        {
            Type ctl = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
            Type specT = Type.GetType("BecquerelMonitor.EnergySpectrum, BecquerelMonitor");
            Type cfgT = Type.GetType("BecquerelMonitor.DeviceConfigInfo, BecquerelMonitor");

            if (ctl == null || specT == null || cfgT == null)
            {
                Console.WriteLine("n/a: the controller is not in this build");
                return true;
            }

            MethodInfo apply = ctl.GetMethod("ApplyBoardCalibration", BindingFlags.Public | BindingFlags.Static);
            if (apply == null)
            {
                Check(false, "the controller has no ApplyBoardCalibration");
                return true;
            }

            try
            {
                Func<object> newSpectrum = () =>
                {
                    try
                    {
                        return Activator.CreateInstance(specT, new object[] { 1.0, 4096 });
                    }
                    catch (MissingMethodException)
                    {
                        return Activator.CreateInstance(specT, new object[] { 1, 4096 });
                    }
                };

                Func<object, double[]> coeffs = owner =>
                {
                    object cal = owner.GetType().GetProperty("EnergyCalibration").GetValue(owner);
                    return cal == null ? null : (double[])cal.GetType().GetProperty("Coefficients").GetValue(cal);
                };

                object config = Activator.CreateInstance(cfgT);
                var numChProp = cfgT.GetProperty("NumberOfChannels");
                if (numChProp != null && numChProp.CanWrite)
                {
                    numChProp.SetValue(config, 4096);
                }

                int saves = 0;
                Type funcT = typeof(Func<,>).MakeGenericType(cfgT, typeof(bool));
                Delegate save = Delegate.CreateDelegate(funcT, typeof(Program).GetMethod("CountSave", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(cfgT));

                // Case 1
                object spec = newSpectrum();
                saveCount = 0;
                string msg = (string)apply.Invoke(null, new object[] { spec, config, (double[])MockCalib.Clone(), save });
                Console.WriteLine("case 1: " + msg);
                Check(coeffs(spec) != null && coeffs(spec).SequenceEqual(MockCalib), "Start: the document gets the board calibration");
                Check(coeffs(config) != null && coeffs(config).SequenceEqual(MockCalib), "Start: the device configuration gets the board calibration");
                Check(saveCount == 1, "Start: the device configuration is saved once");

                // Case 2
                object spec2 = newSpectrum();
                double[] before2 = coeffs(spec2);
                object config2 = Activator.CreateInstance(cfgT);
                double[] cfgBefore2 = coeffs(config2);
                saveCount = 0;
                string msg2 = (string)apply.Invoke(null, new object[] { spec2, config2, null, save });
                Console.WriteLine("case 2: " + msg2);
                Check(SameOrBothNull(coeffs(spec2), before2) && SameOrBothNull(coeffs(config2), cfgBefore2) && saveCount == 0, "Start, no board calibration: the document and the configuration keep their own, nothing is saved");
                Check(msg2 != null && msg2.Length > 0, "Start, no board calibration: a reason is given");

                // Case 3
                saveCount = 0;
                apply.Invoke(null, new object[] { spec, config, (double[])MockCalib.Clone(), save });
                Check(saveCount == 0, "Start, the same calibration again: the configuration is not rewritten");

                // Case 4
                object spec4 = newSpectrum();
                double[] before4 = coeffs(spec4);
                saveCount = 0;
                apply.Invoke(null, new object[] { spec4, config, new[] { 3.65, double.NaN, 4.5e-05, 0.0, 0.0 }, save });
                Check(SameOrBothNull(coeffs(spec4), before4) && saveCount == 0, "Start, a gap in the board coefficients: nothing is taken");
            }
            catch (Exception ex)
            {
                Check(false, "start calibration test threw " + ex);
            }

            return true;
        }
    }
}
