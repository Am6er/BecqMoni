using System;
using System.Collections.Generic;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using BecquerelMonitor.Properties;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class RadiaCodeDeviceForm : BecquerelMonitor.InputDeviceForm
    {
        private List<String> adressBLE = new List<String>();
        private Dictionary<ulong, BluetoothLEDevice> devices;
        private BluetoothLEAdvertisementWatcher watcher;
        private BluetoothLEDevice dev;
        private string DeviceSerial;
        private int currentBLEindex = -1;
        bool formLoading = false;
        DeviceConfigForm deviceConfigForm;

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
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.comboBox1.Items.Clear();
            ScanBLEDevices();
        }

        private void ScanBLEDevices()
        {
            try
            {
                if (watcher == null) watcher = new BluetoothLEAdvertisementWatcher();
                adressBLE.Clear();
                devices.Clear();
                Thread.Sleep(200);
                watcher.Stop();
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
                    if (dev.Name == null) return;
                    if (dev.Name.IndexOf("RadiaCode-10") == -1) return;
                } catch (Exception) { }
                if (!devices.ContainsKey(args.BluetoothAddress))
                {
                    try
                    {
                        Trace.WriteLine($"Found {dev.Name} with addr {args.BluetoothAddress}");
                        devices.Add(args.BluetoothAddress, dev);
                        String name = dev.Name.Split('#')[1];
                        comboBox1.Invoke(new Action(() =>
                        {
                            adressBLE.Add(dev.BluetoothAddress.ToString());
                            int item = comboBox1.Items.IndexOf(name);
                            if (item == -1) comboBox1.Items.Add(name);
                        }));
                    } catch (Exception) { }
                }
            }

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (watcher != null)
            {
                watcher.Stop();
                Thread.Sleep(200);
            }
            currentBLEindex = comboBox1.SelectedIndex;
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
            this.DeviceSerial = radiaCodeInputDevice.DeviceSerial;
            if (this.DeviceSerial != null)
            {
                comboBox1.Items.Add(this.DeviceSerial);
                comboBox1.SelectedIndex = 0;
            }
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
                }
                else
                {
                    radiaCodeInputDevice.DeviceSerial = null;
                    radiaCodeInputDevice.AddressBLE = null;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
}
