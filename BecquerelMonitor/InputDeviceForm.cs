using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000D6 RID: 214
    public partial class InputDeviceForm : UserControl
    {
        // Token: 0x170002E2 RID: 738
        // (get) Token: 0x06000ADF RID: 2783 RVA: 0x00045494 File Offset: 0x00043694
        // (set) Token: 0x06000AE0 RID: 2784 RVA: 0x000454A4 File Offset: 0x000436A4
        public string DeviceTypeString
        {
            get
            {
                return this.textBox1.Text;
            }
            set
            {
                this.textBox1.Text = value;
            }
        }

        // Token: 0x170002E3 RID: 739
        // (get) Token: 0x06000AE1 RID: 2785 RVA: 0x000454B4 File Offset: 0x000436B4
        public virtual TextBox UpperThresholdTextBox
        {
            get
            {
                return null;
            }
        }

        // Token: 0x170002E4 RID: 740
        // (get) Token: 0x06000AE2 RID: 2786 RVA: 0x000454B8 File Offset: 0x000436B8
        public virtual TextBox LowerThresholdTextBox
        {
            get
            {
                return null;
            }
        }

        // Token: 0x06000AE3 RID: 2787 RVA: 0x000454BC File Offset: 0x000436BC
        public InputDeviceForm()
        {
            this.InitializeComponent();
        }

        // Token: 0x06000AE4 RID: 2788 RVA: 0x000454CC File Offset: 0x000436CC
        public InputDeviceForm(DeviceConfigForm deviceConfigForm)
        {
            this.InitializeComponent();
            this.deviceConfigForm = deviceConfigForm;
            this.DeviceTypeString = "unknown device";
        }

        // Token: 0x06000AE5 RID: 2789 RVA: 0x000454EC File Offset: 0x000436EC
        public virtual void Initialize()
        {
        }

        // Token: 0x06000AE6 RID: 2790 RVA: 0x000454F0 File Offset: 0x000436F0
        public virtual void FormClosing()
        {
        }

        // Token: 0x06000AE7 RID: 2791 RVA: 0x000454F4 File Offset: 0x000436F4
        public virtual void LoadFormContents(InputDeviceConfig config)
        {
        }

        // Token: 0x06000AE8 RID: 2792 RVA: 0x000454F8 File Offset: 0x000436F8
        public virtual bool SaveFormContents(InputDeviceConfig config)
        {
            return true;
        }
    }
}
