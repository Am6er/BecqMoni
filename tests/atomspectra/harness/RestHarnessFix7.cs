using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix7(string scenario)
        {
            switch (scenario)
            {
                case "bm18-chooser":
                {
                    Type t = typeof(BecquerelMonitor.AtomSpectraDeviceConsole).Assembly.GetType("BecquerelMonitor.AtomSpectraBoardChooser");
                    if (t == null)
                    {
                        Console.WriteLine("n/a: AtomSpectraBoardChooser is not in the source build");
                        return true;
                    }

                    MethodInfo m = t.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (m == null)
                    {
                        Check(false, "AtomSpectraBoardChooser.Build (window builder) exists");
                        return true;
                    }
                    Check(true, "AtomSpectraBoardChooser.Build (window builder) exists");

                    var list = new List<BecquerelMonitor.AtomSpectraBoardEntry>
                    {
                        new BecquerelMonitor.AtomSpectraBoardEntry
                        {
                            Address = "127.0.0.3",
                            HostName = "board-a",
                            Instance = "a",
                            Serial = "00000081",
                            Source = "atomspectra"
                        },
                        new BecquerelMonitor.AtomSpectraBoardEntry
                        {
                            Address = "127.0.0.4",
                            HostName = "board-b",
                            Instance = "b",
                            Serial = "00000051",
                            Source = "http"
                        }
                    };

                    object[] args = { list, null };
                    Form form = (Form)m.Invoke(null, args);
                    ListView lv = args[1] as ListView;

                    IntPtr handle = form.Handle;   // creates the handles of the window and its controls, as ShowDialog does (nothing is shown)
                    Check(handle != IntPtr.Zero, "the window handle is created (not shown)");
                    Check(lv != null, "ListView is not null");
                    if (lv == null)
                    {
                        form.Dispose();
                        return true;
                    }

                    foreach (Control child in form.Controls) { IntPtr ch = child.Handle; }   // every child handle, as on show (DrawToBitmap draws only created controls)
                    Check(lv.Handle != IntPtr.Zero, "the list handle is created (as on show)");
                    Check(form.Controls.Contains(lv), "the list is in form.Controls");
                    Check(lv.Items.Count == 2, "ListView has 2 items");
                    Check(lv.Columns.Count == 4, "ListView has 4 columns");
                    Check(lv.SelectedItems.Count == 1 && lv.SelectedItems[0].Index == 0, "First item is selected");

                    Button okButton = null;
                    Button cancelButton = null;
                    Label labelControl = null;

                    foreach (Control c in form.Controls)
                    {
                        if (c is Button b)
                        {
                            if (b.DialogResult == DialogResult.OK)
                                okButton = b;
                            else if (b.DialogResult == DialogResult.Cancel)
                                cancelButton = b;
                        }
                        else if (c is Label l)
                        {
                            labelControl = l;
                        }
                    }

                    Check(okButton != null, "OK button exists");
                    Check(cancelButton != null, "Cancel button exists");
                    Check(okButton != null && okButton.Enabled, "OK button is enabled");

                    if (okButton != null && cancelButton != null && labelControl != null)
                    {
                        Check(!lv.Bounds.IntersectsWith(okButton.Bounds), "ListView does not intersect OK button");
                        Check(!lv.Bounds.IntersectsWith(cancelButton.Bounds), "ListView does not intersect Cancel button");
                        Check(!lv.Bounds.IntersectsWith(labelControl.Bounds), "ListView does not intersect Label");
                    }

                    Check(form.ClientRectangle.Contains(lv.Bounds), "ListView is within form client area");
                    Check(form.ActiveControl == lv, "the focus is on the list (arrows choose a board, Enter is OK), not on OK");
                    Type resT = t.Assembly.GetType("BecquerelMonitor.Properties.Resources");
                    PropertyInfo okRes = resT == null ? null : resT.GetProperty("AtomSpectraBoardChoiceOk", BindingFlags.Public | BindingFlags.Static);
                    Check(okButton != null && okRes != null && okButton.Text == (string)okRes.GetValue(null), "the OK caption comes from the resources (AtomSpectraBoardChoiceOk)");

                    try
                    {
                        using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                            bmp.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chooser_check.png"), ImageFormat.Png);
                        }
                        Check(true, "the window is rendered to chooser_check.png");
                    }
                    catch (Exception ex)
                    {
                        Check(false, "DrawToBitmap: " + ex.Message);
                    }

                    form.Dispose();

                    // boards that answered without an address: the prompt says how many, in a label that holds it, above the list
                    Type tl = t.Assembly.GetType("BecquerelMonitor.AtomSpectraBoardList");
                    Check(tl != null, "AtomSpectraBoardList (the boards and the count of those without an address) exists");
                    if (tl == null || resT == null) return true;
                    var bl = (System.Collections.IList)Activator.CreateInstance(tl);
                    bl.Add(list[0]);
                    tl.GetField("Incomplete").SetValue(bl, 2);
                    object[] args2 = { bl, null };
                    Form form2 = (Form)m.Invoke(null, args2);
                    ListView lv2 = (ListView)args2[1];
                    IntPtr h2 = form2.Handle;
                    foreach (Control child in form2.Controls) { IntPtr ch = child.Handle; }
                    Label prompt2 = form2.Controls.OfType<Label>().FirstOrDefault();
                    Check(prompt2 != null && prompt2.Text.Contains("2") && prompt2.Text.Contains(Environment.NewLine), "two boards without an address: the prompt says so (" + (prompt2 == null ? "no label" : prompt2.Text.Replace(Environment.NewLine, " / ")) + ")");
                    Check(prompt2 != null && !lv2.Bounds.IntersectsWith(prompt2.Bounds) && form2.ClientRectangle.Contains(lv2.Bounds), "the longer prompt does not cover the list");
                    var rm = (System.Resources.ResourceManager)resT.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                    foreach (string culture in new[] { "en-US", "ru-RU" })
                    {
                        var ci = new CultureInfo(culture);
                        string text = rm.GetString("AtomSpectraBoardChoicePrompt", ci) + Environment.NewLine + string.Format(CultureInfo.InvariantCulture, rm.GetString("AtomSpectraBoardChoiceIncomplete", ci), 2);
                        Size need = TextRenderer.MeasureText(text, prompt2.Font, new Size(prompt2.Width, int.MaxValue), TextFormatFlags.WordBreak);
                        Check(prompt2 != null && need.Height <= prompt2.Height, culture + ": the prompt with the count fits its label (" + need.Height + " of " + prompt2.Height + " px)");
                    }
                    form2.Dispose();
                    return true;
                }
                case "bm18-choose-stale":
                    UiSta(false, ChooseStale);
                    return true;
                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }

        // The choice window comes only for the search still current on the page, and not over another window of the settings form.
        static void ChooseStale()
        {
            Type t = UiPageT;
            FieldInfo hook = t.GetField("ChooseWindow", BindingFlags.Public | BindingFlags.Static);
            Check(hook != null, "the page has the ChooseWindow hook (the board choice window)");
            if (hook == null) return;

            Form host;
            Control page = UiPage(out host, UiMockCfg());
            object savedHook = hook.GetValue(null);
            var savedBrowse = AtomSpectraBoardFinder.Browse;
            int shown = 0;
            ManualResetEvent gate = new ManualResetEvent(true);
            try
            {
                hook.SetValue(null, new Func<IWin32Window, IList<AtomSpectraBoardEntry>, AtomSpectraBoardEntry>((o, b) => { shown++; return null; }));
                AtomSpectraBoardFinder.Browse = () =>
                {
                    gate.WaitOne(10000);   // the browse of a search ends when the scenario lets it
                    return new List<AtomSpectraBoardEntry>
                    {
                        new AtomSpectraBoardEntry { Address = "127.0.0.2", HostName = "board-a", Instance = "a", Serial = "00000051", Source = "atomspectra" },
                        new AtomSpectraBoardEntry { Address = "127.0.0.3", HostName = "board-b", Instance = "b", Serial = "00000119", Source = "atomspectra" }
                    };
                };
                MethodInfo click = t.GetMethod("buttonRefresh_Click", UiNf);
                Action refresh = () => click.Invoke(page, new object[] { null, EventArgs.Empty });
                Func<string, int> refused = why => AtomSpectraTcpLog.Snapshot().Count(l => l.Text.Contains("the board choice is not shown: " + why));

                refresh();
                Pump(() => shown == 1 && UiCtl(page, "buttonRefresh").Enabled, 8000);
                Check(shown == 1, "control: the search still current on the page shows the choice window (shown " + shown + ")");

                gate = new ManualResetEvent(false);
                refresh();
                t.GetMethod("LoadFormContents").Invoke(page, new object[] { UiMockCfg() });
                Application.DoEvents();
                gate.Set();
                Pump(() => refused("another configuration") >= 1, 8000);
                Check(shown == 1 && refused("another configuration") == 1, "another configuration loaded during the search: no window for the old search (shown " + shown + ")");

                gate = new ManualResetEvent(false);
                refresh();
                refresh();
                Application.DoEvents();
                gate.Set();
                Pump(() => shown == 2 && refused("a newer search") >= 1, 8000);
                Check(shown == 2 && refused("a newer search") == 1, "two searches: only the newer one shows the window (shown " + shown + ")");

                gate = new ManualResetEvent(false);
                ManualResetEvent g = gate;
                refresh();
                using (var dlg = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -2000), Size = new Size(200, 100) })
                using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                {
                    int ticks = 0;
                    timer.Tick += (s, e) =>
                    {
                        if (++ticks == 1) g.Set();   // the search ends while a modal window is open over the settings form
                        if (refused("another window") >= 1 || ticks > 80) { timer.Stop(); dlg.Close(); }
                    };
                    timer.Start();
                    dlg.ShowDialog(host);
                }
                Pump(() => false, 300);
                Check(shown == 2 && refused("another window") == 1, "a modal window over the settings form (the question at closing): no choice window over it (shown " + shown + ")");
            }
            finally
            {
                gate.Set();
                hook.SetValue(null, savedHook);
                AtomSpectraBoardFinder.Browse = savedBrowse;
                host.Close();
                host.Dispose();
            }
        }
    }
}
