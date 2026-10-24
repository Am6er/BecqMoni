using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

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
                    return true;
                }
                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}
