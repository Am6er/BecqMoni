using System;
using System.Collections.Generic;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using BecquerelMonitor.Properties;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading.Tasks;
using Windows.Devices.Radios;
using System.Management.Instrumentation;

namespace BecquerelMonitor
{
    public partial class RadiaCodeDeviceForm : BecquerelMonitor.InputDeviceForm
    {
        private List<String> adressBLE = new List<String>();
        private Dictionary<ulong, BluetoothLEDevice> devices;
        private Dictionary<string, string> devicePreset;
        private BluetoothLEAdvertisementWatcher watcher;
        private BluetoothLEDevice dev;
        private string DeviceSerial;
        private int currentBLEindex = -1;
        bool formLoading = false;
        DeviceConfigForm deviceConfigForm;
        private RadiaCodeDeviceConfig config;
        private string tshootText = "";

        public RadiaCodeDeviceForm()
        {
            InitializeComponent();
        }

        public RadiaCodeDeviceForm(DeviceConfigForm deviceConfigForm)
        {
            this.formLoading = true;
            this.InitializeComponent();
            this.deviceConfigForm = deviceConfigForm;
            base.DeviceTypeString = Resources.DeviceTypeRadiaCode;
            this.formLoading = false;
            devices = new Dictionary<ulong, BluetoothLEDevice>();
            devicePreset = new Dictionary<string, string>();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.comboBox1.Items.Clear();
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
                if (null != adapter)
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
                if (watcher == null) watcher = new BluetoothLEAdvertisementWatcher();
                adressBLE.Clear();
                devices.Clear();
                Thread.Sleep(200);
                watcher.Stop();
                TroubleshootText.Clear();
                //watcher.SignalStrengthFilter.InRangeThresholdInDBm = -110;
                //watcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -110;
                watcher.ScanningMode = BluetoothLEScanningMode.Active;
                watcher.Received += Watcher_Recived;
                watcher.Start();
            } catch (Exception ex)
            {
                MessageBox.Show($"{Resources.ERRBLENotFound} Message: {ex.Message}");
                return;
            }
        }

        private async void Watcher_Recived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            dev = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            Tuple<ulong, BluetoothLEDevice> tup = new Tuple<ulong, BluetoothLEDevice>(args.BluetoothAddress, dev);

            if (dev != null && args != null)
            {
                try
                {
                    if (dev.Name == null && dev.Name.IndexOf("RadiaCode-10") == -1) return;
                } catch (Exception) { }
                if (!devices.ContainsKey(args.BluetoothAddress))
                {
                    try
                    {
                        Trace.WriteLine($"Found {dev.Name} with addr {args.BluetoothAddress}");
                        devices.Add(args.BluetoothAddress, dev);
                        String model = dev.Name.Split('#')[0];
                        devicePreset.Add(args.BluetoothAddress.ToString(), model);
                        String name = dev.Name.Split('#')[1];
                        comboBox1.Invoke(new Action(() =>
                        {
                            adressBLE.Add(dev.BluetoothAddress.ToString());
                            int item = comboBox1.Items.IndexOf(name);
                            if (item == -1) comboBox1.Items.Add(name);
                        }));
                        TroubleshootText.Invoke(new Action(() =>
                        {
                            TroubleshootText.AppendText($"Found device {name} with BLE addr {dev.BluetoothAddress}");
                        }));
                    } catch (Exception) { }
                }
            }

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.formLoading) return;
            if (watcher != null)
            {
                watcher.Stop();
                Thread.Sleep(200);
            }
            currentBLEindex = comboBox1.SelectedIndex;

            //Dialog to load RC presets for peak searching
            devicePreset.TryGetValue(adressBLE.ElementAt(currentBLEindex), out string model);
            DialogResult dialogResult = MessageBox.Show(string.Format(Resources.LoadDefaultPeakFinderSettingsQuestion, model), Resources.LoadDefaultPeakFinderSettingsQuestionTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Yes)
            {
                switch (model)
                {
                    case "RadiaCode-101": deviceConfigForm.ActiveDeviceConfig.PeakDetectionMethodConfig = RadiacodePresets.RC101_FWHM_Preset(); break;
                    case "RadiaCode-102": deviceConfigForm.ActiveDeviceConfig.PeakDetectionMethodConfig = RadiacodePresets.RC102_FWHM_Preset(); break;
                    case "RadiaCode-103": deviceConfigForm.ActiveDeviceConfig.PeakDetectionMethodConfig = RadiacodePresets.RC103_FWHM_Preset(); break;
                }
                
                deviceConfigForm.LoadPeakFinderPresetContents(deviceConfigForm.ActiveDeviceConfig);
            }

            SetActiveDeviceConfigDirty();
        }

        void SetActiveDeviceConfigDirty()
        {
            this.deviceConfigForm.SetActiveDeviceConfigDirty();
        }

        public override void FormClosing()
        {
            if (watcher != null) watcher.Stop();
        }

        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            this.formLoading = true;
            RadiaCodeDeviceConfig radiaCodeInputDevice = (RadiaCodeDeviceConfig)inputConfig;
            this.config = radiaCodeInputDevice;
            this.DeviceSerial = radiaCodeInputDevice.DeviceSerial;
            if (this.DeviceSerial != null)
            {
                comboBox1.Items.Clear();
                comboBox1.Items.Add(this.DeviceSerial);
                comboBox1.SelectedIndex = 0;
            }
            TroubleshootText.Clear();
            this.formLoading = false;
        }

        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            try
            {
                RadiaCodeDeviceConfig radiaCodeInputDevice = (RadiaCodeDeviceConfig)inputConfig;
                if (comboBox1.Items.Count > 0 && comboBox1.SelectedItem != null)
                {
                    radiaCodeInputDevice.DeviceSerial = comboBox1.SelectedItem.ToString();
                    radiaCodeInputDevice.AddressBLE = adressBLE.ElementAt(currentBLEindex);
                    radiaCodeInputDevice.RC_EnergyCalibration = radiaCodeInputDevice.RC_EnergyCalibration;
                }
                else
                {
                    radiaCodeInputDevice.DeviceSerial = null;
                    radiaCodeInputDevice.AddressBLE = null;
                    radiaCodeInputDevice.RC_EnergyCalibration = null;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        bool isRunning = false;

        private void troubleShootbtn_Click(object sender, EventArgs e)
        {
            if (!troubleShootbtn.Enabled) return;
            troubleShootbtn.Enabled = false;
            TroubleshootText.Clear();
            tshootText = "";
            List<RadiaCodeIn> instances = RadiaCodeIn.getAllInstances();
            foreach (RadiaCodeIn instance in instances)
            {
                if (instance.GUID == deviceConfigForm.ActiveDeviceConfig.Guid) {
                    tshootText += $"{System.DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss")} Radiacode instance with {deviceConfigForm.ActiveDeviceConfig.Guid} allready running. Shutdown it first." + Environment.NewLine;
                    RadiaCodeIn.cleanUp(deviceConfigForm.ActiveDeviceConfig.Guid);
                    break;
                }
            }
            tshootText += $"{System.DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss")} Starting new RadiaCodeIn instance for GUID {deviceConfigForm.ActiveDeviceConfig.Guid}" + Environment.NewLine;
            RadiaCodeIn radiaCodeIn = RadiaCodeIn.getInstance(deviceConfigForm.ActiveDeviceConfig.Guid, troubleshoot: true);
            radiaCodeIn.TroubleShoot += RadiaCodeIn_TroubleShoot;
            radiaCodeIn.setDeviceSerial(config.DeviceSerial, config.AddressBLE);
            radiaCodeIn.sendCommand("Start");
            isRunning = true;
            while(isRunning)
            {
                TroubleshootText.Text = tshootText;
                TroubleshootText.Refresh();
                Thread.Sleep(200);
            }
            radiaCodeIn.TroubleShoot -= RadiaCodeIn_TroubleShoot;
            RadiaCodeIn.cleanUp(deviceConfigForm.ActiveDeviceConfig.Guid);
            tshootText += "Finish" + Environment.NewLine;
            TroubleshootText.Text = tshootText;
            TroubleshootText.Refresh();
            troubleShootbtn.Enabled = true;
        }

        private void RadiaCodeIn_TroubleShoot(object sender, RadiaCodeTroubleShootArgs e)
        {
            Trace.WriteLine("-->> Got event: " + e.Text);
            if (e.Text.Equals("QUIT"))
            {
                isRunning = false;
                return;
            }
            tshootText += System.DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss") + " " + e.Text + Environment.NewLine;
        }
    }

    public class RadiacodePresets
    {
        public static FWHMPeakDetectionMethodConfig RC101_FWHM_Preset()
        {
            FWHMPeakDetectionMethodConfig config = new FWHMPeakDetectionMethodConfig();
            config.Ch_Concat = 1024;
            config.FWHM_AT_0 = 6;
            config.Min_FWHM_Tol = 10;
            config.Max_FWHM_Tol = 190;
            config.Ch_Fwhm = 234;
            config.Width_Fwhm = 31;
            config.Min_Range = 15;

            return config;
        }

        public static FWHMPeakDetectionMethodConfig RC102_FWHM_Preset()
        {
            FWHMPeakDetectionMethodConfig config = new FWHMPeakDetectionMethodConfig();
            config.Ch_Concat = 1024;
            config.FWHM_AT_0 = 6;
            config.Min_FWHM_Tol = 10;
            config.Max_FWHM_Tol = 190;
            config.Ch_Fwhm = 241;
            config.Width_Fwhm = 20;
            config.Min_Range = 15;

            return config;
        }

        public static FWHMPeakDetectionMethodConfig RC103_FWHM_Preset()
        {
            FWHMPeakDetectionMethodConfig config = new FWHMPeakDetectionMethodConfig();
            config.Ch_Concat = 1024;
            config.FWHM_AT_0 = 4;
            config.Min_FWHM_Tol = 10;
            config.Max_FWHM_Tol = 190;
            config.Ch_Fwhm = 266;
            config.Width_Fwhm = 26;
            config.Min_Range = 10;

            return config;
        }
    }
}
