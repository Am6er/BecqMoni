using BecquerelMonitor.Properties;
using System;

namespace BecquerelMonitor
{
    // Token: 0x0200003D RID: 61
    public partial class DCDebugPanel : ToolWindow
    {
        // Token: 0x1700014B RID: 331
        // (get) Token: 0x06000370 RID: 880 RVA: 0x00010DA0 File Offset: 0x0000EFA0
        // (set) Token: 0x06000371 RID: 881 RVA: 0x00010DB0 File Offset: 0x0000EFB0
        public string TestFilename
        {
            get
            {
                return this.textBox9.Text;
            }
            set
            {
                this.textBox9.Text = value;
            }
        }

        // Token: 0x06000372 RID: 882 RVA: 0x00010DC0 File Offset: 0x0000EFC0
        public DCDebugPanel(MainForm mainForm)
        {
            this.InitializeComponent();
            this.mainForm = mainForm;
            base.Icon = Resources.becqmoni;
        }

        // Token: 0x06000373 RID: 883 RVA: 0x00010DE0 File Offset: 0x0000EFE0
        void button4_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            activeDocument.Dirty = true;
            this.button4.Enabled = false;
            this.button6.Enabled = false;
            this.button5.Enabled = true;
            this.button4.Enabled = true;
            this.button6.Enabled = true;
            this.button5.Enabled = false;
        }

        // Token: 0x06000374 RID: 884 RVA: 0x00010E54 File Offset: 0x0000F054
        void button6_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            activeDocument.Dirty = true;
            this.button4.Enabled = false;
            this.button6.Enabled = false;
            this.button5.Enabled = true;
            this.button4.Enabled = true;
            this.button6.Enabled = true;
            this.button5.Enabled = false;
        }

        // Token: 0x06000375 RID: 885 RVA: 0x00010EC8 File Offset: 0x0000F0C8
        void button5_Click(object sender, EventArgs e)
        {
            if (this.mainForm.ActiveDocument == null)
            {
                return;
            }
            this.button4.Enabled = true;
            this.button6.Enabled = true;
            this.button5.Enabled = false;
        }

        // Token: 0x0400015C RID: 348
        MainForm mainForm;
    }
}
