// windows of the REST transport: the board is read on a worker thread behind a small modal wait window, so the UI thread never blocks on the network; the Start dialog and the Clear confirmation always run on the UI thread (marshalled when asked from a worker).
using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // What to do after a reset of the board from outside: nothing, the board's spectrum into this document, or into a new one
    public enum AtomSpectraBoardResetChoice
    {
        Close,
        Replace,
        NewDocument
    }

    public static partial class AtomSpectraRestUi
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

                Form outer = currentWait;   // a wait window opened inside another one (a window answered during it): the outer one is restored
                currentWait = wait;
                try { wait.ShowDialog(MainWindow()); }
                finally { currentWait = outer; if (outer == null) RunAfterWait(); }   // the messages posted during the wait come now, not over it
            }

            if (error != null) throw new InvalidOperationException(error.Message, error);
            return result;
        }

        public static AtomSpectraRestStartChoice AskStart(AtomSpectraRestStartQuestion q)
        {
            return OverWait(AtomSpectraRestStartChoice.Cancel, owner => ShowStartDialog(q, owner));   // the wait window hidden under it
        }

        private static string Span(string format, long seconds, long counts)
        {
            return string.Format(CultureInfo.InvariantCulture, format, seconds / 3600, (seconds % 3600) / 60, counts.ToString(CultureInfo.InvariantCulture));
        }

        public static string StartQuestionText(AtomSpectraRestStartQuestion q)
        {
            long bt = q.Board != null && q.Board.HasData ? q.Board.Time : 0;
            long bn = q.Board != null && q.Board.HasData ? q.Board.Total : 0;

            // a board that sent no status (TCP: a silent bridge) is described by the note; "stopped, 0 h 0 min" would be untrue
            bool known = q.Board != null && q.Board.HasData;
            string text = known || string.IsNullOrEmpty(q.Note)
                ? Span(q.Counting ? Resources.AtomSpectraRestStartRunning : Resources.AtomSpectraRestStartStopped, bt, bn)
                : "";

            if (q.DocumentSeconds > 0 || q.DocumentCounts > 0)
                text += (text.Length > 0 ? Environment.NewLine : "") + Span(Resources.AtomSpectraRestStartDocument, q.DocumentSeconds, q.DocumentCounts);

            // the board below the document in time or counts: reset; in a channel only: another acquisition that overtook ours
            bool behind = q.Board == null || !q.Board.HasData || bt < q.DocumentSeconds || bn < q.DocumentCounts;
            if (!q.ContinueAllowed)
                text += (text.Length > 0 ? Environment.NewLine + Environment.NewLine : "") + (q.ChannelBelow >= 0 && !behind
                    ? string.Format(CultureInfo.InvariantCulture, q.ReplaceAllowed ? Resources.AtomSpectraRestStartOtherSpectrumReplace : Resources.AtomSpectraRestStartOtherSpectrum, q.ChannelBelow)
                    : (q.ReplaceAllowed ? Resources.AtomSpectraRestStartBehindReplace : Resources.AtomSpectraRestStartBehind));   // no "create a new document" beside Replace
            if (q.ReplaceAllowed)   // observe only sends nothing at all (ObservePlan), not even -sta to a stopped board
                text += " " + (q.ObserveOnly ? Resources.AtomSpectraRestStartReplaceHintObserve : Resources.AtomSpectraRestStartReplaceHint);

            if (!string.IsNullOrEmpty(q.Note))
                text += (text.Length > 0 ? Environment.NewLine + Environment.NewLine : "") + q.Note;   // TCP says what each button sends

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
                int width = q.ReplaceAllowed ? 658 : 480;   // room for the fourth button
                f.ClientSize = new Size(width, 190);
                f.AutoScaleMode = AutoScaleMode.Font;

                Label lbl = new Label();
                lbl.Location = new Point(12, 12);
                lbl.Size = new Size(width - 24, 120);
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

                // the board's spectrum into this document, the board not reset; shown only where Continue is refused
                Button replace = new Button();
                replace.Location = new Point(368, 150);
                replace.Size = new Size(170, 28);
                replace.Text = Resources.AtomSpectraRestReplace;
                replace.DialogResult = DialogResult.OK;
                replace.Visible = q.ReplaceAllowed;
                f.Controls.Add(replace);

                Button cancel = new Button();
                cancel.Location = new Point(width - 112, 150);
                cancel.Size = new Size(100, 28);
                cancel.Text = Resources.AtomSpectraRestCancel;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);

                // room for the text as it lays out at this width (the reason lines and the TCP note vary in length)
                Size need = TextRenderer.MeasureText(lbl.Text, lbl.Font, new Size(lbl.Width, int.MaxValue), TextFormatFlags.WordBreak);
                int extra = Math.Max(0, need.Height + 8 - lbl.Height);
                f.ClientSize = new Size(width, 190 + extra);
                lbl.Height += extra;
                foreach (Control b in new Control[] { cont, restart, replace, cancel }) b.Top += extra;

                f.AcceptButton = q.ContinueAllowed ? cont : cancel;
                f.CancelButton = cancel;

                while (true)
                {
                    DialogResult r = f.ShowDialog(owner);
                    if (r == DialogResult.Yes && q.ContinueAllowed) return AtomSpectraRestStartChoice.Continue;
                    if (r == DialogResult.Retry) return AtomSpectraRestStartChoice.Restart;
                    if (r != DialogResult.OK || !q.ReplaceAllowed) return AtomSpectraRestStartChoice.Cancel;
                    // declined: back to the question, nothing is sent
                    if (ConfirmReplace(owner, q.DocumentSeconds, q.DocumentCounts, q.Board.Time, q.Board.Total)) return AtomSpectraRestStartChoice.Replace;
                }
            }
        }

        public static string ReplaceConfirmText(long docSeconds, long docCounts, long boardSeconds, long boardCounts)
        {
            return string.Format(CultureInfo.InvariantCulture, Resources.MSGAtomSpectraRestReplaceConfirm,
                docSeconds / 3600, (docSeconds % 3600) / 60, docCounts.ToString(CultureInfo.InvariantCulture),
                boardSeconds / 3600, (boardSeconds % 3600) / 60, boardCounts.ToString(CultureInfo.InvariantCulture));
        }

        // "No" is the default: Enter does not give the document's data away
        public static bool ConfirmReplace(long docSeconds, long docCounts, long boardSeconds, long boardCounts)
        {
            return AppUi.AskYesNo(ReplaceConfirmText(docSeconds, docCounts, boardSeconds, boardCounts), Resources.ConfirmationDialogTitle, MessageBoxIcon.Warning, true);
        }

        // After a reset of the board from outside (the recording is stopped already), on the UI thread: the message and what to
        // do with the board's new acquisition. Replace is confirmed with the numbers first; "No" returns to the question.
        public static AtomSpectraBoardResetChoice AskAfterBoardReset(string message, long docSeconds, long docCounts, long boardSeconds, long boardCounts)
        {
            using (Form f = BoardResetForm(message))
            {
                while (true)
                {
                    DialogResult r = f.ShowDialog(MainWindow());
                    if (r == DialogResult.Retry) return AtomSpectraBoardResetChoice.NewDocument;
                    if (r != DialogResult.Yes) return AtomSpectraBoardResetChoice.Close;
                    if (ConfirmReplace(docSeconds, docCounts, boardSeconds, boardCounts)) return AtomSpectraBoardResetChoice.Replace;
                }
            }
        }

        private static Form BoardResetForm(string message)
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.Text = Resources.AtomSpectraRestBoardResetTitle;
            f.AutoScaleMode = AutoScaleMode.Font;
            Label lbl = new Label { Location = new Point(12, 12), Text = message + Environment.NewLine + Environment.NewLine + Resources.AtomSpectraRestBoardResetChoices };
            Size need = TextRenderer.MeasureText(lbl.Text, lbl.Font, new Size(566, int.MaxValue), TextFormatFlags.WordBreak);
            lbl.Size = new Size(566, need.Height + 8);
            f.Controls.Add(lbl);
            int top = lbl.Bottom + 12;
            f.Controls.Add(new Button { Location = new Point(12, top), Size = new Size(170, 28), Text = Resources.AtomSpectraRestReplace, DialogResult = DialogResult.Yes });
            // 280: the English "Open the board's acquisition in a new document" wrapped to two cut lines at 250 (seen on screen, PR #38 review)
            f.Controls.Add(new Button { Location = new Point(190, top), Size = new Size(280, 28), Text = Resources.AtomSpectraRestOpenNewDocument, DialogResult = DialogResult.Retry });
            Button close = new Button { Location = new Point(478, top), Size = new Size(100, 28), Text = Resources.AtomSpectraRestClose, DialogResult = DialogResult.Cancel };
            f.Controls.Add(close);
            f.ClientSize = new Size(590, top + 40);
            f.AcceptButton = f.CancelButton = close;
            return f;
        }

        public static bool ConfirmClear(AtomSpectraRestBoardSample s)
        {
            return ConfirmClear(s, false);
        }

        // withDocument (#BM-22): the ONE question of "Clear" - it speaks of the document and the board together
        public static bool ConfirmClear(AtomSpectraRestBoardSample s, bool withDocument)
        {
            long t = s != null && s.HasData ? s.Time : 0;
            long n = s != null && s.HasData ? s.Total : 0;
            string format = withDocument ? Resources.MSGAtomSpectraRestClearBothConfirm : Resources.MSGAtomSpectraRestClearConfirm;
            // through the AppUi door: without windows it throws instead of hanging (S100, tools/check_headless.py);
            // the windows held over the wait window keep waiting until it is answered
            return HoldingAfterWait(() => AppUi.AskYesNo(Span(format, t, n), Resources.ConfirmationDialogTitle, MessageBoxIcon.Warning, true));
        }
    }
}
