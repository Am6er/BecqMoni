using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Settings panel of the Amplituda Serial device type. Texts come from Properties.Resources
    // (neutral English + Russian), so the form needs no .resx of its own.
    //
    // "Test" and "Find blocks" only READ (status, header, first page): they never start, stop
    // or clear a block, and they share the line with a measurement that may be running.
    public partial class AmplitudaSerialDeviceForm : InputDeviceForm
    {
        const int TestPollMs = 200;
        const int ScanAddresses = 16;
        const int TestTimeoutMs = 30000;

        readonly Timer testTimer = new Timer();
        readonly object resultsLock = new object();
        readonly List<KeyValuePair<int, AmplitudaSerialReading>> results = new List<KeyValuePair<int, AmplitudaSerialReading>>();   // address -> answer
        bool formLoading;
        AmplitudaSerialLine testLine;
        int testGeneration;
        int expectedResults;
        bool scanning;
        int testAddress;
        int testElapsedMs;

        // Instruction window state (Task 8). `instructionShown` mirrors
        // AmplitudaSerialDeviceConfig.InstructionShown, loaded in LoadFormContents and written
        // back in SaveFormContents, the same way every other field on this panel works.
        // `loadToken` is bumped on every LoadFormContents call and captured by the deferred
        // automatic-show callback below, so a callback scheduled for an earlier config (or an
        // earlier load of the same config) never fires once a newer LoadFormContents has run -
        // that is what keeps the automatic window from ever opening twice or for the wrong
        // config after a quick re-selection.
        bool instructionShown;
        int loadToken;
        AmplitudaSerialInstructionForm openInstructionForm;

        public AmplitudaSerialDeviceForm()
        {
            InitializeComponent();
            ApplyTexts();
            WireTestTimer();
        }

        public AmplitudaSerialDeviceForm(DeviceConfigForm deviceConfigForm) : base(deviceConfigForm)
        {
            formLoading = true;
            InitializeComponent();
            ApplyTexts();
            WireTestTimer();
            base.DeviceTypeString = Resources.DeviceTypeAmplitudaSerial;
            formLoading = false;
        }

        void ApplyTexts()
        {
            labelPort.Text = Resources.AmplitudaSerialPort;
            buttonRefresh.Text = Resources.AmplitudaSerialRefresh;
            labelAddress.Text = Resources.AmplitudaSerialAddress;
            labelPoll.Text = Resources.AmplitudaSerialPoll;
            labelGap.Text = Resources.AmplitudaSerialGap;
            labelDeadTime.Text = Resources.AmplitudaSerialDeadTime;
            labelSettle.Text = Resources.AmplitudaSerialSettle;
            labelHint.Text = Resources.AmplitudaSerialHint;
            buttonTest.Text = Resources.AmplitudaSerialTest;
            buttonScan.Text = Resources.AmplitudaSerialScan;
            buttonInstruction.Text = Resources.AmplitudaSerialInstruction;
        }

        void WireTestTimer()
        {
            testTimer.Interval = TestPollMs;
            testTimer.Tick += testTimer_Tick;
        }

        // Every port Windows knows now; a saved port that is absent is kept in the list
        // (a USB adapter may simply be unplugged) instead of being silently dropped.
        void FillPorts(string selectPort)
        {
            bool wasLoading = formLoading;
            formLoading = true;
            comboPort.Items.Clear();
            string[] ports;
            try
            {
                ports = SerialPort.GetPortNames();
            }
            catch (Exception)
            {
                ports = new string[0];
            }
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
            int select = -1;
            foreach (string port in ports)
            {
                comboPort.Items.Add(port);
                if (string.Equals(port, selectPort, StringComparison.OrdinalIgnoreCase))
                {
                    select = comboPort.Items.Count - 1;
                }
            }
            if (select < 0 && !string.IsNullOrEmpty(selectPort))
            {
                comboPort.Items.Add(selectPort);
                select = comboPort.Items.Count - 1;
            }
            if (select < 0 && comboPort.Items.Count > 0)
            {
                select = 0;
            }
            comboPort.SelectedIndex = select;
            formLoading = wasLoading;
        }

        string SelectedPort()
        {
            return comboPort.SelectedItem as string ?? "";
        }

        static decimal Clamp(decimal value, NumericUpDown box)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            StopTest();
            formLoading = true;
            int token = ++loadToken;    // invalidates any automatic-show callback still pending
            AmplitudaSerialDeviceConfig config = (AmplitudaSerialDeviceConfig)inputConfig;
            numericAddress.Value = Clamp(config.Address, numericAddress);
            numericPoll.Value = Clamp(config.PollSeconds, numericPoll);
            numericGap.Value = Clamp(config.GapMilliseconds, numericGap);
            numericDeadTime.Value = Clamp((decimal)config.DeadTimeMicroseconds, numericDeadTime);
            numericSettle.Value = Clamp(config.HvSettleSeconds, numericSettle);
            labelTestResult.Text = "";
            FillPorts(config.PortName);
            instructionShown = config.InstructionShown;
            formLoading = false;

            if (!instructionShown)
            {
                ScheduleAutomaticInstruction(token);
            }
        }

        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            try
            {
                AmplitudaSerialDeviceConfig config = (AmplitudaSerialDeviceConfig)inputConfig;
                config.PortName = SelectedPort();
                config.Address = (int)numericAddress.Value;
                config.PollSeconds = (int)numericPoll.Value;
                config.GapMilliseconds = (int)numericGap.Value;
                config.DeadTimeMicroseconds = (double)numericDeadTime.Value;
                config.HvSettleSeconds = (int)numericSettle.Value;
                config.InstructionShown = instructionShown;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public override void FormClosing()
        {
            StopTest();
        }

        // Deferred, once-per-load automatic showing of the instruction window (Task 8).
        //
        // Call sites of InputDeviceForm.LoadFormContents (see DeviceConfigForm): selecting a
        // config row, "New"/"Duplicate", reloading after an external change, and switching the
        // device type combo to AmplitudaSerial. None of them run while the config DIALOG itself
        // is under construction (LoadFormContents is always called explicitly, after
        // InitializeComponent has already returned), so the window can never open during
        // construction. What LoadFormContents runs INSIDE is DeviceConfigForm's own
        // "contentsLoading = true" section, so calling deviceConfigForm.SetActiveDeviceConfigDirty()
        // synchronously from here would be silently swallowed - which is exactly why the window
        // (and the resulting dirty-marking, in OpenInstructionForm) is deferred with BeginInvoke
        // until after this call, and the whole LoadFormContents chain, has returned control to
        // the message loop.
        void ScheduleAutomaticInstruction(int token)
        {
            if (IsDisposed)
            {
                return;
            }
            if (IsHandleCreated)
            {
                BeginInvoke(new MethodInvoker(delegate { ShowAutomaticInstructionIfDue(token); }));
                return;
            }
            // The panel may not have a window handle yet right after PrepareDeviceForm just
            // created it (freshly switched device type). Wait for one, then defer as usual.
            EventHandler onHandleCreated = null;
            onHandleCreated = delegate
            {
                HandleCreated -= onHandleCreated;
                BeginInvoke(new MethodInvoker(delegate { ShowAutomaticInstructionIfDue(token); }));
            };
            HandleCreated += onHandleCreated;
        }

        void ShowAutomaticInstructionIfDue(int token)
        {
            // Stale callback (a newer LoadFormContents ran since this one was scheduled), the
            // flag was already set some other way, the window is already open (re-entrancy), the
            // panel/its form is gone by now, or the device type was switched away from
            // AmplitudaSerial in the meantime (PrepareDeviceForm detaches the old panel by
            // clearing its parent's Controls, which orphans it - IsDisposed alone would not
            // catch that, since Controls.Clear() does not dispose the control it removes) - in
            // every case, do nothing.
            if (token != loadToken || instructionShown || openInstructionForm != null ||
                IsDisposed || !IsHandleCreated || Parent == null)
            {
                return;
            }
            OpenInstructionForm(true);
        }

        void buttonInstruction_Click(object sender, EventArgs e)
        {
            OpenInstructionForm(false);
        }

        // Shared by the automatic and the manual path. Never opens a second window while one is
        // already up (ShowDialog below also disables the owner form for the duration, so no
        // re-entrant click or config switch can reach here while it is showing).
        void OpenInstructionForm(bool withCountdown)
        {
            if (openInstructionForm != null)
            {
                return;
            }
            AmplitudaSerialInstructionForm form = new AmplitudaSerialInstructionForm(withCountdown);
            openInstructionForm = form;
            try
            {
                Form owner = FindForm();
                if (owner != null)
                {
                    form.ShowDialog(owner);
                }
                else
                {
                    form.ShowDialog();
                }
            }
            finally
            {
                openInstructionForm = null;
                form.Dispose();
            }
            if (!instructionShown)
            {
                instructionShown = true;
                if (!formLoading && deviceConfigForm != null)
                {
                    deviceConfigForm.SetActiveDeviceConfigDirty();
                }
            }
        }

        void Setting_Changed(object sender, EventArgs e)
        {
            if (!formLoading && deviceConfigForm != null)
            {
                deviceConfigForm.SetActiveDeviceConfigDirty();
            }
        }

        void buttonRefresh_Click(object sender, EventArgs e)
        {
            FillPorts(SelectedPort());
        }

        void buttonTest_Click(object sender, EventArgs e)
        {
            BeginTest(false);
        }

        void buttonScan_Click(object sender, EventArgs e)
        {
            BeginTest(true);
        }

        // Requests go to the shared line; results come back on the line thread into a locked
        // list, and a UI timer waits for them: the UI thread never blocks.
        void BeginTest(bool scan)
        {
            StopTest();
            string port = SelectedPort();
            if (string.IsNullOrEmpty(port))
            {
                labelTestResult.Text = Resources.ERRAmplitudaSerialNoPort;
                return;
            }
            AmplitudaSerialSettings settings = new AmplitudaSerialSettings { PortName = port, GapMs = (int)numericGap.Value };
            string error;
            testLine = AmplitudaSerialLine.Acquire(settings, out error);
            if (testLine == null)
            {
                labelTestResult.Text = AmplitudaSerialDeviceController.DescribeAcquireError(port, error);
                return;
            }
            scanning = scan;
            testAddress = (int)numericAddress.Value;
            testElapsedMs = 0;
            int generation;
            lock (resultsLock)
            {
                generation = ++testGeneration;
                results.Clear();
            }
            expectedResults = scan ? ScanAddresses : 1;
            for (int i = 0; i < expectedResults; i++)
            {
                AmplitudaSerialRequest request = new AmplitudaSerialRequest();
                request.Kind = scan ? AmplitudaSerialRequestKind.Probe : AmplitudaSerialRequestKind.Read;
                request.Address = (byte)(scan ? i : testAddress);
                request.Channels = AmplitudaSerialProtocol.ChannelsPerPage;     // one page is enough for a test
                int asked = request.Address;
                request.Completed = delegate (AmplitudaSerialReading reading)
                {
                    lock (resultsLock)
                    {
                        if (generation == testGeneration)
                        {
                            results.Add(new KeyValuePair<int, AmplitudaSerialReading>(asked, reading));
                        }
                    }
                };
                testLine.Enqueue(request);
            }
            buttonTest.Enabled = false;
            buttonScan.Enabled = false;
            labelTestResult.Text = Resources.AmplitudaSerialTesting;
            testTimer.Start();
        }

        void testTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                testElapsedMs += TestPollMs;
                List<KeyValuePair<int, AmplitudaSerialReading>> done;
                lock (resultsLock)
                {
                    if (results.Count < expectedResults && testElapsedMs < TestTimeoutMs)
                    {
                        return;
                    }
                    done = new List<KeyValuePair<int, AmplitudaSerialReading>>(results);
                }
                string text;
                if (scanning)
                {
                    List<string> found = new List<string>();
                    foreach (KeyValuePair<int, AmplitudaSerialReading> answer in done)
                    {
                        if (answer.Value.Ok)
                        {
                            found.Add(answer.Key.ToString());
                        }
                    }
                    text = found.Count > 0
                        ? string.Format(Resources.AmplitudaSerialScanResult, string.Join(", ", found.ToArray()))
                        : Resources.AmplitudaSerialScanNone;
                }
                else if (done.Count > 0 && done[0].Value.Ok)
                {
                    AmplitudaSerialReading reading = done[0].Value;
                    text = string.Format(Resources.AmplitudaSerialTestResult, testAddress,
                        reading.Running ? Resources.AmplitudaSerialAcquiring : Resources.AmplitudaSerialStopped,
                        reading.LiveSeconds);
                }
                else
                {
                    text = string.Format(Resources.AmplitudaSerialTestNoAnswer, testAddress);
                }
                StopTest();
                labelTestResult.Text = text;
            }
            catch (Exception ex)
            {
                StopTest();
                labelTestResult.Text = string.Format(Resources.ERRAmplitudaSerialError, ex.Message);
            }
        }

        // Idempotent; also called from Dispose and FormClosing so a test line can never leak.
        void StopTest()
        {
            testTimer.Stop();
            lock (resultsLock)
            {
                testGeneration++;
            }
            if (testLine != null)
            {
                testLine.Release();
                testLine = null;
            }
            if (buttonTest != null)
            {
                buttonTest.Enabled = true;
            }
            if (buttonScan != null)
            {
                buttonScan.Enabled = true;
            }
        }
    }
}
