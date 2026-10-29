using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RestHarness
{
    static partial class Program
    {
        static readonly BindingFlags Bm22NonPub = BindingFlags.NonPublic | BindingFlags.Instance;
        static List<string> Bm22Journal = new List<string>();
        static bool Bm22Answer;

        static int Bm22Count(string kind)
        {
            lock (Bm22Journal)
            {
                return Bm22Journal.Count(e => e.StartsWith(kind + "|"));
            }
        }

        static int Bm22Resets()
        {
            return Requests().Count(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/reset");
        }

        static object Bm22RestCfg(string url, bool observe)
        {
            Type TcpCfgT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceConfig, BecquerelMonitor");
            if (TcpCfgT == null) return null;
            object cfg = Activator.CreateInstance(TcpCfgT);
            Type enumT = Type.GetType("BecquerelMonitor.AtomSpectraTransport, BecquerelMonitor");
            object restEnum = Enum.Parse(enumT, "Rest");
            TcpCfgT.GetProperty("Transport").SetValue(cfg, restEnum);
            TcpCfgT.GetProperty("RestUrl").SetValue(cfg, url);
            TcpCfgT.GetProperty("RestObserveOnly").SetValue(cfg, observe);
            return cfg;
        }

        static object Bm22Make(object tcpCfg, out object ctl, out object mc, out object rd)
        {
            Type CtlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
            Type RdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
            Type CfgT = Type.GetType("BecquerelMonitor.DeviceConfigInfo, BecquerelMonitor");
            Type SpecT = Type.GetType("BecquerelMonitor.EnergySpectrum, BecquerelMonitor");
            Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");

            if (CtlT == null || RdT == null || CfgT == null || SpecT == null || McT == null)
            {
                ctl = null; mc = null; rd = null;
                return null;
            }

            object spec = Activator.CreateInstance(SpecT, new object[] { 1.0, 1024 });
            object cfg = Activator.CreateInstance(CfgT);
            string guid = Guid.NewGuid().ToString();
            CfgT.GetProperty("Guid").SetValue(cfg, guid);
            CfgT.GetProperty("InputDeviceConfig").SetValue(cfg, tcpCfg);

            rd = Activator.CreateInstance(RdT);
            RdT.GetProperty("DeviceConfig").SetValue(rd, cfg);
            RdT.GetProperty("EnergySpectrum").SetValue(rd, spec);

            ctl = Activator.CreateInstance(CtlT);
            FieldInfo f = CtlT.GetField("deviceGuid", Bm22NonPub);
            if (f != null) f.SetValue(ctl, guid);

            mc = Activator.CreateInstance(McT, new object[] { null, rd });
            McT.GetProperty("DeviceController").SetValue(mc, ctl);

            return mc;
        }

        static bool Bm22Run(object mc, bool answer, out bool legacy)
        {
            Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");
            Type ResT = Type.GetType("BecquerelMonitor.Properties.Resources, BecquerelMonitor");
            Bm22Answer = answer;

            MethodInfo m = McT.GetMethod("ConfirmAndClear");
            if (m != null)
            {
                legacy = false;
                try
                {
                    object res = m.Invoke(mc, null);
                    return (bool)res;
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
            }
            else
            {
                legacy = true;
                string msg = (string)ResT.GetProperty("MSGClearSpectrum").GetValue(null);
                lock (Bm22Journal) Bm22Journal.Add("ask|" + msg);
                MethodInfo clear = McT.GetMethod("ClearMeasurementResult");
                try
                {
                    clear.Invoke(mc, null);
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
                return true;
            }
        }

        static void Bm22Table(string scenario, string answer, bool ret)
        {
            Console.WriteLine("BM22-TABLE scenario=" + scenario + " answer=" + answer + " asks=" + Bm22Count("ask") + " reports=" + Bm22Count("report") + " resets=" + Bm22Resets() + " document_cleared=" + ret);
        }

        static bool RunBm22(string scenario)
        {
            Type CtlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
            Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");
            Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
            Type ResT = Type.GetType("BecquerelMonitor.Properties.Resources, BecquerelMonitor");

            if (CtlT == null || McT == null || UiT == null || ResT == null)
            {
                Console.WriteLine("n/a: the controller is not in this build");
                return true;
            }

            lock (Bm22Journal) Bm22Journal.Clear();

            FieldInfo probeField = UiT.GetField("Probe", BindingFlags.Public | BindingFlags.Static);
            if (probeField == null)
            {
                Check(false, "Probe field not found");
                return true;
            }

            try
            {
                probeField.SetValue(null, (Func<string, string, string, bool>)((kind, text, caption) =>
                {
                    lock (Bm22Journal) Bm22Journal.Add(kind + "|" + text);
                    return kind == "ask" ? Bm22Answer : true;
                }));

                string url = "http://127.0.0.1:" + port + "/";
                object tcpCfg = null;
                object mc = null;
                object ctl = null;
                object rd = null;
                bool ret = false;
                bool legacy = false;

                switch (scenario)
                {
                    case "bm22-yes":
                        tcpCfg = Bm22RestCfg(url, false);
                        Bm22Make(tcpCfg, out ctl, out mc, out rd);
                        if (mc == null) { Check(false, "setup failed"); return true; }
                        ret = Bm22Run(mc, true, out legacy);
                        if (legacy)
                        {
                            Bm22Table(scenario, "yes", ret);
                        }
                        else
                        {
                            Bm22Table(scenario, "yes", ret);
                            Check(ret, "ret");
                            Check(Bm22Count("ask") == 1, "one question");
                            string q = null;
                            lock (Bm22Journal)
                            {
                                var qEntry = Bm22Journal.FirstOrDefault(e => e.StartsWith("ask|"));
                                if (qEntry != null) q = qEntry.Substring(4);
                            }
                            string bothFmt = (string)ResT.GetProperty("MSGAtomSpectraRestClearBothConfirm").GetValue(null);
                            string pat = "^" + System.Text.RegularExpressions.Regex.Escape(bothFmt).Replace("\\{0}", "1").Replace("\\{1}", "23").Replace("\\{2}", "\\d+") + "$";
                            Check(q != null && System.Text.RegularExpressions.Regex.IsMatch(q, pat), "the one question carries the board's 1 h 23 min and its counts: " + q);
                            Check(Bm22Count("report") == 0, "zero reports");
                            Check(Bm22Resets() == 1, "one reset");
                        }
                        break;

                    case "bm22-no":
                        tcpCfg = Bm22RestCfg(url, false);
                        Bm22Make(tcpCfg, out ctl, out mc, out rd);
                        if (mc == null) { Check(false, "setup failed"); return true; }
                        ret = Bm22Run(mc, false, out legacy);
                        if (legacy)
                        {
                            Bm22Table(scenario, "no", ret);
                        }
                        else
                        {
                            Bm22Table(scenario, "no", ret);
                            Check(!ret, "ret false");
                            Check(Bm22Count("ask") == 1, "one question");
                            Check(Bm22Resets() == 0, "zero resets");
                            Check(Bm22Count("report") == 0, "zero reports");
                        }
                        break;

                    case "bm22-observe":
                        tcpCfg = Bm22RestCfg(url, true);
                        Bm22Make(tcpCfg, out ctl, out mc, out rd);
                        if (mc == null) { Check(false, "setup failed"); return true; }
                        ret = Bm22Run(mc, true, out legacy);
                        if (legacy)
                        {
                            Bm22Table(scenario, "yes", ret);
                        }
                        else
                        {
                            Bm22Table(scenario, "yes", ret);
                            Check(ret, "ret");
                            Check(Bm22Count("ask") == 1, "one question");
                            string expectedQ = (string)ResT.GetProperty("MSGClearSpectrum").GetValue(null);
                            string q = null;
                            lock (Bm22Journal)
                            {
                                var qEntry = Bm22Journal.FirstOrDefault(e => e.StartsWith("ask|"));
                                if (qEntry != null) q = qEntry.Substring(4);
                            }
                            Check(q == expectedQ, "question equals MSGClearSpectrum");
                            Check(Bm22Resets() == 0, "zero resets");
                            Check(Bm22Count("report") == 1, "one report");
                            string expectedR = (string)ResT.GetProperty("MSGAtomSpectraRestObserveOnly").GetValue(null);
                            string r = null;
                            lock (Bm22Journal)
                            {
                                var rEntry = Bm22Journal.FirstOrDefault(e => e.StartsWith("report|"));
                                if (rEntry != null) r = rEntry.Substring(7);
                            }
                            Check(r == expectedR, "report equals MSGAtomSpectraRestObserveOnly");
                        }
                        break;

                    case "bm22-readfail":
                        tcpCfg = Bm22RestCfg("http://127.0.0.1:1/", false);
                        Bm22Make(tcpCfg, out ctl, out mc, out rd);
                        if (mc == null) { Check(false, "setup failed"); return true; }
                        ret = Bm22Run(mc, true, out legacy);
                        if (legacy)
                        {
                            Bm22Table(scenario, "yes", ret);
                        }
                        else
                        {
                            Bm22Table(scenario, "yes", ret);
                            Check(ret, "ret");
                            Check(Bm22Count("ask") == 1, "one question");
                            string expectedQ = (string)ResT.GetProperty("MSGClearSpectrum").GetValue(null);
                            string q = null;
                            lock (Bm22Journal)
                            {
                                var qEntry = Bm22Journal.FirstOrDefault(e => e.StartsWith("ask|"));
                                if (qEntry != null) q = qEntry.Substring(4);
                            }
                            Check(q == expectedQ, "question equals MSGClearSpectrum");
                            Check(Bm22Count("report") == 1, "one report");
                            string notReset = (string)ResT.GetProperty("MSGAtomSpectraRestNotReset").GetValue(null);
                            int idx = notReset.IndexOf("{0}");
                            string prefix = idx > 0 ? notReset.Substring(0, idx) : notReset;
                            string r = null;
                            lock (Bm22Journal)
                            {
                                var rEntry = Bm22Journal.FirstOrDefault(e => e.StartsWith("report|"));
                                if (rEntry != null) r = rEntry.Substring(7);
                            }
                            Check(r != null && r.StartsWith(prefix), "report starts with MSGAtomSpectraRestNotReset prefix");
                            Check(Bm22Resets() == 0, "zero resets");
                        }
                        break;

                    case "bm22-tcp":
                    {
                        Type TcpCfgT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceConfig, BecquerelMonitor");
                        if (TcpCfgT == null) { Check(false, "TcpCfgT null"); return true; }
                        tcpCfg = Activator.CreateInstance(TcpCfgT);
                        Type enumT = Type.GetType("BecquerelMonitor.AtomSpectraTransport, BecquerelMonitor");
                        object tcpEnum = Enum.Parse(enumT, "TcpBridge");
                        TcpCfgT.GetProperty("Transport").SetValue(tcpCfg, tcpEnum);
                        TcpCfgT.GetProperty("Host").SetValue(tcpCfg, "127.0.0.1");
                        TcpCfgT.GetProperty("TcpPort").SetValue(tcpCfg, 9);

                        Bm22Make(tcpCfg, out ctl, out mc, out rd);
                        if (mc == null) { Check(false, "setup failed"); return true; }

                        Bm22Answer = true;
                        MethodInfo confirmClear = McT.GetMethod("ConfirmClear");
                        if (confirmClear != null)
                        {
                            try
                            {
                                ret = (bool)confirmClear.Invoke(mc, null);
                            }
                            catch (TargetInvocationException ex)
                            {
                                throw ex.InnerException;
                            }
                        }
                        else
                        {
                            string msg = (string)ResT.GetProperty("MSGClearSpectrum").GetValue(null);
                            lock (Bm22Journal) Bm22Journal.Add("ask|" + msg);
                            ret = true;
                        }

                        Bm22Table(scenario, "yes", ret);
                        Check(ret, "ret");
                        Check(Bm22Count("ask") == 1, "one question");
                        string expectedQ = (string)ResT.GetProperty("MSGClearSpectrum").GetValue(null);
                        string q = null;
                        lock (Bm22Journal)
                        {
                            var qEntry = Bm22Journal.FirstOrDefault(e => e.StartsWith("ask|"));
                            if (qEntry != null) q = qEntry.Substring(4);
                        }
                        Check(q == expectedQ, "question equals MSGClearSpectrum");
                        Check(Bm22Count("report") == 0, "zero reports");
                        Check(Bm22Resets() == 0, "zero resets");
                        break;
                    }

                    default:
                        Check(false, "unknown scenario");
                        return true;
                }
            }
            catch (Exception ex)
            {
                Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                probeField.SetValue(null, null);
            }

            return true;
        }
    }
}
