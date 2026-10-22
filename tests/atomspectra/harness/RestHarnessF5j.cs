// TCP -cal and the form button at 4096/2048 channels), the length of the histogram before the calibration,
// Controller and form code are in the REAL build only (reflection); the source build prints "n/a".
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5j(string scenario)
        {
            Action body;
            switch (scenario)
            {
                case "f5-tcp-cal-n": body = F5jTcpCalN; break;
                case "f5-ctl-length-first": body = F5jLengthFirst; break;
                case "f5-form-cal-n": body = F5jFormCalN; break;
                case "f5-linklost-restored": body = F5jRestored; break;
                case "f5-linklost-count-reset": body = F5jCountReset; break;
                default: return RunF5k(scenario);   // RestHarnessF5k.cs
            }
            F5dWith(body);
            return true;
        }

        static Type CtlT { get { return Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor"); } }
        static Type RdT { get { return Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor"); } }
        static Type SpecT { get { return Type.GetType("BecquerelMonitor.EnergySpectrum, BecquerelMonitor"); } }
        static Type CfgT { get { return Type.GetType("BecquerelMonitor.DeviceConfigInfo, BecquerelMonitor"); } }
        static readonly BindingFlags NonPub = BindingFlags.NonPublic | BindingFlags.Instance;

        static object F5jSpec(int n)
        {
            try { return Activator.CreateInstance(SpecT, new object[] { 1.0, n }); }
            catch (MissingMethodException) { return Activator.CreateInstance(SpecT, new object[] { 1, n }); }
        }

        static object F5jConfig(int n)
        {
            object c = Activator.CreateInstance(CfgT);
            CfgT.GetProperty("NumberOfChannels").SetValue(c, n);
            return c;
        }

        static double[] F5jCoeffs(object owner)
        {
            object cal = owner.GetType().GetProperty("EnergyCalibration").GetValue(owner);
            return cal == null ? null : (double[])cal.GetType().GetProperty("Coefficients").GetValue(cal);
        }

        // the answer of -cal: 5 coefficients as 2 x 8 hex digits each, then the CRC32 of the ten lines (AtomSpectraTcpCalibrationReader.Parse)
        static string F5jCalText(double[] c)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (double d in c) { byte[] b = BitConverter.GetBytes(d).Reverse().ToArray(); string h = BitConverter.ToString(b).Replace("-", ""); lines.Add(h.Substring(0, 8)); lines.Add(h.Substring(8)); }
            Type crc = Type.GetType("BecquerelMonitor.Hash.Crc32, BecquerelMonitor");
            byte[] raw = Encoding.ASCII.GetBytes(string.Concat(lines));
            lines.Add(((uint)crc.GetMethod("Compute", new[] { typeof(byte[]) }).Invoke(null, new object[] { raw })).ToString("X8"));
            return string.Join("\r\n", lines) + "\r\n";
        }

        // a controller whose configuration "file" is a counter, with a document of n channels and a configuration of n channels
        static object F5jController(int n, out object rd, out object spec, out object cfg)
        {
            object ctl = Activator.CreateInstance(CtlT);
            rd = Activator.CreateInstance(RdT);
            spec = F5jSpec(n);
            cfg = F5jConfig(n);
            RdT.GetProperty("EnergySpectrum").SetValue(rd, spec);
            RdT.GetProperty("DeviceConfig").SetValue(rd, cfg);
            object status = RdT.GetProperty("ResultDataStatus").GetValue(rd);
            status.GetType().GetProperty("Recording").SetValue(status, true);
            Type funcT = typeof(Func<,>).MakeGenericType(CfgT, typeof(bool));
            CtlT.GetField("configSaver", NonPub).SetValue(ctl, Delegate.CreateDelegate(funcT, typeof(Program).GetMethod("CountSave", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(CfgT)));
            return ctl;
        }

        // the answer of -cal at Start. The scale of the instrument's polynomial at 4096/2048 channels is not established (not established):
        // the document and the configuration (and its file) keep their own. At 8192 channels the calibration is taken and saved once.
        static void F5jTcpCalN()
        {
            if (CtlT == null || RdT == null || SpecT == null || CfgT == null) { Console.WriteLine("n/a: the controller is not in this build"); return; }
            MethodInfo m = CtlT.GetMethod("RefreshCalibrationFromAnswer", NonPub);
            Check(m != null, "the controller takes the answer of -cal");
            if (m == null) return;
            string text = F5jCalText(MockCalib);
            foreach (int n in new[] { 4096, 2048, 16384, 6000, 8192 })   // and a size above 8192, and one that does not divide it
            {
                object rd, spec, cfg;
                object ctl = F5jController(n, out rd, out spec, out cfg);
                double[] bs = F5jCoeffs(spec), bc = F5jCoeffs(cfg);
                saveCount = 0;
                m.Invoke(ctl, new object[] { rd, text });
                Console.WriteLine(n + ": document " + (F5jCoeffs(spec) == null ? "-" : string.Join(";", F5jCoeffs(spec))) + ", saves " + saveCount);
                bool took = F5jCoeffs(spec) != null && F5jCoeffs(spec).SequenceEqual(MockCalib);
                if (n == 8192) Check(took && F5jCoeffs(cfg) != null && F5jCoeffs(cfg).SequenceEqual(MockCalib) && saveCount == 1, "8192 channels: the document and the configuration get -cal, saved once");
                else Check(SameOrBothNull(F5jCoeffs(spec), bs) && SameOrBothNull(F5jCoeffs(cfg), bc) && saveCount == 0, n + " channels: -cal is not taken (document, configuration, file untouched; saves " + saveCount + ")");
            }
            // a document without a configuration copy: N is the length of the spectrum, the same rule
            foreach (int n in new[] { 4096, 8192 })
            {
                object rd, spec, cfg;
                object ctl = F5jController(n, out rd, out spec, out cfg);
                RdT.GetProperty("DeviceConfig").SetValue(rd, null);
                saveCount = 0;
                m.Invoke(ctl, new object[] { rd, text });
                bool took = F5jCoeffs(spec) != null && F5jCoeffs(spec).SequenceEqual(MockCalib);
                Check(took == (n == 8192), n + " channels, no configuration copy in the document: -cal " + (took ? "taken" : "not taken") + " by the length of the spectrum");
            }
        }

        static bool F5jRecording(object rd)
        {
            object st = RdT.GetProperty("ResultDataStatus").GetValue(rd);
            return (bool)st.GetType().GetProperty("Recording").GetValue(st);
        }

        // the length of the histogram is checked BEFORE the board calibration is applied (a REST snapshot of another length
        // than the document must not rewrite the calibration and the configuration file before the Detach).
        static void F5jLengthFirst()
        {
            if (CtlT == null || RdT == null || SpecT == null || CfgT == null) { Console.WriteLine("n/a: the controller is not in this build"); return; }
            MethodInfo upd = CtlT.GetMethod("UpdateHistogram", NonPub);
            Check(upd != null, "the controller has UpdateHistogram");
            if (upd == null) return;
            foreach (int len in new[] { 8192, 4096 }) F5jLengthCase(upd, len);
        }

        static void F5jLengthCase(MethodInfo upd, int len)
        {
            object rd, spec, cfg;
            object ctl = F5jController(4096, out rd, out spec, out cfg);
            CtlT.GetField("resultData", NonPub).SetValue(ctl, rd);
            CtlT.GetField("session", NonPub).SetValue(ctl, new AtomSpectraRestSession(Client(), 1, false, 0));
            CtlT.GetField("boardCalibrationPending", NonPub).SetValue(ctl, true);
            double[] bs = F5jCoeffs(spec), bc = F5jCoeffs(cfg);
            var snap = new AtomSpectraRestDataReadyArgs(new int[len], 0, 15, 0, 0, double.NaN, 0, false, 0, (double[])MockCalib.Clone());
            snap.CalibChannels = 8192;
            saveCount = 0;
            upd.Invoke(ctl, new object[] { snap });
            Console.WriteLine("length " + len + " vs document 4096: saves " + saveCount + ", recording " + F5jRecording(rd));
            if (len == 4096)
            {   // the NUMBERS of the fold 8192 -> 4096 (Fold4096, computed beforehand), in the document and in the configuration
                Check(Near(F5jCoeffs(spec), Fold4096) && Near(F5jCoeffs(cfg), Fold4096), "matching length: the document and the configuration get the board polynomial folded to 4096 channels (" + Dbl(F5jCoeffs(spec)) + ")");
                Check(saveCount == 1 && F5jRecording(rd), "matching length: the configuration is saved once, the run goes on (saves " + saveCount + ")");
                return;
            }
            Check(SameOrBothNull(F5jCoeffs(spec), bs) && SameOrBothNull(F5jCoeffs(cfg), bc) && saveCount == 0, "another length: calibration of the document and the configuration untouched, file not saved (saves " + saveCount + ")");
            Check(!F5jRecording(rd), "another length: the run is detached (Recording false)");
        }

        // the button "read the calibration" of the device configuration form. TCP at 4096/2048 channels: the fields, the
        // coefficients and the dirty flag are untouched. REST: the 8192-scale calibration of the board folded to the profile's channels.
        static void F5jFormCalN()
        {
            Type rdr = Type.GetType("BecquerelMonitor.AtomSpectraTcpCalibrationReader, BecquerelMonitor");
            Type resT = Type.GetType("BecquerelMonitor.AtomSpectraTcpCalibrationResult, BecquerelMonitor");
            Type cfgT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceConfig, BecquerelMonitor");
            Type calT = Type.GetType("BecquerelMonitor.PolynomialEnergyCalibration, BecquerelMonitor");
            MethodInfo ap = rdr == null ? null : rdr.GetMethod("ApplyResult", BindingFlags.NonPublic | BindingFlags.Static);
            if (ap == null || resT == null || cfgT == null || calT == null) { Console.WriteLine("n/a: the form code is not in this build"); return; }
            Type failT = Type.GetType("BecquerelMonitor.AtomSpectraTcpCalibrationFailure, BecquerelMonitor");
            Type trT = Type.GetType("BecquerelMonitor.AtomSpectraTransport, BecquerelMonitor");
            object ok = Activator.CreateInstance(resT, new object[] { Enum.ToObject(failT, 0), null, (double[])MockCalib.Clone(), "" });
            foreach (string transport in new[] { "TcpBridge", "Rest" })
                foreach (int n in new[] { 4096, 2048, 8192 })
                    F5jFormCase(ap, ok, cfgT, calT, Enum.Parse(trT, transport), transport, n);
        }

        static void F5jFormCase(MethodInfo ap, object result, Type cfgT, Type calT, object transportValue, string transport, int n)
        {
            object config = Activator.CreateInstance(cfgT);
            cfgT.GetProperty("Transport").SetValue(config, transportValue);
            object cal = Activator.CreateInstance(calT);
            double[] before = (double[])calT.GetProperty("Coefficients").GetValue(cal);
            var fields = new Control[] { new TextBox(), new TextBox(), new TextBox(), new TextBox(), new TextBox() };
            foreach (Control f in fields) f.Text = "keep";
            int dirty = 0;
            object getCal = typeof(Program).GetMethod("F5jConst", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(calT).Invoke(null, new object[] { cal });
            ap.Invoke(null, new object[] { result, config, getCal, fields, (Action)(() => dirty++), (Func<int>)(() => n) });
            double[] after = (double[])calT.GetProperty("Coefficients").GetValue(cal);
            string tag = transport + " " + n + " channels: ";
            Console.WriteLine(tag + "field0=" + fields[0].Text + ", dirty=" + dirty + ", coefficients " + (after == null ? "-" : string.Join(";", after)));
            // the expectation is the NUMBERS (Fold4096/Fold2048, computed beforehand), not ForChannels, the function under test
            double[] want = transport == "Rest" && n == 4096 ? Fold4096 : transport == "Rest" && n == 2048 ? Fold2048 : (double[])MockCalib.Clone();
            if (transport == "TcpBridge" && n != 8192)
                Check(fields[0].Text == "keep" && dirty == 0 && SameOrBothNull(after, before), tag + "the button does not take -cal: fields, coefficients and the dirty flag untouched");
            else
                Check(Near(after, want) && dirty == 1, tag + "the calibration is taken (REST: folded to the channels of the profile): " + Dbl(want));
        }

        static Func<T> F5jConst<T>(T v) { return () => v; }
        // after the loss by answered failures, an answer that is again a failure does NOT restore the link (no flapping), the first
        // good snapshot does, once.
        static void F5jRestored()
        {
            int lost = 0, restored = 0, goodBeforeRestore = -1;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => lost++;
            a.LinkRestored += (o, e) => { restored++; goodBeforeRestore = a.Failures; };
            for (int i = 0; i < 16 && restored == 0; i++) a.PollOnce();
            Console.WriteLine("failures " + a.Failures + ", lost " + lost + ", restored " + restored + ", failures at the restore " + goodBeforeRestore);
            Check(lost == 1 && restored == 1, "lost once, restored once (" + lost + ", " + restored + ")");
            Check(goodBeforeRestore >= 3, "no restore while the board keeps failing (failures at the restore " + goodBeforeRestore + ")");
            Check(!a.IsLinkLost, "the link is in order after a good snapshot");
            a.Dispose();
        }
        // a good poll ends the run: failures split by good polls - never 3 in a row, the link is never lost
        static void F5jCountReset()
        {
            int lost = 0;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => lost++;
            for (int i = 0; i < 7; i++) a.PollOnce();
            Console.WriteLine("failures " + a.Failures + ", successes " + a.Successes + ", lost " + lost);
            Check(a.Failures >= 3 && a.Successes >= 2, "enough failures (not in a row) and good polls between them (failures " + a.Failures + ", successes " + a.Successes + ")");   // a repeat inside a period may take a case, so no exact count
            Check(lost == 0 && !a.IsLinkLost, "a good poll between the failures ends the run: no loss (events " + lost + ")");
            a.Dispose();
        }
        // ==END==
    }
}
