// the board's reply compression level in the form (GET/POST /api/boot-config gzip_level) and the compression shown in the REST test.
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static int GzRead(Type bc, AtomSpectraRestClient c)
        {
            return (int)bc.GetMethod("ReadGzipLevel").Invoke(null, new object[] { c, 3000 });
        }

        static bool GzWrite(Type bc, AtomSpectraRestClient c, int level, out string message)
        {
            object[] a = { c, level, 3000, null };
            bool ok = (bool)bc.GetMethod("WriteGzipLevel").Invoke(null, a);
            message = (string)a[3];
            return ok;
        }

        static int BootPosts()
        {
            return Requests().Count(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/boot-config");
        }

        static bool RunGzip(string scenario)
        {
            if (scenario != "gzip-read" && scenario != "gzip-unsupported" && scenario != "gzip-write" && scenario != "gzip-okfalse" && scenario != "gzip-ignored" && scenario != "form-gzip" && scenario != "form-gzip-unsupported")
            {
                return false;
            }

            if (scenario == "form-gzip" || scenario == "form-gzip-unsupported")
            {
                return RunFormGzip(scenario == "form-gzip-unsupported");
            }

            Type bc = typeof(AtomSpectraRestClient).Assembly.GetType("BecquerelMonitor.AtomSpectraRestBootConfig");
            if (bc == null)
            {
                Check(false, "no AtomSpectraRestBootConfig in this build");
                return true;
            }

            var c = new AtomSpectraRestClient("http://127.0.0.1:" + port + "/");
            string msg;

            if (scenario == "gzip-read")
            {
                int v = GzRead(bc, c);
                Console.WriteLine("read " + v);
                Check(v == 6, "gzip_level 6 is read from GET /api/boot-config");
                Check(Posts() == 0, "reading sends no POST");
            }
            else if (scenario == "gzip-unsupported")
            {
                int v = GzRead(bc, c);
                Check(v == -1, "no gzip_level field: not supported (-1)");
                bool ok = GzWrite(bc, c, 6, out msg);
                Console.WriteLine("write: " + ok + " " + msg);
                Check(!ok && BootPosts() == 0, "not supported: nothing is POSTed (the release would answer ok and change nothing)");
                Check(msg != null && (msg.Contains("not supported") || msg.Contains("не поддерживается")), "not supported: the reason is named (a resource: en or ru)");
            }
            else if (scenario == "gzip-write")
            {
                bool ok = GzWrite(bc, c, 6, out msg);
                Console.WriteLine("write 6: " + ok + " " + msg);
                var post = Requests().LastOrDefault(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/boot-config");
                Check(ok, "level 6 is written and read back");
                Check(post != null && Str(post, "csrf").Length > 0 && Str(post, "body").Replace(" ", "") == "{\"gzip_level\":6}", "POST /api/boot-config {\"gzip_level\":6} with the CSRF token");
                Check(GzRead(bc, c) == 6, "GET /api/boot-config shows 6 afterwards");
                int before = BootPosts();
                bool bad = GzWrite(bc, c, 3, out msg);
                Check(!bad && BootPosts() == before, "level 3 is refused before any POST");
            }
            else if (scenario == "gzip-ignored")   // the board says {"ok":true} but keeps 0: only the read-back shows it
            {
                bool ok = GzWrite(bc, c, 6, out msg);
                Console.WriteLine("write 6: " + ok + " " + msg);
                Check(!ok && BootPosts() == 1 && msg != null && (msg.Contains("instead of 6") || msg.Contains("вместо 6")), "ok without the new value is a failure (read back 0 instead of 6)");
            }
            else if (scenario == "gzip-okfalse")
            {
                bool ok = GzWrite(bc, c, 1, out msg);
                Console.WriteLine("write 1: " + ok + " " + msg);
                Check(!ok && msg != null && msg.Contains("ok\":false"), "{\"ok\":false} is a failure, whatever the HTTP code");
                Check(GzRead(bc, c) == 0, "the level stays 0");
            }

            return true;
        }

        static bool RunFormGzip(bool unsupported)
        {
            Type t = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
            if (t == null)
            {
                Console.WriteLine("n/a: the settings form is not in this build");
                return true;
            }

            Thread sta = new Thread(() =>
            {
                try
                {
                    var f = BindingFlags.NonPublic | BindingFlags.Instance;
                    var host = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new Size(490, 599) };
                    var form = (Control)Activator.CreateInstance(t);
                    host.Controls.Add(form);
                    var cfg = new AtomSpectraTcpDeviceConfig();
                    cfg.Transport = AtomSpectraTransport.Rest;
                    cfg.RestUrl = "http://127.0.0.1:" + port + "/";
                    t.GetMethod("LoadFormContents").Invoke(form, new object[] { cfg });
                    host.Show();
                    Application.DoEvents();

                    Func<string, Control> c = n =>
                    {
                        FieldInfo fi = t.GetField(n, f);
                        return fi == null ? null : (Control)fi.GetValue(form);
                    };

                    if (c("comboGzip") == null || c("buttonGzipApply") == null || c("labelGzipState") == null)
                    {
                        Check(false, "the form has no compression block (comboGzip, buttonGzipApply, labelGzipState)");
                        host.Close();
                        host.Dispose();
                        return;
                    }

                    var combo = (ComboBox)c("comboGzip");
                    var apply = c("buttonGzipApply");
                    var state = c("labelGzipState");

                    FieldInfo confirm = t.GetField("ConfirmGzip", BindingFlags.Public | BindingFlags.Static);
                    int asked = 0;
                    bool answer = false;
                    confirm.SetValue(null, new Func<string, bool>(text =>
                    {
                        asked++;
                        Console.WriteLine("confirm: " + text.Replace("\r\n", " / ").Replace("\n", " / "));
                        return answer;
                    }));

                    t.GetMethod("buttonRestTest_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                    Pump(() => c("buttonRestTest").Enabled, 10000);
                    Application.DoEvents();
                    Console.WriteLine("test: " + c("labelRestResult").Text + " | gzip " + combo.SelectedIndex + " enabled " + combo.Enabled + "/" + apply.Enabled + " | " + state.Text);

                    if (unsupported)
                    {
                        Check(!combo.Enabled && !apply.Enabled && state.Text.Length > 0, "no gzip_level: the block is inactive and says why");
                        int p0 = BootPosts();
                        answer = true;
                        t.GetMethod("buttonGzipApply_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                        Pump(() => false, 1500);
                        Check(BootPosts() == p0 && asked == 0, "no gzip_level: Apply sends nothing and asks nothing");
                    }
                    else
                    {
                        Check(combo.SelectedIndex == 1 && combo.Enabled && apply.Enabled, "level 1 is shown and the block is active");
                        Check(c("labelRestResult").Text.Contains("gzip"), "Test connection (REST) names the reply compression (gzip)");
                        Check(AtomSpectraTcpLog.Snapshot().Any(l => l.Text.Contains("/api/spectrum.json") && l.Text.Contains("compressed: gzip") && l.Text.Contains("body")), "the log shows the size on the wire and the body size of a compressed reply");
                        ((CheckBox)c("checkObserve")).Checked = true;
                        Application.DoEvents();
                        Check(!apply.Enabled, "Observe only: Apply is blocked");
                        ((CheckBox)c("checkObserve")).Checked = false;
                        Application.DoEvents();

                        int p0 = Posts();
                        combo.SelectedIndex = 2;
                        answer = false;
                        t.GetMethod("buttonGzipApply_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                        Pump(() => false, 800);
                        Check(asked == 1 && Posts() == p0, "Apply asks first; \"No\" sends nothing");

                        answer = true;
                        t.GetMethod("buttonGzipApply_Click", f).Invoke(form, new object[] { null, EventArgs.Empty });
                        Pump(() => BootPosts() > 0 && apply.Enabled, 8000);
                        Application.DoEvents();
                        Console.WriteLine("applied: " + c("textOutput").Text.Replace("\r\n", " / "));
                        Check(asked == 2 && BootPosts() == 1, "\"Yes\" sends exactly one POST /api/boot-config");
                        Check(GzRead(t.Assembly.GetType("BecquerelMonitor.AtomSpectraRestBootConfig"), new AtomSpectraRestClient("http://127.0.0.1:" + port + "/")) == 6, "the board now has level 6");
                        Check(AtomSpectraTcpLog.Snapshot().Any(l => l.Source == "Gzip" && l.Text.Contains("6") && c("textOutput").Text.Contains(l.Text)), "the output window says what was set (the same line is in the log)");
                    }

                    host.Close();
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    Check(false, "gzip form test threw " + ex);
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            return true;
        }
    }
}
