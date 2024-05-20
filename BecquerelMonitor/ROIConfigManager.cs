using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000140 RID: 320
    public class ROIConfigManager
    {
        // Token: 0x1400005B RID: 91
        // (add) Token: 0x06001029 RID: 4137 RVA: 0x00058148 File Offset: 0x00056348
        // (remove) Token: 0x0600102A RID: 4138 RVA: 0x00058184 File Offset: 0x00056384
        public event EventHandler ROIConfigListChanged;

        // Token: 0x1700044E RID: 1102
        // (get) Token: 0x0600102B RID: 4139 RVA: 0x000581C0 File Offset: 0x000563C0
        // (set) Token: 0x0600102C RID: 4140 RVA: 0x000581C8 File Offset: 0x000563C8
        public List<ROIConfigData> ROIConfigList
        {
            get
            {
                return this.roiConfigList;
            }
            set
            {
                this.roiConfigList = value;
            }
        }

        // Token: 0x1700044F RID: 1103
        // (get) Token: 0x0600102D RID: 4141 RVA: 0x000581D4 File Offset: 0x000563D4
        // (set) Token: 0x0600102E RID: 4142 RVA: 0x000581DC File Offset: 0x000563DC
        public Dictionary<string, ROIConfigData> ROIConfigMap
        {
            get
            {
                return this.roiConfigMap;
            }
            set
            {
                this.roiConfigMap = value;
            }
        }

        // Token: 0x0600102F RID: 4143 RVA: 0x000581E8 File Offset: 0x000563E8
        public static ROIConfigManager GetInstance()
        {
            ROIConfigManager.instance.LoadAllConfigFiles();
            return ROIConfigManager.instance;
        }

        // Token: 0x06001031 RID: 4145 RVA: 0x0005821C File Offset: 0x0005641C
        public void LoadAllConfigFiles()
        {
            if (this.isLoaded)
            {
                return;
            }
            this.roiConfigList.Clear();
            this.roiConfigMap.Clear();
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
            try
            {
                string[] files = Directory.GetFiles(configROI, "*.xml");
                foreach (string path in files)
                {
                    ROIConfigData roiconfigData;
                    using (FileStream fileStream = new FileStream(path, FileMode.Open))
                    {
                        roiconfigData = (ROIConfigData)xmlSerializer.Deserialize(fileStream);
                    }
                    if (!(roiconfigData.FormatVersion == "120920"))
                    {
                        roiconfigData.InitFormatVersion();
                    }
                    foreach (ROIDefinitionData roidefinitionData in roiconfigData.ROIDefinitions)
                    {
                        foreach (ROIPrimitiveData roiprimitiveData in roidefinitionData.ROIPrimitives)
                        {
                            roiprimitiveData.Primitive = ROIPrimitiveDefinition.DefinitionsMap[roiprimitiveData.PrimitiveType];
                            roiprimitiveData.Operation = ROIPrimitiveOperation.OperationsMap[roiprimitiveData.OperationType];
                        }
                    }
                    roiconfigData.OriginalFilename = Path.GetFileName(path);
                    roiconfigData.Filename = Path.GetFileName(path);
                    if (this.roiConfigMap.ContainsKey(roiconfigData.Guid))
                    {
                        MessageBox.Show(string.Format(Resources.ERRDuplicateROIConfigGUID, roiconfigData.Filename), Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                    else
                    {
                        this.roiConfigList.Add(roiconfigData);
                        this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
                    }
                }
            }
            catch (Exception)
            {
                Directory.CreateDirectory(configROIDir);
                MessageBox.Show(Resources.ERRLoadingROIConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            this.roiConfigList.Sort();
            this.isLoaded = true;
        }

        // Token: 0x06001032 RID: 4146 RVA: 0x0005848C File Offset: 0x0005668C
        public ROIConfigData CreateConfig(string filename)
        {
            ROIConfigData roiconfigData = new ROIConfigData();
            roiconfigData.InitFormatVersion();
            roiconfigData.Guid = Guid.NewGuid().ToString();
            roiconfigData.OriginalFilename = filename;
            roiconfigData.Filename = filename;
            roiconfigData.Name = Path.GetFileNameWithoutExtension(filename);
            string path = configROI + roiconfigData.Filename;
            try
            {
                using (FileStream fileStream = new FileStream(path, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                    xmlSerializer.Serialize(fileStream, roiconfigData);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingROIConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            return roiconfigData;
        }

        // Token: 0x06001033 RID: 4147 RVA: 0x00058598 File Offset: 0x00056798
        public ROIConfigData DuplicateConfig(ROIConfigData config, string filename)
        {
            ROIConfigData roiconfigData = config.Clone();
            roiconfigData.InitFormatVersion();
            roiconfigData.Guid = Guid.NewGuid().ToString();
            roiconfigData.OriginalFilename = filename;
            roiconfigData.Filename = filename;
            roiconfigData.Name = config.Name + Resources.CopyPostfix;
            try
            {
                string path = configROI + roiconfigData.Filename;
                using (FileStream fileStream = new FileStream(path, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                    xmlSerializer.Serialize(fileStream, roiconfigData);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingROIConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            this.roiConfigList.Sort();
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            return roiconfigData;
        }

        // Token: 0x06001034 RID: 4148 RVA: 0x000586BC File Offset: 0x000568BC
        public bool LoadConfig(ROIConfigData roiConfig)
        {
            ROIConfigData roiconfigData = this.roiConfigMap[roiConfig.Guid];
            this.roiConfigMap.Remove(roiconfigData.Guid);
            this.roiConfigList.Remove(roiconfigData);
            try
            {
                string path = configROI + roiconfigData.OriginalFilename;
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                using (FileStream fileStream = new FileStream(path, FileMode.Open))
                {
                    roiconfigData = (ROIConfigData)xmlSerializer.Deserialize(fileStream);
                }
                if (!(roiconfigData.FormatVersion == "120920"))
                {
                    roiconfigData.InitFormatVersion();
                }
                foreach (ROIDefinitionData roidefinitionData in roiconfigData.ROIDefinitions)
                {
                    foreach (ROIPrimitiveData roiprimitiveData in roidefinitionData.ROIPrimitives)
                    {
                        roiprimitiveData.Primitive = ROIPrimitiveDefinition.DefinitionsMap[roiprimitiveData.PrimitiveType];
                        roiprimitiveData.Operation = ROIPrimitiveOperation.OperationsMap[roiprimitiveData.OperationType];
                    }
                }
                roiconfigData.OriginalFilename = Path.GetFileName(path);
                roiconfigData.Filename = Path.GetFileName(path);
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRLoadingROIConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            this.roiConfigList.Sort();
            return true;
        }

        // Token: 0x06001035 RID: 4149 RVA: 0x000588C8 File Offset: 0x00056AC8
        public bool SaveConfig(ROIConfigData roiConfig)
        {
            ROIConfigData roiconfigData = this.roiConfigMap[roiConfig.Guid];
            this.roiConfigMap.Remove(roiconfigData.Guid);
            this.roiConfigList.Remove(roiconfigData);
            if (roiConfig.OriginalFilename != roiConfig.Filename)
            {
                try
                {
                    File.Delete(configROI + roiConfig.OriginalFilename);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            roiConfig.OriginalFilename = roiConfig.Filename;
            roiConfig.LastUpdated = DateTime.Now;
            roiConfig.Dirty = false;
            roiconfigData = roiConfig.Clone();
            try
            {
                string path = configROI + roiconfigData.Filename;
                using (FileStream fileStream = new FileStream(path, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                    xmlSerializer.Serialize(fileStream, roiconfigData);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRSavingROIConfigFailed, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            this.roiConfigList.Sort();
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            return true;
        }

        // Token: 0x06001036 RID: 4150 RVA: 0x00058A38 File Offset: 0x00056C38
        public void DeleteConfig(ROIConfigData roiConfig)
        {
            ROIConfigData roiconfigData = this.roiConfigMap[roiConfig.Guid];
            try
            {
                File.Delete(configROI + roiconfigData.OriginalFilename);
            }
            catch (Exception)
            {
            }
            this.roiConfigList.Remove(roiconfigData);
            this.roiConfigMap.Remove(roiconfigData.Guid);
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
        }

        public bool ImportEffCalcMCtoROI(string ROIName, string filename)
        {
            Dictionary<double, double> points = new Dictionary<double, double>();
            try
            {
                // read file
                using (StreamReader streamReader = new StreamReader(filename, Encoding.GetEncoding(65001)))
                {
                    // skip first line like "Energy, keV	Efficiency	Uncertainty, %"
                    streamReader.ReadLine();
                    while (streamReader.Peek() != -1)
                    {
                        List<string> lineList = streamReader.ReadLine().Split(new char[] { '\t' }).ToList<string>();
                        if (lineList.Count > 5)
                        {
                            for (int i = 0; i < lineList.Count; i++)
                            {
                                if (lineList[i] == "")
                                {
                                    lineList.RemoveAt(i);
                                    i--;
                                    if (i > lineList.Count - 1) break;
                                }
                            }
                            points.Add(Convert.ToDouble(lineList[0]), Convert.ToDouble(lineList[1]));
                        }
                    }

                    // add this points to ROI config
                    ROIConfigData roiconfigData = new ROIConfigData();
                    roiconfigData.InitFormatVersion();
                    roiconfigData.Guid = Guid.NewGuid().ToString();
                    roiconfigData.Name = ROIName;
                    roiconfigData.Filename = ROIName + ".xml";
                    roiconfigData.OriginalFilename = roiconfigData.Filename;
                    roiconfigData.LastUpdated = DateTime.Now;
                    roiconfigData.Dirty = false;
                    foreach (KeyValuePair<double, double> point in points)
                    {
                        double energy = point.Key;
                        double eff = point.Value;
                        ROIDefinitionData rOIDefinitionData = new ROIDefinitionData();
                        rOIDefinitionData.Name = energy.ToString();
                        rOIDefinitionData.Enabled = true;
                        rOIDefinitionData.PeakEnergy = energy;
                        rOIDefinitionData.LowerLimit = -100;
                        rOIDefinitionData.UpperLimit = -100;
                        rOIDefinitionData.Intencity = eff * 100;
                        rOIDefinitionData.Color = Color.Red;
                        roiconfigData.ROIDefinitions.Add(rOIDefinitionData);
                    }
                    string path = configROI + ROIName + ".xml";
                    using (FileStream fileStream = new FileStream(path, FileMode.Create))
                    {
                        XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                        xmlSerializer.Serialize(fileStream, roiconfigData);
                    }
                    this.roiConfigList.Add(roiconfigData);
                    this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
                    this.roiConfigList.Sort();
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message));
            }
            return false;
        }


        string configROI = Package.GetInstance().ROI;
        string configROIDir = Package.GetInstance().ROIDir;

        // Token: 0x04000964 RID: 2404
        List<ROIConfigData> roiConfigList = new List<ROIConfigData>();

        // Token: 0x04000965 RID: 2405
        Dictionary<string, ROIConfigData> roiConfigMap = new Dictionary<string, ROIConfigData>();

        // Token: 0x04000966 RID: 2406
        bool isLoaded;

        // Token: 0x04000968 RID: 2408
        static ROIConfigManager instance = new ROIConfigManager();
    }
}
