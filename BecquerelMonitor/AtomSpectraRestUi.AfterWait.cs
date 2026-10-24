// No window over the wait window "Reading the board...": a dialog asked during the wait hides it and is owned by it, a message posted
// during the wait waits for its end; also the Stop question of a joined acquisition.
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public static partial class AtomSpectraRestUi
    {
        // the actions held while the wait window is open (UI thread only).
        private static readonly List<Action> afterWait = new List<Action>();

        // runs the action now, or right after the wait window closes when one is open (a message must not open over the wait window); UI thread only.
        public static void AfterWait(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (currentWait == null)
            {
                action();
                return;
            }

            afterWait.Add(action);
        }

        // called once the wait window is closed: the held actions are posted to the UI thread in their order.
        private static void RunAfterWait()
        {
            if (afterWait.Count == 0)
            {
                return;
            }

            Action[] actions = afterWait.ToArray();
            afterWait.Clear();

            Form main = MainWindow();
            foreach (Action action in actions)
            {
                if (main != null && !main.IsDisposed && main.IsHandleCreated)
                {
                    Action held = action;
                    main.BeginInvoke(new Action(() => AfterWaitSafe(held)));   // a new wait window may be open by now: held again
                }
                else
                {
                    AfterWaitSafe(action);
                }
            }
        }

        // each held action on its own: one that throws is written to the log and must not take the others, nor the result of the wait, with it
        private static void AfterWaitSafe(Action action)
        {
            try
            {
                AfterWait(action);
            }
            catch (Exception ex)
            {
                AtomSpectraTcpLog.Warn("Session", "an action held over the wait window failed: " + ex.Message);
            }
        }

        // a dialog asked while the wait window may be open, shown on the UI thread.
        private static T OverWait<T>(T fallback, Func<IWin32Window, T> dialog)
        {
            Form host = currentWait ?? MainWindow();
            if (host == null)
            {
                return fallback;
            }

            if (host.InvokeRequired)
            {
                return (T)host.Invoke(new Func<T>(delegate { return ShowOver(host, dialog); }));
            }

            return ShowOver(host, dialog);
        }

        // the wait window stays the owner (the main window stays disabled) but is invisible meanwhile.
        private static T ShowOver<T>(Form host, Func<IWin32Window, T> dialog)
        {
            bool hide = ReferenceEquals(host, currentWait);
            double opacity = host.Opacity;

            if (hide)
            {
                host.Opacity = 0;
            }

            try
            {
                return dialog(host);
            }
            finally
            {
                if (hide && !host.IsDisposed)
                {
                    host.Opacity = opacity;
                }
            }
        }

        // the Replace confirmation owned by the window it is asked over; "No" is the default.
        public static bool ConfirmReplace(IWin32Window owner, long docSeconds, long docCounts, long boardSeconds, long boardCounts)
        {
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException("BecqMoni: AtomSpectra: the Replace confirmation needs a window; nothing was replaced.");
            }

            string text = ReplaceConfirmText(docSeconds, docCounts, boardSeconds, boardCounts);

            // through the door of AppUi (tools\check_headless.py): the owner stays the window it is asked over
            return AppUi.AskYesNo(owner, text, Resources.ConfirmationDialogTitle, MessageBoxIcon.Warning, true);
        }

        // Stop of an acquisition BecqMoni only joined: true = stop it on the board too, false = it keeps counting (the default); any thread.
        public static bool AskStopJoined(string endpoint)
        {
            if (!AppUi.HasWindows)
            {
                return false;
            }

            return OverWait(false, owner => ShowStopJoinedDialog(owner, endpoint));
        }

        // the Stop question dialog for a joined acquisition.
        private static bool ShowStopJoinedDialog(IWin32Window owner, string endpoint)
        {
            using (Form f = new Form())
            {
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Text = Resources.ConfirmationDialogTitle;
                f.AutoScaleMode = AutoScaleMode.Font;

                string message = string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraStopJoinedAsk, endpoint ?? "?");
                Label label = new Label();
                label.Location = new Point(12, 12);
                label.Text = message;
                label.AutoSize = false;
                label.Width = 456;
                label.Height = TextRenderer.MeasureText(message, label.Font, new Size(456, int.MaxValue), TextFormatFlags.WordBreak).Height + 8;
                f.Controls.Add(label);

                int top = label.Bottom + 12;

                Button stop = new Button();
                stop.Location = new Point(12, top);
                stop.Size = new Size(220, 28);
                stop.Text = Resources.AtomSpectraStopOnBoard;
                stop.DialogResult = DialogResult.Yes;

                Button keep = new Button();
                keep.Location = new Point(248, top);
                keep.Size = new Size(220, 28);
                keep.Text = Resources.AtomSpectraKeepCounting;
                keep.DialogResult = DialogResult.No;

                f.Controls.Add(stop);
                f.Controls.Add(keep);

                f.ClientSize = new Size(480, top + 40);
                f.AcceptButton = keep;
                f.CancelButton = keep;
                f.ActiveControl = keep;

                return f.ShowDialog(owner) == DialogResult.Yes;
            }
        }
    }
}
