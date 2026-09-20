using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Read-only instruction text for the AmplitudaSerial device type. Built entirely in code
    // (no .Designer.cs / .resx of its own), in the style of the other AmplitudaSerial files.
    //
    // Two modes, chosen by the constructor argument:
    //  - withCountdown = true:  shown automatically, once per device config (see
    //    AmplitudaSerialDeviceForm). The OK button is disabled and counts down from
    //    CountdownSeconds; the window has no control box and FormClosing is cancelled while the
    //    countdown is still running, so the user cannot Alt+F4 / Esc / Enter their way out early.
    //    Once the countdown reaches zero every normal way of closing the window works again.
    //  - withCountdown = false: opened by the "Instruction" button at any time; OK is enabled
    //    immediately and the window has a normal close box.
    public sealed class AmplitudaSerialInstructionForm : Form
    {
        public const int CountdownSeconds = 15;

        readonly bool withCountdown;
        readonly Button buttonOk;
        readonly Timer countdownTimer;
        int secondsLeft;

        public AmplitudaSerialInstructionForm(bool withCountdown)
        {
            this.withCountdown = withCountdown;

            Text = Resources.AmplitudaSerialInstructionTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = !withCountdown;      // no close box while the countdown must run to the end
            ClientSize = new Size(560, 460);

            TextBox text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                TabStop = false,
                BackColor = SystemColors.Window,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(12, 12),
                Size = new Size(536, 396),
                Text = BuildInstructionText()
            };

            buttonOk = new Button
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(460, 420),
                Size = new Size(88, 28),
                UseVisualStyleBackColor = true
            };
            buttonOk.Click += ButtonOk_Click;

            Controls.Add(text);
            Controls.Add(buttonOk);
            AcceptButton = buttonOk;

            Shown += delegate
            {
                // The text must not come up selected.
                text.SelectionStart = 0;
                text.SelectionLength = 0;
                buttonOk.Focus();
            };

            if (withCountdown)
            {
                secondsLeft = CountdownSeconds;
                buttonOk.Enabled = false;
                buttonOk.Text = string.Format(Resources.AmplitudaSerialInstructionOkCountdown, secondsLeft);
                countdownTimer = new Timer { Interval = 1000 };
                countdownTimer.Tick += CountdownTimer_Tick;
                countdownTimer.Start();
            }
            else
            {
                buttonOk.Text = Resources.AmplitudaSerialInstructionOk;
            }
        }

        static string BuildInstructionText()
        {
            string[] paragraphs =
            {
                Resources.AmplitudaSerialInstructionP01,
                Resources.AmplitudaSerialInstructionP02,
                Resources.AmplitudaSerialInstructionP03,
                Resources.AmplitudaSerialInstructionP04,
                Resources.AmplitudaSerialInstructionP05,
                Resources.AmplitudaSerialInstructionP06,
                Resources.AmplitudaSerialInstructionP07,
                Resources.AmplitudaSerialInstructionP08,
                Resources.AmplitudaSerialInstructionP09,
                Resources.AmplitudaSerialInstructionP10,
                Resources.AmplitudaSerialInstructionP13,    // number of channels: written later, belongs here
                Resources.AmplitudaSerialInstructionP11,
                Resources.AmplitudaSerialInstructionP12
            };
            return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        }

        void CountdownTimer_Tick(object sender, EventArgs e)
        {
            secondsLeft--;
            if (secondsLeft <= 0)
            {
                countdownTimer.Stop();
                buttonOk.Text = Resources.AmplitudaSerialInstructionOk;
                buttonOk.Enabled = true;
            }
            else
            {
                buttonOk.Text = string.Format(Resources.AmplitudaSerialInstructionOkCountdown, secondsLeft);
            }
        }

        void ButtonOk_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Belt and braces alongside ControlBox = false: Alt+F4 (and any other way the USER
            // asks the window to close) still raises FormClosing, so the countdown is enforced
            // here too, not only by hiding the close box. Restricted to CloseReason.UserClosing
            // (M5, add-ons review): a Windows shutdown, the owner form closing, or the
            // application exiting must never be blocked on this 15 s countdown - only a person
            // clicking the close box, pressing Alt+F4, or a parent explicitly asking THIS window
            // to close counts. Once the countdown is over this never fires again (secondsLeft
            // stays at 0), so every normal way of closing works again: OK, Enter (AcceptButton,
            // live only once OK is Enabled) or Alt+F4 - Esc does nothing either way, since no
            // CancelButton is set and ControlBox stays false while withCountdown is true.
            if (withCountdown && secondsLeft > 0 && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (countdownTimer != null)
                {
                    countdownTimer.Stop();
                    countdownTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
