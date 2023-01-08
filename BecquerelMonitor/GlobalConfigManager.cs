using BecquerelMonitor.Properties;
using System;
using System.Deployment.Application;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000078 RID: 120
    public class GlobalConfigManager
    {
        // Token: 0x170001D2 RID: 466
        // (get) Token: 0x0600060B RID: 1547 RVA: 0x00026490 File Offset: 0x00024690
        // (set) Token: 0x0600060C RID: 1548 RVA: 0x00026498 File Offset: 0x00024698
        public GlobalConfigInfo GlobalConfig
        {
            get
            {
                return this.globalConfig;
            }
            set
            {
                this.globalConfig = value;
            }
        }

        // Token: 0x0600060D RID: 1549 RVA: 0x000264A4 File Offset: 0x000246A4
        public static GlobalConfigManager GetInstance()
        {
            GlobalConfigManager.instance.LoadConfigFile();
            return GlobalConfigManager.instance;
        }

        // Token: 0x0600060F RID: 1551 RVA: 0x000264E8 File Offset: 0x000246E8
        public void LoadConfigFile()
        {
            if (this.isLoaded)
            {
                return;
            }
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(GlobalConfigInfo));
            try
            {
                using (FileStream fileStream = new FileStream(becqMoniMainConfig, FileMode.Open))
                {
                    this.globalConfig = (GlobalConfigInfo)xmlSerializer.Deserialize(fileStream);
                }
                if (this.globalConfig.ColorConfig.SpectrumColorList == null || this.globalConfig.ColorConfig.SpectrumColorList.Count < this.MaximumSpectrumPerFile)
                {
                    this.globalConfig.ColorConfig.InitializeSpectrumColor();
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRLoadingGlobalConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                this.globalConfig = new GlobalConfigInfo();
                this.globalConfig.ColorConfig.InitializeSpectrumColor();
            }

            this.VersionString = BecquerelMonitor.Package.GetInstance().PackageVersion;

            this.isLoaded = true;
        }

        // Token: 0x06000610 RID: 1552 RVA: 0x000265E4 File Offset: 0x000247E4
        public void PrepareConfigFile()
        {
            DeviceConfigManager.GetInstance();
            ROIConfigManager.GetInstance();
        }

        // Token: 0x06000611 RID: 1553 RVA: 0x000265F4 File Offset: 0x000247F4
        public void SaveConfigFile()
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(GlobalConfigInfo));
            try
            {
                using (FileStream fileStream = new FileStream(becqMoniMainConfig, FileMode.Create))
                {
                    xmlSerializer.Serialize(fileStream, this.globalConfig);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingGlobalConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        string becqMoniMainConfig = BecquerelMonitor.Package.GetInstance().MainConfig;

        // Token: 0x04000330 RID: 816
        public string VersionString = "1.0";

        // Token: 0x04000331 RID: 817
        public DateTime LimitDate = new DateTime(2111, 3, 11);

        // Token: 0x04000332 RID: 818
        public int MaximumSpectrumPerFile = 16;

        // Token: 0x04000333 RID: 819
        static GlobalConfigManager instance = new GlobalConfigManager();

        // Token: 0x04000334 RID: 820
        GlobalConfigInfo globalConfig;

        // Token: 0x04000335 RID: 821
        bool isLoaded;
    }
}
