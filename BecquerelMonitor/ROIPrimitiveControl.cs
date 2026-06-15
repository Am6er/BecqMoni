using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class ROIPrimitiveControl : UserControl
    {
        public event EventHandler ROIPrimitiveModified;

        public ROIPrimitiveControl()
        {
            this.InitializeComponent();
        }

        public virtual void LoadFormContents(ROIPrimitiveData prim)
        {
        }

        public virtual bool SaveFormContents(ROIPrimitiveData prim)
        {
            prim = new ROIPrimitiveData();
            return true;
        }

        public virtual void PrepareForm(ROIConfigData config)
        {
        }

        protected void PrimitiveModified()
        {
            if (this.ROIPrimitiveModified != null)
            {
                this.ROIPrimitiveModified(this, new EventArgs());
            }
        }
    }
}
