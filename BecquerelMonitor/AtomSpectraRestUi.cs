// windows of the REST transport: the board is read on a worker thread behind a small modal wait window, so the UI thread never blocks on the network; the Start dialog and the Clear confirmation always run on the UI thread (marshalled when asked from a worker).
using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public static class AtomSpectraRestUi
    {
        private static Form currentWait;

        private static Form MainWindow()
        {
            return Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        }

        public static bool OnUiThread()
        {
            Form f = MainWindow();
            return f != null && !f.InvokeRequired;
        }

        public static T RunWithWait<T>(Func<T> work)
        {
            if (!AppUi.HasWindows || !OnUiThread()) return work();

            T result = default(T);
            Exception error = null;
            bool done = false;

            using (Form wait = new Form())
            {
                wait.FormBorderStyle = FormBorderStyle.FixedDialog;
                wait.ControlBox = false;
                wait.ShowInTaskbar = false;
                wait.StartPosition = FormStartPosition.CenterParent;
                wait.ClientSize = new Size(320, 70);
                wait.Text = Resources.AtomSpectraRestWait;

                Label label = new Label();
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Text = Resources.AtomSpectraRestWait;
                wait.Controls.Add(label);

                wait.FormClosing += (s, e) => { if (!done) e.Cancel = true; };

                wait.Shown += (s, e) => ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { result = work(); }
                    catch (Exception ex) { error = ex; }
                    finally
                    {
                        try { wait.BeginInvoke(new MethodInvoker(delegate { done = true; wait.Close(); })); }
                        catch (InvalidOperationException) { done = true; }
                    }
                });

                currentWait = wait;
                try { wait.ShowDialog(MainWindow()); }
                finally { currentWait = null; }
            }

            if (error != null) throw new InvalidOperationException(error.Message, error);
            return result;
        }

        public static AtomSpectraRestStartChoice AskStart(AtomSpectraRestStartQuestion q)
        {
            Form host = currentWait ?? MainWindow();
            if (host == null) return AtomSpectraRestStartChoice.Cancel;
            if (host.InvokeRequired)
                return (AtomSpectraRestStartChoice)host.Invoke(new Func<AtomSpectraRestStartChoice>(delegate { return ShowStartDialog(q, host); }));
            return ShowStartDialog(q, host);
        }

        private static string Span(string format, long seconds, long counts)
        {
            return string.Format(CultureInfo.CurrentCulture, format, seconds / 3600, (seconds % 3600) / 60, counts.ToString("N0", CultureInfo.CurrentCulture));
        }

        public static string StartQuestionText(AtomSpectraRestStartQuestion q)
        {
            long bt = q.Board != null && q.Board.HasData ? q.Board.Time : 0;
            long bn = q.Board != null && q.Board.HasData ? q.Board.Total : 0;

            string text = Span(q.Counting ? Resources.AtomSpectraRestStartRunning : Resources.AtomSpectraRestStartStopped, bt, bn);

            if (q.DocumentSeconds > 0 || q.DocumentCounts > 0)
                text += Environment.NewLine + Span(Resources.AtomSpectraRestStartDocument, q.DocumentSeconds, q.DocumentCounts);

            if (!q.ContinueAllowed)
                text += Environment.NewLine + Environment.NewLine + Resources.AtomSpectraRestStartBehind;

            if (!string.IsNullOrEmpty(q.Note))
                text += Environment.NewLine + Environment.NewLine + q.Note;   // TCP says what each button sends

            return text;
        }

        private static AtomSpectraRestStartChoice ShowStartDialog(AtomSpectraRestStartQuestion q, IWin32Window owner)
        {
            using (Form f = new Form())
            {
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Text = Resources.AtomSpectraRestStartTitle;
                f.ClientSize = new Size(480, 190);
                f.AutoScaleMode = AutoScaleMode.Font;

                Label lbl = new Label();
                lbl.Location = new Point(12, 12);
                lbl.Size = new Size(456, 120);
                lbl.Text = StartQuestionText(q);
                f.Controls.Add(lbl);

                Button cont = new Button();
                cont.Location = new Point(12, 150);
                cont.Size = new Size(170, 28);
                cont.Text = Resources.AtomSpectraRestContinue;
                cont.DialogResult = DialogResult.Yes;
                cont.Enabled = q.ContinueAllowed;
                f.Controls.Add(cont);

                Button restart = new Button();
                restart.Location = new Point(190, 150);
                restart.Size = new Size(170, 28);
                restart.Text = Resources.AtomSpectraRestRestart;
                restart.DialogResult = DialogResult.Retry;
                restart.Enabled = q.DocumentSeconds <= 0 && q.DocumentCounts <= 0;   // only into an empty document
                f.Controls.Add(restart);

                Button cancel = new Button();
                cancel.Location = new Point(368, 150);
                cancel.Size = new Size(100, 28);
                cancel.Text = Resources.AtomSpectraRestCancel;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);

                int extra = string.IsNullOrEmpty(q.Note) ? 0 : 100;   // room for the TCP note
                f.ClientSize = new Size(480, 190 + extra);
                lbl.Height += extra;
                foreach (Control b in new Control[] { cont, restart, cancel }) b.Top += extra;

                f.AcceptButton = q.ContinueAllowed ? cont : cancel;
                f.CancelButton = cancel;

                DialogResult r = f.ShowDialog(owner);
                if (r == DialogResult.Yes && q.ContinueAllowed) return AtomSpectraRestStartChoice.Continue;
                if (r == DialogResult.Retry) return AtomSpectraRestStartChoice.Restart;
                return AtomSpectraRestStartChoice.Cancel;
            }
        }

        public static bool ConfirmClear(AtomSpectraRestBoardSample s)
        {
            long t = s != null && s.HasData ? s.Time : 0;
            long n = s != null && s.HasData ? s.Total : 0;
            return MessageBox.Show(MainWindow(), Span(Resources.MSGAtomSpectraRestClearConfirm, t, n), Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }
}
