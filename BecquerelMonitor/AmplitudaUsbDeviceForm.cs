using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Settings panel of the Amplituda USB device type. Texts come from Properties.Resources
    // (neutral English + Russian), so the form needs no .resx of its own.
    public partial class AmplitudaUsbDeviceForm : InputDeviceForm
    {
        sealed class DeviceItem
        {
            public string Serial;
            public string Text;

            public override string ToString()
            {
                return Text;
            }
        }

        const int TestDurationMs = 3000;
        const int TestPollMs = 250;

        readonly Timer testTimer = new Timer();
        readonly Stopwatch testClock = new Stopwatch();
        bool formLoading;
        int vendorId = 0x534B;
        int productId = 0x0874;
        AmplitudaUsbReader testReader;
        long testFrames;
        long testEvents;
        long testDeviceMicros;
        long testMalformed;

        public AmplitudaUsbDeviceForm()
        {
            InitializeComponent();
            ApplyTexts();
            WireTestTimer();
        }

        public AmplitudaUsbDeviceForm(DeviceConfigForm deviceConfigForm) : base(deviceConfigForm)
        {
            formLoading = true;
            InitializeComponent();
            ApplyTexts();
            WireTestTimer();
            base.DeviceTypeString = Resources.DeviceTypeAmplitudaUSB;
            formLoading = false;
        }

        void ApplyTexts()
        {
            labelDevice.Text = Resources.AmplitudaUsbDevice;
            buttonRefresh.Text = Resources.AmplitudaUsbRefresh;
            labelLower.Text = Resources.AmplitudaUsbLowerThreshold;
            labelUpper.Text = Resources.AmplitudaUsbUpperThreshold;
            labelDeadTime.Text = Resources.AmplitudaUsbDeadTime;
            labelHint.Text = Resources.AmplitudaUsbChannelsHint;
            buttonTest.Text = Resources.AmplitudaUsbTest;
        }

        void WireTestTimer()
        {
            testTimer.Interval = TestPollMs;
            testTimer.Tick += testTimer_Tick;
        }

        // "(first found)" + every connected unit; a saved serial that is absent now is kept
        // in the list as "not connected" instead of being silently dropped.
        void FillDevices(string selectSerial)
        {
            bool wasLoading = formLoading;
            formLoading = true;
            comboDevice.Items.Clear();
            comboDevice.Items.Add(new DeviceItem { Serial = "", Text = Resources.AmplitudaUsbFirstFound });
            List<AmplitudaUsbDeviceInfo> found;
            try
            {
                found = AmplitudaUsbReader.FindDevices(vendorId, productId);
            }
            catch (Exception)
            {
                found = new List<AmplitudaUsbDeviceInfo>();
            }
            int select = 0;
            foreach (AmplitudaUsbDeviceInfo device in found)
            {
                comboDevice.Items.Add(new DeviceItem { Serial = device.SerialNumber, Text = device.ToString() });
                if (!string.IsNullOrEmpty(selectSerial) && string.Equals(device.SerialNumber, selectSerial, StringComparison.OrdinalIgnoreCase))
                {
                    select = comboDevice.Items.Count - 1;
                }
            }
            if (!string.IsNullOrEmpty(selectSerial) && select == 0)
            {
                comboDevice.Items.Add(new DeviceItem { Serial = selectSerial, Text = string.Format(Resources.AmplitudaUsbNotConnected, selectSerial) });
                select = comboDevice.Items.Count - 1;
            }
            comboDevice.SelectedIndex = select;
            formLoading = wasLoading;
        }

        string SelectedSerial()
        {
            DeviceItem item = comboDevice.SelectedItem as DeviceItem;
            return item != null ? item.Serial : "";
        }

        static decimal Clamp(decimal value, NumericUpDown box)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            StopTest();
            formLoading = true;
            AmplitudaUsbDeviceConfig config = (AmplitudaUsbDeviceConfig)inputConfig;
            vendorId = config.VendorId;
            productId = config.ProductId;
            numericLower.Value = Clamp(config.LowerThreshold, numericLower);
            numericUpper.Value = Clamp(config.UpperThreshold, numericUpper);
            numericDeadTime.Value = Clamp((decimal)config.DeadTimeMicroseconds, numericDeadTime);
            labelTestResult.Text = "";
            FillDevices(config.SerialNumber);
            formLoading = false;
        }

        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            try
            {
                AmplitudaUsbDeviceConfig config = (AmplitudaUsbDeviceConfig)inputConfig;
                config.VendorId = vendorId;
                config.ProductId = productId;
                config.SerialNumber = SelectedSerial();
                int lower = (int)numericLower.Value;
                int upper = (int)numericUpper.Value;
                config.LowerThreshold = Math.Min(lower, upper);
                config.UpperThreshold = Math.Max(lower, upper);
                config.DeadTimeMicroseconds = (double)numericDeadTime.Value;
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

        void Setting_Changed(object sender, EventArgs e)
        {
            if (!formLoading && deviceConfigForm != null)
            {
                deviceConfigForm.SetActiveDeviceConfigDirty();
            }
        }

        void buttonRefresh_Click(object sender, EventArgs e)
        {
            FillDevices(SelectedSerial());
        }

        // Three seconds on a temporary reader, polled by a UI timer: the UI thread never blocks.
        void buttonTest_Click(object sender, EventArgs e)
        {
            StopTest();
            string serial = SelectedSerial();
            testReader = new AmplitudaUsbReader(vendorId, productId, serial, (double)numericDeadTime.Value);
            int error;
            if (!testReader.Start(out error))
            {
                StopTest();
                labelTestResult.Text = AmplitudaUsbDeviceController.DescribeOpenError(error, serial);
                return;
            }
            testFrames = 0;
            testEvents = 0;
            testDeviceMicros = 0;
            testMalformed = 0;
            buttonTest.Enabled = false;
            labelTestResult.Text = Resources.AmplitudaUsbTestRunning;
            testClock.Restart();
            testTimer.Start();
        }

        void TakeTestBatch()
        {
            AmplitudaUsbBatch batch = testReader.TakeBatch();
            testFrames += batch.Frames;
            testEvents += batch.Count;
            testDeviceMicros += batch.RealMicros;
            testMalformed += batch.MalformedFrames;
        }

        void testTimer_Tick(object sender, EventArgs e)
        {
            if (testReader == null)
            {
                StopTest();
                return;
            }
            try
            {
                TakeTestBatch();
                bool failed = testReader.State == AmplitudaUsbReaderState.Failed;
                if (!failed && testClock.ElapsedMilliseconds < TestDurationMs)
                {
                    return;
                }
                testClock.Stop();
                int lastError = testReader.LastError;
                testReader.Stop();
                TakeTestBatch();
                double wall = testClock.Elapsed.TotalSeconds;
                double device = testDeviceMicros / 1.0E+06;
                string text = string.Format(Resources.AmplitudaUsbTestResult,
                    wall > 0 ? testFrames / wall : 0.0,
                    device > 0 ? testEvents / device : 0.0,
                    wall > 0 ? Math.Max(0.0, 100.0 * (1.0 - device / wall)) : 0.0,
                    testMalformed);
                if (failed)
                {
                    text = AmplitudaUsbDeviceController.DescribeRuntimeError(lastError) + Environment.NewLine + text;
                }
                StopTest();
                labelTestResult.Text = text;
            }
            catch (Exception ex)
            {
                StopTest();
                labelTestResult.Text = string.Format(Resources.ERRAmplitudaUsbOpenFailed, ex.Message);
            }
        }

        // Idempotent; also called from Dispose and FormClosing so a test reader can never leak.
        void StopTest()
        {
            testTimer.Stop();
            if (testReader != null)
            {
                testReader.Dispose();
                testReader = null;
            }
            if (buttonTest != null)
            {
                buttonTest.Enabled = true;
            }
        }
    }
}
