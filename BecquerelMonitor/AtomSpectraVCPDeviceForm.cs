using BecquerelMonitor.Hash;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraVCPDeviceForm : InputDeviceForm
    {
        TextBox doubleTextBox2 = null;
        TextBox doubleTextBox1 = null;
        private string ComPort = "-------";
        private int BaudRate = 600000;
        bool formLoading = false;
        private double deadTime = 0;

        public AtomSpectraVCPDeviceForm()
        {
            this.InitializeComponent();
            base.DeviceTypeString = Resources.DeviceTypeAtomSpectraVCP;
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            fillPorts();
            TestConnection((string)comPortsBox.SelectedItem, int.Parse((string)baudratesBox.SelectedItem));
        }

        private void deadTimeBtn_Click(object sender, EventArgs e)
        {
            try
            {
                AtomSpectraVCPIn device = null;
                string temporaryGuid = null;
                string comPort = comPortsBox.SelectedItem.ToString();
                int baudRate = int.Parse(baudratesBox.SelectedItem.ToString());

                device = AtomSpectraVCPIn.findByPort(comPort);
                if (device == null)
                {
                    temporaryGuid = Guid.NewGuid().ToString();
                    device = AtomSpectraVCPIn.getInstance(temporaryGuid);
                    device.setPort(comPort, baudRate);
                }
                device.sendCommand("-inf");
                string[] output = device.getCommandOutput(2000).Split(' ');
                int rise = int.Parse(output[3]);
                int fall = int.Parse(output[5]);
                double f = double.Parse(output[9]);
                this.deadTime = ((double)rise + (double)fall + 1.0) / f;
                this.deadTimeLbl.Text = String.Format(Resources.DeadTimeLblText, this.deadTime * 1.0E+06);
                SetActiveDeviceConfigDirty();
                if (temporaryGuid != null)
                {
                    AtomSpectraVCPIn.cleanUp(temporaryGuid);
                }
            }
            catch
            {

            }
        }

        private void fillPorts()
        {
            string savedComPort = null;
            string savedBaudRate = null;
            if (comPortsBox.SelectedIndex != -1)
            {
                savedComPort = (string)comPortsBox.Items[comPortsBox.SelectedIndex];
                savedBaudRate = (string)baudratesBox.Items[baudratesBox.SelectedIndex];
            }
            comPortsBox.Items.Clear();
            try
            {
                string[] Ports = SerialPort.GetPortNames();
                comPortsBox.Items.AddRange(Ports);
            } catch
            {

            }

            if (comPortsBox.Items.Count > 0)
            {
                if (savedComPort != null && !this.formLoading)
                {
                    comPortsBox.SelectedIndex = comPortsBox.Items.IndexOf(savedComPort);
                    baudratesBox.SelectedIndex = baudratesBox.Items.IndexOf(savedBaudRate);
                }
                else
                {
                    if (this.ComPort != null)
                    {
                        comPortsBox.SelectedIndex = comPortsBox.Items.IndexOf(this.ComPort);
                        baudratesBox.SelectedIndex = baudratesBox.Items.IndexOf(this.BaudRate.ToString());
                    } else
                    {
                        this.ComPort = "-------";
                    }
                }
            } else
            {
                MessageBox.Show(Resources.ERRVCPComPortsNumeration);
            }

            if(comPortsBox.SelectedIndex != -1)
            {
                return;
            }

            comPortsBox.Items.Add(this.ComPort);
            comPortsBox.SelectedIndex = comPortsBox.Items.IndexOf(this.ComPort);
            baudratesBox.SelectedIndex = baudratesBox.Items.IndexOf(this.BaudRate.ToString());
        }

        private void ComPortsBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comPortsBox.SelectedItem != null && baudratesBox.SelectedItem != null && !this.formLoading)
            {
                TestConnection((string)comPortsBox.SelectedItem, int.Parse((string)baudratesBox.SelectedItem));
                SetActiveDeviceConfigDirty();
            }
        }

        private void BaudratesBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comPortsBox.SelectedItem != null && baudratesBox.SelectedItem != null && !this.formLoading)
            {
                TestConnection((string)comPortsBox.SelectedItem, int.Parse((string)baudratesBox.SelectedItem));
                SetActiveDeviceConfigDirty();
            }
        }

        // Token: 0x17000451 RID: 1105
        // (get) Token: 0x0600103D RID: 4157 RVA: 0x00059B94 File Offset: 0x00057D94
        public override TextBox UpperThresholdTextBox
        {
            get
            {
                return this.doubleTextBox2;
            }
        }

        // Token: 0x17000452 RID: 1106
        // (get) Token: 0x0600103E RID: 4158 RVA: 0x00059B9C File Offset: 0x00057D9C
        public override TextBox LowerThresholdTextBox
        {
            get
            {
                return this.doubleTextBox1;
            }
        }

        // Token: 0x0600103F RID: 4159 RVA: 0x00059BA4 File Offset: 0x00057DA4
        public AtomSpectraVCPDeviceForm(DeviceConfigForm deviceConfigForm)
        {
            this.formLoading = true;
            this.InitializeComponent();
            this.deviceConfigForm = deviceConfigForm;
            base.DeviceTypeString = Resources.DeviceTypeAtomSpectraVCP;
            this.formLoading = false;
        }

        // Token: 0x06001041 RID: 4161 RVA: 0x00059CB4 File Offset: 0x00057EB4
        public override void FormClosing()
        {

        }

        // Token: 0x06001043 RID: 4163 RVA: 0x00059D54 File Offset: 0x00057F54
        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            this.formLoading = true;
            AtomSpectraDeviceConfig atomSpectraVCPInputDevice = (AtomSpectraDeviceConfig)inputConfig;
            this.ComPort = atomSpectraVCPInputDevice.ComPortName;
            this.BaudRate = atomSpectraVCPInputDevice.BaudRate;
            fillPorts();
            this.deadTime = atomSpectraVCPInputDevice.DeadTimeValue;
            this.deadTimeLbl.Text = String.Format(Resources.DeadTimeLblText, this.deadTime * 1.0E+06);
            this.formLoading = false;
            TestConnection(this.ComPort, this.BaudRate);
        }

        // Token: 0x06001044 RID: 4164 RVA: 0x00059FDC File Offset: 0x000581DC
        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            try
            {
                AtomSpectraDeviceConfig atomSpectraVCPInputDevice = (AtomSpectraDeviceConfig)inputConfig;
                if (comPortsBox.Items.Count > 0 && comPortsBox.SelectedItem != null)
                {
                    atomSpectraVCPInputDevice.ComPortName = comPortsBox.SelectedItem.ToString();
                    atomSpectraVCPInputDevice.BaudRate = int.Parse(baudratesBox.SelectedItem.ToString());
                    atomSpectraVCPInputDevice.DeadTimeValue = deadTime;
                }
                else
                {
                    atomSpectraVCPInputDevice.ComPortName = null;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        // Token: 0x06001058 RID: 4184 RVA: 0x0005A640 File Offset: 0x00058840
        void SetActiveDeviceConfigDirty()
        {
            this.deviceConfigForm.SetActiveDeviceConfigDirty();
        }

        private void CommandLineIn_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    this.CommandLineIn.AutoCompleteCustomSource.Add(this.CommandLineIn.Text);
                    AtomSpectraVCPIn device = null;
                    string temporaryGuid = null;
                    string comPort = comPortsBox.SelectedItem.ToString();
                    int baudRate = int.Parse(baudratesBox.SelectedItem.ToString());

                    device = AtomSpectraVCPIn.findByPort(comPort);
                    if (device == null)
                    {
                        temporaryGuid = Guid.NewGuid().ToString();
                        device = AtomSpectraVCPIn.getInstance(temporaryGuid);
                        device.setPort(comPort, baudRate);
                    }
                    device.sendCommand(this.CommandLineIn.Text);
                    this.CommandLineOut.Text = ">> " + this.CommandLineIn.Text + Environment.NewLine
                        + device.getCommandOutput(2000) + Environment.NewLine + this.CommandLineOut.Text;
                    if (temporaryGuid != null)
                    {
                        AtomSpectraVCPIn.cleanUp(temporaryGuid);
                    }
                }
                catch
                {

                }
                e.SuppressKeyPress = true;
            }
        }

        (int, string) TestSerialNumber(string comPort, int baudRate)
        {
            string returnvalue = null;
            int returnstatus = -1;
            AtomSpectraVCPIn device = null;
            string temporaryGuid = null;
            try
            {
                device = AtomSpectraVCPIn.findByPort(comPort);
                if (device != null && device.BaudRate != baudRate)
                {
                    returnstatus = 1;
                    returnvalue = device.BaudRate.ToString();
                    return (returnstatus, returnvalue);
                }
                if (device == null)
                {
                    temporaryGuid = Guid.NewGuid().ToString();
                    device = AtomSpectraVCPIn.getInstance(temporaryGuid);
                    device.setPort(comPort, baudRate);
                }
                device.sendCommand("-cal");
                String result = device.getCommandOutput(2000);
                string[] separator = new string[] { "\r\n" };
                string[] result_arr = result.Split(separator, StringSplitOptions.None);
                Trace.WriteLine("result -cal array, size: " + result_arr.Length);
                if (result_arr.Length > 2)
                {
                    returnvalue = result_arr[result_arr.Length - 2];
                    returnstatus = 0;
                    Trace.WriteLine("Serial number: " + returnvalue);
                }
                if (temporaryGuid != null)
                {
                    AtomSpectraVCPIn.cleanUp(temporaryGuid);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message + " " + ex.StackTrace);
            }
            Trace.WriteLine("Return value: " + returnvalue);
            return (returnstatus, returnvalue);
        }

        void TestConnection(string comPort, int baudRate)
        {
            this.button1.Enabled = false;
            this.comPortsBox.Enabled = false;
            this.baudratesBox.Enabled = false;

            this.label3.ForeColor = Color.Gray;
            this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusTesting);

            string serialNumber = null;
            int status = -2;

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(delegate (object o, DoWorkEventArgs args)
            {
                (status, serialNumber) = TestSerialNumber(comPort, baudRate);
            });

            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(delegate (object o, RunWorkerCompletedEventArgs args)
            {
                Trace.WriteLine("Got status: " + status.ToString());
                switch (status)
                {
                    case 0:
                        this.label3.ForeColor = Color.Green;
                        this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusConnected) + Environment.NewLine +
                        "SN: " + serialNumber;
                        this.deadTimeBtn.Enabled = true;
                        break;
                    case -1:
                        this.label3.ForeColor = Color.Red;
                        this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusUnknown);
                        this.deadTimeBtn.Enabled = false;
                        break;
                    case 1:
                        this.label3.ForeColor = Color.Purple;
                        this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusBusy) + Environment.NewLine +
                        "baudrate: " + serialNumber;
                        this.deadTimeBtn.Enabled = false;
                        break;
                }

                this.button1.Enabled = true;
                this.comPortsBox.Enabled = true;
                this.baudratesBox.Enabled = true;
            });

            worker.RunWorkerAsync();
        }

    }
}
