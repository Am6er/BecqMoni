// PR #37 fixes of the UI lane (settings page, console, log window, DeviceConfigForm):
// ui-console-classify  - which console commands change the board, the output limits, the problem texts (both builds);
// ui-console-confirm   - a command that changes the board is sent only after "Yes"; Observe only holds in TCP mode too;
// ui-page-generations  - one background operation does not leave another one's button grey; another configuration forgets
//                        the board's compression level and drops late answers; the compression question names the board;
// ui-read-edited       - "Read from device" takes the address from the page fields, and its answer reaches the same profile
//                        after the form made a fresh clone of it;
// ui-save-refusal      - a page that refused to save and said why marks the refusal as reported (no second window).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        const BindingFlags UiNf = BindingFlags.NonPublic | BindingFlags.Instance;

        static bool RunUiFix(string scenario)
        {
            switch (scenario)
            {
                case "ui-console-classify": UiClassify(); return true;
                case "ui-console-confirm": UiSta(false, UiConsoleConfirm); return true;
                case "ui-page-generations": UiSta(false, UiPageGenerations); return true;
                case "ui-read-edited": UiSta(true, UiReadEdited); return true;
                case "ui-save-refusal": UiSta(true, UiSaveRefusal); return true;
                default: return false;
            }
        }

        static Type UiPageT { get { return Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor"); } }
        static Type UiFormT { get { return Type.GetType("BecquerelMonitor.DeviceConfigForm, BecquerelMonitor"); } }

        // the form scenarios run on an STA thread of their own, at most 60 s (a dialog waiting for a person ends with the process)
        static void UiSta(bool needsConfigForm, Action body)
        {
            if (UiPageT == null || (needsConfigForm && (UiFormT == null || CfgT == null)))
            {
                Console.WriteLine("n/a: the settings forms are not in this build (source build without WinForms)");
                return;
            }
            var th = new Thread(() => { try { body(); } catch (Exception ex) { Check(false, "form test threw " + ex); } });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            if (!th.Join(60000)) Check(false, "the form test did not finish in 60 s (a dialog waiting for a person?)");
        }

        static Control UiCtl(object owner, string name)
        {
            FieldInfo fi = owner.GetType().GetField(name, UiNf);
            return fi == null ? null : (Control)fi.GetValue(owner);
        }

        // a page on a borderless host off the screen, like the device tab of DeviceConfigForm
        static Control UiPage(out Form host, AtomSpectraTcpDeviceConfig cfg)
        {
            host = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new Size(490, 599) };
            var page = (Control)Activator.CreateInstance(UiPageT);
            host.Controls.Add(page);
            UiPageT.GetMethod("LoadFormContents").Invoke(page, new object[] { cfg });
            host.Show();
            Application.DoEvents();
            return page;
        }

        static AtomSpectraTcpDeviceConfig UiMockCfg()
        {
            return new AtomSpectraTcpDeviceConfig { Transport = AtomSpectraTransport.Rest, RestUrl = "http://127.0.0.1:" + port + "/" };
        }

        static string UiOneLine(string s) { return (s ?? "").Replace("\r\n", " / ").Replace("\n", " / "); }

        // ---------------------------------------------------------------- both builds
        static void UiClassify()
        {
            Func<string, AtomSpectraCommandKind> k = AtomSpectraDeviceConsole.Classify;
            Check(k("-inf") == AtomSpectraCommandKind.Query && k("-INF") == AtomSpectraCommandKind.Query && k("-sho") == AtomSpectraCommandKind.Query, "-inf, -INF, -sho are queries");
            Check(k("-cal") == AtomSpectraCommandKind.Query, "-cal without values reads the calibration (a query)");
            Check(k("-cal 1 2 3") == AtomSpectraCommandKind.CalibrationWrite, "-cal with values writes the calibration");
            Check(k("-rst") == AtomSpectraCommandKind.Reset && k("-sto") == AtomSpectraCommandKind.Stop, "-rst resets, -sto stops");
            Check(k("-sta") == AtomSpectraCommandKind.Start && k("-sta 120") == AtomSpectraCommandKind.Start, "-sta and -sta 120 start");
            Check(k("-inf 5") == AtomSpectraCommandKind.Unknown && k("-dis 30") == AtomSpectraCommandKind.Unknown && k("") == AtomSpectraCommandKind.Unknown, "anything else is unknown (asked about)");
            Check(AtomSpectraDeviceConsole.EffectText(AtomSpectraCommandKind.Query) == null && !string.IsNullOrEmpty(AtomSpectraDeviceConsole.EffectText(AtomSpectraCommandKind.Reset)), "a query has no effect text, a reset has one");

            string text = string.Concat(Enumerable.Range(0, 3000).Select(i => "line " + i + "\r\n"));
            string head = AtomSpectraDeviceConsole.ClipHead(text, 1 << 20, 2000);
            Check(head.StartsWith("line 0\r\n") && head.EndsWith("line 1999\r\n"), "ClipHead keeps the first 2000 lines (the console writes on top)");
            string tail = AtomSpectraDeviceConsole.ClipTail(text, 1 << 20, 2000);
            Check(tail.StartsWith("line 1000\r\n") && tail.EndsWith("line 2999\r\n"), "ClipTail keeps the last 2000 lines (the board log appends)");
            string h2 = AtomSpectraDeviceConsole.ClipHead(text, 1000, 100000), t2 = AtomSpectraDeviceConsole.ClipTail(text, 1000, 100000);
            Check(h2.Length <= 1000 && h2.EndsWith("\r\n") && t2.Length <= 1000 && t2.StartsWith("line ") && t2.EndsWith("line 2999\r\n"), "the character limit cuts whole lines");
            Check(AtomSpectraDeviceConsole.ClipHead("short\r\n", 1000, 10) == "short\r\n" && AtomSpectraDeviceConsole.ClipTail("short\r\n", 1000, 10) == "short\r\n", "a short text is left as it is");

            string p0 = AtomSpectraDeviceConsole.Problem(new AtomSpectraRestResponse { Status = 0, Error = "Unable to connect to the remote server" });
            string p404 = AtomSpectraDeviceConsole.Problem(new AtomSpectraRestResponse { Status = 404, Error = "HTTP 404" });
            string pb = AtomSpectraDeviceConsole.Problem(new AtomSpectraRestResponse { Status = 200, BodyBroken = true, Error = "gzip trailer missing" });
            Console.WriteLine("problems: " + p0 + " | " + p404 + " | " + pb);
            Check(p0.Length > 0 && !p0.Contains("Unable to connect"), "no answer: a text of the program, not the framework's words");
            Check(p404.Contains("404") && p404 != "HTTP 404", "an HTTP status is named inside a text of the program");
            Check(pb.Length > 0 && !pb.Contains("gzip trailer") && pb != p404, "a broken body has its own text");
        }

        // ---------------------------------------------------------------- U1
        static void UiConsoleConfirm()
        {
            Type t = UiPageT;
            Form host;
            Control page = UiPage(out host, UiMockCfg());
            FieldInfo hook = t.GetField("ConfirmCommand", BindingFlags.Public | BindingFlags.Static);
            Check(hook != null, "the page has the ConfirmCommand hook (the question before a command that changes the board)");
            if (hook == null) { host.Close(); host.Dispose(); return; }

            object saved = hook.GetValue(null);
            var asked = new List<string>();
            bool answer = false;
            hook.SetValue(null, new Func<string, bool>(q => { asked.Add(q); Console.WriteLine("confirm: " + UiOneLine(q)); return answer; }));
            try
            {
                var output = (TextBox)UiCtl(page, "textOutput");
                Action<string> enter = cmd =>
                {
                    var tb = (TextBox)UiCtl(page, "textCommand");
                    tb.Text = cmd;
                    t.GetMethod("textCommand_KeyDown", UiNf).Invoke(page, new object[] { tb, new KeyEventArgs(Keys.Return) });
                };
                Func<string, int> sent = body => Requests().Count(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/command" && Str(r, "body").Trim() == body);

                enter("-inf");
                Pump(() => sent("-inf") == 1 && output.Text.Contains(">> -inf"), 10000);
                Check(asked.Count == 0 && sent("-inf") == 1, "-inf (a query) is sent without a question");

                enter("-rst");
                Pump(() => false, 800);
                Check(asked.Count == 1 && sent("-rst") == 0, "-rst asks first; \"No\" (the default) sends nothing");
                string q0 = asked.Count > 0 ? asked[0] : "";
                Check(q0.Contains("-rst") && q0.Contains("127.0.0.1:" + port), "the question names the command and the board");
                Check(output.Text.StartsWith(">> -rst"), "the output says the command was not sent");

                answer = true;
                enter("-sta 60");
                Pump(() => sent("-sta 60") == 1, 8000);
                Check(asked.Count == 2 && sent("-sta 60") == 1, "\"Yes\" sends the command (-sta 60)");

                answer = false;
                enter("-xyz 5");
                Pump(() => false, 500);
                Check(asked.Count == 3 && sent("-xyz 5") == 0, "an unknown command is asked about too");

                // Observe only, set in REST mode, holds in TCP mode: the command is refused at once, no question, no connection
                ((CheckBox)UiCtl(page, "checkObserve")).Checked = true;
                ((RadioButton)UiCtl(page, "radioTcp")).Checked = true;
                UiCtl(page, "textAddress").Text = "127.0.0.1:1";
                Application.DoEvents();
                int before = asked.Count;
                enter("-rst");
                string top = output.Text.Substring(0, Math.Min(300, output.Text.Length));
                Console.WriteLine("observe, TCP: " + UiOneLine(top));
                Check(asked.Count == before && top.StartsWith(">> -rst") && (top.Contains("Observe only") || top.Contains("только наблюдение")), "Observe only in TCP mode: refused on the spot, no question");
            }
            finally
            {
                hook.SetValue(null, saved);
                host.Close();
                host.Dispose();
            }
        }

        // ---------------------------------------------------------------- U2, U3
        static void UiPageGenerations()
        {
            Type t = UiPageT;
            Form host;
            Control page = UiPage(out host, UiMockCfg());
            var savedResolve = AtomSpectraDeviceConsole.Resolve;
            FieldInfo confirm = t.GetField("ConfirmGzip", BindingFlags.Public | BindingFlags.Static);
            object savedConfirm = confirm.GetValue(null);
            try
            {
                AtomSpectraDeviceConsole.Resolve = n => { if (n == "atomspectra.local") return new[] { IPAddress.Loopback }; throw new SocketException(11001); };
                t.GetField("restAddressPort", UiNf).SetValue(page, port);

                // a REST test and a Refresh started together: each one gives its own button back
                t.GetMethod("buttonRestTest_Click", UiNf).Invoke(page, new object[] { null, EventArgs.Empty });
                t.GetMethod("buttonRefresh_Click", UiNf).Invoke(page, new object[] { null, EventArgs.Empty });
                bool both = Pump(() => UiCtl(page, "buttonRestTest").Enabled && UiCtl(page, "buttonRefresh").Enabled, 15000);
                Console.WriteLine("test: " + UiOneLine(UiCtl(page, "labelRestResult").Text) + " | refresh: " + UiCtl(page, "labelStatus").Text);
                Check(both, "Test connection (REST) and Refresh at once: both buttons are enabled again");

                var combo = (ComboBox)UiCtl(page, "comboGzip");
                Pump(() => combo.Enabled, 3000);
                Check(combo.Enabled, "the level read from the board (gzip_level 1) opens the compression block");

                // the question names the board the setting would go to
                string question = null;
                confirm.SetValue(null, new Func<string, bool>(q => { question = q; return false; }));
                combo.SelectedIndex = 2;
                t.GetMethod("buttonGzipApply_Click", UiNf).Invoke(page, new object[] { null, EventArgs.Empty });
                Console.WriteLine("gzip question: " + UiOneLine(question));
                Check(question != null && question.Contains("127.0.0.1:" + port), "the compression question names the board");

                // another configuration: the compression level of the previous board is forgotten
                var other = new AtomSpectraTcpDeviceConfig { Transport = AtomSpectraTransport.Rest, RestUrl = "http://127.0.0.1:1/" };
                t.GetMethod("LoadFormContents").Invoke(page, new object[] { other });
                Application.DoEvents();
                Check(!combo.Enabled && !UiCtl(page, "buttonGzipApply").Enabled, "another configuration: the block is closed until its own board is read");

                // a test of the mock board still on its way when another configuration is loaded: its answer does not reach the page
                t.GetMethod("LoadFormContents").Invoke(page, new object[] { UiMockCfg() });
                t.GetMethod("buttonRestTest_Click", UiNf).Invoke(page, new object[] { null, EventArgs.Empty });
                t.GetMethod("LoadFormContents").Invoke(page, new object[] { other });
                Pump(() => false, 3000);
                string late = UiCtl(page, "labelRestResult").Text;
                Console.WriteLine("late answer: '" + UiOneLine(late) + "'");
                Check(late.Length == 0 && !combo.Enabled, "a late answer of the previous configuration does not reach the page");
            }
            finally
            {
                AtomSpectraDeviceConsole.Resolve = savedResolve;
                confirm.SetValue(null, savedConfirm);
                host.Close();
                host.Dispose();
            }
        }

        // ---------------------------------------------------------------- DeviceConfigForm with the AtomSpectra page
        static Form UiConfigForm(object info)
        {
            Type.GetType("BecquerelMonitor.DeviceType, BecquerelMonitor").GetMethod("InitializeDeviceTypes").Invoke(null, null);
            Type.GetType("BecquerelMonitor.ThermometerType, BecquerelMonitor").GetMethod("InitializeThermometerTypes").Invoke(null, null);
            var form = (Form)Activator.CreateInstance(UiFormT);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3000, -3000);
            form.ShowInTaskbar = false;
            form.Show();
            UiFormT.GetProperty("ActiveDeviceConfig").SetValue(form, info);
            // what selecting the row does: the fields of the form and the device page are filled from the profile
            UiFormT.GetMethod("LoadFormContents", UiNf, null, new[] { CfgT }, null).Invoke(form, new[] { info });
            Application.DoEvents();
            return form;
        }

        static object UiInfo(AtomSpectraTcpDeviceConfig input)
        {
            object info = Activator.CreateInstance(CfgT);
            CfgT.GetProperty("Guid").SetValue(info, Guid.NewGuid().ToString());
            CfgT.GetProperty("Name").SetValue(info, "ui-fix");
            CfgT.GetProperty("DeviceType").SetValue(info, AtomSpectraTcpDeviceConfig.DeviceTypeId);
            CfgT.GetProperty("NumberOfChannels").SetValue(info, 8192);
            CfgT.GetProperty("InputDeviceConfig").SetValue(info, input);
            return info;
        }

        static double[] UiFields(Form form)
        {
            double[] got = new double[5];
            string[] names = { "numericUpDown7", "numericUpDown2", "numericUpDown1", "numericUpDown9", "numericUpDown8" };
            for (int i = 0; i < 5; i++) double.TryParse(UiCtl(form, names[i]).Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out got[i]);
            return got;
        }

        // ---------------------------------------------------------------- U11
        static void UiReadEdited()
        {
            for (int swap = 0; swap < 2; swap++)
            {
                // the profile as saved: the TCP bridge, no host yet (a new profile before its first Save)
                var savedInput = new AtomSpectraTcpDeviceConfig { Transport = AtomSpectraTransport.TcpBridge };
                object info = UiInfo(savedInput);
                Form form = UiConfigForm(info);
                try
                {
                    object page = UiFormT.GetField("inputDeviceForm", UiNf).GetValue(form);
                    Check(page != null && page.GetType() == UiPageT, "the device page of the profile is the AtomSpectra page");
                    if (page == null || page.GetType() != UiPageT) return;
                    // the person switches the page to REST and types the board URL; nothing is saved
                    ((RadioButton)UiCtl(page, "radioRest")).Checked = true;
                    UiCtl(page, "textRestUrl").Text = "http://127.0.0.1:" + port + "/";
                    Application.DoEvents();
                    CfgT.GetProperty("Dirty").SetValue(info, false);

                    UiFormT.GetMethod("button13_Click", UiNf).Invoke(form, new object[] { null, EventArgs.Empty });
                    object active = info;
                    if (swap == 1)
                    {
                        // the form takes a fresh clone of the same profile meanwhile (a switch of rows and back): the same Guid
                        active = CfgT.GetMethod("Clone").Invoke(info, null);
                        UiFormT.GetProperty("ActiveDeviceConfig").SetValue(form, active);
                    }
                    bool done = Pump(() => (bool)CfgT.GetProperty("Dirty").GetValue(active), 10000);
                    double[] got = UiFields(form);
                    string what = swap == 0 ? "the address of the page fields (REST, not saved)" : "a fresh clone of the profile (same Guid)";
                    Console.WriteLine(what + ": fields " + Dbl(got));
                    Check(done && Near(got, MockCalib), "Read from device, " + what + ": the board calibration reaches the fields");
                    Check(savedInput.Transport == AtomSpectraTransport.TcpBridge && string.IsNullOrEmpty(savedInput.Host) && string.IsNullOrEmpty(savedInput.RestUrl), "the saved profile is not changed by the read");
                }
                finally
                {
                    form.Dispose();   // not Close: FormClosing would ask "save the changes?" and wait for a person
                }
            }
        }

        // ---------------------------------------------------------------- U5
        static void UiSaveRefusal()
        {
            Type t = UiPageT;
            FieldInfo hook = t.GetField("ShowWarning", BindingFlags.Public | BindingFlags.Static);
            object savedHook = hook.GetValue(null);
            var warnings = new List<string>();
            hook.SetValue(null, new Func<string, DialogResult>(s => { warnings.Add(s); Console.WriteLine("warning: " + UiOneLine(s)); return DialogResult.OK; }));
            object info = UiInfo(UiMockCfg());
            Form form = UiConfigForm(info);
            try
            {
                object page = UiFormT.GetField("inputDeviceForm", UiNf).GetValue(form);
                MethodInfo save = UiFormT.GetMethod("SaveFormContents", UiNf, null, new[] { CfgT }, null);
                FieldInfo reported = UiFormT.GetField("saveRefusalReported", UiNf);

                // positive control: with a valid page the whole method goes through
                bool ok = (bool)save.Invoke(form, new[] { info });
                Check(ok && warnings.Count == 0, "positive control: a valid page saves, no warning");

                ((RadioButton)UiCtl(page, "radioRest")).Checked = true;
                UiCtl(page, "textRestUrl").Text = "ftp://192.0.2.19/";
                bool ok2 = (bool)save.Invoke(form, new[] { info });
                bool said = (bool)reported.GetValue(form);
                Console.WriteLine("invalid URL: saved " + ok2 + ", warnings " + warnings.Count + ", reported " + said);
                Check(!ok2 && warnings.Count == 1, "an invalid board URL is refused with one warning of the page");
                Check(said, "the refusal is marked as reported: the form adds no \"Invalid input form\" window");
            }
            finally
            {
                hook.SetValue(null, savedHook);
                form.Dispose();
            }
        }
    }
}
