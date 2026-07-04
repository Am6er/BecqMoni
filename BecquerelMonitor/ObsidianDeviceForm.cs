using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;

namespace BecquerelMonitor
{
    public partial class ObsidianDeviceForm : InputDeviceForm
    {
        private readonly List<string> addressBLE = new List<string>();
        private HashSet<ulong> devices;
        private BluetoothLEAdvertisementWatcher watcher;
        private string DeviceSerial;
        private int currentBLEindex = -1;
        private bool formLoading = false;
        private ObsidianDeviceConfig config;
        private string tshootText = "";
        private bool isRunning = false;

        public ObsidianDeviceForm()
        {
            InitializeComponent();
            devices = new HashSet<ulong>();
        }

        public ObsidianDeviceForm(DeviceConfigForm deviceConfigForm)
        {
            formLoading = true;
            InitializeComponent();
            this.deviceConfigForm = deviceConfigForm;
            base.DeviceTypeString = Resources.DeviceTypeObsidian;
            formLoading = false;
            devices = new HashSet<ulong>();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            comboBox1.Items.Clear();
            ScanBLEDevices();
        }

        private async Task TestBT()
        {
            try
            {
                Trace.WriteLine("Check BT status");
                RadioAccessStatus access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed)
                {
                    return;
                }
                BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync();
                if (adapter != null)
                {
                    Radio btRadio = await adapter.GetRadioAsync();
                    if (btRadio.State != RadioState.On)
                    {
                        Trace.WriteLine("BT was disabled, enabling it");
                        await btRadio.SetStateAsync(RadioState.On);
                    }
                    else
                    {
                        Trace.WriteLine($"BT status: {btRadio.State}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception while enabling BT: {ex.Message} {ex.StackTrace}");
            }
        }

        private async void ScanBLEDevices()
        {
            try
            {
                await TestBT();
                if (watcher == null)
                {
                    watcher = new BluetoothLEAdvertisementWatcher();
                }
                addressBLE.Clear();
                devices.Clear();
                Thread.Sleep(200);
                watcher.Stop();
                watcher.Received -= Watcher_Recived;
                TroubleshootText.Clear();
                watcher.ScanningMode = BluetoothLEScanningMode.Active;
                watcher.Received += Watcher_Recived;
                watcher.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Resources.ERRBLENotFoundObsidian} Message: {ex.Message}");
            }
        }

        private void Watcher_Recived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                if (args == null)
                {
                    return;
                }
                string deviceName = args.Advertisement?.LocalName;
                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    return;
                }
                if (deviceName.IndexOf("Obsidian", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    return;
                }
                if (devices == null)
                {
                    devices = new HashSet<ulong>();
                }
                if (devices.Contains(args.BluetoothAddress))
                {
                    return;
                }

                Trace.WriteLine($"Found {deviceName} with addr {args.BluetoothAddress}");
                devices.Add(args.BluetoothAddress);
                string serial = ExtractSerial(deviceName);
                comboBox1.Invoke(new Action(() =>
                {
                    addressBLE.Add(args.BluetoothAddress.ToString());
                    if (comboBox1.Items.IndexOf(serial) == -1)
                    {
                        comboBox1.Items.Add(serial);
                    }
                    if (!comboBox1.DroppedDown)
                    {
                        comboBox1.DroppedDown = true;
                    }
                }));
                TroubleshootText.Invoke(new Action(() =>
                {
                    TroubleshootText.AppendText($"Found device {serial} with BLE addr {args.BluetoothAddress}{Environment.NewLine}");
                }));
            }
            catch (Exception)
            {
            }
        }

        private static string ExtractSerial(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }
            int separatorIndex = name.IndexOf(' ');
            if (separatorIndex >= 0 && separatorIndex < name.Length - 1)
            {
                return name.Substring(separatorIndex + 1).Trim();
            }
            return name.Trim();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (formLoading)
            {
                return;
            }
            if (watcher != null)
            {
                watcher.Stop();
                watcher.Received -= Watcher_Recived;
                Thread.Sleep(200);
            }
            currentBLEindex = comboBox1.SelectedIndex;
            if (addressBLE.Count <= currentBLEindex)
            {
                return;
            }

            DialogResult dialogResult = MessageBox.Show(
                string.Format(Resources.LoadDefaultPeakFinderSettingsQuestion, "Obsidian"),
                Resources.LoadDefaultPeakFinderSettingsQuestionTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Yes)
            {
                deviceConfigForm.ActiveDeviceConfig.PeakDetectionMethodConfig = ObsidianPresets.DefaultFwhmPreset();
                deviceConfigForm.LoadPeakFinderPresetContents(deviceConfigForm.ActiveDeviceConfig);
            }

            troubleShootbtn.Enabled = true;
            SetActiveDeviceConfigDirty();
        }

        void SetActiveDeviceConfigDirty()
        {
            deviceConfigForm.SetActiveDeviceConfigDirty();
        }

        public override void FormClosing()
        {
            if (watcher != null)
            {
                watcher.Stop();
                watcher.Received -= Watcher_Recived;
            }
        }

        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            formLoading = true;
            troubleShootbtn.Enabled = false;
            ObsidianDeviceConfig obsidianInputDevice = (ObsidianDeviceConfig)inputConfig;
            config = obsidianInputDevice;
            DeviceSerial = obsidianInputDevice.DeviceSerial;
            addressBLE.Clear();
            if (DeviceSerial != null)
            {
                comboBox1.Items.Clear();
                comboBox1.Items.Add(DeviceSerial);
                comboBox1.SelectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(obsidianInputDevice.AddressBLE))
                {
                    addressBLE.Add(obsidianInputDevice.AddressBLE);
                    currentBLEindex = 0;
                }
                troubleShootbtn.Enabled = true;
            }
            TroubleshootText.Clear();
            formLoading = false;
        }

        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            try
            {
                ObsidianDeviceConfig obsidianInputDevice = (ObsidianDeviceConfig)inputConfig;
                if (comboBox1.Items.Count > 0 && comboBox1.SelectedItem != null && currentBLEindex >= 0 && addressBLE.Count > currentBLEindex)
                {
                    obsidianInputDevice.DeviceSerial = comboBox1.SelectedItem.ToString();
                    obsidianInputDevice.AddressBLE = addressBLE.ElementAt(currentBLEindex);
                    troubleShootbtn.Enabled = true;
                }
                else
                {
                    obsidianInputDevice.DeviceSerial = null;
                    obsidianInputDevice.AddressBLE = null;
                    obsidianInputDevice.OBS_EnergyCalibration = null;
                    troubleShootbtn.Enabled = false;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private void troubleShootbtn_Click(object sender, EventArgs e)
        {
            if (config.DeviceSerial == null || config.AddressBLE == null)
            {
                MessageBox.Show(Resources.ERREmptyObsidianData);
                return;
            }
            if (!troubleShootbtn.Enabled)
            {
                return;
            }
            troubleShootbtn.Enabled = false;
            TroubleshootText.Clear();
            tshootText = "";
            List<ObsidianIn> instances = ObsidianIn.getAllInstances();
            foreach (ObsidianIn instance in instances)
            {
                if (instance.GUID == deviceConfigForm.ActiveDeviceConfig.Guid)
                {
                    tshootText += $"{DateTime.Now:dd-MM-yyyy HH:mm:ss} Obsidian instance with {deviceConfigForm.ActiveDeviceConfig.Guid} already running. Shutdown it first.{Environment.NewLine}";
                    ObsidianIn.cleanUp(deviceConfigForm.ActiveDeviceConfig.Guid);
                    break;
                }
            }
            tshootText += $"{DateTime.Now:dd-MM-yyyy HH:mm:ss} Starting new ObsidianIn instance for GUID {deviceConfigForm.ActiveDeviceConfig.Guid}{Environment.NewLine}";
            ObsidianIn obsidianIn = ObsidianIn.getInstance(deviceConfigForm.ActiveDeviceConfig.Guid, troubleshoot: true);
            obsidianIn.TroubleShoot += ObsidianIn_TroubleShoot;
            obsidianIn.setDeviceSerial(config.DeviceSerial, config.AddressBLE);
            obsidianIn.sendCommand("Start");
            isRunning = true;
            int counter = 0;
            while (isRunning && counter <= 75)
            {
                counter++;
                TroubleshootText.Text = tshootText;
                TroubleshootText.Refresh();
                Thread.Sleep(200);
            }
            obsidianIn.TroubleShoot -= ObsidianIn_TroubleShoot;
            ObsidianIn.cleanUp(deviceConfigForm.ActiveDeviceConfig.Guid);
            if (counter >= 75)
            {
                tshootText += $"Error. Timeout troubleshooting.{Environment.NewLine}";
            }
            else
            {
                tshootText += $"Finish{Environment.NewLine}";
            }
            TroubleshootText.Text = tshootText;
            TroubleshootText.Refresh();
            troubleShootbtn.Enabled = true;
        }

        private void ObsidianIn_TroubleShoot(object sender, ObsidianTroubleShootArgs e)
        {
            Trace.WriteLine("-->> Got event: " + e.Text);
            if (e.Text.Equals("QUIT"))
            {
                isRunning = false;
                return;
            }
            tshootText += DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss") + " " + e.Text + Environment.NewLine;
        }
    }

    public class ObsidianPresets
    {
        public static FWHMPeakDetectionMethodConfig DefaultFwhmPreset()
        {
            FWHMPeakDetectionMethodConfig config = new FWHMPeakDetectionMethodConfig();
            config.Ch_Concat = 1024;
            config.FWHM_AT_0 = 5.59394239000857;
            config.Min_FWHM_Tol = 1;
            config.Max_FWHM_Tol = 199;
            config.Ch_Fwhm = 291;
            config.Width_Fwhm = 39;
            config.Min_Range = 30;
            config.FwhmCalibration = CreateSqrtCalibration();
            return config;
        }

        private static FwhmCalibration CreateSqrtCalibration()
        {
            return new SqrtFwhmCalibration
            {
                CalibrationPeaks = new List<CalibrationPeak>
                {
                    new CalibrationPeak { Channel = 32, Energy = 59.54, FWHM = 13 },
                    new CalibrationPeak { Channel = 139, Energy = 300.02956878446986, FWHM = 24 },
                    new CalibrationPeak { Channel = 291, Energy = 654.51183387815468, FWHM = 39 },
                    new CalibrationPeak { Channel = 620, Energy = 1473.44192432156, FWHM = 65 }
                },
                Coefficients = new[]
                {
                    31.292193476365014,
                    3.4918724129749568,
                    0.0052870613233780717
                },
                PeakType = FwhmCalibration.GaussianPeakType,
                ExpGaussExpLeftTail = 1.0,
                ExpGaussExpRightTail = 1.0,
                VoigtSigma = 1.0,
                VoigtGamma = 1.0,
                GaussianChi2Total = 2861.9689761169598,
                ExpGaussExpChi2Total = 2861.8619262196221,
                VoigtChi2Total = 2868.8536653647052,
                Chi2pNdp = 9.50820257846166
            };
        }
    }
}
