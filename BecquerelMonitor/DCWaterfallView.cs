namespace BecquerelMonitor
{
    public partial class DCWaterfallView : ToolWindow
    {
        public DCWaterfallView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
        }

        MainForm mainForm;
    }
}
