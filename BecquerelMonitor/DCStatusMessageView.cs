using System;
using System.Drawing;
using BecquerelMonitor.Controls;

namespace BecquerelMonitor
{
    // Token: 0x02000029 RID: 41
    public partial class DCStatusMessageView : ToolWindow
    {
        // Token: 0x17000109 RID: 265
        // (get) Token: 0x0600022C RID: 556 RVA: 0x00008D50 File Offset: 0x00006F50
        // (set) Token: 0x0600022D RID: 557 RVA: 0x00008D60 File Offset: 0x00006F60
        public string Message
        {
            get
            {
                return this.statusMessageControl == null ? string.Empty : this.statusMessageControl.Message;
            }
            set
            {
                this.EnsureRuntimeStatusMessage();
                this.statusMessageControl.Message = value;
            }
        }

        // Token: 0x1700010A RID: 266
        // (get) Token: 0x0600022E RID: 558 RVA: 0x00008D70 File Offset: 0x00006F70
        // (set) Token: 0x0600022F RID: 559 RVA: 0x00008D80 File Offset: 0x00006F80
        public Color MessageColor
        {
            get
            {
                return this.statusMessageControl == null ? Color.Red : this.statusMessageControl.MessageColor;
            }
            set
            {
                this.EnsureRuntimeStatusMessage();
                this.statusMessageControl.MessageColor = value;
            }
        }

        // Token: 0x1700010B RID: 267
        // (get) Token: 0x06000230 RID: 560 RVA: 0x00008D90 File Offset: 0x00006F90
        // (set) Token: 0x06000231 RID: 561 RVA: 0x00008DA0 File Offset: 0x00006FA0
        public bool DoScroll
        {
            get
            {
                return this.statusMessageControl != null && this.statusMessageControl.DoScroll;
            }
            set
            {
                this.EnsureRuntimeStatusMessage();
                this.statusMessageControl.DoScroll = value;
            }
        }

        public DCStatusMessageView()
        {
            this.InitializeComponent();
        }

        // Token: 0x06000232 RID: 562 RVA: 0x00008DB0 File Offset: 0x00006FB0
        public DCStatusMessageView(MainForm mainForm)
            : this()
        {
            this.mainForm = mainForm;
            this.EnsureRuntimeStatusMessage();
        }

        // Token: 0x06000233 RID: 563 RVA: 0x00008DD0 File Offset: 0x00006FD0
        void DCStatusMessageView_Load(object sender, EventArgs e)
        {
        }

        void EnsureRuntimeStatusMessage()
        {
            if (this.statusMessageControl != null || this.statusMessage1 == null)
            {
                return;
            }
            this.statusMessageControl = new StatusMessage();
            this.statusMessageControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusMessageControl.BackColor = Color.Black;
            this.statusMessageControl.ForeColor = Color.White;
            this.statusMessageControl.Message = "The sample is being measured.";
            this.statusMessageControl.MessageColor = Color.Red;
            this.statusMessage1.Controls.Add(this.statusMessageControl);
        }

        // Token: 0x040000A2 RID: 162
        MainForm mainForm;

        StatusMessage statusMessageControl;
    }
}
