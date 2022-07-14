using CoreAudioApi;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x020000C2 RID: 194
    public partial class CoreAudioTestForm : Form
    {
        // Token: 0x06000967 RID: 2407
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint waveInMessage(UIntPtr deviceID, uint uMsg, ref uint dwParam1, ref uint dwParam2);

        // Token: 0x06000968 RID: 2408
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint waveInMessage(UIntPtr deviceID, uint uMsg, StringBuilder dwParam1, ref uint dwParam2);

        // Token: 0x06000969 RID: 2409 RVA: 0x0003720C File Offset: 0x0003540C
        public CoreAudioTestForm()
        {
            this.InitializeComponent();
            uint value = 0u;
            uint capacity = 0u;
            uint num = 0u;
            CoreAudioTestForm.waveInMessage(new UIntPtr(value), 2066u, ref capacity, ref num);
            StringBuilder stringBuilder = new StringBuilder((int)capacity);
            CoreAudioTestForm.waveInMessage(new UIntPtr(value), 2065u, stringBuilder, ref capacity);
            this.label1.Text = stringBuilder.ToString();
            MMDeviceEnumerator mmdeviceEnumerator = new MMDeviceEnumerator();
            MMDeviceCollection mmdeviceCollection = mmdeviceEnumerator.EnumerateAudioEndPoints(EDataFlow.eCapture, EDeviceState.DEVICE_STATEMASK_ALL);
            Guid guid = new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc");
            for (int i = 0; i < mmdeviceCollection.Count; i++)
            {
                MMDevice mmdevice = mmdeviceCollection[i];
                string friendlyName = mmdevice.FriendlyName;
                PropertyStore properties = mmdevice.Properties;
                Row row = new Row();
                row.Cells.Add(new Cell(mmdevice.ID));
                row.Cells.Add(new Cell(mmdevice.FriendlyName));
                row.Cells.Add(new Cell(properties[guid].Value.ToString()));
                this.tableModel1.Rows.Add(row);
                if (stringBuilder.ToString() == mmdevice.ID)
                {
                    this.device = mmdevice;
                }
            }
            if (this.device != null)
            {
                this.trackBar1.Value = (int)(this.device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
            }
        }

        // Token: 0x0600096A RID: 2410 RVA: 0x00037384 File Offset: 0x00035584
        void trackBar1_Scroll(object sender, EventArgs e)
        {
            if (this.device != null)
            {
                this.device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)this.trackBar1.Value / 100f;
            }
        }

        // Token: 0x04000535 RID: 1333
        const uint DRV_RESERVED = 2048u;

        // Token: 0x04000536 RID: 1334
        const uint DRV_QUERYFUNCTIONINSTANCEID = 2065u;

        // Token: 0x04000537 RID: 1335
        const uint DRV_QUERYFUNCTIONINSTANCEIDSIZE = 2066u;

        // Token: 0x04000541 RID: 1345
        MMDevice device;
    }
}
