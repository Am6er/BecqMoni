using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x020000BE RID: 190
    public class NuclideDefinitionManager
    {
        // Token: 0x1700028C RID: 652
        // (get) Token: 0x0600092E RID: 2350 RVA: 0x0003571C File Offset: 0x0003391C
        // (set) Token: 0x0600092F RID: 2351 RVA: 0x00035724 File Offset: 0x00033924
        public NuclideDefinitionFile NuclideDefinitionFile
        {
            get
            {
                return this.nuclideDefinitionFile;
            }
            set
            {
                this.nuclideDefinitionFile = value;
            }
        }

        // Token: 0x1700028D RID: 653
        // (get) Token: 0x06000930 RID: 2352 RVA: 0x00035730 File Offset: 0x00033930
        // (set) Token: 0x06000931 RID: 2353 RVA: 0x00035740 File Offset: 0x00033940
        public List<NuclideDefinition> NuclideDefinitions
        {
            get
            {
                return this.nuclideDefinitionFile.NuclideDefinitions;
            }
            set
            {
                this.nuclideDefinitionFile.NuclideDefinitions = value;
            }
        }

        // Token: 0x06000932 RID: 2354 RVA: 0x00035750 File Offset: 0x00033950
        public static NuclideDefinitionManager GetInstance()
        {
            if (!NuclideDefinitionManager.instance.LoadDefinitionFile())
            {
                NuclideDefinitionManager.instance.NuclideDefinitionFile = new NuclideDefinitionFile();
                NuclideDefinitionManager.instance.InitializeNuclideDefinitionFile();
                Directory.CreateDirectory("config");
                if (NuclideDefinitionManager.instance.SaveDefinitionFile())
                {
                    MessageBox.Show(Resources.MSGNewNuclideDefinitionFileCreated, Resources.NotificationDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
            }
            return NuclideDefinitionManager.instance;
        }

        // Token: 0x06000934 RID: 2356 RVA: 0x000357C4 File Offset: 0x000339C4
        public bool LoadDefinitionFile()
        {
            if (this.isLoaded)
            {
                return true;
            }
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(NuclideDefinitionFile));
            try
            {
                using (FileStream fileStream = new FileStream(nuclideDefinitionFilename, FileMode.Open))
                {
                    this.nuclideDefinitionFile = (NuclideDefinitionFile)xmlSerializer.Deserialize(fileStream);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRLoadingNuclideDefinitionFile, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                this.nuclideDefinitionFile = null;
                return false;
            }
            this.isLoaded = true;
            return true;
        }

        // Token: 0x06000935 RID: 2357 RVA: 0x0003586C File Offset: 0x00033A6C
        public bool SaveDefinitionFile()
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(NuclideDefinitionFile));
            try
            {
                using (FileStream fileStream = new FileStream(nuclideDefinitionFilename, FileMode.Create))
                {
                    xmlSerializer.Serialize(fileStream, this.nuclideDefinitionFile);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingNuclideDefinitionFile, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            return true;
        }

        // Token: 0x06000936 RID: 2358 RVA: 0x000358F4 File Offset: 0x00033AF4
        void InitializeNuclideDefinitionFile()
        {
            this.nuclideDefinitionFile.NuclideDefinitions.Clear();
            NuclideDefinition nuclideDefinition = new NuclideDefinition();
            nuclideDefinition.Name = "Cs134";
            nuclideDefinition.Energy = 605.0;
            nuclideDefinition.HalfLife = 2.0648;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition);
            NuclideDefinition nuclideDefinition2 = new NuclideDefinition();
            nuclideDefinition2.Name = "Cs137";
            nuclideDefinition2.Energy = 662.0;
            nuclideDefinition2.HalfLife = 30.07;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition2);
            NuclideDefinition nuclideDefinition3 = new NuclideDefinition();
            nuclideDefinition3.Name = "Cs134";
            nuclideDefinition3.Energy = 798.0;
            nuclideDefinition3.HalfLife = 2.0648;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition3);
            NuclideDefinition nuclideDefinition4 = new NuclideDefinition();
            nuclideDefinition4.Name = "K40";
            nuclideDefinition4.Energy = 1460.0;
            nuclideDefinition4.HalfLife = 1277000000.0;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition4);
        }

        string nuclideDefinitionFilename = Package.GetInstance().NuclideDefinition;

        // Token: 0x04000513 RID: 1299
        static NuclideDefinitionManager instance = new NuclideDefinitionManager();

        // Token: 0x04000514 RID: 1300
        NuclideDefinitionFile nuclideDefinitionFile;

        // Token: 0x04000515 RID: 1301
        bool isLoaded;
    }
}
