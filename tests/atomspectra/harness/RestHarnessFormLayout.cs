using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFormLayout()
        {
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

                    // The host imitates the device page of the configuration window (tab page 490 x 599)
                    var host = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(-3000, -3000),
                        ShowInTaskbar = false
                    };
                    host.ClientSize = new Size(490, 599);

                    var page = new Panel
                    {
                        Location = new Point(0, 0),
                        Size = new Size(490, 599),
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
                    };
                    host.Controls.Add(page);

                    // Like DeviceConfigForm.PrepareDeviceForm adds it to its tab page
                    var form = (Control)Activator.CreateInstance(t);
                    page.Controls.Add(form);

                    // A fresh configuration opens in REST mode
                    t.GetMethod("LoadFormContents").Invoke(form, new object[] { new AtomSpectraTcpDeviceConfig() });

                    host.Show();
                    Application.DoEvents();

                    Func<string, Control> c = n => (Control)t.GetField(n, f).GetValue(form);

                    // Find the label of the time limit
                    Control limit = c("numericLimit");
                    Label limitLabel = form.Controls.OfType<Label>().FirstOrDefault(l => l.Left < limit.Left && Math.Abs(l.Top - limit.Top) <= 8);   // by its row: the text depends on the language
                    bool ru = Thread.CurrentThread.CurrentUICulture.Name == "ru-RU";
                    Check(limitLabel != null && (ru ? limitLabel.Text[0] >= 'Ѐ' : limitLabel.Text.StartsWith("Time limit")), "the captions are in the language of the run (" + Thread.CurrentThread.CurrentUICulture.Name + ")");

                    // Measure at width 490
                    Action<string> measure = tag =>
                    {
                        Console.WriteLine(tag + ": form " + form.Bounds + ", hscroll " + ((ScrollableControl)form).HorizontalScroll.Visible + ", label " + (limitLabel == null ? "none" : limitLabel.Bounds.ToString()) + ", limit " + limit.Bounds + ", url " + c("textRestUrl").Bounds + ", cmd " + c("textCommand").Bounds + ", out " + c("textOutput").Bounds);

                        // Save a picture
                        using (var bmp = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                            string png = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "form_layout_" + (ru ? "ru_" : "") + tag + ".png");
                            try { using (var fs = System.IO.File.Create(png)) bmp.Save(fs, ImageFormat.Png); }
                            catch (Exception ex) { Console.WriteLine("    picture not saved (" + png + "): " + ex.GetType().Name + " " + ex.HResult.ToString("X8")); }   // evidence only, not a check
                        }
                    };

                    measure("w490");

                    Check(limitLabel != null && limitLabel.Right <= limit.Left - 2, "the time-limit label does not run into its field");
                    Check(!((ScrollableControl)form).HorizontalScroll.Visible, "no horizontal scroll bar: every field fits the page width");

                    int visible = form.ClientSize.Width;
                    Check(c("textRestUrl").Right <= visible && c("textCommand").Right <= visible && c("textOutput").Right <= visible, "board URL, command line and output end inside the visible width");
                    var outside = form.Controls.Cast<Control>().Where(x => x.Visible && x.Right > visible).Select(x => x.GetType().Name + " '" + x.Text.Substring(0, Math.Min(24, x.Text.Length)) + "' " + x.Bounds).ToList();
                    Console.WriteLine("    display " + form.DisplayRectangle + ", client " + form.ClientSize);
                    Check(outside.Count == 0, "every visible control ends inside the visible width" + (outside.Count == 0 ? "" : ": " + string.Join("; ", outside)));
                    var shown = form.Controls.Cast<Control>().Where(x => x.Visible).ToList();
                    var overlaps = shown.OfType<Label>().SelectMany(l => shown.Where(o => o != l && o.Bounds.IntersectsWith(l.Bounds)).Select(o => "'" + l.Text.Substring(0, Math.Min(20, l.Text.Length)) + "' " + l.Bounds + " x " + o.GetType().Name + " " + o.Bounds)).ToList();
                    Check(overlaps.Count == 0, "no label overlaps another control" + (overlaps.Count == 0 ? "" : ": " + string.Join("; ", overlaps)));
                    Check(c("textRestUrl").Width >= 200, "the board URL field is wide enough for an address like http://192.0.2.19/ (>= 200 px)");
                    Check(form.Width >= page.ClientSize.Width - 1 && form.Height >= page.ClientSize.Height - 1, "the form fills its page");

                    // the proxy credentials warning, shown only with authorization in REST mode
                    Control warn = t.GetField("labelAuthWarning", f) == null ? null : c("labelAuthWarning");
                    var combo = (ComboBox)c("comboAuth");
                    Check(warn != null && !warn.Visible, "no proxy warning while authorization is None");
                    combo.SelectedIndex = 1;
                    Application.DoEvents();
                    Check(warn != null && warn.Visible && warn.Text.Contains("HTTP") && warn.Top >= c("textAuthSecret").Bottom && warn.Bottom <= c("buttonRestTest").Top && warn.Right <= form.ClientSize.Width, "Basic shows the warning above the buttons, inside the width");
                    ((RadioButton)c("radioTcp")).Checked = true;
                    Application.DoEvents();
                    Check(warn != null && !warn.Visible, "TCP mode hides the warning");
                    ((RadioButton)c("radioRest")).Checked = true;
                    combo.SelectedIndex = 2;
                    Application.DoEvents();
                    Check(warn != null && warn.Visible, "Bearer shows the warning too");
                    combo.SelectedIndex = 0;
                    Application.DoEvents();

                    // the TCP hint next to "Poll period" was cut at the right edge
                    ((RadioButton)c("radioTcp")).Checked = true;
                    Application.DoEvents();
                    var tcpShown = form.Controls.Cast<Control>().Where(x => x.Visible).ToList();
                    Control hint = c("labelPollHint");
                    Console.WriteLine("    TCP poll hint " + hint.Bounds + " '" + hint.Text + "'");
                    Check(hint.Visible && hint.Right <= form.ClientSize.Width && !tcpShown.Any(o => o != hint && o.Bounds.IntersectsWith(hint.Bounds)), "TCP: the poll hint ends inside the width and overlaps nothing");
                    ((RadioButton)c("radioRest")).Checked = true;
                    Application.DoEvents();

                    // Widen
                    int urlBefore = c("textRestUrl").Width;
                    int outBefore = c("textOutput").Width;
                    host.ClientSize = new Size(690, 599);
                    page.Size = new Size(690, 599);
                    Application.DoEvents();

                    measure("w690");

                    Check(c("textRestUrl").Width > urlBefore && c("textOutput").Width > outBefore, "a wider page widens the URL field and the output window");
                    Check(c("textOutput").Right <= form.ClientSize.Width, "the output still ends inside the visible width");

                    host.Close();
                    host.Dispose();
                }
                catch (Exception ex)
                {
                    Check(false, "layout test threw " + ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return true;
        }
    }
}
