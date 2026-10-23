using BecquerelMonitor.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

// Non-modal log window of the AtomSpectra TCP/REST device type, one for the whole program.
// Lines arrive on any thread through LineAdded and are only queued;
// a UI timer appends them in batches at most 10 times a second, so writers never wait for the window.
// Closing the window neither stops a measurement nor loses the buffer (the buffer is AtomSpectraTcpLog).
// It has no owner: closing the device settings window it was opened from does not close it.
namespace BecquerelMonitor
{
    public sealed partial class AtomSpectraTcpLogForm : Form
    {
        private static AtomSpectraTcpLogForm instance;

        private readonly RichTextBox box;
        private readonly FlowLayoutPanel topPanel;
        private readonly Button pauseButton;
        private readonly Button clearButton;
        private readonly Button copyButton;
        private readonly Button saveButton;
        private readonly ComboBox levelBox;
        private readonly StatusStrip statusStrip;
        private readonly ToolStripStatusLabel statusLabel;

        private readonly ConcurrentQueue<AtomSpectraTcpLogLine> pending;
        private readonly Timer flushTimer;
        private bool paused;
        private int shownLines;

        public AtomSpectraTcpLogForm()
        {
            pending = new ConcurrentQueue<AtomSpectraTcpLogLine>();
            flushTimer = new Timer();
            flushTimer.Interval = 100;
            flushTimer.Tick += flushTimer_Tick;

            // Form setup
            Text = Resources.AtomSpectraTcpLogTitle;
            Size = new Size(900, 500);
            StartPosition = FormStartPosition.CenterScreen;   // no owner to center on
            ShowInTaskbar = true;

            // RichTextBox
            box = new RichTextBox();
            box.ReadOnly = true;
            box.Dock = DockStyle.Fill;
            box.Font = new Font("Consolas", 9f);
            box.WordWrap = false;
            box.BackColor = Color.White;
            box.HideSelection = false;

            // Top panel with buttons and filter
            topPanel = new FlowLayoutPanel();
            topPanel.Dock = DockStyle.Top;
            topPanel.AutoSize = true;
            topPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            topPanel.Padding = new Padding(4);

            pauseButton = new Button();
            pauseButton.Text = Resources.AtomSpectraTcpLogPause;
            pauseButton.Click += pauseButton_Click;

            clearButton = new Button();
            clearButton.Text = Resources.AtomSpectraTcpLogClear;
            clearButton.Click += clearButton_Click;

            copyButton = new Button();
            copyButton.Text = Resources.AtomSpectraTcpLogCopy;
            copyButton.Click += copyButton_Click;

            saveButton = new Button();
            saveButton.Text = Resources.AtomSpectraTcpLogSave;
            saveButton.Click += saveButton_Click;

            levelBox = new ComboBox();
            levelBox.DropDownStyle = ComboBoxStyle.DropDownList;
            levelBox.Items.Add(Resources.AtomSpectraTcpLogFilterAll);
            levelBox.Items.Add(Resources.AtomSpectraTcpLogFilterWarn);
            levelBox.Items.Add(Resources.AtomSpectraTcpLogFilterError);
            levelBox.Items.Add(Resources.AtomSpectraTcpLogFilterDebug);   // device answers, hidden unless chosen
            levelBox.SelectedIndex = 0;
            levelBox.SelectedIndexChanged += levelBox_SelectedIndexChanged;

            topPanel.Controls.Add(pauseButton);
            topPanel.Controls.Add(clearButton);
            topPanel.Controls.Add(copyButton);
            topPanel.Controls.Add(saveButton);
            topPanel.Controls.Add(levelBox);

            // Status strip
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            statusLabel.Text = Resources.AtomSpectraTcpLogNoStatus;
            statusStrip.Items.Add(statusLabel);

            // Add controls to form
            Controls.Add(BuildTabs(box));   // tab 1: this log; tab 2: the board's own log (GET /api/devlog)
            Controls.Add(topPanel);
            Controls.Add(statusStrip);

            // Subscribe to log events
            AtomSpectraTcpLog.LineAdded += OnLineAdded;

            // Initial rebuild
            Rebuild();

            // Start timer
            flushTimer.Start();
        }

        // Shown WITHOUT an owner: an owned window is closed together with its owner, and the button that opens
        // the log sits in the device settings window, which is closed long before a measurement ends.
        public static void ShowLog()
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new AtomSpectraTcpLogForm();
            }

            if (!instance.Visible)
            {
                instance.Show();
            }

            instance.Activate();

            if (instance.WindowState == FormWindowState.Minimized)
            {
                instance.WindowState = FormWindowState.Normal;
            }
        }

        private void OnLineAdded(object sender, AtomSpectraTcpLogLine line)
        {
            // Any thread; never touches controls
            pending.Enqueue(line);
            // While paused the queue must not grow without bound: Resume rebuilds from the buffer anyway
            AtomSpectraTcpLogLine dropped;
            while (pending.Count > AtomSpectraTcpLog.Capacity && pending.TryDequeue(out dropped)) { }
        }

        private bool Passes(AtomSpectraTcpLogLine l)
        {
            int idx = levelBox.SelectedIndex;
            if (idx == 3) return true;   // "all, with debug"
            if (idx == 0) return l.Level != AtomSpectraTcpLogLevel.Debug;   // the default hides the Debug lines (-inf may carry the serial)
            if (idx == 1) return l.Level == AtomSpectraTcpLogLevel.Warn || l.Level == AtomSpectraTcpLogLevel.Error;
            if (idx == 2) return l.Level == AtomSpectraTcpLogLevel.Error;
            return true;
        }

        private void Append(IList<AtomSpectraTcpLogLine> lines)
        {
            if (lines == null || lines.Count == 0) return;

            box.SuspendLayout();
            try
            {
                foreach (AtomSpectraTcpLogLine line in lines)
                {
                    box.SelectionStart = box.TextLength;
                    box.SelectionLength = 0;

                    Color color;
                    if (line.Level == AtomSpectraTcpLogLevel.Info)
                        color = Color.Black;
                    else if (line.Level == AtomSpectraTcpLogLevel.Warn)
                        color = Color.DarkOrange;
                    else if (line.Level == AtomSpectraTcpLogLevel.Debug)
                        color = Color.Gray;
                    else
                        color = Color.Red;

                    box.SelectionColor = color;
                    box.AppendText(line.ToString() + "\n");
                }

                if (!paused)
                {
                    box.SelectionStart = box.TextLength;
                    box.ScrollToCaret();
                }
            }
            finally
            {
                box.ResumeLayout();
            }

            shownLines += lines.Count;

            if (shownLines > AtomSpectraTcpLog.Capacity + 500)
            {
                Rebuild();
            }
        }

        private void flushTimer_Tick(object sender, EventArgs e)
        {
            statusLabel.Text = (AtomSpectraTcpLog.StatusText ?? Resources.AtomSpectraTcpLogNoStatus) + StaleSuffix();

            if (paused) return;

            List<AtomSpectraTcpLogLine> batch = new List<AtomSpectraTcpLogLine>();
            AtomSpectraTcpLogLine line;
            int count = 0;
            while (count < 500 && pending.TryDequeue(out line))
            {
                if (Passes(line))
                {
                    batch.Add(line);
                }
                count++;
            }

            if (batch.Count > 0)
            {
                Append(batch);
            }
        }

        private void Rebuild()
        {
            // Drain and drop pending (snapshot already contains them)
            AtomSpectraTcpLogLine line;
            while (pending.TryDequeue(out line))
            {
                // discard
            }

            box.Clear();
            shownLines = 0;

            List<AtomSpectraTcpLogLine> snapshot = AtomSpectraTcpLog.Snapshot();
            List<AtomSpectraTcpLogLine> filtered = new List<AtomSpectraTcpLogLine>();
            foreach (AtomSpectraTcpLogLine l in snapshot)
            {
                if (Passes(l))
                {
                    filtered.Add(l);
                }
            }

            Append(filtered);
        }

        private void pauseButton_Click(object sender, EventArgs e)
        {
            paused = !paused;
            if (paused)
            {
                pauseButton.Text = Resources.AtomSpectraTcpLogResume;
            }
            else
            {
                pauseButton.Text = Resources.AtomSpectraTcpLogPause;
                Rebuild();
            }
        }

        private void clearButton_Click(object sender, EventArgs e)
        {
            AtomSpectraTcpLog.Clear();
            Rebuild();
        }

        private void copyButton_Click(object sender, EventArgs e)
        {
            if (box.TextLength > 0)
            {
                try
                {
                    Clipboard.SetText(box.Text);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // Clipboard may be locked by another process
                }
            }
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = Resources.AtomSpectraTcpLogFileFilter;
                dlg.FileName = "atomspectra-tcp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        List<AtomSpectraTcpLogLine> snapshot = AtomSpectraTcpLog.Snapshot();
                        List<string> lines = new List<string>();
                        foreach (AtomSpectraTcpLogLine l in snapshot)
                        {
                            if (Passes(l))
                            {
                                lines.Add(l.ToString());
                            }
                        }
                        File.WriteAllLines(dlg.FileName, lines, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, Resources.AtomSpectraTcpLogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void levelBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            Rebuild();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            AtomSpectraTcpLog.LineAdded -= OnLineAdded;
            flushTimer.Stop();
            flushTimer.Dispose();
            instance = null;
            base.OnFormClosed(e);
        }
    }
}
