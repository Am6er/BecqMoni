using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    public partial class ThermometerForm : UserControl
    {
        protected DeviceConfigForm deviceConfigForm;

        public string ThermometerTypeString
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

        public ThermometerForm()
        {
            this.InitializeComponent();
        }

        public ThermometerForm(DeviceConfigForm deviceConfigForm)
            : this()
        {
            this.deviceConfigForm = deviceConfigForm;
            this.ThermometerTypeString = Resources.ThermometerTypeNone;
        }

        public virtual void Initialize()
        {
        }

        public virtual void FormClosing()
        {
        }

        public virtual void LoadFormContents(ThermometerConfig config)
        {
        }

        public virtual bool SaveFormContents(ThermometerConfig config)
        {
            return true;
        }
    }
}
