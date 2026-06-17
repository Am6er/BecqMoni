using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public class ToolStripEx : ToolStrip
    {
        [Category("Behavior")]
        [DefaultValue(false)]
        public bool ClickThrough
        {
            get
            {
                return clickThrough;
            }
            set
            {
                clickThrough = value;
            }
        }

        [Category("Behavior")]
        [DefaultValue(true)]
        public bool SuppressHighlighting
        {
            get
            {
                return suppressHighlighting;
            }
            set
            {
                suppressHighlighting = value;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if ((long)m.Msg == 512L && suppressHighlighting && !TopLevelControl.ContainsFocus)
            {
                return;
            }

            base.WndProc(ref m);

            if ((long)m.Msg == 33L && clickThrough && m.Result == (IntPtr)2L)
            {
                m.Result = (IntPtr)1L;
            }
        }

        bool clickThrough;
        bool suppressHighlighting = true;
    }
}
