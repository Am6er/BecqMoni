using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using WinMM;
using System.IO.Ports;

namespace BecquerelMonitor
{
// Token: 0x02000142 RID: 322
    public partial class AtomSpectraVCPDeviceForm : InputDeviceForm
    {
        TextBox doubleTextBox2;
        private ComboBox comPortsBox;
        private Label label1;
        TextBox doubleTextBox1;

        void InitializeComponent()
        {
            this.comPortsBox = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.CommandLineIn = new System.Windows.Forms.TextBox();
            this.CommandLineOut = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // comPortsBox
            // 
            this.comPortsBox.FormattingEnabled = true;
            this.comPortsBox.Location = new System.Drawing.Point(135, 73);
            this.comPortsBox.Name = "comPortsBox";
            this.comPortsBox.Size = new System.Drawing.Size(121, 21);
            this.comPortsBox.TabIndex = 98;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(17, 76);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(56, 13);
            this.label1.TabIndex = 99;
            this.label1.Text = "COM Port:";
            // 
            // CommandLineIn
            // 
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
            this.label2.Text = "Type command and press Enter. Some commands ARE HAZARDOUS!\r\n You must completely " +
    "sure, what you are doing.";
            // 
            // AtomSpectraVCPDeviceForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.CommandLineOut);
            this.Controls.Add(this.CommandLineIn);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comPortsBox);
            this.Name = "AtomSpectraVCPDeviceForm";
            this.Controls.SetChildIndex(this.comPortsBox, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.CommandLineIn, 0);
            this.Controls.SetChildIndex(this.CommandLineOut, 0);
            this.Controls.SetChildIndex(this.label2, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void ComPortsBox_SelectedIndexChanged(object sender, EventArgs e)
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

            comPortsBox.Items.Clear();
            comPortsBox.Items.Add("-------");
            comPortsBox.Items.AddRange(SerialPort.GetPortNames());
            if(comPortsBox.Items.Count > 1)
            {
                comPortsBox.SelectedIndex = 1;
            }
        }

        // Token: 0x06001041 RID: 4161 RVA: 0x00059CB4 File Offset: 0x00057EB4
        public override void FormClosing()
        {
        
        }

        // Token: 0x06001043 RID: 4163 RVA: 0x00059D54 File Offset: 0x00057F54
        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            AtomSpectraDeviceConfig atomSpectraVCPInputDevice = (AtomSpectraDeviceConfig)inputConfig;
            bool flag = false;
            if (atomSpectraVCPInputDevice.ComPortName != null)
            {
                foreach(string s in comPortsBox.Items){
                    if (s.Equals(atomSpectraVCPInputDevice.ComPortName))
                    {
                        comPortsBox.SelectedItem = s;
                        flag = true;
                        break;
                    }
                }
            }
            if (!flag)
            {
                //MessageBox.Show(Resources.ERRAudioDeviceNotFound);
                if (comPortsBox.Items.Count > 1)
                {
                    atomSpectraVCPInputDevice.ComPortName = comPortsBox.Items[1].ToString();
                    comPortsBox.SelectedIndex = 0;
                } else
                {
                    atomSpectraVCPInputDevice.ComPortName = "COM1";
                }
                this.SetActiveDeviceConfigDirty();
            }
        }

        // Token: 0x06001044 RID: 4164 RVA: 0x00059FDC File Offset: 0x000581DC
        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            try
            {
                AtomSpectraDeviceConfig atomSpectraVCPInputDevice = (AtomSpectraDeviceConfig)inputConfig;
                if (comPortsBox.Items.Count > 0)
                {
                    atomSpectraVCPInputDevice.ComPortName = comPortsBox.SelectedItem.ToString();
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
                    AtomSpectraVCPIn device = new AtomSpectraVCPIn("Test");
                    device.setPort(comPortsBox.SelectedItem.ToString());
                    device.sendCommand(this.CommandLineIn.Text);
                    this.CommandLineOut.Text = device.getCommandOutput(2000);
                    device.Dispose();
                }
                catch
                {
                    
                }
                this.CommandLineIn.Text = "";
                e.SuppressKeyPress = true;
            }
        }
    }
}