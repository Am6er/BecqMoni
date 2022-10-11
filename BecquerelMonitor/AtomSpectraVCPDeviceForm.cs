using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000142 RID: 322
    public partial class AtomSpectraVCPDeviceForm : InputDeviceForm
    {
        TextBox doubleTextBox2;
        private ComboBox comPortsBox;
        private ComboBox baudratesBox;
        private Label label1;
        TextBox doubleTextBox1;
        private string ComPort = "-------";
        private int BaudRate = 600000;
        AutoCompleteStringCollection autoComplete = new AutoCompleteStringCollection();

        void InitializeComponent()
        {
            this.comPortsBox = new System.Windows.Forms.ComboBox();
            this.baudratesBox = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.CommandLineIn = new System.Windows.Forms.TextBox();
            this.CommandLineOut = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // comPortsBox
            // 
            this.comPortsBox.FormattingEnabled = true;
            this.comPortsBox.Location = new System.Drawing.Point(135, 73);
            this.comPortsBox.Name = "comPortsBox";
            this.comPortsBox.Size = new System.Drawing.Size(60, 21);
            this.comPortsBox.TabIndex = 98;
            //
            // baudratesBox
            //
            this.baudratesBox.FormattingEnabled = false;
            this.baudratesBox.Location = new System.Drawing.Point(200, 73);
            this.baudratesBox.Name = "baudratesBox";
            this.baudratesBox.Size = new System.Drawing.Size(60, 21);
            this.baudratesBox.TabIndex = 105;
            this.baudratesBox.Items.AddRange(new string[] { "38400", "115200", "460800", "600000", "921600" });
            this.baudratesBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(17, 76);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(56, 13);
            this.label1.TabIndex = 99;
            this.label1.Text = Resources.MSGComPort;
            //
            // button 1
            //
            this.button1.AutoSize = true;
            this.button1.Location = new System.Drawing.Point(270, 72);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(56, 13);
            this.button1.TabIndex = 103;
            this.button1.Text = Resources.ButtonRefresh;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(340, 76);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(56, 13);
            this.label3.TabIndex = 104;
            this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusUnknown);
            // 
            // CommandLineIn
            // 
            this.CommandLineIn.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.CommandLineIn.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.CustomSource;
            this.CommandLineIn.AutoCompleteCustomSource = autoComplete;
            this.CommandLineIn.Location = new System.Drawing.Point(20, 145);
            this.CommandLineIn.Name = "CommandLineIn";
            this.CommandLineIn.Size = new System.Drawing.Size(428, 20);
            this.CommandLineIn.TabIndex = 100;
            this.CommandLineIn.KeyDown += new System.Windows.Forms.KeyEventHandler(this.CommandLineIn_KeyDown);
            // 
            // CommandLineOut
            // 
            this.CommandLineOut.Location = new System.Drawing.Point(20, 171);
            this.CommandLineOut.Multiline = true;
            this.CommandLineOut.Name = "CommandLineOut";
            this.CommandLineOut.ReadOnly = true;
            this.CommandLineOut.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.CommandLineOut.Size = new System.Drawing.Size(428, 409);
            this.CommandLineOut.TabIndex = 101;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(17, 116);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(343, 26);
            this.label2.TabIndex = 102;
            this.label2.Text = Resources.MSGAtomSpectraWarning;
            // 
            // AtomSpectraVCPDeviceForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.CommandLineOut);
            this.Controls.Add(this.CommandLineIn);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.button1);
            button1.Click += Button1_Click;
            this.Controls.Add(this.comPortsBox);
            this.Controls.Add(this.baudratesBox);
            this.Name = "AtomSpectraVCPDeviceForm";
            comPortsBox.SelectedIndexChanged += ComPortsBox_SelectedIndexChanged;
            baudratesBox.SelectedIndexChanged += BaudratesBox_SelectedIndexChanged;
            this.Controls.SetChildIndex(this.comPortsBox, 0);
            this.Controls.SetChildIndex(this.baudratesBox, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.CommandLineIn, 0);
            this.Controls.SetChildIndex(this.CommandLineOut, 0);
            this.Controls.SetChildIndex(this.label2, 0);
            this.Controls.SetChildIndex(this.label3, 0);
            this.Controls.SetChildIndex(this.button1, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void Button1_Click(object sender, EventArgs e)
        {
            fillPorts();
        }

        private void fillPorts()
        {
            comPortsBox.Items.Clear();
            comPortsBox.Items.Add("-------");
            try
            {
                string[] Ports = SerialPort.GetPortNames();
                comPortsBox.Items.AddRange(Ports);
            } catch (Exception ex)
            {

            }
            

            if (comPortsBox.Items.Count > 1)
            {
                if (this.ComPort != null && comPortsBox.Items.Contains(this.ComPort))
                {
                    comPortsBox.SelectedIndex = comPortsBox.Items.IndexOf(this.ComPort);
                    baudratesBox.SelectedIndex = baudratesBox.Items.IndexOf(this.BaudRate.ToString());
                    label3.ForeColor = Color.Green;
                    this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusConnected);
                }
                else
                {
                    //comPortsBox.Items.Add(this.ComPort);
                    //comPortsBox.SelectedIndex = comPortsBox.Items.Count - 1;
                    //label3.ForeColor = Color.Red;
                    //this.label3.Text = String.Format(Resources.LabelVCPSpectraInfo, Resources.VCPDeviceStatusUnknown);
                }
            }
        }

        private void ComPortsBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            //fillPorts();
            SetActiveDeviceConfigDirty();
        }

        private void BaudratesBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetActiveDeviceConfigDirty();
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
            this.InitializeComponent();
            this.deviceConfigForm = deviceConfigForm;
            base.DeviceTypeString = Resources.DeviceTypeAtomSpectraVCP;

            fillPorts();
        }

        // Token: 0x06001041 RID: 4161 RVA: 0x00059CB4 File Offset: 0x00057EB4
        public override void FormClosing()
        {

        }

        // Token: 0x06001043 RID: 4163 RVA: 0x00059D54 File Offset: 0x00057F54
        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            AtomSpectraDeviceConfig atomSpectraVCPInputDevice = (AtomSpectraDeviceConfig)inputConfig;
            this.ComPort = atomSpectraVCPInputDevice.ComPortName;
            this.BaudRate = atomSpectraVCPInputDevice.BaudRate;
            fillPorts();
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

        // Token: 0x06001045 RID: 4165 RVA: 0x0005A200 File Offset: 0x00058400
        void StartPulseRecording()
        {
            this.tempConfig = new AtomSpectraDeviceConfig();
            if (!this.SaveFormContents(this.tempConfig))
            {
                MessageBox.Show(Resources.ERRInvalidInputForm);
                return;
            }
            if (this.tempConfig.ComPortName == null)
            {
                MessageBox.Show(Resources.ERRInputDeviceNotSet);
                return;
            }
        }

        // Token: 0x06001046 RID: 4166 RVA: 0x0005A37C File Offset: 0x0005857C
        void StopPulseRecording()
        {

            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06001047 RID: 4167 RVA: 0x0005A418 File Offset: 0x00058618
        void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06001048 RID: 4168 RVA: 0x0005A420 File Offset: 0x00058620
        void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06001049 RID: 4169 RVA: 0x0005A428 File Offset: 0x00058628
        void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600104C RID: 4172 RVA: 0x0005A4A0 File Offset: 0x000586A0
        void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600104D RID: 4173 RVA: 0x0005A4A8 File Offset: 0x000586A8
        void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600104E RID: 4174 RVA: 0x0005A4B0 File Offset: 0x000586B0
        void doubleTextBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600104F RID: 4175 RVA: 0x0005A4B8 File Offset: 0x000586B8
        void doubleTextBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06001058 RID: 4184 RVA: 0x0005A640 File Offset: 0x00058840
        void SetActiveDeviceConfigDirty()
        {
            this.deviceConfigForm.SetActiveDeviceConfigDirty();
        }

        // Token: 0x04000998 RID: 2456
        AtomSpectraDeviceConfig tempConfig;

        private void CommandLineIn_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    autoComplete.Add(this.CommandLineIn.Text);
                    List<AtomSpectraVCPIn> instances = AtomSpectraVCPIn.getAllInstances();
                    AtomSpectraVCPIn device;
                    bool runexist = false;
                    if (instances.Count > 0)
                    {
                        device = instances[0];
                        runexist = true;
                    }
                    else
                    {
                        device = new AtomSpectraVCPIn("Test");
                        device.setPort(comPortsBox.SelectedItem.ToString(), int.Parse(baudratesBox.SelectedItem.ToString()));
                    }
                    device.sendCommand(this.CommandLineIn.Text);
                    this.CommandLineOut.Text = ">> " + this.CommandLineIn.Text + Environment.NewLine
                        + device.getCommandOutput(2000) + Environment.NewLine + this.CommandLineOut.Text;
                    if (!runexist)
                    {
                        device.Dispose();
                    }
                }
                catch
                {

                }
                //this.CommandLineIn.Text = "";
                e.SuppressKeyPress = true;
            }
        }
    }
}