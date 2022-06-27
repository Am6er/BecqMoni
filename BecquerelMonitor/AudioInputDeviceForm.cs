using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using WinMM;

namespace BecquerelMonitor
{
	// Token: 0x02000142 RID: 322
	public partial class AudioInputDeviceForm : InputDeviceForm
	{
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
		public AudioInputDeviceForm(DeviceConfigForm deviceConfigForm)
		{
			this.InitializeComponent();
			this.deviceConfigForm = deviceConfigForm;
			this.waveDeviceList = WaveIn.Devices;
			this.comboBox1.Items.Clear();
			foreach (WaveInDeviceCaps waveInDeviceCaps in this.waveDeviceList)
			{
				this.comboBox1.Items.Add(waveInDeviceCaps.Name);
			}
			this.pulseRecorder.StandardPulseView = this.standardPulseView1;
			this.button1.Enabled = true;
			this.button2.Enabled = false;
			base.DeviceTypeString = Resources.DeviceTypeAudioInput;
		}

		// Token: 0x06001040 RID: 4160 RVA: 0x00059C78 File Offset: 0x00057E78
		public override void Initialize()
		{
			this.timer = new Timer();
			this.timer.Interval = 50;
			this.timer.Tick += this.OnTimer;
			this.timer.Start();
		}

		// Token: 0x06001041 RID: 4161 RVA: 0x00059CB4 File Offset: 0x00057EB4
		public override void FormClosing()
		{
			if (this.pulseRecorder.Recording)
			{
				this.pulseRecorder.StopRecording();
			}
			this.timer.Stop();
		}

		// Token: 0x06001042 RID: 4162 RVA: 0x00059CDC File Offset: 0x00057EDC
		void OnTimer(object sender, EventArgs e)
		{
			if (this.deviceConfigForm.ActiveDeviceConfig != null && this.pulseRecorder.Recording)
			{
				this.textBox4.Text = this.pulseRecorder.NumberOfPulses.ToString();
				this.standardPulseView1.Invalidate();
				if (this.pulseRecorder.NumberOfPulses >= 10000)
				{
					this.StopPulseRecording();
				}
			}
		}

		// Token: 0x06001043 RID: 4163 RVA: 0x00059D54 File Offset: 0x00057F54
		public override void LoadFormContents(InputDeviceConfig inputConfig)
		{
			AudioInputDeviceConfig audioInputDeviceConfig = (AudioInputDeviceConfig)inputConfig;
			bool flag = false;
			if (audioInputDeviceConfig.AudioInputDevice != null)
			{
				int deviceId = audioInputDeviceConfig.AudioInputDevice.DeviceId;
				if (deviceId >= 0 && deviceId < this.waveDeviceList.Count && audioInputDeviceConfig.AudioInputDevice.Name == this.waveDeviceList[deviceId].Name)
				{
					this.comboBox1.SelectedIndex = deviceId;
				}
				flag = true;
			}
			if (!flag && this.waveDeviceList.Count > 0)
			{
				MessageBox.Show(Resources.ERRAudioDeviceNotFound);
				audioInputDeviceConfig.AudioInputDevice = this.waveDeviceList[0];
				this.SetActiveDeviceConfigDirty();
				this.comboBox1.SelectedIndex = 0;
			}
			this.comboBox2.Text = audioInputDeviceConfig.SamplesPerSecond.ToString();
			this.comboBox3.Text = audioInputDeviceConfig.BitsPerSample.ToString();
			this.trackBar1.Value = audioInputDeviceConfig.Volume;
			this.maskedTextBox1.Text = audioInputDeviceConfig.Volume.ToString();
			this.checkBox2.Checked = audioInputDeviceConfig.AutoVolumeSetting;
			this.checkBox1.Checked = audioInputDeviceConfig.NegativePolarity;
			PRAHomageMethodConfig prahomageMethodConfig = (PRAHomageMethodConfig)audioInputDeviceConfig.PulseDetectionMethodConfig;
			this.doubleTextBox1.Text = prahomageMethodConfig.LowerThreshold.ToString();
			this.doubleTextBox2.Text = prahomageMethodConfig.UpperThreshold.ToString();
			double pulseThreshold = prahomageMethodConfig.PulseThreshold;
			this.trackBar2.Value = (int)(pulseThreshold * 100.0);
			this.maskedTextBox2.Text = ((int)(pulseThreshold * 100.0)).ToString();
			this.numericUpDown1.Value = prahomageMethodConfig.PulseShapeSize;
			this.numericUpDown2.Value = prahomageMethodConfig.PeakIndex;
			this.doubleTextBox3.Text = prahomageMethodConfig.PulseLowerThreshold.ToString();
			this.doubleTextBox4.Text = prahomageMethodConfig.PulseUpperThreshold.ToString();
			this.textBox4.Text = prahomageMethodConfig.NumberOfPulses.ToString();
			this.standardPulseView1.PulseShape = prahomageMethodConfig.PulseShape;
			this.standardPulseView1.PeakIndex = prahomageMethodConfig.PeakIndex;
			this.standardPulseView1.PulseShapeSize = prahomageMethodConfig.PulseShapeSize;
			this.standardPulseView1.Invalidate();
		}

		// Token: 0x06001044 RID: 4164 RVA: 0x00059FDC File Offset: 0x000581DC
		public override bool SaveFormContents(InputDeviceConfig inputConfig)
		{
			try
			{
				AudioInputDeviceConfig audioInputDeviceConfig = (AudioInputDeviceConfig)inputConfig;
				if (this.comboBox1.SelectedIndex < 0)
				{
					audioInputDeviceConfig.AudioInputDevice = null;
				}
				else
				{
					audioInputDeviceConfig.AudioInputDevice = this.waveDeviceList[this.comboBox1.SelectedIndex];
				}
				audioInputDeviceConfig.SamplesPerSecond = int.Parse(this.comboBox2.Text);
				audioInputDeviceConfig.BitsPerSample = int.Parse(this.comboBox3.Text);
				audioInputDeviceConfig.Volume = int.Parse(this.maskedTextBox1.Text);
				audioInputDeviceConfig.AutoVolumeSetting = this.checkBox2.Checked;
				audioInputDeviceConfig.NegativePolarity = this.checkBox1.Checked;
				PRAHomageMethodConfig prahomageMethodConfig = (PRAHomageMethodConfig)audioInputDeviceConfig.PulseDetectionMethodConfig;
				prahomageMethodConfig.LowerThreshold = double.Parse(this.doubleTextBox1.Text);
				prahomageMethodConfig.UpperThreshold = double.Parse(this.doubleTextBox2.Text);
				if (prahomageMethodConfig.UpperThreshold < prahomageMethodConfig.LowerThreshold)
				{
					prahomageMethodConfig.UpperThreshold = prahomageMethodConfig.LowerThreshold;
					this.doubleTextBox2.Text = prahomageMethodConfig.UpperThreshold.ToString();
				}
				prahomageMethodConfig.PulseThreshold = double.Parse(this.maskedTextBox2.Text) / 100.0;
				prahomageMethodConfig.PulseShapeSize = (int)this.numericUpDown1.Value;
				prahomageMethodConfig.PeakIndex = (int)this.numericUpDown2.Value;
				prahomageMethodConfig.PulseLowerThreshold = double.Parse(this.doubleTextBox3.Text);
				prahomageMethodConfig.PulseUpperThreshold = double.Parse(this.doubleTextBox4.Text);
				if (prahomageMethodConfig.PulseUpperThreshold < prahomageMethodConfig.PulseLowerThreshold)
				{
					prahomageMethodConfig.PulseUpperThreshold = prahomageMethodConfig.PulseLowerThreshold;
					this.doubleTextBox4.Text = prahomageMethodConfig.PulseUpperThreshold.ToString();
				}
				prahomageMethodConfig.PulseShape = this.standardPulseView1.PulseShape;
				prahomageMethodConfig.NumberOfPulses = int.Parse(this.textBox4.Text);
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
			this.tempConfig = new AudioInputDeviceConfig();
			if (!this.SaveFormContents(this.tempConfig))
			{
				MessageBox.Show(Resources.ERRInvalidInputForm);
				return;
			}
			if (this.tempConfig.AudioInputDevice == null)
			{
				MessageBox.Show(Resources.ERRInputDeviceNotSet);
				return;
			}
			this.button1.Enabled = false;
			this.button2.Enabled = true;
			this.standardPulseView1.Invalidate();
			this.pulseRecorder.Initialize(this.tempConfig);
			if (this.tempConfig.AutoVolumeSetting)
			{
				int deviceId = this.tempConfig.AudioInputDevice.DeviceId;
				this.audioVolumeController = new AudioVolumeController();
				try
				{
					this.previousVolume = this.audioVolumeController.GetVolume(deviceId);
					this.audioVolumeController.SetVolume(deviceId, (float)this.tempConfig.Volume / 100f);
				}
				catch (Exception)
				{
					this.audioVolumeController = null;
				}
			}
			WaveFormat waveFormat = new WaveFormat();
			waveFormat.FormatTag = WaveFormatTag.Pcm;
			waveFormat.SamplesPerSecond = this.tempConfig.SamplesPerSecond;
			waveFormat.BitsPerSample = (short)this.tempConfig.BitsPerSample;
			waveFormat.Channels = 1;
			if (!this.pulseRecorder.StartRecording(this.tempConfig.AudioInputDevice, waveFormat, this.tempConfig.NegativePolarity))
			{
				this.button1.Enabled = true;
				this.button2.Enabled = false;
			}
		}

		// Token: 0x06001046 RID: 4166 RVA: 0x0005A37C File Offset: 0x0005857C
		void StopPulseRecording()
		{
			this.button1.Enabled = true;
			this.button2.Enabled = false;
			this.pulseRecorder.StopRecording();
			this.textBox4.Text = this.pulseRecorder.NumberOfPulses.ToString();
			this.standardPulseView1.Invalidate();
			if (this.audioVolumeController != null && this.tempConfig != null)
			{
				this.audioVolumeController.SetVolume(this.tempConfig.AudioInputDevice.DeviceId, this.previousVolume);
			}
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

		// Token: 0x0600104A RID: 4170 RVA: 0x0005A430 File Offset: 0x00058630
		void trackBar1_Scroll(object sender, EventArgs e)
		{
			this.maskedTextBox1.Text = this.trackBar1.Value.ToString();
			this.SetActiveDeviceConfigDirty();
		}

		// Token: 0x0600104B RID: 4171 RVA: 0x0005A468 File Offset: 0x00058668
		void maskedTextBox1_TextChanged(object sender, EventArgs e)
		{
			int value = 0;
			int.TryParse(this.maskedTextBox1.Text, out value);
			this.trackBar1.Value = value;
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

		// Token: 0x06001050 RID: 4176 RVA: 0x0005A4C0 File Offset: 0x000586C0
		void numericUpDown1_ValueChanged(object sender, EventArgs e)
		{
			this.standardPulseView1.PulseShapeSize = (int)this.numericUpDown1.Value;
			this.standardPulseView1.PulseShape = new double[this.standardPulseView1.PulseShapeSize];
			this.SetActiveDeviceConfigDirty();
			this.numericUpDown2.Minimum = 0m;
			this.numericUpDown2.Maximum = this.standardPulseView1.PulseShapeSize - 1;
			this.standardPulseView1.PeakIndex = (int)this.numericUpDown2.Value;
		}

		// Token: 0x06001051 RID: 4177 RVA: 0x0005A558 File Offset: 0x00058758
		void numericUpDown2_ValueChanged(object sender, EventArgs e)
		{
			this.SetActiveDeviceConfigDirty();
		}

		// Token: 0x06001052 RID: 4178 RVA: 0x0005A560 File Offset: 0x00058760
		void doubleTextBox3_TextChanged(object sender, EventArgs e)
		{
			this.SetActiveDeviceConfigDirty();
		}

		// Token: 0x06001053 RID: 4179 RVA: 0x0005A568 File Offset: 0x00058768
		void doubleTextBox4_TextChanged(object sender, EventArgs e)
		{
			this.SetActiveDeviceConfigDirty();
		}

		// Token: 0x06001054 RID: 4180 RVA: 0x0005A570 File Offset: 0x00058770
		void trackBar2_Scroll(object sender, EventArgs e)
		{
			this.maskedTextBox2.Text = this.trackBar2.Value.ToString();
			this.SetActiveDeviceConfigDirty();
		}

		// Token: 0x06001055 RID: 4181 RVA: 0x0005A5A8 File Offset: 0x000587A8
		void maskedTextBox2_TextChanged(object sender, EventArgs e)
		{
			int num = 0;
			int.TryParse(this.maskedTextBox2.Text, out num);
			if (num < 0)
			{
				num = 0;
			}
			if (num > 100)
			{
				num = 100;
			}
			this.maskedTextBox2.Text = num.ToString();
			this.trackBar2.Value = num;
			this.SetActiveDeviceConfigDirty();
		}

		// Token: 0x06001056 RID: 4182 RVA: 0x0005A608 File Offset: 0x00058808
		void button1_Click(object sender, EventArgs e)
		{
			if (this.deviceConfigForm.ActiveDeviceConfig == null)
			{
				return;
			}
			this.StartPulseRecording();
		}

		// Token: 0x06001057 RID: 4183 RVA: 0x0005A624 File Offset: 0x00058824
		void button2_Click(object sender, EventArgs e)
		{
			if (this.deviceConfigForm.ActiveDeviceConfig == null)
			{
				return;
			}
			this.StopPulseRecording();
		}

		// Token: 0x06001058 RID: 4184 RVA: 0x0005A640 File Offset: 0x00058840
		void SetActiveDeviceConfigDirty()
		{
			this.deviceConfigForm.SetActiveDeviceConfigDirty();
		}

	}
}
